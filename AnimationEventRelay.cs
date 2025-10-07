using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class AnimationEventRelay : MonoBehaviour
{
    private PlayerController player;

    [Header("Hubs")]
    // 武器（单个）
    public AttackEventHub attackHub;

    // 特效（可多个）
    [Tooltip("可添加多个特效 AttackEventHub（HandSocket 下每个特效各挂一份）")]
    public List<AttackEventHub> vfxHubs = new List<AttackEventHub>();

    // 盾（单个，类似武器）
    [Tooltip("盾牌的 AttackEventHub（独立于武器与特效）")]
    public AttackEventHub shieldHub;

    [Header("Shield States")]
    [Tooltip("站立举盾时，盾Hub Animator 要播放的状态名（循环显示盾）")]
    public string shieldStandingState = "player_shield_idle";
    
    [Tooltip("下蹲举盾时，盾Hub Animator 要播放的状态名（循环显示盾）")]
    public string shieldDuckState = "player_duck_shield_idle";

    private void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    // ================= 武器（单个） =================
    public void SetWeaponXYOffsetStr(string xy)
    {
        if (!attackHub) return;
        if (!TryParseXY(xy, out float x, out float y)) return;
        attackHub.SetWeaponXYOffset(x, y);
    }

    public void PlayWeapon(string stateName)
    {
        if (attackHub) attackHub.PlayWeapon(stateName);
    }

    public void StopWeapon()
    {
        if (attackHub) attackHub.StopWeapon();
    }

    // ================= 特效（可多个，广播） =================
    public void SetVfxXYOffsetStr(string xy)
    {
        if (!TryParseXY(xy, out float x, out float y)) return;
        foreach (var hub in vfxHubs)
        {
            if (!hub) continue;
            hub.SetWeaponXYOffset(x, y);
        }
    }

    public void PlayVfx(string stateName)
    {
        if (string.IsNullOrEmpty(stateName)) return;
        foreach (var hub in vfxHubs)
        {
            if (!hub) continue;
            hub.PlayWeapon(stateName);
        }
    }

    public void StopVfx()
    {
        foreach (var hub in vfxHubs)
        {
            if (!hub) continue;
            hub.StopWeapon();
        }
    }

    // ================= 盾（单个，类似武器） =================
    public void SetShieldXYOffsetStr(string xy)
    {
        if (!shieldHub) return;
        if (!TryParseXY(xy, out float x, out float y)) return;
        shieldHub.SetWeaponXYOffset(x, y);
    }

    public void PlayShield(string stateName)
    {
        if (shieldHub) shieldHub.PlayWeapon(stateName);
    }

    public void StopShield()
    {
        if (shieldHub) shieldHub.StopWeapon();
    }

    // 盾：便捷入口（供 PlayerController 直接调用，避免写字符串）
    public void PlayShieldStanding()
    {
        if (!shieldHub || string.IsNullOrEmpty(shieldStandingState)) return;
        shieldHub.PlayWeapon(shieldStandingState);
    }
    public void PlayShieldDuck()
    {
        if (!shieldHub || string.IsNullOrEmpty(shieldDuckState)) return;
        shieldHub.PlayWeapon(shieldDuckState);
    }

    // 动画事件：根据当前输入与姿态“纠正”盾的显示
    // 规则：未按 L -> Stop；按 L -> 仅地面且按 S 时用下蹲盾；空中一律用站立盾
    // 核心修复：后闪期间（含过渡期）屏蔽 ShieldRefresh 事件，避免“下↑下”抖动
    public void ShieldRefresh()
    {
        if (!shieldHub) return;

        // 后闪动画/过渡期间：由 PlayerController 统一屏蔽，避免“下↑上”
        if (player && player.IsInBackFlashAnimOrTransition())
            return;

        bool holdShield = Input.GetKey(KeyCode.L);
        if (!holdShield)
        {
            shieldHub.StopWeapon();
            return;
        }

        // 直接读取控制器的地面状态，避免依赖 Animator 参数
        bool grounded = player ? player.IsGroundedNow : false;

        bool wantDuck = grounded && Input.GetKey(KeyCode.S);
        string target = wantDuck ? shieldDuckState : shieldStandingState;
        if (!string.IsNullOrEmpty(target))
            shieldHub.PlayWeapon(target);
    }

    // ================= 原有：转发角色其它事件（保留） =================
    public void OnAttackStart() => player?.OnAttackStart();
    public void OnAttackEnd() => player?.OnAttackEnd();

    public void OnDuckAttackStart() => player?.OnDuckAttackStart();
    public void OnDuckAttackEnd() => player?.OnDuckAttackEnd();

    public void OnDuckFwdAttackStart() => player?.OnDuckFwdAttackStart();
    public void OnDuckFwdAttackEnd() => player?.OnDuckFwdAttackEnd();

    // ================= Helpers =================
    private static bool TryParseXY(string input, out float x, out float y)
    {
        x = 0f; y = 0f;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string s = input.Trim().Replace('，', ',').Replace('；', ';');
        string[] parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        bool okX = float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        bool okY = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        return okX && okY;
    }
}