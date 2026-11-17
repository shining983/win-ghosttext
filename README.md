# WinGhostText

轻量离线英文补全——本地词库、无 AI、系统托盘常驻。面向 Windows 10 / 11 x64。建议以**光标旁输入法式气泡**显示（整词），不再叠灰色后缀到正文里。

## 怎么用

1. 运行已发布的 **`WinGhostText.exe`**（桌面快捷方式或解压目录均可）→ 托盘出现图标（默认开启）。
2. 在记事本、浏览器、Word 等常见应用里输入英文（至少 **3** 个字母）→ 光标附近出现建议气泡（优先在光标下方）。
3. 托盘勾选/取消 **Enabled** 可随时开关；**Exit** 退出。下次再开快捷方式或 `WinGhostText.exe` 即可。

### 按键

| 键 | 有建议时 | 无建议时 |
|---|---|---|
| **Tab** | **仅 Tab 采纳**补全 / 拼写纠正 | 正常 Tab |
| **空格** | 插入普通空格并结束建议（**不会**采纳） | 正常空格 |
| **Esc** | 取消建议 | 正常 Esc |

### 中文输入法

补全气泡 **只在英文模式**下出现，不干扰中文组字（快捷键与 IME 规则不变）：

- 微信输入法 **Shift → 中文**：不补全；组字过程中也不会弹出。
- **Shift → 英文**：补全立即恢复。
- 仍觉得打扰时，用托盘关掉 Enabled。

### 拼写纠正

轻量纠错（常见错词表 + 谨慎编辑距离），含相邻字母颠倒，例如 `becuase` → `because`。Tab 会替换已输入前缀为正确词。

### 作用范围

全局尽力支持常见应用（记事本、浏览器、Word 等）。密码框、常见终端（如 Windows Terminal / cmd / PowerShell）会跳过。兼容细节见 [`docs/smoke-notes.md`](docs/smoke-notes.md)。

### 隐私

全程离线，无遥测。内置英文词频表（非牛津等版权词典正文）。见 `THIRD_PARTY_NOTICES.md`。代码：MIT。

---

## Build / publish（.NET 8，win-x64）

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。不需要管理员权限。

```powershell
git clone https://github.com/shining983/win-ghosttext.git
cd win-ghosttext
.\scripts\publish-zip.ps1
```

得到 `artifacts\WinGhostText-win-x64.zip`（自包含绿色包）。解压后运行 **`WinGhostText.exe`**，可自行建桌面快捷方式。

开发运行：

```powershell
dotnet build .\WinGhostText.sln -c Release
dotnet run --project .\src\WinGhostText.App\WinGhostText.App.csproj -c Release
```

测试：

```bash
dotnet test .\tests\WinGhostText.Core.Tests\WinGhostText.Core.Tests.csproj -c Release
```

```
src/WinGhostText.Core/     Trie、拼写、词库 (net8.0)
src/WinGhostText.App/      WPF 建议气泡、钩子、托盘 (net8.0-windows)
tests/WinGhostText.Core.Tests/
scripts/publish-zip.ps1
```

---

## English (short)

Offline English completion for Windows 10/11: local dict, no AI, tray app. Suggestions appear in a small **IME-like bubble** near the caret (full word; prefers below the caret). Run `WinGhostText.exe` (or a desktop shortcut). **Tab** accepts only; **Space** inserts a normal space and dismisses (does **not** accept); **Esc** dismisses. Completions only in **English IME mode** (WeChat IME: Shift to Chinese → off; Shift to English → on; never during Chinese composition). Light spelling includes letter transposition (`becuase` → `because`). Works across common apps; skips password fields and terminals. Publish: `.\scripts\publish-zip.ps1` → self-contained win-x64 zip.
