using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新手引导步骤机(11.2):首局剧本节拍 拆炎→合炎→出炎→三选一。
    /// 2026-07-19「只能合已收集的字」后,升阶字合不出来,首局改为用【炎】演示拆/合/出。</summary>
    public class TutorialTests
    {
        [Test]
        public void StartsAtDismantleFlame()
        {
            Assert.That(new Tutorial().Step, Is.EqualTo(TutorialStep.DismantleFlame));
        }

        [Test]
        public void WrongAction_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Cast, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleFlame));
        }

        [Test]
        public void WrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "木");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleFlame));
        }

        [Test]
        public void DismantleFlame_AdvancesToRecomposeFlame()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeFlame));
        }

        [Test]
        public void RecomposeFlame_AdvancesToCastFlame()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.CastFlame));
        }

        [Test]
        public void FullSequence_ReachesDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            tutorial.Notify(TutorialAction.Cast, "炎");
            tutorial.Notify(TutorialAction.PickReward);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
            Assert.That(tutorial.Done, Is.True);
        }

        [Test]
        public void ComposeWrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "林");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeFlame));
        }

        [Test]
        public void EndTurnMidway_DoesNotDerail() // AP 用尽结束回合不该打断节拍
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.EndTurn);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.RecomposeFlame));
        }

        /// <summary>首局剧本必须真能打通(否则新手卡死):字库起始【炎】+ 池两个火,
        /// 3 AP 走完 拆→合→出,靠回合末灼烧补刀取胜。实船数值守卫见 ConfigLoaderTests。
        /// 掉字改造(2026-08-04):UnlockedChars=[炎] 会在构造函数的 StartTurn() 里触发开局
        /// 掉落,实际库是两张【炎】(起始 1 张 + 开局掉 1 张)——教程脚本的拆/合/出三步只碰
        /// 其中一张,不影响流程,但下面钉一条断言把这处隐藏状态标出来。</summary>
        [Test]
        public void FirstTowerScript_IsActuallyCompletable()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("火", Element.Fire),
                new CharDef("炎", Element.Fire, new[] { "火", "火" }, effects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 12),
                    new EffectDef(EffectKind.BurnSingle, 2),
                }),
            });
            var config = new BattleConfig
            {
                DropTable = new[] { "火" },
                UnlockedChars = new[] { "炎" },       // 只收集了炎——升阶字合不出来
            };
            var battle = new BattleEngine(graph, config, new[] { "炎" }, new[] { "火", "火" },
                new[] { new EnemyDef("错字鬼", Element.Wood, 14, 4) }, seed: 7);

            // 开局掉落已把库从 1 张炎变成 2 张(2026-08-04):钉住,别让后人以为库里只有 1 张
            Assert.That(battle.Library, Is.EqualTo(new[] { "炎", "炎" }));

            Assert.That(battle.Dismantle("炎"), Is.EqualTo(BattleError.None)); // 1 AP
            Assert.That(battle.Compose("炎"), Is.EqualTo(BattleError.None));   // 2 AP
            Assert.That(battle.Cast("炎"), Is.EqualTo(BattleError.None));      // 3 AP
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn));     // 12 伤打不死 14 血
            battle.EndTurn();                                                  // 灼烧补刀先于敌人行动
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void NotifyAfterDone_StaysDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            tutorial.Notify(TutorialAction.Cast, "炎");
            tutorial.Notify(TutorialAction.PickReward);
            tutorial.Notify(TutorialAction.Cast, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
        }
    }
}
