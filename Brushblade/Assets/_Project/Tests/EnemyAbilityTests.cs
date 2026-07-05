using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>字怪特殊能力(8.3):缺笔妖自补全、叠字怪受击分裂。</summary>
    public class EnemyAbilityTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("烧", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 5) }),
        });

        private static EnemyDef Regrower(int hp = 30) =>
            new("缺笔妖", Element.Metal, hp, 2, EnemyAbility.Regrow);

        private static EnemyDef Splitter(int hp = 16) =>
            new("叠字怪", Element.Wood, hp, 5, EnemyAbility.Split);

        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig(), new[] { "烧" }, new[] { "火", "火", "火" },
                enemies, seed: 1);

        // ---- 缺笔妖:自补全 ----

        [Test]
        public void Regrow_GainsAttackAndHeals_EachEnemyTurn()
        {
            var engine = Engine(Regrower(hp: 30));
            engine.Cast("火", 0);  // 破点血:30−4=26(金被火克 ×1.5 → 6;30−6=24)
            int hpAfterHit = engine.Enemies[0].Hp;
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(2 + 2));          // 攻 +2
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpAfterHit + 3));     // 回 3 血
            Assert.That(engine.Enemies[0].RegrowProgress, Is.EqualTo(1));
        }

        [Test]
        public void Regrow_HealCapsAtMaxHp()
        {
            var engine = Engine(Regrower(hp: 30)); // 未受伤
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(30));
        }

        [Test]
        public void Regrow_ThirdTurn_Completes_DoubleAttackFullHeal()
        {
            var engine = Engine(Regrower(hp: 30));
            engine.Cast("火", 0); // 掉 6 血
            engine.EndTurn();     // 进度 1,攻 4
            engine.EndTurn();     // 进度 2,攻 6
            engine.EndTurn();     // 进度 3:补全完成

            var enemy = engine.Enemies[0];
            Assert.That(enemy.RegrowProgress, Is.EqualTo(3));
            Assert.That(enemy.Attack, Is.EqualTo((2 + 2 * 3) * 2)); // (基础2+2×3)×2 = 16
            Assert.That(enemy.Hp, Is.EqualTo(30));                  // 血回满

            engine.EndTurn(); // 完成后不再成长
            Assert.That(enemy.Attack, Is.EqualTo(16));
        }

        // ---- 叠字怪:受击分裂 ----

        [Test]
        public void Split_FirstDamageSurvived_SpawnsCloneHalfHp()
        {
            var engine = Engine(Splitter(hp: 16));
            engine.Cast("火", 0); // 火 vs 木 1.0 → 4 伤 → 12 血,分裂

            Assert.That(engine.Enemies.Count, Is.EqualTo(2));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(6));  // ceil(12/2)
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(6));
            Assert.That(engine.Enemies[1].Def.Id, Is.EqualTo("叠字怪"));
            Assert.That(engine.Enemies.All(e => e.HasSplit), Is.True);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemySplit), Is.True);
        }

        [Test]
        public void Split_OnlyOnce()
        {
            var engine = Engine(Splitter(hp: 16));
            engine.Cast("火", 0);            // 分裂 → 两只 6 血
            engine.EndTurn();
            engine.Cast("火", 0);            // 再打不再分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(2));
        }

        [Test]
        public void Split_NotWhenKilled()
        {
            var engine = Engine(Splitter(hp: 4));
            engine.Cast("火", 0); // 4 伤致死
            Assert.That(engine.Enemies.Count, Is.EqualTo(1));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Split_CappedAtFourEnemies()
        {
            var engine = Engine(Splitter(), Splitter(), Splitter(), Splitter());
            engine.Cast("烧"); // AOE 打全场:已 4 只,不再分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(4));
        }

        // ---- 配置解析 ----

        [Test]
        public void LoadCampaign_ParsesAbility_DefaultsNone()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""叠字怪"", ""element"": ""Wood"", ""maxHp"": 16, ""attack"": 5, ""ability"": ""Split"" },
                    { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 10, ""attack"": 3 }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""蒙学"",
                    ""stages"": [ { ""encounters"": [ [ ""叠字怪"", ""错字鬼"" ] ] } ], ""rewardPool"": [] } ]
            }", graph);
            var encounter = campaign.Chapters[0].Stages[0].Encounters[0];
            Assert.That(encounter[0].Ability, Is.EqualTo(EnemyAbility.Split));
            Assert.That(encounter[1].Ability, Is.EqualTo(EnemyAbility.None));
        }

        [Test]
        public void LoadCampaign_UnknownAbility_Throws()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(@"{
                ""enemies"": [ { ""id"": ""謎"", ""element"": ""Wood"", ""maxHp"": 1, ""attack"": 1, ""ability"": ""Fly"" } ],
                ""dropTable"": [], ""chapters"": [ { ""name"": ""x"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph));
        }
    }
}
