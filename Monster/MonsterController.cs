using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// 怪物控制器：出生 → 巡逻 → 发现
/// 移除了空中阶段/地空切换/死亡阶段相关代码；发现阶段使用 discoveryV2Config。
public class MonsterController : MonoBehaviour
{
    [Header("怪物配置 ScriptableObject")]
    public MonsterConfig config;

    [HideInInspector] public MonsterSpawner spawner;
    private Animator animator;
    private Transform player;
    private Collider2D col;
    private Rigidbody2D rb;

    private bool isDead;
    private int patrolIndex = 0;
    private bool isResting = false;

    private List<PatrolMovement> patrolRuntimeMoves;

    // === 地形检测参数 ===
    [Header("地形检测参数")]
    public LayerMask groundLayer;
    public float wallCheckDistance = 0.4f;
    public float wallCheckHeightOffset = 0.2f;
    public float cliffCheckDistance = 0.6f;
    public float cliffCheckOffsetX = 0.3f;

    // === 跳跃运行时状态 ===
    private bool isJumping = false;
    private float vY = 0f;
    private const float BASE_G = 25f;
    private bool inJumpRestPhase = false;

    private float turnCooldown = 0f;
    private const float SKIN = 0.02f;
    private bool groundedAfterVerticalMove = false;

    [Header("AutoJump 许可区")]
    public string autoJumpPermitTag = "AutoJumpPermit";
    private bool inAutoJumpPermitZone = false;
    private bool autoJumpReady = true;
    private bool autoJumpRearmAfterLanding = false;
    private bool isAutoJumping = false;

    private enum MonsterState { Idle, Patrol, Discovery, Dead }
    private MonsterState state = MonsterState.Idle;

    // 四通道各自独立的“当前段”引用
    private PatrolMovement activeStraightMove = null;
    private PatrolMovement activeJumpMove = null;

    // Rigidbody & 斜坡物理
    private bool isGroundedMC = false;
    private Vector2 groundNormalMC = Vector2.up;
    private bool slopeIdleLockedMC = false;
    private float slopeSavedGravityMC = 0f;

    // 路点巡逻
    [SerializeField, Tooltip("接近路点的判定半径")] private float pointArriveRadius = 0.05f;
    private int waypointIndex = 0;
    private int waypointTargetIndex = 0;
    private int waypointDir = +1;

    private int suppressUpwardOnGroundFrames = 0;
    private int FacingSign() => (transform.rotation.eulerAngles.y == 0f) ? +1 : -1;
    private float desiredSpeedX = 0f;

    private List<int> moveOrder = null;
    private int moveOrderPos = 0;

    [Header("AutoJump 落地后抑制")]
    public int autoJumpIgnoreCliffFrames = 8;
    private int ignoreCliffFramesLeft = 0;

    [Header("斜坡/地面移动（怪物）")]
    [SerializeField, Range(0f, 1f)] private float groundMinNormalYMC = 0.70f;
    [SerializeField] private float horizontalStopThresholdOnSlopeMC = 0.02f;
    [SerializeField] private bool slopeIdleFreezeGravityMC = true;
    [SerializeField] private float slopeEnterIdleSpeedEpsilonMC = 0.50f;
    [SerializeField] private float slopeStopVyThresholdMC = -0.05f;

    [Header("特效锚点（可选）")]
    [SerializeField] private Transform fxSpawnPoint;
    [SerializeField] private Transform fxIdlePoint;
    [SerializeField] private Transform fxMovePoint;
    [SerializeField] private Transform fxRestPoint;
    [SerializeField] private Transform fxJumpPoint;
    [SerializeField] private Transform fxJumpRestPoint;

    // 发现阶段专用锚点（Follow/Back 各4个，均可选）
    [Header("发现阶段特效锚点（可选）")]
    [SerializeField] private Transform fxFindMovePoint;
    [SerializeField] private Transform fxFindRestPoint;
    [SerializeField] private Transform fxFindJumpPoint;
    [SerializeField] private Transform fxFindJumpRestPoint;
    [SerializeField] private Transform fxBackMovePoint;
    [SerializeField] private Transform fxBackRestPoint;
    [SerializeField] private Transform fxBackJumpPoint;
    [SerializeField] private Transform fxBackJumpRestPoint;

    // 1) 地形检测参数后，新增一组“前方低矮障碍自动跳（双射线）”配置
    [Header("前方低矮障碍自动跳（双射线）")]
    [SerializeField] private bool enableForwardGapAutoJump = true;
    [SerializeField, Tooltip("脚部水平射线长度（米）")]
    private float lowerRayLen = 0.30f;
    [SerializeField, Tooltip("判定‘很近阻挡’的命中距离阈值（米）")]
    private float lowerHitMax = 0.12f;
    [SerializeField, Tooltip("头顶水平射线长度（米）")]
    private float upperRayLen = 0.40f;
    [SerializeField, Tooltip("头顶以上的额外抬升（0=使用 autojumpHeight）")]
    private float upperHeightAdd = 0f;
    [SerializeField, Tooltip("脚尖向前的额外外扩（米）")]
    private float forwardToeOffset = 0.03f;

    // 可选：脚下 GroundPoint（不填则用碰撞框脚边前沿）
    [Header("地面锚点（可选）")]
    [SerializeField] private Transform groundPoint;

    [Header("发现距离计算锚点（可选）")]
    [SerializeField] private Transform monsterDistPoint;   // 怪物用于距离判定的点
    [SerializeField] private Transform playerDistPoint;    // 玩家用于距离判定的点

    [Header("发现距离设置")]
    [SerializeField, Tooltip("仅用水平距离判定（忽略Y），用于平台类玩法更稳定")]
    private bool discoveryUseHorizontalDistanceOnly = false;


    private enum FxSlot
    {
        Spawn, Idle, Move, Rest, Jump, JumpRest,
        FindMove, FindRest, FindJump, FindJumpRest,
        BackMove, BackRest, BackJump, BackJumpRest
    }

    // ================== 发现 V2 运行时字段 ==================
    private enum DiscoveryBand { Follow, Retreat, Backstep }
    private List<int> discoveryOrder = null;
    private int discoveryOrderPos = 0;
    private int activeDiscoveryIndex = 0;
    private DiscoveryEventV2 activeDiscoveryEvent = null;
    private DiscoveryBand currentBand = DiscoveryBand.Follow;
    private bool discoveryRestJustFinished = false;

    // 抖动抑制（档位滞回 + 最小驻留时间）
    [Header("发现抖动抑制")]
    [Tooltip("档位边界滞回（单位：米）；避免在边界来回切换导致左右闪")]
    public float bandHysteresis = 0.2f;
    [Tooltip("档位最小驻留时间（秒）；驻留期内不允许切档")]
    public float bandMinDwellTime = 0.35f;
    private float bandDwellTimer = 0f;

    // 发现：映射为“运行时镜像”的 PatrolMovement（直线/跳跃各三档）
    private PatrolMovement d2_move_follow, d2_move_retreat, d2_move_back;
    private PatrolMovement d2_jump_follow, d2_jump_retreat, d2_jump_back;

    [Header("发现阶段朝向抖动抑制")]
    public float faceFlipDeadZone = 0.15f; // 玩家与怪物X差在此死区内不翻转朝向

    // 跳跃：记录“移动方向”（与面向解耦，用于倒退跳跃等）
    private int currentJumpDirSign = +1;

    // 发现档位切换辅助：记录上一帧档位
    private DiscoveryBand prevBand = DiscoveryBand.Follow;

    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        animator = GetComponentInChildren<Animator>();

        if (animator) animator.applyRootMotion = false;
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();

        if (rb == null)
        {
            Debug.LogError($"[MonsterController] {name} 未找到 Rigidbody2D！");
            enabled = false;
            return;
        }

        // 初始朝向
        if (config.spawnConfig.spawnOrientation == Orientation.FaceLeft)
            transform.rotation = Quaternion.Euler(0, 180f, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FaceRight)
            transform.rotation = Quaternion.Euler(0, 0, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FacePlayer && player)
        {
            var p0 = GetPlayerDistPos();
            var m0 = GetMonsterDistPos();
            transform.rotation = (p0.x > m0.x) ? Quaternion.Euler(0, 0f, 0) : Quaternion.Euler(0, 180f, 0);
        }
        

        // 事件中继器
        if (animator != null && animator.GetComponent<MonsterAnimationEventRelay>() == null)
            animator.gameObject.AddComponent<MonsterAnimationEventRelay>();

        // 克隆配置，避免运行时修改资产
        if (config != null)
            config = ScriptableObject.Instantiate(config);

        // 巡逻运行态
        if (config && config.patrolConfig != null && config.patrolConfig.movements != null)
            patrolRuntimeMoves = config.patrolConfig.movements;
        else
            patrolRuntimeMoves = new List<PatrolMovement>();

        BuildMoveOrderFromConfig();
        InitWaypointsIfAny();

        // 发现V2初始化（若已配置）
        if (config.discoveryV2Config != null &&
            config.discoveryV2Config.events != null &&
            config.discoveryV2Config.events.Count > 0)
        {
            BuildDiscoveryOrderFromConfig();
            EnsureActiveDiscoveryEventSetup(resetRuntimes: true);
        }

