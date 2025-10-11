using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector (Minimal + Shield + BackFlash)
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private AnimationEventRelay relay;

    [Header("地面检测")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Transform groundPoint;
    [SerializeField] private Transform groundPoint2;             // 新增：第二个检测点
    [SerializeField] private Vector2 groundCheckOffset = new Vector2(-0.2f, -0.9f);  // 左脚
    [SerializeField] private Vector2 groundCheckOffset2 = new Vector2(0.2f, -0.9f);   // 右脚
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.28f, 0.12f);
    [SerializeField] private Vector2 groundCheckSize2 = new Vector2(0.28f, 0.12f);

    [Header("天空检测")]
    [Tooltip("SkyPoint 命中 groundLayer 时屏蔽地面双点检测")]
    [SerializeField] private Transform SkyPoint;
    [SerializeField] private Transform SkyPoint2;
    [Tooltip("SkyPoint 的本地偏移(XY)")]
    [SerializeField] private Vector2 SkyCheckOffset = Vector2.zero;
    [Tooltip("SkyPoint2 的本地偏移(XY)")]
    [SerializeField] private Vector2 SkyCheckOffset2 = Vector2.zero;
    [SerializeField] private Vector2 SkyCheckSize1 = new Vector2(0.28f, 0.12f);
    [SerializeField] private Vector2 SkyCheckSize2 = new Vector2(0.28f, 0.12f);

    [Header("落地抗抖/记忆")]
    [Tooltip("离地后需要连续多少帧都不命中脚下地面，才真正判定为离地（防止边缘抖动）")]
    [SerializeField] private int groundReleaseFrames = 2;

    [Header("踩墙层")]
    [Tooltip("可踩墙图层（建议墙体的Collider放在这些图层）")]
    [SerializeField] private LayerMask wallMask;

    [Header("碰撞体切换")]
    [Tooltip("站立形态使用的碰撞体（建议 BoxCollider2D 或 CapsuleCollider2D）")]
    [SerializeField] private Collider2D standingCollider;
    [Tooltip("下蹲形态使用的碰撞体（建议 BoxCollider2D 或 CapsuleCollider2D）")]
    [SerializeField] private Collider2D duckCollider;


    [Header("移动")]
    public float moveSpeed = 5f;
    public float groundAcceleration = 40f;     // 可删：若不要平滑加速，直接用瞬时速度
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

    [Header("盾 (Shield)")]     // 不再暴露按键，固定 L 作为举盾键
    public float shieldStationaryThreshold = 0.05f; // 判定静止用
    [Tooltip("勾选：盾的可视动画（Up/Down）在移动/跳跃中也立即播放；取消勾选：需静止才播（可能出现延迟）")]
    public bool shieldInstantVisual = true;
    [Tooltip("勾选：空中举盾会屏蔽空中攻击；取消勾选：按 J 会优先打断空中盾并执行空中攻击")]
    public bool airShieldBlocksAttack = false;
    // 新增：站立举盾（player_shield_up）冷却（由 player_shield_down 结束开始计时）


    [Header("后退闪避 (BackFlash)")]     // 不再暴露按键，固定 I 作为后退闪避键
    public float backFlashSpeed = 7f;           // 后退速度
    public float backFlashDistance = 2.5f;      // 后退距离（米）—以“速度+距离”计算收尾
    [Range(0f, 1f)]
    public float backFlashNoInterruptNorm = 0.5f;   // 动画前50%禁止方向键打断
    [Range(0f, 1f)]
    public float backFlashReTriggerNorm = 0.9f;     // 动画90%后才允许再次触发

    [Header("魔法 (Magic)")]
    public float magicAttackAirDuration = 0.4f; // 空中魔法攻击锁定时长（无动画时用）
    // public int magicReenterStillFrames = 3;   // 已删除：未使用
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
    //移动
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string TRIG_Attack = "Trig_Attack";
    private const string STATE_IdleStart = "player_idle_start";  // 强制退出magic时切回的站立状态（按你的Controller调整名字）

    //下蹲
    private const string PARAM_IsGrounded = "IsGrounded";
    private const string PARAM_IsDucking = "IsDucking";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_DuckShieldUp = "Trig_DuckShieldUp";
    private const string TRIG_DuckShieldDown = "Trig_DuckShieldDown";

    //空中
    private const string PARAM_IsFalling = "IsFalling";
    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";
    private const string STATE_JumpAttack = "player_jump_attack";
    private const string STATE_JumpDownFwdAttack = "player_jump_downForward_attack";

    //二段跳
    // private const string TRIG_JumpDouble = "Trig_JumpDouble"; // 已删除：未使用
    private const string STATE_JumpDouble = "player_jump_double";

    //盾
    private const string PARAM_ShieldHold = "ShieldHold";

    private const string TRIG_ShieldUp = "Trig_ShieldUp";
    private const string TRIG_ShieldDown = "Trig_ShieldDown";

    //魔法
    private const string PARAM_MagicHold = "MagicHold"; // W 按住
    private const string TRIG_MagicUp = "Trig_MagicUp";
    private const string TRIG_MagicDown = "Trig_MagicDown";
    private const string TRIG_MagicAttack = "Trig_MagicAttack";
    private const string STATE_MagicUp = "player_magic_up";
    private const string STATE_MagicIdle = "player_shield_idle";
    private const string STATE_MagicAttack = "player_magic_attack";
    private const string STATE_MagicDown = "player_magic_down";

    //闪避后退
    private const string PARAM_BackFlashInterruptible = "BackFlashInterruptible";
    // private const string PARAM_BackFlashActive = "BackFlashActive"; // 已删除：未使用
    private const string TRIG_BackFlash = "Trig_BackFlash";
    private const string STATE_BackFlash = "player_backflash";

    //踩墙反跳
    private const string TRIG_Land = "Trig_Land";
    private const string TRIG_WallTurn = "Trig_WallTurn";
    private static readonly string[]
        AirAttackAnimStates = { STATE_JumpAttack, STATE_JumpDownFwdAttack };
    #endregion

    #region Runtime
    private Animator anim;
    private Rigidbody2D rb;

    // 基础状态
    private bool isGrounded;
    private bool prevGrounded;
    private bool isDucking;

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
    private int extraJumpsUsed = 0; // 用“已用额外跳次数”计数器
    private bool doubleJumpActive = false;   // 当前是否处于二段跳的上升/保持阶段
    private float doubleJumpStartY = 0f;     // 二段跳起跳高度（用于相对限高）
    private bool doubleJumpPoseHold = false; // 动画播完但仍在上升：保持最后一帧

    // 输入
    private float rawInputX;
    private bool keyDownLeft;
    private bool keyDownRight;
    private bool keyDownJump;
    private bool keyDownAttack;
    private bool keyDownBackFlash;

    // W 魔法输入
    private bool keyDownMagic;
    // private bool keyUpMagic; // 已删除：未使用
    private bool magicHeldKey;
    private float magicAttackAvailableAt = 0f;   // 早于该时间不允许再次施放魔法攻击

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

    // 后退闪避（速度+距离+第N帧可打断）
    private bool backFlashActive = false; //private int backFlashFrame = 0;
    private float backFlashStartX = 0f; // 记录起点X（用于按距离结束）
    private bool backFlashLock = false; // 动画未结束前禁止再次触发
    private float backFlashStartTime = 0f;    // 本次后退起始时间（用于时长兜底）
    private float backFlashMaxDuration = 0f;  // 本次后退最大时长（由 距离/速度 推算）
    private bool backFlashMoving = false;          // 是否仍在做位移（到达距离后为 false，但动画可继续）
    //private bool backFlashReTriggerArmed = false;  // 是否已“武装”，达到第M帧后才允许覆盖重启

    // Magic 运行时
    private bool magicActive = false;          // 处于施法流程（up/idle/down/attack 任一）
    private bool magicAttackPlaying = false;   // 处于 magic_attack（不可被打断）
    private float magicAttackStartTime = 0f;   // 空中攻击时用时长结束

    private float landDebounceUntil = 0f;

    //踩墙反跳
    private bool wallTurnActive = false;            // 转身硬锁
    private bool wallJumpAutoPhase = false;         // 自动斜跳锁
    private bool wallJumpControlUnlocked = false;   // 是否解锁可控
    private float savedGravityScale = 0f;           // 转身时暂存重力
    private float wallJumpStartY = 0f;              // 本次反跳起点Y
    private float wallJumpLastTime = -999f;         // 冷却
    private int wallSide = 0;                       // 墙在左(-1)/右(+1)
    private bool nearWall = false;                  // 是否检测到可踩墙

    // 当前是否启用的是下蹲碰撞体
    private bool colliderDuckActive = false;

    private bool IsAutoPhaseLocked() => wallJumpAutoPhase && !wallJumpControlUnlocked;
    private bool IsHardLocked() => wallTurnActive;

    // === Runtime: 抗抖/状态缓存 ===
    private int groundReleaseCounter = 0;   // 离地计数（用于延时释放）
    private bool skyBlockingActive = false; // 仅 Sky 命中但脚下未命中（本帧）                                      
    public bool IsSkyBlockingNow => skyBlockingActive; // 可选：对外可读

    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>(); // 自动抓取

        // 只启用站立碰撞体，禁用下蹲碰撞体（很关键）
        if (standingCollider) standingCollider.enabled = true;
        if (duckCollider) duckCollider.enabled = false;
        colliderDuckActive = false;
    }

    private void Update()
    {
        //先更新墙的贴合情况（法线判定）
        UpdateWallProximity();

        CaptureInput();
        CheckGrounded();

        if (isGrounded && (wallTurnActive || wallJumpAutoPhase))
            ForceExitWallJumpLocks(false);

        HandleJump(); // 跳跃优先

        //墙跳三件套：必须每帧都跑（即便硬锁/自动锁定）
        HandleWallJumpInput();       // 本帧是否触发踩墙
        PollWallTurnAndLaunch();     // 转身播完后发射前跳
        EnforceMaxWallJumpHeight();  // 反跳限高

        // 盾
        if (!(IsHardLocked() || IsAutoPhaseLocked()))
            HandleShield();

        //魔法：锁定中不进入地面施法，但不退出整帧
        if (IsHardLocked() || IsAutoPhaseLocked())
            anim.SetBool(PARAM_MagicHold, false);
        else
            HandleMagic();

        // ===== 在空中攻击之前插入这四行 =====
        HandleDoubleJump();               // 处理按K触发二段跳（空中一次）
        EnforceMaxDoubleJumpHeight();     // 限制二段跳相对高度
        HoldDoubleJumpPoseWhileAscending();// 动画播完仍上升则钉住最后一帧
        AutoExitDoubleJumpOnFall();       // 过顶点开始下落则退出二段跳保持


        //BackFlash 触发逻辑（保持不变）
        var stNow = anim.GetCurrentAnimatorStateInfo(0);
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

        // 地面攻击与下蹲逻辑
        HandleGroundDuckAndAttacks();

        // 同步站立/下蹲碰撞体（无顶头门禁）
        SyncCrouchColliders();

        // 自动斜跳允许用 J 打断：先解锁再走空攻
        if (IsAutoPhaseLocked() && keyDownAttack)
        {
            wallJumpAutoPhase = false;
            wallJumpControlUnlocked = true;
        }
        // 硬锁中不处理空攻；否则照常
        if (!IsHardLocked())
            HandleAirAttack();

        // 水平移动：
        // - 硬锁：完全停住刚体
        // - 自动斜跳锁定：忽略输入，不改 rb.velocity
        if (IsHardLocked())
        {
            rb.velocity = Vector2.zero;
            currentSpeedX = 0f;
        }
        else if (!IsAutoPhaseLocked() || doubleJumpActive)
        {
            HandleHorizontal();
        }

        // 可变跳截断：放到函数内部做门禁（避免整帧 return）
        HandleVariableJumpCut();

        // 翻转：锁定中不允许翻转
        if (!IsHardLocked() && !IsAutoPhaseLocked())
        {
            HandleFacingFlip();
        }

        // 常规最大跳高
        EnforceMaxJumpHeight();

        // 空中从 jump_up 切 jump_forward（保留你的逻辑）
        HandleAirJumpForwardSwitch();

        // 收尾
        AutoEndAirAttack();
        AutoEndBackFlash_ByDistance();
        AutoExitBackFlashOnStateLeave();

        // 关键：动画真正离开 backflash 才解锁（恢复这段）
        var stAfter = anim.GetCurrentAnimatorStateInfo(0);
        if (backFlashLock && !stAfter.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
            backFlashLock = false;

        UpdateAnimatorParams();

        // 下落动画触发（保留）
        if (prevGrounded && !isGrounded)
        {
            SafeResetTrigger("Trig_JumpDown");
            SafeSetTrigger("Trig_JumpDown");
        }

        if (!isGrounded && rb.velocity.y <= -0.01f)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (!st.IsName("player_jump_down"))
            {
                SafeResetTrigger("Trig_JumpDown");
                SafeSetTrigger("Trig_JumpDown");
            }
        }
        else if (prevGrounded && !isGrounded && !isJumping)
        {
            // 从平台边走落（不是主动起跳），允许直接进入下落
            SafeResetTrigger("Trig_JumpDown");
            SafeSetTrigger("Trig_JumpDown");
        }

        prevGrounded = isGrounded;
    }
    #endregion

    #region 输入
    private void CaptureInput()
    {
        rawInputX = Input.GetAxisRaw("Horizontal");
        keyDownLeft = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
        keyDownRight = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
        keyDownJump = Input.GetKeyDown(KeyCode.K);
        keyDownAttack = Input.GetKeyDown(KeyCode.J);

        // 固定键：I 触发 BackFlash；L 举盾；W 施法
        keyDownBackFlash = Input.GetKeyDown(KeyCode.I);
        shieldHeld = Input.GetKey(KeyCode.L);

        keyDownMagic = Input.GetKeyDown(KeyCode.W);
        // keyUpMagic = Input.GetKeyUp(KeyCode.W); // 已删除：未使用
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
            if (backFlashActive) CancelBackFlash(); // 跳跃随时打断后退闪避
            if (magicActive && !magicAttackPlaying) CancelMagic(); // 跳跃打断魔法 up/idle/down

            isJumping = true;
            jumpStartY = rb.position.y;

            float dir = GetEffectiveInputDir();
            currentSpeedX = dir * moveSpeed;
            rb.velocity = new Vector2(currentSpeedX, jumpForce);

            isDucking = false;
            if (shieldActiveDuck) CancelShieldDuck(false);

            string trig = Mathf.Abs(dir) > 0.05f ? TRIG_JumpForward : TRIG_JumpUp;
            SafeResetTrigger(TRIG_JumpForward);
            SafeResetTrigger(TRIG_JumpUp);
            SafeSetTrigger(trig);
        }

        if (!prevGrounded && isGrounded)
        {
            // 先强制结束墙跳锁定并清水平速度
            ForceExitWallJumpLocks(true);

            // ---- 落地瞬间逻辑 ----
            isJumping = false;
            airAttackActive = false;
            airAttackAnimPlaying = false;

            // 清理跳跃相关 Trigger，避免残留导致回跳
            SafeResetTrigger(TRIG_JumpUp);
            SafeResetTrigger(TRIG_JumpForward);
            SafeResetTrigger("Trig_JumpDown");

            // 只发一次“落地”触发器，由 Animator 统一进 player_jump_ground
            if (Time.time >= landDebounceUntil)
            {
                landDebounceUntil = Time.time + 0.05f; // 50ms 去抖，防止连发
                SafeResetTrigger(TRIG_Land);
                SafeSetTrigger(TRIG_Land);
            }

            // 不要在这里 CrossFade 到 player_idle_start，交给 Animator 从 jump_ground 退出
            if (shieldHeld && !shieldActiveStanding && !shieldActiveDuck)
                TryActivateStandingShield(true);

            // 重置额外跳配额（落地才恢复）
            extraJumpsUsed = 0;
            doubleJumpActive = false;
            doubleJumpPoseHold = false;
        }
    }

    // 新增：空中“转向切前跳”逻辑（只切动画，不改物理）
    private void HandleAirJumpForwardSwitch()
    {
        if (isGrounded) return;

        // 仍在上升阶段才允许从 jump_up 切到 jump_forward
        if (rb.velocity.y <= 0f) return;

        // 当前动画是 jump_up，且本帧有横向输入（按下A/D 或 轴有输入）
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

        // 墙转身阶段 或 自动斜跳未解锁阶段：禁用截断（松开K不影响发射速度）
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

    private bool IsBackFlashInterruptibleNow()
    {
        if (!backFlashActive) return true;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (!st.IsName(STATE_BackFlash) || anim.IsInTransition(0)) return false;
        return st.normalizedTime >= backFlashNoInterruptNorm;
    }

    private void HandleShield()
    {
        // 攻击时强制取消盾（保持优先级）
        if (groundAttackActive)
        {
            if (shieldActiveStanding) CancelShieldStanding(true);
            if (shieldActiveDuck) CancelShieldDuck(true);
            if (shieldActiveAir) CancelShieldAir();
            return;
        }

        // BackFlash 非可打断区间：屏蔽盾/下蹲
        if (backFlashActive && !IsBackFlashInterruptibleNow())
            return;

        // BackFlash 可打断后：按 L 允许先打断 BackFlash，再进入举盾逻辑
        if (backFlashActive && IsBackFlashInterruptibleNow() && shieldHeld && !magicAttackPlaying)
        {
            CancelBackFlash();
            // 继续走举盾流程
        }

        // 空中攻击期间禁止盾
        if (airAttackActive || airAttackAnimPlaying)
        {
            if (shieldActiveAir) CancelShieldAir();
            return;
        }

        // 盾随时打断魔法/后退闪避（up/idle/down）
        if ((backFlashActive || magicActive) && shieldHeld && !magicAttackPlaying)
        {
            if (backFlashActive) CancelBackFlash();
            if (magicActive) CancelMagic();
            // 继续走举盾流程
        }

        if (!isGrounded) // 空中
        {
            if (shieldHeld)
            {
                if (!shieldActiveAir)
                {
                    shieldActiveAir = true;
                    CancelShieldStanding(false);
                    CancelShieldDuck(false);
                    relay?.PlayShieldStanding();
                }
            }
            else if (shieldActiveAir)
            {
                CancelShieldAir();
            }
            return;
        }

        // 地面
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
                if (shieldActiveDuck)
                    CancelShieldDuck(true);
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

        // 消费 pending
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

    // —— 站立举盾：立刻驱动盾Hub —— 
    private void PlayStandingShieldUp()
    {
        SafeResetTrigger(TRIG_ShieldDown);
        SafeSetTrigger(TRIG_ShieldUp);
        shieldAnimUpPlayed = true;
        relay?.PlayShieldStanding();
    }

    //允许“立即”播放下蹲盾Up
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

    // —— 下蹲举盾：立刻驱动盾Hub —— 
    private void PlayDuckShieldUp()
    {
        SafeResetTrigger(TRIG_DuckShieldDown);
        SafeSetTrigger(TRIG_DuckShieldUp);
        shieldAnimUpPlayed = true;
        relay?.PlayShieldDuck();
    }

    //在“收站立盾”时，触发 Down 动画的同时武装冷却
    private void CancelShieldStanding(bool playDownAnim)
    {
        if (!shieldActiveStanding) return;
        if (playDownAnim && shieldAnimUpPlayed && (shieldInstantVisual || IsStationaryHorizontally()))
            SafeSetTrigger(TRIG_ShieldDown);
        shieldActiveStanding = false;
        shieldAnimUpPlayed = false;
        pendingShieldUp = false;
        relay?.StopShield();
    }

    // 收下蹲盾（需要 Down）
    private void CancelShieldDuck(bool playDownAnim)
    {
        if (!shieldActiveDuck) return;
        if (playDownAnim && shieldAnimUpPlayed && (shieldInstantVisual || IsStationaryHorizontally()))
            SafeSetTrigger(TRIG_DuckShieldDown);
        shieldActiveDuck = false;
        duckAttackFacingLocked = false;
        shieldAnimUpPlayed = false;
        pendingShieldUp = false;
        relay?.StopShield();
    }


    private void CancelShieldAir()
    {
        shieldActiveAir = false;
        relay?.StopShield(); // 关键：空中松开立即隐藏可视盾
    }

    private bool AnyShieldActive() =>
        shieldActiveStanding || shieldActiveDuck || shieldActiveAir;
    #endregion

    #region 后退闪避 BackFlash（速度 + 距离 + 第N帧可打断 + 可覆盖重启）

    //初始化“时长兜底”
    private void StartBackFlash()
    {
        if (backFlashLock || backFlashActive) return; // 双保险，避免重复触发
        backFlashLock = true;        // 动画结束前不允许再次由外部 StartBackFlash 触发
        backFlashActive = true;
        backFlashMoving = true;      // 开始位移

        // 记录起点X（用于按距离结束）
        backFlashStartX = rb.position.x;

        // 计算本次后退的最大时长 = 距离 / 速度 + 微小余量
        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        // 直接切到 backflash 动画（不再依赖 Trigger 排队）
        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f); // 强制立即生效

        // 关键：保持 Animator 的 MoveSpeed 为 0，避免立刻触发 run_start
        currentSpeedX = 0f;

        // 用刚体推进后退
        int dir = facingRight ? -1 : 1;
        rb.velocity = new Vector2(dir * backFlashSpeed, rb.velocity.y);
    }

    // 同样初始化“时长兜底”
    private void RestartBackFlashOverride()
    {
        backFlashActive = true;
        backFlashMoving = true;

        // 记录新的起点与时长兜底（关键修复）
        backFlashStartX = rb.position.x;
        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f);

        // 立即推进位移
        currentSpeedX = 0f;
        int dir = facingRight ? -1 : 1;
        rb.velocity = new Vector2(dir * backFlashSpeed, rb.velocity.y);
    }

    private void CancelBackFlash()
    {
        if (!backFlashActive) return;
        backFlashActive = false;
        backFlashMoving = false;
        // rb.velocity = new Vector2(0f, rb.velocity.y);
    }

    // 按距离结束（不解锁，等动画真正离开 backflash 再解锁）,加入“时长兜底”终止位移
    private void AutoEndBackFlash_ByDistance()
    {
        if (!backFlashActive || !backFlashMoving) return;

        float traveled = Mathf.Abs(rb.position.x - backFlashStartX);
        bool reachDistance = traveled >= backFlashDistance;
        bool reachTime = (Time.time - backFlashStartTime) >= backFlashMaxDuration;

        if (reachDistance || reachTime)
        {
            backFlashMoving = false;
            rb.velocity = new Vector2(0f, rb.velocity.y);
            currentSpeedX = 0f;
        }
    }

    // 按动画状态离开来结束 BackFlash（清掉 active，允许常规移动）
    private void AutoExitBackFlashOnStateLeave()
    {
        if (!backFlashActive) return;

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (!st.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
        {
            backFlashActive = false;
            backFlashMoving = false;
        }
    }
    #endregion

    #region 魔法 Magic
    private bool CanPlayMagicAnimOnGround()
    {
        // 站立静止才能播放魔法动画（up/idle/down）；空中不播放动画
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
        // MagicHold 仅在魔法逻辑态激活时为真
        anim.SetBool(PARAM_MagicHold, magicHeldKey && magicActive);

        // 魔法攻击阶段：必须播完/到时长结束
        if (magicAttackPlaying)
        {
            if (isGrounded)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                if (!st.IsName(STATE_MagicAttack) || st.normalizedTime >= 0.98f)
                    EndMagicAttack();
            }
            else
            {
                if (Time.time - magicAttackStartTime >= magicAttackAirDuration)
                    EndMagicAttack();
            }
            return;
        }

        // 冷却判定：未到可施放时间则忽略本帧 W+J
        if (magicHeldKey && keyDownAttack)
        {
            if (Time.time >= magicAttackAvailableAt)
            {
                magicActive = true;                      // 标记进入魔法逻辑，阻断普通攻击
                anim.SetBool(PARAM_MagicHold, true);
                StartMagicAttack();                      // 内部不触发角色魔法攻击动画
            }
            // 无论是否成功施放，这里都 return，避免后续分支覆盖
            return;
        }

        // 盾优先：屏蔽 up/idle/down（不影响上面的 W+J 立即施放）
        if (shieldHeld || AnyShieldActive())
        {
            if (magicActive) CancelMagic();
            return;
        }

        // W 首次按下：尝试进入魔法准备（仅当不被其它行为封锁时）
        if (keyDownMagic)
        {
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);

            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger(TRIG_MagicUp);
                anim.SetTrigger(TRIG_MagicUp);
            }
        }

        // W 按住期间：自动回归 & 自动覆盖
        if (magicHeldKey && !magicAttackPlaying)
        {
            bool blocked = IsMagicAnimBlockedByOtherActions();

            if (!magicActive)
            {
                if (!blocked)
                {
                    magicActive = true;
                    anim.SetBool(PARAM_MagicHold, true);
                    anim.ResetTrigger(TRIG_MagicUp);
                    anim.SetTrigger(TRIG_MagicUp);
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

        // W 松开：退出魔法准备（地面静止时播放Down）
        if (!magicHeldKey && magicActive)
        {
            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger(TRIG_MagicDown);
                anim.SetTrigger(TRIG_MagicDown);
            }
            CancelMagic();
        }
    }

    private void StartMagicAttack()
    {
        magicAttackPlaying = true;
        magicAttackStartTime = Time.time;

        // 不播放角色魔法攻击动画（无论空中还是地面）
        // 仅进入“魔法攻击逻辑/锁定”。地面原地的 up/idle/down（盾动画）仍由 HandleMagic 的准备态门禁决定。

        if (isGrounded && CanPlayMagicAnimOnGround()) { anim.ResetTrigger(TRIG_MagicAttack); anim.SetTrigger(TRIG_MagicAttack); }
    }

    // ====== 4) EndMagicAttack：结束时启动冷却计时 ======
    private void EndMagicAttack()
    {
        magicAttackPlaying = false;

        // 新增：设置下次可施放时间
        magicAttackAvailableAt = Time.time + magicAttackCooldown;

        if (!magicHeldKey)
        {
            // 松开W：尝试播放Down（仅地面静止时）
            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger(TRIG_MagicDown);
                anim.SetTrigger(TRIG_MagicDown);
            }
            magicActive = false;
            anim.SetBool(PARAM_MagicHold, false);
        }
        else
        {
            // 仍按W：回到idle（由Animator根据 MagicHold 转回）
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);
        }
    }

    private void CancelMagic()
    {
        if (magicAttackPlaying) return; // 攻击中不可被打断

        magicActive = false;
        anim.SetBool(PARAM_MagicHold, false);

        // 清理所有魔法触发器，避免排队
        anim.ResetTrigger(TRIG_MagicUp);
        anim.ResetTrigger(TRIG_MagicAttack);
        anim.ResetTrigger(TRIG_MagicDown);

        // 若当前仍在 magic 的 up/idle/down，强制切回站立起始（避免卡在 player_magic_idle）
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName(STATE_MagicIdle) || st.IsName(STATE_MagicUp) || st.IsName(STATE_MagicDown))
        {
            anim.CrossFadeInFixedTime(STATE_IdleStart, 0f, 0, 0f);
            anim.Update(0f); // 立即生效
        }
    }

    // 判断当前是否允许播放“地面施法动画”（up/idle/down）
    // 说明：空中逻辑存在但不播放动画，所以 isGrounded 必须为 true
    private bool IsMagicAnimBlockedByOtherActions()
    {
        // 地面静止 + 无输入 + 无下蹲 + 无举盾 + 无后闪/地面攻击 才允许播放魔法up/idle/down动画
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
        if (!isGrounded)
        {
            isDucking = false; // 空中绝不保留地面下蹲状态
            return;
        }


        // 本帧刚起跳，不要再用 S 把 isDucking 设回 true，避免空中移动被地面下蹲锁住
        if (isJumping)
        {
            isDucking = false;
            return;
        }

        if (shieldActiveDuck) // 下蹲盾强制下蹲
            isDucking = true;
        else
            isDucking = Input.GetKey(KeyCode.S);

        // 其它行为覆盖魔法（up/idle/down）
        if (isDucking && magicActive && !magicAttackPlaying) CancelMagic();

        if (keyDownAttack && AnyShieldActive())
        {
            CancelShieldStanding(true);
            CancelShieldDuck(true);
            CancelShieldAir();
        }

        // 若 W 按住，攻击键 J 已在 HandleMagic 中被用于触发魔法攻击，这里不再触发普通攻击
        if (magicActive || magicAttackPlaying)
        {
            // 施法攻击中禁止普通攻击；idle/up/down 时 J 已被上面消耗
            return;
        }

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
        if (backFlashActive) CancelBackFlash(); // 攻击随时打断后退闪避
        if (magicActive && !magicAttackPlaying) CancelMagic(); // 攻击覆盖魔法 idle/up/down
        if (magicAttackPlaying) return; // 魔法攻击播放期间禁止其它攻击

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

    // 动画事件

    public void OnAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
        // 攻击结束自动重举盾（按住 L）
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
    //空中攻击不再被“空中举盾”拦截；按 J 时先取消空中盾
    private void HandleAirAttack()
    {
        if (isGrounded) return;
        if (airAttackActive) return;
        // 原先：if (shieldActiveAir) return;  -> 改为：
        if (shieldActiveAir && airShieldBlocksAttack) return;
        if (magicAttackPlaying) return;

        if (keyDownAttack && !magicActive)
        {
            // 若不希望空中盾拦截攻击，则在攻击前先取消空中盾
            if (shieldActiveAir && !airShieldBlocksAttack)
                CancelShieldAir();

            var st = anim.GetCurrentAnimatorStateInfo(0);
            foreach (var a in AirAttackAnimStates)
                if (st.IsName(a)) return;

            // 打断二段跳姿势保持，但不改变刚体速度（满足“空攻不打断上升速度”）
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

        // 空中攻击结束后仍按着 L -> 允许恢复空中盾
        if (!isGrounded && shieldHeld)
        {
            shieldActiveAir = true;
            // 需要立即恢复可视层就加这行；否则由下一帧 HandleShield 恢复
            relay?.PlayShieldStanding();
        }
    }

    private void AutoEndAirAttack()
    {
        if (!airAttackActive) return;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool inAtk = false;
        foreach (var a in AirAttackAnimStates)
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
            if (shieldHeld && !isGrounded) shieldActiveAir = true;
        }
    }

    // 立即结束空中攻击（不等动画事件/最小时长）——用于被墙跳覆盖
    private void ForceEndAirAttack()
    {
        if (!airAttackActive && !airAttackAnimPlaying) return;
        airAttackActive = false;
        airAttackAnimPlaying = false;
        // 如需强制打断动画，可按需加入 CrossFade 到 jump_turn_wall 前的安全态，但通常直接 CrossFade 到转身即可覆盖
    }
    #endregion

    #region 水平移动

    //限制“覆盖重启”只能在位移结束后触发
    private void HandleHorizontal()
    {
        // --- 后退闪避（优先处理） ---
        if (backFlashActive)
        {
            var stBF = anim.GetCurrentAnimatorStateInfo(0);
            bool inBFState = stBF.IsName(STATE_BackFlash);
            float bfNorm = inBFState ? stBF.normalizedTime : 0f;

            bool inNoInterruptWindow = inBFState && !anim.IsInTransition(0) && bfNorm < backFlashNoInterruptNorm;

            // I 键覆盖重启：仅当“位移已结束 + 动画进度 ≥ backFlashReTriggerNorm + 非过渡”才允许
            if (keyDownBackFlash)
            {
                if (!backFlashMoving && inBFState && !anim.IsInTransition(0) && bfNorm >= backFlashReTriggerNorm)
                {
                    RestartBackFlashOverride();
                    return;
                }
                else
                {
                    anim.ResetTrigger(TRIG_BackFlash);
                }
            }

            // 下蹲：不打断动画，但停止水平位移
            if (Input.GetKey(KeyCode.S))
            {
                rb.velocity = new Vector2(0f, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }

            // 位移已结束：在“禁止打断窗口”内也不允许方向打断
            if (!backFlashMoving)
            {
                if (inNoInterruptWindow)
                {
                    rb.velocity = new Vector2(0f, rb.velocity.y);
                    currentSpeedX = 0f;
                    return;
                }

                float input2 = GetEffectiveInputDir();
                if (Mathf.Abs(input2) > 0.01f)
                {
                    CancelBackFlash();
                    currentSpeedX = input2 * moveSpeed;
                }
                else
                {
                    rb.velocity = new Vector2(0f, rb.velocity.y);
                    currentSpeedX = 0f;
                    return;
                }
            }

            // 位移推进阶段：在“禁止打断窗口”内强制推进且不可打断
            if (inNoInterruptWindow)
            {
                int d0 = facingRight ? -1 : 1;
                rb.velocity = new Vector2(d0 * backFlashSpeed, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }

            // 已过打断窗口：有方向输入则打断；否则继续推进
            float input = GetEffectiveInputDir();
            if (Mathf.Abs(input) > 0.01f)
            {
                CancelBackFlash();
                currentSpeedX = input * moveSpeed;
                // 不 return，继续常规移动
            }
            else
            {
                int d = facingRight ? -1 : 1;
                rb.velocity = new Vector2(d * backFlashSpeed, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }
        }

        // 魔法攻击阶段：锁水平移动
        if (magicAttackPlaying && isGrounded)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 下蹲攻击全程锁
        if (groundAttackActive && duckAttackFacingLocked)
        {
            if (duckAttackHorizLockRemain > 0) duckAttackHorizLockRemain--;
            ApplyHorizontal(0f);
            return;
        }

        // 下蹲盾锁
        if (shieldActiveDuck)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 站立攻击主体锁
        if (groundAttackActive && !duckAttackFacingLocked)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 普通下蹲锁（不处于下蹲盾）
        if (isDucking)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 常规移动
        float dir = GetEffectiveInputDir();
        float target = dir * moveSpeed;

        float accel = Mathf.Abs(dir) > 0.01f ? groundAcceleration : groundDeceleration;
        currentSpeedX = Mathf.MoveTowards(currentSpeedX, target, accel * Time.deltaTime);
        if (Mathf.Abs(currentSpeedX) < stopThreshold) currentSpeedX = 0f;

        rb.velocity = new Vector2(currentSpeedX, rb.velocity.y);

        // 移动中按盾延迟 Up
        if (shieldActiveStanding && shieldHeld && !shieldAnimUpPlayed && !pendingShieldDown && !IsStationaryHorizontally())
            pendingShieldUp = true;

        // 移动覆盖魔法 idle/up/down
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

    // 被动保活：不在后闪时，只要按着 L，每帧把盾Hub纠正到正确的可视层
    // ====== 6) LateUpdate：被动重举盾同样需要冷却已到 ======
    private void LateUpdate()
    {
        if (IsInBackFlashAnimOrTransition()) return;

        if (shieldHeld)
        {
            if (isGrounded)
            {
                if (Input.GetKey(KeyCode.S))
                {
                    if (!shieldActiveDuck)
                    {
                        shieldActiveDuck = true;
                        shieldActiveStanding = false;
                        shieldActiveAir = false;
                    }
                    relay?.PlayShieldDuck();
                }
                else
                {
                    if (!shieldActiveStanding)
                    {
                        shieldActiveStanding = true;
                        shieldActiveDuck = false;
                        shieldActiveAir = false;
                    }
                    relay?.PlayShieldStanding();
                }
            }
            else
            {
                if (!shieldActiveAir)
                {
                    shieldActiveAir = true;
                    shieldActiveStanding = false;
                    shieldActiveDuck = false;
                }
                relay?.PlayShieldStanding();
            }
        }
        else
        {
            if (shieldActiveStanding || shieldActiveDuck || shieldActiveAir)
            {
                shieldActiveStanding = shieldActiveDuck = shieldActiveAir = false;
                relay?.StopShield();
            }
        }
    }

    #region Facing
    private void HandleFacingFlip()
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);

        // 后退位移中、(地面)魔法攻击中禁止翻转
        // 原来：if ((backFlashActive && backFlashMoving) || magicAttackPlaying) return;
        if ((backFlashActive && backFlashMoving) || (magicAttackPlaying && isGrounded)) return;

        if (airAttackActive || st.IsName(STATE_JumpAttack) || st.IsName(STATE_JumpDownFwdAttack)) return;   // 空中攻击期间禁止翻转朝向

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
    // 用 BoxCast + 法线过滤 的“脚底判地”，Sky 保持 OverlapBox
    private void CheckGrounded()
    {
        // 本地小工具：把“锚点 + 本地偏移”换算到世界坐标
        Vector2 W(Transform anchor, Vector2 localOffset) =>
            anchor ? (Vector2)anchor.TransformPoint((Vector3)localOffset)
                   : (Vector2)transform.TransformPoint((Vector3)localOffset);

        // 采样点（世界）
        Vector2 p1 = W(groundPoint, groundCheckOffset);
        Vector2 p2 = W(groundPoint2, groundCheckOffset2);
        Vector2 s1 = W(SkyPoint, SkyCheckOffset);
        Vector2 s2 = W(SkyPoint2, SkyCheckOffset2);

        // 脚底“只认下面”的地面检测：向下 BoxCast 少量距离，并筛法线
        const float feetProbe = 0.08f;      // 向下探测距离（根据像素可调到 0.05~0.12）
        const float floorNyMin = 0.5f;      // 只接受“相对水平”的地面（ny >= 0.5）
        bool g1 = false, g2 = false;

        // 左脚
        {
            var hit = Physics2D.BoxCast(p1, groundCheckSize, 0f, Vector2.down, feetProbe, groundLayer);
            g1 = hit && hit.normal.y >= floorNyMin;
        }
        // 右脚
        {
            var hit = Physics2D.BoxCast(p2, groundCheckSize2, 0f, Vector2.down, feetProbe, groundLayer);
            g2 = hit && hit.normal.y >= floorNyMin;
        }

        // 2) Sky：仍用 OverlapBox（你需要“膝侧/头侧碰到墙就视为空中”的门禁）
        bool sH1 = Physics2D.OverlapBox(s1, SkyCheckSize1, 0f, groundLayer);
        bool sH2 = Physics2D.OverlapBox(s2, SkyCheckSize2, 0f, groundLayer);

        bool groundHit = g1 || g2;
        bool skyHit = sH1 || sH2;

        // 规则：脚下命中优先；只有 Sky 命中时强制空中
        skyBlockingActive = skyHit && !groundHit;

        if (groundHit)
        {
            isGrounded = true;
            groundReleaseCounter = 0;
            return;
        }
        if (skyBlockingActive)
        {
            isGrounded = false;
            groundReleaseCounter = Mathf.Max(groundReleaseCounter, 1);
            return;
        }

        // 抗抖：进入地面即时、离地延迟 N 帧
        if (isGrounded)
        {
            groundReleaseCounter++;
            if (groundReleaseCounter >= groundReleaseFrames) isGrounded = false;
        }
        else
        {
            groundReleaseCounter = Mathf.Max(groundReleaseCounter, 1);
            isGrounded = false;
        }
    }

    // Gizmos：用 TransformPoint 计算位置；同时画出 BoxCast 的探测线，便于校准
    private void OnDrawGizmosSelected()
    {
        if (!enabled) return;

        Vector2 W(Transform anchor, Vector2 localOffset) =>
            anchor ? (Vector2)anchor.TransformPoint((Vector3)localOffset)
                   : (Vector2)transform.TransformPoint((Vector3)localOffset);

        Vector2 p1 = W(groundPoint, groundCheckOffset);
        Vector2 p2 = W(groundPoint2, groundCheckOffset2);
        Vector2 s1 = W(SkyPoint, SkyCheckOffset);
        Vector2 s2 = W(SkyPoint2, SkyCheckOffset2);

        const float feetProbe = 0.08f;

        // 实时检测结果（场景里能看到颜色变化）
        bool g1 = Physics2D.BoxCast(p1, groundCheckSize, 0f, Vector2.down, feetProbe, groundLayer) is RaycastHit2D h1 && h1 && h1.normal.y >= 0.5f;
        bool g2 = Physics2D.BoxCast(p2, groundCheckSize2, 0f, Vector2.down, feetProbe, groundLayer) is RaycastHit2D h2 && h2 && h2.normal.y >= 0.5f;
        bool sH1 = Physics2D.OverlapBox(s1, SkyCheckSize1, 0f, groundLayer);
        bool sH2 = Physics2D.OverlapBox(s2, SkyCheckSize2, 0f, groundLayer);

        // Ground 可视
        Gizmos.color = g1 ? Color.green : Color.red;
        Gizmos.DrawWireCube(p1, groundCheckSize);
        Gizmos.DrawLine(p1, p1 + Vector2.down * feetProbe); // 探测线
        Gizmos.color = g2 ? Color.green : Color.red;
        Gizmos.DrawWireCube(p2, groundCheckSize2);
        Gizmos.DrawLine(p2, p2 + Vector2.down * feetProbe);

        // Sky 可视
        Gizmos.color = sH1 ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(s1, SkyCheckSize1);
        Gizmos.color = sH2 ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(s2, SkyCheckSize2);
    }
    #endregion


    #region Animator
    // UpdateAnimatorParams：把 Sky 阻断也喂给 Animator（可选，不存在该参数会被 SafeSetBool 忽略）
    private void UpdateAnimatorParams()
    {

        SafeSetBool(PARAM_ShieldHold, shieldHeld);

        bool bfInterruptible = false;
        if (backFlashActive)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
                bfInterruptible = st.normalizedTime >= backFlashNoInterruptNorm;
        }
        SafeSetBool(PARAM_BackFlashInterruptible, bfInterruptible);

        SafeSetFloat(PARAM_MoveSpeed, Mathf.Abs(currentSpeedX));
        SafeSetBool(PARAM_IsGrounded, isGrounded);
        SafeSetBool(PARAM_IsDucking,
            isDucking ||
            shieldActiveDuck ||
            (groundAttackActive && duckAttackFacingLocked));
        SafeSetBool(PARAM_IsFalling, !isGrounded && rb.velocity.y < 0);
    }
    #endregion

    // 1) Safe 助手：防止 Animator 里被删参数/Trigger 报错
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

    // 工具：检测墙（法线判定）
    private void UpdateWallProximity()
    {
        nearWall = false;
        wallSide = 0;

        if (!wallJumpEnabled || isGrounded) return;

        // 计算阈值：法线需近水平 → |ny| <= sin(tol)
        float nyTol = Mathf.Sin(wallNormalVerticalToleranceDeg * Mathf.Deg2Rad);

        Vector2 pos = transform.position;
        Vector2[] sampleOffsets = {
        new Vector2(0f, wallCheckYOffset),
        new Vector2(0f, -wallCheckYOffset),
    };

        // 右侧探测
        foreach (var off in sampleOffsets)
        {
            var hit = Physics2D.Raycast(pos + off, Vector2.right, wallCheckDistance, wallMask);
            if (hit && Mathf.Abs(hit.normal.y) <= nyTol && hit.normal.x < 0f)
            {
                nearWall = true; wallSide = +1; return;
            }
        }
        // 左侧探测
        foreach (var off in sampleOffsets)
        {
            var hit = Physics2D.Raycast(pos + off, Vector2.left, wallCheckDistance, wallMask);
            if (hit && Mathf.Abs(hit.normal.y) <= nyTol && hit.normal.x > 0f)
            {
                nearWall = true; wallSide = -1; return;
            }
        }
    }

    // 触发踩墙（空中 + 贴墙 + 朝墙方向保持 + 本帧K + 冷却）
    private void HandleWallJumpInput()
    {
        if (!wallJumpEnabled) return;
        if (IsHardLocked() || IsAutoPhaseLocked()) return;

        // 新增：空中攻击期间禁止踩墙反跳
        if (airAttackActive || airAttackAnimPlaying) return;
        // 可选兜底（更稳）：检查 Animator 当前是否在空攻状态
        // var st = anim.GetCurrentAnimatorStateInfo(0);
        // if (st.IsName("player_jump_attack") || st.IsName("player_jump_downForward_attack")) return;

        if (isGrounded || !nearWall) return;
        if (!keyDownJump) return;
        if (Time.time - wallJumpLastTime < wallJumpCooldown) return;

        float dir = GetEffectiveInputDir();
        if (Mathf.Abs(dir) <= 0.01f || Mathf.Sign(dir) != wallSide) return;

        // 新增：如当前处于空中攻击，先立即结束空中攻击（由墙跳覆盖）
        ForceEndAirAttack();

        // 进入转身硬锁
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

    // 转身播完 → 发射 player_jump_forward + 自动斜跳锁定
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
            // 不在该状态且不在过渡，视为已结束
        }

        // 发射
        rb.gravityScale = savedGravityScale;
        float xSpd = (wallJumpSpeedX > 0f ? wallJumpSpeedX : moveSpeed);
        float ySpd = (wallJumpSpeedY > 0f ? wallJumpSpeedY : jumpForce);
        int away = (wallSide == +1) ? -1 : +1;

        rb.velocity = new Vector2(away * xSpd, ySpd);

        // 切前跳动画
        anim.ResetTrigger(TRIG_JumpForward);
        anim.SetTrigger(TRIG_JumpForward);

        // 状态更新
        wallTurnActive = false;
        wallJumpAutoPhase = true;
        wallJumpControlUnlocked = false;
        wallJumpStartY = rb.position.y;

        // 标记“正在跳跃”，以配合你的其它跳高限制
        isJumping = true;
        jumpStartY = rb.position.y;

        // 墙反跳发射后，结束二段跳的“姿势保持”，但不恢复配额
        doubleJumpActive = false;
        doubleJumpPoseHold = false;
    }

    // 动画事件回调：player_jump_forward 的关键帧调用
    public void OnWallJumpForwardUnlock()
    {
        wallJumpControlUnlocked = true;
        wallJumpAutoPhase = false;
    }

    // 反跳高度限制（与普通 maxJumpHeight 类似，但按反跳起点限高）
    private void EnforceMaxWallJumpHeight()
    {
        if (wallTurnActive) return;
        // 自动斜跳期 或 一般空中跳跃期
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

        // 清除转身/自动斜跳锁定
        wallTurnActive = false;
        wallJumpAutoPhase = false;
        wallJumpControlUnlocked = false;

        // 恢复重力（若转身阶段被置为0）
        rb.gravityScale = savedGravityScale;

        // 立即停住水平速度（避免继续滑行）
        if (stopHorizontalImmediately)
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        // 清掉可能残留的动画触发器（保险）
        SafeResetTrigger(TRIG_WallTurn);
        SafeResetTrigger(TRIG_JumpForward);
    }

    // 二段跳触发后，立即解除墙反跳自动锁，恢复水平控制
    private void HandleDoubleJump()
    {
        if (!doubleJumpEnabled) return;
        if (isGrounded) { doubleJumpActive = false; return; }
        if (IsHardLocked()) return;            // 墙转身硬锁期禁用
        if (magicAttackPlaying) return;        // 魔法攻击期间禁用

        // 还有配额才允许触发
        if (keyDownJump && extraJumpsUsed < extraJumpsPerAir)
        {
            // 空攻动画进行中不触发，避免冲突
            if (airAttackActive || airAttackAnimPlaying) return;

            extraJumpsUsed++;                  // 消耗一次配额
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

            if (shieldHeld) relay?.PlayShieldStanding();

            // 若正处在墙反跳自动锁定，二段跳触发后立即解除，允许A/D控制
            if (wallJumpAutoPhase && !wallJumpControlUnlocked)
            {
                wallJumpAutoPhase = false;
                wallJumpControlUnlocked = true;
            }
        }
    }

    // 二段跳相对高度限制（从二段起跳点起算） =====
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

    // 二段跳动画保持：动画播完但仍在上升，钉住最后一帧 =====
    private void HoldDoubleJumpPoseWhileAscending()
    {
        if (!doubleJumpActive) return;
        if (rb.velocity.y <= 0f) { doubleJumpPoseHold = false; return; }

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName(STATE_JumpDouble) && !anim.IsInTransition(0) && st.normalizedTime >= 0.98f && doubleJumpPoseHold)
        {
            // 将动画钉在末帧
            anim.CrossFade(STATE_JumpDouble, 0f, 0, 0.98f);
            anim.Update(0f);
        }
    }

    // 过顶点开始下落 -> 退出二段跳保持（下落由你现有 Trig_JumpDown 触发） =====
    private void AutoExitDoubleJumpOnFall()
    {
        if (!doubleJumpActive) return;
        if (rb.velocity.y <= 0f)
        {
            doubleJumpActive = false;
            doubleJumpPoseHold = false;
        }
    }

    // 仅按 isDucking 切换启停；不做任何“头顶空间”门禁检查
    private void SyncCrouchColliders()
    {
        if (!standingCollider || !duckCollider) return;

        bool wantDuck = isDucking;

        // 状态未变化就不重复切换
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

        // 立即同步物理，避免一帧延迟
        Physics2D.SyncTransforms();
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

    // 2) 对外暴露地面状态，避免 Relay 再读 Animator 的 IsGrounded
    public bool IsGroundedNow => isGrounded;
}