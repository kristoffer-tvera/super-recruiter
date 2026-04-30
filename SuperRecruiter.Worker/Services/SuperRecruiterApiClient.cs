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

    public async Task<List<PlayerResponse>> GetAllPlayersAsync()
    {
        var response = await httpClient.GetFromJsonAsync<List<PlayerResponse>>(
            "/api/players?limit=10000&offset=0"
        );
        return response ?? [];
    }

    public async Task<List<PlayerCacheResponse>> GetPlayersCacheAsync()
    {
        var response = await httpClient.GetFromJsonAsync<List<PlayerCacheResponse>>(
            "/api/players/cache"
        );
        return response ?? [];
    }

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

    public async Task<List<SeenPlayerResponse>> GetAllSeenPlayersAsync()
    {
        var response = await httpClient.GetFromJsonAsync<List<SeenPlayerResponse>>(
            "/api/players/seen/all"
        );
        return response ?? [];
    }

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

    public async Task BulkAddSeenPlayersAsync(List<SeenPlayerRequest> requests)
    {
        if (requests.Count == 0)
            return;

        var response = await httpClient.PostAsJsonAsync("/api/players/seen/bulk", requests);
        response.EnsureSuccessStatusCode();
    }

    public async Task CleanupSeenPlayersAsync(int daysToKeep = 30)
    {
        var response = await httpClient.PostAsync(
            $"/api/players/seen/cleanup?daysToKeep={daysToKeep}",
            null
        );
        response.EnsureSuccessStatusCode();
    }

    private record LastSeenResponse(DateTime? LastSeenAt);
}
