# Global smoke notes (V1)

Architecture uses a low-level keyboard hook + Win32/UIA prefix reads, so many apps *may* work. **V1 acceptance is Notepad only.** Other hosts are best-effort; gaps here do not block the V1 PR.

| App | Prefix read | Caret overlay | Accept inject | Notes |
|---|---|---|---|---|
| **Notepad** (Win10 classic `Edit`) | Expected OK via `WM_GETTEXT` / `EM_GETSEL` | OK via `GetGUIThreadInfo` caret | OK via Unicode `SendInput` | **Acceptance target** |
| **Notepad** (Win11 TextInputHost) | UIA `TextPattern` path | Usually OK | Usually OK | If caret drifts, still dismisses safely |
| **Word** | Often OK via UIA | Caret may be inside document window; DPI/zoom can offset overlay | Accept usually OK | Overlay alignment is the main gap |
| **Edge** (contenteditable / omnibox) | Mixed; page sandbox may hide text | Unreliable caret | Inject may land in wrong place | Treat as unsupported in V1 |
| **WeChat** | Custom controls; UIA often sparse | Likely misaligned / missing | Risky | Skip if password-like; expect gap |
| **Obsidian** (Electron/Chromium) | Similar to Edge | Unreliable | May work in plain editor focus | Record as experimental |

## Manual checklist (Notepad)

1. Start `WinGhostText.exe` → tray shows enabled.
2. Type `environ` → gray `ment`.
3. Tab accepts → `environment`.
4. Type `develop` → gray suffix for `development` (or best-ranked completion).
5. Type `becuase` → spelling GhostText for `because`; Tab replaces.
6. With suggestion visible, Space accepts + space; without suggestion, Space is normal.
7. Esc dismisses; arrows dismiss.
8. Switch to Chinese IME, compose characters → no GhostText.
9. Tray → uncheck Enabled → no suggestions; re-enable → works again.
10. Password field / Windows Terminal → no suggestions.

## Known gaps

- Overlay font matching is best-effort (UIA font attributes are not universal).
- Electron / Chromium caret coordinates are frequently wrong.
- Spelling correction is intentionally conservative (common table first).
- No auto-start (deferred past V1).
- Full App build/publish requires a Windows machine; Linux CI can only test `WinGhostText.Core`.
