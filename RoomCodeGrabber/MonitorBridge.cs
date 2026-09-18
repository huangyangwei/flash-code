using System.Text.Json;

namespace RoomCodeGrabber;

public sealed class MonitorBridge : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly ProtectedCredentialStore _credentials;
    private readonly LiveMonitorService _monitor;
    private readonly Action<string> _send;
    private readonly Action<AppConfig> _save;

    public MonitorBridge(AppConfig config, LiveMonitorService monitor, Action<string> send, Action<AppConfig> save)
    {
        _config = config;
        _credentials = new ProtectedCredentialStore(config);
        _monitor = monitor;
        _send = send;
        _save = save;
        _monitor.StatusChanged += PublishStatus;
        _monitor.StreamDiscovered += PublishStream;
    }

    public async Task<bool> TryHandleAsync(BridgeCommand command)
    {
        switch (command.Type)
        {
            case "monitor.start":
            {
                var room = command.Payload.GetProperty("room").GetString()!;
                var intervalSec = command.Payload.GetProperty("intervalSec").GetInt32();
                var normalized = RoomPageParser.NormalizeRoom(room);
                if (!normalized.Ok)
                {
                    SendError("直播间链接或房间号无效");
                    return true;
                }
                if (command.Payload.TryGetProperty("credential", out var supplied) &&
                    supplied.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(supplied.GetString()))
                    _credentials.Set(supplied.GetString());
                var credential = _credentials.Get();
                if (_credentials.IsConfigured && credential is null)
                {
                    SendError("已保存的 Cookie 无法读取，请清除后重新填写");
                    return true;
                }
                _config.MonitorRoom = normalized.Room!;
                _config.MonitorIntervalSeconds = intervalSec;
                _save(_config);
                await _monitor.StartAsync(normalized.Room!, intervalSec, credential);
                return true;
            }
            case "monitor.stop":
                await _monitor.StopAsync();
                return true;
            case "credential.clear":
                _credentials.Clear();
                _save(_config);
                var status = _monitor.GetStatus();
                if (status.Enabled) await _monitor.StartAsync(status.Room!, status.IntervalSec);
                PublishStatus(_monitor.GetStatus());
                return true;
            case "playback.status":
                if (command.Payload.GetProperty("state").GetString() == "error") _monitor.RequestRefresh();
                return true;
            default:
                return false;
        }
    }

    public void PublishStatus(MonitorStatus status) => _send(BridgeProtocol.SerializeEvent("monitor.status", new
    {
        enabled = status.Enabled,
        room = status.Room ?? _config.MonitorRoom,
        intervalSec = status.IntervalSec,
        live = status.Live,
        hasStream = status.HasStream,
        error = status.Error,
        lastCheckAt = status.LastCheckAt,
        failCount = status.FailCount,
        credentialConfigured = _credentials.IsConfigured,
    }));

    public MonitorStatus GetStatus() => _monitor.GetStatus();

    private void PublishStream(string url) => _send(BridgeProtocol.SerializeEvent("monitor.stream", new { url, source = "monitor" }));

    private void SendError(string message) => _send(BridgeProtocol.SerializeEvent("app.error", new { message }));

    public async ValueTask DisposeAsync()
    {
        _monitor.StatusChanged -= PublishStatus;
        _monitor.StreamDiscovered -= PublishStream;
        await _monitor.DisposeAsync();
    }
}
