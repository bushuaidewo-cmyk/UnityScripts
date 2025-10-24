using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 怪物生成器（批量 + 间隔）：
/// - maxSpawnCount == 0  => 不出生
/// - 首批立即刷（按 spawnBatchCount，受剩余额度截断）
/// - 每 spawnInterval 秒再刷一批，直到“总出生数”达到 maxSpawnCount
/// - Points：同一批里若只有 1 个点，则同点重叠生成；多点则按顺序/随机分配
/// - Area：同一批在区域内随机散点
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("怪物配置 ScriptableObject")]
    public MonsterConfig monsterConfig;

    // 运行时
    private readonly List<GameObject> alive = new List<GameObject>(); // 当前场景仍存活
    private int spawnedTotal = 0;                                      // 累计已出生（总量上限依据此计）
    private bool loopRunning = false;                                  // 防止重复开协程
    private int nextPointIndex = 0;                                    // 顺序模式下的点序号

    void Start()
    {
        if (monsterConfig == null)
        {
            Debug.LogError("[MonsterSpawner] 未分配 MonsterConfig!");
            enabled = false;
            return;
        }

        var spawn = monsterConfig.spawnConfig;

        // maxSpawnCount == 0 表示不出生
        if (spawn.maxSpawnCount == 0)
        {
            Debug.Log("[MonsterSpawner] maxSpawnCount == 0,跳过出生。");
            return;
        }

        // ✅ 首批：立即刷一次（受剩余额度截断）
        int firstBatch = CalcBatchToSpawn();
        if (firstBatch > 0) SpawnBatch(firstBatch);

        // ✅ 循环：先等一个间隔，再刷下一批（避免和首批叠在同一帧）
        if (spawn.spawnInterval > 0f && spawnedTotal < spawn.maxSpawnCount && !loopRunning)
            StartCoroutine(SpawnLoop());
    }

    IEnumerator SpawnLoop()
    {
        loopRunning = true;
        var spawn = monsterConfig.spawnConfig;

        while (enabled && monsterConfig != null)
        {
            yield return new WaitForSeconds(spawn.spawnInterval);

            if (spawn.maxSpawnCount == 0) break; // 0 表示不出生，保险判断
            if (spawnedTotal >= spawn.maxSpawnCount) continue;

            int toSpawn = CalcBatchToSpawn();
            if (toSpawn > 0) SpawnBatch(toSpawn);
        }

        loopRunning = false;
    }

    /// <summary>
    /// 计算本批应刷数量（受剩余名额截断）
    /// </summary>
    private int CalcBatchToSpawn()
    {
        var spawn = monsterConfig.spawnConfig;
        if (spawn.maxSpawnCount == 0) return 0;

        int remain = Mathf.Max(0, spawn.maxSpawnCount - spawnedTotal);
        if (remain == 0) return 0;

        int batch = Mathf.Max(1, spawn.spawnBatchCount);
        return Mathf.Min(batch, remain);
    }

    /// <summary>
    /// 刷一批
    /// </summary>
    private void SpawnBatch(int count)
    {
        var spawn = monsterConfig.spawnConfig;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = GetSpawnPosition(spawn);

            // 朝向
            bool faceRight = true;
            if (spawn.spawnOrientation == Orientation.FacePlayer)
            {
                var player = GameObject.FindWithTag("Player")?.transform;
                if (player) faceRight = player.position.x >= pos.x;
            }
            else if (spawn.spawnOrientation == Orientation.FaceLeft)
            {
                faceRight = false;
            }

            // Prefab 校验
            if (monsterConfig.monsterPrefab == null)
            {
                Debug.LogError("[MonsterSpawner] 未配置 monsterPrefab！");
                return;
            }

            // 实例化
            GameObject go = Instantiate(monsterConfig.monsterPrefab, pos, Quaternion.identity);

            // 设定朝向（仅用旋转，不改 scale）
            go.transform.rotation = faceRight ? Quaternion.identity : Quaternion.Euler(0, 180f, 0);

            // 确保 MonsterController
            var ctrl = go.GetComponentInChildren<MonsterController>();
            if (ctrl == null)
            {
                Debug.LogWarning($"[MonsterSpawner] {go.name} 未找到 MonsterController，自动挂到根节点。");
                ctrl = go.AddComponent<MonsterController>();
            }

            // 注入配置 & 回调
            ctrl.config = monsterConfig;
            ctrl.spawner = this;

            alive.Add(go);
            spawnedTotal++;

            // 达到总上限后直接结束本批
            if (spawnedTotal >= spawn.maxSpawnCount) break;
        }
    }

    /// <summary>
    /// 取本次生成坐标
    /// Points：若仅 1 个点 -> 同一批所有都会用这个点（重叠）
    ///          多个点时按 sequentialSpawn 顺序/随机挑点
    /// Area：   在范围内随机
    /// </summary>
    private Vector3 GetSpawnPosition(SpawnConfig spawn)
    {
        if (spawn.positionType == SpawnPositionType.Area)
        {
            Vector2 c = spawn.areaCenter;
            Vector2 s = spawn.areaSize;
            return new Vector3(
                Random.Range(c.x - s.x * 0.5f, c.x + s.x * 0.5f),
                Random.Range(c.y - s.y * 0.5f, c.y + s.y * 0.5f),
                0f
            );
        }
        else
        {
            // Points
            if (spawn.spawnPoints == null || spawn.spawnPoints.Count == 0)
                return transform.position;

            if (spawn.spawnPoints.Count == 1)
                return spawn.spawnPoints[0];   // 单点：同批全部重叠在此点

            // 多点：顺序或随机
            if (spawn.sequentialSpawn)
            {
                int idx = nextPointIndex % spawn.spawnPoints.Count;
                nextPointIndex++;
                return spawn.spawnPoints[idx];
            }
            else
            {
                int idx = Random.Range(0, spawn.spawnPoints.Count);
                return spawn.spawnPoints[idx];
            }
        }
    }

    /// <summary>
    /// 被 MonsterController 在 Die() 时调用
    /// （当前逻辑下 maxSpawnCount 是“总出生数上限”，不是“存活上限”，
    ///  因此这里不影响后续是否继续刷，只做清理。）
    /// </summary>
    public void NotifyMonsterDeath(GameObject monster)
    {
        if (monster) alive.Remove(monster);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (monsterConfig == null) return;

        var spawn = monsterConfig.spawnConfig;
        if (spawn.positionType == SpawnPositionType.Area)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(spawn.areaCenter, spawn.areaSize);
        }
        else
        {
            Gizmos.color = Color.green;
            if (spawn.spawnPoints != null)
                foreach (var p in spawn.spawnPoints)
                    Gizmos.DrawWireSphere(p, 0.1f);
        }
    }
#endif
}
