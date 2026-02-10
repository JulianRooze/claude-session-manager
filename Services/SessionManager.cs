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
            // Decode the directory name to get the actual project path
            var dirName = Path.GetFileName(projectDir);
            var decodedPath = DecodeProjectDirName(dirName);

            var indexedSessionIds = new HashSet<string>();

            // Load sessions from the index file
            var indexFile = Path.Combine(projectDir, "sessions-index.json");
            if (File.Exists(indexFile))
            {
                try
                {
                    var json = File.ReadAllText(indexFile);
                    var index = JsonSerializer.Deserialize(json, JsonContext.Default.SessionsIndex);
                    if (index?.Entries != null)
                    {
                        foreach (var session in index.Entries)
                        {
                            indexedSessionIds.Add(session.SessionId);

                            if (!string.IsNullOrEmpty(decodedPath))
                            {
                                session.ProjectPath = decodedPath;
                            }

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

            // Discover JSONL files not in the index (recent/unindexed sessions)
            try
            {
                foreach (var jsonlFile in Directory.GetFiles(projectDir, "*.jsonl"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(jsonlFile);
                    if (indexedSessionIds.Contains(fileName))
                        continue;

                    var session = LoadSessionFromJsonl(jsonlFile, fileName, decodedPath);
                    if (session != null)
                    {
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
                // Skip if we can't read the directory
            }
        }

        return allSessions;
    }

    /// <summary>
    /// Returns a clean display name for a session, replacing error messages with a fallback.
    /// </summary>
    public static string GetDisplayName(ClaudeSession session)
    {
        if (session.Promoted?.Name != null)
            return session.Promoted.Name;

        var text = session.Summary;
        if (!string.IsNullOrEmpty(text) && !LooksLikeError(text))
            return text;

        text = session.FirstPrompt;
        if (!string.IsNullOrEmpty(text) && !LooksLikeError(text))
            return text;

        return "(no summary)";
    }

    /// <summary>
    /// Returns a shell command prefix that sets the terminal title and prevents Claude from overriding it.
    /// Returns empty string for sessions without a promoted name.
    /// </summary>
    public static string GetTitleCommandPrefix(ClaudeSession session)
    {
        if (string.IsNullOrEmpty(session.Promoted?.Name))
            return "";

        var name = session.Promoted.Name.Replace("'", "");
        return $"printf '\\x1b]0;{name}\\x07' && export CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1 && ";
    }

    internal static bool LooksLikeError(string text)
    {
        return text.StartsWith("API Error:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("{\"type\":\"error\"", StringComparison.Ordinal)
            || text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts basic session metadata from a JSONL conversation file using System.Text.Json.
    /// Reads the first 50 lines to get first prompt, timestamps, git branch, etc.
    /// Then counts remaining messages with a fast string check.
    /// </summary>
    private ClaudeSession? LoadSessionFromJsonl(string jsonlPath, string sessionId, string decodedPath)
    {
        try
        {
            var fileInfo = new FileInfo(jsonlPath);
            using var reader = new StreamReader(jsonlPath);

            string? firstPrompt = null;
            string? gitBranch = null;
            string? cwd = null;
            DateTime? created = null;
            DateTime? lastTimestamp = null;
            int messageCount = 0;
            bool isSidechain = false;

            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null && lineCount < 50)
            {
                lineCount++;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeEl))
                        continue;

                    var type = typeEl.GetString();
                    if (type != "user" && type != "assistant")
                        continue;

                    messageCount++;

                    if (gitBranch == null && root.TryGetProperty("gitBranch", out var branchEl))
                        gitBranch = branchEl.GetString();

                    if (cwd == null && root.TryGetProperty("cwd", out var cwdEl))
                        cwd = cwdEl.GetString();

                    if (!isSidechain && root.TryGetProperty("isSidechain", out var sidechainEl)
                        && sidechainEl.ValueKind == JsonValueKind.True)
                        isSidechain = true;

                    if (root.TryGetProperty("timestamp", out var tsEl))
                    {
                        var tsStr = tsEl.GetString();
                        if (tsStr != null && DateTime.TryParse(tsStr, out var ts))
                        {
                            if (created == null) created = ts;
                            lastTimestamp = ts;
                        }
                    }

                    if (firstPrompt == null && type == "user"
                        && root.TryGetProperty("message", out var msgEl)
                        && msgEl.TryGetProperty("content", out var contentEl)
                        && contentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = contentEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            firstPrompt = text.Length > 200 ? text.Substring(0, 197) + "..." : text;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // Count remaining messages with fast string check
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains("\"type\":\"user\"") || line.Contains("\"type\":\"assistant\""))
                    messageCount++;
            }

            return new ClaudeSession
            {
                SessionId = sessionId,
                FullPath = jsonlPath,
                FirstPrompt = firstPrompt ?? "",
                Summary = firstPrompt ?? "",
                MessageCount = messageCount,
                Created = created ?? fileInfo.CreationTimeUtc,
                Modified = lastTimestamp ?? fileInfo.LastWriteTimeUtc,
                GitBranch = gitBranch ?? "",
                ProjectPath = !string.IsNullOrEmpty(decodedPath) ? decodedPath : (cwd ?? ""),
                IsSidechain = isSidechain,
                FileMtime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds()
            };
        }
        catch
        {
            return null;
        }
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

            foreach (var word in queryWords)
            {
                if ((session.Promoted?.Name?.ToLowerInvariant().Contains(word) ?? false) ||
                    session.Summary.ToLowerInvariant().Contains(word) ||
                    session.FirstPrompt.ToLowerInvariant().Contains(word))
                {
                    matchedWords.Add(word);
                }
            }

            var unmatchedWords = queryWords.Except(matchedWords).ToList();
            if (unmatchedWords.Any())
            {
                var conversationMatches = SearchConversationContentWithContext(session.FullPath, unmatchedWords);
                foreach (var match in conversationMatches)
                {
                    matchedWords.Add(match.Word);
                    if (matchPreviews.Count < 2 && match.Context != "[match in conversation]")
                    {
                        matchPreviews.Add(match.Context);
                    }
                }
            }

            if (matchedWords.Any())
            {
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

        return results.OrderByDescending(r => r.MatchCount)
                     .ThenByDescending(r => r.Session.Modified)
                     .Take(10)
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
    /// Extracts the human-readable message content from a JSONL line using System.Text.Json.
    /// Only returns text from "text" type content blocks, skipping tool_use, thinking, etc.
    /// </summary>
    private string? ExtractMessageContent(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl))
                return null;

            var type = typeEl.GetString();
            if (type != "user" && type != "assistant")
                return null;

            if (!root.TryGetProperty("message", out var messageEl))
                return null;

            if (!messageEl.TryGetProperty("content", out var contentEl))
                return null;

            // Content can be a plain string (common for user messages)
            if (contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (text != null && LooksLikeError(text))
                    return null;
                return text;
            }

            // Content can be an array of typed blocks (assistant messages)
            if (contentEl.ValueKind == JsonValueKind.Array)
            {
                var texts = new List<string>();
                foreach (var block in contentEl.EnumerateArray())
                {
                    if (block.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!block.TryGetProperty("type", out var blockType))
                        continue;
                    if (blockType.GetString() == "text" && block.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text) && !LooksLikeError(text))
                            texts.Add(text);
                    }
                }
                return texts.Count > 0 ? string.Join(" ", texts) : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
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

