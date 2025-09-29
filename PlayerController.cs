using UnityEngine;
using System.Linq;

public class PlayerController : MonoBehaviour
{
    [Header("组件")]
    private Rigidbody2D rb;
    private Animator anim;

    [Header("翻转节点 (Flip)")]
    public Transform flipRoot;

    [Header("调试")]
    public bool debugJumpLog = true;
    public bool logMissingAnimatorParams = true;
    public bool debugTurnLog = true;
    public bool debugAirAttackLog = true;

    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float groundAcceleration = 40f;
    public float airAcceleration = 25f;
    public float groundDeceleration = 60f;
    public float airDeceleration = 30f;
    public float runStopThreshold = 0.05f;

    [Header("跳跃参数")]
    public float jumpForce = 10f;
    public float maxJumpHeight = 5f;
    public bool enableVariableJump = true;
    public float variableJumpCutMultiplier = 0.5f;

    [Header("转身参数")]
    public bool turnLocksMovement = false;
    public TurnFlipMode turnFlipMode = TurnFlipMode.ByEvent;
    public enum TurnFlipMode { ByEvent, Immediate }
    public bool allowAirImmediateTurn = true;  // 空中瞬时转向（不播 turn）

    private bool isTurning = false;
    private bool desiredFacingRight = true;
    private bool hasFlippedInThisTurn = false;

    [Header("空中攻击参数（普通 & 下落前倾）")]
    public bool allowAirMoveDuringAirAttack = true;   // 空中攻击允许水平漂移

    // 为防止在跳起的同一帧就触发空中攻击，加入短延时（秒）
    [Header("空中攻击时序")]
    [Tooltip("跳起后多长时间后允许触发空中攻击（秒），防止按键连按在同帧产生误触）")]
    public float airAttackAllowDelayAfterJump = 0.08f;
    private float airAttackAllowedTime = 0f;

    private bool isInAirAttack = false;       // 是否处于任一空中攻击（普通/下落）
    // 不再对 downForward 做特殊重力处理（和普通空中攻击一致）
    // 保留标志以区分动画/判定差异（若需要）
    private bool inDownForwardAttack = false;

    private bool isJumping;
    private bool isFalling;
    private float jumpStartY;

    [Header("状态参数")]
    private bool isFacingRight = true;
    private bool isDucking = false;
    private bool isGettingUp = false;
    private bool duckCancelable = false;
    private bool attackLocked = false;
    private bool inBackFlash = false;
    private bool inDuckAttack = false;

    [Header("地面检测")]
    public Transform groundPoint;
    public float groundCheckRadius = 0.1f;
    public LayerMask groundLayer;
    private bool isGrounded;
    private bool prevIsGrounded;

    // 输入与速度缓存
    private float rawInputX;
    private float currentSpeedX;
    private float targetSpeedX;

    // Animator 参数名
    private const string PARAM_MoveSpeed = "MoveSpeed";
    private const string PARAM_IsGrounded = "IsGrounded";
    private const string PARAM_IsDucking = "IsDucking";
    private const string PARAM_IsGettingUp = "IsGettingUp";
    private const string PARAM_IsFalling = "IsFalling";

