namespace ClaudeSessionManager.Models;

public class PromotedSessionsStore
{
    public int Version { get; set; } = 1;
    public Dictionary<string, PromotedMetadata> Sessions { get; set; } = new();
}
