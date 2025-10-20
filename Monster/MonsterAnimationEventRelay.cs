using UnityEngine;

/// <summary>
/// 动画事件中继器：挂在 Animator 节点（Monster_test）上，
/// 用于把动画事件转发给 MonsterController。
/// </summary>
public class MonsterAnimationEventRelay : MonoBehaviour
{
    private MonsterController controller;

    void Awake()
    {
        // 自动在父层中查找 MonsterController（支持多层嵌套）
        controller = GetComponentInParent<MonsterController>();
        if (controller == null)
            Debug.LogWarning($"[MonsterAnimationEventRelay] 未找到 MonsterController！路径：{transform.name}");
    }

    // === 以下函数名称必须与动画事件一致 ===
    public void OnSpawnEffect() => controller?.OnSpawnEffect();
    public void OnIdleEffect() => controller?.OnIdleEffect();
    public void OnPatrolMoveEffect() => controller?.OnPatrolMoveEffect();
    public void OnPatrolRestEffect() => controller?.OnPatrolRestEffect();
    public void OnPatrolJumpEffect() => controller?.OnPatrolJumpEffect();
    public void OnPatrolJumpRestEffect() => controller?.OnPatrolJumpRestEffect();



}
