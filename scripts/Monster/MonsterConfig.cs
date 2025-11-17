using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMonsterConfig", menuName = "怪物配置/新怪物配置")]
public class MonsterConfig : ScriptableObject
{
    [Header("基础属性")]
    [Tooltip("怪物唯一ID，用于查找或存档标识")]
    public string monsterID;
    [Tooltip("怪物等级（用于数值与掉落曲线）")]
    public int level;
    [Tooltip("最大生命值")]
    public float maxHP;
    [Tooltip("被击败后给予的经验值")]
    public int exp;

    [Header("资源配置")]
    [Tooltip("怪物的Prefab（包含动画、碰撞体、脚本等）")]
    public GameObject monsterPrefab;

    [Header("出生阶段配置")]
    [Tooltip("出生/待机相关播放与位置配置")]
    public SpawnConfig spawnConfig;

    [Header("巡逻阶段配置(地面)")]
    [Tooltip("路点与巡逻动作序列配置")]
    public PatrolConfig patrolConfig;

    [Header("发现阶段配置(V2)")]
    [Tooltip("发现/靠近/后退/倒退与攻击（V2）整体配置")]
    public DiscoveryV2Config discoveryV2Config;
}

#region Spawn / Patrol

[System.Serializable]
public class SpawnConfig
{
    [Tooltip("出生位置来源：点集/矩形区域")]
    public SpawnPositionType positionType;

    [Tooltip("positionType=Points 时的出生点列表")]
    public List<Vector2> spawnPoints = new List<Vector2>();

    [Tooltip("positionType=Points 时，是否顺序使用点（否则每次随机）")]
    public bool sequentialSpawn = true;

    [Tooltip("positionType=Area 时的矩形区域中心（世界坐标）")]
    public Vector2 areaCenter;

    [Tooltip("positionType=Area 时的区域尺寸（宽,高）")]
    public Vector2 areaSize;

    [Header("出生属性")]
    [Tooltip("出生面向：朝左/朝右/朝向玩家")]
    public Orientation spawnOrientation;

    [Tooltip("本次最多生成的怪物数量（由外部Spawner控制时可作为上限参考）")]
    public int maxSpawnCount = 1;

    [Tooltip("每波生成数量")]
    public int spawnBatchCount = 1;

    [Tooltip("每波之间的时间间隔（秒）")]
    public float spawnInterval = 0;

    [Header("动画设置")]
    [Tooltip("出生动画状态名（为空则不播放）")]
    public string spawnAnimation;

    [Tooltip("出生后进入的待机动画状态名（为空则不播放）")]
    public string idleAnimation;

    [InspectorName("Idle Time")]
    [Tooltip("出生后进入待机的持续时间（秒），0 表示不等待")]
    public float idleTime = 1f;

    [Header("特效设置")]
    [Tooltip("出生时播放的特效Prefab（可为空）")]
    public GameObject spawnEffectPrefab;

    [Tooltip("待机时播放的特效Prefab（可为空）")]
    public GameObject idleEffectPrefab;
}

[System.Serializable]
public class PatrolConfig
{
    [Tooltip("巡逻路点序列（世界坐标）")]
    public List<Vector2> patrolPoints = new List<Vector2>();

    [Tooltip("巡逻动作列表（直线/跳跃等），按顺序轮询或随机")]
    public List<PatrolMovement> movements = new List<PatrolMovement>();

    [Tooltip("是否随机播放巡逻动作序列")]
    public bool randomOrder = false;
}

[System.Serializable]
public class PatrolMovement
{
    [Tooltip("动作类型：直线移动 或 跳跃")]
    public MovementType type;

    [Tooltip("匀速阶段的目标水平速度（单位：米/秒）")]
    public float moveSpeed;
    [Tooltip("加速度（单位：米/秒²），当 accelerationTime<=0 时使用该值，否则由时间推算")]
    public float acceleration;
    [Tooltip("减速度（单位：米/秒²），当 decelerationTime<=0 时使用该值，否则由时间推算")]
    public float deceleration;
    [Tooltip("加速阶段所需时间（秒），>0 时按时间插值至 moveSpeed")]
    public float accelerationTime = 0f;
    [Tooltip("减速阶段所需时间（秒），>0 时按时间插值至 0")]
    public float decelerationTime = 0f;
    [Tooltip("匀速阶段的持续时间（秒），为 0 则无匀速直接进入减速/休息")]
    public float moveDuration;

