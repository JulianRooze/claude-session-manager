using System.ComponentModel;
using System.Diagnostics;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class ResumeCommand : Command<ResumeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<session-id>")]
        [Description("Session ID or name (full or first 8 characters)")]
        public string SessionId { get; set; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var manager = new SessionManager();
        var allSessions = manager.LoadAllSessions();

        // Try to find by ID first, then by name
        var session = allSessions.FirstOrDefault(s =>
            s.SessionId == settings.SessionId ||
            s.SessionId.StartsWith(settings.SessionId) ||
            (s.Promoted?.Name?.Equals(settings.SessionId, StringComparison.OrdinalIgnoreCase) ?? false));

        if (session == null)
        {
            AnsiConsole.MarkupLine($"[red]Session not found: {settings.SessionId}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Resuming session:[/] {SessionManager.GetDisplayName(session)}");
        AnsiConsole.MarkupLine($"[dim]Project: {session.ProjectPath}[/]");
        AnsiConsole.MarkupLine($"[dim]Session ID: {session.SessionId}[/]");
        AnsiConsole.WriteLine();

        // Build the claude command
        var titleCmd = SessionManager.GetTitleCommandPrefix(session);
        var claudeCommand = $"claude --resume {session.SessionId}";

        // Start the process in the project directory
        var processInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{titleCmd}cd '{session.ProjectPath}' && {claudeCommand}\"",
            UseShellExecute = false
        };

        try
        {
            var process = Process.Start(processInfo);
            process?.WaitForExit();
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error resuming session: {ex.Message}[/]");
            return 1;
        }
    }
}
