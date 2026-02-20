using UnityEngine;
using Fusion;
using System.Collections;

public class HandColocation : MonoBehaviour
{
    [Header("设置")]
    public OVRHand LeftHand;        // 必须手动拖入左手的 OVRHand 组件
    public Transform MainCamera;    // 拖入 CenterEyeAnchor 或 Main Camera
    public float HoldTime = 2.0f;   // 需要捏合多久
    public GameObject CalibrationVisual; // 一个可选的视觉提示（比如一个 Loading 圈）

    [Header("校准目标")]
    // 假设现实中的校准点（桌角）对应虚拟世界的 (0,0,0)
    public Vector3 TargetWorldPosition = Vector3.zero;
    public Vector3 TargetWorldForward = Vector3.forward; // 假设正前方是 Z 轴正向

    private float _pinchTimer = 0f;
    private bool _isCalibrated = false;

    void Start()
    {
        if (CalibrationVisual != null) CalibrationVisual.SetActive(false);
    }

    void Update()
    {
        // 1. 基础检查：左手是否被追踪
        if (LeftHand == null || !LeftHand.IsTracked)
        {
            ResetTimer();
            return;
        }

        // 2. 检测捏合 (食指 + 拇指)
        bool isPinching = LeftHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        if (isPinching)
        {
            // 累加时间
            _pinchTimer += Time.deltaTime;

            // 可选：显示进度反馈 (比如让前面的文字变色，或者 Loading 图标出现)
            if (CalibrationVisual != null) CalibrationVisual.SetActive(true);

            // 3. 时间达标，执行校准
            if (_pinchTimer >= HoldTime)
            {
                PerformCalibration();
                ResetTimer(); // 重置防止连续触发
            }
        }
        else
        {
            // 松手了，重置计时
            ResetTimer();
        }
    }

    void ResetTimer()
    {
        _pinchTimer = 0f;
        if (CalibrationVisual != null) CalibrationVisual.SetActive(false);
    }

    // --- 核心校准逻辑 ---
    public void PerformCalibration()
    {
        // 逻辑：玩家此刻站在现实世界的“锚点”上，且面向“正前方”。
        // 我们要瞬间移动 XR Origin，让玩家在虚拟世界里也正好站在 (0,0,0) 且面向 Z 轴。

        // 1. 获取当前头显相对于 Origin 的偏移
        // (因为我们移动的是 Origin，不是头显，头显是跟着 Origin 动的)
        Vector3 headPos = MainCamera.position;
        Vector3 originPos = transform.position; // XR Origin 的位置

        // 计算头显在 Origin 局部坐标系下的平面偏移 (忽略高度 Y，因为我们要对齐的是地面位置)
        // 假设 XR Origin 当前是 (0,0,0)，头显在 (x, y, z)
        // 我们希望移动 Origin 后，头显的世界坐标变成 (0, y, 0)

        float relativeX = headPos.x - originPos.x;
        float relativeZ = headPos.z - originPos.z;

        // 2. 移动 XR Origin
        // 新的 Origin 位置 = 目标点 (0,0,0) - 相对偏移
        Vector3 newOriginPos = new Vector3(
            TargetWorldPosition.x - relativeX,
            transform.position.y, // 保持高度不变（通常由 Guardian 系统决定地面高度）
            TargetWorldPosition.z - relativeZ
        );

        transform.position = newOriginPos;

        // 3. 旋转 XR Origin (校准朝向)
        // 获取头显当前的 Y 轴朝向
        float currentHeadY = MainCamera.eulerAngles.y;
        // 目标朝向 (比如 0 度)
        float targetY = 0f;

        float rotationDiff = targetY - currentHeadY;

        // 绕着头显的位置旋转 (这样玩家看的位置不变，世界在转)
        transform.RotateAround(MainCamera.position, Vector3.up, rotationDiff);

        // 反馈
        Debug.Log($"[Colocation] 校准完成！Origin 移动到了 {newOriginPos}");

        // 震动反馈 (如果是手势，没震动，只能靠声音或视觉)
        // 建议播放一个音效
        var audio = GetComponent<AudioSource>();
        if (audio) audio.Play();
    }
}