    // 用区间替代固定 Rest 时长
    [Tooltip("直线动作结束后的休息时长下限（秒）")]
    public float restMin = 0f;
    [Tooltip("直线动作结束后的休息时长上限（秒）")]
    public float restMax = 0f;

    
    [Tooltip("直线移动时播放的动画状态名")]
    public string moveAnimation;
    [Tooltip("直线休息时播放的动画状态名")]
    public string restAnimation;
    [Tooltip("直线移动时播放的特效")]
    public GameObject moveEffectPrefab;
    [Tooltip("直线休息时播放的特效")]
    public GameObject restEffectPrefab;

    
    [Tooltip("跳跃的水平速度（米/秒）")]
    public float jumpSpeed;
    [Tooltip("跳跃的竖直高度（米）")]
    public float jumpHeight;
    [Tooltip("跳跃中的重力缩放（乘以基础重力）")]
    public float gravityScale;
    [Tooltip("用于“按时长规划多次跳”的预算（秒）；0 表示仅跳一次）")]
    public float jumpDuration;

    // 用区间替代固定 JumpRest 时长
    [Tooltip("跳跃到地面后的休息时长下限（秒）")]
    public float jumprestMin = 0f;
    [Tooltip("跳跃到地面后的休息时长上限（秒）")]
    public float jumprestMax = 0f;

    
    [Tooltip("起跳/空中阶段播放的动画状态名")]
    public string jumpAnimation;
    [Tooltip("落地休息阶段播放的动画状态名")]
    public string jumpRestAnimation;
    [Tooltip("起跳时播放的特效")]
    public GameObject jumpEffectPrefab;
    [Tooltip("跳休时播放的特效")]
    public GameObject jumpRestEffectPrefab;

    
    [Tooltip("自动跳时的水平速度（米/秒）")]
    public float autojumpSpeed;
    [Tooltip("自动跳时的竖直高度（米）")]
    public float autojumpHeight;
    [Tooltip("自动跳时的重力缩放")]
    public float autogravityScale = 1f;
    [Tooltip("自动跳的移动预算时长（秒），用于分割为多次连跳；0 表示只执行一次自动跳")]
    public float automoveDuration = 0f;
    [Tooltip("自动跳落地后的休息时长（秒）")]
    public float autorestDuration = 0f;

    // 运行时字段（不需配置）
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

public enum AttackType { Melee, Ranged }

[System.Serializable]
public class DiscoveryV2Config
{
    [Header("三档水平距离(Gizmos:红=发现,白=后退,黑=倒退)")]
    [Tooltip("发现距离（<=该距离进入“跟随/靠近”带）")]

    public float findRange = 6f;
    [Tooltip("后退距离（<=该距离进入“后退”带）")]
    public float reverseRange = 3.5f;
    [Tooltip("倒退距离（<=该距离进入“倒退/回拉”带）")]
    public float backRange = 1.5f;

    
    [Tooltip("周期内单个状态（正常/屏蔽）的随机持续时间下限（秒）。和上限均为0则关闭此功能。")]
    public float backDCTMin = 0f;
    [Tooltip("周期内单个状态（正常/屏蔽）的随机持续时间上限（秒）。")]
    public float backDCTMax = 0f;

    [Header("Back 档额外选项")]
    [Tooltip("勾选后：处于 Retreat/Backstep 且靠近墙或悬崖时，自动向玩家方向跳跃（使用事件的 JumpSet）。")]
    public bool enableBackAutoJumpOnObstacle = false;

    
    [Header("事件播放")]
    [Tooltip("是否随机播放事件列表（否则顺序播放）")]
    public bool findRandomOrder = false;

    [Tooltip("发现事件列表(Move 或 Jump)。会在发现阶段循环播放。")]
    public List<DiscoveryEventV2> events = new List<DiscoveryEventV2>();

    [Header("攻击（V2）- 无数据即不开启攻击")]
    [Tooltip("可选的攻击事件列表（近战/远程各一条或多条），空则不攻击")]
    public List<AttackEventV2> attacks = new List<AttackEventV2>();

