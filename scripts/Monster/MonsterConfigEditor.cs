#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonsterConfig))]
public class MonsterConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 基础字段
        var spMonsterID = serializedObject.FindProperty("monsterID");
        var spLevel = serializedObject.FindProperty("level");
        var spMaxHP = serializedObject.FindProperty("maxHP");
        var spExp = serializedObject.FindProperty("exp");
        var spPrefab = serializedObject.FindProperty("monsterPrefab");

        // 各配置块
        var spSpawn = serializedObject.FindProperty("spawnConfig");
        var spPatrol = serializedObject.FindProperty("patrolConfig");
        var spDiscoveryV2 = serializedObject.FindProperty("discoveryV2Config");

        // 空中：阶段ID 与 功能占位
        var spAirIDs = serializedObject.FindProperty("airPhaseConfig");
        var spAirStage = serializedObject.FindProperty("airStageConfig");

        // 1) 基础属性（新增折叠三角）
        if (Fold("basic", "基础属性", true))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(spMonsterID);
                EditorGUILayout.PropertyField(spLevel);
                EditorGUILayout.PropertyField(spMaxHP);
                EditorGUILayout.PropertyField(spExp);
                EditorGUILayout.PropertyField(spPrefab);
            }
        }

        // 2) 出生阶段配置
        DrawSpawnConfig(spSpawn);

        // 3) 地面配置与空中配置ID
        DrawAirPhaseConfig(spAirIDs);

        // 4) 地面配置（巡逻 + 发现）
        bool groundFoldOpen = Fold("ground", "地面配置", true);
        if (groundFoldOpen)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DrawPatrolConfig(spPatrol);
                DrawDiscoveryV2Config(spDiscoveryV2);
            }
        }

        // 5) 空中配置（巡逻/发现）
        DrawAirStageConfig(spAirStage);

        // 6) 导入/导出
        DrawIOButtons();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawAirStageConfig(SerializedProperty spAirStage)
    {
        if (spAirStage == null) return;
        if (!Fold("air.stage", "空中配置", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            // ================== 空中巡逻 ==================
            var spAirPatrol = spAirStage.FindPropertyRelative("patrol");
            if (Fold("air.stage.patrol", "巡逻阶段配置(空中)", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var spRandom = spAirPatrol.FindPropertyRelative("randomOrder");
                    var spPass = spAirPatrol.FindPropertyRelative("canPassThroughScene");

                    var spSkyMoveAnim = spAirPatrol.FindPropertyRelative("skymoveAnimation");
                    var spSkyRestAnim = spAirPatrol.FindPropertyRelative("skyrestAnimation");
                    var spSkyMoveFx = spAirPatrol.FindPropertyRelative("skymoveEffectPrefab");
                    var spSkyRestFx = spAirPatrol.FindPropertyRelative("skyrestEffectPrefab");

                    var spPathType = spAirPatrol.FindPropertyRelative("pathType");
                    var spElems = spAirPatrol.FindPropertyRelative("elements");

                    // 路径类型提示
                    if (spPathType != null)
                    {
                        var pt = (AirPatrolPathType)spPathType.enumValueIndex;
                        string hint = pt switch
                        {
                            AirPatrolPathType.AreaRandom => "随机方向：每段开始重新挑随机2D方向",
                            AirPatrolPathType.AreaRandomH => "随机固定方向：初次随机，之后沿碰撞/出界反射保持延续",
                            AirPatrolPathType.AreaHorizontal => "水平往返：在区域左右边缘之间移动",
                            AirPatrolPathType.AreaVertical => "垂直往返：在区域上下边缘之间移动",
                            _ => ""
                        };
                        if (!string.IsNullOrEmpty(hint))
                            EditorGUILayout.HelpBox(hint, MessageType.None);
                    }
                    EditorGUILayout.PropertyField(spPathType, new GUIContent("区域移动类型"));

                    // 区域尺寸同步（使用第一个元素为全局编辑入口）
                    if (spElems != null)
                    {
                        int n = spElems.arraySize;
                        if (n <= 0)
                        {
                            EditorGUILayout.HelpBox("添加至少一个元素后可编辑区域参数。", MessageType.Info);
                        }
                        else
                        {
                            var first = spElems.GetArrayElementAtIndex(0);
                            var spAreaCenterGlobal = first.FindPropertyRelative("areaCenter");
                            var spAreaSizeGlobal = first.FindPropertyRelative("areaSize");
                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.PropertyField(spAreaCenterGlobal, new GUIContent("areaCenter"));
                            EditorGUILayout.PropertyField(spAreaSizeGlobal, new GUIContent("areaSize"));
                            if (EditorGUI.EndChangeCheck())
                            {
                                for (int i = 0; i < n; i++)
                                {
                                    var e = spElems.GetArrayElementAtIndex(i);
                                    e.FindPropertyRelative("areaCenter").vector2Value = spAreaCenterGlobal.vector2Value;
                                    e.FindPropertyRelative("areaSize").vector2Value = spAreaSizeGlobal.vector2Value;
                                }
                            }
                        }
                    }

                    EditorGUILayout.PropertyField(spPass, new GUIContent("受区域限制"));

                    EditorGUILayout.Space(2);
                    // 将随机播放巡逻动作移动到受区域限制下面
                    EditorGUILayout.PropertyField(spRandom, new GUIContent("随机播放巡逻动作"));

                    // 巡逻资源折叠
                    if (Fold("air.stage.patrol.res", "巡逻资源 (动画/特效)", true))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(spSkyMoveAnim, new GUIContent("Skymove Animation"));
                            EditorGUILayout.PropertyField(spSkyRestAnim, new GUIContent("Skyrest Animation"));
                            EditorGUILayout.PropertyField(spSkyMoveFx, new GUIContent("Skymove Effect Prefab"));
                            EditorGUILayout.PropertyField(spSkyRestFx, new GUIContent("Skyrest Effect Prefab"));
                        }
                    }

                    EditorGUILayout.Space(4);

                    if (Fold("air.stage.patrol.elements", $"空中巡逻元素列表 (Count={spElems.arraySize})", true))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            for (int i = 0; i < spElems.arraySize; i++)
                            {
                                var elem = spElems.GetArrayElementAtIndex(i);
                                if (Fold($"air.stage.patrol.elements.{i}", $"element {i}", true))
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        var spMove = elem.FindPropertyRelative("move");
                                        DrawAirPatrolMoveCore(spMove);

                                        // S 型叠加
                                        var spSinOn = elem.FindPropertyRelative("sinEnabled");
                                        var spFreq = elem.FindPropertyRelative("sinFrequency");
                                        var spAmp = elem.FindPropertyRelative("sinAmplitude");
                                        EditorGUILayout.Space(2);
                                        EditorGUILayout.PropertyField(spSinOn, new GUIContent("启用S波摆动(S型)"));
                                        if (spSinOn.boolValue)
                                        {
                                            EditorGUILayout.PropertyField(spFreq, new GUIContent("S波摆动频率(Hz)"));
                                            EditorGUILayout.PropertyField(spAmp, new GUIContent("S波摆动幅度(米)"));
                                        }
                                    }
                                }
                            }

                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button("+ 添加"))
                                spElems.InsertArrayElementAtIndex(spElems.arraySize);
                            if (spElems.arraySize > 0 && GUILayout.Button("- 移除最后"))
                                spElems.DeleteArrayElementAtIndex(spElems.arraySize - 1);
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
            }

            // ================== 空中发现 ==================
            var spAirDiscovery = spAirStage.FindPropertyRelative("discovery");
            if (Fold("air.stage.discovery", "发现阶段配置(空中)", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (spAirDiscovery != null)
                    {
                        // 距离
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("findRange"), new GUIContent("Find Range (绿)"));
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("reverseRange"), new GUIContent("Reverse Range (白)"));
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backRange"), new GUIContent("Back Range (黑)"));

                        // 周期性屏蔽
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField("后退检测周期性屏蔽", EditorStyles.miniBoldLabel);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backDCTMin"), new GUIContent("Min Time"));
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backDCTMax"), new GUIContent("Max Time"));
                        EditorGUILayout.EndHorizontal();

                        // 全局 S 波
                        EditorGUILayout.Space(4);
                        var spSinOn = spAirDiscovery.FindPropertyRelative("sinEnabled");
                        var spSinFreq = spAirDiscovery.FindPropertyRelative("sinFrequency");
                        var spSinAmp = spAirDiscovery.FindPropertyRelative("sinAmplitude");
                        EditorGUILayout.PropertyField(spSinOn, new GUIContent("启用 S 波 (全局)"));

                        if (spSinOn.boolValue)
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                EditorGUILayout.PropertyField(spSinFreq, new GUIContent("S 波频率(Hz)"));
                                EditorGUILayout.PropertyField(spSinAmp, new GUIContent("S 波幅度(米)"));
                            }
                        }

                        // Follow 资源折叠
                        EditorGUILayout.Space(4);
                        if (Fold("air.stage.discovery.res.follow", "Follow 资源 (动画/特效)", true))
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("followMoveAnimation"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("followRestAnimation"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("followMoveEffectPrefab"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("followRestEffectPrefab"));
                            }
                        }

                        // Back 资源折叠
                        EditorGUILayout.Space(2);
                        if (Fold("air.stage.discovery.res.back", "Back 资源 (动画/特效)", true))
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backMoveAnimation"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backRestAnimation"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backMoveEffectPrefab"));
                                EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("backRestEffectPrefab"));
                            }
                        }

                        // 列表控制
                        EditorGUILayout.Space(4);
                        EditorGUILayout.PropertyField(spAirDiscovery.FindPropertyRelative("findRandomOrder"), new GUIContent("Random Order"));

                        // 动作列表
                        var spElems = spAirDiscovery.FindPropertyRelative("elements");
                        if (Fold("air.stage.discovery.elements", $"空中发现动作列表 (Count={spElems.arraySize})", true))
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                for (int i = 0; i < spElems.arraySize; i++)
                                {
                                    var elem = spElems.GetArrayElementAtIndex(i);
                                    if (Fold($"air.stage.discovery.elements.{i}", $"动作 {i}", true))
                                    {
                                        using (new EditorGUI.IndentLevelScope())
                                        {
                                            var spFollow = elem.FindPropertyRelative("follow");
                                            var spBackstep = elem.FindPropertyRelative("backstep");

                                            if (Fold($"air.disc.{i}.follow", "Follow (跟踪参数)", true))
                                            {
                                                using (new EditorGUI.IndentLevelScope()) DrawAirFollowParams(spFollow);
                                            }
                                            if (Fold($"air.disc.{i}.back", "Backstep (后退参数)", true))
                                            {
                                                using (new EditorGUI.IndentLevelScope()) DrawAirBackstepParams(spBackstep);
                                            }
                                        }
                                    }
                                }

                                EditorGUILayout.BeginHorizontal();
                                if (GUILayout.Button("+ 添加动作")) spElems.InsertArrayElementAtIndex(spElems.arraySize);
                                if (spElems.arraySize > 0 && GUILayout.Button("- 移除最后")) spElems.DeleteArrayElementAtIndex(spElems.arraySize - 1);
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                    }
                }
            }
        }
    }

    private void DrawAirFollowParams(SerializedProperty sp)
    {
        if (sp == null) return;

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("moveSpeed"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("acceleration"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("accelerationTime"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("deceleration"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("decelerationTime"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("moveDuration"));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("restMin"), new GUIContent("Rest Min"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("restMax"), new GUIContent("Rest Max"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("homingFrequency"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("homingStrength"));
        EditorGUILayout.Space(2);
    }

    private void DrawAirBackstepParams(SerializedProperty sp)
    {
        if (sp == null) return;

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("moveSpeed"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("acceleration"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("accelerationTime"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("deceleration"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("decelerationTime"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("moveDuration"));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("restMin"), new GUIContent("Rest Min"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("restMax"), new GUIContent("Rest Max"));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAirPhaseConfig(SerializedProperty spAir)
    {
        if (spAir == null) return;
        if (!Fold("air", "地面/空中阶段勾选", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            var spGround = spAir.FindPropertyRelative("groundPhase");
            var spAirFlag = spAir.FindPropertyRelative("airPhase");
            EditorGUILayout.PropertyField(spGround, new GUIContent("groundPhase"));
            EditorGUILayout.PropertyField(spAirFlag, new GUIContent("airPhase"));

            var spShowGround = spAir.FindPropertyRelative("showGroundGizmosManual");
            var spShowAir = spAir.FindPropertyRelative("showAirGizmosManual");
            if (spShowGround != null && spShowAir != null)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(spShowGround, new GUIContent("显示地面发现/攻击 Gizmos"));
                EditorGUILayout.PropertyField(spShowAir, new GUIContent("显示空中发现/攻击 Gizmos"));
            }
        }
    }

    private void DrawSpawnConfig(SerializedProperty spSpawn)
    {
        if (spSpawn == null) return;
        if (!Fold("spawn", "出生阶段配置", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
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
            DrawPatrolGlobalResources(spMovements);

            SpaceMinor();
            if (Fold("patrol.movements", $"巡逻移动/跳跃列表 (Count={spMovements.arraySize})", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < spMovements.arraySize; i++)
                    {
                        var elem = spMovements.GetArrayElementAtIndex(i);
                        if (Fold($"patrol.movements.{i}", $"动作 {i}", true))
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                DrawPatrolMovement(elem);
                            }
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ 添加"))
                        spMovements.InsertArrayElementAtIndex(spMovements.arraySize);
                    if (spMovements.arraySize > 0 && GUILayout.Button("- 移除最后"))
                        spMovements.DeleteArrayElementAtIndex(spMovements.arraySize - 1);
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

        var spMoveSpeed = spMove.FindPropertyRelative("moveSpeed");
        var spAcceleration = spMove.FindPropertyRelative("acceleration");
        var spAccelerationTime = spMove.FindPropertyRelative("accelerationTime");
        var spDeceleration = spMove.FindPropertyRelative("deceleration");
        var spDecelerationTime = spMove.FindPropertyRelative("decelerationTime");
        var spMoveDuration = spMove.FindPropertyRelative("moveDuration");

        var spRestMin = spMove.FindPropertyRelative("restMin");
        var spRestMax = spMove.FindPropertyRelative("restMax");

        var spJumpSpeed = spMove.FindPropertyRelative("jumpSpeed");
        var spJumpHeight = spMove.FindPropertyRelative("jumpHeight");
        var spGravityScale = spMove.FindPropertyRelative("gravityScale");
        var spJumpDuration = spMove.FindPropertyRelative("jumpDuration");
        var spJumpRestMin = spMove.FindPropertyRelative("jumprestMin");
        var spJumpRestMax = spMove.FindPropertyRelative("jumprestMax");

        if (type == MovementType.Straight)
        {
            EditorGUILayout.PropertyField(spMoveSpeed);
            EditorGUILayout.PropertyField(spAcceleration);
            EditorGUILayout.PropertyField(spAccelerationTime);
            EditorGUILayout.PropertyField(spDeceleration);
            EditorGUILayout.PropertyField(spDecelerationTime);
            EditorGUILayout.PropertyField(spMoveDuration);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spRestMin, new GUIContent("restMin"));
            EditorGUILayout.PropertyField(spRestMax, new GUIContent("restMax"));
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.PropertyField(spJumpSpeed);
            EditorGUILayout.PropertyField(spJumpHeight);
            EditorGUILayout.PropertyField(spGravityScale);
            EditorGUILayout.PropertyField(spJumpDuration);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spJumpRestMin, new GUIContent("jumprestMin"));
            EditorGUILayout.PropertyField(spJumpRestMax, new GUIContent("jumprestMax"));
            EditorGUILayout.EndHorizontal();
        }

        SpaceMinor();
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

    private void DrawPatrolGlobalResources(SerializedProperty spMovements)
    {
        if (spMovements == null) return;

        int n = spMovements.arraySize;
        if (n <= 0)
        {
            EditorGUILayout.HelpBox("巡逻动作列表为空。添加至少一个元素后可编辑‘全局资源’。", MessageType.Info);
            return;
        }

        var first = spMovements.GetArrayElementAtIndex(0);

        if (Fold("patrol.res.move", "资源（直线 Move/Rest）（批量应用全部元素）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                string moveAnim = first.FindPropertyRelative("moveAnimation").stringValue;
                string restAnim = first.FindPropertyRelative("restAnimation").stringValue;
                var moveFx = first.FindPropertyRelative("moveEffectPrefab").objectReferenceValue;
                var restFx = first.FindPropertyRelative("restEffectPrefab").objectReferenceValue;

                string newMoveAnim = EditorGUILayout.TextField("Move Animation", moveAnim);
                string newRestAnim = EditorGUILayout.TextField("Rest Animation", restAnim);
                var newMoveFx = EditorGUILayout.ObjectField("Move Effect Prefab", moveFx, typeof(GameObject), false);
                var newRestFx = EditorGUILayout.ObjectField("Rest Effect Prefab", restFx, typeof(GameObject), false);

                if (newMoveAnim != moveAnim || newRestAnim != restAnim || newMoveFx != moveFx || newRestFx != restFx)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var e = spMovements.GetArrayElementAtIndex(i);
                        e.FindPropertyRelative("moveAnimation").stringValue = newMoveAnim;
                        e.FindPropertyRelative("restAnimation").stringValue = newRestAnim;
                        e.FindPropertyRelative("moveEffectPrefab").objectReferenceValue = newMoveFx;
                        e.FindPropertyRelative("restEffectPrefab").objectReferenceValue = newRestFx;
                    }
                }
            }
        }

        if (Fold("patrol.res.jump", "资源（跳跃 Jump/JumpRest）（批量应用全部元素）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                string jAnim = first.FindPropertyRelative("jumpAnimation").stringValue;
                string jRestAnim = first.FindPropertyRelative("jumpRestAnimation").stringValue;
                var jFx = first.FindPropertyRelative("jumpEffectPrefab").objectReferenceValue;
                var jRestFx = first.FindPropertyRelative("jumpRestEffectPrefab").objectReferenceValue;

                string newJAnim = EditorGUILayout.TextField("Jump Animation", jAnim);
                string newJRestAnim = EditorGUILayout.TextField("Jump Rest Animation", jRestAnim);
                var newJFx = EditorGUILayout.ObjectField("Jump Effect Prefab", jFx, typeof(GameObject), false);
                var newJRestFx = EditorGUILayout.ObjectField("Jump Rest Effect Prefab", jRestFx, typeof(GameObject), false);

                if (newJAnim != jAnim || newJRestAnim != jRestAnim || newJFx != jFx || newJRestFx != jRestFx)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var e = spMovements.GetArrayElementAtIndex(i);
                        e.FindPropertyRelative("jumpAnimation").stringValue = newJAnim;
                        e.FindPropertyRelative("jumpRestAnimation").stringValue = newJRestAnim;
                        e.FindPropertyRelative("jumpEffectPrefab").objectReferenceValue = newJFx;
                        e.FindPropertyRelative("jumpRestEffectPrefab").objectReferenceValue = newJRestFx;
                    }
                }
            }
        }
    }

    private void DrawDiscoveryV2Config(SerializedProperty spV2)
    {
        if (spV2 == null) return;
        if (!Fold("discover", "发现阶段配置", true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            var spFindRange = spV2.FindPropertyRelative("findRange");
            var spReverseR = spV2.FindPropertyRelative("reverseRange");
            var spBackR = spV2.FindPropertyRelative("backRange");
            var spBackAuto = spV2.FindPropertyRelative("enableBackAutoJumpOnObstacle");
            var spRandom = spV2.FindPropertyRelative("findRandomOrder");
            var spEvents = spV2.FindPropertyRelative("events");
            var spAttacks = spV2.FindPropertyRelative("attacks");
            var spAttacksRandom = spV2.FindPropertyRelative("attacksRandomOrder");

            EditorGUILayout.PropertyField(spFindRange);
            EditorGUILayout.PropertyField(spReverseR);
            EditorGUILayout.PropertyField(spBackR);

            Header("后退检测周期性屏蔽");
            var spCycleMin = spV2.FindPropertyRelative("backDCTMin");
            var spCycleMax = spV2.FindPropertyRelative("backDCTMax");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spCycleMin);
            EditorGUILayout.PropertyField(spCycleMax);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(spBackAuto, new GUIContent("Back: Auto-Jump On Obstacle"));

            EditorGUILayout.PropertyField(spRandom, new GUIContent("Events Random Order"));
            DrawDiscoveryGlobalResources(spV2);

            SpaceMinor();
            if (Fold("discover.events", $"发现移动/跳跃列表 (Count={spEvents.arraySize})", true))
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
                        spEvents.InsertArrayElementAtIndex(spEvents.arraySize);
                    if (spEvents.arraySize > 0 && GUILayout.Button("- 移除最后"))
                        spEvents.DeleteArrayElementAtIndex(spEvents.arraySize - 1);
                    EditorGUILayout.EndHorizontal();
                }
            }

            SpaceMinor();
            EditorGUILayout.PropertyField(spAttacksRandom, new GUIContent("Attacks Random Order"));

            if (Fold("discover.attacks", $"攻击列表 (Count={spAttacks.arraySize})", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < spAttacks.arraySize; i++)
                    {
                        var a = spAttacks.GetArrayElementAtIndex(i);
                        DrawAttackEventV2(a, i);
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ 添加攻击"))
                        spAttacks.InsertArrayElementAtIndex(spAttacks.arraySize);
                    if (spAttacks.arraySize > 0 && GUILayout.Button("- 移除最后"))
                        spAttacks.DeleteArrayElementAtIndex(spAttacks.arraySize - 1);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }

    private void DrawDiscoveryEvent(SerializedProperty spEvent, int index)
    {
        var label = $"动作 {index}";
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
                DrawMoveSet(spMoveSet);
            else
                DrawJumpSet(spJumpSet);
        }
    }

    private void DrawMoveSet(SerializedProperty spMoveSet)
    {
        if (spMoveSet == null) return;
        var spFind = spMoveSet.FindPropertyRelative("find");
        var spBack = spMoveSet.FindPropertyRelative("back");

        using (new EditorGUI.IndentLevelScope()) DrawMoveParamsFind(spFind);
        using (new EditorGUI.IndentLevelScope()) DrawMoveParamsBack(spBack);
    }

    private void DrawJumpSet(SerializedProperty spJumpSet)
    {
        if (spJumpSet == null) return;
        var spFind = spJumpSet.FindPropertyRelative("find");
        var spBack = spJumpSet.FindPropertyRelative("back");

        Section("Follow（发现跳跃 参数）");
        using (new EditorGUI.IndentLevelScope()) DrawJumpParamsFind(spFind);

        Section("Backstep（倒退/后退跳 参数）");
        using (new EditorGUI.IndentLevelScope()) DrawJumpParamsBack(spBack);
    }

    private void DrawDiscoveryGlobalResources(SerializedProperty spV2)
    {
        if (spV2 == null) return;
        var spEvents = spV2.FindPropertyRelative("events");
        int n = spEvents.arraySize;
        if (n <= 0)
        {
            EditorGUILayout.HelpBox("发现事件列表为空。添加至少一个事件后可编辑‘全局资源’。", MessageType.Info);
            return;
        }

        SerializedProperty pickMoveSet = null, pickJumpSet = null;
        for (int i = 0; i < n; i++)
        {
            var ev = spEvents.GetArrayElementAtIndex(i);
            pickMoveSet ??= ev.FindPropertyRelative("moveSet");
            pickJumpSet ??= ev.FindPropertyRelative("jumpSet");
            if (pickMoveSet != null && pickJumpSet != null) break;
        }

        if (pickMoveSet != null && Fold("discover.res.move", "发现阶段 资源（Move：find/retreat 共用；back 单独）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                var spFindAnim = pickMoveSet.FindPropertyRelative("findmoveAnimation");
                var spRestAnim = pickMoveSet.FindPropertyRelative("findrestAnimation");
                var spFindFx = pickMoveSet.FindPropertyRelative("findmoveEffectPrefab");
                var spRestFx = pickMoveSet.FindPropertyRelative("findrestEffectPrefab");
                var spBackAnim = pickMoveSet.FindPropertyRelative("backmoveAnimation");

                string findAnim = EditorGUILayout.TextField("Find/Retreat Move Animation", spFindAnim.stringValue);
                string restAnim = EditorGUILayout.TextField("Find/Retreat Rest Animation", spRestAnim.stringValue);
                var findFx = EditorGUILayout.ObjectField("Find/Retreat Move FX", spFindFx.objectReferenceValue, typeof(GameObject), false);
                var restFx = EditorGUILayout.ObjectField("Find/Retreat Rest FX", spRestFx.objectReferenceValue, typeof(GameObject), false);
                string backAnim = EditorGUILayout.TextField("Back Move Animation", spBackAnim.stringValue);

                for (int i = 0; i < n; i++)
                {
                    var ev = spEvents.GetArrayElementAtIndex(i);
                    var ms = ev.FindPropertyRelative("moveSet");
                    if (ms == null) continue;
                    ms.FindPropertyRelative("findmoveAnimation").stringValue = findAnim;
                    ms.FindPropertyRelative("findrestAnimation").stringValue = restAnim;
                    ms.FindPropertyRelative("findmoveEffectPrefab").objectReferenceValue = findFx;
                    ms.FindPropertyRelative("findrestEffectPrefab").objectReferenceValue = restFx;
                    ms.FindPropertyRelative("backmoveAnimation").stringValue = backAnim;
                }
            }
        }

        if (pickJumpSet != null && Fold("discover.res.jump", "发现阶段 资源（Jump：find/retreat 共用；back 单独）", false))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                var spFindJAnim = pickJumpSet.FindPropertyRelative("findjumpAnimation");
                var spFindJRest = pickJumpSet.FindPropertyRelative("findjumpRestAnimation");
                var spFindJFx = pickJumpSet.FindPropertyRelative("findjumpEffectPrefab");
                var spFindJRFx = pickJumpSet.FindPropertyRelative("findjumpRestEffectPrefab");
                var spBackJAnim = pickJumpSet.FindPropertyRelative("backjumpAnimation");

                string findJAnim = EditorGUILayout.TextField("Find/Retreat Jump Animation", spFindJAnim.stringValue);
                string findJRest = EditorGUILayout.TextField("Find/Retreat Jump Rest Animation", spFindJRest.stringValue);
                var findJFx = EditorGUILayout.ObjectField("Find/Retreat Jump FX", spFindJFx.objectReferenceValue, typeof(GameObject), false);
                var findJRFx = EditorGUILayout.ObjectField("Find/Retreat JumpRest FX", spFindJRFx.objectReferenceValue, typeof(GameObject), false);
                string backJAnim = EditorGUILayout.TextField("Back Jump Animation", spBackJAnim.stringValue);

                for (int i = 0; i < n; i++)
                {
                    var ev = spEvents.GetArrayElementAtIndex(i);
                    var js = ev.FindPropertyRelative("jumpSet");
                    if (js == null) continue;
                    js.FindPropertyRelative("findjumpAnimation").stringValue = findJAnim;
                    js.FindPropertyRelative("findjumpRestAnimation").stringValue = findJRest;
                    js.FindPropertyRelative("findjumpEffectPrefab").objectReferenceValue = findJFx;
                    js.FindPropertyRelative("findjumpRestEffectPrefab").objectReferenceValue = findJRFx;
                    js.FindPropertyRelative("backjumpAnimation").stringValue = backJAnim;
                }
            }
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

        var spMin = spFind.FindPropertyRelative("findrestMin");
        var spMax = spFind.FindPropertyRelative("findrestMax");
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(spMin, new GUIContent("findrestMin"));
        EditorGUILayout.PropertyField(spMax, new GUIContent("findrestMax"));
        EditorGUILayout.EndHorizontal();
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

        var spMin = spFind.FindPropertyRelative("findjumpRestMin");
        var spMax = spFind.FindPropertyRelative("findjumpRestMax");
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Find 跳休时长区间（秒）", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(spMin, new GUIContent("findjumpRestMin"));
        EditorGUILayout.PropertyField(spMax, new GUIContent("findjumpRestMax"));
        EditorGUILayout.EndHorizontal();
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

    private void DrawAttackEventV2(SerializedProperty spAttack, int index)
    {
        var label = $"攻击 {index}";
        if (!Fold($"discover.attacks.{index}", label, true)) return;

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackDuration"));
            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("repeatedHitsCount"), new GUIContent("repeatedHitsCount"));
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackRestMin"), new GUIContent("restMin"));
            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackRestMax"), new GUIContent("restMax"));
            EditorGUILayout.EndHorizontal();

            if (Fold($"discover.attacks.{index}.melee", "近战（Melee）", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("meleeRange"), new GUIContent("触发距离（米）"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackAnimation"), new GUIContent("动画"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackEffectPrefab"), new GUIContent("特效Prefab"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackSpawnChildPath"), new GUIContent("特效释放点子物体路径"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("meleeHitboxChildPath"), new GUIContent("命中体子物体路径"));
                }
            }

            if (Fold($"discover.attacks.{index}.ranged", "远程（Ranged）", true))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("rangedRange"), new GUIContent("触发距离（米）"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackFarAnimation"), new GUIContent("动画"));
                    EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackFarEffectPrefab"), new GUIContent("特效Prefab"));

                    if (Fold($"discover.attacks.{index}.projectile", "Projectile 配置", true))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            var spProj = spAttack.FindPropertyRelative("projectile");
                            if (spProj != null)
                            {
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("countPerBurst"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("intraBurstInterval"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("lifeTime"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("spreadAngle"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("spreadUniform"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("FlygunAnimation"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("FlygunEffectPrefab"));
                                EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackFarSpawnChildPath"), new GUIContent("发射点子物体路径"));

                                var spSpinEnable = spProj.FindPropertyRelative("selfRotate");
                                var spSpinX = spProj.FindPropertyRelative("selfRotateX");
                                var spSpinY = spProj.FindPropertyRelative("selfRotateY");
                                var spSpinZ = spProj.FindPropertyRelative("selfRotateZ");
                                var spSpinSpeed = spProj.FindPropertyRelative("selfRotateSpeedDeg");
                                EditorGUILayout.PropertyField(spSpinEnable, new GUIContent("飞行物自身旋转"));
                                if (spSpinEnable.boolValue)
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.PropertyField(spSpinX, new GUIContent("X"), GUILayout.Width(20));
                                    EditorGUILayout.PropertyField(spSpinY, new GUIContent("Y"), GUILayout.Width(20));
                                    EditorGUILayout.PropertyField(spSpinZ, new GUIContent("Z"), GUILayout.Width(20));
                                    EditorGUILayout.EndHorizontal();
                                    EditorGUILayout.PropertyField(spSpinSpeed, new GUIContent("旋转速度(度/秒)"));
                                }

                                using (new EditorGUI.DisabledScope(spSpinEnable.boolValue))
                                {
                                    EditorGUILayout.PropertyField(spProj.FindPropertyRelative("faceAlongPath"), new GUIContent("沿移动方向自动朝向"));
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("spawnAim"), new GUIContent("发射朝向"));

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("radius"), new GUIContent("爆炸半径"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("duration"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("interval"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("FlygunBoomAnimation"));
                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("FlygunBoomEffectPrefab"));

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("linearEnabled"));
                                if (spProj.FindPropertyRelative("linearEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("speed"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("accel"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("accelTime"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("decel"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("decelTime"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("moveDuration"));
                                    }
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("sinEnabled"));
                                if (spProj.FindPropertyRelative("sinEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("sinAmplitude"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("sinFrequency"));
                                    }
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("parabolaEnabled"));
                                if (spProj.FindPropertyRelative("parabolaEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("gravityScale"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("bounceCoefficient"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("bounceEnergyMode"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("bounceDecayFactor"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("parabolaApexHeight"), new GUIContent("最高点高度(米)"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("bounceEndVyThreshold"));
                                    }
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("homingEnabled"));
                                if (spProj.FindPropertyRelative("homingEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("homingFrequency"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("homingStrength"), new GUIContent("Homing Strength (0~1)"));
                                    }
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("orbitEnabled"));
                                if (spProj.FindPropertyRelative("orbitEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("orbitRadius"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("orbitAngular"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("orbitSweepSpeedDeg"));
                                    }
                                }

                                EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangEnabled"));
                                if (spProj.FindPropertyRelative("boomerangEnabled").boolValue)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangOutMaxDistance"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangApexStopTime"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackUniformSpeed"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackUniformTime"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackAccel"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackAccelTime"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackDecel"));
                                        EditorGUILayout.PropertyField(spProj.FindPropertyRelative("boomerangBackDecelTime"));
                                    }
                                }
                            }
                        }
                    }
                }

                var spMode = spAttack.FindPropertyRelative("attackMotionMode");
                EditorGUILayout.PropertyField(spMode, new GUIContent("Mode"));
                var mode = (AttackMotionMode)spMode.enumValueIndex;

                if (mode == AttackMotionMode.Move)
                {
                    if (Fold($"discover.attacks.{index}.attackMoveMelee", "移动中近战攻击（Melee）", false))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackmoveSpeedMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackaccelerationMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackaccelerationTimeMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackdecelerationMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackdecelerationTimeMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackmoveDurationMelee"));
                        }
                    }
                    if (Fold($"discover.attacks.{index}.attackMoveRanged", "移动中远程攻击（Ranged）", false))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackmoveSpeedRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackaccelerationRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackaccelerationTimeRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackdecelerationRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackdecelerationTimeRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackmoveDurationRanged"));
                        }
                    }
                }
                else if (mode == AttackMotionMode.Jump)
                {
                    if (Fold($"discover.attacks.{index}.attackJumpMelee", "跳跃中攻击（Melee）", false))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpSpeedMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpHeightMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackgravityScaleMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpDurationMelee"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpRestDurationMelee"));
                        }
                    }
                    if (Fold($"discover.attacks.{index}.attackJumpRanged", "跳跃中攻击（Ranged）", false))
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpSpeedRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpHeightRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackgravityScaleRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpDurationRanged"));
                            EditorGUILayout.PropertyField(spAttack.FindPropertyRelative("attackjumpRestDurationRanged"));
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("未选择叠加移动：本次攻击不附加位移/跳跃。", MessageType.Info);
                }
            }
        }
    }

    private void DrawAirPatrolMoveCore(SerializedProperty spMove)
    {
        if (spMove == null) return;

        var spMoveSpeed = spMove.FindPropertyRelative("moveSpeed");
        var spAcceleration = spMove.FindPropertyRelative("acceleration");
        var spAccelerationTime = spMove.FindPropertyRelative("accelerationTime");
        var spDeceleration = spMove.FindPropertyRelative("deceleration");
        var spDecelerationTime = spMove.FindPropertyRelative("decelerationTime");
        var spMoveDuration = spMove.FindPropertyRelative("moveDuration");
        var spRestMin = spMove.FindPropertyRelative("restMin");
        var spRestMax = spMove.FindPropertyRelative("restMax");

        EditorGUILayout.PropertyField(spMoveSpeed);
        EditorGUILayout.PropertyField(spAcceleration);
        EditorGUILayout.PropertyField(spAccelerationTime);
        EditorGUILayout.PropertyField(spDeceleration);
        EditorGUILayout.PropertyField(spDecelerationTime);
        EditorGUILayout.PropertyField(spMoveDuration);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(spRestMin, new GUIContent("restMin"));
        EditorGUILayout.PropertyField(spRestMax, new GUIContent("restMax"));
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