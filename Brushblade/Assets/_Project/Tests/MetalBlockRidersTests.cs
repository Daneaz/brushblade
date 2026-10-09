using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 2:格挡附带运行时(附录 J1)与 AP 返还(J2)。
    /// 贯穿 / 多击 / 立威 / 格挡流血 / 格挡加战意 / 反击击杀返还 AP;预算共用扣完即止(Q2)、立威不吃预算而 Boss ×2 吃(Q3)、
    /// 合并(Q4)、得利每轮至多一次(Q5);木灵格挡同样生效;Block 新字段 Clone 与存档往返;ConfigLoader。
    ///
    /// 夹具:字一律 Element.Heart、攻击面 DamageSingle 100、Lv1 → 反击 30;PlayerAttack 100。
    /// 预算 = 敌人攻击 × 60%(格挡减伤后)× 60%:攻击 1000 → 360,攻击 100 → 36。</summary>
    public class MetalBlockRidersTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 100000, PlayerAttack = 100, ApPerTurn = 20 };

        /// <summary>格挡 <paramref name="charges"/> 次 + 一条 BlockMod(写在本体里,同样由 Fold 折叠)。</summary>
        private static CharDef Guard(string id, EffectDef mod, int charges = 9) => new(id, Element.Heart,
            effects: mod == null
                ? new[] { new EffectDef(EffectKind.Block, charges) }
                : new[] { new EffectDef(EffectKind.Block, charges), mod },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static EffectDef Mod(bool column = false, int hits = 0, int execute = 0, int bleed = 0, int morale = 0,
            int refund = 0) =>
            new(EffectKind.BlockMod, 0, counterColumn: column, counterHits: hits, counterExecuteBelow: execute,
                blockBleed: bleed, blockMorale: morale, killRefundAp: refund);

        private static BattleEngine Battle(CharDef[] defs, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => 1));

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) => Battle(new[] { def }, enemies);

        private static EnemyDef Mob(int attack, int hp = Hp) => RebalanceFixture.Mob(hp: hp, attack: attack);

        private static int Lost(BattleEngine b, int i) => b.Enemies[i].MaxHp - b.Enemies[i].Hp;

        private static List<BattleEvent> EndTurn(BattleEngine b)
        {
            b.EndTurn();
            return b.LastEvents.ToList();
        }

        private static int CounterEvents(IEnumerable<BattleEvent> events) =>
            events.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.BlockCounter);

        // ---------------- 多击(剁截) ----------------

        [Test]
        public void CounterHits_ThreeHitsOfFullCounter_WhenBudgetAllows()
        {
            var b = Battle(Guard("剁", Mod(hits: 3)), Mob(1000));
            b.Cast("剁", -1, attackMode: false);
            var ev = EndTurn(b);
            Assert.That(CounterEvents(ev), Is.EqualTo(3));
            Assert.That(Lost(b, 0), Is.EqualTo(90), "30 × 3,预算 360 够用");
        }

        [Test]
        public void CounterHits_ShareOneBudget_StopWhenExhausted()
        {
            var b = Battle(Guard("剁", Mod(hits: 3)), Mob(100));
            b.Cast("剁", -1, attackMode: false);
            var ev = EndTurn(b);
            Assert.That(Lost(b, 0), Is.EqualTo(36), "30 + 6 = 预算 36,扣完即止");
            Assert.That(CounterEvents(ev), Is.EqualTo(2), "第三击没有余额,不打");
        }

        [Test]
        public void CounterHits_StopWhenAttackerDies()
        {
            var b = Battle(Guard("剁", Mod(hits: 3)), Mob(1000, hp: 50), Mob(0));
            b.Cast("剁", -1, attackMode: false);
            var ev = EndTurn(b);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(CounterEvents(ev.Where(e => e.TargetIndex == 0)), Is.EqualTo(2), "30 + 30 → 死,第三击不打");
        }

        // ---------------- 贯穿(锥立) ----------------

        private static EnemyDef[] FourMobs(int attackerSlot, int attack) => Enumerable.Range(0, 4).Select(i =>
            new EnemyDef("怔" + i, Element.Heart, Hp, i == attackerSlot ? attack : 0,
                row: i == 3 ? EnemyRow.Back : EnemyRow.Front)).ToArray();

        /// <summary>前排里与后排(下标 3)同列的那只。</summary>
        private static int FrontMateOfBack()
        {
            var probe = Battle(Guard("锥", null), FourMobs(-1, 0));
            var back = probe.Enemies[3];
            for (int i = 0; i < 3; i++)
                if (probe.Enemies[i].Column < back.ColumnEnd && back.Column < probe.Enemies[i].ColumnEnd) return i;
            Assert.Fail("夹具:后排没有同列的前排");
            return -1;
        }

        [Test]
        public void CounterColumn_HitsAttacker_ThenSameColumnAt70Percent()
        {
            int attacker = FrontMateOfBack();
            var b = Battle(Guard("锥", Mod(column: true)), FourMobs(attacker, 1000));
            b.Cast("锥", -1, attackMode: false);
            EndTurn(b);
            Assert.That(Lost(b, attacker), Is.EqualTo(30));
            Assert.That(Lost(b, 3), Is.EqualTo(21), "同列其余 = 30 × 70%");
            for (int i = 0; i < 3; i++)
                if (i != attacker) Assert.That(Lost(b, i), Is.EqualTo(0), $"不同列的 {i} 不挨");
        }

        [Test]
        public void CounterColumn_SharesBudget_AttackerFirst()
        {
            int attacker = FrontMateOfBack();
            // 攻击 100 → 预算 36:攻击者 30,同列只剩 6
            var b = Battle(Guard("锥", Mod(column: true)), FourMobs(attacker, 100));
            b.Cast("锥", -1, attackMode: false);
            EndTurn(b);
            Assert.That(Lost(b, attacker), Is.EqualTo(30));
            Assert.That(Lost(b, 3), Is.EqualTo(6), "共用一份预算,先攻击者再同列");
        }

        // ---------------- 立威 ----------------

        [Test]
        public void Execute_KillsMobBelowThreshold_IgnoresBudget_WinsIfLast()
        {
            var b = Battle(Guard("铡", Mod(execute: 20)), Mob(1, hp: 1000));
            b.Enemies[0].Hp = 150;   // 15% < 20%
            b.Cast("铡", -1, attackMode: false);
            var ev = EndTurn(b);
            Assert.That(b.Enemies[0].Alive, Is.False, "攻击 1 → 预算 0,斩杀照样发生(Q3)");
            Assert.That(ev.Any(e => e.Kind == BattleEventKind.EnemyDied && e.TargetIndex == 0), Is.True);
            Assert.That(CounterEvents(ev), Is.EqualTo(0), "斩杀不是反击伤害");
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "敌人回合里斩掉最后一名敌人照常判胜");
        }

        [Test]
        public void Execute_AboveThreshold_IsPlainCounter()
        {
            var b = Battle(Guard("铡", Mod(execute: 20)), Mob(1000, hp: 1000));
            b.Enemies[0].Hp = 250;   // 25%
            b.Cast("铡", -1, attackMode: false);
            EndTurn(b);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(220), "照常反击 30");
        }

        [Test]
        public void Execute_OnBoss_DoublesCounter_StillBudgeted()
        {
            var roomy = Battle(Guard("铡", Mod(execute: 20)), RebalanceFixture.Boss(attack: 1000));
            roomy.Enemies[0].Hp = 10000;   // 10%
            roomy.Cast("铡", -1, attackMode: false);
            EndTurn(roomy);
            Assert.That(roomy.Enemies[0].Alive, Is.True, "Boss 不可斩杀");
            Assert.That(10000 - roomy.Enemies[0].Hp, Is.EqualTo(60), "反击 ×2");

            var tight = Battle(Guard("铡", Mod(execute: 20)), RebalanceFixture.Boss(attack: 100));
            tight.Enemies[0].Hp = 10000;
            tight.Cast("铡", -1, attackMode: false);
            EndTurn(tight);
            Assert.That(10000 - tight.Enemies[0].Hp, Is.EqualTo(36), "×2 后的 60 仍被预算 36 钳住");
        }

        [Test]
        public void Execute_RecordsSourceCharId()
        {
            var b = Battle(Guard("铡", Mod(execute: 20)), Mob(0));
            b.Cast("铡", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).ExecuteSourceCharId, Is.EqualTo("铡"));
            var plain = Battle(Guard("铠", null), Mob(0));
            plain.Cast("铠", -1, attackMode: false);
            Assert.That(plain.PlayerStatuses.Find(StatusKind.Block).ExecuteSourceCharId, Is.Null);
        }

        // ---------------- 格挡流血(刀山 / 匿锋)、格挡加战意(坚营) ----------------

        [Test]
        public void BlockBleed_AppliedOnBlock_EvenWithZeroBudget_AmountLockedAtCast()
        {
            var b = Battle(Guard("刲", Mod(bleed: 35)), Mob(1));
            b.Cast("刲", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).BlockBleed, Is.EqualTo(35), "Lv1 × 攻击力 100%");
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = -1 });
            EndTurn(b);
            var bleed = b.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed, Is.Not.Null, "预算 0 也挂");
            Assert.That(bleed.Magnitude, Is.EqualTo(35), "量出字时定死,之后涨攻击力不变");
            Assert.That(bleed.TurnsLeft, Is.EqualTo(3).Or.EqualTo(2), "缺省 3 回合");
        }

        [Test]
        public void BlockBleed_ScalesWithAttackAtCast()
        {
            var b = Battle(Guard("刲", Mod(bleed: 35)), Mob(0));
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = -1 });
            b.Cast("刲", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).BlockBleed, Is.EqualTo(35 * 150 / 100), "战意 5 层 = 攻击 150%");
        }

        [Test]
        public void BlockMorale_EachConsumedBlock_GivesPlayerMorale()
        {
            var b = Battle(Guard("剿", Mod(morale: 1)), Mob(100), Mob(100));
            b.Cast("剿", -1, attackMode: false);
            EndTurn(b);
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "两次格挡各 +1");
        }

        // ---------------- 得利(J1 killRefund + J2 ApRefund) ----------------

        [Test]
        public void KillRefund_CounterKill_GivesOneApNextTurn_AtMostOncePerRound()
        {
            var control = Battle(Guard("利", null), Mob(1000, hp: 20), Mob(1000, hp: 20), Mob(0));
            control.Cast("利", -1, attackMode: false);
            EndTurn(control);
            int baseAp = control.Ap;

            var b = Battle(Guard("利", Mod(refund: 1)), Mob(1000, hp: 20), Mob(1000, hp: 20), Mob(0));
            b.Cast("利", -1, attackMode: false);
            EndTurn(b);
            Assert.That(b.Enemies[0].Alive || b.Enemies[1].Alive, Is.False, "前提:两只都被反击打死");
            Assert.That(b.Ap, Is.EqualTo(baseAp + 1), "两次反击击杀,本轮只返还一次");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ApRefund), Is.False, "兑现即移除");
            EndTurn(b);
            Assert.That(b.Ap, Is.EqualTo(baseAp), "没有击杀的一轮不返还");
        }

        [Test]
        public void KillRefund_ExecuteKillCounts()
        {
            var control = Battle(Guard("利", null), Mob(1), Mob(0));
            EndTurn(control);
            int baseAp = control.Ap;
            var b = Battle(Guard("利", new EffectDef(EffectKind.BlockMod, 0, counterExecuteBelow: 20, killRefundAp: 1)),
                Mob(1, hp: 1000), Mob(0));
            b.Enemies[0].Hp = 100;
            b.Cast("利", -1, attackMode: false);
            EndTurn(b);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.Ap, Is.EqualTo(baseAp + 1));
        }

        [Test]
        public void NoKill_NoRefund()
        {
            var b = Battle(Guard("利", Mod(refund: 1)), Mob(1000));
            b.Cast("利", -1, attackMode: false);
            EndTurn(b);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ApRefund), Is.False);
            Assert.That(b.Ap, Is.EqualTo(Config.ApPerTurn));
        }

        // ---------------- 木灵格挡 ----------------

        private static CharDef Summoner() => new("垛", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 400, summonCount: 1, summonAttack: 0, summonChar: "木") });

        private static int CastOnSummon(BattleEngine b, string guard)
        {
            Assert.That(b.Cast("垛"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);
            Assert.That(b.Cast(guard, -1, attackMode: false, allySlot: slot), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[slot].Statuses.Has(StatusKind.Block), Is.True, "前提:格挡落在木灵身上");
            return slot;
        }

        [Test]
        public void SummonBlock_Riders_HitsMoraleRefund()
        {
            var control = Battle(new[] { Guard("剁", null), Summoner() }, Mob(300, hp: 50), Mob(0));
            EndTurn(control);
            int baseAp = control.Ap;

            // 攻击 300 → 木灵挨 180 → 预算 108;反击 30 × 3 = 90 打死 50 血的攻击者(第 2 击)
            var b = Battle(new[] { Guard("剁", Mod(hits: 3, morale: 1, refund: 1)), Summoner() }, Mob(300, hp: 50), Mob(0));
            CastOnSummon(b, "剁");
            var ev = EndTurn(b);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(CounterEvents(ev.Where(e => e.TargetIndex == 0)), Is.EqualTo(2));
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.GreaterThanOrEqualTo(1), "木灵格挡的战意给玩家");
            Assert.That(b.Ap, Is.EqualTo(baseAp + 1), "木灵格挡的反击击杀也返还(Q5)");
        }

        [Test]
        public void SummonBlock_HitsShareBudget()
        {
            // 攻击 100 → 木灵挨 60 → 预算 36
            var b = Battle(new[] { Guard("剁", Mod(hits: 3)), Summoner() }, Mob(100));
            CastOnSummon(b, "剁");
            EndTurn(b);
            Assert.That(Lost(b, 0), Is.EqualTo(36));
        }

        // ---------------- 合并(Q4) ----------------

        [Test]
        public void Merge_NumbersTakeMax_SwitchesUnion_ExecuteSourceFollowsLatestExecute()
        {
            var a = Guard("甲二", new EffectDef(EffectKind.BlockMod, 0, counterHits: 3, counterExecuteBelow: 20, blockBleed: 10), charges: 1);
            var c = Guard("乙二", new EffectDef(EffectKind.BlockMod, 0, counterColumn: true, counterHits: 2, blockBleed: 35,
                blockMorale: 1, killRefundAp: 1), charges: 2);
            var d = Guard("丙二", new EffectDef(EffectKind.BlockMod, 0, counterExecuteBelow: 10), charges: 1);
            var b = Battle(new[] { a, c, d }, Mob(0));
            b.Cast("甲二", -1, attackMode: false);
            b.Cast("乙二", -1, attackMode: false);
            var s = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(b.PlayerStatuses.All.Count(x => x.Kind == StatusKind.Block), Is.EqualTo(1));
            Assert.That((s.Magnitude, s.CounterHits, s.CounterColumn, s.CounterExecuteBelow, s.BlockBleed, s.BlockMorale, s.KillRefundAp),
                Is.EqualTo((2, 3, true, 20, 35, 1, 1)));
            Assert.That(s.ExecuteSourceCharId, Is.EqualTo("甲二"), "后来的没有立威:沿用");
            b.Cast("丙二", -1, attackMode: false);
            s = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(s.CounterExecuteBelow, Is.EqualTo(20), "阈值取大");
            Assert.That(s.ExecuteSourceCharId, Is.EqualTo("丙二"), "跟最近一次带立威的施加");
            Assert.That((s.CounterColumn, s.CounterHits, s.BlockBleed, s.BlockMorale, s.KillRefundAp), Is.EqualTo((true, 3, 35, 1, 1)),
                "新条不带的附带从旧条沿用(开关取并、数值取大)");
        }

        // ---------------- Clone / 存档 ----------------

        private static StatusEffect FullBlock() => new()
        {
            Kind = StatusKind.Block, Polarity = StatusPolarity.Buff, Magnitude = 2, CounterDamage = 30, TurnsLeft = -1,
            CounterColumn = true, CounterHits = 3, CounterExecuteBelow = 20, BlockBleed = 35, BlockMorale = 1,
            KillRefundAp = 1, ExecuteSourceCharId = "铡",
        };

        private static void AssertRiders(StatusEffect s)
        {
            Assert.That((s.CounterColumn, s.CounterHits, s.CounterExecuteBelow, s.BlockBleed, s.BlockMorale, s.KillRefundAp,
                s.ExecuteSourceCharId), Is.EqualTo((true, 3, 20, 35, 1, 1, "铡")));
        }

        [Test]
        public void Clone_CopiesBlockRiders()
        {
            AssertRiders(FullBlock().Clone());
        }

        [Test]
        public void BlockRiders_SurviveRealSaveFile()
        {
            var guard = Guard("铡", Mod(column: true, hits: 3, execute: 20, bleed: 35, morale: 1, refund: 1));
            var enemy = Mob(100);
            var graph = RebalanceFixture.Graph(guard);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "铡" } };
            var run = new RunEngine(graph, runConfig, Config, new[] { "铡", "铡" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2);
            Assert.That(run.Battle.Cast("铡", -1, attackMode: false), Is.EqualTo(BattleError.None));
            run.Battle.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.ApRefund, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = -1 });
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, null,
                startingInk: 50, perFloorNormalShield: 2);
            var block = restored.Battle.PlayerStatuses.Find(StatusKind.Block);
            Assert.That((block.CounterHits, block.CounterColumn, block.CounterExecuteBelow, block.BlockBleed,
                block.BlockMorale, block.KillRefundAp, block.ExecuteSourceCharId),
                Is.EqualTo((3, true, 20, 35, 1, 1, "铡")));
            Assert.That(restored.Battle.PlayerStatuses.Find(StatusKind.ApRefund)?.Magnitude, Is.EqualTo(1), "ApRefund 跨存档");
        }

        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string effects) =>
            Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""剁"",""element"":""Metal"",""effects"":[" + effects
                + @"],""attackEffects"":[{""kind"":""DamageSingle"",""value"":40}]}]}");

        [Test]
        public void ConfigLoader_ParsesRiderFields_FoldCarriesThemToBlock()
        {
            var g = Load(@"{""kind"":""Block"",""value"":1},{""kind"":""BlockMod"",""counterColumn"":true,""counterHits"":3,
                ""counterExecuteBelow"":20,""blockBleed"":35,""blockMorale"":1,""killRefundAp"":1}");
            var m = g.Get("剁").Effects[1];
            Assert.That((m.CounterColumn, m.CounterHits, m.CounterExecuteBelow, m.BlockBleed, m.BlockMorale, m.KillRefundAp),
                Is.EqualTo((true, 3, 20, 35, 1, 1)));
            var folded = TraitRules.CastEffects(g.Get("剁"), CardFace.Feature, 1);
            Assert.That(folded.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.Block }));
            var blk = folded[0];
            Assert.That((blk.CounterColumn, blk.CounterHits, blk.CounterExecuteBelow, blk.BlockBleed, blk.BlockMorale, blk.KillRefundAp),
                Is.EqualTo((true, 3, 20, 35, 1, 1)));
        }

        [TestCase(@"{""kind"":""Block"",""value"":1,""counterHits"":3}")]           // 只给 BlockMod
        [TestCase(@"{""kind"":""Shield"",""value"":5,""blockBleed"":35}")]
        [TestCase(@"{""kind"":""BlockMod"",""counterHits"":-1}")]
        [TestCase(@"{""kind"":""BlockMod"",""counterExecuteBelow"":100}")]
        [TestCase(@"{""kind"":""BlockMod"",""killRefundAp"":-1}")]
        public void ConfigLoader_RejectsMisplacedOrBadRiders(string effect)
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(effect));
        }

        [Test]
        public void ConfigLoader_BlockModWithOnlyRider_IsAccepted()
        {
            Assert.DoesNotThrow(() => Load(@"{""kind"":""Block"",""value"":1},{""kind"":""BlockMod"",""blockMorale"":1}"));
        }
    }
}
