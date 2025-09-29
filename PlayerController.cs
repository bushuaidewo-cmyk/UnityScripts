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
    private bool isTurning = false;
    private bool desiredFacingRight = true;
    private bool hasFlippedInThisTurn = false;

    [Header("空中攻击参数")]
    public float downForwardAttackFastFallSpeed = -15f; // 下落前倾攻击立即赋予的下落速度（可调）
    public bool allowDownForwardOverrideUpwardVelocity = true;

    private bool isInAirAttack = false;  // 是否正在空中攻击

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

    private const string TRIG_JumpUp = "Trig_JumpUp";
    private const string TRIG_JumpForward = "Trig_JumpForward";
    private const string TRIG_DuckAttack = "Trig_DuckAttack";
    private const string TRIG_DuckFwdAttack = "Trig_DuckFwdAttack";
    private const string TRIG_Attack = "Trig_Attack";
    private const string TRIG_JumpAttack = "Trig_JumpAttack";
    private const string TRIG_JumpDownFwdAttack = "Trig_JumpDownFwdAttack";
    private const string TRIG_Turn = "Trig_Turn";
    // 若你有落地 Trigger，可解开
    // private const string TRIG_JumpGround = "Trig_JumpGround";

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
        HandleAttackInput();     // 包括空中攻击
        HandleTurnInput();       // 空中也能转身（除空中攻击中）
        UpdateAnimatorParams();
        HandleAirAttackLandingInterrupt();
        prevIsGrounded = isGrounded;
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
    }

    #region 输入
    private void ReadInputs()
    {
        rawInputX = Input.GetAxisRaw("Horizontal");
    }
    #endregion

    #region 下蹲（按住 S 锁移动）
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
        // 下蹲立即锁移动（除攻击或其它强制移动中）
        if (isDucking && isGrounded && !inDuckAttack && !attackLocked)
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

        if (IsMovementLocked())
        {
            currentSpeedX = 0f;
            rb.velocity = new Vector2(0f, rb.velocity.y);
            return;
        }

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
        // 空中攻击时是否要完全锁水平速度？如果想允许保持惯性但禁止输入，可以不在这里加 isInAirAttack
        if (attackLocked || inDuckAttack || inBackFlash || isGettingUp) return true;
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

        // 空中攻击
        if (!isGrounded)
        {
            if (isInAirAttack)
                return; // 正在空中攻击中，等待当前结束

            // 下落前倾攻击 (S + J)
            if (Input.GetKey(KeyCode.S))
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
        attackLocked = true; // 锁防止连按立即重入
        if (debugAirAttackLog) Debug.Log("[AirAttack] Start " + (isDownForward ? "DownForward" : "Normal"));

        // 处理下落前倾攻击的快速下落（可选）
        if (isDownForward && allowDownForwardOverrideUpwardVelocity)
        {
            // 立即赋一个向下速度（更果断），也可以 Mathf.Min 保留更快降速
            rb.velocity = new Vector2(rb.velocity.x, downForwardAttackFastFallSpeed);
        }
    }

    // 动画事件：空中攻击开始（如果你希望通过事件锁，也可以只在事件里锁，去掉上面的 StartAirAttack 里锁定）
    public void OnAirAttackStart()
    {
        // 如果改用事件驱动，把锁放到这里
        // attackLocked = true; isInAirAttack = true;
    }

    // 动画事件：空中攻击结束
    public void OnAirAttackEnd()
    {
        if (!isGrounded)
        {
            // 仍在空中 -> 允许再次攻击
            attackLocked = false;
            isInAirAttack = false;
            if (debugAirAttackLog) Debug.Log("[AirAttack] End in air -> can re-attack");
        }
        else
        {
            // 已经落地（落地逻辑会统一处理），保险清理
            attackLocked = false;
            isInAirAttack = false;
        }
    }

    private void HandleAirAttackLandingInterrupt()
    {
        if (isGrounded && isInAirAttack)
        {
            // 落地硬切：空中攻击终止
            ForceLandFromAirAttack();
        }
    }

    private void ForceLandFromAirAttack()
    {
        if (debugAirAttackLog) Debug.Log("[AirAttack] Force land interrupt");
        attackLocked = false;
        isInAirAttack = false;
        // 如果你使用落地 Trigger，可在此：
        // anim.SetTrigger(TRIG_JumpGround);
        // 否则确保空中攻击状态有 IsGrounded==true → jump_ground 的过渡
    }
    #endregion

    #region 地面检测
    private void CheckGrounded()
    {
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundCheckRadius, groundLayer);
    }
    #endregion

    #region 转身（空中可转身，空中攻击中禁止）
    private void HandleTurnInput()
    {
        int dir = 0;
        if (rawInputX > 0.01f) dir = 1;
        else if (rawInputX < -0.01f) dir = -1;
        if (dir == 0) return;

        bool wantRight = dir > 0;

        if (wantRight == isFacingRight && !(isTurning && !hasFlippedInThisTurn && wantRight != desiredFacingRight))
            return;

        RequestTurn(wantRight);
    }

    private bool CanTurn()
    {
        if (attackLocked || inDuckAttack || inBackFlash || isGettingUp) return false;
        if (isDucking && isGrounded) return false;
        if (isInAirAttack) return false; // 空中攻击中禁止转身
        // 空中允许转身 -> 不再限制 isGrounded
        return true;
    }

    private void RequestTurn(bool faceRight)
    {
        if (!CanTurn()) return;
        desiredFacingRight = faceRight;

        if (!isTurning)
        {
            anim.SetTrigger(TRIG_Turn);
            isTurning = true;
            hasFlippedInThisTurn = false;
            if (turnFlipMode == TurnFlipMode.Immediate)
            {
                ApplyFlipImmediate();
                hasFlippedInThisTurn = true;
            }
            if (debugTurnLog) Debug.Log($"[Turn] Start -> {(faceRight ? "Right" : "Left")}, mode={turnFlipMode}");
        }
        else
        {
            if (!hasFlippedInThisTurn)
            {
                if (debugTurnLog) Debug.Log($"[Turn] Update desired -> {(faceRight ? "Right" : "Left")}");
            }
            else
            {
                anim.SetTrigger(TRIG_Turn);
                hasFlippedInThisTurn = false;
                if (turnFlipMode == TurnFlipMode.Immediate)
                {
                    ApplyFlipImmediate();
                    hasFlippedInThisTurn = true;
                }
                if (debugTurnLog) Debug.Log($"[Turn] Retrigger -> {(faceRight ? "Right" : "Left")}");
            }
        }
    }

    private void ApplyFlipImmediate()
    {
        isFacingRight = desiredFacingRight;
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

    #region 其它动画事件（地面攻击/下蹲攻击等）
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

    // 转身事件
    public void OnTurnStart() { }
    public void OnTurnFlip()
    {
        if (turnFlipMode == TurnFlipMode.ByEvent && !hasFlippedInThisTurn)
        {
            ApplyFlipImmediate();
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

    // 地面通用攻击
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

    // 空中攻击事件（若你在动画里添加 OnAirAttackStart / OnAirAttackEnd，可以调用上面的空中流程）
    // 已经在上面实现 OnAirAttackStart / OnAirAttackEnd
    #endregion

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