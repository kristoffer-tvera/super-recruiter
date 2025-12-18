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

    public async Task<RaiderIOProfile?> GetCharacterProfileAsync(
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
                return null;
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
                return null;
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

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error fetching RaiderIO profile for {Character} on {Realm}",
                characterName,
                realm
            );
            return null;
        }
    }

    private string GetRaidProgressionSummary(RaiderIOProfile profile)
    {
        if (profile.Raid_progression == null || !profile.Raid_progression.Any())
            return "No raid data";

        // Get the first (most recent) raid tier
        var latestRaid = profile.Raid_progression.First();
        return $"{latestRaid.Key}: {latestRaid.Value.Summary}";
    }
}
