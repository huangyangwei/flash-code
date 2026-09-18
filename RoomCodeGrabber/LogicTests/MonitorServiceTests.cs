using System.Collections.Concurrent;
using System.Text.Json;
using RoomCodeGrabber;

internal static class MonitorServiceTests
{
    private const string LiveA = """x\"flv_pull_url\":{\"FULL_HD1\":\"http://cdn/first.flv?sign=1\"}y""";
    private const string LiveB = """x\"flv_pull_url\":{\"FULL_HD1\":\"http://cdn/next.flv?sign=2\"}y""";
    private const string Offline = """x\"status\":4y""";

    public static async Task<bool> RunAsync()
    {
        var passed = await ColdStartAndStop() && await RepeatedStartStop()
            && await TransitionFailureAndRefresh()
            && await HardTimeoutAndCredentialSafety() && await RequestTimeout();
        passed &= LiveMonitorService.Backoff(15, 1) == TimeSpan.FromSeconds(30)
            && LiveMonitorService.Backoff(15, 2) == TimeSpan.FromSeconds(60)
            && LiveMonitorService.Backoff(15, 5) == TimeSpan.FromSeconds(300);
        Console.WriteLine(passed ? "PASS  轮询：开播沿、退避、超时、刷新与安全停止" : "FAIL  轮询：开播沿、退避、超时、刷新与安全停止");
        return passed;
    }

    private static async Task<bool> RepeatedStartStop()
    {
        var calls = 0;
        var events = 0;
        await using var monitor = new LiveMonitorService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(LiveA);
        }, (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        monitor.StreamDiscovered += _ => Interlocked.Increment(ref events);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await monitor.StartAsync("123", 1);
            await Until(() => Volatile.Read(ref events) == attempt);
            await monitor.StopAsync();
            var afterStop = Volatile.Read(ref calls);
            await Task.Delay(20);
            if (monitor.GetStatus().Enabled || Volatile.Read(ref calls) != afterStop) return false;
        }
        return events == 3;
    }

    private static async Task<bool> ColdStartAndStop()
    {
        var calls = 0;
        var delay = new ControlledDelay();
        var streams = new ConcurrentQueue<string>();
        await using var monitor = new LiveMonitorService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(LiveA);
        }, delay.Wait);
        monitor.StreamDiscovered += streams.Enqueue;
        var invalid = await monitor.StartAsync("bad-room", 15);
        if (invalid.Ok || calls != 0) return false;
        var started = await monitor.StartAsync("https://live.douyin.com/123?x=1", 15);
        await Until(() => calls >= 1 && monitor.GetStatus().Live);
        if (!started.Ok || monitor.GetStatus().Room != "123" || streams.Count != 1) return false;
        var updated = await monitor.StartAsync("123", 20);
        await delay.NextAsync();
        await Until(() => calls >= 2);
        if (!updated.Ok || streams.Count != 1 || monitor.GetStatus().IntervalSec != 20) return false;
        await monitor.StopAsync();
        var afterStop = calls;
        await delay.NextAsync();
        await Task.Delay(20);
        return afterStop == calls && !monitor.GetStatus().Enabled && !monitor.GetStatus().HasStream;
    }

    private static async Task<bool> TransitionFailureAndRefresh()
    {
        var responses = new Queue<object>(new object[] { Offline, new Exception("temporary"), LiveA, new Exception("temporary"), LiveA, LiveB });
        var delay = new ControlledDelay();
        var streams = new ConcurrentQueue<string>();
        await using var monitor = new LiveMonitorService((_, _, _) => Task.FromResult((string)(responses.Dequeue() is { } response && response is not Exception ? response : throw new HttpRequestException())), delay.Wait);
        monitor.StreamDiscovered += streams.Enqueue;
        await monitor.StartAsync("123", 1);
        await Until(() => monitor.GetStatus().LastCheckAt is not null);
        if (monitor.GetStatus().Live) return false;
        await delay.NextAsync();
        await Until(() => monitor.GetStatus().FailCount == 1);
        if (monitor.GetStatus().Live || delay.LastWait != TimeSpan.FromSeconds(2)) return false;
        await delay.NextAsync();
        await Until(() => monitor.GetStatus().Live && streams.Count == 1);
        await delay.NextAsync();
        await Until(() => monitor.GetStatus().FailCount == 1);
        if (!monitor.GetStatus().Live || streams.Count != 1) return false;
        await delay.NextAsync();
        await Until(() => monitor.GetStatus().FailCount == 0);
        if (streams.Count != 1) return false;
        monitor.RequestRefresh();
        await delay.NextAsync();
        await Until(() => streams.Count == 2);
        await monitor.StopAsync();
        return streams.TryDequeue(out var first) && first == "http://cdn/first.flv?sign=1"
            && streams.TryDequeue(out var second) && second == "http://cdn/next.flv?sign=2";
    }

    private static async Task<bool> HardTimeoutAndCredentialSafety()
    {
        const string exampleCredential = "ttwid=example-only";
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        string? seenCredential = null;
        var delay = new ControlledDelay();
        await using var monitor = new LiveMonitorService((_, credential, _) =>
        {
            Interlocked.Increment(ref calls);
            seenCredential = credential;
            return pending.Task;
        }, delay.Wait, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(25));
        await monitor.StartAsync("123", 1, exampleCredential);
        await Until(() => monitor.GetStatus().FailCount == 1);
        var snapshot = monitor.GetStatus();
        var safe = calls == 1 && snapshot.CredentialConfigured && seenCredential == exampleCredential
            && snapshot.Error == "房间请求超时"
            && !JsonSerializer.Serialize(snapshot).Contains(exampleCredential, StringComparison.Ordinal);
        await monitor.StopAsync();
        pending.TrySetResult(Offline);
        return safe && !monitor.GetStatus().Enabled;
    }

    private static async Task<bool> RequestTimeout()
    {
        var delay = new ControlledDelay();
        await using var monitor = new LiveMonitorService(
            async (_, _, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return LiveA; },
            delay.Wait, TimeSpan.FromMilliseconds(15), TimeSpan.FromSeconds(1));
        await monitor.StartAsync("123", 1);
        await Until(() => monitor.GetStatus().FailCount == 1);
        var timedOut = monitor.GetStatus().Error == "房间请求超时";
        await monitor.StopAsync();
        return timedOut;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Monitor fixture did not advance");
            await Task.Delay(5);
        }
    }

    private sealed class ControlledDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _waiters = new();
        public TimeSpan LastWait { get; private set; }

        public Task Wait(TimeSpan duration, CancellationToken stop)
        {
            LastWait = duration;
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            stop.Register(() => waiter.TrySetCanceled(stop));
            return waiter.Task;
        }

        public async Task NextAsync()
        {
            await Until(() => !_waiters.IsEmpty);
            if (!_waiters.TryDequeue(out var waiter)) throw new InvalidOperationException("No scheduled monitor wait");
            waiter.TrySetResult();
        }
    }
}
