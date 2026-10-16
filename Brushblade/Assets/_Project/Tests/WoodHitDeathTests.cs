using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 3:受击 / 阵亡 / 守护 / 标记。
    /// M3 阵亡挂点(秋实 DeathPart / 落木 DeathBlast / 藻荇 SproutDeathSlow)、M4 受击挂点(披荆 ThornsBleed / 良弓 ReturnFire)、
    /// M5 守护 Guard、M6 荆刺 ThornMark;Review Focus 2 的组合用例(减伤与反伤两份 60% 预算、守护转移的位置与守恒)。
    ///
    /// 夹具:Element.Heart 敌人(对木 1.0×)、PlayerAttack = 100、玩家暴击率 0、卡等级 1、无掉字。木灵用起手携带直接摆位。</summary>
    public class WoodHitDeathTests
    {
        private static BattleConfig Config(int poolCapacity = 10, int slots = BattleEngine.AllSummonSlotsMask) => new()
        {
            PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, PoolCapacity = poolCapacity,
            UnlockedSummonSlots = slots, BossPhaseJitterPercent = 0,
        };

        private static SummonSnapshot W(int slot, int hp = 100000, int attack = 0, SummonPassive passive = null,
            string source = "林", string ch = "木", int sproutParent = -1, int shield = 0, StatusEffect[] statuses = null) => new()
        {
            Slot = slot, Char = ch, SourceChar = source, Element = Element.Wood, Hp = hp, MaxHp = Math.Max(hp, 100000),
            Attack = attack, Speed = 100, Passive = passive, SproutParentSlot = sproutParent, Shield = shield,
            Statuses = (statuses ?? Array.Empty<StatusEffect>()).ToList(),
        };

        private static BattleEngine Battle(CharDef[] defs, IReadOnlyList<SummonSnapshot> summons, EnemyDef[] enemies,
            BattleConfig config = null, IReadOnlyList<string> pool = null, IReadOnlyDictionary<string, int> levels = null) =>
            new(RebalanceFixture.Graph(defs), config ?? Config(), defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(),
                pool ?? Array.Empty<string>(), enemies, seed: 7, startingHp: 3000, cardLevels: levels, startingSummons: summons);

        private static CharDef Card(string id, params EffectDef[] effects) => RebalanceFixture.Char(id, effects);

        private static StatusEffect St(StatusKind kind, int magnitude, int turns = -1, int counter = 0) => new()
        {
            Kind = kind, Polarity = StatusPolarity.Buff, Magnitude = magnitude, TurnsLeft = turns, CounterDamage = counter,
        };

        private static EnemyDef Melee(int attack, Element element = Element.Heart, int defense = 0) =>
            new("怔", element, 100000, attack, defense: defense);

        /// <summary>只打玩家的远程(Focus.Player):木灵不挨打。</summary>
        private static EnemyDef Shooter(int attack, Element element = Element.Heart) =>
            new("盯", element, 100000, attack, range: AttackRange.Ranged, focus: AttackFocus.Player);

        private static EnemyDef Dummy(Element element = Element.Heart, int defense = 0) =>
            new("靶", element, 100000, 0, defense: defense);

        private static int Dealt(BattleEngine b, EffectSource source, int enemy = -1) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == source && (enemy < 0 || e.TargetIndex == enemy))
            .Sum(e => e.Amount);

        private static int Count(BattleEngine b, EffectSource source) =>
            b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == source);

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<HookArgs> All = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => All.Add(args);
        }

        // ================= M6 荆刺 ThornMark =================

        private static CharDef Mark => Card("刺", new EffectDef(EffectKind.ThornMark, 30, turns: 2));

        [Test]
        public void M6_ThornMark_HangsOnEnemy_SameSourceRefreshes_ExpiresAfterTwoEnemyActions()
        {
            var b = Battle(new[] { Mark }, null, new[] { Shooter(10) });
            Assert.That(b.Cast("刺", 0), Is.EqualTo(BattleError.None));
            var mark = b.Enemies[0].Statuses.All.Single(s => s.Kind == StatusKind.ThornMark);
            Assert.That((mark.Magnitude, mark.TurnsLeft, mark.Polarity), Is.EqualTo((30, 2, StatusPolarity.Debuff)));
            b.Cast("刺", 0);
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.ThornMark), Is.EqualTo(1), "同源刷新");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.ThornMark)?.TurnsLeft, Is.EqualTo(1), "该敌人行动一次 → 剩 1");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.ThornMark), Is.False, "行动两次后移除");
        }

        [TestCase(20, 500)]   // 镜 20 + 荆刺 30 = 50%
        [TestCase(50, 600)]   // 镜 50 + 荆刺 30 = 80% → 钳 60%
        [TestCase(0, 300)]
        public void M6_ThornMark_JoinsPlayerReflect_Clamped60(int mirror, int expected)
        {
            var b = Battle(new[] { Mark }, null, new[] { Shooter(1000) });
            if (mirror > 0) b.PlayerStatuses.Apply(St(StatusKind.Reflect, mirror, 5));
            b.Cast("刺", 0);
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Reflect), Is.EqualTo(expected), "1000 × min(60, 镜 + 荆刺)%");
        }

        [Test]
        public void M6_ThornMark_JoinsSummonReflect_SharesBudgetWithThorns()
        {
            // 荆棘 40 先扣,反弹(玩家镜 0 + 木灵自身 0 + 荆刺 30)只能拿剩下的 20
            var b = Battle(new[] { Mark }, new[] { W(0, passive: new SummonPassive { Thorns = 40, Taunt = true }) },
                new[] { Melee(1000) });
            b.Cast("刺", 0);
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Thorns), Is.EqualTo(400));
            Assert.That(Dealt(b, EffectSource.Reflect), Is.EqualTo(200), "荆刺并入召唤侧 Reflect,钳在 60 − 荆棘");
        }

        [Test]
        public void M6_ThornMark_OnlyTheMarkedAttackerBounces()
        {
            var b = Battle(new[] { Mark }, null, new[] { Shooter(1000), Shooter(1000) });
            b.Cast("刺", 0);
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Reflect, 0), Is.EqualTo(300));
            Assert.That(Dealt(b, EffectSource.Reflect, 1), Is.EqualTo(0), "标记挂在攻击者身上,不是玩家身上");
        }

        [Test]
        public void M6_ThornMark_RulesAndLoader()
        {
            Assert.That(EffectPickRules.Supports(EffectKind.ThornMark), Is.True);
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.ThornMark, 30, turns: 2)), Is.True, "主目标要选敌");
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.ThornMark, 30, turns: 2, pick: EffectPick.All)),
                Is.False);
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.ThornMark), Is.True);
            var e = Load(@"{""kind"":""ThornMark"",""value"":30,""turns"":2,""pick"":""All""}").Get("林").Effects[0];
            Assert.That((e.Kind, e.Value, e.Turns, e.Pick), Is.EqualTo((EffectKind.ThornMark, 30, 2, EffectPick.All)));
        }

        [TestCase(@"{""kind"":""ThornMark"",""value"":0,""turns"":2}")]
        [TestCase(@"{""kind"":""ThornMark"",""value"":30}")]
        [TestCase(@"{""kind"":""ThornMark"",""value"":30,""turns"":0}")]
        [TestCase(@"{""kind"":""ThornMark"",""value"":30,""turns"":2,""pick"":""AllSummons""}")]
        public void M6_Loader_RejectsBadThornMark(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }

        private static RecipeGraph Load(string effects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""林"",""element"":""Wood"",""effects"":[" + effects + @"],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10}]}]}");

        // ================= M4 披荆 ThornsBleed =================

        [Test]
        public void M4_ThornsBleed_BleedsAttackerWhenOwnThornsBounce()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { Thorns = 40, ThornsBleed = 35, Taunt = true }) }, new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Thorns), Is.EqualTo(400), "夹具前提:荆棘弹了");
            var bleed = b.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed?.Magnitude, Is.EqualTo(35), "攻击者流血");
        }

        [Test]
        public void M4_ThornsBleed_NotFromAuraThorns()
        {
            // Q12:荆棘光环(玩家袋 SummonThorns)弹出去的不触发披荆 —— 只认荆灵自己的本命荆棘
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { ThornsBleed = 35, Taunt = true }) }, new[] { Melee(1000) });
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.SummonThorns, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = 5, TargetSlot = -1 });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Thorns), Is.EqualTo(400), "夹具前提:光环荆棘弹了");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.False);
        }

        [Test]
        public void M4_ThornsBleed_NoBleedWhenBounceRoundsToZero()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { Thorns = 40, ThornsBleed = 35, Taunt = true }) }, new[] { Melee(2) });
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.False, "bounced = 0 不挂");
        }

        [Test]
        public void M4_ThornsBleed_ScalesWithCardLevelAtSummonTime()
        {
            var thorn = new CharDef("荆", Element.Wood, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 300, summonAttack: 10, summonChar: "木",
                    passive: new SummonPassive { Thorns = 40, ThornsBleed = 35 }),
            });
            var b = Battle(new[] { thorn }, null, new[] { Dummy() }, levels: new Dictionary<string, int> { ["荆"] = 5 });
            b.Cast("荆");
            Assert.That(b.Summons[0].Passive.ThornsBleed, Is.EqualTo(MetaRules.ScaleByCardLevel(35, 5)));
            Assert.That(b.Summons[0].Passive.ThornsBleed, Is.GreaterThan(35));
        }

        // ================= M4 良弓 ReturnFire =================

        [Test]
        public void M4_ReturnFire_DefenseTimesPercent_HeartIgnoresArmor()
        {
            // 柘灵护甲 100:1000 → 500 落身;预算 300;良弓 = 100 × 200% = 200,心属性、无视敌人 100 护甲 / 金克木不影响
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { ReturnFire = 200, Taunt = true }, statuses: new[] { St(StatusKind.DefenseBuff, 100) }) },
                new[] { Melee(1000, Element.Heart, defense: 100) });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.ReturnFire), Is.EqualTo(200));
            Assert.That(Count(b, EffectSource.ReturnFire), Is.EqualTo(1), "每次受击一箭");
        }

        [Test]
        public void M4_ReturnFire_LastInBudget_ThornsReflectCounterThenBow()
        {
            // 1000 → 护甲 100 → 500 → 格挡 40% → 300;预算按格挡前 500 × 60% = 300(2026-10-16 起):荆棘 20% = 100 → 镜 10% = 50 → 格挡反击 30 → 良弓拿剩下 120(而不是 200)
            var b = Battle(Array.Empty<CharDef>(),
                new[]
                {
                    W(0, passive: new SummonPassive { Thorns = 20, ReturnFire = 200, Taunt = true },
                        statuses: new[] { St(StatusKind.DefenseBuff, 100), St(StatusKind.Block, 1, counter: 30) }),
                }, new[] { Melee(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.Reflect, 10, 5));
            b.EndTurn();
            Assert.That((Dealt(b, EffectSource.Thorns), Dealt(b, EffectSource.Reflect), Dealt(b, EffectSource.BlockCounter),
                Dealt(b, EffectSource.ReturnFire)), Is.EqualTo((100, 50, 30, 120)));
            int ret = b.LastEvents.ToList().FindIndex(e => e.Source == EffectSource.ReturnFire);
            int counter = b.LastEvents.ToList().FindIndex(e => e.Source == EffectSource.BlockCounter);
            Assert.That(ret, Is.GreaterThan(counter), "良弓排在格挡反击之后");
        }

        [Test]
        public void M4_ReturnFire_NoBudgetLeft_NoShot()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { Thorns = 60, ReturnFire = 200, Taunt = true }, statuses: new[] { St(StatusKind.DefenseBuff, 100) }) },
                new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Thorns), Is.EqualTo(300));
            Assert.That(Count(b, EffectSource.ReturnFire), Is.EqualTo(0), "预算被荆棘吃满");
        }

        [Test]
        public void M4_ReturnFire_NotOnMiss()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { ReturnFire = 200, Taunt = true, Dodge = 100 }, statuses: new[] { St(StatusKind.DefenseBuff, 100) }) },
                new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Missed), Is.True, "夹具前提:打空");
            Assert.That(Count(b, EffectSource.ReturnFire), Is.EqualTo(0));
        }

        [Test]
        public void M4_ReturnFire_NotOnDevour()
        {
            var boss = new EnemyDef("噬", Element.Heart, 100000, 50,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 100000, 50, skill: BossSkill.Devour) });
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, passive: new SummonPassive { ReturnFire = 200, Taunt = true }, statuses: new[] { St(StatusKind.DefenseBuff, 100) }) },
                new[] { boss });
            bool devoured = false;
            for (int t = 0; t < 4 && !devoured; t++)
            {
                b.EndTurn();
                devoured = b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast && e.Amount == (int)BossSkill.Devour);
                if (devoured) Assert.That(Count(b, EffectSource.ReturnFire), Is.EqualTo(0), "吞噬不触发良弓");
            }
            Assert.That(devoured, Is.True, "夹具前提:放了吞噬");
            Assert.That(b.Summons[0].Alive, Is.False);
        }

        // ================= M5 守护 Guard =================

        private static readonly SummonPassive GuardP = new() { Guard = 30 };

        [Test]
        public void M5_Guard_TransfersAfterCuts_BeforePlayerShield()
        {
            // 1000 → 减伤 20% → 800;转移 240 给柘灵;玩家 560:护盾 100 先吸,血 −460
            var shield = Card("盾", new EffectDef(EffectKind.Shield, 100));
            var b = Battle(new[] { shield }, new[] { W(1, hp: 1000, passive: GuardP) }, new[] { Shooter(1000, Element.Metal) });
            b.PlayerStatuses.Apply(St(StatusKind.DamageCut, 20, 5));
            b.Cast("盾");
            Assert.That(b.ShieldNormal, Is.EqualTo(100), "夹具前提");
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Summons[1].Hp, Is.EqualTo(760), "转移不过柘灵护甲 / 生克(金克木不 ×1.5)");
            Assert.That(hp - b.PlayerHp, Is.EqualTo(460));
            var attack = b.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That((attack.Amount, attack.Absorbed), Is.EqualTo((560, 100)), "EnemyAttack 报玩家实际承受的量");
            var guard = b.LastEvents.Single(e => e.Kind == BattleEventKind.GuardTransfer);
            Assert.That((guard.TargetIndex, guard.Amount, guard.SecondIndex, guard.Absorbed), Is.EqualTo((0, 240, 1, 0)));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.False, "不算挥击");
        }

        [Test]
        public void M5_Guard_SummonShieldFirst_NoArmor_NoSwingReactions()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[]
                {
                    W(1, hp: 1000, shield: 100,
                        passive: new SummonPassive { Guard = 30, Thorns = 50, ReturnFire = 200 },
                        statuses: new[] { St(StatusKind.DefenseBuff, 100) }),
                },
                new[] { Shooter(1000) });
            b.EndTurn();
            Assert.That((b.Summons[1].Shield, b.Summons[1].Hp), Is.EqualTo((0, 800)), "300:盾 100 → 血 200;不吃护甲 100");
            Assert.That(Count(b, EffectSource.Thorns) + Count(b, EffectSource.ReturnFire), Is.EqualTo(0),
                "转移不算挥击:不触发它的荆棘 / 良弓");
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.GuardTransfer).Absorbed, Is.EqualTo(100));
        }

        [Test]
        public void M5_Guard_MirrorUsesPreTransferDamage()
        {
            var b = Battle(Array.Empty<CharDef>(), new[] { W(1, hp: 1000, passive: GuardP) }, new[] { Shooter(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.Reflect, 10, 5));
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Reflect), Is.EqualTo(100), "镜按转移前的 1000 反");
        }

        [Test]
        public void M5_Guard_ImmunityBlocksFirst_NoTransfer()
        {
            var b = Battle(Array.Empty<CharDef>(), new[] { W(1, hp: 1000, passive: GuardP) }, new[] { Shooter(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.Immunity, 1));
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ImmunityBlocked), Is.True);
            Assert.That(b.Summons[1].Hp, Is.EqualTo(1000));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.GuardTransfer), Is.False);
        }

        [Test]
        public void M5_Guard_LowestSlotGuardTakesIt()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(3, hp: 1000, passive: new SummonPassive { Guard = 50 }), W(1, hp: 1000, passive: GuardP), W(0, hp: 1000) },
                new[] { Shooter(1000) });
            b.EndTurn();
            Assert.That((b.Summons[0].Hp, b.Summons[1].Hp, b.Summons[3].Hp), Is.EqualTo((1000, 700, 1000)),
                "取槽位最小的带守护木灵,按它的 30%");
        }

        [Test]
        public void M5_Guard_LethalTransfer_TriggersDeathTraits()
        {
            var log = new HookLog();
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(1, hp: 100, passive: new SummonPassive { Guard = 30, DeathPart = true }) }, new[] { Shooter(1000) });
            b.AddHookListener(log);
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Summons[1].Alive, Is.False, "转移可致死");
            Assert.That(hp - b.PlayerHp, Is.EqualTo(700), "玩家只承受转移后的部分,致死不回吐");
            Assert.That(b.Pool, Is.EqualTo(new[] { "木" }), "守护转移致死触发秋实(Q15)");
            var died = log.All.Single(h => h.Kind == HookKind.SummonDied);
            Assert.That((died.Subject.Index, died.Other.Side, died.Other.Index), Is.EqualTo((1, UnitSide.Enemy, 0)));
        }

        [Test]
        public void M5_Guard_NotOnPlayerWithoutGuardSummon_Identical()
        {
            var b = Battle(Array.Empty<CharDef>(), new[] { W(1, hp: 1000) }, new[] { Shooter(1000) });
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(hp - b.PlayerHp, Is.EqualTo(1000));
            Assert.That(b.Summons[1].Hp, Is.EqualTo(1000));
        }

        // ---- Task 3 review Minor 1 / 2:守护与反震 / 碎玉同场,守护三条边界 ----

        private static StatusEffect Recoil(int percent) => new()
            { Kind = StatusKind.ShieldRecoil, Polarity = StatusPolarity.Buff, Magnitude = percent, TurnsLeft = -1, SourceId = "护" };

        private static StatusEffect Shatter(int percent) => new()
            { Kind = StatusKind.ShieldShatter, Polarity = StatusPolarity.Buff, Magnitude = percent, TurnsLeft = -1, SourceId = "玉" };

        [Test]
        public void M5_Guard_WithShieldRecoil_AmountFromPostTransferAbsorbed()
        {
            // 1000 → 守护转 300,玩家 700 全被盾 1000 吸。反震 50% 按吸收量:700 × 50% = 350(不是转移前的 500)
            var shield = Card("盾", new EffectDef(EffectKind.Shield, 1000));
            var b = Battle(new[] { shield }, new[] { W(1, hp: 5000, passive: GuardP) }, new[] { Shooter(1000) });
            b.Cast("盾");
            b.PlayerStatuses.Apply(Recoil(50));
            b.EndTurn();
            Assert.That(5000 - b.Summons[1].Hp, Is.EqualTo(300), "夹具前提:守护转移 300");
            Assert.That(Dealt(b, EffectSource.ShieldRecoil), Is.EqualTo(350));
        }

        [Test]
        public void M5_Guard_WithShieldRecoilAndShatter_BudgetFromPreTransferDamage()
        {
            // 1000 → 守护转 300,玩家 700 恰好打空盾 700。反震 50% = 350;碎玉 100% = 700 钳在剩余预算:
            // 预算以转移前 1000 × 60% = 600 为基数 → 碎玉 250(若按转移后 700 × 60% = 420 则只剩 70)
            var shield = Card("盾", new EffectDef(EffectKind.Shield, 700));
            var b = Battle(new[] { shield }, new[] { W(1, hp: 5000, passive: GuardP) }, new[] { Shooter(1000) });
            b.Cast("盾");
            b.PlayerStatuses.Apply(Recoil(50));
            b.PlayerStatuses.Apply(Shatter(100));
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That((hp - b.PlayerHp, b.ShieldNormal), Is.EqualTo((0, 0)), "夹具前提:盾恰好吃满");
            Assert.That((Dealt(b, EffectSource.ShieldRecoil), Dealt(b, EffectSource.ShieldShatter)), Is.EqualTo((350, 250)));
        }

        [Test]
        public void M5_Guard_NotOnIronBarbBacklash()
        {
            // 铁画反噬(allowReflect = false)不是敌人挥击:不转移,玩家全额承受
            var hit = Card("击", new EffectDef(EffectKind.DamageSingle, 1000));
            var barb = new EnemyDef("铁", Element.Heart, 100000, 0, ability: EnemyAbility.Barb);
            var b = Battle(new[] { hit }, new[] { W(1, hp: 5000, passive: GuardP) }, new[] { barb });
            int hp = b.PlayerHp;
            Assert.That(b.Cast("击", 0), Is.EqualTo(BattleError.None));
            Assert.That(hp - b.PlayerHp, Is.GreaterThan(0), "夹具前提:反噬确实发生");
            Assert.That(b.Summons[1].Hp, Is.EqualTo(5000), "反噬不转移");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.GuardTransfer), Is.False);
        }

        [Test]
        public void M5_Guard_BossSkillPlayerShare_IsTransferred()
        {
            // 倾覆:玩家份 = 攻击 × 2 = 1000,守护转 300,玩家 700
            var boss = new EnemyDef("覆", Element.Heart, 100000, 500,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 100000, 500, skill: BossSkill.Topple) });
            var cfg = Config();
            cfg.BossChargeEvery = 1;
            var b = Battle(Array.Empty<CharDef>(), new[] { W(1, hp: 50000, passive: GuardP) }, new[] { boss }, cfg);
            for (int t = 0; t < 6; t++)
            {
                int hp = b.PlayerHp, wood = b.Summons[1].Hp;
                b.EndTurn();
                if (!b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast)) continue;
                Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.GuardTransfer).Amount, Is.EqualTo(300));
                Assert.That((hp - b.PlayerHp, wood - b.Summons[1].Hp), Is.EqualTo((700, 300)), "大招玩家份照常转移");
                return;
            }
            Assert.Fail("夹具前提:Boss 没放出倾覆");
        }

        [Test]
        public void M5_Guard_PlayerAndGuardDieSameHit_SkipsDeathFlow()
        {
            // 玩家 3000:5000 → 守护转 1500 打死柘灵(100 血),玩家 3500 阵亡 → 战斗已分胜负,不走阵亡流程(秋实不入池)
            var log = new HookLog();
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(1, hp: 100, passive: new SummonPassive { Guard = 30, DeathPart = true }) }, new[] { Shooter(5000) });
            b.AddHookListener(log);
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Lost), "夹具前提:玩家阵亡");
            Assert.That(b.Summons[1].Hp, Is.EqualTo(0), "夹具前提:柘灵被转移打死");
            Assert.That(b.Pool, Is.Empty, "不走阵亡流程:秋实不入池");
            Assert.That(log.All.Any(h => h.Kind == HookKind.SummonDied), Is.False);
        }

        // ================= Review Focus 2 组合 =================

        [Test]
        public void RF2_GroveCutThenHitCap_ThenGuard_Conserved()
        {
            // 林荫 20 → 1000 × 80% = 800;铜墙铁壁 10% × 6000 = 600(下限 1000 × 40% = 400)→ 600;守护 30% = 180;玩家 420
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 5000, passive: new SummonPassive { GroveCut = 20, GroveCutCap = 20, Guard = 30 }) },
                new[] { Shooter(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.HitCap, 10, 5));
            int hp = b.PlayerHp;
            b.EndTurn();
            int playerLoss = hp - b.PlayerHp, summonLoss = 5000 - b.Summons[0].Hp;
            Assert.That((playerLoss, summonLoss), Is.EqualTo((420, 180)));
            Assert.That(playerLoss + summonLoss, Is.EqualTo(600), "转移不重复计入、不为负:两边之和 = 减伤 / HitCap 之后的伤害");
        }

        [Test]
        public void RF2_GroveCutWithHitCap_NoGuard()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 5000, passive: new SummonPassive { GroveCut = 20, GroveCutCap = 20 }) }, new[] { Shooter(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.HitCap, 10, 5));
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(hp - b.PlayerHp, Is.EqualTo(600), "先林荫 → 800,再 HitCap → 600");
        }

        [Test]
        public void RF2_SummonBlockPlusThicket_ClampsAt60()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 5000, passive: new SummonPassive { ThicketCut = 25, ThicketMin = 1, Taunt = true },
                    statuses: new[] { St(StatusKind.Block, 1) }) },
                new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(5000 - b.Summons[0].Hp, Is.EqualTo(400), "格挡 40 + 森严 25 = 65 → 钳 60");
        }

        [Test]
        public void RF2_SummonSide_CutAndReflectBudgetsBothCapped()
        {
            // 1000 → 护甲 100 → 500 → 格挡 40 + 森严 25 → 钳 60 → 200。反伤预算按格挡 / 森严前的 500 × 60% = 300(2026-10-16 起):
            // 荆棘 40% = 200 → 反弹(镜 20 + 荆刺 30 → 钳到 60 − 40 = 20)= 100 → 格挡反击 0 → 良弓 0
            var b = Battle(new[] { Mark },
                new[]
                {
                    W(0, hp: 5000, passive: new SummonPassive { Thorns = 40, ReturnFire = 200, ThicketCut = 25, ThicketMin = 1, Taunt = true },
                        statuses: new[] { St(StatusKind.DefenseBuff, 100), St(StatusKind.Block, 1, counter: 100) }),
                }, new[] { Melee(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.Reflect, 20, 5));
            b.Cast("刺", 0);
            b.EndTurn();
            Assert.That(5000 - b.Summons[0].Hp, Is.EqualTo(200));
            int total = Dealt(b, EffectSource.Thorns) + Dealt(b, EffectSource.Reflect) + Dealt(b, EffectSource.BlockCounter)
                        + Dealt(b, EffectSource.ReturnFire);
            Assert.That((Dealt(b, EffectSource.Thorns), Dealt(b, EffectSource.Reflect)), Is.EqualTo((200, 100)));
            Assert.That(total, Is.EqualTo(300), "反伤合计 = 减伤前 500 × 60%");
        }

        [Test]
        public void RF2_PlayerSide_CutAndReflectBudgetsBothCapped_GuardAfterCut()
        {
            // 1000 → 格挡 40 + 减伤 30 → 钳 60 → 400;守护转移 120,玩家 280。
            // 反伤基数 = 减伤前 1000(2026-10-16 起,原为转移前 400):镜 50 + 荆刺 30 → 钳 60% = 600;格挡反击拿剩下的 0
            var b = Battle(new[] { Mark }, new[] { W(1, hp: 5000, passive: GuardP) }, new[] { Shooter(1000) });
            b.PlayerStatuses.Apply(St(StatusKind.Reflect, 50, 5));
            b.PlayerStatuses.Apply(St(StatusKind.DamageCut, 30, 5));
            b.PlayerStatuses.Apply(St(StatusKind.Block, 1, counter: 500));
            b.Cast("刺", 0);
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That((hp - b.PlayerHp, 5000 - b.Summons[1].Hp), Is.EqualTo((280, 120)));
            Assert.That(Dealt(b, EffectSource.Reflect), Is.EqualTo(600));
            Assert.That(Dealt(b, EffectSource.Reflect) + Dealt(b, EffectSource.BlockCounter), Is.EqualTo(600), "合计 ≤ 减伤前 1000 × 60%");
        }

        // ================= M3 秋实 DeathPart =================

        [Test]
        public void M3_DeathPart_GrantsWoodToPool()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 100, passive: new SummonPassive { DeathPart = true, Taunt = true }) }, new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False);
            Assert.That(b.Pool, Is.EqualTo(new[] { "木" }));
            var e = b.LastEvents.Single(x => x.Kind == BattleEventKind.PartGranted);
            Assert.That((e.TargetIndex, e.Amount), Is.EqualTo((0, 1)));
        }

        [Test]
        public void M3_DeathPart_PoolFull_NotGranted()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 100, passive: new SummonPassive { DeathPart = true, Taunt = true }) }, new[] { Melee(1000) },
                Config(poolCapacity: 2), pool: new[] { "火", "金" });
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False);
            Assert.That(b.Pool, Is.EqualTo(new[] { "火", "金" }), "满池不入、不替换");
            Assert.That(b.LastEvents.Any(x => x.Kind == BattleEventKind.PartGranted), Is.False);
        }

        [Test]
        public void M3_BurnDeath_AlsoCounts()
        {
            var b = Battle(Array.Empty<CharDef>(),
                new[]
                {
                    W(0, hp: 100, passive: new SummonPassive { DeathPart = true },
                        statuses: new[] { new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 100, TurnsLeft = -1 } }),
                }, new[] { Dummy() });
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False, "夹具前提:烧死");
            Assert.That(b.Pool, Is.EqualTo(new[] { "木" }));
        }

        [Test]
        public void M3_Replacement_IsNotDeath()
        {
            // 只开 1 格,顶替掉带秋实 + 落木的木灵:不入池、不爆(Q15)
            var plain = new CharDef("木", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 300, summonAttack: 10, summonChar: "木") });
            var b = Battle(new[] { plain },
                new[] { W(0, hp: 1000, attack: 100, passive: new SummonPassive { DeathPart = true, DeathBlast = 300 }) },
                new[] { Dummy() }, Config(slots: 0b1));
            Assert.That(b.Cast("木", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Passive?.DeathPart ?? false, Is.False, "夹具前提:已被顶替");
            Assert.That(b.Pool, Is.Empty);
            Assert.That(Count(b, EffectSource.DeathBlast), Is.EqualTo(0));
            b.EndTurn();
            Assert.That(Count(b, EffectSource.DeathBlast), Is.EqualTo(0));
        }

        // ================= M3 落木 DeathBlast =================

        [Test]
        public void M3_DeathBlast_AllEnemies_AttackTimes300_WoodElement_Armor_NoCrit_AsReaction()
        {
            // 有效攻击 100 × 300% = 300,木属性:心 300、土(木克土 ×1.5)450、护甲 100 的心 150;玩家暴击 100% 也不暴
            var log = new HookLog();
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 100, attack: 100, passive: new SummonPassive { DeathBlast = 300, Taunt = true }) },
                new[] { Melee(1000), Dummy(Element.Earth), Dummy(Element.Heart, defense: 100) });
            b.PlayerStatuses.Apply(St(StatusKind.CritBuff, 100));
            b.AddHookListener(log);
            b.EndTurn();
            Assert.That(b.Summons[0].Alive, Is.False);
            Assert.That((Dealt(b, EffectSource.DeathBlast, 0), Dealt(b, EffectSource.DeathBlast, 1), Dealt(b, EffectSource.DeathBlast, 2)),
                Is.EqualTo((300, 450, 150)));
            Assert.That(b.LastEvents.Where(e => e.Source == EffectSource.DeathBlast).Any(e => e.Crit), Is.False, "不暴击");
            var hits = log.All.Where(h => h.Kind == HookKind.EnemyHit && h.Source == EffectSource.DeathBlast).ToList();
            Assert.That(hits.Count, Is.EqualTo(3));
            Assert.That(hits.All(h => h.Depth >= 1), Is.True, "作为反应兑现(TriggerDepth ≥ 1,R4)");
            var died = b.LastEvents.ToList().FindIndex(e => e.Kind == BattleEventKind.SummonHit);
            var blast = b.LastEvents.ToList().FindIndex(e => e.Source == EffectSource.DeathBlast);
            Assert.That(blast, Is.GreaterThan(died), "阵亡之后才兑现");
        }

        [Test]
        public void M3_DeathBlast_UsesEffectiveAttackAtDeath()
        {
            // 光环 AuraAttack 50(含自己)→ 有效攻击 150 → 450
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 100, attack: 100, passive: new SummonPassive { DeathBlast = 300, Taunt = true, AuraAttack = 50 }) },
                new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.DeathBlast), Is.EqualTo(450));
        }

        [Test]
        public void M3_DeathBlast_OnDevour()
        {
            var boss = new EnemyDef("噬", Element.Heart, 100000, 0,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 100000, 0, skill: BossSkill.Devour) });
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, attack: 100, passive: new SummonPassive { DeathBlast = 300 }) }, new[] { boss });
            int blast = 0;
            for (int t = 0; t < 4 && b.Summons[0].Alive; t++)
            {
                b.EndTurn();
                blast += Dealt(b, EffectSource.DeathBlast);
            }
            Assert.That(b.Summons[0].Alive, Is.False, "夹具前提:被吞噬");
            Assert.That(blast, Is.EqualTo(300), "吞噬致死同样触发落木");
        }

        [Test]
        public void M3_DeathBlast_KillDoesNotChainOnKillTraits()
        {
            // 落木打死的敌人身上挂着焚城(击杀时入队 BurnBurst 的载体):落木是反应(Depth ≥ 1),R4 —— 不连锁焚城。
            // 若连锁,其余敌人会再吃一发「死者剩余灼」的结算;这里只吃落木的 300
            var b = Battle(Array.Empty<CharDef>(),
                new[] { W(0, hp: 100, attack: 100, passive: new SummonPassive { DeathBlast = 300, Taunt = true }) },
                new[] { Melee(1000), new EnemyDef("靶", Element.Heart, 300, 0), Dummy() });
            var victim = b.Enemies[1].Statuses;
            victim.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1, Potency = 100 });
            victim.Apply(new StatusEffect
                { Kind = StatusKind.BurnBurstMark, Polarity = StatusPolarity.Debuff, TurnsLeft = -1, SourceId = "燃" });
            b.EndTurn();
            Assert.That((b.Summons[0].Alive, b.Enemies[1].Alive), Is.EqualTo((false, false)), "夹具前提:木灵阵亡、落木打死靶");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyDied && e.TargetIndex == 1), Is.True);
            Assert.That(100000 - b.Enemies[2].Hp, Is.EqualTo(300), "只吃落木,不连锁焚城");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BurnTick && e.TargetIndex == 2), Is.False);
        }

        // ================= M3 藻荇 SproutDeathSlow =================

        private static SummonSnapshot[] AlgaeWithSprout(int sproutHp = 10, params StatusEffect[] sproutStatuses) => new[]
        {
            W(0, passive: new SummonPassive { SproutDeathSlow = 1 }, source: "藻", ch: "藻"),
            W(1, hp: sproutHp, passive: new SummonPassive { Taunt = true }, source: "藻", ch: "苗", sproutParent: 0, statuses: sproutStatuses),
        };

        [Test]
        public void M3_SproutDeathSlow_SlowsEnemyKiller_FullTurns()
        {
            var b = Battle(Array.Empty<CharDef>(), AlgaeWithSprout(), new[] { Melee(1000), Dummy() });
            b.EndTurn();
            Assert.That(b.Summons[1].Alive, Is.False, "夹具前提:小藻被打死");
            var slow = b.Enemies[0].Statuses.All.SingleOrDefault(s => s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0);
            Assert.That(slow, Is.Not.Null, "killer 被减速");
            Assert.That(slow.TurnsLeft, Is.EqualTo(1), "拍尾兑现:不被 killer 自己那一拍的递减吃掉");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.SpeedModifier), Is.False, "只给 killer");
        }

        [Test]
        public void M3_SproutDeathSlow_NoEnemyKiller_NoSlow()
        {
            var burn = new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 100, TurnsLeft = -1 };
            var b = Battle(Array.Empty<CharDef>(), AlgaeWithSprout(sproutStatuses: burn), new[] { Dummy() });
            b.EndTurn();
            Assert.That(b.Summons[1].Alive, Is.False, "夹具前提:小藻烧死");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SpeedModifier), Is.False, "killer 不是敌人 → 不触发");
        }

        [Test]
        public void M3_SproutDeathSlow_OnlyForSproutsOfThatParent()
        {
            // 普通幼苗(没有母体记号)阵亡不触发
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(0, passive: new SummonPassive { SproutDeathSlow = 1 }, source: "藻", ch: "藻"),
                W(1, hp: 10, passive: new SummonPassive { Taunt = true }, source: "藻", ch: "苗"),
            }, new[] { Melee(1000) });
            b.EndTurn();
            Assert.That(b.Summons[1].Alive, Is.False);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SpeedModifier), Is.False);
        }

        [Test]
        public void M3_SproutDeathSlow_ParentDeadStillCounts()
        {
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(0, hp: 0, passive: new SummonPassive { SproutDeathSlow = 1 }, source: "藻", ch: "藻"),
                W(1, hp: 10, passive: new SummonPassive { Taunt = true }, source: "藻", ch: "苗", sproutParent: 0),
            }, new[] { Melee(1000) });
            Assume.That(b.Summons[0] != null && !b.Summons[0].Alive, "夹具:母体以尸体在场");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SpeedModifier), Is.True, "母体死活不论(附录 §2 藻荇)");
        }

        // ---- 终审 Minor 2:跨场携带落位回落换槽时,小藻的母体记号跟着重映射 ----

        [Test]
        public void CarriedParentRelocated_SproutDeathSlowReadsRelocatedParent()
        {
            // 只开 0–2 号:记在 4 号的木灵先回落进 0 号 → 母体(记 0)回落到 2 号;小藻记的 0 要改成 2,
            // 否则藻荇读到的是 0 号那只(无本命)
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(4),
                W(1, hp: 10, passive: new SummonPassive { Taunt = true }, source: "藻", ch: "苗", sproutParent: 0),
                W(0, passive: new SummonPassive { SproutDeathSlow = 1 }, source: "藻", ch: "藻"),
            }, new[] { Melee(1000) }, config: Config(slots: 0b111));
            Assert.That(b.Summons[2].Char, Is.EqualTo("藻"), "夹具前提:母体回落到 2 号");
            Assert.That(b.Summons[1].SproutParentSlot, Is.EqualTo(2), "母体记号随换槽重映射");
            b.EndTurn();
            Assert.That(b.Summons[1].Alive, Is.False, "夹具前提:小藻被打死");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SpeedModifier), Is.True, "藻荇读到真正的母体");
        }

        [Test]
        public void CarriedParentRelocated_SproutCountFollowsParent()
        {
            // 只开 0–4 号:记在 5 号的木灵先回落进 0 号 → 母体(记 0)回落到 3 号;名下 2 只小藻已满 SproutMax 2:
            // 那一拍不再分裂(4 号保持空)
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(5),
                W(1, source: "藻", ch: "苗", sproutParent: 0),
                W(2, source: "藻", ch: "苗", sproutParent: 0),
                W(0, passive: new SummonPassive { SproutPercent = 50, SproutMax = 2 }, source: "藻", ch: "藻"),
            }, new[] { Dummy() }, config: Config(slots: 0b11111));
            Assert.That(b.Summons[3].Char, Is.EqualTo("藻"), "夹具前提:母体回落到 3 号");
            Assert.That((b.Summons[1].SproutParentSlot, b.Summons[2].SproutParentSlot), Is.EqualTo((3, 3)));
            b.EndTurn();
            Assert.That(b.Summons[4], Is.Null, "名下计数正确:已满不再分裂");
        }

        [Test]
        public void CarriedSprout_ParentNotCarried_MarkCleared()
        {
            var b = Battle(Array.Empty<CharDef>(), new[]
            {
                W(0),
                W(1, source: "藻", ch: "苗", sproutParent: 5),
            }, new[] { Dummy() });
            Assert.That(b.Summons[1].SproutParentSlot, Is.EqualTo(-1), "母体没被携带 → −1");
        }
    }
}
