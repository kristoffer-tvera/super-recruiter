using System.Net;
using System.Net.Http.Json;
using SuperRecruiter.Shared.Helpers;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// https://raider.io/api#/character/getApiV1CharactersProfile
/// </summary>
public class RaiderIOService(ILogger<RaiderIOService> logger, HttpClient httpClient, IConfiguration configuration)
{
    private const string BaseUrl = "https://raider.io/api";
    private static readonly string[] TierSlugs = ["the-venomous-abyss", "tier-mn-1", "manaforge-omega", "liberation-of-undermine", "nerubar-palace"];
    private static readonly IReadOnlyDictionary<int, string> LanguageNames = new Dictionary<int, string>
    {
        [1] = "English",
        [2] = "German",
        [3] = "Spanish",
        [4] = "French",
        [5] = "Italian",
        [7] = "Russian",
        [88] = "Norwegian",
        [96] = "Polish",
        [114] = "Swedish",
        [120] = "Turkish",
    };

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

            var normalizedRealm = RealmSlugHelper.ToSlug(realm);
            var url = BuildUrl(
                "/v1/characters/profile",
                new Dictionary<string, string>
                {
                    ["access_key"] = apiKey,
                    ["region"] = region,
                    ["realm"] = normalizedRealm,
                    ["name"] = characterName,
                    ["fields"] = $"gear,raid_progression:current-expansion,raid_achievement_curve:{string.Join(':', TierSlugs)}",
                }
            );

            logger.LogDebug("Fetching RaiderIO profile for {Character} on {Realm} ({Region})", characterName, normalizedRealm, region);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("RaiderIO profile not found for {Character} on {Realm}", characterName, normalizedRealm);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("RaiderIO profile request failed for {Character} on {Realm}. Status: {Status}", characterName, normalizedRealm, response.StatusCode);
                return null;
            }

            var profile = await response.Content.ReadFromJsonAsync<RaiderIOProfile>(cancellationToken);
            if (profile == null)
            {
                logger.LogWarning("RaiderIO returned an empty profile for {Character} on {Realm}", characterName, normalizedRealm);
                return null;
            }

            NormalizeProfile(profile);
            logger.LogInformation("Successfully fetched raid progression for {Character}: {Summary}", characterName, profile.Raid_progression_summary);

            return profile;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching RaiderIO profile for {Character} on {Realm}", characterName, realm);
            return null;
        }
    }

    public async Task<List<Player>> GetLfgPlayers(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = BuildUrl(
                "/search-advanced",
                new Dictionary<string, string>
                {
                    ["type"] = "character",
                    ["region[0][eq]"] = "eu",
                    ["timezone"] = "UTC",
                    ["sort[recruitment.guild_raids.profile.published_at]"] = "desc",
                    ["limit"] = "40",
                    ["offset"] = "0",
                }
            );

            logger.LogInformation("Fetching RaiderIO LFG matches");

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch RaiderIO LFG matches. Status: {Status}", response.StatusCode);
                return [];
            }

            var lfgResponse = await response.Content.ReadFromJsonAsync<RaiderIOLfg>(cancellationToken);
            var players = new List<Player>();

            foreach (var match in lfgResponse?.Matches ?? [])
            {
                var player = MapPlayer(match);
                if (player == null)
                {
                    logger.LogWarning("Skipping malformed RaiderIO LFG match for {Character}", match.Name ?? "unknown");
                    continue;
                }

                players.Add(player);
            }

            logger.LogInformation("Successfully parsed {Count} players from RaiderIO", players.Count);
            return players;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching RaiderIO LFG matches");
            return [];
        }
    }

    private static Player? MapPlayer(LfgMatch match)
    {
        if (
            string.IsNullOrWhiteSpace(match.Name)
            || match.Data is not { } data
            || data.Realm is not { } realm
            || string.IsNullOrWhiteSpace(realm.Name)
            || string.IsNullOrWhiteSpace(realm.Slug)
            || data.Class is not { } playerClass
            || string.IsNullOrWhiteSpace(playerClass.Name)
        )
            return null;

        return new Player
        {
            CharacterName = match.Name,
            Realm = realm.Name,
            Class = playerClass.Name,
            SpecsPlaying = data.Spec?.Name,
            CharacterUrl = $"https://www.wowprogress.com/character/eu/{realm.Slug}/{match.Name}",
            Bio = data.Recruitment?.GuildRaids?.Profile?.Caption ?? string.Empty,
            ItemLevel = data.ItemLevelEquipped,
            LastUpdated = data.Recruitment?.GuildRaids?.Profile?.ThrottledPublishedAt ?? DateTime.UtcNow,
            Languages = GetLanguages(data.Recruitment),
            Source = LfgSource.RaiderIO,
        };
    }

    private static string GetLanguages(LfgRecruitment? recruitment)
    {
        var languageCodes = recruitment?.GuildRaids?.Profile?.Languages;
        if (languageCodes == null || languageCodes.Count == 0)
            return "N/A";

        var names = languageCodes.Where(LanguageNames.ContainsKey).Select(code => LanguageNames[code]);
        var result = string.Join(", ", names);

        return string.IsNullOrEmpty(result) ? "N/A" : result;
    }

    private static void NormalizeProfile(RaiderIOProfile profile)
    {
        if (profile.Raid_achievement_curve != null)
        {
            foreach (var tier in profile.Raid_achievement_curve)
                tier.Raid = GetNameFromKebabCase(tier.Raid);
        }

        profile.Raid_progression_summary = GetRaidProgressionSummary(profile);
    }

    private static List<string> GetRaidProgressionSummary(RaiderIOProfile profile)
    {
        if (profile.Raid_progression == null || profile.Raid_progression.Count == 0)
            return ["No raid data"];

        var summaries = new List<string>();
        foreach (var tier in profile.Raid_progression)
        {
            if (string.IsNullOrEmpty(tier.Value.Summary))
                continue;

            var tierName = GetNameFromKebabCase(tier.Key);
            var tierProgress = tier.Value.Summary;

            summaries.Add($"**{tierName}** | {tierProgress}");
        }

        return summaries.Count > 0 ? summaries : ["No raid data"];
    }

    private static string BuildUrl(string path, IReadOnlyDictionary<string, string> parameters)
    {
        var query = string.Join('&', parameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"));
        return $"{BaseUrl}{path}?{query}";
    }

    private static string GetNameFromKebabCase(string kebabCase)
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
