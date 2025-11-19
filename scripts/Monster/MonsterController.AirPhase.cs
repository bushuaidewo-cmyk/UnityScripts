using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static System.Net.WebRequestMethods;

public partial class MonsterController : MonoBehaviour
{
    // ===== 空中巡逻运行态 =====
    private float _airSavedGravity = 0f;
    private bool _airSetupDone = false;

    private List<int> _airOrder = null;
    private int _airOrderPos = 0;
    private int _airActiveIndex = 0;

    // 运行用：当前线性速度（世界空间）——“主运动速度”
    private Vector2 _airVel = Vector2.zero;

    // 最近一次非零的“纯方向”（用于随机模式在速度=0阶段保持原方向）
    private Vector2 _airLastDir = Vector2.right;

    private int _airPingPongSign = +1; // 水平当前符号：+1=向右，-1=向左
    private int _airVerticalSign = +1; // 垂直当前符号：+1=向上，-1=向下

    // Sine 累计时间（用于正弦摆动）
    private float _airTime = 0f;

    private bool _airMoveFxPlayedThisSegment = false;
    private bool _airRestFxPlayedThisRest = false;

    // 仅用于调试：当前 anchor 是否在区域边缘
    private bool isAtEdge = false;

    // 进入空中阶段：去重力 + 初始化顺序
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

        // 用当前锚点（BoxCollider2D中心）检测是否接近区域边缘，仅作为 debug 标记
        Rect area = new Rect(elem.areaCenter - elem.areaSize * 0.5f, elem.areaSize);
        Vector2 anchorPos;
        if (col != null)
            anchorPos = col.bounds.center;                 // 区域反弹点：BoxCollider2D 中心
        else if (monsterDistPoint)
            anchorPos = monsterDistPoint.position;
        else
            anchorPos = rb.position;

        bool atLeft = anchorPos.x <= area.xMin + 0.0001f;
        bool atRight = anchorPos.x >= area.xMax - 0.0001f;
        bool atTop = anchorPos.y >= area.yMax - 0.0001f;
        bool atBottom = anchorPos.y <= area.yMin + 0.0001f;
        bool boundaryDetected = atLeft || atRight || atTop || atBottom;
        isAtEdge = boundaryDetected;    // 真正的反弹逻辑放在 AirPatrolPhysicsStep，这里只做标记

