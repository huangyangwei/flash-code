using System.Media;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoomCodeGrabber;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_SELECT = 1; // Ctrl+Alt+S
    private const int HOTKEY_ID_TOGGLE = 2; // Ctrl+Alt+G

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
    private readonly System.Windows.Forms.Timer _clearFeedbackTimer;

    public MainForm(AppConfig config, CaptureEngine engine)
    {
        _config = config;
        _engine = engine;

        Text = "RoomCodeGrabber - 抖音无畏契约房间码抓取";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 384);
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
        _cooldown.ValueChanged += (_, _) => { _config.CooldownSeconds = (int)_cooldown.Value; _config.Save(); };

        var lblInterval = new Label { Text = "抓帧间隔(ms):", Location = new Point(210, 132), AutoSize = true };
        _interval = new NumericUpDown { Location = new Point(310, 128), Width = 60, Minimum = 10, Maximum = 200, Value = _config.CaptureIntervalMs };
        _interval.ValueChanged += (_, _) =>
        {
            _config.CaptureIntervalMs = (int)_interval.Value;
            _config.Save();
            // 间隔即时生效：重启循环
            if (_engine.Running) { _engine.Stop(); _engine.Start(); }
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
            _config.ConfirmFrames = (int)_confirm.Value;
            _config.Save();
            // 确认帧数在检测器构造时固定：改后重启循环生效
            if (_engine.Running) { _engine.Stop(); _engine.Start(); }
        };

        var lblOcr = new Label { Text = "OCR 原文（调试）:", Location = new Point(16, 209), AutoSize = true };
        var lblScale = new Label { Text = "放大倍数:", Location = new Point(614, 132), AutoSize = true };
        _scale = new NumericUpDown { Location = new Point(682, 128), Width = 50, Minimum = 2, Maximum = 6, Value = _config.ScaleFactor };
        _scale.ValueChanged += (_, _) =>
        {
            _config.ScaleFactor = (int)_scale.Value;
            _config.Save();
            // 放大倍数在引擎构造时固定：改后重启循环生效
            if (_engine.Running) { _engine.Stop(); _engine.Start(); }
        };

        var grabOnStartCheck = new CheckBox
        {
            Text = "启动即抓", Location = new Point(16, 156), AutoSize = true,
            Checked = _config.GrabOnStart,
        };
        grabOnStartCheck.CheckedChanged += (_, _) =>
        {
            _config.GrabOnStart = grabOnStartCheck.Checked;
            _config.Save();
            if (_engine.Running) { _engine.Stop(); _engine.Start(); }
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
        _engine.OcrDebug += OnOcrDebug;

        // 全局热键
        RegisterHotKey(Handle, HOTKEY_ID_SELECT, MOD_CONTROL | MOD_ALT, (int)Keys.S);
        RegisterHotKey(Handle, HOTKEY_ID_TOGGLE, MOD_CONTROL | MOD_ALT, (int)Keys.G);

        FormClosing += (_, e) =>
        {
            if (_engine.Running) _engine.Stop();
            UnregisterHotKey(Handle, HOTKEY_ID_SELECT);
            UnregisterHotKey(Handle, HOTKEY_ID_TOGGLE);
            _tray.Visible = false;
        };

        if (_engine.InitError != null)
        {
            MessageBox.Show(_engine.InitError, "OCR 不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (_engine.InitWarning != null)
        {
            MessageBox.Show(_engine.InitWarning, "OCR 识别率提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (_config.Region == null)
        {
            // 首次运行直接进入框选
            BeginInvoke(SelectRegion);
        }
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

        _config.OcrEngine = kind;
        _config.Save();

        // 引擎在 CaptureEngine 构造时固定：停-重建-启，切换即时生效
        _engine.ReinitializeOcr();
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
            if (!wasRunning) _engine.Start(); // 首次框选后自动开始
        }

        _toggleButton.Text = _engine.Running ? "停止抓取 (Ctrl+Alt+G)" : "开始抓取 (Ctrl+Alt+G)";
        _statusLabel.Text = StatusText();
    }

    /// <summary>立即清空剪贴板（手动入口）。按钮文本短暂置"已清空"作反馈。</summary>
    private void ClearClipboardNow()
    {
        ClipboardService.Clear();
        _clearClipboardButton.Text = "已清空";
        _clearFeedbackTimer.Stop();
        _clearFeedbackTimer.Start();
    }

    private void OnCodeGrabbed(string code, double latencyMs)
    {
        BeginInvoke(() =>
        {
            _codeLabel.Text = code;
            _latencyLabel.Text = $"{latencyMs:F0} ms";
            _latencyLabel.ForeColor = latencyMs <= 200 ? Color.Green : Color.Red;
            if (_config.SoundEnabled) SystemSounds.Exclamation.Play();
            _tray.ShowBalloonTip(1500, "已抓取房间码", $"{code}（{latencyMs:F0}ms）", ToolTipIcon.Info);
            _statusLabel.Text = StatusText();
        });
    }

    private DateTime _lastOcrDebug = DateTime.MinValue;

    private void OnOcrDebug(string text)
    {
        // 视频画面常变时 OCR 接连触发，调试框最多 5 次/秒刷新，避免拖慢 UI
        if ((DateTime.UtcNow - _lastOcrDebug).TotalMilliseconds < 200) return;
        _lastOcrDebug = DateTime.UtcNow;
        BeginInvoke(() =>
        {
            _ocrBox.AppendText(text.Replace("\r\n", " / ").Replace("\n", " / ") + Environment.NewLine);
            // 只保留最近若干行
            if (_ocrBox.Lines.Length > 200)
                _ocrBox.Text = string.Join(Environment.NewLine,
                    _ocrBox.Lines[^100..]);
        });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HOTKEY_ID_SELECT: BeginInvoke(SelectRegion); return;
                case HOTKEY_ID_TOGGLE: BeginInvoke(ToggleGrabbing); return;
            }
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;
}
