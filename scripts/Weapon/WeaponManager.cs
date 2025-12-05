using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [Header("绑定路径")]
    [SerializeField] private Animator playerAnimator;           // PlayerRoot/.../Flip/player 的 Animator
    [SerializeField] private AnimationEventRelay relay;         // 同层级的 AnimationEventRelay
    [SerializeField] private Transform weaponSlot;              // HandSocketR/WeaponOffset/player_weapon
    [SerializeField] private Transform vfxSlot;                 // HandSocketR/WeaponOffset/VfxWeaponOffset
    [SerializeField] private Transform vfxStarSlot;             // HandSocketR/WeaponOffset/VfxWeaponOffsetstar

    [Header("武器库（按 id 查找）")]
    [SerializeField] private List<WeaponDefinition> database = new List<WeaponDefinition>();

    private readonly Dictionary<string, WeaponDefinition> _map = new Dictionary<string, WeaponDefinition>();
    private GameObject _currentWeaponGO;
    private GameObject _currentFxGO;
    private GameObject _currentFxStarGO;
    private AttackEventHub _currentWeaponHub; // 本体 Hub
    private HitboxController _currentHitbox;

    public string CurrentWeaponId { get; private set; } = "";

    void Awake()
    {
        if (!playerAnimator) playerAnimator = GetComponentInChildren<Animator>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>();
        BuildIndex();
    }

    private void Start()
    {
        // 用你在 WeaponDefinition 里配置的 id，比如 "001" 或 "Weapon_001"
        EquipWeapon("001");
    }

    // 调试：数字键切换武器ID（仅开发期）
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) EquipWeapon("001");
        if (Input.GetKeyDown(KeyCode.Alpha2)) EquipWeapon("002");
    }

    private void BuildIndex()
    {
        _map.Clear();
        foreach (var w in database)
        {
            if (w && !string.IsNullOrEmpty(w.id)) _map[w.id] = w;
        }
    }

    public bool EquipWeapon(string id)
    {
        if (string.IsNullOrEmpty(id) || !_map.TryGetValue(id, out var def)) return false;
        if (CurrentWeaponId == id) return true;

        // 1) 覆盖玩家攻击动画 6 段
        if (playerAnimator && def.playerOverride)
            playerAnimator.runtimeAnimatorController = def.playerOverride;

        // 2) 替换武器本体
        if (weaponSlot)
        {
            if (_currentWeaponGO) Destroy(_currentWeaponGO);
            _currentWeaponGO = null;
            _currentWeaponHub = null;
            _currentHitbox = null;

            if (def.weaponPrefab)
            {
                _currentWeaponGO = Instantiate(def.weaponPrefab, weaponSlot, worldPositionStays: false);
                _currentWeaponHub = _currentWeaponGO.GetComponentInChildren<AttackEventHub>(true);
                _currentHitbox = _currentWeaponGO.GetComponentInChildren<HitboxController>(true);

                if (_currentHitbox && def.baseDamage > 0) _currentHitbox.InjectBaseDamage(def.baseDamage);

                // 将 bodyAnimator 绑定到玩家（用于速度同步）；如果该字段是 private，可用反射；若你愿意把它改为 public/setter，则直接赋值即可
                if (_currentWeaponHub && playerAnimator)
                {
                    var f = typeof(AttackEventHub).GetField("bodyAnimator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null) f.SetValue(_currentWeaponHub, playerAnimator);
                }
            }
        }

        // 3) 替换 FX A（player_weapon_effect）
        if (vfxSlot)
        {
            if (_currentFxGO) Destroy(_currentFxGO);
            _currentFxGO = null;

            if (def.effectPrefab)
            {
                _currentFxGO = Instantiate(def.effectPrefab, vfxSlot, worldPositionStays: false);
                _currentFxGO.name = "player_weapon_effect"; // 可选：保持原命名
                BindFxHubBodyAnimator(_currentFxGO);
            }
        }

        // 4) 替换 FX B（player_weapon_effectstar）
        if (vfxStarSlot)
        {
            if (_currentFxStarGO) Destroy(_currentFxStarGO);
            _currentFxStarGO = null;

            if (def.effectStarPrefab)
            {
                _currentFxStarGO = Instantiate(def.effectStarPrefab, vfxStarSlot, worldPositionStays: false);
                _currentFxStarGO.name = "player_weapon_effectstar"; // 可选：保持原命名
                BindFxHubBodyAnimator(_currentFxStarGO);
            }
        }

        // 5) 注入到 Relay：武器本体 Hub、命中体、以及所有 FX Hub 进入 vfxHubs
        if (relay)
        {
            relay.attackHub = _currentWeaponHub;
            if (_currentHitbox) relay.SetWeaponHitbox(_currentHitbox);
            relay.vfxHubs.Clear();

            // 收集两个 FX 插槽里的 AttackEventHub（一个 FX 里也可以有多个 Hub）
            if (_currentFxGO)
            {
                var hubs = _currentFxGO.GetComponentsInChildren<AttackEventHub>(true);
                foreach (var h in hubs) if (h) relay.vfxHubs.Add(h);
            }
            if (_currentFxStarGO)
            {
                var hubs = _currentFxStarGO.GetComponentsInChildren<AttackEventHub>(true);
                foreach (var h in hubs) if (h) relay.vfxHubs.Add(h);
            }

            // 如果武器本体 prefab 里还有额外 FX Hub，也一并纳入 vfxHubs（可选）
            if (_currentWeaponGO)
            {
                var extraFxHubs = _currentWeaponGO.GetComponentsInChildren<AttackEventHub>(true);
                foreach (var h in extraFxHubs)
                {
                    if (!h) continue;
                    if (h == _currentWeaponHub) continue; // 跳过本体 Hub
                    relay.vfxHubs.Add(h);
                }
            }
        }

        CurrentWeaponId = id;
        return true;
    }

    private void BindFxHubBodyAnimator(GameObject fxRoot)
    {
        if (!fxRoot || !playerAnimator) return;
        var hubs = fxRoot.GetComponentsInChildren<AttackEventHub>(true);
        foreach (var hub in hubs)
        {
            if (!hub) continue;
            var f = typeof(AttackEventHub).GetField("bodyAnimator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(hub, playerAnimator);
        }
    }
}