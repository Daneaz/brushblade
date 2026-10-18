using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>上状态动效统一 Task 1(spec 2026-10-18 §1):凡玩家可见的状态在单位身上新增 / 增加,Core 发一条
    /// <see cref="BattleEventKind.StatusApplied"/>。TargetIndex = 敌人下标 / 木灵槽位(玩家 −1);SecondIndex = 阵营
    /// (0 敌 / 1 玩家 / 2 木灵);Amount = (int)StatusKind;Absorbed = 本次增量(刷新 0)。
    /// 不发:灼(Burn / SummonBurn)、敌人加攻(EnemyBuff)、锋芒(MoraleGain)、隐藏载体、ApBoost、被拦下的施加。
    /// 夹具:Element.Heart、PlayerAttack = 100、卡等级 1、10 万血靶子。</summary>
    public class StatusAppliedEventTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config() => new()
        {
            PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 60, BossPhaseJitterPercent = 0,
            UnlockedSummonSlots = BattleEngine.AllSummonSlotsMask,
        };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, BattleConfig config = null,
            IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(defs), config ?? Config(), defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(),
                Array.Empty<string>(), enemies.Length > 0 ? enemies : new[] { Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => 1), startingSummons: summons);

        private static BattleEngine Battle(params CharDef[] defs) => Battle(defs, Array.Empty<EnemyDef>());

        private static EnemyDef Mob(EnemyAbility ability = EnemyAbility.None, int attack = 0) =>
            new("怔", Element.Heart, Hp, attack, ability);

        private static void Cast(BattleEngine b, string id, int target = -1, bool attackMode = false) =>
            Assert.That(b.Cast(id, target, attackMode: attackMode), Is.EqualTo(BattleError.None), id);

        private static List<BattleEvent> Applied(BattleEngine b) =>
            b.LastEvents.Where(e => e.Kind == BattleEventKind.StatusApplied).ToList();

        private static List<(int target, int side, StatusKind kind, int delta)> Rows(BattleEngine b) =>
            Applied(b).Select(e => (e.TargetIndex, e.SecondIndex, (StatusKind)e.Amount, e.Absorbed)).ToList();

        private static bool Has(BattleEngine b, StatusKind kind) => Applied(b).Any(e => e.Amount == (int)kind);

        private static SummonSnapshot Wood(int slot, SummonPassive passive = null, params StatusEffect[] statuses) => new()
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = Hp, MaxHp = Hp, Attack = 0,
            Speed = 100, Passive = passive, Statuses = statuses.ToList(),
        };

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void StatusApplied_AppendedAtEnd()
        {
            Assert.That((int)BattleEventKind.StatusApplied, Is.EqualTo((int)BattleEventKind.ElementChanged + 1));
            // 斩首再追加 Behead(BeheadTests 守它的位置)
            Assert.That((int)BattleEventKind.Behead, Is.EqualTo((int)BattleEventKind.StatusApplied + 1));
        }

        // ---------------- 敌人身上的减益 ----------------

        [Test]
        public void EnemyDebuffs_OneEventEach_WithDelta()
        {
            var def = RebalanceFixture.Char("缠",
                new EffectDef(EffectKind.Slow, 2),
                new EffectDef(EffectKind.ArmorBreak, 10, turns: 2),
                new EffectDef(EffectKind.Bleed, 10, turns: 3),
                new EffectDef(EffectKind.Vulnerable, 20, turns: 2));
            var b = Battle(new[] { def }, new[] { Mob(), Mob() });
            Cast(b, "缠", 1);
            var rows = Rows(b);
            Assert.That(rows.Count, Is.EqualTo(4), string.Join(",", rows));
            Assert.That(rows.Contains((1, 0, StatusKind.SpeedModifier, 50)), Is.True, "减速 −50 → 增量 50");
            Assert.That(rows.Contains((1, 0, StatusKind.ArmorBreak, 10)), Is.True);
            Assert.That(rows.Contains((1, 0, StatusKind.Bleed, 10)), Is.True);
            Assert.That(rows.Contains((1, 0, StatusKind.Vulnerable, 20)), Is.True);
        }

        [Test]
        public void GroupApplication_OneEventPerEnemy()
        {
            var def = RebalanceFixture.Char("裂", new EffectDef(EffectKind.ArmorBreak, 10, turns: 2, targetAll: true));
            var b = Battle(new[] { def }, new[] { Mob(), Mob(), Mob() });
            Cast(b, "裂", 0);
            Assert.That(Rows(b), Is.EqualTo(new[]
            {
                (0, 0, StatusKind.ArmorBreak, 10), (1, 0, StatusKind.ArmorBreak, 10), (2, 0, StatusKind.ArmorBreak, 10),
            }));
        }

        [Test]
        public void Refresh_SameSlowAgain_EmittedWithZeroDelta()
        {
            var def = RebalanceFixture.Char("缓", new EffectDef(EffectKind.Slow, 2));
            var b = Battle(def);
            Cast(b, "缓", 0);
            Assert.That(Rows(b), Is.EqualTo(new[] { (0, 0, StatusKind.SpeedModifier, 50) }));
            Cast(b, "缓", 0);
            Assert.That(Rows(b), Is.EqualTo(new[] { (0, 0, StatusKind.SpeedModifier, 0) }), "刷新照发,增量 0");
        }

        // ---------------- 玩家自身增益 ----------------

        [Test]
        public void PlayerBuffs_SidePlayer_TargetMinusOne()
        {
            var def = RebalanceFixture.Char("备",
                new EffectDef(EffectKind.Empower, 20),
                new EffectDef(EffectKind.DefenseBuff, 10, turns: 2),
                new EffectDef(EffectKind.Block, 2),
                new EffectDef(EffectKind.Kindle, 30, turns: 2));
            var b = Battle(def);
            Cast(b, "备");
            var rows = Rows(b);
            Assert.That(rows.Contains((-1, 1, StatusKind.AttackBuff, 20)), Is.True, string.Join(",", rows));
            Assert.That(rows.Contains((-1, 1, StatusKind.DefenseBuff, 10)), Is.True);
            Assert.That(rows.Contains((-1, 1, StatusKind.Block, 2)), Is.True);
            Assert.That(rows.Contains((-1, 1, StatusKind.Kindle, 30)), Is.True);
            Assert.That(rows.All(r => r.target == -1 && r.side == 1), Is.True);
        }

        // ---------------- AddPlayerCounter:战意 / 厚 / 泉 ----------------

        [Test]
        public void Morale_FirstAndStacked_DeltaIsActualGain_ZeroAtCapStillEmitted()
        {
            var def = RebalanceFixture.Char("战", new EffectDef(EffectKind.Morale, 2));
            var b = Battle(def);
            var deltas = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Cast(b, "战");
                var rows = Rows(b).Where(r => r.kind == StatusKind.Morale).ToList();
                Assert.That(rows.Count, Is.EqualTo(1), $"第 {i + 1} 张");
                Assert.That((rows[0].target, rows[0].side), Is.EqualTo((-1, 1)));
                deltas.Add(rows[0].delta);
            }
            Assert.That(deltas, Is.EqualTo(new[] { 2, 2, 1, 0 }), "上限 5:2 → 4 → 5 → 5");
        }

        [Test]
        public void HeftAndWellspring_Stacked_Emitted()
        {
            var def = RebalanceFixture.Char("蓄", new EffectDef(EffectKind.AddHeft, 1), new EffectDef(EffectKind.AddWellspring, 2));
            var b = Battle(def);
            Cast(b, "蓄");
            Cast(b, "蓄");
            var rows = Rows(b);
            Assert.That(rows.Contains((-1, 1, StatusKind.Heft, 1)), Is.True, string.Join(",", rows));
            Assert.That(rows.Contains((-1, 1, StatusKind.Wellspring, 2)), Is.True);
        }

        // ---------------- 旁路:金汇 / 磐固 / 杜门不出 ----------------

        [Test]
        public void BlockGain_OnExistingBlock_DeltaIsAddedCharges()
        {
            var def = new CharDef("汇", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 2, counterPercent: 60) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.BlockGain, 1) },
                isComponent: false);
            var b = Battle(def);
            Cast(b, "汇");
            Assert.That(Rows(b), Is.EqualTo(new[] { (-1, 1, StatusKind.Block, 2) }));
            Cast(b, "汇", 0, attackMode: true);
            Assert.That(Rows(b), Is.EqualTo(new[] { (-1, 1, StatusKind.Block, 1) }));
        }

        [Test]
        public void Harden_EachHit_SummonDefenseBuff_StopsAtCap()
        {
            var passive = new SummonPassive { Taunt = true, Harden = 10, HardenMax = 50 };
            var b = Battle(new CharDef[0], new[] { new EnemyDef("怔", Element.Heart, Hp, 100) },
                summons: new[] { Wood(0, passive) });
            int hits = 0;
            var rows = new List<(int target, int side, StatusKind kind, int delta)>();
            for (int turn = 0; turn < 20 && hits < 7; turn++)
            {
                b.EndTurn();
                hits += b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonHit && e.SecondIndex == 0);
                rows.AddRange(Rows(b).Where(r => r.kind == StatusKind.DefenseBuff));
            }
            Assert.That(hits, Is.GreaterThanOrEqualTo(6), "夹具前提:挨够 6 下才验得到封顶");
            Assert.That(rows, Is.EqualTo(Enumerable.Repeat((0, 2, StatusKind.DefenseBuff, 10), 5)), "封顶 50 后不再发");
        }

        [Test]
        public void ApCost_Seal_Emitted()
        {
            var def = RebalanceFixture.Char("杜", new EffectDef(EffectKind.DamageCut, 60), new EffectDef(EffectKind.ApCost, 1));
            var b = Battle(def);
            Cast(b, "杜");
            Assert.That(Rows(b).Contains((-1, 1, StatusKind.Seal, 1)), Is.True, string.Join(",", Rows(b)));
        }

        // ---------------- Fresh:首次上身 vs 叠层 / 刷新(Magnitude 恒 0 的状态靠它分) ----------------

        private static List<(StatusKind kind, bool fresh)> FreshRows(BattleEngine b) =>
            Applied(b).Select(e => ((StatusKind)e.Amount, e.Fresh)).ToList();

        [Test]
        public void Fresh_SummonTaunt_FirstTrueThenFalse()
        {
            var def = RebalanceFixture.Char("柘", new EffectDef(EffectKind.Taunt, 2, pick: EffectPick.AllSummons));
            var b = Battle(new[] { def }, Array.Empty<EnemyDef>(), summons: new[] { Wood(0) });
            Cast(b, "柘");
            Assert.That(FreshRows(b), Is.EqualTo(new[] { (StatusKind.Taunt, true) }), "首次嘲讽");
            Cast(b, "柘");
            Assert.That(FreshRows(b), Is.EqualTo(new[] { (StatusKind.Taunt, false) }), "再嘲讽 = 刷新");
        }

        [Test]
        public void Fresh_Slow_FirstTrueThenRefreshFalse()
        {
            var def = RebalanceFixture.Char("缓", new EffectDef(EffectKind.Slow, 2));
            var b = Battle(def);
            Cast(b, "缓", 0);
            Assert.That(FreshRows(b), Is.EqualTo(new[] { (StatusKind.SpeedModifier, true) }));
            Cast(b, "缓", 0);
            Assert.That(FreshRows(b), Is.EqualTo(new[] { (StatusKind.SpeedModifier, false) }));
        }

        [Test]
        public void Fresh_MoraleStacked_False()
        {
            var def = RebalanceFixture.Char("战", new EffectDef(EffectKind.Morale, 2));
            var b = Battle(def);
            Cast(b, "战");
            Assert.That(FreshRows(b).Where(r => r.kind == StatusKind.Morale), Is.EqualTo(new[] { (StatusKind.Morale, true) }));
            Cast(b, "战");
            Assert.That(FreshRows(b).Where(r => r.kind == StatusKind.Morale), Is.EqualTo(new[] { (StatusKind.Morale, false) }));
        }

        [Test]
        public void Fresh_OtherEvents_AlwaysFalse()
        {
            var def = RebalanceFixture.Char("缓", new EffectDef(EffectKind.Slow, 2), new EffectDef(EffectKind.DamageSingle, 100));
            var b = Battle(def);
            Cast(b, "缓", 0);
            Assert.That(b.LastEvents.Where(e => e.Kind != BattleEventKind.StatusApplied).All(e => !e.Fresh), Is.True);
        }

        // ---------------- 木灵光环 ----------------

        [Test]
        public void SummonEmpower_AllSummons_OneEventPerAliveSummon()
        {
            var def = RebalanceFixture.Char("立", new EffectDef(EffectKind.SummonEmpower, 20, turns: 3, pick: EffectPick.AllSummons));
            var b = Battle(new[] { def }, Array.Empty<EnemyDef>(), summons: new[] { Wood(0), Wood(2) });
            Cast(b, "立");
            Assert.That(Rows(b), Is.EqualTo(new[] { (0, 2, StatusKind.SummonEmpower, 20), (2, 2, StatusKind.SummonEmpower, 20) }));
        }

        // ---------------- 不发 ----------------

        [Test]
        public void Burn_OnlyBurnEvent()
        {
            var def = RebalanceFixture.Char("灼", new EffectDef(EffectKind.BurnSingle, 3));
            var b = Battle(def);
            Cast(b, "灼", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn), Is.True, "前提");
            Assert.That(Applied(b), Is.Empty);
        }

        [Test]
        public void EnemyScorchBuff_OnlyEnemyBuff()
        {
            var hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
            var b = Battle(new[] { hit }, new[] { Mob(EnemyAbility.Scorch) });
            Cast(b, "击", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyBuff), Is.True, "前提");
            Assert.That(Applied(b), Is.Empty);
        }

        [Test]
        public void CritMorale_OnlyMoraleGain_FirstAndStacked()
        {
            var config = Config();
            config.PlayerCritChance = 100;
            config.MoraleOnCrit = 1;
            var hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
            var b = Battle(new[] { hit }, new[] { Mob() }, config);
            for (int i = 0; i < 2; i++)
            {
                Cast(b, "击", 0);
                Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.MoraleGain), Is.True, $"前提:第 {i + 1} 张锋芒兑现");
                Assert.That(Has(b, StatusKind.Morale), Is.False, "锋芒已有 MoraleGain,不重复发");
            }
        }

        [Test]
        public void HiddenCarrier_ShieldRecoil_NotEmitted()
        {
            var def = RebalanceFixture.Char("震", new EffectDef(EffectKind.Shield, 100), new EffectDef(EffectKind.ShieldRecoil, 30));
            var b = Battle(def);
            Cast(b, "震");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.True, "前提:反震挂上了");
            Assert.That(Has(b, StatusKind.ShieldRecoil), Is.False);
        }

        [Test]
        public void ApBoost_NotEmitted()
        {
            var def = RebalanceFixture.Char("利", new EffectDef(EffectKind.ApBoost, 1));
            var b = Battle(def);
            Cast(b, "利");
            Cast(b, "利");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.ApBoost), Is.EqualTo(2), "前提");
            Assert.That(Applied(b), Is.Empty);
        }

        [Test]
        public void BuffBlocked_NotEmitted()
        {
            var lockDef = RebalanceFixture.Char("锁",
                new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 2));
            var hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
            var b = Battle(new[] { lockDef, hit }, new[] { Mob(EnemyAbility.Scorch) });
            Cast(b, "锁", 0);
            Assert.That(Rows(b).Contains((0, 0, StatusKind.BuffBlock, 0)), Is.True, "禁增益本身可见,照发");
            Cast(b, "击", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BuffBlocked), Is.True, "前提");
            Assert.That(Applied(b), Is.Empty);
        }

        [Test]
        public void DebuffWarded_NotEmitted()
        {
            var bathe = RebalanceFixture.Char("濯",
                new EffectDef(EffectKind.Cleanse, 0), new EffectDef(EffectKind.DebuffWard, 0, turns: 1));
            var b = Battle(new[] { bathe }, new[] { new EnemyDef("覆", Element.Heart, Hp, 10, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, Hp, 10, skill: BossSkill.Topple) }) });
            bool sawWard = false;
            for (int round = 0; round < 3 && !sawWard; round++)
            {
                Cast(b, "濯");
                b.EndTurn();
                sawWard = b.LastEvents.Any(e => e.Kind == BattleEventKind.DebuffWarded && e.Amount == (int)StatusKind.Seal);
                if (sawWard) Assert.That(Has(b, StatusKind.Seal), Is.False, "被挡下的封字不发");
            }
            Assert.That(sawWard, Is.True, "前提:倾覆的封字被挡");
        }

        [Test]
        public void Freeze_BlockedByFrostResist_NotEmitted()
        {
            var def = RebalanceFixture.Char("冻", new EffectDef(EffectKind.Freeze, 1));
            var b = Battle(def);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = 2 });
            Cast(b, "冻", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "前提:霜抗挡下");
            Assert.That(Applied(b), Is.Empty);
        }

        [Test]
        public void BossFreeze_EmitsIceStallOnly()
        {
            var def = RebalanceFixture.Char("冻", new EffectDef(EffectKind.Freeze, 1));
            var b = Battle(new[] { def }, new[] { RebalanceFixture.Boss() });
            Cast(b, "冻", 0);
            var rows = Rows(b);
            Assert.That(rows.Count, Is.EqualTo(1), string.Join(",", rows));
            Assert.That((rows[0].target, rows[0].side, rows[0].kind), Is.EqualTo((0, 0, StatusKind.IceStall)));
        }

        // ---------------- 白名单对账 ----------------

        /// <summary>审计 §5:StatusText.Of 没有 case 的 kind(Presentation 侧无名、无 chip)。写死,
        /// 新增 StatusKind 时由下面的对账逼着决定它属于哪边。⚠ 改 Presentation 的 StatusText.Of(增删 case)要同步这里与 StatusCueRules。</summary>
        private static readonly StatusKind[] Hidden =
        {
            StatusKind.ObsoleteDamageReduction, StatusKind.TraitRider, StatusKind.ShieldRecoil,
            StatusKind.BurnGrow, StatusKind.BurnHold, StatusKind.BurnBurstMark, StatusKind.BurnBacklashMark,
            StatusKind.ApRefund, StatusKind.MoraleArmor, StatusKind.MoraleShield,
            StatusKind.FrostBite, StatusKind.ThawStrike, StatusKind.ThawSlow,
            StatusKind.ShieldFrost, StatusKind.ShieldShatter,
        };

        [Test]
        public void IsAnnounced_ExactlyVisibleAndNotApBoost()
        {
            foreach (StatusKind k in Enum.GetValues(typeof(StatusKind)))
            {
                bool expected = !Hidden.Contains(k) && k != StatusKind.ApBoost;
                Assert.That(StatusCueRules.IsAnnounced(k), Is.EqualTo(expected), k.ToString());
            }
        }

        // ---------------- 顺手修:桂全场木灵加盾 ----------------

        [Test]
        public void SummonShield_AllSummons_EachGetsShieldEvent()
        {
            var def = new CharDef("桂", Element.Wood, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 300, summonAttack: 0, summonShield: 50),
            });
            var b = Battle(new[] { def }, Array.Empty<EnemyDef>(), summons: new[] { Wood(0) });
            Cast(b, "桂");
            var shields = b.LastEvents.Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex >= 0)
                .Select(e => (e.TargetIndex, e.Amount)).ToList();
            var alive = Enumerable.Range(0, b.Summons.Count).Where(s => b.Summons[s] != null && b.Summons[s].Alive).ToList();
            Assert.That(alive.Count, Is.EqualTo(2), "前提:原有一只 + 新召一只");
            Assert.That(shields, Is.EqualTo(alive.Select(s => (s, 50))));
        }
    }
}
