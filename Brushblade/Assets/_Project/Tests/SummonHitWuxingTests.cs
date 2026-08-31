using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>敌人打召唤物那一记也走生克,事件也带标记(2026-08-31)。
    ///
    /// <see cref="BattleEngine"/> 的 DamageSummon 里本来就有 ResolveEffect(攻方, 召唤物属性),
    /// 只是标记一直没往事件上带 —— 于是玩家看着自己的召唤物被某只怪一口咬掉半管血,
    /// 却不知道是属性被压制,还以为那只怪就是这么强。
    ///
    /// 为什么由 Core 标而不是表现层自己算:表现层拿得到敌人属性和召唤物属性,推一遍生克
    /// 也能出结果 —— 但那就是**规则的第二个来源**,与 EnemyDef 里那条注释说的
    /// 「表现层不该自己再推一遍规则,那正是两处口径分叉的起点」是同一件事。
    ///
    /// EnemyAttack(敌人打玩家)刻意**不**在此列:玩家没有五行属性,那条路径根本不过生克
    /// (DamagePlayerDirect 收的是算好的 enemy.Attack)。</summary>
    public sealed class SummonHitWuxingTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 召唤字:召唤物的属性继承**这张牌**,不是 summonChar 的字表属性。
            // value = 召唤物血量,给厚一点撑住十几回合;summonAttack = 0 免得它反过来把敌人打死
            new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 400,
                summonCount: 1, summonAttack: 0, summonChar: "木") }),
            new CharDef("乙", Element.Metal, effects: new[] { new EffectDef(EffectKind.Summon, 400,
                summonCount: 1, summonAttack: 0, summonChar: "木") }),
            new CharDef("丙", Element.Heart, effects: new[] { new EffectDef(EffectKind.Summon, 400,
                summonCount: 1, summonAttack: 0, summonChar: "木") }),
        });

        /// <summary>一只只会普攻的怪,属性可调。血厚到打不死,免得召唤物的反伤提前结束战斗。</summary>
        private static EnemyDef Mob(Element element, int attack = 20) =>
            new("怔", element, 9999, attack);

        private static BattleEngine Battle(Element enemyElement, string summonChar) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 500 },
                new[] { summonChar }, Array.Empty<string>(),
                new[] { Mob(enemyElement) }, seed: 1);

        /// <summary>召唤一只,然后一直 EndTurn 到敌人打中它为止,返回那条 SummonHit。</summary>
        private static BattleEvent FirstSummonHit(BattleEngine engine, string summonChar)
        {
            Assert.That(engine.Cast(summonChar, 0), Is.EqualTo(BattleError.None));
            for (int turn = 0; turn < 12 && engine.Phase == BattlePhase.PlayerTurn; turn++)
            {
                engine.EndTurn();
                foreach (var e in engine.LastEvents)
                    if (e.Kind == BattleEventKind.SummonHit) return e;
            }
            Assert.Fail("十二个回合里敌人一次都没打到召唤物");
            return default;
        }

        [Test]
        public void MetalEnemyHitsWoodSummon_MarksKe()
        {
            // 金克木:敌人占便宜,那一记该标 Ke
            var hit = FirstSummonHit(Battle(Element.Metal, "甲"), "甲");
            Assert.That(hit.Ke, Is.True, "金克木");
            Assert.That(hit.Countered, Is.False);
        }

        [Test]
        public void WoodEnemyHitsMetalSummon_MarksCountered()
        {
            // 反过来:木打金是 0.5x,敌人这一记打软了
            var hit = FirstSummonHit(Battle(Element.Wood, "乙"), "乙");
            Assert.That(hit.Countered, Is.True, "木被金克");
            Assert.That(hit.Ke, Is.False);
        }

        [Test]
        public void HeartSummon_NeverMarksEither()
        {
            // 心系中立:与所有属性 1.0x,两个标记都不亮
            foreach (var element in new[] { Element.Wood, Element.Fire, Element.Earth,
                Element.Metal, Element.Water, Element.Heart })
            {
                var hit = FirstSummonHit(Battle(element, "丙"), "丙");
                Assert.That(hit.Ke, Is.False, $"{element} 对心中立");
                Assert.That(hit.Countered, Is.False, $"{element} 对心中立");
            }
        }

        [Test]
        public void KeAndCountered_AreNeverBothTrue()
        {
            // 与直接伤害那边同一条不变式:同源于一个倍率,不可能同时成立
            foreach (var element in new[] { Element.Wood, Element.Fire, Element.Earth,
                Element.Metal, Element.Water, Element.Heart })
                foreach (var summonChar in new[] { "甲", "乙", "丙" })
                {
                    var hit = FirstSummonHit(Battle(element, summonChar), summonChar);
                    Assert.That(hit.Ke && hit.Countered, Is.False,
                        $"{element} 打 {summonChar} 召出来的召唤物:两个标记同时为真");
                }
        }

        [Test]
        public void MarkMatchesTheDamageActuallyDealt()
        {
            // 标记不能与数值脱节:标了 Ke 的那一记,打出来就该是 1.5 倍那一档。
            // 攻 20、召唤物无护甲无盾 —— 金打木 30,木打金 10,中立 20
            var ke = FirstSummonHit(Battle(Element.Metal, "甲"), "甲");
            Assert.That(ke.Amount, Is.EqualTo(30), "金克木 20 × 1.5");

            var countered = FirstSummonHit(Battle(Element.Wood, "乙"), "乙");
            Assert.That(countered.Amount, Is.EqualTo(10), "木被金克 20 × 0.5");

            var neutral = FirstSummonHit(Battle(Element.Water, "丙"), "丙");
            Assert.That(neutral.Amount, Is.EqualTo(20), "水对心 1.0x");
        }
    }
}
