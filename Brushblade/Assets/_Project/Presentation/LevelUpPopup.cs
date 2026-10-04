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
        private const int CapProbe = 1000;   // 远超任何属性封顶等级,取曲线终值当「封顶值」

        public static void Show(Transform root, MetaState meta, LevelUpSummary summary)
        {
            if (summary == null) return;
            var sheet = Ui.Sheet(root, "LevelUp", W, H, dismissable: false, replaceSameName: true,
                Theme.Scrim, out var content);
            // content 已是 Sheet 给的带内边距 VStack,直接在里面排,不再套一层
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 21;
            // 不强制撑满:开了 childForceExpandWidth,uGUI 会把每个子项的 flexible 抬成 1,
            // 「知道了」请求的 335 会被拉成整行宽(稿上钮 160pt 居中)。
            // 该撑满的(两栏、里程碑条)自己声明 flexibleWidth;其余按首选宽、随 UpperCenter 居中。
            layout.childForceExpandWidth = false;
            layout.padding = new RectOffset(38, 38, (int)PadV, (int)PadV);

            // 抬头两行给定高(= 字号 × 1.45,Noto 的行高),两栏的高要按它们反算,见 ColsHeight
            Ui.Sized(Ui.KickerRow(content, Strings.T("levelup.kicker"), 25, Theme.Primary), height: KickerH);
            Ui.Sized(Ui.ThemedLabel(content,
                Strings.T("levelup.level_range", ("from", summary.FromLevel), ("to", summary.ToLevel)),
                50, Theme.TextMain, Theme.TitleFont).gameObject, height: LevelH);

            var cols = Ui.Row(content, "Cols", 29);
            var colsElement = cols.AddComponent<LayoutElement>();
            colsElement.flexibleWidth = 1;
            colsElement.flexibleHeight = 1;
            cols.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;

            int owed = LevelRewardRules.OwedChests(meta);
            BuildStats(cols.transform, summary.FromLevel, summary.ToLevel);
            BuildChests(cols.transform, meta, summary.FromLevel, summary.ToLevel, owed,
                ColsHeight(summary.Milestones.Count));

            foreach (int lv in summary.Milestones)
                MilestoneBar(content, lv);

            Ui.PillButton(content, Strings.T("levelup.ok"), () => Object.Destroy(sheet),
                Theme.Cta, Color.white, 31, new Vector2(335, ButtonH));
        }

        // ---- 竖向预算(弹窗定高,两栏吃剩余高;升级奖励卡的立绘按它反算) ----
        private const float Spacing = 21f, PadV = 29f, KickerH = 36f, LevelH = 72f, ButtonH = 84f;
        private const float MileH = 63f;   // 稿 30pt

        /// <summary>两栏实得的高 = 卡内净高 − 抬头两行 − 里程碑条 − 钮 − 行距。
        /// 无里程碑 429、一条 345(属性栏要 30 + 5×(50+10) = 330,放得下)。</summary>
        private static float ColsHeight(int milestones) =>
            H - 2 * Ui.SheetBorder - 2 * PadV - KickerH - LevelH - ButtonH
            - milestones * MileH - (3 + milestones) * Spacing;

        /// <summary>里程碑提示条(稿 .mile):金系浅底、0.55 金描边,「Lv.20 里程碑」宋体 +「1,200 墨 + 金字
        /// 3 选 1 已解锁」+ 右端「回主界面后在角色页领取」。只提示、不放跳转钮 —— 局内到不了角色页。</summary>
        private static void MilestoneBar(Transform parent, int level)
        {
            var def = MilestoneRules.ForLevel(level);
            if (def == null) return;
            var edge = Theme.RarityColor(CardRarity.Gold);
            edge.a = 0.55f;
            var bar = Ui.OutlinedPanel(parent, "Milestone", Theme.GoldSoft, edge, 17, 2f, out var face);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // 填充层自己按锚点铺,不进行排
            Ui.Sized(bar.gameObject, height: MileH, flexWidth: 1);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(25, 25, 0, 0);
            row.spacing = 17;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            Ui.ThemedLabel(bar.transform, Strings.T("levelup.milestone_title", ("level", level)),
                23, Theme.GoldDeep, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(bar.transform, Strings.T("levelup.milestone_body",
                    ("ink", Ui.InkText(def.Value.Ink)), ("rarity", CharInfo.RarityName(def.Value.Rarity))),
                21, Theme.GoldText, null, TextAnchor.MiddleLeft);
            Ui.Sized(Ui.Panel(bar.transform, "Spacer"), flexWidth: 1);
            Ui.ThemedLabel(bar.transform, Strings.T("levelup.milestone_hint"), 19, Theme.GoldDeep, null,
                TextAnchor.MiddleRight);
        }

        private static void BuildStats(Transform parent, int from, int to)
        {
            var col = Ui.VStack(parent, "Stats", 10);
            col.AddComponent<LayoutElement>().flexibleWidth = 1;
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Ui.ThemedLabel(col.transform, Strings.T("levelup.stats_title") + " · " + Strings.T("levelup.stats_note"),
                21, Theme.TextDim, Theme.TitleFont, TextAnchor.MiddleLeft);
            StatRow(col.transform, Strings.T("levelup.stat.hp"), MetaRules.MaxHpFor(from), MetaRules.MaxHpFor(to), MetaRules.MaxHpFor(CapProbe), "", hp: true);
            StatRow(col.transform, Strings.T("levelup.stat.attack"), MetaRules.AttackFor(from), MetaRules.AttackFor(to), MetaRules.AttackFor(CapProbe), "");
            StatRow(col.transform, Strings.T("levelup.stat.defense"), MetaRules.DefenseFor(from), MetaRules.DefenseFor(to), MetaRules.DefenseFor(CapProbe), "");
            StatRow(col.transform, Strings.T("levelup.stat.dodge"), MetaRules.DodgeFor(from), MetaRules.DodgeFor(to), MetaRules.DodgeFor(CapProbe), "%");
            StatRow(col.transform, Strings.T("levelup.stat.speed"), MetaRules.SpeedFor(from), MetaRules.SpeedFor(to), MetaRules.SpeedFor(CapProbe), "");
        }

        /// <summary>一行「名 旧 → 新 +差」;没涨的行照列:真封顶写「已封顶」,只是这一段没跨档写「—」。
        /// 稿 .num:`panel-inset` 底条(稿 #F2EEE4 不在 tokens,取最近的 panel-inset)圆角 7pt,
        /// 旧值宋体 12pt `text-warm` → 箭头 9pt `text-warm` → 新值宋体 14pt 粗(生命 `fire-glyph`,
        /// 没涨 `text-dim`)→ 增量 9.5pt 粗 `upgrade-text` 靠右(没涨常规字重 `text-warm`)。
        /// Unity 一个 Text 只有一种字号字色,所以拆成四个 Text 横排;行高 50 不变,两栏的竖向预算不受影响。</summary>
        private static void StatRow(Transform parent, string name, int was, int now, int cap, string unit,
            bool hp = false)
        {
            bool up = now > was;
            var bg = Ui.CardPanel(parent, "Stat", Theme.PanelInset, 15);   // 稿圆角 7pt
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
            var row = Ui.Row(bg.transform, "Row", 15);
            Ui.Stretch((RectTransform)row.transform);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(17, 17, 0, 0);                // 稿 0 8pt
            Ui.ThemedLabel(row.transform, name, 20, Theme.TextDim, null, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 105;
            Ui.ThemedLabel(row.transform, $"{was}{unit}", 25, Theme.TextWarm, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(row.transform, "→", 19, Theme.TextWarm, null, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(row.transform, $"{now}{unit}", 29,
                    !up ? Theme.TextDim : hp ? Theme.GlyphColor(Element.Fire) : Theme.TextMain,
                    Theme.TitleFont, TextAnchor.MiddleLeft)
                .fontStyle = FontStyle.Bold;
            Ui.Sized(Ui.Panel(row.transform, "Spacer"), flexWidth: 1);
            var delta = Ui.ThemedLabel(row.transform,
                up ? Strings.T("levelup.stat_delta", ("value", $"{now - was}{unit}"))
                    : now == cap ? Strings.T("levelup.stat_capped") : Strings.T("levelup.stat_same"),
                20, up ? Theme.UpgradeText : Theme.TextWarm, null, TextAnchor.MiddleRight);
            if (up) delta.fontStyle = FontStyle.Bold;
        }

        // ---- 升级奖励卡(稿 .box:pt × 2.093) ----
        private const float ChestTitleH = 30f, ColGap = 10f;
        private const float CardPadTop = 13f, CardPadBottom = 19f, CardGap = 8f;
        private const float NameH = 40f, BandH = 33f, YieldH = 28f, SlotH = 26f;
        /// <summary>卡里除立绘外的定高:内边距 + 箱名 + 档位 chip + 产出 + 箱位 + 四道行距 = 191。</summary>
        private const float CardFixedH = CardPadTop + CardPadBottom + NameH + BandH + YieldH + SlotH + 4 * CardGap;
        private const float ArtMax = 180f;  // 稿 86pt
        private const float ArtMulti = 84f; // 原逐行版的立绘尺寸;多档时每张卡都用它

        /// <summary>按「Lv.from → Lv.to」这一段逐级查档,与标题同源(本次调用前已静默补发过的也算在内);
        /// 同档合并成一张卡「× N」(稿 MultiLevel 的读法)。没发出的那几只由 owed 另行提示。
        /// 卡放在竖向 ScrollList 里:一档时卡撑满列、立绘按剩余高反算(封顶 86pt,稿的 86pt 在它自己的
        /// 356pt 定高里其实放不下);多档(连升跨档,
        /// 当前数值下几乎碰不到)每张卡立绘 84,放不下就滚,第二张的顶会露出来提示还有。</summary>
        private static void BuildChests(Transform parent, MetaState meta, int from, int to, int owed, float colsH)
        {
            var col = Ui.VStack(parent, "Chests", ColGap);
            col.AddComponent<LayoutElement>().preferredWidth = 456; // 稿 218pt
            var colLayout = col.GetComponent<VerticalLayoutGroup>();
            colLayout.childForceExpandWidth = true;
            colLayout.childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(Ui.ThemedLabel(col.transform, Strings.T("levelup.reward_title"), 21, Theme.TextDim,
                Theme.TitleFont, TextAnchor.MiddleLeft).gameObject, height: ChestTitleH);

            var counts = new SortedDictionary<ChestTier, int>();
            for (int lv = from + 1; lv <= to; lv++)
            {
                var tier = LevelRewardRules.TierForLevel(lv);
                counts.TryGetValue(tier, out int n);
                counts[tier] = n + 1;
            }

            float listH = colsH - ChestTitleH - ColGap - (owed > 0 ? ColGap + Ui.ChipHeight(OwedFont) : 0f);
            // 一档:立绘吃掉卡里剩下的高(无里程碑 180 封顶、一条里程碑 114、再加欠箱 chip 73),卡恰好不滚
            float art = counts.Count == 1 ? Mathf.Clamp(listH - CardFixedH, 0f, ArtMax) : ArtMulti;
            var list = Ui.ScrollList(col.transform, "ChestList", ColGap, out var listContent);
            Ui.Sized(list, height: listH);
            int i = 0;
            foreach (var kv in counts)
            {
                bool last = ++i == counts.Count;  // 箱位只说一次,写在末张
                float cardH = CardFixedH + art - (last ? 0f : SlotH + CardGap);
                if (counts.Count == 1) cardH = Mathf.Max(listH, cardH);  // 一档时撑满列(稿 .box flex: 1)
                ChestCard(listContent, kv.Key, kv.Value, art, cardH, showSlots: last, meta);
            }
            if (owed > 0)
                Ui.Chip(col.transform, Strings.T("levelup.owed", ("count", owed)), Theme.WarningBg, Theme.WarningText, OwedFont);
        }

        private const int OwedFont = 19;

        /// <summary>一张箱卡:立绘 → 色点箱名 → 档位 chip「Lv.11–20」→ 产出「8 种 40 张 · 120 墨 · 4 小时」
        /// → (末张)「已放进箱位 3 / 4」。产出取 ChestRules 的种数 / 期望张数 / 墨锭 / 开启时长,与商城同源。</summary>
        private static void ChestCard(Transform parent, ChestTier tier, int count, float art, float height,
            bool showSlots, MetaState meta)
        {
            int t = (int)tier - 1;
            var card = Ui.OutlinedPanel(parent, "ChestCard", Theme.CardWhite, Theme.PanelBorder, 21, 2f, out var face);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Sized(card.gameObject, height: height);
            var stack = card.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(21, 21, (int)CardPadTop, (int)CardPadBottom);
            stack.spacing = CardGap;
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childForceExpandWidth = false;
            stack.childForceExpandHeight = false;

            ChestArt.Draw(card.transform, tier, ChestView.State.Idle, art);

            var name = Ui.Row(card.transform, "Name", 13);
            Ui.Sized(name, height: NameH);
            Ui.Sized(Ui.CardPanel(name.transform, "Dot", Theme.ChestColor(tier), 6).gameObject, 17, 17);
            Ui.ThemedLabel(name.transform, count > 1
                    ? Strings.T("levelup.chest_row", ("tierName", ChestRules.TierName(tier)), ("count", count))
                    : ChestRules.TierName(tier),
                29, Theme.TextMain, Theme.TitleFont);

            if (LevelRewardRules.TryGetLevelBand(tier, out int min, out int max))
                Ui.Chip(card.transform, max == int.MaxValue
                        ? Strings.T("levelup.chest_band_open", ("min", min))
                        : Strings.T("levelup.chest_band", ("min", min), ("max", max)),
                    Theme.PanelInset, Theme.TextDim, 18, padX: 0, padY: (int)BandH - 18,
                    border: Theme.PanelBorder, borderThickness: 2f);

            long seconds = ChestRules.DurationSeconds[t];
            string duration = seconds % 3600 == 0
                ? Strings.T("levelup.duration_hours", ("hours", seconds / 3600))
                : Strings.T("levelup.duration_minutes", ("minutes", seconds / 60));
            Ui.Sized(Ui.ThemedLabel(card.transform, Strings.T("levelup.chest_yield",
                    ("kinds", ChestRules.KindCount[t]), ("cards", ChestRules.ExpectedCards(tier)),
                    ("ink", Ui.InkText(ChestRules.InkReward[t])), ("duration", duration)),
                20, Theme.TextDim).gameObject, height: YieldH);

            if (!showSlots) return;
            var slot = Ui.Row(card.transform, "Slots", 6);
            Ui.Sized(slot, height: SlotH);
            Ui.ThemedLabel(slot.transform, Strings.T("levelup.slot_label"), 19, Theme.LockGray);
            Ui.ThemedLabel(slot.transform, Strings.T("levelup.slot_count",
                    ("count", meta.Chests.Count), ("limit", ChestRules.SlotLimit)), 19, Theme.UpgradeText)
                .fontStyle = FontStyle.Bold;
        }
    }
}
