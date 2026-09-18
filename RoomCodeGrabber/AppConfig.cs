using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomCodeGrabber;

public sealed class AppConfig
{
    public const int MinMonitorIntervalSeconds = 1;
    public const int MaxMonitorIntervalSeconds = 300;
    public const int MinSpeedThresholdMs = 0;
    public const int MaxSpeedThresholdMs = 60000;
    public const double MinSpeedRate = 1.05;
    public const double MaxSpeedRate = 2.0;
    public const int MaxJumpThresholdMs = 120000;

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

    // 复制成功后多少秒自动清空剪贴板（0 = 禁用）。仅在内容仍是程序写入的房间码时才清。
    public int ClipboardClearSeconds { get; set; } = 180;

    // 抓到码写入剪贴板后，向当前焦点窗口自动粘贴一次 Ctrl+V。默认关闭；
    // 目标窗口由用户自行聚焦，程序不做窗口选择。游戏窗口能否收到需真机验证
    public bool AutoPasteEnabled { get; set; } = false;

    // 自动粘贴前等待的毫秒数（0 = 写入后立即粘贴），给用户切窗口/聚焦输入框的时间
    public int AutoPasteDelayMs { get; set; } = 0;

    // 可选：复制成功后先点击记录的屏幕位置，再向取得焦点的窗口粘贴。
    public bool AutoClickEnabled { get; set; } = false;
    public Point? AutoClickPosition { get; set; }

    // OCR 引擎选择。Windows = 系统自带（零依赖，任何机器可用，默认）；
    // Paddle = PaddleOCR GPU（需 NVIDIA 独显 + sm_120 CUDA runtime，识别率更高，用于对比切换）
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Windows;

    // 抓到码时是否提示音
    public bool SoundEnabled { get; set; } = true;

    // 开播监测：房间号或抖音直播间 URL；空值表示尚未配置
    public string MonitorRoom { get; set; } = "";

    // 正常轮询间隔。失败退避在 LiveMonitorService 内独立计算
    public int MonitorIntervalSeconds { get; set; } = 15;

    // 低延迟播放的追帧参数（前端只展示宿主确认后的值）
    public bool CatchupEnabled { get; set; } = true;
    public int SpeedThresholdMs { get; set; } = 1000;
    public double SpeedRate { get; set; } = 1.1;
    public int? JumpThresholdMs { get; set; } = 2000;

    // DPAPI 密文；明文永不进入 JSON。由 ProtectedCredentialStore 管理。
    public string? ProtectedMonitorCredential { get; set; }

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
        => LoadFrom(FilePath);

    public static AppConfig LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
                if (cfg != null) return cfg.Sanitize();
            }
        }
        catch
        {
            // 配置损坏时退回默认值
        }
        return new AppConfig();
    }

    public void Save()
        => SaveTo(FilePath);

    public void SaveTo(string path)
    {
        Sanitize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }

    public AppConfig Sanitize()
    {
        Pattern = string.IsNullOrWhiteSpace(Pattern) || Pattern.Length > 256
            ? @"[A-Z0-9]{6}"
            : Pattern;
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 600);
        ConfirmFrames = Math.Clamp(ConfirmFrames, 1, 4);
        CaptureIntervalMs = Math.Clamp(CaptureIntervalMs, 10, 200);
        ScaleFactor = Math.Clamp(ScaleFactor, 2, 6);
        ClipboardClearSeconds = Math.Clamp(ClipboardClearSeconds, 0, 3600);
        AutoPasteDelayMs = Math.Clamp(AutoPasteDelayMs, 0, AutoPastePolicy.MaxDelayMs);
        if (AutoClickPosition is { } position &&
            (position.X is < -32768 or > 32767 || position.Y is < -32768 or > 32767))
            AutoClickPosition = null;
        if (AutoClickPosition is null) AutoClickEnabled = false;
        if (!Enum.IsDefined(OcrEngine)) OcrEngine = OcrEngineKind.Windows;

        MonitorRoom = (MonitorRoom ?? "").Trim();
        if (MonitorRoom.Length > 512) MonitorRoom = MonitorRoom[..512];
        MonitorIntervalSeconds = Math.Clamp(
            MonitorIntervalSeconds,
            MinMonitorIntervalSeconds,
            MaxMonitorIntervalSeconds);

        SpeedThresholdMs = Math.Clamp(SpeedThresholdMs, MinSpeedThresholdMs, MaxSpeedThresholdMs);
        SpeedRate = double.IsFinite(SpeedRate)
            ? Math.Clamp(SpeedRate, MinSpeedRate, MaxSpeedRate)
            : 1.1;
        if (JumpThresholdMs is { } jump)
            JumpThresholdMs = Math.Clamp(jump, 1, MaxJumpThresholdMs);

        if (string.IsNullOrWhiteSpace(ProtectedMonitorCredential))
            ProtectedMonitorCredential = null;

        return this;
    }
}

/// <summary>OCR 引擎种类。JSON 中按整数值持久化（Windows=0, Paddle=1）。</summary>
public enum OcrEngineKind
{
    Windows,
    Paddle,
}
