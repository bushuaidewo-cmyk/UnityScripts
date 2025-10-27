using System.Collections.Generic;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 怪物配置 ScriptableObject，包含怪物各阶段的参数配置。
/// 保留：出生/巡逻/发现V2；移除：空中阶段配置、地空状态切换、死亡阶段配置。
/// </summary>
[CreateAssetMenu(fileName = "NewMonsterConfig", menuName = "怪物配置/新怪物配置")]
public class MonsterConfig : ScriptableObject
{
    // 基本属性参数
    [Header("基础属性")]
    [Tooltip("怪物唯一ID，用于识别怪物类型")]
    public string monsterID;
    [Tooltip("怪物等级")]
    public int level;
    [Tooltip("怪物最大生命值")]
    public float maxHP;
    [Tooltip("击杀怪物后获得的经验值")]
    public int exp;

    // 预制体和阶段配置
    [Header("基础设置")]
    [Tooltip("怪物Prefab预制体（应包含碰撞体、检测触发器、Animator等组件）")]
    public GameObject monsterPrefab;

    [Header("出生阶段配置")]
    public SpawnConfig spawnConfig;

    [Header("巡逻阶段配置（地面）")]
    public PatrolConfig patrolConfig;

    // 新版发现阶段（事件列表 + 三档距离）
    [Header("发现阶段配置（V2）")]
    [Tooltip("发现阶段事件列表与三档距离设置；资源/参数独立，底层复用巡逻逻辑")]
    public DiscoveryV2Config discoveryV2Config;
}

#region Spawn / Patrol（保留）

[System.Serializable]
public class SpawnConfig
{
    public SpawnPositionType positionType;
    public List<Vector2> spawnPoints = new List<Vector2>();
    public bool sequentialSpawn = true;
    public Vector2 areaCenter;

    [Header("出生属性")]
    public Vector2 areaSize;
    public Orientation spawnOrientation;
    public int maxSpawnCount = 1;
    public int spawnBatchCount = 1;
    public float spawnInterval = 0;

    [Header("动画设置")]
    public string spawnAnimation;
    public string idleAnimation;
    [FormerlySerializedAs("idleDelay")]
    [InspectorName("Idle Time")]
    public float idleTime = 1f;

    [Header("特效设置")]
    public GameObject spawnEffectPrefab;
    public GameObject idleEffectPrefab;
}

[System.Serializable]
public class PatrolConfig
{
    public List<Vector2> patrolPoints = new List<Vector2>();
    public List<PatrolMovement> movements = new List<PatrolMovement>();
    public bool randomOrder = false;
}

[System.Serializable]
public class PatrolMovement
{
    public MovementType type;

    [Header("直线移动参数")]
    public float moveSpeed;
    public float acceleration;
    public float deceleration;
    public float accelerationTime = 0f;
    public float decelerationTime = 0f;
    public float moveDuration;
    public float restDuration;

    [Header("移动动画配置")]
    public string moveAnimation;
    public string restAnimation;
    public GameObject moveEffectPrefab;
    public GameObject restEffectPrefab;

    [Header("跳跃移动参数（type=Jump 时使用）")]
    public float jumpSpeed;
    public float jumpHeight;
    public float gravityScale;
    public float jumpDuration;
    public float jumpRestDuration;

    [Header("跳跃动画/特效（Jump 与 AutoJump 资源共用）")]
    public string jumpAnimation;
    public string jumpRestAnimation;
    public GameObject jumpEffectPrefab;
    public GameObject jumpRestEffectPrefab;

    [Header("Auto Jump 参数")]
    public float autojumpSpeed;
    public float autojumpHeight;
    public float autogravityScale = 1f;
    public float automoveDuration = 0f;
    public float autorestDuration = 0f;

    // ===== 运行时字段（不序列化）=====
    [System.NonSerialized] public int rtExecuteRemain = 0;
    [System.NonSerialized] public float rtMoveTimer = 0f;
    [System.NonSerialized] public float rtRestTimer = 0f;
    [System.NonSerialized] public float rtCurrentSpeed = 0f;
    [System.NonSerialized] public StraightPhase rtStraightPhase = StraightPhase.None;
    [System.NonSerialized] public bool rtUsingAutoJumpParams = false;
    [System.NonSerialized] public float rtAccelTimer = 0f;
    [System.NonSerialized] public float rtCruiseTimer = 0f;
    [System.NonSerialized] public float rtDecelTimer = 0f;
}

public enum StraightPhase { None, Accel, Cruise, Decel, Rest }

#endregion

#region 发现阶段 V2（保留）——事件列表 + 三档距离（Follow/Retreat/Backstep）

public enum DiscoveryV2Mode { Move, Jump }

[System.Serializable]
public class DiscoveryV2Config
{
    [Header("三档水平距离（Gizmos：红=发现，白=后退，黑=倒退）")]
    [Tooltip("发现距离（红）：d > findRange 则退出发现回巡逻；d <= findRange 进入发现带内")]
    public float findRange = 6f;
    [Tooltip("后退距离（白）：d <= reverseRange 进入后退带；必须 < findRange")]
    public float reverseRange = 3.5f;
    [Tooltip("倒退距离（黑）：d <= backRange 进入倒退带；必须 < reverseRange")]
    public float backRange = 1.5f;

    [Header("事件播放")]
    [Tooltip("勾选后每轮事件重洗随机顺序")]
    public bool findRandomOrder = false;

