using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public sealed class PlayerSourceClient(IHttpClientFactory clients, IConfiguration configuration)
{
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryAfter = new();
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public async Task<JsonElement> FetchRaiderIoAsync(PlayerResponse player, CancellationToken cancellationToken)
    {
        var profile = await RaiderProfileAsync(player, "gear,raid_progression:current-expansion:previous-expansion", cancellationToken);
        var currentTier = await RaiderProfileAsync(player, "raid_progression:current-tier", cancellationToken);
        var result = JsonNode.Parse(profile.GetRawText())!.AsObject();
        if (profile.TryGetProperty("raid_progression", out var progression))
        {
            var slugs = progression.EnumerateObject().Select(raid => raid.Name).Take(12).ToArray();
            if (slugs.Length > 0)
            {
                var achievements = await RaiderProfileAsync(player, $"raid_achievement_curve:{string.Join(':', slugs)}", cancellationToken);
                if (achievements.TryGetProperty("raid_achievement_curve", out var curve))
                    result["raid_achievement_curve"] = JsonNode.Parse(curve.GetRawText());
            }
        }
        return JsonSerializer.SerializeToElement(new { profile = result, currentTier });
    }

    private async Task<JsonElement> RaiderProfileAsync(PlayerResponse player, string fields, CancellationToken cancellationToken)
    {
        var key = configuration["RaiderIO:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new SourceFetchException("not_configured");
        var url =
            "https://raider.io/api/v1/characters/profile?"
            + string.Join(
                '&',
                new Dictionary<string, string>
                {
                    ["access_key"] = key,
                    ["region"] = "eu",
                    ["realm"] = player.RealmSlug,
                    ["name"] = player.CharacterName,
                    ["fields"] = fields,
                }.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}")
            );
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendJsonAsync("raiderio", request, 1024 * 1024, cancellationToken);
    }

    public async Task<JsonElement> FetchWarcraftLogsAsync(PlayerResponse player, CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_token is null || DateTimeOffset.UtcNow >= _tokenExpiresAt)
            {
                var clientId = configuration["WarcraftLogs:ClientId"];
                var clientSecret = configuration["WarcraftLogs:ClientSecret"];
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                    throw new SourceFetchException("not_configured");
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.warcraftlogs.com/oauth/token");
                tokenRequest.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
                tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
                var token = await SendJsonAsync("warcraftlogs", tokenRequest, 64 * 1024, cancellationToken);
                _token = token.GetProperty("access_token").GetString() ?? throw new SourceFetchException("authentication_failed");
                _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.GetProperty("expires_in").GetInt32() - 60));
            }
        }
        finally
        {
            _tokenGate.Release();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.warcraftlogs.com/api/v2/client");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = JsonContent.Create(
            new
            {
                query = "query Character($name: String!, $realm: String!) { characterData { character(name: $name, serverSlug: $realm, serverRegion: \"EU\") { id name level classID zoneRankings } } }",
                variables = new { name = player.CharacterName, realm = player.RealmSlug },
            }
        );
        var result = await SendJsonAsync("warcraftlogs", request, 1024 * 1024, cancellationToken);
        if (result.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new SourceFetchException("upstream_error");
        if (
            !result.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("characterData", out var characters)
            || characters.ValueKind != JsonValueKind.Object
            || !characters.TryGetProperty("character", out var character)
            || character.ValueKind != JsonValueKind.Object
        )
            throw new SourceFetchException("not_found_or_private");
        return result;
    }

    private async Task<JsonElement> SendJsonAsync(string source, HttpRequestMessage request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (_retryAfter.TryGetValue(source, out var until) && until > DateTimeOffset.UtcNow)
            throw new SourceFetchException("rate_limited");
        using var client = clients.CreateClient("enrichment");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _retryAfter[source] = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new SourceFetchException("rate_limited");
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new SourceFetchException("not_found");
        if (!response.IsSuccessStatusCode)
            throw new SourceFetchException("upstream_error");
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new SourceFetchException("response_too_large");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > maximumBytes)
                throw new SourceFetchException("response_too_large");
            buffer.Write(chunk, 0, count);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new SourceFetchException("invalid_response");
        return document.RootElement.Clone();
    }
}

public sealed class SourceFetchException(string outcome) : Exception(outcome)
{
    public string Outcome { get; } = outcome;
}
