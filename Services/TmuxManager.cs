using System.Diagnostics;
using ClaudeSessionManager.Models;

namespace ClaudeSessionManager.Services;

public class TmuxManager
{
    private const string TmuxSessionPrefix = "claude-csm";

    public bool IsTmuxAvailable()
    {
        try
        {
            var result = RunCommand("tmux -V");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public bool IsTmuxSessionActive(string sessionName)
    {
        var result = RunCommand($"tmux has-session -t {sessionName} 2>/dev/null");
        return result.Success;
    }

    public List<string> ListTmuxSessions()
    {
        var result = RunCommand("tmux list-sessions -F '#{session_name}' 2>/dev/null");
        if (!result.Success) return new List<string>();

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith(TmuxSessionPrefix))
            .ToList();
    }

    public string CreateSession(List<ClaudeSession> sessions, string? sessionName = null)
    {
        var tmuxSessionName = sessionName ?? $"{TmuxSessionPrefix}-{DateTime.Now:yyyyMMdd-HHmmss}";

        // Create the tmux session with the first Claude session
        var firstSession = sessions[0];
        var firstCommand = $"cd '{firstSession.ProjectPath}' && claude --resume {firstSession.SessionId} --dangerously-skip-permissions";
        var firstName = (firstSession.Promoted?.Name ?? firstSession.Summary).Replace("'", "\\'").Replace("\"", "\\\"");

        RunCommand($"tmux new-session -d -s {tmuxSessionName} -n \"{firstName}\" '{firstCommand}'");

        // Add remaining sessions as new windows
        for (int i = 1; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var command = $"cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions";
            var windowName = (session.Promoted?.Name ?? session.Summary).Replace("'", "\\'").Replace("\"", "\\\"");

            RunCommand($"tmux new-window -t {tmuxSessionName}: -n \"{windowName}\" '{command}'");
        }

        // Select the first window
        RunCommand($"tmux select-window -t {tmuxSessionName}:0");

        return tmuxSessionName;
    }

    public void AttachToSession(string sessionName)
    {
        // We need to exec into tmux to replace the current process
        // This will happen after we return, so we just prepare the command
        var processInfo = new ProcessStartInfo
        {
            FileName = "tmux",
            Arguments = $"attach-session -t {sessionName}",
            UseShellExecute = false
        };

        var process = Process.Start(processInfo);
        process?.WaitForExit();
    }

    public void KillSession(string sessionName)
    {
        RunCommand($"tmux kill-session -t {sessionName}");
    }

    public void AddWindowToSession(string sessionName, ClaudeSession session)
    {
        var command = $"cd '{session.ProjectPath}' && claude --resume {session.SessionId} --dangerously-skip-permissions";
        var windowName = (session.Promoted?.Name ?? session.Summary).Replace("'", "\\'").Replace("\"", "\\\"");

        RunCommand($"tmux new-window -t {sessionName}: -n \"{windowName}\" '{command}'");
    }

    private CommandResult RunCommand(string command)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return new CommandResult { Success = false };

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return new CommandResult
            {
                Success = process.ExitCode == 0,
                Output = output.Trim(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new CommandResult { Success = false, Output = ex.Message };
        }
    }

    private class CommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public int ExitCode { get; set; }
    }
}
