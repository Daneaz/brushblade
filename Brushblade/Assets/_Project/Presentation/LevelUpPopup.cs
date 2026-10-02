using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>升级弹窗(spec 2026-10-02 §5.2;稿 角色页画布 LevelUp.dc.html)。
    /// 在安全层 / 登塔结算页之上弹,一次说清这一段升了几级。局内进不了角色页,所以里程碑只提示、不放跳转钮。</summary>
    public static class LevelUpPopup
    {
        private const float W = 1256f, H = 745f; // 稿 600×356pt

        public static void Show(Transform root, MetaState meta, LevelUpSummary summary,
            IReadOnlyList<LevelChestGrant> grants)
        {
            if (summary == null) return;
            var sheet = Ui.Sheet(root, "LevelUp", W, H, dismissable: false, replaceSameName: true,
                Theme.Scrim, out var content);
            var stack = Ui.VStack(content, "Stack", 21);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(38, 38, 29, 29);

            Ui.ThemedLabel(stack.transform, Strings.T("levelup.kicker"), 25, Theme.CinnabarDark, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                Strings.T("levelup.level_range", ("from", summary.FromLevel), ("to", summary.ToLevel)),
                50, Theme.TextMain, Theme.TitleFont);

            var cols = Ui.Row(stack.transform, "Cols", 29);
            cols.AddComponent<LayoutElement>().flexibleHeight = 1;
            cols.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;

            BuildStats(cols.transform, summary.FromLevel, summary.ToLevel);
            BuildChests(cols.transform, grants, LevelRewardRules.OwedChests(meta));

            foreach (int lv in summary.Milestones)
                Ui.Chip(stack.transform, Strings.T("levelup.milestone", ("level", lv)),
                    Theme.GoldSoft, Theme.GoldText, 21, border: Theme.GoldBorder);

            Ui.PillButton(stack.transform, Strings.T("levelup.ok"), () => Object.Destroy(sheet),
                Theme.Cinnabar, Color.white, 31, new Vector2(335, 84));
        }

        private static void BuildStats(Transform parent, int from, int to)
        {
            var col = Ui.VStack(parent, "Stats", 10);
            col.AddComponent<LayoutElement>().flexibleWidth = 1;
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Ui.ThemedLabel(col.transform, Strings.T("levelup.stats_title") + " · " + Strings.T("levelup.stats_note"),
                21, Theme.TextDim, Theme.TitleFont, TextAnchor.MiddleLeft);
            StatRow(col.transform, Strings.T("levelup.stat.hp"), MetaRules.MaxHpFor(from), MetaRules.MaxHpFor(to), "");
            StatRow(col.transform, Strings.T("levelup.stat.attack"), MetaRules.AttackFor(from), MetaRules.AttackFor(to), "");
            StatRow(col.transform, Strings.T("levelup.stat.defense"), MetaRules.DefenseFor(from), MetaRules.DefenseFor(to), "");
            StatRow(col.transform, Strings.T("levelup.stat.dodge"), MetaRules.DodgeFor(from), MetaRules.DodgeFor(to), "%");
            StatRow(col.transform, Strings.T("levelup.stat.speed"), MetaRules.SpeedFor(from), MetaRules.SpeedFor(to), "");
        }

        /// <summary>一行「名 旧 → 新 +差」;没涨的行照列(写「已封顶」),藏了玩家会以为属性丢了。</summary>
        private static void StatRow(Transform parent, string name, int was, int now, string unit)
        {
            var row = Ui.Row(parent, "Stat", 15);
            row.AddComponent<LayoutElement>().preferredHeight = 50;
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(row.transform, name, 20, Theme.TextDim, null, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 105;
            Ui.ThemedLabel(row.transform, $"{was}{unit} → {now}{unit}", 27, Theme.TextMain, Theme.TitleFont,
                TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(row.transform,
                now > was ? Strings.T("levelup.stat_delta", ("value", $"{now - was}{unit}")) : Strings.T("levelup.stat_capped"),
                20, now > was ? Theme.UpgradeText : Theme.TextDim, null, TextAnchor.MiddleRight);
        }

        /// <summary>同档合并成一行「×N」(稿 MultiLevel 的读法),档位再多也不撑破。</summary>
        private static void BuildChests(Transform parent, IReadOnlyList<LevelChestGrant> grants, int owed)
        {
            var col = Ui.VStack(parent, "Chests", 10);
            col.AddComponent<LayoutElement>().preferredWidth = 456; // 稿 218pt
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Ui.ThemedLabel(col.transform, Strings.T("levelup.reward_title"), 21, Theme.TextDim, Theme.TitleFont,
                TextAnchor.MiddleLeft);
            var counts = new SortedDictionary<ChestTier, int>();
            foreach (var g in grants)
            {
                counts.TryGetValue(g.Tier, out int n);
                counts[g.Tier] = n + 1;
            }
            foreach (var kv in counts)
            {
                var row = Ui.Row(col.transform, "ChestRow", 12);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                ChestArt.Draw(row.transform, kv.Key, ChestView.State.Idle, 84f);
                Ui.Chip(row.transform,
                    Strings.T("levelup.chest_row", ("tierName", ChestRules.TierName(kv.Key)), ("count", kv.Value)),
                    Theme.CardWhite, Theme.TextMain, 25, border: Theme.ChestColor(kv.Key), borderThickness: 3f);
            }
            if (owed > 0)
                Ui.Chip(col.transform, Strings.T("levelup.owed", ("count", owed)), Theme.WarnBg, Theme.WarnText, 19);
        }
    }
}
