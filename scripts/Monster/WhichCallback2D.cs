using UnityEngine;
public class WhichCallback2D : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other) { Debug.Log($"[Trigger] {name} hit {other.name}"); }
    void OnCollisionEnter2D(Collision2D col) { Debug.Log($"[Collision] {name} hit {col.collider.name}"); }
}