namespace RoomCodeGrabber;

/// <summary>
/// 剪贴板自动清空的判定逻辑。刻意做成不含任何 Win32 P/Invoke 的纯函数，
/// 以便被 LogicTests（独立程序集、net8.0 目标）直接引用并测试。
/// </summary>
public static class ClipboardClearPolicy
{
    /// <summary>
    /// 是否应清空剪贴板：仅当延迟已到 <b>且</b> 当前内容与程序写入的内容逐字相同。
    /// 这样用户若在延迟期内复制了别的内容，就不会被误清（fail-safe）。
    /// </summary>
    /// <param name="written">程序写入剪贴板的内容；未写入过则为 null。</param>
    /// <param name="current">当前剪贴板文本；非文本格式（图片/文件/空）时为 null。</param>
    /// <param name="delayElapsed">延迟是否已到。</param>
    public static bool ShouldClear(string? written, string? current, bool delayElapsed)
        => delayElapsed && written != null && current == written;
}
