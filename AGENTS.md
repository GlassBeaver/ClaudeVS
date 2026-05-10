# AGENTS.md

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

## Terminal Project (c:\work\terminal)

ClaudeVS embeds the Windows Terminal WPF control. The project references two DLLs built from the terminal source:

- `Microsoft.Terminal.Control.dll` (native C++ DLL with flat C ABI exports) — referenced as Content from `terminal\bin\x64\{Config}\Microsoft.Terminal.Control\`
- `Microsoft.Terminal.Wpf.dll` (managed C# wrapper) — referenced as an assembly from `terminal\bin\AnyCPU\{Config}\WpfTerminalControl\net472\`

Both Debug and Release configurations are referenced via conditions in the csproj, so both must be built.

### Building the Terminal

The terminal must be built with **VS 2022's MSBuild and toolchain** (not VS 18), because the native C++ code and prebuilt libraries use the v143 platform toolset.

Both batch files accept an optional configuration parameter (defaults to Debug).

**Step 1 — Build the native TerminalControl DLL:**
```bash
c:\work\terminal\build_control.bat Debug
c:\work\terminal\build_control.bat Release
```
This batch file:
1. Initializes the VS 2022 x64 build environment via `vcvarsall.bat`
2. Runs MIDL to generate `ITerminalHandoff.h` into `obj\x64\{Config}\OpenConsoleProxy\`
3. Builds `TerminalControl.vcxproj` (outputs to `bin\x64\{Config}\Microsoft.Terminal.Control\`)

**Step 2 — Build the managed WPF wrapper:**
```bash
c:\work\terminal\build_wpf.bat Debug
c:\work\terminal\build_wpf.bat Release
```
This builds `WpfTerminalControl.csproj` (outputs to `bin\x64\{Config}\WpfTerminalControl\net472\`).

### After Building the Terminal

Building ClaudeVS automatically picks up the DLLs from the terminal output directories via the csproj references. However, if the VS experimental instance already has a cached copy of the extension, you must also copy the updated DLLs into the extension deployment directory:

```
c:\users\sirse\appdata\local\microsoft\visualstudio\18.0_cec03a6aExp\Extensions\GlassBeaver\ClaudeVS - Claude Code Integration\4.2\
```

Copy both `Microsoft.Terminal.Control.dll` and `Microsoft.Terminal.Wpf.dll` there, then restart the VS experimental instance.

### Architecture

The WPF terminal control uses a flat C ABI to call into the native DLL. Adding new native methods requires changes in all layers:
1. `HwndTerminal.hpp` / `HwndTerminal.cpp` — declare and implement the extern "C" function
2. `Microsoft.Terminal.Control.def` — add the export
3. `NativeMethods.cs` — add the P/Invoke declaration
4. `TerminalContainer.cs` — add an internal wrapper
5. `TerminalControl.xaml.cs` — add the public method