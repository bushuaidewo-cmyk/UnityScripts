using UnityEngine;

[DisallowMultipleComponent]
public class AttackEventHub : MonoBehaviour
{
    [Header("Animators")]
    [SerializeField] private Animator weaponAnimator;   // 本物体的 Animator（武器或特效）
    [SerializeField] private Animator bodyAnimator;     // 可选：角色 Animator（速度同步）

    [Header("Offset Root")]
    [Tooltip("偏移根（建议 HandSocket 下建一个 Offset 空物体，把本对象放在其下）")]
    [SerializeField] private Transform weaponOffsetRoot;

    [Header("Idle 状态名（透明空帧）")]
    [SerializeField] private string weaponIdleState = "player_weapon_idle";

    [Header("选项")]
    [Tooltip("StopWeapon 时是否把偏移复位为 (0,0)")]
    [SerializeField] private bool resetOffsetOnStop = true;

    // 设置 X/Y 偏移（单位 = world units，像素/PPU）
    public void SetWeaponXYOffset(float x, float y)
    {
        if (!weaponOffsetRoot) return;
        var p = weaponOffsetRoot.localPosition;
        p.x = x; p.y = y;
        weaponOffsetRoot.localPosition = p;
    }

    // 脚本侧：设置 bodyAnimator 以进行速度同步（隐藏出动画事件列表）
    internal void SetBodyAnimator(Animator a) => bodyAnimator = a;

    // 脚本侧：播放武器/特效的 Animator 状态（隐藏出动画事件列表）
    internal void PlayWeapon(string stateName)
    {
        if (!weaponAnimator || string.IsNullOrEmpty(stateName)) return;

        int hash = Animator.StringToHash(stateName);

        // 避免重复 Play 相同状态导致视觉抖动
        var st = weaponAnimator.GetCurrentAnimatorStateInfo(0);
        if (st.shortNameHash == hash && !weaponAnimator.IsInTransition(0))
        {
            if (bodyAnimator) weaponAnimator.speed = bodyAnimator.speed;
            return;
        }

        if (!weaponAnimator.HasState(0, hash)) return;

        weaponAnimator.Play(hash, 0, 0f);
        if (bodyAnimator) weaponAnimator.speed = bodyAnimator.speed;
    }

    // 脚本侧：停止/回 Idle，并可复位偏移（隐藏出动画事件列表）
    internal void StopWeapon()
    {
        if (weaponAnimator)
        {
            int idleHash = Animator.StringToHash(weaponIdleState);
            if (weaponAnimator.HasState(0, idleHash))
                weaponAnimator.Play(idleHash, 0, 0f);
        }

        if (resetOffsetOnStop && weaponOffsetRoot)
        {
            var p = weaponOffsetRoot.localPosition;
            p.x = 0f; p.y = 0f;
            weaponOffsetRoot.localPosition = p;
        }

        if (bodyAnimator && weaponAnimator) weaponAnimator.speed = bodyAnimator.speed;
    }
}