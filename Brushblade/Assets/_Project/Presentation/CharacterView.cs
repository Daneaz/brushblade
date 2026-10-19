using System;
using System.Collections.Generic;
using System.Globalization;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>角色页(spec 2026-10-02 §6.2;稿 Character.dc.html,等级奖励轨道按 2026-10-19 稿
    /// character-chest-claim):左档案、右上等级奖励长轨道、右下统计四页签。
    /// 与稿的差异不止一条(顶栏排布、字距、若干色值与尺寸),逐条见 docs/design/current/CharacterScreen/README.md
    /// 的「与拍板稿的差异」节。</summary>
    public sealed class CharacterView : MonoBehaviour
    {
        private enum Tab { Chest, Tower, Battle, Econ }

        private RecipeGraph _graph;
        private CampaignConfig _campaign;
        private MetaState _meta;
        private IReadOnlyList<string> _cardPool;
        private ITimeSource _time;
        private Action _save, _onBack;
        private Tab _tab = Tab.Chest;

        // ---- 滚动位置:切页签、领奖励都会整页重建,不记下来的话每点一次就弹回最左 ----
        // 两层横滚上下分开、不嵌套(稿 character-chest-claim §1「与右栏横滚的手势冲突」):
        // 上面是等级奖励轨道自己的 ScrollRect,下面是只包统计面板的 HScrollFill。
        // 位置都存 Content.anchoredPosition.x(≤ 0)。Rebuild 前从下面两个引用读,不用
        // GetComponentInChildren —— 子树里有两只 ScrollRect,MilestonePickSheet 也挂在本节点下。
        private ScrollRect _trackScroll, _statsScroll;
        /// <summary>统计面板(HScrollFill)的横向位置;null = 本次打开还没重建过(停在最左)。</summary>
        private float? _rightScrollX;
        /// <summary>等级奖励轨道的横向位置;null = 本次打开还没定过位 → 首次按 <see cref="LevelRewardRules.FocusLevel"/> 聚焦。</summary>
        private float? _trackScrollX;

        // ---- 轨道(边缘小签随滚动刷新要用) ----
        private readonly List<int> _trackLevels = new();
        private RectTransform _trackContent, _trackViewport;
        private GameObject _edgeLeft, _edgeRight;
        private Text _edgeLeftText, _edgeRightText;

        private const float TopBarH = 80f;     // 与 PerkView 同
        private const float ProfW = 419f;      // 稿 200pt
        private const float MsH = 327f;        // 稿 156pt
        private const float Gap = 21f;         // 稿 10pt
        private const int PanelPad = 21;       // 稿 10pt
        /// <summary>统计面板排版的最小宽:932×430pt 基准机上右栏的实际宽
        /// (画布 900×2796/1290 ≈ 1951 − 两侧安全区 123×2 − 左栏 419 − 栏距 21)。全部间距都按这个宽验算过;
        /// 16:9(画布宽 1600)下右栏只剩约 914,统计栏会溢出,所以统计面板套在横向滚动区里,
        /// 不足这个宽时按它排、横向滚出来(2026-10-04 用户拍板)。等级奖励面板不在里面 —— 它宽 = 右栏视口,
        /// 轨道自己横滚(2026-10-19 稿 character-chest-claim)。</summary>
        private const float RightMinW = 1265f;

        // ---- 等级奖励轨道(稿 character-chest-claim §1;pt × 2.093) ----
        private const float NodeW = 115f;          // 格宽 54.95pt(= 旧 10 格等分时的格宽)
        private const float NodeGap = 8f;          // 格距 3.82pt
        private const float CellStride = NodeW + NodeGap;
        private const float TrackPadTop = 6f;      // 稿 .strip top 3pt:给里程碑钉的外圈留位
        private const float PinRow = 42f;          // 里程碑大钉 20pt;普通小钉上下各让 7 与它对齐
        private const float PinSmall = 27f;        // 普通级小钉 13pt
        private const float FadeW = 38f;           // 两侧纸色渐隐 18pt
        private const int MilestoneTableStep = 5;  // Lv.50 内每 5 级一个里程碑(之后见 MilestoneRules.AfterStep)

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void Init(RecipeGraph graph, CampaignConfig campaign, MetaState meta,
            IReadOnlyList<string> cardPool, ITimeSource time, Action save, Action onBack)
        {
            _graph = graph; _campaign = campaign; _meta = meta; _cardPool = cardPool; _time = time;
            _save = save; _onBack = onBack;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_statsScroll != null) _rightScrollX = _statsScroll.content.anchoredPosition.x;
            if (_trackScroll != null) _trackScrollX = _trackScroll.content.anchoredPosition.x;
            StopAllCoroutines();   // 上一轮的聚焦 / 小签刷新别作用到即将销毁的轨道上
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);
            var (padSide, padBottom) = SafeArea.MissingInset();
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));

            BuildTopBar(content.transform);

            var body = Ui.Row(content.transform, "Body", Gap);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(0, 17), new Vector2(0, -TopBarH));
            var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.childForceExpandWidth = false;

            BuildProfile(body.transform);

            var right = Ui.VStack(body.transform, "Right", Gap);
            right.AddComponent<LayoutElement>().flexibleWidth = 1;
            var rightLayout = right.GetComponent<VerticalLayoutGroup>();
            rightLayout.childForceExpandWidth = true;
            rightLayout.childForceExpandHeight = false;

            BuildRewards(right.transform);   // 宽 = 右栏视口,不进 HScrollFill

            var statsScroll = Ui.HScrollFill(right.transform, "StatsScroll", RightMinW, out var statsContent);
            statsScroll.AddComponent<LayoutElement>().flexibleHeight = 1;
            var statsHolder = Ui.VStack(statsContent, "StatsHolder", 0);
            Ui.Stretch((RectTransform)statsHolder.transform);
            var holderLayout = statsHolder.GetComponent<VerticalLayoutGroup>();
            holderLayout.childForceExpandWidth = true;
            holderLayout.childForceExpandHeight = false;
            BuildStats(statsHolder.transform);
            _statsScroll = statsScroll.GetComponent<ScrollRect>();
            if (_rightScrollX is float x)
                statsContent.anchoredPosition = new Vector2(x, 0f);   // Clamped 会在越界时自己收回

            if (_trackScrollX is float tx)
            {
                _trackContent.anchoredPosition = new Vector2(tx, 0f);
                StartCoroutine(RefreshEdgesNextFrame());
            }
            else
                StartCoroutine(ScrollToClaimable());
        }

        /// <summary>首次打开:把 <see cref="LevelRewardRules.FocusLevel"/> 那格滚到视口左侧、左边留 1 格上下文;
        /// 都领完(聚焦格 = 当前等级且它没东西可领)→ 当前等级那格落在视口约 60% 处,右边露出后几级。
        /// 等一帧 —— 视口宽要布局算完才有。</summary>
        private System.Collections.IEnumerator ScrollToClaimable()
        {
            _trackScrollX = 0f;
            yield return null;
            if (_trackContent == null || _trackViewport == null) yield break;
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            int focus = LevelRewardRules.FocusLevel(_meta);
            int index = _trackLevels.IndexOf(focus);
            if (index < 0) { RefreshEdges(); yield break; }   // Lv.1:轨道从 Lv.2 起,停在最左
            float offset = focus == level && !IsCellClaimable(level)
                ? index * CellStride - _trackViewport.rect.width * 0.6f
                : (index - 1) * CellStride;
            SetTrackOffset(offset);
        }

        private System.Collections.IEnumerator RefreshEdgesNextFrame()
        {
            yield return null;
            RefreshEdges();
        }

        // ================= 顶栏 =================

        /// <summary>骨架照抄 PerkView.BuildTopBar(去掉渐隐与手势提示):标题 40 / 副标 23 /
        /// 墨锭 25 / 返回 25 + (130, 63) —— 右侧那对在每一页的位置都必须一样。</summary>
        private void BuildTopBar(Transform parent)
        {
            var bar = Ui.Panel(parent, "TopBar");
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -TopBarH), Vector2.zero);

            var top = Ui.Row(bar.transform, "Top", 21);
            Ui.Stretch((RectTransform)top.transform);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            Ui.ThemedLabel(top.transform, Strings.T("character.title"), 40, Theme.TextMain, Theme.TitleFont);
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            Ui.ThemedLabel(top.transform,
                Strings.T("character.subtitle", ("level", level),
                    ("rank", EndlessRules.RankTitle(_meta.BestDepth)), ("best", _meta.BestDepth)),
                23, Theme.TextDim);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.Info, Color.white, 25, new Vector2(130, 63)); // 返回地图 = Info 浓墨(2026-10-04 五色语义)
        }

        // ================= 左:执笔人档案 =================

        private void BuildProfile(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Prof", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().preferredWidth = ProfW;
            var stack = PaddedStack(panel.transform, "Stack", 19, new RectOffset(25, 25, 25, 25));

            int level = MetaRules.LevelProgress(_meta.CharacterXp, out int into, out int need);

            // 头像 + 名 + 等级/段位(与 MapView 同一组 key)
            var avatar = Ui.Row(stack, "Avatar", 21);
            avatar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.CircleGlyph(avatar.transform, Strings.T("map.hero.face"), Theme.Ink, Theme.Paper, 100);
            var names = Ui.VStack(avatar.transform, "Names", 4);
            names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(names.transform, Strings.T("map.hero.name"), 36, Theme.TextMain, Theme.TitleFont,
                TextAnchor.MiddleLeft);
            Ui.ThemedLabel(names.transform,
                Strings.T("map.hero.level_line", ("level", level), ("rank", EndlessRules.RankTitle(_meta.BestDepth))),
                21, Theme.TextDim, null, TextAnchor.MiddleLeft);

            // 经验:等级与进度同源于 MetaRules.LevelProgress
            var xp = Ui.VStack(stack, "Xp", 6);
            xp.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            var xpRow = Ui.Row(xp.transform, "XpRow", 8);
            xpRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            FlexLabel(xpRow.transform, Strings.T("character.xp_line", ("next", level + 1)), 19, Theme.LockGray);
            Ui.ThemedLabel(xpRow.transform, Strings.T("map.hero.xp_value", ("into", into), ("need", need)),
                19, Theme.LockGray, null, TextAnchor.MiddleRight);
            Ui.Bar(xp.transform, need > 0 ? (float)into / need : 0f, Theme.Info, new Vector2(370, 10));

            var ttl = TitleRow(stack, Strings.T("character.attrs_title"));
            Ui.ThemedLabel(ttl, Strings.T("character.attrs_next"), 19, Theme.LockGray);
            Rule(ttl);

            // 读的是战斗真正吃到的那份配置(与 MapView 同源),增量按等级曲线算下一级差多少
            var stats = MetaRules.BuildBattleConfig(_meta, _campaign.DropTable);
            int next = level + 1;
            var attrs = Ui.VStack(stack, "Attrs", 6);
            attrs.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            AttrRow(attrs.transform, Strings.T("map.hero.stat.hp"), stats.PlayerMaxHp.ToString(), Theme.Cinnabar,
                MetaRules.MaxHpFor(next) - MetaRules.MaxHpFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.attack"), stats.PlayerAttack.ToString(), Theme.TextMain,
                MetaRules.AttackFor(next) - MetaRules.AttackFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.defense"), stats.PlayerDefense.ToString(), Theme.TextMain,
                MetaRules.DefenseFor(next) - MetaRules.DefenseFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.dodge"),
                Strings.T("map.hero.stat_percent", ("value", stats.PlayerDodge)), Theme.TextMain,
                MetaRules.DodgeFor(next) - MetaRules.DodgeFor(level), "%");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.speed"), stats.PlayerSpeed.ToString(), Theme.TextMain,
                MetaRules.SpeedFor(next) - MetaRules.SpeedFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.ap"), stats.ApPerTurn.ToString(), Theme.TextMain,
                0, ""); // AP 不随等级成长,恒写「—」

            Spring(stack); // 封顶说明钉在面板底(稿 margin-top:auto)
            var note = Ui.ThemedLabel(stack, Strings.T("character.cap_note"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>一行属性:名靠左、值靠右、下一级增量在最右一小列;增量为 0 写「—」。</summary>
        private static void AttrRow(Transform parent, string name, string value, Color valueColor,
            int delta, string unit)
        {
            var bg = Ui.CardPanel(parent, "Attr", Theme.PanelInset, 12);
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;   // 稿 21pt
            var row = Ui.Row(bg.transform, "Row", 12);
            Ui.Stretch((RectTransform)row.transform);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            FlexLabel(row.transform, name, 21, Theme.TextDim);
            Ui.ThemedLabel(row.transform, value, 27, valueColor, Theme.TitleFont, TextAnchor.MiddleRight);
            var d = Ui.ThemedLabel(row.transform,
                delta > 0 ? Strings.T("levelup.stat_delta", ("value", $"{delta}{unit}"))
                    : Strings.T("character.attr_capped"),
                18, delta > 0 ? Theme.UpgradeText : Theme.LockGray, null, TextAnchor.MiddleRight);
            d.gameObject.AddComponent<LayoutElement>().preferredWidth = 54;     // 稿 26pt
        }

        // ================= 右上:等级奖励长轨道 =================
        // 稿 docs/design/drafts/character-chest-claim/README.md §1:每级一格(Lv.2 → TrackEnd),每格挂该级宝箱,
        // 里程碑等级的格子同时挂里程碑奖励。轨道是面板内独立的横向 ScrollRect(只横向、惯性、无滚动条)。

        private void BuildRewards(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Rewards", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().preferredHeight = MsH;
            var stack = PaddedStack(panel.transform, "Stack", 17,
                new RectOffset(PanelPad, PanelPad, PanelPad, 19));

            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            BuildRewardsTitle(stack, level);

            // ---- 滚动区:ScrollRect → Viewport(RectMask2D + 透明接射线底)→ Content(横排格子) ----
            var trackGo = Ui.Panel(stack, "Track");
            trackGo.AddComponent<LayoutElement>().flexibleHeight = 1;
            var scroll = trackGo.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;

            var viewport = Ui.Panel(trackGo.transform, "Viewport");
            _trackViewport = (RectTransform)viewport.transform;
            Ui.Stretch(_trackViewport);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0); // 格缝里也能拖,见 Ui.ScrollList

            _trackLevels.Clear();
            int end = LevelRewardRules.TrackEnd(_meta);
            for (int lv = LevelRewardRules.TrackStart; lv <= end; lv++) _trackLevels.Add(lv);
            int count = _trackLevels.Count;
            float contentW = count * NodeW + Math.Max(0, count - 1) * NodeGap;

            var contentGo = Ui.Row(viewport.transform, "Content", NodeGap);
            _trackContent = (RectTransform)contentGo.transform;
            _trackContent.anchorMin = Vector2.zero;            // 锚左、竖向铺满
            _trackContent.anchorMax = new Vector2(0f, 1f);
            _trackContent.pivot = new Vector2(0f, 0.5f);
            _trackContent.anchoredPosition = Vector2.zero;
            _trackContent.sizeDelta = new Vector2(contentW, 0f);
            var rowLayout = contentGo.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.padding = new RectOffset(0, 0, (int)TrackPadTop, 0);

            // 底线:从第一格的钉连到最后一格,染到当前等级那格(稿 .rail)
            int lastReached = -1;
            for (int i = 0; i < count; i++)
                if (_trackLevels[i] <= level) lastReached = i;
            var rail = Ui.Bar(contentGo.transform,
                count > 1 && lastReached > 0 ? (float)lastReached / (count - 1) : 0f,
                Theme.RarityGold, new Vector2(0, 6));
            rail.GetComponent<LayoutElement>().ignoreLayout = true;
            float pinCenter = TrackPadTop + PinRow / 2f;
            Ui.Anchor((RectTransform)rail.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(NodeW / 2f, -pinCenter - 3), new Vector2(-NodeW / 2f, -pinCenter + 3));

            bool slotsFull = _meta.Chests.Count >= ChestRules.SlotLimit;
            foreach (int lv in _trackLevels)
                RewardNode(contentGo.transform, lv, level, slotsFull);

            scroll.viewport = _trackViewport;
            scroll.content = _trackContent;
            _trackScroll = scroll;

            // 两侧纸色渐隐(提示还能滚)+ 屏外可领小签;都压在滚动区之上、不随内容走
            EdgeFade(trackGo.transform, left: true);
            EdgeFade(trackGo.transform, left: false);
            (_edgeLeft, _edgeLeftText) = EdgePill(trackGo.transform, left: true);
            (_edgeRight, _edgeRightText) = EdgePill(trackGo.transform, left: false);
            scroll.onValueChanged.AddListener(_ => RefreshEdges());
        }

        /// <summary>标题行:「等级奖励」+「每级 1 只宝箱 · 每 5 级里程碑」+ 分隔线 + 右端「待领 宝箱 N · 里程碑 M · 箱位 n / 4」。
        /// 没有待领时只写「箱位 n / 4」;箱位数字 `upgrade-text` 粗体,满了 `cinnabar-dark`(稿 .slots b / b.full)。</summary>
        private void BuildRewardsTitle(Transform stack, int level)
        {
            var ttl = TitleRow(stack, Strings.T("character.rewards.title"));
            // 稿:当前等级 ≥ 50 时轨道后面的里程碑都是每 10 级一个
            int step = level >= MilestoneRules.TableEnd ? MilestoneRules.AfterStep : MilestoneTableStep;
            Ui.ThemedLabel(ttl, Strings.T("character.rewards.sub", ("step", step)), 19, Theme.LockGray);
            Rule(ttl);

            int pendingChests = LevelRewardRules.PendingChestCount(_meta);
            int pendingMilestones = 0;
            foreach (var m in MilestoneRules.MilestonesUpTo(level))
                if (!_meta.ClaimedMilestones.Contains(m.Level)) pendingMilestones++;
            string pending = "";
            if (pendingChests > 0 || pendingMilestones > 0)
            {
                pending = Strings.T("character.rewards.pending", ("chests", pendingChests));
                if (pendingMilestones > 0)
                    pending += Strings.T("character.rewards.pending_ms", ("count", pendingMilestones));
                pending += " · ";
            }
            // 数字段整体着色:开标签挂在 count 前、闭标签挂在 limit 后,「n / 4」一起变色加粗
            bool full = _meta.Chests.Count >= ChestRules.SlotLimit;
            string hex = ColorUtility.ToHtmlStringRGB(full ? Theme.CinnabarDark : Theme.UpgradeText);
            string slots = Strings.T("character.rewards.slots",
                ("count", $"<color=#{hex}><b>{_meta.Chests.Count}"), ("limit", $"{ChestRules.SlotLimit}</b></color>"));
            var label = Ui.ThemedLabel(ttl, pending + slots, 19, Theme.LockGray, null, TextAnchor.MiddleRight);
            label.supportRichText = true;
        }

        private enum ChestMark { Ready, Done, Locked }

        /// <summary>一格:顶上一颗钉 + 下面一张卡。普通格「Lv.N → 箱立绘 → 箱名 → 底行」;里程碑格
        /// 「Lv.N + 小箱 → 墨锭 → 金字 3 选 1 → 底行」。底行五态见稿 §1 的表 —— **里程碑优先**。</summary>
        private void RewardNode(Transform parent, int lv, int level, bool slotsFull)
        {
            var ms = MilestoneRules.ForLevel(lv);
            bool isMs = ms.HasValue;
            bool reached = lv <= level;
            bool chestReady = LevelRewardRules.IsChestClaimable(_meta, lv);
            bool chestDone = reached && _meta.ClaimedLevelChests.Contains(lv);
            bool msReady = isMs && MilestoneRules.IsClaimable(_meta, lv);
            bool msDone = isMs && reached && _meta.ClaimedMilestones.Contains(lv);
            bool allDone = reached && !chestReady && !msReady;
            var mark = chestDone ? ChestMark.Done : reached ? ChestMark.Ready : ChestMark.Locked;
            var tier = LevelRewardRules.TierForLevel(lv);

            var node = Ui.VStack(parent, "Node", 4);   // 钉与卡间距 2pt
            Ui.Sized(node, width: NodeW);
            var nodeLayout = node.GetComponent<VerticalLayoutGroup>();
            nodeLayout.childAlignment = TextAnchor.UpperCenter;
            nodeLayout.childForceExpandWidth = false;

            Pin(node.transform, isMs, allDone, reached && (chestReady || msReady));

            // 卡:里程碑格 = gold-soft 淡化底 + rarity-gold 60% 描边;普通格已领完压 panel-inset,其余 card-face
            var gold = Theme.RarityGold;
            var card = Ui.OutlinedPanel(node.transform, "Card",
                isMs ? MilestoneCardFill : allDone ? Theme.PanelInset : Theme.CardWhite,
                isMs ? new Color(gold.r, gold.g, gold.b, 0.6f) : Theme.PanelBorder, 17, 2f);
            var cardElement = card.gameObject.AddComponent<LayoutElement>();
            cardElement.flexibleWidth = 1;
            cardElement.flexibleHeight = 1;
            var cell = PaddedStack(card.transform, "Cell", 4, new RectOffset(6, 6, 8, 8));
            var cellLayout = cell.GetComponent<VerticalLayoutGroup>();
            cellLayout.childAlignment = TextAnchor.UpperCenter;
            cellLayout.childForceExpandWidth = false;

            // 三位数等级(Lv.100 起)「Lv.100」+ 小箱会超出卡内净宽 99,字号 25 → 21
            int lvFont = lv >= 100 ? 21 : 25;
            var lvColor = allDone ? Theme.TextDim : Theme.TextMain;
            if (isMs)
            {
                var lvRow = Ui.Row(cell, "LvRow", 4);
                LevelLabel(lvRow.transform, lv, lvFont, lvColor);
                ChestIcon(lvRow.transform, tier, 23f, mark);

                var def = ms.Value;
                var reward = Ui.VStack(cell, "Milestone", 4);
                if (msDone) reward.AddComponent<CanvasGroup>().alpha = 0.55f;   // 稿 .mrow.done
                Ui.IngotLabel(reward.transform, Ui.InkText(def.Ink), 20);
                var pick = Ui.Row(reward.transform, "Pick", 5);
                Dot(pick.transform, Theme.RarityColor(def.Rarity), 13);
                Ui.ThemedLabel(pick.transform,
                    Strings.T("character.ms_pick", ("rarity", CharInfo.RarityName(def.Rarity))), 17, Theme.TextDim)
                    .raycastTarget = false;
            }
            else
            {
                LevelLabel(cell, lv, lvFont, lvColor);
                ChestIcon(cell, tier, 50f, mark);
                Ui.ThemedLabel(cell, ChestRules.TierName(tier), 17, reached ? Theme.TextDim : Theme.LockGray)
                    .raycastTarget = false;
            }

            Spring(cell); // 底行钉在卡底
            int claimLevel = lv;
            if (msReady)
                CellButton(cell, Strings.T("character.rewards.claim_ms"),
                    () => MilestonePickSheet.Show(transform, _graph, _meta, claimLevel, _cardPool, _save, Rebuild));
            else if (chestReady && slotsFull)
                CellButton(cell, Strings.T("character.rewards.slots_full"), null);
            else if (chestReady)
                CellButton(cell, isMs ? Strings.T("character.rewards.claim_chest") : Strings.T("character.ms_claim"),
                    () => ClaimChest(claimLevel));
            else if (reached)
                Ui.ThemedLabel(cell, Strings.T("character.ms_claimed"), 18, Theme.LockGray);
            else
                Ui.ThemedLabel(cell, Strings.T("character.ms_gap", ("gap", lv - level)), 18, Theme.LockGray);
        }

        /// <summary>领一只每级宝箱:规则侧拒绝(连点、箱位刚满)时不改状态;成功先落盘再重建,
        /// 重建保持轨道滚动位置(<see cref="_trackScrollX"/>)。</summary>
        private void ClaimChest(int level)
        {
            if (LevelRewardRules.TryClaimChest(_meta, level, _cardPool, _time))
                _save();
            Rebuild();
        }

        /// <summary>稿:里程碑格卡底「gold-soft 淡化(#FBF5E4)」。#FBF5E4 不在 tokens,按稿的说法取
        /// gold-soft 与 card-face(Theme.CardWhite)各半 ≈ #FAF6EA。</summary>
        private static Color MilestoneCardFill => Color.Lerp(Theme.GoldSoft, Theme.CardWhite, 0.5f);

        private static void LevelLabel(Transform parent, int lv, int size, Color color)
        {
            // 宋体行高 ≈ 字号 × 1.44,布局盒按稿给 31(15pt),字照常居中画出(同 PityBlock 的做法)
            var label = Ui.ThemedLabel(parent, Strings.T("character.ms_level", ("level", lv)), size, color,
                Theme.TitleFont);
            label.raycastTarget = false;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 31;
        }

        /// <summary>钉:里程碑大钉 42 外加赭金描圈(gold-soft 2pt + rarity-gold 1pt),普通小钉 27 上下各让 7。
        /// 全领完 `jade` + 白勾;有可领 `cta`;未达 `panel-border` 描圈。</summary>
        private static void Pin(Transform parent, bool milestone, bool done, bool ready)
        {
            var box = Ui.Panel(parent, "PinBox");
            Ui.Sized(box, PinRow, PinRow);
            float d = milestone ? PinRow : PinSmall;
            if (milestone)
            {
                Disc(box.transform, "RingOuter", Theme.RarityGold, d + 12);
                Disc(box.transform, "RingInner", Theme.GoldSoft, d + 8);
            }
            if (done)
            {
                Disc(box.transform, "Face", Theme.Success, d);
                if (Icons.Get("check") is { } check)
                {
                    var icon = Ui.Panel(box.transform, "Check").AddComponent<Image>();
                    icon.sprite = check;
                    icon.color = Color.white;
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    CenterSize(icon.rectTransform, d * 0.52f);
                }
            }
            else if (ready)
                Disc(box.transform, "Face", Theme.Primary, d);
            else
            {
                Disc(box.transform, "Face", Theme.PanelBorder, d);
                Disc(box.transform, "Inner", Theme.PanelPaper, d - 6);   // 1.5pt 描圈
            }
        }

        private static Image Disc(Transform parent, string name, Color color, float size)
        {
            var image = Ui.Panel(parent, name).AddComponent<Image>();
            image.sprite = Theme.Circle;
            image.color = color;
            image.raycastTarget = false;
            CenterSize(image.rectTransform, size);
            return image;
        }

        private static void CenterSize(RectTransform rect, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
        }

        /// <summary>宝箱状态画在立绘上:可领 = 原色;已领 = 立绘 35% + 右下 `jade` 小勾;未达 = 压到 45%。
        /// 稿上未达还要去色 70%,uGUI 没有现成的去色材质,只做了透明度(见 current/CharacterScreen 差异)。</summary>
        private static void ChestIcon(Transform parent, ChestTier tier, float size, ChestMark mark)
        {
            var holder = Ui.Row(parent, "Chest", 0);
            ChestArt.Draw(holder.transform, tier, ChestView.State.Idle, size);
            var art = holder.transform.GetChild(holder.transform.childCount - 1).gameObject;
            if (mark != ChestMark.Ready)
                art.AddComponent<CanvasGroup>().alpha = mark == ChestMark.Done ? 0.35f : 0.45f;
            if (mark != ChestMark.Done) return;

            bool small = size < 40f;
            float badge = small ? 15f : 21f;            // 稿 7pt / 10pt
            var tick = Ui.Panel(holder.transform, "Tick");
            tick.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)tick.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(6f, small ? -4f : -2f);   // 稿 right -3pt、bottom -2 / -1pt
            rect.sizeDelta = new Vector2(badge, badge);
            var disc = tick.AddComponent<Image>();
            disc.sprite = Theme.Circle;
            disc.color = Theme.Success;
            disc.raycastTarget = false;
            if (Icons.Get("check") is { } check)
            {
                var icon = Ui.Panel(tick.transform, "Check").AddComponent<Image>();
                icon.sprite = check;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                CenterSize(icon.rectTransform, badge * 0.7f);
            }
        }

        /// <summary>格底一颗钮:高 40(19pt)、撑满卡宽、字 19、`cta` 底白字;onClick 为 null 时画成
        /// 置灰 `locked-bg` 底 `text-faint` 字、不可点(「箱位已满」)。</summary>
        private static void CellButton(Transform parent, string text, Action onClick)
        {
            const float H = 40f;
            const int Font = 19, Radius = 20;
            GameObject go;
            if (onClick != null)
                go = Ui.RoundButton(parent, text, onClick, Theme.Primary, Color.white, Font,
                    new Vector2(NodeW - 12, H), Radius).gameObject;
            else
            {
                var face = Ui.CardPanel(parent, "Disabled", Theme.LockedBg, Radius);
                face.raycastTarget = false;
                Ui.Stretch(Ui.ThemedLabel(face.transform, text, Font, Theme.LockGray).rectTransform);
                go = face.gameObject;
                Ui.Sized(go, NodeW - 12, H);
            }
            go.GetComponent<LayoutElement>().flexibleWidth = 1;
        }

        // ---- 渐隐与边缘小签 ----

        private static Texture2D _fadeTexture;

        /// <summary>两像素的白→透明横向渐变,双线性采样;RawImage 取 uv 0.25–0.75 段正好是纯 1 → 0。</summary>
        private static Texture2D FadeTexture
        {
            get
            {
                if (_fadeTexture != null) return _fadeTexture;
                _fadeTexture = new Texture2D(2, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                _fadeTexture.SetPixels(new[] { Color.white, new Color(1f, 1f, 1f, 0f) });
                _fadeTexture.Apply();
                return _fadeTexture;
            }
        }

        private static void EdgeFade(Transform parent, bool left)
        {
            var image = Ui.Panel(parent, left ? "FadeL" : "FadeR").AddComponent<RawImage>();
            image.texture = FadeTexture;
            image.uvRect = left ? new Rect(0.25f, 0f, 0.5f, 1f) : new Rect(0.75f, 0f, -0.5f, 1f);
            image.color = Theme.PanelPaper;
            image.raycastTarget = false;
            float x = left ? 0f : 1f;
            Ui.Anchor(image.rectTransform, new Vector2(x, 0f), new Vector2(x, 1f),
                new Vector2(left ? 0f : -FadeW, 0f), new Vector2(left ? FadeW : 0f, 0f));
        }

        /// <summary>屏外还有可领时贴边出的 `cta` 小签「◂ 2 待领」/「3 待领 ▸」:高 31(15pt)、字 17(8.12pt)、
        /// 左右内边距 13(6pt),距轨道顶 54(26pt)、贴边 4(2pt)。点了把最近的那个屏外可领格滚进视野。</summary>
        private (GameObject, Text) EdgePill(Transform parent, bool left)
        {
            var go = Ui.Panel(parent, left ? "EdgeL" : "EdgeR");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(15);
            image.type = Image.Type.Sliced;
            image.color = Theme.Primary;
            var rect = (RectTransform)go.transform;
            float x = left ? 0f : 1f;
            rect.anchorMin = rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(x, 1f);
            rect.anchoredPosition = new Vector2(left ? 4f : -4f, -54f);
            rect.sizeDelta = new Vector2(0f, 31f);
            var label = Ui.ThemedLabel(go.transform, "", 17, Color.white);
            label.raycastTarget = false;
            Ui.Stretch(label.rectTransform);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => JumpToOffscreen(left));
            go.SetActive(false);
            return (go, label);
        }

        private bool IsCellClaimable(int lv) =>
            LevelRewardRules.IsChestClaimable(_meta, lv) || MilestoneRules.IsClaimable(_meta, lv);

        /// <summary>格心落在视口左 / 右之外算「屏外」。</summary>
        private float CellCenter(int index) => index * CellStride + NodeW / 2f;

        private float TrackOffset => _trackContent != null ? -_trackContent.anchoredPosition.x : 0f;

        /// <summary>按当前滚动位置数两侧屏外的可领格,刷新两枚边缘小签(滚动时由 onValueChanged 调)。</summary>
        private void RefreshEdges()
        {
            if (_trackContent == null || _trackViewport == null || _edgeLeft == null) return;
            float off = TrackOffset, viewW = _trackViewport.rect.width;
            int left = 0, right = 0;
            for (int i = 0; i < _trackLevels.Count; i++)
            {
                if (!IsCellClaimable(_trackLevels[i])) continue;
                float c = CellCenter(i);
                if (c < off) left++;
                else if (c > off + viewW) right++;
            }
            SetEdge(_edgeLeft, _edgeLeftText, left,
                left > 0 ? Strings.T("character.rewards.edge_left", ("count", left)) : "");
            SetEdge(_edgeRight, _edgeRightText, right,
                right > 0 ? Strings.T("character.rewards.edge_right", ("count", right)) : "");
        }

        private static void SetEdge(GameObject pill, Text label, int count, string text)
        {
            pill.SetActive(count > 0);
            if (count <= 0) return;
            label.text = text;
            var rect = (RectTransform)pill.transform;
            rect.sizeDelta = new Vector2(Ui.EstimateWidth(text, label.fontSize) + 26f, rect.sizeDelta.y);
        }

        /// <summary>左签:最近的左侧屏外可领格滚到视口左、左边留 1 格;右签:最近的右侧屏外可领格滚到视口右、右边留 1 格。</summary>
        private void JumpToOffscreen(bool left)
        {
            if (_trackContent == null || _trackViewport == null) return;
            float off = TrackOffset, viewW = _trackViewport.rect.width;
            if (left)
            {
                for (int i = _trackLevels.Count - 1; i >= 0; i--)
                    if (CellCenter(i) < off && IsCellClaimable(_trackLevels[i]))
                    {
                        SetTrackOffset((i - 1) * CellStride);
                        return;
                    }
            }
            else
            {
                for (int i = 0; i < _trackLevels.Count; i++)
                    if (CellCenter(i) > off + viewW && IsCellClaimable(_trackLevels[i]))
                    {
                        SetTrackOffset((i + 2) * CellStride - NodeGap - viewW);
                        return;
                    }
            }
        }

        /// <summary>把轨道滚到 offset(内容左缘在视口左缘之左多少),夹在 [0, 内容宽 − 视口宽];同步记下位置。</summary>
        private void SetTrackOffset(float offset)
        {
            float maxOff = Mathf.Max(0f, _trackContent.rect.width - _trackViewport.rect.width);
            offset = Mathf.Clamp(offset, 0f, maxOff);
            if (_trackScroll != null) _trackScroll.StopMovement();
            _trackContent.anchoredPosition = new Vector2(-offset, _trackContent.anchoredPosition.y);
            _trackScrollX = -offset;
            RefreshEdges();
        }

        // ================= 右下:统计四页签 =================

        private void BuildStats(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Stats", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var stack = PaddedStack(panel.transform, "Stack", 17,
                new RectOffset(PanelPad, PanelPad, 19, PanelPad));

            var tabs = Ui.Row(stack, "Tabs", 13);
            tabs.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            TabButton(tabs.transform, Tab.Chest, Strings.T("character.tab.chest"));
            TabButton(tabs.transform, Tab.Tower, Strings.T("character.tab.tower"));
            TabButton(tabs.transform, Tab.Battle, Strings.T("character.tab.battle"));
            TabButton(tabs.transform, Tab.Econ, Strings.T("character.tab.econ"));

            var pane = Ui.Row(stack, "Pane", 29);
            pane.AddComponent<LayoutElement>().flexibleHeight = 1;
            var paneLayout = pane.GetComponent<HorizontalLayoutGroup>();
            paneLayout.childAlignment = TextAnchor.UpperLeft;
            paneLayout.childForceExpandHeight = true;

            switch (_tab)
            {
                case Tab.Chest: BuildChestPane(pane.transform); break;
                case Tab.Tower: BuildTowerPane(pane.transform); break;
                case Tab.Battle: BuildBattlePane(pane.transform); break;
                case Tab.Econ: BuildEconPane(pane.transform); break;
            }
        }

        /// <summary>页签:选中深灰蓝底白字;未选宣纸底 + 描边(浅卡压浅底必须带描边)。</summary>
        private void TabButton(Transform parent, Tab tab, string label)
        {
            bool on = _tab == tab;
            const int FontSize = 23;   // 稿 11pt
            var outer = Ui.OutlinedPanel(parent, "Tab", on ? Theme.InkSoft : Theme.PanelPaper,
                on ? Theme.InkSoft : Theme.PanelBorder, 25, 2f, out var face);
            Ui.Sized(outer.gameObject, label.Length * FontSize + 58, 50);   // 稿 高 24pt、左右各 14pt
            var text = Ui.ThemedLabel(outer.transform, label, FontSize, on ? Color.white : Theme.TextDim,
                Theme.TitleFont);
            Ui.Stretch(text.rectTransform);
            var button = outer.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() =>
            {
                if (_tab == tab) return;
                _tab = tab;
                Rebuild();
            });
        }

        private void BuildChestPane(Transform pane)
        {
            var stats = _meta.Stats;

            // ① 开箱:七档箱各开了几只
            int opened = 0;
            foreach (var kv in stats.ChestsOpened) opened += kv.Value;
            var chests = Column(pane, 268, Strings.T("character.chest.opened", ("count", opened.ToString("N0", Inv))));
            for (var tier = ChestTier.Paper; tier <= ChestTier.Crimson; tier++)
            {
                stats.ChestsOpened.TryGetValue(tier, out int n);
                ListRow(chests, Theme.ChestColor(tier), ChestRules.TierName(tier), n);
            }
            VSep(pane);

            // ② 开出字卡:按稀有度的张数
            int cards = 0;
            foreach (var kv in stats.CardsFromChests) cards += kv.Value;
            var rarities = Column(pane, 234, Strings.T("character.chest.cards", ("count", cards.ToString("N0", Inv))));
            for (var r = CardRarity.White; r <= CardRarity.Red; r++)
            {
                stats.CardsFromChests.TryGetValue(r, out int n);
                ListRow(rarities, Theme.RarityColor(r),
                    Strings.T("character.chest.rarity_row", ("rarity", CharInfo.RarityName(r))), n);
            }
            VSep(pane);

            // ③ 保底进度:读真实保底计数(与 ChestRules 同源);规则表是红→橙→金,展示倒过来金→橙→红
            // 行距 8(space-4):原 15 时这一栏首选高 333 > 页面区 304(932×430 下),竖排按比例压扁各行
            var pity = Ui.VStack(pane, "Pity", 8);
            pity.AddComponent<LayoutElement>().flexibleWidth = 1;
            var pityLayout = pity.GetComponent<VerticalLayoutGroup>();
            pityLayout.childAlignment = TextAnchor.UpperLeft;
            pityLayout.childForceExpandWidth = true;
            Ui.ThemedLabel(pity.transform, Strings.T("character.pity.title"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            for (int i = ChestRules.PityRules.Length - 1; i >= 0; i--)
            {
                var rule = ChestRules.PityRules[i];
                int now = rule.Rarity switch
                {
                    CardRarity.Red => _meta.RedPity,
                    CardRarity.Orange => _meta.OrangePity,
                    _ => _meta.GoldPity,
                };
                PityBlock(pity.transform, rule.Rarity, rule.MinTier, now, rule.Threshold);
            }
        }

        private static void PityBlock(Transform parent, CardRarity rarity, ChestTier minTier, int now, int max)
        {
            string rarityName = CharInfo.RarityName(rarity);
            string tierName = ChestRules.TierName(minTier);
            var block = Ui.VStack(parent, "PityRow", 6);
            block.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            var row = Ui.Row(block.transform, "Head", 12);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Dot(row.transform, Theme.RarityColor(rarity), 17);
            FlexLabel(row.transform,
                Strings.T("character.pity.row", ("rarity", rarityName), ("tierName", tierName)), 21, Theme.TextMain);
            // 27 号宋体的行高 38.8 比同行 21 号黑体(30.4)高出一截、全是字身上下的空白。布局盒按同行
            // 正文行高给 30,数字仍以盒心居中画出(verticalOverflow = Overflow),看起来不变,每块省 8.8
            Ui.ThemedLabel(row.transform, Strings.T("character.pity.value", ("now", now), ("max", max)),
                27, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleRight)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            Ui.Bar(block.transform, max > 0 ? (float)now / max : 0f, Theme.RarityColor(rarity), new Vector2(0, 10));
            Ui.ThemedLabel(block.transform,
                Strings.T("character.pity.hint", ("left", Math.Max(0, max - now)), ("tierName", tierName),
                    ("rarity", rarityName)),
                18, Theme.LockGray, null, TextAnchor.MiddleLeft);
        }

        private void BuildTowerPane(Transform pane)
        {
            var s = _meta.Stats;
            string times = Strings.T("character.unit.times"), floors = Strings.T("character.unit.floors");
            var tiles = TileRow(pane);
            StatTile(tiles, Strings.T("character.tower.climbs"), s.Climbs, times,
                Strings.T("character.tower.climbs_note"));
            StatTile(tiles, Strings.T("character.tower.floors"), s.FloorsCleared, floors,
                Strings.T("character.tower.floors_note"));
            StatTile(tiles, Strings.T("character.tower.best"), _meta.BestDepth, floors,
                Strings.T("character.tower.best_note", ("rank", EndlessRules.RankTitle(_meta.BestDepth))));
            StatTile(tiles, Strings.T("character.tower.bosses"), s.BossesDefeated, Strings.T("character.unit.bosses"),
                Strings.T("character.tower.bosses_note"));
            StatTile(tiles, Strings.T("character.tower.deaths"), s.Deaths, times,
                Strings.T("character.tower.deaths_note"));
        }

        private void BuildBattlePane(Transform pane)
        {
            const int PerRow = 5;
            var s = _meta.Stats;

            // 左:出手最多前 10(5 × 2)
            var left = Ui.VStack(pane, "Top", 10);
            var leftLayout = left.GetComponent<VerticalLayoutGroup>();
            leftLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.ThemedLabel(left.transform, Strings.T("character.battle.top"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            var top = StatsRules.TopPlays(_meta, _graph, PerRow * 2);
            if (top.Count == 0)
                Ui.ThemedLabel(left.transform, Strings.T("map.hero.top_empty"), 19, Theme.LockGray, null,
                    TextAnchor.MiddleLeft);
            Transform row = null;
            for (int i = 0; i < top.Count; i++)
            {
                if (i % PerRow == 0)
                {
                    var rowGo = Ui.Row(left.transform, "TopRow", 21);
                    rowGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    row = rowGo.transform;
                }
                var cell = Ui.Row(row, "Cell", 10);
                cell.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                var tile = Ui.MiniGlyphTile(cell.transform, _graph.Get(top[i].Id), new Vector2(64, 80));
                Ui.RankBadge(tile, i + 1, 27f, 10f, 17);  // 稿 13pt 圆、探出 5pt、8pt 字
                var count = Ui.ThemedLabel(cell.transform, top[i].Plays.ToString("N0", Inv), 21, Theme.TextMain,
                    null, TextAnchor.MiddleLeft);
                count.gameObject.AddComponent<LayoutElement>().preferredWidth = 50;
            }
            VSep(pane);

            // 右:合成 / 拆解 并排,最高单次伤害通栏
            var right = Ui.VStack(pane, "Tiles", 17);
            right.AddComponent<LayoutElement>().flexibleWidth = 1;
            var rightLayout = right.GetComponent<VerticalLayoutGroup>();
            rightLayout.childAlignment = TextAnchor.UpperLeft;
            rightLayout.childForceExpandWidth = true;
            var pair = TileRowIn(right.transform);
            StatTile(pair, Strings.T("character.battle.composes"), s.Composes, Strings.T("character.unit.times"));
            StatTile(pair, Strings.T("character.battle.dismantles"), s.Dismantles, Strings.T("character.unit.times"));
            StatTile(right.transform, Strings.T("character.battle.maxhit"), s.MaxHit);
        }

        private void BuildEconPane(Transform pane)
        {
            var s = _meta.Stats;
            var tiles = TileRow(pane);
            StatTile(tiles, Strings.T("character.econ.earned"), s.InkEarned, null,
                Strings.T("character.econ.earned_note"));
            StatTile(tiles, Strings.T("character.econ.spent"), s.InkSpent, null,
                Strings.T("character.econ.spent_note"));
            StatTile(tiles, Strings.T("character.econ.ads"), s.AdRewards, Strings.T("character.unit.times"),
                Strings.T("character.econ.ads_note"));
        }

        /// <summary>大数字卡(稿 .tile):标签 9.5pt `text-dim` → 数值宋体 22pt(千分位)+ 单位 10pt `text-faint`
        /// → 说明 8.5pt `text-faint`;内边距 10/10/9pt、竖排 4pt。数值行按稿 line-height 1.1 给定高 51
        /// (字身照常画出,Text 不裁切),单位与数值**基线对齐**:两个 Text 底对齐后,单位再垫高两者下沉量之差
        /// (宋体 46 × 0.286 − 黑体 21 × 0.288 ≈ 7)。卡高 = 40 + 29 + 8 + 51(+ 8 + 26 说明行)= 128 / 162。
        /// 战斗页三格稿上没有说明行(<paramref name="note"/> 传 null)。</summary>
        private static void StatTile(Transform parent, string label, int value, string unit = null,
            string note = null)
        {
            const float ValueH = 51f, NoteH = 26f, Gap = 8f;
            var tile = Ui.OutlinedPanel(parent, "Tile", Theme.CardWhite, Theme.PanelBorder, 17);
            var element = tile.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.preferredHeight = 40 + 29 + Gap + ValueH + (note != null ? Gap + NoteH : 0f);
            var stack = PaddedStack(tile.transform, "Stack", Gap, new RectOffset(21, 21, 21, 19));
            Ui.ThemedLabel(stack, label, 20, Theme.TextDim, null, TextAnchor.MiddleLeft);

            var line = Ui.Row(stack, "Value", 8);   // 稿 margin-left 2pt → 4,再加宋体数字右侧字身空白的余量
            line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.Sized(line, height: ValueH);
            Ui.Sized(Ui.ThemedLabel(line.transform, value.ToString("N0", Inv), 46, Theme.TextMain, Theme.TitleFont,
                TextAnchor.LowerLeft).gameObject, height: ValueH);
            if (!string.IsNullOrEmpty(unit))
            {
                var unitBox = Ui.Row(line.transform, "Unit", 0);
                var unitLayout = unitBox.GetComponent<HorizontalLayoutGroup>();
                unitLayout.padding = new RectOffset(0, 0, 0, 7);
                unitLayout.childAlignment = TextAnchor.LowerLeft;
                Ui.ThemedLabel(unitBox.transform, unit, 21, Theme.LockGray, null, TextAnchor.LowerLeft);
            }
            if (note != null)
                Ui.Sized(Ui.ThemedLabel(stack, note, 18, Theme.LockGray, null, TextAnchor.MiddleLeft).gameObject,
                    height: NoteH);
        }

        /// <summary>整页一排等宽大数字卡(登塔 / 经济):贴顶,不被页高拉长。</summary>
        private static Transform TileRow(Transform pane)
        {
            var holder = Ui.VStack(pane, "TilesHolder", 0);
            holder.AddComponent<LayoutElement>().flexibleWidth = 1;
            var layout = holder.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            return TileRowIn(holder.transform);
        }

        private static Transform TileRowIn(Transform parent)
        {
            var row = Ui.Row(parent, "Tiles", 17);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            return row.transform;
        }

        // ================= 小件 =================

        /// <summary>撑满父节点、带内边距的竖排容器(面板里的内容都挂在这上面)。</summary>
        private static Transform PaddedStack(Transform parent, string name, float spacing, RectOffset padding)
        {
            var stack = Ui.VStack(parent, name, spacing);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.padding = padding;
            return stack.transform;
        }

        /// <summary>面板标题行:宋体小标题打头,后面由调用方接副标与分隔线(稿 .ttl)。</summary>
        private static Transform TitleRow(Transform parent, string title)
        {
            var row = Ui.Row(parent, "Ttl", 13);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(row.transform, title, 25, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            return row.transform;
        }

        /// <summary>竖排列表一列:表头 + 若干行(宝箱页签 ① ②)。</summary>
        private static Transform Column(Transform pane, float width, string header)
        {
            var col = Ui.VStack(pane, "Col", 4);
            col.AddComponent<LayoutElement>().preferredWidth = width;
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            Ui.ThemedLabel(col.transform, header, 19, Theme.LockGray, null, TextAnchor.MiddleLeft);
            return col.transform;
        }

        /// <summary>一行「色点 + 名 + 数」;数为 0 整行压灰(稿 .li.zero)。</summary>
        private static void ListRow(Transform parent, Color dot, string name, int value)
        {
            var color = value == 0 ? Theme.LockGray : Theme.TextMain;
            var row = Ui.Row(parent, "Li", 12);
            row.AddComponent<LayoutElement>().preferredHeight = 33;   // 稿 16pt
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Dot(row.transform, value == 0 ? Theme.LockGray : dot, 17);
            FlexLabel(row.transform, name, 21, color);
            Ui.ThemedLabel(row.transform, value.ToString("N0", Inv), 21, color, null, TextAnchor.MiddleRight);
        }

        private static void Dot(Transform parent, Color color, float size)
        {
            var dot = Ui.Panel(parent, "Dot").AddComponent<Image>();
            dot.sprite = Theme.Rounded(6);
            dot.type = Image.Type.Sliced;
            dot.color = color;
            dot.raycastTarget = false;
            Ui.Sized(dot.gameObject, size, size);
        }

        private static void FlexLabel(Transform parent, string text, int size, Color color) =>
            Ui.ThemedLabel(parent, text, size, color, null, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        private static void Rule(Transform parent)
        {
            var rule = Ui.Panel(parent, "Rule").AddComponent<Image>();
            rule.color = Theme.PanelBorder;
            rule.raycastTarget = false;
            Ui.Sized(rule.gameObject, height: 2, flexWidth: 1);
        }

        private static void VSep(Transform parent)
        {
            var sep = Ui.Panel(parent, "VSep").AddComponent<Image>();
            sep.color = Theme.PanelBorder;
            sep.raycastTarget = false;
            Ui.Sized(sep.gameObject, width: 2);
        }

        private static void Spring(Transform parent) =>
            Ui.Sized(Ui.Panel(parent, "Spring"), flexHeight: 1);
    }
}
