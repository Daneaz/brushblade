using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新手引导步骤机(11.2):首局剧本节拍 拆灯→合林→合焚→结束回合→出焚→三选一。
    /// v0.7 出字即消耗(无回归),连招一战内闭环。</summary>
    public class TutorialTests
    {
        [Test]
        public void StartsAtDismantleLamp()
        {
            Assert.That(new Tutorial().Step, Is.EqualTo(TutorialStep.DismantleLamp));
        }

        [Test]
        public void WrongAction_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Cast, "灯");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleLamp));
        }

        [Test]
        public void WrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "木");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.DismantleLamp));
        }

        [Test]
        public void DismantleLamp_AdvancesToComposeForest()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "灯");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.ComposeForest));
        }

        [Test]
        public void EndTurnStep_IgnoresCharId()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "灯");
            tutorial.Notify(TutorialAction.Compose, "林");
            tutorial.Notify(TutorialAction.Compose, "焚");
            tutorial.Notify(TutorialAction.EndTurn);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.CastBurn));
        }

        [Test]
        public void FullSequence_ReachesDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "灯");
            tutorial.Notify(TutorialAction.Compose, "林");
            tutorial.Notify(TutorialAction.Compose, "焚");
            tutorial.Notify(TutorialAction.EndTurn);
            tutorial.Notify(TutorialAction.Cast, "焚");
            tutorial.Notify(TutorialAction.PickReward);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
            Assert.That(tutorial.Done, Is.True);
        }

        [Test]
        public void ComposeWrongChar_DoesNotAdvance()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "灯");
            tutorial.Notify(TutorialAction.Compose, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.ComposeForest));
        }

        [Test]
        public void NotifyAfterDone_StaysDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "灯");
            tutorial.Notify(TutorialAction.Compose, "林");
            tutorial.Notify(TutorialAction.Compose, "焚");
            tutorial.Notify(TutorialAction.EndTurn);
            tutorial.Notify(TutorialAction.Cast, "焚");
            tutorial.Notify(TutorialAction.PickReward);
            tutorial.Notify(TutorialAction.Cast, "灯");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
        }
    }
}
