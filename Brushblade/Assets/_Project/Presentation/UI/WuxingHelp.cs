using System.Text.RegularExpressions;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>卡详情名字行「?」点开的本系五行说明(稿 drafts/卡详情五行说明,2026-10-21 拍板)。
    /// 定宽 <see cref="W"/>、高按内容、屏幕居中,叠在卡详情之上;点遮罩或 × 只关这一层。
    /// 文案逐字取自字符串表 <c>wuxing_help.*</c>(用户审定,不可改写)。
    /// 金系「锋刃回鞘」的百分比由调用方从 Core 读(战斗内 <see cref="BattleEngine.EdgeSheatheShownPercent"/>、
    /// 战斗外 <see cref="MetaRules.EdgeSheatheShownPercent"/>),这里不手算断金。</summary>
    public static class WuxingHelp
    {
        private const float W = 1120f;
        private const int HeadH = 46, TitleSize = 34, TagSize = 22, TagH = 30;
        private const int ItemSize = 23, NoteSize = 19, RelH = 44, RelSize = 22;
        private const float LineH = ItemSize * 1.35f;   // 31.05
        private const float ItemGap = 12f, MarkCol = 22f, MarkGap = 6f, MarkSize = 10f;

        public static bool Supports(Element? element) =>
            element is Element.Fire or Element.Metal or Element.Water or Element.Earth or Element.Wood;

        public static void Open(Transform root, Element element, int edgeSheathePercent, bool hasDuanJin)
        {
            if (!Supports(element)) return;
            const float inner = W - 2f * Ui.SheetBorder - 2f * Ui.SheetPad;   // 1069
            const float textW = inner - MarkCol - MarkGap;                      // 1041

            var items = Items(element, edgeSheathePercent, hasDuanJin);
            float itemsH = ItemGap * (items.Length - 1);
            var itemHs = new float[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                itemHs[i] = Ui.WrappedTextHeight(Plain(items[i]), ItemSize, textW);
                itemsH += itemHs[i];
            }
            float h = 2f * Ui.SheetBorder + 2f * Ui.SheetPad + HeadH + TagH + RelH + itemsH + 3f * Ui.SheetSpacing;

            var overlay = Ui.Sheet(root, "WuxingHelp", W, h, dismissable: true, replaceSameName: true,
                Theme.ScrimSoft, out var content);
            var el = (Element?)element;

            // 头行:标题(宋体、本系 glyph 色)+ 弹簧 + ×
            var head = Ui.Row(content, "Head", 15);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, inner, HeadH);
            var title = Ui.ThemedLabel(head.transform, Title(element), TitleSize, Theme.GlyphColor(el), Theme.TitleFont);
            title.alignment = TextAnchor.MiddleLeft;
            var spring = Ui.Panel(head.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.RoundButton(head.transform, Strings.T("common.close"),
                () => Object.Destroy(overlay),
                Theme.PanelInset, Theme.TextDim, 22, new Vector2(46, 46), 23);

            var tag = Ui.ThemedLabel(content, Tagline(element), TagSize, Theme.TextDim, null, TextAnchor.MiddleLeft);
            Ui.Sized(tag.gameObject, inner, TagH);

            var list = Ui.VStack(content, "Items", ItemGap);
            list.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(list, inner, itemsH);
            for (int i = 0; i < items.Length; i++)
            {
                var row = Ui.Row(list.transform, "Item", MarkGap);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                Ui.Sized(row, inner, itemHs[i]);
                var col = Ui.Panel(row.transform, "Mark");
                Ui.Sized(col, MarkCol, itemHs[i]);
                var block = Ui.Panel(col.transform, "Block");
                block.AddComponent<Image>().color = Theme.ElementColor(el);
                float y = (LineH - MarkSize) / 2f;
                Ui.Anchor((RectTransform)block.transform, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(MarkGap, -y - MarkSize), new Vector2(MarkGap + MarkSize, -y));
                var text = Ui.ThemedLabel(row.transform, Ui.NoBreakSpaces(Labelled(items[i], el)), ItemSize,
                    Theme.TextQuiet, null, TextAnchor.UpperLeft);
                text.supportRichText = true;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(text.gameObject, textW, itemHs[i]);
            }

            // 生克条:两个系字加粗配该系 fg
            var rel = Ui.CardPanel(content, "Relation", Theme.PanelInset, 9);
            Ui.Sized(rel.gameObject, inner, RelH);
            var relText = Ui.ThemedLabel(rel.transform, RelationRich(element), RelSize, Theme.TextQuiet, null, TextAnchor.MiddleLeft);
            relText.supportRichText = true;
            Ui.Anchor(relText.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0));
        }

        // ---- 文案(key 一律字面量,字符串表检查只认字面量) ----

        private static string Title(Element e) => e switch
        {
            Element.Fire => Strings.T("wuxing_help.fire.title"),
            Element.Metal => Strings.T("wuxing_help.metal.title"),
            Element.Water => Strings.T("wuxing_help.water.title"),
            Element.Earth => Strings.T("wuxing_help.earth.title"),
            _ => Strings.T("wuxing_help.wood.title"),
        };

        private static string Tagline(Element e) => e switch
        {
            Element.Fire => Strings.T("wuxing_help.fire.tagline"),
            Element.Metal => Strings.T("wuxing_help.metal.tagline"),
            Element.Water => Strings.T("wuxing_help.water.tagline"),
            Element.Earth => Strings.T("wuxing_help.earth.tagline"),
            _ => Strings.T("wuxing_help.wood.tagline"),
        };

        private static string Relation(Element e) => e switch
        {
            Element.Fire => Strings.T("wuxing_help.fire.relation"),
            Element.Metal => Strings.T("wuxing_help.metal.relation"),
            Element.Water => Strings.T("wuxing_help.water.relation"),
            Element.Earth => Strings.T("wuxing_help.earth.relation"),
            _ => Strings.T("wuxing_help.wood.relation"),
        };

        private static string[] Items(Element e, int percent, bool hasDuanJin)
        {
            switch (e)
            {
                case Element.Fire:
                    return new[] { Strings.T("wuxing_help.fire.attack"), Strings.T("wuxing_help.fire.face") };
                case Element.Metal:
                {
                    string note = hasDuanJin
                        ? $"<size={NoteSize}><color=#{ColorUtility.ToHtmlStringRGB(Theme.TextDim)}>{Strings.T("wuxing_help.perk_note")}</color></size>"
                        : "";
                    return new[]
                    {
                        Strings.T("wuxing_help.metal.attack"),
                        Strings.T("wuxing_help.metal.face"),
                        Strings.T("wuxing_help.metal.sheathe", ("percent", percent), ("note", note))
                            .Replace($"+{percent}%", $"<b>+{percent}%</b>"),   // 稿:整个「+N%」加粗
                    };
                }
                case Element.Water:
                    return new[] { Strings.T("wuxing_help.water.attack"), Strings.T("wuxing_help.water.face") };
                case Element.Earth:
                    return new[] { Strings.T("wuxing_help.earth.attack"), Strings.T("wuxing_help.earth.face") };
                default:
                    return new[] { Strings.T("wuxing_help.wood.attack"), Strings.T("wuxing_help.wood.face") };
            }
        }

        /// <summary>估高用:去掉富文本标记。</summary>
        private static string Plain(string s) => Regex.Replace(s, "<[^>]+>", "");

        /// <summary>在第一个全角冒号处拆开,冒号前的标签加粗配本系 fg。</summary>
        private static string Labelled(string line, Element? el)
        {
            int i = line.IndexOf('：');
            if (i <= 0) return line;
            string hex = ColorUtility.ToHtmlStringRGB(Theme.ElementSoftFg(el));
            return $"<b><color=#{hex}>{line.Substring(0, i)}</color></b>{line.Substring(i)}";
        }

        private static Element? ElementOfChar(char c) => c switch
        {
            '金' => Element.Metal,
            '木' => Element.Wood,
            '水' => Element.Water,
            '火' => Element.Fire,
            '土' => Element.Earth,
            _ => null,
        };

        /// <summary>「克X，被Y克。」里的 X、Y 两个系字加粗配各自的 fg。</summary>
        private static string RelationRich(Element e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in Relation(e))
            {
                var el = ElementOfChar(c);
                if (el == null) sb.Append(c);
                else sb.Append($"<b><color=#{ColorUtility.ToHtmlStringRGB(Theme.ElementSoftFg(el))}>{c}</color></b>");
            }
            return sb.ToString();
        }
    }
}
