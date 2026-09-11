using System.Runtime.InteropServices;
using Timer = System.Threading.Timer;   // 主项目 UseWindowsForms 引入了 WinForms Timer，此处显式消歧

namespace RoomCodeGrabber;

/// <summary>
/// 低延迟剪贴板写入 + 可选的延迟自动清空。直接走 Win32 API（同步、在调用线程完成），
/// 避开 WinForms Clipboard.SetText 的重试机制。单次写入 &lt;1ms；剪贴板被占用时按
/// 1ms 间隔重试（仍远小于 100ms 预算）。
///
/// 自动清空：写入成功后调度一个后台定时器；到期时读取剪贴板，仅当内容仍逐字等于
/// 本次写入的文本才清空（判定见 <see cref="ClipboardClearPolicy"/>），
/// 因此用户若在延迟期内复制了别的内容，不会被误清。
/// </summary>
internal static class ClipboardService
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int RetryCount = 20;

    // ---- 自动清空状态（由 _gate 保护）----
    private static readonly object _gate = new();
    private static Timer? _clearTimer;
    private static string? _writtenText;

    /// <summary>同步写入文本到剪贴板。失败重试约 20ms 后放弃。</summary>
    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            if (TrySetClipboard(text)) return true;
            Thread.Sleep(1);
        }
        return false;
    }

    /// <summary>
    /// 写入文本，并在 seconds 秒后自动清空剪贴板（前提：届时内容仍是本次写入的文本）。
    /// seconds &lt;= 0 表示禁用自动清空，等价于 <see cref="SetText"/>。
    /// </summary>
    public static bool SetTextWithAutoClear(string text, int seconds)
    {
        if (!SetText(text)) return false;   // 写入失败：不调度，避免清掉非本次写入的内容
        if (seconds <= 0) return true;

        lock (_gate)
        {
            _writtenText = text;
            _clearTimer ??= new Timer(OnClearDue, null, Timeout.Infinite, Timeout.Infinite);
            _clearTimer.Change(seconds * 1000, Timeout.Infinite);
        }
        return true;
    }

    /// <summary>读取剪贴板文本。剪贴板无文本格式（图片/文件/空）时返回 null。</summary>
    public static string? GetText()
    {
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            if (TryGetText(out var text)) return text;
            Thread.Sleep(1);
        }
        return null;
    }

    /// <summary>清空剪贴板（彻底清空，不留文本格式）。</summary>
    public static bool Clear()
    {
        for (int attempt = 0; attempt < RetryCount; attempt++)
        {
            if (TryClear()) return true;
            Thread.Sleep(1);
        }
        return false;
    }

    /// <summary>释放后台定时器（程序退出时调用，避免残留回调）。</summary>
    public static void Shutdown()
    {
        lock (_gate)
        {
            _clearTimer?.Dispose();
            _clearTimer = null;
            _writtenText = null;
        }
    }

    private static void OnClearDue(object? _)
    {
        string? written;
        lock (_gate) { written = _writtenText; }
        if (written == null) return;

        var current = GetText();
        if (ClipboardClearPolicy.ShouldClear(written, current, delayElapsed: true))
            Clear();
    }

    // ---- Win32 原语 ----

    private static bool TrySetClipboard(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (hMem == IntPtr.Zero) return false;

            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            // 成功后系统接管 hMem，不要释放
            return SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// 尝试读取。返回 false 表示剪贴板被占用（应重试）；
    /// 返回 true 且 text 为 null 表示读取成功但无文本格式（图片/文件/空）。
    /// </summary>
    private static bool TryGetText(out string? text)
    {
        text = null;
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return true;   // 无文本格式
            var ptr = GlobalLock(h);
            if (ptr == IntPtr.Zero) return true;
            try { text = Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(h); }
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryClear()
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try { return EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
