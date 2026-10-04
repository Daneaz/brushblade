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
        // 抬头 36 + Lv 102 + 墨锭行 42 + 小节行 33 + 候选排 329(环 288 + 10 + chip 31)+ 脚行 84
        // = 626,加 6 个行距。原来行距 21、卡高 745 → 752 > 卡内净高 684,竖排按比例把子项往 0 收,
        // 候选牌高被压到 ≈ 239 而宽不变,0.8 竖版牌框被拉胖。现在行距回到 Sheet 默认 14、卡高 785
        // → 710 ≤ 724,余下的交给弹簧。785 在 900 高的画布(按高匹配)里扣掉底部安全区仍有余量。
        private const float W = 1256f, H = 785f;           // 稿 600×356pt,高见上
        private static readonly Vector2 TileSize = new(218f, 272f); // 稿 104×130pt
        private const float RingPad = 8f;                  // 选中环比牌外扩一圈(稿 box-shadow 3+2pt)

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

            Ui.ThemedLabel(content, Strings.T("milestone.kicker"), 25, Theme.GoldDeep, Theme.TitleFont);
            Ui.ThemedLabel(content, Strings.T("character.ms_level", ("level", level)), 71,
                Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(content,
                Strings.T("milestone.ink", ("ink", Ui.InkText(def.Value.Ink))), 29);

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
            picks.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var rings = new List<(string id, Image ring)>();
            foreach (var id in offer)
            {
                if (!graph.TryGet(id, out var c)) continue; // 字表删字后的幽灵候选:不画,也领不走
                bool owned = meta.OwnedCards.Contains(id);
                var cell = Ui.VStack(picks.transform, "Cand", 10);
                var ring = Ui.Panel(cell.transform, "Ring").AddComponent<Image>();
                ring.sprite = Theme.Rounded(18);
                ring.type = Image.Type.Sliced;
                ring.color = Color.clear;
                Ui.Sized(ring.gameObject, TileSize.x + RingPad * 2, TileSize.y + RingPad * 2);
                var pickId = id;
                var tile = Ui.GlyphTile(ring.transform, c, false, () =>
                {
                    picked = pickId;
                    foreach (var r in rings) r.ring.color = r.id == pickId ? Theme.InkSoft : Color.clear;
                    okFace.color = Theme.Cinnabar;
                    okLabel.color = Color.white;
                }, TileSize);
                Ui.Anchor((RectTransform)tile.transform, Vector2.zero, Vector2.one,
                    new Vector2(RingPad, RingPad), new Vector2(-RingPad, -RingPad));
                Ui.Chip(cell.transform,
                    owned ? Strings.T("milestone.tag_dup") : Strings.T("milestone.tag_new"),
                    owned ? Theme.PanelInset : Theme.GoldSoft,
                    owned ? Theme.TextDim : Theme.GoldDeep, 19,
                    border: owned ? Theme.PanelBorder : Theme.Gold);
                rings.Add((id, ring));
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
            // 没选之前是灰钮(稿 .pill.off),点了也不动;选了一张才换成朱砂主钮
            okFace = (Image)ok.targetGraphic;
            okLabel = ok.GetComponentInChildren<Text>();
        }
    }
}
