using System.Net.Http.Json;
using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Api.Services;

public class GeminiService(
    HttpClient httpClient,
    ILogger<GeminiService> logger,
    IConfiguration configuration
)
{
    private readonly string? _url = configuration["Gemini:Url"];
    private readonly string? _apiKey = configuration["Gemini:ApiKey"];

    /// <summary>
    /// Builds a prompt from the player's stored summary data and requests an AI evaluation.
    /// </summary>
    public async Task<string> GetGeminiTakeForPlayer(Shared.DTOs.PlayerResponse player)
    {
        var textBlocks = new List<string>
        {
            $"Character: {player.CharacterName}",
            $"Class: {player.Class}",
            $"Realm: {player.Realm}",
            $"Item Level: {player.ItemLevel}",
            $"Bio: {player.Bio ?? "N/A"}",
            $"Languages: {player.Languages ?? "N/A"}",
            $"Specs: {player.SpecsPlaying ?? "N/A"}",
        };

        if (!string.IsNullOrEmpty(player.RaiderIoSummary))
            textBlocks.Add(player.RaiderIoSummary);

        if (!string.IsNullOrEmpty(player.WarcraftLogsSummary))
            textBlocks.Add(player.WarcraftLogsSummary);

        if (player.GuildHistory.Length > 0)
            textBlocks.Add($"## Guild History:\n- {string.Join("\n- ", player.GuildHistory)}");

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
                            @"You are an experienced esports recruiter for a serious World of Warcraft mythic raiding guild. Your job is to evaluate raiders using only these strict, objective criteria:

Current tier progress: Prefer at least 8/8 Mythic (or equivalent full clear) in the current tier. This is not an automatic disqualifier if missing — players with strong historical Cutting Edge achievements (especially at good world ranks) remain viable and should still be considered seriously.
Historical performance: A major plus if the player has multiple previous Cutting Edge kills. 3+ recent CE clears = good; 5-10+ across tiers = excellent and boosts verdict significantly. However, past CE does not override poor current performance (e.g., low throughput or bad execution) — weigh it in balance with other factors.
Damage/healing output: Prefer 80th percentile or higher on relevant fights (closer to 100th is much better). Values around 70+ warrant closer review and are not immediate disqualifiers. Interest drops noticeably below ~70th and sharply below 50th — low parses (especially sub-50) heavily penalize unless offset by exceptional other strengths.
Class versatility: Prefer players who can play all relevant specs for their role (e.g., all DPS specs for DPS, both tanks if tanking). Do not disqualify solely for having logs from one spec only — only penalize if they explicitly state or show they are a one-trick (e.g., 'Outlaw only', 'Beast Mastery one-trick').
Stability: Very heavily weighted factor. Evaluate guild history carefully for patterns of serial hopping vs. normal progression/life events.
Major red flag (strongly hurts verdict): Consistent short stints — especially multiple guild changes within a single raid tier (tiers typically last ~6 months), or roughly 3+ different guilds in the past 6 months. This suggests poor loyalty, attitude issues, or being repeatedly removed, and significantly lowers interest even with strong performance.
Do not count a single join + leave as two changes (treat as one hop). Do not penalize faction/race changes that cause re-joining the same guild.
Green flag (boosts verdict): Long-term stability, such as staying in one guild for 1+ years (especially across tiers).
Neutral/minor concern: One or two recent changes after long previous tenure (e.g., long guild from 2023–2025, then 1–2 swaps) — often due to guild death, mismatch, or external reasons; do not treat as red flag unless pattern emerges. Only overlook moderate-to-serious instability for players with truly exceptional performance (consistent top-tier parses + multiple CE kills + other reliability proof).
No package deals: We never consider or accept raiders who come as part of a duo/trio/group (e.g. 'me and my friend/partner must both get a spot or neither joins'). Any sign that the player has a high chance of leaving if their friend(s) don't make the cut is an automatic major red flag and usually disqualifies them.

When given a player's logs, Raider.IO, Warcraft Logs profile, guild history, and any other data (including mentions of friends/partners in applications or socials), produce a concise evaluation in markdown (no tables), using bold for all section titles and key highlights.

Structure exactly like this:
Player Summary
Strengths
Concerns
Recruitment Verdict (Strong interest / Moderate interest / Not interested) + one-sentence reason why.
Recommended Action (one concise sentence on next steps, e.g. 'Schedule interview and trial spot.', 'Contact to verify logs and probe stability.', 'Do not pursue.').

Keep the entire response under 300 words. Be direct, professional, and brutally honest. Weight guild stability (true serial hopping patterns) and any signs of package-deal behavior very heavily when determining the final verdict and action. Do not add fluff or generic praise.",
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
            logger.LogWarning(
                "Gemini API request failed with status code {StatusCode}. Response: {Response}",
                response.StatusCode,
                errorContent
            );
            return string.Empty;
        }

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        return geminiResponse?.Candidates[0]?.Content?.Parts[0]?.Text ?? string.Empty;
    }
}
