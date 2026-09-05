using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary><see cref="CampaignConfig.Scale"/> 是 <see cref="EnemyDef"/> 的**重建点**:
    /// 它按深度缩放血/攻/甲,其余字段原样抄过去。抄漏一个不会编译错、不会抛异常 ——
    /// 那个字段只是静默回落成构造函数的默认值,而**每一只**上场的敌人都过这里
    /// (EndlessGenerator.BuildFloor 逐只 Scale),所以漏一个就是全场失效。
    ///
    /// 2026-09-05 rowSpan 就这么丢过一次:字表与 ConfigLoader 都配好了跨排 Boss,
    /// 缩放之后 RowSpan 回落成 1,Boss 在屏上只占前排两格。当时 EnemySlotTests 那 8 条
    /// 跨排测试全绿 —— 因为它们直接构造 EnemyDef,没有一条走过 Scale。
    /// 本文件就是补上那条缺口:**非数值字段逐个断言**,下次再加字段时这条会替你记着。</summary>
    public class CampaignScaleTests
    {
        /// <summary>一只把每个非数值字段都设成**非默认值**的怪 —— 全设默认值的话
        /// 「抄漏了」与「抄对了」给出同一个结果,测试就成了摆设。</summary>
        private static EnemyDef Exotic() => new(
            "样怪", Element.Water, 100, 20,
            EnemyAbility.Scorch,
            new List<BossPhaseDef>
            {
                new("甲", Element.Fire, 60, 10, BossSkill.Impale, defense: 8),
                new("乙", Element.Metal, 70, 12, BossSkill.Topple),
            },
            defense: 15, speed: 130,
            row: EnemyRow.Back, range: AttackRange.Ranged, focus: AttackFocus.Player,
            columnSpan: 2, minDepth: 7, rowSpan: Targeting.RowSpanBoth);

        [Test]
        public void Scale_KeepsEveryNonNumericField()
        {
            var source = Exotic();
            var scaled = CampaignConfig.Scale(source, 2.5f);

            Assert.That(scaled.Id, Is.EqualTo(source.Id));
            Assert.That(scaled.Element, Is.EqualTo(source.Element));
            Assert.That(scaled.Ability, Is.EqualTo(source.Ability));
            Assert.That(scaled.Speed, Is.EqualTo(source.Speed), "速度不吃深度缩放");
            Assert.That(scaled.Row, Is.EqualTo(source.Row));
            Assert.That(scaled.Range, Is.EqualTo(source.Range));
            Assert.That(scaled.Focus, Is.EqualTo(source.Focus));
            Assert.That(scaled.ColumnSpan, Is.EqualTo(source.ColumnSpan));
            Assert.That(scaled.RowSpan, Is.EqualTo(source.RowSpan), "跨排:2026-09-05 就是这一个丢过");
            Assert.That(scaled.MinDepth, Is.EqualTo(source.MinDepth));
            Assert.That(scaled.Phases.Count, Is.EqualTo(source.Phases.Count));
            for (int i = 0; i < source.Phases.Count; i++)
            {
                Assert.That(scaled.Phases[i].Char, Is.EqualTo(source.Phases[i].Char));
                Assert.That(scaled.Phases[i].Element, Is.EqualTo(source.Phases[i].Element));
                Assert.That(scaled.Phases[i].Skill, Is.EqualTo(source.Phases[i].Skill));
            }
        }

        /// <summary>该缩的确实缩了 —— 否则上面那条「原样抄」可以靠「整个函数返回入参」通过。</summary>
        [Test]
        public void Scale_StillScalesTheNumbers()
        {
            var scaled = CampaignConfig.Scale(Exotic(), 2f);
            Assert.That(scaled.MaxHp, Is.GreaterThan(100));
            Assert.That(scaled.Attack, Is.GreaterThan(20));
            Assert.That(scaled.Defense, Is.GreaterThan(15), "护甲半速缩放,但仍要涨");
            Assert.That(scaled.Phases[0].MaxHp, Is.GreaterThan(60), "阶段的数也要跟着缩");
        }

        /// <summary>真实数据上的端到端断言:成语 Boss 经 BuildFloor(内部会 Scale)之后
        /// 仍是跨排的。这一条盯的是「配置 → 生成 → 上场」整条链,不是单个函数。</summary>
        [Test]
        public void BossFromGenerator_IsStillCrossRow()
        {
            var config = new EndlessConfig
            {
                Bands = new[]
                {
                    new BandDef
                    {
                        Name = "字林", FromDepth = 1,
                        EnemyPool = new[] { new EnemyDef("卒", Element.Wood, 10, 2) },
                        BossPool = new EnemyDef[0],   // 空池:BuildFloor 会数它的 Count,null 会炸
                        IdiomBossPool = new[]
                        {
                            new IdiomBossDef
                            {
                                Chars = "排山倒海",
                                Elements = new[] { Element.Water, Element.Earth, Element.Metal, Element.Fire },
                            },
                        },
                        RewardPool = new[] { "灼" },
                    },
                },
            };
            var floor = EndlessGenerator.BuildFloor(config, 5, new GameRandom(1));
            Assert.That(floor[0].Phases.Count, Is.EqualTo(4), "夹具前提:抽到的是成语 Boss");
            Assert.That(floor[0].RowSpan, Is.EqualTo(Targeting.RowSpanBoth),
                "生成出来的 Boss 必须仍跨两排 —— 中间那道 Scale 不能把它抹平");
            Assert.That(floor[0].ColumnSpan, Is.EqualTo(Targeting.BossColumnSpan));
        }
    }
}
