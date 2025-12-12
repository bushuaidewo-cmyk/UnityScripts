using UnityEngine;

// 轻量转发器：把子命中体的 Trigger/Collision 事件转发到 PlayerController。
// 不改任何层/标签；只做消息转发。运行时由 PlayerController.Awake 自动挂载。
public class PlayerHitboxEventRelay : MonoBehaviour
{
    private PlayerController _player;

    public void Init(PlayerController player)
    {
        _player = player;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_player != null)
            _player.OnPlayerHitboxTriggerEnter2D(other);
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (_player != null)
            _player.OnPlayerHitboxCollisionEnter2D(col);
    }
}