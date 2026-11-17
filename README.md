# WinGhostText

[![License: PolyForm Noncommercial 1.0.0](https://img.shields.io/badge/License-PolyForm%20Noncommercial%201.0.0-blue.svg)](https://polyformproject.org/licenses/noncommercial/1.0.0)

Windows 10/11 托盘轻量**英文**补全：本地词频词典，无 AI、全程离线。光标旁**气泡**显示整词建议；词结束后可给出拼写/语法/大小写/专有名词标注。

---

## 使用方法

1. 运行 **`WinGhostText.exe`**（桌面快捷方式或解压目录均可）→ 系统托盘出现图标（默认开启）。
2. 托盘勾选/取消 **Enabled** 随时开关；**Exit** 退出。
3. 在常见应用里输入英文（至少约 3 个字母）→ 光标附近出现建议气泡（优先在下方）。
4. **词结束标注**：空格/标点结束一个词后，若拼写、轻量语法、句首/专有名词大小写有建议 → 短下划线 + 共用气泡。

### 按键

| 键 | 有建议时 | 无建议时 |
|---|---|---|
| **Tab** | 采纳补全 / 词结束纠正（替换错词跨度） | 正常 Tab |
| **空格** | 插入普通空格并结束当前建议（**不会**采纳）；随后可触发词结束检查 | 正常空格 |
| **Esc** | 关闭建议 | 正常 Esc |

补全 vs 纠正：打字中（词未结束）补全优先；空格/标点结束词后纠正优先。面向**英文打字**场景；密码框与终端会跳过。

---

## 技术栈

| 层 | 说明 |
|---|---|
| UI / 宿主 | **.NET 8 WPF** 托盘应用 + 光标旁建议气泡 |
| 按键 | **WH_KEYBOARD_LL** 低级键盘钩子；采纳时用 **SendInput** 注入 |
| 光标定位 | **UIA / GUI** caret 探测（多应用） |
| 补全 | **Trie + 词频词典**（本地嵌入） |
| 拼写 | 本地 **SpellingCorrector**（常见错词表 + 谨慎 **Damerau** 编辑距离，含字母换位） |
| 语法 | 本地规则级（a/an、轻量主谓/双情态、句首大写、`i`→`I`、专有名词大小写） |
| 发布 | `scripts/publish-zip.ps1` → 自包含 **win-x64** zip |

```
src/WinGhostText.Core/     Trie、拼写、语法规则、词库 (net8.0)
src/WinGhostText.App/      WPF 气泡、钩子、托盘 (net8.0-windows)
tests/WinGhostText.Core.Tests/
scripts/publish-zip.ps1
```

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。不需要管理员权限。

```powershell
git clone https://github.com/shining983/win-ghosttext.git
cd win-ghosttext
.\scripts\publish-zip.ps1
```

得到 `artifacts\WinGhostText-win-x64.zip`。解压后运行 **`WinGhostText.exe`**。

```powershell
dotnet build .\WinGhostText.sln -c Release
dotnet run --project .\src\WinGhostText.App\WinGhostText.App.csproj -c Release
dotnet test .\tests\WinGhostText.Core.Tests\WinGhostText.Core.Tests.csproj -c Release
```

词库与第三方说明见 [`ATTRIBUTION.md`](ATTRIBUTION.md)、[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

---

## 注意事项

请按现状理解，不必当成「完美」产品：

- **Win11 记事本**：气泡补全仍可能不稳定（与 `InputSite` 等路径有关）。其它应用一般正常。
- **中文输入法**：中文模式下仍可能出现英文补全气泡。嫌吵可用托盘关掉 Enabled。
- **语法**：仅规则级，**不是**完整校对；无联网语法 API。
- **密码框 / 终端**：应跳过（如 Windows Terminal、cmd、PowerShell）。
- **全程离线**：无遥测、无云端词典/语法；仅本地词频 + 扩展词表 + 小专有名词表。
- **网页复杂编辑区**：可能半支持；气泡贴光标附近即可，不做正文字号/基线像素级对齐。

更多兼容细节见 [`docs/smoke-notes.md`](docs/smoke-notes.md)（部分条目可能偏旧；以本 README 为准）。

---

## 可改进

- 更好的 **Win11 记事本 / WinUI** caret 与气泡稳定性
- 更强的本地语法规则（或日后可选对接 LanguageTool 等，仍保持默认可离线）
- 扩充专有名词与领域词表
- **DPI / 多显示器** 下气泡定位更稳
- 更精细的 IME 状态检测（减少中文组词时的干扰）
- 复杂 `contenteditable` / 网页编辑器兼容

---

## License

本项目源码以 **[PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0)** 提供：

- **允许**：非商业用途下查看、使用、修改、分发（见许可证全文）
- **禁止**：商业用途（Commercial use is prohibited under this license）

完整条款见 [`LICENSE`](LICENSE)。

**说明**：仓库早期提交曾使用 MIT；历史提交仍按当时 MIT 适用。自本仓库采用 PolyForm Noncommercial 1.0.0 起，**现行 `LICENSE` 约束后续发布与新贡献**。第三方嵌入数据（词频等）仍按其各自许可，见 `THIRD_PARTY_NOTICES.md`。

---

## English (short)

Lightweight offline English completion for Windows 10/11: local frequency dict, no AI, tray app. Suggestions show in a caret-side bubble. Run `WinGhostText.exe` or a desktop shortcut; tray **Enabled** toggles.

**Tab** accepts; **Space** is always a normal space (does **not** accept); **Esc** dismisses. After Space/punctuation ends a word, local marks can underline a bad span (spelling / light grammar / caps / proper nouns) in the same bubble. Grammar is rule-level only — not full proofreading.

**Stack:** .NET 8 WPF, WH_KEYBOARD_LL, SendInput, UIA/GUI caret, Trie+freq, local SpellingCorrector/Damerau, local grammar rules; `publish-zip.ps1` win-x64.

**License:** [PolyForm Noncommercial 1.0.0](LICENSE) — source available / open for non-commercial use; commercial use prohibited. Earlier git history was MIT; current `LICENSE` governs going forward.