        // 段起点：初始化一次 FX 防抖 + 速度相位
        if (mv.rtStraightPhase == StraightPhase.None)
        {
            _airMoveFxPlayedThisSegment = false;
            _airRestFxPlayedThisRest = false;

            Vector2 initDir;

            switch (pCfg.pathType)
            {
                case AirPatrolPathType.AreaHorizontal:
                    // 只在 X 轴左右移动
                    if (_airVel.sqrMagnitude > 0.0001f)
                        initDir = new Vector2(Mathf.Sign(_airVel.x == 0f ? 1f : _airVel.x), 0f);
                    else
                        initDir = Vector2.right * Mathf.Sign(_airPingPongSign == 0 ? 1 : _airPingPongSign);
                    break;

                case AirPatrolPathType.AreaVertical:
                    // 只在 Y 轴上下移动
                    if (_airVel.sqrMagnitude > 0.0001f)
                        initDir = new Vector2(0f, Mathf.Sign(_airVel.y == 0f ? 1f : _airVel.y));
                    else
                        initDir = Vector2.up * Mathf.Sign(_airVerticalSign == 0 ? 1 : _airVerticalSign);
                    break;

                case AirPatrolPathType.AreaRandom:
                    // 每段起点重新随机一个 2D 方向
                    initDir = Random.insideUnitCircle.normalized;
                    break;

                case AirPatrolPathType.AreaRandomH:
                    // 初始角度随机为 45° / -45° / 135° / -135°；其余时沿当前方向
                    if (_airVel.sqrMagnitude < 0.0001f)
                    {
                        float[] degs = { 45f, -45f, 135f, -135f };
                        float ang = degs[Random.Range(0, degs.Length)] * Mathf.Deg2Rad;
                        initDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    }
                    else
                    {
                        initDir = _airVel.normalized;
                    }
                    break;

                default:
                    initDir = Random.insideUnitCircle.normalized;
                    break;
            }

            if (initDir.sqrMagnitude < 0.01f)
                initDir = Vector2.right;

            // 三相速度运行态（仅按“时间”字段）
            mv.rtCurrentSpeed = 0f;
            mv.rtCruiseTimer = Mathf.Max(0f, mv.moveDuration);
            mv.rtAccelTimer = Mathf.Max(0f, mv.accelerationTime);
            mv.rtDecelTimer = Mathf.Max(0f, mv.decelerationTime);

            bool instantAccel = (mv.accelerationTime <= 0f);
            bool instantDecel = (mv.decelerationTime <= 0f);

            mv.rtStraightPhase = instantAccel
                ? ((mv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (instantDecel ? StraightPhase.Rest : StraightPhase.Decel))
                : StraightPhase.Accel;

            if (instantAccel)
                mv.rtCurrentSpeed = Mathf.Max(0f, mv.moveSpeed);

            float seedSpeed = (mv.rtCurrentSpeed > 0f ? mv.rtCurrentSpeed : Mathf.Max(0.01f, mv.moveSpeed * 0.1f));
            Vector2 initDirNorm = initDir.normalized;
            if (initDirNorm.sqrMagnitude < 0.0001f) initDirNorm = Vector2.right;
            _airVel = initDirNorm * seedSpeed;
            _airLastDir = initDirNorm; // 保存段起点方向
        }

        // 速度标量推进（仅按时间字段）
        float targetSpd = Mathf.Max(0f, mv.moveSpeed);
        float accelRate = (mv.accelerationTime > 0f)
            ? (Mathf.Max(0f, targetSpd) / Mathf.Max(0.0001f, mv.accelerationTime))
            : float.PositiveInfinity;
        float decelRate = (mv.decelerationTime > 0f)
            ? (Mathf.Max(0f, targetSpd) / Mathf.Max(0.0001f, mv.decelerationTime))
            : float.PositiveInfinity;

        switch (mv.rtStraightPhase)
        {
            case StraightPhase.Accel:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation))
                    PlayAnimIfNotCurrent(pCfg.skymoveAnimation);

                if (float.IsPositiveInfinity(accelRate))
                {
                    mv.rtCurrentSpeed = targetSpd;
                    mv.rtStraightPhase = (mv.rtCruiseTimer > 0f)
                        ? StraightPhase.Cruise
                        : (!float.IsPositiveInfinity(decelRate) ? StraightPhase.Decel : StraightPhase.Rest);
                }
                else
                {
                    mv.rtCurrentSpeed = Mathf.MoveTowards(mv.rtCurrentSpeed, targetSpd, accelRate * Time.deltaTime);
                    if (mv.accelerationTime > 0f)
                        mv.rtAccelTimer = Mathf.Max(0f, mv.rtAccelTimer - Time.deltaTime);
                    if (Mathf.Approximately(mv.rtCurrentSpeed, targetSpd) || mv.rtAccelTimer <= 0f)
                        mv.rtStraightPhase = (mv.rtCruiseTimer > 0f)
                            ? StraightPhase.Cruise
                            : (!float.IsPositiveInfinity(decelRate) ? StraightPhase.Decel : StraightPhase.Rest);
                }
                break;

