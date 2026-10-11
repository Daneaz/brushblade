using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>木系专属机制(Plan D2-木)。Task 1 = 共用小扩展里需要读木灵编队的那几项:E40 按木灵攻击 / 护甲加伤、
    /// E41 幼苗取本体、E46 柘弓只让本字最强木灵出手、E50 条件 OwnSummonAlive。Task 2 = 光环族:M1 玩家袋光环、
    /// M2 本命在场光环(林荫 / 森严 / 蟾宫)、馥郁读点。Task 3 = 受击 / 阵亡 / 守护 / 标记:M3 秋实 / 落木 / 藻荇、
    /// M4 披荆 / 良弓、M5 守护、M6 荆刺。挂点处只留一行调用,实现放这里。
    ///
    /// 「本字木灵」(Q2)= SourceChar == 本字 且 Char != 苗 的存活木灵;嫁接不改变身份。
    /// 恒等:各项只在对应字段 / 条件非缺省时才走到,一律不摇随机数。</summary>
    public sealed partial class BattleEngine
    {
        // ---- 预留骨架(Ruling 17):木系新机制 M1–M11 的挂点集中在这里,Task 2–4 逐项填 ----

        /// <summary>木灵阵亡的特性挂点(M3 秋实 / 落木 / 藻荇、M7 攀附清理等,Task 3 / 4):OnSummonDeath 的单点调用,
        /// 在归根治疗之后、刷光环之前(落木读的是阵亡这一刻的有效攻击,光环份额还没摘)。
        /// 阵亡 = 走 OnSummonDeath 的路径:挨打、灼烧、吞噬、守护转移致死;顶替召唤与绿潮并入不走这里(Q15)。
        /// 死者没有本命、也不是有母体的小藻时只做光环清理,不摇随机数。</summary>
        private void OnSummonDeathTraits(int slot, UnitRef killer)
        {
            ClearSlotAuras(slot);   // M1:按槽位挂的光环随木灵阵亡清理(Review Focus 1)
            ClearSlotBinds(slot);   // M7:藤灵阵亡 → 攀附解除
            var dead = _summons[slot];
            if (dead == null) return;
            var p = dead.Passive;
            if (p != null)
            {
                if (p.DeathPart) GrantDeathPart(slot);                               // 秋实
                if (p.DeathBlast > 0) EnqueueDeathBlast(slot, dead, p.DeathBlast);   // 落木
            }
            if (dead.SproutParentSlot >= 0) SproutDeathSlow(dead, killer);          // 藻荇
        }

        // ---- M7 攀附(Task 4)----

        /// <summary>本次出字召出的第一只存活木灵(嫁接时为被嫁接者,E42);没有 −1。</summary>
        private int FirstSummonedThisCast()
        {
            foreach (int s in _cast.SummonedSlots)
                if (s >= 0 && s < _summons.Length && _summons[s] != null && _summons[s].Alive) return s;
            return -1;
        }

        /// <summary>攀附(Q13):敌人挂 Bind(Debuff,Magnitude = 打击 %,TargetSlot = 藤灵槽,本场 −1);同源同槽刷新。
        /// 解除只走槽位清理(藤灵阵亡 / 被顶替 / 被绿潮并入),见 <see cref="ClearSlotBinds"/>。</summary>
        private void ApplyBind(int enemyIndex, EffectDef effect, string sourceId, int vineSlot)
        {
            ApplyStatus(_enemies[enemyIndex].Statuses, new StatusEffect
            {
                Kind = StatusKind.Bind, Polarity = StatusPolarity.Debuff, Magnitude = Math.Max(0, effect.Value),
                TurnsLeft = -1, SourceId = sourceId, TraitKey = effect.TraitKey, TargetSlot = vineSlot,
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
        }

        /// <summary>攀附结算(Q13):敌人每次行动开始(种之后、冻结判定之前,含被冻结跳过的这一拍),身上每条 Bind 让对应藤灵
        /// 打它一下 —— 有效攻击 × N%,走 StrikeTargetWithSummon(暴击 / 出手附带照常,缠绕本命的冻结随这一击摇号)。
        /// 没有 Bind 时一次判断即返回,不摇号。藤灵已不在(防御性)就顺手摘掉这条。</summary>
        private void SettleBinds(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Statuses.Has(StatusKind.Bind)) return;
            RefreshSummonAura();   // 打击量读这一刻的有效攻击(幂等,不摇号)
            foreach (var bind in enemy.Statuses.All.Where(s => s.Kind == StatusKind.Bind).ToList())
            {
                if (!enemy.Alive || Phase == BattlePhase.Lost || Phase == BattlePhase.Won) return;
                int slot = bind.TargetSlot;
                var vine = slot >= 0 && slot < _summons.Length ? _summons[slot] : null;
                if (vine == null || !vine.Alive) { enemy.Statuses.RemoveEntry(bind); continue; }
                if (!HasStrikeOutput(vine)) continue;
                StrikeTargetWithSummon(slot, enemyIndex, (int)((long)vine.EffectiveAttack * bind.Magnitude / 100), sameSwing: false);
            }
        }

        /// <summary>某槽位换了单位 / 阵亡 / 被并入:敌人身上指向它的攀附一并解除(槽位下标会被复用)。没有 Bind 时逐个敌人一次判断。</summary>
        private void ClearSlotBinds(int slot)
        {
            foreach (var enemy in _enemies)
            {
                if (!enemy.Statuses.Has(StatusKind.Bind)) continue;
                foreach (var st in enemy.Statuses.All.Where(s => s.Kind == StatusKind.Bind && s.TargetSlot == slot).ToList())
                    enemy.Statuses.RemoveEntry(st);
            }
        }

        // ---- M8 绿潮(Task 4)----

        /// <summary>绿潮(Q18):场上全部小藻(<c>Char == 苗 && SourceChar == 本字</c>,含丛生 / 丛藻,不含接收者自己)的当前生命 /
        /// 最大生命 / 攻击加到本次召出的藻灵(第一只;嫁接 = 被嫁接者)上,小藻槽位直接置空 —— 不走阵亡(不发 SummonDied、
        /// 不触发归根 / 秋实 / 藻荇),但按槽位清掉指向它的光环、攀附、丛生母体记号。不摇号。</summary>
        private void MergeSaplingsInto(string sourceChar)
        {
            int receiver = FirstSummonedThisCast();
            if (receiver < 0) return;
            var r = _summons[receiver];
            bool merged = false;
            for (int s = 0; s < _summons.Length; s++)
            {
                var w = _summons[s];
                if (s == receiver || w == null || !w.Alive || w.Char != SaplingChar || w.SourceChar != sourceChar) continue;
                r.MaxHp += w.MaxHp;
                r.Hp += w.Hp;
                r.Attack += w.Attack;
                _summons[s] = null;
                _summonThresholdCrossed.Remove(s);
                ClearSproutParent(s);
                ClearSlotAuras(s);
                ClearSlotBinds(s);
                _events.Add(new BattleEvent(BattleEventKind.SummonMerged, s, w.Hp, receiver));
                merged = true;
            }
            if (merged) RefreshSummonAura();
        }

        // ---- M9 四木成林(Task 4)----

        /// <summary>四木成林(Q24):按槽位升序,每只存活木灵按自己的目标规则额外出手一次(StrikeOnceWithSummon)。
        /// 不推进行动条、不递减状态、不触发光环治疗 / 丛生 / 灼烧;随机数只在这里真出手时才消费。</summary>
        private void ActAllSummonsNow()
        {
            RefreshSummonAura();
            for (int s = 0; s < _summons.Length; s++)
            {
                if (!_enemies.Any(e => e.Alive)) return;
                var w = _summons[s];
                if (w != null && w.Alive) StrikeOnceWithSummon(s);
            }
        }

        // ---- M10 折桂(Task 4)----

        /// <summary>折桂(Q16):本场字库 +Value 张。候选 = 玩家已拥有(UnlockedChars)的非部件字里档位最高的一档,
        /// 没有记录时退回全字表非部件的最高档;按 ID 排序后用 _traitRandom 抽。满库不入,且一张都没入时退还 E49 在入队时记下的
        /// 那一次场次(「满库不入、不计次」)。入库的字随字库延续到之后的战斗。</summary>
        private void GrantTraitChars(EffectDef effect)
        {
            int granted = 0;
            for (int n = 0; n < effect.Value; n++)
            {
                if (_forge.Library.Count >= _config.LibraryCapacity) break;
                var candidates = GrantCandidates();
                if (candidates.Count == 0) break;
                string pick = candidates[_traitRandom.Next(candidates.Count)];
                if (!GrantLibraryChar(pick)) break;
                _events.Add(new BattleEvent(BattleEventKind.CharGranted, -1, _forge.Library.Count - 1));
                granted++;
            }
            if (granted == 0) RefundBattleUse(effect.TraitKey);
        }

        private List<string> GrantCandidates()
        {
            var owned = new List<CharDef>();
            if (_config.UnlockedChars != null)
                foreach (var id in _config.UnlockedChars)
                    if (_graph.TryGet(id, out var d) && !d.IsComponent) owned.Add(d);
            IReadOnlyCollection<CharDef> source = owned.Count > 0 ? owned : _graph.All.Where(d => !d.IsComponent).ToList();
            if (source.Count == 0) return new List<string>();
            var top = source.Max(d => d.Rarity);
            return source.Where(d => d.Rarity == top).Select(d => d.Id).Distinct()
                .OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        /// <summary>退还一次特性级每场次数(E49 击杀时在入队时记次;兑现时发现满库不入,把那一次还回去)。
        /// 没有 limit 的特性从未记过次,键不在表里,空转。</summary>
        private void RefundBattleUse(string traitKey)
        {
            if (traitKey == null) return;
            string key = "场次:" + traitKey;   // 同 BattleUseKey
            if (_traitUsesThisBattle.TryGetValue(key, out int battle) && battle > 0) _traitUsesThisBattle[key] = battle - 1;
            if (_traitUsesThisTurn.TryGetValue(key, out int turn) && turn > 0) _traitUsesThisTurn[key] = turn - 1;
        }

        // ---- M11 水草(Task 4)----

        /// <summary>顶层出字的实际治疗量里不含给带水草的藻灵的部分(CastCore 置 0、ApplyEffects 出门时写入、MirrorCastHeal 读后清零)。
        /// 不进快照:生命周期在一次 CastCore 之内。</summary>
        private int _castMirrorBase;

        /// <summary>出字内的木灵治疗计数(HealAlly / HealPlayerAndSummons 的木灵支):HealTotal 照旧累加;落在带水草的木灵身上的另记一份。</summary>
        private void CountCastHealOnSummon(SummonState summon, int given)
        {
            _cast.HealTotal += given;
            if ((summon.Passive?.HealMirror ?? 0) > 0) _cast.MirrorHealed += given;
        }

        /// <summary>水草(Q17):顶层出字结束后,本次实际治疗量(不含给藻灵的)&gt; 0 → 每只带 HealMirror 的存活木灵回复其 × N%。
        /// 不吃泉、不攒泉、不触发溢流;种 / 桂香 / 反应里的治疗不在出字的 HealTotal 里,不算。</summary>
        private void MirrorCastHeal()
        {
            int baseHeal = _castMirrorBase;
            _castMirrorBase = 0;
            if (baseHeal <= 0) return;
            for (int s = 0; s < _summons.Length; s++)
            {
                var w = _summons[s];
                int percent = w != null && w.Alive ? w.Passive?.HealMirror ?? 0 : 0;
                if (percent <= 0) continue;
                int amount = (int)((long)baseHeal * percent / 100);
                if (amount > 0) HealAlly(s, amount, overflowToDamage: false);
            }
        }

        // ---- M3 阵亡挂点(Task 3)----

        /// <summary>秋实(Q15):部件池 +「木」;满池不入(不替换)、不发事件。</summary>
        private void GrantDeathPart(int slot)
        {
            if (GrantPoolComponent(WoodPart)) _events.Add(new BattleEvent(BattleEventKind.PartGranted, slot, 1));
        }

        /// <summary>秋实入池的部件。</summary>
        private const string WoodPart = "木";

        /// <summary>落木(Q15):伤害 = 阵亡这一刻的有效攻击 × N%,入队为反应,在下一个安全点兑现(不重入 ApplyEffects)。
        /// 兑现时打全体存活敌人:死者元素、过生克与护甲(DamageEnemy 全套,吃标记)、不暴击、不算挥击(allowBarb: false)、
        /// source = DeathBlast;TriggerDepth = 入队深度 + 1(R4:落木打死的不入队击杀时,不连锁)。</summary>
        private void EnqueueDeathBlast(int slot, SummonState dead, int percent)
        {
            int amount = (int)((long)dead.EffectiveAttack * percent / 100);
            if (amount <= 0) return;
            var element = dead.Element;
            Enqueue(new Reaction(() => ResolveDeathBlast(slot, element, amount), TriggerDepth + 1));
        }

        private void ResolveDeathBlast(int slot, Element element, int amount)
        {
            int count = _enemies.Count;   // 兑现那一刻的表长:分裂出来的新怪不吃同一发
            for (int i = 0; i < count; i++)
                if (_enemies[i].Alive)
                    DamageEnemy(i, amount, element, allowBarb: false,
                        source: EffectSource.DeathBlast, attackerRef: UnitRef.Summon(slot));
            CheckWin();
        }

        /// <summary>藻荇(Q15):小藻(幼苗、记着母体槽位)阵亡,且母体槽位上的木灵(死活不论)本命带 SproutDeathSlow N →
        /// 给 killer 挂减速 N 回合。只对敌人 killer 生效(灼烧自焚等 None 不触发)。挥击发生在 killer 自己那一拍里,
        /// 推迟到那一拍收尾(TickTurns 之后)兑现,回合数足额(同回敬 / 冰晶,Ruling 12)。母体槽位被复用后记号已清,不触发。</summary>
        private void SproutDeathSlow(SummonState dead, UnitRef killer)
        {
            if (killer.Side != UnitSide.Enemy || dead.Char != SaplingChar) return;
            var parent = dead.SproutParentSlot < _summons.Length ? _summons[dead.SproutParentSlot] : null;
            int turns = parent?.Passive?.SproutDeathSlow ?? 0;
            if (turns <= 0) return;
            int enemyIndex = killer.Index;
            string sourceId = parent.SourceChar;
            DeferToBeatEnd(new Reaction(() => ApplySproutDeathSlow(enemyIndex, turns, sourceId), TriggerDepth + 1));
        }

        private void ApplySproutDeathSlow(int enemyIndex, int turns, string sourceId)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemies.Count || !_enemies[enemyIndex].Alive) return;
            ApplyStatus(_enemies[enemyIndex].Statuses, new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = turns, SourceId = sourceId,   // 幅度同 EffectKind.Slow
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
        }

        // ---- M4 受击挂点(Task 3)----

        /// <summary>披荆(Q12):荆棘段 bounced &gt; 0 后调用。只认这只木灵自己的本命荆棘(Thorns &gt; 0)—— 荆棘光环弹出去的不算。
        /// 给攻击者挂流血 N(召唤时已吃卡等级,定死在本命里),缺省 3 回合;流血按 Kind 合并取强 = 同源刷新。不是伤害,不占 60% 预算。</summary>
        private void ThornsBleed(int enemyIndex, int slot, SummonState summon)
        {
            var p = summon.Passive;
            if (p == null || p.ThornsBleed <= 0 || p.Thorns <= 0 || !_enemies[enemyIndex].Alive) return;
            ApplyStatus(_enemies[enemyIndex].Statuses, new StatusEffect
            {
                Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff,
                Magnitude = p.ThornsBleed, TurnsLeft = BleedDefaultTurns, SourceId = summon.SourceChar,
            }, UnitRef.Enemy(enemyIndex), UnitRef.Summon(slot));
        }

        /// <summary>良弓(Q10):DamageSummon 末段(格挡反击之后),对攻击者造成 min(SummonDefense × N%, 剩余预算),
        /// 心属性、无视护甲、不算挥击。打空 / 免疫挡下在更早处 return,吞噬不走 DamageSummon —— 都不触发。</summary>
        private void ReturnFire(int enemyIndex, int slot, SummonState summon, int budget)
        {
            int percent = summon.Passive?.ReturnFire ?? 0;
            if (percent <= 0 || budget <= 0 || !_enemies[enemyIndex].Alive) return;
            int dealt = Math.Min((int)((long)SummonDefense(summon) * percent / 100), budget);
            if (dealt > 0)
                DamageEnemy(enemyIndex, dealt, Element.Heart, bypassDefense: true, allowBarb: false,
                    source: EffectSource.ReturnFire, attackerRef: UnitRef.Summon(slot));
        }

        // ---- M5 守护(Task 3)----

        /// <summary>带守护(本命 Guard &gt; 0)的存活木灵里槽位最小的一只;没有 −1(Q9)。</summary>
        private int GuardSlot()
        {
            for (int s = 0; s < _summons.Length; s++)
            {
                var w = _summons[s];
                if (w != null && w.Alive && w.Passive != null && w.Passive.Guard > 0) return s;
            }
            return -1;
        }

        /// <summary>守护(Q9):把 <paramref name="damage"/>(玩家这一击过完护甲 / 格挡 / 减伤 / HitCap 之后)× Guard% 转给 <paramref name="slot"/>
        /// 的木灵:先扣它的护盾、再扣血(LoseSummonHp,保命照常),不再过护甲 / 生克,不算挥击(不发 SummonHit、不触发它的荆棘 / 良弓)。
        /// 返回转移量(0 ≤ 转移 ≤ damage)。致死由调用方在这一击收尾时走 OnSummonDeath。</summary>
        private int TransferToGuard(int enemyIndex, int slot, int damage)
        {
            var guard = _summons[slot];
            int transfer = damage * Math.Clamp(guard.Passive.Guard, 0, 100) / 100;
            if (transfer <= 0) return 0;
            int hpBefore = guard.Hp;
            int absorbed = Math.Min(guard.Shield, transfer);
            guard.Shield -= absorbed;
            LoseSummonHp(guard, transfer - absorbed);
            _events.Add(new BattleEvent(BattleEventKind.GuardTransfer, enemyIndex, transfer, slot, absorbed));
            CheckThreshold(UnitRef.Summon(slot), hpBefore, guard.Hp, guard.MaxHp);
            return transfer;
        }

        // ---- M6 荆刺(Task 3)----

        /// <summary>荆刺(Q11):给敌人挂 ThornMark(Debuff,Magnitude = 反伤 %,TurnsLeft = 该敌人行动次数,SourceId = 字 ID 同源刷新)。</summary>
        private void ApplyThornMark(int enemyIndex, EffectDef effect, string sourceId)
        {
            ApplyStatus(_enemies[enemyIndex].Statuses, new StatusEffect
            {
                Kind = StatusKind.ThornMark, Polarity = StatusPolarity.Debuff,
                Magnitude = Math.Clamp(effect.Value, 0, 100), TurnsLeft = Math.Max(1, effect.Turns),
                SourceId = sourceId, TraitKey = effect.TraitKey,
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
        }

        /// <summary>攻击者身上的荆刺 %(并入玩家侧与召唤侧两条反弹管线的 Reflect 合计,随后各自钳 60%);没有 = 0。</summary>
        private int ThornMarkOf(int enemyIndex) =>
            enemyIndex >= 0 && enemyIndex < _enemies.Count ? _enemies[enemyIndex].Statuses.TotalMagnitude(StatusKind.ThornMark) : 0;

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

        // ---- 表现层读口(D2-木 Task 5):只读,与结算同源 ----

        /// <summary>这只木灵此刻是不是远程:本命远程 || 箭灵光环(Q1)。木灵出手选目标与战场射程图标同读这一处。</summary>
        public bool IsSummonRanged(int slot)
        {
            var s = slot >= 0 && slot < _summons.Length ? _summons[slot] : null;
            return s != null && ((s.Passive?.Ranged ?? false) || HasRangedAura(slot));
        }

        /// <summary>这只木灵出手的暴击率:自身 CritBuff + 蟾宫(存活木灵本命 CritAura 取最大),钳 0..100(Q23)。</summary>
        public int SummonCritPercent(SummonState summon) =>
            Math.Clamp(summon.Statuses.TotalMagnitude(StatusKind.CritBuff) + SummonCritAura(), 0, 100);

        /// <summary>林荫 / 森严此刻给的减伤百分比(Q8;与 DamageCut 取最强之前的那一份)。
        /// <paramref name="forSummon"/> = 木灵侧(再与森严取最强);玩家侧只有林荫。</summary>
        public int WoodDamageCutPercent(bool forSummon) => WoodCutPercent(forSummon);

        /// <summary>玩家袋里落在这一槽位的木灵光环条目(TargetSlot −1 全队,或 == slot 那一只):攻击 / 荆棘 / 远程。
        /// 详情弹窗按条列出来源与剩余回合。返回新列表,元素是袋子里的原条目(只读,别改)。</summary>
        public IReadOnlyList<StatusEffect> SummonAurasAt(int slot)
        {
            var list = new List<StatusEffect>();
            foreach (var st in _playerStatuses.All)
                if (IsSummonAura(st.Kind) && (st.TargetSlot < 0 || st.TargetSlot == slot)) list.Add(st);
            return list;
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
