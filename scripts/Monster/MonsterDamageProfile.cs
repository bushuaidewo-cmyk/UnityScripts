using UnityEngine;

[DisallowMultipleComponent]
public class MonsterDamageProfile : MonoBehaviour
{
    [Header("怪物伤害（本体/近战/飞行物/爆炸）")]
    [SerializeField] private int bodyDamage = 5;            // 贴身伤害
    [SerializeField] private int meleeDamage = 10;           // 武器近战命中体伤害
    [SerializeField] private int projectileDamage = 8;       // 飞行物命中伤害

    public int BodyDamage => Mathf.Max(0, bodyDamage);
    public int MeleeDamage => Mathf.Max(0, meleeDamage);
    public int ProjectileDamage => Mathf.Max(0, projectileDamage);
    public void Apply(int body, int melee, int proj)
    {
        bodyDamage = body;
        meleeDamage = melee;
        projectileDamage = proj;
    }
}