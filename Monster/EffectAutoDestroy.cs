using UnityEngine;

/// <summary>
/// ���ڶ����¼�������������ļ򵥽ű�
/// </summary>
public class EffectAutoDestroy : MonoBehaviour
{
    // �����¼����ô˺������������Լ�
    public void DestroySelf()
    {
        Destroy(gameObject);
    }
}
