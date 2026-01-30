using UnityEngine;
using Fusion;

public class AvatarMovement : NetworkBehaviour
{
    // 本地摄像机（头显）的引用
    private Transform _headsetTransform;

    public override void Spawned()
    {
        // 只有“我自己”需要去同步位置
        if (Object.HasInputAuthority)
        {
            // 找到场景里的主摄像机 (Quest 3 的头)
            if (Camera.main != null)
            {
                _headsetTransform = Camera.main.transform;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 如果是我控制这个 Avatar，且找到了头显
        if (Object.HasInputAuthority && _headsetTransform != null)
        {
            // 1. 获取头显位置
            Vector3 targetPos = _headsetTransform.position;
            Quaternion targetRot = _headsetTransform.rotation;

            // 2. 为了防止 Avatar 上下抖动或者倾斜，通常只同步 Y 轴旋转（可选）
            // 这里为了简单，直接全同步，或者只同步位置
            transform.position = targetPos;
            transform.rotation = targetRot;
        }
    }
}