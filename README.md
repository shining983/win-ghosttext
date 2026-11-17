# WinGhostText

Local, offline English **GhostText** completion for Windows — gray suffix near the caret, **Tab** or **Space** to accept, **Esc** to dismiss. No cloud, no AI.

V1 acceptance target: **Notepad**. Architecture is global-input ready (low-level keyboard hook + UI Automation / Win32), with Chinese IME composition gating and a tray toggle.

## Features (V1)

- Local high-frequency English word list (~47k heads) + Trie prefix completion
- Light spelling correction (common typo table + cautious edit distance)
- Transparent GhostText overlay near the caret (best-effort font match)
- **Tab** → accept suffix · **Space** → accept + space (only when a suggestion is shown) · **Esc** → dismiss
- Minimum prefix length **3**
- Hide on blur / selection / focus change / caret leave
- Skip password boxes and common terminal windows
- Pause while IME composition is active (Chinese input safe)
- System tray: enable / disable / exit
- Green self-contained **x64 zip** publish (no installer required)

## Requirements

- Windows 10/11 x64
- For building: .NET 8 SDK **on Windows** (WPF / `net8.0-windows`)
- Core library + unit tests also build on Linux/macOS (`net8.0`)

## Run (dev, Windows)

```powershell
dotnet build .\WinGhostText.sln -c Release
dotnet run --project .\src\WinGhostText.App\WinGhostText.App.csproj -c Release
```

A tray icon appears. Open Notepad, type `environ` — you should see a gray `ment`. Press Tab or Space to accept.

## Publish (green zip)

On a Windows x64 machine with the .NET 8 SDK:

```powershell
.\scripts\publish-zip.ps1
```

Output: `artifacts\WinGhostText-win-x64.zip` (self-contained, single-folder).

Bash equivalent (from Git Bash / WSL calling Windows `dotnet.exe` is not enough — run on Windows):

```powershell
dotnet publish .\src\WinGhostText.App\WinGhostText.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o .\artifacts\win-x64
Compress-Archive -Path .\artifacts\win-x64\* -DestinationPath .\artifacts\WinGhostText-win-x64.zip -Force
```

Unzip anywhere and run `WinGhostText.exe`.

## Keyboard & behavior

| Key | With suggestion | Without suggestion |
|---|---|---|
| Tab | Accept suffix / spelling fix | Normal Tab |
| Space | Accept + insert space | Normal Space |
| Esc | Dismiss overlay | Normal Esc |
| Letters / Backspace | Refresh suggestion | — |
| Arrows / Home / End / Delete | Dismiss | Normal |

## IME notes (Chinese input)

- While an IME **composition string** is active (`ImmGetCompositionString` / GCS_COMPSTR), GhostText is fully suppressed so Chinese typing is not interrupted.
- After composition commits to Latin/English text, normal English prefix completion resumes.
- If GhostText ever interferes with a specific IME, use the tray **Enabled** toggle to pause instantly.

## Word list & licensing

- Embedded list: OpenSubtitles-derived frequencies from [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) (`en_50k`), filtered to ASCII letters. See `THIRD_PARTY_NOTICES.md`.
- **Does not** embed Oxford / New Oxford copyrighted dictionary text or definitions.
- Optional future: load a user-provided `word\trank` file via `WordListLoader.LoadFromFiles` (API ready; no UI in V1).
- Project code: MIT (`LICENSE`).

## Tests

```bash
dotnet test .\tests\WinGhostText.Core.Tests\WinGhostText.Core.Tests.csproj -c Release
```

Covers prefix samples (`environ`→`environment`, `develop`→`development`), misspellings (`becuase`→`because`), and prefix extraction.

## Smoke matrix (non-blocking)

See [`docs/smoke-notes.md`](docs/smoke-notes.md) for Word / Edge / WeChat / Obsidian expectations and known gaps.

## Project layout

```
src/WinGhostText.Core/     Trie, spelling, word list (net8.0)
src/WinGhostText.App/      WPF overlay, hooks, tray (net8.0-windows)
tests/WinGhostText.Core.Tests/
scripts/publish-zip.ps1
```

## Privacy

Runs entirely offline. No telemetry, no network calls from the app.
