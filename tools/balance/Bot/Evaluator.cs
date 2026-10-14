using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>局面打分(spec §3)。只读本体;<see cref="Full"/> 的敌人回合只在副本上跑。</summary>
    public static class Evaluator
    {
        public const double WinScore = 1_000_000;
        public const double LossScore = -1_000_000;
        public const double DamageWeight = 100;     // 敌人损失血量占比 × 100
        public const double KillBonus = 50;         // 每打倒一只
        public const double SurvivalHigh = 80;      // 玩家血线 > 50% 时的 W
        public const double SurvivalLow = 200;      // 玩家血线 ≤ 50% 时的 W
        public const double StackValue = 3;         // 厚 / 泉 / 战意 每层

        /// <summary>快速打分(spec §3.2):不跑敌人回合,按当前局面记分。只给剪枝排序用。</summary>
        public static double Quick(BattleEngine b)
        {
            if (b.Phase == BattlePhase.Won) return WinScore - b.Turn;
            if (b.Phase == BattlePhase.Lost) return LossScore;
            return Terms(b);
        }

        /// <summary><see cref="Quick"/> 去掉胜负两档后的「进攻 + 生存 + 蓄能」本身。局外估值用它:
        /// 参考局面里一张字清场时,按打掉的血与击杀记分,而不是记成 1e6 的「已赢」。</summary>
        public static double Terms(BattleEngine b)
        {
            double offense = 0;
            foreach (var e in b.Enemies)
            {
                if (e.MaxHp > 0) offense += (double)(e.MaxHp - Math.Max(0, e.Hp)) / e.MaxHp * DamageWeight;
                if (!e.Alive) offense += KillBonus;
            }

            int maxHp = b.MaxHp;
            double w = b.PlayerHp * 2 > maxHp ? SurvivalHigh : SurvivalLow; // 血线(不含盾)> 50%
            double survival = maxHp > 0 ? (double)(b.PlayerHp + b.PlayerShield) / maxHp * w : 0;
            foreach (var s in b.Summons)
                if (s != null && s.Alive && s.MaxHp > 0)
                    survival += (double)(s.Hp + s.Shield) / s.MaxHp * (w / 2);

            var bag = b.PlayerStatuses;
            int stacks = bag.TotalMagnitude(StatusKind.Heft) + bag.TotalMagnitude(StatusKind.Wellspring)
                         + bag.TotalMagnitude(StatusKind.Morale);

            return offense + survival + stacks * StackValue;
        }

        /// <summary>完整打分(spec §3.1):在换了固定种子的副本上「结束回合 → 敌人回合 → 停在下一个玩家回合开始」
        /// 再 <see cref="Quick"/>。副本 RNG 只由 <paramref name="leafSeed"/> 派生,与本体 RNG 无关(spec §2.4)。
        /// 下一回合开头若因满库挂起掉字(DropChoice),副本里一律 <see cref="BattleEngine.SkipDrop"/>:
        /// 字库不进打分,跳过不改分数,只为让相位回到玩家回合。
        /// 副本置 <see cref="BattleEngine.SuppressCritRolls"/>:玩家侧暴击按不暴击算(与搜索副本同口径)。</summary>
        public static double Full(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, ulong leafSeed)
        {
            var copy = CloneWithSeed(b, g, cfg, cardLevels, leafSeed);
            copy.SuppressCritRolls = true; // spec §2.3:副本暴击按不暴击算(必暴照算);不进快照,克隆后显式置位
            if (copy.Phase == BattlePhase.DropChoice) copy.SkipDrop();
            if (copy.Phase == BattlePhase.PlayerTurn) copy.EndTurn(); // 内部推进到下一个玩家回合开始或分出胜负
            if (copy.Phase == BattlePhase.DropChoice) copy.SkipDrop();
            return Quick(copy);
        }

        /// <summary>经 <c>Capture → Restore</c> 复制出独立副本。<paramref name="seed"/> 非空时把三条随机流
        /// (主流 / 择敌流 / 特性流)替换成由它派生的固定状态;为空时照抄本体的随机流。
        /// 敌人定义从本体的 <see cref="EnemyState.Def"/> 收集(分裂克隆与本体共用一个 Def)。</summary>
        public static BattleEngine CloneWithSeed(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, ulong? seed)
        {
            var snap = b.Capture();
            if (seed.HasValue)
            {
                ulong x = seed.Value;
                snap.RandomState = NonZero(SplitMix64(ref x));
                snap.TargetRandomState = NonZero(SplitMix64(ref x));
                snap.TraitRandomState = NonZero(SplitMix64(ref x));
            }
            return BattleEngine.Restore(snap, g, cfg, cardLevels, EnemyDefsOf(b));
        }

        public static Dictionary<string, EnemyDef> EnemyDefsOf(BattleEngine b)
        {
            var defs = new Dictionary<string, EnemyDef>(StringComparer.Ordinal);
            foreach (var e in b.Enemies) defs[e.Def.Id] = e.Def;
            return defs;
        }

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                ulong z = x += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // xorshift 的 0 是吸收态;TraitRandomState == 0 还会被 Restore 当成「旧快照缺字段」
        private static uint NonZero(ulong v)
        {
            uint u = unchecked((uint)(v ^ (v >> 32)));
            return u == 0 ? 0x9E3779B9u : u;
        }
    }
}
