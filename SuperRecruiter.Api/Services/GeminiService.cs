using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public class GeminiService(HttpClient httpClient, ILogger<GeminiService> logger, IConfiguration configuration)
{
    private readonly string? _url = configuration["Gemini:Url"];
    private readonly string? _apiKey = configuration["Gemini:ApiKey"];

    /// <summary>
    /// Builds a prompt from the player's stored summary data and requests an AI evaluation.
    /// </summary>
    public async Task<string> GetGeminiTakeForPlayer(Shared.DTOs.PlayerResponse player)
    {
        logger.LogInformation("Building Gemini prompt for player {PlayerName} on realm {Realm}, using url: {Url}", player.CharacterName, player.Realm, _url);

        var textBlocks = new List<string>
        {
            $"Character: {player.CharacterName}",
            $"Class: {player.Class}",
            $"Realm: {player.Realm}",
            $"Item Level: {player.ItemLevel:F0}",
            $"Current tier mythic bosses killed: {player.CurrentTierMythicKillCount}",
            $"Bio: {player.Bio ?? "N/A"}",
            $"Languages: {player.Languages ?? "N/A"}",
            $"Specs: {player.SpecsPlaying ?? "N/A"}",
        };

        textBlocks.Add(!string.IsNullOrEmpty(player.RaiderIoSummary) ? player.RaiderIoSummary : "## Raider.IO\n- No data");

        textBlocks.Add(!string.IsNullOrEmpty(player.WarcraftLogsSummary) ? player.WarcraftLogsSummary : "## WarcraftLogs\n- No data");

        textBlocks.Add(
            player.GuildHistory.Length > 0
                ? $"## Guild History (unordered, {player.GuildHistory.Length} entries):\n- {string.Join("\n- ", player.GuildHistory)}"
                : "## Guild History\n- No data"
        );

        var prompt = string.Join("\n\n", textBlocks);
        return await GetGeminiTake(prompt);
    }

    private async Task<string> GetGeminiTake(string userContent)
    {
        var request = new GeminiRequest
        {
            SystemInstruction = new SystemInstruction
            {
                Parts =
                [
                    new Part
                    {
                        Text =
                            @"You are a recruiting officer for a Mythic-progression World of Warcraft guild. You evaluate applicants from scraped profile data and produce a short, decisive report.

## Data rules
- The data is incomplete by nature. Judge only what is present.
- Never invent kills, parses, dates, guild names or personal details. If something is missing or says ""No data"", treat it as uncertainty, not as a negative, and say what you'd need.
- Warcraft Logs percentiles: ""Best"" is their best pull, ""Median"" is typical. Weigh median far more heavily than best.
- Guild history is an unordered list of names, usually without dates. Only call out instability if the list itself makes it evident. Never guess when they joined or left a guild.
- Anchor every claim to a concrete number from the data.

## Rubric
Score the applicant on these four axes, then let the weakest important axis drive the verdict.
1. Current tier progression. A full Mythic clear of the current tier is the benchmark — measure against that tier's actual boss count, never assume a fixed number.
2. Throughput (median percentile). 90+ exceptional, 75-89 strong, 60-74 acceptable with context, below 60 a serious concern, below 40 disqualifying on its own.
3. Track record. Cutting Edge in earlier tiers is a strong positive and recent CE counts more than old CE; AOTC-only is neutral.
4. Stability. Long tenures are a green flag; a long list of guilds is a red flag that outweighs raw performance.

## Judgement calls
- Sample size matters — high percentiles on one or two kills are weak evidence. Say so.
- A single spec in the logs is not proof of a one-trick; only flag it if the applicant says so.
- Only mention duo/package applications if the bio explicitly states one.
- Missing Warcraft Logs data often just means private logging. It is not evidence of poor play.
- Strong history does not excuse weak current performance, and strong current numbers do not excuse serial guild hopping.

## Output
Markdown, no tables, under 200 words, using exactly these bold section titles:

**Summary** — one or two sentences: who they are and their headline numbers.
**Strengths** — up to 3 bullets, each anchored to a number.
**Concerns** — up to 3 bullets, each anchored to a number or a specific gap in the data. Write ""None material."" if there are none.
**Verdict** — begin with exactly one of `Strong interest`, `Moderate interest`, `Not interested`, then a dash and one sentence of reasoning.
**Action** — one imperative sentence, e.g. ""Offer a trial."" / ""Ask for recent Mythic logs before deciding."" / ""Do not pursue.""

Be direct and specific. No flattery, no hedging, no generic advice, no restating data without interpreting it. If the data is too thin to judge, say so plainly in the Verdict and pick the Action that closes the gap.",
                    },
                ],
            },
            Contents = [new Content { Parts = [new Part { Text = userContent }] }],
        };

        var url = $"{_url}?key={_apiKey}";
        httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
        var response = await httpClient.PostAsJsonAsync(url, request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogWarning("Gemini API request failed with status code {StatusCode}. Response: {Response}", response.StatusCode, errorContent);
            return string.Empty;
        }

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        return geminiResponse?.Candidates[0]?.Content?.Parts[0]?.Text ?? string.Empty;
    }
}
