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

        // Set headers to avoid being blocked
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
        );
        _httpClient.DefaultRequestHeaders.Add(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"
        );
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        _httpClient.DefaultRequestHeaders.Add("DNT", "1");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.wowprogress.com/");
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

                // Create a request message with all headers
                var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + LfgUrl);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Failed to fetch data. Status: {StatusCode}, Reason: {ReasonPhrase}",
                        response.StatusCode,
                        response.ReasonPhrase
                    );
                    _logger.LogWarning(
                        "TIP: Set 'UseTestData: true' in appsettings.json to test with sample data"
                    );
                    return players;
                }

                html = await response.Content.ReadAsStringAsync(cancellationToken);
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
