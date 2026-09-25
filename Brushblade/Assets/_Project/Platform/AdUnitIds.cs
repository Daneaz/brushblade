using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Platform
{
    /// <summary>广告单元 ID。默认全部走 **Google 官方测试单元**,出包前才换自己的。
    ///
    /// Google 的测试单元是公开常量、恒定返回测试广告,可以随便提交进仓库 ——
    /// 它们不是密钥。真单元 ID 也不是密钥(客户端里本来就藏不住),但**用真单元做开发测试
    /// 会被判为无效流量(invalid traffic),严重的会封号**,所以这里默认必须是测试单元。
    ///
    /// 来源:developers.google.com/admob/unity/test-ads(2026-09-23 核对)。</summary>
    public static class AdUnitIds
    {
        // ---- Google 官方测试单元(公开常量)----
        public const string TestAppIdAndroid = "ca-app-pub-3940256099942544~3347511713";
        public const string TestAppIdIos = "ca-app-pub-3940256099942544~1458002511";
        public const string TestRewardedAndroid = "ca-app-pub-3940256099942544/5224354917";
        public const string TestRewardedIos = "ca-app-pub-3940256099942544/1712485313";

        /// <summary>true = 无视下面的生产表,一律用测试单元。**出包给玩家前置 false**。</summary>
        public static bool UseTestUnits = true;

        /// <summary>生产单元 ID:每个广告位**各建一个**,否则 AdMob 后台分不出哪个位在赚钱。
        /// 在 AdMob 后台建好后把 ID 填进来;留空的位会自动退回测试单元(并在
        /// <see cref="MissingProductionUnits"/> 里报出来)。</summary>
        private static readonly Dictionary<AdPlacement, (string Android, string Ios)> Production =
            new()
            {
                [AdPlacement.ChestBoost] = ("", ""),
                [AdPlacement.ShopRefresh] = ("", ""),
                [AdPlacement.ShopInk] = ("", ""),
                [AdPlacement.BattleLibrary] = ("", ""),
                [AdPlacement.BattleParts] = ("", ""),
                [AdPlacement.Revive] = ("", ""),
                [AdPlacement.BattleRestock] = ("", ""),
            };

        /// <summary>取某个广告位在当前平台的奖励式单元 ID。</summary>
        public static string RewardedFor(AdPlacement placement, bool android)
        {
            if (!UseTestUnits && Production.TryGetValue(placement, out var pair))
            {
                string id = android ? pair.Android : pair.Ios;
                if (!string.IsNullOrEmpty(id)) return id;
            }
            return android ? TestRewardedAndroid : TestRewardedIos;
        }

        /// <summary>还没填生产 ID 的广告位。出包前的自检用 —— 非空就说明有位在用测试单元,
        /// 那些位**一分钱都不会进账**。</summary>
        public static IReadOnlyList<AdPlacement> MissingProductionUnits(bool android)
            => Production
                .Where(kv => string.IsNullOrEmpty(android ? kv.Value.Android : kv.Value.Ios))
                .Select(kv => kv.Key)
                .ToList();
    }
}
