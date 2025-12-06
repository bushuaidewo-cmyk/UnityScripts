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

    // 新增：当前武器命中体控制器（由 WeaponManager 在装备时设置）
    [SerializeField] private HitboxController weaponHitbox;

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

    // 追加到类内其它事件旁
    public void OnWallJumpForwardUnlock()
    {
        player?.OnWallJumpForwardUnlock();
    }
    // ================= 原有：转发角色其它事件（保留） =================

    public void OnAttackEnd()
    {
        player?.OnAttackEnd();
        weaponHitbox?.CloseAll(); // 兜底：关闭所有命中体，防止漏关
    }
    public void OnDuckAttackEnd()
    {
        player?.OnDuckAttackEnd();
        weaponHitbox?.CloseAll(); // 兜底：关闭所有命中体，防止漏关
    }

    // ====== 命中体：动画事件入口（仅保留两条） ======
    public void EvtHitOn(int index)
    {
        if (weaponHitbox) weaponHitbox.Open(index);
    }
    public void EvtHitOff(int index)
    {
        if (weaponHitbox) weaponHitbox.Close(index);
    }
    // 供 WeaponManager 在装备时注入命中体控制器
    public void SetWeaponHitbox(HitboxController hb) { weaponHitbox = hb; }

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

    // 统一关闭所有攻击命中体（供 PlayerController 在兜底/强制收尾时直接调用）
    public void StopAttackHitbox()
    {
        weaponHitbox?.CloseAll();
    }
}