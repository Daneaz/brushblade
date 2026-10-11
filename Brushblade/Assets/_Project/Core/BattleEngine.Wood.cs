using System;

namespace Brushblade.Core
{
    /// <summary>木系专属机制(Plan D2-木)。Task 1 = 共用小扩展里需要读木灵编队的那几项:E40 按木灵攻击 / 护甲加伤、
    /// E41 幼苗取本体、E46 柘弓只让本字最强木灵出手、E50 条件 OwnSummonAlive。Task 2 = 光环族:M1 玩家袋光环、
    /// M2 本命在场光环(林荫 / 森严 / 蟾宫)、馥郁读点。挂点处只留一行调用,实现放这里。
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
            ClearSlotAuras(slot);   // M1:按槽位挂的光环随木灵阵亡清理(Review Focus 1)
        }

        // ---- M1 木灵光环(Task 2):玩家袋 SummonEmpower / SummonThorns / SummonRanged,按玩家回合计(Q7) ----
        // TargetSlot −1 = 全部木灵(含之后召出的);≥0 = 那一只。同一只取最强(Q6)。读点都以袋里 Has 一次判断短路。

        private static bool IsSummonAura(StatusKind kind) =>
            kind == StatusKind.SummonEmpower || kind == StatusKind.SummonThorns || kind == StatusKind.SummonRanged;

        /// <summary>挂一条光环。turns ≤ 0 = 本场(TurnsLeft −1)。SourceId = 字 ID:同源同槽刷新(StatusBag.Apply 按 TargetSlot 区分)。</summary>
        private void ApplySummonAura(StatusKind kind, int magnitude, int turns, string sourceId, int slot)
        {
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = magnitude,
                TurnsLeft = turns > 0 ? turns : -1, SourceId = sourceId, TargetSlot = slot,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>林立 / 桂冠 / 擎天(M1):按 pick 挂攻击光环。AllSummons = 一条 −1;OtherSummons(Q22)= 出字时在场、
        /// 不在本次召出名单里的存活木灵逐只挂;StrongestSummon = 有效攻击最高的存活木灵(同值取槽位小),
        /// 此分支已被推迟到本面全部效果之后(IsRosterPick),含本次召出的。不摇号。</summary>
        private void ApplySummonEmpower(EffectDef effect, string sourceId)
        {
            switch (effect.Pick)
            {
                case EffectPick.AllSummons:
                    ApplySummonAura(StatusKind.SummonEmpower, effect.Value, effect.Turns, sourceId, -1);
                    break;
                case EffectPick.OtherSummons:
                    for (int s = 0; s < _summons.Length; s++)
                        if (_summons[s] != null && _summons[s].Alive && !_cast.SummonedSlots.Contains(s)
                            && !_cast.EntrySaplingSlots.Contains(s))   // 森然入场幼苗是本次召唤的一部分(fix round 1 裁定)
                            ApplySummonAura(StatusKind.SummonEmpower, effect.Value, effect.Turns, sourceId, s);
                    break;
                case EffectPick.StrongestSummon:
                {
                    RefreshSummonAura();   // 比的是这一刻的有效攻击(含光环乘区)
                    int best = -1;
                    for (int s = 0; s < _summons.Length; s++)
                        if (_summons[s] != null && _summons[s].Alive
                            && (best < 0 || _summons[s].EffectiveAttack > _summons[best].EffectiveAttack))
                            best = s;
                    if (best >= 0) ApplySummonAura(StatusKind.SummonEmpower, effect.Value, effect.Turns, sourceId, best);
                    break;
                }
            }
            RefreshSummonAura();
        }

        /// <summary>该槽位木灵吃到的光环量:TargetSlot −1 或 == slot 的条目取最大。调用方先用 Has 短路。</summary>
        private int SummonAuraOf(StatusKind kind, int slot)
        {
            int max = 0;
            foreach (var st in _playerStatuses.All)
                if (st.Kind == kind && (st.TargetSlot < 0 || st.TargetSlot == slot) && st.Magnitude > max) max = st.Magnitude;
            return max;
        }

        /// <summary>该槽位有没有远程光环(箭灵,Q1)。</summary>
        private bool HasRangedAura(int slot)
        {
            if (!_playerStatuses.Has(StatusKind.SummonRanged)) return false;
            foreach (var st in _playerStatuses.All)
                if (st.Kind == StatusKind.SummonRanged && (st.TargetSlot < 0 || st.TargetSlot == slot)) return true;
            return false;
        }

        /// <summary>荆棘百分比 = max(本命荆棘, 荆棘光环)(Q12);没有光环时就是本命,恒等。</summary>
        private int ThornsPercentOf(int slot, SummonState summon)
        {
            int own = summon.Passive?.Thorns ?? 0;
            return _playerStatuses.Has(StatusKind.SummonThorns) ? Math.Max(own, SummonAuraOf(StatusKind.SummonThorns, slot)) : own;
        }

        /// <summary>某槽位换了单位或阵亡:清掉指向它的按槽光环(TargetSlot == slot)。没有光环时一次遍历即返回。</summary>
        private void ClearSlotAuras(int slot)
        {
            var all = _playerStatuses.All;
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].TargetSlot == slot && IsSummonAura(all[i].Kind)) _playerStatuses.RemoveEntry(all[i]);
        }

        /// <summary>攻击光环注入值(RefreshSummonAura 调用;无光环的情形调用方已短路为 0)。</summary>
        private int EmpowerPercentOf(int slot) => SummonAuraOf(StatusKind.SummonEmpower, slot);

        // ---- M2 本命在场光环(Task 2):实时读存活木灵的本命 ----

        /// <summary>蟾宫(Q23):存活木灵本命 CritAura 的最大值;没有 = 0。玩家 EffectiveCrit 与木灵 RollCritForSummon 都加它 ——
        /// 木灵本来没有暴击通道,0 时木灵暴击仍走下端短路、不摇号(Ruling 18 闸门)。</summary>
        private int SummonCritAura()
        {
            int max = 0;
            foreach (var s in _summons)
                if (s != null && s.Alive && s.Passive != null && s.Passive.CritAura > max) max = s.Passive.CritAura;
            return max;
        }

        /// <summary>林荫 / 森严(Q8):本命光环的减伤百分比,与 DamageCut 取最强后再加格挡、钳 60%。
        /// 林荫 = min(GroveCutCap, GroveCut × 存活木灵数)(0 = 不设上限),我方全体;森严 = 存活木灵数 ≥ ThicketMin 时 ThicketCut,
        /// 只给木灵(<paramref name="forSummon"/>)。多只取最强。没有带这两个字段的存活木灵时返回 0(恒等)。</summary>
        private int WoodCutPercent(bool forSummon)
        {
            int alive = 0;
            bool any = false;
            foreach (var s in _summons)
            {
                if (s == null || !s.Alive) continue;
                alive++;
                if (s.Passive != null && (s.Passive.GroveCut > 0 || (forSummon && s.Passive.ThicketCut > 0))) any = true;
            }
            if (!any) return 0;
            int best = 0;
            foreach (var s in _summons)
            {
                var p = s != null && s.Alive ? s.Passive : null;
                if (p == null) continue;
                if (p.GroveCut > 0)
                {
                    int grove = p.GroveCut * alive;
                    if (p.GroveCutCap > 0) grove = Math.Min(p.GroveCutCap, grove);
                    best = Math.Max(best, grove);
                }
                if (forSummon && p.ThicketCut > 0 && alive >= p.ThicketMin) best = Math.Max(best, p.ThicketCut);
            }
            return best;
        }

        /// <summary>馥郁(HealAllyPercent):桂香治疗量 × (100 + N)/100;缺省 0 原值。</summary>
        private static int HealAllyAmountOf(SummonPassive passive) =>
            passive.HealAllyPercent > 0 ? passive.HealAlly * (100 + passive.HealAllyPercent) / 100 : passive.HealAlly;

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

        /// <summary>E43:点尸体复活(方法头的前置复活)用本面 Fold 后首条 Revive 的血量百分比 —— 与随后 Revive 分支结算的
        /// 是同一份效果表(Lv3 替换 / 特性追加都算)。fix round 1:原先读未 Fold 的本体。</summary>
        private static int RevivePercentOf(CharDef def, bool attackMode, int cardLevel)
        {
            foreach (var effect in CastEffectsOf(def, attackMode, cardLevel))
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
