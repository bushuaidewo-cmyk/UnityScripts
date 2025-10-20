using UnityEngine;

/// <summary>
/// 用于动画事件触发销毁自身的简单脚本
/// </summary>
public class EffectAutoDestroy : MonoBehaviour
{
    // 动画事件调用此函数即可销毁自己
    public void DestroySelf()
    {
        Destroy(gameObject);
    }
}
