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
using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// 封装对 MRUK 最近真实表面查询的工具类，供鱼群避障使用。
///
/// MRUK 的 TryGetClosestSurfacePosition 内部要遍历 Room 里的所有 anchor mesh，
/// 对数十条鱼每帧都调用会比较贵。这里做了极简缓存/节流：
///   - 若 MRUK 还未加载，直接返回 Vector3.zero（即无推力）。
///   - 每次调用直接查询，但每条鱼默认只在 Fusion 的定频 FixedUpdateNetwork 里调用一次（较低频率，可接受）。
///
/// 如果鱼数量上去之后这里成为瓶颈，可以改为：对每条鱼用一个 per-fish throttle（每 N 次 tick 查一次，中间复用结果）。
/// </summary>
public static class MRSurfaceAvoidance
{
    /// <summary>
    /// 获取让鱼远离最近真实表面的推力向量。
    /// </summary>
    /// <param name="worldPos">鱼的世界位置</param>
    /// <param name="radius">感知半径：距离真实表面小于该值才产生推力</param>
    /// <returns>
    /// 朝"真实表面法线"方向的推力向量；距离越近，幅度越大（1/d 衰减，最大 1）。
    /// 若 MRUK 未就绪 / 无最近表面 / 距离大于 radius，则返回 Vector3.zero。
    /// </returns>
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

        // dist < 0 表示没找到
        if (dist < 0f || dist >= radius) return Vector3.zero;

        // 距离越近，推力越大：strength in (0, 1]
        // 例如 radius=0.5, dist=0.1 -> strength = 0.8
        float strength = 1f - Mathf.Clamp01(dist / radius);

        // 沿法线方向推出 + 沿"表面到鱼"方向确保不会卡在表面内部
        Vector3 awayFromSurface = (worldPos - surfPos);
        if (awayFromSurface.sqrMagnitude < 1e-6f)
        {
            awayFromSurface = normal;
        }
        else
        {
            awayFromSurface.Normalize();
            // 结合表面法线（以法线为主），防止切向滑动卡住
            awayFromSurface = Vector3.Slerp(awayFromSurface, normal, 0.5f).normalized;
        }

        return awayFromSurface * strength;
    }

    /// <summary>
    /// 查询给定位置是否在房间内。未加载时返回 true（视为不受限）。
    /// </summary>
    public static bool IsPositionInRoom(Vector3 worldPos, bool testVerticalBounds = true)
    {
        if (MRUK.Instance == null) return true;
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return true;
        return room.IsPositionInRoom(worldPos, testVerticalBounds);
    }

    /// <summary>
    /// 给定位置的最近真实表面（世界坐标），未就绪返回 false。
    /// </summary>
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
