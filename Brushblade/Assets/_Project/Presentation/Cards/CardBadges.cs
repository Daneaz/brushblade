using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字牌角标族(基线 <c>docs/design/ui/scenes/CardStates.dc.html</c>)。
    ///
    /// 牌面只承载「一眼要认出的四件事」:等级、稀有度、能不能升、在不在阵上;
    /// 未拥有再加一档锁态。其余全部让给右栏详情。
    ///
    /// **为什么单独成类**:同一套角标要贴在两处牌上 —— 卡组页的收集网格,与主界面的开箱结果。
    /// 此前两处各画各的(开箱那批牌连等级都没有),稿上明明是同一张牌。写在一处 = 加新角标时
    /// 不可能只改到一半。
    ///
    /// **尺寸一律按牌自己的高宽算比例**,不写死像素:卡组网格 216×268、开箱宽档 174×218、
    /// 窄档 126×158 都是同一张牌的不同大小,角标要跟着缩。比例取自稿上的 103×128pt。</summary>
    public static class CardBadges
    {
        // 稿上的比例(除以 103×128pt)
        private const float PadRatio = 5f / 128f;        // 角标距牌边
        private const float ChipHRatio = 15f / 128f;     // 等级/可升/出阵带的高度
        private const float FontRatio = 8.5f / 128f;     // 角标字号
        private const float DotRatio = 13f / 103f;       // 稀有度色点直径
        private const float FlagRatio = 34f / 103f;      // 新字角旗见方
        private const float FootHRatio = 12f / 128f;     // 牌脚高度
        private const float BarHRatio = 4f / 128f;       // 牌脚进度条高度

        /// <summary>一张牌当前的状态。<see cref="Locked"/> 为真时其余角标一概不画 ——
        /// 未拥有的字没有等级、没有出阵、也谈不上能不能升。</summary>
        public struct Spec
        {
            public CardRarity Rarity;
            public int Level;
            public bool Maxed;
            public bool InDeck;
            public bool CanUpgrade;
            public bool IsNew;
            public bool Locked;
        }

        /// <summary>把角标贴到 <see cref="Ui.GlyphTile"/> 建出来的牌上。
        /// <paramref name="size"/> 必须与建牌时传的一致 —— 角标全按它算比例。</summary>
        public static void Apply(GameObject tile, Vector2 size, Spec spec)
        {
            if (tile == null) return;
            float pad = size.y * PadRatio;
            int font = Mathf.Max(10, Mathf.RoundToInt(size.y * FontRatio));
            float chipH = size.y * ChipHRatio;

            if (spec.Locked)
            {
                LockIcon(tile.transform, pad, size.y * ChipHRatio * 1.15f);
                RarityDot(tile.transform, spec.Rarity, pad, size.x * DotRatio, dimmed: true);
                return;
            }

            // 左上:等级。满级转金底 —— 那是终点,不该与「Lv.3」同一个视觉重量
            string levelText = spec.Maxed ? Strings.T("common.maxed_short") : $"Lv.{spec.Level}";
            var level = Badge(tile.transform, "Level", levelText, font, chipH,
                spec.Maxed ? Theme.Gold : Theme.Ink, spec.Maxed ? Theme.GoldText : Color.white);
            Ui.Anchor(level, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(pad, -pad - chipH), new Vector2(pad + Ui.ChipWidth(levelText, font), -pad));

            RarityDot(tile.transform, spec.Rarity, pad, size.x * DotRatio, dimmed: false);

            // 牌底一条藕荷色带:与稀有度框不抢层次
            if (spec.InDeck)
            {
                var bar = Badge(tile.transform, "DeckBar", Strings.T("collection.card.deployed_bar"),
                    font, chipH, Theme.ExitPink, Color.white, radius: 0);
                Ui.Anchor(bar, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, chipH));
            }

            // 左下:可升徽标。遇出阵带自动上移 —— 四种状态各占一角,互不遮挡
            if (spec.CanUpgrade)
            {
                string text = Strings.T("collection.card.upgradable_badge");
                float bottom = spec.InDeck ? chipH + pad : pad;
                var badge = Badge(tile.transform, "Upgradable", text, font, chipH, Theme.Jade, Color.white);
                Ui.Anchor(badge, Vector2.zero, Vector2.zero,
                    new Vector2(pad, bottom), new Vector2(pad + Ui.ChipWidth(text, font), bottom + chipH));
            }

            if (spec.IsNew) NewFlag(tile.transform, size.x * FlagRatio, font);
        }

        /// <summary>右上角的稀有度色点。未拥有时掺灰但**保三成色相** ——
        /// 「那张红卡我还没拿到」是收集页最该说清的一件事,全灰掉就说不出来了。</summary>
        private static void RarityDot(Transform parent, CardRarity rarity, float pad, float diameter, bool dimmed)
        {
            var go = Ui.Panel(parent, "RarityDot");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Circle;
            image.raycastTarget = false;
            image.color = dimmed
                ? Color.Lerp(Theme.LockedBg, Theme.RarityColor(rarity), 0.42f)
                : Theme.RarityColor(rarity);
            Ui.Anchor((RectTransform)go.transform, Vector2.one, Vector2.one,
                new Vector2(-pad - diameter, -pad - diameter), new Vector2(-pad, -pad));
        }

        /// <summary>左上角的锁标(未拥有)。与等级角标同一个位子 —— 这两件事互斥。</summary>
        private static void LockIcon(Transform parent, float pad, float diameter)
        {
            var go = Ui.Panel(parent, "Lock");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Circle;
            image.color = new Color(Theme.LockGray.r, Theme.LockGray.g, Theme.LockGray.b, 0.16f);
            image.raycastTarget = false;
            Ui.Anchor((RectTransform)go.transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(pad, -pad - diameter), new Vector2(pad + diameter, -pad));
            var glyph = Ui.ThemedLabel(go.transform, Strings.T("collection.card.lock_icon"),
                Mathf.Max(10, Mathf.RoundToInt(diameter * 0.62f)), Theme.LockGray);
            Ui.Stretch(glyph.rectTransform);
        }

        /// <summary>新字:右上角一面朱砂角旗 + 一圈会呼吸的赭金光。
        /// 角旗做成一枚小方标而不是 45° 斜带 —— uGUI 里旋转的 Image 会连带旋转它的裁剪矩形,
        /// 稿上那条「转 45° 再用 overflow:hidden 切掉牌外那半」在这里没有等价写法。</summary>
        private static void NewFlag(Transform parent, float side, int font)
        {
            var halo = Ui.Panel(parent, "NewHalo");
            halo.transform.SetAsFirstSibling(); // 排在最底:字形与其余角标都盖在光之上
            var image = halo.AddComponent<Image>();
            image.sprite = Theme.Halo(10);
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.raycastTarget = false;
            var rect = (RectTransform)halo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-Theme.HaloPad, -Theme.HaloPad);
            rect.offsetMax = new Vector2(Theme.HaloPad, Theme.HaloPad);
            halo.AddComponent<CardHalo>().Init(image, Theme.Gold);

            string text = Strings.T("collection.card.new_flag");
            var flag = Badge(parent, "NewFlag", text, font, side * 0.5f, Theme.Cinnabar, Color.white);
            Ui.Anchor(flag, Vector2.one, Vector2.one,
                new Vector2(-Ui.ChipWidth(text, font), -side * 0.5f), Vector2.zero);
        }

        /// <summary>一枚实底圆角小标(等级 / 可升 / 出阵带 / 新字旗共用)。返回它的 rect,
        /// 由调用方钉到哪个角上 —— 角标之间的避让规则写在 <see cref="Apply"/> 里,不分散到各处。</summary>
        private static RectTransform Badge(Transform parent, string name, string text,
            int font, float height, Color bg, Color fg, int radius = 8)
        {
            var go = Ui.Panel(parent, name);
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.color = bg;
            image.raycastTarget = false; // 角标不抢牌自己的点击
            var label = Ui.ThemedLabel(go.transform, text, font, fg);
            Ui.Stretch(label.rectTransform);
            return (RectTransform)go.transform;
        }

        /// <summary>牌脚:升级材料进度条 + 张数。未拥有时换成产出来源一行小字。
        ///
        /// 牌脚**不进牌里**:牌面是「这张字是什么」,牌脚是「我离下一级还差多少」——
        /// 后者会天天变,压进牌面会与稀有度框抢注意力。返回值挂在网格格子里,与牌同宽。</summary>
        public static GameObject Foot(Transform parent, Vector2 cardSize, bool owned,
            int copies, int needed, bool maxed, bool canUpgrade)
        {
            var foot = Ui.Row(parent, "Foot", cardSize.y * PadRatio);
            var layout = foot.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            var element = foot.AddComponent<LayoutElement>();
            element.preferredWidth = cardSize.x;
            element.preferredHeight = cardSize.y * FootHRatio;
            element.flexibleWidth = 0;
            int font = Mathf.Max(10, Mathf.RoundToInt(cardSize.y * FontRatio));

            if (!owned)
            {
                Ui.ThemedLabel(foot.transform, Strings.T("collection.card.locked_foot"), font, Theme.LockGray);
                return foot;
            }

            float fraction = maxed ? 1f : (needed <= 0 ? 1f : Mathf.Clamp01((float)copies / needed));
            var bar = Ui.Bar(foot.transform, fraction,
                canUpgrade ? Theme.Jade : Theme.LockGray,
                new Vector2(cardSize.x * 0.62f, cardSize.y * BarHRatio));
            bar.GetComponent<LayoutElement>().flexibleWidth = 1;

            // 满级不写进度:别拿一个填不满的分母吊着人
            string text = maxed ? Strings.T("common.maxed") : $"{copies}/{needed}";
            var color = maxed ? Theme.GoldDeep : (canUpgrade ? Theme.UpgradeText : Theme.TextDim);
            Ui.ThemedLabel(foot.transform, text, font, color);
            return foot;
        }
    }
}
