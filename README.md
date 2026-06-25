# ClaudeVS

[Available on the Visual Studio Marketplace for free](https://marketplace.visualstudio.com/items?itemName=GlassBeaver.ClaudeVS)

ClaudeVS brings Claude Code, Codex, Gemini, Copilot, and custom AI coding CLIs into Visual Studio as native, tabbed terminal agents with IDE context on tap.

## Core Features

1. **Debugger MCP integration**

   Let your agent step into the crash with you: ClaudeVS exposes live Visual Studio debugger state through MCP, so Claude or Codex can inspect stack frames, locals, expressions, exceptions, output logs, and breakpoints instead of guessing.

   The bundled `claudevs-debugger` MCP server connects the agent to the matching Visual Studio instance for the current solution. It can report debugger status, threads, call stacks, locals, evaluated expressions, current or captured exceptions, recent Output window lines, breakpoints, and it can select stack frames, terminate debugging, or disconnect its event hooks when finished.

2. **Hotkeyable context delivery**

   Send the agent exactly what it needs without staging a prompt by hand: selected code with file path and line number, task comments, clipboard text, terminal control shortcuts, and full debugger exception packets with stack trace, call stack, current frame, process/thread details, breakpoint info, and recent Debug output.

## More Features

- **Native agent terminals inside Visual Studio** - Run your existing CLI tools in integrated terminal tabs while keeping their normal keyboard behavior.
- **Multiple agents in parallel** - Open separate tabs for separate agents or tasks.
- **QuickSwitch model presets** - Use `Alt+1` through `Alt+4` by default to switch Claude or Codex model presets from the terminal.
- **Custom command support** - Start with presets for Claude, Codex, Gemini, and Copilot, or enter any command you want ClaudeVS to run.
- **Speech input** - Press `Alt+S` by default, speak, and send the recognized text to the active agent.
- **Theme and font controls** - Use Light, Dark, or System theme with adjustable terminal font size.
- **Privacy-conscious design** - ClaudeVS does not store credentials or conversation history; it stores only local extension settings such as command, theme, font size, and QuickSwitch presets.

## Debugger MCP Setup

Open **View -> ClaudeVS**, click **MCP**, then choose **Configure Codex** or **Configure Claude**. ClaudeVS copies the MCP helper to a stable local path, registers `claudevs-debugger` with the selected CLI, and prompts you to restart the current agent.

The same popup also includes:

- **Verify Setup** - Checks the helper file, MCP handshake, active Visual Studio bridge, and Claude/Codex registrations.
- **Copy Setup Commands** - Copies the exact manual `codex mcp add` and `claude mcp add` commands for your machine.

## Sending Context

All commands are rebindable in **Tools -> Options -> Environment -> Keyboard**.

| Command | What it sends |
| --- | --- |
| `View.SendLocationtoAgent` | Active file path, current line number, and selected text. |
| `View.SendTasktoAgent` | The current comment line as a code-generation task for insertion after that line. |
| `View.SendDebuggerExceptiontoAgent` | Exception details, managed stack trace, inner exception, break reason, call stack, current frame, breakpoint info, process/thread info, and the last 10 Debug output lines. |
| `ClaudeVS.AgentAction1` - `ClaudeVS.AgentAction8` | Terminal-scoped key forwarding for agent shortcuts such as `Ctrl+T`, `Ctrl+R`, `Ctrl+O`, `Ctrl+B`, `Alt+V`, `Ctrl+V`, `Alt+T`, and `Ctrl+F`; `Ctrl+V` sends clipboard text using bracketed paste. |
| `ClaudeVS.QuickSwitch1` - `ClaudeVS.QuickSwitch4` | Run QuickSwitch preset 1 through 4. |
| `ClaudeVS.SpeechCommand` | Toggle Windows speech recognition and send recognized text to the active agent. |

## Usage

| Menu Item | Description |
| --- | --- |
| **View -> ClaudeVS** | Open the ClaudeVS agent terminal. |
| **View -> Send Location to Agent** | Send active editor context to the agent. |
| **View -> Send Task to Agent** | Turn the current comment line into an agent task. |
| **View -> Send Exception to Agent** | Send the current debugger exception bundle to the agent. |

## Requirements

- Visual Studio 2022 or newer on Windows.
- At least one supported CLI installed: Claude Code, Codex, Gemini, Copilot, or another command-line agent.
- For debugger MCP, Claude or Codex must be available where ClaudeVS can find it, either on `PATH` or in the expected CLI install location.

## Screenshots

<img width="3840" height="2160" alt="ClaudeVS terminal inside Visual Studio" src="https://github.com/user-attachments/assets/7eb4b818-4e14-40e6-8c1c-172c666c3f66" />

<img width="3840" height="2160" alt="ClaudeVS Visual Studio integration" src="https://github.com/user-attachments/assets/0506ccb0-5136-4ed2-8a97-1da37f925c03" />
