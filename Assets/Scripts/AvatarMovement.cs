using UnityEngine;
using Fusion;

public class AvatarMovement : NetworkBehaviour
{
    private Transform _headsetTransform;

    public override void Spawned()
    {
        // 1. 只有客户端自己需要绑定摄像机
        if (Object.HasInputAuthority)
        {
            if (Camera.main != null)
            {
                _headsetTransform = Camera.main.transform;
            }
            else
            {
                Debug.LogError("找不到 MainCamera，请检查 Tag！");
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 只有拥有输入权限的人（Client 自己）执行
        if (Object.HasInputAuthority && _headsetTransform != null)
        {
            // A. 本地先动起来（保证自己看到的画面是流畅无延迟的）
            transform.position = _headsetTransform.position;
            transform.rotation = _headsetTransform.rotation;

            // B. [关键修复] 发送 RPC 告诉 Host 我在哪
            // 使用 Unreliable 通道，因为位置更新非常频繁，丢一两包无所谓，追求速度
            RPC_SendPosition(_headsetTransform.position, _headsetTransform.rotation);
        }
    }

    // --- 新增：RPC 定义 ---
    // Source: InputAuthority (Client 发起)
    // Target: StateAuthority (Host 接收)
    // Channel: Unreliable (不保证送达，但速度最快，适合实时移动)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    private void RPC_SendPosition(Vector3 pos, Quaternion rot)
    {
        // 这段代码只在 Host 上运行

        // Host 收到坐标后，更新物体位置
        // Host 更新后，NetworkTransform 组件会自动把这个新位置同步给所有其他 Client
        transform.position = pos;
        transform.rotation = rot;
    }
}