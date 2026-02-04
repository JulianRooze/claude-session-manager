using System.Text.Json;
using ClaudeSessionManager.Models;

namespace ClaudeSessionManager.Services;

public class SessionManager
{
    private readonly string _claudeDir;
    private readonly string _promotedStoreFile;
    private PromotedSessionsStore _promotedStore;

    public SessionManager(string? claudeDir = null)
    {
        _claudeDir = claudeDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        _promotedStoreFile = Path.Combine(_claudeDir, "sessions-manager.json");
        _promotedStore = LoadPromotedStore();
    }

    private PromotedSessionsStore LoadPromotedStore()
    {
        if (!File.Exists(_promotedStoreFile))
        {
            return new PromotedSessionsStore();
        }

        try
        {
            var json = File.ReadAllText(_promotedStoreFile);
            return JsonSerializer.Deserialize(json, JsonContext.Default.PromotedSessionsStore) ?? new PromotedSessionsStore();
        }
        catch
        {
            return new PromotedSessionsStore();
        }
    }

    private void SavePromotedStore()
    {
        var json = JsonSerializer.Serialize(_promotedStore, JsonContext.Default.PromotedSessionsStore);
        File.WriteAllText(_promotedStoreFile, json);
    }

    public List<ClaudeSession> LoadAllSessions()
    {
        var allSessions = new List<ClaudeSession>();
        var projectsDir = Path.Combine(_claudeDir, "projects");

        if (!Directory.Exists(projectsDir))
        {
            return allSessions;
        }

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            var indexFile = Path.Combine(projectDir, "sessions-index.json");
            if (!File.Exists(indexFile))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(indexFile);
                var index = JsonSerializer.Deserialize(json, JsonContext.Default.SessionsIndex);
                if (index?.Entries != null)
                {
                    foreach (var session in index.Entries)
                    {
                        // Attach promoted metadata if exists
                        if (_promotedStore.Sessions.TryGetValue(session.SessionId, out var promoted))
                        {
                            session.Promoted = promoted;
                        }
                        allSessions.Add(session);
                    }
                }
            }
            catch
            {
                // Skip invalid index files
            }
        }

        return allSessions;
    }

    public void PromoteSession(string sessionId, string? name = null, string? description = null,
        List<string>? tags = null, SessionStatus? status = null)
    {
        if (!_promotedStore.Sessions.ContainsKey(sessionId))
        {
            _promotedStore.Sessions[sessionId] = new PromotedMetadata();
        }

        var metadata = _promotedStore.Sessions[sessionId];

        if (name != null) metadata.Name = name;
        if (description != null) metadata.Description = description;
        if (tags != null) metadata.Tags = tags;
        if (status.HasValue) metadata.Status = status.Value;

        SavePromotedStore();
    }

    public void AddNote(string sessionId, string noteText)
    {
        if (!_promotedStore.Sessions.ContainsKey(sessionId))
        {
            _promotedStore.Sessions[sessionId] = new PromotedMetadata();
        }

        _promotedStore.Sessions[sessionId].Notes.Add(new Note { Text = noteText });
        SavePromotedStore();
    }

    public void ArchiveSession(string sessionId)
    {
        if (!_promotedStore.Sessions.ContainsKey(sessionId))
        {
            _promotedStore.Sessions[sessionId] = new PromotedMetadata();
        }

        _promotedStore.Sessions[sessionId].Status = SessionStatus.Archived;
        SavePromotedStore();
    }

    public void UpdateStatus(string sessionId, SessionStatus status)
    {
        if (!_promotedStore.Sessions.ContainsKey(sessionId))
        {
            _promotedStore.Sessions[sessionId] = new PromotedMetadata();
        }

        _promotedStore.Sessions[sessionId].Status = status;
        SavePromotedStore();
    }

    public List<SessionSearchResult> SearchSessions(string query)
    {
        var allSessions = LoadAllSessions();
        var queryWords = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!queryWords.Any()) return new List<SessionSearchResult>();

        var results = new List<SessionSearchResult>();

        foreach (var session in allSessions)
        {
            var matchedWords = new HashSet<string>();
            var matchPreviews = new List<string>();

            // Check metadata first for each word
            foreach (var word in queryWords)
            {
                if ((session.Promoted?.Name?.ToLowerInvariant().Contains(word) ?? false) ||
                    session.Summary.ToLowerInvariant().Contains(word) ||
                    session.FirstPrompt.ToLowerInvariant().Contains(word))
                {
                    matchedWords.Add(word);
                }
            }

            // Then check conversation content for remaining words
            var unmatchedWords = queryWords.Except(matchedWords).ToList();
            if (unmatchedWords.Any())
            {
                var conversationMatches = SearchConversationContentWithContext(session.FullPath, unmatchedWords);
                foreach (var match in conversationMatches)
                {
                    matchedWords.Add(match.Word);
                    // Only add preview if it's readable (not the fallback)
                    if (matchPreviews.Count < 2 && match.Context != "[match in conversation]")
                    {
                        matchPreviews.Add(match.Context);
                    }
                }
            }

            if (matchedWords.Any())
            {
                // Use summary as preview if we don't have any good conversation excerpts
                var preview = matchPreviews.Any() ? string.Join(" ... ", matchPreviews) : session.Summary;
                if (preview.Length > 150) preview = preview.Substring(0, 147) + "...";

                results.Add(new SessionSearchResult
                {
                    Session = session,
                    MatchCount = matchedWords.Count,
                    TotalWords = queryWords.Count,
                    MatchPreview = preview
                });
            }
        }

        // Sort by match count (descending), then by modified date (descending)
        return results.OrderByDescending(r => r.MatchCount)
                     .ThenByDescending(r => r.Session.Modified)
                     .ToList();
    }

    private List<ConversationMatch> SearchConversationContentWithContext(string jsonlPath, List<string> words)
    {
        var matches = new List<ConversationMatch>();
        if (!File.Exists(jsonlPath) || !words.Any()) return matches;

        try
        {
            using var reader = new StreamReader(jsonlPath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var lineLower = line.ToLowerInvariant();
                foreach (var word in words)
                {
                    if (lineLower.Contains(word) && !matches.Any(m => m.Word == word))
                    {
                        // Extract a snippet of text around the match
                        var context = ExtractContext(line, word);
                        matches.Add(new ConversationMatch { Word = word, Context = context });

                        if (matches.Count >= words.Count)
                        {
                            return matches; // Found all words
                        }
                    }
                }
            }
        }
        catch
        {
            // If we can't read the file, skip it
        }

        return matches;
    }

    private string ExtractContext(string line, string word)
    {
        try
        {
            // Look for the word in the line
            var wordIdx = line.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (wordIdx < 0) return "[match in conversation]";

            // Extract a window around the match
            var start = Math.Max(0, wordIdx - 60);
            var end = Math.Min(line.Length, wordIdx + 100);
            var snippet = line.Substring(start, end - start);

            // Clean up
            snippet = snippet.Replace("\\n", " ").Replace("\\t", " ")
                           .Replace("\\\"", "\"").Replace("\\\\", "\\")
                           .Replace("\\r", " ");

            // Remove JSON noise
            snippet = System.Text.RegularExpressions.Regex.Replace(snippet, @"[{}\[\]"",:]", " ");
            snippet = System.Text.RegularExpressions.Regex.Replace(snippet, @"\s+", " ").Trim();

            // Check if this looks like readable text
            if (!IsReadableText(snippet))
            {
                return "[match in conversation]";
            }

            // Truncate to reasonable length
            if (snippet.Length > 100) snippet = snippet.Substring(0, 97) + "...";

            return snippet;
        }
        catch
        {
            return "[match in conversation]";
        }
    }

    private bool IsReadableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 15) return false;

        // Count alphanumeric vs total chars
        var alphaNum = text.Count(char.IsLetterOrDigit);
        var total = text.Length;

        if (total == 0) return false;
        var ratio = (double)alphaNum / total;

        // Should be at least 60% alphanumeric (filters out base64, UUIDs, etc.)
        if (ratio < 0.6) return false;

        // Must contain spaces (actual sentences)
        if (!text.Contains(' ')) return false;

        // Check for common noise patterns
        if (text.Contains("signature") && text.Length < 30) return false;
        if (text.Contains("version") && text.Contains("gitBranch")) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z0-9+/]{40,}")) return false; // Base64
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[a-f0-9-]{30,}")) return false; // UUIDs

        // Count actual words (sequences of letters)
        var words = System.Text.RegularExpressions.Regex.Matches(text, @"\b[a-zA-Z]{3,}\b").Count;
        if (words < 3) return false; // At least 3 real words

        return true;
    }

    private class ConversationMatch
    {
        public string Word { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
    }

    public class SessionSearchResult
    {
        public ClaudeSession Session { get; set; } = null!;
        public int MatchCount { get; set; }
        public int TotalWords { get; set; }
        public string MatchPreview { get; set; } = string.Empty;
    }

    public List<ClaudeSession> GetPromotedSessions()
    {
        var allSessions = LoadAllSessions();
        return allSessions.Where(s => s.Promoted != null).ToList();
    }
}
