using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector (Minimal + Shield + BackFlash)
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private AnimationEventRelay relay;

    [Header("地面检测")]
    [SerializeField] private LayerMask groundLayer;

    [Header("踩墙层")]
    [Tooltip("可踩墙图层（建议墙体的Collider放在这些图层）")]
    [SerializeField] private LayerMask wallMask;

    [Header("碰撞体切换")]
    [Tooltip("站立形态使用的碰撞体（建议 BoxCollider2D ）")]
    [SerializeField] private Collider2D standingCollider;
    [Tooltip("下蹲形态使用的碰撞体（建议 BoxCollider2D ）")]
    [SerializeField] private Collider2D duckCollider;

    [Header("接地/墙面判定")]
    [Tooltip("接地判定所需的最小法线Y。0.7≈允许到45°斜坡，0.86≈允许到30°斜坡")]
    [SerializeField, Range(0f, 1f)] private float groundMinNormalY = 0.70f;

    [Header("墙面滑落限制")]
    [Tooltip("勾选：在贴墙但未落地时，限制向下滑落速度")]
    [SerializeField] private bool limitWallSlide = true;
    [Tooltip("墙面最大向下滑落速度（负数，0表示不下滑）")]
    [SerializeField] private float maxWallSlideSpeed = 0f;
    [Tooltip("判定“近垂直墙”的法线X阈值（0.9≈>~25°内的近垂直）")]
    [SerializeField, Range(0.8f, 1f)] private float wallVerticalNormalX = 0.90f;

    [Tooltip("脚底向下兜底射线的长度（米），用于切换碰撞体时防止瞬间判空地")]
    [SerializeField] private float groundProbeRayDistance = 0.08f;

    [Header("斜坡防滑（无输入时）")]
    [Tooltip("向下速度小于该阈值时钳为0，阻止极慢的下滑（负数，靠近0更“粘”）")]
    [SerializeField] private float slopeStopVyThreshold = -0.05f;
    [Tooltip("沿斜面切向速度绝对值小于该阈值则清零（米/秒）；目前仅用于平地兜底")]
    [SerializeField] private float horizontalStopThresholdOnSlope = 0.02f;
    [Tooltip("斜坡停驻锁（无输入时冻结重力，彻底止滑）")]
    [SerializeField] private bool slopeIdleFreezeGravity = true;      
    [Tooltip("进入锁时的速度门槛（米/秒）")]
    [SerializeField] private float slopeEnterIdleSpeedEpsilon = 0.50f;

    [Header("接地稳定")]
    [SerializeField] private float groundedExitCoyote = 0.08f;

    [Header("台阶抬步 Step Up")]
    [SerializeField] private bool stepUpEnabled = true;
    
    [Tooltip("允许抬步的最大台阶高度（米），典型 0.10~0.18；过大容易“爬墙")]
    [SerializeField] private float stepUpMaxHeight = 0.14f;

    [Tooltip("前方横向探测距离（米），用于确认抬到该高度后前方不再被墙阻挡")]
    [SerializeField] private float stepUpForwardProbe = 0.08f;

    [Tooltip("抬步高度离散次数（越大越细，但稍增开销）")]
    [SerializeField, Range(1, 6)] private int stepUpChecks = 3;

    [Header("移动")]
    public float moveSpeed = 5f;
    public float groundAcceleration = 40f;
    public float groundDeceleration = 60f;
    public float stopThreshold = 0.05f;

    [Header("跳跃")]
    public float jumpForce = 10f;
    public float maxJumpHeight = 5f;
    public bool variableJump = true;
    [Range(0.1f, 1f)] public float jumpCutMultiplier = 0.5f;

    [Header("二段跳 Double Jump")]
    [Tooltip("是否启用二段跳")]
    public bool doubleJumpEnabled = true;
    [Tooltip("二段跳起跳赋予的向上速度")]
    public float doubleJumpForceY = 9f;
    [Tooltip("二段跳最大升高（从二段起跳点起算）；<=0 不限制")]
    public float doubleJumpMaxHeight = 0f;
    [Tooltip("二段跳时的横向速度（0=沿用 moveSpeed；有输入按左右方向，无输入保持当前X速度）")]
    public float doubleJumpSpeedX = 0f;
    [Tooltip("本次离地期间可用的“额外跳跃”次数（1=二段跳；2=三段跳）；落地重置")]
    public int extraJumpsPerAir = 1;

    [Header("下蹲攻击")]
    public bool allowInstantDuckAttack = true;
    public bool allowInstantDuckForwardAttack = true;
    public int duckAttackHorizLockFrames = 4;

    [Header("空中攻击")]
    public float airAttackMinDuration = 0.12f;

    [Header("输入脉冲防丢")]
    public int tapImpulseFrames = 3;

    [Header("空中转向")]
    public bool allowAirImmediateTurn = true;

    [Header("盾 (Shield)")]
    public float shieldStationaryThreshold = 0.05f;
    public bool shieldInstantVisual = true;
    public bool airShieldBlocksAttack = false;

    [Header("后退闪避 (BackFlash)")]
    public float backFlashSpeed = 7f;
    public float backFlashDistance = 2.5f;
    public float backFlashNoInterruptNorm = 0.5f;   // AD 可打断阈值（秒，从 BackFlash 开始计时）
    public float backFlashReTriggerNorm = 0.9f;     // I 自打断阈值（秒，从 BackFlash 开始计时）

    [Header("魔法 (Magic)")]
    public float magicAttackAirDuration = 0.4f;
    [Tooltip("player_magic_attack 结束后到下一次允许施放的冷却秒数")]
    public float magicAttackCooldown = 0.35f;

    [Header("墙面反跳 Wall Jump")]
    public bool wallJumpEnabled = true;
    [Tooltip("水平探测距离（从角色左右发射）")]
    public float wallCheckDistance = 0.4f;
    [Range(0f, 30f)]
    [Tooltip("墙面法线相对水平的夹角阈值（度），越小越接近90°竖直墙")]
    public float wallNormalVerticalToleranceDeg = 15f;
    [Tooltip("反跳水平速度（0=沿用moveSpeed）")]
    public float wallJumpSpeedX = 0f;
    [Tooltip("反跳垂直速度（0=沿用jumpForce）")]
    public float wallJumpSpeedY = 0f;
    [Tooltip("反跳最大高度（<=0 使用 maxJumpHeight）")]
    public float wallJumpMaxHeight = 0f;
    [Tooltip("二次按K的冷却（秒）")]
    public float wallJumpCooldown = 0.10f;
    [Tooltip("墙面探测的上/下采样偏移Y")]
    public float wallCheckYOffset = 0.35f;
    #endregion

    #region Animator 常量
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string TRIG_Attack = "Trig_Attack";
    private const string STATE_IdleStart = "player_idle_start";
    private const string PARAM_IsGrounded = "IsGrounded";
    private const string PARAM_IsDucking = "IsDucking";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_DuckShieldUp = "Trig_DuckShieldUp";
    private const string TRIG_DuckShieldDown = "Trig_DuckShieldDown";
    private const string PARAM_IsFalling = "IsFalling";
    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";
    private const string STATE_JumpAttack = "player_jump_attack";
    private const string STATE_JumpDownFwdAttack = "player_jump_downForward_attack";
    private const string TRIG_JumpDown = "Trig_JumpDown";
    private const string STATE_JumpDown = "player_jump_down";
    private const string STATE_JumpUp = "player_jump_up";
    private const string STATE_JumpDouble = "player_jump_double";
    private const string PARAM_ShieldHold = "ShieldHold";
    private const string TRIG_ShieldUp = "Trig_ShieldUp";
    private const string TRIG_ShieldDown = "Trig_ShieldDown";
    private const string PARAM_MagicHold = "MagicHold";
    private const string PARAM_BackFlashInterruptible = "BackFlashInterruptible";
    private const string TRIG_BackFlash = "Trig_BackFlash";
    private const string STATE_BackFlash = "player_backflash";
    private const string TRIG_Land = "Trig_Land";
    private const string TRIG_WallTurn = "Trig_WallTurn";
    private const float FALL_VY_TRIGGER = -0.05f;
    #endregion

    #region Runtime
    private Animator anim;
    private Rigidbody2D rb;

    // 受伤
    private float currentHP;

    // === 受伤/死亡（新增配置与运行时） ===
    [Header("玩家伤害命中体（PHitbox）")]
    [Tooltip("站立姿态使用的伤害命中体（建议设置为子物体上的 Trigger Collider2D，Layer: EPHitbox）")]
    [SerializeField] private Collider2D hitboxStanding;
    [Tooltip("下蹲姿态使用的伤害命中体（建议设置为子物体上的 Trigger Collider2D，Layer: EPHitbox）")]
    [SerializeField] private Collider2D hitboxDuck;

    [Header("受伤/死亡 参数")]
    [SerializeField] private int maxHP = 100;

    [Header("时间参数 (秒)")]
    [Tooltip("强制位移/硬直时间：此期间玩家受到击退力控制，无法移动")]
    [SerializeField] private float hitForceDuration = 0.2f;

    [Tooltip("无敌时间：此期间玩家处于无敌状态（通常 >= 硬直时间）")]
    [SerializeField] private float hitStunDuration = 0.5f;

    [Header("受击/死亡 特效")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private GameObject deathEffectPrefab;
    [Tooltip("特效生成位置 (如果不填则使用角色 Transform)")]
    [SerializeField] private Transform vfxSpawnPoint;

    [Header("击退力度")]
    [Tooltip("正面受击时的击退力度（X=水平向后退的力，Y=垂直跳起的力）。X通常为负值表示向后，Y为正值表示向上。")]
    [SerializeField] private Vector2 frontHitKnockback = new Vector2(-6f, -3f);
    [Tooltip("背面受击时的击退力度（X=水平向前扑的力，Y=垂直跳起的力）。X通常为正值表示向前，Y为正值表示向上。")]
    [SerializeField] private Vector2 backHitKnockback = new Vector2(6f, -3f);
    [Tooltip("死亡时的击飞力度（无论面向，X总是背离伤害源方向，Y为垂直力）。")]
    [SerializeField] private Vector2 deathKnockback = new Vector2(-4f, -2f);

    [Header("受击来源伤害数值")]
    private int damageMelee = 10;     // MHitbox HIT
    private int damageBody = 5;       // EMHitbox / Monster
    private int damageProjectile = 8; // MProjectile HIT

    // 运行时状态
    private bool isInvulnerable = false;   // 是否无敌
    private bool isKnockback = false;      // 是否处于硬直/不可操作状态

    // 独立计时器
    private float hitForceTimer = 0f;      // 控制硬直（不可操作）
    private float hitStunTimer = 0f;       // 控制无敌

    // 特效驱动动画相关
    private float vfxTimer = 0f;           // 特效剩余时间
    private bool isHitAnimFrozen = false;  // 是否已冻结在最后一帧
    private string currentHitAnimState = ""; // 当前播放的受伤动画名

    // 动画交替索引（0/1）
    private int frontHitVariant = 0;
    private int backHitVariant = 0;
    private int lastHitSourceLayer = -1;

    // 最近一次命中源位置（由 OnTriggerEnter2D 写入）
    private Vector2 lastHitSourcePos = Vector2.zero;

    private bool lastHitWasProjectile = false;
    private float lastProjectileVelX = 0f;

    // 基础状态
    private bool isGrounded;
    private bool prevGrounded;
    private bool isDucking;

    // 接地面法线（斜坡防滑计算用）
    private Vector2 groundNormal = Vector2.up;

    // 攻击
    private bool groundAttackActive;
    private bool duckAttackFacingLocked;
    private int duckAttackHorizLockRemain;

    // 空攻
    private bool airAttackActive;
    private bool airAttackAnimPlaying;
    private float airAttackStartTime;

    // 跳跃
    private bool isJumping;
    private float jumpStartY;

    //二段跳
    private int extraJumpsUsed = 0;
    private bool doubleJumpActive = false;
    private float doubleJumpStartY = 0f;
    private bool doubleJumpPoseHold = false;

    // 输入
    private float rawInputX;
    private bool keyDownLeft;
    private bool keyDownRight;
    private bool keyDownJump;
    private bool keyDownAttack;
    private bool keyDownBackFlash;

    // W 魔法输入
    private bool keyDownMagic;
    private bool magicHeldKey;
    private float magicAttackAvailableAt = 0f;

    // 方向脉冲
    private float tapImpulseDir;
    private int tapImpulseRemain;

    // 水平移动
    private float currentSpeedX;
    private bool facingRight = true;

    // 盾
    private bool shieldHeld;
    private bool shieldActiveStanding;
    private bool shieldActiveDuck;
    private bool shieldActiveAir;
    private bool shieldAnimUpPlayed;
    private bool pendingShieldUp;
    private bool pendingShieldDown;

    // 后退闪避
    private bool backFlashActive = false;
    
    private bool backFlashLock = false;
    private float backFlashStartTime = 0f;
    private float backFlashMaxDuration = 0f;
    private bool backFlashMoving = false;
    private Vector2 backFlashStartPos = Vector2.zero;

    // Magic 运行时
    private bool magicActive = false;
    private bool magicAttackPlaying = false;
    private float magicAttackStartTime = 0f;

    private float landDebounceUntil = 0f;

    //踩墙反跳
    private bool wallTurnActive = false;
    private bool wallJumpAutoPhase = false;
    private bool wallJumpControlUnlocked = false;
    private float savedGravityScale = 0f;
    private float wallJumpStartY = 0f;
    private float wallJumpLastTime = -999f;
    private int wallSide = 0;
    private bool nearWall = false;

    // 当前是否启用的是下蹲碰撞体
    private bool colliderDuckActive = false;

    //墙面侧面判断
    private readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[8];
    private bool touchingGroundSide = false;
    private float groundSideNormalX = 0f;
    private bool slopeIdleLocked = false;
    private float slopeSavedGravity = 0f;

    // 切换站立/下蹲后的短暂“粘地”窗口
    private int crouchGroundStickyFrames = 0;

    // 起跳后短暂屏蔽下蹲输入（帧）
    private int duckInputSuspendFrames = 0;
    private float lastGroundedAt = -999f;
    private int justJumpedFrames = 0;
    private int crouchReenterLockFrames = 0;
    private enum ShieldVisual { None, Standing, Duck, Air }
    private ShieldVisual lastShieldVisual = ShieldVisual.None;
    private bool IsAutoPhaseLocked() => wallJumpAutoPhase && !wallJumpControlUnlocked;
    private bool IsHardLocked() => wallTurnActive;
    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>();

        // 开启玩家刚体插值，让渲染帧看到连续位置（相机 LateUpdate 会更稳）
        if (rb) rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (standingCollider) standingCollider.enabled = true;
        if (duckCollider) duckCollider.enabled = false;
        colliderDuckActive = false;
        currentHP = Mathf.Max(0, maxHP);
        // 受击命中体初始状态：站立启用、下蹲关闭（与碰撞体一致）
        if (hitboxStanding) hitboxStanding.enabled = true;
        if (hitboxDuck) hitboxDuck.enabled = false;
        // 绑定命中体事件转发器（运行时自动挂载；不改资源/Prefab）
        if (hitboxStanding)
            hitboxStanding.gameObject.AddComponent<PlayerHitboxEventRelay>().Init(this);
        if (hitboxDuck)
            hitboxDuck.gameObject.AddComponent<PlayerHitboxEventRelay>().Init(this);
    }

    private void Update()
    {
        UpdateWallProximity();
        CaptureInput();
        CheckGrounded();

        // --- 受伤/硬直/无敌 逻辑 ---
        if (isKnockback)
        {
            // 递减计时器：hitForceDuration 只负责强制位移时间；hitStunDuration 包含硬直/无敌/动画停留时间
            if (hitForceTimer > 0f) hitForceTimer -= Time.deltaTime;
            if (hitStunTimer > 0f) hitStunTimer -= Time.deltaTime;

            //控制动画时长（根据 VFX 时间 + hitStunDuration）★★★
            UpdateHitAnimationByVfxDuration();

            // 锁死输入：整个 hitStunDuration 期间不允许任何按键生效
            rawInputX = 0f;
            keyDownLeft = keyDownRight = keyDownJump = keyDownAttack = keyDownBackFlash = false;
            keyDownMagic = false;
            magicHeldKey = false;
            shieldHeld = false;

            // 强制关闭盾牌
            if (shieldActiveStanding) CancelShieldStanding(false);
            if (shieldActiveDuck) CancelShieldDuck(false);
            if (shieldActiveAir) CancelShieldAir();

            // 1. 强制位移处理
            //    活着：hitForceDuration 结束后清水平速度
            //    已死亡：不再依赖 hitForceTimer，交由下面“死亡最终静止”兜底
            if (currentHP > 0)
            {
                if (hitForceTimer <= 0f)
                {
                    if (Mathf.Abs(currentSpeedX) > 0.01f)
                    {
                        currentSpeedX = 0f;
                        rb.velocity = new Vector2(0f, rb.velocity.y);
                    }
                }
            }

            // 2. hitStunDuration 结束逻辑（硬直 + 无敌 + 受击动画停留整体结束）
            //    活着：结束硬直/无敌 → 恢复操作
            //    已死亡：保持 isKnockback=true，不再恢复操作（死亡阶段全程锁死）
            if (hitStunTimer <= 0f)
            {
                hitStunTimer = 0f;          // 钳到 0，防止反复进入

                if (currentHP > 0)
                {
                    isKnockback = false;
                    isInvulnerable = false;
                }

                // 死亡阶段：当死亡动画停在最后一帧时，强制将水平速度清为 0，彻底静止
                if (currentHP <= 0)
                {
                    var dieState = anim.GetCurrentAnimatorStateInfo(0);
                    if (dieState.IsName("player_die") && dieState.normalizedTime >= 1f)
                    {
                        currentSpeedX = 0f;
                        rb.velocity = new Vector2(0f, rb.velocity.y);
                    }
                }

                // 结束时统一解冻动画速度（死亡也需要，让 Animator 正常停在最后一帧）
                ForceEndHitAnimation();
            }

            // 3. 受击期间落地处理（只做“物理兜底”，不触动画面/状态机）
            //    空中被击飞后落到地面，稳定一下水平速度，避免“在地面继续飘”
            if (!prevGrounded && isGrounded && currentHP > 0)
            {
                // 只兜底 X 速度，不触发 Land 动画，不改 WallJump 等其它逻辑
                if (hitForceTimer <= 0f)
                {
                    if (Mathf.Abs(currentSpeedX) > 0.01f)
                    {
                        currentSpeedX = 0f;
                        rb.velocity = new Vector2(0f, rb.velocity.y);
                    }
                }
            }

            // === 死亡：死亡动画最后一帧时强制刹车 ===
            if (currentHP <= 0)
            {
                // 约定：死亡主状态名为 "player_die"
                var dieInfo = anim.GetCurrentAnimatorStateInfo(0);
                bool inDieState = dieInfo.IsName("player_die");

                // normalizedTime >= 1 表示已经播完一轮；!IsInTransition 保证不在过渡中
                if (inDieState && dieInfo.normalizedTime >= 1.0f && !anim.IsInTransition(0))
                {
                    // 水平方向完全停止，只保留当前竖直速度（一般这里已经很小了）
                    float vy = rb.velocity.y;
                    rb.velocity = new Vector2(0f, vy);
                    currentSpeedX = 0f;

                    if (isGrounded) rb.gravityScale = 0f;
                    rb.velocity = Vector2.zero;
                }
            }

            return;
        }

        // --- 以下为正常控制逻辑 ---

        if (!isGrounded) ExitSlopeIdleLock();

        if (isGrounded && (wallTurnActive || wallJumpAutoPhase))
            ForceExitWallJumpLocks(false);

        HandleJump(); // 跳跃优先

        HandleWallJumpInput();
        PollWallTurnAndLaunch();
        EnforceMaxWallJumpHeight();

        // 盾（不屏蔽 BackFlash，随时可打断）
        if (!(IsHardLocked() || IsAutoPhaseLocked()))
            HandleShield();

        // 魔法
        if (IsHardLocked() || IsAutoPhaseLocked())
            anim.SetBool(PARAM_MagicHold, false);
        else
            HandleMagic();

        // 二段跳相关
        HandleDoubleJump();
        EnforceMaxDoubleJumpHeight();
        HoldDoubleJumpPoseWhileAscending();
        AutoExitDoubleJumpOnFall();

        // BackFlash 触发
        var stNow = anim.GetCurrentAnimatorStateInfo(0);

        bool inDuckAttackEnd = stNow.IsName("player_duck_attack_end") || stNow.IsName("player_duckForward_attack_end");

        // 进入任何“下蹲攻击结束”状态时，立即解除下蹲姿态锁，让 IsDucking 跟随输入
        if (groundAttackActive && inDuckAttackEnd)
            duckAttackFacingLocked = false;

        bool inBackflashAnimOrTransition = stNow.IsName(STATE_BackFlash) || anim.IsInTransition(0);
        if (keyDownBackFlash &&
            isGrounded && !isDucking &&
            !AnyShieldActive() &&
            !groundAttackActive &&
            !backFlashActive &&
            !backFlashLock &&
            !inBackflashAnimOrTransition &&
            !magicAttackPlaying)
        {
            if (magicActive && !magicAttackPlaying) CancelMagic();
            StartBackFlash();
        }
        else if (keyDownBackFlash && (backFlashActive || backFlashLock || inBackflashAnimOrTransition))
        {
            SafeResetTrigger(TRIG_BackFlash);
        }

        // 地面攻击与下蹲
        HandleGroundDuckAndAttacks();

        // 同步站立/下蹲碰撞体
        SyncCrouchColliders();

        // 自动斜跳允许用 J 打断：先解锁再走空攻
        if (IsAutoPhaseLocked() && keyDownAttack)
        {
            wallJumpAutoPhase = false;
            wallJumpControlUnlocked = true;
        }
        if (!IsHardLocked())
            HandleAirAttack();

        // 水平移动
        if (IsHardLocked())
        {
            rb.velocity = Vector2.zero;
            currentSpeedX = 0f;
        }
        else if (!IsAutoPhaseLocked() || doubleJumpActive)
        {
            HandleHorizontal();
        }

        // 斜坡无输入时防滑动
        FreezeOnSlopeWhenNoInput();

        // 贴墙防下滑
        LimitWallSlideIfTouchingSide();

        // 可变跳截断
        HandleVariableJumpCut();

        // 翻转
        if (!IsHardLocked() && !IsAutoPhaseLocked())
        {
            HandleFacingFlip();
        }

        // 常规最大跳高
        EnforceMaxJumpHeight();

        // 空中从 jump_up 切 jump_forward
        HandleAirJumpForwardSwitch();

        // 收尾
        AutoEndAirAttack();
        AutoEndGroundAttack();
        AutoEndBackFlash_ByDistance();
        AutoExitBackFlashOnStateLeave();

        // 仅在“接平地”时才防抬头；斜坡接地不再清正向 vy
        if (backFlashActive && isGrounded && groundNormal.y >= 0.999f && rb.velocity.y > 0f)
            rb.velocity = new Vector2(rb.velocity.x, 0f);

        // backflash 解锁
        var stAfter = anim.GetCurrentAnimatorStateInfo(0);
        if (backFlashLock && !stAfter.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
            backFlashLock = false;

        UpdateAnimatorParams();

        // 统一的“下落动画触发”（加入速度门禁，避免误判）
        if (prevGrounded && !isGrounded && rb.velocity.y < FALL_VY_TRIGGER)
        {
            SafeResetTrigger("Trig_JumpDown");
            SafeSetTrigger("Trig_JumpDown");
        }
        if (!isGrounded && rb.velocity.y <= FALL_VY_TRIGGER)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (!st.IsName("player_jump_down"))
            {
                SafeResetTrigger("Trig_JumpDown");
                SafeSetTrigger("Trig_JumpDown");
            }
        }

        // 统一递减去抖与已存在计数
        prevGrounded = isGrounded;
        if (justJumpedFrames > 0) justJumpedFrames--;
        if (crouchReenterLockFrames > 0) crouchReenterLockFrames--;
    }
    #endregion

    // 播放特效并设置 vfxTimer (用于控制动画时长)
    private void PlayVfx(GameObject prefab, Transform spawnPoint)
    {
        if (prefab == null)
        {
            vfxTimer = 0.5f; // 无特效时的保底时间
            return;
        }

        Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
        GameObject instance = Instantiate(prefab, pos, Quaternion.identity, spawnPoint ? spawnPoint : transform);

        float duration = 1.0f;

        // 1）优先按粒子系统时长
        var ps = instance.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Play(true);                        // ← 确保粒子启动
            duration = ps.main.duration;
            // 特效自行销毁，稍微延迟一点防止刚播完就没
            Destroy(instance, duration + 0.1f);
        }
        else
        {
            // 2）没有粒子系统：如果是带 Animator 的“死亡动画 prefab”，按状态时长销毁
            var animVfx = instance.GetComponentInChildren<Animator>();
            if (animVfx != null)
            {
                animVfx.gameObject.SetActive(true);   // 防止预制体子节点是关着的
                try
                {
                    var info = animVfx.GetCurrentAnimatorStateInfo(0);
                    duration = Mathf.Max(0.1f, info.length);
                }
                catch
                {
                    duration = 2.0f; // 兜底
                }
                Destroy(instance, duration + 0.1f);
            }
            else
            {
                // 3）既没有粒子也没有 Animator：默认给 2 秒
                duration = 2.0f;
                Destroy(instance, 2.0f);
            }
        }
        vfxTimer = duration;
    }

    // === 提供给动画事件用的受击/死亡特效入口（只播特效，不改数值逻辑） ===
    public void PlayHitVfxOnce()
    {
        PlayVfx(hitEffectPrefab, vfxSpawnPoint);
    }

    // === 提供给动画事件用的受击/死亡特效入口（只播特效，不改数值逻辑） ===
    public void PlayDeathVfxOnce()
    {
        // 使用 Inspector 里配置的 deathEffectPrefab + vfxSpawnPoint
        PlayVfx(deathEffectPrefab, vfxSpawnPoint);
    }

    // 由投射物或其它伤害体在命中瞬间通知“伤害源世界位置”
    public void NotifyHitSource(Vector2 srcPos)
    {
        lastHitSourcePos = srcPos;
    }

    // PlayerController.cs 中：替换 ApplyKnockback
    private void ApplyKnockback(Vector2 force, float dxToSource)
    {
        float magX = (Mathf.Abs(force.x) > 0.0001f) ? Mathf.Abs(force.x) : Mathf.Abs(frontHitKnockback.x);
        float finalX = 0f;

        if (lastHitWasProjectile)
        {
            
            // 对于飞行物：优先使用飞行物的水平速度方向决定击退方向
            if (Mathf.Abs(lastProjectileVelX) > 0.0001f)
            {
                finalX = Mathf.Sign(lastProjectileVelX) * magX;
            }
            else
            {
                // 兜底：使用记录的位置决定（飞来的方向）
                float dir = lastHitSourcePos.x - transform.position.x;
                if (Mathf.Abs(dir) < 0.0001f) dir = dxToSource;
                // 注意：此处采用“飞来的方向”，与近战背离源不同
                finalX = (dir > 0f) ? +magX : -magX;
            }
        }
        else
        {
            // 非飞行物（近战/怪物等）：保持原有逻辑，总是背离伤害源
            float dir = lastHitSourcePos.x - transform.position.x;
            if (Mathf.Abs(dir) < 0.0001f) dir = dxToSource;
            finalX = (dir > 0f) ? -magX : +magX;
        }

        rb.velocity = new Vector2(finalX, force.y);
        currentSpeedX = finalX;

        // 使用完成后清标志（避免影响下一次判定）
        lastHitWasProjectile = false;
        lastProjectileVelX = 0f;
    }

    // === 玩家受伤：动画关键帧触发特效（EPHitbox） ===
    public void OnPlayerHitVfx()
    {
        PlayVfx(hitEffectPrefab, vfxSpawnPoint);
    }

    // === 玩家死亡：动画关键帧触发特效（EPDiebox） ===
    public void OnPlayerDieVfx()
    {
        // 死亡流程中再补一次死亡特效（例如地面落地那一刻）
        PlayVfx(deathEffectPrefab, vfxSpawnPoint);
    }

    // 死亡动画最后一帧：由动画事件调用，彻底停止水平移动
    public void OnPlayerDeathAnimEnd()
    {
        if (rb != null)
        {
            rb.velocity = Vector2.zero;   // 水平和垂直都归零，保证尸体完全停住
            currentSpeedX = 0f;
            rb.gravityScale = 0f;
        }
    }

    // --- 根据特效时长控制受伤动画 ---
    private void UpdateHitAnimationByVfxDuration()
    {
        if (!isKnockback) return;       // 仅在受伤阶段处理
        if (vfxTimer > 0f)
            vfxTimer -= Time.deltaTime;

        // 检查当前动画进度
        var stateInfo = anim.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.IsName(currentHitAnimState))
        {
            if (hitStunTimer <= 0f)
            {
                hitStunTimer = 0f;      // 钳到 0，防止重复进入
                ForceEndHitAnimation(); // 立即恢复动画速度，交回状态机

                if (currentHP <= 0 && rb != null)
                {
                    rb.velocity = Vector2.zero; // X/Y 都清零
                    currentSpeedX = 0f;
                }
            }
            else
            {
                if (stateInfo.normalizedTime >= 1.0f)
                {
                    if (!isHitAnimFrozen)
                    {
                        anim.speed = 0f;    // 冻在最后一帧，直到 hitStunTimer 结束
                        isHitAnimFrozen = true;
                    }
                }
            }
        }

        if (vfxTimer <= 0f && hitStunTimer <= 0f)
        {
            ForceEndHitAnimation();
        }
    }

    private void ForceEndHitAnimation()
    {
        if (isHitAnimFrozen)
        {
            anim.speed = 1f;
            isHitAnimFrozen = false;
        }
    }

    // 新增：供飞行物在命中扣血前注入击退方向依据（速度方向 + 源位置）
    public void NotifyProjectileKnockback(Vector2 srcPos, float projectileVelX)
    {
        lastHitWasProjectile = true;
        lastProjectileVelX = projectileVelX;
        lastHitSourcePos = srcPos;
    }

    #region 输入
    private void CaptureInput()
    {
        rawInputX = Input.GetAxisRaw("Horizontal");
        keyDownLeft = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
        keyDownRight = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
        keyDownJump = Input.GetKeyDown(KeyCode.K);
        keyDownAttack = Input.GetKeyDown(KeyCode.J);

        keyDownBackFlash = Input.GetKeyDown(KeyCode.I);
        shieldHeld = Input.GetKey(KeyCode.L);
        keyDownMagic = Input.GetKeyDown(KeyCode.W);
        magicHeldKey = Input.GetKey(KeyCode.W);

        if (keyDownLeft) { tapImpulseDir = -1; tapImpulseRemain = tapImpulseFrames; }
        else if (keyDownRight) { tapImpulseDir = 1; tapImpulseRemain = tapImpulseFrames; }

        if (Mathf.Abs(rawInputX) > 0.01f)
        {
            tapImpulseDir = 0;
            tapImpulseRemain = 0;
        }
        else if (tapImpulseRemain > 0)
        {
            tapImpulseRemain--;
            if (tapImpulseRemain == 0) tapImpulseDir = 0;
        }
    }

    private float GetEffectiveInputDir()
    {
        if (Mathf.Abs(rawInputX) > 0.01f) return rawInputX;
        if (tapImpulseRemain > 0) return tapImpulseDir;
        return 0f;
    }
    #endregion

    #region 跳跃
    private void HandleJump()
    {
        if (keyDownJump && isGrounded && !isJumping && !groundAttackActive && !magicAttackPlaying)
        {
            if (backFlashActive) CancelBackFlash();                // 随时打断
            ExitSlopeIdleLock();
            justJumpedFrames = 2;
            isJumping = true;
            jumpStartY = rb.position.y;

            float dir = GetEffectiveInputDir();
            currentSpeedX = dir * moveSpeed;
            rb.velocity = new Vector2(currentSpeedX, jumpForce);
            
            landDebounceUntil = Time.time + 0.12f;     // 短暂压制“落地去抖”，防被 AnyState 抢占

            isDucking = false;

            ForceStandingCollider();
            crouchGroundStickyFrames = 0; // 起跳时清掉粘地锁

            duckInputSuspendFrames = 2;

            if (shieldActiveDuck) CancelShieldDuck(false);

            if (shieldHeld)
            {
                // 仅在按住 L 时，起跳当帧切空中盾
                shieldActiveAir = true;
                shieldActiveStanding = false;
                shieldActiveDuck = false;

                shieldAnimUpPlayed = false;
                pendingShieldUp = false;

                // 清理鸭盾触发，避免残留
                SafeResetTrigger(TRIG_DuckShieldUp);
                SafeResetTrigger(TRIG_DuckShieldDown);
            }
            else
            {
                // 没按 L：确保所有盾状态关闭（防止任何残留状态引发可视）
                if (shieldActiveStanding) CancelShieldStanding(false);
                if (shieldActiveDuck) CancelShieldDuck(false);
                if (shieldActiveAir) CancelShieldAir();
            }

            // 起跳当帧：恢复触发器并强制切到起跳动画
            string trig = Mathf.Abs(dir) > 0.05f ? TRIG_JumpForward : TRIG_JumpUp;
            SafeResetTrigger(TRIG_JumpForward);
            SafeResetTrigger(TRIG_JumpUp);
            SafeSetTrigger(trig);

            // 就在 SafeSetTrigger(trig); 的下一行插入这两句（关键点）
            anim.CrossFadeInFixedTime(STATE_JumpUp, 0f, 0, 0f);
            anim.Update(0f);

            // 最后再把魔法彻底清掉（现在状态已是 jump_up，不会被切回 idle）
            if (magicActive && !magicAttackPlaying) CancelMagic();
        }

        if (!prevGrounded && isGrounded)
        {
            OnLanding();
        }
    }

    // 把原有的落地重置逻辑提取为一个独立方法
    private void OnLanding()
    {
        // 只有非受伤状态下才去重置 WallJump 锁，避免受伤击飞过程被重置重力
        if (!isKnockback)
        {
            ForceExitWallJumpLocks(true);
        }
        else
        {
            // 受伤期间落地，只简单把 WallJump 状态清掉，不强制改 velocity (否则会在这停住)
            wallTurnActive = false;
            wallJumpAutoPhase = false;
            wallJumpControlUnlocked = false;
            // 恢复重力以免卡在空中
            if (rb.gravityScale == 0f) rb.gravityScale = savedGravityScale != 0f ? savedGravityScale : 1f;
        }

        isJumping = false;
        airAttackActive = false;
        airAttackAnimPlaying = false;
        relay?.StopAttackHitbox();

        // 无论是否受伤，都重置动画触发器，防止积压
        SafeResetTrigger(TRIG_JumpUp);
        SafeResetTrigger(TRIG_JumpForward);
        SafeResetTrigger("Trig_JumpDown");

        // 仅在非受伤时触发 Land 动画
        if (!isKnockback && Time.time >= landDebounceUntil)
        {
            landDebounceUntil = Time.time + 0.05f;
            SafeResetTrigger(TRIG_Land);
            SafeSetTrigger(TRIG_Land);
        }

        // 恢复盾（仅非受伤）
        if (!isKnockback && shieldHeld && !shieldActiveStanding && !shieldActiveDuck)
            TryActivateStandingShield(true);

        extraJumpsUsed = 0;
        doubleJumpActive = false;
        doubleJumpPoseHold = false;
    }
    private void HandleAirJumpForwardSwitch()
    {
        if (isGrounded) return;
        if (rb.velocity.y <= 0f) return;

        // 起跳的前几帧不允许切前进起跳，先把 jump_up 展示出来
        if (justJumpedFrames > 0) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool hasHorizInputNow = keyDownLeft || keyDownRight || Mathf.Abs(GetEffectiveInputDir()) > 0.01f;

        if (st.IsName("player_jump_up") && hasHorizInputNow)
        {
            SafeResetTrigger(TRIG_JumpUp);
            SafeSetTrigger(TRIG_JumpForward);
        }
    }

    private void HandleVariableJumpCut()
    {
        if (!variableJump) return;
        if (IsHardLocked() || IsAutoPhaseLocked()) return;

        if (Input.GetKeyUp(KeyCode.K) && rb.velocity.y > 0f)
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutMultiplier);
    }

    private void EnforceMaxJumpHeight()
    {
        if (isJumping && rb.velocity.y > 0f)
        {
            float deltaH = rb.position.y - jumpStartY;
            if (deltaH >= maxJumpHeight)
                rb.velocity = new Vector2(rb.velocity.x, 0f);
        }
    }
    #endregion

    #region Shield
    private bool IsStationaryHorizontally() =>
        Mathf.Abs(currentSpeedX) <= shieldStationaryThreshold &&
        Mathf.Abs(GetEffectiveInputDir()) < 0.01f;

    private void HandleShield()
    {
        // 攻击时强制取消盾
        if (groundAttackActive)
        {
            if (shieldActiveStanding) CancelShieldStanding(true);
            if (shieldActiveDuck) CancelShieldDuck(true);
            if (shieldActiveAir) CancelShieldAir();
            return;
        }

        // 盾随时打断魔法/后退闪避（不受不可打断窗口限制）
        if ((backFlashActive || magicActive) && shieldHeld && !magicAttackPlaying)
        {
            if (backFlashActive) CancelBackFlash();
            if (magicActive) CancelMagic();
        }

        if (!isGrounded) // 空中：只改状态，不播可视
        {
            // 空中攻击进行时：硬禁空中盾（按住 L 也无效）
            if (airAttackActive || airAttackAnimPlaying)
            {
                if (shieldActiveAir) CancelShieldAir();
                return;
            }

            if (shieldHeld)
            {
                if (!shieldActiveAir)
                {
                    shieldActiveAir = true;
                    CancelShieldStanding(false);
                    CancelShieldDuck(false);
                }
            }
            else if (shieldActiveAir)
            {
                CancelShieldAir();
            }
            return;
        }

        // 地面：只改状态，不播可视
        bool duckKey = Input.GetKey(KeyCode.S);
        if (duckKey)
        {
            isDucking = true;
            if (shieldHeld)
            {
                if (!shieldActiveDuck)
                {
                    CancelShieldStanding(false);
                    CancelShieldAir();
                    ActivateDuckShield();
                }
                else if (!shieldAnimUpPlayed)
                {
                    if (shieldInstantVisual || IsStationaryHorizontally())
                        PlayDuckShieldUp();
                    else
                        pendingShieldUp = true;
                }
            }
            else
            {
                if (shieldActiveDuck) CancelShieldDuck(true);
            }
            return;
        }
        else
        {
            isDucking = false;
        }

        // 站立盾
        if (shieldHeld)
        {
            if (!shieldActiveStanding)
            {
                CancelShieldDuck(false);
                CancelShieldAir();
                TryActivateStandingShield(true);
            }
            else if (!shieldAnimUpPlayed)
            {
                if (shieldInstantVisual || IsStationaryHorizontally())
                    PlayStandingShieldUp();
                else
                    pendingShieldUp = true;
            }
        }
        else
        {
            if (shieldActiveStanding)
            {
                if (shieldInstantVisual || IsStationaryHorizontally())
                    CancelShieldStanding(true);
                else
                {
                    pendingShieldDown = true;
                    shieldActiveStanding = false;
                    shieldAnimUpPlayed = false;
                }
            }
        }

        // 消费 pending（只发 trigger，不播可视）
        if (pendingShieldUp && (shieldInstantVisual || IsStationaryHorizontally()) && shieldHeld)
        {
            pendingShieldUp = false;
            PlayStandingShieldUp();
        }
        if (pendingShieldDown && (shieldInstantVisual || IsStationaryHorizontally()))
        {
            pendingShieldDown = false;
            anim.SetTrigger(TRIG_ShieldDown);
        }
    }

    private void TryActivateStandingShield(bool playAnimationIfStill)
    {
        shieldActiveStanding = true;
        shieldActiveDuck = false;
        shieldActiveAir = false;
        shieldAnimUpPlayed = false;

        if (shieldInstantVisual || (playAnimationIfStill && IsStationaryHorizontally()))
            PlayStandingShieldUp();
        else
            pendingShieldUp = true;
    }

    private void PlayStandingShieldUp()
    {
        SafeResetTrigger(TRIG_ShieldDown);
        SafeSetTrigger(TRIG_ShieldUp);
        shieldAnimUpPlayed = true;
        
    }

    private void ActivateDuckShield()
    {
        shieldActiveDuck = true;
        shieldActiveStanding = false;
        shieldActiveAir = false;
        shieldAnimUpPlayed = false;
        if (shieldInstantVisual || IsStationaryHorizontally())
            PlayDuckShieldUp();
        else
            pendingShieldUp = true;
    }

    private void PlayDuckShieldUp()
    {
        SafeResetTrigger(TRIG_DuckShieldDown);
        SafeSetTrigger(TRIG_DuckShieldUp);
        shieldAnimUpPlayed = true;
        
    }

    private void ForceStandingCollider()
    {
        if (!standingCollider || !duckCollider) return;
        if (!colliderDuckActive) return;

        duckCollider.enabled = false;
        standingCollider.enabled = true;
        colliderDuckActive = false;
        Physics2D.SyncTransforms();
        // 同步：受击命中体切换为站立
        if (hitboxDuck) hitboxDuck.enabled = false;
        if (hitboxStanding) hitboxStanding.enabled = true;
    }

    private void CancelShieldStanding(bool playDownAnim)
    {
        if (!shieldActiveStanding) return;
        if (playDownAnim && shieldAnimUpPlayed && (shieldInstantVisual || IsStationaryHorizontally()))
            SafeSetTrigger(TRIG_ShieldDown);
        shieldActiveStanding = false;
        shieldAnimUpPlayed = false;
        pendingShieldUp = false;
        
    }

    private void CancelShieldDuck(bool playDownAnim)
    {
        if (!shieldActiveDuck) return;
        if (playDownAnim && shieldAnimUpPlayed && (shieldInstantVisual || IsStationaryHorizontally()))
            SafeSetTrigger(TRIG_DuckShieldDown);
        shieldActiveDuck = false;
        duckAttackFacingLocked = false;
        shieldAnimUpPlayed = false;
        pendingShieldUp = false;
        
    }

    private void CancelShieldAir()
    {
        shieldActiveAir = false;
        
    }

    private bool AnyShieldActive() =>
        shieldActiveStanding || shieldActiveDuck || shieldActiveAir;
    #endregion

    #region 后退闪避 BackFlash
    private void StartBackFlash()
    {
        if (backFlashLock || backFlashActive) return;
        backFlashLock = true;
        backFlashActive = true;
        backFlashMoving = true;

        ExitSlopeIdleLock();

        
        backFlashStartPos = rb.position;

        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f);

        currentSpeedX = 0f;

        ApplyBackFlashVelocity();
    }

    private void RestartBackFlashOverride()
    {
        backFlashActive = true;
        backFlashMoving = true;
        backFlashStartPos = rb.position;
        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f);

        currentSpeedX = 0f;

        ApplyBackFlashVelocity();
    }

    private void CancelBackFlash()
    {
        if (!backFlashActive) return;
        backFlashActive = false;
        backFlashMoving = false;
    }

    private void AutoEndBackFlash_ByDistance()
    {
        if (!backFlashActive || !backFlashMoving) return;

        float traveled = Vector2.Distance(rb.position, backFlashStartPos);

        bool reachDistance = traveled >= backFlashDistance;
        bool reachTime = (Time.time - backFlashStartTime) >= backFlashMaxDuration;

        if (reachDistance || reachTime)
        {
            backFlashMoving = false;

            float vy = rb.velocity.y;
            if (isGrounded && vy > 0f) vy = 0f;

            rb.velocity = new Vector2(0f, vy);
            currentSpeedX = 0f;
        }
    }

    // 修复点 1：只有在“位移已经结束”后，且动画确实离开了 BackFlash，才清理 backFlashActive。
    // 不再这里动 backFlashMoving（位移结束由 AutoEndBackFlash_ByDistance 负责）。
    private void AutoExitBackFlashOnStateLeave()
    {
        if (!backFlashActive) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);

        // 位移未结束：即使动画已离开，也先维持 backFlashActive=true，让阈值判定继续生效
        if (backFlashMoving) return;

        // 位移已结束：当动画离开 BackFlash 且不在过渡中，才把 active 关掉
        if (!st.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
        {
            backFlashActive = false;
            // backFlashMoving 已由 AutoEndBackFlash_ByDistance 置 false，这里不重复处理
        }
    }
    #endregion

    private void ApplyBackFlashVelocity()
    {
        float signX = facingRight ? -1f : 1f;

        if (isGrounded && groundNormal.y < 0.999f)
        {
            Vector2 n = groundNormal.normalized;
            Vector2 t = new Vector2(n.y, -n.x).normalized;

            Vector2 vA = t * backFlashSpeed;
            Vector2 vB = -t * backFlashSpeed;
            Vector2 v = (Mathf.Sign(signX) == Mathf.Sign(vA.x)) ? vA : vB;

            if (v.y >= 0f)
            {
                const float backFlashStickDown = 0.10f;
                v += -n * backFlashStickDown;
            }

            rb.velocity = v;
        }
        else
        {
            rb.velocity = new Vector2(signX * backFlashSpeed, rb.velocity.y);
        }

        currentSpeedX = 0f;
    }

    // 新增：从本次 BackFlash 开始后已经过去的秒数
    private float GetBackFlashElapsedSeconds()
    {
        if (!backFlashActive) return 0f;
        return Mathf.Max(0f, Time.time - backFlashStartTime);
    }


    // 新增：判断“AD 时间门槛是否还在生效”
    private bool IsBackFlashADGateActive()
    {
        if (!backFlashActive) return false;
        float elapsed = GetBackFlashElapsedSeconds();
        return elapsed < Mathf.Max(0f, backFlashNoInterruptNorm);
    }

    #region 魔法 Magic
    private bool CanPlayMagicAnimOnGround()
    {
        if (!isGrounded) return false;
        if (Mathf.Abs(currentSpeedX) > shieldStationaryThreshold) return false;
        if (Mathf.Abs(GetEffectiveInputDir()) > 0.01f) return false;
        if (isDucking) return false;
        if (AnyShieldActive()) return false;
        if (backFlashActive) return false;
        if (groundAttackActive) return false;
        return true;
    }

    private void HandleMagic()
    {
        anim.SetBool(PARAM_MagicHold, magicHeldKey && magicActive);

        if (magicAttackPlaying)
        {
            if (isGrounded)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                if (!st.IsName("player_magic_attack") || st.normalizedTime >= 0.98f)
                    EndMagicAttack();
            }
            else
            {
                if (Time.time - magicAttackStartTime >= magicAttackAirDuration)
                    EndMagicAttack();
            }
            return;
        }

        if (magicHeldKey && keyDownAttack)
        {
            if (Time.time >= magicAttackAvailableAt)
            {
                magicActive = true;
                anim.SetBool(PARAM_MagicHold, true);
                StartMagicAttack();
            }
            return;
        }

        if (shieldHeld || AnyShieldActive())
        {
            if (magicActive) CancelMagic();
            return;
        }

        if (keyDownMagic)
        {
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);

            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger("Trig_MagicUp");
                anim.SetTrigger("Trig_MagicUp");
            }
        }

        if (magicHeldKey && !magicAttackPlaying)
        {
            bool blocked = IsMagicAnimBlockedByOtherActions();

            if (!magicActive)
            {
                if (!blocked)
                {
                    magicActive = true;
                    anim.SetBool(PARAM_MagicHold, true);
                    anim.ResetTrigger("Trig_MagicUp");
                    anim.SetTrigger("Trig_MagicUp");
                }
            }
            else
            {
                if (blocked)
                {
                    CancelMagic();
                }
            }
        }

        if (!magicHeldKey && magicActive)
        {
            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger("Trig_MagicDown");
                anim.SetTrigger("Trig_MagicDown");
            }
            CancelMagic();
        }
    }
    private void StartMagicAttack()
    {
        magicAttackPlaying = true;
        magicAttackStartTime = Time.time;

        if (isGrounded && CanPlayMagicAnimOnGround()) { anim.ResetTrigger("Trig_MagicAttack"); anim.SetTrigger("Trig_MagicAttack"); }
    }

    private void EndMagicAttack()
    {
        magicAttackPlaying = false;

        magicAttackAvailableAt = Time.time + magicAttackCooldown;

        if (!magicHeldKey)
        {
            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger("Trig_MagicDown");
                anim.SetTrigger("Trig_MagicDown");
            }
            magicActive = false;
            anim.SetBool(PARAM_MagicHold, false);
        }
        else
        {
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);
        }
    }

    private void CancelMagic()
    {
        if (magicAttackPlaying) return;

        magicActive = false;
        anim.SetBool(PARAM_MagicHold, false);

        anim.ResetTrigger("Trig_MagicUp");
        anim.ResetTrigger("Trig_MagicAttack");
        anim.ResetTrigger("Trig_MagicDown");

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("player_shield_idle") || st.IsName("player_magic_up") || st.IsName("player_magic_down"))
        {
            anim.CrossFadeInFixedTime(STATE_IdleStart, 0f, 0, 0f);
            anim.Update(0f);
        }
    }

    private bool IsMagicAnimBlockedByOtherActions()
    {
        // 起跳当帧一律屏蔽魔法可视
        if (isJumping /* 或者 justJumpedFrames > 0 */) return true;

        if (!isGrounded) return true;
        if (Mathf.Abs(currentSpeedX) > shieldStationaryThreshold) return true;
        if (Mathf.Abs(GetEffectiveInputDir()) > 0.01f) return true;
        if (isDucking) return true;
        if (AnyShieldActive()) return true;
        if (backFlashActive) return true;
        if (groundAttackActive) return true;
        return false;
    }
    #endregion

    #region 下蹲 & 地面攻击
    private void HandleGroundDuckAndAttacks()
    {
        // 放宽：允许在 coyote 窗口内仍然处理下蹲（解决地面判定抖动导致不切换的问题）
        bool groundedOrCoyote = isGrounded || (Time.time - lastGroundedAt <= groundedExitCoyote);

        if (!groundedOrCoyote) { isDucking = false; return; }
        if (isJumping) { isDucking = false; return; }

        // 追加：攻击期间不允许按下切换碰撞体，保持当前姿态
        if (groundAttackActive)
        {
            // 仅保留“鸭攻锁朝向”时的下蹲态，其余保持站立
            isDucking = duckAttackFacingLocked;
            return;
        }

        bool justLandedNow = !prevGrounded && groundedOrCoyote;

        if (duckInputSuspendFrames > 0) duckInputSuspendFrames--;

        // 盾优先保持下蹲形态
        if (shieldActiveDuck)
        {
            isDucking = true;
        }
        else
        {
            // 落地瞬间且仍按着 S：锁存为“下蹲”，避免斜坡/接缝抖动导致二次触发
            if (justLandedNow && Input.GetKey(KeyCode.S))
            {
                isDucking = true;
                crouchReenterLockFrames = 3; // 可按需微调为 2~4
            }

            // 锁存期内强制保持下蹲
            else if (crouchReenterLockFrames > 0)
            {
                isDucking = true;
            }
            // 常规：无锁存时按输入决定下蹲
            else
            {
                isDucking = (duckInputSuspendFrames == 0) && Input.GetKey(KeyCode.S);
            }
        }

        if (isDucking && magicActive && !magicAttackPlaying) CancelMagic();

        if (keyDownAttack && AnyShieldActive())
        {
            CancelShieldStanding(true);
            CancelShieldDuck(true);
            CancelShieldAir();
        }

        if (magicActive || magicAttackPlaying)
            return;

        if (isDucking && keyDownAttack && allowInstantDuckAttack && !groundAttackActive && !AnyShieldActive())
        {
            StartDuckAttack();
            return;
        }

        if (!isDucking && keyDownAttack && !groundAttackActive && !AnyShieldActive())
        {
            StartGroundAttack();
        }
    }
    private void StartGroundAttack()
    {
        if (backFlashActive) CancelBackFlash();                    // 随时打断
        if (magicActive && !magicAttackPlaying) CancelMagic();
        if (magicAttackPlaying) return;

        // 门禁：处于攻击主段或正在过渡时，不重新触发（防“连续两次”）
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("player_attack") || anim.IsInTransition(0)) return;

        groundAttackActive = true;
        anim.SetTrigger(TRIG_Attack);
    }
    private void StartDuckAttack()
    {
        if (magicActive && !magicAttackPlaying) CancelMagic();
        if (magicAttackPlaying) return;

        groundAttackActive = true;
        duckAttackFacingLocked = true;
        duckAttackHorizLockRemain = duckAttackHorizLockFrames;
        float dir = GetEffectiveInputDir();
        bool forward = allowInstantDuckForwardAttack && Mathf.Abs(dir) > 0.01f;
        anim.SetTrigger(forward ? TRIG_DuckFwdAttack : TRIG_DuckAttack);
    }

    public void OnAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
        if (shieldHeld && isGrounded && !isDucking)
            TryActivateStandingShield(true);
        else if (shieldHeld && isGrounded && isDucking)
            ActivateDuckShield();
    }

    public void OnDuckAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
        if (shieldHeld && isGrounded && isDucking)
            ActivateDuckShield();
    }
    #endregion

    #region 空中攻击
    private void HandleAirAttack()
    {
        if (isGrounded) return;
        if (airAttackActive) return;
        if (shieldActiveAir && airShieldBlocksAttack) return;
        if (magicAttackPlaying) return;

        if (keyDownAttack && !magicActive)
        {
            if (shieldActiveAir && !airShieldBlocksAttack)
                CancelShieldAir();
            // 同步参数：避免状态机因 ShieldHold=true 抢占空攻过渡
            SafeSetBool(PARAM_ShieldHold, false);

            var st = anim.GetCurrentAnimatorStateInfo(0);
            foreach (var a in new[] { STATE_JumpAttack, STATE_JumpDownFwdAttack })
                if (st.IsName(a)) return;

            doubleJumpActive = false;
            doubleJumpPoseHold = false;

            airAttackActive = true;
            airAttackAnimPlaying = true;
            airAttackStartTime = Time.time;

            bool downFwd = Input.GetKey(KeyCode.S);
            anim.SetTrigger(downFwd ? TRIG_JumpDownFwdAttack : TRIG_JumpAttack);
        }
    }
    public void OnAirAttackEnd()
    {
        if (!airAttackActive) return;
        if (Time.time - airAttackStartTime < airAttackMinDuration) return;
        airAttackActive = false;
        airAttackAnimPlaying = false;

        if (!isGrounded && shieldHeld)
        {
            shieldActiveAir = true;
            
        }
    }
    private void AutoEndAirAttack()
    {
        if (!airAttackActive) return;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool inAtk = false;
        foreach (var a in new[] { STATE_JumpAttack, STATE_JumpDownFwdAttack })
        {
            if (st.IsName(a))
            {
                inAtk = true;
                if (st.normalizedTime >= 0.98f &&
                    Time.time - airAttackStartTime >= airAttackMinDuration)
                {
                    airAttackActive = false;
                    airAttackAnimPlaying = false;
                    if (shieldHeld && !isGrounded) shieldActiveAir = true;
                }
                break;
            }
        }
        if (!inAtk && airAttackAnimPlaying &&
            Time.time - airAttackStartTime >= airAttackMinDuration)
        {
            airAttackActive = false;
            airAttackAnimPlaying = false;
            relay?.StopAttackHitbox();          // ← 兜底
            if (shieldHeld && !isGrounded) shieldActiveAir = true;
        }
    }

    // PlayerController.cs 中：替换 HandleHitFromCollider 方法中的伤害判定部分
    private void HandleHitFromCollider(Collider2D other)
    {
        if (!other) return;

        // 只在非死亡且非受伤无敌时记录并生效
        if (currentHP <= 0 || isInvulnerable) return;

        int layer = other.gameObject.layer;
        string lname = LayerMask.LayerToName(layer);

        // 记录最近一次命中源位置（用于朝向 & 击退方向）
        lastHitSourcePos = other.bounds.center;

        // 重置飞行物标志
        lastHitWasProjectile = false;
        lastProjectileVelX = 0f;

        // 如果是飞行物层，尝试记录其水平速度（attachedRigidbody）
        if (lname == "projectile" || lname == "Projectile" || lname == "MProjectile HIT")
        {
            lastHitWasProjectile = true;
            var otherRb = other.attachedRigidbody;
            if (otherRb != null)
                lastProjectileVelX = otherRb.velocity.x;
            else
                lastProjectileVelX = other.bounds.center.x - transform.position.x; // 兜底（位置差）
        }

        // 近战 / 怪物身体 / 飞行物 伤害
        // 修改：现在 mhitbox 图层混合了近战和身体，需要通过物体名字区分
        if (lname == "mhitbox" || lname == "MHitbox" || lname == "MHitbox HIT" ||
            lname == "monster" || lname == "Monster" ||
            lname == "projectile" || lname == "Projectile" || lname == "MProjectile HIT")
        {
            int dmg = 0;

            // 优先从命中源的 MonsterDamageProfile 读取伤害数值
            var prof = other.GetComponentInParent<MonsterDamageProfile>();

            // 1. 判断是否是飞行物
            bool isProj = (lname == "projectile" || lname == "Projectile" || lname == "MProjectile HIT");

            // 2. 判断是否是怪物身体 (Body)
            // 逻辑：如果图层是 Monster (旧逻辑兼容)，或者 图层是 mhitbox 且 物体名字叫 "MHitbox" (新需求)
            bool isBody = (lname == "monster" || lname == "Monster") ||
                          (lname.Contains("mhitbox") || lname.Contains("MHitbox") && other.name.Equals("MHitbox"));

            if (isProj)
            {
                // 飞行物伤害
                dmg = (prof != null) ? Mathf.Max(0, prof.ProjectileDamage) : Mathf.Max(0, damageProjectile);
            }
            else if (isBody)
            {
                // 贴身伤害 (Body)
                dmg = (prof != null) ? Mathf.Max(0, prof.BodyDamage) : Mathf.Max(0, damageBody);
            }
            else
            {
                // 剩下的情况认为是：近战攻击 (Melee)
                // 即：图层是 mhitbox，且名字不是 "MHitbox" (通常是 Hitbox_Attack 等)
                dmg = (prof != null) ? Mathf.Max(0, prof.MeleeDamage) : Mathf.Max(0, damageMelee);
            }

            TakeDamage(dmg);
            return;
        }

        // 即死触发器（如悬崖）
        if (lname == "DeathZone" && other.isTrigger)
        {
            currentHP = 0;
            TakeDamage(0); // 0伤也会触发死亡检测
        }
    }

    // 地面攻击自动收尾（防止动画事件缺失导致卡死）
    private void AutoEndGroundAttack()
    {
        if (!groundAttackActive) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool inGroundAtk =
            st.IsName("player_attack") ||
            st.IsName("player_duck_attack") ||
            st.IsName("player_duckForward_attack");

        // 仍在攻击主状态或过渡中就不收尾
        if (inGroundAtk || anim.IsInTransition(0)) return;

        // 仅当进入“攻击收尾段”且接近结束时才兜底收尾，避免中途解锁移动
        bool inGroundAtkEnd =
            st.IsName("player_attack_end") ||
            st.IsName("player_duck_attack_end") ||
            st.IsName("player_duckForward_attack_end");
        if (!inGroundAtkEnd || st.normalizedTime < 0.95f) return;

        // 已进入 End 段且即将结束：按当前姿态触发既有结束流程
        relay?.StopAttackHitbox();              // ← 兜底
        if (isDucking) OnDuckAttackEnd();
        else OnAttackEnd();
    }

    private void ForceEndAirAttack()
    {
        if (!airAttackActive && !airAttackAnimPlaying) return;
        airAttackActive = false;
        airAttackAnimPlaying = false;
    }
    #endregion

    #region 水平移动
    // 替换：HandleHorizontal 中 BackFlash 分支——AD/I 窗口基于“时间阈值（秒）”，AD 优先，且无“位移结束即放开”
    private void HandleHorizontal()
    {
        // BackFlash 分支：优先且早返回
        if (backFlashActive)
        {
            float elapsed = GetBackFlashElapsedSeconds();

            bool allowMoveInterrupt = (elapsed >= Mathf.Max(0f, backFlashNoInterruptNorm));
            SafeSetBool(PARAM_BackFlashInterruptible, allowMoveInterrupt);

            if (Input.GetKey(KeyCode.S))
            {
                rb.velocity = new Vector2(0f, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }

            float input = GetEffectiveInputDir();
            if (allowMoveInterrupt && Mathf.Abs(input) > 0.01f)
            {
                CancelBackFlash();
                currentSpeedX = input * moveSpeed;
                // 不 return，落到下面的常规移动
            }
            else
            {
                bool allowReTrigger = (elapsed >= Mathf.Max(0f, backFlashReTriggerNorm));
                if (keyDownBackFlash)
                {
                    if (allowReTrigger)
                    {
                        RestartBackFlashOverride();
                        return;
                    }
                    else
                    {
                        anim.ResetTrigger(TRIG_BackFlash);
                    }
                }

                if (backFlashMoving)
                {
                    ApplyBackFlashVelocity();
                    return;
                }

                // 位移已结束 且 未达时间门槛：保持静止并留在 BackFlash 分支，禁止 AD/I
                rb.velocity = new Vector2(0f, rb.velocity.y);
                currentSpeedX = 0f;
                return; // ← 关键：防止掉入常规移动
            }
        }

        // ====== 常规移动（BackFlash 之外） ======
        if (magicAttackPlaying && isGrounded)
        {
            ApplyHorizontal(0f);
            return;
        }
        if (groundAttackActive && duckAttackFacingLocked)
        {
            if (duckAttackHorizLockRemain > 0) duckAttackHorizLockRemain--;
            ApplyHorizontal(0f);
            return;
        }
        if (shieldActiveDuck)
        {
            ApplyHorizontal(0f);
            return;
        }
        if (groundAttackActive && !duckAttackFacingLocked)
        {
            ApplyHorizontal(0f);
            return;
        }
        if (isDucking)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 常规加减速
        float dir = GetEffectiveInputDir();
        float target = dir * moveSpeed;

        float accel = Mathf.Abs(dir) > 0.01f ? groundAcceleration : groundDeceleration;
        currentSpeedX = Mathf.MoveTowards(currentSpeedX, target, accel * Time.deltaTime);
        if (Mathf.Abs(currentSpeedX) < stopThreshold) currentSpeedX = 0f;

        rb.velocity = new Vector2(currentSpeedX, rb.velocity.y);

        ProjectGroundedVelocityAlongSlope();

        if (isGrounded && touchingGroundSide && !backFlashActive)
        {
            float inX = GetEffectiveInputDir();
            bool pushingIntoWall =
                Mathf.Abs(inX) > 0.01f &&
                ((inX > 0f && groundSideNormalX < 0f) ||
                 (inX < 0f && groundSideNormalX > 0f));

            if (pushingIntoWall &&
         justJumpedFrames == 0 && !isJumping && rb.velocity.y <= 0f)
            {
                float dirSign = Mathf.Sign(inX);

                // 先试抬步，小“竖口/台阶”直接抬上去；超过阈值（真墙）则按原逻辑刹停
                if (!TryStepUpSmallLedge(dirSign))
                {
                    currentSpeedX = 0f;
                    rb.velocity = new Vector2(0f, rb.velocity.y);
                }
                // 抬步成功则不刹车，保持本帧速度（下一帧会继续向前）
            }
        }

        if (shieldActiveStanding && shieldHeld && !shieldAnimUpPlayed && !pendingShieldDown && !IsStationaryHorizontally())
            pendingShieldUp = true;

        if (Mathf.Abs(dir) > 0.01f && magicActive && !magicAttackPlaying)
            CancelMagic();
    }

    private void ApplyHorizontal(float x)
    {
        currentSpeedX = x;
        rb.velocity = new Vector2(x, rb.velocity.y);
    }
    #endregion

    public bool IsInBackFlashAnimOrTransition()
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0))
        {
            var next = anim.GetNextAnimatorStateInfo(0);
            return st.IsName(STATE_BackFlash) || next.IsName(STATE_BackFlash);
        }
        return st.IsName(STATE_BackFlash);
    }

    private void LateUpdate()
    {
        if (IsInBackFlashAnimOrTransition()) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool inGroundAttackMain =
            st.IsName("player_attack") ||
            st.IsName("player_duck_attack") ||
            st.IsName("player_duckForward_attack");
        bool inAirAttackMain =
            st.IsName(STATE_JumpAttack) ||
            st.IsName(STATE_JumpDownFwdAttack);

        if (!(inGroundAttackMain || inAirAttackMain))
            relay?.StopAttackHitbox();          // ← 巡检兜底

        UpdateShieldVisuals();
    }

    // 仅负责根据状态渲染（relay），绝不改 shieldActiveX 布尔
    private void UpdateShieldVisuals()
    {
        ShieldVisual desired =
            shieldActiveDuck ? ShieldVisual.Duck :
            (shieldActiveStanding ? ShieldVisual.Standing :
            (shieldActiveAir ? ShieldVisual.Air : ShieldVisual.None));

        if (desired == lastShieldVisual) return;

        // 先必要时停止旧的，再按需要播放新的
        if (desired == ShieldVisual.None)
        {
            relay?.StopShield();
        }
        else
        {
            // 你的空中盾复用站立盾可视
            if (desired == ShieldVisual.Duck) relay?.PlayShieldDuck();
            else /* Standing or Air */ relay?.PlayShieldStanding();
        }

        lastShieldVisual = desired;
    }

    #region Facing
    private void HandleFacingFlip()
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);



        if (backFlashActive /* 全程禁用转向 */ || (magicAttackPlaying && isGrounded)) return;

        if (airAttackActive || st.IsName(STATE_JumpAttack) || st.IsName(STATE_JumpDownFwdAttack)) return;

        float dir = GetEffectiveInputDir();
        if (Mathf.Abs(dir) < 0.01f) return;

        if (groundAttackActive && (duckAttackFacingLocked || !isDucking)) return;

        if (shieldActiveAir && !allowAirImmediateTurn) return;

        bool wantRight = dir > 0;
        if (wantRight == facingRight) return;
        facingRight = wantRight;
        if (flipRoot)
            flipRoot.localScale = facingRight ? Vector3.one : new Vector3(-1, 1, 1);
    }
    #endregion

    #region Ground Check



    // PATCH: 粘地 Ray 的“兜底长度”加大，快速站/蹲切换时更容易重新挂住地面
    private void CheckGrounded()
    {
        // 先缓存上一帧“接地外观”信息，用于 coyote 或锁定时维持稳定
        var prevN = groundNormal;
        var prevTouchSide = touchingGroundSide;
        var prevSideNx = groundSideNormalX;

        touchingGroundSide = false;
        bool groundedByBody = false;
        bool groundedByRay = false;
        groundSideNormalX = 0f;

        Vector2 bestGroundNormal = Vector2.up;
        float bestNy = -1f;

        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(groundLayer);

        int count = rb.GetContacts(filter, contactBuffer);
        for (int i = 0; i < count; i++)
        {
            var cp = contactBuffer[i];
            Vector2 n = cp.normal;

            if (n.y >= groundMinNormalY)
            {
                groundedByBody = true;
                if (n.y > bestNy) { bestNy = n.y; bestGroundNormal = n; }
            }
            if (Mathf.Abs(n.x) >= wallVerticalNormalX && n.y > -0.1f)
            {
                touchingGroundSide = true;
                groundSideNormalX = n.x;
            }
        }

        Collider2D bodyCol = colliderDuckActive && duckCollider ? duckCollider : standingCollider;
        if (!groundedByBody && bodyCol)
        {
            var b = bodyCol.bounds;
            Vector2 bottomCenter = new Vector2(b.center.x, b.min.y + 0.01f);
            float halfW = b.extents.x * 0.8f;
            Vector2[] origins = new[]
            {
            bottomCenter + new Vector2(-halfW, 0f),
            bottomCenter,
            bottomCenter + new Vector2(+halfW, 0f),
        };

            for (int i = 0; i < origins.Length; i++)
            {
                var hit = Physics2D.Raycast(origins[i], Vector2.down, groundProbeRayDistance, groundLayer);
                if (hit && hit.normal.y >= groundMinNormalY)
                {
                    groundedByRay = true;
                    if (hit.normal.y > bestNy) { bestNy = hit.normal.y; bestGroundNormal = hit.normal; }
                    break;
                }
            }
        }

        // 有真实命中就刷新“最后接地时间”
        if (groundedByBody || groundedByRay) lastGroundedAt = Time.time;

        // 基础的稳定接地判定
        bool groundedStable = groundedByBody || groundedByRay || (Time.time - lastGroundedAt <= groundedExitCoyote);

        // 关键1：处于斜坡静止锁时，一律视为接地，且刷新“最后接地时间”，避免锁内掉出接地
        if (slopeIdleLocked)
        {
            groundedStable = true;
            lastGroundedAt = Time.time;
        }

        isGrounded = groundedStable;

        // 关键2：仅凭 coyote/锁定而“接地”时，沿用上一帧地面法线与侧墙信息，避免瞬间变平地导致抖动
        bool hasAnyHitThisFrame = groundedByBody || groundedByRay;
        if (hasAnyHitThisFrame)
        {
            groundNormal = bestGroundNormal;
            // touchingGroundSide/groundSideNormalX 已在上面按命中更新
        }
        else if (groundedStable)
        {
            groundNormal = prevN;
            touchingGroundSide = prevTouchSide;
            groundSideNormalX = prevSideNx;
        }
        else
        {
            groundNormal = Vector2.up;
            // touchingGroundSide 已是 false
        }

        // 粘地窗口期间：加长 Ray，进一步兜底挂地（长度从原 3x/0.06 提升到 4.5x/0.10）
        if (!isGrounded && crouchGroundStickyFrames > 0)
        {
            Collider2D stickyBodyCol = colliderDuckActive && duckCollider ? duckCollider : standingCollider;
            if (stickyBodyCol)
            {
                var b = stickyBodyCol.bounds;
                Vector2 bottomCenter = new Vector2(b.center.x, b.min.y + 0.005f);
                float halfW = b.extents.x * 0.85f;
                Vector2[] origins = new[]
                {
                bottomCenter + new Vector2(-halfW, 0f),
                bottomCenter,
                bottomCenter + new Vector2(+halfW, 0f),
            };

                float stickyProbe = Mathf.Max(groundProbeRayDistance * 4.5f, 0.10f);
                for (int i = 0; i < origins.Length; i++)
                {
                    var hit = Physics2D.Raycast(origins[i], Vector2.down, stickyProbe, groundLayer);
                    if (hit && hit.normal.y >= groundMinNormalY)
                    {
                        isGrounded = true;
                        groundNormal = hit.normal;
                        break;
                    }
                }
            }
        }

        if (crouchGroundStickyFrames > 0) crouchGroundStickyFrames--;
    }
    #endregion

    #region Animator
    private void UpdateAnimatorParams()
    {
        float inputAbs = Mathf.Abs(GetEffectiveInputDir());
        float animSpeed = Mathf.Abs(currentSpeedX);

        if (backFlashActive)
        {
            animSpeed = 0f;
        }
        else
        {
            // 起跳的前几帧压制 MoveSpeed，保证 jump_up 能显示
            if (justJumpedFrames > 0)
            {
                animSpeed = 0f;
            }
            else
            {
                // 贴墙“跑步原地”
                if (IsPushingIntoWallNow())
                {
                    animSpeed = Mathf.Max(animSpeed, moveSpeed * inputAbs);
                }
                // 落地后一瞬间仍按方向但物理速度尚未起来：给最低动画速度
                else if (isGrounded && prevGrounded && inputAbs > 0.01f && animSpeed < stopThreshold)
                {
                    animSpeed = Mathf.Max(animSpeed, moveSpeed * 0.5f * inputAbs);
                }
            }
        }

        SafeSetFloat(PARAM_MoveSpeed, animSpeed);

        // 空攻期间硬压 ShieldHold=false，防止 L 覆盖攻击动画
        if (!isGrounded && (airAttackActive || airAttackAnimPlaying))
            SafeSetBool(PARAM_ShieldHold, false);
        else
            SafeSetBool(PARAM_ShieldHold, shieldHeld);

        SafeSetBool(PARAM_ShieldHold, shieldHeld);
        SafeSetBool(PARAM_IsGrounded, isGrounded);
        SafeSetBool(PARAM_IsDucking,
            isDucking ||
            shieldActiveDuck ||
            (groundAttackActive && duckAttackFacingLocked));
        SafeSetBool(PARAM_IsFalling, !isGrounded && rb.velocity.y < FALL_VY_TRIGGER);
    }
    #endregion

    private void ExitSlopeIdleLock()
    {
        if (!slopeIdleLocked) return;
        slopeIdleLocked = false;
        rb.gravityScale = slopeSavedGravity;
    }

    #region Slope/Wall helpers
    // FreezeOnSlopeWhenNoInput：在“无水平输入 + 斜坡”时，直接清掉切向分量，避免落坡后一帧沿斜面滑动
    private void FreezeOnSlopeWhenNoInput()
    {
        // BackFlash 位移中不干预
        if (backFlashActive && backFlashMoving)
        {
            ExitSlopeIdleLock();
            return;
        }

        // 未接地/锁定中则退出锁
        if (!isGrounded || IsHardLocked() || IsAutoPhaseLocked())
        {
            ExitSlopeIdleLock();
            return;
        }

        bool adGateBlockingNow = IsBackFlashADGateActive() && !backFlashMoving;

        bool noHorizInput =
            adGateBlockingNow ||
            IsPushingIntoWallNow() ||
            Mathf.Abs(GetEffectiveInputDir()) < 0.01f ||
            isDucking || shieldActiveDuck;

        if (!noHorizInput)
        {
            // 有输入时确保释放停驻锁
            ExitSlopeIdleLock();
            return;
        }

        bool onSlope = groundNormal.y < 0.999f;

        // 起跳上升期不处理
        if (isJumping && rb.velocity.y > 0f) return;

        // 斜坡停驻锁：无输入且速度很小时，冻结重力并清零速度
        if (slopeIdleFreezeGravity && onSlope)
        {
            if (!slopeIdleLocked)
            {
                if (isDucking || shieldActiveDuck ||
                    rb.velocity.sqrMagnitude <= slopeEnterIdleSpeedEpsilon * slopeEnterIdleSpeedEpsilon)
                {
                    slopeSavedGravity = rb.gravityScale;
                    rb.gravityScale = 0f;
                    slopeIdleLocked = true;
                }
            }

            if (slopeIdleLocked)
            {
                rb.velocity = Vector2.zero;
                currentSpeedX = 0f;
                return;
            }
        }

        // 常规防滑：清掉切向速度，并对很小的下滑 vy 做钳制
        var v = rb.velocity;

        // 轻微下滑钳制：vy 在一个很小的负数以下就压回 0，避免慢慢“溜坡”
        if (v.y < slopeStopVyThreshold) v.y = 0f;

        Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x).normalized;
        float vTan = Vector2.Dot(v, tangent);

        if (onSlope)
        {
            // 无输入时直接去掉切向分量
            v -= vTan * tangent;
            // 同时不允许出现向上的小抬头
            if (v.y > 0f) v.y = 0f;
        }
        else
        {
            // 平地兜底（可保留/可去掉）
            if (Mathf.Abs(vTan) < horizontalStopThresholdOnSlope) v -= vTan * tangent;
        }

        rb.velocity = v;
    }

    private void LimitWallSlideIfTouchingSide()
    {
        if (!limitWallSlide) return;
        if (isGrounded) return;
        if (!touchingGroundSide) return;

        if (rb.velocity.y < maxWallSlideSpeed)
        {
            rb.velocity = new Vector2(rb.velocity.x, maxWallSlideSpeed);
        }
    }
    #endregion


    private bool HasParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        if (!a) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.type == type && p.name == name) return true;
        }
        return false;
    }


    public bool IsGroundedNow => isGrounded;

    private void UpdateWallProximity()
    {
        nearWall = false;
        wallSide = 0;

        if (!wallJumpEnabled || isGrounded) return;

        float nyTol = Mathf.Sin(wallNormalVerticalToleranceDeg * Mathf.Deg2Rad);

        Vector2 pos = transform.position;
        Vector2[] sampleOffsets = {
            new Vector2(0f, wallCheckYOffset),
            new Vector2(0f, -wallCheckYOffset),
        };

        foreach (var off in sampleOffsets)
        {
            var hit = Physics2D.Raycast(pos + off, Vector2.right, wallCheckDistance, wallMask);
            if (hit && Mathf.Abs(hit.normal.y) <= nyTol && hit.normal.x < 0f)
            {
                nearWall = true; wallSide = +1; return;
            }
        }
        foreach (var off in sampleOffsets)
        {
            var hit = Physics2D.Raycast(pos + off, Vector2.left, wallCheckDistance, wallMask);
            if (hit && Mathf.Abs(hit.normal.y) <= nyTol && hit.normal.x > 0f)
            {
                nearWall = true; wallSide = -1; return;
            }
        }
    }

    private void HandleWallJumpInput()
    {
        if (!wallJumpEnabled) return;
        if (IsHardLocked() || IsAutoPhaseLocked()) return;

        if (airAttackActive || airAttackAnimPlaying) return;

        if (isGrounded || !nearWall) return;
        if (!keyDownJump) return;
        if (Time.time - wallJumpLastTime < wallJumpCooldown) return;

        float dir = GetEffectiveInputDir();
        if (Mathf.Abs(dir) <= 0.01f || Mathf.Sign(dir) != wallSide) return;

        ForceEndAirAttack();

        wallTurnActive = true;
        wallJumpAutoPhase = false;
        wallJumpControlUnlocked = false;

        wallJumpStartY = rb.position.y;
        savedGravityScale = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.velocity = Vector2.zero;
        currentSpeedX = 0f;

        bool wantRight = (wallSide == -1);
        if (facingRight != wantRight)
        {
            facingRight = wantRight;
            if (flipRoot) flipRoot.localScale = facingRight ? Vector3.one : new Vector3(-1, 1, 1);
        }

        anim.ResetTrigger(TRIG_WallTurn);
        anim.CrossFadeInFixedTime("player_jump_turn_wall", 0f, 0, 0f);
        anim.Update(0f);

        wallJumpLastTime = Time.time;
    }

    private void PollWallTurnAndLaunch()
    {
        if (!wallTurnActive) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("player_jump_turn_wall"))
        {
            if (st.normalizedTime < 0.98f) return;
        }
        else
        {
            if (anim.IsInTransition(0)) return;
        }

        rb.gravityScale = savedGravityScale;
        float xSpd = (wallJumpSpeedX > 0f ? wallJumpSpeedX : moveSpeed);
        float ySpd = (wallJumpSpeedY > 0f ? wallJumpSpeedY : jumpForce);
        int away = (wallSide == +1) ? -1 : +1;

        rb.velocity = new Vector2(away * xSpd, ySpd);

        anim.ResetTrigger(TRIG_JumpForward);
        anim.SetTrigger(TRIG_JumpForward);

        wallTurnActive = false;
        wallJumpAutoPhase = true;
        wallJumpControlUnlocked = false;
        wallJumpStartY = rb.position.y;

        isJumping = true;
        jumpStartY = rb.position.y;

        doubleJumpActive = false;
        doubleJumpPoseHold = false;
    }

    public void OnWallJumpForwardUnlock()
    {
        wallJumpControlUnlocked = true;
        wallJumpAutoPhase = false;
    }

    private void EnforceMaxWallJumpHeight()
    {
        if (wallTurnActive) return;
        if ((wallJumpAutoPhase && !wallJumpControlUnlocked) || (!isGrounded && isJumping))
        {
            if (rb.velocity.y > 0f)
            {
                float maxH = (wallJumpMaxHeight > 0f) ? wallJumpMaxHeight : maxJumpHeight;
                float deltaH = rb.position.y - wallJumpStartY;
                if (deltaH >= maxH)
                    rb.velocity = new Vector2(rb.velocity.x, 0f);
            }
        }
    }

    private void ForceExitWallJumpLocks(bool stopHorizontalImmediately)
    {
        if (!wallTurnActive && !wallJumpAutoPhase) return;

        wallTurnActive = false;
        wallJumpAutoPhase = false;
        wallJumpControlUnlocked = false;

        rb.gravityScale = savedGravityScale;

        if (stopHorizontalImmediately)
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        SafeResetTrigger(TRIG_WallTurn);
        SafeResetTrigger(TRIG_JumpForward);
    }

    private void HandleDoubleJump()
    {
        if (!doubleJumpEnabled) return;
        if (isGrounded) { doubleJumpActive = false; return; }
        if (IsHardLocked()) return;
        if (magicAttackPlaying) return;

        if (keyDownJump && extraJumpsUsed < extraJumpsPerAir)
        {
            if (airAttackActive || airAttackAnimPlaying) return;

            extraJumpsUsed++;
            doubleJumpActive = true;
            doubleJumpPoseHold = true;
            doubleJumpStartY = rb.position.y;

            float dir = GetEffectiveInputDir();
            float xSpd = Mathf.Abs(dir) > 0.01f
                ? (doubleJumpSpeedX > 0f ? doubleJumpSpeedX : moveSpeed) * Mathf.Sign(dir)
                : rb.velocity.x;

            rb.velocity = new Vector2(xSpd, doubleJumpForceY);
            currentSpeedX = xSpd;
            isJumping = true;

            SafeResetTrigger(TRIG_JumpUp);
            SafeResetTrigger(TRIG_JumpForward);
            SafeResetTrigger("Trig_JumpDown");
            SafeResetTrigger(TRIG_JumpAttack);
            SafeResetTrigger(TRIG_JumpDownFwdAttack);
            anim.CrossFadeInFixedTime(STATE_JumpDouble, 0f, 0, 0f);
            anim.Update(0f);

            if (wallJumpAutoPhase && !wallJumpControlUnlocked)
            {
                wallJumpAutoPhase = false;
                wallJumpControlUnlocked = true;
            }
        }
    }

    private void EnforceMaxDoubleJumpHeight()
    {
        if (!doubleJumpActive) return;
        if (doubleJumpMaxHeight <= 0f) return;
        if (rb.velocity.y > 0f)
        {
            float deltaH = rb.position.y - doubleJumpStartY;
            if (deltaH >= doubleJumpMaxHeight)
                rb.velocity = new Vector2(rb.velocity.x, 0f);
        }
    }

    private void HoldDoubleJumpPoseWhileAscending()
    {
        if (!doubleJumpActive) return;
        if (rb.velocity.y <= 0f) { doubleJumpPoseHold = false; return; }

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName(STATE_JumpDouble) && !anim.IsInTransition(0) && st.normalizedTime >= 0.98f && doubleJumpPoseHold)
        {
            anim.CrossFade(STATE_JumpDouble, 0f, 0, 0.98f);
            anim.Update(0f);
        }
    }

    private void AutoExitDoubleJumpOnFall()
    {
        if (!doubleJumpActive) return;
        if (rb.velocity.y <= 0f)
        {
            doubleJumpActive = false;
            doubleJumpPoseHold = false;
        }
    }

    private void SyncCrouchColliders()
    {
        if (!standingCollider || !duckCollider) return;

        // 只禁止在“起跳态”中切换，放宽对 isGrounded 的要求，解决抖动导致的切换失败
        if (isJumping) return;

        bool wantDuck = isDucking;
        if (wantDuck == colliderDuckActive) return;

        if (wantDuck)
        {
            standingCollider.enabled = false;
            duckCollider.enabled = true;
            colliderDuckActive = true;
        }
        else
        {
            duckCollider.enabled = false;
            standingCollider.enabled = true;
            colliderDuckActive = false;
        }

        Physics2D.SyncTransforms();

        // 【关键逻辑】同步受击 Hitbox 的启用状态
        // 确保 Hitbox_Standing 只在站立时启用，Hitbox_Duck 只在下蹲时启用
        // 这对应了 requirements 中的 "need to switch them"
        if (hitboxStanding) hitboxStanding.enabled = !colliderDuckActive;
        if (hitboxDuck) hitboxDuck.enabled = colliderDuckActive;

        // 保持更长的粘地窗口，减少极端抖动
        crouchGroundStickyFrames = 5;
    }

    private bool IsPushingIntoWallNow()
    {
        if (!isGrounded || !touchingGroundSide || backFlashActive) return false;

        float inX = GetEffectiveInputDir();
        if (Mathf.Abs(inX) <= 0.01f) return false;

        bool pushingRightIntoRightWall = (inX > 0f && groundSideNormalX < 0f);
        bool pushingLeftIntoLeftWall = (inX < 0f && groundSideNormalX > 0f);

        return pushingRightIntoRightWall || pushingLeftIntoLeftWall;
    }


    // PlayerController.cs 中：可以在玩家本体的 OnTriggerEnter2D 入口加一行日志（可选）
    void OnTriggerEnter2D(Collider2D other)
    {
        HandleHitFromCollider(other);
    }

    // 命中体子物体的事件转发入口
    public void OnPlayerHitboxTriggerEnter2D(Collider2D other)
    {
        HandleHitFromCollider(other);
    }

    // Collision 兜底
    public void OnPlayerHitboxCollisionEnter2D(Collision2D col)
    {
        var other = col.collider;
        if (!other) return;
        HandleHitFromCollider(other);
    }

    // 斜坡上投影移动：不钳制向上分量；轻微贴地；回写 currentSpeedX 保持加速手感一致
    private void ProjectGroundedVelocityAlongSlope()
    {
        if (!isGrounded) return;
        if (isJumping) return;
        if (backFlashActive) return;

        float input = GetEffectiveInputDir();
        if (Mathf.Abs(input) <= 0.01f) return;

        // 顶墙时不做投影（FreezeOnSlopeWhenNoInput 已处理稳定贴墙不抖）
        if (IsPushingIntoWallNow()) return;

        // 平地无需投影
        if (groundNormal.y >= 0.999f) return;

        Vector2 n = groundNormal.normalized;
        Vector2 tangent = new Vector2(n.y, -n.x).normalized;

        // 以“期望水平速度”的绝对值作为切向速度标量
        float speedMag = Mathf.Abs(currentSpeedX);
        float dirSign = Mathf.Sign(input);

        Vector2 vAlong = tangent * (speedMag * dirSign);

        // 极轻的贴地（避免离地/抖动），把 0.02 调小到 0.01，减少在接缝处被“卡口”概率
        const float slopeStickDown = 0.01f;
        vAlong += -n * slopeStickDown;

        rb.velocity = vAlong;

        // 回写 currentSpeedX：用“切向速度的大小”驱动加减速（而不是世界X速度），上坡不再感到变慢
        currentSpeedX = vAlong.magnitude * dirSign;
    }

    #region 受伤与死亡逻辑
    // 玩家受击入口（扣血、击退、特效、动画）
    public void TakeDamage(int damage)
    {
        // 铁律：仅在受伤窗口才无敌
        if (isInvulnerable) return;

        int dmg = Mathf.Max(0, damage);
        currentHP -= dmg;

        // 1. 立即打断当前所有动作（清理中间态）
        ForceStopActionsOnHit();

        // 判定死亡
        if (currentHP <= 0)
        {
            currentHP = 0;
            StartDeath();
            return;
        }

        // --- 受伤逻辑 ---

        // 2. 判定击退方向与力度
        // hitFromFront: 源在角色前方
        bool sourceIsRight = lastHitSourcePos.x > transform.position.x;
        bool hitFromFront = (facingRight && sourceIsRight) || (!facingRight && !sourceIsRight);

        Vector2 kbConfig = hitFromFront ? frontHitKnockback : backHitKnockback;

        // 3. 应用击退速度
        float dxToSource = lastHitSourcePos.x - transform.position.x;
        ApplyKnockback(kbConfig, dxToSource);

        // 4. 设置状态与计时器：
        //    hitForceDuration：纯“被击飞位移”时间；
        //    hitStunDuration：包含“硬直时间 + 无敌时间”的总时长（硬直结束=无敌结束）
        isKnockback = true;
        isInvulnerable = true;

        hitForceTimer = hitForceDuration; // 仅控制强制位移阶段
        hitStunTimer = hitStunDuration;   // 统一控制硬直+无敌整体时长

        // 5. 先确定要播的受击动画状态，再立刻播放特效（保证“动画一开始就有特效”）
        if (hitFromFront)
        {
            currentHitAnimState = (frontHitVariant == 0) ? "player_frontHit1" : "player_frontHit2";
            frontHitVariant = 1 - frontHitVariant;
        }
        else
        {
            currentHitAnimState = (backHitVariant == 0) ? "player_backHit1" : "player_backHit2";
            backHitVariant = 1 - backHitVariant;
        }
        PlayVfx(hitEffectPrefab, vfxSpawnPoint);  // 受击动画开始时立即在锚点生成并跟随

        // 6. 播放受击动画
        // 确保动画速度重置
        anim.speed = 1f;
        isHitAnimFrozen = false;
        anim.CrossFadeInFixedTime(currentHitAnimState, 0f, 0, 0f);

        Debug.Log($"玩家受到 {dmg} 点伤害，剩余 HP: {currentHP}");
    }

    // 【用于受伤时清理所有残留状态】
    private void ForceStopActionsOnHit()
    {
        // 1. 打断 BackFlash
        if (backFlashActive) CancelBackFlash();
        backFlashLock = false;

        // 2. 打断 魔法
        if (magicActive) CancelMagic();

        // 3. 打断 普通攻击 / 下蹲攻击
        if (groundAttackActive)
        {
            groundAttackActive = false;
            duckAttackFacingLocked = false;
            relay?.StopAttackHitbox(); // 立即关闭攻击框
            relay?.StopWeapon();       // ★新增：强制调用 StopWeapon 关闭武器显示
        }

        // 4. 打断 空中攻击
        if (airAttackActive || airAttackAnimPlaying)
        {
            airAttackActive = false;
            airAttackAnimPlaying = false;
            relay?.StopAttackHitbox();
            relay?.StopWeapon();       // ★新增：强制调用 StopWeapon 关闭武器显示
        }

        // 5. 打断 踩墙 / 自动跳
        if (wallTurnActive || wallJumpAutoPhase)
        {
            ForceExitWallJumpLocks(false);
        }

        // 6. 重置盾牌相关的中间态
        shieldActiveStanding = false;
        shieldActiveDuck = false;
        shieldActiveAir = false;
        shieldAnimUpPlayed = false;
        pendingShieldUp = false;
        pendingShieldDown = false;
        relay?.StopShield();           // ★新增：强制关闭盾牌显示（防止残留）

        // 7. 确保重力恢复 (防止从斜坡锁定状态被打飞)
        if (rb.gravityScale == 0f)
        {
            rb.gravityScale = (savedGravityScale != 0f) ? savedGravityScale :
                              ((slopeSavedGravity != 0f) ? slopeSavedGravity : 1f);
        }
    }

    // 死亡流程
    private void StartDeath()
    {
        // 1. 播放死亡特效
        PlayVfx(deathEffectPrefab, vfxSpawnPoint);

        // 2. 施加死亡击飞
        float dx = lastHitSourcePos.x - transform.position.x;
        ApplyKnockback(deathKnockback, dx);

        // 3. 状态锁定
        isKnockback = true;
        isInvulnerable = true;
        // 死亡后不再恢复控制（hitStunTimer 只是用来控制动画冻结，不再解锁输入）
        hitForceTimer = 9999f;
        hitStunTimer = 9999f;

        // 4. 播放死亡动画（保持已有状态机命名）
        anim.speed = 1f;                                  // 确保不会是冻结状态进入
        anim.CrossFadeInFixedTime("player_die", 0f, 0, 0f);
        anim.Update(0f);
    }

    private bool TryStepUpSmallLedge(float dirSign)
    {
        if (!stepUpEnabled) return false;
        if (!isGrounded) return false;
        if (backFlashActive || IsHardLocked() || IsAutoPhaseLocked()) return false;

        Collider2D bodyCol = colliderDuckActive && duckCollider ? duckCollider : standingCollider;
        if (!bodyCol) return false;

        var b = bodyCol.bounds;

        LayerMask stepMask = groundLayer | wallMask;

        int checks = Mathf.Max(1, stepUpChecks);
        float stepUnit = stepUpMaxHeight / checks;

        var hits = new RaycastHit2D[4];
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(stepMask);

        for (int i = 1; i <= checks; i++)
        {
            float raise = stepUnit * i;

            // 1) 头顶净空
            int hitCount = bodyCol.Cast(Vector2.up, filter, hits, raise);
            if (hitCount > 0) break;

            // 2) 前方不再被墙挡
            float newFootY = b.min.y + raise + 0.01f;
            Vector2 fwdOrigin = new Vector2(b.center.x, newFootY);
            var fwdHit = Physics2D.Raycast(fwdOrigin, new Vector2(dirSign, 0f), stepUpForwardProbe, stepMask);
            if (fwdHit) continue;

            // 3) 前方脚下必须有落脚面（更稳）
            Vector2 downOrigin = new Vector2(b.center.x + dirSign * (b.extents.x - 0.005f), newFootY + 0.02f);
            var footing = Physics2D.Raycast(downOrigin, Vector2.down, raise + 0.15f, groundLayer);
            if (!footing) continue;

            // 执行抬步
            rb.position = rb.position + new Vector2(0f, raise);
            Physics2D.SyncTransforms();
            return true;
        }

        // --- 尖角补偿逻辑：用于“走不上尖角”情况 ---
        {
            // 从角色脚前方略高一点的位置，向前发射一条射线
            Vector2 origin = rb.position + Vector2.up * 0.05f;
            Vector2 dir = Vector2.right * dirSign;

            // 发射探测，确认前方有没有短距离内可抬上的面
            RaycastHit2D hit = Physics2D.Raycast(origin, dir, stepUpForwardProbe, groundLayer);

            if (hit && hit.collider != null)
            {
                float deltaY = hit.point.y - rb.position.y;

                // 命中面法线有一定“上向”分量且高度差在允许范围内
                if (hit.normal.y > 0.2f && deltaY < stepUpMaxHeight * 1.5f)
                {
                    // 微抬角色位置（防止被卡在尖角）
                    rb.position = new Vector2(
                        rb.position.x,
                        rb.position.y + deltaY + 0.02f
                    );

                    // 同时轻推前进一点点，帮助顺利跨上去
                    rb.velocity = new Vector2(dirSign * moveSpeed * 0.3f, rb.velocity.y);
                    return true;
                }
            }
        }

        return false;
    }

    
    private void SafeSetTrigger(string name)
    {
        if (HasParam(anim, name, AnimatorControllerParameterType.Trigger))
            anim.SetTrigger(name);
    }
    private void SafeResetTrigger(string name)
    {
        if (HasParam(anim, name, AnimatorControllerParameterType.Trigger))
            anim.ResetTrigger(name);
    }
    private void SafeSetBool(string name, bool v)
    {
        if (HasParam(anim, name, AnimatorControllerParameterType.Bool))
            anim.SetBool(name, v);
    }
    private void SafeSetFloat(string name, float v)
    {
        if (HasParam(anim, name, AnimatorControllerParameterType.Float))
            anim.SetFloat(name, v);
    }
}

#endregion