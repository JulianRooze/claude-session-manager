using System.ComponentModel;
using ClaudeSessionManager.Models;
using ClaudeSessionManager.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ClaudeSessionManager.Commands;

public class ListCommand : Command<ListCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-p|--promoted")]
        [Description("Show only promoted sessions")]
        public bool Promoted { get; set; }

        [CommandOption("-r|--recent")]
        [Description("Show only the N most recent sessions")]
        public int? Recent { get; set; }

        [CommandOption("-s|--status")]
        [Description("Filter by status")]
        public SessionStatus? Status { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var manager = new SessionManager();
        var sessions = settings.Promoted ? manager.GetPromotedSessions() : manager.LoadAllSessions();

        if (settings.Status.HasValue)
        {
            sessions = sessions.Where(s => s.Promoted?.Status == settings.Status.Value).ToList();
        }

        sessions = sessions.OrderByDescending(s => s.Modified).ToList();

        if (settings.Recent.HasValue)
        {
            sessions = sessions.Take(settings.Recent.Value).ToList();
        }

        if (!sessions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No sessions found[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Name/Summary");
        table.AddColumn("Project");
        table.AddColumn("Modified");
        table.AddColumn("Messages");
        table.AddColumn("Status");

        foreach (var session in sessions)
        {
            var displayName = session.Promoted?.Name ?? session.Summary;
            if (displayName.Length > 50)
            {
                displayName = displayName.Substring(0, 47) + "...";
            }

            var projectName = Path.GetFileName(session.ProjectPath);
            var modifiedAgo = GetTimeAgo(session.Modified);
            var status = session.Promoted?.Status.ToString() ?? "-";
            var statusColor = GetStatusColor(session.Promoted?.Status);

            table.AddRow(
                session.SessionId.Substring(0, 8),
                displayName,
                projectName,
                modifiedAgo,
                session.MessageCount.ToString(),
                $"[{statusColor}]{status}[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[dim]Total: {sessions.Count} sessions[/]");
        return 0;
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

    private static string GetStatusColor(SessionStatus? status)
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
}
