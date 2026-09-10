using System.Runtime.Versioning;

namespace RoomCodeGrabber;

[SupportedOSPlatform("windows10.0.17763.0")]
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // PerMonitorV2：保证框选坐标与实际屏幕像素一致（多显示器/缩放下不出错）
        ApplicationConfiguration.Initialize();

        var config = AppConfig.Load();
        using var engine = new CaptureEngine(config);

        Application.Run(new MainForm(config, engine));
    }
}
