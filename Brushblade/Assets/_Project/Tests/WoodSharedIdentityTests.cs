using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 1 第一步:附录 §3 末「trace 不守、必须单元对照」第 3 / 4 / 5 / 6 / 8 项(第 1 项见
    /// <see cref="ThornsChain_NoNewState_Identical"/>)。E37–E50 要往这些木特有路径里插分支,trace 卡池(箭 / 林 / 藻 / 森,全 1 级,
    /// 恒出生面)一次都走不到:桂香光环 HealAlly、魅惑反打、嫁接、SummonSapling / HealSummons / ShieldSummons / SummonStrike /
    /// Revive、荆棘反弹全段、木灵跨场携带。
    ///
    /// 指纹 = 每回合出字结果 + 每拍事件 + 钩子序列 + 玩家血 / 护盾 / 状态袋 + 木灵血 / 盾 / 状态袋 + 敌人血 / 行动条 + 三条随机流;
    /// 携带项另加 CarriedSummons 全字段。期望值在改动前(HEAD 647ca19a,即 D2-木 base)跑出,
    /// 并经 `git archive 647ca19a` 导出的干净树复核。</summary>
    public class WoodSharedIdentityTests
    {
        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static BattleConfig Config(int dodge = 0) => new()
            { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 300, PlayerDodge = dodge };

        private static BattleEngine Battle(HookLog log, CharDef[] defs, EnemyDef[] enemies,
            IReadOnlyDictionary<string, int> levels = null, IReadOnlyList<SummonSnapshot> summons = null)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config(),
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                enemies, seed: 23, startingHp: 3000, cardLevels: levels, startingSummons: summons);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, StringBuilder sb, bool attack = false,
            int allySlot = Targeting.PlayerTarget, bool replace = false)
        {
            sb.Append($"#{id}{target}/{allySlot}{(attack ? "A" : "")}={b.Cast(id, target, replaceSummon: replace, attackMode: attack, allySlot: allySlot)};");
            // BlockUsed(格挡反击动效,2026-10-15)是纯追加的表现事件,不进恒等指纹 —— 其余事件流仍逐位对照
            foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.BlockUsed))   // 出字事件逐张记(LastEvents 只留最后一次 Cast)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
        }

        private static void AppendSummons(StringBuilder sb, IEnumerable<SummonState> summons)
        {
            int i = 0;
            foreach (var s in summons)
            {
                i++;
                if (s == null) continue;
                sb.Append($",W{i - 1}:{s.Char}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.Shield}/{s.Element}/{PassiveOf(s.Passive)}[");
                foreach (var st in s.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft}:{st.SourceId} ");
                sb.Append(']');
            }
        }

        private static string PassiveOf(SummonPassive p) => p == null ? "-"
            : $"{p.Speed}.{p.Thorns}.{p.HealAlly}.{p.HealAllyTimes}.{p.Regen}.{p.AuraAttack}.{p.OnHitFreezeChance}.{p.OnHitCharmChance}"
              + $".{p.Taunt}.{p.Ranged}.{p.Armor}.{p.SproutPercent}.{p.SproutMax}.{p.EntrySaplings}.{p.PerAllyAttackPercent}";

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int, StringBuilder> eachTurn,
            int turns, StringBuilder all)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                eachTurn(b, t, sb);
                sb.Append($"|C{b.Ap}/S{b.ShieldNormal}+0");
                b.EndTurn();
                // BlockUsed(格挡反击动效,2026-10-15)是纯追加的表现事件,不进恒等指纹 —— 其余事件流仍逐位对照
                foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.BlockUsed))
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0[");
                foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                sb.Append(']');
                AppendSummons(sb, b.Summons);
                for (int i = 0; i < b.Enemies.Count; i++)
                {
                    var en = b.Enemies[i];
                    sb.Append($",E{en.Hp}/{en.ActionMeter}[");
                    foreach (var st in en.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft} ");
                    sb.Append(']');
                }
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            all.Append(raw);
            return Hash(raw);
        }

        private static string Hash(string raw)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        private static CharDef Wood(string id, params EffectDef[] effects) => new(id, Element.Wood, effects: effects,
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) });

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        // ---------------- 第 3 项:桂香光环 HealAlly(馥郁 / M11 的落点)----------------

        [Test]
        public void HealAllyAura_NoNewState_Identical()
        {
            var gui = Wood("桂", new EffectDef(EffectKind.Summon, 300, summonAttack: 20, summonChar: "木",
                passive: new SummonPassive { HealAlly = 30, HealAllyTimes = 2 }));
            var tao = Wood("桃", new EffectDef(EffectKind.Summon, 200, summonAttack: 10, summonChar: "木",
                passive: new SummonPassive { HealAlly = 15 }));
            var spring = RebalanceFixture.Char("水", new EffectDef(EffectKind.HealSelf, 50));
            var log = new HookLog();
            var b = Battle(log, new[] { gui, tao, spring, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 150),
                new EnemyDef("远", Element.Heart, 100000, 90, range: AttackRange.Ranged),
            });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "桂", -1, sb);
                if (t == 1) Do(x, "桃", -1, sb);
                if (t % 3 == 2) Do(x, "水", -1, sb);
                Do(x, "击", t % 2, sb);
            }, 8, all);
            string raw = all.ToString();
            Assert.That(raw.Contains("Heal:"), Is.True, "场景没走到治疗");
            Assert.That(raw.Contains("SummonHit:"), Is.True, "场景没走到木灵挨打");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(8));
            Assert.That(fp, Is.EqualTo(HealAllyExpected), fp);
        }

        // ---------------- 第 4 项:魅惑反打(E45)----------------

        [Test]
        public void CharmCounterAttack_NoNewState_Identical()
        {
            var flower = RebalanceFixture.Char("花", new EffectDef(EffectKind.Charm, 0, turns: 2));
            var lure = Wood("迷", new EffectDef(EffectKind.Summon, 400, summonAttack: 30, summonChar: "木",
                passive: new SummonPassive { OnHitCharmChance = 40 }));
            var log = new HookLog();
            var b = Battle(log, new[] { flower, lure, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 100),
                new EnemyDef("壮", Element.Heart, 100000, 140, defense: 5),
                new EnemyDef("远", Element.Heart, 100000, 80, range: AttackRange.Ranged),
            });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "迷", -1, sb);
                if (t % 2 == 1) Do(x, "花", t % 3, sb);
                Do(x, "击", (t + 1) % 3, sb);
            }, 9, all);
            string raw = all.ToString();
            Assert.That(raw.Contains("CharmedAttack:"), Is.True, "场景没走到魅惑反打");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(9));
            Assert.That(fp, Is.EqualTo(CharmExpected), fp);
        }

        // ---------------- 第 5 项:嫁接全流程(E37 / E42)----------------

        [Test]
        public void GraftFlow_NoNewState_Identical()
        {
            // Lv3 强化(Replaces Lv1 的 Summon 0)走现有 Fold 合并;幼苗跟嫁接取被嫁接者;本面无 pick SummonedThisCast 的效果
            var enhance = new TraitDef(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Active, TraitSlot.Lv1, "强化",
                new[] { new EffectDef(EffectKind.Summon, 0, passive: new SummonPassive { OnHitFreezeChance = 40 }) });
            var vine = new CharDef("藤", Element.Wood,
                effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 300, summonAttack: 20, summonChar: "木",
                        passive: new SummonPassive { OnHitFreezeChance = 20, OnHitFreezeTurns = 1 }),
                    new EffectDef(EffectKind.SummonSapling, 30, summonCount: 1),
                    new EffectDef(EffectKind.HealSummons, 20),
                },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) },
                traits: new[] { enhance });
            var thorn = Wood("荆", new EffectDef(EffectKind.Summon, 500, summonChar: "木",
                passive: new SummonPassive { Thorns = 50, Taunt = true }, summonDefense: 6));
            var log = new HookLog();
            var b = Battle(log, new[] { vine, thorn, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 120),
                new EnemyDef("远", Element.Heart, 100000, 70, range: AttackRange.Ranged),
            }, levels: new Dictionary<string, int> { ["藤"] = 5, ["荆"] = 2 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "荆", -1, sb);
                if (t == 1) Do(x, "藤", -1, sb, allySlot: 0);   // 嫁接到荆灵
                if (t == 2) Do(x, "藤", -1, sb);                 // 普通召唤
                if (t == 4) Do(x, "藤", -1, sb, allySlot: 1);   // 再嫁接
                Do(x, "击", t % 2, sb);
            }, 8, all);
            string raw = all.ToString();
            Assert.That(raw.Contains("Graft:"), Is.True, "场景没走到嫁接\n" + raw);
            Assert.That(raw.Contains(":Thorns;"), Is.True, "场景没走到荆棘");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(8));
            Assert.That(fp, Is.EqualTo(GraftExpected), fp);
        }

        // ---------------- 第 6 项:Summon 系效果分支(E38 / E41 / E43 / E46 / E47 / E48)----------------

        [Test]
        public void SummonFamilyBranches_NoAmpTerms_Identical()
        {
            var grove = Wood("林",
                new EffectDef(EffectKind.Summon, 400, summonCount: 2, summonAttack: 25, summonChar: "木",
                    passive: new SummonPassive { PerAllyAttackPercent = 10 }),
                new EffectDef(EffectKind.SummonSapling, 40, summonCount: 2));
            var mend = RebalanceFixture.Char("疗", new EffectDef(EffectKind.HealSummons, 40));
            var shade = RebalanceFixture.Char("荫",
                new EffectDef(EffectKind.HealSummons, 20, percentOfMax: true), new EffectDef(EffectKind.ShieldSummons, 44));
            var stab = RebalanceFixture.Char("刺", new EffectDef(EffectKind.SummonStrike, 70));
            var revive = RebalanceFixture.Char("复", new EffectDef(EffectKind.Revive, 1));
            var log = new HookLog();
            var b = Battle(log, new[] { grove, mend, shade, stab, revive, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 260),
                new EnemyDef("远", Element.Heart, 100000, 150, range: AttackRange.Ranged),
            }, levels: new Dictionary<string, int> { ["林"] = 3, ["疗"] = 4, ["荫"] = 2, ["刺"] = 2 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t % 4 == 0) Do(x, "林", -1, sb, replace: true);
                if (t % 3 == 1) Do(x, "疗", -1, sb);
                if (t % 3 == 2) Do(x, "荫", -1, sb);
                if (t % 2 == 1) Do(x, "刺", t % 2, sb);
                if (t % 4 == 3) Do(x, "复", -1, sb);
                Do(x, "击", t % 2, sb);
            }, 10, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "Summon:", "SummonAttack:", "Shield:", "Heal:", "SummonDied:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Contains("#复-1/-1=None;Summon:-1:"), Is.True, "复活真救回了一只\n" + raw);
            Assert.That(raw.Contains("#刺1/-1=None;SummonAttack:"), Is.True, "群刺真出手了\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(10));
            Assert.That(fp, Is.EqualTo(SummonFamilyExpected), fp);
        }

        // ---------------- 第 1 项:荆棘本命木灵挨打,荆棘 → 反弹 → 格挡反击全段(M1 荆棘光环 / M4 / M6 的落点)----------------

        [Test]
        public void ThornsChain_NoNewState_Identical()
        {
            var thorn = Wood("荆", new EffectDef(EffectKind.Summon, 900, summonAttack: 10, summonChar: "木",
                passive: new SummonPassive { Thorns = 40, Taunt = true }));
            var mirror = RebalanceFixture.Char("镜", new EffectDef(EffectKind.Reflect, 20, turns: 2));
            var parry = new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 2), new EffectDef(EffectKind.CounterBoost, 50) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 200) });
            var log = new HookLog();
            var b = Battle(log, new[] { thorn, mirror, parry, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 180),
                new EnemyDef("远", Element.Heart, 100000, 90, range: AttackRange.Ranged),
            });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "荆", -1, sb);
                if (t % 3 == 1) Do(x, "镜", -1, sb, allySlot: 0);
                if (t % 2 == 1) Do(x, "挡", -1, sb, allySlot: 0);
                Do(x, "击", t % 2, sb);
            }, 8, all);
            string raw = all.ToString();
            foreach (var needle in new[] { ":Thorns;", ":Reflect;", ":BlockCounter;", "SummonHit:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(8));
            Assert.That(fp, Is.EqualTo(ThornsExpected), fp);
        }

        // ---------------- 第 8 项:木灵跨场携带(E44)----------------

        [Test]
        public void SummonCarry_EntryArmorAndBurn_Identical()
        {
            // 只放现行会被原样携带的条目:Burn −1、SummonDefense 入场护甲(SourceId = 字)、坚木本命(字#armor)。
            // TurnsLeft > 0 的限时条目是 E44 刻意改变的行为,不在恒等范围内。
            var wood = new SummonSnapshot
            {
                Slot = 1, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 600, MaxHp = 800, Attack = 30, Speed = 150,
                Shield = 40, Passive = new SummonPassive { Armor = 5, Thorns = 20 },
                Statuses = new List<StatusEffect>
                {
                    new() { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 9, Potency = 100, TurnsLeft = -1, SourceId = "burn" },
                    new() { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 7, TurnsLeft = -1, SourceId = "塔" },
                    new() { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = -1, SourceId = "桩#armor" },
                },
            };
            var config = new RunConfig
            {
                Encounters = new[]
                {
                    new[] { RebalanceFixture.Mob(hp: 120, attack: 30) },
                    new[] { RebalanceFixture.Mob(hp: 120, attack: 30) },
                },
                RewardPool = new[] { "甲" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(), config,
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "甲", "甲", "甲", "甲" }, Array.Empty<string>(), seed: 5, startingSummons: new[] { wood });
            var sb = new StringBuilder();
            for (int t = 0; t < 12 && run.Battle.Phase == BattlePhase.PlayerTurn; t++)
            {
                sb.Append(run.Battle.Cast("甲", 0)).Append(';');
                if (run.Battle.Phase == BattlePhase.PlayerTurn) run.Battle.EndTurn();
                AppendSummons(sb, run.Battle.Summons);
                sb.Append('\n');
            }
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:第一场打赢");
            run.AdvanceAfterBattle();
            Assert.That(run.CarriedSummons.Count, Is.GreaterThan(0), "夹具前提:有木灵跨场");
            foreach (var s in run.CarriedSummons)
            {
                sb.Append($"#S{s.Slot}:{s.Char}:{s.SourceChar}:{s.Element}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.ActionMeter}"
                    + $"/{s.Speed}/{s.Shield}/{s.SproutParentSlot}/{PassiveOf(s.Passive)}[");
                foreach (var st in s.Statuses) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft}:{st.SourceId}:{st.Potency} ");
                sb.Append(']');
            }
            string raw = sb.ToString();
            Assert.That(raw.Contains("Burn:"), Is.True, "灼随木灵携带");
            Assert.That(raw.Contains("桩#armor"), Is.True, "坚木护甲随木灵携带");
            Assert.That(Hash(raw), Is.EqualTo(CarryExpected), raw);
        }

        private const string HealAllyExpected = "a0dba9e573253865 6345";
        private const string CharmExpected = "e9fb1d457f6e0db1 7216";
        // Plan E3 Task 1(Q14):Graft 事件的 Amount 由恒 0 改为回复量 —— 指纹只因这一个字段变化(原值 "40191a65b2438a21 9264";
        // 把 Amount 临时写回 0 时本测试原样通过,已核验)。
        private const string GraftExpected = "6b74b2e958eb187a 9266";
        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "e524cbf0cff04a05 18948")。
        private const string SummonFamilyExpected = "96f77c3695d381ea 18976";
        // 2026-10-16(反击预算改格挡前):预算基数换 preCut,指纹重取(原值 "5a776f5f6463ca6d 7005")。
        private const string ThornsExpected = "8f218f0526c07b61 6953";
        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "2944619ae0101fd6 403")。
        // spec C:删战后复原用的「原属性 / 原本命」两个快照字段,指纹只因格式变化(原值 "92e2ba139f7c4649 405";
        // 把这两段临时写回为战后恒有的 null 值时本测试原样通过,已核验)。
        private const string CarryExpected = "db65210af876dd47 402";
    }
}
