# 全局兼容冒烟笔记 / Global smoke notes

## 为什么感觉「只有记事本能用」？

V1 **验收目标就是记事本**（经典 `Edit` / Win11 `RichEditD2DPT`）。架构虽是全局键盘钩子 + Win32/UIA，但补全能不能出来取决于三件事同时成立：

1. **前缀读取**（caret 前的英文单词）— `PrefixExtractor`
2. **光标屏幕坐标**（建议气泡锚定）— `FocusInfo` / UIA bounding rect
3. **采纳注入**（Tab → `SendInput` Unicode）— `TextInjector`

记事本三条路径都成熟；Word / 浏览器 / 微信 / Obsidian 往往在 1 或 2 上失败，看起来就像「没反应」或「气泡飞到别处」。

本轮已加强 UIA：`TextPattern` 按 **caret 前文本** 取前缀、FocusedElement / 浅层树搜索、UIA caret 矩形回退、RichEdit `EM_EXGETSEL`。记事本 Win32 路径保持优先，避免回归。

---

## 应用兼容预期（诚实表）

| App | Prefix 前缀 | Caret 气泡位置 | Accept 注入 | 预期 |
|---|---|---|---|---|
| **Notepad** Win10 经典 `Edit` | ✅ `WM_GETTEXT` + `EM_GETSEL` | ✅ `GetGUIThreadInfo` | ✅ Unicode `SendInput` | **验收目标 / 应稳定** |
| **Notepad** Win11 `RichEditD2DPT` | ✅ Win32 `EM_EXGETSEL` 或 UIA | ✅ GUI caret 或 UIA rect | ✅ | **应可用**（本轮 RichEdit 选区修复） |
| **Word** | 🟡 常可用（UIA TextPattern） | 🟡 缩放/DPI 可能偏移 | 🟡 一般可插入 | **实验性**：补全常有，对齐可能偏 |
| **Edge/Chrome 地址栏** | 🟡 ValuePattern 偶发可用 | 🟡 | 🟡 | **半支持**：短输入框更可能 |
| **Edge/Chrome 网页编辑区** | 🔴 沙箱/无障碍树常读不到词 | 🔴 caret 常错 | 🔴 可能打到错误焦点 | **V1 不支持** |
| **微信** 聊天输入 | 🔴 自绘控件，UIA 稀疏 | 🔴 | 🔴 有风险 | **缺口**：默认当失败 |
| **Obsidian**（Electron） | 🟡 焦点在编辑器时偶尔可读 | 🔴 Chromium caret 常错 | 🟡 注入有时成功 | **实验性**：可能「能补全但气泡错位」 |
| Windows Terminal / 密码框 | ⏭ 主动跳过 | — | — | **设计如此** |

图例：✅ 预期可用 · 🟡 不稳定/实验 · 🔴 多半失败 · ⏭ 跳过

---

## 本轮代码改动（多应用）

- **UIA 前缀**：用 document→caret 的文本 + `WordPrefix`，避免 `ExpandToEnclosingUnit` 把 caret **后面** 的字母算进前缀。
- **元素解析**：`FromHandle` → `FocusedElement` → 浅层 `TextPattern` 子树（Electron/复合窗体）。
- **Caret**：GUI caret → `GetCaretPos` → **UIA `GetBoundingRectangles`** → `EM_GETRECT`。
- **RichEdit**：`EM_EXGETSEL`（32-bit），避免长文档 caret 被 `EM_GETSEL` 截断。
- **密码框**：额外读 UIA `IsPassword`。
- **SendInput x64**：`INPUT` union 补齐到 40 字节（否则 Tab 采纳在 win-x64 上 Error 87）。
- **建议气泡**：caret 下方优先锚定（空间不足则上方）；显示整词；浅/深色面板；点击穿透、不抢焦点。不再做 FormattedText 基线叠字。
- **Space**：有建议时 Space 插入普通空格并取消建议（**不采纳**）；仅 Tab 采纳。

---

## 记事本手动清单（验收）

1. Start `WinGhostText.exe` → tray shows enabled.
2. Type `environ` → bubble shows `environment` near the caret.
3. Tab accepts → `environment`.
4. Type `develop` → bubble for `development` (or best-ranked completion).
5. Type `becuase` → spelling bubble for `because`; Tab replaces.
6. With suggestion visible, Space inserts a normal space and dismisses (does **not** accept); without suggestion, Space is normal.
7. Esc dismisses; arrows dismiss.
8. Switch to Chinese IME, compose characters → no bubble.
9. Tray → uncheck Enabled → no suggestions; re-enable → works again.
10. Password field / Windows Terminal → no suggestions.

## 仍需真实 Windows 桌面验证

- [ ] Win10 / Win11 记事本回归（前缀 + 气泡贴 caret + Tab/Space）
- [ ] Word：键入 `environ`，看气泡是否贴近 caret、Tab 是否插入
- [ ] Edge 地址栏 vs 网页 `contenteditable`（预期后者仍差）
- [ ] Obsidian 编辑器：能否出建议；气泡是否错位
- [ ] 微信输入框：确认仍失败且不误注入
- [ ] 125%/150% DPI 下气泡位置；底部行是否翻到光标上方

Linux CI 只能跑 `WinGhostText.Core` 单测；完整 App 需 Windows。

## 已知缺口（未在本轮解决）

- Chromium/Electron caret 坐标经常错误（需专用策略或放弃气泡）。
- Bubble font is fixed / lightly scaled (no pixel-perfect host face match).
- Spelling correction is intentionally conservative (common table first).
- No auto-start (deferred past V1).
