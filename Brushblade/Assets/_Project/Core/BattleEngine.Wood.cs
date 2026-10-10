using System;

namespace Brushblade.Core
{
    /// <summary>木系专属机制(Plan D2-木)。Task 1 = 共用小扩展里需要读木灵编队的那几项:E40 按木灵攻击 / 护甲加伤、
    /// E41 幼苗取本体、E46 柘弓只让本字最强木灵出手、E50 条件 OwnSummonAlive。挂点处只留一行调用,实现放这里。
    ///
    /// 「本字木灵」(Q2)= SourceChar == 本字 且 Char != 苗 的存活木灵;嫁接不改变身份。
    /// 恒等:各项只在对应字段 / 条件非缺省时才走到,一律不摇随机数。</summary>
    public sealed partial class BattleEngine
    {
        // ---- 预留骨架(Ruling 17):木系新机制 M1–M11 的挂点集中在这里,Task 2–4 逐项填 ----

        /// <summary>木灵阵亡的特性挂点(M3 秋实 / 落木 / 藻荇、M7 攀附清理等,Task 3 / 4):OnSummonDeath 的单点调用,
        /// 在归根治疗之后、刷光环之前。顶替召唤与绿潮并入不走这里(Q15)。Task 1 只预留,空实现。</summary>
        private void OnSummonDeathTraits(int slot, UnitRef killer)
        {
        }

        /// <summary>该槽位是不是本字的木灵(Q2:主动特性读的「X灵」;幼苗不算)。</summary>
        private bool IsOwnSummon(int slot, string charId)
        {
            var s = _summons[slot];
            return s != null && s.Alive && s.SourceChar == charId && s.Char != SaplingChar;
        }

        /// <summary>条件 OwnSummonAlive(E50):本字木灵在场。charId 为 null(出字之外)时不满足。</summary>
        private bool OwnSummonAlive(string charId)
        {
            if (charId == null) return false;
            for (int s = 0; s < _summons.Length; s++)
                if (IsOwnSummon(s, charId)) return true;
            return false;
        }

        /// <summary>本字木灵里 <paramref name="measure"/> 最大的一只的槽位(同值取槽位小);不在场 −1。</summary>
        private int StrongestOwnSummon(string charId, Func<SummonState, int> measure)
        {
            int best = -1, bestValue = int.MinValue;
            for (int s = 0; s < _summons.Length; s++)
            {
                if (!IsOwnSummon(s, charId)) continue;
                int v = measure(_summons[s]);
                if (v > bestValue) { best = s; bestValue = v; }
            }
            return best;
        }

        /// <summary>E40(森罗 / 撼天 / 硬弓):每个主目标第一段的额外伤害 = Σ 存活木灵有效攻击 × a% + 本字木灵有效攻击最大值 × b%
        /// + 本字木灵护甲(SummonDefense,含富甲)最大值 × c%。结算那一刻读,先刷光环乘区。调用方保证三项不全为 0。</summary>
        private int SummonStrikeBonus(EffectDef effect, string charId)
        {
            if (effect.SummonAtkStrikePercent > 0 || effect.HostAtkStrikePercent > 0) RefreshSummonAura();
            int bonus = 0;
            if (effect.SummonAtkStrikePercent > 0)
            {
                int sum = 0;
                foreach (var s in _summons)
                    if (s != null && s.Alive) sum += s.EffectiveAttack;
                bonus += sum * effect.SummonAtkStrikePercent / 100;
            }
            if (effect.HostAtkStrikePercent > 0)
            {
                int host = StrongestOwnSummon(charId, s => s.EffectiveAttack);
                if (host >= 0) bonus += _summons[host].EffectiveAttack * effect.HostAtkStrikePercent / 100;
            }
            if (effect.HostArmorStrikePercent > 0)
            {
                int host = StrongestOwnSummon(charId, SummonDefense);
                if (host >= 0) bonus += SummonDefense(_summons[host]) * effect.HostArmorStrikePercent / 100;
            }
            return bonus;
        }

        /// <summary>E43:点尸体复活(方法头的前置复活)用本面首条 Revive 的血量百分比;口径同 <see cref="HasRevive"/>(读本体)。</summary>
        private static int RevivePercentOf(CharDef def, bool attackMode)
        {
            foreach (var effect in EffectsOf(def, attackMode))
                if (effect.Kind == EffectKind.Revive) return effect.ReviveHpPercent;
            return 0;
        }

        /// <summary>E41(双木 `fromBody`):本字生面本体首条 Summon 的血(按卡等级)与攻(ScaleSummonAttack(卡等级))。
        /// 反应里的 CharDef 是合成的,按字 ID 回图谱取真身;取不到 / 生面没有 Summon 返回 false。</summary>
        private bool BodySummonStats(CharDef def, int cardLevel, out int hp, out int attack)
        {
            hp = attack = 0;
            if (!_graph.TryGet(def.Id, out var real)) real = def;
            foreach (var e in EffectsOf(real, false))
            {
                if (e.Kind != EffectKind.Summon) continue;
                hp = MetaRules.ScaleEffectValue(EffectKind.Summon, e.Value, cardLevel);
                attack = ScaleSummonAttack(MetaRules.ScaleByCardLevel(e.SummonAttack, cardLevel));
                return true;
            }
            return false;
        }
    }
}
