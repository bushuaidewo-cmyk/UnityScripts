using UnityEngine;

[DisallowMultipleComponent]
public class AttackEventHub : MonoBehaviour
{
    [Header("Animators")]
    [SerializeField] private Animator weaponAnimator;   // 绑定：本物体的 Animator（可用于武器或特效）
    [SerializeField] private Animator bodyAnimator;     // 可选：角色 Animator（速度同步）

    [Header("Offset Root")]
    [Tooltip("偏移根（建议 HandSocket 下建一个 Offset 空物体，把本对象放在其下）")]
    [SerializeField] private Transform weaponOffsetRoot;

    [Header("Idle 状态名（透明空帧）")]
    [SerializeField] private string weaponIdleState = "player_weapon_idle";

    [Header("选项")]
    [Tooltip("StopWeapon 时是否把偏移复位为 (0,0)")]
    [SerializeField] private bool resetOffsetOnStop = true;

    [Header("跟随/脱离")]
    [Tooltip("勾选：始终跟随角色（保持在角色层级内）。取消勾选：播放时临时将偏移根脱离角色层级，不再跟随。停止时自动还原。")]
    [SerializeField] private bool followBody = true;
    [Tooltip("当不跟随时，临时脱离时挂到哪个父物体（留空=场景根）。")]
    [SerializeField] private Transform detachedParent;

    // 运行时缓存（用于脱离/还原）
    private Transform cachedParent;
    private Vector3 cachedLocalPos;
    private Quaternion cachedLocalRot;
    private Vector3 cachedLocalScale;
    private bool isDetached;

    // 设置 X/Y 偏移（单位 = world units，像素/PPU）
    public void SetWeaponXYOffset(float x, float y)
    {
        if (!weaponOffsetRoot) return;
        var p = weaponOffsetRoot.localPosition;
        p.x = x; p.y = y;
        weaponOffsetRoot.localPosition = p;
    }

    // 从头播放（不改偏移）
    public void PlayWeapon(string stateName)
    {
        DetachIfNeededOnPlay();
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

    // 停止/回 Idle，并复位偏移 + 还原父子关系
    public void StopWeapon()
    {
        if (weaponAnimator)
        {
            int idleHash = Animator.StringToHash(weaponIdleState);
            if (weaponAnimator.HasState(0, idleHash))
                weaponAnimator.Play(idleHash, 0, 0f);
        }
        ReattachIfNeededOnStop();

        if (resetOffsetOnStop && weaponOffsetRoot)
        {
            var p = weaponOffsetRoot.localPosition;
            p.x = 0f; p.y = 0f;
            weaponOffsetRoot.localPosition = p;
        }

        if (bodyAnimator && weaponAnimator) weaponAnimator.speed = bodyAnimator.speed;
    }

    private void DetachIfNeededOnPlay()
    {
        if (followBody) return;
        if (!weaponOffsetRoot) return;
        if (isDetached) return;

        // 缓存当前父级与局部 TRS
        cachedParent = weaponOffsetRoot.parent;
        cachedLocalPos = weaponOffsetRoot.localPosition;
        cachedLocalRot = weaponOffsetRoot.localRotation;
        cachedLocalScale = weaponOffsetRoot.localScale;

        // 脱离到目标父级（保持世界位姿）
        weaponOffsetRoot.SetParent(detachedParent, true);
        isDetached = true;
    }

    private void ReattachIfNeededOnStop()
    {
        if (!isDetached) return;
        if (!weaponOffsetRoot) { isDetached = false; return; }

        // 还原父子（保持世界位姿），随后恢复原局部 TRS
        weaponOffsetRoot.SetParent(cachedParent, true);
        weaponOffsetRoot.localPosition = cachedLocalPos;
        weaponOffsetRoot.localRotation = cachedLocalRot;
        weaponOffsetRoot.localScale = cachedLocalScale;

        cachedParent = null;
        isDetached = false;
    }

    private void OnDisable()
    {
        // 组件被禁用/销毁前，尽量还原父子关系
        ReattachIfNeededOnStop();
    }
}