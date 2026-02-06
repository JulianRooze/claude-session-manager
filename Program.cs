using ClaudeSessionManager.Commands;
using ClaudeSessionManager.UI;
using Spectre.Console.Cli;

// If no arguments, run interactive mode
if (args.Length == 0)
{
    var app = new InteractiveApp();
    app.Run();
    return 0;
}

// Otherwise, use CLI commands
var cliApp = new CommandApp();
cliApp.Configure(config =>
{
    config.AddCommand<SearchCommand>("search")
        .WithDescription("Search sessions");
    config.AddCommand<ListCommand>("list")
        .WithDescription("List sessions");
    config.AddCommand<PromoteCommand>("promote")
        .WithDescription("Promote a session");
    config.AddCommand<ShowCommand>("show")
        .WithDescription("Show session details");
    config.AddCommand<ResumeCommand>("resume")
        .WithDescription("Resume a session");
    config.AddCommand<ArchiveCommand>("archive")
        .WithDescription("Archive a session");
    config.AddCommand<StatusCommand>("status")
        .WithDescription("Update session status");
    config.AddCommand<NoteCommand>("note")
        .WithDescription("Add a note to a session");
});

return cliApp.Run(args);
