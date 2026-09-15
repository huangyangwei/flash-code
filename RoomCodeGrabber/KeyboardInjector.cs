using System.Runtime.InteropServices;

namespace RoomCodeGrabber;

/// <summary>
/// 底层键盘注入：以携带 scancode 的 SendInput 合成按键，最接近真实键盘，
/// 兼容 DirectInput / 原始输入的游戏窗口。不走 SendKeys 的虚拟键消息——
/// 走消息的游戏窗口常常读不到。注入前检查 Ctrl 是否已被物理按住，避免
/// 打断用户正在进行的组合键（此刻注入语义不明，宁可跳过本次）。
/// </summary>
internal static class KeyboardInjector
{
    internal const ushort ScanCtrl = 0x1D;
    internal const ushort ScanV = 0x2F;
    internal const uint FlagKeyDown = 0x0008;             // KEYEVENTF_SCANCODE
    internal const uint FlagKeyUp = 0x0008 | 0x0002;      // KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP

    private const ushort VK_CONTROL = 0x11;
    private const uint INPUT_KEYBOARD = 1;

    /// <summary>一次按键描述（scancode + 标志位），纯数据，便于单测组装序列。</summary>
    internal readonly record struct KeyStroke(ushort Scan, uint Flags);

    /// <summary>Ctrl+V 的完整按键序列：Ctrl↓ V↓ V↑ Ctrl↑。</summary>
    internal static KeyStroke[] BuildCtrlVSequence() => new[]
    {
        new KeyStroke(ScanCtrl, FlagKeyDown),
        new KeyStroke(ScanV, FlagKeyDown),
        new KeyStroke(ScanV, FlagKeyUp),
        new KeyStroke(ScanCtrl, FlagKeyUp),
    };

    /// <summary>
    /// 向当前焦点窗口合成一次 Ctrl+V。返回 false 表示未注入
    /// （Ctrl 正被按住，或 SendInput 被系统拒绝，如目标是更高权限窗口）。
    /// </summary>
    internal static bool SendCtrlV()
    {
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) return false;   // 用户正按着 Ctrl：跳过

        var strokes = BuildCtrlVSequence();
        var inputs = new INPUT[strokes.Length];
        for (int i = 0; i < strokes.Length; i++)
        {
            inputs[i].type = INPUT_KEYBOARD;
            inputs[i].U.ki.wScan = strokes[i].Scan;
            inputs[i].U.ki.dwFlags = strokes[i].Flags;
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // 联合体按最大分支 MOUSEINPUT 取大小，否则 SendInput 收到残缺结构
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
