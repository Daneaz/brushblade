using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>木灵替换 / 嫁接二选一(2026-10-13 spec §3):<see cref="SummonDropMode"/> 显式指定对活木灵格
    /// 嫁接还是替换;<c>Auto</c>(缺省)= 旧行为(可嫁接则嫁接)。替换走现有满员顶替的移除口径:
    /// 不算阵亡、阵亡触发不兑现、旧单位的光环 / 护盾 / 状态一并丢弃。</summary>
    public class SummonReplaceTests
    {
        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static SummonPassive Vine => new() { OnHitFreezeChance = 20, OnHitFreezeTurns = 1 };
        private static SummonPassive Thorny => new() { Thorns = 50 };

        private static CharDef Graftor() => new("藤", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "藤", passive: Vine) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) });

        /// <summary>非木的召唤字(夹具):有召唤面 → 可替换;不是木的生面 → 不可嫁接。</summary>
        private static CharDef HeartSummoner() => new("偶", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Summon, 80, summonCount: 1, summonAttack: 5, summonChar: "偶") });

        private static CharDef Striker() => new("锐", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 30) });

        private static SummonSnapshot Wood(int slot, int hp, int maxHp = 100, SummonPassive passive = null) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = hp, MaxHp = maxHp, Attack = 10, Speed = 100,
            Passive = passive,
        };

        private static BattleEngine Battle(IReadOnlyList<SummonSnapshot> summons, params CharDef[] defs) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 0) }, seed: 1, startingSummons: summons);

        private static string Snapshot(BattleEngine b)
        {
            var sb = new StringBuilder();
            sb.Append($"AP{b.Ap}/L{b.Library.Count};");
            foreach (var e in b.LastEvents) sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex};");
            foreach (var s in b.Summons)
                sb.Append(s == null ? "_," : $"{s.Char}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.Passive?.OnHitFreezeChance}/{s.Passive?.Thorns}/{s.BasePassive?.Thorns},");
            var snap = b.Capture();
            sb.Append($"R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}");
            return sb.ToString();
        }

        // ---------------- Replace ----------------

        [Test]
        public void Replace_BackSlot_SwapsInNewSummon_NoDeath_AuraGone_PassiveLive_ApSpent()
        {
            // 后排(槽 1)那只带光环 + 落木;替换后光环随之消失、落木不兑现、不发阵亡钩子
            var doomed = new SummonPassive { AuraAttack = 10, DeathBlast = 50, Thorns = 30 };
            var b = Battle(new[] { Wood(0, 60, 100, Thorny), Wood(1, 40, 100, doomed) }, Graftor());
            var log = new HookLog();
            b.AddHookListener(log);
            var front = b.Summons[0];
            Assert.That(front.AuraAttackBonus, Is.EqualTo(10), "夹具自检:替换前槽 0 吃到槽 1 的光环");
            int ap = b.Ap;
            int library = b.Library.Count;

            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 1, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.None));

            var fresh = b.Summons[1];
            Assert.That(fresh.Char, Is.EqualTo("藤"), "槽 1 换成新木灵");
            Assert.That(fresh.Hp, Is.EqualTo(fresh.MaxHp));
            Assert.That(fresh.Passive.OnHitFreezeChance, Is.EqualTo(20), "新木灵本命生效");
            Assert.That(fresh.Passive.Thorns, Is.EqualTo(0));
            Assert.That(fresh.BasePassive, Is.Null, "替换不是嫁接,不留原本命");
            Assert.That(ReferenceEquals(b.Summons[0], front), Is.True, "别的木灵原样");
            Assert.That(front.Hp, Is.EqualTo(60));
            Assert.That(front.AuraAttackBonus, Is.EqualTo(0), "被替换者的光环消失");
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(2), "只数不变");
            Assert.That(b.Ap, Is.EqualTo(ap - Graftor().ApCost), "AP 照扣");
            Assert.That(b.Library.Count, Is.EqualTo(library - 1), "字照常消耗");

            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Summon && e.SecondIndex == 1), Is.True);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Graft), Is.False, "不是嫁接");
            Assert.That(b.LastEvents.Any(e => e.Source == EffectSource.DeathBlast), Is.False, "落木不兑现");
            Assert.That(log.Sb.ToString().Contains("SummonDied"), Is.False, "不算阵亡:" + log.Sb);
        }

        [Test]
        public void Replace_WhenSummonRowFull_SucceedsWithoutReplaceSummonFlag()
        {
            int capacity = Battle(null, Graftor()).SummonCapacity;
            var all = Enumerable.Range(0, capacity).Select(i => Wood(i, 40, 100, Thorny)).ToArray();
            var b = Battle(all, Graftor());
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: capacity - 1, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.None), "替换不吃召唤位满校验");
            Assert.That(b.Summons[capacity - 1].Char, Is.EqualTo("藤"));
            Assert.That(b.Summons[0].Char, Is.EqualTo("木"), "不是顶掉最前那只");
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(capacity));
        }

        [Test]
        public void Replace_OnEmptyOrCorpseOrNoSummonFace_InvalidTarget_NoApNoConsume()
        {
            var b = Battle(new[] { Wood(0, 50), Wood(1, 0) }, Graftor(), Striker());
            int ap = b.Ap;
            int library = b.Library.Count;
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 1, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.InvalidTarget), "尸体格");
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 2, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.InvalidTarget), "空格");
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: Targeting.PlayerTarget, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.InvalidTarget), "没指格");
            Assert.That(b.Cast("锐", 0, attackMode: false, allySlot: 0, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.InvalidTarget), "无召唤面的字");
            Assert.That(b.Cast("藤", 0, attackMode: true, allySlot: 0, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.InvalidTarget), "攻击面没有召唤");
            Assert.That(b.Ap, Is.EqualTo(ap));
            Assert.That(b.Library.Count, Is.EqualTo(library));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(50));
        }

        [Test]
        public void Replace_NonWoodSummoner_ReplacesSlot()
        {
            var b = Battle(new[] { Wood(0, 50, 100, Thorny) }, HeartSummoner());
            Assert.That(b.Cast("偶", -1, attackMode: false, allySlot: 0, dropMode: SummonDropMode.Replace),
                Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Char, Is.EqualTo("偶"));
            Assert.That(b.Summons.Count(x => x != null && x.Alive), Is.EqualTo(1));
        }

        // ---------------- Graft / Auto ----------------

        [Test]
        public void Graft_Explicit_EqualsAuto()
        {
            var auto = Battle(new[] { Wood(0, 30, 100, Thorny), Wood(1, 20, 100) }, Graftor());
            var graft = Battle(new[] { Wood(0, 30, 100, Thorny), Wood(1, 20, 100) }, Graftor());
            Assert.That(auto.Cast("藤", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(graft.Cast("藤", -1, attackMode: false, allySlot: 0, dropMode: SummonDropMode.Graft),
                Is.EqualTo(BattleError.None));
            Assert.That(Snapshot(graft), Is.EqualTo(Snapshot(auto)));
            Assert.That(graft.LastEvents.Any(e => e.Kind == BattleEventKind.Graft && e.SecondIndex == 0), Is.True);
        }

        [Test]
        public void Graft_Explicit_OnEmptyOrNonWood_InvalidTarget()
        {
            var b = Battle(new[] { Wood(0, 50) }, Graftor(), HeartSummoner());
            int ap = b.Ap;
            Assert.That(b.Cast("藤", -1, attackMode: false, allySlot: 2, dropMode: SummonDropMode.Graft),
                Is.EqualTo(BattleError.InvalidTarget), "空格");
            Assert.That(b.Cast("偶", -1, attackMode: false, allySlot: 0, dropMode: SummonDropMode.Graft),
                Is.EqualTo(BattleError.InvalidTarget), "非木的召唤字不可嫁接");
            Assert.That(b.Ap, Is.EqualTo(ap));
        }

        // ---------------- 查询 ----------------

        [Test]
        public void CanGraftAt_CanReplaceAt_Combinations()
        {
            var b = Battle(new[] { Wood(0, 50), Wood(1, 0) }, Graftor(), HeartSummoner(), Striker());
            // 两者皆可:木的生面 → 活木灵
            Assert.That(b.CanGraftAt("藤", false, 0), Is.True);
            Assert.That(b.CanReplaceAt("藤", false, 0), Is.True);
            // 只可替换:非木的召唤面 → 活木灵
            Assert.That(b.CanGraftAt("偶", false, 0), Is.False);
            Assert.That(b.CanReplaceAt("偶", false, 0), Is.True);
            // 皆不可:尸体 / 空格 / 玩家 / 越界 / 攻击面 / 无召唤面 / 未知字
            foreach (int slot in new[] { 1, 2, Targeting.PlayerTarget, 99 })
            {
                Assert.That(b.CanGraftAt("藤", false, slot), Is.False, $"graft slot {slot}");
                Assert.That(b.CanReplaceAt("藤", false, slot), Is.False, $"replace slot {slot}");
            }
            Assert.That(b.CanGraftAt("藤", true, 0), Is.False, "攻击面");
            Assert.That(b.CanReplaceAt("藤", true, 0), Is.False, "攻击面");
            Assert.That(b.CanGraftAt("锐", false, 0), Is.False);
            Assert.That(b.CanReplaceAt("锐", false, 0), Is.False);
            Assert.That(b.CanGraftAt("无", false, 0), Is.False);
            Assert.That(b.CanReplaceAt("无", false, 0), Is.False);
        }

        // ---------------- 预估 ----------------

        [Test]
        public void EstimateCast_PassesDropMode_AndLeavesEngineUntouched()
        {
            var b = Battle(new[] { Wood(0, 30, 100, Thorny) }, Graftor());
            string before = Snapshot(b);
            Assert.That(b.EstimateCast("藤", -1, false, allySlot: 0, dropMode: SummonDropMode.Replace).Error,
                Is.EqualTo(BattleError.None));
            Assert.That(b.EstimateCast("藤", -1, false, allySlot: 2, dropMode: SummonDropMode.Replace).Error,
                Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(b.EstimateCast("藤", -1, false, allySlot: 0, dropMode: SummonDropMode.Graft).Error,
                Is.EqualTo(BattleError.None));
            Assert.That(b.EstimateCast("藤", -1, false, allySlot: 0).Error, Is.EqualTo(BattleError.None));
            Assert.That(Snapshot(b), Is.EqualTo(before), "预估不动本体");
        }

        // ---------------- Auto 逐位不变 ----------------

        /// <summary>缺省参数(Auto)的出字:嫁接、普通召唤、指定槽召唤、满员顶替、预估,事件流与随机流逐位不变。
        /// 期望值在改动前(base 3b8a0ee4)经 `git archive` 导出的干净树实跑得到(本测试只用旧 API,base 上可编)。</summary>
        [Test]
        public void Auto_DefaultPath_Identical()
        {
            var thorn = new CharDef("荆", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 120, summonAttack: 8, summonChar: "荆",
                    passive: new SummonPassive { Thorns = 40, AuraAttack = 5 }, summonDefense: 4) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) });
            var defs = new[] { Graftor(), thorn, Striker() };
            var log = new HookLog();
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 3000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 300 },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 90), new EnemyDef("远", Element.Heart, 100000, 60, range: AttackRange.Ranged) },
                seed: 31, startingSummons: new[] { Wood(0, 40, 100, Thorny), Wood(2, 70, 100) });
            b.AddHookListener(log);
            int capacity = b.SummonCapacity;
            var sb = new StringBuilder();
            void Do(string id, int target, bool attack = false, int ally = Targeting.PlayerTarget, bool replace = false,
                IReadOnlyList<int> slots = null)
            {
                var est = b.EstimateCast(id, target, attack, allySlot: ally);
                sb.Append($"~{est.Error}/{est.ShieldGranted}/{est.HealAmount}/{est.Enemies?.Count};");
                sb.Append($"#{id}{target}/{ally}={b.Cast(id, target, replace, attack, summonSlots: slots, allySlot: ally)};");
                foreach (var e in b.LastEvents) sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Source};");
            }
            for (int t = 0; t < 8 && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                if (t == 0) Do("藤", -1, ally: 0);                        // 嫁接
                if (t == 1) Do("荆", -1, slots: new[] { 3 });             // 指定槽召唤
                if (t == 2) Do("藤", -1);                                 // 普通召唤
                if (t == 3) Do("荆", -1, ally: 3);                        // 再嫁接(嫁到 t1 召出的荆灵)
                if (t == 4) { while (b.Summons.Count(x => x != null && x.Alive) < capacity) Do("荆", -1); }
                if (t == 5) { Do("藤", -1); Do("藤", -1, replace: true); } // 满员拒 → 确认顶替最前
                if (t == 6) Do("荆", -1, slots: new[] { 1 }, replace: true); // 指定槽顶替
                Do("锐", t % 2);
                b.EndTurn();
                foreach (var e in b.LastEvents) sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append('|').Append(Snapshot(b)).Append('\n');
            }
            string raw = sb.ToString();
            Assert.That(raw.Contains("Graft:"), Is.True, raw);
            Assert.That(raw.Contains("=SummonCapFull;"), Is.True, raw);
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            string fp = h.ToString("x16") + " " + raw.Length;
            Assert.That(fp, Is.EqualTo(AutoExpected), fp + "\n" + raw);
        }

        private const string AutoExpected = "18147eeefa50350b 14858";
    }
}
