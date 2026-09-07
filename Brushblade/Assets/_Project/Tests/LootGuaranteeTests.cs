using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战利品定向保底(spec §3.2):五行 L2 让 5 张候选里至少有 1 张该系。
    ///
    /// 起手每系只有 1 格,后续续航全靠战后 5 选 2 —— L1 管得到起手、管不到续航,
    /// 这一条补的就是那一段。</summary>
    public class LootGuaranteeTests
    {
        /// <summary>五系各两张的奖池:保底能不能占到坑,靠的是「该系确实有候选」。</summary>
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood, isComponent: true),
            new CharDef("火", Element.Fire, isComponent: true),
            new CharDef("金", Element.Metal, isComponent: true),
            new CharDef("水", Element.Water, isComponent: true),
            new CharDef("土", Element.Earth, isComponent: true),
            new CharDef("焚", Element.Fire, rarity: CardRarity.Green,
                recipe: new[] { "火", "木" },
                effects: new[] { new EffectDef(EffectKind.DamageAll, 40) }),
            new CharDef("炎", Element.Fire, rarity: CardRarity.Blue,
                recipe: new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.DamageAll, 60) }),
            new CharDef("林", Element.Wood, rarity: CardRarity.Green,
                recipe: new[] { "木", "木" },
                effects: new[] { new EffectDef(EffectKind.Summon, 1) }),
            new CharDef("森", Element.Wood, rarity: CardRarity.Blue,
                recipe: new[] { "木", "林" },
                effects: new[] { new EffectDef(EffectKind.Summon, 1) }),
            new CharDef("锐", Element.Metal, rarity: CardRarity.Green,
                recipe: new[] { "金", "金" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 40) }),
            new CharDef("锋", Element.Metal, rarity: CardRarity.Blue,
                recipe: new[] { "金", "锐" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 60) }),
            new CharDef("冻", Element.Water, rarity: CardRarity.Green,
                recipe: new[] { "水", "水" },
                effects: new[] { new EffectDef(EffectKind.Freeze, 1) }),
            new CharDef("海", Element.Water, rarity: CardRarity.Blue,
                recipe: new[] { "水", "冻" },
                effects: new[] { new EffectDef(EffectKind.DamageAll, 50) }),
            new CharDef("垒", Element.Earth, rarity: CardRarity.Green,
                recipe: new[] { "土", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 55) }),
            new CharDef("碎", Element.Earth, rarity: CardRarity.Blue,
                recipe: new[] { "土", "垒" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 90) }),
        });

        private static readonly string[] Pool =
            { "焚", "炎", "林", "森", "锐", "锋", "冻", "海", "垒", "碎" };

        private static EnemyDef Weak() => new("枯", Element.Wood, 4, 2);

        private static RunEngine Run(IReadOnlyList<Element> guaranteed = null,
            int rewardRolls = 1, int seed = 7)
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { Weak() } },
                RewardPool = Pool,
                GuaranteedElements = guaranteed ?? Array.Empty<Element>(),
                RewardDrawRolls = rewardRolls,
            };
            return new RunEngine(Graph(), config,
                new BattleConfig { DropTable = new[] { "木" } },
                startingLibrary: new[] { "焚" }, startingPool: Array.Empty<string>(), seed: seed);
        }

        private static List<Element> OptionElements(RunEngine run)
        {
            var graph = Graph();
            var result = new List<Element>();
            foreach (var id in run.RewardOptions) result.Add(graph.Get(id).Element.Value);
            return result;
        }

        private static RunEngine WonRun(IReadOnlyList<Element> guaranteed = null,
            int rewardRolls = 1, int seed = 7)
        {
            var run = Run(guaranteed, rewardRolls, seed);
            Assert.That(run.Battle.Cast("焚"), Is.EqualTo(BattleError.None), "夹具自检");
            run.AdvanceAfterBattle();
            return run;
        }

        /// <summary>没点任何 L2 时,候选构成与引入保底之前**逐字节相同**。
        /// 同一个种子跑两次(一次空保底、一次显式传空数组)必须完全一致。</summary>
        [Test]
        public void NoGuarantee_LeavesTheCandidateRollUnchanged()
        {
            var a = WonRun(null, 1, seed: 31);
            var b = WonRun(Array.Empty<Element>(), 1, seed: 31);
            Assert.That(a.RewardOptions.Count, Is.EqualTo(b.RewardOptions.Count));
            for (int i = 0; i < a.RewardOptions.Count; i++)
                Assert.That(a.RewardOptions[i], Is.EqualTo(b.RewardOptions[i]), $"下标 {i}");
        }

        /// <summary>点了金脉 L2:5 张候选里必有金系。多个种子都要成立 ——
        /// 单种子通过可能只是运气好。</summary>
        [Test]
        public void MetalGuarantee_PutsAtLeastOneMetalInTheOptions()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var run = WonRun(new[] { Element.Metal }, 1, seed);
                Assert.That(OptionElements(run).Contains(Element.Metal), Is.True,
                    $"seed {seed}:金脉 L2 没占到坑");
            }
        }

        /// <summary>点满五系 L2:5 张候选五系各一。</summary>
        [Test]
        public void AllFiveGuarantees_FillEverySlot()
        {
            var all = new[] { Element.Metal, Element.Wood, Element.Water,
                              Element.Fire, Element.Earth };
            for (int seed = 0; seed < 8; seed++)
            {
                var got = OptionElements(WonRun(all, 1, seed));
                foreach (var element in all)
                    Assert.That(got.Contains(element), Is.True, $"seed {seed}:缺 {element}");
            }
        }

        /// <summary>该系在奖池里一张都没有时,保底**静默让位**给自由抽 ——
        /// 不能因为凑不齐就少发候选。心属性在本夹具的奖池里一张都没有。</summary>
        [Test]
        public void MissingElement_FallsBackToAFreeDraw()
        {
            var run = WonRun(new[] { Element.Heart }, 1, seed: 5);
            Assert.That(run.RewardOptions.Count, Is.EqualTo(5),
                "凑不齐保底不该少发候选");
        }

        /// <summary>保底占的坑与自由抽的坑**不重复发同一张字** ——
        /// 定向抽走的那张要从主池同步移除。</summary>
        [Test]
        public void GuaranteedPick_IsRemovedFromTheFreePool()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var run = WonRun(new[] { Element.Fire }, 1, seed);
                var seen = new List<string>();
                foreach (var id in run.RewardOptions)
                {
                    Assert.That(seen.Contains(id), Is.False, $"seed {seed}:{id} 发了两次");
                    seen.Add(id);
                }
            }
        }

        /// <summary>慧眼 L2(rewardRolls = 2)不该把奖池抽干 —— 落选的要放回。
        /// 池里 10 张、发 5 张,抽两次仍必须发满 5 张。</summary>
        [Test]
        public void ExtraRewardRolls_DoNotDrainThePool()
        {
            for (int seed = 0; seed < 8; seed++)
                Assert.That(WonRun(null, 2, seed).RewardOptions.Count, Is.EqualTo(5),
                    $"seed {seed}:落选项没放回,候选被抽干了");
        }
    }
}
