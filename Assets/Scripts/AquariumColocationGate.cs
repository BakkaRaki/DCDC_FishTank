using System;

/// <summary>Global gate: MR spawn / feed / avatar sync wait until shared spatial frame is ready.</summary>
public static class AquariumColocationGate
{
    public static bool IsReady { get; private set; }
    public static string StatusMessage { get; private set; } = "等待空间对齐…";

    public static event Action Ready;

    public static void Reset()
    {
        IsReady = false;
        StatusMessage = "等待空间对齐…";
    }

    public static void SetStatus(string message)
    {
        if (!string.IsNullOrEmpty(message))
            StatusMessage = message;
    }

    public static void SetReady(string reason = null)
    {
        if (IsReady) return;
        IsReady = true;
        StatusMessage = string.IsNullOrEmpty(reason) ? "空间已对齐" : reason;
        FishTankLog.Info($"Colocation ready: {StatusMessage}");
        Ready?.Invoke();
    }
}
