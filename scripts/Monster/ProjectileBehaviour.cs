using UnityEngine;

public class ProjectileBehaviour : MonoBehaviour
{
    private Transform _target;
    private ProjectileConfig _cfg;
    private LayerMask _ground; // 场景地面/障碍层
    private Rigidbody2D _rb;
    private Transform _spinTarget;     // 自旋目标（通常是可视子物体）

    private Vector2 _heading;
    private float _baseSpeed;
    private float _t;
    private float _life;

    // S型
    private float _sinPhase;

    // 抛物线（新：独立竖直偏移/速度）
    private float _g;
    private Vector2 _parabolaVel;     // 抛物线（仅竖直）速度
    private Vector2 _parabolaOffset;  // 抛物线（仅竖直）偏移（解耦 transform）
    private Vector2 _prevPos;         // 上一帧位置（保留：若以后需要估算法线）
    private const float GROUND_SKIN = 0.005f;
    private float _bounceScale = 1f;  // 每次反弹后的额外缩放（DecayToZero 模式使用）
    private bool _hitResolvedThisFrame = false;

    // 反弹基线（满足“每次等高”的需求）
    private float _bounceBaselineVy = 0f;

    // 载体（半径旋转绕此做圆周，PathTangent 空间）
    private Vector2 _carrierPos;
    private float _orbitAngleDeg;         // 当前相位（仅用于调试/可视化）
    private float _orbitSweepAccumDeg = 0f; // 本次 sweep 已累计角度（0..orbitAngular）

    // 回旋镖
    private bool _boomerangGoingOut = false;
    private bool _boomerangAtApex = false;
    private bool _boomerangReturning = false;
    private float _boomerangOutDist = 0f;
    private float _boomerangApexTimer = 0f;
    private Vector2 _spawnPos;

    // 跟踪导弹
    private float _homingTimer = 0f;
    private float _homingInterval = 0f;

    // 碰撞一次性保护
    private bool _explodedOrDestroyed = false;

    public void Init(Transform target, ProjectileConfig cfg, Vector2 shotDir, LayerMask groundLayer)
    {
        _bounceScale = 1f;
        _target = target;
        _cfg = cfg;
        _ground = groundLayer;

        _rb = GetComponent<Rigidbody2D>() ?? gameObject.AddComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0f;
        _rb.interpolation = RigidbodyInterpolation2D.None;

        _spinTarget = transform;

        _heading = shotDir.sqrMagnitude > 0.0001f ? shotDir.normalized : Vector2.right;
        _baseSpeed = Mathf.Max(0f, cfg.speed);
        _life = Mathf.Max(0.01f, cfg.lifeTime);

        _carrierPos = transform.position;
        _spawnPos = transform.position;

        // 重力
        _g = Mathf.Max(0f, cfg.gravityScale) * 9.81f;

        // 抛物线：仅竖直速度/偏移
        _parabolaVel = Vector2.zero;
        _parabolaOffset = Vector2.zero;
        if (_cfg.parabolaEnabled)
        {
            if (_cfg.parabolaApexHeight > 0f && _g > 0f)
            {
                float vy0 = Mathf.Sqrt(2f * _g * _cfg.parabolaApexHeight);
                _parabolaVel = new Vector2(0f, vy0);   // 仅竖直分量
            }
            else
            {
                _parabolaVel = Vector2.zero;           // 无 apex 配置：从静止下落
            }
        }

        _homingInterval = (_cfg.homingEnabled && _cfg.homingFrequency > 0f) ? (1f / _cfg.homingFrequency) : 0f;
        _homingTimer = _homingInterval;

        _boomerangGoingOut = cfg.boomerangEnabled && cfg.boomerangOutMaxDistance > 0f;
        _boomerangAtApex = false;
        _boomerangReturning = false;

        _orbitAngleDeg = 0f;
        _orbitSweepAccumDeg = 0f;

        if (_cfg.faceAlongPath && !_cfg.selfRotate)
        {
            float ang = Mathf.Atan2(_heading.y, _heading.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, ang);
        }

        if (GetComponentInChildren<Collider2D>(includeInactive: true) == null)
        {
            Debug.LogWarning($"[Projectile] {name} 层级下未发现 Collider2D（isTrigger）。将无法触发命中事件。");
        }

        _prevPos = transform.position;
        _bounceScale = 1f;

        // 基线 vy：优先用 apexHeight 算出的 vy0；否则先置 0，等第一次落地时再取入射速度
        _bounceBaselineVy = 0f;
        if (_cfg.parabolaEnabled && _cfg.parabolaApexHeight > 0f && _g > 0f)
            _bounceBaselineVy = Mathf.Sqrt(2f * _g * _cfg.parabolaApexHeight);
    }

