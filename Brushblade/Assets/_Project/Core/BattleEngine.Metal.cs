using System;
using System.Linq;

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
            // 聚金(J7):溢出 = 加之前 + 本次 − 上限 的正部。只有这条分支累计 —— 补满(上面已返回)与锋芒(GrantMoraleFromCrit)不经过这里
            int overflow = MoraleStacks + value - cap;
            if (overflow > 0) _cast.MoraleOverflow += overflow;
            AddPlayerCounter(StatusKind.Morale, value, cap);
        }

        /// <summary>聚金(J7,Q14):本次出字累计的战意溢出 × Value → 护盾。没有溢出时直接返回。</summary>
        private void ResolveMoraleOverflowShield(int value)
        {
            int amount = _cast.MoraleOverflow * value;
            if (amount <= 0) return;
            int granted = AddPlayerShield(amount, persist: false);
            _events.Add(new BattleEvent(BattleEventKind.Shield, Targeting.PlayerTarget, granted));
        }

        /// <summary>富甲 / 金气(J8):玩家身上挂一枚本场隐藏光环,同类取最强(Magnitude 取大,不叠)。</summary>
        private void GrantMoraleAura(StatusKind kind, int magnitude)
        {
            var existing = _playerStatuses.Find(kind);
            if (existing != null)
            {
                existing.Magnitude = Math.Max(existing.Magnitude, magnitude);
                return;
            }
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = magnitude, TurnsLeft = -1,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>富甲加给护甲的点数 = 战意层数 × Magnitude(随战意即时变化,断金清空战意后即时失效,Q13)。
        /// 玩家的 EffectivePlayerDefense 与木灵受击时的护甲共用。没挂富甲时恒 0。</summary>
        private int MoraleArmorBonus
        {
            get
            {
                var aura = _playerStatuses.Find(StatusKind.MoraleArmor);
                return aura == null ? 0 : MoraleStacks * aura.Magnitude;
            }
        }

        /// <summary>金气(J8b):玩家回合开始、清盾之后、TurnStarted 之前,战意 ≥ 上限就加盾(只给玩家)。没挂金气时一次判断即返回。</summary>
        private void ApplyMoraleShield()
        {
            var aura = _playerStatuses.Find(StatusKind.MoraleShield);
            if (aura == null || MoraleStacks < MoraleCapOrDefault) return;
            int granted = AddPlayerShield(aura.Magnitude, persist: false);
            if (granted > 0) _events.Add(new BattleEvent(BattleEventKind.Shield, Targeting.PlayerTarget, granted));
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
                    EnqueueBlockExecuteTraits(block.ExecuteSourceCharId, enemyIndex);   // 铁则(J3):已分胜负时 Enqueue 自会丢弃
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

        // ---- Task 3:斩杀族(附录 J3 / J4 / J5)----

        /// <summary>立威斩杀的「斩杀时」(J3,Q6):按 Block 上记的施加者字 ID 回查字表,取该字已解锁的 OnExecute 特性入队
        /// (目标 = 被斩杀者,Depth = TriggerDepth + 1),在 ActOneEnemy 那一拍末尾的 DrainReactions 兑现。
        /// 面按五行面(Feature)取:立威是铠面特性,格挡来自五行面;两面通用的铁则照常命中。
        /// 没记来源 / 查不到字 / 没有该类特性时一次判断即返回,不摇随机数。反应里(TriggerDepth &gt; 0)的斩杀不入队(R4)。</summary>
        private void EnqueueBlockExecuteTraits(string charId, int enemyIndex)
        {
            if (charId == null || TriggerDepth > 0 || !_graph.TryGet(charId, out var def)) return;
            var traits = TraitRules.Triggered(def, CardFace.Feature, CardLevelOf(charId), TraitTrigger.OnExecute);
            if (traits.Count == 0) return;
            var element = def.Element ?? Element.Heart;
            var body = EffectsOf(def, false);
            foreach (var t in traits)
                Enqueue(new Reaction(def.Id, element,
                    t.Effects.Select(e => TraitRules.ForCast(e, t, def, body)).ToList(), enemyIndex, TriggerDepth + 1));
        }

        /// <summary>斩杀溅射(J4,Q18):对死者 <paramref name="victim"/> 同排左右的存活敌人(Adjacent 口径,同 D2-火 E2;
        /// 死者照样占位,几何照读)各打「死者 MaxHp × <paramref name="percent"/>%」:<paramref name="attacker"/> 元素、过生克与护甲、
        /// 不暴击、不算挥击(不触发铁画)、source = ExecuteSplash。整段抬一层 TriggerDepth(R4)—— 溅射打死的不入队
        /// 击杀时 / 斩杀时 / 焚城。跨排 Boss 的双记去重。</summary>
        private void ExecuteSplash(int victim, int percent, Element attacker)
        {
            int damage = (int)((long)_enemies[victim].MaxHp * percent / 100);
            if (damage <= 0) return;
            var targets = Targeting.ExpandTargets(_enemies, victim, TargetArea.Adjacent, 0).Where(i => i != victim).Distinct().ToList();
            if (targets.Count == 0) return;
            EnterTrigger();
            try
            {
                foreach (int i in targets)
                    if (_enemies[i].Alive)
                        DamageEnemy(i, damage, attacker, allowBarb: false,
                            source: EffectSource.ExecuteSplash, attackerRef: UnitRef.Player);
            }
            finally { ExitTrigger(); }
        }

        /// <summary>致命的斩杀线(J5,割喉):杂兵生命低于最大生命的这个百分比即被斩杀。</summary>
        public const int DoomExecutePercent = 30;

        /// <summary>致命(J5):给选中的存活敌人挂 Doom(回合 = Value,不吃卡等级;同源刷新取长),挂上即判一次(施加时已低于 30% 立即斩杀)。</summary>
        private void ApplyDoom(EffectDef effect, int targetIndex, string sourceId)
        {
            foreach (int ti in PickTargets(effect, targetIndex))
            {
                if (!OnlyIfMet(effect, ti) || !_enemies[ti].Alive) continue;
                ApplyStatus(_enemies[ti].Statuses, new StatusEffect
                {
                    Kind = StatusKind.Doom, Polarity = StatusPolarity.Debuff,
                    Magnitude = 1, TurnsLeft = Math.Max(1, effect.Value), SourceId = sourceId, TraitKey = effect.TraitKey,
                }, UnitRef.Enemy(ti), UnitRef.Player);
                AfterEnemyHpLoss(ti);
            }
        }

        /// <summary>敌人掉血之后(存活时)的统一挂点(J5,Q16)。调用点:DamageEnemy(含埋雷 / 反弹 / 格挡反击 / 召唤物 / 追加一击 /
        /// 斩杀溅射等全部走它的伤害)、叠字怪分裂、SettleBurnOn、SettleBleedOn、Detonate、BurstBurn(焚城),以及施加致命的那一刻。
        /// 带致命的杂兵生命 &lt; 30% → 斩杀(killer 玩家、source Execute:出字内会触发斩杀时 / 击杀时特性),当场判胜。
        /// 返回是否斩杀(调用方据此跳过「存活」后续)。没有致命 / Boss / 已死时一次判断即返回,不摇随机数。</summary>
        private bool AfterEnemyHpLoss(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Statuses.Has(StatusKind.Doom) || enemy.IsBoss || !enemy.Alive) return false;
            if ((long)enemy.Hp * 100 >= (long)enemy.MaxHp * DoomExecutePercent) return false;
            int lost = enemy.Hp;   // 同 TryExecuteKill:报实际抹掉的血量
            enemy.Hp = 0;
            _events.Add(new BattleEvent(BattleEventKind.Damage, enemyIndex, lost));
            ResolveDefeat(enemyIndex, UnitRef.Player, EffectSource.Execute);
            CheckWin();
            return true;
        }

        /// <summary>致命 · Boss 版(J5):Boss 身上有致命时这一下 DamageEnemy ×2,并移除全部致命(多来源也只翻一次)。</summary>
        private static int ConsumeBossDoom(EnemyState enemy, int damage)
        {
            if (!enemy.IsBoss || !enemy.Statuses.Has(StatusKind.Doom)) return damage;
            enemy.Statuses.Remove(StatusKind.Doom);
            return damage * 2;
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
