using UnityEngine;
using UnityEngine.XR;

public class ColocationAligner : MonoBehaviour
{
    [Header("References")]
    public Transform XROrigin; // 拖入你的 XR Origin
    public Transform Headset;  // 拖入 Main Camera

    // 这是一个虚拟的标记点，放在场景 (0,0,0)
    // 现实中，两名玩家约定：场景的 (0,0,0) 就是现实中“桌子的左下角”
    public Vector3 TargetWorldOrigin = Vector3.zero;

    // 只有 Client 需要操作这个
    public void Calibrate()
    {
        // 假设玩家现在的头显位置，就在“桌子左下角”的正上方
        // 我们要移动 XR Origin，使得 Headset 的位置对齐到 TargetWorldOrigin (忽略高度，或者包含高度)

        // 1. 计算偏移量
        // 目标：让 Headset.position(X,Z) == TargetWorldOrigin(X,Z)
        // 当前：Headset 是 XROrigin 的子物体

        // 简单算法：把整个 XR Origin 平移，抵消掉头显和原点的差距
        Vector3 offset = Headset.position - XROrigin.position;

        // 新的 Origin 位置 = 目标世界坐标 - 头显相对于Origin的偏移
        // 这里假设玩家站在原点面向正前方 (Z轴)
        // 如果需要校准旋转，还需要让玩家“面向桌子长边”站立

        Vector3 newOriginPos = TargetWorldOrigin - new Vector3(offset.x, 0, offset.z);

        // 应用校准
        XROrigin.position = new Vector3(newOriginPos.x, XROrigin.position.y, newOriginPos.z);

        // 旋转校准 (假设玩家面向正 Z 轴)
        float currentYRot = Headset.eulerAngles.y;
        float targetYRot = 0f; // 约定的正前方
        float rotDiff = targetYRot - currentYRot;

        XROrigin.RotateAround(Headset.position, Vector3.up, rotDiff);

        Debug.Log("Colocation Calibrated!");
    }
}