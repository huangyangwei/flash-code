克隆后进入项目目录，用 .NET 8 SDK 运行即可：

  cd RoomCodeGrabber
  dotnet run

  要求：
  - Windows 10/11 + .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)（项目是 net8.0-windows WinForms）
  - en-US OCR 语言包（必须，中文引擎识别不了字母数字码）。没装的话先在 PowerShell（管理员）执行：
  Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"

  运行后流程：
  1. 首次启动弹全屏遮罩，拖拽框选直播视频画面（框住码出现的区域即可）
  2. 框选完自动开始抓取，识别到新码自动复制到剪贴板
  3. 热键：Ctrl+Alt+S 重新框选，Ctrl+Alt+G 开始/停止，Esc 取消框选

  发布单文件 exe（可选）：

  dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
  # 产物: bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\RoomCodeGrabber.exe

  注意 dotnet run 会同时编译同目录下的 LogicTests/OcrTest 子文件夹吗？不会——csproj 里已用 <Compile Remove>
  排除了它们，主程序直接 dotnet run 没问题。
