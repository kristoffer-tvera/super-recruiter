using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SuperRecruiter.Api.Services;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;
using SuperRecruiter.Worker.Services;
using Xunit;

namespace SuperRecruiter.Tests;

[Collection("Database activity")]
public sealed class EnrichmentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlayerResponse Character = new()
    {
        Id = 1,
        CharacterName = "Example",
        RealmSlug = "silvermoon",
        Realm = "Silvermoon",
    };
    private const string Current =
        """{"raid_progression":{"current-a":{"summary":"3/8 M","total_bosses":8,"mythic_bosses_killed":3},"current-b":{"summary":"1/1 M","total_bosses":1,"mythic_bosses_killed":1}}}""";

    [Theory]
    [InlineData(LfgSource.WoWProgress, null)]
    [InlineData(LfgSource.RaiderIO, "N/A")]
    [InlineData(LfgSource.RaiderIO, "Unknown")]
    [InlineData(LfgSource.Manual, "German")]
    public async Task DiscoveryOnlyFetchesCurrentTier(LfgSource source, string? languages)
    {
        var configuration = Configuration();
        var lookups = 0;
        var raiderHandler = new Handler(request =>
        {
            lookups++;
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("fields=raid_progression:current-tier", query);
            Assert.DoesNotContain("gear", query);
            return Task.FromResult(Json(Current));
        });
        CreatePlayerRequest? saved = null;
        var apiHandler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("config"))
                return Json("""{"bossKills":5,"acceptedClasses":[]}""");
            if (request.RequestUri.AbsolutePath.EndsWith("seen/bulk"))
                return Json("{}");
            saved = await request.Content!.ReadFromJsonAsync<CreatePlayerRequest>();
            return Json(JsonSerializer.Serialize(Character, JsonOptions));
        });
        var api = new SuperRecruiterApiClient(new HttpClient(apiHandler) { BaseAddress = new("http://test/api/") }, NullLogger<SuperRecruiterApiClient>.Instance);
        var filter = new AdminFilterService(api, NullLogger<AdminFilterService>.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        var discord = new DiscordBotService(NullLogger<DiscordBotService>.Instance, configuration, services);
        var ingestion = new PlayerIngestionService(
            NullLogger<PlayerIngestionService>.Instance,
            new RaiderIOService(NullLogger<RaiderIOService>.Instance, new HttpClient(raiderHandler), configuration),
            api,
            filter,
            discord
        );
        await ingestion.ProcessPlayerAsync(
            new Player
            {
                CharacterName = Character.CharacterName,
                Realm = Character.Realm,
                Class = "Mage",
                Source = source,
                Languages = languages,
            },
            CancellationToken.None
        );
        Assert.Equal(1, lookups);
        Assert.NotNull(saved);
        Assert.Equal(4, saved.CurrentTierMythicKillCount);
        Assert.Equal(2, saved.DiscoveryProgression!.Raids.Count);
        Assert.Null(saved.WarcraftLogsSummary);
        Assert.Null(saved.RaiderIoSummary);
    }

    [Fact]
    public void ProjectionUsesCurrentTierAndPreservesUnknownRawFields()
    {
        var raw = Element(
            """{"profile":{"unknown":{"nested":[1,2]},"raid_progression":{"old":{"summary":"10/10 M","total_bosses":10,"mythic_bosses_killed":10}}},"currentTier":CURRENT}""".Replace(
                "CURRENT",
                Current
            )
        );
        var snapshot = Snapshot(raw);
        var before = raw.GetRawText();
        var projection = PlayerEnrichmentService.Project(new(snapshot, null, null));
        Assert.Equal(4, projection.Progression!.Raids.Values.Sum(raid => raid.Mythic_bosses_killed));
        Assert.Equal(before, raw.GetRawText());
        var roundTrip = JsonSerializer.Deserialize<SourceSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;
        Assert.Equal(2, roundTrip.Data!.Value.GetProperty("profile").GetProperty("unknown").GetProperty("nested").GetArrayLength());
        Assert.Equal(JsonValueKind.Object, roundTrip.Data.Value.ValueKind);
        Assert.Contains("old", projection.RaiderIoSummary);
    }

    [Fact]
    public void DiscordProgressionIncludesAllCurrentTierRaidsRegardlessOfOrder()
    {
        var profile = JsonSerializer.Deserialize<RaiderIOProfile>(Current, JsonOptions)!;
        Assert.Equal("4/9 M", PlayerSummaryHelper.GetCurrentExpansionProgressForDiscord(profile));
        profile.Raid_progression = profile.Raid_progression!.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("4/9 M", PlayerSummaryHelper.GetCurrentExpansionProgressForDiscord(profile));
    }

    [Theory]
    [InlineData("not_found")]
    [InlineData("not_found_or_private")]
    [InlineData("rate_limited")]
    [InlineData("not_configured")]
    public async Task FailedSourceRetainsPreviousPayloadAndTimestamp(string outcome)
    {
        var previous = Snapshot(Element("""{"old":true}"""));
        var result = await Service().CaptureAsync(previous, _ => throw new SourceFetchException(outcome), 1, CancellationToken.None);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(previous.FetchedAt, result.FetchedAt);
        Assert.True(result.Data!.Value.GetProperty("old").GetBoolean());
        Assert.Null(PlayerEnrichmentService.WithoutData(new(result, null, null)).RaiderIo!.Data);
    }

    [Fact]
    public async Task ProjectionFailureDoesNotDiscardNewRawSnapshot()
    {
        var previous = Snapshot(Element("""{"old":true}"""));
        var result = await Service().CaptureAsync(previous, _ => Task.FromResult(Element("""{"newField":true}""")), 1, CancellationToken.None);
        var projection = PlayerEnrichmentService.Project(new(result, null, null));
        Assert.Equal("success", result.Outcome);
        Assert.True(result.Data!.Value.GetProperty("newField").GetBoolean());
        Assert.Null(projection.RaiderIoSummary);
    }

    [Fact]
    public async Task OversizedSourceIsRejectedAndCancellationPropagates()
    {
        var service = Service();
        var result = await service.CaptureAsync(
            null,
            _ => Task.FromResult(JsonSerializer.SerializeToElement(new { value = new string('a', 1024 * 1024) })),
            1,
            CancellationToken.None
        );
        Assert.Equal("response_too_large", result.Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureAsync(null, token => Task.FromCanceled<JsonElement>(token), 1, cancellation.Token));
    }

    [Fact]
    public async Task SourceTimeoutIsAnOutcomeNotZeroProgression()
    {
        var result = await Service()
            .CaptureAsync(
                null,
                async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return Element("{}");
                },
                0,
                CancellationToken.None
            );
        Assert.Equal("timeout", result.Outcome);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task RaiderIoKeepsNativeProfileAndRequestsTierAutomatically()
    {
        var fields = new List<string>();
        var client = Sources(
            new Handler(request =>
            {
                var query = Uri.UnescapeDataString(request.RequestUri!.Query);
                fields.Add(query);
                return Task.FromResult(
                    Json(
                        query.Contains("raid_achievement_curve") ? """{"raid_achievement_curve":[{"raid":"current-a","cutting_edge":"2026-01-01"}]}"""
                        : query.Contains("current-expansion") ? """{"unknown":{"retained":true},"raid_progression":{"current-a":{"mythic_bosses_killed":3}}}"""
                        : Current
                    )
                );
            })
        );
        var snapshot = await client.FetchRaiderIoAsync(Character, CancellationToken.None);
        Assert.Equal(3, fields.Count);
        Assert.Contains(fields, value => value.Contains("fields=raid_progression:current-tier"));
        Assert.True(snapshot.GetProperty("profile").GetProperty("unknown").GetProperty("retained").GetBoolean());
        Assert.Equal("current-a", snapshot.GetProperty("profile").GetProperty("raid_achievement_curve")[0].GetProperty("raid").GetString());
    }

    [Fact]
    public async Task RateLimitBackoffAvoidsRepeatedUpstreamCalls()
    {
        var count = 0;
        var client = Sources(
            new Handler(_ =>
            {
                count++;
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));
                return Task.FromResult(response);
            })
        );
        await Assert.ThrowsAsync<SourceFetchException>(() => client.FetchRaiderIoAsync(Character, CancellationToken.None));
        await Assert.ThrowsAsync<SourceFetchException>(() => client.FetchRaiderIoAsync(Character, CancellationToken.None));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GraphQlErrorsAreNotSuccessfulSnapshots()
    {
        var client = Sources(
            new Handler(request =>
                Task.FromResult(
                    Json(
                        request.RequestUri!.AbsolutePath.EndsWith("token")
                            ? """{"access_token":"test-token","expires_in":3600}"""
                            : """{"errors":[{"message":"private"}],"data":null}"""
                    )
                )
            )
        );
        var exception = await Assert.ThrowsAsync<SourceFetchException>(() => client.FetchWarcraftLogsAsync(Character, CancellationToken.None));
        Assert.Equal("upstream_error", exception.Outcome);
    }

    [Fact]
    public void WowProgressProducesDecodedStructuredDetailsWithoutHtml()
    {
        var details = WowProgressService.ParseDetails(
            """
            <div class="registeredTo"><span class="profileBattletag">Example#123</span>
            <div class="language">Languages: English</div><div class="charCommentary">A &amp; B</div></div>
            <ul class="eventList"><li>Joined guild</li><li>Joined guild</li></ul>
            """
        );
        Assert.Equal("A & B", details.Bio);
        Assert.Equal("English", details.Languages);
        Assert.Single(details.GuildHistory);
        Assert.Throws<WowProgressFetchException>(() => WowProgressService.ParseDetails("<h1>Challenge page</h1>"));
    }

    [PostgresFact]
    public async Task EndpointsAreExplicitAuthenticatedAndPreservePartialSuccess()
    {
        var requests = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = false;
        var sourceHandler = new Handler(async request =>
        {
            if (Interlocked.Increment(ref requests) == 3)
                secondEntered.TrySetResult();
            if (block)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (request.RequestUri!.Host.Contains("raider.io"))
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                return Json(query.Contains("raid_achievement_curve") ? """{"raid_achievement_curve":[]}""" : Current);
            }
            if (request.RequestUri.AbsolutePath.EndsWith("token"))
                return Json("""{"access_token":"test","expires_in":3600}""");
            if (request.RequestUri.Host.Contains("warcraftlogs"))
                return Json("""{"data":{"characterData":{"character":null}}}""");
            return Json("""{"status":"error"}""");
        });
        await using var factory = new WebApplicationFactory<PlayerEnrichmentService>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSQL", Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES"));
            builder.UseSetting("ApiKey", "test-api-key");
            builder.UseSetting("RaiderIO:ApiKey", "test");
            builder.UseSetting("WarcraftLogs:ClientId", "test");
            builder.UseSetting("WarcraftLogs:ClientSecret", "test");
            builder.UseSetting("FlareSolverrUrl", "http://solver.test/v1");
            builder.ConfigureTestServices(services => services.AddSingleton<IHttpClientFactory>(new ClientFactory(sourceHandler)));
        });
        using var http = factory.CreateClient();
        var databaseOperations = 0;
        using (
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                ActivityStarted = _ => Interlocked.Increment(ref databaseOperations),
            }
        )
        {
            ActivitySource.AddActivityListener(listener);
            for (var index = 0; index < 10; index++)
                (await http.GetAsync("/api/live")).EnsureSuccessStatusCode();
            Assert.Equal(0, databaseOperations);
            (await http.GetAsync("/api/health")).EnsureSuccessStatusCode();
            Assert.True(databaseOperations > 0, "Readiness should still check PostgreSQL; routine liveness must not.");
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/players/1/enrichment")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/api/wowprogress/jobs/claim", null)).StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/players/2147483647/enrichment")).StatusCode);
        var create = new CreatePlayerRequest
        {
            CharacterName = "Http" + Guid.NewGuid().ToString("N"),
            Realm = "Silvermoon",
            RealmSlug = "silvermoon",
            Class = "Mage",
            LastUpdated = DateTime.UtcNow,
        };
        var created = await http.PostAsJsonAsync("/api/players", create);
        created.EnsureSuccessStatusCode();
        var player = (await created.Content.ReadFromJsonAsync<PlayerResponse>())!;
        var others = new List<PlayerResponse>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                create.CharacterName = "Concurrent" + Guid.NewGuid().ToString("N");
                var other = await http.PostAsJsonAsync("/api/players", create);
                other.EnsureSuccessStatusCode();
                others.Add((await other.Content.ReadFromJsonAsync<PlayerResponse>())!);
            }
            var metadata = await http.GetAsync($"/api/players/{player.Id}/enrichment?includeData=false");
            metadata.EnsureSuccessStatusCode();
            await http.GetAsync($"/api/players/{player.Id}");
            await http.GetAsync("/api/players");
            Assert.Equal(0, requests);
            block = true;
            var refresh = http.PostAsync($"/api/players/{player.Id}/enrichment", null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsync($"/api/players/{player.Id}/enrichment", null)).StatusCode);
            var secondRefresh = http.PostAsync($"/api/players/{others[0].Id}/enrichment", null);
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostAsync($"/api/players/{others[1].Id}/enrichment", null)).StatusCode);
            release.SetResult();
            var response = await refresh;
            (await secondRefresh).EnsureSuccessStatusCode();
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<PlayerEnrichmentRefreshResponse>())!;
            Assert.Equal("success", result.Enrichment.RaiderIo!.Outcome);
            Assert.Equal("not_found_or_private", result.Enrichment.WarcraftLogs!.Outcome);
            Assert.Equal("pending", result.Enrichment.WowProgress!.Outcome);
            Assert.Null(result.Enrichment.RaiderIo.Data);
            Assert.Equal(4, result.Player.CurrentTierMythicKillCount);
            var cooldown = await http.PostAsync($"/api/players/{player.Id}/enrichment", null);
            Assert.Equal(HttpStatusCode.TooManyRequests, cooldown.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(60), cooldown.Headers.RetryAfter!.Delta);
            var raw = await http.GetFromJsonAsync<PlayerEnrichmentResponse>($"/api/players/{player.Id}/enrichment");
            Assert.Equal(JsonValueKind.Object, raw!.RaiderIo!.Data!.Value.ValueKind);
            var count = requests;
            await http.GetAsync($"/api/players/{player.Id}/enrichment?includeData=false");
            Assert.Equal(count, requests);
            var jobs = new List<WowProgressJobResponse>();
            for (var index = 0; index < 2; index++)
            {
                using var claim = await http.PostAsync("/api/wowprogress/jobs/claim", null);
                claim.EnsureSuccessStatusCode();
                jobs.Add((await claim.Content.ReadFromJsonAsync<WowProgressJobResponse>())!);
            }
            var job = Assert.Single(jobs, job => job.PlayerId == player.Id);
            var completion = new CompleteWowProgressJobRequest(job.LeaseId, "success", new("Home#123", "Local profile", "English", "Fire", ["Joined guild"]));
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"/api/wowprogress/jobs/{job.Id}/complete", completion)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"/api/wowprogress/jobs/{job.Id}/complete", completion)).StatusCode);
            Assert.False((await http.GetFromJsonAsync<WowProgressPendingResponse>($"/api/wowprogress/jobs/pending/{player.Id}"))!.Pending);
            var completed = (await http.GetFromJsonAsync<PlayerEnrichmentResponse>($"/api/players/{player.Id}/enrichment"))!;
            Assert.Equal("success", completed.WowProgress!.Outcome);
            Assert.Equal("Home#123", completed.WowProgress.Data!.Value.GetProperty("battleTag").GetString());
            Assert.Equal(raw.RaiderIo.Data!.Value.GetRawText(), completed.RaiderIo!.Data!.Value.GetRawText());
            Assert.Equal("Home#123", (await http.GetFromJsonAsync<PlayerResponse>($"/api/players/{player.Id}"))!.BattleTag);
            (await http.PostAsync($"/api/players/{others[1].Id}/enrichment", null)).EnsureSuccessStatusCode();
        }
        finally
        {
            release.TrySetResult();
            using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES"));
            await connection.ExecuteAsync("DELETE FROM players WHERE id = ANY(@Ids)", new { Ids = others.Select(other => other.Id).Append(player.Id).ToArray() });
        }
    }

    [PostgresFact]
    public async Task DatabaseUpgradeAndOwnershipRoundTrip()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES")!;
        var configuration = Configuration(new() { ["ConnectionStrings:PostgreSQL"] = connectionString });
        var database = new PlayerDatabaseService(configuration, NullLogger<PlayerDatabaseService>.Instance);
        await database.InitializeDatabaseAsync();
        await database.InitializeDatabaseAsync();
        Assert.True(await database.IsReadyAsync());
        var request = new CreatePlayerRequest
        {
            CharacterName = "Test" + Guid.NewGuid().ToString("N"),
            Realm = "Silvermoon",
            RealmSlug = "silvermoon",
            Class = "Mage",
            LastUpdated = DateTime.UtcNow,
            RaiderIoSummary = "Legacy summary",
            BattleTag = "Old#123",
            DiscordMessageId = 123,
        };
        var player = await database.UpsertPlayerAsync(request);
        try
        {
            using (var legacyConnection = new NpgsqlConnection(connectionString))
            {
                await legacyConnection.ExecuteAsync(
                    """
                    ALTER TABLE players DROP COLUMN discovery_progression, DROP COLUMN raiderio_data,
                        DROP COLUMN warcraftlogs_data, DROP COLUMN wowprogress_data,
                        DROP COLUMN enrichment_attempted_at, DROP COLUMN enriched_at, DROP COLUMN gemini_generated_at;
                    """
                );
            }
            await database.InitializeDatabaseAsync();
            await database.InitializeDatabaseAsync();
            Assert.True(await database.IsReadyAsync());
            Assert.Equal("Legacy summary", (await database.GetPlayerByIdAsync(player.Id))!.RaiderIoSummary);
            await database.UpdatePlayerStatusAsync(player.Id, PlayerStatus.Interested);
            var raw = Element("""{"profile":{"unknown":true},"currentTier":CURRENT}""".Replace("CURRENT", Current));
            var snapshot = Snapshot(raw);
            var snapshots = new PlayerEnrichmentResponse(snapshot, null, null);
            await database.SaveEnrichmentAsync(player.Id, snapshots, PlayerEnrichmentService.Project(snapshots));
            await database.UpdateGeminiTakeAsync(player.Id, "Existing AI");
            await database.UpsertPlayerAsync(
                new CreatePlayerRequest
                {
                    CharacterName = request.CharacterName,
                    Realm = request.Realm,
                    RealmSlug = request.RealmSlug,
                    Class = request.Class,
                    LastUpdated = request.LastUpdated,
                }
            );
            var updated = await database.GetPlayerByIdAsync(player.Id);
            Assert.NotNull(updated!.EnrichedAt);
            Assert.NotNull(updated.GeminiGeneratedAt);
            Assert.Equal(PlayerStatus.Interested, updated.Status);
            Assert.Equal((ulong)123, updated.DiscordMessageId);
            Assert.Equal("Old#123", updated.BattleTag);
            Assert.Equal("Existing AI", updated.GeminiTake);
            Assert.Equal(4, updated.CurrentTierMythicKillCount);
            var stored = await database.GetEnrichmentAsync(player.Id);
            Assert.True(stored.RaiderIo!.Data!.Value.GetProperty("profile").GetProperty("unknown").GetBoolean());
            Assert.Null((await database.GetEnrichmentAsync(player.Id, false)).RaiderIo!.Data);
            Assert.True(await database.TryBeginEnrichmentAsync(player.Id));
            Assert.False(await database.TryBeginEnrichmentAsync(player.Id));
            var failed = stored.RaiderIo with { Outcome = "timeout", LastAttemptAt = DateTimeOffset.UtcNow };
            await database.SaveEnrichmentAsync(player.Id, new(failed, null, null), new(null, null, null, null));
            var retained = await database.GetPlayerByIdAsync(player.Id);
            Assert.Equal(updated.EnrichedAt, retained!.EnrichedAt);
            Assert.Equal(updated.RaiderIoSummary, retained.RaiderIoSummary);
            Assert.Equal(updated.LastUpdated, retained.LastUpdated);
            using var connection = new NpgsqlConnection(connectionString);
            Assert.Equal("object", await connection.ExecuteScalarAsync<string>("SELECT jsonb_typeof(raiderio_data->'data') FROM players WHERE id = @Id", new { player.Id }));
        }
        finally
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.ExecuteAsync("DELETE FROM players WHERE id = @Id", new { player.Id });
        }
    }

    [PostgresFact]
    public async Task LocalResultsPreserveOtherSourcesAndRetainLastGoodDetailsOnFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES")!;
        var database = new PlayerDatabaseService(Configuration(new() { ["ConnectionStrings:PostgreSQL"] = connectionString }), NullLogger<PlayerDatabaseService>.Instance);
        await database.InitializeDatabaseAsync();
        var player = await database.UpsertPlayerAsync(
            new CreatePlayerRequest
            {
                CharacterName = "Local" + Guid.NewGuid().ToString("N"),
                Realm = "Silvermoon",
                RealmSlug = "silvermoon",
                Class = "Mage",
                LastUpdated = DateTime.UtcNow,
            }
        );
        try
        {
            var serverSnapshot = Snapshot(Element("{\"retained\":true}"));
            await database.SaveEnrichmentAsync(player.Id, new(serverSnapshot, serverSnapshot, null), new(null, null, null, null));
            await database.SaveWowProgressAsync(
                player.Id,
                new(Guid.NewGuid(), "success", new("Local#123", "Biography", "English", "Fire", ["Joined guild"])),
                CancellationToken.None
            );
            var stored = await database.GetEnrichmentAsync(player.Id);
            var fetchedAt = stored.WowProgress!.FetchedAt;
            Assert.Equal("Biography", stored.WowProgress.Data!.Value.GetProperty("bio").GetString());
            Assert.True(stored.RaiderIo!.Data!.Value.GetProperty("retained").GetBoolean());
            Assert.True(stored.WarcraftLogs!.Data!.Value.GetProperty("retained").GetBoolean());
            await database.SaveEnrichmentAsync(player.Id, new(serverSnapshot, serverSnapshot, null), new(null, null, null, null));
            Assert.Equal(fetchedAt, (await database.GetEnrichmentAsync(player.Id)).WowProgress!.FetchedAt);
            await database.SaveWowProgressAsync(player.Id, new(Guid.NewGuid(), "unavailable", null), CancellationToken.None);
            stored = await database.GetEnrichmentAsync(player.Id);
            Assert.Equal("unavailable", stored.WowProgress!.Outcome);
            Assert.Equal(fetchedAt, stored.WowProgress.FetchedAt);
            Assert.Equal("Biography", stored.WowProgress.Data!.Value.GetProperty("bio").GetString());
            Assert.Equal("Local#123", (await database.GetPlayerByIdAsync(player.Id))!.BattleTag);
        }
        finally
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.ExecuteAsync("DELETE FROM players WHERE id = @Id", new { player.Id });
        }
    }

    [PostgresFact]
    public async Task SeenBulkUpsertCombinesDuplicateKeysAndNeverMovesLastSeenBackwards()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES")!;
        var database = new PlayerDatabaseService(Configuration(new() { ["ConnectionStrings:PostgreSQL"] = connectionString }), NullLogger<PlayerDatabaseService>.Instance);
        await database.InitializeDatabaseAsync();
        var existingName = "SeenExisting" + Guid.NewGuid().ToString("N");
        var newName = "SeenNew" + Guid.NewGuid().ToString("N");
        const string realm = "Silvermoon";
        var timestamp = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await database.AddSeenPlayerAsync(existingName, realm, timestamp.AddDays(-1));
            await database.BulkAddSeenPlayersAsync(
                [
                    (existingName, realm, timestamp),
                    (newName, realm, timestamp.AddHours(2)),
                    (existingName, realm, timestamp.AddHours(2)),
                    (newName, realm, timestamp),
                    (existingName, realm, timestamp.AddHours(1)),
                    (newName, realm, timestamp.AddHours(1)),
                    (newName, realm, timestamp.AddHours(2)),
                ]
            );
            Assert.Equal(
                2,
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM seen_players WHERE character_name = ANY(@Names) AND realm = @Realm",
                    new { Names = new[] { existingName, newName }, Realm = realm }
                )
            );
            Assert.Equal(timestamp.AddHours(2), await database.GetLastSeenAtAsync(existingName, realm));
            Assert.Equal(timestamp.AddHours(2), await database.GetLastSeenAtAsync(newName, realm));
            Assert.Equal(
                timestamp,
                await connection.ExecuteScalarAsync<DateTime>(
                    "SELECT first_seen_at FROM seen_players WHERE character_name = @Name AND realm = @Realm",
                    new { Name = newName, Realm = realm }
                )
            );

            await database.BulkAddSeenPlayersAsync([(existingName, realm, timestamp), (newName, realm, timestamp.AddHours(1)), (newName, realm, timestamp)]);
            Assert.Equal(timestamp.AddHours(2), await database.GetLastSeenAtAsync(existingName, realm));
            Assert.Equal(timestamp.AddHours(2), await database.GetLastSeenAtAsync(newName, realm));
            Assert.Equal(
                timestamp.AddDays(-1),
                await connection.ExecuteScalarAsync<DateTime>(
                    "SELECT first_seen_at FROM seen_players WHERE character_name = @Name AND realm = @Realm",
                    new { Name = existingName, Realm = realm }
                )
            );
            await database.BulkAddSeenPlayersAsync([]);
        }
        finally
        {
            await connection.ExecuteAsync(
                "DELETE FROM seen_players WHERE character_name = ANY(@Names) AND realm = @Realm",
                new { Names = new[] { existingName, newName }, Realm = realm }
            );
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? extra = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RaiderIO:ApiKey"] = "test",
                    ["WarcraftLogs:ClientId"] = "test",
                    ["WarcraftLogs:ClientSecret"] = "test",
                    ["FrontendBaseUrl"] = "http://localhost",
                    ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=unused",
                }
            )
            .AddInMemoryCollection(extra ?? [])
            .Build();

    private static PlayerEnrichmentService Service() =>
        new(
            new PlayerDatabaseService(Configuration(), NullLogger<PlayerDatabaseService>.Instance),
            Sources(new Handler(_ => throw new InvalidOperationException())),
            NullLogger<PlayerEnrichmentService>.Instance,
            new WowProgressJobQueue(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System)
        );

    private static PlayerSourceClient Sources(HttpMessageHandler handler) => new(new ClientFactory(handler), Configuration());

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static SourceSnapshot Snapshot(JsonElement data) => new(1, data, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "success");

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPER_RECRUITER_TEST_POSTGRES")))
            Skip = "Set SUPER_RECRUITER_TEST_POSTGRES to an isolated disposable PostgreSQL database.";
    }
}
