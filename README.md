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

| Path | What |
|---|---|
| `src/CentralGit.Core` | UI-free engine: `GitRunner` (process + streaming), parsers for `git log` / `status --porcelain=v2`, `RepositoryScanner`, `GraphLayout` (lane assignment) |
| `src/CentralGit.App` | WinUI 3 shell: custom title bar, sidebar, virtualized history with graph, status bar |
| `tests/CentralGit.Core.Tests` | Parser and graph unit tests, plus an end-to-end test against a real temp repository |
| `design/` | The design canvas (`*.dc.html`): History Browse / Investigate / Focus, visual language, Settings, Installer, Commit Composer |

## Roadmap

1. **Skeleton** — done: tokens, window chrome, sidebar, status bar
2. **Git engine** — done: runner, log/status parsing, repo scanning, graph layout
3. **History** — basic version done (graph + refs + virtualized list); next: filter, Win2D graph renderer, keyboard nav
4. **Inspector & diff** — Browse / Investigate / Focus, split + unified diff, syntax highlighting
5. **Operations Center** — fetch/pull/push with live progress and exact diagnostics, Git Credential Manager sign-in
6. **Commit Composer**, **Settings** (persisted scan folders, accounts), **Installer** (Velopack)
