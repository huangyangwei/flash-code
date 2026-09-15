namespace RoomCodeGrabber;

/// <summary>
/// 自动粘贴调度：判定 → （异步）延迟等待 → 注入。注入动作与执行器以委托注入，
/// LogicTests 用 mock 记录调用次数而不真正发键。默认执行器 Task.Run，
/// 延迟等待发生在后台线程，不阻塞抓帧循环。
/// </summary>
internal sealed class AutoPasteScheduler
{
    private readonly Func<bool> _send;
    private readonly Action<Action> _run;

    /// <param name="send">注入动作，返回是否成功。</param>
    /// <param name="run">执行器，决定延迟等待跑在哪个线程。</param>
    internal AutoPasteScheduler(Func<bool> send, Action<Action> run)
    {
        _send = send;
        _run = run;
    }

    /// <summary>剪贴板写入完成后调用；写入失败或开关关闭时什么都不做。</summary>
    internal void AfterWrite(bool writeSucceeded, bool enabled, int configuredDelayMs)
    {
        if (!AutoPastePolicy.ShouldInject(writeSucceeded, enabled, configuredDelayMs, out int delay)) return;
        _run(() =>
        {
            if (delay > 0) Thread.Sleep(delay);
            _send();
        });
    }
}
