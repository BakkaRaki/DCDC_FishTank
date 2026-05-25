using UnityEngine;

/// <summary>Uses LogWarning so messages appear in release logcat (W/Unity) when Development Build is off.</summary>
public static class FishTankLog
{
    const string Tag = "FishTank";

    public static void Info(string message) => Debug.LogWarning($"[{Tag}] {message}");

    public static void Warn(string message) => Debug.LogWarning($"[{Tag}] {message}");
}
