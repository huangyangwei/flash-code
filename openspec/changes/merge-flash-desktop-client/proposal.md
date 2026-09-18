## Why

`flash-code` 已经具备低延迟的屏幕 OCR、房间码判定、剪贴板和自动粘贴能力，`flash-video` 已经具备低延迟 FLV 播放和抖音开播监测能力，但目前需要分别运行 WinForms 程序与 Vite/Node 播放页。用户仍需在两个运行时之间切换，且自动监测依赖开发服务器，无法交付为一个可直接使用的 Windows 客户端。

现在合并可以把“开播监测 -> 低延迟播放 -> 房间码识别 -> 自动粘贴”闭合在一个窗口和一个配置体系内，同时保留现有两边已经验证过的核心能力。

## What Changes

- 新增一个 Windows 桌面宿主，以 .NET 8 WinForms/WPF 承载应用生命周期、托盘、热键、配置、OCR、剪贴板和自动粘贴。
- 将 `flash-video` 的前端播放器资源嵌入 WebView2，保留 `mpegts.js` 的 FLV 播放、追帧、断流提示和手动播放入口。
- 将开播监测从 Vite 开发服务器插件迁移到桌面宿主，通过本地消息桥向 WebView2 推送直播状态和新鲜 FLV 地址。
- 增加统一消息契约：宿主向前端发送播放/监测/OCR 状态，前端向宿主发送播放设置、房间监测配置和用户操作。
- 将房间码抓取结果接入统一界面；识别成功后继续使用原生剪贴板、自动清空和可选的 Ctrl+V 自动粘贴。
- 将前端构建产物作为桌面程序资源打包，最终运行时不依赖 Node.js、Vite 开发服务器或额外本地 HTTP API。
- 统一配置持久化，避免 Cookie、播放设置和 OCR 设置分散在浏览器 `localStorage` 与桌面 `config.json` 中；敏感 Cookie 不回显到状态快照或日志。
- 保留手动粘贴 FLV 地址、手动框选 OCR 区域和人工恢复播放作为自动链路失败时的兜底入口。

## Capabilities

### New Capabilities

- `unified-desktop-client`: 单一 Windows 桌面宿主、WebView2 前端嵌入、统一配置与宿主消息桥。
- `low-latency-playback`: 在桌面客户端内输入或接收 FLV 地址，以最小缓冲播放并执行追帧、断流恢复和错误提示。
- `live-room-monitoring`: 轮询抖音房间页，识别开播跳变，提取有效 FLV 地址并驱动当前客户端自动播放。
- `room-code-capture`: 对可见直播画面进行区域 OCR，识别符合房间码格式的新码，复制到剪贴板并按配置自动粘贴。

### Modified Capabilities

（当前主项目 `openspec/specs/` 没有已登记的 capability；本变更全部建立新 capability。）

## Impact

- 主要代码边界：`RoomCodeGrabber/` 的 WinForms 宿主、`CaptureEngine`、OCR、剪贴板和配置模块；`flash-video/src/` 的播放器与前端 UI；`flash-video/src/lib/` 的房间页解析和轮询决策逻辑。
- 新增 Windows WebView2 宿主依赖和打包配置；Node/Vite 仅保留为前端构建工具。
- 需要新增宿主与 WebView2 之间的消息协议、C# 版房间页轮询/解析服务和集成测试。
- 需要在真实 Windows 环境验证 WebView2 硬件合成画面能否稳定被现有 `CopyFromScreen` OCR 抓取，以及 WebView2 Runtime、FLV CDN CORS 和签名地址过期等运行时约束。
