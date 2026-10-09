using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 3:拦截族 —— 附录 W3 BuffBlock(洗尽铅华,Q21 霜抗豁免)、W4 DebuffWard(濯身 / 浇熄,
    /// WardOf / WardCount / 转盾;灼按 RefreshBurn 增量计层)。
    ///
    /// 夹具口径同 WaterSharedExtTests:Element.Heart、PlayerAttack = 100、敌人 10 万血。
    /// 我方的减益来源只有灯花(Sear,RefreshBurn 1 层)与 Boss 倾覆(Seal);灯花恒刷 1 层,
    /// 多层增量那几条直调 <c>BattleEngine.RefreshBurn</c>(internal,同一个入口)。</summary>
    public class WaterWardTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Sprout = new("苗", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 3000, summonCount: 1, summonAttack: 0) });
        private static readonly CharDef Chill = RebalanceFixture.Char("冻",
            new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Freeze, 1));

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies)
        {
            var defs = new[] { def, Hit, Sprout, Chill };
            return new BattleEngine(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: new Dictionary<string, int> { [def.Id] = 1 });
        }

        private static void Cast(BattleEngine b, string id, int target = -1, int allySlot = Targeting.PlayerTarget) =>
            Assert.That(b.Cast(id, target, allySlot: allySlot), Is.EqualTo(BattleError.None), id);

        private static EnemyDef Mob(EnemyAbility ability = EnemyAbility.None, int attack = 0) =>
            new("怔", Element.Heart, Hp, attack, ability);

        private static EnemyDef Toppler() =>
            new("覆", Element.Heart, Hp, 10, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, Hp, 10, skill: BossSkill.Topple) });

        private static int AttackBuffs(BattleEngine b, int i) =>
            b.Enemies[i].Statuses.All.Count(s => s.Kind == StatusKind.AttackBuff);

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<(UnitRef Subject, StatusKind Status)> Applied = new();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied) Applied.Add((args.Subject, args.Status));
            }
        }

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)StatusKind.BuffBlock, Is.EqualTo((int)StatusKind.ThawSlow + 1));
            Assert.That((int)StatusKind.DebuffWard, Is.EqualTo((int)StatusKind.ThawSlow + 2));
            Assert.That((int)EffectKind.BuffBlock, Is.EqualTo((int)EffectKind.ChargeDelay + 1));
            Assert.That((int)EffectKind.DebuffWard, Is.EqualTo((int)EffectKind.ChargeDelay + 2));
        }

        // ================= W3 BuffBlock =================

        /// <summary>洗尽铅华:DispelAll + BuffBlock turns 2。之后焦痕受击不再自燃加攻(也不发 EnemyBuff),StatusApplied 不发。</summary>
        [Test]
        public void BuffBlock_StopsScorchSelfBuff_NoEventNoHook()
        {
            var wash = RebalanceFixture.Char("洗", new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.Dispel, -1), new EffectDef(EffectKind.BuffBlock, 0, turns: 2));
            var b = Battle(wash, Mob(EnemyAbility.Scorch));
            var log = new HookLog();
            b.AddHookListener(log);
            Cast(b, "洗", 0);
            Assert.That(AttackBuffs(b, 0), Is.EqualTo(0), "本字伤害触发的自燃被同面的驱散清掉");
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.BuffBlock)?.TurnsLeft, Is.EqualTo(2));
            log.Applied.Clear();
            Cast(b, "击", 0);
            Assert.That(AttackBuffs(b, 0), Is.EqualTo(0), "BuffBlock 期间焦痕自燃被拦下");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyBuff), Is.False, "拦下的加攻不发 EnemyBuff");
            Assert.That(log.Applied.Any(a => a.Status == StatusKind.AttackBuff), Is.False, "拦下不发 StatusApplied");
        }

        /// <summary>标点小妖给同伴加攻:带 BuffBlock 的那只拿不到,另一只照拿;EnemyBuff 事件只发给真加上的。</summary>
        [Test]
        public void BuffBlock_StopsPunctuationAura_OnlyOnBlockedTarget()
        {
            var block = RebalanceFixture.Char("锁", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 2));
            var b = Battle(block, Mob(), Mob(), Mob(EnemyAbility.Buff));
            Cast(b, "锁", 0);
            b.EndTurn();
            Assert.That(AttackBuffs(b, 0), Is.EqualTo(0), "带 BuffBlock 的拿不到加攻");
            Assert.That(AttackBuffs(b, 1), Is.EqualTo(1), "没带的照拿");
            var buffEvents = b.LastEvents.Where(e => e.Kind == BattleEventKind.EnemyBuff).Select(e => e.TargetIndex).ToList();
            Assert.That(buffEvents.Contains(0), Is.False, "被拦的不发 EnemyBuff");
            Assert.That(buffEvents.Contains(1), Is.True);
        }

        /// <summary>Q21(硬要求):BuffBlock 豁免霜抗 —— 冻结到期照挂霜抗,否则冻结可无限连锁(破 R1)。</summary>
        [Test]
        public void BuffBlock_DoesNotBlockFrostResist()
        {
            var block = RebalanceFixture.Char("锁", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 3));
            var b = Battle(block, Mob());
            Cast(b, "锁", 0);
            Cast(b, "冻", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.True);
            b.EndTurn();   // 冻结那拍到期 → 霜抗
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.BuffBlock), Is.True, "BuffBlock 仍在(3 → 2)");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostResist), Is.True, "霜抗豁免 BuffBlock");
            Assert.That(b.Cast("冻", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "霜抗中不得再冻(R1 照常)");
        }

        /// <summary>「2 回合」= 该敌人 2 次行动:第 1 次行动后仍挡,第 2 次行动后移除,之后自燃照常。</summary>
        [Test]
        public void BuffBlock_ExpiresAfterTwoEnemyActions()
        {
            var block = RebalanceFixture.Char("锁", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 2));
            var b = Battle(block, Mob(EnemyAbility.Scorch));
            Cast(b, "锁", 0);
            b.Enemies[0].Statuses.Remove(StatusKind.AttackBuff);   // 本字伤害先触发的那次自燃,不在本测试范围
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.BuffBlock)?.TurnsLeft, Is.EqualTo(1), "第 1 次行动后剩 1");
            Cast(b, "击", 0);
            Assert.That(AttackBuffs(b, 0), Is.EqualTo(0), "仍在拦截期");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.BuffBlock), Is.False, "第 2 次行动后移除");
            Cast(b, "击", 0);
            Assert.That(AttackBuffs(b, 0), Is.EqualTo(1), "失效后自燃照常");
        }

        /// <summary>支持 Pick / OnlyIf:pick All 给全体存活敌人各挂一条;if Frozen 只挂冻结中的。</summary>
        [Test]
        public void BuffBlock_SupportsPickAndOnlyIf()
        {
            var all = RebalanceFixture.Char("锁", new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.BuffBlock, 0, turns: 2, pick: EffectPick.All));
            var b = Battle(all, Mob(), Mob());
            Cast(b, "锁", 0);
            Assert.That(b.Enemies.All(e => e.Statuses.Has(StatusKind.BuffBlock)), Is.True);

            var onlyFrozen = RebalanceFixture.Char("锁", new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.BuffBlock, 0, turns: 2, pick: EffectPick.All, onlyIf: DamageCondition.Frozen));
            var c = Battle(onlyFrozen, Mob(), Mob());
            Cast(c, "冻", 1);
            Cast(c, "锁", 0);
            Assert.That(c.Enemies[0].Statuses.Has(StatusKind.BuffBlock), Is.False);
            Assert.That(c.Enemies[1].Statuses.Has(StatusKind.BuffBlock), Is.True);
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.BuffBlock, 0, turns: 2)), Is.True, "Primary 写法选主目标");
        }

        // ================= W4 DebuffWard =================

        private static readonly CharDef Bathe = RebalanceFixture.Char("濯",
            new EffectDef(EffectKind.Cleanse, 0), new EffectDef(EffectKind.DebuffWard, 0, turns: 1));

        /// <summary>濯身:全量拦截、TurnsLeft 1 —— 本轮敌方段的灼与封字都挡下(不发 StatusApplied、不发灼事件);
        /// 玩家回合开始到期,下一轮照常挂上。</summary>
        [Test]
        public void Bathe_FullWard_OneRound_BlocksBurnAndSeal()
        {
            var b = Battle(Bathe, Mob(EnemyAbility.Sear, attack: 10), Toppler());
            var log = new HookLog();
            b.AddHookListener(log);
            bool sawSeal = false, sawBurn = false;
            for (int round = 0; round < 3; round++)
            {
                Cast(b, "濯");
                Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard)?.TurnsLeft, Is.EqualTo(1));
                b.EndTurn();
                Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == -1), Is.False, "挡下的灼不发事件");
                sawSeal |= b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast);
                sawBurn |= b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyAttack && e.TargetIndex == 0);
                Assert.That(b.PlayerStatuses.Has(StatusKind.Burn), Is.False);
                Assert.That(b.PlayerStatuses.Has(StatusKind.Seal), Is.False);
                Assert.That(b.PlayerStatuses.Has(StatusKind.DebuffWard), Is.False, "玩家回合开始到期");
            }
            Assert.That(sawSeal && sawBurn, Is.True, "场景确实走到了倾覆与灯花");
            Assert.That(log.Applied.Any(a => a.Subject.Side == UnitSide.Player && (a.Status == StatusKind.Burn || a.Status == StatusKind.Seal)),
                Is.False, "拦下不发 StatusApplied");
            // 不再续:下一轮照常挂上
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == -1), Is.True, "免疫到期后灯花照常上灼");
        }

        /// <summary>浇熄:只拦灼,每拦 1 层给护盾 Value;刷新按 RefreshBurn 增量计层,增量为 0 不算拦、不给盾;封字照挂。</summary>
        [Test]
        public void Douse_OnlyBurn_ShieldPerIncrementLayer()
        {
            var douse = RebalanceFixture.Char("浇", new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn));
            var b = Battle(douse, Mob());   // 直调 RefreshBurn,不经敌方段
            Cast(b, "浇");
            var ward = b.PlayerStatuses.Find(StatusKind.DebuffWard);
            Assert.That(ward.WardOf, Is.EqualTo(StatusKind.Burn));
            Assert.That(ward.Magnitude, Is.EqualTo(50));
            Assert.That(ward.TurnsLeft, Is.EqualTo(3));

            int shield0 = b.PlayerShield;
            b.RefreshBurn(b.PlayerStatuses, 3, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Burn), Is.False, "拦下后不改层");
            Assert.That(b.PlayerShield - shield0, Is.EqualTo(150), "0 → 3 层,增量 3 × 50");

            // 已有 2 层(护栏外挂上的),刷新到 3 → 增量 1
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 2, TurnsLeft = -1 });
            int shield1 = b.PlayerShield;
            b.RefreshBurn(b.PlayerStatuses, 3, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Burn).Magnitude, Is.EqualTo(2), "拦下后不改层");
            Assert.That(b.PlayerShield - shield1, Is.EqualTo(50), "2 → 3,增量 1 层");

            // 刷新到不高于现有层数:增量 0,不算拦截、不给盾
            int shield2 = b.PlayerShield;
            b.RefreshBurn(b.PlayerStatuses, 1, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerShield, Is.EqualTo(shield2));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Burn).Magnitude, Is.EqualTo(2));
            Assert.That(b.PlayerStatuses.Has(StatusKind.DebuffWard), Is.True, "期间不限次,仍在");
        }

        /// <summary>浇熄只认灼:倾覆的封字照挂、不给盾。</summary>
        [Test]
        public void Douse_DoesNotBlockSeal()
        {
            var douse = RebalanceFixture.Char("浇", new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn));
            var b = Battle(douse, Toppler());
            bool sealed_ = false;
            for (int round = 0; round < 3 && !sealed_; round++)
            {
                Cast(b, "浇");
                b.EndTurn();
                sealed_ = b.PlayerStatuses.Has(StatusKind.Seal);
            }
            Assert.That(sealed_, Is.True, "封字不在浇熄的拦截范围");
        }

        /// <summary>WardCount = 前 N 次:用尽即移除,之后照常挂上。</summary>
        [Test]
        public void WardCount_ExhaustedThenRemoved()
        {
            var plug = RebalanceFixture.Char("杜", new EffectDef(EffectKind.DebuffWard, 0, turns: 5, wardCount: 2));
            var b = Battle(plug, Mob());
            Cast(b, "杜");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard).WardCount, Is.EqualTo(2));
            b.RefreshBurn(b.PlayerStatuses, 1, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard).WardCount, Is.EqualTo(1));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Burn), Is.False);
            b.RefreshBurn(b.PlayerStatuses, 1, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.DebuffWard), Is.False, "第 2 次用尽即移除");
            Assert.That(b.PlayerStatuses.Has(StatusKind.Burn), Is.False);
            b.RefreshBurn(b.PlayerStatuses, 1, UnitRef.Player, UnitRef.Enemy(0));
            Assert.That(b.PlayerStatuses.Find(StatusKind.Burn)?.Magnitude, Is.EqualTo(1), "用尽后照常挂上");
        }

        /// <summary>落木灵:挂在 AllyStatuses(槽) 上;灯花打木灵 → 木灵那条计数 / 护盾,玩家那条不动;按木灵那一拍递减。</summary>
        [Test]
        public void Ward_OnSummon_CountsAndShieldsTheSummon()
        {
            var douse = RebalanceFixture.Char("浇", new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn, wardCount: 1));
            var b = Battle(douse, Mob(EnemyAbility.Sear, attack: 10));
            Cast(b, "苗");
            Assert.That(b.Summons[0], Is.Not.Null);
            Cast(b, "浇", allySlot: 0);
            Cast(b, "浇");
            Assert.That(b.Summons[0].Statuses.Find(StatusKind.DebuffWard)?.WardCount, Is.EqualTo(1));
            Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard)?.WardCount, Is.EqualTo(1));

            int summonShield = b.Summons[0].Shield, playerShield = b.PlayerShield;
            b.RefreshBurn(b.Summons[0].Statuses, 2, UnitRef.Summon(0), UnitRef.Enemy(0));
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Burn), Is.False);
            Assert.That(b.Summons[0].Shield - summonShield, Is.EqualTo(100), "护盾给木灵(2 层 × 50)");
            Assert.That(b.PlayerShield, Is.EqualTo(playerShield), "玩家护盾不动");
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.DebuffWard), Is.False, "木灵那条计数用尽");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard)?.WardCount, Is.EqualTo(1), "玩家那条不动");

            // 回合数按木灵那一拍递减
            var c = Battle(douse, Mob());
            Cast(c, "苗");
            Cast(c, "浇", allySlot: 0);
            c.EndTurn();
            Assert.That(c.Summons[0].Statuses.Find(StatusKind.DebuffWard)?.TurnsLeft, Is.EqualTo(2));
        }

        /// <summary>灯花真打木灵:木灵带浇熄 → 不上灼、不发 SummonBurn,木灵拿盾。</summary>
        [Test]
        public void Ward_OnSummon_SearEndToEnd()
        {
            var douse = RebalanceFixture.Char("浇", new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn));
            var b = Battle(douse, Mob(EnemyAbility.Sear, attack: 10));
            Cast(b, "苗");
            Cast(b, "浇", allySlot: 0);
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "灯花打的是木灵");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonBurn), Is.False);
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Burn), Is.False);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == 0 && e.Amount == 50), Is.True);
        }

        [Test]
        public void StatusEffect_Clone_CopiesWardFields()
        {
            var s = new StatusEffect { Kind = StatusKind.DebuffWard, WardOf = StatusKind.Burn, WardCount = 3 };
            var c = s.Clone();
            Assert.That(c.WardOf, Is.EqualTo(StatusKind.Burn));
            Assert.That(c.WardCount, Is.EqualTo(3));
            Assert.That(StatusRules.IsBattleScoped(StatusKind.DebuffWard), Is.True);
        }

        /// <summary>存档往返(SaveSerializer 真实入口):WardOf / WardCount / Magnitude 不丢;WardOf = null(全量)也不丢。</summary>
        [Test]
        public void Ward_SurvivesRealSaveFile()
        {
            var douse = RebalanceFixture.Char("试",
                new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn, wardCount: 2));
            var graph = RebalanceFixture.Graph(douse);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "试" } };
            var levels = new Dictionary<string, int> { ["试"] = 1 };
            var cfg = new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };
            var run = new RunEngine(graph, runConfig, cfg, new[] { "试", "试" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            Assert.That(run.Battle.Cast("试"), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, cfg, levels,
                startingInk: 50, perFloorNormalShield: 2);
            var ward = restored.Battle.PlayerStatuses.Find(StatusKind.DebuffWard);
            Assert.That(ward, Is.Not.Null);
            Assert.That(ward.WardOf, Is.EqualTo(StatusKind.Burn), "WardOf 进存档 JSON 往返");
            Assert.That(ward.WardCount, Is.EqualTo(2), "WardCount 进存档 JSON 往返");
            Assert.That(ward.Magnitude, Is.EqualTo(50));

            var full = new StatusEffect { Kind = StatusKind.DebuffWard, Polarity = StatusPolarity.Buff, TurnsLeft = 1 };
            restored.Battle.PlayerStatuses.Remove(StatusKind.DebuffWard);
            restored.Battle.PlayerStatuses.Apply(full);
            var again = SaveSerializer.FromJson(SaveSerializer.ToJson(new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = restored.Capture() },
                },
            }));
            var back = RunEngine.Restore(again.EndlessV2.InProgress.Run, graph, runConfig, cfg, levels,
                startingInk: 50, perFloorNormalShield: 2);
            Assert.That(back.Battle.PlayerStatuses.Find(StatusKind.DebuffWard).WardOf, Is.Null, "全量(null)往返仍是 null");
        }

        // ================= 同步件 =================

        [Test]
        public void Classification_ScalingHostilityAllyTarget()
        {
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.BuffBlock), Is.False, "BuffBlock 离散");
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.DebuffWard), Is.True, "DebuffWard 护盾量连续");
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.BuffBlock), Is.True);
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.DebuffWard), Is.False);
            Assert.That(EffectPickRules.Supports(EffectKind.BuffBlock), Is.True);
            Assert.That(BattleEngine.EffectNeedsAllyTarget(new EffectDef(EffectKind.DebuffWard, 0, turns: 1)), Is.True, "落点 = 玩家或木灵");
        }

        /// <summary>卡等级:DebuffWard 的护盾量吃等级(连续)。</summary>
        [Test]
        public void DebuffWard_ShieldScalesWithCardLevel()
        {
            var douse = RebalanceFixture.Char("浇", new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn));
            var defs = new[] { douse };
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config, new[] { "浇", "浇" }, Array.Empty<string>(),
                new[] { Mob() }, seed: 1, cardLevels: new Dictionary<string, int> { ["浇"] = 6 });
            Cast(b, "浇");
            Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard).Magnitude, Is.EqualTo(MetaRules.ScaleByCardLevel(50, 6)));
        }

        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""淋"",""element"":""Water"",""effects"":[{""kind"":""HealSelf"",""value"":10}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10}],
              ""traits"":[{""slot"":""Lv6"",""face"":""Feature"",""form"":""Passive"",""name"":""浇"",""effects"":[" + traitEffects + @"]},
                          {""slot"":""Lv5"",""face"":""Attack"",""form"":""Active"",""name"":""洗"",""effects"":[{""kind"":""Dispel"",""value"":-1},{""kind"":""BuffBlock"",""value"":0,""turns"":2}]}]}]}");

        [Test]
        public void Loader_ReadsWardAndBlock()
        {
            var g = Load(@"{""kind"":""DebuffWard"",""value"":50,""turns"":3,""wardOf"":""Burn"",""wardCount"":2}");
            var ward = g.Get("淋").Traits[0].Effects[0];
            Assert.That(ward.Kind, Is.EqualTo(EffectKind.DebuffWard));
            Assert.That(ward.WardOf, Is.EqualTo(StatusKind.Burn));
            Assert.That(ward.WardCount, Is.EqualTo(2));
            Assert.That(ward.Turns, Is.EqualTo(3));
            Assert.That(g.Get("淋").Traits[1].Effects[1].Kind, Is.EqualTo(EffectKind.BuffBlock));
            var full = Load(@"{""kind"":""DebuffWard"",""value"":0,""turns"":1}").Get("淋").Traits[0].Effects[0];
            Assert.That(full.WardOf, Is.Null);
            Assert.That(full.WardCount, Is.EqualTo(0));
        }

        [TestCase(@"{""kind"":""DebuffWard"",""value"":50}")]                                         // 缺 turns
        [TestCase(@"{""kind"":""DebuffWard"",""value"":50,""turns"":3,""wardOf"":""Nope""}")]         // 未知状态
        [TestCase(@"{""kind"":""DebuffWard"",""value"":50,""turns"":3,""wardOf"":""AttackBuff""}")]   // 不是减益
        [TestCase(@"{""kind"":""DebuffWard"",""value"":50,""turns"":3,""wardCount"":-1}")]           // 负次数
        [TestCase(@"{""kind"":""Shield"",""value"":50,""wardOf"":""Burn""}")]                         // 宿主不对
        [TestCase(@"{""kind"":""Shield"",""value"":50,""wardCount"":1}")]                             // 宿主不对
        public void Loader_RejectsBadWard(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }

        [Test]
        public void Loader_RejectsBuffBlockWithoutTurns()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""澡"",""element"":""Water"",""effects"":[{""kind"":""HealSelf"",""value"":10}],
                  ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10},{""kind"":""BuffBlock"",""value"":0}]}]}"));
        }
    }
}
