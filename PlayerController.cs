using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("组件")]
    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;

    [Header("子节点引用")]
    public Transform groundPoint;   // 拖 Ground Point
    public Transform flipRoot;      // 拖 Flip 节点

    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float jumpForce = 12f;
    public float groundCheckRadius = 0.1f;
    public LayerMask groundLayer;

    [Header("状态参数")]
    private bool isGrounded;
    private bool allowSecondJump = true;
    private bool isFacingRight = true;
    private bool attackLocked = false;
    private bool inBackFlash = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();              // 挂在 Component 上
        anim = GetComponentInChildren<Animator>();     // 从子物体 Flip/player 找
        sr = GetComponentInChildren<SpriteRenderer>(); // 找 SpriteRenderer
    }

    private void Update()
    {
        CheckGrounded();
        HandleInput();
        UpdateAnimatorParams();
    }

    private void FixedUpdate()
    {
        HandleMovement();
    }

    // ================== 输入处理 ==================
    private void HandleInput()
    {
        float moveInput = Input.GetAxisRaw("Horizontal");

        TryTurn(moveInput);

        // 攻击 J
        if (Input.GetKeyDown(KeyCode.J))
        {
            if (CanStandAttack())
            {
                if (!isGrounded) // 空中攻击
                {
                    if (Input.GetKey(KeyCode.S))
                        anim.SetTrigger("Trig_JumpDownFwdAttack"); // 下劈攻击
                    else
                        anim.SetTrigger("Trig_JumpAttack"); // 空中普通攻击
                }
                else if (Input.GetKey(KeyCode.S)) // 地面蹲下攻击
                {
                    if (Mathf.Abs(moveInput) > 0.01f)
                        anim.SetTrigger("Trig_DuckFwdAttack");
                    else
                        anim.SetTrigger("Trig_DuckAttack");
                }
                else // 普通站立攻击
                {
                    anim.SetTrigger("Trig_Attack");
                }
            }
        }

        // 跳跃 K
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (isGrounded)
            {
                if (Mathf.Abs(moveInput) > 0.01f)
                    anim.SetTrigger("Trig_JumpForward");
                else
                    anim.SetTrigger("Trig_JumpUp");

                rb.velocity = new Vector2(rb.velocity.x, jumpForce);
                allowSecondJump = true;
            }
            else if (allowSecondJump)
            {
                anim.SetTrigger("Trig_JumpDouble");
                rb.velocity = new Vector2(rb.velocity.x, jumpForce);
                allowSecondJump = false;
            }
        }

        // 闪避 I
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (CanBackFlash())
                anim.SetTrigger("Trig_BackFlash");
        }

        // 魔法 W
        if (Input.GetKeyDown(KeyCode.W))
        {
            if (CanMagic())
                anim.SetTrigger("Trig_MagicUp");
        }
        if (Input.GetKeyUp(KeyCode.W))
        {
            anim.SetTrigger("Trig_MagicDown");
        }

        // 盾 L
        if (Input.GetKeyDown(KeyCode.L))
        {
            anim.SetTrigger("Trig_ShieldUp");
        }
        if (Input.GetKeyUp(KeyCode.L))
        {
            anim.SetTrigger("Trig_ShieldDown");
        }

        // 下蹲 S（持续）
        anim.SetBool("IsDucking", Input.GetKey(KeyCode.S) && isGrounded);
    }

    // ================== 移动 ==================
    private void HandleMovement()
    {
        float moveInput = Input.GetAxisRaw("Horizontal");
        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);
    }

    // ================== 动画参数同步 ==================
    private void UpdateAnimatorParams()
    {
        anim.SetFloat("MoveSpeed", Mathf.Abs(rb.velocity.x));
        anim.SetFloat("VertSpeed", rb.velocity.y);
        anim.SetBool("IsGrounded", isGrounded);
        anim.SetBool("IsFalling", rb.velocity.y < 0);
        anim.SetBool("IsFacingRight", isFacingRight);
    }

    // ================== 转向 ==================
    private void TryTurn(float moveInput)
    {
        if (attackLocked || inBackFlash) return;

        if (moveInput > 0 && !isFacingRight)
        {
            isFacingRight = true;
            flipRoot.localScale = new Vector3(1, 1, 1);   // 向右
            anim.SetTrigger("Trig_Turn");
        }
        else if (moveInput < 0 && isFacingRight)
        {
            isFacingRight = false;
            flipRoot.localScale = new Vector3(-1, 1, 1);  // 向左
            anim.SetTrigger("Trig_Turn");
        }
    }

    // ================== 判定函数 ==================
    private bool CanStandAttack()
    {
        if (attackLocked) return false;
        if (inBackFlash) return false;
        return true;
    }

    private bool CanBackFlash()
    {
        if (attackLocked) return false;
        return true;
    }

    private bool CanMagic()
    {
        if (attackLocked) return false;
        if (inBackFlash) return false;
        return true;
    }

    // ================== Ground 判定 ==================
    private void CheckGrounded()
    {
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundCheckRadius, groundLayer);
        if (isGrounded) allowSecondJump = true;
    }

    // ================== 动画事件接口 ==================
    public void OnLockStart() { attackLocked = true; }
    public void OnLockEnd() { attackLocked = false; }
    public void OnBackFlashStart() { inBackFlash = true; }
    public void OnBackFlashEnd() { inBackFlash = false; }

    public void OnAttackWindowOpen() { /* 开启攻击判定HitBox */ }
    public void OnAttackWindowClose() { /* 关闭攻击判定HitBox */ }
    public void OnLand() { allowSecondJump = true; }
    public void OnShieldActive() { /* isShielding = true; */ }
    public void OnShieldInactive() { /* isShielding = false; */ }
    public void OnMagicReady() { /* magicIdle可用 */ }
    public void OnMagicAttackCast() { /* 生成魔法弹or技能 */ }
    public void OnMagicEnd() { /* 清空魔法状态 */ }
    public void OnInvincibleStart() { /* 设置无敌=true */ }
    public void OnInvincibleEnd() { /* 设置无敌=false */ }
    public void OnJumpApex() { /* 到达跳跃顶点逻辑 */ }
}
