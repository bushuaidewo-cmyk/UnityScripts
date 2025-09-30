using System.Linq;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Inspector
    [Header("组件")]
    [SerializeField] private Transform flipRoot;
    [SerializeField] private Transform groundPoint;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.1f;

    [Header("移动")]
    public float moveSpeed = 5f;
    public float groundAcceleration = 40f;
    public float airAcceleration = 25f;
    public float groundDeceleration = 60f;
    public float airDeceleration = 30f;
    public float stopThreshold = 0.05f;

    [Header("跳跃")]
    public float jumpForce = 10f;
    public float maxJumpHeight = 5f;
    public bool variableJump = true;
    public float jumpCutMultiplier = 0.5f;
    
    
    public bool allowAirControlDuringRise = true;
    

    [Header("下蹲 / 下蹲攻击")]
    public float crouchActivationDelay = 0.08f;
    public int crouchIgnoreAfterJumpFrames = 4;
    public bool allowInstantDuckAttack = true;          // 锁定关闭时可瞬发
    public bool allowInstantDuckForwardAttack = true;
    public bool zeroHorizontalOnDuckAttackStart = true;
    public int duckAttackHorizLockFrames = 4;

    [Header("下蹲锁定 (第 N 帧后才能攻击 / 起身 / 移动)")]
    public bool useCrouchLock = true;
    public int crouchActionUnlockFrame = 8;             // 进入 Crouch 后锁定帧数
    public bool allowInstantDuckDuringLock = false;     // 若为 true 即使锁定期也允许瞬发

    [Header("地面攻击")]
    public bool zeroHorizontalOnGroundAttackStart = false;
    public bool lockFacingDuringGroundAttack = true;
    public bool lockFacingDuringDuckAttack = true;

    [Header("空中攻击")]
    public float airAttackMinDuration = 0.12f;
    public float airAttackAllowDelayAfterJump = 0.0f;
    public bool allowAirAttackDuringRise = true;
    public float airAttackRiseMinDelay = 0f;
    public bool allowAirMoveDuringAirAttack = true;

    [Header("输入缓冲/脉冲")]
    public int horizBufferFrames = 2;
    public int tapImpulseFrames = 3;

    [Header("空中转向")]
    public bool allowAirImmediateTurn = true;
    #endregion

    #region Animator 常量
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

    private static readonly string[] AirAttackAnimStates = {
        "player_jump_attack",
        "player_jump_downForward_attack"
    };
    #endregion

    #region FSM
    private enum PCState
    {
        GroundIdle,
        GroundMove,
        CrouchCandidate,
        Crouch,
        GroundAttack,
        DuckAttack,
        JumpRise,
        Air,
        AirAttack,
        LandingLock
    }
    private PCState state;
    private float stateEnterTime;
    #endregion

    #region Runtime
    private Animator anim;
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool prevGrounded;
    private float rawInputX;
    private float bufferedInputX;
    private int bufferedFramesRemain;
    private float tapImpulseDir;
    private int tapImpulseRemain;
    private bool keyDownLeft;
    private bool keyDownRight;
    private bool keyDownJump;
    private bool keyDownAttack;
    private float lastNonZeroDir;
    private int framesSinceDirNonZero;
    private bool jumping;
    private float jumpStartY;
    private float jumpStartTime;
    private int framesSinceJump = 999;
    private bool airAttackActive;
    private bool airAttackLocked;
    private bool airAttackAnimPlaying;
    private float airAttackStartTime;
    private bool groundAttackActive;
    private bool attackFacingLocked;
    private bool duckAttackFacingLocked;
    private int duckAttackHorizLockRemain;
    private float downKeyHoldStart = -100f;
    private int framesIgnoreCrouch;

    private float currentSpeedX;
    private float targetSpeedX;
    private bool facingRight = true;

    // 下蹲锁定
    private int crouchLockFramesRemain = 0;
    #endregion

    #region Unity
    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        ChangeState(PCState.GroundIdle);
    }

    private void Update()
    {
        CaptureInput();
        CheckGrounded();
        UpdateFSM();

        UpdateAnimatorParams();
        AutoAirAttackEnd();
        prevGrounded = isGrounded;
    }
    #endregion

    #region FSM 主流程
    private void UpdateFSM()
    {
        TickState();
        EvaluateTransitions();
        framesSinceJump++;
        HandleAutoFlip();
        if (crouchLockFramesRemain > 0 && state == PCState.Crouch)
            crouchLockFramesRemain--;

        // 可变跳：松开 K 立刻截掉上升速度
        if (variableJump && keyDownJump == false && Input.GetKeyUp(KeyCode.K) && rb.velocity.y > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutMultiplier);
        }
    }


    private void ChangeState(PCState next)
    {
        if (state == next) return;
        state = next;
        stateEnterTime = Time.time;

        if (next == PCState.Crouch)
        {
            crouchLockFramesRemain = useCrouchLock ? crouchActionUnlockFrame : 0;
        }
        if (next == PCState.CrouchCandidate)
        {
            // 进入候选时也准备锁（实际在变成 Crouch 时重置）
            crouchLockFramesRemain = useCrouchLock ? crouchActionUnlockFrame : 0;
        }
        if (next == PCState.GroundIdle || next == PCState.GroundMove)
        {
            // 离开下蹲后重置
            crouchLockFramesRemain = 0;
        }

    }


    #endregion


    #region Tick
    private void TickState()
    {
        switch (state)
        {
            case PCState.GroundIdle: ApplyHorizontal(0); break;
            case PCState.GroundMove: TickGroundMove(); break;
            case PCState.CrouchCandidate: TickCrouchCandidate(); break;
            case PCState.Crouch: ApplyHorizontal(0); break;
            case PCState.GroundAttack: TickGroundAttack(); break;
            case PCState.DuckAttack: TickDuckAttack(); break;
            case PCState.JumpRise: TickJumpRise(); break;
            case PCState.Air: TickAir(); break;
            case PCState.AirAttack: TickAirAttack(); break;
            case PCState.LandingLock: AccelerateHorizontal(true, false); break;
        }
    }

    private void TickGroundMove()
    {
        float dir = GetEffectiveInputDir();
        targetSpeedX = dir * moveSpeed;
        AccelerateHorizontal(true, dir != 0);
    }

    private void TickCrouchCandidate()
    {
        if (Mathf.Abs(currentSpeedX) > 0.01f) AccelerateHorizontal(true, false);
        else ApplyHorizontal(0);
    }

    private void TickGroundAttack()
    {
        if (zeroHorizontalOnGroundAttackStart) ApplyHorizontal(0);
        else AccelerateHorizontal(true, false);
    }

    private void TickDuckAttack()
    {
        if (duckAttackHorizLockRemain > 0) { duckAttackHorizLockRemain--; ApplyHorizontal(0); }
        else ApplyHorizontal(0);
    }

    private void TickJumpRise()
    {
        if (allowAirControlDuringRise)
        {
            float dir = GetEffectiveInputDir();
            if (dir != 0)
            {
                targetSpeedX = dir * moveSpeed;
                AccelerateHorizontal(false, true);
            }
            else AccelerateHorizontal(false, false);
        }

        

        if (allowAirAttackDuringRise && keyDownAttack && !airAttackLocked && !airAttackActive)
        {
            if (Time.time - jumpStartTime >= Mathf.Max(airAttackAllowDelayAfterJump, airAttackRiseMinDelay))
                TryStartAirAttack();
        }
    }


    private void TickAir()
    {
        if (!allowAirMoveDuringAirAttack) return;
        float dir = GetEffectiveInputDir();
        if (dir != 0)
        {
            targetSpeedX = dir * moveSpeed;
            AccelerateHorizontal(false, true);
        }
        else AccelerateHorizontal(false, false);
    }

    private void TickAirAttack()
    {
        if (allowAirMoveDuringAirAttack)
        {
            float dir = GetEffectiveInputDir();
            if (dir != 0)
            {
                targetSpeedX = dir * moveSpeed * 0.8f;
                AccelerateHorizontal(false, true);
            }
            else AccelerateHorizontal(false, false);
        }
    }
    #endregion

    #region 转向
    private void HandleAutoFlip()
    {
        float dir = GetEffectiveInputDir();
        if (Mathf.Abs(dir) < 0.01f) return;
        if ((groundAttackActive && lockFacingDuringGroundAttack && state == PCState.GroundAttack) ||
            (state == PCState.DuckAttack && duckAttackFacingLocked) ||
            state == PCState.AirAttack ||
            (!isGrounded && !allowAirImmediateTurn))
            return;

        bool wantRight = dir > 0;
        if (wantRight == facingRight) return;
        facingRight = wantRight;
        if (flipRoot) flipRoot.localScale = facingRight ? Vector3.one : new Vector3(-1, 1, 1);
    }
    #endregion

    #region Transitions
    private void EvaluateTransitions()
    {
        if (!prevGrounded && isGrounded &&
            (state == PCState.Air || state == PCState.AirAttack || state == PCState.JumpRise))
        {
            ChangeState(PCState.LandingLock);
            framesIgnoreCrouch = crouchIgnoreAfterJumpFrames;
            jumping = false;
            airAttackActive = false;
            airAttackLocked = false;
            airAttackAnimPlaying = false;
            return;
        }
        if (prevGrounded && !isGrounded && state is not (PCState.JumpRise or PCState.Air or PCState.AirAttack))
        {
            ChangeState(PCState.Air);
            return;
        }

        switch (state)
        {
            case PCState.GroundIdle:
            case PCState.GroundMove: GroundCommonTransitions(); break;
            case PCState.CrouchCandidate: CrouchCandidateTransitions(); break;
            case PCState.Crouch: CrouchTransitions(); break;
            case PCState.GroundAttack:
                if (!groundAttackActive) EndGroundAttackReturn(); break;
            case PCState.DuckAttack:
                if (!groundAttackActive) ChangeState(PCState.Crouch); break;
            case PCState.JumpRise:
                if (rb.velocity.y <= 0f && !airAttackActive) ChangeState(PCState.Air); break;
            case PCState.Air:
                AirTransitions(); break;
            case PCState.AirAttack:
                if (!airAttackActive) ChangeState(PCState.Air); break;
            case PCState.LandingLock:
                if (Time.time - stateEnterTime > 0.05f) ChangeState(PostLandingState()); break;
        }
    }

    private void GroundCommonTransitions()
    {
        if (keyDownJump && !groundAttackActive) { StartJump(); return; }

        bool instantWant = allowInstantDuckAttack &&
                           keyDownAttack &&
                           Input.GetKey(KeyCode.S) &&
                           !groundAttackActive;

        if (instantWant && (!useCrouchLock || allowInstantDuckDuringLock))
        {
            StartDuckAttackInstant(); return;
        }

        if (keyDownAttack && !groundAttackActive && !Input.GetKey(KeyCode.S))
        {
            StartGroundAttack(); return;
        }

        if (Input.GetKey(KeyCode.S) && framesIgnoreCrouch <= 0)
        {
            ChangeState(PCState.CrouchCandidate);
            downKeyHoldStart = Time.time;
            return;
        }

        float dir = GetEffectiveInputDir();
        ChangeState(Mathf.Abs(dir) > 0.01f ? PCState.GroundMove : PCState.GroundIdle);
    }

    private void CrouchCandidateTransitions()
    {
        if (!Input.GetKey(KeyCode.S)) { ChangeState(PCState.GroundIdle); return; }

        bool instantWant = allowInstantDuckAttack && keyDownAttack && !groundAttackActive;
        if (instantWant && (!useCrouchLock || allowInstantDuckDuringLock))
        {
            StartDuckAttackInstant(); return;
        }

        if (keyDownJump && !groundAttackActive) { StartJump(); return; }

        if (Time.time - downKeyHoldStart >= crouchActivationDelay)
        {
            ChangeState(PCState.Crouch);
        }
    }

    private void CrouchTransitions()
    {
        // 锁定期：禁止攻击/起身/移动
        if (useCrouchLock && crouchLockFramesRemain > 0)
            return;

        // 松开 S：离开下蹲
        if (!Input.GetKey(KeyCode.S))
        {
            // 松开瞬间根据是否有方向决定去 Idle 还是 Move
            if (Mathf.Abs(GetEffectiveInputDir()) > 0.01f)
                ChangeState(PCState.GroundMove);
            else
                ChangeState(PCState.GroundIdle);
            return;
        }

        // 跳跃（如果你也想在锁定期禁止跳跃，把上面锁定期 return 放在这里前面即可）
        if (keyDownJump && !groundAttackActive)
        {
            StartJump();
            return;
        }

        // 下蹲攻击（含前进下蹲攻击判定）
        if (keyDownAttack && !groundAttackActive)
        {
            StartDuckAttack();
            return;
        }

        // 重要：不再因为水平输入离开下蹲
        // 朝向翻转依旧由 HandleAutoFlip() 在 UpdateFSM 里处理
    }

    private void AirTransitions()
    {
        if (keyDownAttack && !airAttackLocked && !airAttackActive &&
            Time.time - jumpStartTime >= airAttackAllowDelayAfterJump)
            TryStartAirAttack();
    }

    private void EndGroundAttackReturn()
    {
        ChangeState(PostAttackGroundState());
        attackFacingLocked = false;
    }

    private PCState PostAttackGroundState()
    {
        float dir = GetEffectiveInputDir();
        return Mathf.Abs(dir) > 0.01f ? PCState.GroundMove : PCState.GroundIdle;
    }

    private PCState PostLandingState()
    {
        float dir = GetEffectiveInputDir();
        return Mathf.Abs(dir) > 0.01f ? PCState.GroundMove : PCState.GroundIdle;
    }
    #endregion

    #region Jump & Attack
    private void StartJump()
    {
        jumping = true;
        framesSinceJump = 0;
        jumpStartTime = Time.time;
        jumpStartY = rb.position.y;

        float dir = DetermineJumpDir();
        currentSpeedX = dir * moveSpeed;    // 直接采用常规速度（想完全不注入就去掉这一行）
        ApplyHorizontal(currentSpeedX);
        rb.velocity = new Vector2(rb.velocity.x, jumpForce);

        string trig = Mathf.Abs(dir) > 0.05f ? TRIG_JumpForward : TRIG_JumpUp;
        anim.ResetTrigger(TRIG_JumpForward);
        anim.ResetTrigger(TRIG_JumpUp);
        anim.SetTrigger(trig);

        framesIgnoreCrouch = crouchIgnoreAfterJumpFrames;
        airAttackLocked = false;
        airAttackActive = false;
        airAttackAnimPlaying = false;

        ChangeState(PCState.JumpRise);
    }

    private float DetermineJumpDir()
    {
        if (keyDownRight) return 1f;
        if (keyDownLeft) return -1f;
        float eff = GetEffectiveInputDirRawOrTap();
        
        if (Mathf.Abs(eff) > 0.01f) return Mathf.Sign(eff);
        if (framesSinceDirNonZero <= 3) return lastNonZeroDir;
        return 0f;
    }

    private void StartGroundAttack()
    {
        groundAttackActive = true;
        attackFacingLocked = lockFacingDuringGroundAttack;
        if (zeroHorizontalOnGroundAttackStart) ApplyHorizontal(0);
        anim.SetTrigger(TRIG_Attack);
        ChangeState(PCState.GroundAttack);
    }

    private void StartDuckAttack()
    {
        groundAttackActive = true;
        duckAttackFacingLocked = lockFacingDuringDuckAttack;
        if (zeroHorizontalOnDuckAttackStart) ApplyHorizontal(0);
        duckAttackHorizLockRemain = duckAttackHorizLockFrames;
        anim.SetBool(PARAM_IsDucking, true);
        bool fwd = allowInstantDuckForwardAttack && Mathf.Abs(GetEffectiveInputDirRawOrTap()) > 0.01f;
        anim.SetTrigger(fwd ? TRIG_DuckFwdAttack : TRIG_DuckAttack);
        ChangeState(PCState.DuckAttack);
    }

    private void StartDuckAttackInstant()
    {
        groundAttackActive = true;
        duckAttackFacingLocked = lockFacingDuringDuckAttack;
        if (zeroHorizontalOnDuckAttackStart) ApplyHorizontal(0);
        duckAttackHorizLockRemain = duckAttackHorizLockFrames;
        anim.SetBool(PARAM_IsDucking, true);
        bool fwd = allowInstantDuckForwardAttack && Mathf.Abs(GetEffectiveInputDirRawOrTap()) > 0.01f;
        anim.SetTrigger(fwd ? TRIG_DuckFwdAttack : TRIG_DuckAttack);
        ChangeState(PCState.DuckAttack);
    }

    private void TryStartAirAttack()
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < AirAttackAnimStates.Length; i++)
            if (st.IsName(AirAttackAnimStates[i])) return;
        if (airAttackAnimPlaying || airAttackActive) return;

        airAttackActive = true;
        airAttackLocked = true;
        airAttackAnimPlaying = true;
        airAttackStartTime = Time.time;

        bool downFwd = Input.GetKey(KeyCode.S);
        anim.SetTrigger(downFwd ? TRIG_JumpDownFwdAttack : TRIG_JumpAttack);
        ChangeState(PCState.AirAttack);
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

        if (Mathf.Abs(rawInputX) > 0.01f)
        {
            bufferedInputX = rawInputX;
            bufferedFramesRemain = horizBufferFrames;
            tapImpulseDir = 0; tapImpulseRemain = 0;
            lastNonZeroDir = Mathf.Sign(rawInputX);
            framesSinceDirNonZero = 0;
        }
        else
        {
            if (bufferedFramesRemain > 0) bufferedFramesRemain--;
            else bufferedInputX = 0f;
            framesSinceDirNonZero++;
        }

        if (keyDownLeft) { tapImpulseDir = -1; tapImpulseRemain = tapImpulseFrames; }
        else if (keyDownRight) { tapImpulseDir = 1; tapImpulseRemain = tapImpulseFrames; }

        if (tapImpulseRemain > 0 && Mathf.Abs(rawInputX) > 0.01f)
        {
            tapImpulseRemain = 0; tapImpulseDir = 0;
        }

        if (framesIgnoreCrouch > 0) framesIgnoreCrouch--;
    }

    private float GetEffectiveInputDir()
    {
        if (Mathf.Abs(rawInputX) > 0.01f) return rawInputX;
        if (tapImpulseRemain > 0) return tapImpulseDir;
        if (Mathf.Abs(bufferedInputX) > 0.01f) return bufferedInputX;
        return 0f;
    }
    private float GetEffectiveInputDirRawOrTap()
    {
        if (Mathf.Abs(rawInputX) > 0.01f) return rawInputX;
        if (tapImpulseRemain > 0) return tapImpulseDir;
        return 0f;
    }
    #endregion

    #region Movement
    private void ApplyHorizontal(float x)
    {
        var v = rb.velocity;
        v.x = x;
        rb.velocity = v;
        currentSpeedX = x;
    }
    private void AccelerateHorizontal(bool grounded, bool hasInput)
    {
        float eff = GetEffectiveInputDir();
        float target = hasInput ? eff * moveSpeed : 0f;
        float accel = grounded
            ? (hasInput ? groundAcceleration : groundDeceleration)
            : (hasInput ? airAcceleration : airDeceleration);
        currentSpeedX = Mathf.MoveTowards(currentSpeedX, target, accel * Time.deltaTime);
        if (grounded && !hasInput && Mathf.Abs(currentSpeedX) < stopThreshold) currentSpeedX = 0f;
        ApplyHorizontal(currentSpeedX);
    }
    #endregion

    #region Ground / Air
    private void CheckGrounded()
    {
        if (!groundPoint) { isGrounded = false; return; }
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundCheckRadius, groundLayer);
        if (isGrounded) jumping = false;
    }
    #endregion

    #region 空攻结束
    private void AutoAirAttackEnd()
    {
        if (!airAttackActive) return;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        bool inAtk = false;
        for (int i = 0; i < AirAttackAnimStates.Length; i++)
        {
            if (st.IsName(AirAttackAnimStates[i]))
            {
                inAtk = true;
                if (st.normalizedTime >= 0.98f &&
                    Time.time - airAttackStartTime >= airAttackMinDuration)
                {
                    airAttackActive = false;
                    airAttackLocked = false;
                    airAttackAnimPlaying = false;
                    ChangeState(PCState.Air);
                }
                break;
            }
        }
        if (!inAtk && airAttackAnimPlaying &&
            Time.time - airAttackStartTime >= airAttackMinDuration)
        {
            airAttackActive = false;
            airAttackLocked = false;
            airAttackAnimPlaying = false;
            ChangeState(PCState.Air);
        }
    }
    #endregion

    #region 动画事件 (保留)
    public void OnAttackStart() { groundAttackActive = true; }
    public void OnAttackEnd() { groundAttackActive = false; attackFacingLocked = false; }
    public void OnDuckAttackStart() { groundAttackActive = true; }
    public void OnDuckAttackEnd() { groundAttackActive = false; duckAttackFacingLocked = false; }
    public void OnDuckFwdAttackStart() { groundAttackActive = true; }
    public void OnDuckFwdAttackEnd() { groundAttackActive = false; duckAttackFacingLocked = false; }
    public void OnAirAttackEnd()
    {
        if (!airAttackActive) return;
        if (Time.time - airAttackStartTime < airAttackMinDuration) return;
        airAttackActive = false;
        airAttackLocked = false;
        airAttackAnimPlaying = false;
        ChangeState(PCState.Air);
    }
    #endregion

    #region Animator 参数
    private void UpdateAnimatorParams()
    {
        anim.SetFloat(PARAM_MoveSpeed, Mathf.Abs(currentSpeedX));
        anim.SetBool(PARAM_IsGrounded, isGrounded);
        anim.SetBool(PARAM_IsDucking,
            state == PCState.Crouch ||
            state == PCState.CrouchCandidate ||
            state == PCState.DuckAttack);
        anim.SetBool(PARAM_IsGettingUp, false);
        anim.SetBool(PARAM_IsFalling, !isGrounded && rb.velocity.y < 0);
    }
    #endregion
}