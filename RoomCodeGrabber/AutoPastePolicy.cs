namespace RoomCodeGrabber;

/// <summary>
/// 自动粘贴触发判定（纯逻辑，不触碰窗口系统）：仅当剪贴板写入成功且开关开启时注入。
/// 延迟在此统一夹取到 [0, Max]，配置层与 UI 不做二次校验。
/// </summary>
internal static class AutoPastePolicy
{
    internal const int MaxDelayMs = 10000;

    /// <summary>是否应注入粘贴；out 参数为夹取后的延迟毫秒数。</summary>
    internal static bool ShouldInject(bool writeSucceeded, bool enabled, int configuredDelayMs, out int delayMs)
    {
        delayMs = Math.Clamp(configuredDelayMs, 0, MaxDelayMs);
        return writeSucceeded && enabled;
    }
}
