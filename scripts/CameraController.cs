using UnityEngine;

public class CameraController : MonoBehaviour
{
    public static CameraController instance;

    [Header("要跟随的目标")]
    public Transform target;

    [Header("垂直范围限制 (可选)")]
    public float minY = -10f;
    public float maxY = 10f;

    // 相机锁定状态
    private bool isLocked = false;
    private Vector3 lockedPosition;

    // 轴向锁定（支持只锁X或只锁Y）
    private bool lockXAxis = true;
    private bool lockYAxis = true;

    private void Awake()
    {
        instance = this;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (isLocked)
        {
            // ✅ 按轴锁定：被锁的轴取 lockedPosition，未锁的轴继续跟随
            float x = lockXAxis ? lockedPosition.x : target.position.x;
            float y = lockYAxis ? lockedPosition.y : Mathf.Clamp(target.position.y, minY, maxY);
            transform.position = new Vector3(x, y, transform.position.z);
            return;
        }

        // 正常跟随
        Vector3 targetPos = new Vector3(
            target.position.x,
            Mathf.Clamp(target.position.y, minY, maxY),
            transform.position.z
        );
        transform.position = targetPos;
    }



    /// <summary>
    /// 锁定相机（进入 CameraZone 时调用），把相机固定在当前帧位置，锁定X和Y。
    /// </summary>
    public void LockCamera()
    {
        isLocked = true;
        lockedPosition = transform.position;
        lockXAxis = true;
        lockYAxis = true;
    }

    /// <summary>
    /// 锁定相机到指定位置；可选择只锁X或只锁Y。
    /// （CameraLockZone 使用的三参数重载）
    /// </summary>
    public void LockCamera(Vector2 position, bool lockX, bool lockY)
    {
        isLocked = true;
        float y = Mathf.Clamp(position.y, minY, maxY);
        lockedPosition = new Vector3(position.x, y, transform.position.z);
        lockXAxis = lockX;
        lockYAxis = lockY;
    }


    /// <summary>
    /// 解锁相机（离开所有 CameraZone 时调用）
    /// </summary>
    public void UnlockCamera()
    {
        isLocked = false;
    }
}