            case StraightPhase.Cruise:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation))
                    PlayAnimIfNotCurrent(pCfg.skymoveAnimation);

                mv.rtCurrentSpeed = targetSpd;
                mv.rtCruiseTimer = Mathf.Max(0f, mv.rtCruiseTimer - Time.deltaTime);
                if (mv.rtCruiseTimer <= 0f)
                    mv.rtStraightPhase = (!float.IsPositiveInfinity(decelRate)) ? StraightPhase.Decel : StraightPhase.Rest;
                break;

            case StraightPhase.Decel:
                if (!string.IsNullOrEmpty(pCfg.skymoveAnimation))
                    PlayAnimIfNotCurrent(pCfg.skymoveAnimation);

                if (float.IsPositiveInfinity(decelRate))
                {
                    mv.rtCurrentSpeed = 0f;
                    mv.rtStraightPhase = StraightPhase.Rest;
                }
                else
                {
                    mv.rtCurrentSpeed = Mathf.MoveTowards(mv.rtCurrentSpeed, 0f, decelRate * Time.deltaTime);
                    if (mv.rtCurrentSpeed <= 0.0001f)
                    {
                        mv.rtCurrentSpeed = 0f;
                        mv.rtStraightPhase = StraightPhase.Rest;
                    }
                }
                break;

            case StraightPhase.Rest:
            default:
                if (!string.IsNullOrEmpty(pCfg.skyrestAnimation))
                    PlayAnimIfNotCurrent(pCfg.skyrestAnimation);

                // 休息计时与切下一段（不做位移）
                mv.rtRestTimer = (mv.rtRestTimer > 0f)
                    ? mv.rtRestTimer - Time.deltaTime
                    : PickStraightRestTime(mv) - Time.deltaTime;

                rb.velocity = Vector2.zero;

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
                return;
        }

        // 仅更新“主速度”的标量；方向在段起点或撞击/出界时由 _airVel 自己维护
        float spd = Mathf.Max(0f, mv.rtCurrentSpeed);

        Vector2 dir;
        switch (pCfg.pathType)
        {
            case AirPatrolPathType.AreaHorizontal:
                {
                    int sx = Mathf.Abs(_airVel.x) > 0.0001f
                        ? (_airVel.x >= 0f ? +1 : -1)
                        : (_airPingPongSign == 0 ? +1 : _airPingPongSign);
                    dir = new Vector2(sx, 0f);
                    break;
                }
            case AirPatrolPathType.AreaVertical:
                {
                    int sy = Mathf.Abs(_airVel.y) > 0.0001f
                        ? (_airVel.y >= 0f ? +1 : -1)
                        : (_airVerticalSign == 0 ? +1 : _airVerticalSign);
                    dir = new Vector2(0f, sy);
                    break;
                }
            case AirPatrolPathType.AreaRandom:
            case AirPatrolPathType.AreaRandomH:
                {
                    if (_airVel.sqrMagnitude > 0.0001f)
                        dir = _airVel.normalized;
                    else
                        dir = (_airLastDir.sqrMagnitude > 0.0001f) ? _airLastDir : Vector2.right;
                    break;
                }
            default:
                dir = (_airVel.sqrMagnitude > 0.0001f) ? _airVel.normalized : (_airLastDir.sqrMagnitude > 0.0001f ? _airLastDir : Vector2.right);
                break;
        }

        _airVel = dir * spd;

        // 更新保存方向（仅在有有效方向时）
        if (dir.sqrMagnitude > 0.0001f)
            _airLastDir = dir;

        // 轴向硬约束
        if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            _airVel = new Vector2(_airVel.x, 0f);
        else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            _airVel = new Vector2(0f, _airVel.y);

        if (!string.IsNullOrEmpty(pCfg.skymoveAnimation))
            PlayAnimIfNotCurrent(pCfg.skymoveAnimation);
    }
    private void AirPatrolPhysicsStep()
    {
        var pCfg = config?.airStageConfig?.patrol;
        if (pCfg == null || pCfg.elements == null || pCfg.elements.Count == 0) return;
        if (_airActiveIndex < 0 || _airActiveIndex >= pCfg.elements.Count) return;

        var elem = pCfg.elements[_airActiveIndex];
        var mv = elem.move;

        bool affectByArea =
            (
                pCfg.pathType == AirPatrolPathType.AreaHorizontal
                || pCfg.pathType == AirPatrolPathType.AreaVertical
                || pCfg.pathType == AirPatrolPathType.AreaRandom
                || pCfg.pathType == AirPatrolPathType.AreaRandomH
            )
            ? true : pCfg.canPassThroughScene;

        // newVel 是“本步结束后的主速度”，最后会写回 _airVel
        Vector2 newVel = _airVel;

        // Sine 偏移：fixedDeltaTime 推进（只是位移偏移，不改主速度）
        _airTime += Time.fixedDeltaTime;
        Vector2 sineDelta = Vector2.zero;
        if (elem.sinEnabled && elem.sinAmplitude > 0f && elem.sinFrequency > 0f)
        {
            float s = Mathf.Sin(_airTime * Mathf.PI * 2f * elem.sinFrequency) * elem.sinAmplitude;

            Vector2 axis;
            if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            {
                // 水平主运动：正弦沿竖直方向摆动（上下）
                axis = Vector2.up;
            }
            else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            {
                // 垂直主运动：正弦沿水平方向摆动（左右）
                axis = Vector2.right;
            }
            else
            {
                // Random / RandomH：正弦沿当前运动方向的法线（蛇形）
                Vector2 d = (_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);
                axis = new Vector2(-d.y, d.x); // 计算法线
            }

            sineDelta = axis.normalized * (s * Time.fixedDeltaTime);
        }

        // 以 BoxCollider2D 中心为锚点，保持区域/碰撞一致
        Vector2 anchor;
        if (col != null)
            anchor = col.bounds.center;               // 区域反弹点：BoxCollider2D 中心
        else if (monsterDistPoint)
            anchor = monsterDistPoint.position;
        else
            anchor = rb.position;

        Vector2 rootToAnchor = rb.position - anchor;

        Rect area = new Rect(elem.areaCenter - elem.areaSize * 0.5f, elem.areaSize);

        // 若当前 anchor 已经跑出区域，先夹回区域内
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

        // 本物理步计划位移（根位置 = 主速度位移 + 正弦偏移）
        Vector2 posRB = rb.position;
        Vector2 plannedDeltaRB = _airVel * Time.fixedDeltaTime + sineDelta;
        Vector2 nextRB = posRB + plannedDeltaRB;

        // 场景碰撞（Collider2D.Cast）
        if (col != null)
        {
            Vector2 castDirScene;
            float castDistScene;

            if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            {
                // 场景：只沿 X 轴做碰撞投射
                float sx = Mathf.Sign(_airVel.x == 0f ? 1f : _airVel.x);
                castDirScene = new Vector2(sx, 0f);
                castDistScene = Mathf.Abs(_airVel.x) * Time.fixedDeltaTime;
            }
            else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            {
                // 场景：只沿 Y 轴做碰撞投射
                float sy = Mathf.Sign(_airVel.y == 0f ? -1f : _airVel.y);
                castDirScene = new Vector2(0f, sy);
                castDistScene = Mathf.Abs(_airVel.y) * Time.fixedDeltaTime;
            }
            else
            {
                // Random / RandomH：按整体位移方向投射
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
                    if (h.collider.CompareTag(autoJumpPermitTag)) continue;
                    if (h.collider.CompareTag("Player")) continue;
                    if (player && h.collider.transform.root == player.root) continue;

                    if (h.distance < bestD)
                    {
                        bestD = h.distance;
                        best = h;
                    }
                }

                if (best.collider)
                {
                    const float SKIN = 0.005f;
                    float allow = Mathf.Max(0f, best.distance - SKIN);

                    if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
                    {
                        // 场景：水平立即反向（不做斜向反射）
                        float speed = Mathf.Max(0f, _airVel.magnitude);
                        int newSign = -(_airVel.x >= 0f ? +1 : -1);

                        nextRB = posRB + castDirScene * allow + new Vector2(newSign, 0f) * 0.01f;
                        newVel = new Vector2(newSign, 0f) * speed;

                        _airPingPongSign = newSign;
                        ForceFaceSign(newSign);

                        if (newVel.sqrMagnitude > 0.0001f) _airLastDir = newVel.normalized;
                    }
                    else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
                    {
                        // 场景：竖直立即反向（不做斜向反射）
                        float speed = Mathf.Max(0f, _airVel.magnitude);
                        int newSignY = -(_airVel.y >= 0f ? +1 : -1);

                        nextRB = posRB + castDirScene * allow + Vector2.down * 0.01f;
                        newVel = new Vector2(0f, newSignY) * speed;
                        _airVerticalSign = newSignY;
                        if (newVel.sqrMagnitude > 0.0001f) _airLastDir = newVel.normalized;
                    }
                    else
                    {
                        // 场景：Random / RandomH 仍按法线做向量反射
                        Vector2 n = best.normal.normalized;
                        float inAng = Mathf.Atan2(_airVel.y, _airVel.x);
                        Vector2 t = new Vector2(-n.y, n.x);
                        float tAng = Mathf.Atan2(t.y, t.x);
                        float outAng = 2f * tAng - inAng;
                        Vector2 rDir = new Vector2(Mathf.Cos(outAng), Mathf.Sin(outAng));
                        if (rDir.sqrMagnitude < 0.0001f)
                            rDir = -(_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);

                        float speed = Mathf.Max(_airVel.magnitude, mv.rtCurrentSpeed);
                        newVel = rDir.normalized * speed;
                        if (newVel.sqrMagnitude > 0.0001f) _airLastDir = rDir.normalized;

                        nextRB = posRB + castDirScene * allow + rDir.normalized * 0.01f;
                    }
                }
            }
        }

        // 区域边界反弹（用 BoxCollider2D 中心 anchor）
        Vector2 nextAnchor = nextRB - rootToAnchor;

        if (affectByArea)
        {
            bool outside = !area.Contains(nextAnchor);
            bool touchLeft = nextAnchor.x <= area.xMin + 0.0001f;
            bool touchRight = nextAnchor.x >= area.xMax - 0.0001f;
            bool touchBottom = nextAnchor.y <= area.yMin + 0.0001f;
            bool touchTop = nextAnchor.y >= area.yMax - 0.0001f;

            if (outside || touchLeft || touchRight || touchBottom || touchTop)
            {
                bool hitH = (touchLeft || touchRight);
                bool hitV = (touchTop || touchBottom);

                if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
                {
                    // 只在撞到“左右边”时才反向；撞到“顶/底”不改水平速度与朝向
                    if (hitH)
                    {
                        float speed = Mathf.Max(0f, newVel.magnitude);
                        int newSign = -((newVel.x >= 0f) ? +1 : -1);
                        newVel = new Vector2(newSign, 0f) * speed;
                        _airPingPongSign = newSign;
                        ForceFaceSign(newSign);
                    }
                }
                else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
                {
                    // 只在撞到“顶/底”时才反向；撞到“左/右”不改竖直速度
                    if (hitV)
                    {
                        float speed = Mathf.Max(0f, newVel.magnitude);
                        int newSignY = -((newVel.y >= 0f) ? +1 : -1);
                        newVel = new Vector2(0f, newSignY) * speed;
                        _airVerticalSign = newSignY;
                    }
                }
                else
                {
                    // AreaRandom / AreaRandomH：按边界法线做向量反射（入射角 = 反射角）
                    Vector2 cen = elem.areaCenter;
                    Vector2 half = elem.areaSize * 0.5f;

                    Vector2 p = nextAnchor;
                    Vector2 q = new Vector2(
                        Mathf.Clamp(p.x, cen.x - half.x, cen.x + half.x),
                        Mathf.Clamp(p.y, cen.y - half.y, cen.y + half.y)
                    );

                    Vector2 nApprox = (p - q);
                    if (nApprox.sqrMagnitude < 0.000001f)
                    {
                        float dxAbs = Mathf.Abs(p.x - cen.x) - half.x;
                        float dyAbs = Mathf.Abs(p.y - cen.y) - half.y;
                        bool overX = dxAbs > 0f;
                        bool overY = dyAbs > 0f;

                        if (overX && overY)
                        {
                            float maxOut = Mathf.Max(Mathf.Abs(dxAbs), Mathf.Abs(dyAbs));
                            if (maxOut > 0f && Mathf.Abs(Mathf.Abs(dxAbs) - Mathf.Abs(dyAbs)) < 0.15f * maxOut)
                                nApprox = new Vector2(Mathf.Sign(p.x - cen.x), Mathf.Sign(p.y - cen.y)).normalized;
                            else
                                nApprox = (Mathf.Abs(dxAbs) > Mathf.Abs(dyAbs))
                                    ? new Vector2(Mathf.Sign(p.x - cen.x), 0f)
                                    : new Vector2(0f, Mathf.Sign(p.y - cen.y));
                        }
                        else
                        {
                            if (overX) nApprox = new Vector2(Mathf.Sign(p.x - cen.x), 0f);
                            else if (overY) nApprox = new Vector2(0f, Mathf.Sign(p.y - cen.y));
                            else nApprox = Vector2.zero;
                        }
                    }

                    if (nApprox == Vector2.zero)
                        nApprox = -(_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);
                    nApprox = nApprox.normalized;

                    float inAng = Mathf.Atan2(newVel.y, newVel.x);
                    Vector2 t = new Vector2(-nApprox.y, nApprox.x);
                    float tAng = Mathf.Atan2(t.y, t.x);
                    float outAng = 2f * tAng - inAng;

                    Vector2 rDir = new Vector2(Mathf.Cos(outAng), Mathf.Sin(outAng));
                    if (rDir.sqrMagnitude < 0.0001f)
                        rDir = -(_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);

                    newVel = rDir.normalized * Mathf.Max(newVel.magnitude, mv.rtCurrentSpeed);
                }

                // 位置夹回区域内（无论命中哪条边都要夹回）
                nextAnchor = new Vector2(
                    Mathf.Clamp(nextAnchor.x, area.xMin, area.xMax),
                    Mathf.Clamp(nextAnchor.y, area.yMin, area.yMax)
                );
                nextRB = nextAnchor + rootToAnchor;
            }
        }

            // 应用位移 + 回写主速度
            rb.MovePosition(nextRB);
        _airVel = newVel;

        // 再统一保证主速度轴向约束（防止数值误差带来微小偏斜）
        if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
            _airVel = new Vector2(_airVel.x, 0f);
        else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
            _airVel = new Vector2(0f, _airVel.y);

        // 朝向：只在有水平分量且需要翻面的模式下改变
        if ((pCfg.pathType == AirPatrolPathType.AreaHorizontal
             || pCfg.pathType == AirPatrolPathType.AreaRandom
             || pCfg.pathType == AirPatrolPathType.AreaRandomH)
            && Mathf.Abs(_airVel.x) > 0.0005f)
        {
            // 朝向只看水平分量：x 改变正负时才左右翻面；纯竖直反弹不改变朝向
            ForceFaceSign(_airVel.x >= 0f ? +1 : -1);
        }

        // 同步水平/垂直符号到当前速度方向，保证下一段 initDir 沿用最新方向
        if (pCfg.pathType == AirPatrolPathType.AreaHorizontal && Mathf.Abs(_airVel.x) > 0.0005f)
            _airPingPongSign = (_airVel.x >= 0f) ? +1 : -1;
        else if (pCfg.pathType == AirPatrolPathType.AreaVertical && Mathf.Abs(_airVel.y) > 0.0005f)
            _airVerticalSign = (_airVel.y >= 0f) ? +1 : -1;

        // 自转（fixedDeltaTime）
        if (state == MonsterState.Air && pCfg.selfRotate)
        {
            float deg = pCfg.selfRotateSpeedDeg * Time.fixedDeltaTime;

            Vector3 pivotW = transform.position;
            if (col != null) pivotW = col.bounds.center;
            else if (monsterDistPoint) pivotW = monsterDistPoint.position;

            Transform t = animator ? animator.transform : transform;

            Vector3 keepRootPos = transform.position;
            bool rotatingRoot = (t == transform);

            if (pCfg.selfRotateX) t.RotateAround(pivotW, t.right, deg);
            if (pCfg.selfRotateY) t.RotateAround(pivotW, t.up, deg);
            if (pCfg.selfRotateZ) t.RotateAround(pivotW, t.forward, deg);

            if (rotatingRoot) transform.position = keepRootPos;
        }
    }

    // 空中移动特效（由动画事件触发）
    public void OnFxSkyMove()
    {
        if (state != MonsterState.Air) return;

        var pCfg = config?.airStageConfig?.patrol;
        if (pCfg == null) return;
        if (pCfg.skymoveEffectPrefab == null) return;
        // 去重：同一“线性段”只播一次
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
