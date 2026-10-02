using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const float W = 1256f, H = 745f;           // 稿 600×356pt
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
            layout.spacing = 21;
            layout.childForceExpandWidth = true;
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
                Strings.T("milestone.ink", ("ink", def.Value.Ink.ToString("N0", CultureInfo.InvariantCulture))), 29);

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
