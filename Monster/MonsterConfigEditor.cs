#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[CustomEditor(typeof(MonsterConfig))]
public class MonsterConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var spMonsterID = serializedObject.FindProperty("monsterID");
        var spLevel = serializedObject.FindProperty("level");
        var spMaxHP = serializedObject.FindProperty("maxHP");
        var spExp = serializedObject.FindProperty("exp");
        var spPrefab = serializedObject.FindProperty("monsterPrefab");

        var spSpawn = serializedObject.FindProperty("spawnConfig");
        var spPatrol = serializedObject.FindProperty("patrolConfig");
        var spDiscoveryV2 = serializedObject.FindProperty("discoveryV2Config");

        // ========== 基础设置 ==========
        Header("基础设置");
        EditorGUILayout.PropertyField(spMonsterID);
        EditorGUILayout.PropertyField(spLevel);
        EditorGUILayout.PropertyField(spMaxHP);
        EditorGUILayout.PropertyField(spExp);
        EditorGUILayout.PropertyField(spPrefab);

        // ========== 出生阶段 ==========
        DrawSpawnConfig(spSpawn);

        // ========== 巡逻阶段 ==========
        DrawPatrolConfig(spPatrol);

        // ========== 发现阶段（V2） ==========
        DrawDiscoveryV2Config(spDiscoveryV2);

        // ========== 导入 / 导出 ==========
        DrawIOButtons();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSpawnConfig(SerializedProperty spSpawn)
    {
        if (spSpawn == null) return;

        if (!Fold("spawn", "出生阶段配置", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            // 基础字段
            var spPosType = spSpawn.FindPropertyRelative("positionType");
            var spSpawnPoints = spSpawn.FindPropertyRelative("spawnPoints");
            var spSequential = spSpawn.FindPropertyRelative("sequentialSpawn");
            var spAreaCenter = spSpawn.FindPropertyRelative("areaCenter");
            var spAreaSize = spSpawn.FindPropertyRelative("areaSize");

            var spOrientation = spSpawn.FindPropertyRelative("spawnOrientation");
            var spMaxCount = spSpawn.FindPropertyRelative("maxSpawnCount");
            var spBatch = spSpawn.FindPropertyRelative("spawnBatchCount");
            var spInterval = spSpawn.FindPropertyRelative("spawnInterval");

            var spSpawnAnim = spSpawn.FindPropertyRelative("spawnAnimation");
            var spIdleAnim = spSpawn.FindPropertyRelative("idleAnimation");
            var spIdleTime = spSpawn.FindPropertyRelative("idleTime");

            var spFxSpawn = spSpawn.FindPropertyRelative("spawnEffectPrefab");
            var spFxIdle = spSpawn.FindPropertyRelative("idleEffectPrefab");

            EditorGUILayout.PropertyField(spPosType);

            var posType = (SpawnPositionType)spPosType.enumValueIndex;
            if (posType == SpawnPositionType.Points)
            {
                EditorGUILayout.PropertyField(spSpawnPoints, true);
                EditorGUILayout.PropertyField(spSequential);
            }
            else
            {
                EditorGUILayout.PropertyField(spAreaCenter);
                EditorGUILayout.PropertyField(spAreaSize);
            }

            SpaceMinor();

            EditorGUILayout.PropertyField(spOrientation);
            EditorGUILayout.PropertyField(spMaxCount);
            EditorGUILayout.PropertyField(spBatch);
            EditorGUILayout.PropertyField(spInterval);

            SpaceMinor();

            EditorGUILayout.PropertyField(spSpawnAnim);
            EditorGUILayout.PropertyField(spIdleAnim);
            EditorGUILayout.PropertyField(spIdleTime);

            SpaceMinor();

            EditorGUILayout.PropertyField(spFxSpawn);
            EditorGUILayout.PropertyField(spFxIdle);
        }
    }

    private void DrawPatrolConfig(SerializedProperty spPatrol)
    {
        if (spPatrol == null) return;
        if (!Fold("patrol", "巡逻阶段配置", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            var spPatrolPoints = spPatrol.FindPropertyRelative("patrolPoints");
            var spMovements = spPatrol.FindPropertyRelative("movements");
            var spRandomOrder = spPatrol.FindPropertyRelative("randomOrder");

            EditorGUILayout.PropertyField(spPatrolPoints, true);
            EditorGUILayout.PropertyField(spRandomOrder);

            SpaceMinor();
            if (Fold("patrol.movements", $"巡逻动作列表 (Count={spMovements.arraySize})", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < spMovements.arraySize; i++)
                    {
                        var elem = spMovements.GetArrayElementAtIndex(i);
                        if (Fold($"patrol.movements.{i}", $"元素 {i}", true))
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                DrawPatrolMovement(elem);
                            }
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ 添加"))
                    {
                        spMovements.InsertArrayElementAtIndex(spMovements.arraySize);
                    }
                    if (spMovements.arraySize > 0 && GUILayout.Button("- 移除最后"))
                    {
                        spMovements.DeleteArrayElementAtIndex(spMovements.arraySize - 1);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }

    private void DrawPatrolMovement(SerializedProperty spMove)
    {
        var spType = spMove.FindPropertyRelative("type");
        EditorGUILayout.PropertyField(spType);
        var type = (MovementType)spType.enumValueIndex;

        // 直线参数
        var spMoveSpeed = spMove.FindPropertyRelative("moveSpeed");
        var spAcceleration = spMove.FindPropertyRelative("acceleration");
        var spDeceleration = spMove.FindPropertyRelative("deceleration");
        var spAccelerationTime = spMove.FindPropertyRelative("accelerationTime");
        var spDecelerationTime = spMove.FindPropertyRelative("decelerationTime");
        var spMoveDuration = spMove.FindPropertyRelative("moveDuration");
        
        // 直线休息区间
        var spRestMin = spMove.FindPropertyRelative("restMin");
        var spRestMax = spMove.FindPropertyRelative("restMax");

        // 跳跃参数
        var spJumpSpeed = spMove.FindPropertyRelative("jumpSpeed");
        var spJumpHeight = spMove.FindPropertyRelative("jumpHeight");
        var spGravityScale = spMove.FindPropertyRelative("gravityScale");
        var spJumpDuration = spMove.FindPropertyRelative("jumpDuration");
        
        // 跳休区间
        var spJumpRestMin = spMove.FindPropertyRelative("jumprestMin");
        var spJumpRestMax = spMove.FindPropertyRelative("jumprestMax");

        // 资源（直线）
        var spMoveAnim = spMove.FindPropertyRelative("moveAnimation");
        var spRestAnim = spMove.FindPropertyRelative("restAnimation");
        var spMoveFx = spMove.FindPropertyRelative("moveEffectPrefab");
        var spRestFx = spMove.FindPropertyRelative("restEffectPrefab");

        // 资源（跳跃）
        var spJumpAnim = spMove.FindPropertyRelative("jumpAnimation");
        var spJumpRestAnim = spMove.FindPropertyRelative("jumpRestAnimation");
        var spJumpFx = spMove.FindPropertyRelative("jumpEffectPrefab");
        var spJumpRestFx = spMove.FindPropertyRelative("jumpRestEffectPrefab");

        

        if (type == MovementType.Straight)
        {
            Section("直线参数");
            EditorGUILayout.PropertyField(spMoveSpeed);
            EditorGUILayout.PropertyField(spAcceleration);
            EditorGUILayout.PropertyField(spDeceleration);
            EditorGUILayout.PropertyField(spAccelerationTime);
            EditorGUILayout.PropertyField(spDecelerationTime);
            EditorGUILayout.PropertyField(spMoveDuration);

            // 区间
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("巡逻休息时长区间（秒）", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spRestMin, new GUIContent("restMin"));
            EditorGUILayout.PropertyField(spRestMax, new GUIContent("restMax"));
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            Section("跳跃参数");
            EditorGUILayout.PropertyField(spJumpSpeed);
            EditorGUILayout.PropertyField(spJumpHeight);
            EditorGUILayout.PropertyField(spGravityScale);
            EditorGUILayout.PropertyField(spJumpDuration);

            // 区间
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("巡逻跳休时长区间（秒）", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spJumpRestMin, new GUIContent("jumprestMin"));
            EditorGUILayout.PropertyField(spJumpRestMax, new GUIContent("jumprestMax"));
            EditorGUILayout.EndHorizontal();
        }

        SpaceMinor();

        if (Fold(spMove.propertyPath + ".res.move", "资源（直线 Move/Rest）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(spMoveAnim, new GUIContent("Move Animation"));
                EditorGUILayout.PropertyField(spRestAnim, new GUIContent("Rest Animation"));
                EditorGUILayout.PropertyField(spMoveFx, new GUIContent("Move Effect Prefab"));
                EditorGUILayout.PropertyField(spRestFx, new GUIContent("Rest Effect Prefab"));
            }
        }

        if (Fold(spMove.propertyPath + ".res.jump", "资源（跳跃 Jump/JumpRest）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(spJumpAnim, new GUIContent("Jump Animation"));
                EditorGUILayout.PropertyField(spJumpRestAnim, new GUIContent("Jump Rest Animation"));
                EditorGUILayout.PropertyField(spJumpFx, new GUIContent("Jump Effect Prefab"));
                EditorGUILayout.PropertyField(spJumpRestFx, new GUIContent("Jump Rest Effect Prefab"));
            }
        }

        SpaceMinor();

        if (Fold(spMove.propertyPath + ".autojump", "Auto Jump 参数", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(spMove.FindPropertyRelative("autojumpSpeed"));
                EditorGUILayout.PropertyField(spMove.FindPropertyRelative("autojumpHeight"));
                EditorGUILayout.PropertyField(spMove.FindPropertyRelative("autogravityScale"));
                EditorGUILayout.PropertyField(spMove.FindPropertyRelative("automoveDuration"));
                EditorGUILayout.PropertyField(spMove.FindPropertyRelative("autorestDuration"));
            }
        }
    }

    private void DrawDiscoveryV2Config(SerializedProperty spV2)
    {
        if (spV2 == null) return;
        if (!Fold("discover", "发现阶段配置（V2）", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            var spFindRange = spV2.FindPropertyRelative("findRange");
            var spReverseR = spV2.FindPropertyRelative("reverseRange");
            var spBackR = spV2.FindPropertyRelative("backRange");

            var spBackAuto = spV2.FindPropertyRelative("enableBackAutoJumpOnObstacle");
            var spBackSuppress = spV2.FindPropertyRelative("suppressBackBandDuringRest");

            var spRandom = spV2.FindPropertyRelative("findRandomOrder");
            var spEvents = spV2.FindPropertyRelative("events");

            EditorGUILayout.PropertyField(spFindRange);
            EditorGUILayout.PropertyField(spReverseR);
            EditorGUILayout.PropertyField(spBackR);

            EditorGUILayout.PropertyField(spBackAuto, new GUIContent("Back: Auto-Jump On Obstacle"));
            EditorGUILayout.PropertyField(spBackSuppress, new GUIContent("Back: Suppress Bands During Rest"));

            EditorGUILayout.PropertyField(spRandom);

            SpaceMinor();
            if (Fold("discover.events", $"事件列表 (Count={spEvents.arraySize})", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < spEvents.arraySize; i++)
                    {
                        var ev = spEvents.GetArrayElementAtIndex(i);
                        DrawDiscoveryEvent(ev, i);
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ 添加事件"))
                    {
                        spEvents.InsertArrayElementAtIndex(spEvents.arraySize);
                    }
                    if (spEvents.arraySize > 0 && GUILayout.Button("- 移除最后"))
                    {
                        spEvents.DeleteArrayElementAtIndex(spEvents.arraySize - 1);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }

    private void DrawDiscoveryEvent(SerializedProperty spEvent, int index)
    {
        var label = $"事件 {index}";
        if (!Fold($"discover.events.{index}", label, true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            var spMode = spEvent.FindPropertyRelative("mode");
            var spTurnMode = spEvent.FindPropertyRelative("obstacleTurnMode");
            var spMoveSet = spEvent.FindPropertyRelative("moveSet");
            var spJumpSet = spEvent.FindPropertyRelative("jumpSet");

            EditorGUILayout.PropertyField(spMode);
            var mode = (DiscoveryV2Mode)spMode.enumValueIndex;

            if (mode == DiscoveryV2Mode.Move)
                EditorGUILayout.PropertyField(spTurnMode);

            SpaceMinor();

            if (mode == DiscoveryV2Mode.Move)
            {
                DrawMoveSet(spMoveSet);
            }
            else
            {
                DrawJumpSet(spJumpSet);
            }
        }
    }

    private void DrawMoveSet(SerializedProperty spMoveSet)
    {
        if (spMoveSet == null) return;

        var spFind = spMoveSet.FindPropertyRelative("find");
        var spBack = spMoveSet.FindPropertyRelative("back");

        var spFindAnim = spMoveSet.FindPropertyRelative("findmoveAnimation");
        var spRestAnim = spMoveSet.FindPropertyRelative("findrestAnimation");
        var spFindFx = spMoveSet.FindPropertyRelative("findmoveEffectPrefab");
        var spRestFx = spMoveSet.FindPropertyRelative("findrestEffectPrefab");

        var spBackAnim = spMoveSet.FindPropertyRelative("backmoveAnimation");

        Section("Follow（发现移动 参数）");
        using (new EditorGUI.IndentLevelScope())
        {
            DrawMoveParamsFind(spFind);
        }

        Section("Backstep（倒退/后退 公用 参数）");
        using (new EditorGUI.IndentLevelScope())
        {
            DrawMoveParamsBack(spBack);
        }

        Section("动画/特效（find 与 retreat 共用；back 仅 move 动画不同，特效复用 find）");
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(spFindAnim, new GUIContent("Findmove Animation"));
            EditorGUILayout.PropertyField(spRestAnim, new GUIContent("Findrest Animation"));
            EditorGUILayout.PropertyField(spFindFx, new GUIContent("Findmove Effect Prefab"));
            EditorGUILayout.PropertyField(spRestFx, new GUIContent("Findrest Effect Prefab"));
            EditorGUILayout.PropertyField(spBackAnim, new GUIContent("Backmove Animation"));
        }
    }

    private void DrawJumpSet(SerializedProperty spJumpSet)
    {
        if (spJumpSet == null) return;

        var spFind = spJumpSet.FindPropertyRelative("find");
        var spBack = spJumpSet.FindPropertyRelative("back");

        var spJumpAnim = spJumpSet.FindPropertyRelative("findjumpAnimation");
        var spJumpRestAnim = spJumpSet.FindPropertyRelative("findjumpRestAnimation");
        var spJumpFx = spJumpSet.FindPropertyRelative("findjumpEffectPrefab");
        var spJumpRestFx = spJumpSet.FindPropertyRelative("findjumpRestEffectPrefab");

        var spBackJumpAnim = spJumpSet.FindPropertyRelative("backjumpAnimation");

        Section("Follow（发现跳跃 参数）");
        using (new EditorGUI.IndentLevelScope())
        {
            DrawJumpParamsFind(spFind);
        }

        Section("Backstep（倒退/后退跳 参数）");
        using (new EditorGUI.IndentLevelScope())
        {
            DrawJumpParamsBack(spBack);
        }

        Section("动画/特效（find 与 retreat 共用；back 仅 jump 动画不同，特效复用 find）");
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(spJumpAnim, new GUIContent("Findjump Animation"));
            EditorGUILayout.PropertyField(spJumpRestAnim, new GUIContent("Findjump Rest Animation"));
            EditorGUILayout.PropertyField(spJumpFx, new GUIContent("Findjump Effect Prefab"));
            EditorGUILayout.PropertyField(spJumpRestFx, new GUIContent("Findjump Rest Effect Prefab"));
            EditorGUILayout.PropertyField(spBackJumpAnim, new GUIContent("Backjump Animation"));
        }
    }

    private void DrawMoveParamsFind(SerializedProperty spFind)
    {
        if (spFind == null) return;
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findmoveSpeed"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findacceleration"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findaccelerationTime"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("finddeceleration"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("finddecelerationTime"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findmoveDuration"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findrestDuration"));
    }

    private void DrawMoveParamsBack(SerializedProperty spBack)
    {
        if (spBack == null) return;
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backmoveSpeed"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backacceleration"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backaccelerationTime"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backdeceleration"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backdecelerationTime"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backmoveDuration"));

        var spMin = spBack.FindPropertyRelative("backrestMin");
        var spMax = spBack.FindPropertyRelative("backrestMax");
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Back 休息时长区间（秒）", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(spMin, new GUIContent("backrestMin"));
        EditorGUILayout.PropertyField(spMax, new GUIContent("backrestMax"));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawJumpParamsFind(SerializedProperty spFind)
    {
        if (spFind == null) return;
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findjumpSpeed"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findjumpHeight"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findgravityScale"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findjumpDuration"));
        EditorGUILayout.PropertyField(spFind.FindPropertyRelative("findjumpRestDuration"));
    }

    private void DrawJumpParamsBack(SerializedProperty spBack)
    {
        if (spBack == null) return;
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backjumpSpeed"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backjumpHeight"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backgravityScale"));
        EditorGUILayout.PropertyField(spBack.FindPropertyRelative("backjumpDuration"));

        var spMin = spBack.FindPropertyRelative("backjumpRestMin");
        var spMax = spBack.FindPropertyRelative("backjumpRestMax");
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Back 跳休时长区间（秒）", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(spMin, new GUIContent("backjumpRestMin"));
        EditorGUILayout.PropertyField(spMax, new GUIContent("backjumpRestMax"));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawIOButtons()
    {
        EditorGUILayout.Space();
        Header("配置导入/导出");

        var config = (MonsterConfig)target;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导出配置JSON"))
        {
            string path = EditorUtility.SaveFilePanel("导出怪物配置JSON", "", config.monsterID + ".json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(path, json);
                Debug.Log("怪物配置已导出到: " + path);
            }
        }
        if (GUILayout.Button("从JSON导入配置"))
        {
            string path = EditorUtility.OpenFilePanel("导入怪物配置JSON", "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                string json = File.ReadAllText(path);
                JsonUtility.FromJsonOverwrite(json, config);
                EditorUtility.SetDirty(config);
                Debug.Log("已从JSON导入怪物配置: " + path);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ========== UI Helpers ==========
    private static void Header(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static void Section(string title)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
    }

    private static void SpaceMinor() => EditorGUILayout.Space(4);

    private bool Fold(string key, string title, bool defaultState)
    {
        int id = target != null ? target.GetInstanceID() : 0;
        string fullKey = $"MonsterConfigEditor.fold.{id}.{key}";
        int val = EditorPrefs.GetInt(fullKey, defaultState ? 1 : 0);
        bool state = val != 0;
        bool newState = EditorGUILayout.Foldout(state, title, true);
        if (newState != state)
            EditorPrefs.SetInt(fullKey, newState ? 1 : 0);
        return newState;
    }
}
#endif