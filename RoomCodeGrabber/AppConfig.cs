using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomCodeGrabber;

public sealed class AppConfig
{
    // 抓取区域（屏幕像素坐标），null 表示尚未框选
    public Rectangle? Region { get; set; }

    // 房间码正则：3 位大写字母 + 3 位数字（纠正前的宽松版会由 CodeDetector 处理）
    public string Pattern { get; set; } = @"[A-Z0-9]{6}";

    // 同一个码的冷却秒数，防止画面抖动导致的重复复制
    public int CooldownSeconds { get; set; } = 30;

    // 确认帧数：同一码连续 N 帧一致才复制。>1 可过滤 OCR 乱码帧，
    // 代价是延迟增加约 N-1 个抓帧间隔（20ms/帧），100ms 预算内建议 ≤2
    public int ConfirmFrames { get; set; } = 1;

    // 启动即抓：开始抓取时画面上已有的码也复制（直播开播时的码通常仍有效）。
    // 关闭则压制启动时已存在的码，等它消失后重新出现才触发
    public bool GrabOnStart { get; set; } = true;

    // 抓帧循环间隔（ms）。50fps = 20ms，是延迟预算里"等帧"的上限
    public int CaptureIntervalMs { get; set; } = 20;

    // 预处理放大倍数。3x 在测试截图上全命中；码特别小可调 4，追求低延迟可调 2
    public int ScaleFactor { get; set; } = 3;

    // OCR 引擎选择。Windows = 系统自带（零依赖，任何机器可用，默认）；
    // Paddle = PaddleOCR GPU（需 NVIDIA 独显 + sm_120 CUDA runtime，识别率更高，用于对比切换）
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Windows;

    // 抓到码时是否提示音
    public bool SoundEnabled { get; set; } = true;

    [JsonIgnore]
    public static string FilePath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            return Path.Combine(dir, "config.json");
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath));
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // 配置损坏时退回默认值
        }
        return new AppConfig();
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, options));
    }
}

/// <summary>OCR 引擎种类。JSON 中按整数值持久化（Windows=0, Paddle=1）。</summary>
public enum OcrEngineKind
{
    Windows,
    Paddle,
}
