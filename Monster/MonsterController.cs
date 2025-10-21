using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物控制器：出生 → 巡逻 → 发现 → 攻击 → 死亡
/// 支持墙/悬崖检测 + 空中状态屏蔽 + 特效事件 + 可视化调试
/// </summary>
public class MonsterController : MonoBehaviour
{
    [Header("怪物配置 ScriptableObject")]
    public MonsterConfig config;

    [HideInInspector] public MonsterSpawner spawner;
    private Animator animator;
    private Transform player;
    private Collider2D col;
    private float currentHP;
    private bool isDead;
    private int patrolIndex = 0;
    private bool isResting = false;
    private Transform flip;

    private List<PatrolMovement> patrolRuntimeMoves;

    // === 地形检测参数 ===
    [Header("地形检测参数")]
    public LayerMask groundLayer;
    public float wallCheckDistance = 0.4f;
    public float wallCheckHeightOffset = 0.2f;
    public float cliffCheckDistance = 0.6f;
    public float cliffCheckOffsetX = 0.3f;

    // === 跳跃运行时状态 ===
    private bool isJumping = false;
    private float jumpTimeLeft = 0f;
    private float vY = 0f;
    private const float BASE_G = 25f;
    private bool hasPlayedJumpEffect = false;
    private bool hasPlayedJumpRestEffect = false;
    private float turnCooldown = 0f;
    private const float SKIN = 0.02f;
    // 新增：本帧通过安全位移判定到的“是否着地”
    private bool groundedAfterVerticalMove = false;
    private bool hasAlignedToGround = false;
    private int spawnStableFrames = 6; // 约等于 0.1 秒（按60fps计）

    [Header("AutoJump 许可区")]
    public string autoJumpPermitTag = "AutoJumpPermit";

    private bool inAutoJumpPermitZone = false;

    // AutoJump 触发的“上膛/保险”机制：
    // - 进入许可区即上膛（armed = true）
    // - 一次自动跳后上保险（disarmUntilExit = true），必须先离开许可区才能再次上膛
    private bool autoJumpArmed = false;
    private bool autoJumpDisarmUntilExit = false;

    // 来自直线分支的临时跳跃：落地休息后不切到下一段
    private bool autoJumpFromStraight = false;
    private bool keepSameMoveAfterRest = false;

    private enum MonsterState { Idle, Patrol, Discovery, Attack, Dead }
    private MonsterState state = MonsterState.Idle;

    //FX 播放策略
    private bool fxOnlyFromAnimationEvents = true;  // 默认：只由动画事件触发
                                                    // 四通道各自独立的“当前段”引用
    private PatrolMovement activeStraightMove = null;
    private PatrolMovement activeJumpMove = null;
    public enum ObstacleBehavior { Turn, AutoJump }  // Turn=转向；AutoJump=自动跳过去

    [Header("AutoJump 落地后抑制")]
    [Tooltip("自动跳落地→休息结束后，忽略多少帧“悬崖检测”，避免刚落地又把自己当成悬崖前沿而转向。60FPS下8≈0.13秒。")]
    public int autoJumpIgnoreCliffFrames = 8;
    private int ignoreCliffFramesLeft = 0;



    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        animator = GetComponentInChildren<Animator>();
        col = GetComponent<Collider2D>();
        if (col == null)
            Debug.LogWarning($"[MonsterController] {name} 未找到 Collider2D，将无法进行地面检测。");

        // 设置初始朝向（只用旋转）
        if (config.spawnConfig.spawnOrientation == Orientation.FaceLeft)
            transform.rotation = Quaternion.Euler(0, 180f, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FaceRight)
            transform.rotation = Quaternion.Euler(0, 0, 0);
        else if (config.spawnConfig.spawnOrientation == Orientation.FacePlayer && player)
            transform.rotation = (player.position.x > transform.position.x)
                ? Quaternion.Euler(0, 0, 0)
                : Quaternion.Euler(0, 180f, 0);

        flip = transform.Find("Flip");
        if (animator == null)
        {
            Debug.LogError($"[MonsterController] {name} 未找到 Animator！");
            enabled = false;
            return;
        }

        // 自动添加事件中继器
        if (animator.GetComponent<MonsterAnimationEventRelay>() == null)
            animator.gameObject.AddComponent<MonsterAnimationEventRelay>();

