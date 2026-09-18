using RoomCodeGrabber;

var cases = new (string input, string? expected)[]
{
    ("ASD123", "ASD123"),                     // 标准格式
    ("#ASD123", "ASD123"),                    // 带 # 前缀
    ("A5D123", "ASD123"),                     // 字母位数字误认 5->S
    ("ASD1O3", "ASD103"),                     // 数字位字母误认 O->0
    ("ASDI23", "ASD123"),                     // 数字位字母误认 I->1
    ("房间码 ASD123 上车", "ASD123"),          // 嵌在中文里
    ("660233", null),                          // 纯数字 -> 拒绝（宁漏勿错）
    ("ABCD123", null),                         // 7 位长串 -> 不应截取
    ("ASD12", null),                           // 位数不足
    ("ASD1234", null),                         // 位数超出
    ("A5D1O3", "ASD103"),                      // 双重混淆
    ("代码 AS5D123 混杂", null),                // 7 位 token 也不该匹配
    ("abc123", "ABC123"),                      // OCR 全大写化由上层做，这里输入已大写则原样；小写应拒绝
    ("OZBÅS9", "OZB459"),                      // 4->å(变音符) + 5->S 双重混淆，去音符+数字位纠正
    ("0ZB459", "OZB459"),                      // 字母位 1 位纠正 O<-0
    ("400259", null),                          // 纯数字：字母位需 2 位纠正 -> 拒绝（防假码）
    ("Biilftß OZB459", "OZB459"),              // 真实案例：前面混入乱词 + ß，码本身完整
};

int pass = 0, fail = 0;
foreach (var (input, expected) in cases)
{
    // 与引擎一致：先大写化
    var got = string.Join(",", CodeDetector.ExtractCodes(input.ToUpperInvariant()));
    var want = expected == null ? "" : expected;
    if (got == want) { pass++; Console.WriteLine($"PASS  \"{input}\" -> [{got}]"); }
    else { fail++; Console.WriteLine($"FAIL  \"{input}\" -> [{got}]  期望 [{want}]"); }
}
Console.WriteLine($"\n{pass} passed, {fail} failed");

// 新码判定/去重/冷却（GrabOnStart=false：启动时已有码被压制）
var det = new CodeDetector(30, 1, grabOnStart: false);
var t1 = det.Analyze(new List<string> { "ASD123" });          // 首帧 -> 不触发
var t2 = det.Analyze(new List<string> { "ASD123" });          // 同码持续 -> 不触发
var t3 = det.Analyze(new List<string> { "ASD123", "XYZ789" }); // 新码出现 -> 触发 XYZ789
var t4 = det.Analyze(new List<string> { "XYZ789" });          // ASD123 消失 -> 无触发
var t5 = det.Analyze(new List<string> { "ASD123", "XYZ789" }); // 冷却期内 ASD123 重现 -> 不触发
bool dupOk = t1 == null && t2 == null && t3 == "XYZ789" && t4 == null && t5 == null;
Console.WriteLine(dupOk ? "PASS  新码判定/去重/冷却" : $"FAIL  新码判定: {t1},{t2},{t3},{t4},{t5}");

