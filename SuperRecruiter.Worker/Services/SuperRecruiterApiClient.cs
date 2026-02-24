using System.Net.Http.Json;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

/// <summary>
/// Typed HTTP client for communicating with the SuperRecruiter API.
/// All database access goes through this — the worker never touches the DB directly.
/// </summary>
public class SuperRecruiterApiClient(HttpClient httpClient, ILogger<SuperRecruiterApiClient> logger)
{
    // --- Players ---

    public async Task<PlayerResponse> CreatePlayerAsync(CreatePlayerRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("/api/players", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayerResponse>())!;
    }

    public async Task<PlayerResponse?> UpdatePlayerStatusAsync(int playerId, PlayerStatus status)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/players/{playerId}/status",
            new UpdatePlayerStatusRequest { Status = status }
        );
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PlayerResponse>();
    }

    // --- Seen players ---

    public async Task<DateTime?> GetLastSeenAtAsync(string characterName, string realm)
    {
        var response = await httpClient.GetFromJsonAsync<LastSeenResponse>(
            $"/api/players/seen?name={Uri.EscapeDataString(characterName)}&realm={Uri.EscapeDataString(realm)}"
        );
        return response?.LastSeenAt;
    }

    public async Task AddSeenPlayerAsync(string characterName, string realm, DateTime lastUpdated)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/players/seen",
            new SeenPlayerRequest
            {
                CharacterName = characterName,
                Realm = realm,
                LastUpdated = lastUpdated,
            }
        );
        response.EnsureSuccessStatusCode();
    }

    // --- Blacklist ---

    public async Task<bool> IsPlayerBlacklistedAsync(string characterName, string realm)
    {
        var response = await httpClient.GetFromJsonAsync<BlacklistCheckResponse>(
            $"/api/blacklist/check?name={Uri.EscapeDataString(characterName)}&realm={Uri.EscapeDataString(realm)}"
        );
        return response?.IsBlacklisted ?? false;
    }

    private record LastSeenResponse(DateTime? LastSeenAt);

    private record BlacklistCheckResponse(bool IsBlacklisted);
}
