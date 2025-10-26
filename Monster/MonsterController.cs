using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// 怪物控制器：出生 → 巡逻 → 发现 → 攻击 → 死亡
/// 支持墙/悬崖检测 + 空中状态屏蔽 + 特效事件 + 可视化调试
/// </summary>
public class MonsterController : MonoBehaviour
{
    [Header("怪物配置 ScriptableObject")]
    public MonsterConfig config;

    [HideInInspector] public MonsterSpawner spawner;
    private Animator animator;
    private Transform player;
    private Collider2D col;
    private float currentHP;
    private bool isDead;
    private int patrolIndex = 0;
    private bool isResting = false;
    private Transform flip;

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
    private float jumpTimeLeft = 0f;
    private float vY = 0f;
    private const float BASE_G = 25f;
    // 当前是否处于“跳跃落地后的休息窗口”
    private bool inJumpRestPhase = false;

    private float turnCooldown = 0f;
    private const float SKIN = 0.02f;
    // 新增：本帧通过安全位移判定到的“是否着地”
    private bool groundedAfterVerticalMove = false;
    private bool hasAlignedToGround = false;
    private int spawnStableFrames = 6; // 约等于 0.1 秒（按60fps计）

    [Header("AutoJump 许可区")]
    public string autoJumpPermitTag = "AutoJumpPermit";

    private bool inAutoJumpPermitZone = false;

    private bool autoJumpReady = true;                // 是否允许触发自动跳
    private bool autoJumpRearmAfterLanding = false;   // 等待“落地+忽略帧结束”后才重新上膛

    // AutoJump 触发的“上膛/保险”机制：
    // - 进入许可区即上膛（armed = true）
    // - 一次自动跳后上保险（disarmUntilExit = true），必须先离开许可区才能再次上膛
    private bool autoJumpArmed = false;
    private bool autoJumpDisarmUntilExit = false;

    // 来自直线分支的临时跳跃：落地休息后不切到下一段
    private bool autoJumpFromStraight = false;
    private bool keepSameMoveAfterRest = false;

    private enum MonsterState { Idle, Patrol, Discovery, Attack, Dead }
    private MonsterState state = MonsterState.Idle;

    // 四通道各自独立的“当前段”引用
    private PatrolMovement activeStraightMove = null;
    private PatrolMovement activeJumpMove = null;

    // === Rigidbody & 斜坡物理（对齐 PlayerController） ===
    private Rigidbody2D rb;

    // 接地/斜坡判定输出
    private bool isGroundedMC = false;        // 避免与已有 isDead 混淆，MC 表 MonsterController
    private Vector2 groundNormalMC = Vector2.up;
    private bool slopeIdleLockedMC = false;
    private float slopeSavedGravityMC = 0f;

    // 路点巡逻（启用 patrolPoints 时）
    private int patrolPointIndex = 0;
    [SerializeField, Tooltip("接近路点的判定半径")] private float pointArriveRadius = 0.05f;

    // 路点 ping-pong 状态（仅做方向锚点）
    private int waypointIndex = 0;           // 当前已抵达/所在的路点
    private int waypointTargetIndex = 0;     // 当前要前往的目标路点
    private int waypointDir = +1;            // +1 正向，-1 反向（0..N..0 ping-pong）

    // 落地后短暂抑制“向上分量”叠加（单位：帧）
    private int suppressUpwardOnGroundFrames = 0;

    // 直线当前朝向（基于 transform.rotation.y：0=向右,180=向左）
    private int FacingSign() => (transform.rotation.eulerAngles.y == 0f) ? +1 : -1;

    // 用于 Straight 的期望速度（米/秒；正负含朝向）
    private float desiredSpeedX = 0f;

    // AutoJump 运行时标记
    private bool isAutoJumping = false;

    private readonly Dictionary<string, GameObject> fxLookup = new Dictionary<string, GameObject>();

    private List<int> moveOrder = null;
    private int moveOrderPos = 0;


    [Header("AutoJump 落地后抑制")]
    [Tooltip("自动跳落地→休息结束后，忽略多少帧“悬崖检测”，避免刚落地又把自己当成悬崖前沿而转向。60FPS下8≈0.13秒。")]
    public int autoJumpIgnoreCliffFrames = 8;
    private int ignoreCliffFramesLeft = 0;

    // 可调参数（与玩家保持一致的手感）
    [Header("斜坡/地面移动（怪物）")]
    [Tooltip("接地判定所需的最小法线Y。0.7≈允许到45°斜坡")]
    [SerializeField, Range(0f, 1f)] private float groundMinNormalYMC = 0.70f;
    [Tooltip("沿斜面切向速度绝对值小于该阈值则清零（米/秒）")]
    [SerializeField] private float horizontalStopThresholdOnSlopeMC = 0.02f;
    [Tooltip("斜坡停驻锁（无主动移动时冻结重力，止滑）")]
    [SerializeField] private bool slopeIdleFreezeGravityMC = true;
    [Tooltip("进入停驻锁所需的速度门槛（米/秒）")]
    [SerializeField] private float slopeEnterIdleSpeedEpsilonMC = 0.50f;
    [Tooltip("向下速度小于该阈值时钳为0，阻止极慢的下滑（负数，靠近0更“粘”）")]
    [SerializeField] private float slopeStopVyThresholdMC = -0.05f;

