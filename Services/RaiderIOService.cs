using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IRaiderIOService
{
    Task<(RaiderIOProfile?, List<string>)> GetCharacterProfileAsync(
        string region,
        string realm,
        string characterName,
        CancellationToken cancellationToken = default
    );
}

public class RaiderIOService : IRaiderIOService
{
    private readonly ILogger<RaiderIOService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private const string BaseUrl = "https://raider.io/api/v1/characters/profile";

    public RaiderIOService(
        ILogger<RaiderIOService> logger,
        HttpClient httpClient,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<(RaiderIOProfile?, List<string>)> GetCharacterProfileAsync(
        string region,
        string realm,
        string characterName,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var apiKey = _configuration["RaiderIO:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("RaiderIO API key not configured");
                return (null, new List<string> { "No raid data" });
            }

            // Normalize realm name (replace spaces with hyphens, lowercase)
            var normalizedRealm = realm.ToLowerInvariant().Replace(" ", "-");

            var url =
                $"{BaseUrl}?access_key={apiKey}&region={region}&realm={normalizedRealm}&name={characterName}&fields=raid_progression";

            _logger.LogDebug(
                "Fetching RaiderIO profile for {Character} on {Realm} ({Region})",
                characterName,
                normalizedRealm,
                region
            );

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch RaiderIO profile for {Character}. Status: {Status}",
                    characterName,
                    response.StatusCode
                );
                return (null, new List<string> { "No raid data" });
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var profile = JsonSerializer.Deserialize<RaiderIOProfile>(json, options);

            if (profile != null)
            {
                _logger.LogInformation(
                    "Successfully fetched raid progression for {Character}: {Summary}",
                    characterName,
                    GetRaidProgressionSummary(profile)
                );
            }

            return (profile, GetRaidProgressionSummary(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error fetching RaiderIO profile for {Character} on {Realm}",
                characterName,
                realm
            );
            return (null, new List<string> { "No raid data" });
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

            summaries.Add($"{tierName}: {tierProgress}");
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
