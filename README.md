# ClaudeVS

Simple integration of Claude Code CLI into Visual Studio 2026. Works by launching an integrated console window with claude in it, which means Claude retains all of its command line functionality and look & feel while VS is able to communicate with it.
Currently, two actions are implemented:
* Send active file path & line number to claude
* Have claude execute a comment in code, similar to how copilot's tab completion would work but simpler

Nothing is saved by the extension: no credentials, conversations or anything since this is the actual Claude Code CLI program running inside VS.
The project was written entirely by Sonnet 4.5 so it's got lots of useless comments and debug logging.
