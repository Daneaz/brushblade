using System;

namespace Brushblade.Core
{
    /// <summary>金系专属机制(Plan D2-金)。Task 1 = 共用小扩展:E8 流血合并、E9 补满、E10 计数缩放(战意 / 多命中)、
    /// E12 格挡修饰器的出字时字段、E13 按被杀者最大生命回复。ApplyEffects 的 switch 只留一行分派,实现放这里。
    ///
    /// 恒等:这些分支只在新字段非缺省(Fill / ScaleBy / CounterPercent / OfVictimMaxHp)时才改变结果;一律不摇随机数。
    /// 流血合并改的是「后上的无条件覆盖先上的」—— 现行字表与 trace 卡池都没有流血数据。</summary>
    public sealed partial class BattleEngine
    {
        private int MoraleStacks => _playerStatuses.TotalMagnitude(StatusKind.Morale);

        private int MoraleCapOrDefault => _config?.MoraleCap ?? CombatCaps.MoraleStacks;

        /// <summary>DamageSingle 主目标的击数(E10,大卸八块):ScaleBy == Morale 时 = HitCount + 战意层数(进 DamageSingle 时取一次)。</summary>
        private int HitCountOf(EffectDef effect) =>
            effect.ScaleBy == ScaleBasis.Morale ? effect.HitCount + MoraleStacks : effect.HitCount;

        /// <summary>格挡次数(E10 / E12,双金合璧):ScaleBy == Morale 时 = max(ScaleMin, 结算那一刻的战意);否则 Value。
        /// 本面 Morale 排在 Block 之前时读到的是出字后的值(Q21,产出量)。</summary>
        private int BlockCountOf(EffectDef effect) =>
            effect.ScaleBy == ScaleBasis.Morale ? Math.Max(effect.ScaleMin, MoraleStacks) : effect.Value;

        /// <summary>格挡反击百分比(E12,剑意 / 千锤 / 金刚):CounterPercent &gt; 0 覆盖缺省 30%。</summary>
        private static int BlockCounterPercentOf(EffectDef effect) =>
            effect.CounterPercent > 0 ? effect.CounterPercent : BattleConfig.BlockCounterPercent;

        /// <summary>Morale 分支(E9 / E10):补满 = 战意直接设为上限(顶满不发事件,不算溢出);
        /// ExtraHitTarget = 值 × (本次出字命中过的敌人数 − 1),0 层不挂。其余照旧累加并钳到上限。</summary>
        private void ResolveMorale(EffectDef effect, int value)
        {
            int cap = MoraleCapOrDefault;
            if (effect.Fill)
            {
                AddPlayerCounter(StatusKind.Morale, cap, cap);
                return;
            }
            if (effect.ScaleBy == ScaleBasis.ExtraHitTarget)
            {
                value *= Math.Max(0, _cast.HitTargets.Count - 1);
                if (value <= 0) return;
            }
            AddPlayerCounter(StatusKind.Morale, value, cap);
        }

        /// <summary>流血合并(E8,Q12):不论 SourceId / TraitKey,同一单位身上只留一条 —— 量取大、回合取长。
        /// 由 ApplyStatus 调用:先并掉袋子里已有的全部流血,再由 bag.Apply 放入新条。</summary>
        private static void MergeBleed(StatusBag bag, StatusEffect effect)
        {
            for (int i = bag.All.Count - 1; i >= 0; i--)
            {
                var old = bag.All[i];
                if (old.Kind != StatusKind.Bleed) continue;
                effect.Magnitude = Math.Max(effect.Magnitude, old.Magnitude);
                effect.TurnsLeft = Math.Max(effect.TurnsLeft, old.TurnsLeft);
                bag.RemoveEntry(old);
            }
        }

        /// <summary>按被杀者最大生命回复的基数(E13,割取):反应目标(死者)MaxHp × Value%(不吃卡等级 / 五行 L3 / 攻击力,Q19),
        /// 再吃本条的 Amplify Heal(无同轴项)。目标下标无效时 0。</summary>
        private int VictimHealBase(EffectDef effect, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _enemies.Count) return 0;
            int heal = (int)((long)_enemies[targetIndex].MaxHp * effect.Value / 100);
            return effect.AmpTerms.Count == 0 ? heal : Amplified(heal, AmpPercent(effect, targetIndex));
        }
    }
}
