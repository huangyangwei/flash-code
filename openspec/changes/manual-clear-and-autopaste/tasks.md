# Tasks: manual-clear-and-autopaste

## 1. 配置与基础

- [x] 1.1 `AppConfig` 新增 `AutoPasteEnabled`（默认 false）与 `AutoPasteDelayMs`（默认 0，上限 10000）并随 `Save()` 持久化；验证：构造默认实例断言默认值，`LogicTests` 单测通过
- [x] 1.2 `ClipboardService.SetTextWithAutoClear` 返回值改为 `bool`（写入成败），内部调用点适配；验证：现有逻辑单测 + 编译通过，返回 false 路径为重试耗尽 —— 核实后该方法本就返回 `bool`（重试耗尽返回 false），无需改动，design.md 已修正
- [x] 1.3 新建 `KeyboardInjector.cs`：封装 `SendInput`（`KEYEVENTF_SCANCODE`）的 `SendCtrlV()`，注入前 `GetAsyncKeyState(VK_CONTROL)` 检查 Ctrl 已被物理按住则跳过并返回 false；验证：编译通过，方法可被 LogicTests 以参数化方式测序列组装（注入本体留待真机）

## 2. 手动清空剪贴板

- [ ] 2.1 `MainForm` 增加"清空剪贴板"按钮（现有 `清空剪贴板(秒)` 配置旁），点击调 `ClipboardService.Clear()` 并将按钮文本短暂置"已清空"（约 1 秒后恢复，用 WinForms Timer）；验证：运行程序，点按钮后用外部工具读剪贴板为空，文本恢复
- [ ] 2.2 托盘 `ContextMenuStrip` 增加"清空剪贴板"菜单项，行为同按钮；验证：最小化主窗体后经托盘清空生效
- [ ] 2.3 验证共存语义：`ClipboardClearSeconds>0` 时手动清空后定时到期不误清用户新复制的内容（`ShouldClear` 判 written==current 逻辑不变）；验证：`ClipboardClearPolicy` 现有单测仍通过 + 手动场景走查

## 3. 自动粘贴

- [x] 3.1 `CaptureEngine.FireIfTriggered` 中 `SetTextWithAutoClear` 返回 true 且 `AutoPasteEnabled` 时，按 `AutoPasteDelayMs` 延迟（抓帧线程内等待）后调用 `KeyboardInjector.SendCtrlV()`，失败经 `OcrDebug` 记日志；验证：`LogicTests` 对触发判定做纯逻辑单测（mock 注入器记录调用次数），冷却期内不二次调用 —— 实现为 `AutoPasteScheduler`（延迟+注入在 Task.Run 后台线程，满足 spec"不阻塞抓帧"），mock 单测通过
- [ ] 3.2 主窗体 UI：新增"自动粘贴"复选框 + "粘贴延迟(ms)" NumericUpDown（上限 10000），绑定配置即时保存；验证：改控件后配置文件落盘，重启保持
- [ ] 3.3 验证零阻塞：`AutoPasteDelayMs=5000` 时抓帧循环仍按既有间隔出 `OcrDebug` 输出；验证：运行观察 OCR 原文框持续刷新

## 4. 收尾验证

- [x] 4.1 全量测试与构建：`dotnet build` + `LogicTests` 全绿；验证：命令输出无错误（0 警告 0 错误，LogicTests 33 PASS / 0 FAIL）
- [ ] 4.2 真机验收（spec 已知风险项）：开关关闭时行为与旧版一致（无按键合成）；开启后在记事本等普通输入框验证自动粘贴生效；尝试游戏房间码输入框并记录 Vanguard 是否拦截；验证：三种场景结果记录到本任务勾选说明
- [x] 4.3 README/docs 补充新配置项说明与"需与目标窗口同权限运行"的 UIPI 限制提示；验证：文档条目存在
