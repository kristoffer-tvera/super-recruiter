using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IWarcraftLogsService
{
    Task<Player> GetCharacterDataAsync(
        Player player,
        CancellationToken cancellationToken = default
    );
}

public class WarcraftLogsService(
    ILogger<WarcraftLogsService> logger,
    HttpClient httpClient,
    IConfiguration configuration
) : IWarcraftLogsService
{
    private string? _accessToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    private const string TokenUrl = "https://www.warcraftlogs.com/oauth/token";
    private const string GraphQLUrl = "https://www.warcraftlogs.com/api/v2/client";

    public async Task<Player> GetCharacterDataAsync(
        Player player,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Ensure we have a valid access token
            await EnsureAccessTokenAsync(cancellationToken);

            // Build the GraphQL query
            var query =
                $"{{ characterData {{ character(name: \"{player.CharacterName}\", serverSlug: \"{player.RealmSlug}\", serverRegion: \"EU\") {{ id name level classID zoneRankings }} }} }}";

            var requestBody = new { query };

            // Send the GraphQL request
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQLUrl)
            {
                Content = JsonContent.Create(requestBody),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogInformation(
                "WarcraftLogs response for {CharacterName}: {Response}",
                player.CharacterName,
                responseContent
            );

            // Parse the response
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // TODO: Extract relevant data from the response and populate player object
            // For now, just log the response

            return player;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error fetching WarcraftLogs data for {CharacterName}",
                player.CharacterName
            );
            return player;
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        // Check if we have a valid token
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
        {
            return;
        }

        // Get new access token
        var clientId = configuration["WarcraftLogs:ClientId"];
        var clientSecret = configuration["WarcraftLogs:ClientSecret"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                "WarcraftLogs ClientId and ClientSecret must be configured"
            );
        }

        logger.LogInformation("Requesting new WarcraftLogs access token...");

        // Create Basic auth header
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")
        );

        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(
            new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") }
        );

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<WarcraftLogsTokenResponse>(
            cancellationToken: cancellationToken
        );

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Failed to obtain WarcraftLogs access token");
        }

        _accessToken = tokenResponse.AccessToken;
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // Refresh 1 minute early

        logger.LogInformation(
            "WarcraftLogs access token obtained, expires at {ExpiresAt}",
            _tokenExpiresAt
        );
    }

    private class WarcraftLogsTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
    }
}
