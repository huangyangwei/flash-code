using System.Drawing;

namespace RoomCodeGrabber;

/// <summary>
/// 自动粘贴调度：判定 → （异步）延迟等待 → 注入。注入动作与执行器以委托注入，
/// LogicTests 用 mock 记录调用次数而不真正发键。默认执行器 Task.Run，
/// 延迟等待发生在后台线程，不阻塞抓帧循环。
/// </summary>
internal sealed class AutoPasteScheduler : IDisposable
{
    private readonly Func<bool> _send;
    private readonly Func<Point, bool> _click;
    private readonly Action<Action> _run;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _gate = new();
    private int _generation;

    /// <param name="send">注入动作，返回是否成功。</param>
    /// <param name="run">执行器，决定延迟等待跑在哪个线程。</param>
    internal AutoPasteScheduler(Func<bool> send, Func<Point, bool> click, Action<Action> run)
    {
        _send = send;
        _click = click;
        _run = run;
    }

    /// <summary>剪贴板写入完成后调用；写入失败或开关关闭时什么都不做。</summary>
    internal void AfterWrite(bool writeSucceeded, bool pasteEnabled, int configuredDelayMs,
        bool clickEnabled, Point? clickPosition)
    {
        if (writeSucceeded) Interlocked.Increment(ref _generation);
        if (!writeSucceeded || (!pasteEnabled && !clickEnabled) || (clickEnabled && clickPosition is null)) return;
        AutoPastePolicy.ShouldInject(writeSucceeded, pasteEnabled, configuredDelayMs, out int delay);
        var token = _cancel.Token;
        int generation = Volatile.Read(ref _generation);
        _run(() =>
        {
            try
            {
                if (delay > 0) Task.Delay(delay, token).GetAwaiter().GetResult();
                lock (_gate)
                {
                    if (token.IsCancellationRequested || generation != Volatile.Read(ref _generation)) return;
                    if (clickEnabled && !_click(clickPosition!.Value)) return;
                    if (pasteEnabled && !token.IsCancellationRequested) _send();
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Dispose()
    {
        lock (_gate) _cancel.Cancel();
    }
}
