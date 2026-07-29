using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>五行速查(2026-07-22;2026-07-29 由小按钮+弹窗改为常驻环图):战斗页两角直接摆图。
    /// 图取自 docs/design/wuxing/*.svg 的光栅稿(rsvg-convert -w 720 -h 660 → UI/Resources)。
    /// 数值口径同 WuxingResolver(docs/design/wuxing-reference.md)。</summary>
    public static class WuxingChart
    {
        /// <summary>环图显示尺寸(1600×900 基准),按稿子 360:330 的原比例缩。</summary>
        private static readonly Vector2 ChartSize = new(131, 120);

        /// <summary>建一张带标题的环图(标题在上);整块位置由调用方 Anchor。</summary>
        public static GameObject Mount(Transform parent, bool sheng)
        {
            var stack = Ui.VStack(parent, sheng ? "WuxingSheng" : "WuxingKe", 2);
            Ui.ThemedLabel(stack.transform, sheng ? "相 生" : "相 克", 16,
                Theme.TextMain, Theme.TitleFont);

            var chartGo = Ui.Panel(stack.transform, "Chart");
            var image = chartGo.AddComponent<Image>();
            image.sprite = Chart(sheng ? "wuxing_sheng" : "wuxing_ke");
            image.preserveAspect = true;
            image.raycastTarget = false; // 纯展示:点它等于点背景(取消选中),别把点击吃掉
            image.enabled = image.sprite != null;
            var element = chartGo.AddComponent<LayoutElement>();
            element.preferredWidth = ChartSize.x;
            element.preferredHeight = ChartSize.y;
            return stack;
        }

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>走 Texture2D + Sprite.Create,同 MobAssets:
        /// Resources.Load&lt;Sprite&gt; 依赖 PNG 的 textureType 导入设置,取不到会静默变空。</summary>
        private static Sprite Chart(string key)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }
    }
}