    // Triggers
    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_Attack = "Trig_Attack";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";
    private const string TRIG_Turn = "Trig_Turn";

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();
        if (logMissingAnimatorParams)
            CheckAnimatorParams();
    }

    private void Update()
    {
        ReadInputs();
        CheckGrounded();
        HandleJumpLogic();
        HandleDuck();
        HandleAttackInput();        // 包括空中攻击输入
        HandleTurnInput();          // 空中瞬转 & 地面转身动画
        UpdateAnimatorParams();
        HandleAirAttackLandingInterrupt();
        prevIsGrounded = isGrounded;
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
        // 不再对 down-forward 做特殊连续重力（按你的要求空中攻击与跳跃套用相同物理）
    }

    #region 输入
    private void ReadInputs()
    {
        rawInputX = Input.GetAxisRaw("Horizontal");
    }
    #endregion

    #region 下蹲
    private void HandleDuck()
    {
        bool wantDuck = isGrounded && Input.GetKey(KeyCode.S);

        if (wantDuck && !isDucking)
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        isDucking = wantDuck;
        if (isGettingUp)
            isDucking = false;
    }
    #endregion

    #region 水平移动
    private void HandleHorizontalMovement()
    {
        // 下蹲锁移动
        if (isDucking && isGrounded && !inDuckAttack && !attackLocked)
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        // 空中攻击中可漂移
        if (isInAirAttack && !isGrounded && allowAirMoveDuringAirAttack)
        {
            ProcessNormalHorizontal();
            return;
        }

        if (IsMovementLocked())
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        ProcessNormalHorizontal();
    }

    private void ProcessNormalHorizontal()
    {
        targetSpeedX = rawInputX * moveSpeed;

        if (Mathf.Abs(rawInputX) > 0.01f)
        {
            float accel = isGrounded ? groundAcceleration : airAcceleration;
            currentSpeedX = Mathf.MoveTowards(currentSpeedX, targetSpeedX, accel * Time.fixedDeltaTime);
        }
        else
        {
            float decel = isGrounded ? groundDeceleration : airDeceleration;
            currentSpeedX = Mathf.MoveTowards(currentSpeedX, 0f, decel * Time.fixedDeltaTime);
        }

        if (Mathf.Abs(currentSpeedX) < runStopThreshold)
            currentSpeedX = 0f;

        rb.velocity = new Vector2(currentSpeedX, rb.velocity.y);
    }

    private bool IsMovementLocked()
    {
        // 不把空中攻击硬锁水平，除非地面 attackLocked 或其它锁
        if (attackLocked && (isGrounded || !isInAirAttack)) return true;
        if (inDuckAttack || inBackFlash || isGettingUp) return true;
        if (isTurning && turnLocksMovement) return true;
        return false;
    }
    #endregion

    #region 跳跃
    private void HandleJumpLogic()
    {
        if (Input.GetKeyDown(KeyCode.K) && isGrounded && !isJumping && !attackLocked && !inBackFlash && !isDucking)
        {
            isJumping = true;
            isFalling = false;
            jumpStartY = rb.position.y;
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);

            // 禁止在刚起跳的很短时间里触发空中攻击（避免 K+D+S+J 连按瞬触发）
            airAttackAllowedTime = Time.time + airAttackAllowDelayAfterJump;

            string trig = Mathf.Abs(rawInputX) > 0.05f ? TRIG_JumpForward : TRIG_JumpUp;
            anim.ResetTrigger(TRIG_JumpUp);
            anim.ResetTrigger(TRIG_JumpForward);
            anim.SetTrigger(trig);

            if (debugJumpLog) Debug.Log("[Jump] fire trigger: " + trig);
        }

        if (isJumping && !isGrounded)
        {
            float deltaH = rb.position.y - jumpStartY;
            if (deltaH >= maxJumpHeight && rb.velocity.y > 0f)
                rb.velocity = new Vector2(rb.velocity.x, 0f);
        }

        if (enableVariableJump && Input.GetKeyUp(KeyCode.K) && isJumping && rb.velocity.y > 0f)
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * variableJumpCutMultiplier);

        if (!isGrounded && rb.velocity.y < 0f)
            isFalling = true;

        if (!prevIsGrounded && isGrounded)
        {
            isJumping = false;
            isFalling = false;
            if (debugJumpLog) Debug.Log("[Jump] Landed.");
        }
    }
    #endregion

    #region 空中攻击
    private void HandleAttackInput()
    {
        if (!Input.GetKeyDown(KeyCode.J)) return;
        if (attackLocked || inBackFlash) return;

        // 空中
        if (!isGrounded)
        {
            // 防止在刚起跳的那一小段时间被误触发空中攻击
            if (Time.time < airAttackAllowedTime)
                return;

            if (isInAirAttack) return;

            bool isDownForward = Input.GetKey(KeyCode.S);
            if (isDownForward)
            {
                anim.SetTrigger(TRIG_JumpDownFwdAttack);
                StartAirAttack(true);
            }
            else
            {
                anim.SetTrigger(TRIG_JumpAttack);
                StartAirAttack(false);
            }
            return;
        }

        // 地面下蹲攻击
        if (isDucking && isGrounded)
        {
            if (Mathf.Abs(rawInputX) > 0.01f)
                anim.SetTrigger(TRIG_DuckFwdAttack);
            else
                anim.SetTrigger(TRIG_DuckAttack);
        }
        else
        {
            anim.SetTrigger(TRIG_Attack);
        }
    }

    private void StartAirAttack(bool isDownForward)
    {
        isInAirAttack = true;
        attackLocked = true;
        inDownForwardAttack = isDownForward;

        if (debugAirAttackLog) Debug.Log("[AirAttack] Start " + (isDownForward ? "DownForward" : "Normal"));

        // 不再对下落攻击施加特殊重力或强制落地——与普通空中攻击行为一致
    }

    public void OnAirAttackEnd() // animation event can call this if you have it
    {
        FinishAirAttackIfEnded();
    }

    private void FinishAirAttackIfEnded()
    {
        if (!isInAirAttack) return;

        if (!isGrounded)
        {
            attackLocked = false;
            isInAirAttack = false;
            inDownForwardAttack = false;

            // 空中攻击结束若方向键与朝向相反，立即空中瞬转
            int dir = GetInputDir();
            if (dir != 0)
            {
                bool wantRight = dir > 0;
                if (wantRight != isFacingRight && allowAirImmediateTurn)
                    ImmediateFlip(wantRight);
            }

            if (debugAirAttackLog) Debug.Log("[AirAttack] End in air -> free");
        }
        else
        {
            attackLocked = false;
            isInAirAttack = false;
            inDownForwardAttack = false;
        }
    }

    private void HandleAirAttackLandingInterrupt()
    {
        if (isGrounded && isInAirAttack)
        {
            if (debugAirAttackLog) Debug.Log("[AirAttack] Force land interrupt");
            attackLocked = false;
            isInAirAttack = false;
            inDownForwardAttack = false;
        }
    }
    #endregion

    #region 地面检测
    private void CheckGrounded()
    {
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundCheckRadius, groundLayer);
    }
    #endregion

    #region 转身（空中瞬转 / 地面动画）
    private void HandleTurnInput()
    {
        int dir = GetInputDir();
        if (dir == 0) return;
        bool wantRight = dir > 0;

        if (!isGrounded)
        {
            // 空中：允许瞬时转（攻击中禁止）
            if (isInAirAttack) return;
            if (wantRight != isFacingRight && allowAirImmediateTurn)
            {
                ImmediateFlip(wantRight);
            }
            return;
        }

        // 地面：使用转身动画（但只在脚本判断允许时触发）
        if (wantRight == isFacingRight)
            return;

        // 只有在 CanTurn() 允许的情况下才触发 Turn 的 Trigger（你删掉 AnyState→Turn，必须通过状态过渡到 Turn）
        if (!CanTurn()) return;

        // 发起 ground turn（Animator 需要有从地面相关状态到 Turn 的过渡，条件为 Trig_Turn）
        desiredFacingRight = wantRight;
        anim.SetTrigger(TRIG_Turn);
        isTurning = true;
        hasFlippedInThisTurn = false;
        if (turnFlipMode == TurnFlipMode.Immediate)
        {
            ImmediateFlip(wantRight);
            hasFlippedInThisTurn = true;
        }
        if (debugTurnLog) Debug.Log("[Turn] Ground trigger -> " + (wantRight ? "Right" : "Left"));
    }

    private bool CanTurn()
    {
        // 禁止转向的情况：被锁、下蹲攻击中、起身中、闪避等；空中攻击中也禁止（但空中转向逻辑在上面单独处理）
        if (attackLocked || inDuckAttack || inBackFlash || isGettingUp) return false;
        return true;
    }

    private int GetInputDir()
    {
        if (rawInputX > 0.01f) return 1;
        if (rawInputX < -0.01f) return -1;
        return 0;
    }

    private void ImmediateFlip(bool faceRight)
    {
        isFacingRight = faceRight;
        flipRoot.localScale = isFacingRight ? Vector3.one : new Vector3(-1, 1, 1);
    }
    #endregion

    #region 动画参数同步
    private void UpdateAnimatorParams()
    {
        anim.SetFloat(PARAM_MoveSpeed, Mathf.Abs(currentSpeedX));
        anim.SetBool(PARAM_IsGrounded, isGrounded);
        anim.SetBool(PARAM_IsDucking, isDucking);
        anim.SetBool(PARAM_IsGettingUp, isGettingUp);
        anim.SetBool(PARAM_IsFalling, isFalling);
    }
    #endregion

    #region 其它动画 events
    public void OnDuckCancelable() { duckCancelable = true; }

    public void OnGetUpStart()
    {
        isGettingUp = true;
        anim.SetBool(PARAM_IsGettingUp, true);
        currentSpeedX = 0f;
        rb.velocity = new Vector2(0, rb.velocity.y);
    }
    public void OnGetUpEnd()
    {
        isGettingUp = false;
        anim.SetBool(PARAM_IsGettingUp, false);
    }

    public void OnJumpStart() { }
    public void OnJumpEnd() { }

    // 转身动画事件（地面）
    public void OnTurnStart() { }
    public void OnTurnFlip()
    {
        if (turnFlipMode == TurnFlipMode.ByEvent && !hasFlippedInThisTurn)
        {
            ImmediateFlip(desiredFacingRight);
            hasFlippedInThisTurn = true;
            if (debugTurnLog) Debug.Log("[Turn] Flip event");
        }
    }
    public void OnTurnEnd()
    {
        isTurning = false;
        hasFlippedInThisTurn = false;
        if (debugTurnLog) Debug.Log("[Turn] End");
    }

    // 地面攻击
    public void OnAttackStart()
    {
        attackLocked = true;
        currentSpeedX = 0f;
        rb.velocity = new Vector2(0, rb.velocity.y);
    }
    public void OnAttackEnd()
    {
        attackLocked = false;
    }

    // 下蹲攻击
    public void OnDuckAttackStart()
    {
        inDuckAttack = true; attackLocked = true;
        currentSpeedX = 0f; rb.velocity = new Vector2(0, rb.velocity.y);
    }
    public void OnDuckAttackEnd() { inDuckAttack = false; attackLocked = false; }
    public void OnDuckAttackEndStart() { inDuckAttack = true; }
    public void OnDuckAttackEndEnd() { inDuckAttack = false; attackLocked = false; }

    // 下蹲前进攻击
    public void OnDuckFwdAttackStart()
    {
        inDuckAttack = true; attackLocked = true;
        currentSpeedX = 0f; rb.velocity = new Vector2(0, rb.velocity.y);
    }
    public void OnDuckFwdAttackEnd() { inDuckAttack = false; attackLocked = false; }
    public void OnDuckFwdAttackEndStart() { inDuckAttack = true; }
    public void OnDuckFwdAttackEndEnd() { inDuckAttack = false; attackLocked = false; }
    #endregion

    public void OnAirAttackStart()
    {
        // 可选：如果你想用动画事件作为开始锁定点，可以在这里设置：
        // isInAirAttack = true; attackLocked = true;
        // 但当前系统在 StartAirAttack 已经做了锁定，所以这里默认留空（兼容）
    }

    #region 参数自检
    private void CheckAnimatorParams()
    {
        if (anim == null) return;
        string[] neededTriggers = {
            TRIG_JumpUp, TRIG_JumpForward,
            TRIG_Attack, TRIG_DuckAttack, TRIG_DuckFwdAttack,
            TRIG_JumpAttack, TRIG_JumpDownFwdAttack, TRIG_Turn
        };
        string[] neededBools = {
            PARAM_IsGrounded, PARAM_IsDucking, PARAM_IsGettingUp, PARAM_IsFalling
        };
        var missing = neededTriggers
            .Where(t => !HasParam(t, AnimatorControllerParameterType.Trigger))
            .Concat(neededBools.Where(b => !HasParam(b, AnimatorControllerParameterType.Bool)))
            .ToArray();
        if (missing.Length > 0)
            Debug.LogWarning("[Animator Param Missing] " + string.Join(", ", missing));
    }
    private bool HasParam(string name, AnimatorControllerParameterType type)
    {
        return anim.parameters.Any(p => p.type == type && p.name == name);
    }
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (groundPoint)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundPoint.position, groundCheckRadius);
        }
    }

    
#endif
}