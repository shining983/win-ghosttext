# WinGhostText

Windows 10/11 托盘轻量**英文**补全：本地词频词典，无 AI、全程离线。建议以光标旁**气泡**显示整词（不是正文里的灰色阴影叠字），避免基线漂移。

## 特点（当前可用）

- **UI**：光标旁输入法式气泡；贴光标附近即可，不追求与正文字号像素级对齐
- **按键**：**Tab** 采纳；**空格** 始终是普通空格（不采纳）；**Esc** 关闭建议
- **拼写**：轻量纠错（常见错词 + 谨慎编辑距离），含字母换位，如 `becuase` → `because`
- **多应用**：浏览器、微信、Word、Obsidian 等常见场景一般可用
- **托盘**：可随时开关 Enabled；桌面快捷方式或解压目录运行 `WinGhostText.exe`
- **构建**：.NET 8 win-x64，`scripts/publish-zip.ps1`

## 怎么用

1. 运行 **`WinGhostText.exe`**（桌面快捷方式或解压目录均可）→ 系统托盘出现图标（默认开启）。
2. 在常见应用里输入英文（至少 **3** 个字母）→ 光标附近出现建议气泡（优先在下方）。
3. 托盘勾选/取消 **Enabled** 随时开关；**Exit** 退出。下次再开快捷方式或 exe 即可。

### 按键

| 键 | 有建议时 | 无建议时 |
|---|---|---|
| **Tab** | 采纳补全 / 拼写纠正 | 正常 Tab |
| **空格** | 插入普通空格并结束建议（**不会**采纳） | 正常空格 |
| **Esc** | 关闭建议 | 正常 Esc |

## 已知问题 / 局限

请按现状理解，不必当成「完美」产品：

- **Win11 记事本**：气泡补全仍不稳定、经常不可用（与 `InputSite` 等路径有关）。其它应用一般正常。有未合并修复分支 [PR #10](https://github.com/shining983/win-ghosttext/pull/10)，当前**不强制合并**。
- **中文输入法**：中文模式下仍可能出现英文补全气泡（用户可接受）。理想上应尽量不干扰组词，但现状如此；嫌吵可用托盘关掉 Enabled。
- **密码框 / 终端**：应跳过（如 Windows Terminal、cmd、PowerShell）。
- **网页复杂编辑区**：可能半支持（地址栏等短框更稳；复杂 `contenteditable` 可能读不到词或气泡错位）。
- **气泡位置**：贴光标附近即可，不做正文字号/基线像素级对齐。

更多兼容细节见 [`docs/smoke-notes.md`](docs/smoke-notes.md)（部分条目可能偏旧；以本 README 对产品现状的描述为准）。

## 隐私

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

Lightweight offline English completion for Windows 10/11: local frequency dict, no AI, tray app. Suggestions show in a **caret-side bubble** (full word — not in-line gray ghost overlay). Run `WinGhostText.exe` or a desktop shortcut; tray **Enabled** toggles.

**Tab** accepts; **Space** is always a normal space (does **not** accept); **Esc** dismisses. Light spelling includes letter transposition (`becuase` → `because`). Works in common apps (browser, WeChat, Word, Obsidian, etc.).

**Honest limits:** Win11 Notepad bubble is still unstable / often unavailable (`InputSite`, etc.; other apps usually OK). Open fix: [PR #10](https://github.com/shining983/win-ghosttext/pull/10) — **not** required to merge yet. Chinese IME mode may still show English bubbles (accepted). Password fields / terminals are skipped; complex web editors may be half-supported. Bubble sits near the caret; no pixel-perfect font/baseline match.

Publish: `.\scripts\publish-zip.ps1` → self-contained win-x64 zip.
