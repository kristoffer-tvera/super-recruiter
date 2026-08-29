namespace SuperRecruiter.Shared.DTOs;

/// <summary>
/// A free-text message sent to the chat endpoint, e.g. from a Discord mention.
/// </summary>
public class ChatMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public string? UserName { get; set; }
}
