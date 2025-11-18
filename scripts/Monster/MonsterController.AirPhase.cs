using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public partial class MonsterController : MonoBehaviour
{
    // ===== 空中巡逻运行态 =====
    private float _airSavedGravity = 0f;
    private bool _airSetupDone = false;

    private List<int> _airOrder = null;
    private int _airOrderPos = 0;
    private int _airActiveIndex = 0;

    // 运行用：当前线性速度（世界空间）
    private Vector2 _airVel = Vector2.zero;

    private int _airPingPongSign = +1;
    private int _airVerticalSign = +1; // 垂直当前符号：+1=向上，-1=向下

    // Sine 累计时间
    private float _airTime = 0f;

    private bool _airMoveFxPlayedThisSegment = false;
    
    private bool _airRestFxPlayedThisRest = false;

    // 进入空中阶段：去重力 + 初始化顺序
    // 仅贴出修改片段：EnterAirPhaseSetup 内新增的几行
    private void EnterAirPhaseSetup()
    {
        if (_airSetupDone) return;

        _airSetupDone = true;
        _airTime = 0f;
        _airVel = Vector2.zero;

        // 去重力（保存以便未来恢复）
        _airSavedGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        // 空中阶段不受 AutoJumpZone 影响，进入空中时清理其状态
        inAutoJumpPermitZone = false;
        autoJumpRearmAfterLanding = false;

        // 初始化空中巡逻顺序（原有逻辑保持）
        var cfg = config?.airStageConfig?.patrol;
        _airOrder = new List<int>();
        int n = (cfg != null && cfg.elements != null) ? cfg.elements.Count : 0;
        for (int i = 0; i < n; i++) _airOrder.Add(i);
        _airOrderPos = 0;
        if (cfg != null && cfg.randomOrder && n > 1) Shuffle(_airOrder);
        _airActiveIndex = (n > 0) ? _airOrder[0] : -1;
        _airPingPongSign = (FacingSign() >= 0) ? +1 : -1;
        _airVerticalSign = +1;
        _airMoveFxPlayedThisSegment = false;
        _airRestFxPlayedThisRest = false;
    }

    private void AirPatrolUpdate()
    {
        var pCfg = config?.airStageConfig?.patrol;
        if (pCfg == null || pCfg.elements == null || pCfg.elements.Count == 0)
        {
            if (!string.IsNullOrEmpty(pCfg?.skyrestAnimation))
                PlayAnimIfNotCurrent(pCfg.skyrestAnimation);
            return;
        }

        if (_airActiveIndex < 0 || _airActiveIndex >= pCfg.elements.Count)
        {
            _airActiveIndex = _airOrder.Count > 0 ? _airOrder[0] : -1;
            _airOrderPos = 0;
            if (_airActiveIndex < 0) return;
        }

        var elem = pCfg.elements[_airActiveIndex];
        var mv = elem.move;

        // 新语义：true=受 area 影响；false=忽略 area
        bool affectByArea = pCfg.canPassThroughScene;

        // 段起点：初始化一次性FX与运动方向/速度
        if (mv.rtStraightPhase == StraightPhase.None)
        {
            _airMoveFxPlayedThisSegment = false;
            _airRestFxPlayedThisRest = false;

            Vector2 initDir;
            switch (pCfg.pathType)
            {
                case AirPatrolPathType.AreaHorizontal:
                    initDir = Vector2.right * Mathf.Sign(_airPingPongSign == 0 ? 1 : _airPingPongSign);
                    break;
                case AirPatrolPathType.AreaVertical:
                    initDir = Vector2.up * Mathf.Sign(_airVerticalSign == 0 ? 1 : _airVerticalSign);
                    break;
                case AirPatrolPathType.AreaRandom:
                    initDir = Random.insideUnitCircle.normalized; // 每段重新随机
                    break;
                case AirPatrolPathType.AreaRandomH:
                    // 仅首次随机；之后沿历史方向（由碰撞/边界反弹更新）
                    initDir = (_airVel.sqrMagnitude > 0.0001f) ? _airVel.normalized : Random.insideUnitCircle.normalized;
                    break;
                default:
                    initDir = Random.insideUnitCircle.normalized;
                    break;
            }
            if (initDir.sqrMagnitude < 0.01f) initDir = Vector2.right;

            // 直线段运行态（加/匀/减/休息）
            mv.rtCurrentSpeed = 0f;
            mv.rtCruiseTimer = Mathf.Max(0f, mv.moveDuration);
            mv.rtAccelTimer = Mathf.Max(0f, mv.accelerationTime);
            mv.rtDecelTimer = Mathf.Max(0f, mv.decelerationTime);
            bool instantAccel = (mv.accelerationTime <= 0f && mv.acceleration <= 0f);
            bool instantDecel = (mv.decelerationTime <= 0f && mv.deceleration <= 0f);
            mv.rtStraightPhase = instantAccel
                ? ((mv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (instantDecel ? StraightPhase.Rest : StraightPhase.Decel))
                : StraightPhase.Accel;
            if (instantAccel) mv.rtCurrentSpeed = Mathf.Max(0f, mv.moveSpeed);

            float seedSpeed = (mv.rtCurrentSpeed > 0f ? mv.rtCurrentSpeed : Mathf.Max(0.01f, mv.moveSpeed * 0.1f));
            _airVel = initDir.normalized * seedSpeed;
        }

        // 三相速度标量推进
        float targetSpd = Mathf.Max(0f, mv.moveSpeed);
        float accelRate = (mv.accelerationTime > 0f) ? (Mathf.Max(0.01f, targetSpd) / mv.accelerationTime) : Mathf.Max(0f, mv.acceleration);
        float decelRate = (mv.decelerationTime > 0f) ? (Mathf.Max(0.01f, targetSpd) / mv.decelerationTime) : Mathf.Max(0f, mv.deceleration);

        switch (mv.rtStraightPhase)
        {
            case StraightPhase.Accel:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation)) PlayAnimIfNotCurrent(pCfg.skymoveAnimation);
                if (accelRate <= 0f) { mv.rtCurrentSpeed = targetSpd; mv.rtStraightPhase = (mv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest); }
                else
                {
                    mv.rtCurrentSpeed = Mathf.MoveTowards(mv.rtCurrentSpeed, targetSpd, accelRate * Time.deltaTime);
                    if (mv.accelerationTime > 0f) mv.rtAccelTimer = Mathf.Max(0f, mv.rtAccelTimer - Time.deltaTime);
                    if (Mathf.Approximately(mv.rtCurrentSpeed, targetSpd) || mv.rtAccelTimer <= 0f)
                        mv.rtStraightPhase = (mv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (decelRate > 0f ? StraightPhase.Decel : StraightPhase.Rest);
                }
                break;

            case StraightPhase.Cruise:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation)) PlayAnimIfNotCurrent(pCfg.skymoveAnimation);
                mv.rtCurrentSpeed = targetSpd;
                mv.rtCruiseTimer = Mathf.Max(0f, mv.rtCruiseTimer - Time.deltaTime);
                if (mv.rtCruiseTimer <= 0f) mv.rtStraightPhase = (decelRate > 0f) ? StraightPhase.Decel : StraightPhase.Rest;
                break;

            case StraightPhase.Decel:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation)) PlayAnimIfNotCurrent(pCfg.skymoveAnimation);
                if (decelRate <= 0f) { mv.rtCurrentSpeed = 0f; mv.rtStraightPhase = StraightPhase.Rest; }
                else
                {
                    mv.rtCurrentSpeed = Mathf.MoveTowards(mv.rtCurrentSpeed, 0f, decelRate * Time.deltaTime);
                    if (mv.rtCurrentSpeed <= 0.0001f) { mv.rtCurrentSpeed = 0f; mv.rtStraightPhase = StraightPhase.Rest; }
                }
                break;

            case StraightPhase.Rest:
            default:
                if (!string.IsNullOrEmpty(pCfg.skyrestAnimation)) PlayAnimIfNotCurrent(pCfg.skyrestAnimation);
                mv.rtRestTimer = (mv.rtRestTimer > 0f) ? mv.rtRestTimer - Time.deltaTime : PickStraightRestTime(mv) - Time.deltaTime;
                if (mv.rtRestTimer <= 0f)
                {
                    mv.rtStraightPhase = StraightPhase.None;
                    mv.rtMoveTimer = mv.rtRestTimer = mv.rtAccelTimer = mv.rtCruiseTimer = mv.rtDecelTimer = 0f;

                    if (pCfg.randomOrder && pCfg.elements.Count > 1)
                    {
                        _airOrderPos = (_airOrderPos + 1) % _airOrder.Count;
                        _airActiveIndex = _airOrder[_airOrderPos];
                    }
                    else
                    {
                        _airActiveIndex = (_airActiveIndex + 1) % pCfg.elements.Count;
                    }
                }
                rb.velocity = Vector2.zero;
                return;
        }

        // 两模式：每帧强制主方向是纯 X/纯 Y；随机保留 _airVel 的方向
        if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            _airVel = new Vector2(Mathf.Sign((_airVel.x == 0f) ? _airPingPongSign : _airVel.x), 0f) * Mathf.Max(0f, mv.rtCurrentSpeed);
        else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            _airVel = new Vector2(0f, Mathf.Sign((_airVel.y == 0f) ? _airVerticalSign : _airVel.y)) * Mathf.Max(0f, mv.rtCurrentSpeed);
        else
            _airVel = (_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right) * Mathf.Max(0f, mv.rtCurrentSpeed);

        Vector2 newVel = _airVel; // 本帧“逻辑速度”（包含反弹后的方向/大小），末尾用它回填 _airVel

        // Sine（速度式）：水平→上下摆；竖直→左右摆；随机→沿主方向法线摆
        _airTime += Time.deltaTime;
        Vector2 sineDelta = Vector2.zero;
        if (elem.sinEnabled && elem.sinAmplitude > 0f && elem.sinFrequency > 0f)
        {
            float s = Mathf.Sin(_airTime * Mathf.PI * 2f * elem.sinFrequency) * elem.sinAmplitude;
            Vector2 axis;
            if (pCfg.pathType == AirPatrolPathType.AreaHorizontal) axis = Vector2.up;
            else if (pCfg.pathType == AirPatrolPathType.AreaVertical) axis = Vector2.right;
            else { Vector2 d = (_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right); axis = new Vector2(-d.y, d.x); }
            sineDelta = axis.normalized * (s * Time.deltaTime);
        }

        // ========== 区域（用 monsterDistPoint 作为判定锚点；是否启用由 affectByArea 决定） ==========
        Vector2 anchor = monsterDistPoint ? (Vector2)monsterDistPoint.position : rb.position;
        Vector2 rootToAnchor = rb.position - anchor;

        Rect area = new Rect(elem.areaCenter - elem.areaSize * 0.5f, elem.areaSize);

        // 只有“受 area 影响”时，才把锚点拉回区域内
        if (affectByArea && !area.Contains(anchor))
        {
            Vector2 snappedAnchor = new Vector2(
                Mathf.Clamp(anchor.x, area.xMin, area.xMax),
                Mathf.Clamp(anchor.y, area.yMin, area.yMax)
            );
            Vector2 deltaSnap = snappedAnchor - anchor;
            rb.position += deltaSnap;
            anchor = snappedAnchor;
            rootToAnchor = rb.position - anchor;
        }

        // ========== 场景碰撞（始终生效；使用根 BoxCollider2D Cast） ==========
        Vector2 posRB = rb.position;
        Vector2 plannedDeltaRB = _airVel * Time.deltaTime + sineDelta;
        Vector2 castDir = plannedDeltaRB.sqrMagnitude > 0.0000001f ? plannedDeltaRB.normalized : Vector2.zero;
        float castDist = plannedDeltaRB.magnitude;

        // 默认按计划位移，若命中碰撞再覆盖
        Vector2 nextRB = posRB + plannedDeltaRB;

        // ===== 场景碰撞（根 BoxCollider2D 形状 Cast）：Horizontal/Vertical 强制轴向处理 =====
        if (col != null)
        {
            // 针对不同模式，确定“用于碰撞检测”的轴向 Cast 方向与距离
            Vector2 castDirScene;
            float castDistScene;

            if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            {
                float sx = Mathf.Sign(_airVel.x == 0f ? 1f : _airVel.x);
                castDirScene = new Vector2(sx, 0f);
                castDistScene = Mathf.Abs(_airVel.x) * Time.deltaTime;
            }
            else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            {
                float sy = Mathf.Sign(_airVel.y == 0f ? -1f : _airVel.y);
                castDirScene = new Vector2(0f, sy);
                castDistScene = Mathf.Abs(_airVel.y) * Time.deltaTime;
            }
            else // AreaRandom
            {
                castDirScene = plannedDeltaRB.sqrMagnitude > 0.0000001f ? plannedDeltaRB.normalized : Vector2.zero;
                castDistScene = plannedDeltaRB.magnitude;
            }

            if (castDistScene > 0.00001f && castDirScene.sqrMagnitude > 0f)
            {
                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(groundLayer);

                RaycastHit2D[] hits = new RaycastHit2D[6];
                int cnt = col.Cast(castDirScene, filter, hits, castDistScene + 0.01f);

                float bestD = float.MaxValue;
                RaycastHit2D best = default;
                for (int i = 0; i < cnt; i++)
                {
                    var h = hits[i];
                    if (!h.collider) continue;
                    if (h.collider.isTrigger) continue;
                    if (h.collider.CompareTag(autoJumpPermitTag)) continue; // 许可区不是障碍
                    if (h.collider.CompareTag("Player")) continue;
                    if (player && h.collider.transform.root == player.root) continue;

                    if (h.distance < bestD) { bestD = h.distance; best = h; }
                }

                if (best.collider)
                {
                    const float SKIN = 0.005f;
                    float allow = Mathf.Max(0f, best.distance - SKIN);

                    if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
                    {
                        float speed = Mathf.Max(0f, _airVel.magnitude);
                        float newSign = -Mathf.Sign(_airVel.x == 0f ? 1f : _airVel.x);

                        // 位置：退到接触点前，再沿新水平方向轻推，避免卡边
                        nextRB = posRB + castDirScene * allow + new Vector2(newSign, 0f) * 0.01f;

                        // 速度：纯水平反向，Y=0（禁止顺坡）
                        _airVel = new Vector2(newSign, 0f) * speed;
                        _airPingPongSign = (_airVel.x >= 0f) ? +1 : -1;
                    }
                    else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
                    {
                        float speed = Mathf.Max(0f, _airVel.magnitude);

                        // 位置：退到接触点前，再轻微向下退出
                        nextRB = posRB + castDirScene * allow + Vector2.down * 0.01f;

                        // 速度：强制竖直向下，X=0（禁止顺坡）
                        _airVel = Vector2.down * speed;
                        _airVerticalSign = -1;
                    }
                    else // AreaRandom
                    {
                        Vector2 n = best.normal.normalized;
                        // 入射角（世界）
                        float inAng = Mathf.Atan2(_airVel.y, _airVel.x);
                        // 切线角度（用切线做镜像轴），入射角=出射角
                        Vector2 t = new Vector2(-n.y, n.x);
                        float tAng = Mathf.Atan2(t.y, t.x);
                        float outAng = 2f * tAng - inAng;
                        Vector2 rDir = new Vector2(Mathf.Cos(outAng), Mathf.Sin(outAng));
                        if (rDir.sqrMagnitude < 0.0001f) rDir = -_airVel.normalized;

                        float speed = Mathf.Max(_airVel.magnitude, mv.rtCurrentSpeed);
                        newVel = rDir.normalized * speed; // 不直接改 _airVel，末尾统一回填

                        // 位置：退到接触点前，再沿新方向轻推
                        nextRB = posRB + castDirScene * allow + rDir.normalized * 0.01f;
                    }
                }
            }
        }

        // ========== 区域边界（仅当 affectByArea=true 时生效；规则与碰撞体一致） ==========
        Vector2 nextAnchor = nextRB - rootToAnchor;

        if (affectByArea)
        {
            bool outside = !area.Contains(nextAnchor);
            bool atLeft = nextAnchor.x <= area.xMin + 0.0001f;
            bool atRight = nextAnchor.x >= area.xMax - 0.0001f;
            bool atBottom = nextAnchor.y <= area.yMin + 0.0001f;
            bool atTop = nextAnchor.y >= area.yMax - 0.0001f;

            if (outside || atLeft || atRight || atBottom || atTop)
            {
                if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
                {
                    _airVel.x = -_airVel.x; _airVel.y = 0f;
                    _airPingPongSign = (_airVel.x >= 0f) ? +1 : -1;
                }
                else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
                {
                    _airVel.y = -_airVel.y; _airVel.x = 0f;
                    _airVerticalSign = (_airVel.y >= 0f) ? +1 : -1;
                }
                else
                {
                    // 小球式反弹：在矩形边界上按“入射角 = 出射角”计算
                    // 1) 用“锚点到矩形内部最近点”的向量近似边界法线，再做切线镜像
                    Vector2 cen = elem.areaCenter;
                    Vector2 half = elem.areaSize * 0.5f;

                    // 当前锚点（可能越界）
                    Vector2 p = nextAnchor;

                    // 最近点 q（夹回到矩形内）
                    Vector2 q = new Vector2(
                        Mathf.Clamp(p.x, cen.x - half.x, cen.x + half.x),
                        Mathf.Clamp(p.y, cen.y - half.y, cen.y + half.y)
                    );

                    Vector2 nApprox = (p - q); // 近似法线（指向矩形外）
                    if (nApprox.sqrMagnitude < 0.000001f)
                    {
                        // 极端：正好在边/角导致零向量，回退到原轴向近似
                        float dxAbs = Mathf.Abs(p.x - cen.x) - half.x;
                        float dyAbs = Mathf.Abs(p.y - cen.y) - half.y;
                        bool overX = dxAbs > 0f;
                        bool overY = dyAbs > 0f;

                        if (overX && overY)
                        {
                            float maxOut = Mathf.Max(Mathf.Abs(dxAbs), Mathf.Abs(dyAbs));
                            if (maxOut > 0f && Mathf.Abs(Mathf.Abs(dxAbs) - Mathf.Abs(dyAbs)) < 0.15f * maxOut)
                            {
                                nApprox = new Vector2(Mathf.Sign(p.x - cen.x), Mathf.Sign(p.y - cen.y)).normalized;
                            }
                            else
                            {
                                nApprox = (Mathf.Abs(dxAbs) > Mathf.Abs(dyAbs))
                                    ? new Vector2(Mathf.Sign(p.x - cen.x), 0f)
                                    : new Vector2(0f, Mathf.Sign(p.y - cen.y));
                            }
                        }
                        else
                        {
                            nApprox = (Mathf.Abs(dxAbs) > Mathf.Abs(dyAbs))
                                ? new Vector2(Mathf.Sign(p.x - cen.x), 0f)
                                : new Vector2(0f, Mathf.Sign(p.y - cen.y));
                            if (!overX && overY) nApprox = new Vector2(0f, Mathf.Sign(p.y - cen.y));
                            if (overX && !overY) nApprox = new Vector2(Mathf.Sign(p.x - cen.x), 0f);
                        }
                    }

                    if (nApprox == Vector2.zero)
                        nApprox = -(_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);
                    nApprox = nApprox.normalized;

                    // 2) 切线镜像：outAng = 2*tAng - inAng（入射角=出射角）
                    float inAng = Mathf.Atan2(_airVel.y, _airVel.x);
                    Vector2 t = new Vector2(-nApprox.y, nApprox.x);
                    float tAng = Mathf.Atan2(t.y, t.x);
                    float outAng = 2f * tAng - inAng;

                    Vector2 rDir = new Vector2(Mathf.Cos(outAng), Mathf.Sin(outAng));
                    if (rDir.sqrMagnitude < 0.0001f)
                        rDir = -(_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);

                    newVel = rDir.normalized * Mathf.Max(_airVel.magnitude, mv.rtCurrentSpeed); // 末尾统一回填
                }

                // 锚点夹回区域，刚体位置随之调整
                nextAnchor = new Vector2(
                    Mathf.Clamp(nextAnchor.x, area.xMin, area.xMax),
                    Mathf.Clamp(nextAnchor.y, area.yMin, area.yMax)
                );
                nextRB = nextAnchor + rootToAnchor;
            }
        }

        // 应用位移 + 更新实际速度
        rb.MovePosition(nextRB);
        _airVel = newVel; // 关键：保持本帧计算出的反弹方向与速度，不被“夹回位移”改写

        // 朝向：Horizontal 与 Random 按水平速度翻面；Vertical 不翻面
        if ((pCfg.pathType == AirPatrolPathType.AreaHorizontal
             || pCfg.pathType == AirPatrolPathType.AreaRandom
             || pCfg.pathType == AirPatrolPathType.AreaRandomH)
            && Mathf.Abs(_airVel.x) > 0.0005f)
        {
            ForceFaceSign(_airVel.x >= 0f ? +1 : -1);
        }

        // 自转：绕“碰撞体中心(优先) → monsterDistPoint → 根位置”为轴心；支持正/负角速度；不影响位移
        if (pCfg.selfRotate)
        {
            // 保留符号（支持 Inspector 填负数）
            float deg = pCfg.selfRotateSpeedDeg * Time.deltaTime;

            // 轴心优先级：BoxCollider2D.bounds.center（世界坐标）> monsterDistPoint > transform.position
            Vector3 pivotW = transform.position;
            if (col != null) pivotW = col.bounds.center;
            else if (monsterDistPoint) pivotW = monsterDistPoint.position;

            // 旋转目标：优先旋转可视节点（Animator 根），避免影响刚体根的 MovePosition
            Transform t = animator ? animator.transform : transform;

            // 如果旋转根，需要在旋转后把根位置还原，避免拖拽位移
            Vector3 keepRootPos = transform.position;
            bool rotatingRoot = (t == transform);

            if (pCfg.selfRotateX) t.RotateAround(pivotW, t.right, deg);
            if (pCfg.selfRotateY) t.RotateAround(pivotW, t.up, deg);
            if (pCfg.selfRotateZ) t.RotateAround(pivotW, t.forward, deg);

            if (rotatingRoot) transform.position = keepRootPos;
        }

        // 移动动画
        if (!string.IsNullOrEmpty(pCfg.skymoveAnimation))
            PlayAnimIfNotCurrent(pCfg.skymoveAnimation);
    }

    // 空中移动特效（由动画事件触发）
    public void OnFxSkyMove()
    {
        if (state != MonsterState.Air) return;

        var pCfg = config?.airStageConfig?.patrol;
        if (pCfg == null) return;
        if (pCfg.skymoveEffectPrefab == null) return;
        // 去重：同一“线性段”只播一次（可选，如果你希望动画多次事件就多次播放，删除这行判断即可）
        if (_airMoveFxPlayedThisSegment) return;

        PlayEffect(pCfg.skymoveEffectPrefab, fxSkyMovePoint ? fxSkyMovePoint : transform);
        _airMoveFxPlayedThisSegment = true;
    }

    // 空中休息特效（由动画事件触发）
    public void OnFxSkyRest()
    {
        if (state != MonsterState.Air) return;

        var pCfg = config?.airStageConfig?.patrol;
        if (pCfg == null) return;
        if (pCfg.skyrestEffectPrefab == null) return;
        // 去重：同一休息段只播一次
        if (_airRestFxPlayedThisRest) return;

        PlayEffect(pCfg.skyrestEffectPrefab, fxSkyRestPoint ? fxSkyRestPoint : transform);
        _airRestFxPlayedThisRest = true;
    }

}