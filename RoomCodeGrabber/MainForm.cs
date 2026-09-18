using System.Media;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoomCodeGrabber;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed partial class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_SELECT = 1; // Ctrl+Alt+S
    private const int HOTKEY_ID_TOGGLE = 2; // Ctrl+Alt+G
    private bool _closing;
    private bool _shutdownComplete;

    private readonly AppConfig _config;
    private readonly CaptureEngine _engine;

    private readonly NotifyIcon _tray;
    private readonly Label _codeLabel;
    private readonly Label _statusLabel;
    private readonly Label _latencyLabel;
    private readonly TextBox _ocrBox;
    private readonly Button _toggleButton;
    private readonly NumericUpDown _cooldown;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _confirm;
    private readonly NumericUpDown _scale;
    private readonly ComboBox _engineKind;
    private readonly Button _clearClipboardButton;
    private readonly Button _recordCursorButton;
    private readonly Label _cursorPositionLabel;
    private readonly System.Windows.Forms.Timer _clearFeedbackTimer;

    public MainForm(AppConfig config, CaptureEngine engine)
    {
        _config = config;
        _engine = engine;

        Text = "RoomCodeGrabber - 抖音无畏契约房间码抓取";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(760, 560);
        Font = new Font("Microsoft YaHei UI", 9f);

        var lblCode = new Label { Text = "最新抓取:", Location = new Point(16, 14), AutoSize = true };
        _codeLabel = new Label
        {
            Text = "--",
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 60),
            Location = new Point(100, 4),
            AutoSize = true,
        };
        _latencyLabel = new Label { Text = "", Location = new Point(300, 30), AutoSize = true, ForeColor = Color.DimGray };

        _statusLabel = new Label { Text = StatusText(), Location = new Point(16, 62), AutoSize = true };

        _toggleButton = new Button { Text = "开始抓取 (Ctrl+Alt+G)", Location = new Point(16, 88), Size = new Size(180, 30) };
        _toggleButton.Click += (_, _) => ToggleGrabbing();

        var selectButton = new Button { Text = "框选区域 (Ctrl+Alt+S)", Location = new Point(208, 88), Size = new Size(180, 30) };
        selectButton.Click += (_, _) => SelectRegion();

        var lblCooldown = new Label { Text = "同码冷却(秒):", Location = new Point(16, 132), AutoSize = true };
        _cooldown = new NumericUpDown { Location = new Point(120, 128), Width = 60, Minimum = 0, Maximum = 600, Value = _config.CooldownSeconds };
        _cooldown.ValueChanged += (_, _) => { if (_engine.Reconfigure(() => _config.CooldownSeconds = (int)_cooldown.Value)) _config.Save(); };

        var lblInterval = new Label { Text = "抓帧间隔(ms):", Location = new Point(210, 132), AutoSize = true };
        _interval = new NumericUpDown { Location = new Point(310, 128), Width = 60, Minimum = 10, Maximum = 200, Value = _config.CaptureIntervalMs };
        _interval.ValueChanged += (_, _) =>
        {
            if (_engine.Reconfigure(() => _config.CaptureIntervalMs = (int)_interval.Value)) _config.Save();
        };

        var soundCheck = new CheckBox
        {
            Text = "提示音", Location = new Point(400, 130), AutoSize = true,
            Checked = _config.SoundEnabled,
        };
        soundCheck.CheckedChanged += (_, _) => { _config.SoundEnabled = soundCheck.Checked; _config.Save(); };

        var lblConfirm = new Label { Text = "确认帧数:", Location = new Point(478, 132), AutoSize = true };
        _confirm = new NumericUpDown { Location = new Point(548, 128), Width = 50, Minimum = 1, Maximum = 4, Value = _config.ConfirmFrames };
        _confirm.ValueChanged += (_, _) =>
        {
            if (_engine.Reconfigure(() => _config.ConfirmFrames = (int)_confirm.Value)) _config.Save();
        };

        var lblOcr = new Label { Text = "OCR 原文（调试）:", Location = new Point(16, 209), AutoSize = true };
        var lblScale = new Label { Text = "放大倍数:", Location = new Point(614, 132), AutoSize = true };
        _scale = new NumericUpDown { Location = new Point(682, 128), Width = 50, Minimum = 2, Maximum = 6, Value = _config.ScaleFactor };
        _scale.ValueChanged += (_, _) =>
        {
            if (_engine.Reconfigure(() => _config.ScaleFactor = (int)_scale.Value)) _config.Save();
        };

        var grabOnStartCheck = new CheckBox
        {
            Text = "启动即抓", Location = new Point(16, 156), AutoSize = true,
            Checked = _config.GrabOnStart,
        };
        grabOnStartCheck.CheckedChanged += (_, _) =>
        {
            if (_engine.Reconfigure(() => _config.GrabOnStart = grabOnStartCheck.Checked)) _config.Save();
        };

        var lblEngine = new Label { Text = "OCR 引擎:", Location = new Point(200, 158), AutoSize = true };
        _engineKind = new ComboBox
        {
            Location = new Point(272, 154),
            Width = 170,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _engineKind.Items.AddRange(new object[] { "Windows OCR (默认)", "PaddleOCR (GPU)" });
        _engineKind.SelectedIndex = _config.OcrEngine == OcrEngineKind.Paddle ? 1 : 0;
        _engineKind.SelectedIndexChanged += (_, _) => OnEngineKindChanged();

        var lblClipClear = new Label { Text = "清空剪贴板(秒):", Location = new Point(460, 158), AutoSize = true };
        var clipClear = new NumericUpDown
        {
            Location = new Point(566, 154), Width = 60,
            Minimum = 0, Maximum = 3600, Value = _config.ClipboardClearSeconds,
        };
        clipClear.ValueChanged += (_, _) =>
        {
            _config.ClipboardClearSeconds = (int)clipClear.Value;
            _config.Save();
        };

        // 手动清空：与自动清空共存（秒数 0 = 纯手动），立即清、给 1 秒"已清空"反馈
        _clearClipboardButton = new Button { Text = "清空剪贴板", Location = new Point(636, 152), Size = new Size(112, 26) };
        _clearClipboardButton.Click += (_, _) => ClearClipboardNow();
        _clearFeedbackTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clearFeedbackTimer.Tick += (_, _) =>
        {
            _clearFeedbackTimer.Stop();
            _clearClipboardButton.Text = "清空剪贴板";
        };

        // 自动粘贴：抓到码写入剪贴板后向焦点窗口合成一次 Ctrl+V（目标窗口由用户自行聚焦）
        var autoPasteCheck = new CheckBox
        {
            Text = "自动粘贴", Location = new Point(16, 182), AutoSize = true,
            Checked = _config.AutoPasteEnabled,
        };
        var lblPasteDelay = new Label { Text = "粘贴延迟(ms):", Location = new Point(100, 186), AutoSize = true };
        var pasteDelay = new NumericUpDown
        {
            Location = new Point(196, 182), Width = 70,
            Minimum = 0, Maximum = AutoPastePolicy.MaxDelayMs, Value = _config.AutoPasteDelayMs,
            Enabled = _config.AutoPasteEnabled,
        };
        autoPasteCheck.CheckedChanged += (_, _) =>
        {
            _config.AutoPasteEnabled = autoPasteCheck.Checked;
            pasteDelay.Enabled = autoPasteCheck.Checked;
            _config.Save();
        };
        pasteDelay.ValueChanged += (_, _) =>
        {
            _config.AutoPasteDelayMs = (int)pasteDelay.Value;
            _config.Save();
        };

        var autoClickCheck = new CheckBox
        {
            Text = "自动点击", Location = new Point(285, 182), AutoSize = true,
            Checked = _config.AutoClickEnabled,
            Enabled = _config.AutoClickPosition is not null,
        };
        autoClickCheck.CheckedChanged += (_, _) =>
        {
            _config.AutoClickEnabled = autoClickCheck.Checked;
            _config.Save();
        };
        _recordCursorButton = new Button { Text = "记录光标位置", Location = new Point(390, 181), Size = new Size(130, 26) };
        _recordCursorButton.Click += async (_, _) =>
        {
            await RecordCursorPositionAsync();
            autoClickCheck.Enabled = _config.AutoClickPosition is not null;
        };
        _cursorPositionLabel = new Label
        {
            Text = _config.AutoClickPosition is { } saved ? $"目标: {saved.X}, {saved.Y}" : "目标: 未记录",
            Location = new Point(528, 186), AutoSize = true,
        };

        _ocrBox = new TextBox
        {
            Location = new Point(16, 229),
            Size = new Size(748, 120),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 8.5f),
        };

        Controls.AddRange(new Control[]
        {
            lblCode, _codeLabel, _latencyLabel, _statusLabel,
            _toggleButton, selectButton, lblCooldown, _cooldown, lblInterval, _interval, soundCheck,
            lblConfirm, _confirm, lblScale, _scale,
            lblOcr, _ocrBox, grabOnStartCheck, lblEngine, _engineKind, lblClipClear, clipClear,
            _clearClipboardButton, autoPasteCheck, lblPasteDelay, pasteDelay,
            autoClickCheck, _recordCursorButton, _cursorPositionLabel,
        });

        // 托盘图标
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RoomCodeGrabber",
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("框选区域 (Ctrl+Alt+S)", null, (_, _) => SelectRegion());
        menu.Items.Add("开始/停止 (Ctrl+Alt+G)", null, (_, _) => ToggleGrabbing());
        menu.Items.Add("清空剪贴板", null, (_, _) => ClearClipboardNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };

        // 引擎事件（抓帧线程触发 -> UI 线程）
        _engine.CodeGrabbed += OnCodeGrabbed;
        _engine.ClipboardWriteFailed += OnClipboardWriteFailed;
        _engine.OcrDebug += OnOcrDebug;

        // 全局热键
        RegisterHotKey(Handle, HOTKEY_ID_SELECT, MOD_CONTROL | MOD_ALT, (int)Keys.S);
        RegisterHotKey(Handle, HOTKEY_ID_TOGGLE, MOD_CONTROL | MOD_ALT, (int)Keys.G);

        FormClosing += async (_, e) =>
        {
            if (_shutdownComplete) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            _tray.Visible = false;
            UnregisterHotKey(Handle, HOTKEY_ID_SELECT);
            UnregisterHotKey(Handle, HOTKEY_ID_TOGGLE);
            _engine.CancelPendingPaste();
            _engine.Stop();
            try { await ShutdownEmbeddedClientAsync(); }
            finally
            {
                _engine.CodeGrabbed -= OnCodeGrabbed;
                _engine.ClipboardWriteFailed -= OnClipboardWriteFailed;
                _engine.OcrDebug -= OnOcrDebug;
                _clearFeedbackTimer.Stop();
                _clearFeedbackTimer.Dispose();
                _tray.ContextMenuStrip?.Dispose();
                _tray.Dispose();
                _shutdownComplete = true;
                Close();
            }
        };

        if (_engine.InitError != null)
        {
            MessageBox.Show(_engine.InitError, "OCR 不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (_engine.InitWarning != null)
        {
            MessageBox.Show(_engine.InitWarning, "OCR 识别率提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        InitializeEmbeddedClient();
    }

    private string StatusText() =>
        _engine.Running
            ? $"状态: 抓取中   区域: {RegionText()}   OCR: {_engine.OcrLanguage}"
            : "状态: 已停止";

    private string RegionText()
    {
        var r = _config.Region;
        return r == null ? "未框选" : $"{r.Value.Width}x{r.Value.Height}";
    }

    private void OnEngineKindChanged()
    {
        var kind = _engineKind.SelectedIndex == 1 ? OcrEngineKind.Paddle : OcrEngineKind.Windows;
        if (kind == _config.OcrEngine) return;

        if (_engine.Reconfigure(() => _config.OcrEngine = kind)) _config.Save();
        _toggleButton.Text = _engine.Running ? "停止抓取 (Ctrl+Alt+G)" : "开始抓取 (Ctrl+Alt+G)";
        _statusLabel.Text = StatusText();

        if (_engine.InitError != null)
            MessageBox.Show(_engine.InitError, "OCR 不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else if (_engine.InitWarning != null)
            MessageBox.Show(_engine.InitWarning, "OCR 初始化提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ToggleGrabbing()
    {
        if (_engine.Running)
        {
            _engine.Stop();
        }
        else
        {
            if (_engine.InitError != null)
            {
                MessageBox.Show(_engine.InitError, "OCR 不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                PublishCaptureStatus();
                return;
            }
            if (_config.Region == null)
            {
                SelectRegion();
                return;
            }
            _engine.ResetDetector();
            _engine.Start();
        }
        _toggleButton.Text = _engine.Running ? "停止抓取 (Ctrl+Alt+G)" : "开始抓取 (Ctrl+Alt+G)";
        _statusLabel.Text = StatusText();
        PublishCaptureStatus();
    }

    private void SelectRegion()
    {
        var wasRunning = _engine.Running;
        _engine.Stop();

        using var selector = new RegionSelector();
        selector.ShowDialog();
        if (selector.SelectedRegion != null)
        {
            _config.Region = selector.SelectedRegion;
            _config.Save();
            _engine.ResetDetector();
            _engine.Start(); // 成功选择后立即恢复，已有运行状态不能丢失
        }
        else if (wasRunning) _engine.Start(); // 取消时保留此前的抓取状态

        _toggleButton.Text = _engine.Running ? "停止抓取 (Ctrl+Alt+G)" : "开始抓取 (Ctrl+Alt+G)";
        _statusLabel.Text = StatusText();
        PublishCaptureStatus();
    }

    /// <summary>立即清空剪贴板（手动入口）。按钮文本短暂置"已清空"作反馈。</summary>
    private void ClearClipboardNow()
    {
        if (!ClipboardService.Clear())
        {
            PostError("剪贴板暂不可用，请检查占用后重试");
            return;
        }
        _clearClipboardButton.Text = "已清空";
        _clearFeedbackTimer.Stop();
        _clearFeedbackTimer.Start();
        PostEvent(BridgeProtocol.SerializeEvent("clipboard.status", new { message = "剪贴板已清空" }));
    }

    private void OnCodeGrabbed(string code, double latencyMs)
    {
        SafeBeginInvoke(() =>
        {
            _codeLabel.Text = code;
            _latencyLabel.Text = $"{latencyMs:F0} ms";
            _latencyLabel.ForeColor = latencyMs <= 200 ? Color.Green : Color.Red;
            if (_config.SoundEnabled) SystemSounds.Exclamation.Play();
            _tray.ShowBalloonTip(1500, "已抓取房间码", $"{code}（{latencyMs:F0}ms）", ToolTipIcon.Info);
            _statusLabel.Text = StatusText();
            PostEvent(CaptureBridgeEvents.Copied(code, latencyMs));
        });
    }

    private void OnClipboardWriteFailed()
    {
        SafeBeginInvoke(() =>
        {
            _statusLabel.Text = "状态: 剪贴板写入失败，抓取仍在运行";
            PostEvent(CaptureBridgeEvents.ClipboardFailure());
            PublishCaptureStatus("剪贴板未写入；抓取继续运行");
        });
    }

    private DateTime _lastOcrDebug = DateTime.MinValue;

    private void OnOcrDebug(string text)
    {
        // 视频画面常变时 OCR 接连触发，调试框最多 5 次/秒刷新，避免拖慢 UI
        if ((DateTime.UtcNow - _lastOcrDebug).TotalMilliseconds < 200) return;
        _lastOcrDebug = DateTime.UtcNow;
        SafeBeginInvoke(() =>
        {
            _ocrBox.AppendText(text.Replace("\r\n", " / ").Replace("\n", " / ") + Environment.NewLine);
            // 只保留最近若干行
            if (_ocrBox.Lines.Length > 200)
                _ocrBox.Text = string.Join(Environment.NewLine,
                    _ocrBox.Lines[^100..]);
            _lastOcrText = text.Length > 256 ? text[..256] : text;
            PublishCaptureStatus();
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (!_closing && m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HOTKEY_ID_SELECT: BeginInvoke(SelectRegion); return;
                case HOTKEY_ID_TOGGLE: BeginInvoke(ToggleGrabbing); return;
            }
        }
        base.WndProc(ref m);
    }

    private void SafeBeginInvoke(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!_closing && !IsDisposed) action(); }); }
        catch (InvalidOperationException) { }
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;
}
