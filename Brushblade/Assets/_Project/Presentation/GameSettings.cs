using Brushblade.Core;
using Brushblade.Platform;

namespace Brushblade.Presentation
{
    /// <summary>运行时设置的**唯一读取点**(2026-09-24)。
    ///
    /// 设置本体存在 <see cref="MetaState.Settings"/> 里跟着存档走,但战斗演出、音效、音乐
    /// 三处都要读它,而它们谁也拿不到 MetaState(Juice 是挂在物件上的组件,
    /// MusicPlayer 跨场景常驻)。与 <see cref="Monetization"/> 同一个理由用静态持有:
    /// 这套表现层不是 DI 容器管的,硬塞构造注入要一路改到 GameRoot。
    ///
    /// ⚠️ 改了设置要调 <see cref="Apply"/>,否则音乐开关不会当场生效 ——
    /// 战斗速度是每帧现取的,音乐不是。</summary>
    public static class GameSettings
    {
        /// <summary>当前设置。<see cref="Bind"/> 之前是一份缺省值,所以任何读取点
        /// 都不必判 null(Juice 在 GameRoot 之前就可能跑起来)。</summary>
        public static SettingsState Current { get; private set; } = new();

        /// <summary>订阅是否生效。订阅模块未实装,这里恒 false ——
        /// 等它落地时这一处返回真值,×3 加速自动生效,速度逻辑一行不用改。</summary>
        public static bool Subscribed
            => Monetization.Billing.Status == SubscriptionStatus.Active;

        /// <summary>把存档里的设置接进来(GameRoot 启动时调一次)。</summary>
        public static void Bind(SettingsState settings)
        {
            Current = settings ?? new SettingsState();
            Apply();
        }

        /// <summary>把当前设置推给会受影响的部件。改完设置必须调。</summary>
        public static void Apply()
        {
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.Apply(Current);
        }
    }
}