// 启动即抓（GrabOnStart=true：启动时已有的码在满确认帧后触发一次）
var det2 = new CodeDetector(30, 2, grabOnStart: true);
var g1 = det2.Analyze(new List<string> { "SAT033" });          // 首帧只记录
var g2 = det2.Analyze(new List<string> { "SAT033" });          // 第2帧满确认 -> 触发
var g3 = det2.Analyze(new List<string> { "SAT033" });          // 持续可见 -> 不重复
var g4 = det2.Analyze(new List<string>());                     // 消失
var g5 = det2.Analyze(new List<string> { "SAT033" });          // 冷却期内重现 -> 不触发
bool grabOk = g1 == null && g2 == "SAT033" && g3 == null && g4 == null && g5 == null;
Console.WriteLine(grabOk ? "PASS  启动即抓" : $"FAIL  启动即抓: {g1},{g2},{g3},{g4},{g5}");
// 剪贴板自动清空判定（纯函数，不触碰剪贴板 API）
var scCases = new (string name, string? written, string? current, bool elapsed, bool want)[]
{
    ("内容一致 + 到期 -> 清空",   "ASD123", "ASD123",   true,  true),
    ("内容已变 -> 保留不动",      "ASD123", "别的文字", true,  false),
    ("未到期 -> 不清",            "ASD123", "ASD123",   false, false),
    ("written 为 null -> 不清",   null,     "ASD123",   true,  false),
};
int scPass = 0;
foreach (var (name, written, current, elapsed, want) in scCases)
{
    var got = ClipboardClearPolicy.ShouldClear(written, current, elapsed);
    if (got == want) { scPass++; Console.WriteLine($"PASS  {name}"); }
    else Console.WriteLine($"FAIL  {name}: got {got}, want {want}");
}
Console.WriteLine($"\n{scPass}/{scCases.Length} ShouldClear 断言通过");

// 不变量：Analyze 触发后立刻标记 _fired——调用方若丢弃返回值，码就被"吃掉"
//（CaptureEngine 的静止画面分支曾踩这个坑：调了 Analyze 却把返回值丢掉）
var det3 = new CodeDetector(30, 1, grabOnStart: true);
var e1 = det3.Analyze(new List<string> { "OZB459" });   // 首帧只记录
var e2 = det3.Analyze(new List<string> { "OZB459" });   // 第 2 帧触发
var e3 = det3.Analyze(new List<string> { "OZB459" });   // 已触发：不再重复（哪怕画面没变）
bool fireOk = e1 == null && e2 == "OZB459" && e3 == null;
Console.WriteLine(fireOk ? "PASS  触发即标记（丢弃返回值会吃掉码）" : $"FAIL  触发标记: {e1},{e2},{e3}");

// 自动粘贴触发判定（纯逻辑）
var apCases = new (string name, bool written, bool enabled, int configured, bool wantInject, int wantDelay)[]
{
    ("写入成功 + 开启 -> 注入",       true,  true,  0,     true,  0),
    ("写入失败 -> 不注入",            false, true,  0,     false, 0),
    ("开关关闭 -> 不注入",            true,  false, 0,     false, 0),
    ("负延迟 -> 夹取到 0",            true,  true,  -50,   true,  0),
    ("超上限延迟 -> 夹取到 10000",    true,  true,  99999, true,  AutoPastePolicy.MaxDelayMs),
};
int apPass = 0;
foreach (var (name, written, enabled, configured, wantInject, wantDelay) in apCases)
{
    var inject = AutoPastePolicy.ShouldInject(written, enabled, configured, out var delay);
    if (inject == wantInject && delay == wantDelay) { apPass++; Console.WriteLine($"PASS  {name}"); }
    else Console.WriteLine($"FAIL  {name}: inject={inject}, delay={delay}, 期望 inject={wantInject}, delay={wantDelay}");
}
Console.WriteLine($"{apPass}/{apCases.Length} ShouldInject 断言通过");

// 自动粘贴调度（mock 注入器记录调用次数，不真正发键）
int pasteCalls = 0;
var scheduler = new AutoPasteScheduler(() => { pasteCalls++; return true; }, _ => true, action => action());   // 同步执行器
scheduler.AfterWrite(false, true, 0, false, null);    // 写入失败 -> 不调
scheduler.AfterWrite(true, false, 0, false, null);    // 开关关 -> 不调
scheduler.AfterWrite(true, true, 0, false, null);     // 触发一次
bool schedOk = pasteCalls == 1;
Console.WriteLine(schedOk ? "PASS  调度：失败/关闭不注入，成功注入恰好一次" : $"FAIL  调度: pasteCalls={pasteCalls}");

