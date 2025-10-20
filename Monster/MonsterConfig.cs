using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// �������� ScriptableObject������������׶εĲ������á�
/// </summary>
[CreateAssetMenu(fileName = "NewMonsterConfig", menuName = "��������/�¹�������")]
public class MonsterConfig : ScriptableObject
{
    // �������Բ���
    [Header("��������")]
    [Tooltip("����ΨһID������ʶ���������")]
    public string monsterID;
    [Tooltip("����ȼ�")]
    public int level;
    [Tooltip("�����������ֵ")]
    public float maxHP;
    [Tooltip("��ɱ������õľ���ֵ")]
    public int exp;

    // Ԥ����ͽ׶�����
    [Header("��������")]
    [Tooltip("����PrefabԤ���壨Ӧ������ײ�塢��ⴥ������Animator�������")]
    public GameObject monsterPrefab;

    [Header("�����׶�����")]
    public SpawnConfig spawnConfig;

    [Header("Ѳ�߽׶����ã����棩")]
    public PatrolConfig patrolConfig;

    [Header("���ֽ׶����ã����棩")]
    public DiscoveryConfig discoveryConfig;

    [Header("�����׶����ã����棩")]
    public AttackConfig attackConfig;

    [Header("���н׶���������")]
    [Tooltip("�Ƿ��п��н׶Σ�����������л������н׶Σ�")]
    public bool hasAirPhase;

    [Header("Ѳ�߽׶����ã����У�")]
    public AirPatrolConfig airPatrolConfig;

    [Header("���ֽ׶����ã����У�")]
    public AirDiscoveryConfig airDiscoveryConfig;

    [Header("�����׶����ã����У�")]
    public AttackConfig airAttackConfig;

    [Header("�ؿ�״̬�л�")]
    [Tooltip("������׶ν������Ƿ��л������н׶�")]
    public bool switchToAirAfterGround;
    [Tooltip("������HP���ڴ˰ٷֱ�ʱ�л����н׶Σ�0��ʾ������HP�л���")]
    public float switchHPThreshold; // ����0.5��ʾHP����50%ʱ�л�

    [Header("�����׶�����")]
    public DeathConfig deathConfig;
}

/// <summary>
/// �����׶�����
/// </summary>
[System.Serializable]
public class SpawnConfig
{
    [Tooltip("�������������ͣ��̶����б���������")]
    public SpawnPositionType positionType;
    [Tooltip("Ԥ��ĳ������б��������꣩�����ж�����˳������ѡ��")]
    public List<Vector2> spawnPoints = new List<Vector2>();
    [Tooltip("�Ƿ�˳��ʹ�ó����㣨true��˳��ѭ�����������ѡ��)")]
    public bool sequentialSpawn = true;
    [Tooltip("�����������ĵ㣨�������꣩")]
    public Vector2 areaCenter;
    [Tooltip("���������С����ߣ����絥λ)")]

    [Header("��������")]
    public Vector2 areaSize;
    [Tooltip("�������򣺳�����ң��̶����󣬹̶�����")]
    public Orientation spawnOrientation;
    [Tooltip("��������ͬʱ���ڵĹ����������")]
    public int maxSpawnCount = 1;
    [Tooltip("ÿ�����ɵĹ�������")]
    public int spawnBatchCount = 1;
    [Tooltip("���γ������ɵļ��ʱ�䣨��)")]
    public float spawnInterval = 0;

    [Header("��������")]
    
    [Tooltip("������������")]
    public string spawnAnimation;
    [Tooltip("������ԭ�ش�����������")]
    public string idleAnimation;
    [Tooltip("��������������ȴ��������ٲ���Idle����")]
    public float idleDelay = 1f;

    [Header("��Ч����")]
    [Tooltip("������ЧPrefab������ϵͳ��")]
    public GameObject spawnEffectPrefab;
    [Tooltip("�Ƿ����һ�β��ų���������Ч�����������������ٲ�����Ч")]
    public bool spawnEffectOnlyFirst = true;
    [Tooltip("����Idleѭ����ЧPrefab������ϵͳ��")]
    public GameObject idleEffectPrefab;
    [Tooltip("�Ƿ����һ�β���Idle������Ч������Idle����ѭ�����ٲ�����Ч")]
    public bool idleEffectOnlyFirst = false;