    public void SetSpinTarget(Transform t)
    {
        if (t) _spinTarget = t;
    }

    void Update()
    {
        if (_explodedOrDestroyed) return;

        float dt = Time.deltaTime;
        _t += dt;
        if (_t >= _life)
        {
            ExplodeAndDestroy();   // 原来是 Destroy(gameObject);
            return;
        }

        _hitResolvedThisFrame = false; // 当帧碰撞只处理一次

        UpdateBaseSpeed(dt, forwardPhaseReturning: _boomerangReturning);
        UpdateHeadingWithHoming(dt);

        Vector2 prev = transform.position;
        _prevPos = prev; // 记录上一帧

        // 1) 载体推进（直线/回旋镖）
        bool forwardAllowed = _cfg.linearEnabled || _boomerangGoingOut || _boomerangReturning;
        if (forwardAllowed)
            _carrierPos += _heading * _baseSpeed * dt;

        // 2) 抛物线（仅竖直）
        if (_cfg.parabolaEnabled)
        {
            _parabolaVel += Vector2.down * _g * dt;
            _parabolaOffset += _parabolaVel * dt;
        }

        // 3) 半径旋转（PathTangent 空间，按“角度段 + 角速度”循环）
        Vector2 orbitOffset = Vector2.zero;
        if (_cfg.orbitEnabled)
        {
            float R = Mathf.Max(0f, _cfg.orbitRadius);
            float sweepGoal = Mathf.Max(0.0001f, _cfg.orbitAngular); // 本段角度（度）
            float dir = Mathf.Sign(_cfg.orbitSweepSpeedDeg);         // 方向：>0 逆时针，<0 顺时针

            // 仅用 orbitSweepSpeedDeg（度/秒）推进相位
            float stepDegPerSecAbs = Mathf.Abs(_cfg.orbitSweepSpeedDeg); // deg/s
            float step = stepDegPerSecAbs * dt;                          // 本帧应推进角度（正值）

            // 累计至 sweepGoal，达到后“回到起点”开始下一段
            float remain = Mathf.Max(0f, sweepGoal - _orbitSweepAccumDeg);
            float applied = Mathf.Min(remain, step);
            _orbitSweepAccumDeg += applied;

            // 相对本段起点的相位（驱动 cos/sin）
            float angleDegRel = dir * _orbitSweepAccumDeg;
            _orbitAngleDeg = angleDegRel; // 仅用于调试显示

            if (_orbitSweepAccumDeg >= sweepGoal)
            {
                _orbitSweepAccumDeg = 0f; // 重置到起点
                angleDegRel = 0f;
                _orbitAngleDeg = 0f;
            }

            // PathTangent 基：切线=heading，法线=左法线
            float rad = angleDegRel * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);

            Vector2 t = _heading.sqrMagnitude > 0.000001f ? _heading.normalized : Vector2.right;
            Vector2 n = new Vector2(-t.y, t.x);

            // 相对载体严格圆：cos 在法线、sin 在切线
            orbitOffset = (n * (c * R)) + (t * (s * R));
        }

        // 4) S 型（世界Y上下）
        Vector2 sineOffset = Vector2.zero;
        if (_cfg.sinEnabled && _cfg.sinAmplitude != 0f && _cfg.sinFrequency > 0f)
        {
            _sinPhase += Mathf.PI * 2f * _cfg.sinFrequency * dt;
            sineOffset = Vector2.up * (_cfg.sinAmplitude * Mathf.Sin(_sinPhase));
        }

        // 5) 合成目标位置
        Vector2 targetPos = _carrierPos + _parabolaOffset + orbitOffset + sineOffset;

        // 6) Swept cast 处理地形碰撞；若无命中再落位
        if (!ResolveSweptHit(prev, targetPos, orbitOffset, sineOffset))
        {
            transform.position = targetPos;
        }

        UpdateBoomerangStates(dt);
        
