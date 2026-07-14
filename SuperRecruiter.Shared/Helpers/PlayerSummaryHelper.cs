using SuperRecruiter.Shared.Models;

namespace SuperRecruiter.Shared.Helpers;

public static class PlayerSummaryHelper
{
    public static string GetBossSummary(ZoneRankings? warcraftLogsZoneRankings)
    {
        var header = "## __WarcraftLogs - Boss Rankings__\n- ";
        var rankings =
            warcraftLogsZoneRankings?.Rankings != null
                ? string.Join(
                    "\n- ",
                    warcraftLogsZoneRankings
                        .Rankings.Where(rank => rank.TotalKills > 0)
                        .Select(rank =>
                            $"**{rank.Encounter.Name}** ({rank.TotalKills}) | Best: {rank.RankPercent:F0}% | Median: {rank.MedianPercent:F0}% | Fastest kill: {rank.FastestKillFormatted}"
                        )
                )
                : "No WarcraftLogs data";
        return header + rankings;
    }

    public static string GetAllStarsSummary(ZoneRankings? zoneRankings)
    {
        var header = "## __WarcraftLogs - Allstars__";

        if (zoneRankings == null || zoneRankings.AllStars == null)
            return $"{header}\n- No WarcraftLogs data";

        var best = $"\n- **Best** Perf. Avg {zoneRankings.BestPerformanceAverage:F0}% |  **Median** Perf. Avg {zoneRankings.MedianPerformanceAverage:F0}%";

        var allStars = zoneRankings.AllStars.Select(a => $"**{a.Spec}** | {a.RankPercent:F0}% | ({a.Points:F0} out of {a.PossiblePoints:F0})").ToList();

        return header + best + "\n- " + string.Join("\n- ", allStars);
    }

    public static string GetCurrentExpansionProgressionSummary(RaiderIOProfile? profile)
    {
        var header = "## __Current Expansion Progression__";

        if (profile?.Raid_progression_summary == null)
            return $"{header}\n- No raid data";

        var progression = string.Join("\n- ", profile.Raid_progression_summary);
        return $"{header}\n- {progression}";
    }

    public static string GetCurrentExpansionProgressForDiscord(RaiderIOProfile? profile)
    {
        var lastTier = profile?.Raid_progression_summary?.LastOrDefault() ?? "No raid data";
        var lastPart = lastTier.Split('|').LastOrDefault()?.Trim() ?? "No raid data";

        return lastPart;
    }

    public static string GetCuttingEdgeSummary(RaiderIOProfile? profile)
    {
        var header = "## __Ahead of the Curve / Cutting Edge__";

        if (profile?.Raid_achievement_curve == null)
            return $"{header}\n- No RaiderIO data";

        var curve = string.Join(
            "\n- ",
            profile.Raid_achievement_curve.Select(tier =>
                $"**{tier.Raid}** | {(tier.Cutting_edge != null ? "Mythic | " + tier.Cutting_edge.Value.ToString("dd.MM.yyyy") : tier.Aotc != null ? "Heroic | " + tier.Aotc.Value.ToString("dd.MM.yyyy") : "Uncleared")}"
            )
        );

        return $"{header}\n- {curve}";
    }

    public static int ClassColorFromClassName(string className)
    {
        return className.ToLower() switch
        {
            "death knight" => 0xC41F3B,
            "demon hunter" => 0xA330C9,
            "druid" => 0xFF7D0A,
            "evoker" => 0x33937F,
            "hunter" => 0xABD473,
            "mage" => 0x69CCF0,
            "monk" => 0x00FF96,
            "paladin" => 0xF58CBA,
            "priest" => 0xFFFFFF,
            "rogue" => 0xFFF569,
            "shaman" => 0x0070DE,
            "warlock" => 0x9482C9,
            "warrior" => 0xC79C6E,
            _ => 0xFFFFFF,
        };
    }
}
