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
    public async Task<List<PlayerCacheResponse>> GetPlayersCacheAsync()
    {
        var response = await httpClient.GetFromJsonAsync<List<PlayerCacheResponse>>("players/cache");
        return response ?? [];
    }

    public async Task<PlayerResponse?> GetPlayerByCharacterAsync(string realmSlug, string characterName)
    {
        var response = await httpClient.GetAsync($"players/lookup/{Uri.EscapeDataString(realmSlug)}/{Uri.EscapeDataString(characterName)}");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PlayerResponse>();
    }

    public async Task<PlayerResponse> CreatePlayerAsync(CreatePlayerRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("players", request);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            logger.LogError("CreatePlayer failed with status code {StatusCode}: {ResponseContent}", response.StatusCode, responseContent);
            throw new Exception($"Failed to create player: {responseContent}");
        }
        return (await response.Content.ReadFromJsonAsync<PlayerResponse>())!;
    }

    public async Task<PlayerResponse?> UpdatePlayerStatusByCharacterAsync(string realmSlug, string characterName, PlayerStatus status)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"players/{Uri.EscapeDataString(realmSlug)}/{Uri.EscapeDataString(characterName)}/status",
            new UpdatePlayerStatusRequest { Status = status }
        );
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PlayerResponse>();
    }

    public async Task<List<SeenPlayerResponse>> GetAllSeenPlayersAsync()
    {
        var response = await httpClient.GetFromJsonAsync<List<SeenPlayerResponse>>("players/seen/all");
        return response ?? [];
    }

    public async Task BulkAddSeenPlayersAsync(List<SeenPlayerRequest> requests)
    {
        if (requests.Count == 0)
            return;

        var response = await httpClient.PostAsJsonAsync("players/seen/bulk", requests);

        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Bulk add of seen players failed with status code {StatusCode}: {ResponseContent}", response.StatusCode, responseContent);
            throw new Exception($"Failed to bulk add seen players: {responseContent}");
        }
    }

    public async Task CleanupSeenPlayersAsync(int daysToKeep = 30)
    {
        var response = await httpClient.PostAsync($"players/seen/cleanup?daysToKeep={daysToKeep}", null);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            logger.LogError("CleanupSeenPlayers failed with status code {StatusCode}: {ResponseContent}", response.StatusCode, responseContent);
            throw new Exception($"Failed to cleanup seen players: {responseContent}");
        }
    }

    // --- Admin config ---

    public async Task<AdminConfigResponse?> GetAdminConfigAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<AdminConfigResponse>("config", cancellationToken);
    }

    // --- Chat ---

    /// <summary>
    /// Returns null instead of throwing, so a failed AI call can fall back to a canned Discord reply.
    /// </summary>
    public async Task<string?> GetChatReplyAsync(string message, string? userName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("chat", new ChatMessageRequest { Message = message, UserName = userName }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Chat reply request failed with status code {StatusCode}", response.StatusCode);
                return null;
            }

            var reply = await response.Content.ReadFromJsonAsync<ChatMessageResponse>(cancellationToken);
            return string.IsNullOrWhiteSpace(reply?.Reply) ? null : reply.Reply;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat reply request threw");
            return null;
        }
    }

    // private record LastSeenResponse(DateTime? LastSeenAt);
}
