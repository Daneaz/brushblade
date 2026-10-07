using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 1(第二批):V1 致盲多来源取最强、N13 特性出字内次数上限 limit、
    /// G13 / N12 开局登记(battles N)。
    ///
    /// 夹具口径同 TraitModifierTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、
    /// 暴击率 0。敌人排布同 BurnAdjacentSpreadTests:前三只依次落在前排列 1、2、0,第四只后排列 1。</summary>
    public class FireOpeningLimitBlindTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, int level, EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static BattleEngine Battle(CharDef def, int level, params EnemyDef[] enemies) =>
            Battle(new[] { def }, level, enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() });

        private static EnemyDef[] FourMobs() => new[]
        {
            new EnemyDef("甲", Element.Heart, Hp, 0),
            new EnemyDef("乙", Element.Heart, Hp, 0),
            new EnemyDef("丙", Element.Heart, Hp, 0),
            new EnemyDef("丁", Element.Heart, Hp, 0, row: EnemyRow.Back),
        };

        private static TraitDef Trait(TraitSlot slot, TraitFace face, TraitForm form, params EffectDef[] effects) =>
            new(slot, face, form, null, "试" + (int)slot, effects);

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        // ---------------- V1:致盲多来源取最强 ----------------

        [Test]
        public void Blind_MultipleSources_TakeStrongest_NotSum()
        {
            // 60 + 40:相加 = 100% 必不中,取最强 = 60% → 十拍里至少中一次(种子固定)
            var b = Battle(RebalanceFixture.Char("试", new EffectDef(EffectKind.Shield, 1)), 1, RebalanceFixture.Mob(attack: 10));
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 60, TurnsLeft = -1, SourceId = "甲" });
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 40, TurnsLeft = -1, SourceId = "乙" });
            int hp = b.PlayerHp;
            for (int i = 0; i < 10 && b.Phase == BattlePhase.PlayerTurn; i++) b.EndTurn();
            Assert.That(b.PlayerHp, Is.LessThan(hp), "取最强 60%:仍有 40% 命中");
        }

        // ---------------- N13:limit ----------------

        [Test]
        public void Limit_CapsReactionEnqueuesPerCast()
        {
            TraitDef OnKill(int limit) => new(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "迎",
                new[] { new EffectDef(EffectKind.Morale, 1) }, TraitTrigger.OnKill, maxPerCast: limit);
            CharDef Edge(int limit) => new("刃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 20, shape: TargetArea.All) },
                traits: new[] { OnKill(limit) });
            // 三只一击即死 + 一只打不死的(全灭会直接判胜,不再兑现反应)
            EnemyDef[] three() => new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 10),
                RebalanceFixture.Mob() };

            var unlimited = Battle(new[] { Edge(0) }, 6, three());
            unlimited.Cast("刃", -1, attackMode: true);
            Assert.That(unlimited.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(3), "limit 0 = 不限");

            var capped = Battle(new[] { Edge(2) }, 6, three());
            capped.Cast("刃", -1, attackMode: true);
            Assert.That(capped.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2), "同一次出字最多入队 2 次");
            capped.Cast("刃", -1, attackMode: true);   // 下一张字:计数重置(这次没有击杀)
            Assert.That(capped.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2));
        }

        // ---------------- G13 / N12:开局登记 ----------------

        [Test]
        public void OpeningBattles_RegistersInsteadOfApplying_NextBattlesOpen_Decrements()
        {
            var flame = new CharDef("炎", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.BurnAll, 2) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active,
                    new EffectDef(EffectKind.BurnAll, 2, openingBattles: 2)) });
            var killer = new CharDef("杀", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100000) });
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 4).Select(_ => new[] { RebalanceFixture.Mob(hp: 5000) }).ToArray(),
                RewardPool = new[] { "杀" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(flame, killer), config, Config,
                new[] { "炎", "杀", "杀", "杀", "杀" }, Array.Empty<string>(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["炎"] = 8 });

            Assert.That(run.Battle.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False, "第 1 场开局:还没有登记");
            Assert.That(run.Battle.Cast("炎", -1), Is.EqualTo(BattleError.None));
            Assert.That(Burn(run.Battle, 0), Is.EqualTo(2), "登记的那一条本场不执行,只有本体 2 层");
            var pending = run.Battle.PendingOpenings.Single();
            Assert.That(pending.Kind, Is.EqualTo(EffectKind.BurnAll));
            Assert.That(pending.Value, Is.EqualTo(2));
            Assert.That(pending.BattlesLeft, Is.EqualTo(2));
            Assert.That(pending.SourceCharId, Is.EqualTo("炎"));

            Win(run);
            Assert.That(run.Battle.Enemies[0].Statuses.Has(StatusKind.Burn), Is.True, "第 2 场开局全体 +灼");
            Win(run);
            Assert.That(run.Battle.Enemies[0].Statuses.Has(StatusKind.Burn), Is.True, "第 3 场开局仍生效(battles 2)");
            Win(run);
            Assert.That(run.Battle.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False, "第 4 场:已用完");
        }

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("杀", 0), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
        }

        // ---------------- ConfigLoader:battles / limit ----------------

        private const string FireBody = @"""effects"":[{""kind"":""BurnAll"",""value"":2}],
            ""attackEffects"":[{""kind"":""DamageSingle"",""value"":40}]";

        [Test]
        public void ConfigLoader_ParsesOpeningBattlesAndMaxPerCast()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv8"",""face"":""Feature"",""name"":""炎"",""effects"":[{""kind"":""BurnAll"",""value"":2,""openingBattles"":5}]},
                  {""slot"":""Lv8"",""face"":""Attack"",""form"":""Passive"",""trigger"":""OnKill"",""maxPerCast"":2,""name"":""连"",""effects"":[{""kind"":""Morale"",""value"":1}]}
                ]}]}");
            var t = g.Get("甲").Traits;
            Assert.That(t[0].Effects[0].OpeningBattles, Is.EqualTo(5));
            Assert.That(t[1].MaxPerCast, Is.EqualTo(2));
        }

        [Test]
        public void ConfigLoader_OpeningEffectThatNeedsTarget_Throws()
        {
            // 开局效果脱离出字结算、没有主目标:单体灼不能登记(RegisterOpening 会抛),加载期就拦下
            Assert.Throws<Brushblade.Data.ConfigException>(() => Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv8"",""face"":""Attack"",""name"":""错"",""effects"":[{""kind"":""BurnSingle"",""value"":2,""openingBattles"":1}]}]}]}"));
        }

        // ---------------- 修复轮 1:开局效果保留 Pick / Shape,加载校验与运行一致 ----------------

        /// <summary>开局效果作用于全体:第 1 场只登记,第 2 场开局三只敌人都吃到。</summary>
        private static RunEngine OpeningRun(EffectDef opening, int mobs = 3)
        {
            var src = new CharDef("焱", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 1) },
                traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, opening) });
            var killer = new CharDef("杀", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100000, shape: TargetArea.All) });
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, mobs).Select(__ => RebalanceFixture.Mob(hp: 5000)).ToArray()).ToArray(),
                RewardPool = new[] { "杀" },
            };
            return new RunEngine(RebalanceFixture.Graph(src, killer), config, Config,
                new[] { "焱", "杀", "杀", "杀" }, Array.Empty<string>(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["焱"] = 8 });
        }

        private static void WinAll(RunEngine run)
        {
            Assert.That(run.Battle.Cast("杀", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
        }

        [Test]
        public void Opening_WeakenPickAll_NextBattleHitsAllEnemies()
        {
            var weaken = new EffectDef(EffectKind.Weaken, 20, turns: 2, pick: EffectPick.All, openingBattles: 1);
            Assert.That(BattleEngine.EffectNeedsTarget(OpeningEffect.Of(weaken, "焱", Element.Heart).ToEffect()), Is.False,
                "ToEffect 保留 pick All");
            var run = OpeningRun(weaken);
            Assert.That(run.Battle.Cast("焱", -1), Is.EqualTo(BattleError.None), "登记不抛");
            Assert.That(run.Battle.Enemies.Any(e => e.Statuses.Has(StatusKind.Curse)), Is.False, "本场不执行");
            Assert.That(run.Battle.PendingOpenings.Single().Pick, Is.EqualTo(EffectPick.All));
            WinAll(run);
            Assert.That(run.Battle.Enemies.All(e => e.Statuses.Has(StatusKind.Curse)), Is.True, "下一场开局全体减攻");
        }

        [Test]
        public void Opening_DamageShapeAll_NextBattleHitsAllEnemies()
        {
            var dmg = new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All, openingBattles: 1);
            var run = OpeningRun(dmg);
            Assert.That(run.Battle.Cast("焱", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Enemies.All(e => e.Hp == 5000), Is.True, "本场不执行");
            Assert.That(run.Battle.PendingOpenings.Single().Shape, Is.EqualTo(TargetArea.All));
            WinAll(run);
            Assert.That(run.Battle.Enemies.All(e => e.Hp < 5000), Is.True, "下一场开局全体受伤");
        }

        [Test]
        public void Opening_PickShape_SurviveRealSaveFile()
        {
            var meta = new MetaState { EndlessV2 = new EndlessSaveState() };
            meta.EndlessV2.CarriedOpenings.Add(new OpeningEffect
            {
                SourceCharId = "焱", Element = Element.Fire, Kind = EffectKind.DamageSingle, Value = 50,
                Shape = TargetArea.All, ShapePercent = 60, Pick = EffectPick.All, BattlesLeft = 2,
            });
            var o = Brushblade.Data.SaveSerializer.FromJson(Brushblade.Data.SaveSerializer.ToJson(meta)).EndlessV2.CarriedOpenings.Single();
            Assert.That(o.Shape, Is.EqualTo(TargetArea.All));
            Assert.That(o.ShapePercent, Is.EqualTo(60));
            Assert.That(o.Pick, Is.EqualTo(EffectPick.All));

            var legacy = Brushblade.Data.SaveSerializer.FromJson(
                "{\"EndlessV2\":{\"CarriedOpenings\":[{\"Kind\":2,\"Value\":2,\"BattlesLeft\":1}]}}").EndlessV2.CarriedOpenings.Single();
            Assert.That(legacy.Pick, Is.EqualTo(EffectPick.Primary), "老存档缺字段 = 现状");
            Assert.That(legacy.Shape, Is.EqualTo(TargetArea.Single));
            Assert.That(legacy.ToEffect().ShapePercent, Is.EqualTo(100));
        }

        [TestCase(@"{""kind"":""Weaken"",""value"":20,""turns"":2,""pick"":""All"",""openingBattles"":1}")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":100,""shape"":""All"",""openingBattles"":1}")]
        public void ConfigLoader_OpeningWithPickOrShapeAll_Loads(string effect)
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(@"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv8"",""face"":""Feature"",""name"":""炎"",""effects"":[" + effect + "]}]}]}");
            Assert.That(g.Get("甲").Traits[0].Effects[0].OpeningBattles, Is.EqualTo(1));
        }

        [Test]
        public void ConfigLoader_OpeningWithOnlyIf_Throws()
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""甲"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv8"",""face"":""Feature"",""name"":""错"",""effects"":[{""kind"":""Weaken"",""value"":20,""turns"":2,""pick"":""All"",""onlyIf"":""Burning"",""openingBattles"":1}]}]}]}"));
        }

        [Test]
        public void Opening_SameFaceAmplifyAll_LoadsAndOpeningIsNotAmplified()
        {
            // Ruling 5(spec §5.2 第 5 律「跨场只存不长」):开局效果不挂 AmpTerms —— 同面有 scope All(含 Burn,G3)也照常加载,
            // 登记的值与下一场开局的火力都不吃本场的加成;本场执行的本体灼照常被放大
            var g = Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""炎"",""element"":""Fire""," + FireBody + @",""traits"":[
                  {""slot"":""Lv4"",""form"":""Passive"",""name"":""精"",""effects"":[{""kind"":""Amplify"",""value"":100,""scope"":""All""}]},
                  {""slot"":""Lv8"",""face"":""Feature"",""name"":""炎"",""effects"":[{""kind"":""BurnAll"",""value"":2,""openingBattles"":1}]}]}]}");
            var src = g.Get("炎");
            var folded = TraitRules.CastEffects(src, CardFace.Feature, 8);
            Assert.That(folded[0].AmpTerms.Count, Is.EqualTo(1), "本体 BurnAll 照常挂加成");
            Assert.That(folded.Single(e => e.OpeningBattles > 0).AmpTerms.Count, Is.EqualTo(0), "开局效果不挂加成");

            var killer = new CharDef("杀", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100000) });
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 2).Select(_ => new[] { RebalanceFixture.Mob(hp: 5000) }).ToArray(),
                RewardPool = new[] { "杀" },
            };
            var run = new RunEngine(RebalanceFixture.Graph(src, killer), config, Config,
                new[] { "炎", "杀", "杀", "杀", "杀" }, Array.Empty<string>(), seed: 1,
                cardLevels: new Dictionary<string, int> { ["炎"] = 8 });
            Assert.That(run.Battle.Cast("炎", -1), Is.EqualTo(BattleError.None));
            int lv8 = MetaRules.CardLevelPercent(8);
            Assert.That(run.Battle.Enemies[0].Statuses.Find(StatusKind.Burn).Potency, Is.EqualTo(lv8 * 2), "本场本体灼:火力 ×(100+100)%");
            Assert.That(run.Battle.PendingOpenings.Single().Value, Is.EqualTo(2), "登记的是未放大的 2 层");
            Win(run);
            var opened = run.Battle.Enemies[0].Statuses.Find(StatusKind.Burn);
            Assert.That(opened.Magnitude, Is.EqualTo(2));
            Assert.That(opened.Potency, Is.EqualTo(lv8), "下一场开局的灼不吃本场的 Amplify(只存不长)");
        }
    }
}
