using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    private PlayerController player;

    private void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    // 地面普通攻击
    public void OnAttackStart() => player?.OnAttackStart();
    public void OnAttackEnd() => player?.OnAttackEnd();

    // 下蹲攻击
    public void OnDuckAttackStart() => player?.OnDuckAttackStart();
    public void OnDuckAttackEnd() => player?.OnDuckAttackEnd();
    

    // 下蹲前进攻击
    public void OnDuckFwdAttackStart() => player?.OnDuckFwdAttackStart();
    public void OnDuckFwdAttackEnd() => player?.OnDuckFwdAttackEnd();
 
}