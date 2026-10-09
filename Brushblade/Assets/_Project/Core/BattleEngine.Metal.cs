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

        // ---- Task 2:格挡附带运行时(附录 J1 / J2)----

        /// <summary>贯穿反击打同列其余目标的百分比(锥立「其余目标 70%」)。</summary>
        public const int CounterColumnPercent = 70;

        /// <summary>出字时把 Block 条目上的格挡附带搬进状态(J1)。流血量此刻按卡等级与攻击力定死(Q12);
        /// 立威记下施加者字 ID(Task 3 铁则回查)。全缺省时什么都不写 —— 状态与原格挡逐位相同。</summary>
        private void CarryBlockRiders(StatusEffect block, EffectDef effect, string charId, int cardLevel)
        {
            block.CounterColumn = effect.CounterColumn;
            block.CounterHits = effect.CounterHits;
            block.CounterExecuteBelow = effect.CounterExecuteBelow;
            block.BlockBleed = effect.BlockBleed > 0
                ? ScaleByAttack(MetaRules.ScaleByCardLevel(effect.BlockBleed, cardLevel)) : 0;
            block.BlockMorale = effect.BlockMorale;
            block.KillRefundAp = effect.KillRefundAp;
            block.ExecuteSourceCharId = effect.CounterExecuteBelow > 0 ? charId : null;
        }

        /// <summary>格挡同类合并的附带部分(Q4):数值取大、开关取并;<see cref="StatusEffect.ExecuteSourceCharId"/>
        /// 跟最近一次带立威的施加(新条没有立威就沿用旧条的)。<paramref name="incoming"/> 是将要放进袋子的新条。</summary>
        private static void MergeBlockRiders(StatusEffect incoming, StatusEffect existing)
        {
            incoming.CounterColumn |= existing.CounterColumn;
            incoming.CounterHits = Math.Max(incoming.CounterHits, existing.CounterHits);
            incoming.CounterExecuteBelow = Math.Max(incoming.CounterExecuteBelow, existing.CounterExecuteBelow);
            incoming.BlockBleed = Math.Max(incoming.BlockBleed, existing.BlockBleed);
            incoming.BlockMorale = Math.Max(incoming.BlockMorale, existing.BlockMorale);
            incoming.KillRefundAp = Math.Max(incoming.KillRefundAp, existing.KillRefundAp);
            incoming.ExecuteSourceCharId ??= existing.ExecuteSourceCharId;
        }

        /// <summary>格挡被消耗后的结算(玩家侧 DamagePlayerDirect 与木灵侧 DamageSummon 共用;排在镜 / 荆棘之后、反震之前)。
        /// <paramref name="block"/> = 这一下消耗掉的格挡(没格挡 = null,直接返回 0);<paramref name="counter"/> = 每击反击量
        /// (CounterDamage 已乘反击增强);<paramref name="budget"/> = 60% 反伤预算扣掉先结算的镜 / 荆棘之后的余额。
        ///
        /// 顺序:格挡加战意 → 攻击者流血(两者不是伤害,不占预算,预算为 0 也给)→ 立威判血(杂兵斩杀,不吃预算,Q3;
        /// Boss 改本次反击 ×2)→ 攻击者 N 击 → 贯穿时同列其余存活敌人(下标序)各 N 击 × 70%。
        /// 所有伤害击共用一份预算,扣完即止(Q2)。反击 / 立威造成击杀且带 KillRefundAp 时挂 ApRefund(J2)。
        /// 返回实际打出的反击伤害合计(反震从同一份预算里扣它)。附带字段全缺省时与原反击段逐位相同、不摇随机数。</summary>
        private int ResolveCounter(int enemyIndex, StatusEffect block, int counter, int budget, UnitRef attackerRef)
        {
            if (block == null) return 0;
            var attacker = _enemies[enemyIndex];
            if (block.BlockMorale > 0) AddPlayerCounter(StatusKind.Morale, block.BlockMorale, MoraleCapOrDefault);
            if (block.BlockBleed > 0 && attacker.Alive)
                ApplyStatus(attacker.Statuses, new StatusEffect
                {
                    Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                    Magnitude = block.BlockBleed, TurnsLeft = 3,   // Q12 缺省 3 回合;量出字时已定死
                }, UnitRef.Enemy(enemyIndex), attackerRef);

            bool killed = false;
            int multiplier = 1;
            if (block.CounterExecuteBelow > 0 && attacker.Alive
                && (long)attacker.Hp * 100 < (long)attacker.MaxHp * block.CounterExecuteBelow)
            {
                if (attacker.IsBoss) multiplier = 2;   // Boss 不可斩杀:本次反击 ×2,仍受预算(Q3)
                else
                {
                    // 同 TryExecuteKill:报实际抹掉的血量;斩杀不是伤害,不占预算
                    int lost = attacker.Hp;
                    attacker.Hp = 0;
                    _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, lost));
                    ResolveDefeat(enemyIndex, attackerRef, EffectSource.Execute);
                    CheckWin();   // 敌人回合里斩掉最后一名敌人(同 BurnBurst 先例)
                    killed = true;
                }
            }

            int dealt = 0;
            if (counter > 0)
            {
                int hits = Math.Max(1, block.CounterHits);
                dealt += CounterHitsOn(enemyIndex, counter * multiplier, hits, budget - dealt, attackerRef, ref killed);
                if (block.CounterColumn)
                {
                    int splash = counter * CounterColumnPercent / 100;
                    for (int i = 0; i < _enemies.Count && splash > 0; i++)
                    {
                        if (i == enemyIndex || !_enemies[i].Alive) continue;
                        if (!(_enemies[i].Column < attacker.ColumnEnd && attacker.Column < _enemies[i].ColumnEnd)) continue;
                        dealt += CounterHitsOn(i, splash, hits, budget - dealt, attackerRef, ref killed);
                    }
                }
            }
            if (killed && block.KillRefundAp > 0) GrantApRefund(block.KillRefundAp);
            return dealt;
        }

        /// <summary>对一个目标打至多 <paramref name="hits"/> 击反击,每击 min(量, 余额);目标死亡或余额用完即停。返回打出的合计。</summary>
        private int CounterHitsOn(int target, int perHit, int hits, int budget, UnitRef attackerRef, ref bool killed)
        {
            int dealt = 0;
            for (int h = 0; h < hits; h++)
            {
                if (!_enemies[target].Alive) break;
                int d = Math.Min(perHit, budget - dealt);
                if (d <= 0) break;
                DamageEnemy(target, d, Element.Heart,
                    bypassDefense: true, allowBarb: false,
                    source: EffectSource.BlockCounter, attackerRef: attackerRef);
                dealt += d;
                if (!_enemies[target].Alive) killed = true;
            }
            return dealt;
        }

        /// <summary>得利(J2,Q5):挂 ApRefund。已挂着(本轮已返还过)就不再挂 —— 每轮至多一次。</summary>
        private void GrantApRefund(int ap)
        {
            if (_playerStatuses.Has(StatusKind.ApRefund)) return;
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = StatusKind.ApRefund, Polarity = StatusPolarity.Buff, Magnitude = ap, TurnsLeft = -1,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>StartTurn 算完 AP 后兑现 ApRefund 并移除(J2)。没有时一次判断即返回。</summary>
        private void ConsumeApRefund()
        {
            var refund = _playerStatuses.Find(StatusKind.ApRefund);
            if (refund == null) return;
            Ap += refund.Magnitude;
            _playerStatuses.RemoveEntry(refund);
        }
    }
}
