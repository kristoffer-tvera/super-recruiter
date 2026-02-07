using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IRaiderIOService
{
    Task<RaiderIOProfile?> GetCharacterProfileAsync(
        string region,
        string realm,
        string characterName,
        CancellationToken cancellationToken = default
    );
}

public class RaiderIOService(
    ILogger<RaiderIOService> logger,
    HttpClient httpClient,
    IConfiguration configuration
) : IRaiderIOService
{
    private const string BaseUrl = "https://raider.io/api/v1/characters/profile";

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
                $"{BaseUrl}?{string.Join('&', queryStringParameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";

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

    private List<string> GetRaidProgressionSummary(RaiderIOProfile profile)
    {
        if (profile.Raid_progression == null || !profile.Raid_progression.Any())
            return new List<string> { "No raid data" };

        // Loop all tiers and add them to list:
        var summaries = new List<string>();
        foreach (var tier in profile.Raid_progression)
        {
            var tierName = GetNameFromKebabCase(tier.Key);
            // Tier progress can be empty, so handle that case
            var tierProgress = string.IsNullOrEmpty(tier.Value.Summary)
                ? "No progress"
                : tier.Value.Summary;

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
