// AdMob(Google Mobile Ads)奖励式广告适配器。
//
// ⚠️ 整份代码在 BRUSHBLADE_ADMOB 这个 define 之后 —— Google Mobile Ads Unity SDK
// **不在本工程里**,不加 define 时这个文件等于空文件,工程照常编译。
// 装好 SDK 后在 Player Settings → Scripting Define Symbols 里加上 BRUSHBLADE_ADMOB
// 才会启用。安装步骤见 docs/design/第14章-变现设计.md 的 14.3.2。
//
// ⚠️ 下面用的是 Google Mobile Ads Unity **v9/v10** 的 API 形状(RewardedAd.Load 静态方法、
// new AdRequest() 无 Builder)。v8 及更早是 new AdRequest.Builder().Build(),
// 装的版本不同要对着官方样例改 —— 本文件**从未编译过**(开发环境没装 SDK)。

#if BRUSHBLADE_ADMOB
using System;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Brushblade.Platform
{
    /// <summary>AdMob 奖励式广告。每个广告位各持一条预加载的广告,播完立刻续上。</summary>
    public sealed class AdMobAdService : IAdService
    {
        private readonly Dictionary<AdPlacement, RewardedAd> _loaded = new();
        private readonly HashSet<AdPlacement> _loading = new();
        private bool _initialized;

        private static bool IsAndroid => Application.platform == RuntimePlatform.Android;

        /// <summary>初始化 SDK 并把六个位各预加载一条。启动时调一次。</summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 各家 SDK 的回调线程不一致,而 IAdService 约定回调在主线程。
            // 这一行必须在 Initialize 之前设,否则回调会落在后台线程,
            // 回调里碰 UI 就是「偶发崩溃/不刷新」那类最难查的 bug。
            MobileAds.RaiseAdEventsOnUnityMainThread = true;

            MobileAds.Initialize(_ =>
            {
                foreach (AdPlacement placement in Enum.GetValues(typeof(AdPlacement)))
                    Preload(placement);
            });
        }

        public bool IsReady(AdPlacement placement)
            => _loaded.TryGetValue(placement, out var ad) && ad != null && ad.CanShowAd();

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
        {
            // onComplete 必须**恰好回调一次**(IAdService 的约定)。AdMob 会同时触发
            // 「拿到奖励」和「全屏内容关闭」两个事件,不加这道闸就会发两次奖。
            bool done = false;
            void Finish(AdResult result)
            {
                if (done) return;
                done = true;
                onComplete?.Invoke(result);
            }

            if (!IsReady(placement))
            {
                Preload(placement);           // 没货:补一条,下次能用
                Finish(AdResult.NotReady);
                return;
            }

            var ad = _loaded[placement];
            _loaded.Remove(placement);        // 一条广告只能播一次,先摘下来

            bool earned = false;
            ad.OnAdFullScreenContentClosed += () =>
            {
                Finish(earned ? AdResult.Rewarded : AdResult.Dismissed);
                Preload(placement);           // 播完立刻续上,下一次点击才不是 NotReady
            };
            ad.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning($"[Ads] {placement} 播放失败:{error?.GetMessage()}");
                Finish(AdResult.Failed);
                Preload(placement);
            };

            ad.Show(_ => earned = true);      // 只记账,发奖统一交给上面的 Closed 分支
        }

        private void Preload(AdPlacement placement)
        {
            if (_loading.Contains(placement) || IsReady(placement)) return;
            _loading.Add(placement);

            string unitId = AdUnitIds.RewardedFor(placement, IsAndroid);
            RewardedAd.Load(unitId, new AdRequest(), (ad, error) =>
            {
                _loading.Remove(placement);
                if (error != null || ad == null)
                {
                    // 不重试:反复失败多半是没填充或网络不通,自动重试只会刷爆日志。
                    // 下次玩家点击时 ShowRewarded 会再补一次 Preload。
                    Debug.LogWarning($"[Ads] {placement} 加载失败:{error?.GetMessage()}");
                    return;
                }
                _loaded[placement] = ad;
            });
        }
    }
}
#endif
