using System;
using UnityEngine;

namespace Brushblade.Platform
{
    /// <summary>测试机白名单:名单内的设备**不触发广告,直接拿奖励**(2026-09-23 用户要求)。
    ///
    /// 用途是自测 —— 上线后每验一次「看广告换墨锭」都要真看完 30 秒广告,
    /// 而且**拿真单元反复自测会被 AdMob 判为无效流量**。这条后门让自己的机器
    /// 绕开广告本身,既省时间也避开无效流量。
    ///
    /// ⚠️ 它绕过的是**广告请求**,不是发奖逻辑:白名单机器根本不向 AdMob 发请求,
    /// 所以不会产生任何假曝光。这一点是底线 —— 伪造曝光是广告欺诈,那是另一回事。
    ///
    /// ⚠️ IDFV 会在「同一厂商的 App 全部卸载」后变化,名单项会失效。换机或重装全家桶后
    /// 要重新取一次(见 <see cref="CurrentDeviceId"/>)。
    ///
    /// ⚠️ 名单泄漏的后果:名单里的那几台设备能白拿奖励。影响有限(只有具体设备 ID
    /// 才生效,猜不出来),但**别把这个文件里的 ID 贴到公开渠道**。</summary>
    public static class DeviceWhitelist
    {
        /// <summary>自己的测试机 ID。iOS 填 IDFV,Android 填
        /// <see cref="SystemInfo.deviceUniqueIdentifier"/>。
        /// 留空 = 白名单关闭(默认),所有设备都正常走广告。
        ///
        /// 怎么拿到本机 ID:装一个开发构建,看启动日志里的
        /// 「[Brushblade] 本机设备 ID = ...」那行(见 <see cref="LogCurrentDeviceId"/>)。</summary>
        private static readonly string[] AllowedDeviceIds =
        {
            // "12345678-1234-1234-1234-123456789ABC",  // 例:我的 iPhone(照这样加)
        };

        private static string _cached;

        /// <summary>本机设备 ID。iOS 取 IDFV(identifierForVendor),其他平台取 Unity 的
        /// 设备唯一标识。编辑器里也能取到,方便先在编辑器验通再上真机。</summary>
        public static string CurrentDeviceId
        {
            get
            {
                if (!string.IsNullOrEmpty(_cached)) return _cached;
#if UNITY_IOS && !UNITY_EDITOR
                _cached = UnityEngine.iOS.Device.vendorIdentifier;
#else
                _cached = SystemInfo.deviceUniqueIdentifier;
#endif
                return _cached;
            }
        }

        /// <summary>本机在不在白名单里。名单为空时恒为 false。</summary>
        public static bool IsWhitelisted
        {
            get
            {
                if (AllowedDeviceIds.Length == 0) return false;
                string id = CurrentDeviceId;
                if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier) return false;
                foreach (var allowed in AllowedDeviceIds)
                {
                    // 大小写不敏感:IDFV 各处打印出来的大小写不一致,按字面比对容易白忙
                    if (string.Equals(allowed, id, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        /// <summary>把本机 ID 打进日志,方便照抄进上面的名单。
        /// 只在开发构建/编辑器里打 —— 正式包不该把设备 ID 写进日志。</summary>
        public static void LogCurrentDeviceId()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            Debug.Log($"[Brushblade] 本机设备 ID = {CurrentDeviceId}" +
                      $"(白名单{(IsWhitelisted ? "内:广告将被跳过" : "外:正常走广告")})");
        }
    }

    /// <summary>白名单装饰器:套在真广告服务外面,名单内的设备直接发奖、不发广告请求。
    ///
    /// 做成装饰器而不是写进 AdMob 适配器里,是为了让这条后门**与具体 SDK 无关** ——
    /// 以后换 UnityAds / AppLovin,这一层原样套上去就行。</summary>
    public sealed class WhitelistBypassAdService : IAdService
    {
        private readonly IAdService _inner;

        public WhitelistBypassAdService(IAdService inner)
            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public bool IsReady(AdPlacement placement)
            => DeviceWhitelist.IsWhitelisted || _inner.IsReady(placement);

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
        {
            if (DeviceWhitelist.IsWhitelisted)
            {
                onComplete?.Invoke(AdResult.Rewarded);
                return;
            }
            _inner.ShowRewarded(placement, onComplete);
        }
    }
}
