using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IPlayerDatabaseService
{
    Task InitializeDatabaseAsync();
    Task<bool> HasSeenPlayerAsync(string characterName, string realm);
    Task<DateTime?> GetLastSeenAtAsync(string characterName, string realm);
    Task AddSeenPlayerAsync(string characterName, string realm, DateTime lastUpdated);
    Task<bool> IsPlayerBlacklistedAsync(string characterName, string realm);
    Task AddBlacklistedPlayerAsync(string characterName, string realm, string? reason = null);
    Task RemoveBlacklistedPlayerAsync(string characterName, string realm);
    Task<int> GetSeenPlayersCountAsync();
    Task CleanupOldSeenPlayersAsync(int daysToKeep = 30);
}
