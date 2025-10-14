using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class CameraZone : MonoBehaviour
{
    [Header("进入区域后相机是否锁定轴向")]
    public bool lockX = true;
    public bool lockY = true;

    private static int globalActiveZoneCount = 0;
    private readonly HashSet<Rigidbody2D> _playersInside = new HashSet<Rigidbody2D>();
    private BoxCollider2D _col;

    private void Reset()
    {
        _col = GetComponent<BoxCollider2D>();
        _col.isTrigger = true;
    }

    private void OnValidate()
    {
        _col = GetComponent<BoxCollider2D>();
        if (_col) _col.isTrigger = true;
    }

    private void Awake()
    {
        _col = GetComponent<BoxCollider2D>();
        _col.isTrigger = true;
    }

    private static bool IsPlayerCollider(Collider2D other, out Rigidbody2D playerRb)
    {
        playerRb = other.attachedRigidbody;

        if (playerRb != null)
        {
            if (playerRb.CompareTag("Player")) return true;
            var root = playerRb.transform.root;
            if (root && root.CompareTag("Player")) return true;
        }
        else
        {
            if (other.CompareTag("Player")) return true;
            var root = other.transform.root;
            if (root && root.CompareTag("Player"))
            {
                playerRb = root.GetComponent<Rigidbody2D>();
                return true;
            }
        }
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayerCollider(other, out var rb)) return;

        if (_playersInside.Add(rb))
        {
            globalActiveZoneCount++;
            if (CameraController.instance != null)
            {
                // 锁定相机到当前相机位置，只锁指定轴
                CameraController.instance.LockCamera(
                    CameraController.instance.transform.position,
                    lockX,
                    lockY
                );
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayerCollider(other, out var rb)) return;

        if (_playersInside.Remove(rb))
        {
            globalActiveZoneCount = Mathf.Max(0, globalActiveZoneCount - 1);
            if (globalActiveZoneCount == 0 && CameraController.instance != null)
            {
                CameraController.instance.UnlockCamera();
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!_col) _col = GetComponent<BoxCollider2D>();
        if (!_col) return;

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.25f);
        Vector3 size = new Vector3(_col.size.x, _col.size.y, 0f);
        Vector3 center = transform.position + (Vector3)_col.offset;
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, size);
    }
}
