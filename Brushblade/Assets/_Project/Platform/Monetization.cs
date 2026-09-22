namespace Brushblade.Platform
{
    /// <summary>变现服务的**唯一取用入口**。接真 SDK 时只改这里的两个赋值。
    ///
    /// 用服务定位器而不是构造注入:表现层这些 View 是静态方法 + 回调搭起来的
    /// (GameRoot / ShopView / MapView / BattleView 都不是 DI 容器管的),
    /// 硬塞构造注入要把六个调用点一路往上改到 GameRoot,收益不抵改动面。
    ///
    /// 默认值是「未接 SDK」的那对占位实现,所以**没接 SDK 时游戏行为与现在完全一致**。
    /// 启动时(GameRoot)换成真实现:
    /// <code>
    /// Monetization.Ads = new AdMobAdService(...);
    /// Monetization.Billing = new StoreKitBillingService(...);
    /// </code>
    ///
    /// ⚠️ 出包给玩家前必须确认这两个不再是 Direct/Stub —— 否则广告位全是白送、
    /// 订阅永远买不了。这件事没有编译错会提示你。</summary>
    public static class Monetization
    {
        private static IAdService _ads;
        private static IBillingService _billing;

        public static IAdService Ads
        {
            get => _ads ??= new DirectGrantAdService();
            set => _ads = value;
        }

        public static IBillingService Billing
        {
            get => _billing ??= new StubBillingService();
            set => _billing = value;
        }

        /// <summary>当前跑的是不是占位实现。用于在开发构建上打角标/日志,
        /// 免得「忘了接 SDK 就出包」这种事悄无声息地过去。</summary>
        public static bool IsUsingPlaceholders
            => Ads is DirectGrantAdService || Billing is StubBillingService;
    }
}
