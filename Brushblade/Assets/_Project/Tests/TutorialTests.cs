using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新手引导步骤机(11.2):首局剧本节拍 拆→合→出→三选一(演示字见 Tutorial.DemoChar)。
    /// 2026-07-19「只能合已收集的字」后,升阶字合不出来,首局改为用手上的字演示拆/合/出。</summary>
    public class TutorialTests
    {
        [Test]
        public void StartsAtDismantleFlame()
        {
            Assert.That(new Tutorial().Step, Is.EqualTo(TutorialStep.DismantleDemo));
        }

        [Test]
        public void WrongAction_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Cast, Tutorial.DemoChar);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleDemo));
        }

        [Test]
        public void WrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "木");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleDemo));
        }

        [Test]
        public void DismantleFlame_AdvancesToRecomposeFlame()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeDemo));
        }

        [Test]
        public void RecomposeFlame_AdvancesToCastFlame()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Compose, Tutorial.DemoChar);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.CastDemo));
        }

        [Test]
        public void FullSequence_ReachesDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Compose, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Cast, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.PickReward);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
            Assert.That(tutorial.Done, Is.True);
        }

        [Test]
        public void ComposeWrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Compose, "林");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeDemo));
        }

        [Test]
        public void EndTurnMidway_DoesNotDerail() // AP 用尽结束回合不该打断节拍
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.EndTurn);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeDemo));
        }

        /// <summary>首局剧本必须真能打通(否则新手卡死):字库起始【剑】+ 池 佥·刂,
        /// 3 AP 走完 拆→合→出。实船数值守卫见 ConfigLoaderTests。
        /// 2026-08-05 演示字由【炎】改为【剑】(初始收集改为五系白/绿/蓝,炎不再在手):
        /// 剑是金系 13 伤,金克木 ×1.5 = 19,一击斩掉 14 血的木系错字鬼,不必等灼烧补刀。
        /// 掉字改造(2026-08-04):UnlockedChars=[剑] 会在构造函数的 StartTurn() 里触发开局
        /// 掉落,实际库是两张【剑】(起始 1 张 + 开局掉 1 张)——教程脚本的拆/合/出三步只碰
        /// 其中一张,不影响流程,但下面钉一条断言把这处隐藏状态标出来。</summary>
        [Test]
        public void FirstTowerScript_IsActuallyCompletable()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("佥", null),
                new CharDef("刂", null),
                new CharDef("剑", Element.Metal, new[] { "佥", "刂" }, rarity: CardRarity.Blue,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 13) }),
            });
            var config = new BattleConfig
            {
                DropTable = new[] { "佥" },
                UnlockedChars = new[] { "剑" },       // 只收集了剑——升阶字合不出来
            };
            var battle = new BattleEngine(graph, config, new[] { "剑" }, new[] { "佥", "刂" },
                new[] { new EnemyDef("错字鬼", Element.Wood, 14, 4) }, seed: 7);

            // 开局掉落已把库从 1 张剑变成 2 张(2026-08-04):钉住,别让后人以为库里只有 1 张
            Assert.That(battle.Library, Is.EqualTo(new[] { "剑", "剑" }));

            Assert.That(battle.Dismantle("剑"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Compose("剑"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Cast("剑"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won)); // 金克木:13×1.5 = 19 ≥ 14
        }

        [Test]
        public void NotifyAfterDone_StaysDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Compose, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.Cast, Tutorial.DemoChar);
            tutorial.Notify(TutorialAction.PickReward);
            tutorial.Notify(TutorialAction.Cast, Tutorial.DemoChar);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
        }
    }
}