// 异步执行器路径：只捕获动作，不执行（延迟等待不发生在判定线程）
Action? pending = null;
var scheduler2 = new AutoPasteScheduler(() => true, _ => true, action => pending = action);
scheduler2.AfterWrite(true, true, 5000, false, null);
bool asyncOk = pending != null;
Console.WriteLine(asyncOk ? "PASS  调度：非零延迟交给执行器异步等待" : "FAIL  调度: 未调度延迟动作");
scheduler2.Dispose();
pending?.Invoke();   // 退出后执行器若被调度，也应立即返回，不再注入
var cancelledCalls = 0;
Action? cancelledPaste = null;
var cancelScheduler = new AutoPasteScheduler(() => { cancelledCalls++; return true; }, _ => true, action => cancelledPaste = action);
cancelScheduler.AfterWrite(true, true, 5000, false, null);
cancelScheduler.Dispose();
cancelledPaste?.Invoke();
bool cancelledPasteOk = cancelledCalls == 0;
Console.WriteLine(cancelledPasteOk ? "PASS  退出取消：延迟粘贴不注入" : "FAIL  退出取消：延迟粘贴仍注入");

var injectionOrder = new List<string>();
var clickScheduler = new AutoPasteScheduler(
    () => { injectionOrder.Add("paste"); return true; },
    position => { injectionOrder.Add($"click:{position.X},{position.Y}"); return true; },
    action => action());
var target = new System.Drawing.Point(-100, 200);
clickScheduler.AfterWrite(false, true, 0, true, target);
clickScheduler.AfterWrite(true, true, 0, true, null);
clickScheduler.AfterWrite(true, false, 0, true, target);
clickScheduler.AfterWrite(true, true, 0, true, target);
bool clickOrderOk = injectionOrder.SequenceEqual(new[] { "click:-100,200", "click:-100,200", "paste" });
Console.WriteLine(clickOrderOk ? "PASS  自动点击：写入成功才执行、先点击再粘贴" : "FAIL  自动点击：执行顺序或次数错误");
var failedClickPasteCalls = 0;
var failedClickScheduler = new AutoPasteScheduler(() => { failedClickPasteCalls++; return true; }, _ => false, action => action());
failedClickScheduler.AfterWrite(true, true, 0, true, target);
bool failedClickOk = failedClickPasteCalls == 0;
Console.WriteLine(failedClickOk ? "PASS  自动点击失败后跳过粘贴" : "FAIL  自动点击失败仍发送粘贴");
var canceledClickCalls = 0;
Action? canceledClick = null;
var cancelClickScheduler = new AutoPasteScheduler(() => true, _ => { canceledClickCalls++; return true; }, action => canceledClick = action);
cancelClickScheduler.AfterWrite(true, false, 5000, true, target);
cancelClickScheduler.Dispose();
canceledClick?.Invoke();
bool canceledClickOk = canceledClickCalls == 0;
Console.WriteLine(canceledClickOk ? "PASS  退出取消：待发点击不注入" : "FAIL  退出取消：待发点击仍注入");
var staleClickCalls = 0;
var pendingClicks = new List<Action>();
var staleScheduler = new AutoPasteScheduler(() => true, _ => { staleClickCalls++; return true; }, pendingClicks.Add);
staleScheduler.AfterWrite(true, false, 0, true, target);
staleScheduler.AfterWrite(true, false, 0, true, target);
pendingClicks.ForEach(action => action());
bool staleClickOk = staleClickCalls == 1;
Console.WriteLine(staleClickOk ? "PASS  新码覆盖旧码：旧的待发点击不执行" : "FAIL  新码覆盖旧码：待发点击重复执行");

