using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>宝箱立绘 + 三态的唯一画法(2026-09-02 从 <see cref="MapView"/> 抽出来)。
    ///
    /// 抽出来的原因:登塔结算页此前画的是一个 <c>Theme.Gold</c> 纯色块,而主界面箱位与开箱
    /// 结果页画的是真立绘 —— 同一只「鎏金匣」在两个界面长得完全不一样,玩家读不出「我刚才
    /// 拿到的就是它」。试玩反馈点名了这一条。
    ///
    /// 放在 <c>Chests/</c> 而不是让 <c>GameRoot</c> 反过来调 <c>MapView</c>:两个 View 之间
    /// 互相引用比双方都依赖一个共用件更难拆,而这段本来就只依赖 <see cref="ChestAssets"/> 与
    /// <see cref="ChestView"/>,和 MapView 的其余部分没有关系。
    ///
    /// 素材缺失时回落成档位色块 + 首字(与 <c>Icons.cs</c> 的双轨同理):信息一点不丢,
    /// 换 PNG 也不需要动这里一行。</summary>
    public static class ChestArt
    {
        public static void Draw(Transform parent, ChestTier tier, ChestView.State state, float size)
        {
            var row = Ui.Row(parent, "Art", 0);
            if (ChestAssets.Has(tier))
            {
                var go = new GameObject("ChestArt", typeof(RectTransform));
                go.transform.SetParent(row.transform, false);
                var element = go.AddComponent<LayoutElement>();
                element.preferredWidth = size;
                element.preferredHeight = size;
                go.AddComponent<ChestView>().Init(tier, state, size);
                return;
            }

            var icon = Ui.CardPanel(row.transform, "Body", Theme.ChestColor(tier), 10);
            var iconElement = icon.gameObject.AddComponent<LayoutElement>();
            iconElement.preferredWidth = size;
            iconElement.preferredHeight = size;
            var glyph = Ui.ThemedLabel(icon.transform, ChestRules.TierName(tier).Substring(0, 1),
                Mathf.RoundToInt(size * 0.44f), Color.white, Theme.TitleFont);
            Ui.Stretch(glyph.rectTransform);
        }
    }
}
