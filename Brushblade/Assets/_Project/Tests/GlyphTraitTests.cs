using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>拆字 / 成字特性与部件印记(D2-0 Task 7,spec §9 / §11.8,裁定 E9 / E10)。
    /// 夹具自造字与配方:Element.Heart(生克 1.0×)、PlayerAttack = 100、打不死的无攻靶子。</summary>
    public class GlyphTraitTests
    {
        private static BattleConfig Config => new() { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static CharDef Part(string id, params EffectDef[] effects) =>
            new(id, Element.Heart, effects: effects, isComponent: true);

        private static TraitDef Glyph(TraitTrigger trigger, string partChar, int partCount, params EffectDef[] effects) =>
            new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "字形", effects, trigger, partChar, partCount);

        private static CharDef Twin(string id, string a, string b, TraitDef trait) => new(id, Element.Heart,
            recipe: new[] { a, b },
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: trait == null ? null : new[] { trait });

        // 部件:火本体单体 10 + 灼 1;山本体单体 10;金 / 木本体护盾
        private static readonly CharDef Fire = Part("火", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BurnSingle, 1));
        private static readonly CharDef Mountain = Part("山", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Metal = Part("金", new EffectDef(EffectKind.Shield, 5));
        private static readonly CharDef Wood = Part("木", new EffectDef(EffectKind.Shield, 5));

        /// <summary>鍂:拆字 → 战意 +2(双金)。</summary>
        private static CharDef Jin(TraitDef trait = null) =>
            Twin("鍂", "金", "金", trait ?? Glyph(TraitTrigger.OnDismantle, null, 0, new EffectDef(EffectKind.Morale, 2)));

        /// <summary>炎:拆字印记 → 拆出的 2 个火本回合出手各附灼 2(双焰)。</summary>
        private static CharDef Yan() =>
            Twin("炎", "火", "火", Glyph(TraitTrigger.OnDismantle, "火", 2, new EffectDef(EffectKind.BurnSingle, 2)));

        /// <summary>灿:拆字印记 → 山本回合出手改为全体并全体灼 1(火山)。</summary>
        private static CharDef Can() =>
            Twin("灿", "火", "山", Glyph(TraitTrigger.OnDismantle, "山", 1,
                new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All), new EffectDef(EffectKind.BurnAll, 1)));

        /// <summary>茂:成字 → 木灵得盾。</summary>
        private static CharDef Mao() =>
            Twin("茂", "木", "木", Glyph(TraitTrigger.OnCompose, null, 0, new EffectDef(EffectKind.ShieldSummons, 20)));

        private static CharDef[] AllDefs(params CharDef[] chars) =>
            new[] { Fire, Mountain, Metal, Wood }.Concat(chars).ToArray();

        private static EnemyDef[] Mobs(int n) => Enumerable.Range(0, n).Select(_ => RebalanceFixture.Mob()).ToArray();

        private static BattleEngine Battle(CharDef[] chars, int level, string[] library, string[] pool,
            int enemies = 1, IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(AllDefs(chars)), Config, library, pool, Mobs(enemies), seed: 1,
                cardLevels: chars.ToDictionary(d => d.Id, _ => level), startingSummons: summons);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);
        private static int Burn(BattleEngine b, int i = 0) => b.Enemies[i].Statuses.TotalMagnitude(StatusKind.Burn);

        // ---------------- 拆字即时 ----------------

        [Test]
        public void Dismantle_GlyphTrait_GrantsMorale_OncePerBattle()
        {
            var b = Battle(new[] { Jin() }, 4, new[] { "鍂" }, Array.Empty<string>());
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(2), "拆鍂:战意 +2");

            Assert.That(b.Compose("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(2), "每场 1 次(E9):合回再拆不再给");

            var fresh = Battle(new[] { Jin() }, 4, new[] { "鍂" }, Array.Empty<string>());
            fresh.Dismantle("鍂");
            Assert.That(Morale(fresh), Is.EqualTo(2), "新一场可再给一次");
        }

        [Test]
        public void Dismantle_GlyphTrait_LevelTooLow_DoesNotTrigger()
        {
            var b = Battle(new[] { Jin() }, 3, new[] { "鍂" }, Array.Empty<string>());
            b.Dismantle("鍂");
            Assert.That(Morale(b), Is.EqualTo(0), "Lv4 槽位在 3 级未解锁");
        }

        [Test]
        public void Compose_DoesNotFireDismantleTrait()
        {
            var b = Battle(new[] { Jin() }, 4, Array.Empty<string>(), new[] { "金", "金" });
            b.Compose("鍂");
            Assert.That(Morale(b), Is.EqualTo(0), "拆字特性只在拆时触发");
        }

        [Test]
        public void Dismantle_GlyphTrait_KillsLastEnemy_PhaseWon()
        {
            // I1:字形即时伤害打死最后一只怪 → 拆字收尾必须判胜(DrainReactions → RefreshSummonAura → CheckWin)
            var jin = Jin(Glyph(TraitTrigger.OnDismantle, null, 0,
                new EffectDef(EffectKind.DamageSingle, 99999, shape: TargetArea.All)));
            var b = new BattleEngine(RebalanceFixture.Graph(AllDefs(jin)), Config, new[] { "鍂" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(hp: 50) }, seed: 1, cardLevels: new Dictionary<string, int> { ["鍂"] = 4 });
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Dismantle_GlyphTrait_BurnSettleKillsLastEnemy_PhaseWon()
        {
            var jin = Jin(Glyph(TraitTrigger.OnDismantle, null, 0,
                new EffectDef(EffectKind.BurnAll, 99999),
                new EffectDef(EffectKind.BurnSettleNow, 0, pick: EffectPick.All)));
            var b = new BattleEngine(RebalanceFixture.Graph(AllDefs(jin)), Config, new[] { "鍂" }, Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(hp: 50) }, seed: 1, cardLevels: new Dictionary<string, int> { ["鍂"] = 4 });
            Assert.That(b.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
        }

        // ---------------- 成字即时 ----------------

        private static SummonSnapshot Spirit(int slot) => new()
        {
            Slot = slot, Char = "木", Element = Element.Wood, Hp = 50, MaxHp = 100, Attack = 0, Speed = 100,
        };

        [Test]
        public void Compose_GlyphTrait_ShieldsSummons()
        {
            var b = Battle(new[] { Mao() }, 4, Array.Empty<string>(), new[] { "木", "木" }, summons: new[] { Spirit(0) });
            Assert.That(b.Compose("茂"), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Shield, Is.GreaterThan(0), "合成茂:木灵得盾");
        }

        [Test]
        public void Compose_GlyphTrait_LevelTooLow_DoesNotTrigger()
        {
            var b = Battle(new[] { Mao() }, 3, Array.Empty<string>(), new[] { "木", "木" }, summons: new[] { Spirit(0) });
            b.Compose("茂");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(0));
        }

        // ---------------- 部件印记 ----------------

        private static int BurnDeltaOfCast(BattleEngine b, string part)
        {
            int before = Burn(b);
            Assert.That(b.Cast(part, 0), Is.EqualTo(BattleError.None));
            return Burn(b) - before;
        }

        [Test]
        public void Mark_TwoFires_EachAddBurn2_ThirdDoesNot()
        {
            // 池里先放一个火:拆炎后池 = 火 ×3。印记次数 2,第三次出火不再加
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, new[] { "火" });
            Assert.That(b.Dismantle("炎"), Is.EqualTo(BattleError.None));
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1 + 2), "火本体灼 1 + 印记灼 2");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1 + 2), "第二个火同样");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1), "印记用完");
        }

        [Test]
        public void Mark_ClearedAtNextTurn()
        {
            var b = Battle(new[] { Yan() }, 4, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(3));
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1), "回合开始清空印记(E10)");
        }

        [Test]
        public void Mark_LevelTooLow_NoMark()
        {
            var b = Battle(new[] { Yan() }, 3, new[] { "炎" }, Array.Empty<string>());
            b.Dismantle("炎");
            Assert.That(BurnDeltaOfCast(b, "火"), Is.EqualTo(1));
        }

        [Test]
        public void Mark_Reshape_MakesPartHitAll_AndBurnAll()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, Array.Empty<string>(), enemies: 3);
            b.Dismantle("灿");
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.None), "改为全体后不需要选目标");
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b.Enemies[i].Hp, Is.LessThan(b.Enemies[i].MaxHp), $"敌人 {i} 吃到全体伤害");
                Assert.That(Burn(b, i), Is.EqualTo(1), $"敌人 {i} 全体灼 1");
            }
        }

        [Test]
        public void Mark_Reshape_OnlyOnce()
        {
            var b = Battle(new[] { Can() }, 4, new[] { "灿" }, new[] { "山" }, enemies: 2);
            b.Dismantle("灿");
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast("山", -1), Is.EqualTo(BattleError.InvalidTarget), "印记次数 1:第二个山回到单体,两名敌人要选目标");
            int hp1 = b.Enemies[1].Hp;
            Assert.That(b.Cast("山", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(hp1), "第二个山只打选中的那名");
        }

        // ---------------- 快照 ----------------

        [Test]
        public void Snapshot_RoundTrip_KeepsMarksAndGlyphUses()
        {
            var defs = new[] { Yan(), Jin() };
            var b = Battle(defs, 4, new[] { "炎", "鍂" }, Array.Empty<string>());
            b.Dismantle("炎");
            b.Dismantle("鍂");

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 1, Seed = 1,
                    InProgress = new InProgressRun
                    {
                        FromDepth = 1, FirstTowerSegment = true,
                        Run = new RunSnapshot { Battle = b.Capture() },
                    },
                },
            };
            var snap = SaveSerializer.FromJson(SaveSerializer.ToJson(meta)).EndlessV2.InProgress.Run.Battle;
            var restored = BattleEngine.Restore(snap, RebalanceFixture.Graph(AllDefs(defs)), Config,
                defs.ToDictionary(d => d.Id, _ => 4),
                new Dictionary<string, EnemyDef> { [RebalanceFixture.Mob().Id] = RebalanceFixture.Mob() });

            Assert.That(BurnDeltaOfCast(restored, "火"), Is.EqualTo(3), "读档后印记仍在");
            Assert.That(BurnDeltaOfCast(restored, "火"), Is.EqualTo(3), "剩余次数也在");
            Assert.That(restored.Compose("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(restored.Dismantle("鍂"), Is.EqualTo(BattleError.None));
            Assert.That(Morale(restored), Is.EqualTo(2), "字形次数进快照:读档后不能再拿一次");
        }

        // ---------------- 恒等 ----------------

        [Test]
        public void NoGlyphData_DismantleCompose_DoNotTouchAnyRandom()
        {
            var plain = Twin("鍂", "金", "金", null);
            var b = Battle(new[] { plain }, 4, new[] { "鍂" }, Array.Empty<string>());
            var before = b.Capture();
            b.Dismantle("鍂");
            b.Compose("鍂");
            var after = b.Capture();
            Assert.That(after.RandomState, Is.EqualTo(before.RandomState));
            Assert.That(after.TargetRandomState, Is.EqualTo(before.TargetRandomState));
            Assert.That(after.TraitRandomState, Is.EqualTo(before.TraitRandomState));
            Assert.That(after.TraitUsesThisBattle.Count, Is.EqualTo(0));
            Assert.That(after.PartMarks.Count, Is.EqualTo(0));
        }

        // ---------------- 数据加载 ----------------

        private static string CharJson(string traits) =>
            @"{""chars"":[{""id"":""火"",""element"":""Fire"",""component"":true},{""id"":""山"",""element"":""Earth"",""component"":true},
              {""id"":""炎"",""rarity"":""Gold"",""element"":""Fire"",""recipe"":[""火"",""火""],
                ""effects"":[{""kind"":""BurnSingle"",""value"":3}],
                ""attackEffects"":[{""kind"":""DamageSingle"",""value"":168}],
                ""traits"":[" + traits + "]}]}";

        [Test]
        public void Loader_ReadsGlyphTriggerAndPart()
        {
            var g = ConfigLoader.LoadGraph(CharJson(
                @"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""火"",""partCount"":2,
                   ""name"":""双焰"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}"));
            var t = g.Get("炎").Traits[0];
            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.OnDismantle));
            Assert.That(t.PartChar, Is.EqualTo("火"));
            Assert.That(t.PartCount, Is.EqualTo(2));
        }

        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""name"":""x"",""effects"":[{""kind"":""BurnSingle"",""value"":2}]}",
            TestName = "Loader_InstantGlyph_EnemyTargetWithoutPickAll_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""山"",""partCount"":1,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartNotInRecipe_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""partChar"":""火"",""partCount"":1,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartOnCompose_Throws")]
        [TestCase(@"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnDismantle"",""partChar"":""火"",""partCount"":0,""name"":""x"",""effects"":[]}",
            TestName = "Loader_PartCountZero_Throws")]
        public void Loader_BadGlyph_Throws(string trait)
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(CharJson(trait)));
        }

        [TestCase("OnCompose", TestName = "Loader_GlyphSapling_OnCompose_Throws")]
        [TestCase("OnDismantle", TestName = "Loader_GlyphSapling_OnDismantle_Throws")]
        public void Loader_GlyphWithSummonSapling_Throws(string trigger)
        {
            // 本体面带 Summon:幼苗在「同面须有召唤」那条校验下是合法的,只有字形这条新校验能拦住
            string json = @"{""chars"":[{""id"":""火"",""element"":""Fire"",""component"":true},{""id"":""木"",""element"":""Wood""},
              {""id"":""炎"",""rarity"":""Gold"",""element"":""Wood"",""recipe"":[""火"",""火""],
                ""effects"":[{""kind"":""Summon"",""value"":100,""count"":1,""attack"":5,""summonChar"":""木""}],
                ""traits"":[{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""" + trigger + @""",""name"":""x"",
                   ""effects"":[{""kind"":""SummonSapling"",""value"":20,""count"":1}]}]}]}";
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(json));
            Assert.That(ex.Message, Does.Contain("字形特性"));
        }

        [Test]
        public void Loader_InstantGlyph_PickAll_Ok()
        {
            var g = ConfigLoader.LoadGraph(CharJson(
                @"{""slot"":""Lv4"",""form"":""Passive"",""trigger"":""OnCompose"",""name"":""三焰"",
                   ""effects"":[{""kind"":""BurnSettleNow"",""value"":0,""keepStacks"":true,""pick"":""All""}]}"));
            Assert.That(g.Get("炎").Traits[0].Trigger, Is.EqualTo(TraitTrigger.OnCompose));
        }
    }
}
