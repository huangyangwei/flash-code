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
Environment.Exit(fail == 0 && dupOk && grabOk ? 0 : 1);
