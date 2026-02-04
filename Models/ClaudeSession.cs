namespace ClaudeSessionManager.Models;

public class ClaudeSession
{
    public string SessionId { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long FileMtime { get; set; }
    public string FirstPrompt { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public string GitBranch { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsSidechain { get; set; }

    // Additional metadata from session manager
    public PromotedMetadata? Promoted { get; set; }
}

public class PromotedMetadata
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public List<Note> Notes { get; set; } = new();
    public DateTime PromotedAt { get; set; } = DateTime.UtcNow;
}

public class Note
{
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum SessionStatus
{
    Active,
    Blocked,
    Completed,
    Archived
}

public class SessionsIndex
{
    public int Version { get; set; }
    public List<ClaudeSession> Entries { get; set; } = new();
    public string OriginalPath { get; set; } = string.Empty;
}
