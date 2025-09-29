using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    private PlayerController player;

    void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    // 统一后保留
    public void OnAttackStart() { player?.OnAttackStart(); }
    public void OnAttackEnd() { player?.OnAttackEnd(); }

    // 空中攻击（若动画里有）
    public void OnAirAttackStart() { player?.OnAirAttackStart(); }
    public void OnAirAttackEnd() { player?.OnAirAttackEnd(); }

    // 其他已有事件按需补：
    public void OnTurnStart() { player?.OnTurnStart(); }
    public void OnTurnFlip() { player?.OnTurnFlip(); }
    public void OnTurnEnd() { player?.OnTurnEnd(); }
    public void OnJumpStart() { player?.OnJumpStart(); }
    public void OnJumpEnd() { player?.OnJumpEnd(); }
    public void OnDuckAttackStart() { player?.OnDuckAttackStart(); }
    public void OnDuckAttackEnd() { player?.OnDuckAttackEnd(); }
    public void OnDuckFwdAttackStart() { player?.OnDuckFwdAttackStart(); }
    public void OnDuckFwdAttackEnd() { player?.OnDuckFwdAttackEnd(); }
    public void OnDuckAttackEndStart() { player?.OnDuckAttackEndStart(); }
    public void OnDuckAttackEndEnd() { player?.OnDuckAttackEndEnd(); }
    public void OnDuckFwdAttackEndStart() { player?.OnDuckFwdAttackEndStart(); }
    public void OnDuckFwdAttackEndEnd() { player?.OnDuckFwdAttackEndEnd(); }

    public void OnDuckCancelable() { player?.OnDuckCancelable(); }

}