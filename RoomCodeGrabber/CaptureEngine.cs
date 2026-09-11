using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RoomCodeGrabber;

/// <summary>
/// 抓帧引擎：专用线程循环 抓屏 -> 灰度化(2x放大) -> 变化检测 -> Windows OCR -> 码判定 -> 剪贴板。
/// 延迟预算（interval=20ms）：等帧 ≤20ms + 抓帧 ~5ms + OCR 10~40ms，最坏约 70ms。
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    /// <summary>抓到新码并已写入剪贴板。(码, 实测延迟ms)。从抓帧线程触发。</summary>
    public event Action<string, double>? CodeGrabbed;
    /// <summary>调试：本帧 OCR 原文。从抓帧线程触发。</summary>
    public event Action<string>? OcrDebug;

    private readonly AppConfig _config;
    private readonly CodeDetector _detector;
    private Thread? _thread;
    private volatile bool _running;

    private OcrEngine? _ocr;
    public string? InitError { get; private set; }
    /// <summary>非致命提示：如未装 en-US 语言包而降级到其他引擎（对字母数字码识别率明显下降）。</summary>
    public string? InitWarning { get; private set; }

    // 可复用缓冲
    private readonly Preprocessor _preprocessor;
    private Bitmap? _captureBmp;
    private byte[]? _prevGray;      // 上一帧快照（变化检测用）
    private List<string> _prevCodes = new();

    public CaptureEngine(AppConfig config)
    {
        _config = config;
        _preprocessor = new Preprocessor(config.ScaleFactor);
        _detector = new CodeDetector(config.CooldownSeconds, config.ConfirmFrames, config.GrabOnStart);
        InitOcr();
    }

    private void InitOcr()
    {
        try
        {
            // 纯字母数字的房间码用 en-US 识别最准；不可用则退回用户语言，再不行取第一个可用识别器
            var en = new Language("en-US");
            _ocr = OcrEngine.TryCreateFromLanguage(en);
            if (_ocr == null)
            {
                // en-US 缺失：退回用户语言/任意语言。中文引擎对纯字母数字码极弱——
                // 会吞字、乱插空格（实测真实码 OZB459 被识别成 "OZB 59"），且即使
                // 干净白底黑字也要 ~40px 才能读对。必须显式提示，否则用户只会看到
                // "识别率莫名很低"却无从排查。
                _ocr = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocr == null)
                {
                    var any = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                    if (any != null) _ocr = OcrEngine.TryCreateFromLanguage(any);
                }
                if (_ocr != null)
                {
                    InitWarning =
                        $"未安装 en-US OCR 语言包，当前回退到 {_ocr.RecognizerLanguage.LanguageTag} 引擎，"
                        + "对字母数字房间码识别率会明显偏低（常吞字/乱插空格）。\n\n"
                        + "建议安装英文 OCR 语言包（在管理员 PowerShell 中执行，完成后重启本程序）：\n"
                        + "Add-WindowsCapability -Online -Name \"Language.OCR~~~en-US~0.0.1.0\"";
                }
            }
            if (_ocr == null)
            {
                InitError = "未找到可用的 Windows OCR 语言包。请在 系统设置 > 时间和语言 > 语言 中安装任意语言。";
            }
        }
        catch (Exception ex)
        {
            InitError = "OCR 引擎初始化失败: " + ex.Message;
        }
    }

    public bool Running => _running;
    public string OcrLanguage => _ocr?.RecognizerLanguage.LanguageTag ?? "-";

    public void Start()
    {
        if (_running || InitError != null) return;
        if (_config.Region == null) return;
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "CaptureLoop",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
        _detector.Reset();
        _prevCodes.Clear();
    }

    public void ResetDetector()
    {
        _detector.Reset();
        _prevCodes.Clear();
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        while (_running)
        {
            try
            {
                var region = _config.Region;
                if (region == null || region.Value.Width < 8 || region.Value.Height < 8)
                {
                    Thread.Sleep(100);
                    sw.Restart();
                    continue;
                }
                ProcessFrame(region.Value, sw);
            }
            catch
            {
                // 单帧失败（如窗口移动瞬间区域越界）不影响循环
            }

            var elapsed = (int)sw.ElapsedMilliseconds;
            if (elapsed < _config.CaptureIntervalMs)
                Thread.Sleep(_config.CaptureIntervalMs - elapsed);
            sw.Restart();
        }
    }

    private void ProcessFrame(Rectangle region, Stopwatch sw)
    {
        int w = region.Width, h = region.Height;
        EnsureBuffers(w, h);

        // ---- 1. 抓帧（延迟计时起点：此刻起画面已在我们手上） ----
        using (var g = Graphics.FromImage(_captureBmp!))
        {
            g.CopyFromScreen(region.X, region.Y, 0, 0, new System.Drawing.Size(w, h),
                CopyPixelOperation.SourceCopy);
        }

        // ---- 2. 预处理（Otsu 二值化/对比度拉伸 + 自动反色 + 块放大），见 Preprocessor ----
        var gray = _preprocessor.Process(_captureBmp!, out int gw2, out int gh2);
        if (_prevGray == null || _prevGray.Length != gray.Length)
            _prevGray = new byte[gray.Length];

        // ---- 3. 变化检测：与上一帧抽样对比，静止画面跳过 OCR ----
        if (_prevGray != null && IsSameFrame(gray))
        {
            _detector.Analyze(_prevCodes); // 维持上一帧的可见码状态（不产生新触发）
            return;
        }
        Array.Copy(gray, _prevGray!, gray.Length);

        // ---- 4. OCR ----
        var software = SoftwareBitmap.CreateCopyFromBuffer(
            gray.AsBuffer(), BitmapPixelFormat.Gray8, gw2, gh2);
        string text;
        try
        {
            var result = _ocr!.RecognizeAsync(software).AsTask().GetAwaiter().GetResult();
            text = result.Text;
        }
        finally
        {
            software.Dispose();
        }

        OcrDebug?.Invoke(text);

        // ---- 5. 码判定 + 剪贴板 ----
        var codes = CodeDetector.ExtractCodes(text);
        _prevCodes = codes;
        var trigger = _detector.Analyze(codes);
        if (trigger != null)
        {
            ClipboardService.SetText(trigger);
            CodeGrabbed?.Invoke(trigger, sw.Elapsed.TotalMilliseconds);
        }
    }

    private bool IsSameFrame(byte[] gray)
    {
        // 每 16 字节抽样。阈值必须极低：在终端/输入框里敲 6 个字符的变化只占大区域
        // 的千分之几，若按 1% 判静止会把这类小面积文字变化整个漏掉。
        // 真正想过滤掉的只是静止画面上的压缩噪声（≈0 变化）。
        int diff = 0, sampled = 0;
        var prev = _prevGray!;
        for (int i = 0; i < gray.Length; i += 16)
        {
            sampled++;
            if (Math.Abs(gray[i] - prev[i]) > 12) diff++;
        }
        // 变化抽样点 <0.05% 且绝对数 <4 视为静止
        return diff < Math.Max(4, sampled / 2000);
    }

    private void EnsureBuffers(int w, int h)
    {
        if (_captureBmp == null || _captureBmp.Width != w || _captureBmp.Height != h)
        {
            _captureBmp?.Dispose();
            _captureBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
    }

    public void Dispose()
    {
        Stop();
        _captureBmp?.Dispose();
    }
}
