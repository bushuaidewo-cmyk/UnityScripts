using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HitboxDebug : MonoBehaviour
{
    private Collider2D col;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col == null) Debug.LogWarning("[HitboxDebug] missing Collider2D", this);
        col.isTrigger = true; // 强制是 trigger，安全设置
    }

    void OnEnable()
    {
        Debug.Log($"[HitboxDebug] {name} enabled at time {Time.time}", this);
    }

    void OnDisable()
    {
        Debug.Log($"[HitboxDebug] {name} disabled at time {Time.time}", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[HitboxDebug] {name} OnTriggerEnter2D with {other.name} (tag={other.tag}) at time {Time.time}", this);
    }

    void OnDrawGizmos()
    {
        if (col == null) col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = enabled ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(0.2f, 0.2f, 0.2f, 0.3f);
        var bounds = col.bounds;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}