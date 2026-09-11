using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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

    private IOcrEngine? _ocr;
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
        if (_config.OcrEngine == OcrEngineKind.Paddle)
        {
            try
            {
                _ocr = new PaddleOcrEngine();
                return;
            }
            catch (Exception ex)
            {
                // Paddle 不可用（无 NVIDIA / CUDA runtime 缺失 / 模型加载失败）→ 回退 Windows OCR
                var reason = ex.Message.Split('\n')[0];
                try
                {
                    var win = new WindowsOcrEngine(_config.ScaleFactor);
                    _ocr = win;
                    InitWarning = $"PaddleOCR 初始化失败，已回退到 Windows OCR。\n原因: {reason}"
                                  + (win.Warning != null ? "\n\n" + win.Warning : "");
                    return;
                }
                catch (Exception ex2)
                {
                    InitError = "OCR 引擎初始化失败: " + ex2.Message;
                    return;
                }
            }
        }

        try
        {
            var win = new WindowsOcrEngine(_config.ScaleFactor);
            _ocr = win;
            InitWarning = win.Warning;
        }
        catch (Exception ex)
        {
            InitError = "OCR 引擎初始化失败: " + ex.Message;
        }
    }

    public bool Running => _running;
    public string OcrLanguage => _ocr?.Name ?? "-";

    public void Start()
    {
        if (_running || InitError != null) return;
        if (_config.Region == null) return;
        // 防御：上一个线程若尚未退出（StopThread 超时），拒绝启动，避免两个 Loop 并发
        // 共享无锁的 _captureBmp/_prevGray/_detector。
        if (_thread is { IsAlive: true }) return;
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "CaptureLoop",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>停止抓帧线程（保留引擎）。</summary>
    public void Stop() => StopThread();

    /// <summary>
    /// 停止抓帧线程。返回 true 表示线程已确认退出——<b>只有此时才可安全释放 _ocr</b>；
    /// 否则抓帧线程可能仍停在 Recognize() 内，dispose 引擎会造成 use-after-dispose（原生资源竞态）。
    /// </summary>
    private bool StopThread()
    {
        _running = false;
        var t = _thread;
        if (t == null)
        {
            _detector.Reset();
            _prevCodes.Clear();
            return true;
        }
        // Loop 会在当前帧处理完后检查 _running 退出；Paddle 单帧可达数百 ms，给足 10s。
        bool exited = t.Join(TimeSpan.FromSeconds(10));
        if (exited) _thread = null;   // 确认退出才清引用；未退出则保留，供 Start() 拒绝启动
        _detector.Reset();
        _prevCodes.Clear();
        return exited;
    }

    public void ResetDetector()
    {
        _detector.Reset();
        _prevCodes.Clear();
    }

    /// <summary>按当前配置重建 OCR 引擎（切换引擎后即时生效，无需重启）。</summary>
    public void ReinitializeOcr()
    {
        bool wasRunning = _running;

        // 关键顺序：先确认抓帧线程已退出，再 dispose 引擎——否则线程可能仍停在 Recognize()
        // 内使用它（use-after-dispose）。超时未退出则放弃本次切换，避免原生资源竞态。
        if (!StopThread())
        {
            InitError = "抓帧线程未能在超时内停止，已取消本次 OCR 引擎切换（请重试）。";
            return;
        }

        _ocr?.Dispose();
        _ocr = null;
        InitError = null;
        InitWarning = null;
        InitOcr();
        if (wasRunning && InitError == null) Start();
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

        // ---- 2. 预处理（变化检测用；同时复用给能接受它的引擎，避免每帧两次预处理）----
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

        // ---- 4. OCR（传原图 + 预处理图；Windows 引擎复用后者，Paddle 用原图）----
        string text;
        try
        {
            text = _ocr!.Recognize(_captureBmp!, gray, gw2, gh2);
        }
        catch
        {
            text = ""; // 单帧识别失败不中断抓帧循环
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
        // 线程已退出才释放引擎（含 PaddleOCR 的 CUDA 上下文）；未退出则宁可不释放，也不冒竞态风险
        if (StopThread())
        {
            _ocr?.Dispose();
            _ocr = null;
        }
        _captureBmp?.Dispose();
    }
}
