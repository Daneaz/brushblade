using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>字怪特殊能力(第 8 章 8.3):骚扰拆合/压迫机制。</summary>
    public enum EnemyAbility
    {
        None,
        Regrow, // 缺笔妖:每敌方回合自补全(攻+2/回3血),第 3 次补全完成(攻×2、血回满)
        Split,  // 叠字怪:首次受击存活后分裂成两个半血(场上敌人 <4 时)
    }

    /// <summary>成语 Boss 的单个阶段(8.5:四字成语,四个字 = 四个阶段)。</summary>
    public sealed class BossPhaseDef
    {
        public string Char { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        /// <summary>承伤系数(如「山」0.5 = 超高防御),向下取整。</summary>
        public float DamageTaken { get; }

        public BossPhaseDef(string phaseChar, Element element, int maxHp, int attack, float damageTaken = 1f)
        {
            Char = phaseChar;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            DamageTaken = damageTaken;
        }
    }

    /// <summary>字怪定义(第 8 章)。Phases 非空即成语 Boss,首阶段覆盖基础数值。</summary>
    public sealed class EnemyDef
    {
        public string Id { get; }
        public Element Element { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        public EnemyAbility Ability { get; }
        public IReadOnlyList<BossPhaseDef> Phases { get; }

        public EnemyDef(string id, Element element, int maxHp, int attack,
            EnemyAbility ability = EnemyAbility.None, IReadOnlyList<BossPhaseDef> phases = null)
        {
            Id = id;
            Element = element;
            MaxHp = maxHp;
            Attack = attack;
            Ability = ability;
            Phases = phases ?? System.Array.Empty<BossPhaseDef>();
        }
    }

    /// <summary>战斗中的字怪状态。成语 Boss 的当前阶段覆盖属性/攻击/上限。</summary>
    public sealed class EnemyState
    {
        public EnemyDef Def { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; internal set; }          // 当前阶段上限
        public Element Element { get; internal set; }    // 当前属性(Boss 换阶段会变)
        public int Burn { get; internal set; }
        public int Attack { get; internal set; }         // 当前攻击(缺笔妖会成长)
        public float DamageTaken { get; internal set; } = 1f; // 承伤系数(「山」阶段 0.5)
        public int PhaseIndex { get; internal set; }     // 成语 Boss 当前阶段(0 起)
        public int RegrowProgress { get; internal set; } // 补全进度 0~3
        public bool HasSplit { get; internal set; }
        public bool Alive => Hp > 0;
        public bool IsBoss => Def.Phases.Count > 0;

        internal EnemyState(EnemyDef def)
        {
            Def = def;
            if (def.Phases.Count > 0)
                EnterPhase(0);
            else
            {
                Hp = def.MaxHp;
                MaxHp = def.MaxHp;
                Element = def.Element;
                Attack = def.Attack;
            }
        }

        internal void EnterPhase(int index)
        {
            var phase = Def.Phases[index];
            PhaseIndex = index;
            Hp = phase.MaxHp;
            MaxHp = phase.MaxHp;
            Element = phase.Element;
            Attack = phase.Attack;
            DamageTaken = phase.DamageTaken;
            Burn = 0; // 新字新体,灼烧清零
        }
    }
}
