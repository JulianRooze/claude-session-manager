# Claude Session Manager (CSM)

An interactive terminal UI for managing your Claude Code sessions across projects.

## Features

- **Browse Sessions**: View all your Claude conversations with details like project, branch, message count, and last modified time
- **Search**: Quick metadata search or deep search through full conversation content
- **Promote Sessions**: Give important conversations custom names, descriptions, tags, and status tracking
- **Status Management**: Track sessions as Active, Blocked, Completed, or Archived
- **Notes**: Add timestamped notes to sessions
- **Quick Resume**: Resume sessions in iTerm2 tabs or tmux windows
- **tmux Integration**: Open all sessions in a single tmux session with multiple windows (persistent across terminal restarts)

## Installation

From the session-manager directory:

```bash
./install.sh
```

This will build the project and install the `csm` command to your PATH.

## Usage

Simply run:

```bash
csm
```

You'll be greeted with an interactive menu where you can:

1. **Resume all active sessions (iTerm2 tabs)** - Opens all "Active" sessions in new iTerm2 tabs
2. **Attach to existing tmux session** - Resume a previously created tmux session (if available)
3. **Resume all active sessions in tmux** - Opens all "Active" sessions in a single tmux session with multiple windows
4. **Browse all sessions** - See all your Claude conversations
5. **Browse promoted sessions** - View only the sessions you've promoted
6. **Search sessions** - Search across all sessions
7. **Exit** - Close the app

When viewing a session, you can:
- **Resume session** - Jump back into the conversation in Claude Code
- **Promote/Edit metadata** - Add a name, description, tags, and status
- **Add note** - Add a timestamped note to track progress or decisions
- **Change status** - Update the session status
- **Archive** - Mark the session as archived

## Powerful Multi-Word Search

Search always scans full conversation history with intelligent matching:

**How it works:**
- Enter multiple words: `OAuth implementation bug`
- Each word is searched independently across:
  - Session names, summaries, prompts
  - Projects, branches, tags
  - Full conversation text
- Results sorted by match count (sessions matching all words first)
- Shows why each session matched

**Example:**

Search: `YugabyteDB migration error`

Results:
```
3/3  YugabyteDB ID migration
     'YugabyteDB' in summary, name; 'migration' in summary, conversation; 'error' in conversation

2/3  Database schema fixes
     'migration' in conversation; 'error' in conversation

1/3  YugabyteDB setup
     'YugabyteDB' in summary
```

Sessions matching all 3 words appear first, then 2 words, then 1 word.

## Two Modes: iTerm2 or tmux

### iTerm2 Mode (Default)
- Opens each session in a separate iTerm2 tab
- Great for visual tab management
- Click tabs to switch between sessions

### tmux Mode (Persistent)
- Opens all sessions in one tmux session with multiple windows
- Sessions persist even after closing the terminal
- Switch with keyboard shortcuts (Ctrl+B then number)
- Detach and reattach anytime
- Requires: `brew install tmux`

**tmux keybindings:**
- `Ctrl+B then d` - Detach (sessions keep running)
- `Ctrl+B then n/p` - Next/previous window
- `Ctrl+B then 0-9` - Jump to window number
- `Ctrl+B then w` - List all windows

## How It Works

CSM reads Claude Code's session data from `~/.claude/projects/` and stores promoted session metadata in `~/.claude/sessions-manager.json`.

## Session Statuses

- **Active**: Currently working on this (used by "Resume all active sessions")
- **Blocked**: Waiting on something
- **Completed**: Finished
- **Archived**: No longer actively tracking

## Example Workflow

1. Run `csm`
2. Browse all sessions
3. Find important conversations you're actively working on
4. Promote each one:
   - Give it a name like "User Auth Feature"
   - Add tags: authentication, backend
   - Set status to "Active"
   - Add a note: "Need to implement OAuth before finishing"
5. Repeat for other active projects (e.g., "Frontend Refactor", "API Migration")
6. Next time you run `csm`, choose "Resume all active sessions"
7. All your active work opens in separate iTerm2 tabs - instant context switching!
8. When a project is done, update its status to "Completed"

## Benefits

- **Stay organized**: Keep track of conversations across multiple projects
- **Find things fast**: Search by any keyword instead of scrolling through terminal history
- **Track progress**: Use statuses and notes to remember where you left off
- **Quick context switching**: Resume sessions instantly from any directory

Enjoy! 🚀