    // NEW: 斜坡上保持“世界X速度”一致（视觉不变慢）
    [Tooltip("斜坡上放大切向速度，保持世界X速度≈moveSpeed")]
    [SerializeField] private bool preserveWorldXOnSlopeMC = true;

    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        animator = GetComponentInChildren<Animator>();
        col = GetComponent<Collider2D>();

        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError($"[MonsterController] {name} 未找到 Rigidbody2D！");
            enabled = false;
            return;
        }

        if (col == null)
            Debug.LogWarning($"[MonsterController] {name} 未找到 Collider2D，将无法进行地面检测。");

        // 设置初始朝向（只用旋转）
        if (config.spawnConfig.spawnOrientation == Orientation.FaceLeft)
            transform.rotation = Quaternion.Euler(0, 180f, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FaceRight)
            transform.rotation = Quaternion.Euler(0, 0, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FacePlayer && player)
            transform.rotation = (player.position.x > transform.position.x)
                ? Quaternion.Euler(0, 0, 0)
                : Quaternion.Euler(0, 180f, 0);

        flip = transform.Find("Flip");
        if (animator == null)
        {
            Debug.LogError($"[MonsterController] {name} 未找到 Animator！");
            enabled = false;
            return;
        }

        // 自动添加事件中继器
        if (animator.GetComponent<MonsterAnimationEventRelay>() == null)
            animator.gameObject.AddComponent<MonsterAnimationEventRelay>();

        // 关键：自动克隆整份配置，避免修改到资产本体（一次到位，无需手写逐字段复制）
        if (config != null)
            config = ScriptableObject.Instantiate(config);

        // 直接使用克隆体中的 movements 作为运行态列表
        if (config && config.patrolConfig != null && config.patrolConfig.movements != null)
            patrolRuntimeMoves = config.patrolConfig.movements;
        else
            patrolRuntimeMoves = new List<PatrolMovement>();

        // 统一构建特效名索引（基于克隆体）
        BuildFxLookup();
        //基于当前 movements 和 randomOrder 初始化播放顺序
        BuildMoveOrderFromConfig();

        // 初始化路点（若有），并对齐初始朝向
        InitWaypointsIfAny();

        currentHP = config.maxHP;
        turnCooldown = 1f;
        StartCoroutine(StateMachine());
    }

    void Update()
    {
        if (turnCooldown > 0f)
            turnCooldown -= Time.deltaTime;

        // 退出触发器 + 落地 + 忽略帧结束 才重新允许自动跳
        if (autoJumpRearmAfterLanding && !isJumping && !inAutoJumpPermitZone && ignoreCliffFramesLeft <= 0)
        {
            autoJumpReady = true;
            autoJumpRearmAfterLanding = false;
        }
    }

    IEnumerator StateMachine()
    {
        // 出生阶段：播放一次出生动画；随后按 Idle Time 播放 Idle 动画
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
        Debug.Log($"[Spawn] 出生阶段完成，进入巡逻阶段");

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
                case MonsterState.Attack:
                    AttackUpdate();
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

    // === 核心：巡逻逻辑 ===
    void PatrolUpdate()
    {
        if (ignoreCliffFramesLeft > 0) ignoreCliffFramesLeft--;

        // 刷接地/斜坡
        UpdateGroundedAndSlope();

        // NEW: 跳跃优先级（自动/正常共用）
        // 只要在跳，就交给 JumpUpdate 管空中运动，跳过 Straight 的相位机/速度写入，避免空中被清 X 速度
        if (isJumping && activeJumpMove != null)
        {
            JumpUpdate(activeJumpMove);
            return;
        }

        // ============= Movements 模式（路点仅作为“方向锚点”） =============
        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        PatrolMovement move = patrolRuntimeMoves[patrolIndex];


        // ============== Straight 分支 ==============
        // 直线逻辑（加速 -> 匀速 -> 减速 -> 休息）
        if (!isResting && move.type == MovementType.Straight)
        {
            // 保证“移动阶段的动画”始终在播（覆盖 加速/匀速/减速）
            PlayAnimIfNotCurrent(move.moveAnimation);

            activeStraightMove = move;

            // 初始化三段计时（仅在进入该 movement 的第一次）
            if (move.rtStraightPhase == StraightPhase.None)
            {
                move.rtCurrentSpeed = 0f;

                // 这里的 move.moveDuration 仅作为“匀速时间”
                move.rtCruiseTimer = Mathf.Max(0f, move.moveDuration);

                // 若有“按时长”配置，则优先用时长；否则用速率；都为0表示“瞬时”
                move.rtAccelTimer = Mathf.Max(0f, move.accelerationTime);
                move.rtDecelTimer = Mathf.Max(0f, move.decelerationTime);

                bool instantAccel = (move.accelerationTime <= 0f && move.acceleration <= 0f);
                bool instantDecel = (move.decelerationTime <= 0f && move.deceleration <= 0f);

                if (instantAccel)
                {
                    // 瞬时到达匀速
                    move.rtCurrentSpeed = Mathf.Max(0f, move.moveSpeed);
                    move.rtStraightPhase = (move.rtCruiseTimer > 0f) ? StraightPhase.Cruise
                                          : (!instantDecel ? StraightPhase.Decel : StraightPhase.Rest);
                }
                else
                {
                    move.rtStraightPhase = StraightPhase.Accel;
                }

                // 如果直接进入 Rest，则立即进入休息
                if (move.rtStraightPhase == StraightPhase.Rest)
                {
                    isResting = true;
                    move.rtRestTimer = Mathf.Max(0f, move.restDuration);
                    // 切休息动画
                    PlayAnimIfNotCurrent(move.restAnimation);

                    // 停驻逻辑
                    desiredSpeedX = 0f;
                    ApplySlopeIdleStopIfNoMove();
                    return;
                }
            }

            // 计算线性加/减速率（秒为单位）
            float accelRate = (move.accelerationTime > 0f)
                ? (Mathf.Max(0.01f, move.moveSpeed) / move.accelerationTime)
                : Mathf.Max(0f, move.acceleration);
            float decelRate = (move.decelerationTime > 0f)
                ? (Mathf.Max(0.01f, move.moveSpeed) / move.decelerationTime)
                : Mathf.Max(0f, move.deceleration);

            // 三相状态机
            switch (move.rtStraightPhase)
            {
                case StraightPhase.Accel:
                    {
                        if (accelRate <= 0f)
                        {
                            move.rtCurrentSpeed = Mathf.Max(0f, move.moveSpeed);
                            move.rtStraightPhase = (move.rtCruiseTimer > 0f) ? StraightPhase.Cruise
                                                      : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest);
                        }
                        else
                        {
                            move.rtCurrentSpeed = Mathf.MoveTowards(move.rtCurrentSpeed, move.moveSpeed, accelRate * Time.deltaTime);
                            if (move.accelerationTime > 0f)
                                move.rtAccelTimer = Mathf.Max(0f, move.rtAccelTimer - Time.deltaTime);

                            if (Mathf.Approximately(move.rtCurrentSpeed, move.moveSpeed) || move.rtAccelTimer <= 0f)
                                move.rtStraightPhase = (move.rtCruiseTimer > 0f) ? StraightPhase.Cruise
                                                          : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest);
                        }
                        break;
                    }
                case StraightPhase.Cruise:
                    {
                        move.rtCurrentSpeed = Mathf.Max(0f, move.moveSpeed);
                        // 只有在 Cruise 时才消耗 moveDuration
                        move.rtCruiseTimer = Mathf.Max(0f, move.rtCruiseTimer - Time.deltaTime);

                        if (move.rtCruiseTimer <= 0f)
                            move.rtStraightPhase = (decelRate > 0f) ? StraightPhase.Decel : StraightPhase.Rest;
                        break;
                    }
                case StraightPhase.Decel:
                    {
                        // 减速阶段：只有当速度真正降到 0 时，才允许进入 Rest
                        if (decelRate <= 0f)
                        {
                            // 无减速：瞬间归零 -> 进入休息
                            move.rtCurrentSpeed = 0f;
                            move.rtStraightPhase = StraightPhase.Rest;
                        }
                        else
                        {
                            move.rtCurrentSpeed = Mathf.MoveTowards(move.rtCurrentSpeed, 0f, decelRate * Time.deltaTime);
                            // 注意：不再用 “move.rtDecelTimer <= 0f” 作为提前进入 Rest 的条件
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
                        // 进入休息：速度必须为 0，并立即开始 restDuration
                        isResting = true;
                        move.rtRestTimer = Mathf.Max(0f, move.restDuration);

                        // 强制水平速度为 0，避免残留速度导致“休息还在滑动”
                        rb.velocity = new Vector2(0f, Mathf.Max(rb.velocity.y, 0f));
                        desiredSpeedX = 0f;

                        // 播休息动画（并保证持续播放）
                        PlayAnimIfNotCurrent(move.restAnimation);

                        // 斜坡停驻锁辅助（冻结重力/清切向）
                        ApplySlopeIdleStopIfNoMove();
                        return;
                    }
            }

            // 障碍/悬崖转向（忽略帧期间禁用；在许可区或自动跳期间禁用）
            bool canTurnNow = !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);
            if (canTurnNow)
            {
                bool wallAhead = CheckWallAhead();
                bool cliffAhead = CheckCliffAhead(); // 已由上面的 canTurnNow 统一控制忽略帧
                if ((wallAhead || cliffAhead) && turnCooldown <= 0f)
                    TurnAround();
            }

            // NEW: 路点仅作为“方向锚点”→ 到点推进（ping-pong），并在允许转向时朝目标点纠正朝向
            WaypointUpdateAndMaybeTurn(canTurnNow);

            // 施加速度（接地沿斜坡投影）
            desiredSpeedX = move.rtCurrentSpeed * FacingSign();
            if (isGroundedMC)
                ApplyProjectedVelocityAlongSlope(Mathf.Abs(desiredSpeedX), FacingSign());
            else
                rb.velocity = new Vector2(desiredSpeedX, rb.velocity.y);

            return;
        }

        // ============== Jump 分支：新增“路点方向锚点”调用 ==============
        if (!isResting && move.type == MovementType.Jump)
        {
            bool canTurnNow = !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);

            // 起跳前，用路点锚点纠正/推进（接地才执行，不在空中转向）
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

        // ============== 休息期：新增“路点方向锚点”调用 ==============
        if (isResting)
        {


            string restAnim =
                (move.type == MovementType.Jump && !string.IsNullOrEmpty(move.jumpRestAnimation))
                    ? move.jumpRestAnimation
                    : move.restAnimation;

            PlayAnimIfNotCurrent(restAnim);

            // NEW: 休息中也用路点锚点（接地）纠正朝向，下一次出发/起跳方向正确
            bool canTurnNow = !inAutoJumpPermitZone && !isAutoJumping && (ignoreCliffFramesLeft <= 0);
            WaypointUpdateAndMaybeTurn(canTurnNow);



            // 每帧保持水平速度为 0
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

                if (keepSameMoveAfterRest)
                {
                    keepSameMoveAfterRest = false;
                    ignoreCliffFramesLeft = Mathf.Max(0, autoJumpIgnoreCliffFrames);
                }

                AdvancePatrolIndex();
            }
            return;
        }
    }

    // 出生：若配置了路点，则设定初始目标并朝向它
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

    // 路点仅作“方向锚点”：到点（或越点）则按 ping-pong 推进；接地且允许转向时纠正朝向
    private void WaypointUpdateAndMaybeTurn(bool canTurnNow)
    {
        var pts = config?.patrolConfig?.patrolPoints;
        if (pts == null || pts.Count == 0) return;
        if (!isGroundedMC) return; // 只在接地时生效：空中允许越过路点

        int count = pts.Count;
        waypointTargetIndex = Mathf.Clamp(waypointTargetIndex, 0, count - 1);
        waypointIndex = Mathf.Clamp(waypointIndex, 0, count - 1);

        Vector2 pos = transform.position;
        float arriveR = Mathf.Max(0.01f, pointArriveRadius);

        // 可能一次落地跨过多个路点：循环消费
        int guard = 0;
        while (guard++ < count)
        {
            Vector2 start = pts[waypointIndex];
            Vector2 target = pts[waypointTargetIndex];

            bool arrived = Vector2.Distance(pos, target) <= arriveR;

            // 沿当前段的X方向是否“越过”目标
            float segDirX = Mathf.Sign(target.x - start.x);
            bool overshot = false;
            if (Mathf.Abs(segDirX) > 0f)
            {
                if (segDirX > 0f) overshot = pos.x >= target.x - arriveR;
                else overshot = pos.x <= target.x + arriveR;
            }

            if (!(arrived || overshot)) break;

            // 到达/越点：推进到下一目标（0..N..0 ping-pong）
            waypointIndex = waypointTargetIndex;
            int next = waypointIndex + waypointDir;
            if (next < 0 || next >= count)
            {
                waypointDir = -waypointDir;
                next = waypointIndex + waypointDir;
            }
            waypointTargetIndex = Mathf.Clamp(next, 0, count - 1);
        }

        // 允许转向时再纠正面朝目标
        Vector2 curTarget = pts[waypointTargetIndex];
        int wantSign = (curTarget.x - pos.x >= 0f) ? +1 : -1;
        if (canTurnNow && turnCooldown <= 0f && wantSign != FacingSign())
            TurnAround();
    }

    // === 地形检测模块 ===
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

        // 将射线起点夹在包围盒内，避免太贴地
        float castY = Mathf.Clamp(b.min.y + wallCheckHeightOffset, b.min.y + 0.05f, b.max.y - 0.05f);
        Vector2 origin = new Vector2(b.center.x, castY);
        Vector2 dir = (Vector2)transform.right;

        float dist = Mathf.Max(0.1f, wallCheckDistance);
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, groundLayer);
        Debug.DrawLine(origin, origin + dir * dist, hit.collider ? Color.red : Color.yellow);

        if (!hit.collider) return false;
        if (hit.collider.isTrigger) return false;                     // 忽略触发器
        if (hit.collider.CompareTag(autoJumpPermitTag)) return false; // 忽略自动跳许可区

        // 新增：如果命中面是“向上”的地面/斜坡（例如≤45°），不视为墙
        Vector2 n = hit.normal.normalized;
        if (n.y >= groundMinNormalYMC) return false; // groundMinNormalYMC 默认 0.70 ≈ 45°

        // 保留原来的“面对近似垂直障碍物”判断
        return Vector2.Dot(n, dir) < -0.4f;
    }

    bool CheckCliffAhead()
    {
        if (col == null) return false;
        // 仅在当前已接地时检查悬崖，避免空中或贴边抖动误判
        if (!CheckGrounded()) return false;

        Bounds b = col.bounds;

        // 射线起点高度：稍高于脚底，避免自碰撞
        float baseY = b.min.y + 0.05f;

        // 允许的下台阶高度（米）：小于等于这个落差不算悬崖
        const float STEP_DOWN_ALLOW = 0.10f;

        // 探测距离：至少覆盖允许落差
        float rayDist = Mathf.Max(cliffCheckDistance, STEP_DOWN_ALLOW + 0.1f);

        // 前脚趾位置与半脚趾位置（减少坡转折处的漏检）
        Vector2 forward = (Vector2)transform.right;
        Vector2 originToe = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX));
        Vector2 originHalf = new Vector2(b.center.x, baseY) + forward * (b.extents.x + Mathf.Max(0.0f, cliffCheckOffsetX) * 0.5f);

        bool IsSafe(Vector2 origin)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, rayDist, groundLayer);
            Debug.DrawLine(origin, origin + Vector2.down * rayDist, hit.collider ? Color.green : Color.blue);

            if (!hit.collider) return false;                          // 未命中：此射线不安全
            if (hit.collider.isTrigger) return true;                   // 触发器不算悬崖
            if (hit.collider.CompareTag(autoJumpPermitTag)) return true; // 自动跳许可区不算地形障碍

            // 关键：先看是否“可行走地面/斜坡”（≤45°），是的话直接安全，不看落差
            if (hit.normal.y >= groundMinNormalYMC) return true;

            // 命中面过陡时，再用“下台阶容差”判断
            float drop = (b.min.y) - hit.point.y;
            return drop <= STEP_DOWN_ALLOW;
        }

        // 任一射线安全，就不是悬崖
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

    // === 发现 / 攻击 ===
    void DiscoveryUpdate()
    {
        if (!player) return;
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= config.attackConfig.attackRange)
            state = MonsterState.Attack;
    }

    void AttackUpdate()
    {
        if (!player) return;
        var atk = config.attackConfig.attackPatterns.Count > 0 ? config.attackConfig.attackPatterns[0] : null;
        if (atk == null) return;

        float dist = Vector2.Distance(transform.position, player.position);
        if (dist > atk.meleeRange)
        {
            state = MonsterState.Discovery;
            return;
        }

        if (!string.IsNullOrEmpty(atk.animation))
            PlayAnimIfNotCurrent(atk.animation);

        Debug.Log($"怪物攻击造成伤害：{atk.damage}");
        state = MonsterState.Discovery;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(autoJumpPermitTag)) return;

        // 注意：路点模式不再早退，保持与非路点一致的自动跳逻辑
        inAutoJumpPermitZone = true;

        // 忽略帧期间 或 未上膛 -> 不触发
        if (!autoJumpReady || ignoreCliffFramesLeft > 0) return;
        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        var move = patrolRuntimeMoves[patrolIndex];

        // 新增：为自动跳按 automoveDuration 规划跳数
        EnsureJumpPlan(move, useAutoParams: true);

        BeginOneJump(move, useAutoParams: true);

        // 触发一次后，等“落地 + 忽略帧结束 + 退出触发器”再上膛
        autoJumpReady = false;
        autoJumpRearmAfterLanding = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(autoJumpPermitTag))
        {
            inAutoJumpPermitZone = false;
            // 注意：此处不再直接上膛；要等“落地+忽略帧结束”才上膛（在 Update 里处理）
        }
    }


    void Die()
    {
        isDead = true;
        if (!string.IsNullOrEmpty(config.deathConfig.deathAnimation))
            PlayAnimIfNotCurrent(config.deathConfig.deathAnimation);
        if (!string.IsNullOrEmpty(config.deathConfig.deathEffect))
            Debug.Log($"播放死亡特效：{config.deathConfig.deathEffect}");
        Destroy(gameObject, config.deathConfig.instantRemove ? 0f : 2f);
        if (spawner) spawner.NotifyMonsterDeath(gameObject);
    }

    private void PlayEffect(GameObject prefab)
    {
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, transform.position, Quaternion.identity, transform);
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

    // 跳跃起始：当 autojumpDuration>0 时，用“高度+总时长”反解出 g 与 v0y；否则按 autogravityScale + 高度
    void BeginOneJump(PatrolMovement move, bool useAutoParams)
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

        // 关键点：自动跳时，无论当前是否已在 jumpAnimation，都强制重置到 t=0，保证动画关键帧再次触发
        if (!string.IsNullOrEmpty(move.jumpAnimation))
        {
            if (useAutoParams)
            {
                animator.CrossFadeInFixedTime(move.jumpAnimation, 0f, 0, 0f);
                animator.Update(0f);
            }
            else
            {
                // 正常跳保持原逻辑：只有不在该状态时才切换，避免重复触发
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName(move.jumpAnimation))
                    PlayAnimIfNotCurrent(move.jumpAnimation);
            }
        }

        float vx = spdX * FacingSign();
        rb.velocity = new Vector2(vx, vY);

        move.rtUsingAutoJumpParams = useAutoParams;
    }

    // 跳跃更新：用 groundedAfterVerticalMove 作为唯一落地判定，避免“提前接地→水平清零”
    void JumpUpdate(PatrolMovement move)
    {
        // 保持水平速度（自动/正常）
        float spdX = move.rtUsingAutoJumpParams ? Mathf.Max(move.autojumpSpeed, 0f)
                                                : Mathf.Max(move.jumpSpeed, 0f);

        // 墙壁转向允许
        if (turnCooldown <= 0f && CheckWallAhead())
        {
            TurnAround();
            turnCooldown = 0.25f;
        }

        // 重力（自动/正常）
        float g = (move.rtUsingAutoJumpParams ? Mathf.Max(0.01f, move.autogravityScale)
                                              : Mathf.Max(0.01f, move.gravityScale)) * BASE_G;

        vY -= g * Time.deltaTime;

        // 仅竖直方向“安全位移”；是否“真正落地”由 groundedAfterVerticalMove 决定
        SafeMoveVertical(vY * Time.deltaTime, groundLayer);

        // 垂直位移后，强制写回水平速度（确保空中全程有 X 速度）
        rb.velocity = new Vector2(spdX * FacingSign(), rb.velocity.y);

        // 只在“真正被竖直夹停的那一帧”判定落地，避免提前进入休息导致的垂直落下
        if (groundedAfterVerticalMove && vY <= 0f)
        {
            vY = 0f;
            isJumping = false;

            // 关键：落地当帧清掉 Rigidbody 的竖直速度，并开启短暂抑制窗口
            rb.velocity = new Vector2(rb.velocity.x, 0f);
            suppressUpwardOnGroundFrames = 2;

            // 自动跳：按连跳计划
            if (move.rtUsingAutoJumpParams)
            {
                if (move.rtExecuteRemain > 0)
                    move.rtExecuteRemain = Mathf.Max(0, move.rtExecuteRemain - 1);

                if (move.rtExecuteRemain > 0)
                {
                    BeginOneJump(move, useAutoParams: true);
                    return;
                }

                // 自动跳序列结束
                isAutoJumping = false;
                if (move.restDuration > 0f)
                {
                    isResting = true;
                    inJumpRestPhase = true;   // 跳跃→休息窗口 开启
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
                // 普通跳：进入休息
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


    /// 安全的竖直位移：在位移前用三条射线预判，若将与地面相撞则把位移夹到地面上方（或下方）
    /// 仅用于跳跃分支，避免直接 Translate 穿透
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
                groundedAfterVerticalMove = true; // 真正被夹住才算落地
        }
        else
        {
            transform.Translate(Vector3.up * dy, Space.World);
        }
    }

    // 统一的三射线竖直探测
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
        return best; // best.collider == null 表示无命中
    }

    // 刷新接地/斜坡信息（简化版）
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

        // 非跳跃且临时失地：用 Rigidbody2D.Cast 形状投射向下“安全吸附”，避免穿透/漏检
        if (!isGroundedMC && !isJumping && col != null)
        {
            const float snapDist = 0.12f; // 允许的最大吸附距离（米），按美术台阶/坡缝调整 0.08~0.15
            var snapFilter = new ContactFilter2D { useTriggers = false };
            snapFilter.SetLayerMask(groundLayer);

            // 向下投射自身形状，找最近可行走面（<=45°）
            RaycastHit2D[] hits = new RaycastHit2D[6];
            int hitCount = rb.Cast(Vector2.down, snapFilter, hits, snapDist);

            int pick = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var h = hits[i];
                if (!h.collider) continue;
                if (h.normal.y < groundMinNormalYMC) continue; // 只吸附到可行走法线
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    pick = i;
                }
            }

            if (pick >= 0)
            {
                // 仅当竖直速度不明显向上时吸附，避免干扰起跳/弹起
                if (rb.velocity.y <= 0.05f)
                {
                    // MovePosition/position 确保不穿透：移动距离 = 命中距离 - 微偏移
                    float gap = Mathf.Max(0f, bestDist - 0.005f);
                    if (gap > 0f)
                        rb.position += Vector2.down * gap;
                }

                isGroundedMC = true;
                groundNormalMC = hits[pick].normal;
            }
        }
    }

    // 沿斜坡投影施加速度（保证上/下坡一致）
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

        // 轻微贴地：下压量调小，避免在薄坡面上“压穿”
        const float stickDown = 0.005f;
        v += -n * stickDown;

        // 接地不允许产生向上分量；叠加上一帧正向vY已在落地抑制里处理
        if (v.y > 0f) v.y = 0f;

        float addUp = (suppressUpwardOnGroundFrames > 0) ? 0f : Mathf.Max(rb.velocity.y, 0f);
        rb.velocity = new Vector2(v.x, v.y + addUp);

        if (suppressUpwardOnGroundFrames > 0) suppressUpwardOnGroundFrames--;
    }

    // 无移动时斜坡停驻锁
    private void ApplySlopeIdleStopIfNoMove()
    {
        if (!isGroundedMC)
        {
            ExitSlopeIdleLockMC();
            return;
        }

        bool onSlope = groundNormalMC.y < 0.999f;

        // 轻微下滑钳制
        var v = rb.velocity;
        if (v.y < slopeStopVyThresholdMC) v.y = 0f;

        // 无移动、在斜坡、允许停驻 -> 冻重力并清切向
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
                // 清切向
                Vector2 n = groundNormalMC.normalized;
                Vector2 t = new Vector2(n.y, -n.x).normalized;
                float vTan = Vector2.Dot(v, t);
                v -= vTan * t;
                if (v.y > 0f) v.y = 0f;
                rb.velocity = v;
                return;
            }
        }

        // 平地兜底：极小切向清零
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

    // 等待某个状态播完（随 Animator.speed 缩放，直到 normalizedTime >= 1）
    private IEnumerator WaitForStateFinished(string stateName, int layer = 0)
    {
        if (string.IsNullOrEmpty(stateName)) yield break;

        // 等待切入该状态
        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (info.IsName(stateName)) break;
            yield return null;
        }

        // 等到本轮播放完成
        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (!info.IsName(stateName)) break; // 被过渡打断
            if (info.normalizedTime >= 1f) break;
            yield return null;
        }
    }

    // 计算单次跳跃的空中时长（秒）：T = 2*sqrt(2*h/g)
    private float ComputeAirTime(bool useAutoParams, PatrolMovement move)
    {
        float g = (useAutoParams ? Mathf.Max(0.01f, move.autogravityScale)
                                 : Mathf.Max(0.01f, move.gravityScale)) * BASE_G;
        float h = useAutoParams ? Mathf.Max(0.01f, move.autojumpHeight)
                                : Mathf.Max(0.01f, move.jumpHeight);
        float v0 = Mathf.Sqrt(Mathf.Max(0.01f, 2f * h * g));
        return 2f * v0 / g;
    }

    // 根据总时长预算生成“剩余跳数”（最少1跳）
    private void EnsureJumpPlan(PatrolMovement move, bool useAutoParams)
    {
        if (move.rtExecuteRemain > 0) return;

        // 普通跳用 jumpDuration；自动跳用 automoveDuration
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
    private GameObject ResolveFxByPrefabName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        var key = prefabName.Trim().ToLowerInvariant();

        // 先查索引
        if (fxLookup.TryGetValue(key, out var pf) && pf != null)
            return pf;

        // 兜底：再从当前上下文临时查一次（避免美术刚改名未重启时的极端情况）
        var sc = config?.spawnConfig;
        if (sc?.spawnEffectPrefab && string.Equals(sc.spawnEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return sc.spawnEffectPrefab;
        if (sc?.idleEffectPrefab && string.Equals(sc.idleEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return sc.idleEffectPrefab;

        if (activeStraightMove?.moveEffectPrefab && string.Equals(activeStraightMove.moveEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return activeStraightMove.moveEffectPrefab;
        if (activeStraightMove?.restEffectPrefab && string.Equals(activeStraightMove.restEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return activeStraightMove.restEffectPrefab;
        if (activeJumpMove?.jumpEffectPrefab && string.Equals(activeJumpMove.jumpEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return activeJumpMove.jumpEffectPrefab;
        if (activeJumpMove?.jumpRestEffectPrefab && string.Equals(activeJumpMove.jumpRestEffectPrefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
            return activeJumpMove.jumpRestEffectPrefab;

        return null;
    }

    // 新增：构建 Prefab 名 → Prefab 的索引（不区分大小写）
    private void BuildFxLookup()
    {
        fxLookup.Clear();

        // 出生/待机
        var sc = config?.spawnConfig;
        if (sc?.spawnEffectPrefab)
            fxLookup[sc.spawnEffectPrefab.name.Trim().ToLowerInvariant()] = sc.spawnEffectPrefab;
        if (sc?.idleEffectPrefab)
            fxLookup[sc.idleEffectPrefab.name.Trim().ToLowerInvariant()] = sc.idleEffectPrefab;

        // 巡逻：把配置里所有 movement 的特效都加入索引（而不是仅“当前段”）
        var moves = config?.patrolConfig?.movements;
        if (moves != null)
        {
            foreach (var m in moves)
            {
                if (m.moveEffectPrefab)
                    fxLookup[m.moveEffectPrefab.name.Trim().ToLowerInvariant()] = m.moveEffectPrefab;
                if (m.restEffectPrefab)
                    fxLookup[m.restEffectPrefab.name.Trim().ToLowerInvariant()] = m.restEffectPrefab;
                if (m.jumpEffectPrefab)
                    fxLookup[m.jumpEffectPrefab.name.Trim().ToLowerInvariant()] = m.jumpEffectPrefab;
                if (m.jumpRestEffectPrefab)
                    fxLookup[m.jumpRestEffectPrefab.name.Trim().ToLowerInvariant()] = m.jumpRestEffectPrefab;
            }
        }
    }

    private void PlayAnimIfNotCurrent(string animName)
    {
        if (string.IsNullOrEmpty(animName)) return;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(animName))
        {
            // 统一的切换：无过渡时间、当帧归零到 t=0，保证关键帧从 0 开始
            animator.CrossFadeInFixedTime(animName, 0f, 0, 0f);
            animator.Update(0f);
        }
    }

    // Fisher–Yates 洗牌
    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // 根据 patrolRuntimeMoves 和 randomOrder 初始化顺序表
    private void BuildMoveOrderFromConfig()
    {
        moveOrder = new List<int>();
        int count = (patrolRuntimeMoves != null) ? patrolRuntimeMoves.Count : 0;
        for (int i = 0; i < count; i++) moveOrder.Add(i);

        moveOrderPos = 0;

        // 启用随机：每一轮（完整播完所有元素）打乱一次
        bool rnd = (config?.patrolConfig != null) && (bool)config.patrolConfig.randomOrder;
        if (rnd && count > 1) Shuffle(moveOrder);

        // 设置当前 patrolIndex
        patrolIndex = (moveOrder.Count > 0) ? moveOrder[0] : 0;
    }

    // 在“一个 movement 完整结束（休息期结束）”时推进到下一个
    private void AdvancePatrolIndex()
    {
        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0) return;

        // movements 数量变化时重建一次
        if (moveOrder == null || moveOrder.Count != patrolRuntimeMoves.Count)
            BuildMoveOrderFromConfig();

        // 仅 1 个元素：天然自循环
        if (moveOrder.Count <= 1)
        {
            patrolIndex = moveOrder[0];
            return;
        }

        // 若有“保持同一段”的需求，可在此尊重 keepSameMoveAfterRest（目前默认 false）
        if (keepSameMoveAfterRest)
        {
            keepSameMoveAfterRest = false; // 消耗标记
            return;
        }

        // 前进到下一个
        moveOrderPos = (moveOrderPos + 1) % moveOrder.Count;

        // 如果走完一轮，且勾选了 randomOrder，则重洗下一轮
        bool rnd = (config?.patrolConfig != null) && (bool)config.patrolConfig.randomOrder;
        if (moveOrderPos == 0 && rnd && moveOrder.Count > 1)
            Shuffle(moveOrder);

        patrolIndex = moveOrder[moveOrderPos];
    }



    // 动画特效关键帧
    private bool IsCurrentState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName) || animator == null) return false;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        return st.IsName(stateName);
    }

    // 出生：spawn 槽位
    public void OnFxSpawn()
    {
        // 仅非 Patrol 阶段有效；必须匹配 spawn 动画且 prefab 已配置
        var sc = config?.spawnConfig;
        if (state == MonsterState.Patrol || sc == null) return;
        if (IsCurrentState(sc.spawnAnimation) && sc.spawnEffectPrefab)
            PlayEffect(sc.spawnEffectPrefab);
    }

    // 出生：idle 槽位
    public void OnFxIdle()
    {
        var sc = config?.spawnConfig;
        if (state == MonsterState.Patrol || sc == null) return;
        if (IsCurrentState(sc.idleAnimation) && sc.idleEffectPrefab)
            PlayEffect(sc.idleEffectPrefab);
    }

    // 巡逻直线：move 槽位
    public void OnFxMove()
    {
        // 仅 Patrol + 非跳跃非休息 + 正在直线段 且 动画匹配 + prefab 已配置
        if (state != MonsterState.Patrol) return;
        var m = activeStraightMove;
        if (m == null) return;
        if (isJumping) return;
        if (isResting) return;
        if (IsCurrentState(m.moveAnimation) && m.moveEffectPrefab)
            PlayEffect(m.moveEffectPrefab);
    }

    // 巡逻直线：rest 槽位
    public void OnFxRest()
    {
        // 仅 Patrol + 非跳跃 + 休息中 + 正在直线段 且 动画匹配 + prefab 已配置
        if (state != MonsterState.Patrol) return;
        var m = activeStraightMove;
        if (m == null) return;
        if (isJumping) return;
        if (!isResting) return;
        if (IsCurrentState(m.restAnimation) && m.restEffectPrefab)
            PlayEffect(m.restEffectPrefab);
    }

    // 跳跃（普通/自动共用）：jump 槽位（空中）
    public void OnFxJump()
    {
        // 仅 Patrol + 跳跃中 + 正在跳段 且 动画匹配 + prefab 已配置
        if (state != MonsterState.Patrol) return;
        var m = activeJumpMove;
        if (m == null) return;
        if (!isJumping) return;
        if (IsCurrentState(m.jumpAnimation) && m.jumpEffectPrefab)
            PlayEffect(m.jumpEffectPrefab);
    }

    //  仅在“跳跃休息窗口”才允许播放 jumpRestEffectPrefab
    public void OnFxJumpRest()
    {
        // 仅 Patrol + 非跳跃 + 正处于“跳跃休息窗口” + 正在跳段 且 动画匹配 + prefab 已配置
        if (state != MonsterState.Patrol) return;
        if (isJumping) return;
        if (!isResting) return;
        if (!inJumpRestPhase) return;          // NEW: 核心门禁

        var m = activeJumpMove;
        if (m == null) return;

        if (IsCurrentState(m.jumpRestAnimation) && m.jumpRestEffectPrefab)
            PlayEffect(m.jumpRestEffectPrefab);
    }
}