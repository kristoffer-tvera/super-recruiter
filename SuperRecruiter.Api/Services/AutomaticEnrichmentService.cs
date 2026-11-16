using System.Threading.Channels;

namespace SuperRecruiter.Api.Services;

/// <summary>
/// Enriches newly discovered players from the Raider.IO and WarcraftLogs APIs, one at a time.
/// WoWProgress is deliberately excluded; it stays an on-demand action.
/// </summary>
public sealed class AutomaticEnrichmentService(PlayerDatabaseService database, PlayerEnrichmentService enrichment, IConfiguration configuration, ILogger<AutomaticEnrichmentService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PauseBetweenPlayers = TimeSpan.FromSeconds(2);
    private readonly Channel<int> _queue = Channel.CreateBounded<int>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly bool _enabled = configuration.GetValue("Enrichment:Automatic", true);

    public void Enqueue(int playerId)
    {
        if (_enabled && playerId > 0)
            _queue.Writer.TryWrite(playerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await EnrichAsync(id, stoppingToken);
                await Task.Delay(PauseBetweenPlayers, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task EnrichAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var player = await database.GetPlayerByIdAsync(id, cancellationToken);
            if (player is null || player.EnrichedAt is not null)
                return;
            await enrichment.RefreshAsync(player, cancellationToken, includeWowProgress: false);
        }
        catch (EnrichmentBusyException)
        {
            // Cooldown or a manual refresh is already running for this player; nothing to do.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Automatic enrichment failed for player {PlayerId} with {ExceptionType}", id, exception.GetType().Name);
        }
    }
}
