using System.ComponentModel;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class ShowCommand : Command<ShowCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<session-id>")]
        [Description("Session ID (full or first 8 characters)")]
        public string SessionId { get; set; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var manager = new SessionManager();
        var allSessions = manager.LoadAllSessions();

        var session = allSessions.FirstOrDefault(s =>
            s.SessionId == settings.SessionId || s.SessionId.StartsWith(settings.SessionId));

        if (session == null)
        {
            AnsiConsole.MarkupLine($"[red]Session not found: {settings.SessionId}[/]");
            return 1;
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[blue]Session ID:[/]", session.SessionId);
        grid.AddRow("[blue]Project:[/]", session.ProjectPath);
        grid.AddRow("[blue]Git Branch:[/]", session.GitBranch);
        grid.AddRow("[blue]Created:[/]", session.Created.ToString("yyyy-MM-dd HH:mm:ss"));
        grid.AddRow("[blue]Modified:[/]", session.Modified.ToString("yyyy-MM-dd HH:mm:ss"));
        grid.AddRow("[blue]Messages:[/]", session.MessageCount.ToString());
        grid.AddRow("[blue]Summary:[/]", session.Summary);

        if (session.Promoted != null)
        {
            grid.AddEmptyRow();
            grid.AddRow("[green bold]PROMOTED[/]", "");
            if (!string.IsNullOrEmpty(session.Promoted.Name))
                grid.AddRow("[blue]Name:[/]", session.Promoted.Name);
            if (!string.IsNullOrEmpty(session.Promoted.Description))
                grid.AddRow("[blue]Description:[/]", session.Promoted.Description);
            if (session.Promoted.Tags.Any())
                grid.AddRow("[blue]Tags:[/]", string.Join(", ", session.Promoted.Tags));
            grid.AddRow("[blue]Status:[/]", session.Promoted.Status.ToString());
            grid.AddRow("[blue]Promoted At:[/]", session.Promoted.PromotedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            if (session.Promoted.Notes.Any())
            {
                grid.AddEmptyRow();
                grid.AddRow("[blue bold]Notes:[/]", "");
                foreach (var note in session.Promoted.Notes.OrderByDescending(n => n.CreatedAt))
                {
                    grid.AddRow($"[dim]{note.CreatedAt:yyyy-MM-dd HH:mm}[/]", note.Text);
                }
            }
        }

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[yellow]Session Details[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);

        AnsiConsole.MarkupLine($"\n[dim]First Prompt:[/]");
        AnsiConsole.MarkupLine($"[dim]{session.FirstPrompt}[/]");

        AnsiConsole.MarkupLine($"\n[dim]File: {session.FullPath}[/]");
        return 0;
    }
}
