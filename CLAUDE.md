# ClaudeVS - Visual Studio 2026 Extension

## Project Overview

ClaudeVS is a Visual Studio 2026 (Insiders) extension that integrates Claude Code CLI into the IDE through a dockable tool window with a chat interface. This is an MVP (Minimum Viable Product) implementation.

**Extension ID:** ClaudeVS.2ca07fc7-11ad-4410-baca-b481834cd8a5
**Package GUID:** b7d90b76-b34d-46e0-ab4f-888666287245

## Architecture

### Design Decisions

1. **Integration Approach:** Spawns Claude Code CLI as a subprocess and communicates via stdin/stdout
2. **UI Approach:** Dockable WPF tool window with chat interface
3. **Scope:** MVP - basic chat functionality to get something working quickly

### Technology Stack

- **Framework:** .NET Framework 4.7.2
- **UI:** WPF (Windows Presentation Foundation)
- **VS SDK:** Microsoft.VisualStudio.SDK v17.0.32112.339
- **Build Tools:** Microsoft.VSSDK.BuildTools v17.14.2120
- **Target VS Version:** Visual Studio 2022+ (17.0+), tested on VS 2026 Insiders

## Project Structure

```
ClaudeVS/
├── .claude/
│   └── settings.local.json
├── .vs/                              [VS IDE settings]
├── bin/
│   └── Debug/
│       ├── ClaudeVS.dll
│       ├── ClaudeVS.vsix            [Extension installer]
│       └── extension.vsixmanifest
├── obj/                              [Build artifacts]
├── Properties/
│   └── AssemblyInfo.cs
├── Resources/
│   └── ClaudeToolWindowCommand.png   [Menu icon]
├── ClaudeCliManager.cs               [CLI process manager]
├── ClaudeToolWindow.cs               [Tool window pane]
├── ClaudeToolWindowCommand.cs        [Command handler]
├── ClaudeToolWindowControl.xaml      [WPF UI]
├── ClaudeToolWindowControl.xaml.cs   [UI code-behind]
├── ClaudeVS.csproj                   [Project file]
├── ClaudeVS.slnx                     [Solution file]
├── ClaudeVSPackage.cs                [Main package class]
├── source.extension.vsixmanifest     [Extension manifest]
├── VSCommandTable.vsct               [Command definitions]
├── klaude.bat                        [Helper: start claude]
├── codex.bat                         [Helper: WSL codex launcher]
└── gemini.bat                        [Helper: WSL gemini launcher]
```

## Components

### 1. ClaudeVSPackage.cs
**Purpose:** Main entry point for the VS extension

**Key Features:**
- Registers the extension with Visual Studio
- Initializes the tool window command on startup
- Attributes:
  - `[PackageRegistration]` - Registers the package
  - `[ProvideMenuResource]` - Registers menu commands
  - `[ProvideToolWindow]` - Registers the Claude tool window

**Location:** c:\Work\ClaudeVS\ClaudeVSPackage.cs

### 2. ClaudeToolWindow.cs
**Purpose:** Defines the tool window pane

**Key Features:**
- Inherits from `ToolWindowPane`
- Sets window caption to "Claude Code"
- Hosts the WPF control (`ClaudeToolWindowControl`)

**GUID:** e3c7a8d9-2f4b-5c6e-8d9f-1a2b3c4d5e6f

**Location:** c:\Work\ClaudeVS\ClaudeToolWindow.cs

### 3. ClaudeToolWindowControl.xaml/.cs
**Purpose:** WPF chat interface

**Key Features:**
- Chat message display with scrolling
- User/Assistant message styling (blue for user, gray for assistant)
- Input textbox with send button
- Loading indicator ("Claude is thinking...")
- Keyboard shortcut: Ctrl+Enter to send
- Auto-scroll to latest messages

**UI Elements:**
- `ChatMessages` - ItemsControl displaying chat history
- `InputTextBox` - Multi-line text input
- `SendButton` - Triggers message send
- `LoadingIndicator` - Shows during processing

**Location:** c:\Work\ClaudeVS\ClaudeToolWindowControl.xaml[.cs]

### 4. ClaudeToolWindowCommand.cs
**Purpose:** Command handler to open the tool window

**Key Features:**
- Registers the "Claude Code" command in View menu
- Opens/shows the tool window when invoked
- Handles command initialization