    [Header("ִ�д���")]
    [Tooltip("�������Ƿ�������ʼѲ�ߣ�������ԭ�ش�����")]
    public bool startPatrolImmediately;
    [Tooltip("�����׶�ִ�д�����ѭ�����γ����������Զ�����Ѳ�߽׶Σ�0 ��ʾ���޲�����Ѳ�ߣ�")]
    public int spawnLoopCount = 1;
}

/// <summary>
/// Ѳ�߽׶����ã����棩
/// </summary>
[System.Serializable]
public class PatrolConfig
{
    [Tooltip("����Ѳ��·�����б��������꣩�����ｫ��˳������Щ��֮������Ѳ�ߡ�")]
    public List<Vector2> patrolPoints = new List<Vector2>();
    [Tooltip("Ѳ��·��������true ����·����֮������������false ��ĩβ��ѭ���ص���һ���㡣")]
    public bool pingPong = true;

    [Tooltip("Ѳ���ƶ�ģʽ�б�����϶����ƶ���ʽ��")]
    public List<PatrolMovement> movements = new List<PatrolMovement>();
    [Tooltip("Ѳ��ģʽ�Ƿ�����л���true�����ѡ��һ��ģʽִ�У�false���б�˳��ѭ����")]
    public bool randomOrder = false;
    [Tooltip("Ѳ��ģʽ��������Ƿ�ѭ����false���ģʽִ��һ�κ�Ѳ�߽׶ν�����")]
    public bool loopAll = true;
}

/// <summary>
/// Ѳ���ƶ���Ԫ���ã�ֱ�߻���Ծ�ƶ���
/// </summary>
[System.Serializable]
public class PatrolMovement
{
    [Tooltip("�ƶ����ͣ�ֱ�߻���Ծ")]
    public MovementType type;

    [Header("ֱ���ƶ�����")]
    [Tooltip("�ƶ��ٶ�")]
    public float moveSpeed;
    [Tooltip("�ƶ��𲽼��ٶ�")]
    public float acceleration;
    [Tooltip("ֹͣǰ���ٶ�")]
    public float deceleration;
    [Tooltip("�����ƶ�ʱ��")]
    public float moveDuration;
    [Tooltip("��Ϣͣ��ʱ��")]
    public float restDuration;

    [Header("�ƶ���������")]
    [Tooltip("�ƶ���������")]
    public string moveAnimation;
    [Tooltip("��Ϣ��������")]
    public string restAnimation;
    [Tooltip("�ƶ���Ч�Ƿ������һ��")]
    public bool moveEffectOnlyFirst = false;
    [Tooltip("�ƶ�ʱ��ЧPrefab������ϵͳ��")]
    public GameObject moveEffectPrefab;
    [Tooltip("��Ϣ��Ч�Ƿ������һ��")]
    public bool restEffectOnlyFirst = false;
    [Tooltip("��Ϣʱ��ЧPrefab������ϵͳ��")]
    public GameObject restEffectPrefab;

    

    [Header("��Ծ�ƶ�����")]
    [Tooltip("ˮƽ��Ծ�ٶ�")]
    public float jumpSpeed;
    [Tooltip("��Ծ�߶ȣ���ֱ�����ٶȣ�")]
    public float jumpHeight;
    [Tooltip("����ϵ��")]
    public float gravityScale;
    [Tooltip("������Ծ����ʱ��")]
    public float jumpDuration;
    [Tooltip("��Ծ�����Ϣʱ��")]
    public float jumpRestDuration;

    [Header("��Ծ����/��Ч")]
    [Tooltip("��Ծ���ڿ��У�����")]
    public string jumpAnimation;
    [Tooltip("��Ծ��Ϣ����غ󣩶���")]
    public string jumpRestAnimation;
    [Tooltip("��Ծ����ʱ��Ч����һ��")]
    public bool jumpEffectOnlyFirst = false;
    [Tooltip("��Ծ������ЧPrefab�����ӻ�����֡�Կɣ�")]
    public GameObject jumpEffectPrefab;
    [Tooltip("��Ծ��Ϣ��Ч����һ��")]
    public bool jumpRestEffectOnlyFirst = false;
    [Tooltip("��Ծ��Ϣ��ЧPrefab�����ӻ�����֡�Կɣ�")]
    public GameObject jumpRestEffectPrefab;

    

