namespace RoomCodeGrabber;

public sealed record MonitorStatus(
    bool Enabled = false,
    string? Room = null,
    int IntervalSec = 15,
    bool Live = false,
    bool HasStream = false,
    string? Error = null,
    DateTimeOffset? LastCheckAt = null,
    int FailCount = 0,
    bool CredentialConfigured = false);

public sealed class LiveMonitorService : IAsyncDisposable
{
    private readonly Func<string, string?, CancellationToken, Task<string>> _fetch;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _hardCap;
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private MonitorStatus _status = new();
    private string? _credential;
    private bool? _lastLive;
    private string? _handedUrl;
    private bool _refreshRequested;

    public event Action<MonitorStatus>? StatusChanged;
    public event Action<string>? StreamDiscovered;

    public LiveMonitorService(
        Func<string, string?, CancellationToken, Task<string>> fetch,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? requestTimeout = null,
        TimeSpan? hardCap = null)
    {
        _fetch = fetch;
        _delay = delay ?? Task.Delay;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _hardCap = hardCap ?? TimeSpan.FromSeconds(20);
        if (_requestTimeout <= TimeSpan.Zero || _hardCap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public static LiveMonitorService ForHttpClient(HttpClient client) => new(async (room, credential, token) =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://live.douyin.com/{room}");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36");
        request.Headers.Referrer = new Uri($"https://live.douyin.com/{room}");
        if (!string.IsNullOrEmpty(credential)) request.Headers.TryAddWithoutValidation("Cookie", credential);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    });

    public MonitorStatus GetStatus() { lock (_gate) return _status; }

    public async Task<RoomInputResult> StartAsync(string input, int intervalSec, string? credential = null)
    {
        var normalized = RoomPageParser.NormalizeRoom(input);
        if (!normalized.Ok) return normalized;
        if (intervalSec is < 1 or > 300) return new(false, null, "interval-out-of-range");

        MonitorStatus? updated = null;
        lock (_gate)
        {
            if (_stop is not null && _status.Room == normalized.Room)
            {
                _credential = credential;
                _status = _status with { IntervalSec = intervalSec, CredentialConfigured = credential is not null, FailCount = 0, Error = null };
                updated = _status;
            }
        }
        if (updated is not null)
        {
            PublishStatus(updated);
            return normalized;
        }

        await StopAsync();
        lock (_gate)
        {
            _credential = credential;
            _lastLive = null;
            _handedUrl = null;
            _refreshRequested = false;
            _status = new MonitorStatus(true, normalized.Room, intervalSec, CredentialConfigured: credential is not null);
            var stop = new CancellationTokenSource();
            _stop = stop;
            _worker = Task.Run(() => RunAsync(stop.Token));
        }
        PublishStatus(GetStatus());
        return normalized;
    }

    public void RequestRefresh()
    {
        lock (_gate) if (_status.Enabled && _status.Live) _refreshRequested = true;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? stop;
        Task? worker;
        lock (_gate)
        {
            stop = _stop;
            worker = _worker;
            _stop = null;
            _worker = null;
            _credential = null;
            _lastLive = null;
            _handedUrl = null;
            _refreshRequested = false;
            _status = _status with { Enabled = false, Live = false, HasStream = false, Error = null, FailCount = 0 };
        }
        stop?.Cancel();
        if (worker is not null)
        {
            try { await worker; }
            catch (OperationCanceledException) { }
        }
        stop?.Dispose();
        PublishStatus(GetStatus());
    }

    public static TimeSpan Backoff(int intervalSec, int consecutiveFailures)
    {
        var millis = Math.Clamp((long)intervalSec * 1000L, 1000L, 300000L);
        for (var i = 0; i < consecutiveFailures && millis < 300000L; i++)
            millis = Math.Min(millis * 2, 300000L);
        return TimeSpan.FromMilliseconds(millis);
    }

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            string room;
            string? credential;
            int intervalSec;
            lock (_gate)
            {
                room = _status.Room!;
                credential = _credential;
                intervalSec = _status.IntervalSec;
            }
            string? discovered = null;
            MonitorStatus snapshot;
            try
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                request.CancelAfter(_requestTimeout);
                string page;
                try { page = await _fetch(room, credential, request.Token).WaitAsync(_hardCap, stop); }
                catch (TimeoutException)
                {
                    request.Cancel();
                    throw;
                }
                var parsed = RoomPageParser.Parse(page);
                if (!parsed.Ok) throw new FormatException(parsed.Reason);
                if (stop.IsCancellationRequested) return;

                lock (_gate)
                {
                    if (stop.IsCancellationRequested) return;
                    if (parsed.Live && (!_lastLive.GetValueOrDefault() ||
                        (_refreshRequested && parsed.BestUrl != _handedUrl)))
                    {
                        discovered = parsed.BestUrl;
                        _handedUrl = discovered;
                        _refreshRequested = false;
                    }
                    if (!parsed.Live) { _handedUrl = null; _refreshRequested = false; }
                    _lastLive = parsed.Live;
                    _status = _status with { Live = parsed.Live, HasStream = parsed.Live, Error = null, FailCount = 0, LastCheckAt = DateTimeOffset.UtcNow };
                    snapshot = _status;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                if (stop.IsCancellationRequested) return;
                var message = error switch
                {
                    FormatException { Message: "captcha" } => "房间验证中，请在浏览器完成验证后重试",
                    FormatException => "房间页格式无法识别",
                    TimeoutException or OperationCanceledException => "房间请求超时",
                    HttpRequestException http when http.StatusCode is not null => $"房间请求失败 (HTTP {(int)http.StatusCode})",
                    _ => "房间请求失败，请检查网络",
                };
                lock (_gate)
                {
                    if (stop.IsCancellationRequested) return;
                    _status = _status with { Error = message, FailCount = _status.FailCount + 1, LastCheckAt = DateTimeOffset.UtcNow };
                    snapshot = _status;
                }
            }

            PublishStatus(snapshot);
            if (discovered is not null && !stop.IsCancellationRequested) StreamDiscovered?.Invoke(discovered);
            try
            {
                var wait = snapshot.FailCount > 0 ? Backoff(intervalSec, snapshot.FailCount) : TimeSpan.FromSeconds(intervalSec);
                await _delay(wait, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        }
    }

    private void PublishStatus(MonitorStatus snapshot) => StatusChanged?.Invoke(snapshot);

    public async ValueTask DisposeAsync() => await StopAsync();
}
