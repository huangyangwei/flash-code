using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoomCodeGrabber;

/// <summary>
/// 房间码识别器：正则匹配 + 格式约束的 OCR 混淆纠正 + 新码判定。
/// 目标格式：3 位大写字母 + 3 位数字，如 ASD123。
/// </summary>
public sealed class CodeDetector
{
    // 宽松抓取：6 位字母数字（允许 OCR 把字母/数字认混），且不能是更长数字串的一部分
    private static readonly Regex Loose = new(@"(?<![A-Z0-9])[A-Z0-9]{6}(?![A-Z0-9])",
        RegexOptions.Compiled);

    // 字母位上常见的"数字误认成字母"反向映射：0->O 1->I 5->S 8->B 6->G 2->Z 4->A 7->T 9->Q
    private static readonly Dictionary<char, char> DigitToLetter = new()
    {
        ['0'] = 'O', ['1'] = 'I', ['5'] = 'S', ['8'] = 'B', ['6'] = 'G', ['2'] = 'Z',
        ['4'] = 'A', ['7'] = 'T', ['9'] = 'Q',
    };

    // 数字位上常见的"字母误认成数字"反向映射：O->0 I->1 S->5 B->8 G->6 Z->2 A->4 T->7 D/Q->0
    private static readonly Dictionary<char, char> LetterToDigit = new()
    {
        ['O'] = '0', ['I'] = '1', ['S'] = '5', ['B'] = '8', ['G'] = '6', ['Z'] = '2',
        ['A'] = '4', ['T'] = '7', ['D'] = '0', ['Q'] = '0',
    };

    private readonly int _cooldownMs;
    private readonly int _confirmFrames;
    private readonly bool _grabOnStart;

    // 上一帧可见码集合（null 表示尚未处理过任何帧，首帧只记录不触发）
    private HashSet<string>? _prevFrame;
    // 最近 2 秒内见过的码：防止闪烁一帧的码被误判为新码
    private readonly Dictionary<string, DateTime> _recentlySeen = new();
    // 各码连续出现帧数
    private readonly Dictionary<string, int> _presence = new();
    // 本轮连续出现期间已触发过的码（保证每个连续出现段最多触发一次）
    private readonly HashSet<string> _fired = new();
    // 启动时画面上已有的码：GrabOnStart=false 时压制直到消失
    private readonly HashSet<string> _suppressed = new();
    // 上次实际复制触发的码与时间（冷却窗口）
    private string? _lastCopied;
    private DateTime _lastCopiedAt = DateTime.MinValue;

    public CodeDetector(int cooldownSeconds, int confirmFrames = 1, bool grabOnStart = true)
    {
        _cooldownMs = Math.Max(0, cooldownSeconds) * 1000;
        _confirmFrames = Math.Max(1, confirmFrames);
        _grabOnStart = grabOnStart;
    }

