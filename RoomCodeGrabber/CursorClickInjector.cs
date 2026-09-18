using System.Drawing;
using System.Runtime.InteropServices;

namespace RoomCodeGrabber;

internal static class CursorClickInjector
{
    private const uint InputMouse = 0;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RootWindow = 2;
    private const int VirtualX = 76, VirtualY = 77, VirtualWidth = 78, VirtualHeight = 79;

    internal static bool IsTargetSafe(Point point)
    {
        var bounds = new Rectangle(GetSystemMetrics(VirtualX), GetSystemMetrics(VirtualY),
            GetSystemMetrics(VirtualWidth), GetSystemMetrics(VirtualHeight));
        if (!bounds.Contains(point)) return false;
        var window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(GetAncestor(window, RootWindow), out var processId);
        return processId != Environment.ProcessId;
    }

    internal static bool ClickAt(Point point)
    {
        if (!IsTargetSafe(point) || !ModifiersAndButtonsReleased() || !GetCursorPos(out var previous)) return false;
        if (!SetCursorPos(point.X, point.Y)) return false;
        try
        {
            if (!IsTargetSafe(point)) return false;
            var inputs = new[] { Mouse(LeftDown), Mouse(LeftUp) };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent == 1) SendInput(1, new[] { Mouse(LeftUp) }, Marshal.SizeOf<INPUT>());
            return sent == inputs.Length;
        }
        finally
        {
            if (GetCursorPos(out var current) && current.X == point.X && current.Y == point.Y)
                SetCursorPos(previous.X, previous.Y);
        }
    }

    private static bool ModifiersAndButtonsReleased()
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x01, 0x02, 0x04 })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
        return true;
    }

    private static INPUT Mouse(uint flags) => new() { type = InputMouse, mouse = new MOUSEINPUT { dwFlags = flags } };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEINPUT mouse; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
