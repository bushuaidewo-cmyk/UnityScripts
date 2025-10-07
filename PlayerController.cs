using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector (Minimal + Shield + BackFlash)
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private Transform groundPoint;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.1f;      // 新增：用于直接驱动盾Hub
    [SerializeField] private AnimationEventRelay relay;

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

    [Header("后退闪避 (BackFlash)")]     // 不再暴露按键，固定 I 作为后退闪避键
    public float backFlashSpeed = 7f;           // 后退速度
    public float backFlashDistance = 2.5f;      // 后退距离（米）—以“速度+距离”计算收尾
    [Range(0f, 1f)]
    public float backFlashNoInterruptNorm = 0.5f;   // 动画前50%禁止方向键打断
    [Range(0f, 1f)]
    public float backFlashReTriggerNorm = 0.9f;     // 动画90%后才允许再次触发

    [Header("魔法 (Magic)")]
    public float magicAttackAirDuration = 0.4f; // 空中魔法攻击锁定时长（无动画时用）
    public int magicReenterStillFrames = 3;     // W按住时自动回归前需要连续静止的帧数（去抖）
    #endregion

    #region Animator 常量
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string PARAM_IsGrounded = "IsGrounded";
    private const string PARAM_IsDucking = "IsDucking";
    private const string PARAM_IsFalling = "IsFalling";
    private const string PARAM_ShieldHold = "ShieldHold";
    private const string PARAM_BackFlashInterruptible = "BackFlashInterruptible";
    private const string PARAM_BackFlashActive = "BackFlashActive"; // 新增：给 Relay 用于屏蔽事件
    private const string PARAM_MagicHold = "MagicHold"; // W 按住

    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_Attack = "Trig_Attack";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";

    private const string TRIG_ShieldUp = "Trig_ShieldUp";
    private const string TRIG_ShieldDown = "Trig_ShieldDown";
    private const string TRIG_DuckShieldUp = "Trig_DuckShieldUp";
    private const string TRIG_DuckShieldDown = "Trig_DuckShieldDown";

    private const string TRIG_BackFlash = "Trig_BackFlash";

    // Magic
    private const string TRIG_MagicUp = "Trig_MagicUp";
    private const string TRIG_MagicDown = "Trig_MagicDown";
    private const string TRIG_MagicAttack = "Trig_MagicAttack";

    private const string STATE_JumpAttack = "player_jump_attack";
    private const string STATE_JumpDownFwdAttack = "player_jump_downForward_attack";
    private const string STATE_BackFlash = "player_backflash";

    // Magic states
    private const string STATE_MagicUp = "player_magic_up";
    private const string STATE_MagicIdle = "player_shield_idle";
    private const string STATE_MagicAttack = "player_magic_attack";
    private const string STATE_MagicDown = "player_magic_down";
    private const string STATE_IdleStart = "player_idle_start";  // 强制退出magic时切回的站立状态（按你的Controller调整名字）

    private static readonly string[] AirAttackAnimStates = {
        STATE_JumpAttack,
        STATE_JumpDownFwdAttack
    };
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

    // 输入
    private float rawInputX;
    private bool keyDownLeft;
    private bool keyDownRight;
    private bool keyDownJump;
    private bool keyDownAttack;
    private bool keyDownBackFlash;

    // W 魔法输入
    private bool keyDownMagic;
    private bool keyUpMagic;
    private bool magicHeldKey;

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
    private int magicStillCounter = 0;         // 自动回归的“连续静止帧”计数
    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>(); // 自动抓取
    }

    private void Update()
    {
        CaptureInput();
        CheckGrounded();
        HandleJump();         // 跳跃优先（会打断魔法 idle/up/down）
        HandleShield();       // 举盾优先（会打断魔法 idle/up/down）
        HandleMagic();        // 新增：处理 W 施法（在攻击/移动等之前拦截 J 触发的魔法）

        // 判断当前是否在 backflash 播放或过渡（用于门禁与清理多余 Trigger）
        var stNow = anim.GetCurrentAnimatorStateInfo(0);
        bool inBackflashAnimOrTransition = stNow.IsName(STATE_BackFlash) || anim.IsInTransition(0);

        // 后退闪避触发：地面、非下蹲、非盾、非地面攻击、且不在播、不在过渡、未上锁、且非魔法攻击中
        if (keyDownBackFlash &&
            isGrounded && !isDucking &&
            !AnyShieldActive() &&
            !groundAttackActive &&
            !backFlashActive &&
            !backFlashLock &&
            !inBackflashAnimOrTransition &&
            !magicAttackPlaying)
        {
            // 若处于魔法 up/idle/down，闪避覆盖魔法
            if (magicActive && !magicAttackPlaying) CancelMagic();
            StartBackFlash();
        }
        else if (keyDownBackFlash && (backFlashActive || backFlashLock || inBackflashAnimOrTransition))
        {
            // 动画期间或上锁期间误触发：吃掉多余 Trigger，避免 Animator 白点二次闪导致再次过渡

            SafeResetTrigger(TRIG_BackFlash);
        }

        HandleGroundDuckAndAttacks();
        HandleAirAttack();
        HandleHorizontal();
        HandleVariableJumpCut();
        HandleFacingFlip();
        EnforceMaxJumpHeight();
        AutoEndAirAttack();
        AutoEndBackFlash_ByDistance();      // 按距离结束位移
        AutoExitBackFlashOnStateLeave();    // 离开 backflash 状态就退出 BackFlash

        // 动画真正离开 backflash 才解锁（防止“快结束连按I又后退一次”）
        var stAfter = anim.GetCurrentAnimatorStateInfo(0);
        if (backFlashLock && !stAfter.IsName(STATE_BackFlash) && !anim.IsInTransition(0))
            backFlashLock = false;

        UpdateAnimatorParams();

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
        keyUpMagic = Input.GetKeyUp(KeyCode.W);
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
            isJumping = false;
            airAttackActive = false;
            airAttackAnimPlaying = false;
            if (shieldHeld && !shieldActiveStanding && !shieldActiveDuck)
                TryActivateStandingShield(true);
        }
    }

    private void HandleVariableJumpCut()
    {
        if (!variableJump) return;
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

        // BackFlash 非可打断区间：屏蔽盾/下蹲（修复 I→L→S 卡死与盾抖动）
        if (backFlashActive && !IsBackFlashInterruptibleNow())
        {
            // 可选：若已有盾在显示，可以在此隐藏可视层，但通常不必
            return;
        }

        // BackFlash 可打断后：按 L 允许先打断 BackFlash，再进入举盾逻辑
        if (backFlashActive && IsBackFlashInterruptibleNow() && shieldHeld && !magicAttackPlaying)
        {
            CancelBackFlash();
            // 继续走举盾流程
        }

        // 新增：空中攻击期间禁止盾（关键修复）
        // 说明：空中攻击一旦开始（airAttackActive/airAttackAnimPlaying），
        // 每帧都取消空中盾，且不允许因 L 按住而重新举起
        if (airAttackActive || airAttackAnimPlaying)
        {
            if (shieldActiveAir) CancelShieldAir(); // 内部会 relay?.StopShield()
                                                    // 此处直接返回，避免下面空中/地面分支重新举盾
            return;
        }

        // 盾随时打断后退闪避 / 魔法 up idle down
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
                    relay?.PlayShieldStanding();   // 空中也显示盾（若希望与站立同款，可直接用 PlayShieldStanding）
                }
            }
            else if (shieldActiveAir)
            {
                CancelShieldAir(); // 里面会 StopShield
            }
            return;
        }

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
                    ActivateDuckShield(); // 内部已处理“立即/延迟”
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
                    CancelShieldDuck(true); // 内部已处理“立即/延迟”
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
                TryActivateStandingShield(true); // 内部已处理“立即/延迟”
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

    //允许“立即”播放盾Up
    private void TryActivateStandingShield(bool playAnimationIfStill)
    {
        shieldActiveStanding = true;
        shieldActiveDuck = false;
        shieldActiveAir = false;
        shieldAnimUpPlayed = false;

        // 关键改动：只要打开 shieldInstantVisual 就不再等静止
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

    // 收站立盾（需要 Down）
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

        //魔法攻击阶段：必须播完，期间禁止其它行为接管
        if (magicAttackPlaying)
        {
            if (isGrounded)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                // 修复：只要已经不在 attack（含过渡离开），或已到尾帧，就结束锁定
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

        //W 首次按下：立即尝试进入魔法（动画仅在“未封锁”时播放）
        if (keyDownMagic)
        {
            magicActive = true;
            anim.SetBool(PARAM_MagicHold, true);
            magicStillCounter = 0; // 重置去抖计数

            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger(TRIG_MagicUp);
                anim.SetTrigger(TRIG_MagicUp);
            }
        }

        //W 按住期间：自动回归 & 自动覆盖
        if (magicHeldKey && !magicAttackPlaying)
        {
            bool blocked = IsMagicAnimBlockedByOtherActions();

            if (!magicActive)
            {
                // 不在魔法态且W仍按着：若环境稳定静止连N帧，自动回归Up→Idle
                if (!blocked)
                {
                    magicStillCounter++;
                    if (magicStillCounter >= magicReenterStillFrames)
                    {
                        magicActive = true;
                        anim.SetBool(PARAM_MagicHold, true);
                        anim.ResetTrigger(TRIG_MagicUp);
                        anim.SetTrigger(TRIG_MagicUp);
                        magicStillCounter = 0;
                    }
                }
                else
                {
                    magicStillCounter = 0;
                }
            }
            else
            {
                // 已在魔法态，但被其它行为封锁：自动被覆盖（不强制Down），退出逻辑态
                if (blocked)
                {
                    CancelMagic(); // 会关闭Hold并强制切出动画（见下方实现）
                    magicStillCounter = 0;
                }
            }
        }

        //W按住 + J：触发魔法攻击（消耗J）
        if (magicActive && magicHeldKey && keyDownAttack)
        {
            StartMagicAttack();
            return;
        }

        //松开W：退出魔法准备（地面静止时播放Down）
        if (!magicHeldKey && magicActive)
        {
            if (!IsMagicAnimBlockedByOtherActions())
            {
                anim.ResetTrigger(TRIG_MagicDown);
                anim.SetTrigger(TRIG_MagicDown);
            }
            CancelMagic(); // 逻辑层退出
            magicStillCounter = 0;
        }
    }

    private void StartMagicAttack()
    {
        magicAttackPlaying = true;
        magicAttackStartTime = Time.time;

        //地面才播动画；空中无表现，但会锁定到时长结束
        if (isGrounded && CanPlayMagicAnimOnGround())
        {
            anim.ResetTrigger(TRIG_MagicAttack);
            anim.SetTrigger(TRIG_MagicAttack);
        }
    }

    private void EndMagicAttack()
    {
        magicAttackPlaying = false;

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
        magicStillCounter = 0;
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
        if (!isGrounded) return;

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
    public void OnAttackStart() { }
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
    public void OnDuckAttackStart() { }
    public void OnDuckAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
        if (shieldHeld && isGrounded && isDucking)
            ActivateDuckShield();
    }
    public void OnDuckFwdAttackStart() { }
    public void OnDuckFwdAttackEnd()
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
        if (magicAttackPlaying)
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

    // 供 Relay 查询：是否处于后闪动画或其过渡（稳定屏蔽 ShieldRefresh）
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
    private void LateUpdate()
    {
        // 后闪动画/过渡期间完全不动盾Hub，避免抖动
        if (IsInBackFlashAnimOrTransition()) return;

        if (shieldHeld)
        {
            if (isGrounded)
            {
                // 地面：S=蹲盾；否则站盾
                if (Input.GetKey(KeyCode.S))
                {
                    // 标记逻辑态（不触发可视切换抖动）
                    if (!shieldActiveDuck)
                    {
                        shieldActiveDuck = true;
                        shieldActiveStanding = false;
                        shieldActiveAir = false;
                    }
                    // 可视层：确保是下蹲盾
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
                // 空中：一律用站立盾的可视层
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

        // 后退位移中、魔法攻击中禁止翻转
        if ((backFlashActive && backFlashMoving) || magicAttackPlaying) return;

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
    private void CheckGrounded()
    {
        if (!groundPoint) { isGrounded = false; return; }
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundCheckRadius, groundLayer);
    }
    #endregion

    #region Animator
    // UpdateAnimatorParams：BackFlashInterruptible 用 Safe；其余参数可选也用 Safe
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

        // 下列参数如果 Animator 一定存在，可保留原 SetX；若也可能被清理，改用 SafeSet*
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