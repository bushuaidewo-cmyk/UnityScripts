using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using static MonsterController;

/// <summary>
/// 怪物配置 ScriptableObject，包含怪物各阶段的参数配置。
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

    [Header("发现阶段配置（地面）")]
    public DiscoveryConfig discoveryConfig;

    [Header("攻击阶段配置（地面）")]
    public AttackConfig attackConfig;

    [Header("空中阶段配置启用")]
    [Tooltip("是否有空中阶段（怪物后续会切换到空中阶段）")]
    public bool hasAirPhase;

    [Header("巡逻阶段配置（空中）")]
    public AirPatrolConfig airPatrolConfig;

    [Header("发现阶段配置（空中）")]
    public AirDiscoveryConfig airDiscoveryConfig;

    [Header("攻击阶段配置（空中）")]
    public AttackConfig airAttackConfig;

    [Header("地空状态切换")]
    [Tooltip("当地面阶段结束后是否切换到空中阶段")]
    public bool switchToAirAfterGround;
    [Tooltip("当怪物HP低于此百分比时切换空中阶段（0表示不根据HP切换）")]
    public float switchHPThreshold; // 例如0.5表示HP低于50%时切换

    [Header("死亡阶段配置")]
    public DeathConfig deathConfig;
}

/// <summary>
/// 出生阶段配置
/// </summary>
[System.Serializable]
public class SpawnConfig
{
    [Tooltip("出生点配置类型：固定点列表或随机区域")]
    public SpawnPositionType positionType;
    [Tooltip("预设的出生点列表（世界坐标）。若有多个点可顺序或随机选择。")]
    public List<Vector2> spawnPoints = new List<Vector2>();
    [Tooltip("是否按顺序使用出生点（true则顺序循环，否则随机选择)")]
    public bool sequentialSpawn = true;
    [Tooltip("出生区域中心点（世界坐标）")]
    public Vector2 areaCenter;
    [Tooltip("出生区域大小（宽高，世界单位)")]

    [Header("出生属性")]
    public Vector2 areaSize;
    [Tooltip("出生朝向：朝向玩家，固定朝左，固定朝右")]
    public Orientation spawnOrientation;
    [Tooltip("该区域内同时存在的怪物最大数量")]
    public int maxSpawnCount = 1;
    [Tooltip("每次生成的怪物数量")]
    public int spawnBatchCount = 1;
    [Tooltip("两次出生生成的间隔时间（秒)")]
    public float spawnInterval = 0;

    [Header("动画设置")]
    [Tooltip("出生动画名称")]
    public string spawnAnimation;
    [Tooltip("出生后原地待机动画名称")]
    public string idleAnimation;
    [Tooltip("播放 Idle 动画的持续时间（秒）。在出生动画播放完毕后，播放 Idle 动画并维持此时长。")]
    [FormerlySerializedAs("idleDelay")]
    [InspectorName("Idle Time")]
    public float idleTime = 1f;

    [Header("特效设置")]
    [Tooltip("出生特效Prefab（粒子系统）")]
    public GameObject spawnEffectPrefab;
    [Tooltip("出生Idle循环特效Prefab（粒子系统）")]
    public GameObject idleEffectPrefab;
}


/// 巡逻阶段配置（地面）

[System.Serializable]
public class PatrolConfig
{
    [Tooltip("地面巡逻路径点列表（世界坐标）。怪物将按顺序在这些点之间移动（循环）。")]
    public List<Vector2> patrolPoints = new List<Vector2>();

    [Tooltip("巡逻移动模式列表（可组合多种移动方式）")]
    public List<PatrolMovement> movements = new List<PatrolMovement>();

    [Tooltip("巡逻模式是否随机切换（true则随机选择一种模式执行，false则按列表顺序循环）")]
    public bool randomOrder = false;
}


/// 巡逻移动单元配置（直线或跳跃移动）


[System.Serializable]
public class PatrolMovement
{
    [Tooltip("移动类型：直线或跳跃")]
    public MovementType type;

    [Header("直线移动参数")]
    [Tooltip("匀速速度")]
    public float moveSpeed;
    [Tooltip("起步加速度（当加速时长为0时使用该数值作为速率）")]
    public float acceleration;
    [Tooltip("停止前减速度（当减速时长为0时使用该数值作为速率）")]
    public float deceleration;

    [Tooltip("加速时长（秒），>0 则按“速度/时长”计算加速率")]
    public float accelerationTime = 0f;
    [Tooltip("减速时长（秒），>0 则按“速度/时长”计算减速率")]
    public float decelerationTime = 0f;

    [Tooltip("持续移动时间（含加速/减速）")]
    public float moveDuration;
    [Tooltip("休息停顿时间")]
    public float restDuration;

    [Header("移动动画配置")]
    [Tooltip("移动动画名称")]
    public string moveAnimation;
    [Tooltip("休息动画名称")]
    public string restAnimation;
    [Tooltip("移动时特效Prefab（粒子系统）")]
    public GameObject moveEffectPrefab;
    [Tooltip("休息时特效Prefab（粒子系统）")]
    public GameObject restEffectPrefab;

