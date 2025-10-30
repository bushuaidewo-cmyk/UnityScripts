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

    [Header("AutoJump FX 冷却")]
    [SerializeField, Tooltip("开启后：自动跳跃的跳跃FX（动画事件）进入冷却，同一段时间只播一次")]
    private bool enableAutoJumpFxCooldown = true;

    // 冷却计时（帧）
    private int autoJumpFxCooldownLeft = 0;
    // 本次空中（自动跳）是否已播放过FX
    private bool airJumpFxPlayedThisAir = false;
    // 小工具：是否处于“自动跳跃”状态
    private bool IsAutoJumpNow()
    {
        return isAutoJumping || (activeJumpMove != null && activeJumpMove.rtUsingAutoJumpParams);
    }
    // 小工具：当是“自动跳跃”时，尝试消费一次FX播放票据；成功返回true并开启冷却，失败则拒绝本次播放
    private bool TryConsumeAutoJumpFxTicket()
    {
        if (!enableAutoJumpFxCooldown) return true;

        // 认为是自动跳（两种判断都兼容）
        bool isAuto = isAutoJumping || (activeJumpMove != null && activeJumpMove.rtUsingAutoJumpParams);
        if (!isAuto) return true;

        if (autoJumpFxCooldownLeft > 0)
            return false;

        autoJumpFxCooldownLeft = Mathf.Max(1, autoJumpIgnoreCliffFrames);
        return true;
    }

    private enum FxSlot
    {
        Spawn, Idle, Move, Rest, Jump, JumpRest,
        FindMove, FindRest, FindJump, FindJumpRest
    }

    // ================== 发现 V2 运行时字段 ==================
    private enum DiscoveryBand { Follow, Retreat, Backstep }
    private List<int> discoveryOrder = null;
    private int discoveryOrderPos = 0;
    private int activeDiscoveryIndex = 0;
    private DiscoveryEventV2 activeDiscoveryEvent = null;
    private DiscoveryBand currentBand = DiscoveryBand.Follow;
    private bool discoveryRestJustFinished = false;

    // 新增：发现-跳跃的“生命周期标记”和“起跳时档位快照”
    private bool discoveryJumpActive = false;
    private DiscoveryBand bandAtJumpStart = DiscoveryBand.Follow;

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

    //AutoTurn 后的朝向/移动方向冻结
    [Header("发现阶段：自动转向后的朝向冻结")]
    [SerializeField, Tooltip("AutoTurn 触发后，强制保持当前朝向/移动方向的时间（秒）")]
    private float obstacleTurnFaceLockTime = 0.40f;

    // 发现阶段：落地后的朝向冻结
    [Header("发现阶段：落地后的朝向冻结")]
    [SerializeField, Tooltip("落地后保持当前朝向/移动方向的时间（秒）")]
    private float postLandingFaceLockTime = 0.35f;
    private float postLandingFaceLockTimer = 0f;

    // AutoTurn 后朝向冻结计时器（秒）
    private float obstacleTurnFaceLockTimer = 0f;

    // 由 StraightTickCommon 标记“本帧因障碍发生过 AutoTurn”
    private bool obstacleTurnedThisFrame = false;

    // 跳跃：记录“移动方向”（与面向解耦，用于倒退跳跃等）
    private int currentJumpDirSign = +1;

    // 在已有的“发现阶段朝向抖动抑制”字段附近追加以下字段
    [SerializeField, Tooltip("朝向翻转的最小驻留时间（秒），在此时间内不再允许翻转")]
    private float faceFlipMinDwellTime = 0.40f;

    [SerializeField, Tooltip("越过死区后，还需额外距离才允许翻转（米），避免在边界附近来回抖动")]
    private float faceFlipHysteresis = 0.20f;

    // 运行时：朝向翻转驻留计时器
    private float faceFlipDwellTimer = 0f;

    // 跳跃期间是否锁定朝向（从起跳到落地保持不变）
    [SerializeField, Tooltip("跳跃期间是否锁定朝向（从起跳到落地保持不变）")]
    private bool lockFaceWhileAirborne = true;

    // 空中锁定的面向（+1 / -1，0 表示未锁）
    private int faceSignLockedDuringAir = 0;

    [Header("发现阶段：自动跳后抑制一次常规跳")]
    [SerializeField, Tooltip("开启后：自动跳落地后抑制下一次发现阶段的常规跳跃")]
    private bool suppressOneDiscoveryJumpAfterAuto = true;
    private bool suppressNextDiscoveryJumpOnce = false;

    //后退/倒退检测抑制计时器（秒）
    private float backBandSuppressTimer = 0f;


    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError($"[MonsterController] {name} 未找到 Rigidbody2D!");
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

    //Update() 内递减计时器
    void Update()
    {
        if (turnCooldown > 0f) turnCooldown -= Time.deltaTime;
        if (bandDwellTimer > 0f) bandDwellTimer -= Time.deltaTime;

        if (ignoreCliffFramesLeft > 0) ignoreCliffFramesLeft--;
        if (autoJumpFxCooldownLeft > 0) autoJumpFxCooldownLeft--;

        if (obstacleTurnFaceLockTimer > 0f) obstacleTurnFaceLockTimer -= Time.deltaTime;
        if (postLandingFaceLockTimer > 0f) postLandingFaceLockTimer -= Time.deltaTime;
        if (faceFlipDwellTimer > 0f) faceFlipDwellTimer -= Time.deltaTime;

        // 后退/倒退距离抑制计时器递减
        if (backBandSuppressTimer > 0f) backBandSuppressTimer -= Time.deltaTime;

        // 若配置关闭，确保不生效
        var dcfgU = config != null ? config.discoveryV2Config : null;
        if (dcfgU != null && !dcfgU.suppressBackBandDuringRest)
            backBandSuppressTimer = 0f;


        if (autoJumpRearmAfterLanding && !isJumping && !inAutoJumpPermitZone && ignoreCliffFramesLeft <= 0)
        {
            autoJumpReady = true;
            autoJumpRearmAfterLanding = false;
        }
    }

    IEnumerator StateMachine()
    {
        // 出生
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
        UpdateGroundedAndSlope();

        if (MaybeAutoJumpOverLowObstacle(FacingSign())) return;

        var dcfg = config.discoveryV2Config;
        if (player && dcfg != null)
        {
            float dist = discoveryUseHorizontalDistanceOnly
                ? Mathf.Abs(GetPlayerDistPos().x - GetMonsterDistPos().x)
                : Vector2.Distance(GetPlayerDistPos(), GetMonsterDistPos());
            if (dist <= Mathf.Max(0f, dcfg.findRange))
            {
                state = MonsterState.Discovery;

                bool reset = !(isJumping || isAutoJumping);
                EnsureActiveDiscoveryEventSetup(resetRuntimes: reset);

                MaybeFallbackToIdleIfNoDiscoveryAnims();

                return;
            }
        }

        if (isJumping && activeJumpMove != null)
        {
            JumpUpdate(activeJumpMove);
            return;
        }

        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        PatrolMovement move = patrolRuntimeMoves[patrolIndex];

        if (!isResting && move.type == MovementType.Straight)
        {
            activeStraightMove = move;

            bool finished = StraightTickCommon(move, dirSign: FacingSign(), useWaypoints: true);

            if (finished)
                AdvancePatrolIndex();

            return;
        }

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

            float restLeft = (move.rtRestTimer > 0f) ? move.rtRestTimer : (move.type == MovementType.Jump ? PickJumpRestTime(move) : PickStraightRestTime(move));

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

    // DiscoveryUpdate() 顶部 wantBand 计算处：在算出 wantBand 之后，若计时器>0，强制Follow
    void DiscoveryUpdate()
    {
        discoveryRestJustFinished = false;

        if (!player) { state = MonsterState.Patrol; return; }

        var dcfg = config.discoveryV2Config;
        if (dcfg == null || dcfg.events == null || dcfg.events.Count == 0) { state = MonsterState.Patrol; return; }

        UpdateGroundedAndSlope();

        var p = GetPlayerDistPos();
        var m = GetMonsterDistPos();
        float d = discoveryUseHorizontalDistanceOnly ? Mathf.Abs(p.x - m.x) : Vector2.Distance(p, m);

        if (d > dcfg.findRange) { state = MonsterState.Patrol; return; }

        // 带选择
        DiscoveryBand wantBand = ComputeWantBandWithHysteresis(d, dcfg);

        // 仅当开关开启时才抑制 Back/Retreat 检测
        if (dcfg.suppressBackBandDuringRest && backBandSuppressTimer > 0f && wantBand != DiscoveryBand.Follow)
            wantBand = DiscoveryBand.Follow;


        bool bandChangeAllowedNow = !(activeDiscoveryEvent?.mode == DiscoveryV2Mode.Jump && isJumping)
                                    && !isResting && (bandDwellTimer <= 0f) && (currentBand != wantBand);

        if (bandChangeAllowedNow)
        {
            currentBand = wantBand;
            if (activeDiscoveryEvent != null && activeDiscoveryEvent.mode == DiscoveryV2Mode.Move && !isResting)
            {
                var moveForNewBand = GetD2MoveFor(currentBand);
                ResetStraightRuntime(moveForNewBand);
            }
            bandDwellTimer = Mathf.Max(0.05f, bandMinDwellTime);
        }

        float dxToPlayer = p.x - m.x;
        int dirToPlayer = (Mathf.Abs(dxToPlayer) <= faceFlipDeadZone) ? FacingSign() : (dxToPlayer >= 0f ? +1 : -1);

        // 抑制窗口期间，重叠区间内冻结 dirToPlayer 为当前朝向，避免快速左右闪
        if (dcfg.suppressBackBandDuringRest && backBandSuppressTimer > 0f)
        {
            // 锁定区间：基于现有死区与滞回之和（可按需调大一点）
            float lockZone = Mathf.Max(faceFlipDeadZone, 0.15f) + Mathf.Max(0f, faceFlipHysteresis);
            if (Mathf.Abs(dxToPlayer) <= lockZone)
            {
                dirToPlayer = FacingSign();
            }
        }

        if (activeDiscoveryEvent == null)
        {
            AdvanceDiscoveryEvent(orderResetIfEmpty: true);
            if (activeDiscoveryEvent == null) { state = MonsterState.Patrol; return; }
        }

        if (activeDiscoveryEvent.mode == DiscoveryV2Mode.Move)
        {
            if (isJumping && activeJumpMove != null)
            {
                JumpUpdate(activeJumpMove);

                return;
            }

            var move = GetD2MoveFor(currentBand);

            bool inFaceLock = obstacleTurnFaceLockTimer > 0f;

            int faceSign, moveSign;
            if (inFaceLock)
            {
                faceSign = FacingSign();
                moveSign = FacingSign();
            }
            else
            {
                if (currentBand == DiscoveryBand.Follow)
                {
                    faceSign = dirToPlayer; moveSign = dirToPlayer;
                }
                else if (currentBand == DiscoveryBand.Retreat)
                {
                    // 统一退却时朝向为远离玩家（不在休息时强制朝向玩家）
                    faceSign = -dirToPlayer;
                    moveSign = -dirToPlayer;
                }
                else // Backstep
                {
                    faceSign = dirToPlayer; moveSign = -dirToPlayer;
                }

                faceSign = StabilizeFaceSign(faceSign, dxToPlayer);
            }

            ForceFaceSign(faceSign);

            if (!isJumping && isGroundedMC && MaybeAutoJumpOverLowObstacle(moveSign))
            {
                if (isJumping && activeJumpMove != null)
                {
                    JumpUpdate(activeJumpMove);

                    return;
                }
            }

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

            bool suppressTurnInZone = (activeDiscoveryEvent.obstacleTurnMode != ObstacleTurnMode.AutoTurn);

            obstacleTurnedThisFrame = false;

            discoveryRestJustFinished = StraightTickCommon(
                move,
                moveSign,
                useWaypoints: false,
                useMoveDirForProbes: true,
                allowTurnOnObstacle: allowTurn,
                stopAtCliffEdgeWhenNoTurn: stopAtCliff,
                suppressTurnInAutoJumpZone: suppressTurnInZone
            );

            // 仅当开关开启时执行转换（Move 分支：使用 moveSet.back.backrestMin/Max）
            if (dcfg.suppressBackBandDuringRest
                && (currentBand == DiscoveryBand.Retreat || currentBand == DiscoveryBand.Backstep)
                && isResting
                && backBandSuppressTimer <= 0f)
            {
                float min = Mathf.Max(0f, activeDiscoveryEvent.moveSet.back.backrestMin);
                float max = Mathf.Max(min, activeDiscoveryEvent.moveSet.back.backrestMax);
                backBandSuppressTimer = (max > 0f) ? Random.Range(min, max) : 0f;

                ResetStraightRuntime(move);
                discoveryRestJustFinished = true;
            }

            

            // 当处于 Retreat/Backstep 且配置 enableBackAutoJumpOnObstacle 开启时，
            // 如果检测到墙/悬崖则尝试触发一次向玩家方向的跳跃（使用当前事件的 jumpSet）
            if (dcfg != null && dcfg.enableBackAutoJumpOnObstacle && (currentBand == DiscoveryBand.Retreat || currentBand == DiscoveryBand.Backstep))
            {
                // 仅在地面、非跳跃、非休息时尝试
                if (!isJumping && isGroundedMC && !isResting)
                {
                    // 判定“前方有墙或悬崖”
                    bool wallAhead = CheckWallInDir(moveSign);
                    bool cliffAhead = CheckCliffInDir(moveSign, permitCountsAsGround: true);
                    if (wallAhead || cliffAhead)
                    {
                        // 需要当前事件配置有 jumpSet 才能执行
                        if (activeDiscoveryEvent != null && activeDiscoveryEvent.jumpSet != null)
                        {
                            var jmove = GetD2JumpFor(currentBand);
                            if (jmove != null)
                            {
                                // 准备并起跳：跳跃方向按“朝玩家方向”
                                int dirToPlayerLocal = (Mathf.Abs(dxToPlayer) <= faceFlipDeadZone) ? FacingSign() : (dxToPlayer >= 0f ? +1 : -1);
                                EnsureJumpPlan(jmove, useAutoParams: false);
                                BeginOneJump(jmove, useAutoParams: false, moveDirOverride: dirToPlayerLocal);
                                // 一次触发后跳出，不再继续 StraightTick 后续处理
                                obstacleTurnedThisFrame = false;
                                discoveryRestJustFinished = false;
                                // 直接 return ，避免后续重复逻辑（DiscoveryUpdate 会在下一帧继续处理 JumpUpdate）
                                return;
                            }
                        }
                    }
                }
            }

            if (obstacleTurnedThisFrame)
            {
                obstacleTurnFaceLockTimer = Mathf.Max(0f, obstacleTurnFaceLockTime);
                obstacleTurnedThisFrame = false;
            }

            if (discoveryRestJustFinished)
                AdvanceDiscoveryEvent();

        }
        else
        {
            if (isAutoJumping && activeJumpMove != null)
            {
                JumpUpdate(activeJumpMove);

                return;
            }

            var jmove = GetD2JumpFor(currentBand);

            int faceSign = (currentBand == DiscoveryBand.Retreat) ? -dirToPlayer : dirToPlayer;

            if (lockFaceWhileAirborne && isJumping && faceSignLockedDuringAir != 0)
                faceSign = faceSignLockedDuringAir;
            else
                faceSign = StabilizeFaceSign(faceSign, dxToPlayer);

            ForceFaceSign(faceSign);

            int moveDir = (currentBand == DiscoveryBand.Follow) ? dirToPlayer : -dirToPlayer;

            if (suppressNextDiscoveryJumpOnce)
            {
                suppressNextDiscoveryJumpOnce = false;

                isResting = true;
                inJumpRestPhase = true;

                if (!string.IsNullOrEmpty(jmove.jumpRestAnimation))
                    PlayAnimIfNotCurrent(jmove.jumpRestAnimation);

                jmove.rtRestTimer = PickJumpRestTime(jmove);

                rb.velocity = new Vector2(0f, rb.velocity.y);
                desiredSpeedX = 0f;
                ApplySlopeIdleStopIfNoMove();


                return;
            }

            if (!isJumping && isGroundedMC && MaybeAutoJumpOverLowObstacle(moveDir))
            {
                if (isJumping && activeJumpMove != null)
                {
                    JumpUpdate(activeJumpMove);

                    return;
                }
            }

            if (isResting && inJumpRestPhase)
            {
                // 抑制开关：把跳休转为抑制窗口（用 backjumpRestMin/Max 随机）
                if (dcfg.suppressBackBandDuringRest
                    && (currentBand == DiscoveryBand.Retreat || currentBand == DiscoveryBand.Backstep)
                    && backBandSuppressTimer <= 0f)
                {
                    float min = Mathf.Max(0f, activeDiscoveryEvent.jumpSet.back.backjumpRestMin);
                    float max = Mathf.Max(min, activeDiscoveryEvent.jumpSet.back.backjumpRestMax);
                    backBandSuppressTimer = (max > 0f) ? Random.Range(min, max) : 0f;

                    isResting = false;
                    inJumpRestPhase = false;
                    if (jmove != null) jmove.rtRestTimer = 0f;
                    discoveryRestJustFinished = true;

                    AdvanceDiscoveryEvent();
                    return;
                }

                

                if (!string.IsNullOrEmpty(jmove.jumpRestAnimation))
                    PlayAnimIfNotCurrent(jmove.jumpRestAnimation);

                rb.velocity = new Vector2(0f, rb.velocity.y);
                desiredSpeedX = 0f;
                ApplySlopeIdleStopIfNoMove();

                if (jmove.rtRestTimer <= 0f) jmove.rtRestTimer = PickJumpRestTime(jmove);
                jmove.rtRestTimer -= Time.deltaTime;

                if (jmove.rtRestTimer <= 0f)
                {
                    isResting = false;
                    inJumpRestPhase = false;
                    jmove.rtRestTimer = 0f;
                    discoveryRestJustFinished = true;
                    // 一次发现-跳跃循环彻底结束（从起跳到跳休结束）——清除生命周期标记
                    discoveryJumpActive = false;
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
                    JumpUpdate(jmove);
                }
            }

            if (discoveryRestJustFinished)
                AdvanceDiscoveryEvent();
        }


    }

    private int StabilizeFaceSign(int wantFaceSign, float dxToPlayer)
    {
        int cur = FacingSign();
        if (wantFaceSign == cur) return cur;

        if (Mathf.Abs(dxToPlayer) <= faceFlipDeadZone) return cur;

        if (faceFlipDwellTimer > 0f) return cur;

        float need = faceFlipDeadZone + Mathf.Max(0f, faceFlipHysteresis);
        if (Mathf.Abs(dxToPlayer) < need) return cur;

        faceFlipDwellTimer = Mathf.Max(0.05f, faceFlipMinDwellTime);
        return wantFaceSign;
    }

    private DiscoveryBand ComputeWantBandWithHysteresis(float d, DiscoveryV2Config cfg)
    {
        float find = Mathf.Max(0f, cfg.findRange);
        float revBase = Mathf.Max(0f, cfg.reverseRange);
        float backBase = Mathf.Max(0f, cfg.backRange);

        bool backEnabled = backBase > 0f;
        bool revEnabled = revBase > 0f;

        float revOut = revBase + Mathf.Max(0f, bandHysteresis);
        float backOut = backBase + Mathf.Max(0f, bandHysteresis);

        switch (currentBand)
        {
            case DiscoveryBand.Follow:
                if (backEnabled && d <= backBase) return DiscoveryBand.Backstep;
                if (revEnabled && d <= revBase) return DiscoveryBand.Retreat;
                return DiscoveryBand.Follow;

            case DiscoveryBand.Retreat:
                if (backEnabled && d <= backBase) return DiscoveryBand.Backstep;
                if (!revEnabled || d >= revOut) return DiscoveryBand.Follow;
                return DiscoveryBand.Retreat;

            case DiscoveryBand.Backstep:
                if (!backEnabled || d >= backOut)
                    return (revEnabled && d <= revBase) ? DiscoveryBand.Retreat : DiscoveryBand.Follow;
                return DiscoveryBand.Backstep;

            default:
                if (backEnabled && d <= backBase) return DiscoveryBand.Backstep;
                if (revEnabled && d <= revBase) return DiscoveryBand.Retreat;
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

            // Follow 段只有单值 → 映射为区间[min=max=单值]
            restMin = Mathf.Max(0f, set.find.findrestDuration),
            restMax = Mathf.Max(0f, set.find.findrestDuration),

            moveAnimation = set.findmoveAnimation,
            restAnimation = set.findrestAnimation,
            moveEffectPrefab = set.findmoveEffectPrefab,
            restEffectPrefab = set.findrestEffectPrefab
        };
    }
    // Discovery V2：把 back 的区间写入运行时 PatrolMovement，保证“总是生效”
    private PatrolMovement BuildStraightFromRetreat(MoveSetV2 set)
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

            restMin = Mathf.Max(0f, set.back.backrestMin),
            restMax = Mathf.Max(0f, set.back.backrestMax),

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

            restMin = Mathf.Max(0f, set.back.backrestMin),
            restMax = Mathf.Max(0f, set.back.backrestMax),

            moveAnimation = set.backmoveAnimation,
            restAnimation = set.findrestAnimation,
            moveEffectPrefab = set.findmoveEffectPrefab,
            restEffectPrefab = set.findrestEffectPrefab
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

            // Follow 段只有单值 → 映射为区间[min=max=单值]
            jumprestMin = Mathf.Max(0f, set.find.findjumpRestDuration),
            jumprestMax = Mathf.Max(0f, set.find.findjumpRestDuration),

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
            jumpSpeed = set.back.backjumpSpeed,
            jumpHeight = set.back.backjumpHeight,
            gravityScale = set.back.backgravityScale,
            jumpDuration = set.back.backjumpDuration,

            jumprestMin = Mathf.Max(0f, set.back.backjumpRestMin),
            jumprestMax = Mathf.Max(0f, set.back.backjumpRestMax),

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

            jumprestMin = Mathf.Max(0f, set.back.backjumpRestMin),
            jumprestMax = Mathf.Max(0f, set.back.backjumpRestMax),

            jumpAnimation = set.backjumpAnimation,
            jumpRestAnimation = set.findjumpRestAnimation,
            jumpEffectPrefab = set.findjumpEffectPrefab,
            jumpRestEffectPrefab = set.findjumpRestEffectPrefab
        };
    }

    private PatrolMovement EnsureDummyJump()
    {
        return d2_jump_follow ??= new PatrolMovement { type = MovementType.Jump, jumpSpeed = 0f };
    }

    private bool StraightTickCommon(
        PatrolMovement move,
        int dirSign,
        bool useWaypoints,
        bool useMoveDirForProbes = false,
        bool allowTurnOnObstacle = true,
        bool stopAtCliffEdgeWhenNoTurn = false,
        bool suppressTurnInAutoJumpZone = true)
    {
        obstacleTurnedThisFrame = false;

        UpdateGroundedAndSlope();

        if (!isResting && move.rtStraightPhase == StraightPhase.None && HasStraightRest(move) && (move.moveDuration <= 0f || move.moveSpeed <= 0.0001f))
        {
            move.rtStraightPhase = StraightPhase.Rest;
            isResting = true;
            move.rtRestTimer = PickStraightRestTime(move);
            PlayAnimIfNotCurrent(move.restAnimation);
            rb.velocity = new Vector2(0f, rb.velocity.y);
            desiredSpeedX = 0f;
            ApplySlopeIdleStopIfNoMove();
            return false;
        }

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
                // 无休息区间：直接结束
                if (!HasStraightRest(move))
                {
                    isResting = false;
                    move.rtStraightPhase = StraightPhase.None;
                    move.rtMoveTimer = 0f;
                    move.rtRestTimer = 0f;
                    move.rtAccelTimer = 0f;
                    move.rtCruiseTimer = 0f;
                    move.rtDecelTimer = 0f;
                    return true;
                }

                // 有休息区间：进入休息并随机时长
                isResting = true;
                move.rtRestTimer = PickStraightRestTime(move);
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
                    if (!HasStraightRest(move))
                    {
                        isResting = false;
                        move.rtStraightPhase = StraightPhase.None;
                        move.rtMoveTimer = 0f;
                        move.rtRestTimer = 0f;
                        move.rtAccelTimer = 0f;
                        move.rtCruiseTimer = 0f;
                        move.rtDecelTimer = 0f;
                        return true;
                    }

                    isResting = true;
                    if (move.rtRestTimer <= 0f) move.rtRestTimer = PickStraightRestTime(move);

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
                        return true;
                    }
                    return false;
                }
        }

        bool wallAhead = useMoveDirForProbes ? CheckWallInDir(dirSign) : CheckWallAhead();
        bool permitCountsAsGround = suppressTurnInAutoJumpZone;
        bool cliffAhead = useMoveDirForProbes
            ? CheckCliffInDir(dirSign, permitCountsAsGround)
            : CheckCliffAhead(permitCountsAsGround);

        if (allowTurnOnObstacle && !cliffAhead && !isGroundedMC)
        {
            bool cliffLoose = useMoveDirForProbes
                ? ProbeCliffNoGroundInDir(dirSign, permitCountsAsGround)
                : ProbeCliffNoGroundAhead(permitCountsAsGround);
            if (cliffLoose) cliffAhead = true;
        }

        bool zoneBlocksTurn = suppressTurnInAutoJumpZone && inAutoJumpPermitZone;
        bool cooldownBlocksTurn = suppressTurnInAutoJumpZone ? (ignoreCliffFramesLeft > 0) : false;

        bool canTurnNow = allowTurnOnObstacle && !zoneBlocksTurn && !isAutoJumping && !cooldownBlocksTurn;

        bool cooldownOkForTurn = !suppressTurnInAutoJumpZone || (turnCooldown <= 0f);

        bool turnedThisFrame = false;
        if (canTurnNow && (wallAhead || cliffAhead) && cooldownOkForTurn)
        {
            bool ignoreCooldown = !suppressTurnInAutoJumpZone;
            TurnAround(ignoreCooldown);
            turnedThisFrame = true;

            if (allowTurnOnObstacle)
                obstacleTurnedThisFrame = true;
        }

        int effectiveDir = dirSign;
        if (allowTurnOnObstacle && (turnedThisFrame || turnCooldown > 0f))
        {
            effectiveDir = FacingSign();
        }

        if (useWaypoints)
            WaypointUpdateAndMaybeTurn(canTurnNow);

        float clampedSpeed = Mathf.Clamp(move.rtCurrentSpeed, 0f, targetSpeed);
        bool stopAtCliffEdge = (!allowTurnOnObstacle) && stopAtCliffEdgeWhenNoTurn && cliffAhead && isGroundedMC;
        desiredSpeedX = (stopAtCliffEdge ? 0f : clampedSpeed * effectiveDir);
        if (isGroundedMC)
            ApplyProjectedVelocityAlongSlope(Mathf.Abs(desiredSpeedX), effectiveDir);
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

    bool CheckCliffAhead(bool permitCountsAsGround)
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

            if (hit.collider.isTrigger) return permitCountsAsGround;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return permitCountsAsGround;

            if (hit.normal.y >= groundMinNormalYMC) return true;

            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        bool safeToe = IsSafe(originToe);
        bool safeHalf = IsSafe(originHalf);

        return !(safeToe || safeHalf);
    }

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

    bool CheckCliffInDir(int dirSign, bool permitCountsAsGround)
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

            if (hit.collider.isTrigger) return permitCountsAsGround;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return permitCountsAsGround;

            if (hit.normal.y >= groundMinNormalYMC) return true;

            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        bool safeToe = IsSafe(originToe);
        bool safeHalf = IsSafe(originHalf);

        return !(safeToe || safeHalf);
    }

    void TurnAround(bool ignoreCooldown = false)
    {
        if (!ignoreCooldown && turnCooldown > 0f) return;
        float newY = (transform.rotation.eulerAngles.y == 0f) ? 180f : 0f;
        transform.rotation = Quaternion.Euler(0, newY, 0);
        turnCooldown = 0.25f;
    }

    private bool ProbeCliffNoGroundAhead(bool permitCountsAsGround)
    {
        if (col == null) return false;
        Bounds b = col.bounds;
        float baseY = b.min.y + 0.05f;

        const float STEP_DOWN_ALLOW = 0.10f;
        float rayDist = Mathf.Max(cliffCheckDistance, STEP_DOWN_ALLOW + 0.1f);

        Vector2 forward = (Vector2)transform.right;
        Vector2 originToe = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0f, cliffCheckOffsetX));
        Vector2 originHalf = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0f, cliffCheckOffsetX) * 0.5f);

        bool IsSafe(Vector2 origin)
        {
            var hit = Physics2D.Raycast(origin, Vector2.down, rayDist, groundLayer);
            Debug.DrawLine(origin, origin + Vector2.down * rayDist, hit.collider ? Color.green : Color.blue);
            if (!hit.collider) return false;
            if (hit.collider.isTrigger) return permitCountsAsGround;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return permitCountsAsGround;
            if (hit.normal.y >= groundMinNormalYMC) return true;
            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        return !(IsSafe(originToe) || IsSafe(originHalf));
    }

    private bool ProbeCliffNoGroundInDir(int dirSign, bool permitCountsAsGround)
    {
        if (col == null) return false;
        Bounds b = col.bounds;
        float baseY = b.min.y + 0.05f;

        const float STEP_DOWN_ALLOW = 0.10f;
        float rayDist = Mathf.Max(cliffCheckDistance, STEP_DOWN_ALLOW + 0.1f);

        Vector2 forward = Vector2.right * Mathf.Sign(dirSign);
        Vector2 originToe = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0f, cliffCheckOffsetX));
        Vector2 originHalf = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0f, cliffCheckOffsetX) * 0.5f);

        bool IsSafe(Vector2 origin)
        {
            var hit = Physics2D.Raycast(origin, Vector2.down, rayDist, groundLayer);
            Debug.DrawLine(origin, origin + Vector2.down * rayDist, hit.collider ? Color.cyan : Color.blue);
            if (!hit.collider) return false;
            if (hit.collider.isTrigger) return permitCountsAsGround;
            if (hit.collider.CompareTag(autoJumpPermitTag)) return permitCountsAsGround;
            if (hit.normal.y >= groundMinNormalYMC) return true;
            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        return !(IsSafe(originToe) || IsSafe(originHalf));
    }

    // 跳跃起始
    void BeginOneJump(PatrolMovement move, bool useAutoParams, int? moveDirOverride = null)
    {
        if (!useAutoParams && !CheckGrounded()) return;
        if (isJumping) { if (!useAutoParams) return; }

        isJumping = true;
        isAutoJumping = useAutoParams;
        activeJumpMove = move;

        if (lockFaceWhileAirborne && state == MonsterState.Discovery)
            faceSignLockedDuringAir = FacingSign();

        airJumpFxPlayedThisAir = false;

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

        // 关键：无论是否自动跳，都强制把跳跃动画从 0 重播
        if (!string.IsNullOrEmpty(move.jumpAnimation))
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(move.jumpAnimation))
                animator.Play(move.jumpAnimation, 0, 0f);   // 同状态：从 0 重播，确保事件再次触发
            else
                animator.CrossFadeInFixedTime(move.jumpAnimation, 0f, 0, 0f);
            animator.Update(0f);
        }

        currentJumpDirSign = moveDirOverride.HasValue ? System.Math.Sign(moveDirOverride.Value) : FacingSign();
        rb.velocity = new Vector2(spdX * currentJumpDirSign, vY);

        move.rtUsingAutoJumpParams = useAutoParams;

        // 如果当前处于“发现-跳跃事件”，则标记一个“发现跳跃生命周期”并快照起跳档位
        if (state == MonsterState.Discovery && activeDiscoveryEvent != null && activeDiscoveryEvent.mode == DiscoveryV2Mode.Jump)
        {
            discoveryJumpActive = true;
            bandAtJumpStart = currentBand;
        }
    }

    // 跳跃更新
    void JumpUpdate(PatrolMovement move)
    {
        float spdX = move.rtUsingAutoJumpParams ? Mathf.Max(move.autojumpSpeed, 0f)
                                            : Mathf.Max(move.jumpSpeed, 0f);

        if (!move.rtUsingAutoJumpParams && turnCooldown <= 0f && CheckWallInDir(currentJumpDirSign))
        {
            TurnAround(ignoreCooldown: true);
            currentJumpDirSign = -currentJumpDirSign;
            turnCooldown = 0.10f;
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

            faceSignLockedDuringAir = 0;

            postLandingFaceLockTimer = Mathf.Max(0f, postLandingFaceLockTime);

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

                if (suppressOneDiscoveryJumpAfterAuto && state == MonsterState.Discovery)
                    suppressNextDiscoveryJumpOnce = true;

                float restAfterAuto = (move.type == MovementType.Jump) ? PickJumpRestTime(move) : PickStraightRestTime(move);

                if (restAfterAuto > 0f)
                {
                    isResting = true;
                    inJumpRestPhase = true;
                    move.rtRestTimer = restAfterAuto;
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

                move.rtRestTimer = PickJumpRestTime(move);
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
        if (hitC.collider && hitC.distance < bestDist) { best = hitC; bestDist = hitC.distance; }
        if (hitR.collider && hitR.distance < bestDist) { best = hitR; bestDist = hitR.distance; }
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

    // Patrol 阶段跳跃特效（仅在 Patrol 生效）
    public void OnFxJump()
    {
        if (state != MonsterState.Patrol) return;
        var m = activeJumpMove;
        if (m == null) return;
        if (!isJumping) return;

        if (IsAutoJumpNow())
        {
            if (airJumpFxPlayedThisAir) return;
            if (!TryConsumeAutoJumpFxTicket()) return;
            airJumpFxPlayedThisAir = true;
        }

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

            // Find* 槽位全部公用通用锚点
            case FxSlot.FindMove: return fxMovePoint;
            case FxSlot.FindRest: return fxRestPoint;
            case FxSlot.FindJump: return fxJumpPoint;
            case FxSlot.FindJumpRest: return fxJumpRestPoint;

            default: return null;
        }
    }

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
        move.rtExecuteRemain = 1;

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

    void OnDrawGizmosSelected()
    {
        var dcfg2 = config?.discoveryV2Config;
        if (dcfg2 == null) return;

        Vector3 pos = monsterDistPoint ? monsterDistPoint.position : transform.position;

#if UNITY_EDITOR
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

            d.findRange = Mathf.Max(0f, d.findRange);
            d.reverseRange = Mathf.Max(0f, d.reverseRange);
            d.backRange = Mathf.Max(0f, d.backRange);

            if (d.events != null)
            {
                foreach (var ev in d.events)
                {
                    if (ev != null)
                    {
                        if (ev.obstacleTurnMode == ObstacleTurnMode.AutoTurn && ev.allowObstacleAutoTurnLegacy == false)
                        {
                            ev.obstacleTurnMode = ObstacleTurnMode.NoTurnStopAtCliff;
                        }
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

        float topY = b.max.y + 0.02f;
        float useAutoHeight = 0.8f;
        if (patrolRuntimeMoves != null && patrolRuntimeMoves.Count > 0)
            useAutoHeight = Mathf.Max(0.05f, patrolRuntimeMoves[Mathf.Clamp(patrolIndex, 0, patrolRuntimeMoves.Count - 1)].autojumpHeight);
        float raise = (upperHeightAdd > 0f) ? upperHeightAdd : useAutoHeight;

        Vector2 upperOrigin = new Vector2(lowerOrigin.x, topY + raise);

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

        var hitUp = Physics2D.Raycast(upperOrigin, forward, Mathf.Max(0.05f, upperRayLen), groundLayer);
        Debug.DrawLine(upperOrigin, upperOrigin + forward * Mathf.Max(0.05f, upperRayLen), hitUp.collider ? Color.magenta : Color.green);
        if (hitUp.collider && !hitUp.collider.isTrigger && !hitUp.collider.CompareTag(autoJumpPermitTag))
            return false;

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


    private static bool HasStraightRest(PatrolMovement m) => Mathf.Max(m.restMin, m.restMax) > 0f; private static bool HasJumpRest(PatrolMovement m) => Mathf.Max(m.jumprestMin, m.jumprestMax) > 0f;

    private float PickStraightRestTime(PatrolMovement move) { float min = Mathf.Max(0f, move.restMin); float max = Mathf.Max(min, move.restMax); return (max > 0f) ? Random.Range(min, max) : 0f; }

    private float PickJumpRestTime(PatrolMovement move) { float min = Mathf.Max(0f, move.jumprestMin); float max = Mathf.Max(min, move.jumprestMax); return (max > 0f) ? Random.Range(min, max) : 0f; }

    // ================== 发现阶段：动画特效事件（严格一一对应版） ==================
    // 发现阶段：动画特效事件入口（防抖+稳态版本）


    public void OnFxFindMove()
    {
        if (state != MonsterState.Discovery) return;

        // 新增：若当前处在“发现-跳跃生命周期”或空中/跳休中，屏蔽移动FX，避免共用剪辑时误播
        if (discoveryJumpActive || isJumping || inJumpRestPhase) return;

        // 仅在“发现-移动事件模式”下响应
        if (activeDiscoveryEvent == null || activeDiscoveryEvent.mode != DiscoveryV2Mode.Move) return;

        PlayDiscoveryMoveFxForBand(currentBand, isRest: false);
    }

    public void OnFxFindRest()
    {
        if (state != MonsterState.Discovery) return;

        // 新增：若当前处在“发现-跳跃生命周期”或空中/跳休中，屏蔽移动FX
        if (discoveryJumpActive || isJumping || inJumpRestPhase) return;

        // 仅在“发现-移动事件模式”下响应
        if (activeDiscoveryEvent == null || activeDiscoveryEvent.mode != DiscoveryV2Mode.Move) return;

        PlayDiscoveryMoveFxForBand(currentBand, isRest: true);
    }

    // 跳跃
    public void OnFxFindJump()
    {
        if (state != MonsterState.Discovery) return;

        // 关键：基于“跳跃生命周期”判断，避免事件轮换/切档的瞬时竞态导致偶发不播
        if (!(discoveryJumpActive || isJumping)) return;

        // 使用“起跳时档位”的 Jump 配置，避免中途切档导致取到空资源
        var j = GetD2JumpFor(discoveryJumpActive ? bandAtJumpStart : currentBand);
        if (j == null) return;

        // 缺动画/缺资源均不播（遵守你的严格播放规则）
        if (string.IsNullOrEmpty(j.jumpAnimation)) return;
        var prefab = j.jumpEffectPrefab;
        if (prefab == null) return;

        var anchor = ResolveFxAnchor(FxSlot.FindJump);
        if (anchor == null) return;

        PlayEffect(prefab, anchor);
    }

    public void OnFxFindJumpRest()
    {
        if (state != MonsterState.Discovery) return;

        // 跳休阶段才处理；若仍在生命周期内也允许（共用剪辑时更稳）
        if (!(discoveryJumpActive || (isResting && inJumpRestPhase))) return;

        var j = GetD2JumpFor(discoveryJumpActive ? bandAtJumpStart : currentBand);
        if (j == null) return;

        if (string.IsNullOrEmpty(j.jumpRestAnimation)) return;
        var prefab = j.jumpRestEffectPrefab;
        if (prefab == null) return;

        var anchor = ResolveFxAnchor(FxSlot.FindJumpRest);
        if (anchor == null) return;

        PlayEffect(prefab, anchor);
    }

    // 严格播放（去掉 Animator 状态名强校验；保留‘配置非空/特效非空/锚点非空’）
    private void PlayDiscoveryMoveFxForBand(DiscoveryBand band, bool isRest)
    {
        if (state != MonsterState.Discovery) return;

        var m = GetD2MoveFor(band);
        if (m == null) return;

        // 配置的动画字段必须非空（满足“缺动画不播”）
        string anim = isRest ? m.restAnimation : m.moveAnimation;
        if (string.IsNullOrEmpty(anim)) return;

        // 特效必须非空（满足“缺特效不播”）
        var prefab = isRest ? m.restEffectPrefab : m.moveEffectPrefab;
        if (prefab == null) return;

        // 统一使用 find 锚点（已按规则，不混用不回退）
        FxSlot slot = isRest ? FxSlot.FindRest : FxSlot.FindMove;
        var anchor = ResolveFxAnchor(slot);
        if (anchor == null) return; // 无锚点不播

        PlayEffect(prefab, anchor);
    }

    
}