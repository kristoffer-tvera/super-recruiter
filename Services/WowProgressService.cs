using System.Globalization;
using HtmlAgilityPack;
using super_recruiter.Models;

namespace super_recruiter.Services;

public interface IWowProgressService
{
    Task<List<Player>> GetLookingForGuildPlayersAsync(
        CancellationToken cancellationToken = default
    );
}

public class WowProgressService : IWowProgressService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WowProgressService> _logger;
    private readonly IConfiguration _configuration;
    private const string BaseUrl = "https://www.wowprogress.com";
    private const string LfgUrl = "/gearscore/eu?lfg=1&sortby=ts";

    public WowProgressService(
        HttpClient httpClient,
        ILogger<WowProgressService> logger,
        IConfiguration configuration
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;

        // Keep it as simple as possible - let HttpClient use its defaults
        // Sometimes less is more with bot detection
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

            if (useTestData)
            {
                _logger.LogWarning("Using TEST DATA mode - not fetching from live site");
                html = TestDataProvider.SampleHtml;
            }
            else
            {
                _logger.LogInformation("Fetching player data from WoWProgress...");

                // Try using the simpler GetStringAsync directly
                try
                {
                    html = await _httpClient.GetStringAsync(BaseUrl + LfgUrl, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(
                        "Failed to fetch data. Status: {StatusCode}, Message: {Message}",
                        ex.StatusCode,
                        ex.Message
                    );
                    _logger.LogWarning(
                        "TIP: Set 'UseTestData: true' in appsettings.json to test with sample data"
                    );
                    return players;
                }
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find the table with player data
            // The table has rows with format: character info | guild | raid | realm | ilvl | date
            var rows = doc.DocumentNode.SelectNodes("//table[@class='rating']//tr[position()>1]");

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

                    // Cell 0: Character (contains race and class)
                    var characterCell = cells[0];
                    var characterLink = characterCell.SelectSingleNode(".//a");
                    var characterText = characterCell.InnerText.Trim();
                    var characterUrl =
                        characterLink?.GetAttributeValue("href", string.Empty) ?? string.Empty;

                    // Parse race and class from text (e.g., "orc monk", "blood elf mage")
                    var parts = characterText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string race = string.Empty;
                    string className = string.Empty;

                    if (parts.Length >= 2)
                    {
                        // Handle multi-word races like "blood elf", "kul tiran", etc.
                        if (parts.Length == 2)
                        {
                            race = parts[0];
                            className = parts[1];
                        }
                        else if (parts.Length >= 3)
                        {
                            // Last word is class, rest is race
                            className = parts[^1];
                            race = string.Join(" ", parts[..^1]);
                        }
                    }

                    // Cell 1: Guild
                    var guild = cells[1].InnerText.Trim();
                    if (string.IsNullOrWhiteSpace(guild))
                        guild = null;

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
                    var dateText = cells[5].InnerText.Trim();
                    var lastUpdated = ParseDate(dateText);

                    var player = new Player
                    {
                        Race = race,
                        Class = className,
                        Guild = guild,
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
}