    [Header("跳跃移动参数（type=Jump 时使用）")]
    [Tooltip("水平跳跃速度")]
    public float jumpSpeed;
    [Tooltip("跳跃高度（垂直向上速度）")]
    public float jumpHeight;
    [Tooltip("重力系数")]
    public float gravityScale;
    [Tooltip("连续跳跃持续时间")]
    public float jumpDuration;
    [Tooltip("跳跃后的休息时间")]
    public float jumpRestDuration;

    [Header("跳跃动画/特效（Jump 与 AutoJump 资源共用）")]
    [Tooltip("跳跃（在空中）动画")]
    public string jumpAnimation;
    [Tooltip("跳跃休息（落地后）动画")]
    public string jumpRestAnimation;
    [Tooltip("跳跃起跳特效Prefab（粒子或序列帧皆可）")]
    public GameObject jumpEffectPrefab;
    [Tooltip("跳跃休息特效Prefab（粒子或序列帧皆可）")]
    public GameObject jumpRestEffectPrefab;

    [Header("Auto Jump 参数（由 autoJumpPermitTag 触发，资源与 Jump 共用）")]
    [Tooltip("自动跳跃水平速度 X")]
    public float autojumpSpeed;
    [Tooltip("自动跳跃高度 Y")]
    public float autojumpHeight;
    [Tooltip("自动跳跃重力系数（乘 BASE_G）")]
    public float autogravityScale = 1f;
    [Tooltip("自动跳跃总时长预算（秒）；<=0 表示只跳一次")]
    public float automoveDuration = 0f;

    [Tooltip("自动跳跃落地后的休息时长（秒）；<=0 表示无休息，直接恢复直线逻辑")]
    public float autorestDuration = 0f;


    // ===== 运行时字段（不序列化）=====
    [System.NonSerialized] public int rtExecuteRemain = 0;
    [System.NonSerialized] public float rtMoveTimer = 0f;
    [System.NonSerialized] public float rtRestTimer = 0f;
    [System.NonSerialized] public float rtCurrentSpeed = 0f;
    [System.NonSerialized] public StraightPhase rtStraightPhase = StraightPhase.None;
    [System.NonSerialized] public bool rtUsingAutoJumpParams = false;

    // 新增：将加/匀/减 分开计时（moveDuration 仅在 Cruise 阶段消耗）
    [System.NonSerialized] public float rtAccelTimer = 0f;
    [System.NonSerialized] public float rtCruiseTimer = 0f;
    [System.NonSerialized] public float rtDecelTimer = 0f;
}

// 运行时直线阶段枚举（加到 MonsterConfig.cs 底部或任意可见位置）
public enum StraightPhase { None, Accel, Cruise, Decel, Rest }


/// 发现阶段配置（地面）
[System.Serializable]
public class DiscoveryConfig
{
    [Tooltip("玩家警戒距离（进入该范围则触发发现阶段）")]
    public float alertRange;
    [Tooltip("发现阶段移动模式列表")]
    public List<DiscoveryMovement> movements = new List<DiscoveryMovement>();
    [Tooltip("模式是否随机切换")]
    public bool randomOrder = false;
    [Tooltip("模式组合整体是否循环（false则每种模式执行一次后结束发现阶段）")]
    public bool loopAll = true;
}


/// 发现阶段单种移动配置
[System.Serializable]
public class DiscoveryMovement
{
    [Tooltip("类型：直线追击、保持距离、跳跃追击")]
    public DiscoveryMovementType type;

    // 直线追击参数
    [Tooltip("追击移动速度")]
    public float chaseSpeed;
    [Tooltip("追击加速度")]
    public float acceleration;
    [Tooltip("追击减速度")]
    public float deceleration;
    [Tooltip("连续移动时间")]
    public float moveDuration;
    [Tooltip("暂停休息时间")]
    public float restDuration;


    // 保持距离参数
    [Tooltip("保持距离的最近距离")]
    public float minDistance;
    [Tooltip("保持距离的最远距离")]
    public float maxDistance;
    [Tooltip("维持该距离的时间")]
    public float maintainTime;
    [Tooltip("保持距离时的移动速度")]
    public float maintainSpeed;

    // 跳跃追击参数
    [Tooltip("跳跃追击速度")]
    public float jumpSpeed;
    [Tooltip("跳跃高度")]
    public float jumpHeight;
    [Tooltip("重力系数")]
    public float gravityScale;
    [Tooltip("连续跳跃时间")]
    public float jumpDuration;
    [Tooltip("跳跃后休息时间")]
    public float jumpRestDuration;


    [Tooltip("动画名称")]
    public string animation;
    [Tooltip("特效名称")]
    public string effect;

}