    /// <summary>从一帧 OCR 文本中提取所有候选码（已做格式纠正）。</summary>
    public static List<string> ExtractCodes(string ocrText)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(ocrText)) return results;

        foreach (var line in NormalizeDiacritics(ocrText.ToUpperInvariant()).Split('\n'))
        {
            var added = false;
            foreach (Match m in Loose.Matches(line))
            {
                var corrected = Correct(m.Value);
                if (corrected != null && !results.Contains(corrected))
                {
                    results.Add(corrected);
                    added = true;
                }
            }
            if (added) continue;

            // 空格容错兜底：OCR（尤其中文引擎）常在字母间插空格，如 "OZB 459"。
            // 按行去空格后重试。只对"去空格后恰好 6 位"的行生效，避免拼接连词误触发
            var compact = line.Replace(" ", "").Replace("\r", "");
            if (compact.Length == 6 && Loose.IsMatch(compact))
            {
                var corrected = Correct(compact);
                if (corrected != null && !results.Contains(corrected))
                    results.Add(corrected);
            }
        }
        return results;
    }

    /// <summary>
    /// 去变音符：OCR 偶发把字符读成带调字母（如 4 -> å）。还原为基础字母后再纠正。
    /// </summary>
    private static string NormalizeDiacritics(string s)
    {
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// 格式约束纠正：前 3 位必须是字母，后 3 位必须是数字。
    /// 限制：字母位最多纠 1 位、总纠 2 位——防止纯数字房间号（如 400259 -> AOO259）
    /// 被硬纠成假码。超出限制视为不是真正的房间码，返回 null（宁可漏抓，不抓错码）。
    /// </summary>
    private static string? Correct(string candidate)
    {
        Span<char> chars = stackalloc char[6];
        candidate.CopyTo(chars);
        int letterPosCorrections = 0, totalCorrections = 0;

        for (int i = 0; i < 3; i++)
        {
            if (char.IsAsciiLetter(chars[i]))
                continue;
            if (DigitToLetter.TryGetValue(chars[i], out var letter))
            {
                chars[i] = letter;
                letterPosCorrections++;
                totalCorrections++;
            }
            else
                return null; // 字母位上出现无法纠正的字符
        }
        for (int i = 3; i < 6; i++)
        {
            if (char.IsAsciiDigit(chars[i]))
                continue;
            if (LetterToDigit.TryGetValue(chars[i], out var digit))
            {
                chars[i] = digit;
                totalCorrections++;
            }
            else
                return null;
        }
        return letterPosCorrections <= 1 && totalCorrections <= 2 ? new string(chars) : null;
    }

    /// <summary>
    /// 每帧调用：传入本帧可见的码集合，返回应当触发复制的码，无则返回 null。
    /// 触发条件：连续出现满 ConfirmFrames 帧 + 未触发过 + 通过闪烁/冷却/首帧压制过滤。
    /// GrabOnStart=true 时，启动帧已有的码在满确认帧数后也会触发（即"启动即抓"）。
    /// </summary>
    public string? Analyze(List<string> visibleNow)
    {
        var now = DateTime.UtcNow;
        var current = new HashSet<string>(visibleNow);

        // 更新连续出现计数，并清掉已消失的
        foreach (var code in current)
            _presence[code] = _presence.TryGetValue(code, out var n) ? n + 1 : 1;
        var gone = _presence.Keys.Where(c => !current.Contains(c)).ToList();
        foreach (var code in gone)
        {
            _presence.Remove(code);
            _fired.Remove(code);
            _suppressed.Remove(code);
        }

        string? trigger = null;
        if (_prevFrame != null)
        {
            foreach (var code in current)
            {
                if (_suppressed.Contains(code)) continue;            // 启动压制中的旧码
                if (_fired.Contains(code)) continue;                 // 本轮已触发过
                if (_presence[code] < _confirmFrames) continue;      // 未满确认帧数
                if (_presence[code] == 1 &&
                    _recentlySeen.TryGetValue(code, out var lastSeen) &&
                    (now - lastSeen).TotalMilliseconds < 2000) continue; // 闪烁重现
                if (code == _lastCopied &&
                    (now - _lastCopiedAt).TotalMilliseconds < _cooldownMs) continue;

                trigger = code;
                break; // 一帧最多触发一个
            }
        }

        // 首帧（含 Reset 后第一帧）：只记录状态，不触发
        if (_prevFrame == null && !_grabOnStart)
            _suppressed.UnionWith(current);

        // 判定完成后再更新闪烁表，并清理过期记录
        foreach (var code in current)
            _recentlySeen[code] = now;
        var expired = _recentlySeen.Where(kv => (now - kv.Value).TotalMilliseconds > 2000)
                                   .Select(kv => kv.Key).ToList();
        foreach (var code in expired)
            _recentlySeen.Remove(code);

        _prevFrame = current;

        if (trigger != null)
        {
            _fired.Add(trigger);
            _lastCopied = trigger;
            _lastCopiedAt = now;
        }
        return trigger;
    }

    public void Reset()
    {
        _prevFrame = null;
        _recentlySeen.Clear();
        _presence.Clear();
        _fired.Clear();
        _suppressed.Clear();
        _lastCopied = null;
        _lastCopiedAt = DateTime.MinValue;
    }
}
