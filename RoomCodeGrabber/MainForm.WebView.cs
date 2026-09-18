using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoomCodeGrabber;

public sealed partial class MainForm
{
    private WebView2? _webView;
    private HttpClient? _monitorHttp;
    private MonitorBridge? _monitorBridge;
    private bool _webReady;
    private bool _recordingCursor;
    private string _lastOcrText = "";

    private void InitializeEmbeddedClient()
    {
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "app");
        if (!File.Exists(Path.Combine(assetDirectory, "index.html")))
        {
            MessageBox.Show("客户端页面资源缺失。请重新发布完整安装目录；当前仍可使用原抓码控件。",
                "页面资源缺失", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Text = "Flash Code - 直播工作台";
#if QA_FIXTURE
        _monitorBridge = new MonitorBridge(_config, MonitorQaFixture.Create(), PostEvent, config => config.Save());
#else
        _monitorHttp = new HttpClient();
        _monitorBridge = new MonitorBridge(_config, LiveMonitorService.ForHttpClient(_monitorHttp), PostEvent, config => config.Save());
#endif
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
        _webView.BringToFront();
        _ = OpenClientAsync(assetDirectory);
    }

    private async Task OpenClientAsync(string assetDirectory)
    {
        try
        {
            await _webView!.EnsureCoreWebView2Async();
            if (_closing) return;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "flash-client.local", assetDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (_closing) return;
                if (!args.IsSuccess)
                {
                    MessageBox.Show("页面加载失败，请确认 app 资源完整。", "客户端无法启动",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _webReady = true;
                PublishInit();
                PublishCaptureStatus();
            };
            _webView.CoreWebView2.Navigate("https://flash-client.local/index.html");
        }
        catch (Exception)
        {
            if (_closing) return;
            _webView?.Dispose();
            _webView = null;
            MessageBox.Show("无法启动 WebView2。请安装 Microsoft Edge WebView2 Evergreen Runtime；原抓码控件仍可使用。",
                "缺少播放环境", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_closing) return;
        if (!e.Source.StartsWith("https://flash-client.local/", StringComparison.OrdinalIgnoreCase)) return;
        if (!BridgeProtocol.TryParseCommand(e.WebMessageAsJson, out var command, out var error))
        {
            PostError($"操作被拒绝：{error}");
            return;
        }

        try
        {
            if (await _monitorBridge!.TryHandleAsync(command!)) return;
            switch (command!.Type)
            {
                case "playback.settings":
                    UnifiedSettingsCommands.ApplyPlayback(_config, command);
                    _config.Save();
                    PublishInit();
                    return;
                case "capture.settings":
                    if (command.Payload.GetProperty("autoClickEnabled").GetBoolean() && _config.AutoClickPosition is null)
                    {
                        PostError("请先记录点击位置，再启用自动点击");
                        PublishInit();
                        return;
                    }
                    if (!_engine.Reconfigure(() => UnifiedSettingsCommands.ApplyCapture(_config, command)))
                    {
                        PostError("抓帧尚未停止，设置未保存，请重试");
                        return;
                    }
                    _config.Save();
                    PublishInit();
                    PublishCaptureStatus();
                    return;
                case "capture.cursor.record":
                    await RecordCursorPositionAsync();
                    return;
                case "clipboard.clear":
                    ClearClipboardNow();
                    return;
                case "playback.manual-start":
                    return; // URL was validated by BridgeProtocol; the WebView owns playback.
            }
            if (CaptureHostCommands.TryDispatch(command!, () => _engine.Running, SelectRegion, ToggleGrabbing))
            {
                PublishCaptureStatus();
                return;
            }
            PostError("该操作暂不可用");
        }
        catch (Exception)
        {
            PostError("操作失败，请检查配置或重试");
        }
    }

    private void PublishInit()
    {
        var region = _config.Region;
        var monitor = _monitorBridge?.GetStatus() ?? new MonitorStatus();
        PostEvent(BridgeProtocol.SerializeEvent("app.init", new
        {
            playback = new
            {
                catchupEnabled = _config.CatchupEnabled,
                speedThresholdMs = _config.SpeedThresholdMs,
                speedRate = _config.SpeedRate,
                jumpThresholdMs = _config.JumpThresholdMs,
            },
            monitor = new
            {
                enabled = monitor.Enabled,
                room = monitor.Room ?? _config.MonitorRoom,
                intervalSec = monitor.Enabled ? monitor.IntervalSec : _config.MonitorIntervalSeconds,
                live = monitor.Live,
                error = monitor.Error,
                credentialConfigured = new ProtectedCredentialStore(_config).IsConfigured,
            },
            capture = new
            {
                running = _engine.Running,
                region = region is null ? null : new { width = region.Value.Width, height = region.Value.Height },
                engine = _engine.OcrLanguage,
                captureIntervalMs = _config.CaptureIntervalMs,
                confirmFrames = _config.ConfirmFrames,
                cooldownSeconds = _config.CooldownSeconds,
                scaleFactor = _config.ScaleFactor,
                grabOnStart = _config.GrabOnStart,
                ocrEngine = (int)_config.OcrEngine,
                soundEnabled = _config.SoundEnabled,
                clipboardClearSeconds = _config.ClipboardClearSeconds,
                autoPasteEnabled = _config.AutoPasteEnabled,
                autoPasteDelayMs = _config.AutoPasteDelayMs,
                autoClickEnabled = _config.AutoClickEnabled,
                autoClickPosition = _config.AutoClickPosition is { } point ? new { x = point.X, y = point.Y } : null,
            },
        }));
    }

    private void PublishCaptureStatus(string? message = null)
    {
        var region = _config.Region;
        PostEvent(BridgeProtocol.SerializeEvent("capture.status", new
        {
            running = _engine.Running,
            region = region is null ? null : new { width = region.Value.Width, height = region.Value.Height },
            engine = _engine.OcrLanguage,
            error = _engine.InitError,
            message = message ?? (_engine.InitError is null ? null : "OCR 初始化失败，请检查引擎并使用可见窗口模式"),
            debug = _lastOcrText,
        }));
    }

    private void PostError(string message) => PostEvent(BridgeProtocol.SerializeEvent("app.error", new { message }));

    private async Task RecordCursorPositionAsync()
    {
        if (_recordingCursor) return;
        _recordingCursor = true;
        _recordCursorButton.Enabled = false;
        _cursorPositionLabel.Text = "3 秒后记录...";
        PostEvent(BridgeProtocol.SerializeEvent("clipboard.status", new { message = "3 秒后记录光标，请把鼠标移到目标输入框" }));
        try
        {
            await Task.Delay(3000);
            if (_closing) return;
            var position = Cursor.Position;
            if (!CursorClickInjector.IsTargetSafe(position))
            {
                PostError("未记录：目标位置须在其他程序的可见窗口内");
                return;
            }
            lock (_config) _config.AutoClickPosition = position;
            _config.Save();
            _cursorPositionLabel.Text = $"目标: {position.X}, {position.Y}";
            PostEvent(BridgeProtocol.SerializeEvent("clipboard.status", new { message = $"已记录点击位置 {position.X}, {position.Y}" }));
            PublishInit();
        }
        catch (Exception)
        {
            if (!_closing) PostError("记录点击位置失败，请检查客户端目录是否可写");
        }
        finally
        {
            _recordingCursor = false;
            if (!_closing)
            {
                _cursorPositionLabel.Text = _config.AutoClickPosition is { } saved
                    ? $"目标: {saved.X}, {saved.Y}" : "目标: 未记录";
                _recordCursorButton.Enabled = true;
            }
        }
    }

    private void PostEvent(string json)
    {
        if (_closing || !_webReady || _webView?.IsDisposed != false) return;
        if (_webView.InvokeRequired)
        {
            try { _webView.BeginInvoke(() => PostEvent(json)); }
            catch (InvalidOperationException) { }
            return;
        }
        _webView.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private async Task ShutdownEmbeddedClientAsync()
    {
        _webReady = false;
        if (_webView?.CoreWebView2 is { } core)
        {
            try { await core.ExecuteScriptAsync("window.dispatchEvent(new Event('pagehide'))").WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* Navigation or renderer may already be gone. */ }
            core.WebMessageReceived -= OnWebMessageReceived;
        }
        try
        {
            if (_monitorBridge is not null) await _monitorBridge.DisposeAsync();
        }
        finally
        {
            _monitorBridge = null;
            _monitorHttp?.Dispose();
            _monitorHttp = null;
            _webView?.Dispose();
            _webView = null;
        }
    }
}
