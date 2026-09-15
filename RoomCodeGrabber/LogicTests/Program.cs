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
var scheduler = new AutoPasteScheduler(() => { pasteCalls++; return true; }, action => action());   // 同步执行器
scheduler.AfterWrite(false, true, 0);    // 写入失败 -> 不调
scheduler.AfterWrite(true, false, 0);    // 开关关 -> 不调
scheduler.AfterWrite(true, true, 0);     // 触发一次
bool schedOk = pasteCalls == 1;
Console.WriteLine(schedOk ? "PASS  调度：失败/关闭不注入，成功注入恰好一次" : $"FAIL  调度: pasteCalls={pasteCalls}");

// 异步执行器路径：只捕获动作，不执行（延迟等待不发生在判定线程）
Action? pending = null;
var scheduler2 = new AutoPasteScheduler(() => true, action => pending = action);
scheduler2.AfterWrite(true, true, 5000);
bool asyncOk = pending != null;
Console.WriteLine(asyncOk ? "PASS  调度：非零延迟交给执行器异步等待" : "FAIL  调度: 未调度延迟动作");
pending?.Invoke();   // 补执行：确认动作本身可跑（Thread.Sleep(5000) 后注入）——注入器为 mock，不真正发键

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
bool cfgOk = !defCfg.AutoPasteEnabled && defCfg.AutoPasteDelayMs == 0;
Console.WriteLine(cfgOk ? "PASS  配置默认值：自动粘贴关闭、延迟 0" : $"FAIL  配置默认值: {defCfg.AutoPasteEnabled}, {defCfg.AutoPasteDelayMs}");

Environment.Exit(fail == 0 && dupOk && grabOk && scPass == scCases.Length && fireOk
    && apPass == apCases.Length && schedOk && asyncOk && seqOk && cfgOk ? 0 : 1);
