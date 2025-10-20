using UnityEngine;

/// <summary>
/// �����¼��м��������� Animator �ڵ㣨Monster_test���ϣ�
/// ���ڰѶ����¼�ת���� MonsterController��
/// </summary>
public class MonsterAnimationEventRelay : MonoBehaviour
{
    private MonsterController controller;

    void Awake()
    {
        // �Զ��ڸ����в��� MonsterController��֧�ֶ��Ƕ�ף�
        controller = GetComponentInParent<MonsterController>();
        if (controller == null)
            Debug.LogWarning($"[MonsterAnimationEventRelay] δ�ҵ� MonsterController��·����{transform.name}");
    }

    // === ���º������Ʊ����붯���¼�һ�� ===
    public void OnSpawnEffect() => controller?.OnSpawnEffect();
    public void OnIdleEffect() => controller?.OnIdleEffect();
    public void OnPatrolMoveEffect() => controller?.OnPatrolMoveEffect();
    public void OnPatrolRestEffect() => controller?.OnPatrolRestEffect();
    public void OnPatrolJumpEffect() => controller?.OnPatrolJumpEffect();
    public void OnPatrolJumpRestEffect() => controller?.OnPatrolJumpRestEffect();



}