    [Header("ִ�д���")]
    [Tooltip("ִ�д�����0��ʾ����ѭ����ģʽ)")]
    public int executeCount = 0;
}

/// <summary>
/// ���ֽ׶����ã����棩
/// </summary>
[System.Serializable]
public class DiscoveryConfig
{
    [Tooltip("��Ҿ�����루����÷�Χ�򴥷����ֽ׶Σ�")]
    public float alertRange;
    [Tooltip("���ֽ׶��ƶ�ģʽ�б�")]
    public List<DiscoveryMovement> movements = new List<DiscoveryMovement>();
    [Tooltip("ģʽ�Ƿ�����л�")]
    public bool randomOrder = false;
    [Tooltip("ģʽ��������Ƿ�ѭ����false��ÿ��ģʽִ��һ�κ�������ֽ׶Σ�")]
    public bool loopAll = true;
}

/// <summary>
/// ���ֽ׶ε����ƶ�����
/// </summary>
[System.Serializable]
public class DiscoveryMovement
{
    [Tooltip("���ͣ�ֱ��׷�������־��롢��Ծ׷��")]
    public DiscoveryMovementType type;

    // ֱ��׷������
    [Tooltip("׷���ƶ��ٶ�")]
    public float chaseSpeed;
    [Tooltip("׷�����ٶ�")]
    public float acceleration;
    [Tooltip("׷�����ٶ�")]
    public float deceleration;
    [Tooltip("�����ƶ�ʱ��")]
    public float moveDuration;
    [Tooltip("��ͣ��Ϣʱ��")]
    public float restDuration;
   

    // ���־������
    [Tooltip("���־�����������")]
    public float minDistance;
    [Tooltip("���־������Զ����")]
    public float maxDistance;
    [Tooltip("ά�ָþ����ʱ��")]
    public float maintainTime;
    [Tooltip("���־���ʱ���ƶ��ٶ�")]
    public float maintainSpeed;

    // ��Ծ׷������
    [Tooltip("��Ծ׷���ٶ�")]
    public float jumpSpeed;
    [Tooltip("��Ծ�߶�")]
    public float jumpHeight;
    [Tooltip("����ϵ��")]
    public float gravityScale;
    [Tooltip("������Ծʱ��")]
    public float jumpDuration;
    [Tooltip("��Ծ����Ϣʱ��")]
    public float jumpRestDuration;
 

    [Tooltip("��������")]
    public string animation;
    [Tooltip("��Ч����")]
    public string effect;
    [Tooltip("ִ�д��� (0��ʾ���޳���)")]
    public int executeCount = 0;
}

/// <summary>
/// �����׶����ã����԰������ֹ�����ʽ��
/// </summary>
[System.Serializable]
public class AttackConfig
{
    [Tooltip("�����������루��ҽ���˾��뿪ʼ������")]
    public float attackRange;
    [Tooltip("�����Ĺ���ģʽ�б�")]
    public List<AttackPattern> attackPatterns = new List<AttackPattern>();
    [Tooltip("���ֹ�����ʽʱ�Ƿ����ѡ��false��˳��ѭ����")]
    public bool randomOrder = false;
}

/// <summary>
/// ����ģʽ���ã���ս��Զ�̡���ײ��������
/// </summary>
[System.Serializable]
public class AttackPattern
{
    [Tooltip("�������ͣ���ս��Զ�̡���ײ������")]
    public AttackType type;

    [Tooltip("������������")]
    public string animation;
    [Tooltip("������Ч����")]
    public string effect;
    [Tooltip("�˺�ֵ")]
    public int damage;
    [Tooltip("һ�ι��������е�������������")]
    public int repeatCount = 1;
    [Tooltip("�����������ʱ��")]
    public float repeatInterval = 0;
    [Tooltip("��������ʱ�Ƿ�������ǰһ����")]
    public bool interruptPreviousAnimation = false;

