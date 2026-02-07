using System.Data;
using Dapper;
using Npgsql;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public class PlayerDatabaseService : IPlayerDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<PlayerDatabaseService> _logger;

    public PlayerDatabaseService(
        IConfiguration configuration,
        ILogger<PlayerDatabaseService> logger
    )
    {
        _connectionString =
            configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException(
                "PostgreSQL connection string not found in configuration"
            );
        _logger = logger;
    }

    private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task InitializeDatabaseAsync()
    {
        using var connection = CreateConnection();
        connection.Open();

        // Create seen_players table
        var createSeenPlayersTable =
            @"
            CREATE TABLE IF NOT EXISTS seen_players (
                id SERIAL PRIMARY KEY,
                character_name VARCHAR(255) NOT NULL,
                realm VARCHAR(255) NOT NULL,
                first_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT unique_player UNIQUE (character_name, realm)
            );
            
            CREATE INDEX IF NOT EXISTS idx_seen_players_lookup 
            ON seen_players(character_name, realm);
            
            CREATE INDEX IF NOT EXISTS idx_seen_players_last_seen 
            ON seen_players(last_seen_at);
        ";

        // Create blacklisted_players table
        var createBlacklistTable =
            @"
            CREATE TABLE IF NOT EXISTS blacklisted_players (
                id SERIAL PRIMARY KEY,
                character_name VARCHAR(255) NOT NULL,
                realm VARCHAR(255) NOT NULL,
                reason TEXT,
                blacklisted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT unique_blacklisted_player UNIQUE (character_name, realm)
            );
            
            CREATE INDEX IF NOT EXISTS idx_blacklisted_players_lookup 
            ON blacklisted_players(character_name, realm);
        ";

        await connection.ExecuteAsync(createSeenPlayersTable);
        await connection.ExecuteAsync(createBlacklistTable);

        _logger.LogInformation("Database tables initialized successfully");
    }

    public async Task<bool> HasSeenPlayerAsync(string characterName, string realm)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            SELECT COUNT(1) 
            FROM seen_players 
            WHERE LOWER(character_name) = LOWER(@CharacterName) 
            AND LOWER(realm) = LOWER(@Realm)";

        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { CharacterName = characterName, Realm = realm }
        );
        return count > 0;
    }

    public async Task<DateTime?> GetLastSeenAtAsync(string characterName, string realm)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            SELECT last_seen_at 
            FROM seen_players 
            WHERE LOWER(character_name) = LOWER(@CharacterName) 
            AND LOWER(realm) = LOWER(@Realm)";

        var lastSeenAt = await connection.QueryFirstOrDefaultAsync<DateTime?>(
            sql,
            new { CharacterName = characterName, Realm = realm }
        );
        return lastSeenAt;
    }

    public async Task AddSeenPlayerAsync(string characterName, string realm, DateTime lastUpdated)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            INSERT INTO seen_players (character_name, realm, first_seen_at, last_seen_at)
            VALUES (@CharacterName, @Realm, @LastUpdated, @LastUpdated)
            ON CONFLICT (character_name, realm) 
            DO UPDATE SET last_seen_at = @LastUpdated";

        await connection.ExecuteAsync(
            sql,
            new
            {
                CharacterName = characterName,
                Realm = realm,
                LastUpdated = lastUpdated,
            }
        );

        _logger.LogDebug(
            "Added/updated seen player: {Character}-{Realm} (LastUpdated: {LastUpdated})",
            characterName,
            realm,
            lastUpdated
        );
    }

    public async Task<bool> IsPlayerBlacklistedAsync(string characterName, string realm)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            SELECT COUNT(1) 
            FROM blacklisted_players 
            WHERE LOWER(character_name) = LOWER(@CharacterName) 
            AND LOWER(realm) = LOWER(@Realm)";

        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { CharacterName = characterName, Realm = realm }
        );
        return count > 0;
    }

    public async Task AddBlacklistedPlayerAsync(
        string characterName,
        string realm,
        string? reason = null
    )
    {
        using var connection = CreateConnection();

        var sql =
            @"
            INSERT INTO blacklisted_players (character_name, realm, reason, blacklisted_at)
            VALUES (@CharacterName, @Realm, @Reason, @Now)
            ON CONFLICT (character_name, realm) 
            DO UPDATE SET reason = @Reason, blacklisted_at = @Now";

        await connection.ExecuteAsync(
            sql,
            new
            {
                CharacterName = characterName,
                Realm = realm,
                Reason = reason,
                Now = DateTime.UtcNow,
            }
        );

        _logger.LogInformation(
            "Blacklisted player: {Character}-{Realm} (Reason: {Reason})",
            characterName,
            realm,
            reason ?? "None"
        );
    }

    public async Task RemoveBlacklistedPlayerAsync(string characterName, string realm)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            DELETE FROM blacklisted_players 
            WHERE LOWER(character_name) = LOWER(@CharacterName) 
            AND LOWER(realm) = LOWER(@Realm)";

        await connection.ExecuteAsync(sql, new { CharacterName = characterName, Realm = realm });

        _logger.LogInformation(
            "Removed blacklisted player: {Character}-{Realm}",
            characterName,
            realm
        );
    }

    public async Task<int> GetSeenPlayersCountAsync()
    {
        using var connection = CreateConnection();

        var sql = "SELECT COUNT(*) FROM seen_players";
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    public async Task CleanupOldSeenPlayersAsync(int daysToKeep = 30)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            DELETE FROM seen_players 
            WHERE last_seen_at < @CutoffDate";

        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var deletedCount = await connection.ExecuteAsync(sql, new { CutoffDate = cutoffDate });

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} old seen player records (older than {Days} days)",
                deletedCount,
                daysToKeep
            );
        }
    }
}
