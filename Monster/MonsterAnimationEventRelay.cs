using UnityEngine;

/// <summary>
/// 动画事件中继器：挂在 Animator 节点（Monster_test）上。
/// 统一用固定的方法名来接收动画事件，避免字符串 key 歧义。
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

    // 巡逻直线阶段（沿用原事件名）
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

    // 巡逻跳跃阶段（沿用原事件名）
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

    // =============== 发现阶段：Find/Back 专用事件名（新增） ===============

    // 发现-跟随（Find）
    public void findmoveEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] findmoveEffectPrefab()");
        controller?.OnFxFindMove();
    }

    public void findrestEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] findrestEffectPrefab()");
        controller?.OnFxFindRest();
    }

    public void findjumpEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] findjumpEffectPrefab()");
        controller?.OnFxFindJump();
    }

    public void findjumpRestEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] findjumpRestEffectPrefab()");
        controller?.OnFxFindJumpRest();
    }

    // 发现-后撤/后退（Back）
    public void backmoveEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] backmoveEffectPrefab()");
        controller?.OnFxBackMove();
    }

    public void backrestEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] backrestEffectPrefab()");
        controller?.OnFxBackRest();
    }

    public void backjumpEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] backjumpEffectPrefab()");
        controller?.OnFxBackJump();
    }

    public void backjumpRestEffectPrefab()
    {
        if (debugEvents) Debug.Log("[Relay] backjumpRestEffectPrefab()");
        controller?.OnFxBackJumpRest();
    }
}