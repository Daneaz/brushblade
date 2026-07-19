using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新手引导步骤机(11.2):首局剧本节拍 拆炎→合炎→合焱→结束回合→出焱→三选一。
    /// v0.7 出字即消耗(无回归),连招一战内闭环;首发字库仅五行叠字系。</summary>
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
        public void EndTurnStep_IgnoresCharId()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            tutorial.Notify(TutorialAction.Compose, "焱");
            tutorial.Notify(TutorialAction.EndTurn);
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.CastBlaze));
        }

        [Test]
        public void FullSequence_ReachesDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            tutorial.Notify(TutorialAction.Compose, "焱");
            tutorial.Notify(TutorialAction.EndTurn);
            tutorial.Notify(TutorialAction.Cast, "焱");
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
        public void NotifyAfterDone_StaysDone()
        {
            var tutorial = new Tutorial();
            tutorial.Notify(TutorialAction.Dismantle, "炎");
            tutorial.Notify(TutorialAction.Compose, "炎");
            tutorial.Notify(TutorialAction.Compose, "焱");
            tutorial.Notify(TutorialAction.EndTurn);
            tutorial.Notify(TutorialAction.Cast, "焱");
            tutorial.Notify(TutorialAction.PickReward);
            tutorial.Notify(TutorialAction.Cast, "炎");
            Assert.That(tutorial.Step, Is.EqualTo(TutorialStep.Done));
        }
    }
}
