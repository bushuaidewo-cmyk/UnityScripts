using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector (Minimal Set)
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private Transform groundPoint;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.1f;

    [Header("移动")]
    public float moveSpeed = 5f;
    public float groundAcceleration = 40f;      // 可选：若不要平滑，可删除并用瞬时赋值
    public float groundDeceleration = 60f;      // 可选
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
    #endregion

    #region Animator 常量
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string PARAM_IsGrounded = "IsGrounded";
    private const string PARAM_IsDucking = "IsDucking";
    private const string PARAM_IsFalling = "IsFalling";

    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_Attack = "Trig_Attack";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";

    private static readonly string[] AirAttackAnimStates = {
        "player_jump_attack",
        "player_jump_downForward_attack"
    };
    #endregion

    #region Runtime
    private Animator anim;
    private Rigidbody2D rb;

    // 基本状态
    private bool isGrounded;
    private bool prevGrounded;
    private bool isDucking;
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

    // 方向缓冲（脉冲）
    private float tapImpulseDir;
    private int tapImpulseRemain;

    // 移动
    private float currentSpeedX;
    private bool facingRight = true;
    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        CaptureInput();
        CheckGrounded();
        HandleJump();
        HandleGroundDuckAndAttacks();
        HandleAirAttack();
        HandleHorizontal();
        HandleVariableJumpCut();
        HandleFacingFlip();
        EnforceMaxJumpHeight();
        AutoEndAirAttack();
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
        if (keyDownJump && isGrounded && !isJumping && !groundAttackActive)
        {
            isJumping = true;
            jumpStartY = rb.position.y;

            float dir = GetEffectiveInputDir();
            currentSpeedX = dir * moveSpeed;   // 没有额外 boost
            rb.velocity = new Vector2(currentSpeedX, jumpForce);

            // 下蹲状态起跳取消下蹲
            isDucking = false;

            string trig = Mathf.Abs(dir) > 0.05f ? TRIG_JumpForward : TRIG_JumpUp;
            anim.ResetTrigger(TRIG_JumpForward);
            anim.ResetTrigger(TRIG_JumpUp);
            anim.SetTrigger(trig);
        }

        if (!prevGrounded && isGrounded)
        {
            isJumping = false;
            airAttackActive = false;
            airAttackAnimPlaying = false;
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

    #region 下蹲 & 地面攻击
    private void HandleGroundDuckAndAttacks()
    {
        if (!isGrounded) return;

        // 下蹲判定
        if (Input.GetKey(KeyCode.S))
        {
            isDucking = true;
        }
        else
        {
            isDucking = false;
        }

        // 下蹲攻击（即时）
        if (isDucking && keyDownAttack && allowInstantDuckAttack && !groundAttackActive)
        {
            StartDuckAttack();
            return;
        }

        // 站立攻击
        if (!isDucking && keyDownAttack && !groundAttackActive)
        {
            StartGroundAttack();
        }
    }

    private void StartGroundAttack()
    {
        groundAttackActive = true;
        anim.SetTrigger(TRIG_Attack);
    }

    private void StartDuckAttack()
    {
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
    }
    public void OnDuckAttackStart() { }
    public void OnDuckAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
    }
    public void OnDuckFwdAttackStart() { }
    public void OnDuckFwdAttackEnd()
    {
        groundAttackActive = false;
        duckAttackFacingLocked = false;
    }
    #endregion

    #region 空中攻击
    private void HandleAirAttack()
    {
        if (isGrounded) return;
        if (airAttackActive) return;

        if (keyDownAttack)
        {
            // 防重：当前动画是不是空攻
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
                }
                break;
            }
        }
        if (!inAtk && airAttackAnimPlaying &&
            Time.time - airAttackStartTime >= airAttackMinDuration)
        {
            airAttackActive = false;
            airAttackAnimPlaying = false;
        }
    }
    #endregion

    #region 水平移动
    private void HandleHorizontal()
    {

        float dir = GetEffectiveInputDir();

        // 若地面攻击（站立攻击）期间锁朝向与移动：清零
        if (groundAttackActive && !isDucking && !duckAttackFacingLocked)
        {
            ApplyHorizontal(0f);
            return;
        }


        // ---- Duck Attack 全程锁水平（前 N 帧计数 + 之后保持 0）----
        if (groundAttackActive && duckAttackFacingLocked)
        {
            if (duckAttackHorizLockRemain > 0)
                duckAttackHorizLockRemain--;
            ApplyHorizontal(0f);
            return;
        }

        // 普通移动 / 下蹲保持静止
        if (isDucking)
        {
            ApplyHorizontal(0f);
            return;
        }

        // 计算速度（含加减速，可选）
        float target = dir * moveSpeed;
        float accel = Mathf.Abs(dir) > 0.01f ? groundAcceleration : groundDeceleration;
        currentSpeedX = Mathf.MoveTowards(currentSpeedX, target, accel * Time.deltaTime);

        if (Mathf.Abs(currentSpeedX) < stopThreshold)
            currentSpeedX = 0f;

        rb.velocity = new Vector2(currentSpeedX, rb.velocity.y);
    }

    private void ApplyHorizontal(float x)
    {
        currentSpeedX = x;
        rb.velocity = new Vector2(x, rb.velocity.y);
    }
    #endregion

    #region Facing
    private void HandleFacingFlip()
    {
        float dir = GetEffectiveInputDir();
        if (Mathf.Abs(dir) < 0.01f) return;

        // 锁面向：地面攻击 or 下蹲攻击锁帧
        if (groundAttackActive && (duckAttackFacingLocked || (!isDucking && groundAttackActive)))
            return;
        if (airAttackActive && !allowAirImmediateTurn)
            return;

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
    private void UpdateAnimatorParams()
    {
        anim.SetFloat(PARAM_MoveSpeed, Mathf.Abs(currentSpeedX));
        anim.SetBool(PARAM_IsGrounded, isGrounded);
        anim.SetBool(PARAM_IsDucking, isDucking || (groundAttackActive && duckAttackFacingLocked)); // 下蹲攻击保持下蹲体态
        anim.SetBool(PARAM_IsFalling, !isGrounded && rb.velocity.y < 0);
    }
    #endregion
}