    [Tooltip("攻击列表是否随机顺序（true：随机轮询；false：按 0,1,2… 顺序循环）")]
    public bool attacksRandomOrder = false;

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
    [Tooltip("事件类型：Move=地面移动；Jump=跳跃")]
    public DiscoveryV2Mode mode = DiscoveryV2Mode.Move;

    [Tooltip("Move 模式时遇障碍/悬崖的处理策略")]
    public ObstacleTurnMode obstacleTurnMode = ObstacleTurnMode.AutoTurn;

    [SerializeField, HideInInspector] public bool allowObstacleAutoTurnLegacy = true;

    [Tooltip("Move 模式参数集合（跟随/后退/倒退三档）")]
    public MoveSetV2 moveSet;
    [Tooltip("Jump 模式参数集合（跟随/后退/倒退三档）")]
    public JumpSetV2 jumpSet;
}

#region Move

[System.Serializable]
public class MoveSetV2
{
    [Header("Follow(发现移动距离)")]
    [Tooltip("跟随档位的移动参数")]
    public FollowMoveParams find;

    [SerializeField, HideInInspector] public RetreatMoveParams reverse;

    [Header("Backstep(发现倒退/后退, 公用此组参数)")]
    [Tooltip("后退/倒退档位的移动参数")]
    public BackstepMoveParams back;

    [Header("动画/特效（（find 与 retreat 共用；back 仅 move 动画不同，特效复用 find））")]
    [Tooltip("跟随/后退档位通用的移动动画")]
    public string findmoveAnimation;
    [Tooltip("跟随/后退档位通用的休息动画")]
    public string findrestAnimation;
    [Tooltip("跟随/后退档位通用的移动特效")]
    public GameObject findmoveEffectPrefab;
    [Tooltip("跟随/后退档位通用的休息特效")]
    public GameObject findrestEffectPrefab;
    [Tooltip("倒退档位专用的移动动画")]
    public string backmoveAnimation;
}

[System.Serializable]

public class FollowMoveParams
{
    [Header("Follow阶段")]
    [Tooltip("跟随档位：目标水平速度")]
    public float findmoveSpeed = 1f;
    [Tooltip("跟随档位：加速度")]
    public float findacceleration = 0f;
    [Tooltip("跟随档位：加速时间")]
    public float findaccelerationTime = 0f;
    [Tooltip("跟随档位：减速度")]
    public float finddeceleration = 0f;
    [Tooltip("跟随档位：减速时间")]
    public float finddecelerationTime = 0f;
    [Tooltip("跟随档位：匀速阶段持续时间")]
    public float findmoveDuration = 0f;
    [Tooltip("跟随档位：休息时长下限")]
    public float findrestMin = 0f;
    [Tooltip("跟随档位：休息时长上限")]
    public float findrestMax = 0f;
}

[System.Serializable]
public class RetreatMoveParams
{
    
    [Tooltip("后退档位：目标水平速度")]
    public float reversemoveSpeed = 1f;
    [Tooltip("后退档位：加速度")]
    public float reverseacceleration = 0f;
    [Tooltip("后退档位：加速时间")]
    public float reverseaccelerationTime = 0f;
    [Tooltip("后退档位：减速度")]
    public float reversedeceleration = 0f;
    [Tooltip("后退档位：减速时间")]
    public float reversedecelerationTime = 0f;
    [Tooltip("后退档位：匀速阶段持续时间")]
    public float reversemoveDuration = 0f;
    [Tooltip("后退档位：动作后休息时长（单值）")]
    public float reverserestDuration = 0f;
}

[System.Serializable]
public class BackstepMoveParams
{
    [Header("Backstep阶段")]
    [Tooltip("倒退档位：目标水平速度")]
    public float backmoveSpeed = 1f;
    [Tooltip("倒退档位：加速度")]
    public float backacceleration = 0f;
    [Tooltip("倒退档位：加速时间")]
    public float backaccelerationTime = 0f;
    [Tooltip("倒退档位：减速度")]
    public float backdeceleration = 0f;
    [Tooltip("倒退档位：减速时间")]
    public float backdecelerationTime = 0f;
    [Tooltip("倒退档位：匀速阶段持续时间")]
    public float backmoveDuration = 0f;

    [Tooltip("倒退档位：休息时长下限")]
    public float backrestMin = 0f;
    [Tooltip("倒退档位：休息时长上限")]
    public float backrestMax = 0f;
}

#endregion

#region Jump

[System.Serializable]
public class JumpSetV2
{
    [Header("Follow(发现跳跃距离)")]
    [Tooltip("跟随档位的跳跃参数")]
    public FollowJumpParams find;

