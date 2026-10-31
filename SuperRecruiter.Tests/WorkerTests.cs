using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;
using SuperRecruiter.Worker;
using SuperRecruiter.Worker.Services;
using Xunit;

namespace SuperRecruiter.Tests;

public sealed class WorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilterConfigFailuresBackOffAndPreserveLastGoodSettings(bool alreadyCached)
    {
        var clock = new ManualClock();
        var requests = 0;
        var fail = !alreadyCached;
        using var http = new HttpClient(
            new Handler(_ =>
            {
                requests++;
                return Task.FromResult(
                    fail
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AdminConfigResponse { BossKills = 7 }) }
                );
            })
        )
        {
            BaseAddress = new("http://api.test/"),
        };
        var api = new SuperRecruiterApiClient(http, NullLogger<SuperRecruiterApiClient>.Instance);
        var filters = new AdminFilterService(api, NullLogger<AdminFilterService>.Instance, clock);
        if (alreadyCached)
        {
            Assert.Equal(7, (await filters.GetConfigAsync())!.BossKills);
            fail = true;
            clock.Advance(TimeSpan.FromHours(1));
        }
        var previousRequests = requests;
        for (var index = 0; index < 20; index++)
        {
            var config = await filters.GetConfigAsync();
            Assert.Equal(alreadyCached ? 7 : null, config?.BossKills);
        }
        Assert.Equal(previousRequests + 1, requests);
        clock.Advance(TimeSpan.FromMinutes(5));
        fail = false;
        Assert.Equal(7, (await filters.GetConfigAsync())!.BossKills);
        Assert.Equal(previousRequests + 2, requests);
    }

    [Fact]
    public async Task EmptyFeedsOnlyReadMemoryCachedDiscoveryEvenAcrossCleanupIntervals()
    {
        using var fixture = new WorkerFixture { HasFeed = false };
        for (var index = 0; index < 12; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromHours(6));
            await fixture.ScanAsync();
        }
        Assert.Equal(12, fixture.DiscoveryReads);
        Assert.Equal(fixture.DiscoveryReads, fixture.ApiRequests);
    }

    [Fact]
    public void MergingDuplicateCandidatesPreservesEveryDiscoverySource()
    {
        var wowProgress = new Player
        {
            CharacterName = "Example",
            Realm = "Silvermoon",
            LastUpdated = new DateTime(2026, 10, 25),
            Source = LfgSource.WoWProgress,
        };
        var raiderIo = new Player
        {
            CharacterName = "EXAMPLE",
            Realm = "SILVERMOON",
            LastUpdated = new DateTime(2026, 10, 26),
            Source = LfgSource.RaiderIO,
        };

        var merged = Assert.Single(ScraperWorker.MergePlayers(new[] { wowProgress }, new[] { raiderIo }));

        Assert.Equal(raiderIo.LastUpdated, merged.LastUpdated);
        Assert.Equal(new[] { LfgSource.WoWProgress, LfgSource.RaiderIO }, merged.Sources.OrderBy(source => source).ToArray());
    }

    [Fact]
    public async Task UnchangedFeedDoesNotRefreshCacheOrRunMaintenance()
    {
        using var fixture = new WorkerFixture();
        await fixture.ScanAsync();
        var requestsAfterFirstScan = fixture.ApiRequests - fixture.DiscoveryReads;
        for (var index = 0; index < 12; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromHours(6));
            await fixture.ScanAsync();
        }
        Assert.Equal(requestsAfterFirstScan, fixture.ApiRequests - fixture.DiscoveryReads);
        Assert.Equal(1, fixture.SeenReads);
        Assert.Equal(1, fixture.PlayerReads);
        Assert.Equal(1, fixture.Cleanups);
        Assert.Equal(1, fixture.CreateAttempts);
    }

    [Fact]
    public async Task CacheExpiryAndDailyCleanupAreCheckedOnlyWhenActivityRequiresThem()
    {
        using var fixture = new WorkerFixture();
        await fixture.ScanAsync();
        fixture.RelistedAt = fixture.RelistedAt.AddMinutes(30);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        await fixture.ScanAsync();
        Assert.Equal(1, fixture.PlayerReads);
        Assert.Equal(1, fixture.Cleanups);
        fixture.RelistedAt = fixture.RelistedAt.AddHours(3);
        fixture.Clock.Advance(TimeSpan.FromHours(3));
        await fixture.ScanAsync();
        Assert.Equal(2, fixture.PlayerReads);
        Assert.Equal(1, fixture.Cleanups);
        fixture.RelistedAt = fixture.RelistedAt.AddDays(1);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        await fixture.ScanAsync();
        Assert.Equal(3, fixture.PlayerReads);
        Assert.Equal(2, fixture.Cleanups);
        Assert.Equal(1, fixture.SeenReads);
    }

    [Fact]
    public async Task FailedIngestionIsNotMarkedSeenAndRetriesOnNextScan()
    {
        using var fixture = new WorkerFixture { FailCreateOnce = true };
        await fixture.ScanAsync();
        Assert.Null(fixture.Cache.GetLastSeenAt("Example", "Silvermoon"));
        Assert.Equal(0, fixture.SeenWrites);
        await fixture.ScanAsync();
        Assert.Equal(2, fixture.CreateAttempts);
        Assert.Equal(1, fixture.SeenWrites);
        Assert.Equal(fixture.RelistedAt, fixture.Cache.GetLastSeenAt("Example", "Silvermoon"));
    }

    [Fact]
    public async Task FailedSeenFlushRetriesWithoutReingestingPlayers()
    {
        using var fixture = new WorkerFixture { FailFlushOnce = true };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.ScanAsync());
        await fixture.ScanAsync();
        Assert.Equal(1, fixture.CreateAttempts);
        Assert.Equal(2, fixture.SeenWrites);
        Assert.Equal(1, fixture.Cleanups);
    }

    [Fact]
    public async Task SeenBatchDeduplicatesCharactersAndKeepsLatestTimestamp()
    {
        using var fixture = new WorkerFixture();
        var timestamp = fixture.RelistedAt;
        fixture.Cache.QueueSeenPlayer("Example", "Silvermoon", timestamp);
        fixture.Cache.QueueSeenPlayer("EXAMPLE", "SILVERMOON", timestamp.AddMinutes(1));
        fixture.Cache.QueueSeenPlayer("Example", "Silvermoon", timestamp);
        Assert.True(await fixture.Cache.FlushSeenPlayerBatchAsync());
        Assert.Equal(timestamp.AddMinutes(1), Assert.Single(fixture.LastSeenBatch!).LastUpdated);
        Assert.False(await fixture.Cache.FlushSeenPlayerBatchAsync());
        Assert.Equal(1, fixture.SeenWrites);
    }

    [Fact]
    public async Task BlacklistedCandidatesDoNotTriggerIngestionOrWrites()
    {
        using var fixture = new WorkerFixture { Blacklisted = true };
        await fixture.ScanAsync();
        Assert.Equal(1, fixture.PlayerReads);
        Assert.Equal(0, fixture.CreateAttempts);
        Assert.Equal(0, fixture.SeenWrites);
        Assert.Equal(0, fixture.Cleanups);
    }

    [Fact]
    public async Task LocalCollectorRetriesUploadWithoutRefetchingOrClaimingAnotherJob()
    {
        var job = new WowProgressJobResponse(Guid.NewGuid(), Guid.NewGuid(), 1, "Example", "silvermoon");
        var claims = 0;
        var scrapes = 0;
        var uploads = 0;
        using var apiHttp = new HttpClient(
            new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("claim"))
                {
                    claims++;
                    return Json(job);
                }
                Assert.EndsWith($"{job.Id}/complete", request.RequestUri.AbsolutePath);
                var result = (await request.Content!.ReadFromJsonAsync<CompleteWowProgressJobRequest>())!;
                Assert.Equal(job.LeaseId, result.LeaseId);
                Assert.Equal("success", result.Outcome);
                Assert.Equal("Example#123", result.Details!.BattleTag);
                uploads++;
                return new(uploads == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
            })
        )
        {
            BaseAddress = new("http://api.test/"),
        };
        using var solverHttp = new HttpClient(
            new Handler(async request =>
            {
                scrapes++;
                var payload = await request.Content!.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("https://www.wowprogress.com/character/eu/silvermoon/Example", payload.GetProperty("url").GetString());
                return Json(new { status = "ok", solution = new { status = 200, response = "<div class='registeredTo'><span class='profileBattletag'>Example#123</span></div>" } });
            })
        );
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlareSolverrUrl"] = "http://solver.test/v1" }).Build();
        using var worker = new WowProgressWorker(
            new WowProgressService(NullLogger<WowProgressService>.Instance, solverHttp, config),
            new SuperRecruiterApiClient(apiHttp, NullLogger<SuperRecruiterApiClient>.Instance),
            config,
            NullLogger<WowProgressWorker>.Instance
        );
        await Assert.ThrowsAsync<HttpRequestException>(() => worker.ProcessNextJobAsync(CancellationToken.None));
        await worker.ProcessNextJobAsync(CancellationToken.None);
        Assert.Equal(1, claims);
        Assert.Equal(1, scrapes);
        Assert.Equal(2, uploads);
    }

    [Theory]
    [InlineData(200, "<h1>Challenge page</h1>", "not_found_or_private")]
    [InlineData(403, "blocked", "unavailable")]
    [InlineData(429, "limited", "rate_limited")]
    public async Task LocalCollectorReportsUpstreamFailuresWithoutUploadingDetails(int status, string html, string outcome)
    {
        var job = new WowProgressJobResponse(Guid.NewGuid(), Guid.NewGuid(), 1, "Example", "silvermoon");
        CompleteWowProgressJobRequest? saved = null;
        using var apiHttp = new HttpClient(
            new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("claim"))
                    return Json(job);
                saved = await request.Content!.ReadFromJsonAsync<CompleteWowProgressJobRequest>();
                return new(HttpStatusCode.NoContent);
            })
        )
        {
            BaseAddress = new("http://api.test/"),
        };
        using var solverHttp = new HttpClient(new Handler(_ => Task.FromResult(Json(new { status = "ok", solution = new { status, response = html } }))));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlareSolverrUrl"] = "http://solver.test/v1" }).Build();
        using var worker = new WowProgressWorker(
            new WowProgressService(NullLogger<WowProgressService>.Instance, solverHttp, config),
            new SuperRecruiterApiClient(apiHttp, NullLogger<SuperRecruiterApiClient>.Instance),
            config,
            NullLogger<WowProgressWorker>.Instance
        );
        await worker.ProcessNextJobAsync(CancellationToken.None);
        Assert.Equal(outcome, saved!.Outcome);
        Assert.Null(saved.Details);
    }

    [Fact]
    public async Task LocalCollectorDoesNotScrapeWhenNoJobIsAvailable()
    {
        using var apiHttp = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)))) { BaseAddress = new("http://api.test/") };
        using var solverHttp = new HttpClient(new Handler(_ => throw new InvalidOperationException("Idle polling must not scrape")));
        var config = new ConfigurationBuilder().Build();
        using var worker = new WowProgressWorker(
            new WowProgressService(NullLogger<WowProgressService>.Instance, solverHttp, config),
            new SuperRecruiterApiClient(apiHttp, NullLogger<SuperRecruiterApiClient>.Instance),
            config,
            NullLogger<WowProgressWorker>.Instance
        );
        await worker.ProcessNextJobAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailedDiscoveryDoesNotPublishAnEmptyListing()
    {
        using var apiHttp = new HttpClient(new Handler(_ => throw new InvalidOperationException("Failed discovery must not replace the cached listing")))
        {
            BaseAddress = new("http://api.test/"),
        };
        using var solverHttp = new HttpClient(new Handler(_ => Task.FromResult(Json(new { status = "error" }))));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlareSolverrUrl"] = "http://solver.test/v1" }).Build();
        using var worker = new WowProgressWorker(
            new WowProgressService(NullLogger<WowProgressService>.Instance, solverHttp, config),
            new SuperRecruiterApiClient(apiHttp, NullLogger<SuperRecruiterApiClient>.Instance),
            config,
            NullLogger<WowProgressWorker>.Instance
        );
        await Assert.ThrowsAsync<WowProgressFetchException>(() => worker.PublishDiscoveriesAsync(CancellationToken.None));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class WorkerFixture : IDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly HttpClient _apiHttp;
        private readonly HttpClient _wowHttp;
        private readonly HttpClient _raiderHttp;
        private readonly ScraperWorker _worker;
        public ManualClock Clock { get; } = new();
        public PlayerCacheService Cache { get; }
        public bool HasFeed { get; set; } = true;
        public bool FailCreateOnce { get; set; }
        public bool FailFlushOnce { get; set; }
        public bool Blacklisted { get; set; }
        public DateTime RelistedAt { get; set; } = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        public int ApiRequests { get; private set; }
        public int DiscoveryReads { get; private set; }
        public int SeenReads { get; private set; }
        public int PlayerReads { get; private set; }
        public int SeenWrites { get; private set; }
        public int CreateAttempts { get; private set; }
        public int Cleanups { get; private set; }
        public List<SeenPlayerRequest>? LastSeenBatch { get; private set; }

        public WorkerFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["PollingIntervalMinutes"] = "30",
                        ["RaiderIO:ApiKey"] = "test",
                        ["FrontendBaseUrl"] = "http://test",
                        ["FlareSolverrUrl"] = "http://solver.test/v1",
                    }
                )
                .Build();
            _apiHttp = new HttpClient(new Handler(HandleApiAsync)) { BaseAddress = new("http://api.test/") };
            _wowHttp = new HttpClient(new Handler(_ => Task.FromResult(Json(new { status = "ok", solution = new { status = 200, response = "<html></html>" } }))));
            _raiderHttp = new HttpClient(
                new Handler(request =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("search-advanced"))
                        return Task.FromResult(
                            Json(
                                new
                                {
                                    matches = HasFeed
                                        ? new[]
                                        {
                                            new
                                            {
                                                name = "Example",
                                                data = new
                                                {
                                                    realm = new { name = "Silvermoon", slug = "silvermoon" },
                                                    @class = new { name = "Mage" },
                                                    itemLevelEquipped = 650,
                                                    recruitment = new { guild_raids = new { profile = new { throttled_published_at = RelistedAt, languages = new[] { 1 } } } },
                                                },
                                            },
                                        }
                                        : [],
                                }
                            )
                        );
                    return Task.FromResult(Json(new { raid_progression = new Dictionary<string, object> { ["current"] = new { total_bosses = 8, mythic_bosses_killed = 4 } } }));
                })
            );
            var api = new SuperRecruiterApiClient(_apiHttp, NullLogger<SuperRecruiterApiClient>.Instance);
            var raider = new RaiderIOService(NullLogger<RaiderIOService>.Instance, _raiderHttp, configuration);
            var discord = new DiscordBotService(NullLogger<DiscordBotService>.Instance, configuration, _services);
            var filters = new AdminFilterService(api, NullLogger<AdminFilterService>.Instance);
            var ingestion = new PlayerIngestionService(NullLogger<PlayerIngestionService>.Instance, raider, api, filters, discord);
            Cache = new PlayerCacheService(api, NullLogger<PlayerCacheService>.Instance, configuration, Clock);
            _worker = new ScraperWorker(NullLogger<ScraperWorker>.Instance, raider, api, Cache, ingestion, discord, configuration, Clock);
        }

        public Task ScanAsync() => _worker.RunScanCycleAsync(CancellationToken.None);

        private async Task<HttpResponseMessage> HandleApiAsync(HttpRequestMessage request)
        {
            ApiRequests++;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/wowprogress/discoveries":
                    DiscoveryReads++;
                    return Json(Array.Empty<Player>());
                case "/players/seen/all":
                    SeenReads++;
                    return Json(Array.Empty<SeenPlayerResponse>());
                case "/players/cache":
                    PlayerReads++;
                    return Json(
                        Blacklisted
                            ? new[]
                            {
                                new PlayerCacheResponse
                                {
                                    CharacterName = "Example",
                                    Realm = "Silvermoon",
                                    Status = PlayerStatus.Blacklisted,
                                },
                            }
                            : []
                    );
                case "/config":
                    return Json(new AdminConfigResponse());
                case "/players":
                    CreateAttempts++;
                    if (FailCreateOnce)
                    {
                        FailCreateOnce = false;
                        return new(HttpStatusCode.ServiceUnavailable);
                    }
                    return Json(new PlayerResponse { CharacterName = "Example", Realm = "Silvermoon" });
                case "/players/seen/bulk":
                    SeenWrites++;
                    LastSeenBatch = await request.Content!.ReadFromJsonAsync<List<SeenPlayerRequest>>();
                    if (FailFlushOnce)
                    {
                        FailFlushOnce = false;
                        return new(HttpStatusCode.ServiceUnavailable);
                    }
                    return Json(new { });
                case "/players/seen/cleanup":
                    Cleanups++;
                    return Json(new { });
                default:
                    throw new InvalidOperationException(request.RequestUri.ToString());
            }
        }

        public void Dispose()
        {
            _worker.Dispose();
            _apiHttp.Dispose();
            _wowHttp.Dispose();
            _raiderHttp.Dispose();
            _services.Dispose();
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