        // 克隆巡逻配置（避免修改 ScriptableObject 原始值）
        if (config && config.patrolConfig != null && config.patrolConfig.movements != null)
        {
            patrolRuntimeMoves = new List<PatrolMovement>();
            foreach (var m in config.patrolConfig.movements)
            {
                var clone = new PatrolMovement
                {
                    type = m.type,
                    moveSpeed = m.moveSpeed,
                    acceleration = m.acceleration,
                    deceleration = m.deceleration,
                    moveDuration = m.moveDuration,
                    restDuration = m.restDuration,
                    // ✅ 加上这行：把遇障碍行为也拷贝到运行时
                    obstacleBehavior = m.obstacleBehavior,

                    moveAnimation = m.moveAnimation,
                    restAnimation = m.restAnimation,
                    moveEffectOnlyFirst = m.moveEffectOnlyFirst,
                    moveEffectPrefab = m.moveEffectPrefab,
                    restEffectOnlyFirst = m.restEffectOnlyFirst,
                    restEffectPrefab = m.restEffectPrefab,

                    jumpSpeed = m.jumpSpeed,
                    jumpHeight = m.jumpHeight,
                    gravityScale = m.gravityScale,
                    jumpDuration = m.jumpDuration,
                    jumpRestDuration = m.jumpRestDuration,
                    jumpAnimation = m.jumpAnimation,
                    jumpRestAnimation = m.jumpRestAnimation,
                    jumpEffectOnlyFirst = m.jumpEffectOnlyFirst,
                    jumpEffectPrefab = m.jumpEffectPrefab,
                    jumpRestEffectOnlyFirst = m.jumpRestEffectOnlyFirst,
                    jumpRestEffectPrefab = m.jumpRestEffectPrefab,

                    executeCount = m.executeCount
                };
                patrolRuntimeMoves.Add(clone);
            }
        }

