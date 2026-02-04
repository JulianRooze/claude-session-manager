using System.ComponentModel;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class ArchiveCommand : Command<ArchiveCommand.Settings>
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

        manager.ArchiveSession(session.SessionId);
        AnsiConsole.MarkupLine($"[green]✓[/] Archived session: {session.SessionId.Substring(0, 8)}");
        return 0;
    }
}