    [SerializeField, HideInInspector] public RetreatJumpParams reverse;

    [Header("Backstep(发现倒退/后退跳, 公用此组参数)")]
    [Tooltip("后退/倒退档位的跳跃参数")]
    public BackstepJumpParams back;

    [Header("动画/特效")]
    [Tooltip("跟随/后退档位通用的跳跃动画")]
    public string findjumpAnimation;
    [Tooltip("跟随/后退档位通用的跳休动画")]
    public string findjumpRestAnimation;
    [Tooltip("跟随/后退档位通用的跳跃特效")]
    public GameObject findjumpEffectPrefab;
    [Tooltip("跟随/后退档位通用的跳休特效")]
    public GameObject findjumpRestEffectPrefab;
    [Tooltip("倒退档位专用的跳跃动画")]
    public string backjumpAnimation;
}

[System.Serializable]
public class FollowJumpParams
{
    [Tooltip("跟随档位：跳跃水平速度")]
    public float findjumpSpeed = 2f;
    [Tooltip("跟随档位：跳跃高度")]
    public float findjumpHeight = 1.5f;
    [Tooltip("跟随档位：重力缩放")]
    public float findgravityScale = 1f;
    [Tooltip("跟随档位：按时长预算来决定连跳次数（0=仅一次）")]
    public float findjumpDuration = 0f;
    [Tooltip("跟随档位：落地休息时长下限")]
    public float findjumpRestMin = 0f;
    [Tooltip("跟随档位：落地休息时长上限")]
    public float findjumpRestMax = 0f;

}

[System.Serializable]
public class RetreatJumpParams
{
    [Tooltip("后退档位：跳跃水平速度")]
    public float reversejumpSpeed = 2f;
    [Tooltip("后退档位：跳跃高度")]
    public float reversejumpHeight = 1.5f;
    [Tooltip("后退档位：重力缩放")]
    public float reversegravityScale = 1f;
    [Tooltip("后退档位：按时长预算来决定连跳次数（0=仅一次）")]
    public float reversejumpDuration = 0f;
    [Tooltip("后退档位：落地休息时长（单值）")]
    public float reversejumpRestDuration = 0f;
}

[System.Serializable]
public class BackstepJumpParams
{
    [Tooltip("倒退档位：跳跃水平速度")]
    public float backjumpSpeed = 2f;
    [Tooltip("倒退档位：跳跃高度")]
    public float backjumpHeight = 1.5f;
    [Tooltip("倒退档位：重力缩放")]
    public float backgravityScale = 1f;
    [Tooltip("倒退档位：按时长预算来决定连跳次数（0=仅一次）")]
    public float backjumpDuration = 0f;

    [Tooltip("倒退档位：跳休时长下限")]
    public float backjumpRestMin = 0f;
    [Tooltip("倒退档位：跳休时长上限")]
    public float backjumpRestMax = 0f;
}

#endregion

#endregion

// 互斥枚举（放在 AttackEventV2 定义上方的“攻击（V2）”区域）
public enum AttackMotionMode
{
    None = 0,
    Move = 1,
    Jump = 2
}

[System.Serializable]
public class AttackEventV2
{
    [Tooltip("攻击条目的自定义ID")]
    public string id;

    [Tooltip("攻击类型：近战 或 远程（可保留；运行时会根据距离决定执行模式）")]
    public AttackType attackType = AttackType.Melee;

    [Header("通用时序")]
    [Tooltip("本次攻击的时间窗口（秒）。在该时间内可循环播放攻击动画并触发效果。")]
    public float attackDuration = 0.8f;

    [Tooltip("攻击结束后进入攻击休息区间下限（秒）")]
    public float attackRestMin = 0.8f;
    [Tooltip("攻击结束后进入攻击休息区间上限（秒）")]
    public float attackRestMax = 1.0f;

    [Tooltip("在 attackDuration 时间窗内，攻击动画循环播放的次数（动画播完一遍算一次）")]
    public int repeatedHitsCount = 1;