// Ctrl+V 序列组装：Ctrl↓ V↓ V↑ Ctrl↑，全部带 scancode 标志
var seq = KeyboardInjector.BuildCtrlVSequence();
bool seqOk = seq.Length == 4
    && seq[0] == new KeyboardInjector.KeyStroke(KeyboardInjector.ScanCtrl, KeyboardInjector.FlagKeyDown)
    && seq[1] == new KeyboardInjector.KeyStroke(KeyboardInjector.ScanV, KeyboardInjector.FlagKeyDown)
    && seq[2] == new KeyboardInjector.KeyStroke(KeyboardInjector.ScanV, KeyboardInjector.FlagKeyUp)
    && seq[3] == new KeyboardInjector.KeyStroke(KeyboardInjector.ScanCtrl, KeyboardInjector.FlagKeyUp)
    && (KeyboardInjector.FlagKeyDown & 0x0008) != 0;   // 必须 KEYEVENTF_SCANCODE
Console.WriteLine(seqOk ? "PASS  Ctrl+V 序列组装（scancode 模式）" : "FAIL  Ctrl+V 序列组装");

// 配置默认值：自动粘贴默认关闭、零延迟
var defCfg = new AppConfig();
bool cfgOk = !defCfg.AutoPasteEnabled && defCfg.AutoPasteDelayMs == 0 && !defCfg.AutoClickEnabled && defCfg.AutoClickPosition is null;
Console.WriteLine(cfgOk ? "PASS  配置默认值：自动粘贴关闭、延迟 0" : $"FAIL  配置默认值: {defCfg.AutoPasteEnabled}, {defCfg.AutoPasteDelayMs}");

// 统一配置：非法值逐字段夹取，缺失字段用属性默认值，保存后可重新加载
var dirtyCfg = new AppConfig
{
    CooldownSeconds = -5,
    ConfirmFrames = 99,
    CaptureIntervalMs = 1,
    ScaleFactor = 20,
    ClipboardClearSeconds = 9000,
    AutoPasteDelayMs = -10,
    AutoClickEnabled = true,
    AutoClickPosition = new System.Drawing.Point(50000, 0),
    MonitorRoom = "  https://live.douyin.com/123  ",
    MonitorIntervalSeconds = 0,
    SpeedThresholdMs = 999999,
    SpeedRate = double.NaN,
    JumpThresholdMs = -1,
};
dirtyCfg.Sanitize();
bool sanitizeOk = dirtyCfg.CooldownSeconds == 0
    && dirtyCfg.ConfirmFrames == 4
    && dirtyCfg.CaptureIntervalMs == 10
    && dirtyCfg.ScaleFactor == 6
    && dirtyCfg.ClipboardClearSeconds == 3600
    && dirtyCfg.AutoPasteDelayMs == 0
    && !dirtyCfg.AutoClickEnabled && dirtyCfg.AutoClickPosition is null
    && dirtyCfg.MonitorRoom == "https://live.douyin.com/123"
    && dirtyCfg.MonitorIntervalSeconds == AppConfig.MinMonitorIntervalSeconds
    && dirtyCfg.SpeedThresholdMs == AppConfig.MaxSpeedThresholdMs
    && dirtyCfg.SpeedRate == 1.1
    && dirtyCfg.JumpThresholdMs == 1;
Console.WriteLine(sanitizeOk ? "PASS  统一配置：非法值夹取" : "FAIL  统一配置：非法值夹取");

var configRoundTripPath = Path.Combine(Path.GetTempPath(), "RoomCodeGrabberTests", $"config-{Guid.NewGuid():N}.json");
var savedCfg = new AppConfig
{
    MonitorRoom = "456",
    MonitorIntervalSeconds = 21,
    CatchupEnabled = false,
    SpeedThresholdMs = 750,
    SpeedRate = 1.25,
    JumpThresholdMs = null,
    AutoClickEnabled = true,
    AutoClickPosition = target,
};
savedCfg.SaveTo(configRoundTripPath);
var loadedCfg = AppConfig.LoadFrom(configRoundTripPath);
bool configRoundTripOk = loadedCfg.MonitorRoom == "456"
    && loadedCfg.MonitorIntervalSeconds == 21
    && !loadedCfg.CatchupEnabled
    && loadedCfg.SpeedThresholdMs == 750
    && loadedCfg.SpeedRate == 1.25
    && loadedCfg.JumpThresholdMs == null
    && loadedCfg.AutoClickEnabled && loadedCfg.AutoClickPosition == target;
