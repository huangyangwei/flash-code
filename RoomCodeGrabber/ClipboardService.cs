using System.Runtime.InteropServices;

namespace RoomCodeGrabber;

/// <summary>
/// 低延迟剪贴板写入：直接走 Win32 API（同步、在调用线程完成），
/// 避开 WinForms Clipboard.SetText 的重试机制。单次写入 <1ms；
/// 剪贴板被占用时按 1ms 间隔重试（仍远小于 100ms 预算）。
/// </summary>
internal static class ClipboardService
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>同步写入文本到剪贴板。失败重试约 20ms 后放弃。</summary>
    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (TrySetClipboard(text)) return true;
            Thread.Sleep(1);
        }
        return false;
    }

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

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
