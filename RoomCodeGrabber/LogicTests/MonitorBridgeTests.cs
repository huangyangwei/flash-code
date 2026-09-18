using System.Collections.Concurrent;
using System.Text.Json;
using RoomCodeGrabber;

internal static class MonitorBridgeTests
{
    public static async Task<bool> RunAsync()
    {
        const string exampleCredential = "ttwid=bridge-fixture-only";
        var output = new ConcurrentQueue<string>();
        var pending = new SemaphoreSlim(0);
        var fetchCount = 0;
        string? usedCredential = null;
        const string first = """x\"flv_pull_url\":{\"FULL_HD1\":\"https://cdn/first.flv?sign=1\"}y""";
        const string next = """x\"flv_pull_url\":{\"FULL_HD1\":\"https://cdn/next.flv?sign=2\"}y""";
        var service = new LiveMonitorService((_, credential, _) =>
        {
            usedCredential = credential;
            return Task.FromResult(Interlocked.Increment(ref fetchCount) == 1 ? first : next);
        }, (_, stop) => pending.WaitAsync(stop));
        var config = new AppConfig();
        var saveCount = 0;
        await using var bridge = new MonitorBridge(config, service, output.Enqueue, _ => saveCount++);

        var invalid = Parse("monitor.start", new { room = "bad-room", intervalSec = 1 });
        await bridge.TryHandleAsync(invalid);
        if (fetchCount != 0 || saveCount != 0 || !output.Any(s =>
            JsonDocument.Parse(s).RootElement.GetProperty("payload").GetProperty("message").GetString() == "直播间链接或房间号无效")) return false;

        await bridge.TryHandleAsync(Parse("monitor.start", new { room = "https://live.douyin.com/123", intervalSec = 1, credential = exampleCredential }));
        await Until(() => output.Any(s => s.Contains("\"type\":\"monitor.stream\"")));
        var firstCount = output.Count(s => s.Contains("\"type\":\"monitor.stream\""));
        var safe = firstCount == 1 && usedCredential == exampleCredential && saveCount == 1
            && config.MonitorRoom == "123"
            && output.Where(s => s.Contains("\"type\":\"monitor.status\""))
                .All(s => !s.Contains(exampleCredential, StringComparison.Ordinal) && !s.Contains("first.flv", StringComparison.Ordinal))
            && !JsonSerializer.Serialize(config).Contains(exampleCredential, StringComparison.Ordinal);

        await bridge.TryHandleAsync(Parse("playback.status", new { state = "error" }));
        pending.Release();
        await Until(() => output.Count(s => s.Contains("\"type\":\"monitor.stream\"")) == 2);
        safe &= output.Any(s => s.Contains("next.flv", StringComparison.Ordinal));

        await bridge.TryHandleAsync(Parse("credential.clear", new { }));
        safe &= saveCount == 2 && usedCredential == exampleCredential
            && output.Any(s => s.Contains("\"credentialConfigured\":false"));
        await bridge.TryHandleAsync(Parse("monitor.stop", new { }));
        var countAfterStop = fetchCount;
        pending.Release();
        await Task.Delay(20);
        safe &= !service.GetStatus().Enabled && !service.GetStatus().HasStream && countAfterStop == fetchCount;
        Console.WriteLine(safe ? "PASS  宿主桥：监测接续、凭据隔离和安全停止" : "FAIL  宿主桥：监测接续、凭据隔离和安全停止");
        return safe;
    }

    private static BridgeCommand Parse(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { version = 1, type, payload });
        if (!BridgeProtocol.TryParseCommand(json, out var command, out var error)) throw new InvalidOperationException(error);
        return command!;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Bridge fixture did not advance");
            await Task.Delay(5);
        }
    }
}
