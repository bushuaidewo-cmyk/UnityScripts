using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    private PlayerController player;

    void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    // 攻击锁相关
    public void OnAttackStart() { player?.OnAttackStart(); }
    public void OnAttackEnd() { player?.OnAttackEnd(); }

    // 空中攻击事件（动画里可以直接调用这些）
    public void OnAirAttackStart() { player?.OnAirAttackStart(); }
    public void OnAirAttackEnd() { player?.OnAirAttackEnd(); }

    // 转身事件（地面 turn 动画需要这些事件）
    public void OnTurnStart() { player?.OnTurnStart(); }
    public void OnTurnFlip() { player?.OnTurnFlip(); }
    public void OnTurnEnd() { player?.OnTurnEnd(); }

    // 跳跃/落地
    public void OnJumpStart() { player?.OnJumpStart(); }
    public void OnJumpEnd() { player?.OnJumpEnd(); }

    // 下蹲攻击事件
    public void OnDuckAttackStart() { player?.OnDuckAttackStart(); }
    public void OnDuckAttackEnd() { player?.OnDuckAttackEnd(); }
    public void OnDuckAttackEndStart() { player?.OnDuckAttackEndStart(); }
    public void OnDuckAttackEndEnd() { player?.OnDuckAttackEndEnd(); }

    // 下蹲前进攻击
    public void OnDuckFwdAttackStart() { player?.OnDuckFwdAttackStart(); }
    public void OnDuckFwdAttackEnd() { player?.OnDuckFwdAttackEnd(); }
    public void OnDuckFwdAttackEndStart() { player?.OnDuckFwdAttackEndStart(); }
    public void OnDuckFwdAttackEndEnd() { player?.OnDuckFwdAttackEndEnd(); }

    // 下蹲取消与起身
    public void OnDuckCancelable() { player?.OnDuckCancelable(); }
    public void OnGetUpStart() { player?.OnGetUpStart(); }
    public void OnGetUpEnd() { player?.OnGetUpEnd(); }
}