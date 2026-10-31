using System.Text.Json;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Worker.Services;

namespace SuperRecruiter.Worker;

public sealed class WowProgressWorker(WowProgressService source, SuperRecruiterApiClient api, IConfiguration configuration, ILogger<WowProgressWorker> logger) : BackgroundService
{
    private (Guid Id, CompleteWowProgressJobRequest Result)? _pendingResult;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            RunLoopAsync(PublishDiscoveriesAsync, TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("PollingIntervalMinutes", 30))), stoppingToken),
            RunLoopAsync(ProcessNextJobAsync, TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("WowProgress:JobPollingIntervalSeconds", 10))), stoppingToken)
        );

    internal async Task PublishDiscoveriesAsync(CancellationToken cancellationToken)
    {
        var players = await source.GetLookingForGuildPlayersAsync(cancellationToken);
        await api.PublishWowProgressDiscoveriesAsync(players, cancellationToken);
    }

    internal async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        if (_pendingResult is null)
        {
            var job = await api.ClaimWowProgressJobAsync(cancellationToken);
            if (job is null)
                return;
            CompleteWowProgressJobRequest result;
            try
            {
                var details = await source.GetCharacterDetailsAsync(job.CharacterName, job.RealmSlug, cancellationToken);
                result = new(job.LeaseId, "success", details);
                if (JsonSerializer.SerializeToUtf8Bytes(result).Length > 512 * 1024)
                    result = new(job.LeaseId, "response_too_large", null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new(job.LeaseId, "timeout", null);
            }
            catch (WowProgressFetchException exception)
            {
                result = new(job.LeaseId, exception.Outcome, null);
            }
            catch (JsonException)
            {
                result = new(job.LeaseId, "invalid_response", null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("WoWProgress job {JobId} failed with {ExceptionType}", job.Id, exception.GetType().Name);
                result = new(job.LeaseId, "upstream_error", null);
            }
            _pendingResult = (job.Id, result);
        }
        var pending = _pendingResult.Value;
        await api.CompleteWowProgressJobAsync(pending.Id, pending.Result, cancellationToken);
        _pendingResult = null;
    }

    private async Task RunLoopAsync(Func<CancellationToken, Task> action, TimeSpan interval, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await action(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Local WoWProgress cycle failed; retrying after {Interval}", interval);
            }
            await Task.Delay(interval, stoppingToken);
        }
    }
}
