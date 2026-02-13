using System.Text.Json.Serialization;

public class Candidate
{
    [JsonPropertyName("content")]
    public Content Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string FinishReason { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

public class Content
{
    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; }
}

public class Part
{
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("thoughtSignature")]
    public string ThoughtSignature { get; set; }
}

public class PromptTokensDetail
{
    [JsonPropertyName("modality")]
    public string Modality { get; set; }

    [JsonPropertyName("tokenCount")]
    public int TokenCount { get; set; }
}

public class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<Candidate> Candidates { get; set; }

    [JsonPropertyName("usageMetadata")]
    public UsageMetadata UsageMetadata { get; set; }

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; }

    [JsonPropertyName("responseId")]
    public string ResponseId { get; set; }
}

public class UsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; set; }

    [JsonPropertyName("promptTokensDetails")]
    public List<PromptTokensDetail> PromptTokensDetails { get; set; }

    [JsonPropertyName("thoughtsTokenCount")]
    public int ThoughtsTokenCount { get; set; }
}

public class GeminiRequest
{
    [JsonPropertyName("system_instruction")]
    public SystemInstruction SystemInstruction { get; set; }

    [JsonPropertyName("contents")]
    public List<Content> Contents { get; set; }
}

public class SystemInstruction
{
    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; }
}
