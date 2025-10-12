using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector (Minimal + Shield + BackFlash)
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private AnimationEventRelay relay;

    [Header("地面检测")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("子物体上的不规则Trigger，用于检测地面；名字建议为 CheckGroundPoint1")]
    [SerializeField] private Collider2D checkGroundPoint1;
    [Tooltip("子物体上的不规则Trigger，用于检测地面；名字建议为 CheckGroundPoint2")]
    [SerializeField] private Collider2D checkGroundPoint2;

    [Header("踩墙层")]
    [Tooltip("可踩墙图层（建议墙体的Collider放在这些图层）")]
    [SerializeField] private LayerMask wallMask;

    [Header("碰撞体切换")]
    [Tooltip("站立形态使用的碰撞体（建议 BoxCollider2D ）")]
    [SerializeField] private Collider2D standingCollider;
    [Tooltip("下蹲形态使用的碰撞体（建议 BoxCollider2D ）")]
    [SerializeField] private Collider2D duckCollider;

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
    [Range(0f, 1f)] public float backFlashNoInterruptNorm = 0.5f;
    [Range(0f, 1f)] public float backFlashReTriggerNorm = 0.9f;

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
    //移动
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string TRIG_Attack = "Trig_Attack";
    private const string STATE_IdleStart = "player_idle_start";

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
    private const string STATE_JumpDouble = "player_jump_double";

    //盾
    private const string PARAM_ShieldHold = "ShieldHold";
    private const string TRIG_ShieldUp = "Trig_ShieldUp";
    private const string TRIG_ShieldDown = "Trig_ShieldDown";

    //魔法
    private const string PARAM_MagicHold = "MagicHold";
    private const string TRIG_MagicUp = "Trig_MagicUp";
    private const string TRIG_MagicDown = "Trig_MagicDown";
    private const string TRIG_MagicAttack = "Trig_MagicAttack";
    private const string STATE_MagicUp = "player_magic_up";
    private const string STATE_MagicIdle = "player_shield_idle";
    private const string STATE_MagicAttack = "player_magic_attack";
    private const string STATE_MagicDown = "player_magic_down";

    //闪避后退
    private const string PARAM_BackFlashInterruptible = "BackFlashInterruptible";
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
    private float backFlashStartX = 0f;
    private bool backFlashLock = false;
    private float backFlashStartTime = 0f;
    private float backFlashMaxDuration = 0f;
    private bool backFlashMoving = false;

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

    private bool IsAutoPhaseLocked() => wallJumpAutoPhase && !wallJumpControlUnlocked;
    private bool IsHardLocked() => wallTurnActive;
    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>();

        // 自动绑定两个检测Trigger（可手动在Inspector拖拽覆盖）
        if (!checkGroundPoint1)
        {
            var t1 = transform.Find("Ground Point/CheckGroundPoint1") ?? transform.Find("CheckGroundPoint1");
            if (t1) checkGroundPoint1 = t1.GetComponent<Collider2D>();
        }
        if (!checkGroundPoint2)
        {
            var t2 = transform.Find("Ground Point/CheckGroundPoint2") ?? transform.Find("CheckGroundPoint2");
            if (t2) checkGroundPoint2 = t2.GetComponent<Collider2D>();
        }

        // 仅提示：建议勾选 IsTrigger
        if (checkGroundPoint1 && !checkGroundPoint1.isTrigger)
            Debug.LogWarning("CheckGroundPoint1 建议勾选 IsTrigger。", checkGroundPoint1);
        if (checkGroundPoint2 && !checkGroundPoint2.isTrigger)
            Debug.LogWarning("CheckGroundPoint2 建议勾选 IsTrigger。", checkGroundPoint2);

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

        //墙跳三件套
        HandleWallJumpInput();
        PollWallTurnAndLaunch();
        EnforceMaxWallJumpHeight();

        // 盾
        if (!(IsHardLocked() || IsAutoPhaseLocked()))
            HandleShield();

        //魔法
        if (IsHardLocked() || IsAutoPhaseLocked())
            anim.SetBool(PARAM_MagicHold, false);
        else
            HandleMagic();

        // 二段跳相关
        HandleDoubleJump();
        EnforceMaxDoubleJumpHeight();
        HoldDoubleJumpPoseWhileAscending();
        AutoExitDoubleJumpOnFall();

        //BackFlash 触发逻辑
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
        AutoEndBackFlash_ByDistance();
        AutoExitBackFlashOnStateLeave();

        // backflash 解锁
        var stAfter = anim.GetCurrentAnimatorStateInfo(0);
        if (backFlashLock && !stAfter.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
            backFlashLock = false;

        UpdateAnimatorParams();

        // 统一的“下落动画触发”（不再依赖防抖）
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
            if (backFlashActive) CancelBackFlash();
            if (magicActive && !magicAttackPlaying) CancelMagic();
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
                landDebounceUntil = Time.time + 0.05f; // 50ms 去抖
                SafeResetTrigger(TRIG_Land);
                SafeSetTrigger(TRIG_Land);
            }

            if (shieldHeld && !shieldActiveStanding && !shieldActiveDuck)
                TryActivateStandingShield(true);

            // 重置额外跳配额
            extraJumpsUsed = 0;
            doubleJumpActive = false;
            doubleJumpPoseHold = false;
        }
    }

    // 空中“转向切前跳”（只切动画，不改物理）
    private void HandleAirJumpForwardSwitch()
    {
        if (isGrounded) return;
        if (rb.velocity.y <= 0f) return;

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

    private bool IsBackFlashInterruptibleNow()
    {
        if (!backFlashActive) return true;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (!st.IsName(STATE_BackFlash) || anim.IsInTransition(0)) return false;
        return st.normalizedTime >= backFlashNoInterruptNorm;
    }

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

        // BackFlash 非可打断区间：屏蔽盾/下蹲
        if (backFlashActive && !IsBackFlashInterruptibleNow())
            return;

        // BackFlash 可打断后：按 L 允许先打断 BackFlash，再进入举盾逻辑
        if (backFlashActive && IsBackFlashInterruptibleNow() && shieldHeld && !magicAttackPlaying)
        {
            CancelBackFlash();
        }

        // 空中攻击期间禁止盾
        if (airAttackActive || airAttackAnimPlaying)
        {
            if (shieldActiveAir) CancelShieldAir();
            return;
        }

        // 盾随时打断魔法/后退闪避
        if ((backFlashActive || magicActive) && shieldHeld && !magicAttackPlaying)
        {
            if (backFlashActive) CancelBackFlash();
            if (magicActive) CancelMagic();
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

    private void PlayStandingShieldUp()
    {
        SafeResetTrigger(TRIG_ShieldDown);
        SafeSetTrigger(TRIG_ShieldUp);
        shieldAnimUpPlayed = true;
        relay?.PlayShieldStanding();
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
        relay?.PlayShieldDuck();
    }

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
        relay?.StopShield();
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

        backFlashStartX = rb.position.x;

        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f);

        currentSpeedX = 0f;

        int dir = facingRight ? -1 : 1;
        rb.velocity = new Vector2(dir * backFlashSpeed, rb.velocity.y);
    }

    private void RestartBackFlashOverride()
    {
        backFlashActive = true;
        backFlashMoving = true;

        backFlashStartX = rb.position.x;
        float spd = Mathf.Max(0.01f, backFlashSpeed);
        backFlashStartTime = Time.time;
        backFlashMaxDuration = (backFlashDistance / spd) + 0.05f;

        SafeResetTrigger(TRIG_BackFlash);
        anim.CrossFadeInFixedTime(STATE_BackFlash, 0f, 0, 0f);
        anim.Update(0f);

        currentSpeedX = 0f;
        int dir = facingRight ? -1 : 1;
        rb.velocity = new Vector2(dir * backFlashSpeed, rb.velocity.y);
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
                anim.ResetTrigger(TRIG_MagicUp);
                anim.SetTrigger(TRIG_MagicUp);
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

        if (isGrounded && CanPlayMagicAnimOnGround()) { anim.ResetTrigger(TRIG_MagicAttack); anim.SetTrigger(TRIG_MagicAttack); }
    }

    private void EndMagicAttack()
    {
        magicAttackPlaying = false;

        magicAttackAvailableAt = Time.time + magicAttackCooldown;

        if (!magicHeldKey)
        {
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
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);
        }
    }

    private void CancelMagic()
    {
        if (magicAttackPlaying) return;

        magicActive = false;
        anim.SetBool(PARAM_MagicHold, false);

        anim.ResetTrigger(TRIG_MagicUp);
        anim.ResetTrigger(TRIG_MagicAttack);
        anim.ResetTrigger(TRIG_MagicDown);

        var st = anim.GetCurrentAnimatorStateInfo(0);
        if (st.IsName(STATE_MagicIdle) || st.IsName(STATE_MagicUp) || st.IsName(STATE_MagicDown))
        {
            anim.CrossFadeInFixedTime(STATE_IdleStart, 0f, 0, 0f);
            anim.Update(0f);
        }
    }

    private bool IsMagicAnimBlockedByOtherActions()
    {
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
            isDucking = false;
            return;
        }

        if (isJumping)
        {
            isDucking = false;
            return;
        }

        if (shieldActiveDuck)
            isDucking = true;
        else
            isDucking = Input.GetKey(KeyCode.S);

        if (isDucking && magicActive && !magicAttackPlaying) CancelMagic();

        if (keyDownAttack && AnyShieldActive())
        {
            CancelShieldStanding(true);
            CancelShieldDuck(true);
            CancelShieldAir();
        }

        if (magicActive || magicAttackPlaying)
        {
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
        if (backFlashActive) CancelBackFlash();
        if (magicActive && !magicAttackPlaying) CancelMagic();
        if (magicAttackPlaying) return;

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

            var st = anim.GetCurrentAnimatorStateInfo(0);
            foreach (var a in AirAttackAnimStates)
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

    private void ForceEndAirAttack()
    {
        if (!airAttackActive && !airAttackAnimPlaying) return;
        airAttackActive = false;
        airAttackAnimPlaying = false;
    }
    #endregion

    #region 水平移动
    private void HandleHorizontal()
    {
        // 后退闪避优先
        if (backFlashActive)
        {
            var stBF = anim.GetCurrentAnimatorStateInfo(0);
            bool inBFState = stBF.IsName(STATE_BackFlash);
            float bfNorm = inBFState ? stBF.normalizedTime : 0f;

            bool inNoInterruptWindow = inBFState && !anim.IsInTransition(0) && bfNorm < backFlashNoInterruptNorm;

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

            if (Input.GetKey(KeyCode.S))
            {
                rb.velocity = new Vector2(0f, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }

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

            if (inNoInterruptWindow)
            {
                int d0 = facingRight ? -1 : 1;
                rb.velocity = new Vector2(d0 * backFlashSpeed, rb.velocity.y);
                currentSpeedX = 0f;
                return;
            }

            float input = GetEffectiveInputDir();
            if (Mathf.Abs(input) > 0.01f)
            {
                CancelBackFlash();
                currentSpeedX = input * moveSpeed;
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

        // 普通下蹲锁
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

        if ((backFlashActive && backFlashMoving) || (magicAttackPlaying && isGrounded)) return;

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
    private void CheckGrounded()
    {
        // — 主判定：两个 Trigger 是否与地面重叠 —
        bool g1 = checkGroundPoint1 && checkGroundPoint1.IsTouchingLayers(groundLayer);
        bool g2 = checkGroundPoint2 && checkGroundPoint2.IsTouchingLayers(groundLayer);
        bool footHit = g1 || g2;

        // — 进入/离地：不做延时，立刻取值 —
        isGrounded = footHit;
    }

    // Gizmos 可视化（画出两个 Trigger 的 bounds，方便对齐）
    private void OnDrawGizmosSelected()
    {
        if (!enabled) return;

        if (checkGroundPoint1)
        {
            var b = checkGroundPoint1.bounds;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(b.center, b.size);
        }
        if (checkGroundPoint2)
        {
            var b = checkGroundPoint2.bounds;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
    #endregion

    #region Animator
    private void UpdateAnimatorParams()
    {
        SafeSetFloat(PARAM_MoveSpeed, Mathf.Abs(currentSpeedX));
        SafeSetBool(PARAM_ShieldHold, shieldHeld);
        SafeSetBool(PARAM_IsGrounded, isGrounded);
        SafeSetBool(PARAM_IsDucking,
            isDucking ||
            shieldActiveDuck ||
            (groundAttackActive && duckAttackFacingLocked));
        SafeSetBool(PARAM_IsFalling, !isGrounded && rb.velocity.y < 0);
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

    // 工具：检测墙（法线判定）
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

            if (shieldHeld) relay?.PlayShieldStanding();

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

    public bool IsGroundedNow => isGrounded;
}