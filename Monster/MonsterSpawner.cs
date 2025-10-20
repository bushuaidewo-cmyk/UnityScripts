using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// 怪物生成器：负责周期生成怪物，并自动检测Prefab完整性
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("怪物配置 ScriptableObject")]
    public MonsterConfig monsterConfig;

    // 当前场景已生成的怪物实例列表
    private List<GameObject> spawnedMonsters = new List<GameObject>();

    void Start()
    {
        if (monsterConfig == null)
        {
            Debug.LogError("[MonsterSpawner] 未分配 MonsterConfig！");
            enabled = false;
            return;
        }

        // 初次生成
        SpawnMonsters(monsterConfig.spawnConfig.spawnBatchCount);

        // 循环生成
        if (monsterConfig.spawnConfig.spawnInterval > 0)
            StartCoroutine(SpawnLoop());
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (monsterConfig.spawnConfig.maxSpawnCount > 0 &&
                spawnedMonsters.Count >= monsterConfig.spawnConfig.maxSpawnCount)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            SpawnMonsters(monsterConfig.spawnConfig.spawnBatchCount);
            yield return new WaitForSeconds(monsterConfig.spawnConfig.spawnInterval);
        }
    }

    void SpawnMonsters(int count)
    {
        for (int i = 0; i < count; ++i)
        {
            Vector2 spawnPos = GetSpawnPosition();

            // 朝向
            bool faceRight = true;
            if (monsterConfig.spawnConfig.spawnOrientation == Orientation.FacePlayer)
            {
                GameObject player = GameObject.FindWithTag("Player");
                if (player && player.transform.position.x < spawnPos.x)
                    faceRight = false;
            }
            else if (monsterConfig.spawnConfig.spawnOrientation == Orientation.FaceLeft)
            {
                faceRight = false;
            }

            // ✅ 检查Prefab合法性
            if (monsterConfig.monsterPrefab == null)
            {
                Debug.LogError("[MonsterSpawner] MonsterConfig 未分配 Monster Prefab！");
                return;
            }

            // 实例化怪物
            GameObject newMonster = Instantiate(monsterConfig.monsterPrefab, spawnPos, Quaternion.identity);

            // 朝向翻转
            // 朝向翻转（仅旋转，不改scale）
            if (faceRight)
                newMonster.transform.rotation = Quaternion.identity;
            else
                newMonster.transform.rotation = Quaternion.Euler(0, 180f, 0);

            // ✅ 检查 MonsterController 是否存在
            // ✅ 优先在子节点中查找 MonsterController（支持 Monster_test_Root 层级结构）
            MonsterController controller = newMonster.GetComponentInChildren<MonsterController>();

            if (controller == null)
            {
                Debug.LogWarning($"[MonsterSpawner] {newMonster.name} 及其子节点均未找到 MonsterController，自动添加到根节点。");
                controller = newMonster.AddComponent<MonsterController>();
            }


            // ✅ 自动赋值配置
            if (controller != null)
            {
                controller.config = monsterConfig;
                controller.spawner = this;
                Debug.Log($"[Spawner] 已生成怪物：{newMonster.name}，已赋值 Config：{(controller.config != null ? "✅成功" : "❌失败")}");
            }
            else
            {
                Debug.LogError($"[MonsterSpawner] {newMonster.name} 未能添加 MonsterController，请检查Prefab。");
            }

            spawnedMonsters.Add(newMonster);
        }
    }

    Vector2 GetSpawnPosition()
    {
        var spawn = monsterConfig.spawnConfig;
        if (spawn.positionType == SpawnPositionType.Area)
        {
            float x = Random.Range(spawn.areaCenter.x - spawn.areaSize.x / 2, spawn.areaCenter.x + spawn.areaSize.x / 2);
            float y = Random.Range(spawn.areaCenter.y - spawn.areaSize.y / 2, spawn.areaCenter.y + spawn.areaSize.y / 2);
            return new Vector2(x, y);
        }
        else
        {
            if (spawn.spawnPoints.Count == 0)
                return transform.position;

            if (spawn.sequentialSpawn)
            {
                Vector2 pos = spawn.spawnPoints[0];
                spawn.spawnPoints.RemoveAt(0);
                spawn.spawnPoints.Add(pos);
                return pos;
            }
            else
            {
                int idx = Random.Range(0, spawn.spawnPoints.Count);
                return spawn.spawnPoints[idx];
            }
        }
    }

    public void NotifyMonsterDeath(GameObject monster)
    {
        spawnedMonsters.Remove(monster);
    }

#if UNITY_EDITOR
    // 可视化出生区域调试
    void OnDrawGizmosSelected()
    {
        if (monsterConfig == null) return;
        Gizmos.color = Color.red;
        var area = monsterConfig.spawnConfig;
        if (area.positionType == SpawnPositionType.Area)
        {
            Gizmos.DrawWireCube(area.areaCenter, area.areaSize);
        }
    }
#endif
}
