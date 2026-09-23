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

        /// <summary>装配变现服务。启动时(GameRoot.Boot)调一次。
        ///
        /// 链路是:<c>白名单装饰器 → (AdMob 或 占位)</c>。白名单套在最外层,
        /// 所以名单内的设备连广告请求都不会发 —— 既快,也不会拿真单元刷出无效流量。
        ///
        /// 订阅**故意不装真实现**:四条权益在 Core 里一条都没有,计费通了也不能卖。
        /// 见 ShopView.BuildSubscriptionBar 的注释。</summary>
        public static void InstallDefault()
        {
#if BRUSHBLADE_ADMOB
            var admob = new AdMobAdService();
            admob.Initialize();
            Ads = new WhitelistBypassAdService(admob);
#else
            // 没装 SDK:仍然套白名单装饰器,这样两种构建的代码路径一致 ——
            // 「只在正式包里才走到的分支」是最容易藏 bug 的地方
            Ads = new WhitelistBypassAdService(new DirectGrantAdService());
#endif
            DeviceWhitelist.LogCurrentDeviceId();
        }
    }
}
