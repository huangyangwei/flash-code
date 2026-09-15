# Design: manual-clear-and-autopaste

## Context

- 抓码链路已收敛到 `CaptureEngine.FireIfTriggered`：判定 → `ClipboardService.SetTextWithAutoClear` → `CodeGrabbed` 事件 → UI（`MainForm.OnCodeGrabbed`）。变化帧与静止帧共走这一条路径。
- `ClipboardService` 已具备 `Clear()`（彻底清空、不留文本格式）与后台清空定时器；`ClipboardClearPolicy.ShouldClear` 保证只清程序自己写入的内容（fail-safe）。
- UI 全部在 `MainForm` 构造函数中以绝对布局手工搭建；托盘已有 `ContextMenuStrip`（框选/开关/退出）。
- 配置持久化模式：`AppConfig` 属性 + 控件 `ValueChanged/CheckedChanged` 中 `_config.Save()`。
- 全部 WinForms 主线程 + 抓帧后台线程；`CodeGrabbed` 经 `BeginInvoke` 回 UI。

## Goals / Non-Goals

**Goals:**
- 手动清空入口（按钮 + 托盘项）与自动清空共存，行为可预期（清空幂等、不干扰定时器）。
- 自动粘贴对游戏窗口最大兼容（SendInput + scancode），不阻塞抓帧循环，跟随同码冷却。
- 两个功能均可配置、默认不影响现有行为（自动粘贴默认关）。

**Non-Goals:**
- 不做窗口选择、枚举或焦点管理——粘贴对象永远是当前焦点窗口，由用户负责聚焦。
- 粘贴后不发送回车或其他确认键。
- 不改自动清空策略本身（`ClipboardClearPolicy` 逻辑不动）。
- 不支持自定义粘贴键位（固定 Ctrl+V）。

## Decisions

1. **键盘注入用 `SendInput` + scancode，不用 `SendKeys`。**
   `SendKeys` 走 `WM_KEYDOWN` 虚拟键消息，很多游戏（DirectInput/原始输入）读不到；`SendInput` 带 `KEYEVENTF_SCANCODE` 更接近真实键盘。新封装 `KeyboardInjector.cs`（静态类，`SendCtrlV()`），P/Invoke 声明集中放这里，避免散进 `MainForm`。
   备选：`keybd_event`（同底层但 API 更老）、`PostMessage`（游戏收不到）——均不取。

2. **自动粘贴触发点放在 `FireIfTriggered` 内、`SetTextWithAutoClear` 确认成功之后，延迟等待异步执行。**
   写入成功才粘（spec 硬性要求）。spec 要求延迟期间不阻塞抓帧循环，故延迟 + 注入整体交给 `Task.Run`（`AutoPasteScheduler`，执行器以委托注入供单测捕获）；冷却判定已被 `_detector.Analyze` 前置，粘贴天然不重复。
   备选：抓帧线程内 `Thread.Sleep`（实现最简但阻塞循环，与 spec 冲突）、在 `CodeGrabbed` UI 事件里粘（写入成败与 UI 通知解耦，时机语义不清）——均不取。
   实现备注：`SetTextWithAutoClear` 本就返回 `bool`（设计初稿误记为 void），无需签名改动。

3. **手动清空复用 `ClipboardService.Clear()`，清空后同时取消该次写入的定时清空。**
   `Clear()` 已存在且线程安全（重试封装）。手动清空后把内部 `_written` 置 null 等价语义（延迟到期时 `ShouldClear` 判 false，定时器空转一次，无副作用——与 spec "不产生可见副作用" 一致），不新增取消 API。
   反馈：点击后按钮文本短暂变"已清空"（约 1 秒后恢复），用现有 `System.Windows.Forms.Timer` 即可。

4. **配置项进 `AppConfig`：`AutoPasteEnabled`(bool, 默认 false)、`AutoPasteDelayMs`(int, 默认 0, 上限 10000)。**
   遵循现有属性 + `Save()` 模式；UI 控件放在现有"清空剪贴板(秒)"附近的第二行布局。

5. **SendInput 的 Ctrl 释放需跟踪真实修饰键状态。**
   若用户此刻正物理按着 Ctrl，注入的 Ctrl down/up 会破坏其组合状态。首版简单处理：注入前用 `GetAsyncKeyState(VK_CONTROL)` 检查，若 Ctrl 已被按下则跳过本次注入（记 OcrDebug 日志）。比模拟"补按"更可预期。

## Risks / Trade-offs

- **[Vanguard 拦截合成输入]** → 游戏内粘贴可能无效；spec 已声明此为已知风险，真机验证是验收步骤之一，失败不影响游戏外场景。
- **[抓帧线程 Sleep 延迟]** → 延迟上限设 10s 且默认 0，最坏情况延迟期间少抓 1~2 帧；同码冷却掩盖该影响。
- **[焦点在程序自己窗口时误粘]** → 用户聚焦到 RoomCodeGrabber 主窗体时粘贴进 `ReadOnly` 文本框，无害；文档/气泡中提示用户先聚焦输入框。
- **[SendInput 被 UIPI 阻挡]**（目标是管理员权限窗口而本程序非管理员）→ 首版不处理，记入已知限制；README 提示以同权限运行。

## Migration Plan

纯增量：新配置项缺省值即旧行为（不粘贴），自动清空逻辑不动，无数据迁移。回滚 = 还原代码，配置文件中多余键会被现有 JSON 反序列化忽略。

## Open Questions

（无——粘贴目标、回车、共存关系均已在探索阶段与用户确认。）
