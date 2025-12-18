using System.Globalization;
using HtmlAgilityPack;
using Microsoft.Playwright;
using SuperRecruiter.Models;

namespace SuperRecruiter.Services;

public interface IWowProgressService
{
    Task<List<Player>> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    );
}

public class WowProgressService : IWowProgressService
{
    private readonly ILogger<WowProgressService> _logger;
    private readonly IConfiguration _configuration;
    private const string BaseUrl = "https://www.wowprogress.com";
    private const string LfgUrl = "/gearscore/eu?lfg=1&sortby=ts";

    public WowProgressService(ILogger<WowProgressService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<List<Player>> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    )
    {
        var players = new List<Player>();

        try
        {
            string html;
            var useTestData = _configuration.GetValue<bool>("UseTestData", false);
            var usePlaywright = _configuration.GetValue<bool>("UsePlaywright", true);

            if (useTestData)
            {
                _logger.LogWarning("Using TEST DATA mode - not fetching from live site");
                html = TestDataProvider.SampleHtml;
            }
            else if (usePlaywright)
            {
                _logger.LogInformation("Fetching player data using Playwright (real browser)...");

                try
                {
                    html = await FetchWithPlaywrightAsync(BaseUrl + LfgUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to fetch with Playwright. Have you installed browsers? Run: pwsh bin/Debug/net10.0/playwright.ps1 install chromium"
                    );
                    return players;
                }
            }
            else
            {
                _logger.LogWarning("Playwright disabled - using HttpClient (likely to be blocked)");
                _logger.LogInformation("Fetching player data from WoWProgress...");

                // This will likely fail with 403
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestVersion = new Version(1, 1);

                try
                {
                    html = await httpClient.GetStringAsync(BaseUrl + LfgUrl, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(
                        "Failed to fetch data. Status: {StatusCode}, Message: {Message}",
                        ex.StatusCode,
                        ex.Message
                    );
                    _logger.LogWarning(
                        "TIP: Set 'UsePlaywright: true' or 'UseTestData: true' in appsettings.json"
                    );
                    return players;
                }
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Debug: Log the HTML length and check for table
            _logger.LogDebug("HTML length: {Length} characters", html.Length);

            // Find the table with player data
            // The table has rows with format: character info | guild | raid | realm | ilvl | date
            var rows = doc.DocumentNode.SelectNodes("//table[@class='rating']//tr[position()>1]");

            if (rows == null)
            {
                // Try alternative selector
                rows = doc.DocumentNode.SelectNodes("//table//tr[position()>1]");
                _logger.LogWarning(
                    "Rating table not found, trying generic table. Found {Count} rows",
                    rows?.Count ?? 0
                );

                // Save HTML to file for inspection
                await File.WriteAllTextAsync("debug_output.html", html);
                _logger.LogInformation("Saved HTML to debug_output.html for inspection");
            }

            if (rows == null)
            {
                _logger.LogWarning("No player rows found in the table");
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
                        _logger.LogWarning(
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
                    _logger.LogWarning(ex, "Error parsing player row");
                }
            }

            _logger.LogInformation("Successfully parsed {Count} players", players.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching or parsing WoWProgress data");
        }

        return players;
    }

    private DateTime ParseDate(string dateText)
    {
        // Expected format: "Dec 17, 2025 23:14"
        if (
            DateTime.TryParseExact(
                dateText,
                new[] { "MMM d, yyyy HH:mm", "MMM dd, yyyy HH:mm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            return date;
        }

        return DateTime.UtcNow;
    }

    private async Task<string> FetchWithPlaywrightAsync(
        string url,
        CancellationToken cancellationToken
    )
    {
        // Use Playwright to fetch with a real browser (bypasses bot detection)
        var playwright = await Playwright.CreateAsync();

        // Try non-headless mode - Cloudflare detects headless browsers
        var headless = _configuration.GetValue<bool>("PlaywrightHeadless", false);

        await using var browser = await playwright.Chromium.LaunchAsync(
            new()
            {
                Headless = headless, // Set to false to show browser window
            }
        );

        var page = await browser.NewPageAsync();

        _logger.LogInformation("Navigating to {Url} (headless: {Headless})", url, headless);

        // Navigate to the page
        await page.GotoAsync(
            url,
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }
        );

        // Wait for Cloudflare challenge to complete
        // Look for the rating table or wait for the challenge to disappear
        try
        {
            _logger.LogInformation(
                "Waiting for Cloudflare challenge to complete (this may take 20-30 seconds)..."
            );
            await page.WaitForSelectorAsync("table.rating", new() { Timeout = 60000 });
            _logger.LogInformation("Challenge completed, table loaded!");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timeout waiting for content table. Cloudflare might be blocking automated access."
            );
            _logger.LogInformation(
                "TIP: Try setting 'PlaywrightHeadless: false' to run with visible browser"
            );
        }

        // Get the HTML content
        var html = await page.ContentAsync();

        await browser.CloseAsync();
        playwright.Dispose();

        return html;
    }
}