Console.WriteLine(configRoundTripOk ? "PASS  统一配置：保存并重新加载" : "FAIL  统一配置：保存并重新加载");

var secretConfig = new AppConfig();
var credentialStore = new ProtectedCredentialStore(secretConfig);
const string fakeCredential = "ttwid=fixture-only-value";
credentialStore.Set(fakeCredential);
var storedJson = System.Text.Json.JsonSerializer.Serialize(secretConfig);
var statusJson = System.Text.Json.JsonSerializer.Serialize(credentialStore.ToStatus());
bool credentialOk = credentialStore.IsConfigured
    && credentialStore.Get() == fakeCredential
    && !storedJson.Contains(fakeCredential, StringComparison.Ordinal)
    && !statusJson.Contains(fakeCredential, StringComparison.Ordinal)
    && statusJson.Contains("\"credentialConfigured\":true", StringComparison.Ordinal);
credentialStore.Clear();
credentialOk = credentialOk && !credentialStore.IsConfigured && credentialStore.Get() == null;
Console.WriteLine(credentialOk ? "PASS  凭据：DPAPI 往返、明文不落盘、清除" : "FAIL  凭据：DPAPI 往返、明文不落盘、清除");

// WebView2 消息协议：版本、类型、字段范围和 URL 都在进入原生动作前校验
var bridgeCases = new (string name, string json, bool want)[]
{
    ("合法监测命令", """{"version":1,"type":"monitor.start","payload":{"room":"123","intervalSec":15,"credential":null}}""", true),
    ("合法手动播放", """{"version":1,"type":"playback.manual-start","payload":{"url":"https://cdn.example/live.flv?sign=x"}}""", true),
    ("损坏 JSON", "{", false),
    ("错误版本", """{"version":2,"type":"monitor.stop","payload":{}}""", false),
    ("未知类型", """{"version":1,"type":"system.exec","payload":{}}""", false),
    ("非法间隔", """{"version":1,"type":"monitor.start","payload":{"room":"123","intervalSec":0}}""", false),
    ("非 FLV 地址", """{"version":1,"type":"playback.manual-start","payload":{"url":"https://example.com/page"}}""", false),
    ("倍率越界", """{"version":1,"type":"playback.settings","payload":{"catchupEnabled":true,"speedThresholdMs":1000,"speedRate":3,"jumpThresholdMs":2000}}""", false),
    ("记录光标命令", """{"version":1,"type":"capture.cursor.record","payload":{}}""", true),
};
int bridgePass = 0;
foreach (var (name, json, want) in bridgeCases)
{
    var got = BridgeProtocol.TryParseCommand(json, out _, out _);
    if (got == want) { bridgePass++; Console.WriteLine($"PASS  协议：{name}"); }
    else Console.WriteLine($"FAIL  协议：{name}, got={got}, want={want}");
}
bool bridgeEventOk = false;
bool unknownEventRejected = false;
try
{
    var eventJson = BridgeProtocol.SerializeEvent("capture.code", new { code = "ASD123", latencyMs = 42 });
    bridgeEventOk = eventJson.Contains("\"version\":1") && eventJson.Contains("ASD123");
    BridgeProtocol.SerializeEvent("unknown.event", new { });
}
catch (ArgumentOutOfRangeException)
{
    unknownEventRejected = true;
}
bridgeEventOk = bridgeEventOk && unknownEventRejected;
Console.WriteLine(bridgeEventOk ? "PASS  协议：出站事件白名单" : "FAIL  协议：出站事件白名单");

