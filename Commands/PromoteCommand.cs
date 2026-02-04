using System.ComponentModel;
using ClaudeSessionManager.Models;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class PromoteCommand : Command<PromoteCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<session-id>")]
        [Description("Session ID (full or first 8 characters)")]
        public string SessionId { get; set; } = string.Empty;

        [CommandOption("-n|--name")]
        [Description("Custom name for the session")]
        public string? Name { get; set; }

        [CommandOption("-d|--description")]
        [Description("Description of the session")]
        public string? Description { get; set; }

        [CommandOption("-t|--tags")]
        [Description("Tags (comma-separated)")]
        public string? Tags { get; set; }

        [CommandOption("-s|--status")]
        [Description("Session status")]
        public SessionStatus? Status { get; set; }
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

        var tags = settings.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        manager.PromoteSession(
            session.SessionId,
            settings.Name,
            settings.Description,
            tags,
            settings.Status);

        AnsiConsole.MarkupLine($"[green]✓[/] Promoted session: {session.SessionId.Substring(0, 8)}");
        if (settings.Name != null) AnsiConsole.MarkupLine($"  [blue]Name:[/] {settings.Name}");
        if (settings.Description != null) AnsiConsole.MarkupLine($"  [blue]Description:[/] {settings.Description}");
        if (tags != null && tags.Any()) AnsiConsole.MarkupLine($"  [blue]Tags:[/] {string.Join(", ", tags)}");
        if (settings.Status.HasValue) AnsiConsole.MarkupLine($"  [blue]Status:[/] {settings.Status}");

        return 0;
    }
}
