using System;

namespace Brushblade.Platform
{
    /// <summary>订阅态(第 14 章 14.3:单一月订阅,首版基准 $4.99/月)。</summary>
    public enum SubscriptionStatus
    {
        Unknown,        // 还没查过 —— 别拿它当「未订阅」用,见下面的注释
        NotSubscribed,
        Active,
        Expired,        // 曾订阅、已过期:权益按未订阅算,但 UI 文案可以不同
    }

    /// <summary>一次购买尝试的结局。</summary>
    public enum PurchaseResult
    {
        Purchased,
        Cancelled,      // 玩家自己取消 —— 不是错误,别弹报错
        Failed,
        NotAvailable,   // 商店不可用 / 未接 SDK
        AlreadyOwned,   // 已在订阅期内(补单场景)
    }

    /// <summary>订阅计费服务。真实现走 StoreKit(iOS)/ Play Billing(Android)。
    ///
    /// ⚠️ <see cref="Status"/> 只是**本地缓存**,不是凭据。第 19.9 的口径是服务端校验,
    /// 真接入时权益发放要以服务端校验结果为准 —— 客户端这个值只用来画 UI。
    ///
    /// ⚠️ 两家商店都要求提供「恢复购买」入口,<see cref="Restore"/> 不是可选项。</summary>
    public interface IBillingService
    {
        /// <summary>最近一次查询到的订阅态(本地缓存,可能是 Unknown)。</summary>
        SubscriptionStatus Status { get; }

        void QueryStatus(Action<SubscriptionStatus> onComplete);
        void Purchase(Action<PurchaseResult> onComplete);
        void Restore(Action<SubscriptionStatus> onComplete);
    }

    /// <summary>占位实现:永远未订阅,购买返回 <see cref="PurchaseResult.NotAvailable"/>。
    ///
    /// 对应现状 —— 商城那条订阅栏点了只弹「敬请期待」(ShopView)。接了真计费之后
    /// 换掉 <see cref="Monetization.Billing"/> 即可,UI 那一侧不用动。</summary>
    public sealed class StubBillingService : IBillingService
    {
        public SubscriptionStatus Status => SubscriptionStatus.NotSubscribed;

        public void QueryStatus(Action<SubscriptionStatus> onComplete) => onComplete?.Invoke(Status);

        public void Purchase(Action<PurchaseResult> onComplete)
            => onComplete?.Invoke(PurchaseResult.NotAvailable);

        public void Restore(Action<SubscriptionStatus> onComplete) => onComplete?.Invoke(Status);
    }
}
