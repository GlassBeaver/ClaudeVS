# CLAUDE.md

## Code Conventions

Don't add new comments anywhere. Don't remove existing comments unless instructed to do so.
Don't add log statements unless explicitly told to.
Don't Rebuild the project, just Build it.
Don't ever try to uninstall or install VS extensions.

## Project Overview

ClaudeVS is a Visual Studio 2026 extension that integrates Claude Code CLI into the IDE.

## Build Command
```bash
"C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" "c:\Work\ClaudeVS\ClaudeVS.csproj" -t:Build -p:Configuration=Debug -v:minimal
```