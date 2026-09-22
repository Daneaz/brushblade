using System;

namespace Brushblade.Platform
{
    /// <summary>奖励式广告位(第 14 章 14.2 的那张表,一位一枚)。
    ///
    /// ⚠️ 铁律:**全部奖励式,无强制插屏**。这个枚举里不该出现开屏/插屏/banner ——
    /// 加位之前先回去读 14.4「明确不做清单」。</summary>
    public enum AdPlacement
    {
        ChestBoost,     // 开箱加速:每个宝箱 1 次
        ShopRefresh,    // 商城免费刷新:1 次/日
        ShopInk,        // 商城墨锭位:1 次/日(+30 墨锭)
        BattleLibrary,  // 局内扩容·字库:每关 1 次(+2)
        BattleParts,    // 局内扩容·部件池:每关 1 次(+2)
        Revive,         // 复活位:整次登塔 1 次
    }

    /// <summary>一次广告播放的结局。**只有 Rewarded 能发奖**。</summary>
    public enum AdResult
    {
        Rewarded,   // 看完了,该发奖
        Dismissed,  // 玩家中途关掉 —— 不发奖,但也不是错误,别弹报错
        Failed,     // 加载或播放失败
        NotReady,   // 没有可播的广告(无填充/未初始化)
    }

    /// <summary>奖励式广告服务。真 SDK(AdMob / UnityAds / AppLovin)在这一层适配,
    /// 上面的表现层只认这个接口。
    ///
    /// 回调约定:<paramref name="onComplete"/> **必须恰好回调一次**,且回到主线程 ——
    /// 各家 SDK 的回调线程不一致,适配器负责抹平,调用方不做线程判断。</summary>
    public interface IAdService
    {
        /// <summary>该广告位当前有没有可播的广告。用于给按钮置灰,不是发奖前提。</summary>
        bool IsReady(AdPlacement placement);

        /// <summary>播一条奖励式广告,播完(或失败)回调一次。</summary>
        void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete);
    }

    /// <summary>直通实现:不播广告,立刻当作「已看完」发奖。
    ///
    /// 这**就是接 SDK 之前的现状**(各广告位按钮点了直接给奖励),把它显式化成一个实现,
    /// 是为了让调用方从现在起就走 <see cref="IAdService"/> 这条路 —— 等真 SDK 到位时
    /// 只换 <see cref="Monetization.Ads"/> 这一处,不用再回头改六个调用点。
    ///
    /// ⚠️ 编辑器与未接 SDK 的构建用它;**出包给玩家之前必须换成真实现**,
    /// 否则广告位全是白送。</summary>
    public sealed class DirectGrantAdService : IAdService
    {
        public bool IsReady(AdPlacement placement) => true;

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
            => onComplete?.Invoke(AdResult.Rewarded);
    }
}