**Command ID:** 0x0100
**Command Set GUID:** a7c8e9d0-1234-5678-9abc-def012345678

**Location:** c:\Work\ClaudeVS\ClaudeToolWindowCommand.cs

### 5. ClaudeCliManager.cs
**Purpose:** Manages Claude Code CLI process communication

**Key Features:**
- Spawns `claude-code` process
- Redirects stdin/stdout for communication
- Async message sending via `SendMessageAsync()`
- Event-based response handling:
  - `ResponseReceived` - Fired when Claude responds
  - `ErrorOccurred` - Fired on errors
- Process lifecycle management

**Current Implementation Notes:**
- Uses simple delay-based response collection (2 seconds)
- Basic output buffering
- Assumes `claude-code` is in PATH

**Location:** c:\Work\ClaudeVS\ClaudeCliManager.cs

### 6. VSCommandTable.vsct
**Purpose:** Defines commands and menu structure

**Key Features:**
- Adds "Claude Code" command to View menu
- Defines command GUIDs and IDs
- References menu icon

**Location:** c:\Work\ClaudeVS\VSCommandTable.vsct

### 7. source.extension.vsixmanifest
**Purpose:** Extension manifest and metadata

**Key Details:**
- Display Name: "ClaudeVS - Claude Code Integration"
- Description: "Integrates Claude Code CLI into Visual Studio with a chat interface tool window."
- Version: 1.0
- Installation Target: VS Community 17.0+, amd64

**Location:** c:\Work\ClaudeVS\source.extension.vsixmanifest

## How It Works

### Message Flow

1. User types message in `InputTextBox` and clicks Send or presses Ctrl+Enter
2. `ClaudeToolWindowControl.SendMessage()` is called:
   - Adds user message to chat display
   - Shows loading indicator
   - Disables send button
3. `ClaudeCliManager.SendMessageAsync()` is invoked:
   - Ensures CLI process is running
   - Sends message to process stdin
   - Waits for response (currently 2-second delay)
4. Response handling:
   - Output captured via `OutputDataReceived` event
   - Buffered in `outputBuffer`
   - `ResponseReceived` event fired
5. UI updates:
   - Assistant message added to chat
   - Loading indicator hidden
   - Send button re-enabled
   - Auto-scroll to bottom

### CLI Process Management

- **Initialization:** Lazy - process spawned on first message
- **Executable:** `claude-code` (must be in PATH)
- **Working Directory:** User's home folder
- **I/O:** stdin/stdout redirected, stderr captured for errors
- **Lifecycle:** Process kept alive between messages, disposed on control disposal

## Build Information

### Build Command
```bash
"C:/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe" "c:\Work\ClaudeVS\ClaudeVS.csproj" -t:Rebuild -p:Configuration=Debug -v:minimal
```

### Build Output
- **DLL:** `c:\Work\ClaudeVS\bin\Debug\ClaudeVS.dll`
- **VSIX:** `c:\Work\ClaudeVS\bin\Debug\ClaudeVS.vsix`

### Known Warnings
The project builds successfully but has some threading warnings:
- **VSTHRD100:** Async void methods (ClaudeToolWindowControl:44)
- **VSTHRD001:** Dispatcher.Invoke usage instead of JoinableTaskFactory (multiple locations)
- **VSTHRD110:** Unawaited async invocation (ClaudeToolWindowControl:95)

These are non-critical for MVP but should be addressed for production.

## Installation & Usage

### Installation
1. Build the project to generate `ClaudeVS.vsix`
2. Double-click `c:\Work\ClaudeVS\bin\Debug\ClaudeVS.vsix`
3. Follow the VS extension installer prompts
4. Restart Visual Studio 2026 Insiders

### Usage
1. Open Visual Studio 2026 Insiders
2. Go to **View → Claude Code** menu
3. Tool window will dock (can be moved/docked anywhere)
4. Type message in input box
5. Press **Send** button or **Ctrl+Enter**
6. Claude's responses appear in the chat area

### Requirements
- **Visual Studio 2026 Insiders** (or VS 2022+ version 17.0+)
- **Claude Code CLI** installed and in PATH
  - Test by running `claude-code` in terminal

## Current Status

