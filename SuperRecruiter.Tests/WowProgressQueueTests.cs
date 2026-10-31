using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SuperRecruiter.Api;
using SuperRecruiter.Api.Services;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;
using Xunit;

namespace SuperRecruiter.Tests;

[Collection("Database activity")]
public sealed class WowProgressQueueTests
{
    private static readonly PlayerResponse Player = new()
    {
        Id = 1,
        CharacterName = "Example",
        RealmSlug = "silvermoon",
    };

    [Fact]
    public async Task QueueDeduplicatesAndCompletionRetriesDoNotWriteAgain()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var queue = new WowProgressJobQueue(cache, TimeProvider.System);
        Assert.Null(queue.Claim());
        Assert.True(queue.Enqueue(Player));
        Assert.True(queue.Enqueue(Player));
        var job = Assert.IsType<WowProgressJobResponse>(queue.Claim());
        Assert.Null(queue.Claim());
        Assert.True(queue.IsPending(Player.Id));
        var writes = 0;
        Task Save(WowProgressJobResponse _)
        {
            writes++;
            return Task.CompletedTask;
        }
        Assert.True(await queue.CompleteAsync(job.Id, job.LeaseId, Save));
        Assert.True(await queue.CompleteAsync(job.Id, job.LeaseId, Save));
        Assert.Equal(1, writes);
        Assert.False(queue.IsPending(Player.Id));
        Assert.Null(queue.Claim());
    }

    [Fact]
    public async Task ExpiredLeaseIsReclaimedAndStaleResultsAreRejected()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new Clock();
        var queue = new WowProgressJobQueue(cache, clock);
        queue.Enqueue(Player);
        var first = queue.Claim()!;
        clock.Now = clock.Now.AddMinutes(4);
        var second = queue.Claim()!;
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.LeaseId, second.LeaseId);
        Assert.False(await queue.CompleteAsync(first.Id, first.LeaseId, _ => throw new InvalidOperationException()));
        Assert.True(await queue.CompleteAsync(second.Id, second.LeaseId, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task FailedDatabaseWriteKeepsTheLeaseRetryable()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var queue = new WowProgressJobQueue(cache, TimeProvider.System);
        queue.Enqueue(Player);
        var job = queue.Claim()!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.CompleteAsync(job.Id, job.LeaseId, _ => throw new InvalidOperationException()));
        Assert.True(queue.IsPending(Player.Id));
        Assert.True(await queue.CompleteAsync(job.Id, job.LeaseId, _ => Task.CompletedTask));
    }

    [Fact]
    public void QueueIsBoundedAndPendingStatusPreservesPreviousDataAndEnqueueTime()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new Clock();
        var queue = new WowProgressJobQueue(cache, clock);
        for (var index = 1; index <= 1000; index++)
            Assert.True(
                queue.Enqueue(
                    new PlayerResponse
                    {
                        Id = index,
                        CharacterName = "Example",
                        RealmSlug = "silvermoon",
                    }
                )
            );
        Assert.False(queue.Enqueue(new PlayerResponse { Id = 1001 }));
        var queuedAt = clock.Now;
        clock.Now = clock.Now.AddMinutes(2);
        var previous = new SourceSnapshot(1, System.Text.Json.JsonSerializer.SerializeToElement(new { bio = "Old" }), queuedAt.AddDays(-1), queuedAt.AddDays(-1), "success");
        var pending = queue.WithPending(new(null, null, previous), 1).WowProgress!;
        Assert.Equal(queuedAt, pending.LastAttemptAt);
        Assert.Equal(previous.FetchedAt, pending.FetchedAt);
        Assert.Equal("Old", pending.Data!.Value.GetProperty("bio").GetString());
        clock.Now = clock.Now.AddDays(2);
        Assert.False(queue.IsPending(1));
        Assert.True(queue.Enqueue(Player));
    }

    [Fact]
    public void DiscoveryCacheStartsEmptyAndReplacesThePreviousListing()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var queue = new WowProgressJobQueue(cache, TimeProvider.System);
        Assert.Empty(queue.GetDiscoveries());
        queue.PublishDiscoveries([new Player { CharacterName = "Example", Realm = "Silvermoon" }]);
        Assert.Equal("Example", Assert.Single(queue.GetDiscoveries()).CharacterName);
        queue.PublishDiscoveries([]);
        Assert.Empty(queue.GetDiscoveries());
    }

    [Fact]
    public async Task MemoryEndpointsNeverContactPostgres()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused" });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WowProgressJobQueue>();
        builder.Services.AddSingleton<PlayerDatabaseService>();
        builder.Services.AddSingleton<PlayerSourceClient>();
        builder.Services.AddSingleton<PlayerEnrichmentService>();
        builder.Services.AddHttpClient<GeminiService>();
        await using var app = builder.Build();
        app.MapGroup("/api").MapEndpoints();
        await app.StartAsync();
        using var http = app.GetTestClient();
        var databaseOperations = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => Interlocked.Increment(ref databaseOperations),
        };
        ActivitySource.AddActivityListener(listener);
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/wowprogress/jobs/claim", null)).StatusCode);
            Assert.False((await http.GetFromJsonAsync<WowProgressPendingResponse>("/api/wowprogress/jobs/pending/1"))!.Pending);
            Assert.Empty((await http.GetFromJsonAsync<Player[]>("/api/wowprogress/discoveries"))!);
        }
        (
            await http.PutAsJsonAsync(
                "/api/wowprogress/discoveries",
                new[]
                {
                    new Player { CharacterName = "Example", Realm = "Silvermoon" },
                }
            )
        ).EnsureSuccessStatusCode();
        Assert.Single((await http.GetFromJsonAsync<Player[]>("/api/wowprogress/discoveries"))!);
        app.Services.GetRequiredService<WowProgressJobQueue>().Enqueue(Player);
        var claimed = await http.PostAsync("/api/wowprogress/jobs/claim", null);
        var job = (await claimed.Content.ReadFromJsonAsync<WowProgressJobResponse>())!;
        Assert.Equal(Player.Id, job.PlayerId);
        Assert.True((await http.GetFromJsonAsync<WowProgressPendingResponse>("/api/wowprogress/jobs/pending/1"))!.Pending);
        var stale = await http.PostAsJsonAsync($"/api/wowprogress/jobs/{job.Id}/complete", new CompleteWowProgressJobRequest(Guid.NewGuid(), "timeout", null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var invalid = await http.PostAsJsonAsync($"/api/wowprogress/jobs/{job.Id}/complete", new CompleteWowProgressJobRequest(job.LeaseId, "success", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, databaseOperations);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
