using System.Text.Json;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// https://raider.io/api#/character/getApiV1CharactersProfile
/// </summary>
public class RaiderIOService(ILogger<RaiderIOService> logger, HttpClient httpClient, IConfiguration configuration)
{
    private const string BaseUrl = "https://raider.io/api";

    public async Task<RaiderIOProfile?> GetCharacterProfileAsync(string region, string realm, string characterName, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = configuration["RaiderIO:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("RaiderIO API key not configured");
                return null;
            }

            var normalizedRealm = realm.ToLowerInvariant().Replace(" ", "-");

            var tierSlugs = new[] { "tier-mn-1", "manaforge-omega", "liberation-of-undermine", "nerubar-palace" };

            var queryStringParameters = new Dictionary<string, string>
            {
                { "access_key", apiKey },
                { "region", region },
                { "realm", normalizedRealm },
                { "name", characterName },
                { "fields", $"raid_progression:current-expansion,raid_achievement_curve:{string.Join(':', tierSlugs)}" },
            };

            var url = $"{BaseUrl}/v1/characters/profile?{string.Join('&', queryStringParameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";

            logger.LogDebug("Fetching RaiderIO profile for {Character} on {Realm} ({Region})", characterName, normalizedRealm, region);

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch RaiderIO profile for {Character}. Status: {Status}", characterName, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var profile = JsonSerializer.Deserialize<RaiderIOProfile>(json, options);

            if (profile != null)
            {
                logger.LogInformation("Successfully fetched raid progression for {Character}: {Summary}", characterName, GetRaidProgressionSummary(profile));
            }

            if (profile?.Raid_achievement_curve != null)
                for (int i = 0; i < profile.Raid_achievement_curve?.Count; i++)
                {
                    var raidSlug = profile.Raid_achievement_curve.ElementAt(i).Raid;
                    var raidName = GetNameFromKebabCase(raidSlug);
                    profile.Raid_achievement_curve.ElementAt(i).Raid = raidName;
                }

            profile?.Raid_progression_summary = GetRaidProgressionSummary(profile);

            return profile;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching RaiderIO profile for {Character} on {Realm}", characterName, realm);
            return null;
        }
    }

    public async Task<List<Player>> GetLfgPlayers(CancellationToken cancellationToken = default)
    {
        var queryStringParameters = new Dictionary<string, string>
        {
            { "type", "character" },
            { "region[0][eq]", "eu" },
            { "timezone", "UTC" },
            { "sort[recruitment.guild_raids.profile.published_at]", "desc" },
            { "limit", "40" },
            { "offset", "0" },
        };

        var url = $"{BaseUrl}/search-advanced?{string.Join('&', queryStringParameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";

        logger.LogInformation("Fetching RaiderIO LFG matches with URL: {Url}", url);

        var response = await httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to fetch RaiderIO LFG matches. Status: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var lfgResponse = JsonSerializer.Deserialize<RaiderIOLfg>(json, options);
        var players = new List<Player>();

        foreach (var match in lfgResponse?.Matches ?? Enumerable.Empty<LfgMatch>())
        {
            var player = new Player
            {
                CharacterName = match.Name,
                Realm = match.Data.Realm.Name,
                Class = match.Data.Class.Name,
                SpecsPlaying = match.Data.Spec?.Name,
                CharacterUrl = $"https://www.wowprogress.com/character/eu/{match.Data.Realm.Slug}/{match.Name}",
                Bio = match.Data.Recruitment?.GuildRaids?.Profile?.Caption ?? string.Empty,
                ItemLevel = match.Data.ItemLevelEquipped,
                LastUpdated = match.Data.Recruitment?.GuildRaids?.Profile?.ThrottledPublishedAt ?? DateTime.UtcNow,
                Languages = GetLanguages(match.Data.Recruitment),
                Source = LfgSource.RaiderIO,
            };

            players.Add(player);
        }

        logger.LogInformation("Successfully parsed {Count} players from RaiderIo", players.Count);

        return players;
    }

    private string? GetLanguages(LfgRecruitment? data)
    {
        var languageCodes = data?.GuildRaids?.Profile?.Languages;
        if (languageCodes == null)
        {
            return "N/A";
        }

        var languageNames = languageCodes
            ?.Select(code =>
                code switch
                {
                    1 => "English",
                    2 => "German",
                    3 => "Spanish",
                    4 => "French",
                    5 => "Italian",
                    7 => "Russian",
                    88 => "Norwegian",
                    96 => "Polish",
                    114 => "Swedish",
                    120 => "Turkish",
                    _ => null,
                }
            )
            .Where(name => name != null);
        return languageNames != null ? string.Join(", ", languageNames) : null;
    }

    private List<string> GetRaidProgressionSummary(RaiderIOProfile profile)
    {
        if (profile.Raid_progression == null || !profile.Raid_progression.Any())
            return new List<string> { "No raid data" };

        var summaries = new List<string>();
        foreach (var tier in profile.Raid_progression)
        {
            if (string.IsNullOrEmpty(tier.Value.Summary))
                continue;

            var tierName = GetNameFromKebabCase(tier.Key);
            var tierProgress = tier.Value.Summary;

            summaries.Add($"**{tierName}** | {tierProgress}");
        }

        return summaries;
    }

    private string GetNameFromKebabCase(string kebabCase)
    {
        if (string.IsNullOrWhiteSpace(kebabCase))
            return string.Empty;

        var parts = kebabCase.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].Length > 1 ? char.ToUpper(parts[i][0]) + parts[i][1..].ToLower() : parts[i].ToUpper();
        }
        return string.Join(' ', parts);
    }
}
