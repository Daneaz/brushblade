using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 2:护盾操作族 —— 附录 G1 ShieldSpend(塔倾 / 地裂,Q5 / Q10)、G8 ShieldToMorale(土生金,Q18)、
    /// G9 ShieldCarry(浮屠,Q4)。
    ///
    /// 夹具:Element.Heart、PlayerAttack = 100、PlayerMaxHp 5000(护盾上限 5000)、暴击率缺省 0(不摇号)、敌人 10 万血。
    /// 敌人攻击力缺省 0:回合推进时不挨打,两桶护盾只受被测效果与回合初清盾影响。</summary>
    public class EarthShieldOpsTests
    {
        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static BattleEngine Battle(CharDef[] defs, int normal = 0, int persist = 0, int level = 1,
            params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => level),
                startingNormalShield: normal, startingPersistShield: persist);

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
            Assert.That((int)EffectKind.ShieldCarry, Is.EqualTo((int)EffectKind.TurnPulse + 3));
            Assert.That((int)StatusKind.ShieldCarry, Is.EqualTo((int)StatusKind.TurnPulse + 1));
        }

        [Test]
        public void NewKinds_Classification()
        {
            foreach (var k in new[] { EffectKind.ShieldSpend, EffectKind.ShieldToMorale, EffectKind.ShieldCarry })
            {
                Assert.That(MetaRules.ScalesWithCardLevel(k), Is.False, k + " 百分比离散,不吃卡等级");
                Assert.That(CardFaceRules.HostileKinds.Contains(k), Is.False, k + " 作用于玩家自己");
                Assert.That(BattleEngine.TakesElementPercent(k), Is.False, k + " 不吃五行 L3");
            }
            Assert.That(StatusRules.IsBattleScoped(StatusKind.ShieldCarry), Is.True);
        }

        // ================= G1 ShieldSpend(塔倾 / 地裂) =================

        private static CharDef Spender(int percent, params EffectDef[] before) => new("倾", Element.Heart,
            effects: before.Append(new EffectDef(EffectKind.ShieldSpend, percent)).ToArray());

        [Test]
        public void ShieldSpend_HalfOfBothBuckets_NormalFirst_FloorRounded()
        {
            // 普通 101 + 留存 300 = 401 → × 50% 向下取整 = 200:普通桶先扣光 101,余 99 从留存桶扣
            var b = Battle(new[] { Spender(50) }, normal: 101, persist: 300);
            Cast(b, "倾");
            Assert.That(b.ShieldNormal, Is.EqualTo(0));
            Assert.That(b.ShieldPersist, Is.EqualTo(201));
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
        public void ShieldSpend_NormalBucketCoversAll_PersistUntouched()
        {
            var b = Battle(new[] { Spender(50) }, normal: 400, persist: 200);
            Cast(b, "倾");
            Assert.That(b.ShieldNormal, Is.EqualTo(100), "300 全从普通桶扣");
            Assert.That(b.ShieldPersist, Is.EqualTo(200));
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
            var tower = new CharDef("塔", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 60) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "塔倾", new[]
                    {
                        new EffectDef(EffectKind.Reshape, 0, shieldStrikePercent: 50),
                        new EffectDef(EffectKind.ShieldSpend, 50),
                    }),
                });
            var b = Battle(new[] { tower }, normal: 300, persist: 100, level: 5);
            Assert.That(b.Cast("塔", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerShield, Is.EqualTo(200), "400 × 50%");
            Assert.That(b.ShieldNormal, Is.EqualTo(100));
            Assert.That(b.ShieldPersist, Is.EqualTo(100), "普通桶够扣,留存桶不动");
        }

        [Test]
        public void ShieldSpend_ClearsAll_DropsCarriers_NoFrostNoRecoil()
        {
            // 地裂:清两桶(G1)不算受击 —— 冰晶 / 反震只随判空移除,不冻结、不反伤
            var b = Battle(new[] { Carriers, Spender(100) }, persist: 200);
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
            var b = Battle(new[] { Carriers, Spender(50) }, persist: 200);
            Cast(b, "晶");
            Cast(b, "倾");
            Assert.That(b.PlayerShield, Is.EqualTo(105));
            Assert.That(HasCarriers(b), Is.True, "没扣空就不移除");
        }

        [Test]
        public void ShieldSpend_EarthRift_HitsFromPreCastShield_ThenClears()
        {
            // 地裂:击数按出字前护盾(E36,R3),伤害之后清两桶
            var rift = RebalanceFixture.Char("崩",
                new EffectDef(EffectKind.DamageSingle, 10, scaleBy: ScaleBasis.Shield, scaleDivisor: 100, scaleCap: 4),
                new EffectDef(EffectKind.ShieldSpend, 100));
            var b = Battle(new[] { rift }, normal: 150, persist: 200);
            Cast(b, "崩", 0);
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.None), Is.EqualTo(4),
                "350 盾 → 1 + 3 击");
            Assert.That(b.ShieldNormal + b.ShieldPersist, Is.EqualTo(0));
            int dmgAt = b.LastEvents.ToList().FindLastIndex(e => e.Kind == BattleEventKind.Damage);
            int brokenAt = b.LastEvents.ToList().FindIndex(e => e.Kind == BattleEventKind.ShieldBroken);
            Assert.That(brokenAt, Is.GreaterThan(dmgAt), "清盾在伤害之后");
        }

        // ================= G8 ShieldToMorale(土生金) =================

        private static CharDef ToMorale(int percent, params EffectDef[] around) => new("金", Element.Heart,
            effects: around.Take(1).Concat(new[] { new EffectDef(EffectKind.ShieldToMorale, percent) }).Concat(around.Skip(1)).ToArray());

        [TestCase(500, 0, 150, 2)]   // 150 / 100 向上取整 = 2
        [TestCase(1000, 0, 300, 3)]
        [TestCase(10, 0, 3, 1)]      // 扣除量 > 0 至少 +1
        [TestCase(3, 0, 0, 0)]       // 3 × 30% 取整 = 0:+0
        [TestCase(0, 0, 0, 0)]
        public void ShieldToMorale_CeilOfSpentOverThreshold(int normal, int persist, int spent, int morale)
        {
            var b = Battle(new[] { ToMorale(30) }, normal: normal, persist: persist);
            Cast(b, "金");
            Assert.That(b.PlayerShield, Is.EqualTo(normal + persist - spent));
            Assert.That(Morale(b), Is.EqualTo(morale));
        }

        [Test]
        public void ShieldToMorale_NormalBucketFirst()
        {
            var b = Battle(new[] { ToMorale(30) }, normal: 100, persist: 400);
            Cast(b, "金");
            Assert.That(b.ShieldNormal, Is.EqualTo(0));
            Assert.That(b.ShieldPersist, Is.EqualTo(350), "150 = 普通 100 + 留存 50");
            Assert.That(Morale(b), Is.EqualTo(2));
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

        // ================= G9 ShieldCarry(浮屠) =================

        private static CharDef Carry(int percent, string id = "浮", int turns = 3, params EffectDef[] before) => new(id, Element.Heart,
            effects: before.Append(new EffectDef(EffectKind.ShieldCarry, percent, turns: turns)).ToArray());

        [Test]
        public void ShieldCarry_AddsBackHalfOfClearedNormal_ThreeTurnStarts()
        {
            var b = Battle(new[] { Carry(50) }, normal: 400, persist: 300);
            Cast(b, "浮");
            Assert.That(b.PlayerStatuses.Find(StatusKind.ShieldCarry)?.Magnitude, Is.EqualTo(50));
            int heft = b.PlayerStatuses.TotalMagnitude(StatusKind.Heft);

            var normals = new List<int>();
            for (int t = 0; t < 4; t++)
            {
                b.EndTurn();
                normals.Add(b.ShieldNormal);
                Assert.That(b.ShieldPersist, Is.EqualTo(300), "留存桶不清、不参与加回");
            }
            Assert.That(normals, Is.EqualTo(new[] { 200, 100, 50, 0 }), "之后 3 个回合开始各一次,第 4 次已到期");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldCarry), Is.False);
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Heft), Is.EqualTo(heft), "加回不攒厚");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Shield), Is.False, "第 4 次没有加回");
        }

        [Test]
        public void ShieldCarry_EmitsShieldEvent()
        {
            var b = Battle(new[] { Carry(50) }, normal: 400);
            Cast(b, "浮");
            b.EndTurn();
            var shield = b.LastEvents.Where(e => e.Kind == BattleEventKind.Shield).ToList();
            Assert.That(shield.Count, Is.EqualTo(1));
            Assert.That(shield[0].TargetIndex, Is.EqualTo(Targeting.PlayerTarget));
            Assert.That(shield[0].Amount, Is.EqualTo(200));
        }

        [Test]
        public void ShieldCarry_StrongestWins()
        {
            var b = Battle(new[] { Carry(30, "浮"), Carry(60, "屠") }, normal: 1000);
            Cast(b, "浮");
            Cast(b, "屠");
            b.EndTurn();
            Assert.That(b.ShieldNormal, Is.EqualTo(600), "取最强 60%,不相加");
        }

        [Test]
        public void ShieldCarry_AddBackBeforeEmptyCheck_KeepsCarriers()
        {
            // 只有普通盾:清空后加回 → 判空时还有盾,反震 / 冰晶保留
            var b = Battle(new[] { Carriers, Carry(50) }, normal: 90);
            Cast(b, "晶");
            Cast(b, "浮");
            b.EndTurn();
            Assert.That(b.ShieldNormal, Is.EqualTo(50));
            Assert.That(HasCarriers(b), Is.True, "加回在判空移除之前");
        }

        [Test]
        public void ShieldCarry_NothingKept_CarriersDropped()
        {
            var b = Battle(new[] { Carriers, Carry(5) });
            Cast(b, "晶");   // 普通盾 10 → 10 × 5% = 0
            Cast(b, "浮");
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil) || b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False);
        }

        [Test]
        public void ShieldCarry_BeforeMoraleShield()
        {
            // 顺序:清盾 → 加回 → 判空 → 金气。金气的 40 不进被清量
            var aura = RebalanceFixture.Char("气", new EffectDef(EffectKind.Morale, 5), new EffectDef(EffectKind.MoraleShield, 40));
            var b = Battle(new[] { aura, Carry(50) }, normal: 400);
            Cast(b, "气");
            Cast(b, "浮");
            b.EndTurn();
            Assert.That(b.ShieldNormal, Is.EqualTo(240));
            b.EndTurn();
            Assert.That(b.ShieldNormal, Is.EqualTo(160), "240 × 50% + 40");
        }

        // ================= ConfigLoader =================

        private static RecipeGraph Load(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""塔"",""element"":""Earth"",""effects"":[{""kind"":""Shield"",""value"":60}," + effect + @"]}]}");

        [Test]
        public void Loader_ReadsNewKinds()
        {
            Assert.That(Load(@"{""kind"":""ShieldSpend"",""value"":100}").Get("塔").Effects[1].Kind, Is.EqualTo(EffectKind.ShieldSpend));
            Assert.That(Load(@"{""kind"":""ShieldToMorale"",""value"":30}").Get("塔").Effects[1].Value, Is.EqualTo(30));
            var carry = Load(@"{""kind"":""ShieldCarry"",""value"":50,""turns"":3}").Get("塔").Effects[1];
            Assert.That(carry.Kind, Is.EqualTo(EffectKind.ShieldCarry));
            Assert.That(carry.Turns, Is.EqualTo(3));
        }

        [TestCase(@"{""kind"":""ShieldSpend"",""value"":0}")]
        [TestCase(@"{""kind"":""ShieldSpend"",""value"":101}")]
        [TestCase(@"{""kind"":""ShieldToMorale"",""value"":0}")]
        [TestCase(@"{""kind"":""ShieldToMorale"",""value"":101}")]
        [TestCase(@"{""kind"":""ShieldCarry"",""value"":50}")]               // 漏写 turns
        [TestCase(@"{""kind"":""ShieldCarry"",""value"":0,""turns"":3}")]
        [TestCase(@"{""kind"":""ShieldCarry"",""value"":101,""turns"":3}")]
        public void Loader_RejectsBadValues(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
