# CentralGit — working notes

Native Windows Git client. WinUI 3 / Windows App SDK 2.x, .NET 10, MVVM (CommunityToolkit.Mvvm), git CLI as the engine.

## Build & test
- `dotnet build CentralGit.slnx` (the app defaults to win-x64; `-p:Platform=ARM64` for ARM)
- `dotnet test CentralGit.slnx` — includes an end-to-end test that shells out to real `git`
- Run: `src\CentralGit.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CentralGit.exe <repo>`
- Installer: `.\installer\build.ps1 -Version x.y.z` → `artifacts\setup\CentralGit-Setup-x.y.z.exe`. An Inno Setup wizard (`installer\CentralGit.iss`, compiler fetched from NuGet into `artifacts\tools`) around Velopack's Setup (`artifacts\releases`, vpk is a local dotnet tool), which it runs with `--silent --installto <dir>`. Fresh install: choose folder (must be empty). Existing install (HKCU Uninstall\CentralGit DisplayVersion/InstallLocation): version page with Update/Cancel, same folder. Bump the version for every build you hand out. `EnableMsixTooling` must stay on or published builds lose `CentralGit.pri` and crash at startup.
- User data lives in `%APPDATA%\CentralGit` (settings.json, layout.json). `%LOCALAPPDATA%\CentralGit` belongs to the installer and is wiped on uninstall.
- `Program.cs` replaces the XAML-generated Main so `VelopackApp.Build().Run()` runs first.

## Architecture rules
- `CentralGit.Core` has no UI references. All git access goes through `GitRunner`; every result keeps args, exit code, stdout, stderr and duration so the UI can show exact diagnostics.
- Parse machine formats only: `--porcelain=v2 -z`, `--format` with `%x1f`/`%x1e` separators. Never parse human output.
- `GitRunner` forces `core.quotePath=false`, `color.ui=false`, `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`. Credentials go through Git Credential Manager — never store tokens ourselves.
- Graph: `GraphLayout` (Core) assigns lanes; `GraphColorizer` (App) colors by meaning — violet only for the current branch's first-parent line.
- Pages: `ShellViewModel.Page` (`AppPage` History/Changes/Health). Health data comes from `Core.Insights.RepositoryHealth` (branch verdicts: merged into an integration branch / upstream gone / abandoned after 90 days; main, master, develop, release… are never cleanup candidates). History rows carry `CommitSync` (incoming = orange, not pushed = violet) from `GitRepository.GetSyncSetsAsync`.
- View models built off the UI thread hold `Windows.UI.Color`, not brushes. Row view models use constructors (XAML type info calls `new()` on types with `required` members).

## Visual language (see `design/Foundations.dc.html`)
- Surfaces by tone: deep #17191D, window #1D2025, surface #23262C, raised #2A2E35, elevated #363B43. Seams #101114 instead of light borders.
- Accents, one meaning each: violet = you/HEAD/selection/primary; orange = needs attention; green = success/additions; teal = trunk/running; red = failure/destructive.
- Text: #EDEBE8 / #ADB1B9 / muted #8B929D (4.5:1 on surface).
- Buttons use our own `GhostButtonStyle` template; WinAppSDK 2.x ignores the classic lightweight-styling brush keys for Button.
