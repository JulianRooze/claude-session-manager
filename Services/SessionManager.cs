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

    /// <summary>
    /// Decodes a Claude project directory name back to a filesystem path.
    /// e.g. "-Users-julian-code-my-project" → "/Users/julian/code/my-project"
    /// Uses greedy matching: walks segments left to right, checking which paths exist on disk.
    /// </summary>
    private string DecodeProjectDirName(string encodedName)
    {
        if (string.IsNullOrEmpty(encodedName) || !encodedName.StartsWith("-"))
            return encodedName;

        // Split by '-' and skip the leading empty segment from the initial '-'
        var segments = encodedName.Substring(1).Split('-');

        var currentPath = "";
        var i = 0;

        while (i < segments.Length)
        {
            // Try progressively longer segment combinations to handle hyphens in directory names
            var found = false;
            for (int end = i; end < segments.Length; end++)
            {
                var candidate = string.Join("-", segments[i..(end + 1)]);
                var testPath = currentPath + "/" + candidate;

                if (Directory.Exists(testPath) || end == segments.Length - 1)
                {
                    // If directory exists, or this is the last possible segment, use it
                    if (Directory.Exists(testPath))
                    {
                        currentPath = testPath;
                        i = end + 1;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                // Fallback: treat remaining segments as one path component
                var remaining = string.Join("-", segments[i..]);
                currentPath += "/" + remaining;
                break;
            }
        }

        return currentPath;
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

            // Decode the directory name to get the actual project path
            var dirName = Path.GetFileName(projectDir);
            var decodedPath = DecodeProjectDirName(dirName);

            try
            {
                var json = File.ReadAllText(indexFile);
                var index = JsonSerializer.Deserialize(json, JsonContext.Default.SessionsIndex);
                if (index?.Entries != null)
                {
                    foreach (var session in index.Entries)
                    {
                        // Override ProjectPath with the decoded directory path
                        // The JSON projectPath can be wrong (reflects last working dir, not start dir)
                        if (!string.IsNullOrEmpty(decodedPath))
                        {
                            session.ProjectPath = decodedPath;
                        }

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

    public void DemoteSession(string sessionId)
    {
        if (_promotedStore.Sessions.ContainsKey(sessionId))
        {
            _promotedStore.Sessions.Remove(sessionId);
            SavePromotedStore();
        }
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
                // Extract only the actual message content from the JSONL line
                var content = ExtractMessageContent(line);
                if (string.IsNullOrEmpty(content)) continue;

                var contentLower = content.ToLowerInvariant();
                foreach (var word in words)
                {
                    if (contentLower.Contains(word) && !matches.Any(m => m.Word == word))
                    {
                        var context = ExtractContext(content, word);
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

    /// <summary>
    /// Extracts the human-readable message content from a JSONL line.
    /// Parses the JSON to get message.content, ignoring metadata and tool calls.
    /// </summary>
    private string? ExtractMessageContent(string jsonLine)
    {
        try
        {
            // Quick pre-check: only process user and assistant messages
            if (!jsonLine.Contains("\"type\":\"user\"") && !jsonLine.Contains("\"type\":\"assistant\""))
                return null;

            // Find the "content" field value - look for "content":" pattern
            var contentKey = "\"content\":\"";
            var idx = jsonLine.IndexOf(contentKey);
            if (idx < 0)
            {
                // Try content as array (tool_use messages have content as array)
                contentKey = "\"content\":[";
                idx = jsonLine.IndexOf(contentKey);
                if (idx < 0) return null;

                // For array content, extract all "text" fields
                var texts = new List<string>();
                var textKey = "\"text\":\"";
                var searchStart = idx;
                while (true)
                {
                    var textIdx = jsonLine.IndexOf(textKey, searchStart);
                    if (textIdx < 0) break;
                    var textStart = textIdx + textKey.Length;
                    var textEnd = FindEndOfJsonString(jsonLine, textStart);
                    if (textEnd > textStart)
                    {
                        texts.Add(UnescapeJsonString(jsonLine.Substring(textStart, textEnd - textStart)));
                    }
                    searchStart = textEnd + 1;
                }
                return texts.Any() ? string.Join(" ", texts) : null;
            }

            // Extract string value after "content":"
            var valStart = idx + contentKey.Length;
            var valEnd = FindEndOfJsonString(jsonLine, valStart);
            if (valEnd <= valStart) return null;

            var raw = jsonLine.Substring(valStart, valEnd - valStart);
            return UnescapeJsonString(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the end of a JSON string value (the closing unescaped quote).
    /// </summary>
    private int FindEndOfJsonString(string s, int start)
    {
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '\\')
            {
                i++; // Skip escaped character
                continue;
            }
            if (s[i] == '"')
            {
                return i;
            }
        }
        return s.Length;
    }

    /// <summary>
    /// Unescapes common JSON string escape sequences.
    /// </summary>
    private string UnescapeJsonString(string s)
    {
        return s.Replace("\\n", "\n").Replace("\\t", "\t")
               .Replace("\\\"", "\"").Replace("\\\\", "\\")
               .Replace("\\r", "\r");
    }

    private string ExtractContext(string content, string word)
    {
        try
        {
            var wordIdx = content.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (wordIdx < 0) return content.Length > 100 ? content.Substring(0, 97) + "..." : content;

            // Extract a window around the match
            var start = Math.Max(0, wordIdx - 60);
            var end = Math.Min(content.Length, wordIdx + word.Length + 60);

            // Try to start/end at word boundaries
            while (start > 0 && content[start] != ' ' && content[start] != '\n') start--;
            while (end < content.Length && content[end] != ' ' && content[end] != '\n') end++;

            var snippet = content.Substring(start, end - start).Trim();

            // Clean up newlines
            snippet = snippet.Replace("\n", " ").Replace("\r", " ");
            snippet = System.Text.RegularExpressions.Regex.Replace(snippet, @"\s+", " ").Trim();

            if (snippet.Length > 120) snippet = snippet.Substring(0, 117) + "...";

            return snippet;
        }
        catch
        {
            return content.Length > 100 ? content.Substring(0, 97) + "..." : content;
        }
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
