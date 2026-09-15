# Proposal: manual-clear-and-autopaste

## Why

抓到房间码后剪贴板里会留码长达 `ClipboardClearSeconds`（默认 180 秒），期间若用户误触发粘贴会把旧码带进别处，且当前没有任何手动清除入口，只能等自动清空。同时用户抓码的目的就是粘贴：码出现在主播画面 → 程序复制 → 用户手动切到游戏房间码输入框按 Ctrl+V，这一步每次都重复。把"手动清空"做成按钮/托盘入口、把"粘贴"自动化，分别消除"误粘旧码"的风险和"手动按粘贴"的重复操作。

## What Changes

- **手动清空剪贴板**：主窗体新增"清空剪贴板"按钮，托盘右键菜单新增同名菜单项，点击立即调用已有的 `ClipboardService.Clear()` 并在 UI 上给出反馈。
- 与现有自动清空**共存**：`ClipboardClearSeconds` 延迟清空逻辑不变，设为 0 即纯手动模式。
- **抓码后自动粘贴**：新增配置开关 `AutoPasteEnabled`（默认关）与延迟 `AutoPasteDelayMs`（默认 0）。启用后，`FireIfTriggered` 写入剪贴板成功后，按延迟向**当前拥有键盘焦点的窗口**合成一次 Ctrl+V。
- 合成按键用 `SendInput` + scancode 实现（不用 `SendKeys`，虚拟键消息对游戏窗口兼容性差）。粘贴后不跟随回车。
- 自动粘贴跟随现有同码冷却（`_detector`），不会重复粘贴；主窗体新增对应复选框与延迟输入框。

## Capabilities

### New Capabilities

- `clipboard-manual-clear`: 手动清空剪贴板的用户入口（主窗体按钮 + 托盘菜单项）、立即清空行为、与自动清空策略的共存关系及清空反馈。
- `auto-paste`: 抓到房间码并写入剪贴板后，向当前焦点窗口自动粘贴的行为：开关与延迟配置、SendInput 合成 Ctrl+V、同码冷却下不重复粘贴。

### Modified Capabilities

（无——项目当前 `openspec/specs/` 为空，无既有能力的需求变更。）

## Impact

- **代码**：
  - `RoomCodeGrabber/MainForm.cs` — 新增按钮、托盘菜单项、自动粘贴开关/延迟控件。
  - `RoomCodeGrabber/AppConfig.cs` — 新增 `AutoPasteEnabled`、`AutoPasteDelayMs` 配置项及持久化。
  - `RoomCodeGrabber/CaptureEngine.cs` — `FireIfTriggered` 写剪贴板后触发自动粘贴。
  - `RoomCodeGrabber/ClipboardService.cs` — 已有 `Clear()` 复用；可能需暴露"最近写入"状态供反馈。
  - 新文件：`SendInput` 键盘合成封装（如 `KeyboardInjector.cs`）。
- **测试**：`LogicTests` 中为可纯逻辑部分（配置默认值、粘贴时机判定）补单测；SendInput 本体需真机验证。
- **已知风险（需真机验证）**：无畏契约 Vanguard 可能拦截发往游戏窗口的合成输入，游戏内粘贴可能不生效；游戏外普通输入框不受影响。此风险写入 spec 的假设与设计约束，不阻塞实现。
