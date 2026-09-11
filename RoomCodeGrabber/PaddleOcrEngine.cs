using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace RoomCodeGrabber;

/// <summary>
/// PaddleOCR 引擎（GPU / CUDA）。面向带 NVIDIA 独显的机器（RTX 50 系需 sm_120 的
/// CUDA runtime 包）。两个关键取舍：
/// <list type="number">
/// <item>用 rec-only（<see cref="PaddleOcrRecognizer"/>）跳过文本检测——码区域已由用户
/// 框选、位置已知，检测是纯浪费；实测全管线 ~750ms、rec-only 约减半。</item>
/// <item>直接吃原图，不做二值化/放大——PaddleOCR 自带归一化，硬二值化反而破坏字形边缘。</item>
/// </list>
/// 构造时预热（预建 CUDA 上下文 + 模型），避免首次识别落在热路径击穿延迟预算。
/// 无可用 CUDA 时构造或预热会抛异常，由 <see cref="CaptureEngine"/> 回退到 Windows OCR。
/// </summary>
public sealed class PaddleOcrEngine : IOcrEngine
{
    private readonly PaddleOcrRecognizer _rec;

    /// <exception cref="Exception">CUDA 运行时不可用 / 模型加载失败时抛出。</exception>
    public PaddleOcrEngine()
    {
        var model = LocalFullModels.EnglishV5;
        var rec = new PaddleOcrRecognizer(model.RecognizationModel, PaddleDevice.Gpu());
        try
        {
            Warmup(rec);
        }
        catch
        {
            // 预热失败也必须释放已分配的 recognizer（含 CUDA 上下文），
            // 否则每次回退/切换都泄漏一份原生资源，反复切换累积显存占用。
            rec.Dispose();
            throw;
        }
        _rec = rec;
    }

    public string Name => "PaddleOCR (GPU)";

    /// <summary>预热：跑一次空图，预建 CUDA 上下文并加载模型权重。</summary>
    private static void Warmup(PaddleOcrRecognizer rec)
    {
        using var bmp = new Bitmap(64, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        using var mat = BitmapToMat(bmp);
        rec.Run(mat);
    }

    public string Recognize(Bitmap source, byte[]? preparedGray, int preparedWidth, int preparedHeight)
    {
        // 忽略 preparedGray：PaddleOCR 自带归一化，喂二值化的图反而会伤害它，始终用原图
        using var mat = BitmapToMat(source);
        return _rec.Run(mat).Text ?? "";
    }

    // 经 PNG 中转把 Bitmap 转成 Mat（避免额外引入 OpenCvSharp4.Extensions）；
    // 对 200ms 预算而言这点编解码开销可忽略。
    private static Mat BitmapToMat(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
    }

    public void Dispose() => _rec.Dispose();
}
