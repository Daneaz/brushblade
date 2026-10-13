using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 2:护盾操作族 —— 附录 G1 ShieldSpend(塔倾 / 地裂,Q5 / Q10)、G8 ShieldToMorale(土生金,Q18)。
    /// (G9 ShieldCarry 浮屠与留存桶已于 2026-10-13 删除,spec 2026-10-13 §1;护盾单桶。)
    ///
    /// 夹具:Element.Heart、PlayerAttack = 100、PlayerMaxHp 5000(护盾上限 5000)、暴击率缺省 0(不摇号)、敌人 10 万血。
    /// 敌人攻击力缺省 0:回合推进时不挨打,护盾只受被测效果与回合初清盾影响。</summary>
    public class EarthShieldOpsTests
    {
        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static BattleEngine Battle(CharDef[] defs, int normal = 0, int level = 1,
            params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => level),
                startingNormalShield: normal);

        private static void Cast(BattleEngine b, string id, int target = -1) =>
            Assert.That(b.Cast(id, target), Is.EqualTo(BattleError.None), id);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static void SetMorale(BattleEngine b, int stacks) => b.PlayerStatuses.Apply(new StatusEffect
            { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1 });

        /// <summary>挂反震 + 冰晶两个载体(出字给了 10 点普通盾)。</summary>
        private static readonly CharDef Carriers = RebalanceFixture.Char("晶",
            new EffectDef(EffectKind.Shield, 10), new EffectDef(EffectKind.ShieldRecoil, 30), new EffectDef(EffectKind.ShieldFrost, 1));

        private static bool HasCarriers(BattleEngine b) =>
            b.PlayerStatuses.Has(StatusKind.ShieldRecoil) && b.PlayerStatuses.Has(StatusKind.ShieldFrost);

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)EffectKind.ShieldSpend, Is.EqualTo((int)EffectKind.TurnPulse + 1));
            Assert.That((int)EffectKind.ShieldToMorale, Is.EqualTo((int)EffectKind.TurnPulse + 2));
        }

        [Test]
        public void NewKinds_Classification()
        {
            foreach (var k in new[] { EffectKind.ShieldSpend, EffectKind.ShieldToMorale })
            {
                Assert.That(MetaRules.ScalesWithCardLevel(k), Is.False, k + " 百分比离散,不吃卡等级");
                Assert.That(CardFaceRules.HostileKinds.Contains(k), Is.False, k + " 作用于玩家自己");
                Assert.That(BattleEngine.TakesElementPercent(k), Is.False, k + " 不吃五行 L3");
            }
        }

        // ================= G1 ShieldSpend(塔倾 / 地裂) =================

        private static CharDef Spender(int percent, params EffectDef[] before) => new("倾", Element.Heart,
            effects: before.Append(new EffectDef(EffectKind.ShieldSpend, percent)).ToArray());

        [Test]
        public void ShieldSpend_Half_FloorRounded()
        {
            // 401 → × 50% 向下取整 = 200,剩 201
            var b = Battle(new[] { Spender(50) }, normal: 401);
            Cast(b, "倾");
            Assert.That(b.PlayerShield, Is.EqualTo(201));
            var broken = b.LastEvents.Where(e => e.Kind == BattleEventKind.ShieldBroken).ToList();
            Assert.That(broken.Count, Is.EqualTo(1));
            Assert.That(broken[0].TargetIndex, Is.EqualTo(-1));
            Assert.That(broken[0].Amount, Is.EqualTo(200));
        }

        /// <summary>D2-土 Task 5:扣盾复用 ShieldBroken,但表现层要区分「部分扣盾」与倾覆清空(倾覆提示 / 护盾条归零)——
        /// 扣盾路径(塔倾 / 地裂 / 土生金)在事件上标 <see cref="EffectSource.ShieldSpend"/>,倾覆仍是 None。</summary>
        [Test]
        public void ShieldSpend_ShieldBrokenEvent_TaggedWithSpendSource()
        {
            var b = Battle(new[] { Spender(50) }, normal: 400);
            Cast(b, "倾");
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.ShieldBroken).Source, Is.EqualTo(EffectSource.ShieldSpend));
            var m = Battle(new[] { ToMorale(30) }, normal: 500);
            Cast(m, "金");
            Assert.That(m.LastEvents.Single(e => e.Kind == BattleEventKind.ShieldBroken).Source, Is.EqualTo(EffectSource.ShieldSpend));
            Assert.That((int)EffectSource.ShieldSpend, Is.EqualTo((int)EffectSource.OverkillSplash + 1), "EffectSource 只追加");
        }

        [Test]
        public void ShieldSpend_NothingToSpend_NoEvent()
        {
            var b = Battle(new[] { Spender(50) }, normal: 1);
            Cast(b, "倾");
            Assert.That(b.ShieldNormal, Is.EqualTo(1), "1 × 50% 取整 = 0");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ShieldBroken), Is.False);
        }

        [Test]
        public void ShieldSpend_AfterBodyDamage_StrikeReadsFullShield()
        {
            // 塔倾:本体 shieldStrike 50 读的是扣盾前的护盾 —— 与不带 ShieldSpend 的同一张字伤害相同
            int Dealt(bool spend)
            {
                var hit = new EffectDef(EffectKind.DamageSingle, 100, shieldStrikePercent: 50);
                var def = spend ? Spender(50, hit) : RebalanceFixture.Char("倾", hit);
                var b = Battle(new[] { def }, normal: 400);
                Cast(b, "倾", 0);
                return 100000 - b.Enemies[0].Hp;
            }
            Assert.That(Dealt(true), Is.EqualTo(Dealt(false)));
            Assert.That(Dealt(true), Is.GreaterThan(100), "护盾加成确实生效(否则对照空转)");
        }

        [Test]
        public void ShieldSpend_TowerTilt_AsLv5Trait()
        {
            // 塔·塔倾(Lv5 攻面):Reshape shieldStrike 50 + ShieldSpend 50,先伤害后减半
            static CharDef Tower(bool spend)
            {
                var trait = spend
                    ? new[] { new EffectDef(EffectKind.Reshape, 0, shieldStrikePercent: 50), new EffectDef(EffectKind.ShieldSpend, 50) }
                    : new[] { new EffectDef(EffectKind.Reshape, 0, shieldStrikePercent: 50) };
                return new CharDef("塔", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.Shield, 60) },
                    attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                    traits: new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "塔倾", trait) });
            }
            var b = Battle(new[] { Tower(true) }, normal: 400, level: 5);
            Assert.That(b.Cast("塔", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerShield, Is.EqualTo(200), "400 × 50%");

            // 特性折叠后 shieldStrike 读的仍是扣盾前的 400 —— 与不带 ShieldSpend 的同一张字伤害相同
            var plain = Battle(new[] { Tower(false) }, normal: 400, level: 5);
            Assert.That(plain.Cast("塔", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(plain.PlayerShield, Is.EqualTo(400), "对照组不扣盾");
            int dealt = 100000 - b.Enemies[0].Hp;
            Assert.That(dealt, Is.EqualTo(100000 - plain.Enemies[0].Hp), "shieldStrike 读扣盾前的护盾");
            var bare = Battle(new[] { Tower(false) }, level: 5);
            Assert.That(bare.Cast("塔", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(dealt, Is.GreaterThan(100000 - bare.Enemies[0].Hp), "护盾加成确实生效(否则对照空转)");
        }

        [Test]
        public void ShieldSpend_ClearsAll_DropsCarriers_NoFrostNoRecoil()
        {
            // 地裂:清空护盾(G1)不算受击 —— 冰晶 / 反震只随判空移除,不冻结、不反伤
            var b = Battle(new[] { Carriers, Spender(100) }, normal: 200);
            Cast(b, "晶");
            Assert.That(HasCarriers(b), Is.True);
            Cast(b, "倾");
            Assert.That(b.PlayerShield, Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False, "反震随判空移除");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False, "冰晶随判空移除");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Damage), Is.False, "不反伤");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "不冻结攻击者");
        }

        [Test]
        public void ShieldSpend_PartialSpend_KeepsCarriers()
        {
            var b = Battle(new[] { Carriers, Spender(50) }, normal: 200);
            Cast(b, "晶");
            Cast(b, "倾");
            Assert.That(b.PlayerShield, Is.EqualTo(105));
            Assert.That(HasCarriers(b), Is.True, "没扣空就不移除");
        }

        [Test]
        public void ShieldSpend_EarthRift_HitsFromPreCastShield_ThenClears()
        {
            // 地裂:击数按出字前护盾(E36,R3),伤害之后清空护盾
            var rift = RebalanceFixture.Char("崩",
                new EffectDef(EffectKind.DamageSingle, 10, scaleBy: ScaleBasis.Shield, scaleDivisor: 100, scaleCap: 4),
                new EffectDef(EffectKind.ShieldSpend, 100));
            var b = Battle(new[] { rift }, normal: 350);
            Cast(b, "崩", 0);
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.None), Is.EqualTo(4),
                "350 盾 → 1 + 3 击");
            Assert.That(b.PlayerShield, Is.EqualTo(0));
            int dmgAt = b.LastEvents.ToList().FindLastIndex(e => e.Kind == BattleEventKind.Damage);
            int brokenAt = b.LastEvents.ToList().FindIndex(e => e.Kind == BattleEventKind.ShieldBroken);
            Assert.That(brokenAt, Is.GreaterThan(dmgAt), "清盾在伤害之后");
        }

        // ================= G8 ShieldToMorale(土生金) =================

        private static CharDef ToMorale(int percent, params EffectDef[] around) => new("金", Element.Heart,
            effects: around.Take(1).Concat(new[] { new EffectDef(EffectKind.ShieldToMorale, percent) }).Concat(around.Skip(1)).ToArray());

        [TestCase(500, 150, 2)]   // 150 / 100 向上取整 = 2
        [TestCase(1000, 300, 3)]
        [TestCase(10, 3, 1)]      // 扣除量 > 0 至少 +1
        [TestCase(3, 0, 0)]       // 3 × 30% 取整 = 0:+0
        [TestCase(0, 0, 0)]
        public void ShieldToMorale_CeilOfSpentOverThreshold(int normal, int spent, int morale)
        {
            var b = Battle(new[] { ToMorale(30) }, normal: normal);
            Cast(b, "金");
            Assert.That(b.PlayerShield, Is.EqualTo(normal - spent));
            Assert.That(Morale(b), Is.EqualTo(morale));
        }

        [Test]
        public void ShieldToMorale_ReadsShieldAfterThisFaceShield()
        {
            // 产出量口径:本面先加的 200 盾也算进去
            var b = Battle(new[] { ToMorale(50, new EffectDef(EffectKind.Shield, 200)) });
            Cast(b, "金");
            Assert.That(b.PlayerShield, Is.EqualTo(100));
            Assert.That(Morale(b), Is.EqualTo(1));
        }

        [Test]
        public void ShieldToMorale_ClampedAtCap_OverflowNotCounted()
        {
            // 战意 4 + 3 → 钳 5;后面的聚金(每溢出 1 层给 30 盾)看不到溢出
            var b = Battle(new[] { ToMorale(30, new EffectDef(EffectKind.Shield, 0), new EffectDef(EffectKind.MoraleOverflowShield, 30)) },
                normal: 1000);
            SetMorale(b, 4);
            Cast(b, "金");
            Assert.That(Morale(b), Is.EqualTo(Config.MoraleCap));
            Assert.That(b.PlayerShield, Is.EqualTo(700), "没有聚金加盾 = 不计溢出");
        }

        [Test]
        public void ShieldToMorale_EmptiesShield_DropsCarriers_NoFrost()
        {
            var b = Battle(new[] { Carriers, ToMorale(100) });
            Cast(b, "晶");
            Cast(b, "金");
            Assert.That(b.PlayerShield, Is.EqualTo(0));
            Assert.That(Morale(b), Is.EqualTo(1));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil) || b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False);
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "不触发冰晶");
        }

        // ================= ConfigLoader =================

        private static RecipeGraph Load(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""塔"",""element"":""Earth"",""effects"":[{""kind"":""Shield"",""value"":60}," + effect + @"]}]}");

        [Test]
        public void Loader_ReadsNewKinds()
        {
            Assert.That(Load(@"{""kind"":""ShieldSpend"",""value"":100}").Get("塔").Effects[1].Kind, Is.EqualTo(EffectKind.ShieldSpend));
            Assert.That(Load(@"{""kind"":""ShieldToMorale"",""value"":30}").Get("塔").Effects[1].Value, Is.EqualTo(30));
        }

        [TestCase(@"{""kind"":""ShieldSpend"",""value"":0}")]
        [TestCase(@"{""kind"":""ShieldSpend"",""value"":101}")]
        [TestCase(@"{""kind"":""ShieldToMorale"",""value"":0}")]
        [TestCase(@"{""kind"":""ShieldToMorale"",""value"":101}")]
        [TestCase(@"{""kind"":""ShieldCarry"",""value"":50,""turns"":3}")]   // 浮屠已退役:未知 kind 加载即拒
        public void Loader_RejectsBadValues(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
