using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>领取里程碑:墨锭 + 字卡 3 选 1(稿 MilestonePick.dc.html)。
    /// 候选第一次打开时生成并立刻落盘;× 关闭不丢候选。</summary>
    public static class MilestonePickSheet
    {
        // 高比稿(356pt)多 40:按 uGUI 实算(Noto 行高 宋 1.437 / 黑 1.448),子项首选高合计
        // 抬头 36 + Lv 102 + 墨锭胶囊 59 + 小节行 33 + 候选排 319(上让位 6 + 牌 272 + 10 + chip 31)
        // + 脚行 84 = 633,加 6 个行距 × 14 = 717 ≤ 卡内净高 724,余 7 交给弹簧。
        // 历史:原来行距 21、卡高 745 → 752 > 684,竖排按比例把子项往 0 收,候选牌被压成横向偏胖;
        // 于是行距回 Sheet 默认 14、卡高 785。2026-10-04 补 C 类:墨锭行换成 28pt 高胶囊(+17),
        // 选中环从「占布局的外扩底板」改成画在牌外、不占布局(−16),候选排顶上让出上抬的 6。
        private const float W = 1256f, H = 785f;           // 稿 600×356pt,高见上
        private static readonly Vector2 TileSize = new(218f, 272f); // 稿 104×130pt
        // 选中态(稿 .cand.on:box-shadow 0 0 0 3px 宣纸, 0 0 0 5px ink-soft;translateY(-3px)):
        // 牌外 3pt 宣纸间隙 + 2pt `ink-soft` 外环、整张上抬 3pt → 6 / 4 / 6 逻辑单位。
        // 环画在牌外、不进布局;上抬只挪「Lift」这一层,布局不动。
        private const float GapPad = 6f, RingPad = 10f, Lift = 6f;
        private const float TileRadius = 14f;              // GlyphTile 根圆角(稿 6.7pt)

        public static void Show(Transform root, RecipeGraph graph, MetaState meta, int level,
            IReadOnlyList<string> cardPool, Action save, Action onClaimed)
        {
            var def = MilestoneRules.ForLevel(level);
            if (!def.HasValue || !MilestoneRules.IsClaimable(meta, level)) return;
            bool hadOffer = meta.MilestoneOffers.ContainsKey(level);
            var offer = MilestoneRules.GetOrCreateOffer(meta, level, cardPool, graph,
                new GameRandom(Environment.TickCount));
            if (!hadOffer && offer.Count > 0) save(); // 新生成的候选立刻落档:关掉重进不变

            var sheet = Ui.Sheet(root, "MilestonePick", W, H, dismissable: true, replaceSameName: true,
                Theme.Scrim, out var content, out var card);
            // content 已是 Sheet 给的带内边距 VStack,直接在里面排,不再套一层(同 LevelUpPopup)
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;             // 行距用 Sheet 默认的 SheetSpacing(14),见 H 的注释
            layout.padding = new RectOffset(42, 42, 29, 29);

            // 右上 ×(稿 .x):点遮罩也能关,这颗是给「不知道能点遮罩」的人的显式出口
            var close = Ui.RoundButton(card, Strings.T("common.close"), () => UnityEngine.Object.Destroy(sheet),
                Color.clear, Theme.TextDim, 42, new Vector2(92, 92));
            close.GetComponent<LayoutElement>().ignoreLayout = true;
            Ui.Anchor((RectTransform)close.transform, Vector2.one, Vector2.one,
                new Vector2(-17 - 92, -13 - 92), new Vector2(-17, -13));

            Ui.KickerRow(content, Strings.T("milestone.kicker"), 25, Theme.GoldDeep);
            Ui.ThemedLabel(content, Strings.T("character.ms_level", ("level", level)), 71,
                Theme.TextMain, Theme.TitleFont);
            InkCapsule(content, def.Value.Ink);

            var sec = Ui.Row(content, "Sec", 13);
            sec.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(sec.transform,
                Strings.T("milestone.pick_title", ("rarity", CharInfo.RarityName(def.Value.Rarity))),
                23, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(sec.transform, Strings.T("milestone.pick_note"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            var rule = Ui.Panel(sec.transform, "Rule").AddComponent<Image>();
            rule.color = Theme.PanelBorder;
            Ui.Sized(rule.gameObject, height: 2, flexWidth: 1);

            string picked = null;
            Image okFace = null;
            Text okLabel = null;
            // 这一档稀有度没有可发的字:给一句说明,不画空排(Core 不落档空候选,稍后再开会重抽)
            if (offer.Count == 0)
                Ui.ThemedLabel(content, Strings.T("milestone.no_candidates"), 23, Theme.TextDim);
            var picks = Ui.Row(content, "Picks", 38);
            var picksLayout = picks.GetComponent<HorizontalLayoutGroup>();
            picksLayout.childAlignment = TextAnchor.MiddleCenter;
            picksLayout.padding = new RectOffset(0, 0, (int)Lift, 0);   // 给上抬的那张让位,环不顶到小节行
            var cands = new List<(string id, RectTransform lift, GameObject ring)>();
            foreach (var id in offer)
            {
                if (!graph.TryGet(id, out var c)) continue; // 字表删字后的幽灵候选:不画,也领不走
                bool owned = meta.OwnedCards.Contains(id);
                // Cand 只占「牌 + 10 + chip」的布局;Lift 铺满它、选中时整层上移(牌、环、圆标、chip 一起)
                var cell = Ui.Panel(picks.transform, "Cand");
                Ui.Sized(cell, TileSize.x, TileSize.y + 10 + Ui.ChipHeight(19));
                var lift = Ui.VStack(cell.transform, "Lift", 10);
                Ui.Stretch((RectTransform)lift.transform);
                lift.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;

                var slot = Ui.Panel(lift.transform, "Slot");
                Ui.Sized(slot, TileSize.x, TileSize.y);
                var ring = Ui.Panel(slot.transform, "Ring");
                RoundRect(ring.transform, "Outer", Theme.InkSoft, TileRadius + RingPad, RingPad);
                RoundRect(ring.transform, "Gap", Theme.PanelPaper, TileRadius + GapPad, GapPad);
                Ui.Stretch((RectTransform)ring.transform);
                ring.SetActive(false);
                var pickId = id;
                var tile = Ui.GlyphTile(slot.transform, c, false, () =>
                {
                    picked = pickId;
                    foreach (var (cid, l, r) in cands)
                    {
                        bool on = cid == pickId;
                        r.SetActive(on);
                        l.anchoredPosition = new Vector2(0f, on ? Lift : 0f);
                    }
                    okFace.color = Theme.Cta;
                    okLabel.color = Color.white;
                }, TileSize);
                Ui.Stretch((RectTransform)tile.transform);
                if (c.Element is { } element) ElementBadge(slot.transform, element);

                Ui.Chip(lift.transform,
                    owned ? Strings.T("milestone.tag_dup") : Strings.T("milestone.tag_new"),
                    owned ? Theme.PanelInset : Theme.GoldSoft,
                    owned ? Theme.TextDim : Theme.GoldDeep, 19,
                    border: owned ? Theme.PanelBorder : Theme.Gold);
                cands.Add((id, (RectTransform)lift.transform, ring));
            }

            var spring = Ui.Panel(content, "Spring");
            Ui.Sized(spring, flexHeight: 1);

            var foot = Ui.Row(content, "Foot", 25);
            foot.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var note = Ui.ThemedLabel(foot.transform, Strings.T("milestone.foot"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var ok = Ui.PillButton(foot.transform, Strings.T("milestone.ok"), () =>
            {
                if (picked == null || !MilestoneRules.TryClaim(meta, level, picked, graph)) return;
                save();
                UnityEngine.Object.Destroy(sheet);
                onClaimed();
            }, Theme.LockedBg, Theme.TextDim, 31, new Vector2(314, 84));
            // 没选之前是灰钮(稿 .pill.off),点了也不动;选了一张才换成石青主钮(Theme.Cta)
            okFace = (Image)ok.targetGraphic;
            okLabel = ok.GetComponentInChildren<Text>();
        }
        /// <summary>墨锭胶囊(稿 .inkline):`ink-bar` 底、1pt `panel-border` 描边、高 28pt、左右内边距 14pt、
        /// 图标 18×11pt、间距 8pt、字 11pt → 59 / 29 / 38×23 / 17 / 23 逻辑单位。
        /// 「墨锭」常规、「+1,200」加粗 —— 一个 Text 只有一种字重,拆成两个 Text。</summary>
        private static void InkCapsule(Transform parent, int ink)
        {
            const int FontSize = 23, H = 59, PadX = 29;   // 描边 1pt → 2(同 OutlinedPanel 默认)
            var holder = Ui.Row(parent, "InkLine", 0);          // 居中用:父级横向撑满,胶囊按首选宽
            var capsule = Ui.OutlinedPanel(holder.transform, "Capsule", Theme.InkBar, Theme.PanelBorder,
                H / 2, 2f, out var face);                          // 圆角 14pt = 半高,整条胶囊
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = capsule.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(PadX, PadX, 0, 0);
            row.spacing = 17;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            capsule.gameObject.AddComponent<LayoutElement>().preferredHeight = H;

            var icon = Ui.Panel(capsule.transform, "Icon").AddComponent<Image>();
            icon.sprite = Theme.Ingot;
            icon.color = Theme.IngotDark;
            icon.raycastTarget = false;
            Ui.Sized(icon.gameObject, 38, 23);
            var words = Ui.Row(capsule.transform, "Words", 6);
            Ui.ThemedLabel(words.transform, Strings.T("milestone.ink_label"), FontSize, Theme.TextMain);
            Ui.ThemedLabel(words.transform, Strings.T("milestone.ink_value", ("ink", Ui.InkText(ink))), FontSize,
                Theme.TextMain).fontStyle = FontStyle.Bold;
        }

        /// <summary>候选牌左上角五行圆标(稿 .el):18pt 圆、距牌边 6pt、底 = 属性字形色、白色宋体 10pt 粗
        /// → 38 / 13 / 21 逻辑单位。字取 CharInfo.ElementName(与字卡详情同一组 key)。</summary>
        private static void ElementBadge(Transform tile, Element element)
        {
            const float D = 38f, Inset = 13f;
            var badge = Ui.Panel(tile, "Element").AddComponent<Image>();
            badge.sprite = Theme.Circle;
            badge.color = Theme.GlyphColor(element);
            badge.raycastTarget = false;
            Ui.Anchor(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Inset, -Inset - D), new Vector2(Inset + D, -Inset));
            var label = Ui.ThemedLabel(badge.transform, CharInfo.ElementName(element), 21, Color.white,
                Theme.TitleFont);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            Ui.Stretch(label.rectTransform);
        }

        /// <summary>选中环的一层:圆角实色板,四边比牌外扩 <paramref name="outset"/>。</summary>
        private static void RoundRect(Transform parent, string name, Color color, float radius, float outset)
        {
            var image = Ui.CardPanel(parent, name, color, Mathf.RoundToInt(radius));
            image.raycastTarget = false;
            Ui.Anchor(image.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(-outset, -outset), new Vector2(outset, outset));
        }
    }
}
