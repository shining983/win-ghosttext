# WinGhostText

## 中文说明（怎么用）

**这是什么：** 一款 Windows 本地英文单词「灰色后缀」补全工具——在光标旁显示建议，按 **Tab** 接受，按 **空格** 正常输入空格并结束当前建议，按 **Esc** 取消。完全离线，不联网，不用 AI。

### 运行环境

- Windows 10 / 11（**64 位 x64**）
- **不需要管理员权限**（直接双击运行即可；若系统弹出辅助功能相关提示，按提示允许即可）

### 如何得到可运行的程序

本仓库目前以源码发布。在 **Windows** 电脑上安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 后：

1. 下载或克隆本仓库（也可检出 PR 分支 `cursor/win-ghosttext-v1-3278`）。
2. 在仓库根目录打开 PowerShell，执行：

```powershell
.\scripts\publish-zip.ps1
```

3. 得到压缩包：`artifacts\WinGhostText-win-x64.zip`
4. 解压到任意文件夹，双击运行 **`WinGhostText.exe`**

（绿色包、自包含，解压即用，无需安装程序。）

### 日常使用（记事本验收）

1. 运行 `WinGhostText.exe` → 系统托盘出现图标（默认已开启）。
2. 打开 **记事本（Notepad）**，输入英文，例如 `environ`。
3. 光标旁会出现灰色后缀（如 `ment`）——这就是 GhostText。
4. **Tab**：接受补全。
5. **空格**：始终插入普通空格，并结束/隐藏当前单词建议（**不会**采纳补全）。
6. **Esc**：取消本次建议（不插入空格）。
7. 托盘图标 → 勾选/取消 **Enabled**：随时开启或暂停；需要退出时选 Exit。

提示：至少输入 **3** 个字母才会出现建议，减少打扰。

### 中文输入法说明

用中文输入法打拼音/组字时（正在组合汉字），程序**不会**弹出英文补全，避免干扰中文输入。组字结束后，再输入英文时可正常补全。若仍觉得打扰，用托盘关掉 Enabled 即可。

### 已知限制

- **已重点验证：记事本。** 其他软件（Word、浏览器、微信、Obsidian 等）为尽力支持，光标位置或注入可能不准。
- 密码框、常见终端窗口默认不补全。
- 本 Linux 云环境无法演示 WPF 界面；请在 Windows 本机运行体验。

### 隐私与词库

- 全程离线，无遥测、无联网。
- 内置常用英文词频表（非牛津等版权词典正文）。详见 `THIRD_PARTY_NOTICES.md`。代码许可：MIT。

---

## English

**What it is:** Local, offline English **GhostText** for Windows — a gray suffix near the caret; **Tab** accepts, **Space** inserts a normal space and ends the current suggestion, **Esc** dismisses. No cloud, no AI.

### Requirements

- Windows 10/11 **x64**
- **No admin required** (`asInvoker`). Allow any Accessibility prompt if Windows asks.
- To *build* the zip: .NET 8 SDK on Windows

### Get a runnable build

```powershell
git clone https://github.com/shining983/win-ghosttext.git
cd win-ghosttext
# optional: git checkout cursor/win-ghosttext-v1-3278
.\scripts\publish-zip.ps1
```

Unzip `artifacts\WinGhostText-win-x64.zip` anywhere and run **`WinGhostText.exe`**.

Dev run (instead of zip):

```powershell
dotnet build .\WinGhostText.sln -c Release
dotnet run --project .\src\WinGhostText.App\WinGhostText.App.csproj -c Release
```

### Daily use

1. Start `WinGhostText.exe` → tray icon (enabled by default).
2. Open **Notepad**, type e.g. `environ` → gray `ment`.
3. **Tab** accepts; **Space** always inserts a normal space and dismisses the current word suggestion (does **not** accept); **Esc** dismisses without inserting a space.
4. Tray → **Enabled** on/off; **Exit** to quit.

Minimum prefix length: **3**.

### Chinese IME

While IME **composition** is active, GhostText is fully suppressed so Chinese typing is not interrupted. After commit, English completion resumes. Use the tray toggle if needed.

### Known limits

- **Notepad** is the V1 acceptance target; Word / Edge / WeChat / Obsidian are best-effort (see [`docs/smoke-notes.md`](docs/smoke-notes.md)).
- Password boxes and common terminals are skipped.
- Linux CI / cloud VMs cannot demo the WPF overlay.

### Features (V1)

- ~47k-head frequency word list + Trie prefix completion
- Light spelling correction (common typos + cautious edit distance)
- Transparent overlay (best-effort font match)
- Hide on blur / selection / focus change / caret leave
- Self-contained win-x64 zip (no installer)

| Key | With suggestion | Without suggestion |
|---|---|---|
| Tab | Accept suffix / spelling fix | Normal Tab |
| Space | Insert normal space + dismiss suggestion | Normal Space |
| Esc | Dismiss overlay | Normal Esc |

### Word list & license

- Frequencies from [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) (`en_50k`), ASCII-filtered — see `THIRD_PARTY_NOTICES.md`.
- Does **not** embed Oxford / New Oxford copyrighted text.
- Project code: MIT (`LICENSE`). Offline; no telemetry.

### Tests & layout

```bash
dotnet test .\tests\WinGhostText.Core.Tests\WinGhostText.Core.Tests.csproj -c Release
```

```
src/WinGhostText.Core/     Trie, spelling, word list (net8.0)
src/WinGhostText.App/      WPF overlay, hooks, tray (net8.0-windows)
tests/WinGhostText.Core.Tests/
scripts/publish-zip.ps1
```
