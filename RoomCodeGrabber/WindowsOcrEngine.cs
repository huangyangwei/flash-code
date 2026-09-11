using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RoomCodeGrabber;

/// <summary>
/// Windows.Media.Ocr 引擎（系统自带，零依赖）。内部用 <see cref="Preprocessor"/> 做
/// 放大 + 二值化 + 反色后送 Gray8 位图。纯字母数字的房间码用 en-US 识别最准；
/// 不可用则回退用户语言，再不行取第一个可用识别器，并通过 <see cref="Warning"/> 提示。
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine _ocr;
    private readonly Preprocessor _pre;

    /// <summary>非致命提示：如未装 en-US 语言包而降级到其他引擎（对字母数字码识别率明显下降）。</summary>
    public string? Warning { get; }

    /// <param name="scale">预处理放大倍数，与 <see cref="AppConfig.ScaleFactor"/> 一致。</param>
    /// <exception cref="InvalidOperationException">没有任何可用的 OCR 语言包时抛出。</exception>
    public WindowsOcrEngine(int scale)
    {
        _pre = new Preprocessor(scale);

        var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine == null)
        {
            // en-US 缺失：退回用户语言 / 任意语言。中文引擎对纯字母数字码极弱——
            // 会吞字、乱插空格（实测真实码 OZB459 被识别成 "OZB 59"），且即使
            // 干净白底黑字也要 ~40px 才能读对。必须显式提示，否则用户只会看到
            // "识别率莫名很低"却无从排查。
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                var any = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (any != null) engine = OcrEngine.TryCreateFromLanguage(any);
            }
            if (engine != null)
            {
                Warning =
                    $"未安装 en-US OCR 语言包，当前回退到 {engine.RecognizerLanguage.LanguageTag} 引擎，"
                    + "对字母数字房间码识别率会明显偏低（常吞字/乱插空格）。\n\n"
                    + "建议安装英文 OCR 语言包（在管理员 PowerShell 中执行，完成后重启本程序）：\n"
                    + "Add-WindowsCapability -Online -Name \"Language.OCR~~~en-US~0.0.1.0\"";
            }
        }

        if (engine == null)
        {
            throw new InvalidOperationException(
                "未找到可用的 Windows OCR 语言包。请在 系统设置 > 时间和语言 > 语言 中安装任意语言。");
        }
        _ocr = engine;
    }

    public string Name => $"Windows.Media.Ocr ({_ocr.RecognizerLanguage.LanguageTag})";

    public string Recognize(Bitmap source)
    {
        var gray = _pre.Process(source, out int w, out int h);
        using var software = SoftwareBitmap.CreateCopyFromBuffer(
            gray.AsBuffer(), BitmapPixelFormat.Gray8, w, h);
        return _ocr.RecognizeAsync(software).AsTask().GetAwaiter().GetResult().Text;
    }

    public void Dispose()
    {
        // Windows.Media.Ocr 的 OcrEngine 无需显式释放
    }
}
