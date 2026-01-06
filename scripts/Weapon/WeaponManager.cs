using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [Header("组件引用")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private AnimationEventRelay relay;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private Transform weaponSlot;
    [SerializeField] private Transform vfxSlot;
    [SerializeField] private Transform vfxStarSlot;

    [Header("武器数据库")]
    [SerializeField] private List<WeaponDefinition> database = new List<WeaponDefinition>();

    private readonly Dictionary<string, WeaponDefinition> _map = new Dictionary<string, WeaponDefinition>();
    private GameObject _currentWeaponGO;
    private GameObject _currentFxGO;
    private GameObject _currentFxStarGO;
    private AttackEventHub _currentWeaponHub;
    private HitboxController _currentHitbox;

    public string CurrentWeaponId { get; private set; } = "";

    void Awake()
    {
        if (!playerAnimator) playerAnimator = GetComponentInChildren<Animator>();
        if (!relay) relay = GetComponentInChildren<AnimationEventRelay>();
        if (!playerController) playerController = GetComponentInParent<PlayerController>();
        BuildIndex();
    }

    private void Start()
    {
        EquipWeapon("001");
    }

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

        if (playerAnimator && def.playerOverride)
            playerAnimator.runtimeAnimatorController = def.playerOverride;

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

                if (_currentHitbox)
                {
                    if (def.baseDamage > 0) _currentHitbox.InjectBaseDamage(def.baseDamage);

                    _currentHitbox.SetHitVfx(def.hitImpactEffectPrefab);

                    // FIXED: Explicitly define types to avoid CS0103/CS0123 errors
                    _currentHitbox.OnHitEnemy = (Collider2D target, int dmg, GameObject vfxPrefab, Vector2 hitPoint) =>
                    {
                        if (playerController)
                        {  
                            playerController.OnMyWeaponHitEnemy(target, dmg, vfxPrefab, hitPoint);
                        }
                    };
                }

                if (_currentWeaponHub && playerAnimator)
                {
                    var f = typeof(AttackEventHub).GetField("bodyAnimator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null) f.SetValue(_currentWeaponHub, playerAnimator);
                }
            }
        }

        if (playerController)
        {
            playerController.SetWeaponHitVfx(def.effectStarPrefab);
        }

        if (vfxSlot)
        {
            if (_currentFxGO) Destroy(_currentFxGO);
            _currentFxGO = null;
            if (def.effectPrefab)
            {
                _currentFxGO = Instantiate(def.effectPrefab, vfxSlot, worldPositionStays: false);
                _currentFxGO.name = "player_weapon_effect";
                BindFxHubBodyAnimator(_currentFxGO);
            }
        }

        if (vfxStarSlot)
        {
            if (_currentFxStarGO) Destroy(_currentFxStarGO);
            _currentFxStarGO = null;
            if (def.effectStarPrefab)
            {
                _currentFxStarGO = Instantiate(def.effectStarPrefab, vfxStarSlot, worldPositionStays: false);
                _currentFxStarGO.name = "player_weapon_effectstar";
                BindFxHubBodyAnimator(_currentFxStarGO);
            }
        }

        if (relay)
        {
            relay.attackHub = _currentWeaponHub;
            if (_currentHitbox) relay.SetWeaponHitbox(_currentHitbox);
            relay.vfxHubs.Clear();

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

            if (_currentWeaponGO)
            {
                var extraFxHubs = _currentWeaponGO.GetComponentsInChildren<AttackEventHub>(true);
                foreach (var h in extraFxHubs)
                {
                    if (!h) continue;
                    if (h == _currentWeaponHub) continue;
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