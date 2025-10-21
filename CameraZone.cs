using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public class CameraZone : MonoBehaviour
{
    [Header("�������������Ƿ���������")]
    public bool lockX = true;
    public bool lockY = true;

    // �� ����������������ʱ�������������һ��߽�
    public enum SpawnSnapEdge { None, Left, Right }
    [Header("�����ڱ�����ʱ�����X�������ıߣ�������������Ч��")]
    [SerializeField] private SpawnSnapEdge spawnSnapEdge = SpawnSnapEdge.Right;

    private static int globalActiveZoneCount = 0;
    private readonly HashSet<Rigidbody2D> _playersInside = new HashSet<Rigidbody2D>();
    private BoxCollider2D _col;

    [Header("���ڵ�ļ�⣨������������β�����������")]
    [SerializeField] private bool usePointAnchor = true;          // ���������⡱
    [SerializeField] private Vector2 anchorOffset = Vector2.zero; // ������(=���target)�ľֲ�ƫ��
    

    // �ڲ�״̬�����⣩
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

    // �� ������������������ �� ���̰����X������ѡ���߽粢����
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

        // �á��㡱�жϳ����Ƿ�������
        bool spawnInside = _col.OverlapPoint(anchor);
        _wasInsideByPoint = spawnInside;

        if (spawnInside)
        {
            // �Ȱ����������ָ���ߣ��ٰ�����������
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
        // if (!usePointAnchor) ���ô�����������������߼������������������������
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayerCollider(other, out var rb)) return;
        if (_playersInside.Remove(rb))
            globalActiveZoneCount = Mathf.Max(0, globalActiveZoneCount - 1);
        // if (!usePointAnchor) �����������������ģʽ�� LateUpdate �ġ�ê����ߡ�������
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

        // �ԡ�һ���㡱��Ϊ��ɫ���㣨���ýŵס��������ģ��� anchorOffset ����
        Vector2 anchor = (Vector2)cc.target.position + anchorOffset;

        bool insideNow = _col.OverlapPoint(anchor);

        // ���� ������ -> ���ڣ�ê����룩����
        if (!_wasInsideByPoint && insideNow)
        {
            // ֻ�ڵ�տ���ʱ���뵽������ߡ������ⱻ������ײ��ǿ���Ƽ�
            float distL = Mathf.Abs(anchor.x - _leftX);
            float distR = Mathf.Abs(anchor.x - _rightX);
            float snapX = (distL <= distR) ? _leftX : _rightX;

            float y = Mathf.Clamp(cc.target.position.y, cc.minY, cc.maxY);

            // �ȶ��뵽�ߣ�������ͬ֡��ɣ�0 ����
            cc.transform.position = new Vector3(snapX, y, cc.transform.position.z);
            cc.LockCamera(cc.transform.position, lockX, lockY);
        }

        // ���� ������ -> ���⣨ê����������
        if (_wasInsideByPoint && !insideNow)
        {
            // �ȶ��뵽����Ҫ�����λ�á����ٽ�����ͬ֡��ɣ�0 ����
            float y = Mathf.Clamp(cc.target.position.y, cc.minY, cc.maxY);
            cc.transform.position = new Vector3(cc.target.position.x, y, cc.transform.position.z);
            cc.UnlockCamera();
        }

        _wasInsideByPoint = insideNow;
    }


}
