using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("组件")]
    private Rigidbody2D rb;
    private Animator anim;

    [Header("翻转节点 (Flip)")]
    public Transform flipRoot;   // 挂 Flip 节点

    [Header("移动参数")]
    public float moveSpeed = 5f;

    [Header("状态参数")]
    private bool isFacingRight = true;
    private bool isDucking = false;  // 是否下蹲
    private bool isGettingUp = false; // 是否正在起身（新增）

    private bool duckCancelable = false;


    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();  // 找到子物体 player 上的 Animator
    }

    private void Update()
    {
        HandleFlip();
        HandleDuck();
        UpdateAnimatorParams();
    }

    private void FixedUpdate()
    {
        HandleMovement();
    }

    // ================== 移动 ==================
    private void HandleMovement()
    {
        float moveInput = Input.GetAxisRaw("Horizontal");

        // 🛑 如果正在起身，不能动
        if (isGettingUp)
        {
            rb.velocity = new Vector2(0, rb.velocity.y);
            return;
        }

        // 🛑 如果在蹲下系动画（Duck / DuckIdle），不能动
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("player_duck") || state.IsName("player_duck_idle") || state.IsName("player_getUp"))
        {
            rb.velocity = new Vector2(0, rb.velocity.y);
            return;
        }

        // ✅ 其它情况正常移动
        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);
    }




    // ================== 翻转 ==================
    private void HandleFlip()
    {
        float moveInput = Input.GetAxisRaw("Horizontal");

        if (moveInput > 0 && !isFacingRight)
        {
            isFacingRight = true;
            flipRoot.localScale = new Vector3(1, 1, 1);
        }
        else if (moveInput < 0 && isFacingRight)
        {
            isFacingRight = false;
            flipRoot.localScale = new Vector3(-1, 1, 1);
        }
    }

    // ================== 下蹲逻辑 ==================
    private void HandleDuck()
    {
        if (Input.GetKey(KeyCode.S))
        {
            isDucking = true;
        }
        else
        {
            isDucking = false;
        }

        anim.SetBool("IsDucking", isDucking);
    }

    // ================== 动画参数同步 ==================
    private void UpdateAnimatorParams()
    {
        anim.SetFloat("MoveSpeed", Mathf.Abs(rb.velocity.x));
        anim.SetBool("IsDucking", isDucking);
        // 🛑 如果松开 S 且已进入可取消阶段
        if (!Input.GetKey(KeyCode.S) && duckCancelable)
        {
            anim.SetBool("IsDucking", false);
        }

        anim.SetBool("IsGettingUp", isGettingUp);  // ✅ 同步到 Animator

        if (anim.GetCurrentAnimatorStateInfo(0).IsName("player_duck"))
        {
            if (!Input.GetKey(KeyCode.S) && duckCancelable)
                anim.SetBool("IsDucking", false);
        }

    }

    // ================== 动画事件接口 ==================


    public void OnDuckCancelable()
    {
        duckCancelable = true;
    }
    public void OnGetUpStart()
    {
        isGettingUp = true;
        anim.SetBool("IsGettingUp", true);
        duckCancelable = false;   // 起身时重置
    }

    public void OnGetUpEnd()
    {
        isGettingUp = false;
        anim.SetBool("IsGettingUp", false);
    }


}