    [Header("近战角色自身")]
    [Tooltip("近战可触发的有效距离（米）")]
    public float meleeRange = 1.0f;
    [Tooltip("近战攻击的动画状态名")]
    public string attackAnimation;
    [Tooltip("近战攻击播放的特效")]
    public GameObject attackEffectPrefab;
    [Tooltip("近战命中体 子物体路径（从怪物根开始的相对路径），对应子物体上应挂 Collider2D(isTrigger)")]
    public string meleeHitboxChildPath;

    [Header("远程角色自身")]
    [Tooltip("远程可触发的有效距离（米）")]
    public float rangedRange = 6.0f;
    [Tooltip("远程攻击的动画状态名")]
    public string attackFarAnimation;
    [Tooltip("远程攻击播放的特效")]
    public GameObject attackFarEffectPrefab;
    [Tooltip("投射物配置（速度/寿命/分布等）")]
    public ProjectileConfig projectile;

    [Header("攻击叠加运动模式")]
    [Tooltip("None=不叠加移动/跳跃；Move=攻击期间叠加水平位移；Jump=攻击开始时起跳一次")]
    public AttackMotionMode attackMotionMode = AttackMotionMode.None;

    // 旧共享字段（保留兼容）
    [Header("移动中攻击（旧共享字段，兼容用）")]
    [Tooltip("攻击期间的水平位移速度（旧字段）")]
    public float attackmoveSpeed = 0f;
    [Tooltip("攻击期间的加速度（旧字段）")]
    public float attackacceleration = 0f;
    [Tooltip("攻击期间的加速时间（旧字段）")]
    public float attackaccelerationTime = 0f;
    [Tooltip("攻击期间的减速度（旧字段）")]
    public float attackdeceleration = 0f;
    [Tooltip("攻击期间的减速时间（旧字段）")]
    public float attackdecelerationTime = 0f;
    [Tooltip("攻击中移动持续时间（秒，含加/匀/减；旧字段）")]
    public float attackmoveDuration = 0f;

    [Header("跳跃中攻击（旧共享字段，兼容用）")]
    [Tooltip("攻击起跳的水平速度（旧字段）")]
    public float attackjumpSpeed = 0f;
    [Tooltip("攻击起跳的高度（旧字段）")]
    public float attackjumpHeight = 0f;
    [Tooltip("攻击起跳的重力缩放（旧字段）")]
    public float attackgravityScale = 1f;
    [Tooltip("攻击跳跃预算时长（旧字段）")]
    public float attackjumpDuration = 0f;
    [Tooltip("攻击跳跃落地后的休息时长（旧字段）")]
    public float attackjumpRestDuration = 0f;

    // 新增：按类型分开的“移动中攻击”参数
    
    [Tooltip("近战时的攻击位移速度")]
    public float attackmoveSpeedMelee = 0f;
    [Tooltip("近战时的攻击位移加速度")]
    public float attackaccelerationMelee = 0f;
    [Tooltip("近战时的加速时间")]
    public float attackaccelerationTimeMelee = 0f;
    [Tooltip("近战时的减速度")]
    public float attackdecelerationMelee = 0f;
    [Tooltip("近战时的减速时间")]
    public float attackdecelerationTimeMelee = 0f;
    [Tooltip("近战时的总位移时长（含加/匀/减）")]
    public float attackmoveDurationMelee = 0f;

    
    [Tooltip("远程时的攻击位移速度")]
    public float attackmoveSpeedRanged = 0f;
    [Tooltip("远程时的攻击位移加速度")]
    public float attackaccelerationRanged = 0f;
    [Tooltip("远程时的加速时间")]
    public float attackaccelerationTimeRanged = 0f;
    [Tooltip("远程时的减速度")]
    public float attackdecelerationRanged = 0f;
    [Tooltip("远程时的减速时间")]
    public float attackdecelerationTimeRanged = 0f;
    [Tooltip("远程时的总位移时长（含加/匀/减）")]
    public float attackmoveDurationRanged = 0f;

    // 按类型分开的“跳跃中攻击”参数
    
