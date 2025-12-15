using UnityEngine;

/// <summary>
/// 挂载在玩家武器的 Hitbox 上 (Trigger)。
/// 负责检测怪物的受击框 (EMHitbox) 并传递伤害。
/// </summary>
public class PlayerWeaponHitbox : MonoBehaviour
{
    [Header("攻击参数")]
    [Tooltip("该武器造成的伤害值")]
    public int damage = 10;

    [Header("目标层级过滤")]
    [Tooltip("填写怪物的受击框层级名称，例如 'EMHitbox' 或 'Monster'")]
    public string targetLayerName = "EMHitbox";

    [Tooltip("备用层级名称，例如 'Monster'，防止配置遗漏")]
    public string altTargetLayerName = "Monster";

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 层级检查 (性能优化：先比对层级再获取组件)
        int hitLayer = other.gameObject.layer;
        string hitLayerName = LayerMask.LayerToName(hitLayer);

        // 检查是否撞到了配置的目标层
        bool isTarget = (hitLayerName == targetLayerName) || (hitLayerName == altTargetLayerName);

        if (!isTarget) return;

        // 2. 尝试获取怪物的控制器
        // 通常 MonsterController 挂在父节点或根节点，所以使用 GetComponentInParent
        MonsterController monster = other.GetComponentInParent<MonsterController>();

        if (monster != null)
        {
            // 3. 执行伤害逻辑
            // Debug.Log($"[PlayerWeapon] 命中怪物: {monster.name}, 造成伤害: {damage}");
            monster.TakeHit(damage);
        }
    }
}