    // ��ս���в���
    [Tooltip("��ս�������ж������Χ")]
    public float meleeRange;
    [Tooltip("��ս�����Ƿ�ɱ���Ҷ��Ƹ�")]
    public bool meleeBlockable = true;

    // Զ�����в���
    [Tooltip("Ͷ����Prefab��Զ�̹�����")]
    public GameObject projectilePrefab;
    [Tooltip("Ͷ�����ٶ�")]
    public float projectileSpeed;
    [Tooltip("��������Ͷ�������")]
    public int projectileCount = 1;
    [Tooltip("ÿ�η�����")]
    public float projectileInterval = 0;
    [Tooltip("Ͷ�����������룩")]
    public float projectileLifetime = 5f;
    [Tooltip("Ͷ�����Ƿ�ɱ����Ƹ�")]
    public bool projectileBlockable = true;

    // ��ײ�˺����в���
    [Tooltip("��ײ����˺�����ȴʱ��")]
    public float collisionDamageCooldown = 1f;

    // �������в���
    [Tooltip("��������ʱ��")]
    public float defenseDuration;
    [Tooltip("�����ڼ��Ƿ��޵�")]
    public bool invulnerableDuringDefense = true;
    [Tooltip("����״̬��Ч����")]
    public string defenseEffect;
}

/// <summary>
/// ����Ѳ�߽׶����ã�����ģʽ��
/// </summary>
[System.Serializable]
public class AirPatrolConfig
{
    [Tooltip("����Ѳ���ƶ�ģʽ�б�")]
    public List<AirMove> moves = new List<AirMove>();
    [Tooltip("�ƶ�ģʽ�Ƿ�����л�")]
    public bool randomOrder = false;
    [Tooltip("ģʽ����Ƿ�ѭ��ִ��")]
    public bool loopAll = true;
}

/// <summary>
/// ����Ѳ�ߵ����ƶ�����
/// </summary>
[System.Serializable]
public class AirMove
{
    [Tooltip("�����ٶ�")]
    public float speed;
    [Tooltip("���г���ʱ��")]
    public float moveDuration;
    [Tooltip("��ͣ��Ϣʱ��")]
    public float hoverDuration;
    [Tooltip("���ж�������")]
    public string flyAnimation;
    [Tooltip("������Ч����")]
    public string flyEffect;
}

/// <summary>
/// ���з��ֽ׶�����
/// </summary>
[System.Serializable]
public class AirDiscoveryConfig
{
    [Tooltip("���й��ﾯ�䷶Χ")]
    public float alertRange;
    [Tooltip("���ֽ׶η���ģʽ�б�")]
    public List<AirMove> moves = new List<AirMove>();
    [Tooltip("ģʽ�Ƿ�����л�")]
    public bool randomOrder = false;
    [Tooltip("ģʽ����Ƿ�ѭ��")]
    public bool loopAll = true;
    [Tooltip("����ұ��־������Сֵ")]
    public float minDistance;
    [Tooltip("����ұ��־�������ֵ")]
    public float maxDistance;
    [Tooltip("ʼ�ճ������")]
    public bool facePlayer = true;
}

/// <summary>
/// �����׶�����
/// </summary>
[System.Serializable]
public class DeathConfig
{
    [Tooltip("������������")]
    public string deathAnimation;
    [Tooltip("������Ч����")]
    public string deathEffect;
    [Tooltip("�����Ƿ񴥷���ը�˺�")]
    public bool explosiveDeath;
    [Tooltip("��ը��Χ�뾶")]
    public float explosionRadius;
    [Tooltip("��ը�˺�ֵ")]
    public int explosionDamage;
    [Tooltip("�����������Ƴ�������򲥷��궯�����Ƴ���")]
    public bool instantRemove;
}

/// <summary>��������ö��</summary>
public enum Orientation { FacePlayer, FaceLeft, FaceRight }
/// <summary>Ѳ���ƶ�����</summary>
public enum MovementType { Straight, Jump }
/// <summary>���ֽ׶��ƶ�����</summary>
public enum DiscoveryMovementType { DirectChase, MaintainDistance, JumpChase }
/// <summary>��������</summary>
public enum AttackType { Melee, Ranged, Collision, Defend }
/// <summary>����λ������</summary>
public enum SpawnPositionType { Points, Area }