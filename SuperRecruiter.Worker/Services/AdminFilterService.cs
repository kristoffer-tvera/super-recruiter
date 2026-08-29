using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Caches the admin filter config from the API and decides whether a scraped player
/// is noteworthy enough to be posted to Discord. Refreshed once per clock hour.
/// </summary>
public class AdminFilterService(SuperRecruiterApiClient apiClient, ILogger<AdminFilterService> logger)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AdminConfigResponse? _config;
    private DateTime _fetchedAtUtc;

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
                _fetchedAtUtc = DateTime.UtcNow;
                logger.LogInformation(
                    "Admin filter config refreshed: BossKills={BossKills}, AcceptedClasses=[{Classes}]",
                    config.BossKills,
                    string.Join(", ", config.AcceptedClasses)
                );
            }
            else
            {
                logger.LogWarning("Admin filter config request returned no content — keeping the previous config");
            }
        }
        catch (Exception ex)
        {
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
    private bool IsStale()
    {
        if (_config == null)
            return true;

        var topOfNextHour = new DateTime(_fetchedAtUtc.Year, _fetchedAtUtc.Month, _fetchedAtUtc.Day, _fetchedAtUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        return DateTime.UtcNow >= topOfNextHour;
    }

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
