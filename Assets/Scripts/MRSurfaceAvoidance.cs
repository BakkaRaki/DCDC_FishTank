using UnityEngine;
using Meta.XR.MRUtilityKit;

public static class MRSurfaceAvoidance
{
    public static Vector3 GetScareVector(Vector3 worldPos, float radius)
    {
        if (radius <= 0f) return Vector3.zero;
        if (MRUK.Instance == null) return Vector3.zero;

        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return Vector3.zero;

        float dist = room.TryGetClosestSurfacePosition(
            worldPos,
            out Vector3 surfPos,
            out MRUKAnchor anchor,
            out Vector3 normal);

        if (dist < 0f || dist >= radius) return Vector3.zero;

        float strength = 1f - Mathf.Clamp01(dist / radius);
        Vector3 awayFromSurface = worldPos - surfPos;
        if (awayFromSurface.sqrMagnitude < 1e-6f)
        {
            awayFromSurface = normal;
        }
        else
        {
            awayFromSurface.Normalize();
            awayFromSurface = Vector3.Slerp(awayFromSurface, normal, 0.5f).normalized;
        }

        return awayFromSurface * strength;
    }

    public static bool IsPositionInRoom(Vector3 worldPos, bool testVerticalBounds = true)
    {
        if (MRUK.Instance == null) return true;
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return true;
        return room.IsPositionInRoom(worldPos, testVerticalBounds);
    }

    public static bool TryGetClosestSurface(Vector3 worldPos, out Vector3 surfPos, out Vector3 normal)
    {
        surfPos = worldPos;
        normal = Vector3.up;

        if (MRUK.Instance == null) return false;
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;

        float dist = room.TryGetClosestSurfacePosition(worldPos, out surfPos, out MRUKAnchor anchor, out normal);
        return dist >= 0f;
    }
}

