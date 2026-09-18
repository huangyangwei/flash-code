namespace RoomCodeGrabber;

public static class UnifiedSettingsCommands
{
    // Commands reach this layer only after BridgeProtocol has validated every field.
    public static void ApplyPlayback(AppConfig config, BridgeCommand command)
    {
        var payload = command.Payload;
        config.CatchupEnabled = payload.GetProperty("catchupEnabled").GetBoolean();
        config.SpeedThresholdMs = payload.GetProperty("speedThresholdMs").GetInt32();
        config.SpeedRate = payload.GetProperty("speedRate").GetDouble();
        var jump = payload.GetProperty("jumpThresholdMs");
        config.JumpThresholdMs = jump.ValueKind == System.Text.Json.JsonValueKind.Null ? null : jump.GetInt32();
    }

    public static void ApplyCapture(AppConfig config, BridgeCommand command)
    {
        var payload = command.Payload;
        config.CaptureIntervalMs = payload.GetProperty("captureIntervalMs").GetInt32();
        config.ConfirmFrames = payload.GetProperty("confirmFrames").GetInt32();
        config.CooldownSeconds = payload.GetProperty("cooldownSeconds").GetInt32();
        config.ScaleFactor = payload.GetProperty("scaleFactor").GetInt32();
        config.GrabOnStart = payload.GetProperty("grabOnStart").GetBoolean();
        config.OcrEngine = (OcrEngineKind)payload.GetProperty("ocrEngine").GetInt32();
        config.SoundEnabled = payload.GetProperty("soundEnabled").GetBoolean();
        config.ClipboardClearSeconds = payload.GetProperty("clipboardClearSeconds").GetInt32();
        config.AutoPasteEnabled = payload.GetProperty("autoPasteEnabled").GetBoolean();
        config.AutoClickEnabled = payload.GetProperty("autoClickEnabled").GetBoolean();
        config.AutoPasteDelayMs = payload.GetProperty("autoPasteDelayMs").GetInt32();
    }
}
