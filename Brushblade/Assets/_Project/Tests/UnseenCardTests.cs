using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>新字标记(2026-09-03,卡组页「新字」角旗/红点/计数的唯一依据)。
    ///
    /// 「新」的定义是**首次获得且还没在卡组页点开看过**,不是「最近获得」——
    /// 所以它必须落在存档里:关掉游戏再进来,没看过的那几张仍该是新的。</summary>
    public sealed class UnseenCardTests
    {
        [Test]
        public void AcquireCard_FirstTime_MarksUnseen()
        {
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            Assert.That(MetaRules.IsCardUnseen(meta, "炎"), Is.True);
        }

        [Test]
        public void AcquireCard_RepeatCopy_DoesNotRemarkASeenCard()
        {
            // 重复卡不该把已看过的字重新点亮:那样每开一次箱,整页老字都会挂上新旗
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.MarkCardSeen(meta, "炎");
            MetaRules.AcquireCard(meta, "炎");
            Assert.That(MetaRules.IsCardUnseen(meta, "炎"), Is.False);
            Assert.That(meta.CardCopies["炎"], Is.EqualTo(1));
        }

        [Test]
        public void MarkCardSeen_IsIdempotentAndOnlyTouchesThatCard()
        {
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.AcquireCard(meta, "冷");
            MetaRules.MarkCardSeen(meta, "炎");
            MetaRules.MarkCardSeen(meta, "炎");
            Assert.That(MetaRules.IsCardUnseen(meta, "炎"), Is.False);
            Assert.That(MetaRules.IsCardUnseen(meta, "冷"), Is.True);
            Assert.That(meta.UnseenCards.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnseenCount_CountsOnlyUnseen()
        {
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.AcquireCard(meta, "冷");
            MetaRules.AcquireCard(meta, "碎");
            MetaRules.MarkCardSeen(meta, "冷");
            Assert.That(MetaRules.UnseenCount(meta), Is.EqualTo(2));
        }

        [Test]
        public void EnsureStartingCollection_GrantsMissingCardsButNoneAreNew()
        {
            // 起手 15 张不是「开出来的」——一进游戏 15 面红旗在呼吸,新字这个信号就废了
            var meta = new MetaState();
            MetaRules.EnsureStartingCollection(meta);
            Assert.That(meta.OwnedCards.Count, Is.EqualTo(MetaRules.StartingCollection.Count));
            Assert.That(MetaRules.UnseenCount(meta), Is.EqualTo(0));
        }

        [Test]
        public void EnsureStartingCollection_DoesNotTurnExistingCardsIntoCopies()
        {
            // 每次启动都会跑一遍:已有的字不该被当成重复卡入账
            var meta = new MetaState();
            MetaRules.EnsureStartingCollection(meta);
            MetaRules.EnsureStartingCollection(meta);
            Assert.That(meta.CardCopies.Count, Is.EqualTo(0));
        }

        [Test]
        public void EnsureStartingCollection_LeavesAlreadyUnseenChestDropsAlone()
        {
            // 起手补齐只管它自己那 15 张:别的路径标出来的新字不该被顺手清掉
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.EnsureStartingCollection(meta);
            Assert.That(MetaRules.IsCardUnseen(meta, "炎"), Is.True);
        }

        [Test]
        public void PruneUnknownCards_DropsUnseenEntriesForRemovedChars()
        {
            var graph = new RecipeGraph(new[] { new CharDef("炎", Element.Fire) });
            var meta = new MetaState();
            MetaRules.AcquireCard(meta, "炎");
            MetaRules.AcquireCard(meta, "灯"); // 字表裁剪后不存在
            MetaRules.PruneUnknownCards(meta, graph);
            Assert.That(meta.UnseenCards.Contains("灯"), Is.False, "已下架的字不该还挂着新字红点");
            Assert.That(meta.UnseenCards.Contains("炎"), Is.True);
        }
    }
}
