namespace RoomCodeGrabber;

public static class CaptureBridgeEvents
{
    public static string Copied(string code, double latencyMs)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || !double.IsFinite(latencyMs) || latencyMs < 0)
            throw new ArgumentOutOfRangeException(nameof(code));
        return BridgeProtocol.SerializeEvent("capture.code", new { code, latencyMs });
    }

    public static string ClipboardFailure() => BridgeProtocol.SerializeEvent("app.error", new
    {
        message = "剪贴板暂不可用，房间码未复制；请检查占用后继续抓取",
    });
}
