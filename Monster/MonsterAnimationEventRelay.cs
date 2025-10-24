using UnityEngine;

/// <summary>
/// 动画事件中继器：挂在 Animator 节点（Monster_test）上，
/// 改为使用固定的无参事件方法，避免字符串 key 带来的歧义。
/// </summary>
public class MonsterAnimationEventRelay : MonoBehaviour
{
    [SerializeField] private bool debugEvents = false;

    private MonsterController controller;

    void Awake()
    {
        controller = GetComponentInParent<MonsterController>();
        if (controller == null)
            Debug.LogWarning($"[MonsterAnimationEventRelay] 未找到 MonsterController！路径：{transform.name}");
    }

    // 出生阶段
    public void spawnEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] spawnEffectPrefab()");
        controller?.OnFxSpawn();
    }

    public void idleEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] idleEffectPrefab()");
        controller?.OnFxIdle();
    }

    // 巡逻直线阶段
    public void moveEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] moveEffectPrefab()");
        controller?.OnFxMove();
    }

    public void restEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] restEffectPrefab()");
        controller?.OnFxRest();
    }

    // 跳跃（普通/自动共用资源）
    public void jumpEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] jumpEffectPrefab()");
        controller?.OnFxJump();
    }

    public void jumpRestEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] jumpRestEffectPrefab()");
        controller?.OnFxJumpRest();
    }
}