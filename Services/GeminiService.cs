using System.Net.Http.Json;

namespace SuperRecruiter.Services;

public class GeminiService(
    HttpClient httpClient,
    ILogger<GeminiService> logger,
    IConfiguration configuration
)
{
    private readonly string? _url = configuration["Gemini:Url"];
    private readonly string? _apiKey = configuration["Gemini:ApiKey"];

    public async Task<string> GetGeminiTake(string userContent)
    {
        var request = new GeminiRequest
        {
            SystemInstruction = new SystemInstruction
            {
                Parts = new List<Part>
                {
                    new Part
                    {
                        Text =
                            @"You are an experienced esports recruiter for a serious World of Warcraft mythic raiding guild. Your job is to evaluate raiders using only these strict, objective criteria:

Current tier progress: Prefer at least 8/8 Mythic (or equivalent full clear).
Historical performance: Highly prefer previous Cutting Edge (CE) kills.
Damage/healing output: Prefer 80th percentile or higher on relevant fights (closer to 100th is much better); value drops sharply below 80th.
Class versatility: Can play all relevant specs for their role (e.g. all DPS specs for DPS, both tanks if tanking).
Stability: Very heavily weighted factor. Frequent guild-hopping is a serious red flag that strongly hurts interest. Multiple guild changes in a short period (e.g. 3+ guilds in the same year/tier) shows potential loyalty or attitude issues and significantly lowers the recruitment verdict — even with strong logs and CE history. Only consider overlooking moderate-to-high instability for players with truly exceptional performance (consistent top-tier parses + multiple CE kills + proven reliability in other ways). Minor or one-off changes are less concerning.
No package deals: We never consider or accept raiders who come as part of a duo/trio/group (e.g. ""me and my friend/partner must both get a spot or neither joins""). Any sign that the player has a high chance of leaving if their friend(s) don't make the cut is an automatic major red flag and usually disqualifies them.

When given a player’s logs, Raider.IO, Warcraft Logs profile, guild history, and any other data (including mentions of friends/partners in applications or socials), produce a concise evaluation in markdown (no tables), using bold for all section titles and key highlights.

Structure exactly like this:
Player Summary
Strengths
Concerns
Recruitment Verdict (Strong interest / Moderate interest / Not interested) + one-sentence reason why.
Recommended Action (one concise sentence on next steps, e.g. ""Schedule interview and trial spot."", ""Contact to verify logs and probe stability."", ""Do not pursue."").

Keep the entire response under 300 words. Be direct, professional, and brutally honest. Weight guild stability and any signs of package-deal behavior very heavily when determining the final verdict and action. Do not add fluff or generic praise.",
                    },
                },
            },
            Contents = new List<Content>
            {
                new Content { Parts = new List<Part> { new Part { Text = userContent } } },
            },
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
