#if QA_FIXTURE
namespace RoomCodeGrabber;

internal static class MonitorQaFixture
{
    private const string Offline = """x\"status\":4,\"room_id_str\":\"123\"y""";
    private const string LiveFirst = """x\"flv_pull_url\":{\"HD1\":\"https://cdn.example/first.flv?sign=qa1\"}y""";
    private const string LiveFresh = """x\"flv_pull_url\":{\"HD1\":\"https://cdn.example/fresh.flv?sign=qa2\"}y""";

    public static LiveMonitorService Create()
    {
        var poll = 0;
        return new LiveMonitorService((_, _, _) => Task.FromResult(Interlocked.Increment(ref poll) switch
        {
            1 => Offline,
            2 => LiveFirst,
            3 => "<title>验证码中间页</title>",
            4 => LiveFirst,
            _ => LiveFresh,
        }), (_, token) => Task.Delay(1200, token));
    }
}
#endif
