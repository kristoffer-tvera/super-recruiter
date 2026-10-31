using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using HtmlAgilityPack;
using SuperRecruiter.Shared.DTOs;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Worker.Services;

public class WowProgressService(ILogger<WowProgressService> logger, HttpClient httpClient, IConfiguration configuration)
{
    private const string BaseUrl = "https://www.wowprogress.com";
    private const string LfgUrl = "/gearscore/eu?lfg=1&sortby=ts";

    public async Task<List<Player>> GetLookingForGuildPlayersAsync(CancellationToken cancellationToken = default)
    {
        var players = new List<Player>();

        try
        {
            logger.LogInformation("Fetching player data using FlareSolverr...");

            var html = await FetchWithFlareSolverrAsync(BaseUrl + LfgUrl, cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
            {
                throw new WowProgressFetchException("invalid_response");
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var rows = doc.DocumentNode.SelectNodes("//table//tr[position()>1]");

            if (rows == null)
            {
                if (doc.DocumentNode.SelectSingleNode("//table") is not null)
                    return players;
                throw new WowProgressFetchException("invalid_response");
            }

            foreach (var row in rows)
            {
                try
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count < 6)
                        continue;

                    var characterCell = cells[0];
                    var characterLink = characterCell.SelectSingleNode(".//a");
                    var characterUrl = characterLink?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                    var characterName = characterLink?.InnerText.Trim() ?? string.Empty;

                    var ariaLabel = characterLink?.GetAttributeValue("aria-label", string.Empty) ?? string.Empty;
                    var parts = ariaLabel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string className = string.Empty;

                    if (parts.Length >= 2)
                    {
                        if (parts.Length >= 3 && parts[^2] == "demon" && parts[^1] == "hunter")
                        {
                            className = "demon hunter";
                        }
                        else if (parts.Length == 2)
                        {
                            className = parts[1];
                        }
                        else if (parts.Length >= 3)
                        {
                            className = parts[^1];
                        }
                    }
                    else if (parts.Length == 1)
                    {
                        className = parts[0];
                    }

                    var realm = cells[3].InnerText.Trim();

                    var ilvlText = cells[4].InnerText.Trim();
                    if (!double.TryParse(ilvlText, NumberStyles.Float, CultureInfo.InvariantCulture, out var itemLevel))
                        continue;

                    var dateSpan = cells[5].SelectSingleNode(".//span[@data-ts]");
                    var timestampStr = dateSpan?.GetAttributeValue("data-ts", string.Empty) ?? string.Empty;
                    DateTime lastUpdated;

                    if (!string.IsNullOrEmpty(timestampStr) && long.TryParse(timestampStr, out var unixTimestamp))
                    {
                        lastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                    }
                    else
                    {
                        lastUpdated = DateTime.UtcNow;
                        logger.LogWarning("Failed to parse timestamp for player, using current time");
                    }

                    var player = new Player
                    {
                        CharacterName = characterName,
                        Class = className,
                        Realm = realm,
                        ItemLevel = itemLevel,
                        LastUpdated = lastUpdated,
                        CharacterUrl = characterUrl.StartsWith("/") ? BaseUrl + characterUrl : characterUrl,
                        Bio = "",
                        Source = LfgSource.WoWProgress,
                    };

                    players.Add(player);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error parsing player row");
                }
            }

            logger.LogInformation("Successfully parsed {Count} players from WoWProgress", players.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching or parsing WoWProgress data");
            throw;
        }

        return players;
    }

    public async Task<WowProgressDetails> GetCharacterDetailsAsync(string characterName, string realmSlug, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/character/eu/{Uri.EscapeDataString(realmSlug)}/{Uri.EscapeDataString(characterName)}";
        return ParseDetails(await FetchWithFlareSolverrAsync(url, cancellationToken));
    }

    public static WowProgressDetails ParseDetails(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var registered = document.DocumentNode.SelectSingleNode("//div[@class='registeredTo']");
        var events = document.DocumentNode.SelectNodes("//ul[@class='eventList']//li");
        if (registered is null && events is null)
            throw new WowProgressFetchException("not_found_or_private");
        static string? Text(HtmlNode? node) => node is null ? null : HtmlEntity.DeEntitize(node.InnerText).Trim();
        var languages = Text(registered?.SelectSingleNode(".//div[@class='language' and contains(., 'Languages:')]"))?.Replace("Languages:", "").Trim();
        return new(
            Text(registered?.SelectSingleNode(".//span[@class='profileBattletag']")),
            Text(registered?.SelectSingleNode(".//div[@class='charCommentary']")),
            languages,
            Text(registered?.SelectSingleNode(".//div[@class='language' and contains(., 'Specs playing:')]//strong")),
            events?.Select(Text).OfType<string>().Where(value => value.Length > 0).Distinct().ToArray() ?? []
        );
    }

    private async Task<string> FetchWithFlareSolverrAsync(string url, CancellationToken cancellationToken)
    {
        var flareSolverrUrl = configuration["FlareSolverrUrl"];
        if (string.IsNullOrWhiteSpace(flareSolverrUrl))
            throw new WowProgressFetchException("not_configured");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(65));
        using var request = new HttpRequestMessage(HttpMethod.Post, flareSolverrUrl)
        {
            Content = JsonContent.Create(
                new
                {
                    cmd = "request.get",
                    url,
                    maxTimeout = 60000,
                }
            ),
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new WowProgressFetchException("rate_limited");
        response.EnsureSuccessStatusCode();
        const int maximumBytes = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new WowProgressFetchException("response_too_large");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, deadline.Token)) > 0)
        {
            if (buffer.Length + count > maximumBytes)
                throw new WowProgressFetchException("response_too_large");
            buffer.Write(chunk, 0, count);
        }
        var result = JsonSerializer.Deserialize<FlareSolverrResponse>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (result?.Solution?.Status == 429)
            throw new WowProgressFetchException("rate_limited");
        if (result?.Solution?.Status == 404)
            throw new WowProgressFetchException("not_found");
        if (result?.Status != "ok" || result.Solution is null || result.Solution.Status != 200 || string.IsNullOrWhiteSpace(result.Solution.Response))
            throw new WowProgressFetchException("unavailable");
        return result.Solution.Response;
    }
}

public sealed class WowProgressFetchException(string outcome) : Exception(outcome)
{
    public string Outcome { get; } = outcome;
}
