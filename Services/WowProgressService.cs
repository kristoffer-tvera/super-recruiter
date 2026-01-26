using System.Globalization;
using System.Net.Http.Json;
using HtmlAgilityPack;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IWowProgressService
{
    Task<List<Player>> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    );
    Task<Player> GetPlayerDetailsAsync(
        Player player,
        CancellationToken cancellationToken = default
    );
}

public class WowProgressService(
    ILogger<WowProgressService> logger,
    HttpClient httpClient,
    IConfiguration configuration
) : IWowProgressService
{
    private const string BaseUrl = "https://www.wowprogress.com";
    private const string LfgUrl = "/gearscore/eu?lfg=1&sortby=ts";

    public async Task<List<Player>> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    )
    {
        var players = new List<Player>();

        try
        {
            logger.LogInformation("Fetching player data using FlareSolverr...");

            var html = await FetchWithFlareSolverrAsync(BaseUrl + LfgUrl, cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
            {
                logger.LogInformation("No HTML content fetched, returning empty player list.");
                return players;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find the table with player data
            // The table has rows with format: character info | guild | raid | realm | ilvl | date
            var rows = doc.DocumentNode.SelectNodes("//table[@class='rating']//tr[position()>1]");

            if (rows == null)
            {
                // Try alternative selector
                rows = doc.DocumentNode.SelectNodes("//table//tr[position()>1]");
                logger.LogWarning(
                    "Rating table not found, trying generic table. Found {Count} rows",
                    rows?.Count ?? 0
                );

                // Save HTML to file for inspection
                await File.WriteAllTextAsync("debug_output.html", html);
                logger.LogInformation("Saved HTML to debug_output.html for inspection");
            }

            if (rows == null)
            {
                logger.LogWarning("No player rows found in the table");
                return players;
            }

            foreach (var row in rows)
            {
                try
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count < 6)
                        continue;

                    // Cell 0: Character (contains character name and class in aria-label)
                    var characterCell = cells[0];
                    var characterLink = characterCell.SelectSingleNode(".//a");
                    var characterUrl =
                        characterLink?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                    var characterName = characterLink?.InnerText.Trim() ?? string.Empty;

                    // Parse class from aria-label attribute (e.g., "dwarf hunter", "blood elf demon hunter")
                    var ariaLabel =
                        characterLink?.GetAttributeValue("aria-label", string.Empty)
                        ?? string.Empty;
                    var parts = ariaLabel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string className = string.Empty;

                    if (parts.Length >= 2)
                    {
                        // Handle multi-word classes (e.g., "demon hunter")
                        if (parts.Length >= 3 && parts[^2] == "demon" && parts[^1] == "hunter")
                        {
                            className = "demon hunter";
                        }
                        else if (parts.Length == 2)
                        {
                            // Last word is the class
                            className = parts[1];
                        }
                        else if (parts.Length >= 3)
                        {
                            // Assume last word is class
                            className = parts[^1];
                        }
                    }
                    else if (parts.Length == 1)
                    {
                        // Evoker class (no race specified for Dracthyr)
                        className = parts[0];
                    }

                    // Cell 1: Guild (skip - not interested)

                    // Cell 2: Raid (skip)

                    // Cell 3: Realm
                    var realm = cells[3].InnerText.Trim();

                    // Cell 4: Item Level
                    var ilvlText = cells[4].InnerText.Trim();
                    if (
                        !double.TryParse(
                            ilvlText,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var itemLevel
                        )
                    )
                        continue;

                    // Cell 5: Date/Time
                    // Cell 5: Date/Time - extract from data-ts attribute (Unix timestamp)
                    var dateSpan = cells[5].SelectSingleNode(".//span[@data-ts]");
                    var timestampStr =
                        dateSpan?.GetAttributeValue("data-ts", string.Empty) ?? string.Empty;
                    DateTime lastUpdated;

                    if (
                        !string.IsNullOrEmpty(timestampStr)
                        && long.TryParse(timestampStr, out var unixTimestamp)
                    )
                    {
                        // Convert Unix timestamp to DateTime
                        lastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                    }
                    else
                    {
                        // Fallback to current time if parsing fails
                        lastUpdated = DateTime.UtcNow;
                        logger.LogWarning(
                            "Failed to parse timestamp for player, using current time"
                        );
                    }

                    var player = new Player
                    {
                        CharacterName = characterName,
                        Class = className,
                        Realm = realm,
                        ItemLevel = itemLevel,
                        LastUpdated = lastUpdated,
                        CharacterUrl = characterUrl.StartsWith("/")
                            ? BaseUrl + characterUrl
                            : characterUrl,
                    };

                    players.Add(player);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error parsing player row");
                }
            }

            logger.LogInformation("Successfully parsed {Count} players", players.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching or parsing WoWProgress data");
        }

        return players;
    }

    public async Task<Player> GetPlayerDetailsAsync(
        Player player,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Fetching player details for {CharacterName} using FlareSolverr...",
                player.CharacterName
            );

            var html = await FetchWithFlareSolverrAsync(player.CharacterUrl, cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
            {
                logger.LogInformation("No HTML content fetched, returning player unchanged.");
                return player;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract details from the character page
            var registeredTo = doc.DocumentNode.SelectSingleNode("//div[@class='registeredTo']");

            if (registeredTo != null)
            {
                // Battletag
                var battletagSpan = registeredTo.SelectSingleNode(
                    ".//span[@class='profileBattletag']"
                );
                player.BattleTag = battletagSpan?.InnerText.Trim();

                // Languages
                var languageDiv = registeredTo.SelectSingleNode(
                    ".//div[@class='language' and contains(., 'Languages:')]"
                );
                if (languageDiv != null)
                {
                    var languageText = languageDiv.InnerText.Replace("Languages:", "").Trim();
                    player.Languages = languageText;
                }

                // Specs playing
                var specsDiv = registeredTo.SelectSingleNode(
                    ".//div[@class='language' and contains(., 'Specs playing:')]"
                );
                if (specsDiv != null)
                {
                    var specsText = specsDiv.SelectSingleNode(".//strong")?.InnerText.Trim();
                    player.SpecsPlaying = specsText;
                }

                // Character commentary
                var commentaryDiv = registeredTo.SelectSingleNode(
                    ".//div[@class='charCommentary']"
                );
                if (commentaryDiv != null)
                {
                    player.Bio = commentaryDiv.InnerText.Trim();
                }

                logger.LogInformation(
                    "Successfully parsed details for {CharacterName}: BattleTag={BattleTag}",
                    player.CharacterName,
                    player.BattleTag
                );
            }
            else
            {
                logger.LogWarning(
                    "No registeredTo div found for {CharacterName}. Character may not have a profile.",
                    player.CharacterName
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching or parsing WoWProgress data");
        }

        return player;
    }

    private async Task<string> FetchWithFlareSolverrAsync(
        string url,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var flareSolverrUrl =
                configuration.GetValue<string>("FlareSolverrUrl") ?? "http://192.168.10.66:8191/v1";

            logger.LogInformation("Sending request to FlareSolverr for {Url}", url);

            var request = new
            {
                cmd = "request.get",
                url,
                maxTimeout = 60000,
            };

            var response = await httpClient.PostAsJsonAsync(
                flareSolverrUrl,
                request,
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FlareSolverrResponse>(
                cancellationToken: cancellationToken
            );

            if (result?.Status != "ok" || result.Solution == null)
            {
                logger.LogError(
                    "FlareSolverr request failed. Status: {Status}, Message: {Message}",
                    result?.Status,
                    result?.Message
                );
                return string.Empty;
            }

            logger.LogInformation(
                "Successfully fetched HTML from FlareSolverr (Status: {StatusCode})",
                result.Solution.Status
            );

            return result.Solution.Response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching data from FlareSolverr for {Url}", url);
            return string.Empty;
        }
    }
}