var roomCases = new (string? input, bool ok, string? room)[]
{
    ("134525032676", true, "134525032676"),
    (" https://live.douyin.com/999?from=web ", true, "999"),
    ("", false, null),
    (null, false, null),
    ("https://v.douyin.com/abc/", false, null),
    ("https://example.com/123", false, null),
    ("https://fake-douyin.com/123", false, null),
};
bool roomInputOk = roomCases.All(test =>
{
    var result = RoomPageParser.NormalizeRoom(test.input);
    return result.Ok == test.ok && result.Room == test.room;
});

const string livePage = """x\"flv_pull_url\":{\"FULL_HD1\":\"http://cdn/x_uhd.flv?expire=1\u0026sign=f912\",\"HD1\":\"http://cdn/x_hd.flv?x=1\"}y""";
var liveResult = RoomPageParser.Parse(livePage);
var fallbackResult = RoomPageParser.Parse("""x\"flv_pull_url\":{\"SD1\":\"http://cdn/x_sd.flv?k=1\"}y""");
bool roomPageOk = liveResult.Ok && liveResult.Live && liveResult.BestUrl == "http://cdn/x_uhd.flv?expire=1&sign=f912"
    && liveResult.FlvUrls.Count == 2
    && fallbackResult.Live && fallbackResult.BestUrl == "http://cdn/x_sd.flv?k=1"
    && RoomPageParser.Parse(livePage + "\\\"status\\\":4").Live
    && RoomPageParser.Parse("""...\"status\":4,\"room_id_str\":\"1\"...""") is { Ok: true, Live: false }
    && RoomPageParser.Parse("<title>验证码中间页</title>" ).Reason == "captcha"
    && RoomPageParser.Parse("<body>unknown format</body>") is { Ok: false, Live: false }
    && RoomPageParser.Parse("""x\"flv_pull_url\":{\"HD1\":\"http://cdn/x.html\"}y""") is { Ok: false, Live: false }
    && RoomPageParser.Parse(null).Reason == "empty";
Console.WriteLine(roomInputOk && roomPageOk ? "PASS  房间号/页面解析：与 JavaScript 夹具语义一致" : "FAIL  房间号/页面解析");
bool monitorOk = await MonitorServiceTests.RunAsync();
bool monitorBridgeOk = await MonitorBridgeTests.RunAsync();
var selectCount = 0;
var toggleCount = 0;
var running = false;
var captureSelectJson = """{"version":1,"type":"capture.select","payload":{}}""";
var captureToggleJson = """{"version":1,"type":"capture.toggle","payload":{"enabled":true}}""";
BridgeProtocol.TryParseCommand(captureSelectJson, out var selectCommand, out _);
BridgeProtocol.TryParseCommand(captureToggleJson, out var toggleCommand, out _);
bool captureHostOk = selectCommand is not null && toggleCommand is not null
    && CaptureHostCommands.TryDispatch(selectCommand, () => running, () => selectCount++, () => toggleCount++)
    && CaptureHostCommands.TryDispatch(toggleCommand, () => running, () => selectCount++, () => toggleCount++)
    && selectCount == 1 && toggleCount == 1;
running = true;
captureHostOk &= CaptureHostCommands.TryDispatch(toggleCommand!, () => running, () => selectCount++, () => toggleCount++) && toggleCount == 1;
Console.WriteLine(captureHostOk ? "PASS  宿主抓取命令：框选、幂等切换" : "FAIL  宿主抓取命令");
using var copiedEvent = System.Text.Json.JsonDocument.Parse(CaptureBridgeEvents.Copied("ASD123", 42.5));
using var failedEvent = System.Text.Json.JsonDocument.Parse(CaptureBridgeEvents.ClipboardFailure());
bool capturePayloadOk = copiedEvent.RootElement.GetProperty("type").GetString() == "capture.code"
    && copiedEvent.RootElement.GetProperty("payload").GetProperty("code").GetString() == "ASD123"
    && copiedEvent.RootElement.GetProperty("payload").GetProperty("latencyMs").GetDouble() == 42.5
    && failedEvent.RootElement.GetProperty("type").GetString() == "app.error"
    && !failedEvent.RootElement.GetProperty("payload").TryGetProperty("code", out _);
