# CentralGit — working notes

Native Windows Git client. WinUI 3 / Windows App SDK 2.x, .NET 10, MVVM (CommunityToolkit.Mvvm), git CLI as the engine.

## Build & test
- `dotnet build CentralGit.slnx` (the app defaults to win-x64; `-p:Platform=ARM64` for ARM)
- `dotnet test CentralGit.slnx` — includes an end-to-end test that shells out to real `git`
- Run: `src\CentralGit.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CentralGit.exe <repo>`

## Architecture rules
- `CentralGit.Core` has no UI references. All git access goes through `GitRunner`; every result keeps args, exit code, stdout, stderr and duration so the UI can show exact diagnostics.
- Parse machine formats only: `--porcelain=v2 -z`, `--format` with `%x1f`/`%x1e` separators. Never parse human output.
- `GitRunner` forces `core.quotePath=false`, `color.ui=false`, `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`. Credentials go through Git Credential Manager — never store tokens ourselves.
- Graph: `GraphLayout` (Core) assigns lanes; `GraphColorizer` (App) colors by meaning — violet only for the current branch's first-parent line.
- View models built off the UI thread hold `Windows.UI.Color`, not brushes. Row view models use constructors (XAML type info calls `new()` on types with `required` members).

## Visual language (see `design/Foundations.dc.html`)
- Surfaces by tone: deep #17191D, window #1D2025, surface #23262C, raised #2A2E35, elevated #363B43. Seams #101114 instead of light borders.
- Accents, one meaning each: violet = you/HEAD/selection/primary; orange = needs attention; green = success/additions; teal = trunk/running; red = failure/destructive.
- Text: #EDEBE8 / #ADB1B9 / muted #8B929D (4.5:1 on surface).
- Buttons use our own `GhostButtonStyle` template; WinAppSDK 2.x ignores the classic lightweight-styling brush keys for Button.
