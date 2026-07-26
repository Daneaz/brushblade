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

        // 真身与伪装每次遭遇都随机(2026-07-26);配置里的 element 对通假字不作数
        private static EnemyDef TongJia() => new("通假字", Element.Wood, 20, 3, EnemyAbility.Disguise);

        private static EnemyDef ShengPi() => new("生僻字", Element.Earth, 24, 2, EnemyAbility.Obscure);

        private static BattleEngine Engine(EnemyDef enemy, int seed = 1) =>
            new(Graph(), new BattleConfig(), Array.Empty<string>(),
                new[] { "火", "火", "火" }, new[] { enemy }, seed);

        private static readonly Element[] FiveElements =
            { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };

        // ---- 通假字 ----

        [Test]
        public void Disguise_FakeDiffersFromTrue_BothInFiveElements()
        {
            for (int seed = 0; seed < 40; seed++) // 遍历种子:任何一次都不能露馅或滚出「心」
            {
                var enemy = Engine(TongJia(), seed).Enemies[0];
                Assert.That(enemy.ApparentElement, Is.Not.EqualTo(enemy.Element),
                    $"seed {seed}:伪装与真身撞车,伪装就失去意义");
                Assert.That(FiveElements, Has.Member(enemy.Element));
                Assert.That(FiveElements, Has.Member(enemy.ApparentElement.Value)); // 心不参与生克,不可当真身/伪装
            }
        }

        [Test]
        public void Disguise_TrueElementVariesAcrossSeeds() // 真身不能是伪随机的常数
        {
            var seen = new System.Collections.Generic.HashSet<Element>();
            for (int seed = 0; seed < 40; seed++) seen.Add(Engine(TongJia(), seed).Enemies[0].Element);
            Assert.That(seen.Count, Is.GreaterThan(1));
        }

        [Test]
        public void Disguise_FakeElementVariesAcrossSeeds()
        {
            var seen = new System.Collections.Generic.HashSet<Element?>();
            for (int seed = 0; seed < 40; seed++) seen.Add(Engine(TongJia(), seed).Enemies[0].ApparentElement);
            Assert.That(seen.Count, Is.GreaterThan(1));
        }

        [Test]
        public void Disguise_SameSeedIsReproducible() // 带种子的 RNG:同种子同结果
        {
            var a = Engine(TongJia(), 7).Enemies[0];
            var b = Engine(TongJia(), 7).Enemies[0];
            Assert.That(a.Element, Is.EqualTo(b.Element));
            Assert.That(a.ApparentElement, Is.EqualTo(b.ApparentElement));
        }

        [Test]
        public void Disguise_DamageUsesTrueElement()
        {
            var engine = Engine(TongJia());
            var enemy = engine.Enemies[0];
            int expected = (int)System.Math.Floor(10 * WuxingResolver.KeMultiplier(Element.Fire, enemy.Element));
            engine.Cast("火", 0); // 走真身结算,不看伪装
            Assert.That(enemy.Hp, Is.EqualTo(20 - expected));
        }

        [Test]
        public void Disguise_RevealsAfterFirstAction()
        {
            var engine = Engine(TongJia());
            var trueElement = engine.Enemies[0].Element;
            engine.EndTurn(); // 它行动了 → 现形
            Assert.That(engine.Enemies[0].ApparentElement, Is.EqualTo(trueElement));
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
        public void LoadCampaign_DisguiseNeedsNoElement() // 属性改为运行时随机,配置不再需要 disguiseElement
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = Brushblade.Data.ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""通假字"", ""element"": ""Wood"", ""maxHp"": 20, ""attack"": 3, ""ability"": ""Disguise"" }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""词渊"",
                    ""stages"": [ { ""encounters"": [ [ ""通假字"" ] ] } ], ""rewardPool"": [] } ]
            }", graph);
            var enemy = campaign.Chapters[0].Stages[0].Encounters[0][0];
            Assert.That(enemy.Ability, Is.EqualTo(EnemyAbility.Disguise));
        }
    }
}
