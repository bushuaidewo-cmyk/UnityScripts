using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    // ===== 空中发现运行态 =====
    private bool _airDiscSetupDone = false;
    private List<int> _airDiscOrder = null;
    private int _airDiscOrderPos = 0;
    private int _airDiscActiveIndex = 0;

    private Vector2 _airDiscVel = Vector2.zero;     // 当前发现阶段主速度
    private Vector2 _airDiscLastDir = Vector2.right; // 上一帧的方向（用于Retreat反向基准）
    private float _airDiscHomingTimer = 0f;

    // 空中发现阶段：后退/倒退距离周期性屏蔽（独立于地面 suppressionPhaseTimer）
    private bool _airBackSuppressed = false;
    private float _airBackSuppTimer = 0f;

    // 空中发现统一扩展：滞回 / 驻留 / 面向驻留 / 压制枚举
    // 复用地面发现的 bandHysteresis 与 bandMinDwellTime（不再单独序列化空中版本）
    private float _airBandDwellTimer = 0f;        // 空中档位当前驻留剩余时间
    private float _airFaceFlipDwellTimer = 0f;    // 空中面向翻转驻留计时（复用 faceFlipMinDwellTime 的时长）

    private enum AirBackSuppPhase { Normal, Suppressed }
    private AirBackSuppPhase _airBackSuppPhase = AirBackSuppPhase.Normal;

    // 空中发现：压制期原始距离缓存（方案U2显式归零后恢复）
    private float _airReverseRangeOriginal = -1f;
    private float _airBackRangeOriginal = -1f;

    // 归位逻辑标记
    private bool _airIsReturningToCenter = false;

    // 状态机：空中发现
    private enum AirDiscState { Follow, Retreat, Backstep }
    private AirDiscState _airDiscState = AirDiscState.Follow;
    private float _airDiscStateTimer = 0f; // 状态驻留计时或屏蔽计时复
    
    // 空中发现阶段：四相运行态（仿巡逻）
    private enum AirDiscPhase { Accel, Cruise, Decel, Rest }
    private AirDiscPhase _airDiscPhase = AirDiscPhase.Accel;
    private float _airDiscCurrentSpeed = 0f;
    private float _airDiscAccelTimer = 0f;
    private float _airDiscCruiseTimer = 0f;
    private float _airDiscDecelTimer = 0f;
    private float _airDiscRestTimer = 0f;

    // ===== 空中发现攻击运行态字段（新增，不影响地面） =====
    private List<int> _skyAttackOrder = null;
    private int _skyAttackOrderPos = 0;
    private bool _skyInAttack = false;
    private AirAttackEvent _activeSkyAttack = null;
    private float _skyAttackTimer = 0f;
    private float _skyAttackRestCooldown = 0f;
    private AttackType _skyAttackExecType = AttackType.Melee;
    private int _skyAttackCyclesDone = 0;
    private int _skyAttackCyclesPlanned = 1;
    private bool _skyRangedFiredThisAttack = false;
    private bool _skyRangedFiredInCycle = false;
    private PatrolMovement _skyAttackMoveRuntime = null;
   
    // 到达玩家近距离后，本次攻击期间停止跟踪移动的标记
    private bool _skyAttackReachedPlayer = false;

    // 攻击跟踪频率计时器（Hz -> 秒）
    private float _skyAttackHomingTimer = 0f;

    // 空中攻击范围原始缓存（休息期置零后恢复）
    private float _skyMeleeRangeOriginal = -1f;
    private float _skyRangedRangeOriginal = -1f;

    // 空中攻击近距离面向锁定符号：进入攻击确定，扩展死区内保持以避免左右闪抖
    private int _skyAttackFaceLockSign = 0;

    // 辅助获取当前使用的参数组
    private AirMoveParams GetCurrentAirMoveParams()
    {
        var cfg = config?.airStageConfig?.discovery;
        if (cfg == null || cfg.elements == null || cfg.elements.Count == 0) return null;
        var elem = cfg.elements[Mathf.Clamp(_airDiscActiveIndex, 0, cfg.elements.Count - 1)];
        return (_airDiscState == AirDiscState.Follow) ? elem.follow : elem.backstep;
    }
    // MonsterController.AirPhase.cs 片段：完整的 AirDiscoveryUpdate 方法（已加入：进入阶段强制重置档位；离开阶段恢复范围；保留原逻辑其它部分不变）
    private void AirDiscoveryUpdate()
    {
        // ========== 1. 初始化（仅第一次进入空中发现阶段） ==========
        if (!_airDiscSetupDone)
        {
            _airDiscSetupDone = true;
            _airDiscVel = Vector2.zero;
            _airIsReturningToCenter = false;

            // 构建元素顺序
            var dcfgInit = config?.airStageConfig?.discovery;
            _airDiscOrder = new List<int>();
            int nInit = (dcfgInit != null && dcfgInit.elements != null) ? dcfgInit.elements.Count : 0;
            for (int i = 0; i < nInit; i++) _airDiscOrder.Add(i);
            if (dcfgInit != null && dcfgInit.findRandomOrder && nInit > 1) Shuffle(_airDiscOrder);
            _airDiscActiveIndex = (nInit > 0) ? _airDiscOrder[0] : 0;
            // 使用顺位的第一个元素初始化运行态
            var pInit = GetCurrentAirMoveParams();
            if (pInit != null)
            {
                _airDiscCurrentSpeed = 0f;
                _airDiscAccelTimer = Mathf.Max(0f, pInit.accelerationTime);
                _airDiscCruiseTimer = Mathf.Max(0f, pInit.moveDuration);
                _airDiscDecelTimer = Mathf.Max(0f, pInit.decelerationTime);
                _airDiscRestTimer = 0f;

                bool instantA = (pInit.accelerationTime <= 0f && pInit.acceleration <= 0f);
                bool instantD = (pInit.decelerationTime <= 0f && pInit.deceleration <= 0f);
                _airDiscPhase = instantA
                    ? (_airDiscCruiseTimer > 0f
                        ? AirDiscPhase.Cruise
                        : (instantD ? AirDiscPhase.Rest : AirDiscPhase.Decel))
                    : AirDiscPhase.Accel;
                if (instantA)
                    _airDiscCurrentSpeed = Mathf.Max(0f, pInit.moveSpeed);

                _airMoveFxPlayedThisSegment = false;
                _airRestFxPlayedThisRest = false;
            }

            // 周期性后退屏蔽初始化
            if (dcfgInit != null && dcfgInit.backDCTMax > 0f)
            {
                _airBackSuppressed = false;
                _airBackSuppTimer = Random.Range(dcfgInit.backDCTMin, dcfgInit.backDCTMax);
            }

            // 进入空中发现强制档位与驻留归零，避免残留上一次离开时的 Backstep/Retreat
            _airDiscState = AirDiscState.Follow;
            _airBandDwellTimer = 0f;
            _airFaceFlipDwellTimer = 0f;

            // 首帧哨兵：立即按玩家左右翻面（_airDiscStateTimer 设为 -1 在旋转函数里消费）
            Vector2 initMyPos = GetMonsterDistPos();
            Vector2 initTargetPos = GetPlayerDistPos();
            int faceInit = (initTargetPos.x - initMyPos.x >= 0f) ? +1 : -1;
            ForceFaceSign(faceInit);
            _airDiscStateTimer = -1f;
        }

        // ========== 2. 距离与退出判定 ==========
        Vector2 myPos = GetMonsterDistPos();
        Vector2 targetPos = GetPlayerDistPos();

        float dist = discoveryUseHorizontalDistanceOnly
            ? Mathf.Abs(targetPos.x - myPos.x)
            : Vector2.Distance(myPos, targetPos);

        var discCfg = config.airStageConfig.discovery;
        if (discCfg == null)
        {
            // 配置缺失：回到空中巡逻
            state = MonsterState.Air;
            _airDiscSetupDone = false;
            return;
        }

        // 超出发现范围：退出空中发现 → 回到空中巡逻
        if (dist > discCfg.findRange)
        {
            // 攻击中断（如果正在空中攻击）
            if (_skyInAttack)
            {
                _skyInAttack = false;
                _skyAttackMoveRuntime = null;
                _activeSkyAttack = null;
            }

            state = MonsterState.Air;

            // 离开发现：恢复可能被压制的范围（若之前缓存）
            if (_airReverseRangeOriginal >= 0f)
            {
                discCfg.reverseRange = _airReverseRangeOriginal;
                _airReverseRangeOriginal = -1f;
            }
            if (_airBackRangeOriginal >= 0f)
            {
                discCfg.backRange = _airBackRangeOriginal;
                _airBackRangeOriginal = -1f;
            }
            _airBackSuppPhase = AirBackSuppPhase.Normal;
            _airBackSuppressed = false;

            // 归位准备（保持你原有逻辑）
            _airIsReturningToCenter = true;
            _airDiscSetupDone = false;

            // Blend 回巡逻动画
            var pCfgPatrol = config?.airStageConfig?.patrol;
            if (pCfgPatrol != null && animator)
            {
                string targetAnim = null;
                if (!string.IsNullOrEmpty(pCfgPatrol.skymoveAnimation))
                    targetAnim = pCfgPatrol.skymoveAnimation;
                else if (!string.IsNullOrEmpty(pCfgPatrol.skyrestAnimation))
                    targetAnim = pCfgPatrol.skyrestAnimation;

                if (!string.IsNullOrEmpty(targetAnim))
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (!info.IsName(targetAnim))
                    {
                        animator.CrossFadeInFixedTime(targetAnim, 0.12f, 0, 0f);
                        animator.Update(0f);
                    }
                }
            }

            // 初始化巡逻归位的种子速度（保留原逻辑）
            var pCfgRet = config?.airStageConfig?.patrol;
            if (pCfgRet != null && pCfgRet.elements != null && pCfgRet.elements.Count > 0)
            {
                var mvRet = pCfgRet.elements[0].move;
                mvRet.rtStraightPhase = StraightPhase.None;
                mvRet.rtCurrentSpeed = 0f;
                mvRet.rtAccelTimer = mvRet.rtCruiseTimer = mvRet.rtDecelTimer =
                    mvRet.rtRestTimer = mvRet.rtMoveTimer = 0f;

                float seedSpeed = (mvRet.accelerationTime <= 0f && mvRet.acceleration <= 0f)
                    ? Mathf.Max(0f, mvRet.moveSpeed)
                    : Mathf.Max(0.01f, mvRet.moveSpeed * 0.5f);
                Vector2 dirToCenter = (pCfgRet.elements[0].areaCenter - rb.position).normalized;
                if (dirToCenter.sqrMagnitude < 0.0001f) dirToCenter = Vector2.right;
                _airVel = dirToCenter * seedSpeed;
                _airLastDir = dirToCenter;
            }
            return;
        }

        // ========== 3. 计时器递减 ==========
        if (_airBandDwellTimer > 0f) _airBandDwellTimer -= Time.deltaTime;
        if (_airFaceFlipDwellTimer > 0f) _airFaceFlipDwellTimer -= Time.deltaTime;

        // ========== 4. 周期性后退屏蔽状态机 ==========
        if (discCfg.backDCTMax > 0f)
        {
            _airBackSuppTimer -= Time.deltaTime;
            if (_airBackSuppTimer <= 0f)
            {
                _airBackSuppPhase = (_airBackSuppPhase == AirBackSuppPhase.Normal)
                    ? AirBackSuppPhase.Suppressed
                    : AirBackSuppPhase.Normal;

                _airBackSuppressed = (_airBackSuppPhase == AirBackSuppPhase.Suppressed);

                if (_airBackSuppPhase == AirBackSuppPhase.Suppressed)
                {
                    if (_airReverseRangeOriginal < 0f) _airReverseRangeOriginal = discCfg.reverseRange;
                    if (_airBackRangeOriginal < 0f) _airBackRangeOriginal = discCfg.backRange;
                    discCfg.reverseRange = 0f;
                    discCfg.backRange = 0f;
                }
                else
                {
                    if (_airReverseRangeOriginal >= 0f)
                        discCfg.reverseRange = _airReverseRangeOriginal;
                    if (_airBackRangeOriginal >= 0f)
                        discCfg.backRange = _airBackRangeOriginal;
                    _airReverseRangeOriginal = -1f;
                    _airBackRangeOriginal = -1f;
                }
                _airBackSuppTimer = Random.Range(discCfg.backDCTMin, discCfg.backDCTMax);
            }
        }

        // ========== 5. 档位判定（原逻辑：基于当前状态 + 滞回） ==========
        AirDiscState targetState = AirDiscState.Follow;

        float backR = Mathf.Max(0f, discCfg.backRange);
        float reverseR = Mathf.Max(0f, discCfg.reverseRange);
        float backOut = backR + Mathf.Max(0f, bandHysteresis);
        float reverseOut = reverseR + Mathf.Max(0f, bandHysteresis);

        switch (_airDiscState)
        {
            case AirDiscState.Follow:
                if (backR > 0f && dist <= backR) targetState = AirDiscState.Backstep;
                else if (reverseR > 0f && dist <= reverseR) targetState = AirDiscState.Retreat;
                else targetState = AirDiscState.Follow;
                break;

            case AirDiscState.Retreat:
                if (backR > 0f && dist <= backR) targetState = AirDiscState.Backstep;
                else if (dist >= reverseOut) targetState = AirDiscState.Follow;
                else targetState = AirDiscState.Retreat;
                break;

            case AirDiscState.Backstep:
                if (dist >= backOut)
                {
                    if (reverseR > 0f && dist <= reverseR) targetState = AirDiscState.Retreat;
                    else targetState = AirDiscState.Follow;
                }
                else
                {
                    targetState = AirDiscState.Backstep;
                }
                break;

            default:
                targetState = AirDiscState.Follow;
                break;
        }

        // 最小驻留期：禁止切档
        if (_airBandDwellTimer > 0f)
            targetState = _airDiscState;

        // ========== 6. 档位切换处理 ==========
        if (targetState != _airDiscState)
        {
            if (_airDiscState == AirDiscState.Follow)
                _airDiscLastDir = (_airDiscVel.sqrMagnitude > 0.01f)
                    ? _airDiscVel.normalized
                    : (targetPos - myPos).normalized;

            _airDiscState = targetState;
            _airBandDwellTimer = Mathf.Max(0.05f, bandMinDwellTime);

            _airMoveFxPlayedThisSegment = false;
            _airRestFxPlayedThisRest = false;

            var p = GetCurrentAirMoveParams();
            if (p != null)
            {
                _airDiscCurrentSpeed = 0f;
                _airDiscAccelTimer = Mathf.Max(0f, p.accelerationTime);
                _airDiscCruiseTimer = Mathf.Max(0f, p.moveDuration);
                _airDiscDecelTimer = Mathf.Max(0f, p.decelerationTime);
                _airDiscRestTimer = 0f;

                bool instantAccel = (p.accelerationTime <= 0f && p.acceleration <= 0f);
                bool instantDecel = (p.decelerationTime <= 0f && p.deceleration <= 0f);
                _airDiscPhase = instantAccel
                    ? (_airDiscCruiseTimer > 0f
                        ? AirDiscPhase.Cruise
                        : (instantDecel ? AirDiscPhase.Rest : AirDiscPhase.Decel))
                    : AirDiscPhase.Accel;
                if (instantAccel)
                    _airDiscCurrentSpeed = Mathf.Max(0f, p.moveSpeed);
            }
        }

        // ========== 空中发现攻击判定与更新 ==========
        HandleSkyAttack(dist, myPos, targetPos, discCfg);

        if (_skyInAttack)
        {
            UpdateSkyAttack(myPos, targetPos);
            return; // 攻击期间不执行后续“空中发现移动”推进
        }

        // ========== 7. 物理与时间推进 ==========
        _airDiscStateTimer += Time.deltaTime;
    }
    private void AirDiscoveryPhysicsStep(Vector2 myPos, Vector2 targetPos, AirDiscoveryConfig cfg)
    {
        // 固定时间步
        float dt = Time.fixedDeltaTime;

        // ===== 1. 基础局部变量（默认值） =====
        float p_moveSpeed = 3f;
        float p_accel = 5f;
 
        // ===== 2. 读取当前元素参数 =====
        if (cfg.elements != null && cfg.elements.Count > 0)
        {
            int idx = Mathf.Clamp(_airDiscActiveIndex, 0, cfg.elements.Count - 1);
            var elem = cfg.elements[idx];

            if (_airDiscState == AirDiscState.Follow)
            {
                var f = elem.follow;
                p_moveSpeed = f.moveSpeed;
                p_accel = f.acceleration;

                bool doHoming = f.homingFrequency > 0f && f.homingStrength > 0f;
                Vector2 toPlayer = (targetPos - myPos).normalized;

                float seedSpd = (f.accelerationTime <= 0f && f.acceleration <= 0f)
                    ? Mathf.Max(0f, p_moveSpeed)
                    : (_airDiscCurrentSpeed > 0f
                        ? _airDiscCurrentSpeed
                        : Mathf.Max(0.01f, p_moveSpeed * 0.5f));

                if (_airDiscVel.sqrMagnitude < 0.001f)
                {
                    if (doHoming)
                    {
                        _airDiscVel = toPlayer * seedSpd;
                    }
                    else
                    {
                        // 未启用 Homing：用玩家方向初始化速度，并立刻同步面向，避免背对玩家
                        float dx = targetPos.x - myPos.x;
                        int playerDir = (Mathf.Abs(dx) <= faceFlipDeadZone)
                            ? FacingSign()
                            : (dx >= 0f ? +1 : -1);
                        if (playerDir == 0) playerDir = 1;
                        ForceFaceSign(playerDir);
                        _airDiscVel = new Vector2(playerDir, 0f) * seedSpd;
                        _airDiscLastDir = _airDiscVel.normalized;
                    }
                }

                if (doHoming)
                {
                    _airDiscHomingTimer -= dt;
                    if (_airDiscHomingTimer <= 0f)
                    {
                        _airDiscHomingTimer = 1f / f.homingFrequency;
                        Vector2 currentDir = _airDiscVel.normalized;
                        if (currentDir.sqrMagnitude < 0.001f) currentDir = toPlayer;
                        Vector2 newDir = Vector2.Lerp(currentDir, toPlayer, f.homingStrength);
                        if (newDir.sqrMagnitude < 0.0001f) newDir = currentDir;
                        float useMag = Mathf.Max(_airDiscCurrentSpeed, _airDiscVel.magnitude);
                        _airDiscVel = newDir.normalized * useMag;
                    }
                }
                else
                {
                    if (Mathf.Abs(_airDiscVel.x) < 0.0001f)
                    {
                        int face = FacingSign();
                        if (face == 0) face = 1;
                        _airDiscVel = new Vector2(face, 0f) * Mathf.Max(seedSpd, _airDiscVel.magnitude);
                    }
                    _airDiscVel = new Vector2(_airDiscVel.x, 0f);
                }
            }
            else
            {
                var b = elem.backstep;
                p_moveSpeed = b.moveSpeed;
                p_accel = b.acceleration;
            }
        }

        // ===== 3. 期望方向 =====
        Vector2 wantDir = (_airDiscState == AirDiscState.Follow)
            ? ((_airDiscVel.sqrMagnitude > 0.0001f) ? _airDiscVel.normalized : (targetPos - myPos).normalized)
            : -_airDiscLastDir;

        // ===== 4. 四相速度推进 =====
        var mp = GetCurrentAirMoveParams();
        float tgtSpeed = (mp != null) ? Mathf.Max(0f, mp.moveSpeed) : 0f;

        float accelRate = 0f;
        float decelRate = 0f;
        if (mp != null)
        {
            accelRate = (mp.accelerationTime > 0f)
                ? (tgtSpeed / Mathf.Max(0.0001f, mp.accelerationTime))
                : Mathf.Max(0f, mp.acceleration);
            decelRate = (mp.decelerationTime > 0f)
                ? (tgtSpeed / Mathf.Max(0.0001f, mp.decelerationTime))
                : Mathf.Max(0f, mp.deceleration);
        }

        switch (_airDiscPhase)
        {
            case AirDiscPhase.Accel:
                if (accelRate <= 0f)
                {
                    _airDiscCurrentSpeed = tgtSpeed;
                    _airDiscPhase = (_airDiscCruiseTimer > 0f) ? AirDiscPhase.Cruise
                        : (decelRate > 0f ? AirDiscPhase.Decel : AirDiscPhase.Rest);
                }
                else
                {
                    _airDiscCurrentSpeed = Mathf.MoveTowards(_airDiscCurrentSpeed, tgtSpeed, accelRate * dt);
                    if (mp != null && mp.accelerationTime > 0f)
                        _airDiscAccelTimer = Mathf.Max(0f, _airDiscAccelTimer - dt);
                    if (Mathf.Approximately(_airDiscCurrentSpeed, tgtSpeed) || _airDiscAccelTimer <= 0f)
                        _airDiscPhase = (_airDiscCruiseTimer > 0f) ? AirDiscPhase.Cruise
                            : (decelRate > 0f ? AirDiscPhase.Decel : AirDiscPhase.Rest);
                }
                break;

            case AirDiscPhase.Cruise:
                _airDiscCurrentSpeed = tgtSpeed;
                _airDiscCruiseTimer = Mathf.Max(0f, _airDiscCruiseTimer - dt);
                if (_airDiscCruiseTimer <= 0f)
                    _airDiscPhase = (decelRate > 0f) ? AirDiscPhase.Decel : AirDiscPhase.Rest;
                break;

            case AirDiscPhase.Decel:
                if (decelRate <= 0f)
                {
                    _airDiscCurrentSpeed = 0f;
                    _airDiscPhase = AirDiscPhase.Rest;
                }
                else
                {
                    _airDiscCurrentSpeed = Mathf.MoveTowards(_airDiscCurrentSpeed, 0f, decelRate * dt);
                    if (mp != null && mp.decelerationTime > 0f)
                        _airDiscDecelTimer = Mathf.Max(0f, _airDiscDecelTimer - dt);
                    if (_airDiscCurrentSpeed <= 0.0001f)
                    {
                        _airDiscCurrentSpeed = 0f;
                        _airDiscPhase = AirDiscPhase.Rest;
                    }
                }
                break;

            case AirDiscPhase.Rest:
            default:
                if (mp != null)
                {
                    if (_airDiscRestTimer <= 0f)
                    {
                        float rMin = Mathf.Max(0f, mp.restMin);
                        float rMax = Mathf.Max(rMin, mp.restMax);
                        _airDiscRestTimer = (rMax > 0f) ? Random.Range(rMin, rMax) : 0f;
                        if (_airDiscRestTimer <= 0f)
                        {
                            // 休息区间也是 0：直接进入下一元素
                            if (_airDiscOrder.Count == 1)
                            {
                                _airDiscActiveIndex = _airDiscOrder[0];
                                _airDiscOrderPos = 0;
                            }
                            else
                            {
                                bool wasZero = (_airDiscActiveIndex == _airDiscOrder[0]);
                                _airDiscActiveIndex = wasZero ? _airDiscOrder[1] : _airDiscOrder[0];
                                _airDiscOrderPos = wasZero ? 1 : 0;
                            }
                            mp = GetCurrentAirMoveParams();
                            _airDiscPhase = AirDiscPhase.Accel;
                            _airDiscCurrentSpeed = 0f;
                            _airDiscAccelTimer = Mathf.Max(0f, mp?.accelerationTime ?? 0f);
                            _airDiscCruiseTimer = Mathf.Max(0f, mp?.moveDuration ?? 0f);
                            _airDiscDecelTimer = Mathf.Max(0f, mp?.decelerationTime ?? 0f);
                            _airMoveFxPlayedThisSegment = false;
                            _airRestFxPlayedThisRest = false;
                            break;
                        }
                    }
                    _airDiscRestTimer = Mathf.Max(0f, _airDiscRestTimer - dt);
                    _airDiscCurrentSpeed = 0f;
                    if (_airDiscRestTimer <= 0f)
                    {
                        if (_airDiscOrder.Count == 1)
                        {
                            _airDiscActiveIndex = _airDiscOrder[0];
                            _airDiscOrderPos = 0;
                        }
                        else
                        {
                            bool wasZero = (_airDiscActiveIndex == _airDiscOrder[0]);
                            _airDiscActiveIndex = wasZero ? _airDiscOrder[1] : _airDiscOrder[0];
                            _airDiscOrderPos = wasZero ? 1 : 0;
                        }
                        mp = GetCurrentAirMoveParams();
                        _airDiscPhase = AirDiscPhase.Accel;
                        _airDiscCurrentSpeed = 0f;
                        _airDiscAccelTimer = Mathf.Max(0f, mp?.accelerationTime ?? 0f);
                        _airDiscCruiseTimer = Mathf.Max(0f, mp?.moveDuration ?? 0f);
                        _airDiscDecelTimer = Mathf.Max(0f, mp?.decelerationTime ?? 0f);
                        _airMoveFxPlayedThisSegment = false;
                        _airRestFxPlayedThisRest = false;
                    }
                }
                break;
        }

        // 动画决定（延后）
        if (mp != null)
        {
            if (_airDiscState == AirDiscState.Follow || _airDiscState == AirDiscState.Retreat)
            {
                if (_airDiscPhase == AirDiscPhase.Rest)
                {
                    if (!string.IsNullOrEmpty(cfg.followRestAnimation))
                        PlayAnimIfNotCurrent(cfg.followRestAnimation);
                    _airMoveFxPlayedThisSegment = true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(cfg.followMoveAnimation))
                        PlayAnimIfNotCurrent(cfg.followMoveAnimation);
                }
            }
            else
            {
                if (_airDiscPhase == AirDiscPhase.Rest)
                {
                    if (!string.IsNullOrEmpty(cfg.backRestAnimation))
                        PlayAnimIfNotCurrent(cfg.backRestAnimation);
                    _airMoveFxPlayedThisSegment = true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(cfg.backMoveAnimation))
                        PlayAnimIfNotCurrent(cfg.backMoveAnimation);
                }
            }
        }

        if (_airDiscPhase == AirDiscPhase.Rest && _airDiscCurrentSpeed <= 0.0001f)
            _airRestFxPlayedThisRest = false;

        // ===== 5. 更新速度矢量 =====
        _airDiscVel = wantDir.normalized * _airDiscCurrentSpeed;
        _airTime += dt;
        Vector2 sineOffset = Vector2.zero;
        if (cfg.sinEnabled && cfg.sinFrequency > 0f && cfg.sinAmplitude > 0f)
        {
            float s = Mathf.Sin(_airTime * Mathf.PI * 2f * cfg.sinFrequency) * cfg.sinAmplitude;
            Vector2 normal = new Vector2(-wantDir.y, wantDir.x);
            sineOffset = normal * (s * dt);
        }

        // ===== 7. 步进 & 最小分离（已在 Follow 条件下处理分离） =====
        Vector2 step = _airDiscVel * dt + sineOffset;

        // ===== 8. 碰撞检测（固定步） =====
        if (col != null && step.sqrMagnitude > 0f)
        {
            var filter = new ContactFilter2D { useTriggers = false };


            RaycastHit2D[] hits = new RaycastHit2D[1];
            if (col.Cast(step.normalized, filter, hits, step.magnitude) > 0)

            {
                var hit = hits[0];
                if (!hit.collider.isTrigger && !hit.collider.CompareTag("Player"))
                {
                    // 允许推进距离与剩余距离
                    const float SKIN = 0.005f;
                    float allow = Mathf.Max(0f, hit.distance - SKIN);
                    float total = step.magnitude;
                    Vector2 inDir = (total > 0f) ? step.normalized
                                                 : ((_airDiscVel.sqrMagnitude > 0.0001f) ? _airDiscVel.normalized : wantDir);

                    if (cfg.sceneBounceOnHit)
                    {
                        // 反弹：沿反射方向把剩余位移补走，并微偏离表面避免下一帧再次命中
                        Vector2 baseDir = (_airDiscState == AirDiscState.Retreat && _airDiscVel.sqrMagnitude > 0.0001f)
                                          ? _airDiscVel.normalized
                                          : inDir;

                        Vector2 rDir = Vector2.Reflect(baseDir, hit.normal);
                        if (rDir.sqrMagnitude < 0.0001f) rDir = -baseDir;
                        rDir = rDir.normalized;

                        float rem = Mathf.Max(0f, total - allow);
                        float speed = Mathf.Max(_airDiscVel.magnitude, _airDiscCurrentSpeed);

                        // 更新发现阶段“主速度”为反弹方向
                        _airDiscVel = rDir * speed;

                        // 本帧位移 = 贴到表面 + 剩余沿反弹方向 + 极小分离
                        step = inDir * allow + rDir * rem + rDir * 0.01f;

                        // 保留你原有的 lastDir 更新语义
                        if (_airDiscState == AirDiscState.Follow)
                            _airDiscLastDir = baseDir;
                        else if (_airDiscState == AirDiscState.Retreat)
                            _airDiscLastDir = -((_airDiscVel.sqrMagnitude > 0.0001f) ? _airDiscVel.normalized : _airDiscLastDir);
                    }
                    else
                    {
                        // 贴边停止：推进到允许距离并清零速度，避免持续反弹抖动
                        step = (allow > 0f) ? inDir * allow : Vector2.zero;
                        _airDiscVel = Vector2.zero;
                        _airDiscCurrentSpeed = 0f;
                    }
                }
            }

        }

        // ===== 9. 应用位移（固定步） =====
        rb.MovePosition(rb.position + step);

        // ===== 10. 朝向与自转 =====
        HandleAirDiscoveryRotation(myPos, targetPos, cfg);
    }

    // 空中发现阶段物理步进（固定时间步）
    private void AirDiscoveryFixedStep()
    {
        if (state != MonsterState.Discovery) return;
        var discCfg = config?.airStageConfig?.discovery;
        if (discCfg == null) return;

        // 攻击期间跳过位移物理（空中攻击不使用四相物理）
        if (_skyInAttack) return;

        Vector2 myPos = GetMonsterDistPos();
        Vector2 targetPos = GetPlayerDistPos();
        AirDiscoveryPhysicsStep(myPos, targetPos, discCfg);
    }

    // ---------- 新增：空中攻击运行态方法 ----------

    private void HandleSkyAttack(float dist, Vector2 myPos, Vector2 targetPos, AirDiscoveryConfig cfg)
    {
        // 条件：空中独占且配置存在
        if (!(config?.airPhaseConfig?.airPhase == true && config?.airPhaseConfig?.groundPhase == false)) return;
        if (cfg == null || cfg.skyAttacks == null || cfg.skyAttacks.Count == 0) return;
        if (_skyInAttack) return; // 正在攻击
        if (_skyAttackRestCooldown > 0f)
        {
            _skyAttackRestCooldown -= Time.deltaTime;

            // 冷却刚结束：恢复当前攻击配置的原始范围
            if (_skyAttackRestCooldown <= 0f)
            {
                // 找到当前要使用的攻击配置（按顺序位置），这里恢复缓存到配置
                if (_skyMeleeRangeOriginal >= 0f || _skyRangedRangeOriginal >= 0f)
                {
                    // 恢复到最近一次缓存到的攻击事件（若你的攻击是多个事件循环，这里使用下次将要使用的事件在开始前恢复）
                    // 简单就地恢复：如果队列存在下次攻击索引，则恢复其范围；否则恢复上次缓存对象（如果仍引用）
                    // 为最小改动：直接在 discovery.skyAttacks[_skyAttackOrder[_skyAttackOrderPos]] 上恢复
                    var discCfgLocal = config?.airStageConfig?.discovery;
                    if (discCfgLocal != null && discCfgLocal.skyAttacks != null && discCfgLocal.skyAttacks.Count > 0)
                    {
                        int idxRestore = (_skyAttackOrder != null && _skyAttackOrder.Count > 0) ? _skyAttackOrder[_skyAttackOrderPos] : 0;
                        var restoreAttack = discCfgLocal.skyAttacks[Mathf.Clamp(idxRestore, 0, discCfgLocal.skyAttacks.Count - 1)];
                        if (restoreAttack != null)
                        {
                            if (_skyMeleeRangeOriginal >= 0f) restoreAttack.SkyattackMeleeRange = _skyMeleeRangeOriginal;
                            if (_skyRangedRangeOriginal >= 0f) restoreAttack.SkyattackRangedRange = _skyRangedRangeOriginal;
                        }
                    }
                    _skyMeleeRangeOriginal = -1f;
                    _skyRangedRangeOriginal = -1f;
                }
            }

            // 冷却期仍未结束：不发起新攻击
            if (_skyAttackRestCooldown > 0f)
                return;
        }

        BuildSkyAttackOrderIfNeeded(cfg);

        int n = _skyAttackOrder.Count;
        if (n == 0) return;

        int startPos = _skyAttackOrderPos;
        for (int step = 0; step < n; step++)
        {
            int idx = _skyAttackOrder[_skyAttackOrderPos];
            var a = cfg.skyAttacks[idx];
            bool meleeReady = a != null &&
                              a.SkyattackMeleeRange > 0f &&
                              dist <= a.SkyattackMeleeRange &&
                              !string.IsNullOrEmpty(a.SkyattackAnimation) &&
                              a.SkyattackEffectPrefab != null;

            bool rangedReady = a != null &&
                               a.SkyattackRangedRange > 0f &&
                               dist <= a.SkyattackRangedRange &&
                               !string.IsNullOrEmpty(a.SkyattackFarAnimation) &&
                               a.SkyattackFarEffectPrefab != null &&
                               a.Skyprojectile != null;

            if (meleeReady || rangedReady)
            {
                StartSkyAttack(a, meleeReady ? AttackType.Melee : AttackType.Ranged, myPos, targetPos, cfg);
                _skyAttackOrderPos = (_skyAttackOrderPos + 1) % n;
                if (_skyAttackOrderPos == 0 && cfg.skyattacksRandomOrder && n > 1)
                    Shuffle(_skyAttackOrder);
                return;
            }
            _skyAttackOrderPos = (_skyAttackOrderPos + 1) % n;
        }
    }

    private void StartSkyAttack(AirAttackEvent a, AttackType execType, Vector2 myPos, Vector2 targetPos, AirDiscoveryConfig cfg)
    {
        _activeSkyAttack = a;
        _skyAttackExecType = execType;
        _skyInAttack = true;

        _skyAttackTimer = Mathf.Max(0.01f, a.SkyattackDuration);
        _skyAttackCyclesPlanned = Mathf.Max(1, a.SkyrepeatedHitsCount);
        _skyAttackCyclesDone = 0;
        _skyRangedFiredThisAttack = false;
        _skyRangedFiredInCycle = false;

        // 开始攻击：复位“已到达近距离”标记，由 UpdateSkyAttack 在推进过程中再判定
        _skyAttackReachedPlayer = false;

        // Homing 频率计时器重置：首帧即可更新一次
        _skyAttackHomingTimer = 0f;

        // 锁面向
        int faceSign = (targetPos.x - myPos.x >= 0f) ? +1 : -1;

        // 进入空中攻击：取消发现阶段的面向驻留，避免因为驻留死区仍保持背对玩家
        _airFaceFlipDwellTimer = 0f;

        // 初始化攻击面向锁定符号（用于近距离扩展死区防抖）
        _skyAttackFaceLockSign = faceSign;

        ForceFaceSign(faceSign);

        // 播动画
        if (_skyAttackExecType == AttackType.Melee && !string.IsNullOrEmpty(a.SkyattackAnimation))
        {
            animator.CrossFadeInFixedTime(a.SkyattackAnimation, 0f, 0, 0f);
            animator.Update(0f);
        }
        else if (_skyAttackExecType == AttackType.Ranged && !string.IsNullOrEmpty(a.SkyattackFarAnimation))
        {
            animator.CrossFadeInFixedTime(a.SkyattackFarAnimation, 0f, 0, 0f);
            animator.Update(0f);
        }

        // 构建叠加移动
        _skyAttackMoveRuntime = null;

        if (a.SkyattackMotionMode == SkyAttackMotionMode.SkyfollowmoveXY)
        {
            float spd = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackmoveSpeedMelee : a.SkyattackmoveSpeedRanged;
            float acc = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackaccelerationMelee : a.SkyattackaccelerationRanged;
            float accT = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackaccelerationTimeMelee : a.SkyattackaccelerationTimeRanged;
            float dec = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackdecelerationMelee : a.SkyattackdecelerationRanged;
            float decT = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackdecelerationTimeMelee : a.SkyattackdecelerationTimeRanged;
            float dur = (_skyAttackExecType == AttackType.Melee) ? a.SkyattackmoveDurationMelee : a.SkyattackmoveDurationRanged;

            if (spd > 0f && dur > 0f)
            {
                float accelTime = Mathf.Min(accT, dur);
                float decelTime = Mathf.Min(decT, Mathf.Max(0f, dur - accelTime));
                float cruiseTime = Mathf.Max(0f, dur - accelTime - decelTime);

                _skyAttackMoveRuntime = new PatrolMovement
                {
                    type = MovementType.Straight,
                    moveSpeed = spd,
                    acceleration = acc,
                    accelerationTime = accelTime,
                    deceleration = dec,
                    decelerationTime = decelTime,
                    moveDuration = cruiseTime,
                    restMin = 0f,
                    restMax = 0f
                };
                _skyAttackMoveRuntime.rtStraightPhase = StraightPhase.None;
            }
        }
    }

    private void UpdateSkyAttack(Vector2 myPos, Vector2 targetPos)
    {
        if (!_skyInAttack || _activeSkyAttack == null) return;

        // 面向锁定：使用中心点（monsterDistPoint → playerDistPoint），每帧强制朝向玩家
        int faceSign;
        {
            Vector2 myC = GetMonsterDistPos();
            Vector2 plC = GetPlayerDistPos();
            float dxCenter = plC.x - myC.x;

            // 稳定化：在水平死区内保持当前面向，死区外才转向；避免垂直攻击时左右闪烁
            if (Mathf.Abs(dxCenter) <= faceFlipDeadZone)
                faceSign = FacingSign();
            else
                faceSign = (dxCenter >= 0f) ? +1 : -1;

            if (faceSign == 0) faceSign = 1;

            // 攻击期不保留面向驻留，避免驻留造成反向
            _airFaceFlipDwellTimer = 0f;
            _skyAttackFaceLockSign = faceSign;

            ForceFaceSign(faceSign);
        }

        if (_skyAttackReachedPlayer)
        {
            // 1. 已到达近距：只锁面向 + 停止移动，等待动画/计时结束
            faceSign = FacingSign();
            if (faceSign == 0) faceSign = (targetPos.x - myPos.x >= 0f) ? +1 : -1;
            ForceFaceSign(faceSign);

            rb.velocity = Vector2.zero;
        }
        else
        {
            // 2. 未到近距：在攻击中根据与玩家的水平距离对面向做“锁定死区”稳定处理
            float dxToPlayer = targetPos.x - myPos.x;
            float absDx = Mathf.Abs(dxToPlayer);

            // 扩展死区：原死区基础上增加 0.15f，用于攻击跟踪期间锁定面向
            float attackLockDeadZone = faceFlipDeadZone + 0.15f;

            // 保险：若锁定符号未初始化（极少见），按当前玩家方向初始化
            if (_skyAttackFaceLockSign == 0)
                _skyAttackFaceLockSign = (dxToPlayer >= 0f) ? +1 : -1;

            if (absDx <= attackLockDeadZone)
            {
                // 近距离：完全使用锁定面向，不随 dx 微小符号变化抖动
                faceSign = _skyAttackFaceLockSign;
            }
            else
            {
                // 离开扩展死区：允许通过滞回逻辑稳定翻面，并更新锁定符号
                faceSign = StabilizeFaceSign(faceSign, dxToPlayer);
                _skyAttackFaceLockSign = faceSign;
            }

            ForceFaceSign(faceSign);
        }

        // 3. 攻击期间的叠加移动（只有在尚未到达近距且有移动配置时才推进）
        if (_skyAttackMoveRuntime != null && !_skyAttackReachedPlayer)
        {
            // 3.1 先按原有 StraightTickCommon 水平推进
            StraightTickCommon(
                _skyAttackMoveRuntime,
                dirSign: faceSign,
                useWaypoints: false,
                useMoveDirForProbes: true,
                allowTurnOnObstacle: false,
                stopAtCliffEdgeWhenNoTurn: true,
                suppressTurnInAutoJumpZone: true
            );

            // 3.2 SkyfollowmoveXY：按 SkyattackHomingStrength 在水平→XY 跟踪之间插值 (Strength=0 ⇒ 水平)
            if (_activeSkyAttack != null && _activeSkyAttack.SkyattackMotionMode == SkyAttackMotionMode.SkyfollowmoveXY)
            {
                float spd = Mathf.Max(0f, _skyAttackMoveRuntime.rtCurrentSpeed);

                // 频率门控：freq<=0 每帧；>0 按 1/freq 秒刷新
                float freq = Mathf.Max(0f, _activeSkyAttack.SkyattackHomingFrequency);
                bool shouldUpdateDir = (freq <= 0f);
                if (!shouldUpdateDir)
                {
                    _skyAttackHomingTimer -= Time.deltaTime;
                    if (_skyAttackHomingTimer <= 0f)
                    {
                        _skyAttackHomingTimer = 1f / freq;
                        shouldUpdateDir = true;
                    }
                }

                if (shouldUpdateDir)
                {
                    // 地面阶段的作法：水平基向只根据“面向玩家”的符号，而不是速度符号
                    int baseXSign = faceSign;
                    Vector2 horizDir = new Vector2(baseXSign, 0f);

                    Vector2 toPlayer = (targetPos - myPos);
                    Vector2 fullDir = (toPlayer.sqrMagnitude > 0.0001f) ? toPlayer.normalized : horizDir;

                    float s = Mathf.Clamp01(_activeSkyAttack.SkyattackHomingStrength);
                    Vector2 blendedDir =
                        (s <= 0f) ? horizDir :
                        (s >= 1f ? fullDir : Vector2.Lerp(horizDir, fullDir, s)).normalized;

                    // 玩家在脚下时的稳定窗口：完全禁止水平移动，仅按垂直方向移动
                    var dxLocal = toPlayer.x;
                    var dyLocal = toPlayer.y;

                    // “脚下中间”判定：水平在死区内，竖直距离占优
                    const float verticalBias = 0.25f; // Y 优先阈值（局部常量，不改配置）
                    bool playerUnderFoot =
                        Mathf.Abs(dxLocal) <= faceFlipDeadZone &&
                        Mathf.Abs(dyLocal) >= verticalBias;

                    if (playerUnderFoot)
                    {
                        // 纯上下移动，不给任何水平速度，面向只由 faceSign 控制
                        blendedDir = new Vector2(0f, Mathf.Sign(dyLocal));
                        rb.velocity = blendedDir.normalized * spd;
                    }
                    else
                    {
                        // 普通情况：仍按插值结果移动，但在水平死区内保持当前水平符号，避免来回翻向
                        if (Mathf.Abs(dxLocal) <= faceFlipDeadZone && Mathf.Abs(rb.velocity.x) > 0.0001f)
                        {
                            float keepSign = Mathf.Sign(rb.velocity.x);
                            blendedDir.x = keepSign;
                        }

                        rb.velocity = blendedDir.normalized * spd;
                    }
                }
            }

            // 3.3 到达就近阈值后：停止本次攻击期间的跟踪移动，等待下次攻击再恢复
            float dx = targetPos.x - myPos.x;
            float dy = targetPos.y - myPos.y;
            float nearX = Mathf.Max(0.05f, faceFlipDeadZone); // 水平近距离阈值
            const float nearY = 0.12f;                        // 垂直近距离阈值
            const float nearR = 0.25f;                        // 综合半径阈值（XY 模式）

            if (!_skyAttackReachedPlayer)
            {
                bool reached =
                    (_activeSkyAttack.SkyattackMotionMode == SkyAttackMotionMode.SkyfollowmoveXY)
                        ? ((Mathf.Abs(dx) <= nearX && Mathf.Abs(dy) <= nearY) ||
                           ((targetPos - myPos).sqrMagnitude <= (nearR * nearR)))
                        : (Mathf.Abs(dx) <= nearX);

                if (reached)
                {
                    _skyAttackReachedPlayer = true;
                    _skyAttackMoveRuntime = null;    // 停止后续 StraightTickCommon 推进
                    rb.velocity = Vector2.zero;      // 彻底静止，避免残余微速导致抖动
                }
            }
        }
        else
        {
            // 没有叠加移动配置：保持水平静止（只保留可能的竖直速度）
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        // 攻击中跟踪方向插值（仅当叠加移动模式启用 & 未到达近距离）
        if (!_skyAttackReachedPlayer &&
            _skyAttackMoveRuntime != null &&
            _activeSkyAttack.SkyattackMotionMode != SkyAttackMotionMode.SkyfollowmoveXY &&
            _activeSkyAttack.SkyattackHomingFrequency > 0f &&
            _activeSkyAttack.SkyattackHomingStrength > 0f)
        {
            {
                // 简化：每帧粗略插值（改用当前速度的二维方向作为插值基准）
                Vector2 toPlayer = (targetPos - myPos);
                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    toPlayer.Normalize();
                    float s = _activeSkyAttack.SkyattackHomingStrength;

                    // 用当前速度方向（二维）作为基准，若速度几乎为0则直接取指向玩家
                    Vector2 curVel = rb.velocity;
                    Vector2 curDir = (curVel.sqrMagnitude > 0.0001f) ? curVel.normalized : toPlayer;

                    Vector2 newDir = Vector2.Lerp(curDir, toPlayer, s).normalized;

                    // 维持现有速度幅值，避免插值阶段突然加速/减速
                    float speedMag = curVel.magnitude;

                    // 近距离抖动抑制：
                    // 水平在 faceFlipDeadZone 内：保持当前水平符号的速度，不随微小位置改变翻向
                    var dxLocal = toPlayer.x; // 原 dxToPlayer 未在此作用域声明，改为局部 dxLocal
                    if (Mathf.Abs(dxLocal) <= faceFlipDeadZone && Mathf.Abs(curVel.x) > 0.0001f)
                        newDir.x = Mathf.Sign(curVel.x);

                    // 垂直在很小阈值内：清零竖直分量，避免上下抖动
                    float dyToPlayer = targetPos.y - myPos.y;
                    if (Mathf.Abs(dyToPlayer) <= 0.10f)
                        newDir.y = 0f;

                    rb.velocity = newDir.normalized * speedMag;
                }
            }
        }

        // 计时与循环
        _skyAttackTimer -= Time.deltaTime;
        var info = animator.GetCurrentAnimatorStateInfo(0);

        string animName = (_skyAttackExecType == AttackType.Melee) ? _activeSkyAttack.SkyattackAnimation : _activeSkyAttack.SkyattackFarAnimation;
        if (!string.IsNullOrEmpty(animName) && info.IsName(animName) && info.normalizedTime >= 1f)
        {
            if (_skyAttackCyclesDone < _skyAttackCyclesPlanned - 1 && _skyAttackTimer > 0f)
            {
                _skyAttackCyclesDone++;
                animator.Play(animName, 0, 0f);
                animator.Update(0f);
                _skyRangedFiredInCycle = false;
            }
        }

        if (_skyAttackTimer <= 0f)
        {
            EndSkyAttack();
        }
    }

    private void EndSkyAttack()
    {
        if (!_skyInAttack) return;
        _skyInAttack = false;
        _skyAttackMoveRuntime = null;

        // 攻击结束：复位“已到达近距离”标记，避免下个攻击周期卡住
        _skyAttackReachedPlayer = false;

        // 清除面向锁定符号
        _skyAttackFaceLockSign = 0;

        // 在休息期间将当前攻击的检查范围设为 0（并缓存原值）
        if (_activeSkyAttack != null)
        {
            if (_skyMeleeRangeOriginal < 0f) _skyMeleeRangeOriginal = _activeSkyAttack.SkyattackMeleeRange;
            if (_skyRangedRangeOriginal < 0f) _skyRangedRangeOriginal = _activeSkyAttack.SkyattackRangedRange;
        }

        // 休息冷却
        float min = Mathf.Max(0f, _activeSkyAttack.SkyattackRestMin);
        float max = Mathf.Max(min, _activeSkyAttack.SkyattackRestMax);

        // 置零范围（进入休息）
        if (_activeSkyAttack != null)
        {
            _activeSkyAttack.SkyattackMeleeRange = 0f;
            _activeSkyAttack.SkyattackRangedRange = 0f;
        }

        _skyAttackRestCooldown = (max > 0f) ? Random.Range(min, max) : 0f;

        _activeSkyAttack = null;
    }

    private void BuildSkyAttackOrderIfNeeded(AirDiscoveryConfig cfg)
    {
        if (_skyAttackOrder != null && _skyAttackOrder.Count == cfg.skyAttacks.Count) return;
        _skyAttackOrder = new List<int>();
        for (int i = 0; i < cfg.skyAttacks.Count; i++) _skyAttackOrder.Add(i);
        if (cfg.skyattacksRandomOrder && _skyAttackOrder.Count > 1) Shuffle(_skyAttackOrder);
        _skyAttackOrderPos = 0;
    }

    private void HandleAirDiscoveryRotation(Vector2 myPos, Vector2 targetPos, AirDiscoveryConfig cfg)
    {
        // 首帧强制面向哨兵：进入发现当帧已设置 _airDiscStateTimer 为负，执行一次性强制翻面并清零
        if (_airDiscStateTimer < 0f)
        {
            float dx0 = targetPos.x - myPos.x;
            int face0 = (dx0 >= 0f) ? +1 : -1;
            ForceFaceSign(face0);
            _airDiscStateTimer = 0f; // 清除哨兵
        }

        // 朝向逻辑
        int faceDir = 1;

        int dx = (int)Mathf.Sign(targetPos.x - myPos.x);
        float dxToPlayer = targetPos.x - myPos.x;

        // 面向驻留：期间保持当前面向，不随玩家细微位置变化
        if (_airFaceFlipDwellTimer > 0f)
        {
            dx = FacingSign();
            dxToPlayer = (FacingSign() >= 0) ? Mathf.Abs(dxToPlayer) : -Mathf.Abs(dxToPlayer); // 保持符号形式，后续逻辑仍可使用
        }

        if (dx == 0) dx = 1;

        // 跟随/倒退面向玩家；Retreat 背对玩家
        faceDir = (_airDiscState == AirDiscState.Retreat) ? -dx : dx;

        // 水平死区：靠得很近时保持当前朝向（不翻）
        if (Mathf.Abs(dxToPlayer) <= faceFlipDeadZone)
        {
            faceDir = FacingSign();
        }
        else
        {
            faceDir = StabilizeFaceSign(faceDir, dxToPlayer);
        }
        ForceFaceSign(faceDir);
    }

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
        // 如果配置了空中发现，且玩家存在，检测距离
        if (config?.airStageConfig?.discovery != null && player)
        {
            // 使用与地面发现相同的“仅水平距离”选项（discoveryUseHorizontalDistanceOnly），确保高低差不阻碍进入发现
            Vector2 mPos = GetMonsterDistPos();
            Vector2 pPos = GetPlayerDistPos();
            float d = discoveryUseHorizontalDistanceOnly
                ? Mathf.Abs(pPos.x - mPos.x)
                : Vector2.Distance(mPos, pPos);

            // 进入空中发现：立即切换状态并清除归位标记
            if (d <= config.airStageConfig.discovery.findRange)
            {
                state = MonsterState.Discovery;

                _airFaceFlipDwellTimer = faceFlipMinDwellTime; // 复用地面面向驻留时长

                return; // 下一帧由空中发现接管
            }
        }

        // 插入归位逻辑：如果处于归位状态 (变量名已修改以避免冲突)
        if (_airIsReturningToCenter)
        {
            var retPatrolCfg = config?.airStageConfig?.patrol;
            if (retPatrolCfg == null || retPatrolCfg.elements.Count == 0)
            {
                _airIsReturningToCenter = false;
                return;
            }

            var retElem = retPatrolCfg.elements[0];
            var retMv = retElem.move;

            Vector2 center = retElem.areaCenter;
            Vector2 current = rb.position;
            float distLeft = Vector2.Distance(current, center);

            // 到达中心：结束归位（真实移动在 FixedUpdate 的 AirPatrolPhysicsStep 中执行）
            if (distLeft <= 0.15f)
            {
                _airIsReturningToCenter = false;
                retMv.rtStraightPhase = StraightPhase.None;
                retMv.rtCurrentSpeed = 0f;
                retMv.rtAccelTimer = retMv.rtCruiseTimer = retMv.rtDecelTimer =
                    retMv.rtRestTimer = retMv.rtMoveTimer = 0f;
                _airVel = Vector2.zero;
                return;
            }

            // 仅负责初始化四相，不直接位移
            if (retMv.rtStraightPhase == StraightPhase.None)
            {
                _airMoveFxPlayedThisSegment = false;
                _airRestFxPlayedThisRest = false;

                retMv.rtCurrentSpeed = 0f;
                retMv.rtCruiseTimer = Mathf.Max(0f, retMv.moveDuration);
                retMv.rtAccelTimer = Mathf.Max(0f, retMv.accelerationTime);
                retMv.rtDecelTimer = Mathf.Max(0f, retMv.decelerationTime);

                bool instantAccel = (retMv.accelerationTime <= 0f);
                bool instantDecel = (retMv.decelerationTime <= 0f);

                retMv.rtStraightPhase = instantAccel
                    ? ((retMv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (instantDecel ? StraightPhase.Rest : StraightPhase.Decel))
                    : StraightPhase.Accel;

                if (instantAccel)
                    retMv.rtCurrentSpeed = Mathf.Max(0f, retMv.moveSpeed);

                // 初始方向仅存档，不移动（移动在 FixedUpdate 做）
                Vector2 initDir = (center - current).normalized;
                if (initDir.sqrMagnitude < 0.0001f) initDir = Vector2.right;
                _airLastDir = initDir;
                _airVel = Vector2.zero;
            }
            return;
        }

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

                // 休息阶段：强制主速度为0，防止上一段残留导致漂移
                _airVel = Vector2.zero;
                rb.velocity = Vector2.zero;

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

        // 归位阶段：使用固定步物理推进，保证速度与巡逻一致
        if (_airIsReturningToCenter)
        {
            var retElem = pCfg.elements[0];
            var retMv = retElem.move;

            Vector2 center = retElem.areaCenter;
            Vector2 current = rb.position;
            Vector2 toCenter = center - current;
            float distLeft = toCenter.magnitude;

            // 到达中心：结束归位并复位巡逻运行态
            if (distLeft <= 0.15f)
            {
                _airIsReturningToCenter = false;
                retMv.rtStraightPhase = StraightPhase.None;
                retMv.rtCurrentSpeed = 0f;
                retMv.rtAccelTimer = retMv.rtCruiseTimer = retMv.rtDecelTimer =
                    retMv.rtRestTimer = retMv.rtMoveTimer = 0f;
                _airVel = Vector2.zero;
                return;
            }

            // 段起点：初始化四相
            if (retMv.rtStraightPhase == StraightPhase.None)
            {
                _airMoveFxPlayedThisSegment = false;
                _airRestFxPlayedThisRest = false;

                retMv.rtCurrentSpeed = 0f;
                retMv.rtCruiseTimer = Mathf.Max(0f, retMv.moveDuration);
                retMv.rtAccelTimer = Mathf.Max(0f, retMv.accelerationTime);
                retMv.rtDecelTimer = Mathf.Max(0f, retMv.decelerationTime);

                bool instantAccel = (retMv.accelerationTime <= 0f);
                bool instantDecel = (retMv.decelerationTime <= 0f);

                retMv.rtStraightPhase = instantAccel
                    ? ((retMv.rtCruiseTimer > 0f) ? StraightPhase.Cruise : (instantDecel ? StraightPhase.Rest : StraightPhase.Decel))
                    : StraightPhase.Accel;

                if (instantAccel)
                    retMv.rtCurrentSpeed = Mathf.Max(0f, retMv.moveSpeed);

                Vector2 initDir = toCenter.normalized;
                if (initDir.sqrMagnitude < 0.0001f) initDir = Vector2.right;

                float seedSpeed = (retMv.rtCurrentSpeed > 0f ? retMv.rtCurrentSpeed : Mathf.Max(0.01f, retMv.moveSpeed * 0.5f));
                _airVel = initDir * seedSpeed;
                _airLastDir = initDir;
            }

            // 推进速度标量（用 fixedDeltaTime）
            float targetSpd = Mathf.Max(0f, retMv.moveSpeed);
            float accelRate = (retMv.accelerationTime > 0f)
                ? (targetSpd / Mathf.Max(0.0001f, retMv.accelerationTime))
                : float.PositiveInfinity;
            float decelRate = (retMv.decelerationTime > 0f)
                ? (targetSpd / Mathf.Max(0.0001f, retMv.decelerationTime))
                : float.PositiveInfinity;

            switch (retMv.rtStraightPhase)
            {
                case StraightPhase.Accel:
                    if (float.IsPositiveInfinity(accelRate))
                    {
                        retMv.rtCurrentSpeed = targetSpd;
                        retMv.rtStraightPhase = (retMv.rtCruiseTimer > 0f)
                            ? StraightPhase.Cruise
                            : (!float.IsPositiveInfinity(decelRate) ? StraightPhase.Decel : StraightPhase.Rest);
                    }
                    else
                    {
                        retMv.rtCurrentSpeed = Mathf.MoveTowards(retMv.rtCurrentSpeed, targetSpd, accelRate * Time.fixedDeltaTime);
                        if (retMv.accelerationTime > 0f)
                            retMv.rtAccelTimer = Mathf.Max(0f, retMv.rtAccelTimer - Time.fixedDeltaTime);
                        if (Mathf.Approximately(retMv.rtCurrentSpeed, targetSpd) || retMv.rtAccelTimer <= 0f)
                            retMv.rtStraightPhase = (retMv.rtCruiseTimer > 0f)
                                ? StraightPhase.Cruise
                                : (!float.IsPositiveInfinity(decelRate) ? StraightPhase.Decel : StraightPhase.Rest);
                    }
                    break;

                case StraightPhase.Cruise:
                    retMv.rtCurrentSpeed = targetSpd;
                    retMv.rtCruiseTimer = Mathf.Max(0f, retMv.rtCruiseTimer - Time.fixedDeltaTime);
                    if (retMv.rtCruiseTimer <= 0f)
                        retMv.rtStraightPhase = (!float.IsPositiveInfinity(decelRate)) ? StraightPhase.Decel : StraightPhase.Rest;
                    break;

                case StraightPhase.Decel:
                    if (float.IsPositiveInfinity(decelRate))
                    {
                        retMv.rtCurrentSpeed = 0f;
                        retMv.rtStraightPhase = StraightPhase.Rest;
                    }
                    else
                    {
                        retMv.rtCurrentSpeed = Mathf.MoveTowards(retMv.rtCurrentSpeed, 0f, decelRate * Time.fixedDeltaTime);
                        if (retMv.rtCurrentSpeed <= 0.0001f)
                        {
                            retMv.rtCurrentSpeed = 0f;
                            retMv.rtStraightPhase = StraightPhase.Rest;
                        }
                    }
                    break;

                case StraightPhase.Rest:
                default:
                    retMv.rtRestTimer = (retMv.rtRestTimer > 0f)
                        ? retMv.rtRestTimer - Time.fixedDeltaTime
                        : PickStraightRestTime(retMv) - Time.fixedDeltaTime;

                    if (retMv.rtRestTimer <= 0f)
                    {
                        retMv.rtStraightPhase = StraightPhase.None;
                        retMv.rtMoveTimer = retMv.rtRestTimer = retMv.rtAccelTimer = retMv.rtCruiseTimer = retMv.rtDecelTimer = 0f;
                    }
                    // 休息期不位移
                    return;
            }

            // 方向始终指向中心
            Vector2 dir = (center - rb.position).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = _airLastDir;

            float spd = Mathf.Max(0f, retMv.rtCurrentSpeed);
            _airVel = dir * spd;
            if (dir.sqrMagnitude > 0.0001f) _airLastDir = dir;

            rb.MovePosition(rb.position + _airVel * Time.fixedDeltaTime);

            if (Mathf.Abs(_airVel.x) > 0.0005f)
                ForceFaceSign(_airVel.x >= 0f ? +1 : -1);
            return;
        }

        var elem = pCfg.elements[_airActiveIndex];
        var mv = elem.move;

        // 休息阶段固定：完全静止（禁止主速度与正弦偏移）
        bool airInRest = (mv.rtStraightPhase == StraightPhase.Rest);
        if (airInRest)
        {
            _airVel = Vector2.zero;
        }

        bool affectByArea = pCfg.canPassThroughScene;

        // newVel 是“本步结束后的主速度”，最后会写回 _airVel
        Vector2 newVel = _airVel;

        // Sine 偏移：fixedDeltaTime 推进（只是位移偏移，不改主速度）
        _airTime += Time.fixedDeltaTime;
        Vector2 sineDelta = Vector2.zero;
        // 休息中禁止正弦位移
        if (!airInRest && elem.sinEnabled && elem.sinAmplitude > 0f && elem.sinFrequency > 0f)
        {
            float s = Mathf.Sin(_airTime * Mathf.PI * 2f * elem.sinFrequency) * elem.sinAmplitude;

            Vector2 axis;
            if (pCfg.pathType == AirPatrolPathType.AreaHorizontal)
                axis = Vector2.up;
            else if (pCfg.pathType == AirPatrolPathType.AreaVertical)
                axis = Vector2.right;
            else
            {
                Vector2 d = (_airVel.sqrMagnitude > 0.0001f ? _airVel.normalized : Vector2.right);
                axis = new Vector2(-d.y, d.x);
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
        Vector2 plannedDeltaRB = airInRest
            ? Vector2.zero                // 休息阶段彻底静止
            : _airVel * Time.fixedDeltaTime + sineDelta;
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

    // 空中发现阶段特效
    public void OnFxSkyFindMove()
    {
        if (state != MonsterState.Discovery) return;

        // 仅在“空中独占”模式下播放：airPhase 勾选且 groundPhase 未勾选
        if (!(config?.airStageConfig?.discovery != null &&
              config?.airPhaseConfig != null &&
              config.airPhaseConfig.airPhase &&
              !config.airPhaseConfig.groundPhase))
            return;

        var dcfg = config?.airStageConfig?.discovery;

        if (dcfg == null) return;

        // 仅在非 Rest 相位下播放移动 FX；防抖：同一段只播一次
        if (_airDiscPhase == AirDiscPhase.Rest) return;
        if (_airMoveFxPlayedThisSegment) return;

        GameObject prefab = (_airDiscState == AirDiscState.Backstep)
                            ? dcfg.backMoveEffectPrefab
                            : dcfg.followMoveEffectPrefab;

        if (!prefab) return;

        var anchor = fxSkyfindMovePoint ? fxSkyfindMovePoint : transform;
        PlayEffect(prefab, anchor);
        _airMoveFxPlayedThisSegment = true;
    }

    public void OnFxSkyFindRest()
    {
        if (state != MonsterState.Discovery) return;

        // 仅在“空中独占”模式下播放：airPhase 勾选且 groundPhase 未勾选
        if (!(config?.airStageConfig?.discovery != null &&
              config?.airPhaseConfig != null &&
              config.airPhaseConfig.airPhase &&
              !config.airPhaseConfig.groundPhase))
            return;

        var dcfg = config?.airStageConfig?.discovery;

        if (dcfg == null) return;

        if (_airDiscPhase != AirDiscPhase.Rest) return;
        if (_airRestFxPlayedThisRest) return;

        GameObject prefab = (_airDiscState == AirDiscState.Backstep)
                            ? dcfg.backRestEffectPrefab
                            : dcfg.followRestEffectPrefab;

        if (!prefab) return;

        var anchor = fxSkyfindRestPoint ? fxSkyfindRestPoint : transform;
        PlayEffect(prefab, anchor);
        _airRestFxPlayedThisRest = true;
    }
}