        turnCooldown = 1f;
        StartCoroutine(StateMachine());
    }

    void Update()
    {
        if (turnCooldown > 0f) turnCooldown -= Time.deltaTime;
        if (bandDwellTimer > 0f) bandDwellTimer -= Time.deltaTime;

        // 冷却全局递减（无论处于巡逻或发现）
        if (ignoreCliffFramesLeft > 0) ignoreCliffFramesLeft--;

        // 自动跳重新上膛
        if (autoJumpRearmAfterLanding && !isJumping && !inAutoJumpPermitZone && ignoreCliffFramesLeft <= 0)
        {
            autoJumpReady = true;
            autoJumpRearmAfterLanding = false;
        }
    }

    IEnumerator StateMachine()
    {
        // 出生（保持不变）
        if (!string.IsNullOrEmpty(config.spawnConfig.spawnAnimation))
        {
            animator.CrossFadeInFixedTime(config.spawnConfig.spawnAnimation, 0f, 0, 0f);
            animator.Update(0f);
            yield return WaitForStateFinished(config.spawnConfig.spawnAnimation, 0);
        }
        float idleTime = Mathf.Max(0f, config.spawnConfig.idleTime);
        if (idleTime > 0f && !string.IsNullOrEmpty(config.spawnConfig.idleAnimation))
        {
            animator.CrossFadeInFixedTime(config.spawnConfig.idleAnimation, 0f, 0, 0f);
            animator.Update(0f);
            yield return new WaitForSeconds(idleTime);
        }

        state = MonsterState.Patrol;

        while (!isDead)
        {
            switch (state)
            {
                case MonsterState.Patrol:
                    PatrolUpdate();
                    break;
                case MonsterState.Discovery:
                    DiscoveryUpdate();
                    break;
                default:
                    IdleUpdate();
                    break;
            }
            yield return null;
        }
    }

    void IdleUpdate()
    {
        if (!string.IsNullOrEmpty(config.spawnConfig.idleAnimation))
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(config.spawnConfig.idleAnimation))
                PlayAnimIfNotCurrent(config.spawnConfig.idleAnimation);
        }
    }

    // ================== 巡逻阶段 ==================
    void PatrolUpdate()
    {

        // 刷接地/斜坡
        UpdateGroundedAndSlope();

        // 新增：先尝试双射线触发 AutoJump；若起跳则本帧直接退出（下一帧走 JumpUpdate）
        if (MaybeAutoJumpOverLowObstacle(FacingSign())) return;

        // 进入发现范围：切换到发现阶段
        var dcfg = config.discoveryV2Config;
        if (player && dcfg != null)
        {
            float dist = discoveryUseHorizontalDistanceOnly
                ? Mathf.Abs(GetPlayerDistPos().x - GetMonsterDistPos().x)
                : Vector2.Distance(GetPlayerDistPos(), GetMonsterDistPos());
            if (dist <= Mathf.Max(0f, dcfg.findRange))
            {
                state = MonsterState.Discovery;

                // 仅当不在跳跃中才重置运行态；保持 AutoJump 的连贯性
                bool reset = !(isJumping || isAutoJumping);
                EnsureActiveDiscoveryEventSetup(resetRuntimes: reset);
                

                // 若发现阶段未设置任何动画，立即回到 Idle，避免沿用巡逻动画
                MaybeFallbackToIdleIfNoDiscoveryAnims();

                return;
            }
        }

        // 跳跃优先
        if (isJumping && activeJumpMove != null)
        {
            JumpUpdate(activeJumpMove);
            return;
        }

        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        PatrolMovement move = patrolRuntimeMoves[patrolIndex];

        // 直线分支（已抽公共逻辑）
        if (!isResting && move.type == MovementType.Straight)
        {
            activeStraightMove = move;

            bool finished = StraightTickCommon(move, dirSign: FacingSign(), useWaypoints: true);

            if (finished)
                AdvancePatrolIndex();

            return;
        }

        // 跳跃分支
        if (!isResting && move.type == MovementType.Jump)
        {
            bool canTurnNow = !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);

            if (CheckGrounded()) WaypointUpdateAndMaybeTurn(canTurnNow);

            if (!isJumping)
            {
                if (CheckGrounded())
                {
                    EnsureJumpPlan(move, useAutoParams: false);
                    BeginOneJump(move, useAutoParams: false);
                }
                else
                {
                    return;
                }
            }
            else
            {
                JumpUpdate(move);
            }
            return;
        }

        // 休息期
        if (isResting)
        {
            string restAnim =
                (move.type == MovementType.Jump && !string.IsNullOrEmpty(move.jumpRestAnimation))
                    ? move.jumpRestAnimation
                    : move.restAnimation;

            PlayAnimIfNotCurrent(restAnim);

            bool canTurnNow = !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);
            WaypointUpdateAndMaybeTurn(canTurnNow);

            rb.velocity = new Vector2(0f, rb.velocity.y);

            float restLeft =
                (move.type == MovementType.Jump)
                    ? (move.rtRestTimer > 0f ? move.rtRestTimer : move.jumpRestDuration)
                    : (move.rtRestTimer > 0f ? move.rtRestTimer : move.restDuration);

            restLeft -= Time.deltaTime;
            move.rtRestTimer = restLeft;

            desiredSpeedX = 0f;
            ApplySlopeIdleStopIfNoMove();

            if (restLeft <= 0f)
            {
                isResting = false;
                inJumpRestPhase = false;
                activeJumpMove = null;
                move.rtStraightPhase = StraightPhase.None;
                move.rtMoveTimer = 0f;
                move.rtRestTimer = 0f;
                move.rtUsingAutoJumpParams = false;

                AdvancePatrolIndex();
            }
            return;
        }
    }

    // ================== 发现阶段（V2） ==================
    void DiscoveryUpdate()
    {
        discoveryRestJustFinished = false;

        if (!player)
        {
            state = MonsterState.Patrol;
            return;
        }

        var dcfg = config.discoveryV2Config;
        if (dcfg == null || dcfg.events == null || dcfg.events.Count == 0)
        {
            state = MonsterState.Patrol;
            return;
        }

        // 刷接地，提升自动跳判断稳定性
        UpdateGroundedAndSlope();

        var p = GetPlayerDistPos();
        var m = GetMonsterDistPos();
        float d = discoveryUseHorizontalDistanceOnly ? Mathf.Abs(p.x - m.x) : Vector2.Distance(p, m);

        // 超出发现范围 -> 立即回巡逻
        if (d > dcfg.findRange)
        {
            state = MonsterState.Patrol;
            return;
        }

        

        // 带滞回的档位计算，抑制左右闪
        DiscoveryBand wantBand = ComputeWantBandWithHysteresis(d, dcfg);

        // 切档条件：不在跳跃过程中，且最小驻留时间结束
        bool bandChangeAllowedNow = !(activeDiscoveryEvent?.mode == DiscoveryV2Mode.Jump && isJumping) && !isResting && (bandDwellTimer <= 0f) && (currentBand != wantBand);

        if (bandChangeAllowedNow)
        {
            currentBand = wantBand;

            // Move 模式切档时，不要打断正在进行的休息；仅在非休息时重置运行态
            if (activeDiscoveryEvent != null && activeDiscoveryEvent.mode == DiscoveryV2Mode.Move && !isResting)
            {
                var moveForNewBand = GetD2MoveFor(currentBand);
                ResetStraightRuntime(moveForNewBand);
            }

            bandDwellTimer = Mathf.Max(0.05f, bandMinDwellTime);
        }

        float dxToPlayer = p.x - m.x;
        int dirToPlayer = (Mathf.Abs(dxToPlayer) <= faceFlipDeadZone) ? FacingSign() : (dxToPlayer >= 0f ? +1 : -1);
        
        if (activeDiscoveryEvent == null)
        {
            AdvanceDiscoveryEvent(orderResetIfEmpty: true);
            if (activeDiscoveryEvent == null) { state = MonsterState.Patrol; return; }
        }

        // 是否刚刚从 Backstep 离开（如需延后翻身时可用）
        bool exitedBackThisFrame = (prevBand == DiscoveryBand.Backstep) && (currentBand != DiscoveryBand.Backstep);

        if (activeDiscoveryEvent.mode == DiscoveryV2Mode.Move)
        {
            // 若正在进行 AutoJump（或任何跳跃），避免直线逻辑覆盖水平速度：优先跳跃更新
            if (isJumping && activeJumpMove != null)
            {
                JumpUpdate(activeJumpMove);
                prevBand = currentBand;
                return;
            }

            var move = GetD2MoveFor(currentBand);

            // 面向/移动方向（保持你原有规则）
            int faceSign, moveSign;
            if (currentBand == DiscoveryBand.Follow) { faceSign = dirToPlayer; moveSign = dirToPlayer; }
            else if (currentBand == DiscoveryBand.Retreat) { bool resting = 
                    (move.rtStraightPhase == StraightPhase.Rest) 
                    || isResting; faceSign = resting ? dirToPlayer 
                    : -dirToPlayer; moveSign = -dirToPlayer; }
            else { faceSign = dirToPlayer; moveSign = -dirToPlayer; }

            ForceFaceSign(faceSign);

            // 再按“移动方向”做自动跳检测
            if (!isJumping && isGroundedMC && MaybeAutoJumpOverLowObstacle(moveSign))
            {
                // 本帧直接空中更新并返回，避免直线逻辑覆盖水平速度
                if (isJumping && activeJumpMove != null)
                {
                    JumpUpdate(activeJumpMove);
                    prevBand = currentBand;
                    return;
                }
            }

            // 三态映射 -> allowTurn / stopAtCliff
            bool allowTurn, stopAtCliff;
            switch (activeDiscoveryEvent.obstacleTurnMode)
            {
                case ObstacleTurnMode.AutoTurn:
                    allowTurn = true; stopAtCliff = false; break;
                case ObstacleTurnMode.NoTurnCanFall:
                    allowTurn = false; stopAtCliff = false; break;
                case ObstacleTurnMode.NoTurnStopAtCliff:
                default:
                    allowTurn = false; stopAtCliff = true; break;
            }

            // 调用 StraightTickCommon 时把两者传进去
            discoveryRestJustFinished = StraightTickCommon(
                move,
                moveSign,
                useWaypoints: false,
                useMoveDirForProbes: true,
                allowTurnOnObstacle: allowTurn,
                stopAtCliffEdgeWhenNoTurn: stopAtCliff
            );

            if (discoveryRestJustFinished)
                AdvanceDiscoveryEvent();
        }
        else // Jump 模式
        {
            // 若是 AutoJump 进行中，优先使用 activeJumpMove（巡逻段的 auto 参数），
            // 避免误用 jmove（发现跳配置、通常 auto 参数为 0）导致“跳得很近”
            if (isAutoJumping && activeJumpMove != null)
            {
                JumpUpdate(activeJumpMove);
                prevBand = currentBand;
                return;
            }

            var jmove = GetD2JumpFor(currentBand);

            // 面向规则：Backstep/Follow 面向玩家；Retreat 起跳时背对玩家
            int faceSign = (currentBand == DiscoveryBand.Retreat) ? -dirToPlayer : dirToPlayer;
            ForceFaceSign(faceSign);

            // 移动方向规则：Follow 向玩家；Retreat 远离玩家；Backstep 远离玩家（脸朝玩家）
            int moveDir = (currentBand == DiscoveryBand.Follow) ? dirToPlayer : -dirToPlayer;

            // 在地面且未起跳时，按“移动方向”尝试自动跳（坑底恢复）
            if (!isJumping && isGroundedMC && MaybeAutoJumpOverLowObstacle(moveDir))
            {
                if (isJumping && activeJumpMove != null)
                {
                    JumpUpdate(activeJumpMove);
                    prevBand = currentBand;
                    return;
                }
            }

            // 跳跃休息期推进（发现阶段）
            if (isResting && inJumpRestPhase)
            {
                if (!string.IsNullOrEmpty(jmove.jumpRestAnimation))
                    PlayAnimIfNotCurrent(jmove.jumpRestAnimation);

                rb.velocity = new Vector2(0f, rb.velocity.y);
                desiredSpeedX = 0f;
                ApplySlopeIdleStopIfNoMove();

                if (jmove.rtRestTimer <= 0f) jmove.rtRestTimer = jmove.jumpRestDuration;
                jmove.rtRestTimer -= Time.deltaTime;

                if (jmove.rtRestTimer <= 0f)
                {
                    isResting = false;
                    inJumpRestPhase = false;
                    jmove.rtRestTimer = 0f;
                    discoveryRestJustFinished = true;
                }
            }
            else
            {
                if (!isJumping)
                {
                    EnsureJumpPlan(jmove, useAutoParams: false);
                    BeginOneJump(jmove, useAutoParams: false, moveDirOverride: moveDir);
                }
                else
                {
                    JumpUpdate(jmove); // “这一跳完成后再切档”
                }
            }

            if (discoveryRestJustFinished)
                AdvanceDiscoveryEvent();
        }

        prevBand = currentBand;
    }

    // 档位计算（带滞回，不修改配置）
    private DiscoveryBand ComputeWantBandWithHysteresis(float d, DiscoveryV2Config cfg)
    {
        float find = Mathf.Max(0f, cfg.findRange);
        float revBase = Mathf.Max(0f, cfg.reverseRange);
        float backBase = Mathf.Max(0f, cfg.backRange);

        // 独立阈值 + 本档离开滞回（不相互夹紧）
        float revOut = revBase + Mathf.Max(0f, bandHysteresis);
        float backOut = backBase + Mathf.Max(0f, bandHysteresis);

        switch (currentBand)
        {
            case DiscoveryBand.Follow:
                if (d <= backBase) return DiscoveryBand.Backstep;
                if (d <= revBase) return DiscoveryBand.Retreat;
                return DiscoveryBand.Follow;

            case DiscoveryBand.Retreat:
                if (d <= backBase) return DiscoveryBand.Backstep;
                if (d >= revOut) return DiscoveryBand.Follow;
                return DiscoveryBand.Retreat;

            case DiscoveryBand.Backstep:
                if (d >= backOut)
                    return (d <= revBase) ? DiscoveryBand.Retreat : DiscoveryBand.Follow;
                return DiscoveryBand.Backstep;

            default:
                if (d <= backBase) return DiscoveryBand.Backstep;
                if (d <= revBase) return DiscoveryBand.Retreat;
                return DiscoveryBand.Follow;
        }
    }

    // ===== 发现V2：运行态构建/序列/工具 =====
    private PatrolMovement GetD2MoveFor(DiscoveryBand band)
    {
        var set = activeDiscoveryEvent?.moveSet;
        if (set == null) return EnsureDummyMove();

        if (band == DiscoveryBand.Follow)
            return d2_move_follow ??= BuildStraightFromFollow(set);
        if (band == DiscoveryBand.Retreat)
            return d2_move_retreat ??= BuildStraightFromRetreat(set);
        return d2_move_back ??= BuildStraightFromBack(set);
    }

    private PatrolMovement BuildStraightFromFollow(MoveSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Straight,
            moveSpeed = set.find.findmoveSpeed,
            acceleration = set.find.findacceleration,
            accelerationTime = set.find.findaccelerationTime,
            deceleration = set.find.finddeceleration,
            decelerationTime = set.find.finddecelerationTime,
            moveDuration = set.find.findmoveDuration,
            restDuration = set.find.findrestDuration,
            moveAnimation = set.findmoveAnimation,
            restAnimation = set.findrestAnimation,
            moveEffectPrefab = set.findmoveEffectPrefab,
            restEffectPrefab = set.findrestEffectPrefab
        };
    }

    private PatrolMovement BuildStraightFromRetreat(MoveSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Straight,
            moveSpeed = set.reverse.reversemoveSpeed,
            acceleration = set.reverse.reverseacceleration,
            accelerationTime = set.reverse.reverseaccelerationTime,
            deceleration = set.reverse.reversedeceleration,
            decelerationTime = set.reverse.reversedecelerationTime,
            moveDuration = set.reverse.reversemoveDuration,
            restDuration = set.reverse.reverserestDuration,
            moveAnimation = set.findmoveAnimation,
            restAnimation = set.findrestAnimation,
            moveEffectPrefab = set.findmoveEffectPrefab,
            restEffectPrefab = set.findrestEffectPrefab
        };
    }

    private PatrolMovement BuildStraightFromBack(MoveSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Straight,
            moveSpeed = set.back.backmoveSpeed,
            acceleration = set.back.backacceleration,
            accelerationTime = set.back.backaccelerationTime,
            deceleration = set.back.backdeceleration,
            decelerationTime = set.back.backdecelerationTime,
            moveDuration = set.back.backmoveDuration,
            restDuration = set.back.backrestDuration,

            moveAnimation = set.backmoveAnimation,
            restAnimation = set.backrestAnimation,
            moveEffectPrefab = set.backmoveEffectPrefab,
            restEffectPrefab = set.backrestEffectPrefab
        };
    }

    private PatrolMovement EnsureDummyMove()
    {
        return d2_move_follow ??= new PatrolMovement { type = MovementType.Straight, moveSpeed = 0f };
    }

    private PatrolMovement GetD2JumpFor(DiscoveryBand band)
    {
        var set = activeDiscoveryEvent?.jumpSet;
        if (set == null) return EnsureDummyJump();

        if (band == DiscoveryBand.Follow)
            return d2_jump_follow ??= BuildJumpFromFollow(set);
        if (band == DiscoveryBand.Retreat)
            return d2_jump_retreat ??= BuildJumpFromRetreat(set);
        return d2_jump_back ??= BuildJumpFromBack(set);
    }

    private PatrolMovement BuildJumpFromFollow(JumpSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Jump,
            jumpSpeed = set.find.findjumpSpeed,
            jumpHeight = set.find.findjumpHeight,
            gravityScale = set.find.findgravityScale,
            jumpDuration = set.find.findjumpDuration,
            jumpRestDuration = set.find.findjumpRestDuration,
            jumpAnimation = set.findjumpAnimation,
            jumpRestAnimation = set.findjumpRestAnimation,
            jumpEffectPrefab = set.findjumpEffectPrefab,
            jumpRestEffectPrefab = set.findjumpRestEffectPrefab
        };
    }

    private PatrolMovement BuildJumpFromRetreat(JumpSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Jump,
            jumpSpeed = set.reverse.reversejumpSpeed,
            jumpHeight = set.reverse.reversejumpHeight,
            gravityScale = set.reverse.reversegravityScale,
            jumpDuration = set.reverse.reversejumpDuration,
            jumpRestDuration = set.reverse.reversejumpRestDuration,
            jumpAnimation = set.findjumpAnimation,
            jumpRestAnimation = set.findjumpRestAnimation,
            jumpEffectPrefab = set.findjumpEffectPrefab,
            jumpRestEffectPrefab = set.findjumpRestEffectPrefab
        };
    }

    private PatrolMovement BuildJumpFromBack(JumpSetV2 set)
    {
        return new PatrolMovement
        {
            type = MovementType.Jump,
            jumpSpeed = set.back.backjumpSpeed,
            jumpHeight = set.back.backjumpHeight,
            gravityScale = set.back.backgravityScale,
            jumpDuration = set.back.backjumpDuration,
            jumpRestDuration = set.back.backjumpRestDuration,

            jumpAnimation = set.backjumpAnimation,
            jumpRestAnimation = set.backjumpRestAnimation,
            jumpEffectPrefab = set.backjumpEffectPrefab,
            jumpRestEffectPrefab = set.backjumpRestEffectPrefab
        };
    }

    private PatrolMovement EnsureDummyJump()
    {
        return d2_jump_follow ??= new PatrolMovement { type = MovementType.Jump, jumpSpeed = 0f };
    }

    // 通用直线驱动：复用巡逻直线三相 + 地形 + 速度写入；
    // 返回值：true 表示本次“直线+休息”一个完整周期刚结束（可用于发现事件推进）
    private bool StraightTickCommon(
        PatrolMovement move,
        int dirSign,
        bool useWaypoints,
        bool useMoveDirForProbes = false,
        bool allowTurnOnObstacle = true,
        bool stopAtCliffEdgeWhenNoTurn = false) //不允许转向时，遇悬崖是否原地停住)
    {
        // 刷接地/斜坡
        UpdateGroundedAndSlope();

        // 立即休息快路径
        if (!isResting && move.rtStraightPhase == StraightPhase.None && move.restDuration > 0f && (move.moveDuration <= 0f || move.moveSpeed <= 0.0001f))
        {
            move.rtStraightPhase = StraightPhase.Rest;
            isResting = true;
            move.rtRestTimer = Mathf.Max(0f, move.restDuration);

            PlayAnimIfNotCurrent(move.restAnimation);

            rb.velocity = new Vector2(0f, rb.velocity.y);
            desiredSpeedX = 0f;
            ApplySlopeIdleStopIfNoMove();
            return false;
        }

        // 初始化三相
        if (!isResting && move.rtStraightPhase == StraightPhase.None)
        {
            move.rtCurrentSpeed = 0f;
            move.rtCruiseTimer = Mathf.Max(0f, move.moveDuration);
            move.rtAccelTimer = Mathf.Max(0f, move.accelerationTime);
            move.rtDecelTimer = Mathf.Max(0f, move.decelerationTime);

            bool instantAccel = (move.accelerationTime <= 0f && move.acceleration <= 0f);
            bool instantDecel = (move.decelerationTime <= 0f && move.deceleration <= 0f);

            move.rtStraightPhase = instantAccel
                ? ((move.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (instantDecel ? StraightPhase.Rest : StraightPhase.Decel))
                : StraightPhase.Accel;

            if (instantAccel) move.rtCurrentSpeed = Mathf.Max(0f, move.moveSpeed);

            if (move.rtStraightPhase == StraightPhase.Rest)
            {
                isResting = true;
                move.rtRestTimer = Mathf.Max(0f, move.restDuration);
                PlayAnimIfNotCurrent(move.restAnimation);
                desiredSpeedX = 0f;
                ApplySlopeIdleStopIfNoMove();
                return false;
            }
        }

        float targetSpeed = Mathf.Max(0f, move.moveSpeed);
        float accelRate = (move.accelerationTime > 0f)
            ? (Mathf.Max(0.01f, targetSpeed) / move.accelerationTime)
            : Mathf.Max(0f, move.acceleration);
        float decelRate = (move.decelerationTime > 0f)
            ? (Mathf.Max(0.01f, targetSpeed) / move.decelerationTime)
            : Mathf.Max(0f, move.deceleration);

        switch (move.rtStraightPhase)
        {
            case StraightPhase.Accel:
                {
                    PlayAnimIfNotCurrent(move.moveAnimation);
                    if (accelRate <= 0f)
                    {
                        move.rtCurrentSpeed = targetSpeed;
                        move.rtStraightPhase = (move.rtCruiseTimer > 0f) ? StraightPhase.Cruise
                                              : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest);
                    }
                    else
                    {
                        move.rtCurrentSpeed = Mathf.MoveTowards(move.rtCurrentSpeed, targetSpeed, accelRate * Time.deltaTime);
                        if (move.accelerationTime > 0f) move.rtAccelTimer = Mathf.Max(0f, move.rtAccelTimer - Time.deltaTime);
                        if (Mathf.Approximately(move.rtCurrentSpeed, targetSpeed) || move.rtAccelTimer <= 0f)
                            move.rtStraightPhase = (move.rtCruiseTimer > 0f) ? StraightPhase.Cruise
                                              : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest);
                    }
                    break;
                }
            case StraightPhase.Cruise:
                {
                    PlayAnimIfNotCurrent(move.moveAnimation);
                    move.rtCurrentSpeed = targetSpeed;
                    move.rtCruiseTimer = Mathf.Max(0f, move.rtCruiseTimer - Time.deltaTime);
                    if (move.rtCruiseTimer <= 0f)
                        move.rtStraightPhase = (decelRate > 0f) ? StraightPhase.Decel : StraightPhase.Rest;
                    break;
                }
            case StraightPhase.Decel:
                {
                    PlayAnimIfNotCurrent(move.moveAnimation);
                    if (decelRate <= 0f)
                    {
                        move.rtCurrentSpeed = 0f;
                        move.rtStraightPhase = StraightPhase.Rest;
                    }
                    else
                    {
                        move.rtCurrentSpeed = Mathf.MoveTowards(move.rtCurrentSpeed, 0f, decelRate * Time.deltaTime);
                        if (move.rtCurrentSpeed <= 0.0001f)
                        {
                            move.rtCurrentSpeed = 0f;
                            move.rtStraightPhase = StraightPhase.Rest;
                        }
                    }
                    break;
                }
            case StraightPhase.Rest:
            default:
                {
                    isResting = true;
                    if (move.rtRestTimer <= 0f) move.rtRestTimer = Mathf.Max(0f, move.restDuration);

                    PlayAnimIfNotCurrent(move.restAnimation);

                    rb.velocity = new Vector2(0f, rb.velocity.y);
                    desiredSpeedX = 0f;
                    ApplySlopeIdleStopIfNoMove();

                    move.rtRestTimer -= Time.deltaTime;
                    if (move.rtRestTimer <= 0f)
                    {
                        isResting = false;
                        move.rtStraightPhase = StraightPhase.None;
                        move.rtMoveTimer = 0f;
                        move.rtRestTimer = 0f;
                        move.rtAccelTimer = 0f;
                        move.rtCruiseTimer = 0f;
                        move.rtDecelTimer = 0f;
                        return true; // 本周期完成
                    }
                    return false;
                }
        }

        // 地形探测：无论 allowTurnOnObstacle 与否，都先探测前方墙/悬崖
        bool wallAhead = useMoveDirForProbes ? CheckWallInDir(dirSign) : CheckWallAhead();
        bool cliffAhead = useMoveDirForProbes ? CheckCliffInDir(dirSign) : CheckCliffAhead();

        // 自动转向：仅当允许、且不在自动跳许可区、不在自动跳中、无冷却，并且前方有墙或悬崖
        bool canTurnNow = allowTurnOnObstacle && !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);
        if (canTurnNow && (wallAhead || cliffAhead) && turnCooldown <= 0f)
            TurnAround();

        // 巡逻需要路点锚点；发现不需要
        if (useWaypoints)
            WaypointUpdateAndMaybeTurn(canTurnNow);

        // 写速度（接地沿斜坡投影）
        
        float clampedSpeed = Mathf.Clamp(move.rtCurrentSpeed, 0f, targetSpeed);

        // 不允许转向时的“悬崖停下”策略（仅接地生效）
        bool stopAtCliffEdge = (!allowTurnOnObstacle) && stopAtCliffEdgeWhenNoTurn && cliffAhead && isGroundedMC;

        desiredSpeedX = (stopAtCliffEdge ? 0f : clampedSpeed * dirSign);

        if (isGroundedMC)
            ApplyProjectedVelocityAlongSlope(Mathf.Abs(desiredSpeedX), dirSign);
        else
            rb.velocity = new Vector2(desiredSpeedX, rb.velocity.y);

        return false;
    }

    private void ResetStraightRuntime(PatrolMovement move)
    {
        move.rtStraightPhase = StraightPhase.None;
        move.rtCurrentSpeed = 0f;
        move.rtMoveTimer = 0f;
        move.rtRestTimer = 0f;
        move.rtAccelTimer = 0f;
        move.rtCruiseTimer = 0f;
        move.rtDecelTimer = 0f;
        isResting = false;
    }

    private void AdvanceDiscoveryEvent(bool orderResetIfEmpty = false)
    {
        var dcfg = config.discoveryV2Config;
        if (dcfg == null || dcfg.events == null || dcfg.events.Count == 0)
        {
            if (orderResetIfEmpty) state = MonsterState.Patrol;
            return;
        }

        if (discoveryOrder == null || discoveryOrder.Count != dcfg.events.Count)
            BuildDiscoveryOrderFromConfig();

        if (discoveryOrder.Count <= 0) return;

        discoveryOrderPos = (discoveryOrderPos + 1) % discoveryOrder.Count;
        if (discoveryOrderPos == 0 && dcfg.findRandomOrder && discoveryOrder.Count > 1)
            Shuffle(discoveryOrder);

        activeDiscoveryIndex = discoveryOrder[discoveryOrderPos];
        EnsureActiveDiscoveryEventSetup(resetRuntimes: true);
    }

    private void BuildDiscoveryOrderFromConfig()
    {
        var dcfg = config.discoveryV2Config;
        var list = dcfg?.events;
        discoveryOrder = new List<int>();
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++) discoveryOrder.Add(i);
            if (dcfg.findRandomOrder && discoveryOrder.Count > 1) Shuffle(discoveryOrder);
        }
        discoveryOrderPos = 0;
        if (discoveryOrder.Count > 0) activeDiscoveryIndex = discoveryOrder[0];
    }

    private void EnsureActiveDiscoveryEventSetup(bool resetRuntimes)
    {
        var dcfg = config.discoveryV2Config;
        if (dcfg == null || dcfg.events == null || dcfg.events.Count == 0)
        {
            activeDiscoveryEvent = null;
            return;
        }
        activeDiscoveryIndex = Mathf.Clamp(activeDiscoveryIndex, 0, dcfg.events.Count - 1);
        activeDiscoveryEvent = dcfg.events[activeDiscoveryIndex];

        currentBand = DiscoveryBand.Follow; // 初次进入默认 Follow

        if (resetRuntimes)
        {
            d2_move_follow = d2_move_retreat = d2_move_back = null;
            d2_jump_follow = d2_jump_retreat = d2_jump_back = null;
            isResting = false;
            isJumping = false;
        }
    }

    private void ForceFaceSign(int sign)
    {
        float wantY = (sign >= 0) ? 0f : 180f;
        if (!Mathf.Approximately(transform.rotation.eulerAngles.y, wantY))
            transform.rotation = Quaternion.Euler(0, wantY, 0);
    }

    // ================== 发现阶段：动画特效事件（Find/Back 两套） ==================
    // 注意：这些仅在发现阶段响应，巡逻阶段不会触发

    public void OnFxFindMove()
    {
        PlayDiscoveryMoveFxForBand(DiscoveryBand.Follow, isRest: false);
    }

    public void OnFxFindRest()
    {
        PlayDiscoveryMoveFxForBand(DiscoveryBand.Follow, isRest: true);
    }

    public void OnFxBackMove()
    {
        PlayDiscoveryMoveFxForBand(DiscoveryBand.Backstep, isRest: false);
    }

    public void OnFxBackRest()
    {
        PlayDiscoveryMoveFxForBand(DiscoveryBand.Backstep, isRest: true);
    }

    public void OnFxFindJump()
    {
        PlayDiscoveryJumpFxForBand(DiscoveryBand.Follow, isRest: false);
    }

    public void OnFxFindJumpRest()
    {
        PlayDiscoveryJumpFxForBand(DiscoveryBand.Follow, isRest: true);
    }

    public void OnFxBackJump()
    {
        PlayDiscoveryJumpFxForBand(DiscoveryBand.Backstep, isRest: false);
    }

    public void OnFxBackJumpRest()
    {
        PlayDiscoveryJumpFxForBand(DiscoveryBand.Backstep, isRest: true);
    }

    private void PlayDiscoveryMoveFxForBand(DiscoveryBand band, bool isRest)
    {
        if (state != MonsterState.Discovery) return;
        var m = GetD2MoveFor(band);
        if (m == null) return;

        var prefab = isRest ? m.restEffectPrefab : m.moveEffectPrefab;
        if (prefab == null) return;

        FxSlot slot;
        if (band == DiscoveryBand.Backstep)
            slot = isRest ? FxSlot.BackRest : FxSlot.BackMove;
        else
            slot = isRest ? FxSlot.FindRest : FxSlot.FindMove;

        PlayEffect(prefab, ResolveFxAnchor(slot));
    }

    private void PlayDiscoveryJumpFxForBand(DiscoveryBand band, bool isRest)
    {
        if (state != MonsterState.Discovery) return;
        var j = GetD2JumpFor(band);
        if (j == null) return;

        var prefab = isRest ? j.jumpRestEffectPrefab : j.jumpEffectPrefab;
        if (prefab == null) return;

        FxSlot slot;
        if (band == DiscoveryBand.Backstep)
            slot = isRest ? FxSlot.BackJumpRest : FxSlot.BackJump;
        else
            slot = isRest ? FxSlot.FindJumpRest : FxSlot.FindJump;

        PlayEffect(prefab, ResolveFxAnchor(slot));
    }

    // ================== 出生/巡逻使用的工具方法 ==================
    private void InitWaypointsIfAny()
    {
        var pts = config?.patrolConfig?.patrolPoints;
        if (pts == null || pts.Count == 0) return;

        waypointIndex = 0;
        waypointDir = +1;
        waypointTargetIndex = (pts.Count > 1) ? 1 : 0;

        Vector2 pos = transform.position;
        Vector2 target = pts[waypointTargetIndex];
        int want = (target.x - pos.x >= 0f) ? +1 : -1;
        if (want != FacingSign()) TurnAround();
    }

    private void WaypointUpdateAndMaybeTurn(bool canTurnNow)
    {
        var pts = config?.patrolConfig?.patrolPoints;
        if (pts == null || pts.Count == 0) return;
        if (!isGroundedMC) return;

        int count = pts.Count;
        waypointTargetIndex = Mathf.Clamp(waypointTargetIndex, 0, count - 1);
        waypointIndex = Mathf.Clamp(waypointIndex, 0, count - 1);

        Vector2 pos = transform.position;
        float arriveR = Mathf.Max(0.01f, pointArriveRadius);

        int guard = 0;
        while (guard++ < count)
        {
            Vector2 start = pts[waypointIndex];
            Vector2 target = pts[waypointTargetIndex];

            bool arrived = Vector2.Distance(pos, target) <= arriveR;

            float segDirX = Mathf.Sign(target.x - start.x);
            bool overshot = false;
            if (Mathf.Abs(segDirX) > 0f)
            {
                if (segDirX > 0f) overshot = pos.x >= target.x - arriveR;
                else overshot = pos.x <= target.x + arriveR;
            }

            if (!(arrived || overshot)) break;

            waypointIndex = waypointTargetIndex;
            int next = waypointIndex + waypointDir;
            if (next < 0 || next >= count)
            {
                waypointDir = -waypointDir;
                next = waypointIndex + waypointDir;
            }
            waypointTargetIndex = Mathf.Clamp(next, 0, count - 1);
        }

        Vector2 curTarget = pts[waypointTargetIndex];
        int wantSign = (curTarget.x - pos.x >= 0f) ? +1 : -1;
        if (canTurnNow && turnCooldown <= 0f && wantSign != FacingSign())
            TurnAround();
    }

    bool CheckGrounded()
    {
        if (col == null) return false;
        Physics2D.SyncTransforms();
        return BestVerticalHit(0.12f, true).collider != null;
    }

    // 原基于“面向方向”的墙/悬崖探测
    bool CheckWallAhead()
    {
        if (col == null) return false;
        Bounds b = col.bounds;

        float castY = Mathf.Clamp(b.min.y + wallCheckHeightOffset, b.min.y + 0.05f, b.max.y - 0.05f);
        Vector2 origin = new Vector2(b.center.x, castY);
        Vector2 dir = (Vector2)transform.right;

        float dist = Mathf.Max(0.1f, wallCheckDistance);
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, groundLayer);
        Debug.DrawLine(origin, origin + dir * dist, hit.collider ? Color.red : Color.yellow);

        if (!hit.collider) return false;
        if (hit.collider.isTrigger) return false;
        if (hit.collider.CompareTag(autoJumpPermitTag)) return false;

        Vector2 n = hit.normal.normalized;
        if (n.y >= groundMinNormalYMC) return false;

        return Vector2.Dot(n, dir) < -0.4f;
    }

    bool CheckCliffAhead()
    {
        if (col == null) return false;
        if (!CheckGrounded()) return false;

        Bounds b = col.bounds;
        float baseY = b.min.y + 0.05f;

        const float STEP_DOWN_ALLOW = 0.10f;
        float rayDist = Mathf.Max(cliffCheckDistance, STEP_DOWN_ALLOW + 0.1f);

        Vector2 forward = (Vector2)transform.right;
        Vector2 originToe = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX));
        Vector2 originHalf = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX) * 0.5f);

        bool IsSafe(Vector2 origin)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, rayDist, groundLayer);
            Debug.DrawLine(origin, origin + Vector2.down * rayDist, hit.collider ? Color.green : Color.blue);

            if (!hit.collider) return false;
            if (hit.collider.isTrigger) return true;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return true;

            if (hit.normal.y >= groundMinNormalYMC) return true;

            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        bool safeToe = IsSafe(originToe);
        bool safeHalf = IsSafe(originHalf);

        return !(safeToe || safeHalf);
    }

    // 新增：按“移动方向”的墙/悬崖探测（用于发现阶段 Retreat/Backstep）
    bool CheckWallInDir(int dirSign)
    {
        if (col == null) return false;
        Bounds b = col.bounds;

        float castY = Mathf.Clamp(b.min.y + wallCheckHeightOffset, b.min.y + 0.05f, b.max.y - 0.05f);
        Vector2 origin = new Vector2(b.center.x, castY);
        Vector2 dir = Vector2.right * Mathf.Sign(dirSign);

        float dist = Mathf.Max(0.1f, wallCheckDistance);
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, groundLayer);
        Debug.DrawLine(origin, origin + dir * dist, hit.collider ? Color.magenta : Color.yellow);

        if (!hit.collider) return false;
        if (hit.collider.isTrigger) return false;
        if (hit.collider.CompareTag(autoJumpPermitTag)) return false;

        Vector2 n = hit.normal.normalized;
        if (n.y >= groundMinNormalYMC) return false;

        return Vector2.Dot(n, dir) < -0.4f;
    }

    bool CheckCliffInDir(int dirSign)
    {
        if (col == null) return false;
        if (!CheckGrounded()) return false;

        Bounds b = col.bounds;
        float baseY = b.min.y + 0.05f;

        const float STEP_DOWN_ALLOW = 0.10f;
        float rayDist = Mathf.Max(cliffCheckDistance, STEP_DOWN_ALLOW + 0.1f);

        Vector2 forward = Vector2.right * Mathf.Sign(dirSign);
        Vector2 originToe = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX));
        Vector2 originHalf = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX) * 0.5f);

        bool IsSafe(Vector2 origin)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, rayDist, groundLayer);
            Debug.DrawLine(origin, origin + Vector2.down * rayDist, hit.collider ? Color.cyan : Color.blue);

            if (!hit.collider) return false;
            if (hit.collider.isTrigger) return true;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return true;

            if (hit.normal.y >= groundMinNormalYMC) return true;

            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        bool safeToe = IsSafe(originToe);
        bool safeHalf = IsSafe(originHalf);

        return !(safeToe || safeHalf);
    }

    void TurnAround()
    {
        if (turnCooldown > 0f) return;
        float newY = (transform.rotation.eulerAngles.y == 0f) ? 180f : 0f;
        transform.rotation = Quaternion.Euler(0, newY, 0);
        turnCooldown = 0.25f;
    }

    // 跳跃起始
    void BeginOneJump(PatrolMovement move, bool useAutoParams, int? moveDirOverride = null)
    {
        if (!useAutoParams && !CheckGrounded()) return;
        if (isJumping) { if (!useAutoParams) return; }

        isJumping = true;
        isAutoJumping = useAutoParams;
        activeJumpMove = move;

        float spdX, hY, g;
        if (useAutoParams)
        {
            spdX = Mathf.Max(0f, move.autojumpSpeed);
            hY = Mathf.Max(0.01f, move.autojumpHeight);
            g = Mathf.Max(0.01f, move.autogravityScale) * BASE_G;
        }
        else
        {
            spdX = Mathf.Max(0f, move.jumpSpeed);
            hY = Mathf.Max(0.01f, move.jumpHeight);
            g = Mathf.Max(0.01f, move.gravityScale) * BASE_G;
        }

        vY = Mathf.Sqrt(Mathf.Max(0.01f, 2f * hY * g));
        transform.position += Vector3.up * 0.05f;

        if (!string.IsNullOrEmpty(move.jumpAnimation))
        {
            if (useAutoParams)
            {
                animator.CrossFadeInFixedTime(move.jumpAnimation, 0f, 0, 0f);
                animator.Update(0f);
            }
            else
            {
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName(move.jumpAnimation))
                    PlayAnimIfNotCurrent(move.jumpAnimation);
            }
        }

        // 记录本次跳跃的“移动方向”，与面向解耦（支持倒退跳跃）
        currentJumpDirSign = moveDirOverride.HasValue ? System.Math.Sign(moveDirOverride.Value) : FacingSign();

        float vx = spdX * currentJumpDirSign;
        rb.velocity = new Vector2(vx, vY);

        move.rtUsingAutoJumpParams = useAutoParams;
    }

    // 跳跃更新
    void JumpUpdate(PatrolMovement move)
    {
        float spdX = move.rtUsingAutoJumpParams ? Mathf.Max(move.autojumpSpeed, 0f)
                                            : Mathf.Max(move.jumpSpeed, 0f);

        // 修改：仅在普通跳跃时允许空中撞墙调头；自动跳跃禁用这个能力
        if (!move.rtUsingAutoJumpParams && turnCooldown <= 0f && CheckWallInDir(currentJumpDirSign))
        {
            TurnAround();
            currentJumpDirSign = -currentJumpDirSign;
            turnCooldown = 0.25f;
        }

        float g = (move.rtUsingAutoJumpParams ? Mathf.Max(0.01f, move.autogravityScale)
                                              : Mathf.Max(0.01f, move.gravityScale)) * BASE_G;

        vY -= g * Time.deltaTime;

        SafeMoveVertical(vY * Time.deltaTime, groundLayer);

        rb.velocity = new Vector2(spdX * currentJumpDirSign, rb.velocity.y);

        if (groundedAfterVerticalMove && vY <= 0f)
        {
            vY = 0f;
            isJumping = false;

            rb.velocity = new Vector2(rb.velocity.x, 0f);
            suppressUpwardOnGroundFrames = 2;

            if (move.rtUsingAutoJumpParams)
            {
                if (move.rtExecuteRemain > 0)
                    move.rtExecuteRemain = Mathf.Max(0, move.rtExecuteRemain - 1);

                if (move.rtExecuteRemain > 0)
                {
                    BeginOneJump(move, useAutoParams: true, moveDirOverride: currentJumpDirSign);
                    return;
                }

                isAutoJumping = false;
                if (move.restDuration > 0f)
                {
                    isResting = true;
                    inJumpRestPhase = true;
                    move.rtRestTimer = move.restDuration;
                }
                else
                {
                    isResting = false;
                    inJumpRestPhase = false;
                }
                ignoreCliffFramesLeft = Mathf.Max(0, autoJumpIgnoreCliffFrames);
                move.rtUsingAutoJumpParams = false;
            }
            else
            {
                move.rtExecuteRemain = 0;
                isResting = true;
                inJumpRestPhase = true;

                if (!string.IsNullOrEmpty(move.jumpRestAnimation) &&
                    !animator.GetCurrentAnimatorStateInfo(0).IsName(move.jumpRestAnimation))
                    PlayAnimIfNotCurrent(move.jumpRestAnimation);

                move.rtRestTimer = move.jumpRestDuration;
                move.rtUsingAutoJumpParams = false;
            }
        }
    }

    private void SafeMoveVertical(float dy, LayerMask groundMask)
    {
        groundedAfterVerticalMove = false;
        if (Mathf.Approximately(dy, 0f) || col == null) return;

        float rayLen = Mathf.Abs(dy) + SKIN;
        bool down = dy < 0f;

        var hit = BestVerticalHit(rayLen, down);
        if (hit.collider)
        {
            float allowed = Mathf.Max(0f, hit.distance - SKIN);
            float sign = Mathf.Sign(dy);
            float applied = Mathf.Min(Mathf.Abs(dy), allowed) * sign;

            transform.Translate(Vector3.up * applied, Space.World);

            if (down && allowed <= Mathf.Abs(dy))
                groundedAfterVerticalMove = true;
        }
        else
        {
            transform.Translate(Vector3.up * dy, Space.World);
        }
    }

    private RaycastHit2D BestVerticalHit(float rayLen, bool down)
    {
        Bounds b = col.bounds;

        Vector2 left, center, right, dir;

        if (down)
        {
            left = new Vector2(b.min.x + 0.05f, b.min.y + 0.02f);
            center = new Vector2(b.center.x, b.min.y + 0.02f);
            right = new Vector2(b.max.x - 0.05f, b.min.y + 0.02f);
            dir = Vector2.down;
        }
        else
        {
            left = new Vector2(b.min.x + 0.05f, b.max.y - 0.02f);
            center = new Vector2(b.center.x, b.max.y - 0.02f);
            right = new Vector2(b.max.x - 0.05f, b.max.y - 0.02f);
            dir = Vector2.up;
        }

        var hitL = Physics2D.Raycast(left, dir, rayLen, groundLayer);
        var hitC = Physics2D.Raycast(center, dir, rayLen, groundLayer);
        var hitR = Physics2D.Raycast(right, dir, rayLen, groundLayer);

        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        if (hitL.collider && hitL.distance < bestDist) { best = hitL; bestDist = hitL.distance; }
        if (hitC.collider && hitC.distance < bestDist) { best = hitC; bestDist = hitL.distance; }
        if (hitR.collider && hitR.distance < bestDist) { best = hitR; bestDist = hitL.distance; }
        return best;
    }

    private readonly ContactPoint2D[] _cpBufMC = new ContactPoint2D[8];
    private void UpdateGroundedAndSlope()
    {
        groundNormalMC = Vector2.up;
        isGroundedMC = false;

        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(groundLayer);
        int cnt = rb.GetContacts(filter, _cpBufMC);

        float bestNy = -1f;
        for (int i = 0; i < cnt; i++)
        {
            var n = _cpBufMC[i].normal;
            if (n.y >= groundMinNormalYMC)
            {
                isGroundedMC = true;
                if (n.y > bestNy) { bestNy = n.y; groundNormalMC = n; }
            }
        }

        // 非跳跃且临时失地：向下形状投射吸附
        if (!isGroundedMC && !isJumping && col != null)
        {
            const float snapDist = 0.12f;
            var snapFilter = new ContactFilter2D { useTriggers = false };
            snapFilter.SetLayerMask(groundLayer);

            RaycastHit2D[] hits = new RaycastHit2D[6];
            int hitCount = rb.Cast(Vector2.down, snapFilter, hits, snapDist);

            int pick = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var h = hits[i];
                if (!h.collider) continue;
                if (h.normal.y < groundMinNormalYMC) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    pick = i;
                }
            }

            if (pick >= 0)
            {
                if (rb.velocity.y <= 0.05f)
                {
                    float gap = Mathf.Max(0f, bestDist - 0.005f);
                    if (gap > 0f)
                        rb.position += Vector2.down * gap;
                }

                isGroundedMC = true;
                groundNormalMC = hits[pick].normal;
            }
        }
    }

    private void ApplyProjectedVelocityAlongSlope(float speedAbs, int dirSign)
    {
        if (!isGroundedMC)
        {
            rb.velocity = new Vector2(speedAbs * dirSign, rb.velocity.y);
            return;
        }

        Vector2 n = groundNormalMC.normalized;
        Vector2 t = new Vector2(n.y, -n.x).normalized;

        Vector2 v = t * (speedAbs * dirSign);

        const float stickDown = 0.005f;
        v += -n * stickDown;

        if (v.y > 0f) v.y = 0f;

        float addUp = (suppressUpwardOnGroundFrames > 0) ? 0f : Mathf.Max(rb.velocity.y, 0f);
        rb.velocity = new Vector2(v.x, v.y + addUp);

        if (suppressUpwardOnGroundFrames > 0) suppressUpwardOnGroundFrames--;
    }

    private void ApplySlopeIdleStopIfNoMove()
    {
        if (!isGroundedMC)
        {
            ExitSlopeIdleLockMC();
            return;
        }

        bool onSlope = groundNormalMC.y < 0.999f;

        var v = rb.velocity;
        if (v.y < slopeStopVyThresholdMC) v.y = 0f;

        if (slopeIdleFreezeGravityMC && onSlope && Mathf.Abs(desiredSpeedX) < 0.01f)
        {
            if (!slopeIdleLockedMC)
            {
                if (rb.velocity.sqrMagnitude <= slopeEnterIdleSpeedEpsilonMC * slopeEnterIdleSpeedEpsilonMC)
                {
                    slopeSavedGravityMC = rb.gravityScale;
                    rb.gravityScale = 0f;
                    slopeIdleLockedMC = true;
                }
            }

            if (slopeIdleLockedMC)
            {
                Vector2 n = groundNormalMC.normalized;
                Vector2 t = new Vector2(n.y, -n.x).normalized;
                float vTan = Vector2.Dot(v, t);
                v -= vTan * t;
                if (v.y > 0f) v.y = 0f;
                rb.velocity = v;
                return;
            }
        }

        {
            Vector2 n = groundNormalMC.normalized;
            Vector2 t = new Vector2(n.y, -n.x).normalized;
            float vTan = Vector2.Dot(v, t);
            if (Mathf.Abs(vTan) < horizontalStopThresholdOnSlopeMC) v -= vTan * t;
            rb.velocity = v;
        }

        ExitSlopeIdleLockMC();
    }

    private void ExitSlopeIdleLockMC()
    {
        if (!slopeIdleLockedMC) return;
        slopeIdleLockedMC = false;
        rb.gravityScale = slopeSavedGravityMC;
    }

    private IEnumerator WaitForStateFinished(string stateName, int layer = 0)
    {
        if (string.IsNullOrEmpty(stateName)) yield break;

        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (info.IsName(stateName)) break;
            yield return null;
        }

        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (!info.IsName(stateName)) break;
            if (info.normalizedTime >= 1f) break;
            yield return null;
        }
    }

    private float ComputeAirTime(bool useAutoParams, PatrolMovement move)
    {
        float g = (useAutoParams ? Mathf.Max(0.01f, move.autogravityScale)
                                 : Mathf.Max(0.01f, move.gravityScale)) * BASE_G;
        float h = useAutoParams ? Mathf.Max(0.01f, move.autojumpHeight)
                                : Mathf.Max(0.01f, move.jumpHeight);
        float v0 = Mathf.Sqrt(Mathf.Max(0.01f, 2f * h * g));
        return 2f * v0 / g;
    }

    private void EnsureJumpPlan(PatrolMovement move, bool useAutoParams)
    {
        if (move.rtExecuteRemain > 0) return;

        float budget = useAutoParams
            ? Mathf.Max(0f, move.automoveDuration)
            : Mathf.Max(0f, move.jumpDuration);

        if (budget > 0f)
        {
            float T = Mathf.Max(0.01f, ComputeAirTime(useAutoParams, move));
            move.rtExecuteRemain = Mathf.Max(1, Mathf.CeilToInt(budget / T));
        }
        else
        {
            move.rtExecuteRemain = 1;
        }
    }

    private void PlayAnimIfNotCurrent(string animName)
    {
        if (string.IsNullOrEmpty(animName)) return;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(animName))
        {
            animator.CrossFadeInFixedTime(animName, 0f, 0, 0f);
            animator.Update(0f);
        }
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);

            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void BuildMoveOrderFromConfig()
    {
        moveOrder = new List<int>();
        int count = (patrolRuntimeMoves != null) ? patrolRuntimeMoves.Count : 0;
        for (int i = 0; i < count; i++) moveOrder.Add(i);
        moveOrderPos = 0;

        bool rnd = (config?.patrolConfig != null) && (bool)config.patrolConfig.randomOrder;
        if (rnd && count > 1) Shuffle(moveOrder);

        patrolIndex = (moveOrder.Count > 0) ? moveOrder[0] : 0;
    }

    private void AdvancePatrolIndex()
    {
        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        if (moveOrder == null || moveOrder.Count != patrolRuntimeMoves.Count)
            BuildMoveOrderFromConfig();

        if (moveOrder.Count <= 1)
        {
            patrolIndex = moveOrder[0];
            return;
        }

        moveOrderPos = (moveOrderPos + 1) % moveOrder.Count;

        bool rnd = (config?.patrolConfig != null) && (bool)config.patrolConfig.randomOrder;
        if (moveOrderPos == 0 && rnd && moveOrder.Count > 1)
            Shuffle(moveOrder);

        patrolIndex = moveOrder[moveOrderPos];
    }

    // 动画特效关键帧（只在 Patrol 阶段生效）
    private bool IsCurrentState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName) || animator == null) return false;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        return st.IsName(stateName);
    }

    public void OnFxSpawn()
    {
        var sc = config?.spawnConfig;
        if (state == MonsterState.Patrol || sc == null) return;
        if (IsCurrentState(sc.spawnAnimation) && sc.spawnEffectPrefab)
            PlayEffect(sc.spawnEffectPrefab, ResolveFxAnchor(FxSlot.Spawn));
    }

    public void OnFxIdle()
    {
        var sc = config?.spawnConfig;
        if (state == MonsterState.Patrol || sc == null) return;
        if (IsCurrentState(sc.idleAnimation) && sc.idleEffectPrefab)
            PlayEffect(sc.idleEffectPrefab, ResolveFxAnchor(FxSlot.Idle));
    }

    // 巡逻阶段特效（仍旧只在 Patrol 生效）
    public void OnFxMove()
    {
        if (state != MonsterState.Patrol) return;
        var m = activeStraightMove;
        if (m == null) return;
        if (isJumping) return;
        if (isResting) return;
        if (IsCurrentState(m.moveAnimation) && m.moveEffectPrefab)
            PlayEffect(m.moveEffectPrefab, ResolveFxAnchor(FxSlot.Move));
    }

    public void OnFxRest()
    {
        if (state != MonsterState.Patrol) return;
        var m = activeStraightMove;
        if (m == null) return;
        if (isJumping) return;
        if (!isResting) return;
        if (IsCurrentState(m.restAnimation) && m.restEffectPrefab)
            PlayEffect(m.restEffectPrefab, ResolveFxAnchor(FxSlot.Rest));
    }

    public void OnFxJump()
    {
        if (state != MonsterState.Patrol) return;
        var m = activeJumpMove;
        if (m == null) return;
        if (!isJumping) return;
        if (IsCurrentState(m.jumpAnimation) && m.jumpEffectPrefab)
            PlayEffect(m.jumpEffectPrefab, ResolveFxAnchor(FxSlot.Jump));
    }

    public void OnFxJumpRest()
    {
        if (state != MonsterState.Patrol) return;
        if (isJumping) return;
        if (!isResting) return;
        if (!inJumpRestPhase) return;

        var m = activeJumpMove;
        if (m == null) return;

        if (IsCurrentState(m.jumpRestAnimation) && m.jumpRestEffectPrefab)
            PlayEffect(m.jumpRestEffectPrefab, ResolveFxAnchor(FxSlot.JumpRest));
    }

    // 用严格版本替换 ResolveFxAnchor（不再回退到巡逻/通用）
    private Transform ResolveFxAnchor(FxSlot slot)
    {
        switch (slot)
        {
            case FxSlot.Spawn: return fxSpawnPoint;
            case FxSlot.Idle: return fxIdlePoint;
            case FxSlot.Move: return fxMovePoint;
            case FxSlot.Rest: return fxRestPoint;
            case FxSlot.Jump: return fxJumpPoint;
            case FxSlot.JumpRest: return fxJumpRestPoint;

            case FxSlot.FindMove: return fxFindMovePoint;
            case FxSlot.FindRest: return fxFindRestPoint;
            case FxSlot.FindJump: return fxFindJumpPoint;
            case FxSlot.FindJumpRest: return fxFindJumpRestPoint;

            case FxSlot.BackMove: return fxBackMovePoint;
            case FxSlot.BackRest: return fxBackRestPoint;
            case FxSlot.BackJump: return fxBackJumpPoint;
            case FxSlot.BackJumpRest: return fxBackJumpRestPoint;

            default: return null;
        }
    }

    // 用严格版本替换 PlayEffect（无锚点就不播）
    private void PlayEffect(GameObject prefab, Transform anchor)
    {
        if (prefab == null) return;
        if (anchor == null) return;

        Transform parent = anchor;
        Vector3 pos = parent.position;
        Quaternion rot = parent.rotation;

        GameObject fx = Instantiate(prefab, pos, rot, parent);

        var ps = fx.GetComponentInChildren<ParticleSystem>(true);
        if (ps)
        {
            ps.Play();
            Destroy(fx, ps.main.duration + 0.1f);
        }
        else
        {
            Destroy(fx, 2f);
        }
    }

    // ================== 触发器：AutoJump ==================
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(autoJumpPermitTag)) return;

        inAutoJumpPermitZone = true;

        if (!autoJumpReady || ignoreCliffFramesLeft > 0) return;
        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        var move = patrolRuntimeMoves[patrolIndex];

        EnsureJumpPlan(move, useAutoParams: true);
        BeginOneJump(move, useAutoParams: true);

        autoJumpReady = false;
        autoJumpRearmAfterLanding = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(autoJumpPermitTag))
        {
            inAutoJumpPermitZone = false;
        }
    }

    // 用 XY 平面画圆
    void OnDrawGizmosSelected()
    {
        var dcfg2 = config?.discoveryV2Config;
        if (dcfg2 == null) return;

        // 统一用距离锚点（若未配置则回退 transform）
        Vector3 pos = monsterDistPoint ? monsterDistPoint.position : transform.position;

#if UNITY_EDITOR
        // 可视化辅助：在圆心处标一个小十字和标签，方便确认是否真的是 monsterDistPoint
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.DrawLine(pos + Vector3.left * 0.1f, pos + Vector3.right * 0.1f);
        UnityEditor.Handles.DrawLine(pos + Vector3.down * 0.1f, pos + Vector3.up * 0.1f);
        UnityEditor.Handles.Label(pos + Vector3.up * 0.15f, monsterDistPoint ? "monsterDistPoint" : "transform");
#endif

        Gizmos.color = Color.red; DrawCircleXY(pos, dcfg2.findRange);
        Gizmos.color = Color.white; DrawCircleXY(pos, dcfg2.reverseRange);
        Gizmos.color = Color.black; DrawCircleXY(pos, dcfg2.backRange);
    }

    private void DrawCircleXY(Vector3 center, float radius, int segments = 64)
    {
        if (radius <= 0f) return;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float ang = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 cur = center + new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }

    // 进入发现阶段时，如果当前档位的发现动画都为空，则回退到 Idle（避免沿用巡逻动画）
    private void MaybeFallbackToIdleIfNoDiscoveryAnims()
    {
        var m = GetD2MoveFor(currentBand);
        bool noMoveAnims = (m == null) || (string.IsNullOrEmpty(m.moveAnimation) && string.IsNullOrEmpty(m.restAnimation));

        var j = GetD2JumpFor(currentBand);
        bool noJumpAnims = (j == null) || (string.IsNullOrEmpty(j.jumpAnimation) && string.IsNullOrEmpty(j.jumpRestAnimation));

        if (noMoveAnims && noJumpAnims)
        {
            var idle = config?.spawnConfig?.idleAnimation;
            if (!string.IsNullOrEmpty(idle))
                PlayAnimIfNotCurrent(idle);
        }
    }

    private void OnValidate()
    {
        if (config != null && config.discoveryV2Config != null)
        {
            var d = config.discoveryV2Config;

            // 非负约束
            d.findRange = Mathf.Max(0f, d.findRange);
            d.reverseRange = Mathf.Max(0f, d.reverseRange);
            d.backRange = Mathf.Max(0f, d.backRange);

            // 迁移旧的 allowObstacleAutoTurn -> 新的 obstacleTurnMode
            if (d.events != null)
            {
                foreach (var ev in d.events)
                {
                    // 仅当枚举仍是默认值且存在旧字段语义时，才做一次性映射
                    if (ev != null)
                    {
                        if (ev.obstacleTurnMode == ObstacleTurnMode.AutoTurn && ev.allowObstacleAutoTurnLegacy == false)
                        {
                            ev.obstacleTurnMode = ObstacleTurnMode.NoTurnStopAtCliff;
                        }
                        // 标记成 true，避免每次 OnValidate 都把手工设置覆盖
                        ev.allowObstacleAutoTurnLegacy = true;
                    }
                }
            }
        }
    }
    private Vector2 GetMonsterDistPos()
    {
        return monsterDistPoint ? (Vector2)monsterDistPoint.position : (Vector2)transform.position;
    }

    private Vector2 GetPlayerDistPos()
    {
        if (!player) return (Vector2)transform.position;
        return playerDistPoint ? (Vector2)playerDistPoint.position : (Vector2)player.position;
    }

    // 2) 新增：双射线判定并触发一次 AutoJump；返回 true 表示本帧已起跳
    // 改：双射线自动跳检测增加方向参数（dirSign：+1/-1）
    private bool MaybeAutoJumpOverLowObstacle(int dirSign)
    {
        if (!enableForwardGapAutoJump) return false;
        if (isJumping || !isGroundedMC) return false;
        if (ignoreCliffFramesLeft > 0) return false;
        if (!autoJumpReady) return false;

        if (col == null) return false;
        Bounds b = col.bounds;

        int sign = (dirSign >= 0) ? +1 : -1;   
        Vector2 forward = Vector2.right * sign;

        // 选取脚部起点：优先 groundPoint；否则用碰撞框脚边前沿
        Vector2 lowerOrigin;
        if (groundPoint)
        {
            lowerOrigin = (Vector2)groundPoint.position + forward * Mathf.Max(0f, forwardToeOffset);
        }
        else
        {
            float toeX = b.center.x + sign * (b.extents.x + Mathf.Max(0f, forwardToeOffset));
            float footY = Mathf.Clamp(b.min.y + 0.05f, b.min.y, b.max.y);
            lowerOrigin = new Vector2(toeX, footY);
        }

        // 头顶水平射线起点：头顶 + 抬升（默认用 autojumpHeight）
        float topY = b.max.y + 0.02f;
        float useAutoHeight = 0.8f;
        if (patrolRuntimeMoves != null && patrolRuntimeMoves.Count > 0)
            useAutoHeight = Mathf.Max(0.05f, patrolRuntimeMoves[Mathf.Clamp(patrolIndex, 0, patrolRuntimeMoves.Count - 1)].autojumpHeight);
        float raise = (upperHeightAdd > 0f) ? upperHeightAdd : useAutoHeight;

        Vector2 upperOrigin = new Vector2(lowerOrigin.x, topY + raise);

        // 脚部水平射线：近距离必须命中“墙/陡坡”
        var hitLow = Physics2D.Raycast(lowerOrigin, forward, Mathf.Max(0.05f, lowerRayLen), groundLayer);
        Debug.DrawLine(lowerOrigin, lowerOrigin + forward * Mathf.Max(0.05f, lowerRayLen), hitLow.collider ? Color.red : Color.yellow);

        bool lowBlocked = false;
        if (hitLow.collider && !hitLow.collider.isTrigger && !hitLow.collider.CompareTag(autoJumpPermitTag))
        {
            Vector2 n = hitLow.normal.normalized;
            if (n.y < groundMinNormalYMC && hitLow.distance <= Mathf.Max(0.03f, lowerHitMax))
                lowBlocked = true;
        }
        if (!lowBlocked) return false;

        // 头顶水平射线：应通畅（不命中实心阻挡）
        var hitUp = Physics2D.Raycast(upperOrigin, forward, Mathf.Max(0.05f, upperRayLen), groundLayer);
        Debug.DrawLine(upperOrigin, upperOrigin + forward * Mathf.Max(0.05f, upperRayLen), hitUp.collider ? Color.magenta : Color.green);
        if (hitUp.collider && !hitUp.collider.isTrigger && !hitUp.collider.CompareTag(autoJumpPermitTag))
            return false;

        // 取 AutoJump 参数
        float useAutoSpeed = 2.0f, useAutoGScale = 1.0f;
        if (patrolRuntimeMoves != null && patrolRuntimeMoves.Count > 0)
        {
            var baseMove = patrolRuntimeMoves[Mathf.Clamp(patrolIndex, 0, patrolRuntimeMoves.Count - 1)];
            useAutoSpeed = Mathf.Max(0.01f, baseMove.autojumpSpeed);
            useAutoHeight = Mathf.Max(0.05f, baseMove.autojumpHeight);
            useAutoGScale = Mathf.Max(0.01f, baseMove.autogravityScale);
        }

        var temp = new PatrolMovement
        {
            type = MovementType.Jump,
            autojumpSpeed = useAutoSpeed,
            autojumpHeight = useAutoHeight,
            autogravityScale = useAutoGScale,
            automoveDuration = 0f,
            autorestDuration = 0f
        };

        EnsureJumpPlan(temp, useAutoParams: true);
        BeginOneJump(temp, useAutoParams: true, moveDirOverride: sign);

        autoJumpReady = false;
        autoJumpRearmAfterLanding = true;

        return true;
    }
}