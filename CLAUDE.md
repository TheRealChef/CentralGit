# CentralGit — working notes

Native Windows Git client. WinUI 3 / Windows App SDK 2.x, .NET 10, MVVM (CommunityToolkit.Mvvm), git CLI as the engine.

## Build & test

- `dotnet build CentralGit.slnx -m:1` (the app defaults to win-x64; `-p:Platform=ARM64` for ARM)
- `dotnet test CentralGit.slnx -m:1` — includes an end-to-end test that shells out to real `git`
- Run: `src\CentralGit.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CentralGit.exe <repo>`
- Installer: `.\installer\build.ps1 -Version x.y.z` → `artifacts\setup\CentralGit-Setup-x.y.z.exe`. An Inno Setup wizard (`installer\CentralGit.iss`, compiler fetched from NuGet into `artifacts\tools`) around Velopack's Setup (`artifacts\releases`, vpk is a local dotnet tool), which it runs with `--silent --installto <dir>`. Fresh install: choose folder (must be empty). Existing install (HKCU Uninstall\CentralGit DisplayVersion/InstallLocation): version page with Update/Cancel, same folder. Bump the version for every build you hand out. `EnableMsixTooling` must stay on or published builds lose `CentralGit.pri` and crash at startup.
- User data lives in `%APPDATA%\CentralGit` (settings.json, layout.json). `%LOCALAPPDATA%\CentralGit` belongs to the installer and is wiped on uninstall.
- `Program.cs` replaces the XAML-generated Main so `VelopackApp.Build().Run()` runs first.

## Architecture rules

Clean architecture, four projects; the dependency rule is enforced by `tests/CentralGit.Architecture.Tests`.

- `CentralGit.Domain` — pure model and rules (commits, branches, diff, `GraphLayout`, `RepositoryHealth`, `BranchNames`, `FuzzyMatcher`). References nothing but the base library, no I/O.
- `CentralGit.Application` — ports (`Ports/IGitRepository.cs`, `Ports/IAppPorts.cs`) and feature services (`Features/History|Changes|Branches|Sync|Health|Repositories|Accounts`), plus `AppSettings`. No I/O; time comes from `TimeProvider`. `AddApplication()` registers it.
- `CentralGit.Infrastructure` — adapters: git CLI (`Git/`), file system, JSON stores (`Storage/`), Windows Credential Manager. `AddInfrastructure()` registers it.
- `CentralGit.App` — WinUI. Only `App.xaml.cs` (the composition root) may reference `CentralGit.Infrastructure`; view models get services by constructor injection and never start processes or touch files. Use `Microsoft.UI.Xaml.Application` fully qualified — the `CentralGit.Application` namespace hides it.
- Opening a repository gives a `RepositorySession` (History, Changes, Branches, Sync, Health services bound to one `IGitRepository`); `IRepositoryService` and `IAccountService` are app-wide.
- Remote and branch operations are intent: a `GitOperation` record → `IOperationSource.Prepare` → `IPreparedOperation` (command line known up front, `RunAsync` reports `GitProgress` and returns `OperationOutcome` with a failure explanation).
- Tests: `Domain.Tests` (rules), `Application.Tests` (services against fakes in `Fakes.cs`), `Infrastructure.Tests` (real `git`), `Architecture.Tests`.
- All git access goes through `GitRunner`; every `GitResult` keeps args, exit code, stdout, stderr and duration so the UI can show exact diagnostics.
- Parse machine formats only: `--porcelain=v2 -z`, `--format` with `%x1f`/`%x1e` separators. Never parse human output.
- `GitRunner` forces `core.quotePath=false`, `color.ui=false`, `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`. Credentials go through Git Credential Manager — never store tokens ourselves.
- Graph: `GraphLayout` (Domain) assigns lanes; `GraphColorizer` (App) colors by meaning — violet only for the current branch's first-parent line.
- Pages: `ShellViewModel.Page` (`AppPage` History/Changes/Health). Health data comes from `Domain.Health.RepositoryHealth` via `HealthService` (branch verdicts: merged into an integration branch / upstream gone / abandoned after 90 days; main, master, develop, release… are never cleanup candidates). History rows carry `CommitSync` (incoming = orange, not pushed = violet) from the history source's sync sets.
- View models built off the UI thread hold `Windows.UI.Color`, not brushes. Row view models use constructors (XAML type info calls `new()` on types with `required` members).

## Visual language (see `design/Foundations.dc.html`)

- Surfaces by tone: deep #17191D, window #1D2025, surface #23262C, raised #2A2E35, elevated #363B43. Seams #101114 instead of light borders.
- Accents, one meaning each: violet = you/HEAD/selection/primary; orange = needs attention; green = success/additions; teal = trunk/running; red = failure/destructive.
- Text: #EDEBE8 / #ADB1B9 / muted #8B929D (4.5:1 on surface).
- Buttons use our own `GhostButtonStyle` template; WinAppSDK 2.x ignores the classic lightweight-styling brush keys for Button.

## Themes

- `Theming/ThemeColors.cs` is the single source of every color (Dark, Light; custom themes later = another instance). Never hard-code hex in XAML or view models.
- `Themes/Tokens.xaml` declares every brush key as an empty placeholder in its "Dark" and "Light" theme dictionaries; `ThemeService.Initialize` recolors them in place at launch. Adding keys to theme dictionaries from code breaks stock-control lookups — a new brush needs a placeholder in both dictionaries (missing keys land in `%APPDATA%\CentralGit\crash.log`). Note XamlControlsResources also has a "Light" dictionary, and WinUI files "Dark" under "Default".
- XAML uses `{ThemeResource …Brush}` for colors (`StaticResource` only for styles, fonts, templates). Regions: `Root` = page theme; `AppTitleBar`, `Sidebar`, `StatusBar` = chrome theme (dark in the light theme unless Settings › Appearance turns it off). Flyouts opened from chrome get the chrome theme (`ThemeFlyout`).
- View models read `Palette` (= page theme) or `Palette.Chrome` (sidebar, branch picker, operations) when built; a theme switch rebuilds them (`ShellViewModel.RefreshColorsAsync`). Brushes assigned in code-behind use `ThemeService.ContentBrush/ChromeBrush` and are reassigned in `MainWindow.ApplyTheme`.

## Formatting

- `.\format.ps1` formats everything; `.\format.ps1 -Check` is what CI (`.github/workflows/ci.yml`) enforces.
- CSharpier for C# (`.csharpierrc.json`), XamlStyler for tracked XAML (`Settings.XamlStyler`), Prettier for JSON/YAML/Markdown (`.prettierrc.json`, via npm). Width 120. Tools: `dotnet tool restore` and `npm ci`.
- Build with `-m:1`: the XAML compiler runs out of memory in parallel builds.