/// 攻击阶段配置（可以包含多种攻击方式）
[System.Serializable]
public class AttackConfig
{
    [Tooltip("攻击触发距离（玩家进入此距离开始攻击）")]
    public float attackRange;
    [Tooltip("包含的攻击模式列表")]
    public List<AttackPattern> attackPatterns = new List<AttackPattern>();
    [Tooltip("多种攻击方式时是否随机选择（false则按顺序循环）")]
    public bool randomOrder = false;
}

/// 攻击模式配置（近战、远程、碰撞、防御）
[System.Serializable]
public class AttackPattern
{
    [Tooltip("攻击类型：近战、远程、碰撞、防御")]
    public AttackType type;

    [Tooltip("攻击动画名称")]
    public string animation;
    [Tooltip("攻击特效名称")]
    public string effect;
    [Tooltip("伤害值")]
    public int damage;
    [Tooltip("一次攻击动作中的连续攻击次数")]
    public int repeatCount = 1;
    [Tooltip("连续攻击间隔时间")]
    public float repeatInterval = 0;
    [Tooltip("连续攻击时是否允许打断前一动画")]
    public bool interruptPreviousAnimation = false;

    // 近战特有参数
    [Tooltip("近战攻击的判定距离或范围")]
    public float meleeRange;
    [Tooltip("近战攻击是否可被玩家盾牌格挡")]
    public bool meleeBlockable = true;

    // 远程特有参数
    [Tooltip("投射物Prefab（远程攻击）")]
    public GameObject projectilePrefab;
    [Tooltip("投射物速度")]
    public float projectileSpeed;
    [Tooltip("连续发射投射物次数")]
    public int projectileCount = 1;
    [Tooltip("每次发射间隔")]
    public float projectileInterval = 0;
    [Tooltip("投射物寿命（秒）")]
    public float projectileLifetime = 5f;
    [Tooltip("投射物是否可被盾牌格挡")]
    public bool projectileBlockable = true;

    // 碰撞伤害特有参数
    [Tooltip("碰撞造成伤害的冷却时间")]
    public float collisionDamageCooldown = 1f;

    // 防御特有参数
    [Tooltip("防御持续时间")]
    public float defenseDuration;
    [Tooltip("防御期间是否无敌")]
    public bool invulnerableDuringDefense = true;
    [Tooltip("防御状态特效名称")]
    public string defenseEffect;
}

/// 空中巡逻阶段配置（飞行模式）
[System.Serializable]
public class AirPatrolConfig
{
    [Tooltip("空中巡逻移动模式列表")]
    public List<AirMove> moves = new List<AirMove>();
    [Tooltip("移动模式是否随机切换")]
    public bool randomOrder = false;
    [Tooltip("模式组合是否循环执行")]
    public bool loopAll = true;
}

/// 空中巡逻单个移动配置
[System.Serializable]
public class AirMove
{
    [Tooltip("飞行速度")]
    public float speed;
    [Tooltip("飞行持续时间")]
    public float moveDuration;
    [Tooltip("悬停休息时间")]
    public float hoverDuration;
    [Tooltip("飞行动画名称")]
    public string flyAnimation;
    [Tooltip("飞行特效名称")]
    public string flyEffect;
}

/// 空中发现阶段配置
[System.Serializable]
public class AirDiscoveryConfig
{
    [Tooltip("空中怪物警戒范围")]
    public float alertRange;
    [Tooltip("发现阶段飞行模式列表")]
    public List<AirMove> moves = new List<AirMove>();
    [Tooltip("模式是否随机切换")]
    public bool randomOrder = false;
    [Tooltip("模式组合是否循环")]
    public bool loopAll = true;
    [Tooltip("与玩家保持距离的最小值")]
    public float minDistance;
    [Tooltip("与玩家保持距离的最大值")]
    public float maxDistance;
    [Tooltip("始终朝向玩家")]
    public bool facePlayer = true;
}

/// 死亡阶段配置
[System.Serializable]
public class DeathConfig
{
    [Tooltip("死亡动画名称")]
    public string deathAnimation;
    [Tooltip("死亡特效名称")]
    public string deathEffect;
    [Tooltip("死亡是否触发爆炸伤害")]
    public bool explosiveDeath;
    [Tooltip("爆炸范围半径")]
    public float explosionRadius;
    [Tooltip("爆炸伤害值")]
    public int explosionDamage;
    [Tooltip("死亡后立即移除怪物（否则播放完动画后移除）")]
    public bool instantRemove;
}

/// <summary>出生朝向枚举</summary>
public enum Orientation { FacePlayer, FaceLeft, FaceRight }
/// <summary>巡逻移动类型</summary>
public enum MovementType { Straight, Jump }
/// <summary>发现阶段移动类型</summary>
public enum DiscoveryMovementType { DirectChase, MaintainDistance, JumpChase }
/// <summary>攻击类型</summary>
public enum AttackType { Melee, Ranged, Collision, Defend }
/// <summary>出生位置类型</summary>
public enum SpawnPositionType { Points, Area }