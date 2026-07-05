namespace Brushblade.Core
{
    /// <summary>字怪定义(第 8 章;首版仅基础攻击,特殊能力后续扩展)。</summary>
    public sealed class EnemyDef
    {
        public string Id { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }

        public EnemyDef(string id, Element element, int maxHp, int attack)
        {
            Id = id;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
        }
    }

    /// <summary>战斗中的字怪状态。</summary>
    public sealed class EnemyState
    {
        public EnemyDef Def { get; }
        public int Hp { get; internal set; }
        public int Burn { get; internal set; }
        public bool Alive => Hp > 0;

        internal EnemyState(EnemyDef def)
        {
            Def = def;
            Hp = def.MaxHp;
        }
    }
}
