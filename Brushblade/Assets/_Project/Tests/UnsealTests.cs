using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>解封(2026-09-16,水):被治疗的我方召唤物属性在 6 类中纯随机重掷一次,永久。
    ///
    /// 两个高危点各对应一条测试:
    /// · <see cref="Unseal_OnPlayer_IsNoOp_AndDoesNotConsumeRandom"/> 钉住「玩家槽位必须在
    ///   摇 _random 之前 return」—— 摇了不用的一次也会平移全场依赖种子的既有测试。
    /// · <see cref="SummonElement_SurvivesSnapshotRoundTrip"/> 钉住 SummonState.Element
    ///   改可写之后仍随 SummonSnapshot 正确往返(RunSnapshot.cs 那条「漏补字段是静默的」警告)。</summary>
    public class UnsealTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("兵", Element.Wood, effects: new[]
            {
                new EffectDef(EffectKind.Summon, 999, summonCount: 1, summonAttack: 0, summonChar: "木"),
            }),
            new CharDef("解", Element.Water, effects: new[] { new EffectDef(EffectKind.Unseal, 0) }),
        });

        private static EnemyDef Target() => new("靶", Element.Heart, 999999, 0);

        private static BattleEngine Engine(int seed) => new(Graph(),
            new BattleConfig { PlayerMaxHp = 999, ApPerTurn = 9 },
            new[] { "兵", "解", "解", "解", "解", "解" }, Array.Empty<string>(),
            new[] { Target() }, seed: seed);

        [Test]
        public void Unseal_RerollsSummonElement_AmongAllSixIncludingCurrent()
        {
            // 纯随机:含当前属性(木)、含 Heart(2026-09-05 用户裁定)。多种子重掷应覆盖到
            // 6 类中的多种,且 Heart 必须出现在候选里 —— 排除 Heart 是本任务明确不许做的
            // 自作主张(brief「设计要点」一节)。
            var seen = new HashSet<Element>();
            for (int seed = 0; seed < 200; seed++)
            {
                var engine = Engine(seed);
                engine.Cast("兵");              // slot 0,木系,999 血
                engine.Cast("解", allySlot: 0); // 解封
                seen.Add(engine.Summons[0].Element);
            }
            Assert.That(seen.Count, Is.GreaterThan(3),
                "6 类纯随机,200 个种子应覆盖到 4 种以上");
            Assert.That(seen.Contains(Element.Heart), Is.True, "心也在候选里,不能被自作主张排除");
        }

        [Test]
        public void Unseal_OnPlayer_IsNoOp_AndDoesNotConsumeRandom()
        {
            // 玩家没有五行属性,落到玩家槽位时空转 —— 且必须在碰 _random 之前 return:
            // 用 Capture().RandomState 钉住这一次 Cast 前后随机流状态逐位一致,
            // 摇了不用的一次也会平移后面全部依赖种子的既有测试(危点一)。
            var engine = Engine(1);
            engine.Cast("兵"); // slot 0,场上有活着的召唤物,不会被 AliveSummons()==0 那条免选自动改判
            uint before = engine.Capture().RandomState;

            Assert.DoesNotThrow(() => engine.Cast("解", allySlot: Targeting.PlayerTarget));

            uint after = engine.Capture().RandomState;
            Assert.That(after, Is.EqualTo(before),
                "玩家槽位空转分支必须写在 _random.Next 之前 return,否则会平移随机序列");
            Assert.That(engine.Summons[0].Element, Is.EqualTo(Element.Wood),
                "空转不应该动到场上任何召唤物的属性");
        }

        [Test]
        public void Unseal_IsPermanent_SurvivesTurnTick()
        {
            // 永久:直接改字段,不进 StatusBag,不随回合递减/清空。
            var engine = Engine(2);
            engine.Cast("兵");
            engine.Cast("解", allySlot: 0);
            var rerolled = engine.Summons[0].Element;

            for (int i = 0; i < 5; i++) engine.EndTurn();

            Assert.That(engine.Summons[0].Element, Is.EqualTo(rerolled),
                "解封是永久效果,走完若干回合不应该回落");
        }

        [Test]
        public void SummonElement_SurvivesSnapshotRoundTrip()
        {
            // 守卫(危点二):Element 改成 { get; internal set; } 之后,仍要随
            // SummonSnapshot 正确往返 —— SummonSnapshot.Element 本来就存在(供构造用),
            // 但 accessor 改动本身不该在这条链路上引入回归。
            var engine = Engine(3);
            engine.Cast("兵");
            engine.Cast("解", allySlot: 0);
            var rerolled = engine.Summons[0].Element;

            var snapshot = engine.Capture();
            var defs = new Dictionary<string, EnemyDef> { [Target().Id] = Target() };
            var restored = BattleEngine.Restore(snapshot, Graph(),
                new BattleConfig { PlayerMaxHp = 999, ApPerTurn = 9 }, null, defs);

            Assert.That(restored.Summons[0].Element, Is.EqualTo(rerolled),
                "召唤物属性重掷后必须随快照正确往返");
        }
    }
}
