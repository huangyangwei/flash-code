using System.Text.RegularExpressions;

namespace RoomCodeGrabber;

public sealed record RoomInputResult(bool Ok, string? Room, string? Reason);

public sealed class RoomPageResult
{
    public bool Ok { get; private init; }
    public bool Live { get; private init; }
    public string? Reason { get; private init; }
    public string? FirstUrl { get; private init; }
    public string? BestUrl { get; private init; }
    public IReadOnlyDictionary<string, string> FlvUrls { get; private init; } = new Dictionary<string, string>();

    public static RoomPageResult Failure(string reason) => new() { Reason = reason };
    public static RoomPageResult Offline() => new() { Ok = true };
    public static RoomPageResult Streaming(string first, string best, IReadOnlyDictionary<string, string> urls) =>
        new() { Ok = true, Live = true, FirstUrl = first, BestUrl = best, FlvUrls = urls };
}

public static partial class RoomPageParser
{
    private static readonly Regex RoomPath = new(@"^/(\d+)/?$", RegexOptions.CultureInvariant);
    private static readonly Regex FlvBlock = new(@"\\?""flv_pull_url\\?""\s*:\s*\\?\{", RegexOptions.CultureInvariant);
    private static readonly Regex UrlPair = new(@"\\?""([A-Z0-9_]+)\\?""\s*:\s*\\?""(https?(?:\\u0026|[^""\\])+?)\\?""", RegexOptions.CultureInvariant);
    private static readonly Regex Status = new(@"\\?""status\\?""\s*:\s*(\d+)", RegexOptions.CultureInvariant);

    public static RoomInputResult NormalizeRoom(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return new(false, null, "empty");
        if (text.All(char.IsAsciiDigit)) return new(true, text, null);
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            (uri.IdnHost.Equals("douyin.com", StringComparison.OrdinalIgnoreCase) ||
             uri.IdnHost.EndsWith(".douyin.com", StringComparison.OrdinalIgnoreCase)))
        {
            var match = RoomPath.Match(uri.AbsolutePath);
            if (match.Success) return new(true, match.Groups[1].Value, null);
        }
        return new(false, null, "unrecognized");
    }

    public static RoomPageResult Parse(string? html)
    {
        if (string.IsNullOrEmpty(html)) return RoomPageResult.Failure("empty");
        if (html.Contains("验证码中间页", StringComparison.Ordinal) ||
            html.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            return RoomPageResult.Failure("captcha");

        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        string? firstUrl = null;
        foreach (Match block in FlvBlock.Matches(html))
        {
            var end = html.IndexOf('}', block.Index + block.Length);
            var body = end < 0 ? html[block.Index..] : html[block.Index..end];
            foreach (Match pair in UrlPair.Matches(body))
            {
                var url = pair.Groups[2].Value.Replace(@"\u0026", "&", StringComparison.Ordinal);
                if (!IsFlvUrl(url)) continue;
                firstUrl ??= url;
                urls.TryAdd(pair.Groups[1].Value, url);
            }
        }

        if (firstUrl is not null)
            return RoomPageResult.Streaming(firstUrl, urls.GetValueOrDefault("FULL_HD1") ?? firstUrl, urls);

        foreach (Match match in Status.Matches(html))
        {
            if (match.Groups[1].Value == "4") return RoomPageResult.Offline();
        }
        return RoomPageResult.Failure("no-stream-no-status");
    }

    private static bool IsFlvUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.AbsolutePath.EndsWith(".flv", StringComparison.OrdinalIgnoreCase);
}
