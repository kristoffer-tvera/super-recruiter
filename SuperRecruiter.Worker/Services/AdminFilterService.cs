using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Caches the admin filter config from the API and decides whether a scraped player
/// is noteworthy enough to be posted to Discord. Refreshed once per clock hour.
/// </summary>
public class AdminFilterService(SuperRecruiterApiClient apiClient, ILogger<AdminFilterService> logger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AdminConfigResponse? _config;
    private DateTimeOffset _nextFetchAt = DateTimeOffset.MinValue;

    public async Task<AdminConfigResponse?> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStale())
            return _config;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsStale())
                return _config;

            var config = await apiClient.GetAdminConfigAsync(cancellationToken);
            if (config != null)
            {
                _config = config;
                var now = _clock.GetUtcNow();
                _nextFetchAt = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
                logger.LogInformation(
                    "Admin filter config refreshed: BossKills={BossKills}, AcceptedClasses=[{Classes}]",
                    config.BossKills,
                    string.Join(", ", config.AcceptedClasses)
                );
            }
            else
            {
                _nextFetchAt = _clock.GetUtcNow().AddMinutes(5);
                logger.LogWarning("Admin filter config request returned no content — keeping the previous config");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _nextFetchAt = _clock.GetUtcNow().AddMinutes(5);
            // Keep serving the last known config rather than dropping the filters entirely.
            logger.LogError(ex, "Failed to refresh admin filter config");
        }
        finally
        {
            _refreshLock.Release();
        }

        return _config;
    }

    /// <summary>
    /// True once the clock hour in which the config was fetched has passed.
    /// </summary>
    private bool IsStale() => _clock.GetUtcNow() >= _nextFetchAt;

    /// <summary>
    /// Applies the admin filters. A boss kill cutoff of 0 and an empty class list both mean "no filtering".
    /// </summary>
    public async Task<bool> ShouldPostToDiscordAsync(Player player, int currentTierMythicKillCount, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(cancellationToken);
        if (config == null)
            return true;

        if (config.BossKills > 0 && currentTierMythicKillCount < config.BossKills)
        {
            logger.LogInformation(
                "Not posting {Character}-{Realm} to Discord: {Kills} mythic kills is below the cutoff of {Cutoff}",
                player.CharacterName,
                player.Realm,
                currentTierMythicKillCount,
                config.BossKills
            );
            return false;
        }

        if (config.AcceptedClasses.Count > 0)
        {
            var accepted = config.AcceptedClasses.Select(NormalizeClass).ToHashSet();

            if (!accepted.Contains(NormalizeClass(player.Class)))
            {
                logger.LogInformation("Not posting {Character}-{Realm} to Discord: class {Class} is not accepted", player.CharacterName, player.Realm, player.Class);
                return false;
            }
        }

        return true;
    }

    // "Death Knight", "death knight" and "deathknight" must all compare equal.
    private static string NormalizeClass(string className) => new([.. className.Where(char.IsLetter).Select(char.ToLowerInvariant)]);
}
