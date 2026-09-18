using System.Text.Json;

namespace RoomCodeGrabber;

public sealed record BridgeCommand(string Type, JsonElement Payload);

public static class BridgeProtocol
{
    public const int CurrentVersion = 1;

    private static readonly HashSet<string> HostEventTypes = new(StringComparer.Ordinal)
    {
        "app.init",
        "app.error",
        "monitor.status",
        "monitor.stream",
        "capture.status",
        "capture.code",
        "clipboard.status",
        "playback.command",
    };

    private static readonly HashSet<string> UiCommandTypes = new(StringComparer.Ordinal)
    {
        "app.error",
        "monitor.start",
        "monitor.stop",
        "playback.manual-start",
        "playback.settings",
        "playback.status",
        "capture.select",
        "capture.cursor.record",
        "capture.toggle",
        "capture.settings",
        "clipboard.clear",
        "credential.clear",
    };

    public static bool TryParseCommand(string json, out BridgeCommand? command, out string error)
    {
        command = null;
        error = "";

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        }
        catch (JsonException)
        {
            error = "消息不是合法 JSON";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "消息根节点必须是对象";
                return false;
            }

            if (!root.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out var versionNumber) ||
                versionNumber != CurrentVersion)
            {
                error = "不支持的消息协议版本";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(typeElement.GetString()))
            {
                error = "消息缺少 type";
                return false;
            }

            var type = typeElement.GetString()!;
            if (!UiCommandTypes.Contains(type))
            {
                error = $"未知消息类型: {type}";
                return false;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                error = "消息 payload 必须是对象";
                return false;
            }

            if (!ValidatePayload(type, payload, out error)) return false;
            command = new BridgeCommand(type, payload.Clone());
            return true;
        }
    }

    public static string SerializeEvent(string type, object payload)
    {
        if (!HostEventTypes.Contains(type))
            throw new ArgumentOutOfRangeException(nameof(type), $"Unknown host event type: {type}");

        return JsonSerializer.Serialize(new
        {
            version = CurrentVersion,
            type,
            payload,
        });
    }

    private static bool ValidatePayload(string type, JsonElement payload, out string error)
    {
        error = "";
        switch (type)
        {
            case "monitor.start":
                if (!StringInRange(payload, "room", 1, 512)) return Fail("room 不能为空或过长", out error);
                if (!IntInRange(payload, "intervalSec", 1, 300)) return Fail("intervalSec 必须在 1 到 300 之间", out error);
                if (payload.TryGetProperty("credential", out var credential) &&
                    credential.ValueKind != JsonValueKind.Null &&
                    (credential.ValueKind != JsonValueKind.String || credential.GetString()!.Length > 16384))
                    return Fail("credential 格式无效", out error);
                return true;

            case "playback.manual-start":
                if (!StringInRange(payload, "url", 1, 8192)) return Fail("url 不能为空或过长", out error);
                var text = payload.GetProperty("url").GetString()!;
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    !uri.AbsolutePath.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
                    return Fail("url 必须是 HTTP(S) FLV 地址", out error);
                return true;

            case "playback.settings":
                if (!Bool(payload, "catchupEnabled")) return Fail("catchupEnabled 必须是布尔值", out error);
                if (!IntInRange(payload, "speedThresholdMs", 0, 60000)) return Fail("speedThresholdMs 超出范围", out error);
                if (!DoubleInRange(payload, "speedRate", 1.05, 2.0)) return Fail("speedRate 超出范围", out error);
                if (payload.TryGetProperty("jumpThresholdMs", out var jump) && jump.ValueKind != JsonValueKind.Null &&
                    (!jump.TryGetInt32(out var jumpMs) || jumpMs < 1 || jumpMs > 120000))
                    return Fail("jumpThresholdMs 超出范围", out error);
                return payload.TryGetProperty("jumpThresholdMs", out _)
                    || Fail("缺少 jumpThresholdMs", out error);

            case "capture.toggle":
                return Bool(payload, "enabled") || Fail("enabled 必须是布尔值", out error);

            case "capture.settings":
                if (!IntInRange(payload, "cooldownSeconds", 0, 600)) return Fail("cooldownSeconds 超出范围", out error);
                if (!IntInRange(payload, "captureIntervalMs", 10, 200)) return Fail("captureIntervalMs 超出范围", out error);
                if (!IntInRange(payload, "confirmFrames", 1, 4)) return Fail("confirmFrames 超出范围", out error);
                if (!IntInRange(payload, "scaleFactor", 2, 6)) return Fail("scaleFactor 超出范围", out error);
                if (!IntInRange(payload, "clipboardClearSeconds", 0, 3600)) return Fail("clipboardClearSeconds 超出范围", out error);
                if (!Bool(payload, "autoPasteEnabled")) return Fail("autoPasteEnabled 必须是布尔值", out error);
                if (!Bool(payload, "autoClickEnabled")) return Fail("autoClickEnabled 必须是布尔值", out error);
                if (!IntInRange(payload, "autoPasteDelayMs", 0, AutoPastePolicy.MaxDelayMs)) return Fail("autoPasteDelayMs 超出范围", out error);
                if (!Bool(payload, "grabOnStart")) return Fail("grabOnStart 必须是布尔值", out error);
                if (!Bool(payload, "soundEnabled")) return Fail("soundEnabled 必须是布尔值", out error);
                if (!IntInRange(payload, "ocrEngine", 0, 1)) return Fail("ocrEngine 超出范围", out error);
                return true;

            case "app.error":
                return StringInRange(payload, "message", 1, 1024)
                    || Fail("message 不能为空或过长", out error);

            case "playback.status":
                return StringInRange(payload, "state", 1, 64)
                    || Fail("state 不能为空或过长", out error);

            case "monitor.stop":
            case "capture.select":
            case "capture.cursor.record":
            case "clipboard.clear":
            case "credential.clear":
                return true;

            default:
                return Fail("消息类型未实现", out error);
        }
    }

    private static bool StringInRange(JsonElement payload, string name, int min, int max) =>
        payload.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { } text &&
        text.Trim().Length >= min &&
        text.Length <= max;

    private static bool IntInRange(JsonElement payload, string name, int min, int max) =>
        payload.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var number) &&
        number >= min && number <= max;

    private static bool DoubleInRange(JsonElement payload, string name, double min, double max) =>
        payload.TryGetProperty(name, out var value) &&
        value.TryGetDouble(out var number) &&
        double.IsFinite(number) && number >= min && number <= max;

    private static bool Bool(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