        // 自旋：当勾选 selfRotate 时，每帧按轴和速度增量旋转
        if (_cfg.selfRotate && _spinTarget)
        {
            Vector3 axisMask = new Vector3(
                _cfg.selfRotateX ? 1f : 0f,
                _cfg.selfRotateY ? 1f : 0f,
                _cfg.selfRotateZ ? 1f : 0f
            );

            if (axisMask.sqrMagnitude > 0f)
            {
                float deg = _cfg.selfRotateSpeedDeg * Time.deltaTime;
                Vector3 euler = axisMask * deg;
                _spinTarget.Rotate(euler, Space.Self);
            }
        }

        UpdateFacing(prev, transform.position);
    }

    // 玩家命中：只在这里处理；地形碰撞统一在 ResolveSweptHit
    void OnTriggerEnter2D(Collider2D other)
    {
        if (_hitResolvedThisFrame) return;
        TryHandlePlayerHit(other.gameObject);
    }
    void OnCollisionEnter2D(Collision2D col)
    {
        if (_hitResolvedThisFrame) return;
        TryHandlePlayerHit(col.collider?.gameObject);
    }

    private void TryHandlePlayerHit(GameObject hitGo)
    {
        if (_explodedOrDestroyed || hitGo == null) return;
        if (_target != null)
        {
            var targetRoot = _target.root;
            var hitRoot = hitGo.transform.root;
            if ((hitRoot == targetRoot) || hitGo.CompareTag("Player"))
            {
                ExplodeAndDestroy();
            }
        }
    }

    // 逐步扫掠检测与响应（避免触发器抖动/误分类）
    private bool ResolveSweptHit(Vector2 from, Vector2 to, Vector2 orbitOffset, Vector2 sineOffset)
    {
        Vector2 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= 0.000001f) return false;

        RaycastHit2D hit = Physics2D.Raycast(from, delta.normalized, dist, _ground);
        if (!hit.collider) return false;

        Vector2 p = hit.point;
        Vector2 n = hit.normal.normalized;

        bool isWall = Mathf.Abs(n.x) > 0.5f && Mathf.Abs(n.y) < 0.5f;
        bool isFloor = n.y > 0.5f;
        bool isCeil = n.y < -0.5f;

        if (_cfg.parabolaEnabled && _cfg.bounceCoefficient > 0f)
        {
            if (isFloor)
            {
                // 贴地 + 重建抛物线偏移
                SnapToGroundAndRebaseParabolaOffset(orbitOffset, sineOffset, p, n);

                // 常量反弹：若 baseline 尚未初始化，则用“本次入射竖直速度”初始化一次
                if (_bounceBaselineVy <= 0f)
                    _bounceBaselineVy = Mathf.Abs(_parabolaVel.y);

                float baseVy = Mathf.Max(0f, _bounceBaselineVy);
                float newVy = baseVy * _cfg.bounceCoefficient; // Constant: 每次等高，系数可>1

                if (_cfg.bounceEnergyMode == BounceEnergyMode.DecayToZero)
                {
                    newVy *= _bounceScale;
                    _bounceScale *= _cfg.bounceDecayFactor;
                    if (newVy <= Mathf.Max(0f, _cfg.bounceEndVyThreshold))
                    {
                        ExplodeAndDestroy();
                        return true;
                    }
                }

                _parabolaVel.y = newVy; // 向上
                _hitResolvedThisFrame = true;
                return true;
            }
            else if (isWall)
            {
                // 水平反射 heading，不改竖直速度
                _heading = Vector2.Reflect(_heading, n).normalized;

                // 推 carrier，使最终 pos 贴在碰撞面外侧
                Vector2 basePos = _carrierPos + orbitOffset + sineOffset + new Vector2(0f, _parabolaOffset.y);
                Vector2 target = p + n * GROUND_SKIN;
                _carrierPos += (target - basePos);

                transform.position = target;
                _hitResolvedThisFrame = true;
                return true;
            }
            else if (isCeil)
            {
                // 常量反弹：若 baseline 尚未初始化，则用“本次入射竖直速度”初始化一次
                if (_bounceBaselineVy <= 0f)
                    _bounceBaselineVy = Mathf.Abs(_parabolaVel.y);

                float baseVy = Mathf.Max(0f, _bounceBaselineVy);
                float newVy = -baseVy * _cfg.bounceCoefficient;

                if (_cfg.bounceEnergyMode == BounceEnergyMode.DecayToZero)
                {
                    newVy *= _bounceScale;
                    _bounceScale *= _cfg.bounceDecayFactor;
                    if (Mathf.Abs(newVy) <= Mathf.Max(0f, _cfg.bounceEndVyThreshold))
                    {
                        ExplodeAndDestroy();
                        return true;
                    }
                }

                _parabolaVel.y = newVy;

                Vector2 target = p + n * GROUND_SKIN;
                Vector2 basePos = _carrierPos + orbitOffset + sineOffset + new Vector2(0f, _parabolaOffset.y);
                _carrierPos += (target - basePos);

                transform.position = target;
                _hitResolvedThisFrame = true;
                return true;
            }
        }

        // 未开启抛物线或系数<=0：命中任何地形直接销毁
        ExplodeAndDestroy();
        return true;
    }

    // 爆炸：补上动画播放（FlygunBoomAnimation）+ 原有特效/粒子播放
    private void ExplodeAndDestroy()
    {
        if (_explodedOrDestroyed) return;
        _explodedOrDestroyed = true;

        if (_cfg != null && _cfg.FlygunBoomEffectPrefab != null)
        {
            var fx = Instantiate(_cfg.FlygunBoomEffectPrefab, transform.position, Quaternion.identity);

            // 若 FX 上有 Animator 且配置了动画名，则播放该状态
            if (!string.IsNullOrEmpty(_cfg.FlygunBoomAnimation))
            {
                var anim = fx.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    try { anim.Play(_cfg.FlygunBoomAnimation, 0, 0f); }
                    catch { /* 若找不到状态，不影响粒子播放 */ }
                }
            }

            // 原粒子播放逻辑保留
            var psArray = fx.GetComponentsInChildren<ParticleSystem>(true);
            float life = Mathf.Max(0.05f, _cfg.duration);
            if (psArray != null && psArray.Length > 0)
            {
                float maxDur = 0f;
                foreach (var ps in psArray)
                {
                    if (!ps) continue;
                    ps.Play(true);
                    var main = ps.main;
                    float d = main.duration;
                    float lt = main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                               ? main.startLifetime.constantMax
                               : main.startLifetime.constant;
                    maxDur = Mathf.Max(maxDur, d + lt);
                }
                life = Mathf.Max(life, maxDur);
            }
            Destroy(fx, life + 0.1f);
        }

        Destroy(gameObject);
    }

    private void UpdateBaseSpeed(float dt, bool forwardPhaseReturning)
    {
        if (_cfg.boomerangEnabled && (forwardPhaseReturning || _boomerangGoingOut))
        {
            if (forwardPhaseReturning)
            {
                float target = (_cfg.boomerangBackUniformSpeed > 0f) ? _cfg.boomerangBackUniformSpeed : Mathf.Max(0f, _cfg.speed);

                if (_cfg.boomerangBackUniformTime > 0f)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, Mathf.Max(0.0001f, target / _cfg.boomerangBackUniformTime) * dt);
                else if (_cfg.boomerangBackAccelTime > 0f)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, Mathf.Max(0.0001f, target / _cfg.boomerangBackAccelTime) * dt);
                else if (_cfg.boomerangBackAccel > 0f)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, _cfg.boomerangBackAccel * dt);
                else
                    _baseSpeed = target;
            }
            else
            {
                float target = Mathf.Max(0f, _cfg.speed);

                if (_cfg.accelTime > 0f)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, Mathf.Max(0.0001f, target / _cfg.accelTime) * dt);
                else if (_cfg.accel > 0f && _baseSpeed < target)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, _cfg.accel * dt);

                if (_cfg.decelTime > 0f && _baseSpeed > target)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, Mathf.Max(0.0001f, _baseSpeed / _cfg.decelTime) * dt);
                else if (_cfg.decel > 0f && _baseSpeed > target)
                    _baseSpeed = Mathf.MoveTowards(_baseSpeed, target, _cfg.decel * dt);
            }

            return;
        }

        if (!_cfg.linearEnabled && !_cfg.parabolaEnabled) { _baseSpeed = 0f; return; }

        float tgt = Mathf.Max(0f, _cfg.speed);

        if (_cfg.accelTime > 0f)
            _baseSpeed = Mathf.MoveTowards(_baseSpeed, tgt, Mathf.Max(0.0001f, tgt / _cfg.accelTime) * dt);
        else if (_cfg.accel > 0f && _baseSpeed < tgt)
            _baseSpeed = Mathf.MoveTowards(_baseSpeed, tgt, _cfg.accel * dt);

        if (_cfg.decelTime > 0f && _baseSpeed > tgt)
            _baseSpeed = Mathf.MoveTowards(_baseSpeed, tgt, Mathf.Max(0.0001f, _baseSpeed / _cfg.decelTime) * dt);
        else if (_cfg.decel > 0f && _baseSpeed > tgt)
            _baseSpeed = Mathf.MoveTowards(_baseSpeed, tgt, _cfg.decel * dt);
    }

    private void UpdateHeadingWithHoming(float dt)
    {
        Vector2 curPos = (Vector2)transform.position;

        if (_boomerangReturning && _cfg.boomerangEnabled)
        {
            Vector2 dir = (_spawnPos - curPos);
            if (dir.sqrMagnitude > 0.0001f) _heading = dir.normalized;
            return;
        }

        if (_cfg.homingEnabled && _homingInterval > 0f && _target != null)
        {
            _homingTimer -= dt;
            if (_homingTimer <= 0f)
            {
                _homingTimer += _homingInterval;

                float s = Mathf.Clamp01(_cfg.homingStrength);
                if (s <= 0f) return;

                Vector2 desired = (_cfg.spawnAim == SpawnAimMode.HorizontalToPlayer)
                    ? new Vector2(Mathf.Sign((_target.position - transform.position).x), 0f)
                    : ((Vector2)_target.position - curPos);

                if (desired.sqrMagnitude > 0.000001f)
                {
                    desired.Normalize();
                    _heading = Vector2.Lerp(_heading, desired, s).normalized;
                }
            }
        }
    }

    private void UpdateBoomerangStates(float dt)
    {
        if (!_cfg.boomerangEnabled) return;

        if (_boomerangGoingOut && !_boomerangAtApex)
        {
            float step = Mathf.Max(0f, _baseSpeed * dt);
            _boomerangOutDist += step;

            if (_boomerangOutDist >= Mathf.Max(0f, _cfg.boomerangOutMaxDistance))
            {
                _boomerangAtApex = true;
                _boomerangGoingOut = false;
                _boomerangApexTimer = Mathf.Max(0f, _cfg.boomerangApexStopTime);
                _baseSpeed = 0f;
            }
        }
        else if (_boomerangAtApex && !_boomerangReturning)
        {
            _boomerangApexTimer -= dt;
            if (_boomerangApexTimer <= 0f)
            {
                _boomerangAtApex = false;
                _boomerangReturning = true;
            }
        }
        else if (_boomerangReturning)
        {
            float distToSpawn = Vector2.Distance((Vector2)transform.position, _spawnPos);
            if (distToSpawn <= 0.05f)
                ExplodeAndDestroy(); 
        }
    }

    private void UpdateFacing(Vector2 prev, Vector2 now)
    {
        if (_cfg.selfRotate || !_cfg.faceAlongPath) return;

        Vector2 vel = now - prev;
        if (vel.sqrMagnitude <= 0.000001f) return;

        float ang = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, ang);
    }

    void OnDrawGizmos()
    {
        if (_cfg == null) return;
        float r = Mathf.Max(0f, _cfg.radius);
        if (r <= 0f) return;

        Color blue = new Color(0.2f, 0.6f, 1f, 1f);
        Gizmos.color = blue;
        Vector3 center = transform.position;
        Vector3 prev = center + new Vector3(r, 0f, 0f);
        const int seg = 48;
        for (int i = 1; i <= seg; i++)
        {
            float ang = (i / (float)seg) * Mathf.PI * 2f;
            Vector3 cur = center + new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }

    // 贴地并重建抛物线偏移基线（仅“地面/斜面”使用）
    private void SnapToGroundAndRebaseParabolaOffset(Vector2 orbitOffset, Vector2 sineOffset, Vector2 hitPoint, Vector2 hitNormal)
    {
        // 期望落在地面上方 skin
        float targetY = hitPoint.y + GROUND_SKIN;

        // 基线 = carrier + orbit + sine
        Vector2 basePos = _carrierPos + orbitOffset + sineOffset;

        // 只重建“竖直抛物线偏移”的 y，使最终 pos.y == targetY
        _parabolaOffset.y = targetY - basePos.y;

        // 立即设置 Transform，避免保持重叠
        transform.position = new Vector2(transform.position.x, targetY);
    }
}