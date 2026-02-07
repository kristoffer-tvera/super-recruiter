// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
using System.Text.Json.Serialization;
using SuperRecruiter.Converter;

public class AllStar
{
    [JsonPropertyName("partition")]
    public int Partition { get; set; }

    [JsonPropertyName("spec")]
    public string Spec { get; set; }

    [JsonPropertyName("points")]
    public double Points { get; set; }

    [JsonPropertyName("possiblePoints")]
    public int PossiblePoints { get; set; }

    [JsonPropertyName("rank")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string Rank { get; set; } // can be double or string

    [JsonPropertyName("regionRank")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string RegionRank { get; set; }

    [JsonPropertyName("serverRank")]
    public int ServerRank { get; set; }

    [JsonPropertyName("rankPercent")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string RankPercent { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("rankTooltip")]
    public object RankTooltip { get; set; }
}

public class BestRank
{
    [JsonPropertyName("rank_id")]
    public int RankId { get; set; }

    [JsonPropertyName("class")]
    public int Class { get; set; }

    [JsonPropertyName("spec")]
    public int Spec { get; set; }

    [JsonPropertyName("per_second_amount")]
    public double PerSecondAmount { get; set; }

    [JsonPropertyName("ilvl")]
    public int Ilvl { get; set; }

    [JsonPropertyName("fight_metadata")]
    public int FightMetadata { get; set; }
}

public class Character
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("zoneRankings")]
    public ZoneRankings ZoneRankings { get; set; }
}

public class CharacterData
{
    [JsonPropertyName("character")]
    public Character Character { get; set; }
}

public class Data
{
    [JsonPropertyName("characterData")]
    public CharacterData CharacterData { get; set; }
}

public class Encounter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class Ranking
{
    [JsonPropertyName("encounter")]
    public Encounter Encounter { get; set; }

    [JsonPropertyName("rankPercent")]
    public double? RankPercent { get; set; }

    [JsonPropertyName("medianPercent")]
    public double? MedianPercent { get; set; }

    [JsonPropertyName("lockedIn")]
    public bool LockedIn { get; set; }

    [JsonPropertyName("totalKills")]
    public int TotalKills { get; set; }

    [JsonPropertyName("fastestKill")]
    public int FastestKill { get; set; }

    // Unmapped computed field that prints FastestKill (currently in MS) in a human readable format (e.g. 5m 30s)
    [JsonIgnore]
    public string FastestKillFormatted
    {
        get
        {
            TimeSpan time = TimeSpan.FromMilliseconds(FastestKill);
            return $"{(int)time.TotalMinutes}m {time.Seconds}s";
        }
    }

    [JsonPropertyName("allStars")]
    public AllStar? AllStars { get; set; }

    [JsonPropertyName("spec")]
    public string? Spec { get; set; }

    [JsonPropertyName("bestSpec")]
    public string? BestSpec { get; set; }

    [JsonPropertyName("bestAmount")]
    public double BestAmount { get; set; }

    [JsonPropertyName("rankTooltip")]
    public object? RankTooltip { get; set; }

    [JsonPropertyName("bestRank")]
    public BestRank BestRank { get; set; }
}

public class WarcraftLogsCharacterResponse
{
    [JsonPropertyName("data")]
    public Data Data { get; set; }
}

public class ZoneRankings
{
    [JsonPropertyName("bestPerformanceAverage")]
    public double BestPerformanceAverage { get; set; }

    [JsonPropertyName("medianPerformanceAverage")]
    public double MedianPerformanceAverage { get; set; }

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }

    [JsonPropertyName("metric")]
    public string Metric { get; set; }

    [JsonPropertyName("partition")]
    public int Partition { get; set; }

    [JsonPropertyName("zone")]
    public int Zone { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("allStars")]
    public List<AllStar> AllStars { get; set; }

    [JsonPropertyName("rankings")]
    public List<Ranking> Rankings { get; set; }
}
