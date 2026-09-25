namespace Brushblade.Core
{
    /// <summary>玩家设置(2026-09-24)。跟着存档走,不另开一套 PlayerPrefs ——
    /// 两套持久化迟早对不上,而这个游戏也没有「换设备保留设置但不保留进度」的场景。</summary>
    public sealed class SettingsState
    {
        /// <summary>固定战斗加速:开了就**一直**按加速倍率跑,不用按住屏幕。
        /// 缺省关 —— 第一次进游戏的人该先按正常速度看清结算是怎么回事。</summary>
        public bool FastBattle { get; set; }

        public bool SfxEnabled { get; set; } = true;
        public bool MusicEnabled { get; set; } = true;
    }

    /// <summary>战斗演出速度(2026-09-24 用户拍板)。
    ///
    /// ⚠️ **行为变更**:长按加速本身 2026-08-30 就有了,但倍率写死 3f。本次按用户口径
    /// 改为 **免费 ×2 / 订阅 ×3** —— 也就是说原本白给的 3 倍速,现在 2 倍免费、3 倍进订阅权益。
    ///
    /// ⚠️ 订阅那一档**尚未实装**:整个订阅模块都还没做(第 14 章 14.3.1),
    /// <paramref name="subscribed"/> 目前恒为 false。这里先把权益的**形状**定下来,
    /// 等订阅模块落地时只要把订阅态喂进来即可,不用回头改速度逻辑。</summary>
    public static class SpeedRules
    {
        public const float NormalRate = 1f;

        /// <summary>免费档加速倍率。</summary>
        public const float BaseFastRate = 2f;

        /// <summary>月订阅档加速倍率(第 14 章订阅权益,未实装)。</summary>
        public const float SubscriberFastRate = 3f;

        public static float FastRate(bool subscribed)
            => subscribed ? SubscriberFastRate : BaseFastRate;

        /// <summary>当前该用的演出倍率。
        ///
        /// 「长按」与「设置里固定开」是**或**的关系:固定开了之后再长按不会叠成 ×4 ——
        /// 加速是给「看过一遍了想快点」用的,不是数值,叠加没有意义还会糊成一片。</summary>
        /// <param name="holding">此刻手指/鼠标是否按住屏幕。</param>
        /// <param name="subscribed">订阅是否生效(订阅模块落地前恒 false)。</param>
        public static float RateFor(SettingsState settings, bool holding, bool subscribed)
        {
            bool fast = holding || (settings != null && settings.FastBattle);
            return fast ? FastRate(subscribed) : NormalRate;
        }
    }
}
