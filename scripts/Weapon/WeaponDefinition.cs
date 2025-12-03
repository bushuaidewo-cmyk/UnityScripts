using UnityEngine;

[CreateAssetMenu(menuName = "Game/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    public string id;

    [Header("替换玩家 6 段攻击动画（建议 AnimatorOverrideController）")]
    public RuntimeAnimatorController playerOverride;

    [Header("武器本体 Prefab（挂到 HandSocketR/WeaponOffset/player_weapon）")]
    public GameObject weaponPrefab;

    [Header("武器特效 Prefab（可选）")]
    public GameObject effectPrefab;       // -> VfxWeaponOffset/player_weapon_effect
    public GameObject effectStarPrefab;   // -> VfxWeaponOffsetstar/player_weapon_effectstar

    [Header("基础伤害")]
    public int baseDamage = 10;
}