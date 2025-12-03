using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class CameraZone : MonoBehaviour
{
    [Header("进入区域后相机是否锁定轴向")]
    public bool lockX = true;
    public bool lockY = true;

    // ★ 新增：出生在区内时，相机矫正到哪一侧边界
    public enum SpawnSnapEdge { None, Left, Right }
    [Header("出生在本区内时，相机X矫正到的边（仅出生当下生效）")]
    [SerializeField] private SpawnSnapEdge spawnSnapEdge = SpawnSnapEdge.Right;

    private static int globalActiveZoneCount = 0;
    private readonly HashSet<Rigidbody2D> _playersInside = new HashSet<Rigidbody2D>();
    private BoxCollider2D _col;

    [Header("基于点的检测（替代触发器几何差引发抖动）")]
    [SerializeField] private bool usePointAnchor = true;          // 开启“点检测”
    [SerializeField] private Vector2 anchorOffset = Vector2.zero; // 相对玩家(=相机target)的局部偏移
    

    // 内部状态（点检测）
    private bool _wasInsideByPoint = false;
    private float _leftX, _rightX;


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

    // ★ 新增：出生即在区内 → 立刻把相机X矫正到选定边界并锁定
    private void Start()
    {
        if (!_col) _col = GetComponent<BoxCollider2D>();
        var b = _col.bounds;
        _leftX = b.min.x;
        _rightX = b.max.x;

        if (!usePointAnchor) return;

        var cc = CameraController.instance;
        if (cc == null || cc.target == null) return;

        Vector2 anchor = (Vector2)cc.target.position + anchorOffset;

        // 用“点”判断出生是否在区内
        bool spawnInside = _col.OverlapPoint(anchor);
        _wasInsideByPoint = spawnInside;

        if (spawnInside)
        {
            // 先把相机矫正到指定边，再按本区配置锁
            float snapX = cc.transform.position.x;
            if (spawnSnapEdge == SpawnSnapEdge.Left) snapX = _leftX;
            if (spawnSnapEdge == SpawnSnapEdge.Right) snapX = _rightX;

            float y = Mathf.Clamp(cc.target.position.y, cc.minY, cc.maxY);
            cc.transform.position = new Vector3(snapX, y, cc.transform.position.z);
            cc.LockCamera(cc.transform.position, lockX, lockY);
        }
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
        _playersInside.Add(rb);
        globalActiveZoneCount++;
        // if (!usePointAnchor) 才用触发器驱动相机（老逻辑）；开启点检测后不再在这里锁
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayerCollider(other, out var rb)) return;
        if (_playersInside.Remove(rb))
            globalActiveZoneCount = Mathf.Max(0, globalActiveZoneCount - 1);
        // if (!usePointAnchor) 才在这里解锁；点检测模式用 LateUpdate 的“锚点跨线”来解锁
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
    private void LateUpdate()
    {
        if (!usePointAnchor) return;
        var cc = CameraController.instance;
        if (cc == null || cc.target == null || _col == null) return;

        // 以“一个点”作为角色检测点（可用脚底、身体中心，按 anchorOffset 调）
        Vector2 anchor = (Vector2)cc.target.position + anchorOffset;

        bool insideNow = _col.OverlapPoint(anchor);

        // —— 从区外 -> 区内（锚点跨入）——
        if (!_wasInsideByPoint && insideNow)
        {
            // 只在点刚跨线时对齐到“最近边”，避免被整块碰撞体强行推挤
            float distL = Mathf.Abs(anchor.x - _leftX);
            float distR = Mathf.Abs(anchor.x - _rightX);
            float snapX = (distL <= distR) ? _leftX : _rightX;

            float y = Mathf.Clamp(cc.target.position.y, cc.minY, cc.maxY);

            // 先对齐到边，再锁（同帧完成，0 抖）
            cc.transform.position = new Vector3(snapX, y, cc.transform.position.z);
            cc.LockCamera(cc.transform.position, lockX, lockY);
        }

        // —— 从区内 -> 区外（锚点跨出）——
        if (_wasInsideByPoint && !insideNow)
        {
            // 先对齐到“将要跟随的位置”，再解锁（同帧完成，0 抖）
            float y = Mathf.Clamp(cc.target.position.y, cc.minY, cc.maxY);
            cc.transform.position = new Vector3(cc.target.position.x, y, cc.transform.position.z);
            cc.UnlockCamera();
        }

        _wasInsideByPoint = insideNow;
    }


}
