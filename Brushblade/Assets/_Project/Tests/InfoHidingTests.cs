using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>信息隐藏机制(8.3):通假字伪装、生僻字属性隐藏。结算永远用真实属性。</summary>
    public class InfoHidingTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
        });

        // 真身木,伪装成金(骗玩家用火打:期待 ×1.5 实得 ×1.0)
        private static EnemyDef TongJia() => new("通假字", Element.Wood, 20, 3,
            EnemyAbility.Disguise, disguiseElement: Element.Metal);

        private static EnemyDef ShengPi() => new("生僻字", Element.Earth, 24, 2, EnemyAbility.Obscure);

        private static BattleEngine Engine(EnemyDef enemy) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(),
                new[] { "火", "火", "火" }, new[] { enemy }, seed: 1);

        // ---- 通假字 ----

        [Test]
        public void Disguise_ShowsFakeElement_Initially()
        {
            var enemy = Engine(TongJia()).Enemies[0];
            Assert.That(enemy.ApparentElement, Is.EqualTo(Element.Metal)); // 看起来是金
            Assert.That(enemy.Element, Is.EqualTo(Element.Wood));          // 真身是木
        }

        [Test]
        public void Disguise_DamageUsesTrueElement()
        {
            var engine = Engine(TongJia());
            engine.Cast("火", 0); // 火 vs 真木 = 1.0 → 10(若按伪装的金算会是 15)
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(20 - 10));
        }

        [Test]
        public void Disguise_RevealsAfterFirstAction()
        {
            var engine = Engine(TongJia());
            engine.EndTurn(); // 它行动了 → 现形
            Assert.That(engine.Enemies[0].ApparentElement, Is.EqualTo(Element.Wood));
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyRevealed), Is.True);
        }

        // ---- 生僻字 ----

        [Test]
        public void Obscure_HiddenInitially()
        {
            var enemy = Engine(ShengPi()).Enemies[0];
            Assert.That(enemy.ApparentElement, Is.Null); // UI 显示 "?"
        }

        [Test]
        public void Obscure_RevealsAfterTwoHits()
        {
            var engine = Engine(ShengPi());
            engine.Cast("火", 0);
            Assert.That(engine.Enemies[0].ApparentElement, Is.Null); // 一击还没读懂
            engine.Cast("火", 0);
            Assert.That(engine.Enemies[0].ApparentElement, Is.EqualTo(Element.Earth)); // 读懂了
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyRevealed), Is.True);
        }

        [Test]
        public void Obscure_KillingBlow_DiedFollowsDamageImmediately()
        {
            // 表现层靠「EnemyDied 紧跟致死伤害」判定这记是否击杀(致死不白闪,让位给置灰)。
            // 现形事件插在中间会打断该判定 → 白闪 + 血条瞬间归零 + 置灰错拍
            var engine = new BattleEngine(Graph(), new BattleConfig(), Array.Empty<string>(),
                new[] { "火", "火" }, new[] { new EnemyDef("生僻字", Element.Earth, 20, 2, EnemyAbility.Obscure) },
                seed: 1);
            engine.Cast("火", 0);  // 第 1 击:20 → 10,未读懂
            engine.Cast("火", 0);  // 第 2 击:致死,同时满足「受击两次」的现形条件

            var kinds = engine.LastEvents.Select(e => e.Kind).ToList();
            int damage = kinds.IndexOf(BattleEventKind.Damage);
            Assert.That(kinds[damage + 1], Is.EqualTo(BattleEventKind.EnemyDied));
        }

        [Test]
        public void Obscure_KillingBlow_DoesNotReveal() // 打死了就无所谓读不读得懂
        {
            var engine = new BattleEngine(Graph(), new BattleConfig(), Array.Empty<string>(),
                new[] { "火", "火" }, new[] { new EnemyDef("生僻字", Element.Earth, 20, 2, EnemyAbility.Obscure) },
                seed: 1);
            engine.Cast("火", 0);
            engine.Cast("火", 0);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyRevealed), Is.False);
        }

        // ---- 常规怪不受影响 ----

        [Test]
        public void NormalEnemy_ApparentIsTrue()
        {
            var enemy = Engine(new EnemyDef("错字鬼", Element.Wood, 12, 4)).Enemies[0];
            Assert.That(enemy.ApparentElement, Is.EqualTo(Element.Wood));
        }

        // ---- 配置解析 ----

        [Test]
        public void LoadCampaign_ParsesDisguiseElement()
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = Brushblade.Data.ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""通假字"", ""element"": ""Wood"", ""maxHp"": 20, ""attack"": 3,
                      ""ability"": ""Disguise"", ""disguiseElement"": ""Metal"" }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""词渊"",
                    ""stages"": [ { ""encounters"": [ [ ""通假字"" ] ] } ], ""rewardPool"": [] } ]
            }", graph);
            var enemy = campaign.Chapters[0].Stages[0].Encounters[0][0];
            Assert.That(enemy.DisguiseElement, Is.EqualTo(Element.Metal));
        }

        [Test]
        public void LoadCampaign_DisguiseWithoutElement_Throws()
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<Brushblade.Data.ConfigException>(() => Brushblade.Data.ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""通假字"", ""element"": ""Wood"", ""maxHp"": 20, ""attack"": 3, ""ability"": ""Disguise"" }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""x"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph));
        }
    }
}
