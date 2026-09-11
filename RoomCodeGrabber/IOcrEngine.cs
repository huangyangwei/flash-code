using System.Drawing;

namespace RoomCodeGrabber;

/// <summary>
/// 可插拔 OCR 引擎。输入是抓到的原始区域位图，各引擎自行决定预处理策略：
/// <list type="bullet">
/// <item>Windows OCR 走 <see cref="Preprocessor"/>（放大 + Otsu 二值化 + 自动反色）；</item>
/// <item>PaddleOCR 自带文本检测，直接吃原图——硬二值化会破坏其需要的边缘信息，故不做。</item>
/// </list>
/// 与 <see cref="CaptureEngine"/> 的契约：<see cref="Recognize"/> 同步、在抓帧线程调用；
/// 引擎内部异常由 CaptureEngine 兜底为空结果，避免单帧失败中断抓帧循环。
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>引擎名（诊断 / 日志 / UI 展示，如 "Windows.Media.Ocr (en-US)"）。</summary>
    string Name { get; }

    /// <summary>
    /// 识别一张裁剪位图，返回原始文本（未做房间码格式纠正，由 <see cref="CodeDetector"/> 处理）。
    /// </summary>
    string Recognize(Bitmap source);
}
