using System.ComponentModel;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class SearchCommand : Command<SearchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Search query")]
        public string Query { get; set; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var manager = new SessionManager();
        var results = manager.SearchSessions(settings.Query);

        if (!results.Any())
        {
            AnsiConsole.MarkupLine($"[yellow]No sessions found matching '{settings.Query}'[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Found {results.Count} session(s) matching '{settings.Query}'[/]\n");

        foreach (var result in results.Take(20))
        {
            var session = result.Session;
            var panel = new Panel(new Markup(
                $"[bold]{SessionManager.GetDisplayName(session)}[/]\n" +
                $"[dim]{session.FirstPrompt.Substring(0, Math.Min(100, session.FirstPrompt.Length))}...[/]\n\n" +
                $"[blue]Project:[/] {session.ProjectPath}\n" +
                $"[blue]Branch:[/] {session.GitBranch}\n" +
                $"[blue]Messages:[/] {session.MessageCount}\n" +
                $"[blue]Modified:[/] {session.Modified:yyyy-MM-dd HH:mm}\n" +
                $"[blue]Preview:[/] [dim]{result.MatchPreview}[/]"
            ))
            {
                Header = new PanelHeader($"[yellow]{session.SessionId.Substring(0, 8)}[/] [green]{result.MatchCount}/{result.TotalWords}[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            };

            AnsiConsole.Write(panel);
        }

        if (results.Count > 20)
        {
            AnsiConsole.MarkupLine($"\n[dim]Showing first 20 of {results.Count} results[/]");
        }

        return 0;
    }
}
