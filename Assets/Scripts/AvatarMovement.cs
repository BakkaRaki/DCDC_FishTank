using UnityEngine;
using Fusion;

public class AvatarMovement : NetworkBehaviour
{
    private Transform _headsetTransform;

    public override void Spawned()
    {
        // 1. ֻ�пͻ����Լ���Ҫ�������
        if (Object.HasInputAuthority)
        {
            if (Camera.main != null)
            {
                _headsetTransform = Camera.main.transform;
            }
            else
            {
                Debug.LogError("�Ҳ��� MainCamera������ Tag��");
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // ֻ��ӵ������Ȩ�޵��ˣ�Client �Լ���ִ��
        if (Object.HasInputAuthority && _headsetTransform != null)
        {
            if (!AquariumColocationGate.IsReady)
                return;

            // A. �����ȶ���������֤�Լ������Ļ������������ӳٵģ�
            transform.position = _headsetTransform.position;
            transform.rotation = _headsetTransform.rotation;

            // B. [�ؼ��޸�] ���� RPC ���� Host ������
            // ʹ�� Unreliable ͨ������Ϊλ�ø��·ǳ�Ƶ������һ��������ν��׷���ٶ�
            RPC_SendPosition(_headsetTransform.position, _headsetTransform.rotation);
        }
    }

    // --- ������RPC ���� ---
    // Source: InputAuthority (Client ����)
    // Target: StateAuthority (Host ����)
    // Channel: Unreliable (����֤�ʹ���ٶ���죬�ʺ�ʵʱ�ƶ�)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable)]
    private void RPC_SendPosition(Vector3 pos, Quaternion rot)
    {
        // ��δ���ֻ�� Host ������

        // Host �յ�����󣬸�������λ��
        // Host ���º�NetworkTransform ������Զ��������λ��ͬ������������ Client
        transform.position = pos;
        transform.rotation = rot;
    }
}