    [Tooltip("发现事件列表（Move 或 Jump），每条事件内部包含三档参数，动画/特效与巡逻一致的规则")]
    public List<DiscoveryEventV2> events = new List<DiscoveryEventV2>();
}

public enum ObstacleTurnMode
{
    AutoTurn = 0,          // 自动转向
    NoTurnCanFall = 1,     // 不转向，可走下悬崖
    NoTurnStopAtCliff = 2  // 不转向，遇悬崖停下
}

[System.Serializable]
public class DiscoveryEventV2
{
    [Tooltip("事件模式：移动发现 或 跳跃发现")]
    public DiscoveryV2Mode mode = DiscoveryV2Mode.Move;

    // 仅对 发现-移动 生效；发现-跳跃不生效
    [Tooltip("发现-移动：遇障碍/悬崖时的处理策略")]
    public ObstacleTurnMode obstacleTurnMode = ObstacleTurnMode.AutoTurn;

    // 兼容旧资产：老的 bool（勾上=AutoTurn，没勾=NoTurnStopAtCliff）。仅迁移用，不再参与逻辑。
    [SerializeField, HideInInspector]
    public bool allowObstacleAutoTurnLegacy = true;

    [Tooltip("移动发现参数（仅 mode=Move 时使用）")]
    public MoveSetV2 moveSet;

    [Tooltip("跳跃发现参数（仅 mode=Jump 时使用）")]
    public JumpSetV2 jumpSet;
}

#region Move 事件参数

[System.Serializable]
public class MoveSetV2
{
    [Header("Follow（发现移动距离）")]
    public FollowMoveParams find;

    [Header("Retreat（发现后退移动距离）")]
    public RetreatMoveParams reverse;

    [Header("Backstep（发现倒退移动距离）")]
    public BackstepMoveParams back;

    [Header("动画/特效（与巡逻直线一致；三档共用一套资源）")]
    public string findmoveAnimation;
    public string findrestAnimation;
    public GameObject findmoveEffectPrefab;
    public GameObject findrestEffectPrefab;

    public string backmoveAnimation;
    public string backrestAnimation;
    public GameObject backmoveEffectPrefab;
    public GameObject backrestEffectPrefab;

    public string backjumpAnimation;
    public string backjumpRestAnimation;
    public GameObject backjumpEffectPrefab;
    public GameObject backjumpRestEffectPrefab;
}

[System.Serializable]
public class FollowMoveParams
{
    public float findmoveSpeed = 1f;
    public float findacceleration = 0f;
    public float findaccelerationTime = 0f;
    public float finddeceleration = 0f;
    public float finddecelerationTime = 0f;
    public float findmoveDuration = 0f;
    public float findrestDuration = 0f;
}

[System.Serializable]
public class RetreatMoveParams
{
    public float reversemoveSpeed = 1f;
    public float reverseacceleration = 0f;
    public float reverseaccelerationTime = 0f;
    public float reversedeceleration = 0f;
    public float reversedecelerationTime = 0f;
    public float reversemoveDuration = 0f;
    public float reverserestDuration = 0f;
}

[System.Serializable]
public class BackstepMoveParams
{
    public float backmoveSpeed = 1f;
    public float backacceleration = 0f;
    public float backaccelerationTime = 0f;
    public float backdeceleration = 0f;
    public float backdecelerationTime = 0f;
    public float backmoveDuration = 0f;
    public float backrestDuration = 0f;
}

#endregion

#region Jump 事件参数

[System.Serializable]
public class JumpSetV2
{
    [Header("Follow（发现跳跃距离）")]
    public FollowJumpParams find;

    [Header("Retreat（发现后退跳跃距离）")]
    public RetreatJumpParams reverse;

    [Header("Backstep（发现倒退跳跃距离）")]
    public BackstepJumpParams back;

    [Header("动画/特效（与巡逻跳跃一致；三档共用一套资源）")]
    public string findjumpAnimation;
    public string findjumpRestAnimation;
    public GameObject findjumpEffectPrefab;
    public GameObject findjumpRestEffectPrefab;
    [Header("Back (Jump) Animations")]
    public string backjumpAnimation;
    public string backjumpRestAnimation;

    [Header("Back (Jump) Effects")]
    public GameObject backjumpEffectPrefab;
    public GameObject backjumpRestEffectPrefab;

}

[System.Serializable]
public class FollowJumpParams
{
    public float findjumpSpeed = 2f;
    public float findjumpHeight = 1.5f;
    public float findgravityScale = 1f;
    public float findjumpDuration = 0f;
    public float findjumpRestDuration = 0f;
}

[System.Serializable]
public class RetreatJumpParams
{
    public float reversejumpSpeed = 2f;
    public float reversejumpHeight = 1.5f;
    public float reversegravityScale = 1f;
    public float reversejumpDuration = 0f;
    public float reversejumpRestDuration = 0f;
}

[System.Serializable]
public class BackstepJumpParams
{
    public float backjumpSpeed = 2f;
    public float backjumpHeight = 1.5f;
    public float backgravityScale = 1f;
    public float backjumpDuration = 0f;
    public float backjumpRestDuration = 0f;
}

#endregion

#endregion

/// <summary>出生朝向枚举</summary>
public enum Orientation { FacePlayer, FaceLeft, FaceRight }
/// <summary>巡逻移动类型</summary>
public enum MovementType { Straight, Jump }
/// <summary>出生位置类型</summary>
public enum SpawnPositionType { Points, Area }