namespace RoomCodeGrabber;

public static class CaptureHostCommands
{
    public static bool TryDispatch(BridgeCommand command, Func<bool> isRunning, Action select, Action toggle)
    {
        switch (command.Type)
        {
            case "capture.select":
                select();
                return true;
            case "capture.toggle":
                if (command.Payload.GetProperty("enabled").GetBoolean() != isRunning()) toggle();
                return true;
            default:
                return false;
        }
    }
}