    [Tooltip("近战时攻击起跳的水平速度")]
    public float attackjumpSpeedMelee = 0f;
    [Tooltip("近战时攻击起跳的高度")]
    public float attackjumpHeightMelee = 0f;
    [Tooltip("近战时攻击起跳的重力缩放")]
    public float attackgravityScaleMelee = 1f;
    [Tooltip("近战时攻击跳跃预算时长")]
    public float attackjumpDurationMelee = 0f;
    [Tooltip("近战时攻击跳跃落地后的休息时长")]
    public float attackjumpRestDurationMelee = 0f;

    
    [Tooltip("远程时攻击起跳的水平速度")]
    public float attackjumpSpeedRanged = 0f;
    [Tooltip("远程时攻击起跳的高度")]
    public float attackjumpHeightRanged = 0f;
    [Tooltip("远程时攻击起跳的重力缩放")]
    public float attackgravityScaleRanged = 1f;
    [Tooltip("远程时攻击跳跃预算时长")]
    public float attackjumpDurationRanged = 0f;
    [Tooltip("远程时攻击跳跃落地后的休息时长")]
    public float attackjumpRestDurationRanged = 0f;
}

[System.Serializable]
public class ProjectileConfig
{
    [Header("发射队列")]
    [Tooltip("一次动画触发发射的数量")]
    public int countPerBurst = 1;
    [Tooltip("同一触发内连发的间隔（秒）")]
    public float intraBurstInterval = 0.0f;
    [Tooltip("投射物存活时间（秒）")]
    public float lifeTime = 5.0f;

    [Header("扇形分布")]
    [Tooltip("多发时的扇形角度（度），0 表示同向")]
    public float spreadAngle = 0f;
    [Tooltip("多发时在扇形内是否均匀分布（false 则随机分布）")]
    public bool spreadUniform = true;

    [Header("表现资源")]
    [Tooltip("飞行中的动画名")]
    public string FlygunAnimation;
    [Tooltip("飞行中的特效Prefab")]
    public GameObject FlygunEffectPrefab;

    // 自身旋转
    [Header("自身旋转（可选）")]
    public bool selfRotate = false;
    public bool selfRotateX = false;
    public bool selfRotateY = false;
    public bool selfRotateZ = true;
    [Tooltip("旋转速度（度/秒），对勾选的轴生效")]
    public float selfRotateSpeedDeg = 360f;

    // 统一的“沿移动方向自动朝向”
    
    [Tooltip("勾选后：根对象沿移动方向自动朝向；若“自身旋转”启用，本项失效")]
    public bool faceAlongPath = true;
    
    [Tooltip("TowardsPlayer=朝向玩家飞；HorizontalToPlayer=按水平朝向玩家飞（忽略Y）")]
    public SpawnAimMode spawnAim = SpawnAimMode.TowardsPlayer;

    [Header("爆炸表现")]
    [Tooltip("爆炸半径（米）")]
    public float radius = 1.0f;
    [Tooltip("爆炸表现持续时间（秒）")]
    public float duration = 0.8f;
    [Tooltip("爆炸伤害/子效果的tick间隔（秒）")]
    public float interval = 0.2f;
    [Tooltip("爆炸动画名")]
    public string FlygunBoomAnimation;
    [Tooltip("爆炸特效Prefab")]
    public GameObject FlygunBoomEffectPrefab;

    // ========== 直线（基线推进） ==========
    [Header("直线（基线）")]
    [Tooltip("启用直线基线推进（speed/accel等）。关闭后不使用直线推进（回旋镖返程不受影响）。")]
    public bool linearEnabled = true;

    [Tooltip("初始速度（米/秒）")]
    public float speed = 6f;
    [Tooltip("加速度（米/秒²），当 accelTime<=0 时使用该值")]
    public float accel = 0f;
    [Tooltip("加速时间（秒），>0 时按时间插值至目标速度")]
    public float accelTime = 0f;
    [Tooltip("减速度（米/秒²），当 decelTime<=0 时使用该值")]
    public float decel = 0f;
    [Tooltip("减速时间（秒），>0 时按时间插值至 0")]
    public float decelTime = 0f;
    [Tooltip("匀速阶段持续时间（秒），0 表示无匀速直接进入减速/结束")]
    public float moveDuration = 0f;

    // ========== S 型直线（侧向正弦） ==========
    [Header("S 型直线（侧向正弦）")]
    [Tooltip("启用 S 型侧向偏移")]
    public bool sinEnabled = false;

    [Tooltip("正弦摆动幅度")]
    public float sinAmplitude = 0.5f;
    [Tooltip("正弦摆动频率（Hz）")]
    public float sinFrequency = 3f;

