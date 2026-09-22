using System;
using Brushblade.Platform;

namespace Brushblade.Presentation
{
    /// <summary>广告闸门:「看完广告才发奖」这条规则的**唯一实现处**。
    ///
    /// 六个广告位(商城墨锭/商城刷新/宝箱加速/字库扩容 ×2 路径/复活)此前都是
    /// 点击即调 Core 发奖,广告那一环根本不存在。现在一律走这里。
    ///
    /// 刻意做成一个helper 而不是在六处各写一遍回调:CLAUDE.md 反复栽过的坑正是
    /// 「同一件事两条路径、只改了其中一条」—— 字库扩容本身就有 BattleView 的
    /// 两个入口(手牌区徽章 + 战利品浮层徽章),分开写迟早漂开。
    ///
    /// ⚠️ <paramref name="onRewarded"/> 是**异步**回调:真 SDK 下从点击到发奖之间
    /// 隔着一整条广告播放。调用方不能假设它同步执行完(比如点完立刻读发奖后的状态),
    /// 要发奖后刷新 UI 就把刷新写进这个回调里。占位实现是同步回调,
    /// 所以**这类 bug 在接真 SDK 之前看不出来**。</summary>
    public static class AdGate
    {
        /// <summary>播一条奖励式广告;只有玩家看完才执行 <paramref name="onRewarded"/>。
        /// 中途退出/失败都静默放过 —— 第 14 章的口径是「多给」,不是「解锁」,
        /// 没看成就维持原状,不该弹报错骂玩家。</summary>
        public static void Watch(AdPlacement placement, Action onRewarded)
        {
            if (onRewarded == null) return;
            Monetization.Ads.ShowRewarded(placement, result =>
            {
                if (result == AdResult.Rewarded) onRewarded();
            });
        }
    }
}
