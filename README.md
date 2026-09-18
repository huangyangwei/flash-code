# Flash Code 直播工作台

Windows x64 客户端把开播监测、FLV 播放、可见画面 OCR 抓码和剪贴板操作放在同一窗口。发布后双击 `RoomCodeGrabber.exe`，不需要启动 Node、Vite 或第二个监测进程。`flash-video` 和旧抓码代码保留作对照与回滚。

## 运行客户端

1. 在 Windows 10/11 x64 上安装 [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)；推荐安装英语 (美国) OCR 语言包。发布版自带 .NET 运行时。
2. 将**完整发布目录**放在可写位置，双击其中的 `RoomCodeGrabber.exe`。`app/` 页面资源必须与 exe 同目录，不能只复制 exe；首次运行会在 exe 旁生成 `config.json`。
3. 输入抖音直播间房间号或 `https://live.douyin.com/...` 链接，设置轮询间隔（1-300 秒），按“开始监测”。可选 Cookie 只在当前 Windows 用户下加密保存；界面仅显示是否已保存。
4. 监测到开播后自动接入 FLV；也可把有效的 HTTP(S) `.flv` 地址粘贴到“手动播放 FLV”中。若签名过期或播放中断，重新输入新地址，或等待仍在运行的监测获得更新地址。
5. 保持码出现的画面**可见**，点击“框选画面区域”拖出至少 8x8 像素区域；框选成功后自动开始 OCR，识别到新码才复制。点击清空按钮或托盘菜单可手动清空剪贴板。

`Ctrl+Alt+S` 重新框选，`Ctrl+Alt+G` 开始/停止抓取，框选时 `Esc` 取消。播放和抓码设置由宿主保存，重启后恢复。自动点击和自动粘贴均默认关闭：点“记录光标位置”后有 3 秒将鼠标移到其他程序的目标输入框；记录坐标后可勾选自动点击。新码复制成功后等待“执行延迟”，先点击目标坐标（鼠标随后回到原位置），再按需向获得焦点的窗口发送一次 Ctrl+V。只勾选自动点击时不会粘贴。

## 前置条件与限制

- WebView2 Runtime 缺失时程序会显示安装提示，旧原生抓码控件仍可使用。Windows OCR 若缺少可用语言包会提示；英语 OCR 效果更好，可在管理员 PowerShell 执行 `Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"`。PaddleOCR 是可选 GPU 路径，需要匹配的 NVIDIA/CUDA 环境。
- OCR 使用 Windows 屏幕区域抓取，窗口被遮挡、最小化、屏幕锁定或某些硬件合成/全屏模式都可能抓到空画面。请保持视频在普通可见窗口中；无识别结果时查看“抓码”下的 OCR 调试文本并重新框选。不能保证所有显示模式都可抓取。
- 房间页可能触发验证或改变格式，客户端会停止自动接入并提示；此时可手动粘贴 FLV 地址。流的签名会过期，CDN 的 CORS/MSE 限制也可能导致播放失败。
- 剪贴板被占用时写入会失败，不会触发点击或粘贴；自动清空仅清理由本客户端写入且仍未变更的码。坐标按屏幕绝对位置保存，调整分辨率或显示器布局后请重新记录；位置落在客户端自身窗口、屏幕外或有键/鼠按下时跳过点击和本次粘贴。受 Windows 权限或游戏安全策略限制，自动点击/粘贴不保证可达目标窗口。

## 配置迁移

已有 `RoomCodeGrabber/config.json` 可沿用：在**新发布目录尚无 `config.json` 时**复制旧文件到 exe 旁。缺少新字段使用安全默认值；无效字段逐项回退/夹取，原屏幕区域坐标在换屏幕或缩放后建议重新框选。受 Windows 用户绑定的加密 Cookie 只可由保存它的同一用户读取；换机时重新输入。旧 `flash-video` 浏览器 `localStorage` 中的房间/追帧参数不会自动导入，需在工作台重新设置；前端此后不使用它保存核心设置。不要覆盖新目录中已有的配置而未先备份。

## 开发与发布

在仓库根目录执行（Node >=20.19、.NET 8 SDK；`npm ci` 仅在首次或干净依赖目录运行）：

```powershell
cd ClientWeb
npm ci
npm test
npm run build
cd ..
dotnet run --project RoomCodeGrabber/LogicTests/LogicTests.csproj
dotnet build RoomCodeGrabber/RoomCodeGrabber.csproj -c Debug
./scripts/publish-win-x64.ps1
```

脚本每次创建一个新的 `artifacts/flash-client-win-x64-.../` 发布目录，并核实 exe 与 `app/` 页面资源；不会覆盖旧发布物。开发前端预览可在 `ClientWeb` 运行 `npm run dev -- --port 5175`，此模式不带原生监测/OCR。正式工作流始终通过发布的 exe 测试，关闭时监测、WebView2、抓码与待发的自动点击/粘贴一起结束。
