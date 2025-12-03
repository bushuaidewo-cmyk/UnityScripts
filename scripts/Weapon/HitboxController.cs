using System.Collections.Generic;
using UnityEngine;

public class HitboxController : MonoBehaviour
{
    [Tooltip("按索引顺序的命中体（isTrigger=true，初始请禁用）")]
    [SerializeField] private List<Collider2D> hitboxes = new List<Collider2D>();

    [Tooltip("基础伤害，<=0 时默认 1")]
    [SerializeField] private int baseDamage = 10;

    // 在一个“开窗”内已经命中的对象（避免同一帧重复）
    private readonly HashSet<Collider2D> _hitOnceWindow = new HashSet<Collider2D>();

    void Awake()
    {
        // 进入前全部关闭（如果没在编辑器里关）
        foreach (var c in hitboxes)
            if (c) c.enabled = false;
    }

    public void InjectBaseDamage(int dmg) => baseDamage = dmg;

    public void Open(int index)
    {
        if (!Valid(index)) return;
        var c = hitboxes[index];
        if (!c) return;
        c.enabled = true;
        _hitOnceWindow.Clear();
    }

    public void Close(int index)
    {
        if (!Valid(index)) return;
        var c = hitboxes[index];
        if (!c) return;
        c.enabled = false;
        _hitOnceWindow.Clear();
    }

    public void CloseAll()
    {
        foreach (var c in hitboxes)
            if (c) c.enabled = false;
        _hitOnceWindow.Clear();
    }

    private bool Valid(int i) => i >= 0 && i < hitboxes.Count;

    void OnTriggerEnter2D(Collider2D other)
    {
        // 任何开启的命中体都可能触发（统一处理）
        bool anyOpen = false;
        foreach (var c in hitboxes)
        {
            if (c && c.enabled) { anyOpen = true; break; }
        }
        if (!anyOpen) return;

        if (_hitOnceWindow.Contains(other)) return; // 防一帧多次
        _hitOnceWindow.Add(other);

        int dmg = baseDamage > 0 ? baseDamage : 1;

        // 发送伤害给怪物或其他可受击对象（不要求目标一定实现）
        other.SendMessageUpwards("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
        // 若需命中特效：可在此调用 Weapon FX Hub 播一个 impact 状态（通过引用或事件再扩展）
    }
}