using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMonsterConfig", menuName = "怪物配置/新怪物配置")]
public class MonsterConfig : ScriptableObject
{
    [Header("基础属性")]
    public string monsterID;
    public int level;
    public float maxHP;
    public int exp;

    [Header("基础设置")]
    public GameObject monsterPrefab;

    [Header("出生阶段配置")]
    public SpawnConfig spawnConfig;

    [Header("巡逻阶段配置(地面)")]
    public PatrolConfig patrolConfig;

    [Header("发现阶段配置(V2)")]
    public DiscoveryV2Config discoveryV2Config;
}

#region Spawn / Patrol

[System.Serializable]
public class SpawnConfig
{
    public SpawnPositionType positionType;
    public List<Vector2> spawnPoints = new List<Vector2>();
    public bool sequentialSpawn = true;
    public Vector2 areaCenter;
    public Vector2 areaSize;

    [Header("出生属性")]
    public Orientation spawnOrientation;
    public int maxSpawnCount = 1;
    public int spawnBatchCount = 1;
    public float spawnInterval = 0;

    [Header("动画设置")]
    public string spawnAnimation;
    public string idleAnimation;
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

    // 用区间替代固定 Rest 时长
    public float restMin = 0f;
    public float restMax = 0f;

    [Header("移动动画配置")]
    public string moveAnimation;
    public string restAnimation;
    public GameObject moveEffectPrefab;
    public GameObject restEffectPrefab;

    [Header("跳跃移动参数(type=Jump 时使用)")]
    public float jumpSpeed;
    public float jumpHeight;
    public float gravityScale;
    public float jumpDuration;

    // 用区间替代固定 JumpRest 时长
    public float jumprestMin = 0f;
    public float jumprestMax = 0f;

    [Header("跳跃动画/特效(Jump 与 AutoJump 资源共用)")]
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

    // 运行时字段
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

#region 发现阶段 V2

public enum DiscoveryV2Mode { Move, Jump }

[System.Serializable]
public class DiscoveryV2Config
{
    [Header("三档水平距离(Gizmos:红=发现,白=后退,黑=倒退)")]
    public float findRange = 6f;
    public float reverseRange = 3.5f;
    public float backRange = 1.5f;

    [Header("Back 档额外选项")]
    [Tooltip("勾选后：处于 Retreat/Backstep 且靠近墙或悬崖时，自动向玩家方向跳跃（使用事件的 JumpSet）。")]
    public bool enableBackAutoJumpOnObstacle = false;

    [Tooltip("勾选后：后退/倒退进入休息期间，临时关闭后退/倒退距离检测；计时结束后若玩家仍在该距离内再恢复后退/倒退。")]
    public bool suppressBackBandDuringRest = true;

    [Header("事件播放")]
    public bool findRandomOrder = false;

    [Tooltip("发现事件列表(Move 或 Jump)")]
    public List<DiscoveryEventV2> events = new List<DiscoveryEventV2>();
}

public enum ObstacleTurnMode
{
    AutoTurn = 0,
    NoTurnCanFall = 1,
    NoTurnStopAtCliff = 2
}

[System.Serializable]
public class DiscoveryEventV2
{
    public DiscoveryV2Mode mode = DiscoveryV2Mode.Move;

    [Tooltip("发现-移动:遇障碍/悬崖处理策略（仅 Move 模式有效）")]
    public ObstacleTurnMode obstacleTurnMode = ObstacleTurnMode.AutoTurn;

    [SerializeField, HideInInspector] public bool allowObstacleAutoTurnLegacy = true;

    public MoveSetV2 moveSet;
    public JumpSetV2 jumpSet;
}

#region Move

[System.Serializable]
public class MoveSetV2
{
    [Header("Follow(发现移动距离)")]
    public FollowMoveParams find;

    [SerializeField, HideInInspector] public RetreatMoveParams reverse;

    [Header("Backstep(发现倒退/后退, 公用此组参数)")]
    public BackstepMoveParams back;

    [Header("动画/特效")]
    public string findmoveAnimation;
    public string findrestAnimation;
    public GameObject findmoveEffectPrefab;
    public GameObject findrestEffectPrefab;
    public string backmoveAnimation;
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

    
    public float backrestMin = 0f;
    public float backrestMax = 0f;
}

#endregion

#region Jump

[System.Serializable]
public class JumpSetV2
{
    [Header("Follow(发现跳跃距离)")]
    public FollowJumpParams find;

    [SerializeField, HideInInspector] public RetreatJumpParams reverse;

    [Header("Backstep(发现倒退/后退跳, 公用此组参数)")]
    public BackstepJumpParams back;

    [Header("动画/特效")]
    public string findjumpAnimation;
    public string findjumpRestAnimation;
    public GameObject findjumpEffectPrefab;
    public GameObject findjumpRestEffectPrefab;
    public string backjumpAnimation;
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

    
    public float backjumpRestMin = 0f;
    public float backjumpRestMax = 0f;
}

#endregion

#endregion

public enum Orientation { FacePlayer, FaceLeft, FaceRight }
public enum MovementType { Straight, Jump }
public enum SpawnPositionType { Points, Area }