Console.WriteLine(capturePayloadOk ? "PASS  抓码事件：成功含延迟，失败不冒充已复制" : "FAIL  抓码事件");

var settingsConfig = new AppConfig();
bool playbackSettingsOk = BridgeProtocol.TryParseCommand("""{"version":1,"type":"playback.settings","payload":{"catchupEnabled":false,"speedThresholdMs":850,"speedRate":1.35,"jumpThresholdMs":null}}""", out var playbackCommand, out _);
bool captureSettingsOk = BridgeProtocol.TryParseCommand("""{"version":1,"type":"capture.settings","payload":{"captureIntervalMs":40,"confirmFrames":2,"cooldownSeconds":10,"scaleFactor":4,"grabOnStart":false,"ocrEngine":1,"soundEnabled":false,"clipboardClearSeconds":0,"autoPasteEnabled":true,"autoClickEnabled":true,"autoPasteDelayMs":350}}""", out var settingsCommand, out _);
bool invalidSettingsRejected = !BridgeProtocol.TryParseCommand("""{"version":1,"type":"capture.settings","payload":{"captureIntervalMs":40,"confirmFrames":2,"cooldownSeconds":10,"scaleFactor":4,"grabOnStart":false,"ocrEngine":2,"soundEnabled":false,"clipboardClearSeconds":0,"autoPasteEnabled":true,"autoClickEnabled":true,"autoPasteDelayMs":350}}""", out _, out _);
settingsConfig.AutoClickPosition = target;
if (playbackSettingsOk && captureSettingsOk)
{
    UnifiedSettingsCommands.ApplyPlayback(settingsConfig, playbackCommand!);
    UnifiedSettingsCommands.ApplyCapture(settingsConfig, settingsCommand!);
    var settingsPath = Path.Combine(Path.GetTempPath(), "RoomCodeGrabberTests", $"settings-{Guid.NewGuid():N}.json");
    settingsConfig.SaveTo(settingsPath);
    settingsConfig = AppConfig.LoadFrom(settingsPath);
}
bool settingsRestartOk = playbackSettingsOk && captureSettingsOk && invalidSettingsRejected
    && !settingsConfig.CatchupEnabled && settingsConfig.SpeedThresholdMs == 850
    && settingsConfig.SpeedRate == 1.35 && settingsConfig.JumpThresholdMs is null
    && settingsConfig.CaptureIntervalMs == 40 && settingsConfig.ConfirmFrames == 2
    && settingsConfig.CooldownSeconds == 10 && settingsConfig.ScaleFactor == 4
    && !settingsConfig.GrabOnStart && settingsConfig.OcrEngine == OcrEngineKind.Paddle
    && !settingsConfig.SoundEnabled && settingsConfig.ClipboardClearSeconds == 0
    && settingsConfig.AutoPasteEnabled && settingsConfig.AutoPasteDelayMs == 350
    && settingsConfig.AutoClickEnabled && settingsConfig.AutoClickPosition == target;
Console.WriteLine(settingsRestartOk ? "PASS  设置：消息验证、应用并在重启后恢复" : "FAIL  设置：消息验证或重启恢复");

Environment.Exit(fail == 0 && dupOk && grabOk && scPass == scCases.Length && fireOk
    && apPass == apCases.Length && schedOk && asyncOk && cancelledPasteOk && clickOrderOk && failedClickOk && canceledClickOk && staleClickOk && seqOk && cfgOk && sanitizeOk && configRoundTripOk && credentialOk
    && bridgePass == bridgeCases.Length && bridgeEventOk && roomInputOk && roomPageOk && monitorOk && monitorBridgeOk && captureHostOk && capturePayloadOk && settingsRestartOk ? 0 : 1);
