using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

/// <summary>
/// https://raider.io/api#/character/getApiV1CharactersProfile
/// </summary>
/// <param name="logger"></param>
/// <param name="httpClient"></param>
/// <param name="configuration"></param>
public class RaiderIOService(
    ILogger<RaiderIOService> logger,
    HttpClient httpClient,
    IConfiguration configuration
)
{
    private const string BaseUrl = "https://raider.io/api";

    public async Task<RaiderIOProfile?> GetCharacterProfileAsync(
        string region,
        string realm,
        string characterName,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var apiKey = configuration["RaiderIO:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("RaiderIO API key not configured");
                return null;
            }

            // Normalize realm name (replace spaces with hyphens, lowercase)
            var normalizedRealm = realm.ToLowerInvariant().Replace(" ", "-");

            var tierSlugs = new[]
            {
                "manaforge-omega",
                "liberation-of-undermine",
                "nerubar-palace",
                "amirdrassil-the-dreams-hope",
                "aberrus-the-shadowed-crucible",
                "vault-of-the-incarnates",
            };

            var queryStringParameters = new Dictionary<string, string>
            {
                { "access_key", apiKey },
                { "region", region },
                { "realm", normalizedRealm },
                { "name", characterName },
                {
                    "fields",
                    $"raid_progression:current-expansion,raid_achievement_curve:{string.Join(':', tierSlugs)}"
                },
            };

            var url =
                $"{BaseUrl}/v1/characters/profile?{string.Join('&', queryStringParameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";

            logger.LogDebug(
                "Fetching RaiderIO profile for {Character} on {Realm} ({Region})",
                characterName,
                normalizedRealm,
                region
            );

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Failed to fetch RaiderIO profile for {Character}. Status: {Status}",
                    characterName,
                    response.StatusCode
                );
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var profile = JsonSerializer.Deserialize<RaiderIOProfile>(json, options);

            if (profile != null)
            {
                logger.LogInformation(
                    "Successfully fetched raid progression for {Character}: {Summary}",
                    characterName,
                    GetRaidProgressionSummary(profile)
                );
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
            logger.LogError(
                ex,
                "Error fetching RaiderIO profile for {Character} on {Realm}",
                characterName,
                realm
            );
            return null;
        }
    }

    public async Task<List<Player>?> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var apiKey = configuration["RaiderIO:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("RaiderIO API key not configured");
                return null;
            }

            var queryStringParameters = new Dictionary<string, string>
            {
                { "type", "character" },
                { "recruitment.guild_raids.schedule[0][eq][day]", 4.ToString() }, // Thursday
                { "recruitment.guild_raids.schedule[0][eq][startTime]", "1200" }, // 20:00
                { "recruitment.guild_raids.schedule[0][eq][endTime]", "1410" }, // 23:30
                { "recruitment.guild_raids.schedule[0][eq][relation]", "intersects" },
                { "recruitment.guild_raids.schedule[1][eq][day]", 6.ToString() }, // Saturday
                { "recruitment.guild_raids.schedule[1][eq][startTime]", "1200" }, // 20:00
                { "recruitment.guild_raids.schedule[1][eq][endTime]", "1410" }, // 23:30
                { "recruitment.guild_raids.schedule[1][eq][relation]", "intersects" },
                { "recruitment.guild_raids.schedule[2][eq][day]", 1.ToString() }, // Monday
                { "recruitment.guild_raids.schedule[2][eq][startTime]", "1200" }, // 20:00
                { "recruitment.guild_raids.schedule[2][eq][endTime]", "1410" }, // 23:30
                { "recruitment.guild_raids.schedule[2][eq][relation]", "intersects" },
                { "region[0][eq]", "eu" },
                { "timezone", "UTC" },
                { "sort[recruitment.guild_raids.profile.published_at]", "desc" },
                { "limit", "3" },
                { "offset", "0" },
            };

            var url =
                $"{BaseUrl}/search-advanced?{string.Join('&', queryStringParameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";

            logger.LogDebug("Fetching RaiderIO recruitment feed with URL: {Url}", url);

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Failed to fetch RaiderIO recruitment feed. Status: {Status}",
                    response.StatusCode
                );
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var feed = JsonSerializer.Deserialize<RaiderIoRecruitmentFeed>(json, options);
            var players = new List<Player>();

            foreach (var match in feed?.Matches ?? [])
            {
                var player = new Player
                {
                    CharacterName = match.Name ?? "Unknown",
                    Realm = match?.Data?.Realm?.Name ?? "Unknown",

                    Class = match?.Data?.Class?.Name ?? "Unknown",
                    ItemLevel = match?.Data?.ItemLevelEquipped ?? 0.0,
                    LastUpdated =
                        match?.Data?.Recruitment?.GuildRaids?.Profile?.PublishedAt
                        ?? DateTime.MinValue,
                    CharacterUrl =
                        $"https://www.wowprogress.com/character/eu/{match?.Data?.Realm?.Slug ?? "unknown"}/{match?.Name}",
                    Bio =
                        "Limited info available from RaiderIO feed. Check their profile for more details.",
                };
            }

            return players;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching RaiderIO recruitment feed");
            return null;
        }
    }

    private List<string> GetRaidProgressionSummary(RaiderIOProfile profile)
    {
        if (profile.Raid_progression == null || !profile.Raid_progression.Any())
            return new List<string> { "No raid data" };

        // Loop all tiers and add them to list:
        var summaries = new List<string>();
        foreach (var tier in profile.Raid_progression)
        {
            if (string.IsNullOrEmpty(tier.Value.Summary))
                continue; // skip empty progress

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
            parts[i] =
                parts[i].Length > 1
                    ? char.ToUpper(parts[i][0]) + parts[i][1..].ToLower()
                    : parts[i].ToUpper();
        }
        return string.Join(' ', parts);
    }
}
