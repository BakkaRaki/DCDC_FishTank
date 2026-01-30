using UnityEngine;
using Fusion;
using UnityEngine.XR; // 引用 XR 库

public class PlayerHandSync : NetworkBehaviour
{
    public Transform LeftHandVisual;
    public Transform RightHandVisual;

    // 只有本地玩家(Input Authority)才需要去读取真实的 VR 硬件数据
    public override void FixedUpdateNetwork()
    {
        // 1. 如果是“我自己”控制这个替身，我就负责读取手柄位置并上传网络
        if (Object.HasInputAuthority)
        {
            UpdateHandPosition(XRNode.LeftHand, LeftHandVisual);
            UpdateHandPosition(XRNode.RightHand, RightHandVisual);
        }
    }

    void UpdateHandPosition(XRNode node, Transform targetVisual)
    {
        // 从 Unity XR 系统获取手柄位置
        InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
        InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot);

        // 如果获取到了（手柄连上了），就更新本地位置
        // 注意：因为 NetworkTransform 在父物体上，这里我们可能需要把坐标转换一下
        // 但最简单的方法是：让 Host 信任 Client 的这一帧状态（稍微复杂，我们用最笨的办法：RPC 或者直接改 transform 如果 NetworkTransform 允许 Client 预测）

        // 简化版：直接修改 transform。因为 NetworkTransform (ClientPredicted) 会自动处理同步。
        if (pos != Vector3.zero)
        {
            // 这里我们需要把 VR 的世界坐标转换到 Avatar 的相对坐标，或者直接同步世界坐标
            // 假设 PlayerAvatar 的根节点是跟随 Head 的，那手就是相对 Head 的。
            // Day 4 简化：假设 Avatar 跟随 Head，那我们用 XR Origin 的相对坐标。
            // 为了不卡在数学上，我们假设 PlayerAvatar 跟随 XR Origin 移动。

            // **更简单的做法**：直接让这个 Visual 飞到 VR 手柄的世界坐标
            targetVisual.position = pos + transform.root.position; // 这是一个近似值，取决于你的 XR Origin 设置
            targetVisual.rotation = rot;
        }
    }
}