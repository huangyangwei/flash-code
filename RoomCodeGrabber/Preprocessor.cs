using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RoomCodeGrabber;

/// <summary>
/// OCR 预处理：高质量插值放大 -> 灰度 -> Otsu 二值化（或对比度拉伸）-> 自动反色。
/// 关键顺序：先放大再二值化。小字号的抗锯齿圆环（如 0 的内孔）在 1x 二值化时会
/// 被压缩噪声填死，先放大则能在足够分辨率下保留字形细节。
/// 目标形态：黑字白底。终端反色、半透明贴片、深底浅字都靠这一步抹平。
/// </summary>
public sealed class Preprocessor : IDisposable
{
    private readonly int _scale; // 放大倍数：小字号建议 3~4，默认 3
    private Bitmap? _scaledBmp;
    private byte[]? _gray;

    public Preprocessor(int scale = 3)
    {
        _scale = Math.Clamp(scale, 2, 6);
    }

    /// <summary>处理后返回放大后的灰度缓冲及尺寸。缓冲内部复用，勿长期持有。</summary>
    public byte[] Process(System.Drawing.Bitmap src, out int outW, out int outH)
    {
        int w = src.Width, h = src.Height;
        int gw = w * _scale, gh = h * _scale;
        EnsureBuffers(gw, gh);
        var gray = _gray!;
        Array.Clear(_hist!);

        // 1) 高质量插值放大（灰度化之前，保留字形细节）
        _scaledBmp ??= new Bitmap(gw, gh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_scaledBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, gw, gh);
        }

        // 2) 灰度 + 直方图
        var bd = _scaledBmp.LockBits(new Rectangle(0, 0, gw, gh), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        long sumLum = 0, sumSq = 0;
        try
        {
            var buf = _buf!;
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, buf, 0, bd.Stride * gh);
            for (int y = 0; y < gh; y++)
            {
                int srcRow = y * bd.Stride;
                int rowBase = y * gw;
                for (int x = 0; x < gw; x++)
                {
                    int o = srcRow + x * 4;
                    byte lum = (byte)((buf[o + 2] * 299 + buf[o + 1] * 587 + buf[o] * 114) / 1000);
                    gray[rowBase + x] = lum;
                    _hist![lum]++;
                    sumLum += lum;
                    sumSq += (long)lum * lum;
                }
            }
        }
        finally
        {
            _scaledBmp.UnlockBits(bd);
        }

        int total = gw * gh;
        double mean = (double)sumLum / total;
        double varTotal = (double)sumSq / total - mean * mean;

        // 3) Otsu：找使类间方差最大的阈值
        int bestT = 128;
        double bestVarB = 0;
        double fracMinor = 0; // 少数类占比
        long w0 = 0, sum0 = 0;
        for (int t = 0; t < 255; t++)
        {
            w0 += _hist![t];
            if (w0 == 0) continue;
            long w1 = total - w0;
            if (w1 == 0) break;
            sum0 += (long)t * _hist[t];
            double diff = (double)sum0 / w0 - (double)(sumLum - sum0) / w1;
            double varB = (double)w0 * w1 * diff * diff;
            if (varB > bestVarB)
            {
                bestVarB = varB;
                bestT = t;
                fracMinor = Math.Min(w0, w1) / (double)total;
            }
        }

        // 二值化条件：直方图双峰明显且前景占比像文字；
        // 不满足（码贴在花哨视频画面上）退回对比度拉伸，避免硬切毁掉笔画
        bool useBinary = varTotal > 100 && bestVarB / varTotal > 0.75
                         && fracMinor > 0.002 && fracMinor < 0.5;

        // 拉伸用的 2%/98% 分位
        int lo = 0, hi = 255;
        long acc = 0;
        for (int i = 0; i < 256; i++) { acc += _hist![i]; if (acc >= total * 0.02) { lo = i; break; } }
        acc = 0;
        for (int i = 255; i >= 0; i--) { acc += _hist[i]; if (acc >= total * 0.02) { hi = i; break; } }
        int span = Math.Max(16, hi - lo);

        // 深色背景（终端/游戏暗色 UI 等）反色成白底黑字
        bool invert = mean < 110;

        // 4) 原地变换为最终形态
        for (int i = 0; i < total; i++)
        {
            int l = gray[i];
            byte o;
            if (useBinary)
                o = l > bestT ? (byte)255 : (byte)0;
            else
                o = (byte)Math.Clamp((l - lo) * 255 / span, 0, 255);
            if (invert) o = (byte)(255 - o);
            gray[i] = o;
        }

        outW = gw;
        outH = gh;
        return gray;
    }

    private int[] _hist = new int[256];
    private byte[]? _buf;

    private void EnsureBuffers(int gw, int gh)
    {
        if (_gray == null || _gray.Length != gw * gh)
        {
            _gray = new byte[gw * gh];
            _buf = new byte[gw * 4 * gh];
            _scaledBmp?.Dispose();
            _scaledBmp = new Bitmap(gw, gh, PixelFormat.Format32bppArgb);
        }
        else if (_scaledBmp == null)
        {
            _scaledBmp = new Bitmap(gw, gh, PixelFormat.Format32bppArgb);
        }
    }

    public void Dispose()
    {
        _scaledBmp?.Dispose();
        _scaledBmp = null;
    }
}
