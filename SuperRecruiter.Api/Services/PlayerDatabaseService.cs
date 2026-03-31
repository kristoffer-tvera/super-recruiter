using System.Data;
using Dapper;
using Npgsql;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public class PlayerDatabaseService
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

        var createPlayersTable =
            @"
            CREATE TABLE IF NOT EXISTS players (
                id SERIAL PRIMARY KEY,
                character_name VARCHAR(255) NOT NULL,
                class VARCHAR(100) NOT NULL,
                realm VARCHAR(255) NOT NULL,
                realm_slug VARCHAR(255) NOT NULL,
                item_level DOUBLE PRECISION NOT NULL DEFAULT 0,
                last_updated TIMESTAMP NOT NULL,
                character_url TEXT NOT NULL DEFAULT '',
                battletag VARCHAR(255),
                bio TEXT,
                languages VARCHAR(500),
                specs_playing VARCHAR(500),
                guild_history TEXT[] DEFAULT '{}',
                raiderio_summary TEXT,
                warcraftlogs_summary TEXT,
                gemini_take TEXT,
                status INTEGER NOT NULL DEFAULT 0,
                discord_message_id BIGINT,
                discord_channel_id BIGINT,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT unique_player_record UNIQUE (character_name, realm)
            );

            CREATE INDEX IF NOT EXISTS idx_players_lookup
            ON players(character_name, realm);

            CREATE INDEX IF NOT EXISTS idx_players_status
            ON players(status);

            CREATE INDEX IF NOT EXISTS idx_players_created
            ON players(created_at);
        ";

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

        await connection.ExecuteAsync(createPlayersTable);
        await connection.ExecuteAsync(createSeenPlayersTable);

        _logger.LogInformation("Database tables initialized successfully");
    }

    // --- Players (enriched) ---
    private string BasePlayerSelectQuery =>
        @"SELECT id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
            item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
            battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
            guild_history AS GuildHistory, raiderio_summary AS RaiderIoSummary,
            warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
            status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
            created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM players";

    public async Task<List<PlayerResponse>> GetPlayersAsync(
        PlayerStatus? status = null,
        string? playerClass = null,
        int limit = 50,
        int offset = 0
    )
    {
        using var connection = CreateConnection();

        var sql = BasePlayerSelectQuery;

        if (status.HasValue || !string.IsNullOrEmpty(playerClass))
        {
            sql += " WHERE";
            var conditions = new List<string>();
            if (status.HasValue)
                conditions.Add(" status = @Status");
            if (!string.IsNullOrEmpty(playerClass))
                conditions.Add(" LOWER(class) = LOWER(@PlayerClass)");
            sql += string.Join(" AND", conditions);
        }

        /**where**/
        sql += " ORDER BY created_at DESC LIMIT @Limit OFFSET @Offset";

        var players = await connection.QueryAsync<PlayerResponse>(
            sql,
            new
            {
                Status = (int?)status,
                PlayerClass = playerClass,
                Limit = limit,
                Offset = offset,
            }
        );

        return players.ToList();
    }

    public async Task<PlayerResponse?> GetPlayerByIdAsync(int id)
    {
        using var connection = CreateConnection();

        var sql = $"{BasePlayerSelectQuery} WHERE id = @Id";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(sql, new { Id = id });
    }

    public async Task<PlayerResponse> UpsertPlayerAsync(CreatePlayerRequest request)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            INSERT INTO players (character_name, class, realm, realm_slug, item_level, last_updated,
                character_url, battletag, bio, languages, specs_playing, guild_history,
                raiderio_summary, warcraftlogs_summary, status,
                discord_message_id, discord_channel_id, created_at, updated_at)
            VALUES (@CharacterName, @Class, @Realm, @RealmSlug, @ItemLevel, @LastUpdated,
                @CharacterUrl, @BattleTag, @Bio, @Languages, @SpecsPlaying, @GuildHistory,
                @RaiderIoSummary, @WarcraftLogsSummary, @Status,
                @DiscordMessageId, @DiscordChannelId, @Now, @Now)
            ON CONFLICT (character_name, realm)
            DO UPDATE SET class = @Class, realm_slug = @RealmSlug, item_level = @ItemLevel,
                last_updated = @LastUpdated, character_url = @CharacterUrl, battletag = @BattleTag,
                bio = @Bio, languages = @Languages, specs_playing = @SpecsPlaying,
                guild_history = @GuildHistory, raiderio_summary = @RaiderIoSummary,
                warcraftlogs_summary = @WarcraftLogsSummary,
                discord_message_id = @DiscordMessageId, discord_channel_id = @DiscordChannelId,
                updated_at = @Now
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                created_at AS CreatedAt, updated_at AS UpdatedAt";

        return await connection.QuerySingleAsync<PlayerResponse>(
            sql,
            new
            {
                request.CharacterName,
                request.Class,
                request.Realm,
                request.RealmSlug,
                request.ItemLevel,
                request.LastUpdated,
                request.CharacterUrl,
                request.BattleTag,
                request.Bio,
                request.Languages,
                request.SpecsPlaying,
                GuildHistory = request.GuildHistory.ToArray(),
                request.RaiderIoSummary,
                request.WarcraftLogsSummary,
                Status = (int)PlayerStatus.New,
                DiscordMessageId = (long?)request.DiscordMessageId,
                DiscordChannelId = (long?)request.DiscordChannelId,
                Now = DateTime.UtcNow,
            }
        );
    }

    public async Task<PlayerResponse?> UpdatePlayerStatusAsync(int id, PlayerStatus status)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            UPDATE players SET status = @Status, updated_at = @Now WHERE id = @Id
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                created_at AS CreatedAt, updated_at AS UpdatedAt";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(
            sql,
            new
            {
                Id = id,
                Status = (int)status,
                Now = DateTime.UtcNow,
            }
        );
    }

    public async Task<PlayerResponse?> UpdateGeminiTakeAsync(int id, string geminiTake)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            UPDATE players SET gemini_take = @GeminiTake, updated_at = @Now WHERE id = @Id
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                created_at AS CreatedAt, updated_at AS UpdatedAt";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(
            sql,
            new
            {
                Id = id,
                GeminiTake = geminiTake,
                Now = DateTime.UtcNow,
            }
        );
    }

    // --- Seen Players ---

    public async Task<DateTime?> GetLastSeenAtAsync(string characterName, string realm)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            SELECT last_seen_at
            FROM seen_players
            WHERE LOWER(character_name) = LOWER(@CharacterName)
            AND LOWER(realm) = LOWER(@Realm)";

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(
            sql,
            new { CharacterName = characterName, Realm = realm }
        );
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

    public async Task<int> GetSeenPlayersCountAsync()
    {
        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM seen_players");
    }

    public async Task CleanupOldSeenPlayersAsync(int daysToKeep = 30)
    {
        using var connection = CreateConnection();

        var sql = @"DELETE FROM seen_players WHERE last_seen_at < @CutoffDate";
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