    // ProjectileConfig 内“抛物线（重力）”区块追加字段
    [Header("抛物线（重力）")]
    [Tooltip("启用抛物线重力效果")]
    public bool parabolaEnabled = false;

    [Tooltip("抛物线运动重力缩放")]
    public float gravityScale = 1f;

    [Tooltip("落地反弹系数（0=不反弹，>1 可越弹越高，不夹 [0,1]）")]
    public float bounceCoefficient = 0.0f;

    [Tooltip("反弹能量模式：Constant=每次相同；DecayToZero=每次按衰减因子衰减，低于阈值即销毁")]
    public BounceEnergyMode bounceEnergyMode = BounceEnergyMode.Constant;

    [Tooltip("每次反弹额外衰减因子（<1 逐渐变小；=1 不变；>1 逐渐变大）")]
    public float bounceDecayFactor = 1f;

    [Tooltip("竖直反弹速度低于该阈值则销毁（仅 DecayToZero 生效）")]
    public float bounceEndVyThreshold = 0.05f;

    [Tooltip("最高点高度（米，>0 时用来计算初始竖直速度 vy0 = sqrt(2*g*h)）")]
    public float parabolaApexHeight = 0f;

    // ========== 跟踪导弹 ==========
    [Header("跟踪导弹")]
    [Tooltip("启用跟踪：按频率刷新朝向目标（频率低更棱角，频率高更顺滑）")]
    public bool homingEnabled = false;

    [Tooltip("跟踪频率（Hz）：每秒更新朝向的次数。0 表示不更新（关闭）。")]
    public float homingFrequency = 0f;

    [Range(0f, 1f)]
    public float homingStrength = 1f;   // 0=不跟踪，1=最强跟踪

    // ProjectileConfig 中“半径旋转”相关字段（移除了 orbitTangentialSpeed）
    [Header("半径旋转（相对载体，PathTangent 空间）")]
    [Tooltip("启用半径旋转：相对载体按半径做圆周偏移")]
    public bool orbitEnabled = false;

    [Tooltip("半径（米）")]
    public float orbitRadius = 0f;

    [Tooltip("每次要旋转的角度（度）。例如 90/180/360。达到该角度后从0重新开始下一次 sweep。")]
    public float orbitAngular = 360f;

    [Tooltip("沿半径转动的角速度（度/秒）。符号决定方向：>0 逆时针，<0 顺时针。")]
    public float orbitSweepSpeedDeg = 360f;

    // ========== 回旋镖 ==========
    [Header("回旋镖")]
    [Tooltip("启用回旋镖：飞到最远距离 -> 停顿 -> 返回起点/发射者")]
    public bool boomerangEnabled = false;

    [Tooltip("飞出最远距离（米）：到达后进入‘最远距离停顿’阶段")]
    public float boomerangOutMaxDistance = 0f;

    [Tooltip("最远距离停顿时间（秒）")]
    public float boomerangApexStopTime = 0f;

    [Tooltip("回程匀速目标速度（米/秒）。>0 时作为回程速度目标")]
    public float boomerangBackUniformSpeed = 0f;
    [Tooltip("回程由当前速度匀速过渡到目标速度所需时间（秒）；=0 则瞬时切换")]
    public float boomerangBackUniformTime = 0f;

    [Tooltip("回程加速度（米/秒²），当 backAccelTime<=0 时使用该值")]
    public float boomerangBackAccel = 0f;
    [Tooltip("回程加速时间（秒），>0 时按时间插值到匀速目标")]
    public float boomerangBackAccelTime = 0f;

    [Tooltip("回程减速度（米/秒²），当 backDecelTime<=0 时使用该值（用于接近终点减速，可选）")]
    public float boomerangBackDecel = 0f;
    [Tooltip("回程减速时间（秒），>0 时按时间插值至 0（用于接近终点减速，可选）")]
    public float boomerangBackDecelTime = 0f;

}
public enum Orientation { FacePlayer, FaceLeft, FaceRight }
public enum MovementType { Straight, Jump }
public enum SpawnPositionType { Points, Area }
public enum SpawnAimMode { TowardsPlayer = 0, HorizontalToPlayer = 1 }
// 新增一个枚举，放在现有枚举区域附近
public enum BounceEnergyMode
{
    Constant = 0,    // 每次反弹强度不变
    DecayToZero = 1  // 每次乘以衰减因子，低于阈值即销毁
}
