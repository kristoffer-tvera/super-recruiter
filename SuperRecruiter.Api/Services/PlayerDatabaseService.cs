using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public partial class PlayerDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<PlayerDatabaseService> _logger;

    public PlayerDatabaseService(IConfiguration configuration, ILogger<PlayerDatabaseService> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL") ?? throw new InvalidOperationException("PostgreSQL connection string not found in configuration");
        _logger = logger;
    }

    private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task<bool> IsReadyAsync()
    {
        using var connection = (NpgsqlConnection)CreateConnection();
        await connection.OpenAsync();

        const string sql = """
            SELECT NOT EXISTS (
                SELECT 1
                FROM (VALUES
                    ('players', 'id'),
                    ('players', 'character_name'),
                    ('players', 'class'),
                    ('players', 'realm'),
                    ('players', 'realm_slug'),
                    ('players', 'item_level'),
                    ('players', 'last_updated'),
                    ('players', 'character_url'),
                    ('players', 'battletag'),
                    ('players', 'bio'),
                    ('players', 'languages'),
                    ('players', 'specs_playing'),
                    ('players', 'guild_history'),
                    ('players', 'sources'),
                    ('players', 'raiderio_summary'),
                    ('players', 'warcraftlogs_summary'),
                    ('players', 'gemini_take'),
                    ('players', 'status'),
                    ('players', 'discord_message_id'),
                    ('players', 'discord_channel_id'),
                    ('players', 'current_tier_mythic_kill_count'),
                    ('players', 'discovery_progression'),
                    ('players', 'raiderio_data'),
                    ('players', 'warcraftlogs_data'),
                    ('players', 'wowprogress_data'),
                    ('players', 'enrichment_attempted_at'),
                    ('players', 'enriched_at'),
                    ('players', 'gemini_generated_at'),
                    ('players', 'created_at'),
                    ('players', 'updated_at'),
                    ('seen_players', 'id'),
                    ('seen_players', 'character_name'),
                    ('seen_players', 'realm'),
                    ('seen_players', 'first_seen_at'),
                    ('seen_players', 'last_seen_at'),
                    ('config', 'key'),
                    ('config', 'value')
                ) AS expected(table_name, column_name)
                LEFT JOIN information_schema.columns AS actual
                    ON actual.table_schema = 'public'
                    AND actual.table_name = expected.table_name
                    AND actual.column_name = expected.column_name
                WHERE actual.column_name IS NULL
            )
            """;
        return await connection.ExecuteScalarAsync<bool>(sql);
    }

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
                sources INTEGER[],
                raiderio_summary TEXT,
                warcraftlogs_summary TEXT,
                gemini_take TEXT,
                status INTEGER NOT NULL DEFAULT 0,
                discord_message_id BIGINT,
                discord_channel_id BIGINT,
                current_tier_mythic_kill_count INTEGER NOT NULL DEFAULT 0,
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

        var createConfigTable =
            @"
            CREATE TABLE IF NOT EXISTS config (
                key VARCHAR(100) NOT NULL,
                value VARCHAR(255) NOT NULL,
                CONSTRAINT unique_config_entry UNIQUE (key, value)
            );
        ";

        await connection.ExecuteAsync(createPlayersTable);
        await connection.ExecuteAsync(createSeenPlayersTable);
        await connection.ExecuteAsync(createConfigTable);
        await connection.ExecuteAsync(
            """
            ALTER TABLE players ADD COLUMN IF NOT EXISTS discovery_progression JSONB;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS sources INTEGER[];
            ALTER TABLE players ADD COLUMN IF NOT EXISTS raiderio_data JSONB;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS warcraftlogs_data JSONB;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS wowprogress_data JSONB;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS enrichment_attempted_at TIMESTAMPTZ;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS enriched_at TIMESTAMPTZ;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS gemini_generated_at TIMESTAMPTZ;
            """
        );

        _logger.LogInformation("Database tables initialized successfully");
    }

    // --- Players (enriched) ---
    private string BasePlayerSelectQuery =>
        @"SELECT id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
            item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
            battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
            guild_history AS GuildHistory, COALESCE(sources, ARRAY[]::INTEGER[]) AS Sources, raiderio_summary AS RaiderIoSummary,
            warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
            status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
            current_tier_mythic_kill_count AS CurrentTierMythicKillCount,
            enriched_at AS EnrichedAt, gemini_generated_at AS GeminiGeneratedAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM players";

    public async Task<List<PlayerResponse>> GetPlayersAsync(
        List<PlayerStatus>? statuses = null,
        List<string>? playerClasses = null,
        double? minItemLevel = null,
        int? minMythicKills = null,
        int limit = 50,
        int offset = 0
    )
    {
        using var connection = CreateConnection();

        var sql = BasePlayerSelectQuery;
        var conditions = new List<string>();

        if (statuses != null && statuses.Count > 0)
        {
            var statusValues = string.Join(",", statuses.Select(s => (int)s));
            conditions.Add($" status IN ({statusValues})");
        }

        if (playerClasses != null && playerClasses.Count > 0)
        {
            var classPlaceholders = string.Join(",", playerClasses.Select((_, i) => $"@Class{i}"));
            conditions.Add($" LOWER(class) IN ({classPlaceholders})");
        }

        if (minItemLevel.HasValue)
        {
            conditions.Add(" item_level >= @MinItemLevel");
        }

        if (minMythicKills.HasValue)
        {
            conditions.Add(" current_tier_mythic_kill_count >= @MinMythicKills");
        }

        if (conditions.Count > 0)
        {
            sql += " WHERE" + string.Join(" AND", conditions);
        }

        sql += " ORDER BY updated_at DESC LIMIT @Limit OFFSET @Offset";

        var parameters = new DynamicParameters();
        parameters.Add("@MinItemLevel", minItemLevel);
        parameters.Add("@MinMythicKills", minMythicKills);
        parameters.Add("@Limit", limit);
        parameters.Add("@Offset", offset);

        if (playerClasses != null)
        {
            for (int i = 0; i < playerClasses.Count; i++)
            {
                parameters.Add($"@Class{i}", playerClasses[i].ToLower());
            }
        }

        var players = await connection.QueryAsync<PlayerResponse>(sql, parameters);
        return players.ToList();
    }

    public async Task<PlayerResponse?> GetPlayerByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        var sql = $"{BasePlayerSelectQuery} WHERE id = @Id";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<PlayerResponse> UpsertPlayerAsync(CreatePlayerRequest request)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            INSERT INTO players (character_name, class, realm, realm_slug, item_level, last_updated,
                character_url, battletag, bio, languages, specs_playing, guild_history, sources,
                raiderio_summary, warcraftlogs_summary, status,
                discord_message_id, discord_channel_id, current_tier_mythic_kill_count, discovery_progression, created_at, updated_at)
            VALUES (@CharacterName, @Class, @Realm, @RealmSlug, @ItemLevel, @LastUpdated,
                @CharacterUrl, @BattleTag, @Bio, @Languages, @SpecsPlaying, @GuildHistory, @Sources,
                @RaiderIoSummary, @WarcraftLogsSummary, @Status,
                @DiscordMessageId, @DiscordChannelId, @CurrentTierMythicKillCount, CAST(@DiscoveryProgression AS jsonb), @Now, @Now)
            ON CONFLICT (character_name, realm)
            DO UPDATE SET class = @Class, realm_slug = @RealmSlug, item_level = @ItemLevel,
                last_updated = @LastUpdated, character_url = COALESCE(NULLIF(@CharacterUrl, ''), players.character_url),
                battletag = COALESCE(NULLIF(@BattleTag, ''), players.battletag),
                bio = COALESCE(NULLIF(@Bio, ''), players.bio),
                languages = COALESCE(NULLIF(NULLIF(@Languages, ''), 'N/A'), players.languages),
                specs_playing = COALESCE(NULLIF(@SpecsPlaying, ''), players.specs_playing),
                guild_history = CASE WHEN cardinality(@GuildHistory) > 0 THEN @GuildHistory ELSE players.guild_history END,
                sources = CASE WHEN cardinality(@Sources) > 0 THEN ARRAY(
                    SELECT DISTINCT source_value
                    FROM unnest(COALESCE(players.sources, ARRAY[]::INTEGER[]) || @Sources) AS discovered(source_value)
                    ORDER BY source_value
                ) ELSE players.sources END,
                discord_message_id = COALESCE(@DiscordMessageId, players.discord_message_id),
                discord_channel_id = COALESCE(@DiscordChannelId, players.discord_channel_id),
                discovery_progression = COALESCE(CAST(@DiscoveryProgression AS jsonb), players.discovery_progression),
                current_tier_mythic_kill_count = CASE WHEN @DiscoveryProgression IS NOT NULL
                    THEN @CurrentTierMythicKillCount ELSE players.current_tier_mythic_kill_count END,
                updated_at = @Now
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, COALESCE(sources, ARRAY[]::INTEGER[]) AS Sources, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                current_tier_mythic_kill_count AS CurrentTierMythicKillCount,
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
                request.CurrentTierMythicKillCount,
                Sources = request.Sources.Where(source => Enum.IsDefined(typeof(LfgSource), source)).Select(source => (int)source).Distinct().ToArray(),
                DiscoveryProgression = request.DiscoveryProgression is null ? null : JsonSerializer.Serialize(request.DiscoveryProgression, SnapshotJsonOptions),
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
                guild_history AS GuildHistory, COALESCE(sources, ARRAY[]::INTEGER[]) AS Sources, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                current_tier_mythic_kill_count AS CurrentTierMythicKillCount,
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
            UPDATE players SET gemini_take = @GeminiTake, gemini_generated_at = @Now, updated_at = @Now WHERE id = @Id
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, COALESCE(sources, ARRAY[]::INTEGER[]) AS Sources, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                current_tier_mythic_kill_count AS CurrentTierMythicKillCount,
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

    public async Task<PlayerResponse?> GetPlayerByCharacterAndRealmAsync(string characterName, string realmSlug)
    {
        using var connection = CreateConnection();

        var sql =
            $@"{BasePlayerSelectQuery}
            WHERE LOWER(character_name) = LOWER(@CharacterName)
            AND LOWER(realm_slug) = LOWER(@RealmSlug)";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(sql, new { CharacterName = characterName, RealmSlug = realmSlug });
    }

    public async Task<PlayerResponse?> UpdatePlayerStatusByCharacterAndRealmAsync(string characterName, string realmSlug, PlayerStatus status)
    {
        using var connection = CreateConnection();

        var sql =
            @"
            UPDATE players
            SET status = @Status, updated_at = @Now
            WHERE LOWER(character_name) = LOWER(@CharacterName)
            AND LOWER(realm_slug) = LOWER(@RealmSlug)
            RETURNING id, character_name AS CharacterName, class, realm, realm_slug AS RealmSlug,
                item_level AS ItemLevel, last_updated AS LastUpdated, character_url AS CharacterUrl,
                battletag AS BattleTag, bio, languages, specs_playing AS SpecsPlaying,
                guild_history AS GuildHistory, COALESCE(sources, ARRAY[]::INTEGER[]) AS Sources, raiderio_summary AS RaiderIoSummary,
                warcraftlogs_summary AS WarcraftLogsSummary, gemini_take AS GeminiTake,
                status, discord_message_id AS DiscordMessageId, discord_channel_id AS DiscordChannelId,
                current_tier_mythic_kill_count AS CurrentTierMythicKillCount,
                created_at AS CreatedAt, updated_at AS UpdatedAt";

        return await connection.QueryFirstOrDefaultAsync<PlayerResponse>(
            sql,
            new
            {
                CharacterName = characterName,
                RealmSlug = realmSlug,
                Status = (int)status,
                Now = DateTime.UtcNow,
            }
        );
    }

    // --- Seen Players ---

    public async Task<List<SeenPlayerResponse>> GetAllSeenPlayersAsync()
    {
        using var connection = CreateConnection();

        var sql =
            @"SELECT character_name AS CharacterName, realm, last_seen_at AS LastSeenAt
              FROM seen_players";

        var players = await connection.QueryAsync<SeenPlayerResponse>(sql);
        return players.ToList();
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

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new { CharacterName = characterName, Realm = realm });
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
                LastUpdated = DateTime.SpecifyKind(lastUpdated, DateTimeKind.Unspecified),
            }
        );

        _logger.LogDebug("Added/updated seen player: {Character}-{Realm} (LastUpdated: {LastUpdated})", characterName, realm, lastUpdated);
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
            _logger.LogInformation("Cleaned up {Count} old seen player records (older than {Days} days)", deletedCount, daysToKeep);
        }
    }

    /// <summary>
    /// Bulk insert/update seen players in a single database operation.
    /// Reduces HTTP calls and database round-trips significantly.
    /// </summary>
    public async Task BulkAddSeenPlayersAsync(List<(string CharacterName, string Realm, DateTime LastUpdated)> seenPlayers)
    {
        if (seenPlayers.Count == 0)
            return;

        using var connection = CreateConnection();

        // Use UNNEST to insert multiple rows efficiently
        var sql =
            @"
            INSERT INTO seen_players (character_name, realm, first_seen_at, last_seen_at)
            SELECT character_name, realm, MIN(last_updated), MAX(last_updated)
            FROM UNNEST(@CharacterNames, @Realms, @LastUpdateds)
                AS t(character_name, realm, last_updated)
            GROUP BY character_name, realm
            ON CONFLICT (character_name, realm)
            DO UPDATE SET last_seen_at = GREATEST(seen_players.last_seen_at, EXCLUDED.last_seen_at)";

        await connection.ExecuteAsync(
            sql,
            new
            {
                CharacterNames = seenPlayers.Select(x => x.CharacterName).ToArray(),
                Realms = seenPlayers.Select(x => x.Realm).ToArray(),
                LastUpdateds = seenPlayers.Select(x => DateTime.SpecifyKind(x.LastUpdated, DateTimeKind.Unspecified)).ToArray(),
            }
        );

        _logger.LogInformation("Bulk added/updated {Count} seen players", seenPlayers.Count);
    }

    /// <summary>
    /// Get lightweight player data for cache synchronization.
    /// Returns only essential fields to minimize data transfer.
    /// </summary>
    public async Task<List<PlayerCacheResponse>> GetPlayersCacheAsync()
    {
        using var connection = CreateConnection();

        var sql =
            @"SELECT id, character_name AS CharacterName, realm, status, updated_at AS UpdatedAt
              FROM players";

        var players = await connection.QueryAsync<PlayerCacheResponse>(sql);
        return players.ToList();
    }

    // --- Admin Config ---

    private record ConfigEntry(string Key, string Value);

    public async Task<AdminConfigResponse> GetAdminConfigAsync()
    {
        using var connection = CreateConnection();
        var rows = await connection.QueryAsync<ConfigEntry>("SELECT key, value FROM config");

        var response = new AdminConfigResponse();
        foreach (var row in rows)
        {
            if (row.Key == "bosskills" && int.TryParse(row.Value, out var kills))
                response.BossKills = kills;
            else if (row.Key == "acceptedclass")
                response.AcceptedClasses.Add(row.Value);
        }
        return response;
    }

    public async Task<AdminConfigResponse> UpdateAdminConfigAsync(UpdateAdminConfigRequest request)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync("DELETE FROM config WHERE key = 'bosskills'", transaction: transaction);
        await connection.ExecuteAsync("INSERT INTO config (key, value) VALUES ('bosskills', @Value)", new { Value = request.BossKills.ToString() }, transaction: transaction);

        await connection.ExecuteAsync("DELETE FROM config WHERE key = 'acceptedclass'", transaction: transaction);

        foreach (var cls in request.AcceptedClasses)
        {
            await connection.ExecuteAsync("INSERT INTO config (key, value) VALUES ('acceptedclass', @Value)", new { Value = cls.ToLower() }, transaction: transaction);
        }

        transaction.Commit();

        return new AdminConfigResponse { BossKills = request.BossKills, AcceptedClasses = [.. request.AcceptedClasses.Select(c => c.ToLower())] };
    }
}
