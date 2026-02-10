using System.Diagnostics;
using ClaudeSessionManager.Models;
using ClaudeSessionManager.Services;
using Spectre.Console;

namespace ClaudeSessionManager.UI;

public class InteractiveApp
{
    private readonly SessionManager _manager;
    private readonly TmuxManager _tmuxManager;
    private List<ClaudeSession> _currentSessions;

    public InteractiveApp()
    {
        _manager = new SessionManager();
        _tmuxManager = new TmuxManager();
        _currentSessions = new List<ClaudeSession>();
    }

    public void Run()
    {
        Console.Clear();
        AnsiConsole.Write(new FigletText("CSM").Color(Color.Blue));
        AnsiConsole.MarkupLine("[dim]Claude Session Manager[/]\n");

        var hasTmux = _tmuxManager.IsTmuxAvailable();

        while (true)
        {
            var choices = new List<string>
            {
                "Resume all active sessions (iTerm2 tabs)"
            };

            if (hasTmux)
            {
                var existingSessions = _tmuxManager.ListTmuxSessions();
                if (existingSessions.Any())
                {
                    choices.Add($"Attach to existing tmux session ({existingSessions.Count} available)");
                }
                choices.Add("Resume all active sessions in tmux");
            }

            choices.AddRange(new[]
            {
                "Browse all sessions",
                "Manage promoted sessions",
                "Search sessions",
                "Exit"
            });

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[blue]What would you like to do?[/]")
                    .AddChoices(choices));

            switch (choice)
            {
                case "Resume all active sessions (iTerm2 tabs)":
                    ResumeAllActiveSessions();
                    break;
                case var s when s.StartsWith("Attach to existing tmux"):
                    AttachToExistingTmuxSession();
                    return; // Exit after attaching to tmux
                case "Resume all active sessions in tmux":
                    ResumeAllActiveSessionsInTmux();
                    return; // Exit after creating tmux session
                case "Browse all sessions":
                    BrowseSessions(false);
                    break;
                case "Manage promoted sessions":
                    ManagePromotedSessionsTable();
                    break;
                case "Search sessions":
                    SearchSessions();
                    break;
                case "Exit":
                    return;
            }
        }
    }

    private void BrowseSessions(bool promotedOnly)
    {
        AnsiConsole.Status()
            .Start("Loading sessions...", ctx =>
            {
                _currentSessions = promotedOnly ? _manager.GetPromotedSessions() : _manager.LoadAllSessions();
                _currentSessions = _currentSessions.OrderByDescending(s => s.Modified).ToList();
            });

        if (!_currentSessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No sessions found[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        ShowSessionList();
    }

    private void SearchSessions()
    {
        var query = AnsiConsole.Ask<string>("[blue]Search query:[/] [dim](multiple words supported)[/]");

        List<SessionManager.SessionSearchResult> searchResults = new();

        AnsiConsole.Status()
            .Start("Searching...", ctx =>
            {
                searchResults = _manager.SearchSessions(query);
            });

        if (!searchResults.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]No sessions found matching '{query}'[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[green]Found {searchResults.Count} session(s)[/]\n");

        _currentSessions = searchResults.Select(r => r.Session).ToList();
        ShowSessionListWithMatches(searchResults);
    }

    private void ShowSessionListWithMatches(List<SessionManager.SessionSearchResult> searchResults)
    {
        var choices = searchResults.Select(result =>
        {
            var session = result.Session;
            var name = SessionManager.GetDisplayName(session);
            if (name.Length > 60) name = name.Substring(0, 57) + "...";
            name = name.EscapeMarkup();

            var projectName = Path.GetFileName(session.ProjectPath).EscapeMarkup();
            var timeAgo = GetTimeAgo(session.Modified);
            var statusBadge = session.Promoted != null ? $"[{GetStatusColor(session.Promoted.Status)}]●[/]" : " ";

            var matchRatio = result.MatchCount == result.TotalWords ? "[green]✓[/]" : $"[yellow]{result.MatchCount}/{result.TotalWords}[/]";

            // Truncate match preview
            var preview = result.MatchPreview.EscapeMarkup();
            if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";

            return new SearchResultChoice
            {
                Result = result,
                Display = $"{matchRatio} {statusBadge} {name} [dim]({projectName}, {timeAgo})[/]\n    [dim]{preview}[/]"
            };
        }).ToList();

        choices.Add(new SearchResultChoice
        {
            Result = null,
            Display = "[dim]← Back[/]"
        });

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[blue]Select a session:[/]")
                .PageSize(15)
                .MoreChoicesText("[dim](Move up and down to see more sessions)[/]")
                .AddChoices(choices.Select(c => c.Display)));

        if (selection == "[dim]← Back[/]")
        {
            return;
        }

        var selectedResult = choices.First(c => c.Display == selection).Result;
        if (selectedResult != null)
        {
            ShowSessionDetails(selectedResult.Session);
        }
    }

    private void ShowSessionList()
    {
        var choices = _currentSessions.Select(s =>
        {
            var name = SessionManager.GetDisplayName(s);
            if (name.Length > 60) name = name.Substring(0, 57) + "...";
            name = name.EscapeMarkup(); // Escape markup characters

            var projectName = Path.GetFileName(s.ProjectPath).EscapeMarkup();
            var timeAgo = GetTimeAgo(s.Modified);
            var statusBadge = s.Promoted != null ? $"[{GetStatusColor(s.Promoted.Status)}]●[/]" : " ";

            return new SessionChoice { Session = s, Display = $"{statusBadge} {name} [dim]({projectName}, {timeAgo})[/]" };
        }).ToList();

        choices.Add(new SessionChoice { Session = null, Display = "[dim]← Back[/]" });

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[blue]Select a session:[/]")
                .PageSize(20)
                .MoreChoicesText("[dim](Move up and down to see more sessions)[/]")
                .AddChoices(choices.Select(c => c.Display)));

        if (selection == "[dim]← Back[/]")
        {
            return;
        }

        var selectedSession = choices.First(c => c.Display == selection).Session;
        if (selectedSession != null)
        {
            ShowSessionDetails(selectedSession);
        }
    }

    private void ShowSessionDetails(ClaudeSession session)
    {
        while (true)
        {
            Console.Clear();

            var grid = new Grid();
            grid.AddColumn();
            grid.AddColumn();

            grid.AddRow("[blue bold]Session ID:[/]", session.SessionId);
            grid.AddRow("[blue]Project:[/]", session.ProjectPath.EscapeMarkup());
            grid.AddRow("[blue]Git Branch:[/]", session.GitBranch.EscapeMarkup());
            grid.AddRow("[blue]Created:[/]", session.Created.ToString("yyyy-MM-dd HH:mm"));
            grid.AddRow("[blue]Modified:[/]", session.Modified.ToString("yyyy-MM-dd HH:mm"));
            grid.AddRow("[blue]Messages:[/]", session.MessageCount.ToString());

            if (session.Promoted != null)
            {
                grid.AddEmptyRow();
                grid.AddRow("[green bold]PROMOTED[/]", "");
                if (!string.IsNullOrEmpty(session.Promoted.Name))
                    grid.AddRow("[blue]Name:[/]", session.Promoted.Name.EscapeMarkup());
                if (!string.IsNullOrEmpty(session.Promoted.Description))
                    grid.AddRow("[blue]Description:[/]", session.Promoted.Description.EscapeMarkup());
                if (session.Promoted.Tags.Any())
                    grid.AddRow("[blue]Tags:[/]", string.Join(", ", session.Promoted.Tags.Select(t => t.EscapeMarkup())));
                grid.AddRow("[blue]Status:[/]", $"[{GetStatusColor(session.Promoted.Status)}]{session.Promoted.Status}[/]");

                if (session.Promoted.Notes.Any())
                {
                    grid.AddEmptyRow();
                    grid.AddRow("[blue bold]Notes:[/]", "");
                    foreach (var note in session.Promoted.Notes.OrderByDescending(n => n.CreatedAt).Take(3))
                    {
                        var noteText = note.Text.Length > 50 ? note.Text.Substring(0, 47) + "..." : note.Text;
                        grid.AddRow($"[dim]{note.CreatedAt:MM/dd HH:mm}[/]", noteText.EscapeMarkup());
                    }
                }
            }

            var panelTitle = (SessionManager.GetDisplayName(session)).EscapeMarkup();
            var panel = new Panel(grid)
            {
                Header = new PanelHeader($"[yellow]{panelTitle}[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(2, 1)
            };

            AnsiConsole.Write(panel);

            if (!string.IsNullOrEmpty(session.FirstPrompt) && session.FirstPrompt != "No prompt")
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]First Prompt:[/]");
                var prompt = session.FirstPrompt.Length > 200 ? session.FirstPrompt.Substring(0, 197) + "..." : session.FirstPrompt;
                AnsiConsole.MarkupLine($"[dim]{prompt.EscapeMarkup()}[/]");
            }

            AnsiConsole.WriteLine();

            var actions = new List<string>
            {
                "Resume session",
                session.Promoted == null ? "Promote session" : "Edit metadata",
                "Add note",
                "Change status",
                "Archive",
                "← Back"
            };

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[blue]What would you like to do?[/]")
                    .AddChoices(actions));

            if (action == "← Back") return;

            switch (action)
            {
                case "Resume session":
                    ResumeSession(session);
                    return; // Exit after resume
                case "Promote session":
                case "Edit metadata":
                    PromoteSession(session);
                    // Reload session to get updated metadata
                    session = _manager.LoadAllSessions().First(s => s.SessionId == session.SessionId);
                    break;
                case "Add note":
                    AddNote(session);
                    session = _manager.LoadAllSessions().First(s => s.SessionId == session.SessionId);
                    break;
                case "Change status":
                    ChangeStatus(session);
                    session = _manager.LoadAllSessions().First(s => s.SessionId == session.SessionId);
                    break;
                case "Archive":
                    ArchiveSession(session);
                    return;
            }
        }
    }

    private void ResumeAllActiveSessions()
    {
        AnsiConsole.Status()
            .Start("Loading active sessions...", ctx =>
            {
                _currentSessions = _manager.GetPromotedSessions()
                    .Where(s => s.Promoted?.Status == SessionStatus.Active)
                    .OrderByDescending(s => s.Modified)
                    .ToList();
            });

        if (!_currentSessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No active sessions found[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[green]Found {_currentSessions.Count} active session(s):[/]");
        foreach (var session in _currentSessions)
        {
            AnsiConsole.MarkupLine($"  • {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        }
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm($"Resume all {_currentSessions.Count} session(s)?");
        if (!confirm) return;

        var isITerm2 = Environment.GetEnvironmentVariable("TERM_PROGRAM") == "iTerm.app";

        if (isITerm2)
        {
            // Open all sessions in new iTerm2 tabs
            foreach (var session in _currentSessions)
            {
                AnsiConsole.MarkupLine($"[dim]Opening: {(SessionManager.GetDisplayName(session)).EscapeMarkup()}[/]");

                var tabName = (SessionManager.GetDisplayName(session)).Replace("'", "\\'");
                var titleCmd = SessionManager.GetTitleCommandPrefix(session, escapeForAppleScript: true);
                var appleScript = $@"
tell application ""iTerm2""
    tell current window
        create tab with default profile
        tell current session
            set name to ""{tabName}""
            write text ""{titleCmd}cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions""
        end tell
    end tell
end tell";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e \"{appleScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                try
                {
                    var process = Process.Start(processInfo);
                    process?.WaitForExit();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error opening {session.SessionId.Substring(0, 8)}: {ex.Message}[/]");
                }

                // Small delay between opening tabs
                Thread.Sleep(200);
            }

            AnsiConsole.MarkupLine($"\n[green]✓ Opened {_currentSessions.Count} session(s)[/]");
            Thread.Sleep(1500);
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Opening multiple sessions is only supported in iTerm2[/]");
            AnsiConsole.MarkupLine("[dim]Use 'Browse promoted sessions' to resume them individually[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
        }
    }

    private void ResumeSession(ClaudeSession session)
    {
        AnsiConsole.MarkupLine($"[green]Resuming session:[/] {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[dim]Project: {session.ProjectPath.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        var isITerm2 = Environment.GetEnvironmentVariable("TERM_PROGRAM") == "iTerm.app";

        if (isITerm2)
        {
            // Use AppleScript to open a new iTerm2 tab
            var tabName = (SessionManager.GetDisplayName(session)).Replace("'", "\\'");
            var titleCmd = SessionManager.GetTitleCommandPrefix(session, escapeForAppleScript: true);
            var appleScript = $@"
tell application ""iTerm2""
    tell current window
        create tab with default profile
        tell current session
            set name to ""{tabName}""
            write text ""{titleCmd}cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions""
        end tell
    end tell
end tell";

            var processInfo = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"{appleScript.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            };

            try
            {
                var process = Process.Start(processInfo);
                process?.WaitForExit();

                if (process?.ExitCode != 0)
                {
                    var error = process?.StandardError.ReadToEnd();
                    AnsiConsole.MarkupLine($"[red]Error opening new tab: {error}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                AnsiConsole.Markup("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
        else
        {
            // Fall back to running in current terminal
            var titleCmdBash = SessionManager.GetTitleCommandPrefix(session);
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{titleCmdBash}cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions\"",
                UseShellExecute = false
            };

            try
            {
                var process = Process.Start(processInfo);
                process?.WaitForExit();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                AnsiConsole.Markup("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
    }

    private void PromoteSession(ClaudeSession session)
    {
        var name = AnsiConsole.Ask("[blue]Name:[/]", session.Promoted?.Name ?? "");
        var description = AnsiConsole.Ask("[blue]Description (optional):[/]", session.Promoted?.Description ?? "");

        var tagsInput = AnsiConsole.Ask("[blue]Tags (comma-separated, optional):[/]",
            session.Promoted?.Tags != null ? string.Join(", ", session.Promoted.Tags) : "");
        var tags = string.IsNullOrWhiteSpace(tagsInput)
            ? new List<string>()
            : tagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var status = AnsiConsole.Prompt(
            new SelectionPrompt<SessionStatus>()
                .Title("[blue]Status:[/]")
                .AddChoices(Enum.GetValues<SessionStatus>()));

        _manager.PromoteSession(session.SessionId, name, description, tags, status);
        AnsiConsole.MarkupLine("[green]✓ Session updated[/]");
        Thread.Sleep(1000);
    }

    private void AddNote(ClaudeSession session)
    {
        var note = AnsiConsole.Ask<string>("[blue]Note:[/]");
        _manager.AddNote(session.SessionId, note);
        AnsiConsole.MarkupLine("[green]✓ Note added[/]");
        Thread.Sleep(1000);
    }

    private void ChangeStatus(ClaudeSession session)
    {
        var status = AnsiConsole.Prompt(
            new SelectionPrompt<SessionStatus>()
                .Title("[blue]New status:[/]")
                .AddChoices(Enum.GetValues<SessionStatus>()));

        _manager.UpdateStatus(session.SessionId, status);
        AnsiConsole.MarkupLine($"[green]✓ Status updated to {status}[/]");
        Thread.Sleep(1000);
    }

    private void ArchiveSession(ClaudeSession session)
    {
        var confirm = AnsiConsole.Confirm($"Archive [yellow]{SessionManager.GetDisplayName(session)}[/]?");
        if (confirm)
        {
            _manager.ArchiveSession(session.SessionId);
            AnsiConsole.MarkupLine("[green]✓ Session archived[/]");
            Thread.Sleep(1000);
        }
    }

    private void ManagePromotedSessionsTable()
    {
        List<ClaudeSession> promotedSessions = new();

        AnsiConsole.Status()
            .Start("Loading promoted sessions...", ctx =>
            {
                promotedSessions = _manager.GetPromotedSessions()
                    .OrderByDescending(s => s.Modified)
                    .ToList();
            });

        if (!promotedSessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No promoted sessions found[/]");
            AnsiConsole.MarkupLine("[dim]Promote sessions first using 'Browse all sessions'[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var tableEditor = new InteractiveTableEditor(_manager);
        tableEditor.EditSessions(promotedSessions);
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var diff = DateTime.UtcNow - dateTime;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }

    private static string GetStatusColor(SessionStatus status)
    {
        return status switch
        {
            SessionStatus.Active => "green",
            SessionStatus.Blocked => "red",
            SessionStatus.Completed => "grey",
            SessionStatus.Archived => "dim",
            _ => "white"
        };
    }

    private void AttachToExistingTmuxSession()
    {
        var sessions = _tmuxManager.ListTmuxSessions();
        if (!sessions.Any()) return;

        sessions.Add("← Back");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[blue]Select a tmux session to attach:[/]")
                .AddChoices(sessions));

        if (choice == "← Back") return;

        AnsiConsole.MarkupLine($"[green]Attaching to tmux session:[/] {choice}");
        AnsiConsole.MarkupLine("[dim]Use Ctrl+B then d to detach[/]");
        AnsiConsole.MarkupLine("[dim]Use Ctrl+B then n/p to switch windows[/]");
        Thread.Sleep(2000);

        _tmuxManager.AttachToSession(choice);
    }

    private void ResumeAllActiveSessionsInTmux()
    {
        AnsiConsole.Status()
            .Start("Loading active sessions...", ctx =>
            {
                _currentSessions = _manager.GetPromotedSessions()
                    .Where(s => s.Promoted?.Status == SessionStatus.Active)
                    .OrderByDescending(s => s.Modified)
                    .ToList();
            });

        if (!_currentSessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No active sessions found[/]");
            AnsiConsole.MarkupLine("[dim]Promote sessions and mark them as Active first[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[green]Found {_currentSessions.Count} active session(s):[/]");
        foreach (var session in _currentSessions)
        {
            AnsiConsole.MarkupLine($"  • {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        }
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm($"Create tmux session with all {_currentSessions.Count} session(s)?");
        if (!confirm) return;

        var sessionName = _tmuxManager.CreateSession(_currentSessions);

        AnsiConsole.MarkupLine($"\n[green]✓ Created tmux session:[/] {sessionName}");
        AnsiConsole.MarkupLine($"[dim]Windows created: {_currentSessions.Count}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[blue]tmux keybindings:[/]");
        AnsiConsole.MarkupLine("  [dim]Ctrl+B then d[/]   - Detach (sessions keep running)");
        AnsiConsole.MarkupLine("  [dim]Ctrl+B then n[/]   - Next window");
        AnsiConsole.MarkupLine("  [dim]Ctrl+B then p[/]   - Previous window");
        AnsiConsole.MarkupLine("  [dim]Ctrl+B then 0-9[/] - Switch to window number");
        AnsiConsole.MarkupLine("  [dim]Ctrl+B then w[/]   - List windows");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Attaching in 3 seconds...[/]");
        Thread.Sleep(3000);

        _tmuxManager.AttachToSession(sessionName);
    }

    // Helper classes for SelectionPrompt (Native AOT compatible)
    private class SearchResultChoice
    {
        public SessionManager.SessionSearchResult? Result { get; set; }
        public string Display { get; set; } = string.Empty;
    }

    private class SessionChoice
    {
        public ClaudeSession? Session { get; set; }
        public string Display { get; set; } = string.Empty;
    }
}
