using UnityEngine;

[DisallowMultipleComponent]
public class AttackEventHub : MonoBehaviour
{
    [Header("Animators")]
    [SerializeField] private Animator weaponAnimator;   // �󶨣�������� Animator����������������Ч��
    [SerializeField] private Animator bodyAnimator;     // ��ѡ����ɫ Animator���ٶ�ͬ����

    [Header("Offset Root")]
    [Tooltip("ƫ�Ƹ������� HandSocket �½�һ�� Offset �����壬�ѱ�����������£�")]
    [SerializeField] private Transform weaponOffsetRoot;

    [Header("Idle ״̬����͸����֡��")]
    [SerializeField] private string weaponIdleState = "player_weapon_idle";

    [Header("ѡ��")]
    [Tooltip("StopWeapon ʱ�Ƿ��ƫ�Ƹ�λΪ (0,0)")]
    [SerializeField] private bool resetOffsetOnStop = true;

    [Header("����/����")]
    [Tooltip("��ѡ��ʼ�ո����ɫ�������ڽ�ɫ�㼶�ڣ���ȡ����ѡ������ʱ��ʱ��ƫ�Ƹ������ɫ�㼶�����ٸ��档ֹͣʱ�Զ���ԭ��")]
    [SerializeField] private bool followBody = true;
    [Tooltip("��������ʱ����ʱ����ʱ�ҵ��ĸ������壨����=����������")]
    [SerializeField] private Transform detachedParent;

    // ����ʱ���棨��������/��ԭ��
    private Transform cachedParent;
    private Vector3 cachedLocalPos;
    private Quaternion cachedLocalRot;
    private Vector3 cachedLocalScale;
    private bool isDetached;

    // ���� X/Y ƫ�ƣ���λ = world units������/PPU��
    public void SetWeaponXYOffset(float x, float y)
    {
        if (!weaponOffsetRoot) return;
        var p = weaponOffsetRoot.localPosition;
        p.x = x; p.y = y;
        weaponOffsetRoot.localPosition = p;
    }

    // ��ͷ���ţ�����ƫ�ƣ�
    public void PlayWeapon(string stateName)
    {
        DetachIfNeededOnPlay();
        if (!weaponAnimator || string.IsNullOrEmpty(stateName)) return;

        int hash = Animator.StringToHash(stateName);

        // �����ظ� Play ��ͬ״̬�����Ӿ�����
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

    // ֹͣ/�� Idle������λƫ�� + ��ԭ���ӹ�ϵ
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

        // ���浱ǰ������ֲ� TRS
        cachedParent = weaponOffsetRoot.parent;
        cachedLocalPos = weaponOffsetRoot.localPosition;
        cachedLocalRot = weaponOffsetRoot.localRotation;
        cachedLocalScale = weaponOffsetRoot.localScale;

        // ���뵽Ŀ�길������������λ�ˣ�
        weaponOffsetRoot.SetParent(detachedParent, true);
        isDetached = true;
    }

    private void ReattachIfNeededOnStop()
    {
        if (!isDetached) return;
        if (!weaponOffsetRoot) { isDetached = false; return; }

        // ��ԭ���ӣ���������λ�ˣ������ָ�ԭ�ֲ� TRS
        weaponOffsetRoot.SetParent(cachedParent, true);
        weaponOffsetRoot.localPosition = cachedLocalPos;
        weaponOffsetRoot.localRotation = cachedLocalRot;
        weaponOffsetRoot.localScale = cachedLocalScale;

        cachedParent = null;
        isDetached = false;
    }

    private void OnDisable()
    {
        // ���������/����ǰ��������ԭ���ӹ�ϵ
        ReattachIfNeededOnStop();
    }
}