### ✅ Implemented Features
- Dockable tool window with chat interface
- User/Assistant message styling
- Process spawning for Claude Code CLI
- Basic stdin/stdout communication
- Loading indicator while processing
- Auto-scroll to latest messages
- Ctrl+Enter keyboard shortcut
- View menu integration

### ⚠️ Known Limitations
1. **Response Parsing:** Uses simple 2-second delay instead of proper protocol
2. **No File Context:** Doesn't pass VS context (open files, selections) to Claude
3. **No Configuration:** CLI path hardcoded, no settings page
4. **Error Handling:** Basic error handling, could be more robust
5. **Threading Warnings:** Several VSTHRD analyzer warnings
6. **Process Lifecycle:** No graceful shutdown or restart on crash
7. **No Markdown Rendering:** Messages displayed as plain text

### 🔧 Technical Debt
- Threading patterns not optimal for VS extensions
- No unit tests
- Hardcoded CLI executable name
- No logging/diagnostics
- Response parsing needs improvement

## Future Enhancements

### High Priority
1. **Proper CLI Protocol:** Parse Claude Code CLI output properly instead of delay-based collection
2. **File Context:** Pass active document, selection, project info to Claude
3. **Settings Page:** Allow user to configure:
   - Claude CLI path
   - Working directory
   - API keys/credentials
4. **Error Recovery:** Better error handling and process recovery

### Medium Priority
5. **Markdown Rendering:** Display Claude's responses with proper formatting
6. **Command Integration:** Add right-click menu items in editor (e.g., "Ask Claude about this code")
7. **Fix Threading Warnings:** Use proper JoinableTaskFactory patterns
8. **Session Management:** Save/load chat history
9. **Multiple Windows:** Support multiple Claude chat windows

### Low Priority
10. **Syntax Highlighting:** Highlight code blocks in responses
11. **Code Actions:** Quick actions to insert Claude's code suggestions
12. **Telemetry:** Usage analytics (with user consent)
13. **Auto-updates:** Check for extension updates

## Development Notes

### VS Extension Debugging
- Set startup project to launch VS Experimental Instance
- Debug arguments: `/rootsuffix Exp`
- Logs in: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_[hash]Exp`

### Key VS SDK Concepts Used
- **AsyncPackage:** Modern async-loading package
- **ToolWindowPane:** Base for dockable windows
- **OleMenuCommand:** VS command system
- **VSCT Files:** Command table compiler

### Important GUIDs
- **Package:** b7d90b76-b34d-46e0-ab4f-888666287245
- **Tool Window:** e3c7a8d9-2f4b-5c6e-8d9f-1a2b3c4d5e6f
- **Command Set:** a7c8e9d0-1234-5678-9abc-def012345678
- **Images:** d8f1a2b3-4c5e-6f7a-8b9c-0d1e2f3a4b5c

### Helper Scripts
- **klaude.bat:** Launches `claude` command
- **codex.bat:** Launches codex via WSL
- **gemini.bat:** Launches gemini via WSL

## Troubleshooting

### Extension Won't Load
- Check VS version (needs 17.0+)
- Look for errors in: `View → Output → Extension Manager`
- Try: `devenv.exe /resetuserdata`

### Tool Window Won't Open
- Check: `View → Claude Code` menu item exists
- Look for errors in VS Output window
- Verify package loaded: `Tools → Extensions and Updates`

### Claude CLI Not Working
- Verify `claude-code` is in PATH: Open terminal and run `claude-code --version`
- Check process spawning in Task Manager
- Look at stderr output (errors surface in chat)

### Build Failures
- Clean solution: Delete `bin/` and `obj/` folders
- Restore NuGet packages
- Check MSBuild path for VS 2026 Insiders

## Resources

### Documentation
- [VS Extension Development](https://docs.microsoft.com/en-us/visualstudio/extensibility/)
- [Claude Code CLI](https://docs.claude.com/en/docs/claude-code/)
- [VSIX Deployment](https://docs.microsoft.com/en-us/visualstudio/extensibility/anatomy-of-a-vsix-package)

### Related Files
- Extension manifest: source.extension.vsixmanifest
- Project file: ClaudeVS.csproj
- Solution file: ClaudeVS.slnx

## Version History

### v1.0 (Current)
- Initial MVP implementation
- Basic chat interface
- Claude Code CLI integration via subprocess
- View menu integration
- Auto-scroll and loading indicators