        currentHP = config.maxHP;
        turnCooldown = 1f;
        StartCoroutine(StateMachine());
    }

    void Update()
    {
        if (turnCooldown > 0f)
            turnCooldown -= Time.deltaTime;
    }

    IEnumerator StateMachine()
    {

        int spawnLoops = Mathf.Max(1, config.spawnConfig.spawnLoopCount);

        for (int i = 0; i < spawnLoops; i++)
        {
            if (!string.IsNullOrEmpty(config.spawnConfig.spawnAnimation))
            {
                animator.Play(config.spawnConfig.spawnAnimation);
                Debug.Log($"[Spawn] 播放出生动画第 {i + 1}/{spawnLoops}");
            }

            yield return new WaitForSeconds(Mathf.Max(0.1f, config.spawnConfig.idleDelay));

            if (!string.IsNullOrEmpty(config.spawnConfig.idleAnimation))
                animator.Play(config.spawnConfig.idleAnimation);

            if (i < spawnLoops - 1)
                yield return new WaitForSeconds(1f);
        }

        state = MonsterState.Patrol;
        Debug.Log($"[Spawn] 出生阶段完成，进入巡逻阶段");



        while (!isDead)
        {
            switch (state)
            {
                case MonsterState.Patrol:
                    PatrolUpdate();
                    break;
                case MonsterState.Discovery:
                    DiscoveryUpdate();
                    break;
                case MonsterState.Attack:
                    AttackUpdate();
                    break;
                default:
                    IdleUpdate();
                    break;
            }
            yield return null;
        }
    }

    void IdleUpdate()
    {
        if (!string.IsNullOrEmpty(config.spawnConfig.idleAnimation))
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(config.spawnConfig.idleAnimation))
                animator.Play(config.spawnConfig.idleAnimation);
        }
    }

    // === 核心：巡逻逻辑 ===
    void PatrolUpdate()
    {
        if (ignoreCliffFramesLeft > 0) ignoreCliffFramesLeft--;

        // 出生稳定帧：直接播放动画+位移，不做任何墙/悬崖/转向判定
        // 出生稳定帧：直接播放动画+位移，不做任何墙/悬崖/转向判定
        if (spawnStableFrames > 0)
        {
            spawnStableFrames--;

            var curMove = patrolRuntimeMoves?[patrolIndex]; // ← 改名
            if (curMove != null)
            {
                if (curMove.type == MovementType.Straight)
                {
                    if (!string.IsNullOrEmpty(curMove.moveAnimation) &&
                        !animator.GetCurrentAnimatorStateInfo(0).IsName(curMove.moveAnimation))
                        animator.Play(curMove.moveAnimation);

                    transform.Translate(Vector3.right * curMove.moveSpeed * Time.deltaTime, Space.Self);
                }
                else
                {
                    // Jump 稳定帧：只做重力与半速前进，避免贴地/起跳抖
                    float g = curMove.gravityScale * BASE_G;
                    vY = Mathf.Max(-2f, vY - g * Time.deltaTime);
                    SafeMoveVertical(vY * Time.deltaTime, groundLayer);
                    // 若被夹住落地，清零速度
                    if (groundedAfterVerticalMove && vY <= 0f) vY = 0f;
                    transform.Translate(Vector3.right * (curMove.jumpSpeed * 0.5f) * Time.deltaTime, Space.Self);
                }
            }
            return;
        }


        if (patrolRuntimeMoves == null || patrolRuntimeMoves.Count == 0)
            return;

        PatrolMovement move = patrolRuntimeMoves[patrolIndex];

        if (!isResting)
        {
            if (move.type == MovementType.Straight)
            {
                // 正在执行来自直线的自动跳：本帧只走 Jump 内核
                if (isJumping)
                {
                    JumpUpdate(move);
                    return;
                }

                bool wallAhead = CheckWallAhead();
                bool cliffAhead = (ignoreCliffFramesLeft <= 0) && CheckCliffAhead();
                bool hitObstacle = wallAhead || cliffAhead;


                if (hitObstacle)
                {
                    bool canAutoJump =
                        move.obstacleBehavior == ObstacleBehavior.AutoJump &&
                        inAutoJumpPermitZone &&
                        autoJumpArmed &&
                        CheckGrounded() &&
                        !isJumping &&
                        turnCooldown <= 0f;

                    if (canAutoJump)
                    {
                        // 自动跳：用本 movement 的 jump* 参数 + 你的 Jump 内核
                        BeginOneJump(move);

                        // 直线段“从新开始计算”
                        move.moveDuration = config.patrolConfig.movements[patrolIndex].moveDuration;

                        autoJumpFromStraight = true;
                        autoJumpArmed = false;            // 消耗一次触发
                        autoJumpDisarmUntilExit = true;   // 必须先离开许可区才能再次触发
                        return; // 本帧不再做直线位移
                    }
                    else
                    {
                        // 不满足自动跳条件 —— 按老规矩转向（避免顶墙抖动/卡住）
                        if (turnCooldown <= 0f)
                            TurnAround();
                    }
                }

                // ↓↓↓ 保持你原有的直线动画/位移/计时逻辑 ↓↓↓
                if (!string.IsNullOrEmpty(move.moveAnimation) &&
                    !animator.GetCurrentAnimatorStateInfo(0).IsName(move.moveAnimation))
                {
                    animator.Play(move.moveAnimation);
                    activeStraightMove = move;
                }

                transform.Translate(Vector3.right * move.moveSpeed * Time.deltaTime, Space.Self);

                move.moveDuration -= Time.deltaTime;
                if (move.moveDuration <= 0f)
                {
                    isResting = true;
                    move.moveDuration = config.patrolConfig.movements[patrolIndex].moveDuration;
                }
            }


            else // Jump 模式
            {
                if (!isJumping)
                {
                    if (CheckGrounded())
                    {
                        BeginOneJump(move);
                    }
                    else
                    {
                        // PatrolUpdate() -> Jump 模式里，非 isJumping 分支的 else 中
                        if (!hasAlignedToGround)
                        {
                            if (CheckGrounded())
                            {
                                Bounds b = col.bounds;
                                // 用中间那条射线重新量一下精确落点
                                Vector2 origin = new Vector2(b.center.x, b.min.y + 0.02f);
                                float rayLen = 0.2f;
                                var hit = Physics2D.Raycast(origin, Vector2.down, rayLen, groundLayer);
                                if (hit.collider)
                                {
                                    transform.position = new Vector3(transform.position.x, hit.point.y + b.extents.y + 0.02f, transform.position.z);
                                    vY = 0;
                                    hasAlignedToGround = true;
                                }
                            }
                        }

                        // 无论是否贴地成功，都用统一的重力积分与水平推进（不会反复“强制归零”）
                        float g = move.gravityScale * BASE_G;
                        vY = Mathf.Max(-2f, vY - g * Time.deltaTime);

                        // 用安全竖直位移，避免穿透与反复被“抬回”
                        SafeMoveVertical(vY * Time.deltaTime, groundLayer);
                        if (groundedAfterVerticalMove && vY <= 0f) vY = 0f; // 若被夹住视为落地，清零下落速度

                        transform.Translate(Vector3.right * (move.jumpSpeed * 0.5f) * Time.deltaTime, Space.Self);
                    }

                }
                else
                {
                    JumpUpdate(move);
                }
            }
        }
        else
        {
            string restAnim =
                (move.type == MovementType.Jump && !string.IsNullOrEmpty(move.jumpRestAnimation))
                    ? move.jumpRestAnimation
                    : move.restAnimation;

            if (!string.IsNullOrEmpty(restAnim) &&
                !animator.GetCurrentAnimatorStateInfo(0).IsName(restAnim))
                animator.Play(restAnim);

            float restLeft =
                (move.type == MovementType.Jump) ? move.jumpRestDuration : move.restDuration;

            restLeft -= Time.deltaTime;

            if (move.type == MovementType.Jump)
                move.jumpRestDuration = restLeft;
            else
                move.restDuration = restLeft;

            if (restLeft <= 0f)
            {
                isResting = false;
                move.restDuration = config.patrolConfig.movements[patrolIndex].restDuration;
                move.jumpRestDuration = config.patrolConfig.movements[patrolIndex].jumpRestDuration;

                if (keepSameMoveAfterRest)
                {
                    keepSameMoveAfterRest = false;

                    // ★ 开启悬崖检测抑制窗口（只抑制悬崖，不抑制墙）
                    ignoreCliffFramesLeft = Mathf.Max(0, autoJumpIgnoreCliffFrames);
                }
                else
                {
                    patrolIndex = (patrolIndex + 1) % patrolRuntimeMoves.Count;
                    activeStraightMove = null;
                    activeJumpMove = null;
                }
            }

        }
    }

    // === 地形检测模块 ===
    bool CheckGrounded()
    {
        if (col == null) return false;
        Physics2D.SyncTransforms();
        return BestVerticalHit(0.12f, true).collider != null;
    }

    bool CheckWallAhead()
    {
        if (col == null) return false;
        Bounds b = col.bounds;

        // ✅ 改为从脚底偏移，直观控制高度
        float castY = b.min.y + wallCheckHeightOffset;
        Vector2 origin = new Vector2(b.center.x, castY);
        Vector2 dir = (Vector2)transform.right;

        float dist = Mathf.Max(0.1f, wallCheckDistance);
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, groundLayer);
        Debug.DrawLine(origin, origin + dir * dist, hit.collider ? Color.red : Color.yellow);

        if (!hit.collider) return false;
        if (Vector2.Dot(hit.normal, dir) < -0.4f) return true;
        return false;
    }

    bool CheckCliffAhead()
    {
        if (col == null) return false;
        if (!CheckGrounded()) return false;
        Bounds b = col.bounds;
        Vector2 origin = new Vector2(b.center.x, b.min.y + 0.05f);
        origin += (Vector2)transform.right * (b.extents.x + cliffCheckOffsetX);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, cliffCheckDistance, groundLayer);
        Debug.DrawLine(origin, origin + Vector2.down * cliffCheckDistance, hit.collider ? Color.green : Color.blue);
        return hit.collider == null;
    }

    void TurnAround()
    {
        if (turnCooldown > 0f) return;
        float newY = (transform.rotation.eulerAngles.y == 0f) ? 180f : 0f;
        transform.rotation = Quaternion.Euler(0, newY, 0);
        turnCooldown = 0.25f;
    }

    // === 发现 / 攻击 ===
    void DiscoveryUpdate()
    {
        if (!player) return;
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= config.attackConfig.attackRange)
            state = MonsterState.Attack;
    }

    void AttackUpdate()
    {
        if (!player) return;
        var atk = config.attackConfig.attackPatterns.Count > 0 ? config.attackConfig.attackPatterns[0] : null;
        if (atk == null) return;

        float dist = Vector2.Distance(transform.position, player.position);
        if (dist > atk.meleeRange)
        {
            state = MonsterState.Discovery;
            return;
        }

        if (!string.IsNullOrEmpty(atk.animation))
            animator.Play(atk.animation);

        Debug.Log($"怪物攻击造成伤害：{atk.damage}");
        state = MonsterState.Discovery;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(autoJumpPermitTag))
        {
            inAutoJumpPermitZone = true;
            if (!autoJumpDisarmUntilExit)
                autoJumpArmed = true; // 进入许可区，上膛
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(autoJumpPermitTag))
        {
            inAutoJumpPermitZone = false;
            autoJumpDisarmUntilExit = false; // 离开许可区，解除保险
            autoJumpArmed = true;            // 下次再进入可再次触发
        }
    }



    void Die()
    {
        isDead = true;
        if (!string.IsNullOrEmpty(config.deathConfig.deathAnimation))
            animator.Play(config.deathConfig.deathAnimation);
        if (!string.IsNullOrEmpty(config.deathConfig.deathEffect))
            Debug.Log($"播放死亡特效：{config.deathConfig.deathEffect}");
        Destroy(gameObject, config.deathConfig.instantRemove ? 0f : 2f);
        if (spawner) spawner.NotifyMonsterDeath(gameObject);
    }

    private void PlayEffect(GameObject prefab)
    {
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, transform.position, Quaternion.identity, transform);
        var ps = fx.GetComponentInChildren<ParticleSystem>(true);
        if (ps)
        {
            ps.Play();
            Destroy(fx, ps.main.duration + 0.1f);
        }
        else
        {
            Destroy(fx, 2f);
        }
    }

    void BeginOneJump(PatrolMovement move)
    {
        if (!CheckGrounded() || isJumping) return;
        isJumping = true;
        activeJumpMove = move; // 绑定本次跳跃给“跳跃通道/落地休息通道”
        hasPlayedJumpEffect = false;
        hasPlayedJumpRestEffect = false;
        jumpTimeLeft = move.jumpDuration;

        float g = move.gravityScale * BASE_G;
        vY = Mathf.Sqrt(Mathf.Max(0.01f, 2f * Mathf.Max(0.2f, move.jumpHeight) * g));

        transform.position += Vector3.up * 0.05f;

        if (!string.IsNullOrEmpty(move.jumpAnimation) &&
            !animator.GetCurrentAnimatorStateInfo(0).IsName(move.jumpAnimation))
            animator.Play(move.jumpAnimation);

        if (!fxOnlyFromAnimationEvents)
        {
            if (move.jumpEffectPrefab && (!move.jumpEffectOnlyFirst || !hasPlayedJumpEffect))
            {
                PlayEffect(move.jumpEffectPrefab);
                hasPlayedJumpEffect = true;
            }
        }

    }

    void JumpUpdate(PatrolMovement move)
    {
        bool grounded = CheckGrounded();
        transform.Translate(Vector3.right * move.jumpSpeed * Time.deltaTime, Space.Self);
        float g = move.gravityScale * BASE_G;
        vY -= g * Time.deltaTime;

        // 用安全竖直位移替代直接 Translate，避免穿透
        SafeMoveVertical(vY * Time.deltaTime, groundLayer);

        // 如果本帧因下撞被夹住且速度向下，则视为落地
        if (groundedAfterVerticalMove && vY <= 0f)
        {
            vY = 0f;
        }


        if (grounded && turnCooldown <= 0f && CheckWallAhead())  // 只在落地时才允许因墙翻向
        {
            TurnAround();
            turnCooldown = 0.25f;
        }

        if (jumpTimeLeft > 0f) jumpTimeLeft = Mathf.Max(0f, jumpTimeLeft - Time.deltaTime);

        // 如果 SafeMoveVertical 已经把我们夹在地面上，本帧强制认为已落地
        if (groundedAfterVerticalMove && vY <= 0f) grounded = true;

        // 只有真的落地时才结束一次跳跃
        if (grounded && vY <= 0f)
        {
            vY = 0f;
            isJumping = false;
            isResting = true;
            move.restDuration = move.jumpRestDuration;

            // 来自直线的自动跳：休息完仍回到本直线段
            if (autoJumpFromStraight)
            {
                keepSameMoveAfterRest = true;
                autoJumpFromStraight = false;
            }

            if (!string.IsNullOrEmpty(move.jumpRestAnimation) &&
                !animator.GetCurrentAnimatorStateInfo(0).IsName(move.jumpRestAnimation))
                animator.Play(move.jumpRestAnimation);

            if (!fxOnlyFromAnimationEvents)
            {
                if (move.jumpRestEffectPrefab && (!move.jumpRestEffectOnlyFirst || !hasPlayedJumpRestEffect))
                {
                    PlayEffect(move.jumpRestEffectPrefab);
                    hasPlayedJumpRestEffect = true;
                }
            }

        }

    }

    /// <summary>
    /// 安全的竖直位移：在位移前用三条射线预判，若将与地面相撞则把位移夹到地面上方（或下方）
    /// 仅用于跳跃分支，避免直接 Translate 穿透
    /// </summary>
    private void SafeMoveVertical(float dy, LayerMask groundMask)
    {
        groundedAfterVerticalMove = false;
        if (Mathf.Approximately(dy, 0f) || col == null) return;

        float rayLen = Mathf.Abs(dy) + SKIN;
        bool down = dy < 0f;

        var hit = BestVerticalHit(rayLen, down);
        if (hit.collider)
        {
            float allowed = Mathf.Max(0f, hit.distance - SKIN);
            float sign = Mathf.Sign(dy);
            float applied = Mathf.Min(Mathf.Abs(dy), allowed) * sign;

            transform.Translate(Vector3.up * applied, Space.World);

            if (down && allowed <= Mathf.Abs(dy))
                groundedAfterVerticalMove = true; // 真正被夹住才算落地
        }
        else
        {
            transform.Translate(Vector3.up * dy, Space.World);
        }
    }


    // 统一的三射线竖直探测：down=true 从脚底向下，否则从头顶向上；返回最近命中（无命中 collider 为 null）
    private RaycastHit2D BestVerticalHit(float rayLen, bool down)
    {
        Bounds b = col.bounds;

        Vector2 left, center, right, dir;


        if (down)
        {
            left = new Vector2(b.min.x + 0.05f, b.min.y + 0.02f);
            center = new Vector2(b.center.x, b.min.y + 0.02f);
            right = new Vector2(b.max.x - 0.05f, b.min.y + 0.02f);
            dir = Vector2.down;
        }
        else
        {
            left = new Vector2(b.min.x + 0.05f, b.max.y - 0.02f);
            center = new Vector2(b.center.x, b.max.y - 0.02f);
            right = new Vector2(b.max.x - 0.05f, b.max.y - 0.02f);
            dir = Vector2.up;
        }

        var hitL = Physics2D.Raycast(left, dir, rayLen, groundLayer);
        var hitC = Physics2D.Raycast(center, dir, rayLen, groundLayer);
        var hitR = Physics2D.Raycast(right, dir, rayLen, groundLayer);



        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        if (hitL.collider && hitL.distance < bestDist) { best = hitL; bestDist = hitL.distance; }
        if (hitC.collider && hitC.distance < bestDist) { best = hitC; bestDist = hitC.distance; }
        if (hitR.collider && hitR.distance < bestDist) { best = hitR; bestDist = hitR.distance; }
        return best; // best.collider == null 表示无命中
    }



    // === 动画事件 ===
    public void OnSpawnEffect() => PlayEffect(config.spawnConfig.spawnEffectPrefab);
    public void OnIdleEffect() => PlayEffect(config.spawnConfig.idleEffectPrefab);
    // === 动画事件 ===
    public void OnPatrolMoveEffect()
    {
        var m = activeStraightMove;
        if (m != null && m.moveEffectPrefab)
            PlayEffect(m.moveEffectPrefab);
    }

    public void OnPatrolRestEffect()
    {
        var m = activeStraightMove;
        if (m != null && m.restEffectPrefab)
            PlayEffect(m.restEffectPrefab);
    }

    public void OnPatrolJumpEffect()
    {
        var m = activeJumpMove;
        if (m != null && m.jumpEffectPrefab)
            PlayEffect(m.jumpEffectPrefab);
    }

    public void OnPatrolJumpRestEffect()
    {
        var m = activeJumpMove;
        if (m != null && m.jumpRestEffectPrefab)
            PlayEffect(m.jumpRestEffectPrefab);
    }

}
