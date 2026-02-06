using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeSessionManager.Models;
using ClaudeSessionManager.Services;
using Spectre.Console;

namespace ClaudeSessionManager.UI;

public class InteractiveTableEditor
{
    private readonly SessionManager _manager;
    private readonly List<ColumnDefinition> _columns;

    public InteractiveTableEditor(SessionManager manager)
    {
        _manager = manager;
        _columns = BuildColumnDefinitions();
    }

    public void EditSessions(List<ClaudeSession> sessions)
    {
        if (!sessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No sessions to display[/]");
            return;
        }

        var state = new TableState
        {
            Sessions = sessions,
            SelectedRow = 0,
            SelectedColumn = 1, // Start on Name column (skip ID)
            IsEditing = false
        };

        while (true)
        {
            // Check if we still have sessions (in case they were all demoted)
            if (!state.Sessions.Any())
            {
                Console.Clear();
                AnsiConsole.MarkupLine("[yellow]No more promoted sessions[/]");
                Thread.Sleep(1000);
                Console.Clear();
                return;
            }

            RenderTable(state);

            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    HandleArrowUp(state);
                    break;
                case ConsoleKey.DownArrow:
                    HandleArrowDown(state);
                    break;
                case ConsoleKey.LeftArrow:
                    HandleArrowLeft(state);
                    break;
                case ConsoleKey.RightArrow:
                    HandleArrowRight(state);
                    break;
                case ConsoleKey.Enter:
                case ConsoleKey.E when !state.IsEditing:
                    HandleEdit(state);
                    break;
                case ConsoleKey.Tab:
                    HandleTab(state, shift: key.Modifiers.HasFlag(ConsoleModifiers.Shift));
                    break;
                case ConsoleKey.Escape:
                    if (state.IsEditing)
                        state.IsEditing = false;
                    else
                    {
                        Console.Clear();
                        return; // Exit table
                    }
                    break;
                case ConsoleKey.D when !state.IsEditing:
                    HandleEditDescription(state);
                    break;
                case ConsoleKey.N when !state.IsEditing:
                    HandleAddNote(state);
                    break;
                case ConsoleKey.I when !state.IsEditing:
                    HandleShowDetails(state);
                    break;
                case ConsoleKey.R when !state.IsEditing:
                    HandleResumeSession(state);
                    break;
                case ConsoleKey.X when !state.IsEditing:
                    HandleDemoteSession(state);
                    break;
            }
        }
    }

    private void RenderTable(TableState state)
    {
        Console.Clear();

        // Header
        var header = new Panel(new Markup("[bold yellow]Manage Promoted Sessions[/]\n" +
                                         "[dim]Enter/E: edit | I: details | R: resume | D: description | N: note | X: demote | Esc: exit[/]"))
        {
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(header);
        AnsiConsole.WriteLine();

        // Build table
        var table = new Table();
        table.Border = TableBorder.Rounded;

        // Add columns
        foreach (var column in _columns)
        {
            table.AddColumn(new TableColumn($"[bold]{column.Header}[/]"));
        }

        // Add rows
        for (int rowIndex = 0; rowIndex < state.Sessions.Count; rowIndex++)
        {
            var session = state.Sessions[rowIndex];
            var row = new List<string>();

            for (int colIndex = 0; colIndex < _columns.Count; colIndex++)
            {
                var column = _columns[colIndex];
                var value = column.GetDisplayValue(session);

                // Highlight selected cell
                if (rowIndex == state.SelectedRow && colIndex == state.SelectedColumn)
                {
                    if (state.IsEditing)
                        value = $"[black on yellow] {value} [/]";
                    else
                        value = $"[black on blue] {value} [/]";
                }

                row.Add(value);
            }

            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Footer
        var currentColumn = _columns[state.SelectedColumn];
        var status = currentColumn.IsEditable
            ? $"[green]Editable field: {currentColumn.Header}[/]"
            : $"[dim]Read-only field: {currentColumn.Header}[/]";
        AnsiConsole.MarkupLine(status);
    }

    private void HandleArrowUp(TableState state)
    {
        state.SelectedRow--;
        if (state.SelectedRow < 0)
            state.SelectedRow = state.Sessions.Count - 1;
    }

    private void HandleArrowDown(TableState state)
    {
        state.SelectedRow++;
        if (state.SelectedRow >= state.Sessions.Count)
            state.SelectedRow = 0;
    }

    private void HandleArrowLeft(TableState state)
    {
        do
        {
            state.SelectedColumn--;
            if (state.SelectedColumn < 0)
                state.SelectedColumn = _columns.Count - 1;
        } while (!_columns[state.SelectedColumn].IsEditable && state.SelectedColumn > 0);
    }

    private void HandleArrowRight(TableState state)
    {
        do
        {
            state.SelectedColumn++;
            if (state.SelectedColumn >= _columns.Count)
                state.SelectedColumn = 0;
        } while (!_columns[state.SelectedColumn].IsEditable && state.SelectedColumn < _columns.Count - 1);
    }

    private void HandleTab(TableState state, bool shift)
    {
        if (shift)
            HandleArrowLeft(state);
        else
            HandleArrowRight(state);
    }

    private void HandleEdit(TableState state)
    {
        var column = _columns[state.SelectedColumn];
        if (!column.IsEditable)
            return;

        var session = state.Sessions[state.SelectedRow];

        try
        {
            state.IsEditing = true;
            RenderTable(state);

            column.EditAndSave(session);

            // Reload session data
            var updatedSessions = _manager.GetPromotedSessions()
                .OrderByDescending(s => s.Modified)
                .ToList();

            // Try to find the session by ID and update it in place
            var updatedSession = updatedSessions.FirstOrDefault(s => s.SessionId == session.SessionId);
            if (updatedSession != null)
            {
                state.Sessions[state.SelectedRow] = updatedSession;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error saving: {ex.Message.EscapeMarkup()}[/]");
            Console.ReadKey(true);
        }
        finally
        {
            state.IsEditing = false;
        }
    }

    private void HandleEditDescription(TableState state)
    {
        var session = state.Sessions[state.SelectedRow];

        Console.Clear();
        AnsiConsole.MarkupLine($"[bold]Edit Description for:[/] {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        AnsiConsole.WriteLine();

        var description = AnsiConsole.Ask(
            "[blue]Description:[/]",
            session.Promoted?.Description ?? "");

        _manager.PromoteSession(
            session.SessionId,
            name: session.Promoted?.Name,
            description: description,
            tags: session.Promoted?.Tags,
            status: session.Promoted?.Status);

        // Reload session
        var updatedSession = _manager.GetPromotedSessions()
            .FirstOrDefault(s => s.SessionId == session.SessionId);
        if (updatedSession != null)
        {
            state.Sessions[state.SelectedRow] = updatedSession;
        }
    }

    private void HandleAddNote(TableState state)
    {
        var session = state.Sessions[state.SelectedRow];

        Console.Clear();
        AnsiConsole.MarkupLine($"[bold]Add Note to:[/] {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        AnsiConsole.WriteLine();

        var noteText = AnsiConsole.Ask<string>("[blue]Note:[/]");

        if (!string.IsNullOrWhiteSpace(noteText))
        {
            _manager.AddNote(session.SessionId, noteText);
            AnsiConsole.MarkupLine("[green]Note added![/]");
        }

        AnsiConsole.Markup("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private void HandleShowDetails(TableState state)
    {
        var session = state.Sessions[state.SelectedRow];

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
            grid.AddRow("[blue]Status:[/]", GetStatusDisplay(session.Promoted.Status));

            if (session.Promoted.Notes.Any())
            {
                grid.AddEmptyRow();
                grid.AddRow("[blue bold]Notes:[/]", "");
                foreach (var note in session.Promoted.Notes.OrderByDescending(n => n.CreatedAt).Take(5))
                {
                    var noteText = note.Text.Length > 60 ? note.Text.Substring(0, 57) + "..." : note.Text;
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
        AnsiConsole.Markup("[dim]Press any key to return to table...[/]");
        Console.ReadKey(true);
    }

    private void HandleResumeSession(TableState state)
    {
        var session = state.Sessions[state.SelectedRow];

        Console.Clear();
        AnsiConsole.MarkupLine($"[green]Resuming session:[/] {(SessionManager.GetDisplayName(session)).EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[dim]Project: {session.ProjectPath.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        var isITerm2 = Environment.GetEnvironmentVariable("TERM_PROGRAM") == "iTerm.app";

        if (isITerm2)
        {
            // Use AppleScript to open a new iTerm2 tab
            var tabName = (SessionManager.GetDisplayName(session)).Replace("'", "\\'");
            var appleScript = $@"
tell application ""iTerm2""
    tell current window
        create tab with default profile
        tell current session
            set name to ""{tabName}""
            write text ""cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions""
        end tell
    end tell
end tell";

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"{appleScript.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            };

            try
            {
                var process = System.Diagnostics.Process.Start(processInfo);
                process?.WaitForExit();

                AnsiConsole.MarkupLine("[green]✓ Session opened in new tab[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message.EscapeMarkup()}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]iTerm2 not detected. Session resume requires iTerm2.[/]");
        }

        Thread.Sleep(1000);
    }

    private void HandleDemoteSession(TableState state)
    {
        var session = state.Sessions[state.SelectedRow];

        Console.Clear();
        var confirm = AnsiConsole.Confirm($"Remove [yellow]{(SessionManager.GetDisplayName(session)).EscapeMarkup()}[/] from promoted sessions?");

        if (confirm)
        {
            _manager.DemoteSession(session.SessionId);
            state.Sessions.RemoveAt(state.SelectedRow);

            // Adjust selected row if needed
            if (state.SelectedRow >= state.Sessions.Count && state.Sessions.Count > 0)
            {
                state.SelectedRow = state.Sessions.Count - 1;
            }

            AnsiConsole.MarkupLine("[green]✓ Session demoted[/]");
            Thread.Sleep(1000);
        }
    }

    private List<ColumnDefinition> BuildColumnDefinitions()
    {
        return new List<ColumnDefinition>
        {
            new ColumnDefinition
            {
                Header = "ID",
                IsEditable = false,
                GetDisplayValue = s => s.SessionId.Substring(0, 8),
                EditAndSave = _ => { }
            },
            new ColumnDefinition
            {
                Header = "Name",
                IsEditable = true,
                GetDisplayValue = s => TruncateString(SessionManager.GetDisplayName(s), 40),
                EditAndSave = s => EditNameField(s)
            },
            new ColumnDefinition
            {
                Header = "Status",
                IsEditable = true,
                GetDisplayValue = s => GetStatusDisplay(s.Promoted?.Status ?? SessionStatus.Active),
                EditAndSave = s => EditStatusField(s)
            },
            new ColumnDefinition
            {
                Header = "Tags",
                IsEditable = true,
                GetDisplayValue = s => TruncateString(string.Join(", ", s.Promoted?.Tags ?? new List<string>()), 30),
                EditAndSave = s => EditTagsField(s)
            },
            new ColumnDefinition
            {
                Header = "Modified",
                IsEditable = false,
                GetDisplayValue = s => GetTimeAgo(s.Modified),
                EditAndSave = _ => { }
            }
        };
    }

    private void EditNameField(ClaudeSession session)
    {
        Console.Clear();
        var newName = AnsiConsole.Prompt(
            new TextPrompt<string>("[blue]Name:[/]")
                .DefaultValue(SessionManager.GetDisplayName(session))
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(newName))
            return;

        _manager.PromoteSession(
            session.SessionId,
            name: newName,
            description: session.Promoted?.Description,
            tags: session.Promoted?.Tags,
            status: session.Promoted?.Status);
    }

    private void EditStatusField(ClaudeSession session)
    {
        Console.Clear();
        var newStatus = AnsiConsole.Prompt(
            new SelectionPrompt<SessionStatus>()
                .Title("[blue]Select Status:[/]")
                .AddChoices(Enum.GetValues<SessionStatus>())
                .HighlightStyle(new Style(Color.Yellow)));

        _manager.PromoteSession(
            session.SessionId,
            name: session.Promoted?.Name,
            description: session.Promoted?.Description,
            tags: session.Promoted?.Tags,
            status: newStatus);
    }

    private void EditTagsField(ClaudeSession session)
    {
        Console.Clear();
        var currentTags = session.Promoted?.Tags ?? new List<string>();
        var tagsInput = AnsiConsole.Ask(
            "[blue]Tags (comma-separated):[/]",
            string.Join(", ", currentTags));

        var newTags = string.IsNullOrWhiteSpace(tagsInput)
            ? new List<string>()
            : tagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        _manager.PromoteSession(
            session.SessionId,
            name: session.Promoted?.Name,
            description: session.Promoted?.Description,
            tags: newTags,
            status: session.Promoted?.Status);
    }

    private string GetStatusDisplay(SessionStatus status)
    {
        return status switch
        {
            SessionStatus.Active => "[green]●[/] Active",
            SessionStatus.Blocked => "[red]●[/] Blocked",
            SessionStatus.Completed => "[grey]●[/] Completed",
            SessionStatus.Archived => "[dim]●[/] Archived",
            _ => status.ToString()
        };
    }

    private string GetTimeAgo(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MM/dd/yy");
    }

    private string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 3) + "...";
    }

    private class TableState
    {
        public int SelectedRow { get; set; }
        public int SelectedColumn { get; set; }
        public bool IsEditing { get; set; }
        public List<ClaudeSession> Sessions { get; set; } = new();
    }

    private class ColumnDefinition
    {
        public string Header { get; set; } = string.Empty;
        public bool IsEditable { get; set; }
        public Func<ClaudeSession, string> GetDisplayValue { get; set; } = _ => "";
        public Action<ClaudeSession> EditAndSave { get; set; } = _ => { };
    }
}
