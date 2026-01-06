using System.Collections.Generic;
using UnityEngine;

public class HitboxController : MonoBehaviour
{
    [Tooltip("攻击判定框列表（需要在 Inspector 中把 Collider2D 拖进去，并勾选 IsTrigger）")]
    [SerializeField] private List<Collider2D> hitboxes = new List<Collider2D>();

    [Tooltip("基础伤害（如果未注入则使用此默认值）")]
    [SerializeField] private int baseDamage = 10;

    public System.Action<Collider2D, int, GameObject, Vector2> OnHitEnemy;

    private GameObject _currentHitVfxPrefab;

    private readonly HashSet<Collider2D> _hitOnceWindow = new HashSet<Collider2D>();

    void Awake()
    {
        // 1. 确保自身有 Rigidbody2D (Kinematic)，这是 Trigger 生效的关键
        var rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; // 不受重力影响
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 提高快速挥动的检测精度
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep; // 防止休眠导致检测失效
        }

        foreach (var c in hitboxes)
        {
            if (c)
            {
                c.enabled = false;
                c.isTrigger = true;
            }
        }
    }

    public void InjectBaseDamage(int dmg) => baseDamage = dmg;

    public void SetHitVfx(GameObject vfx)
    {
        _currentHitVfxPrefab = vfx;
    }

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
        bool anyOpen = false;
        Collider2D activeHitbox = null;
        foreach (var c in hitboxes)
        {
            if (c && c.enabled) { anyOpen = true; activeHitbox = c; break; }
        }
        if (!anyOpen) return;

        if (_hitOnceWindow.Contains(other)) return;
        _hitOnceWindow.Add(other);

        int dmg = baseDamage > 0 ? baseDamage : 1;

        Vector2 hitPoint = other.bounds.center; 
        if (activeHitbox != null)
        {
            Vector2 weaponCenter = activeHitbox.bounds.center;
            hitPoint = other.ClosestPoint(weaponCenter);
        }

        if (OnHitEnemy != null)
        {
            OnHitEnemy.Invoke(other, dmg, _currentHitVfxPrefab, hitPoint);
        }
        else
        {
            other.SendMessageUpwards("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
        }
    }
}