using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using RoomCodeGrabber;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

// 用法 1: dotnet run                      -> 全管线扫描（裁剪 x 倍数，需真实截图）
// 用法 2: dotnet run -- dump <scale> <x> <y> <w> <h> <out.png> -> 导出预处理图
// 用法 3: dotnet run -- engine            -> 引擎端到端 smoke（合成图，不依赖外部截图）
if (args.Length > 0 && args[0] == "dump")
{
    DumpPreprocessed(args[1..]);
    return;
}
if (args.Length > 0 && args[0] == "engine")
{
    EngineSmoke();
    return;
}

// 用真实截图验证 Preprocessor + Windows OCR + CodeDetector 完整管线（放大倍数扫描）
var imgPath = args.Length > 0 ? args[0]
    : @"C:\Users\Administrator\Desktop\3f460996dd218c084f154e28b90555b3.png";
using var img = new Bitmap(imgPath);
Console.WriteLine($"图像: {img.Width}x{img.Height}");

var en = new Language("en-US");
var ocr = OcrEngine.TryCreateFromLanguage(en) ?? OcrEngine.TryCreateFromUserProfileLanguages();
Console.WriteLine($"OCR 引擎: {ocr?.RecognizerLanguage.LanguageTag ?? "无"}\n");

(double x, double y, double w, double h, string name)[] crops =
{
    (0.045, 0.375, 0.495, 0.120, "代码框(贴边)"),
    (0.070, 0.400, 0.440, 0.080, "代码框(紧凑)"),
    (0.040, 0.050, 0.920, 0.900, "整个对话框"),
};

foreach (var scale in new[] { 2, 3, 4 })
{
    var pre = new Preprocessor(scale);
    foreach (var (rx, ry, rw, rh, name) in crops)
    {
        var rect = new Rectangle(
            (int)(img.Width * rx), (int)(img.Height * ry),
            Math.Max(8, (int)(img.Width * rw)), Math.Max(8, (int)(img.Height * rh)));
        rect.Intersect(new Rectangle(0, 0, img.Width, img.Height));
        using var crop = img.Clone(rect, img.PixelFormat);

        var gray = pre.Process(crop, out int gw, out int gh);
        using var software = SoftwareBitmap.CreateCopyFromBuffer(
            gray.AsBuffer(), BitmapPixelFormat.Gray8, gw, gh);
        var result = ocr!.RecognizeAsync(software).AsTask().GetAwaiter().GetResult();

        var codes = CodeDetector.ExtractCodes(result.Text);
        var codeText = codes.Count > 0 ? string.Join(",", codes) : "(无)";
        var hit = codes.Contains("OZB459") ? "  <<< 命中" : "";
        Console.WriteLine($"[{scale}x][{name}] 原文: \"{result.Text}\"  提取: {codeText}{hit}");
    }
}

static void DumpPreprocessed(string[] a)
{
    // 用法: dump <img> <scale> <x> <y> <w> <h> <out.png>
    int scale = int.Parse(a[1]);
    var rect = new Rectangle(int.Parse(a[2]), int.Parse(a[3]),
        int.Parse(a[4]), int.Parse(a[5]));
    using var img = new Bitmap(a[0]);
    rect.Intersect(new Rectangle(0, 0, img.Width, img.Height));
    using var crop = img.Clone(rect, img.PixelFormat);

    var pre = new Preprocessor(scale);
    var gray = pre.Process(crop, out int gw, out int gh);

    using var raw = new Bitmap(gw, gh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(raw))
    {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.DrawImage(crop, 0, 0, gw, gh);
    }
    using var processed = new Bitmap(gw, gh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
        {
            byte v = gray[y * gw + x];
            processed.SetPixel(x, y, Color.FromArgb(v, v, v));
        }

    int pad = 8;
    using var canvas = new Bitmap(gw + pad * 2, gh * 2 + pad * 3);
    using (var g = Graphics.FromImage(canvas))
    {
        g.Clear(Color.DimGray);
        g.DrawImage(raw, pad, pad);
        g.DrawImage(processed, pad, gh + pad * 2);
    }
    canvas.Save(a[6], System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"已保存 {a[6]} (上:原图放大 下:预处理后)");
}

// 引擎端到端 smoke：不依赖外部截图，用抗锯齿合成图验证 WindowsOcrEngine 封装。
// 覆盖两条路径——引擎自行预处理（preparedGray=null）与复用调用方预处理图（preparedGray!=null）。
static void EngineSmoke()
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var engine = new WindowsOcrEngine(3);
    Console.WriteLine($"引擎: {engine.Name}");
    if (engine.Warning != null) Console.WriteLine($"警告(首行): {engine.Warning.Split('\n')[0]}");

    int pass = 0, total = 0;
    foreach (var (code, font) in new[] { ("OZB459", "Consolas"), ("ASD123", "Consolas"), ("OZB459", "Arial"), ("SAT033", "Arial") })
    {
        using var bmp = RenderCode(code, 40, font);
        var text = engine.Recognize(bmp, null, 0, 0);   // 引擎自行预处理
        var got = CodeDetector.ExtractCodes(text.ToUpperInvariant());
        bool hit = got.Contains(code);
        total++; if (hit) pass++;
        Console.WriteLine($"{(hit ? "PASS" : "FAIL")}  {code}/{font}  -> 原文 \"{text}\"  提取 [{string.Join(",", got)}]");
    }

    // 复用调用方预处理图路径：结果应与引擎自行预处理一致（Finding 2 修复不改变识别结果）
    using (var bmp = RenderCode("OZB459", 40, "Consolas"))
    {
        var pre = new Preprocessor(3);
        var gray = pre.Process(bmp, out int pw, out int ph);
        var text2 = engine.Recognize(bmp, gray, pw, ph);
        var got2 = CodeDetector.ExtractCodes(text2.ToUpperInvariant());
        bool hit2 = got2.Contains("OZB459");
        total++; if (hit2) pass++;
        Console.WriteLine($"{(hit2 ? "PASS" : "FAIL")}  OZB459/Consolas(复用预处理图)  -> 原文 \"{text2}\"");
    }

    Console.WriteLine($"\n{pass}/{total} 命中");
    engine.Dispose();
    Environment.Exit(pass == total ? 0 : 1);
}

static Bitmap RenderCode(string code, int fontPx, string font)
{
    int h = (int)(fontPx * 1.6);
    int w = (int)(fontPx * code.Length * 0.8) + fontPx;
    var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.White);
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
    using var f = new Font(font, fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
    var sz = g.MeasureString(code, f);
    g.DrawString(code, f, Brushes.Black, (w - sz.Width) / 2f, (h - sz.Height) / 2f);
    return bmp;
}
