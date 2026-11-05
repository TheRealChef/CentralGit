# CentralGit

A native Windows Git client: history-first, fast on large repositories, and explicit about what every Git operation did.

WinUI 3 (Windows App SDK) · C# / .NET 10 · MVVM · the `git` CLI as the engine.

## Run it

```powershell
dotnet build CentralGit.slnx
dotnet test  CentralGit.slnx
src\CentralGit.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CentralGit.exe "D:\path\to\a\repo"
```

Without a path it opens the repository containing the current directory, else the most recently used
repository under `D:\Source\Repos` or `%USERPROFILE%\source\repos`.

Requires Git on `PATH` (2.40+; 2.45+ recommended).

## Layout

| Path                                    | What                                                                                                                         |
| --------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `src/CentralGit.Domain`                 | Pure model and rules: commits, branches, diff, graph lane layout, repository health. No I/O                                  |
| `src/CentralGit.Application`            | Ports (interfaces) and feature services: history, changes, branches, sync, health, repositories                              |
| `src/CentralGit.Infrastructure`         | Adapters: `GitRunner` and git CLI parsers, repository scanning, settings storage, Credential Manager                         |
| `src/CentralGit.App`                    | WinUI 3 shell and view models; `App.xaml.cs` is the composition root (dependency injection)                                  |
| `tests/CentralGit.Domain.Tests`         | Rule tests                                                                                                                   |
| `tests/CentralGit.Application.Tests`    | Service tests against in-memory fakes                                                                                        |
| `tests/CentralGit.Infrastructure.Tests` | Parser tests, plus end-to-end tests against real temp repositories                                                           |
| `tests/CentralGit.Architecture.Tests`   | Enforces the dependency rule (Domain ← Application ← Infrastructure; App uses Infrastructure only at the root)               |
| `design/`                               | The design canvas (`*.dc.html`): History Browse / Investigate / Focus, visual language, Settings, Installer, Commit Composer |

Formatting: `.\format.ps1` (CSharpier, XamlStyler, Prettier); CI fails on unformatted code.

## Roadmap

1. **Skeleton** — done: tokens, window chrome, sidebar, status bar
2. **Git engine** — done: runner, log/status parsing, repo scanning, graph layout
3. **History** — basic version done (graph + refs + virtualized list); next: filter, Win2D graph renderer, keyboard nav
4. **Inspector & diff** — Browse / Investigate / Focus, split + unified diff, syntax highlighting
5. **Operations Center** — fetch/pull/push with live progress and exact diagnostics, Git Credential Manager sign-in
6. **Commit Composer**, **Settings** (persisted scan folders, accounts), **Installer** (Velopack)
