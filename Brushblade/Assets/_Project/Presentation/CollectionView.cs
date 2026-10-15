using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>卡组(收集与卡池概览,19.3)。版式基线 = <c>docs/design/drafts/scenes/Main.dc.html</c>:
    /// 顶栏 / 六系筛选栏 / 左网格右详情。
    ///
    /// ⚠ 尺寸常量都是**逻辑单位**,由稿子的 pt 换算而来(1pt = 2.093,见 Device.dc.html)。
    /// 改稿子就同步改这里 —— 两边都可能过时,但任何一边动了都要留痕(scenes/README.md)。
    ///
    /// 2026-09-03 按稿重写。此前是每页 12 张的翻页网格,只列**已拥有**的字;现在:
    /// · 全部可收集字(字表里非部件、口径同宝箱池)都列出来,没拿到的走锁态沉底 —— 收集页的另一半是「还差什么」;
    /// · 网格内部滚动,不再翻页;
    /// · 右栏常驻卡池概览(2026-09-06,取代出阵编组:出阵概念随抽卡池化一并废止)。
    ///   栏底原有一个「去升级 N 张」钮,2026-09-05 用户拍板移除:升级是**某一张字**的事,
    ///   入口已经在那张字的详情弹窗底部(<see cref="SheetActions"/>),
    ///   而「可升级」排序本来就把能升的顶到网格最前 —— 同一件事的第三个入口。
    ///
    /// 2026-09-05 用户拍板:**字卡详情改走弹窗**(<see cref="CharPreview"/>),与开箱结果、
    /// 战斗里长按看到的是同一屏。此前右栏是双身份的 —— 没选中画出阵表、选中了整栏换成详情,
    /// 而那份详情的排版还与弹窗那一份各写了一套(配方 / 生克 / 数值格三段各有两个实现),
    /// 同一张字在两处读出来不一样。段落现在只有一份:Plan E2 起由 <see cref="CharPreview"/> + <see cref="CardSheetParts"/> 绘制。
    ///
    /// 稿上有而这里**刻意没做**的一处:字头的「相生 ×3」徽标 —— 相生已于 2026-09-02 取消
    /// (`docs/design/wuxing-reference.md` v0.8),稿子画的时候它还在。</summary>
    public sealed class CollectionView : MonoBehaviour
    {
        // ---- 稿上的骨架尺寸(pt → 逻辑单位) ----
        private const float TopH = 80f;         // 顶栏 38pt
        private const float FilterH = 56f;      // 筛选栏 27pt
        private const float Gap = 13f;          // 行间距 6pt
        private const float SideW = 527f;       // 右栏 252pt
        private const float MainGap = 19f;      // 网格与右栏之间 9pt
        private const float SideHeadH = 54f;    // 右栏头 26pt
        private const float SidePad = 21f;      // 右栏内边距 10pt

        // 网格:稿上 5 列,每张 103×128pt;列距 8pt、行距 14pt。
        // ⚠ 5 是**上限**,实际列数按网格可用宽算(见 GridColumnsFor),牌宽不缩。两档宽的账:
        //   基准机 932×430pt:画布宽 ≈1951,安全区两侧各 123 → 内容区 1705,网格 = 1705 − 527 − 19 = 1159
        //       ≥ 5×216 + 4×17 = 1148 → 5 列,与稿一致;
        //   16:9(画布宽 1600,无刘海机 MissingInset 两侧各补 123)→ 内容区 1354,网格 = 808,
        //       5 列要 1148、4 列要 915 都放不下 → 3 列(682),右侧余 126。
        // 此前恒 5 列:横排布局组把 216 宽的牌压到约 150 宽而高不变,整格变形(2026-10-04 实机反馈)。
        private const int GridColumns = 5;
        private const float GridGapX = 17f;
        private const float GridGapY = 29f;
        private static readonly Vector2 CardSize = new(216f, 268f);
        // 升级确认弹窗:520×320pt(稿 Upgrade.dc.html)。两段并排,再挤就要把
        // 「变化前 → 变化后」压成一行文字,而那正是这一屏的全部意义
        private const float UpgradeW = 1088f;
        private const float UpgradeH = 670f;
        private static readonly Vector2 UpgradeTile = new(130f, 163f);

        /// <summary>筛选栏的六个页签。null = 全部。</summary>
        private static readonly Element?[] FilterTabs =
        {
            null, Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth,
        };

        private RecipeGraph _graph;
        private MetaState _meta;
        private Action _onBack;
        private Action _save;
        private GameObject _modal;      // 当前告知弹窗(同屏仅一个)

        private List<CharDef> _all;     // 全部可收集字(非部件),口径同宝箱池
        private Element? _filter;
        private bool _filterIsAll = true;
        private string _selected;
        /// <summary>网格排序(2026-09-05 用户拍板,取代原先「仅看可升级」/「只看未拥有」
        /// 两个筛选 chip):三选一,点击即换。
        ///
        /// 排序而不是筛选,是因为筛选把其余的字**藏起来** —— 而收集页的底色是「我手上有什么、
        /// 还差什么」,藏起来正好把这一页最想让人看见的东西拿掉了。排序把要紧的顶到最前,
        /// 其余仍在后面看得到,同一个诉求代价小得多。</summary>
        private enum SortMode { Rarity, Upgradable, Fresh }

        private SortMode _sort = SortMode.Rarity; // 默认稀有度(用户拍板)

        /// <summary>拥有态筛选(2026-09-05 用户拍板补回:「除了当前的排序,还要保留之前的
        /// filter。已拥有,未拥有」)。与 <see cref="SortMode"/> **并存**、各管一件事:
        /// 排序决定「谁排在前」,筛选决定「谁出现」。
        ///
        /// 与 9-05 早先删掉的那两个 chip 不同:那两个是「仅看可升级」/「只看未拥有」,
        /// 前者已经由「可升级」排序覆盖(顶到最前 = 一眼看见,还不用把其余的藏起来);
        /// 补回的这一组是**拥有态**,而拥有与否是收集页的两半,排序顶不掉它 ——
        /// 「还差哪些」需要把已有的整片挪走才看得清。</summary>
        private enum OwnFilter { All, Owned, Wanted }

        private OwnFilter _own = OwnFilter.All;
        /// <summary>网格的滚动位置(1 = 顶部)。整页是全量重建的,不记着它的话,
        /// 点第三行某张牌 → Rebuild → 列表弹回顶部,那张牌当场从眼前消失(2026-09-03 实机反馈)。
        /// 只在筛选变了时才归顶 —— 那时列表内容本来就换了一批。</summary>
        private float _gridScroll = 1f;
        private ScrollRect _grid;
        /// <summary>筛选栏横向滚动位置(Content.anchoredPosition.x)。点筛选/排序钮都会整页重建,
        /// 不记着的话点完右端的排序钮,筛选栏就弹回最左、刚点的钮滚出视口。</summary>
        private float _filterScrollX;
        private ScrollRect _filterScroll;
        private int _columns = GridColumns;

        public void Init(RecipeGraph graph, MetaState meta, Action save, Action onBack)
        {
            _graph = graph;
            _meta = meta;
            _save = save;
            _onBack = onBack;
            _all = new List<CharDef>();
            foreach (var def in graph.All)
                if (!def.IsComponent)
                    _all.Add(def);
            Rebuild();
        }

        // ================= 骨架 =================

        /// <param name="keepScroll">false = 网格归顶。筛选/开关变了才传 false:
        /// 那时列表换了一批内容,停在原来的位置没有意义。</param>
        private void Rebuild(bool keepScroll = true)
        {
            _gridScroll = keepScroll && _grid != null && !float.IsNaN(_grid.verticalNormalizedPosition)
                ? _grid.verticalNormalizedPosition
                : 1f;
            if (_filterScroll != null) _filterScrollX = _filterScroll.content.anchoredPosition.x;
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            // 稿上 .safe 的内缩;弹窗仍挂在 transform 上,铺满整屏
            var (padSide, padBottom) = SafeArea.MissingInset();
            _columns = GridColumnsFor(ContentWidth(padSide) - SideW - MainGap);
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frame = content.transform;

            BuildTopBar(frame);
            BuildFilters(frame);

            var main = Ui.Panel(frame, "Main");
            Ui.Anchor((RectTransform)main.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0, -(TopH + FilterH + Gap)));

            BuildGrid(main.transform);
            BuildSide(main.transform);
        }

        /// <summary>Content 的逻辑宽:视图挂在 SafeAreaFitter 里(宽 = safeArea),再减两侧补的
        /// MissingInset。按 Screen 算而不读 RectTransform.rect —— 首次 Init 与建画布同一帧,
        /// 那时 CanvasScaler 还没把画布尺寸定下来;换算口径与 <see cref="SafeArea.MissingInset"/> 同
        /// (按高匹配,1 逻辑单位 = Screen.height / 900 像素)。</summary>
        private static float ContentWidth(float padSide)
        {
            float scale = Screen.height / 900f;
            if (scale <= 0f) return 1600f - 2 * padSide;
            return Screen.safeArea.width / scale - 2 * padSide;
        }

        /// <summary>网格放得下几列(牌宽不变,最多稿上的 5 列,至少 1 列)。两档宽的账见 GridColumns。</summary>
        private static int GridColumnsFor(float gridWidth) =>
            Mathf.Clamp(Mathf.FloorToInt((gridWidth + GridGapX) / (CardSize.x + GridGapX)), 1, GridColumns);

        private void BuildTopBar(Transform parent)
        {
            var top = Ui.Row(parent, "Top", 21);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)top.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -TopH), Vector2.zero);

            int owned = OwnedCount();
            int locked = _all.Count - owned;
            int unseen = MetaRules.UnseenCount(_meta);

            Ui.ThemedLabel(top.transform, Strings.T("collection.header.title"), 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(top.transform,
                Strings.T("collection.header.stats", ("owned", owned), ("total", _all.Count)),
                23, Theme.TextDim);
            if (locked > 0)
                Ui.Chip(top.transform, Strings.T("collection.header.locked_chip", ("count", locked)),
                    Theme.PanelInset, Theme.TextDim, 20);
            if (unseen > 0)
                Ui.Chip(top.transform, Strings.T("collection.header.new_chip", ("count", unseen)),
                    Theme.Cinnabar, Color.white, 20);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.Info, Color.white, 25, new Vector2(130, 63)); // 返回地图 = Info 浓墨(2026-10-04 五色语义)
        }

        /// <summary>已拥有的可收集字数:逐条遍历 <see cref="_all"/> 算,顶栏「收集 N/总数」与
        /// 筛选栏「已拥有 / 未拥有」共用这一个数。不能用 <c>OwnedCards.Count</c> ——
        /// 存档里混进部件或已下架的幽灵字时它会多算,同一屏就会出现两个不一样的数。</summary>
        private int OwnedCount()
        {
            int owned = 0;
            foreach (var def in _all)
                if (_meta.OwnedCards.Contains(def.Id)) owned++;
            return owned;
        }

        /// <summary>筛选栏。**横向滚动**(<see cref="Ui.HScrollFill"/>,2026-10-04):整条按首选宽排,
        /// 放不下的部分横向滚出来,宽屏上铺满视口、拖不动。
        ///
        /// 两档宽的账(逻辑单位;页签宽 = 46 + 名字数×29 + 10 + 计数 chip,chip = 字数×18 + 18;
        /// 切换钮 = 字数×20 + 38;间距 15×4):六页签 1079–1187 + 七个切换钮/小字 746–826 + 竖线 4 + 间距 60
        /// = **约 1890–2080**。可用宽:基准机 1705、16:9 1354 —— **两档都放不下**。
        /// 此前是固定宽的横排布局组,放不下时把每个子物件按比例压窄(16:9 下压到约 68%),
        /// 而 Text 不折行,字就探出页签与按钮外、互相叠上。改成滚动不动任何视觉规格;
        /// 最小排版宽不手算,而是建完后量布局组的首选宽 —— 计数位数变了也跟得上。</summary>
        private void BuildFilters(Transform parent)
        {
            var scroll = Ui.HScrollFill(parent, "FilterScroll", 0f, out var barContent);
            Ui.Anchor((RectTransform)scroll.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -(TopH + FilterH)), new Vector2(0, -TopH));
            _filterScroll = scroll.GetComponent<ScrollRect>();

            var bar = Ui.Row(barContent, "Filters", 4);
            bar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Stretch((RectTransform)bar.transform);

            foreach (var tab in FilterTabs) BuildFilterTab(bar.transform, tab);

            var separator = Ui.Panel(bar.transform, "Rule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(separator, 2, FilterH * 0.5f);

            int upgradable = 0, fresh = 0;
            foreach (var def in _all)
            {
                if (!_meta.OwnedCards.Contains(def.Id)) continue;
                if (MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity)) upgradable++;
                if (MetaRules.IsCardUnseen(_meta, def.Id)) fresh++;
            }
            int owned = OwnedCount();
            int wanted = _all.Count - owned;

            // 拥有态筛选(见 OwnFilter 的注释):与排序并排,各管一件事。
            // 计数按**全表**算而不是当前属性页签下 —— 与页签自己那个「已收集/总数」
            // 各答一个问题:页签说「这一系集了多少」,这里说「全表还差多少」。
            OwnToggle(bar.transform, Strings.T("collection.filter.all"), OwnFilter.All,
                Theme.InkSoft, Theme.PanelInset, Theme.TextDim);
            OwnToggle(bar.transform, Strings.T("collection.filter.owned", ("count", owned)),
                OwnFilter.Owned, Theme.Jade, Theme.AdGreenBg, Theme.UpgradeText);
            OwnToggle(bar.transform, Strings.T("collection.filter.wanted", ("count", wanted)),
                OwnFilter.Wanted, Theme.LockGray, Theme.PanelInset, Theme.TextDim);

            var filterRule = Ui.Panel(bar.transform, "Rule2");
            filterRule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(filterRule, 2, FilterH * 0.5f);
            // 三个排序钮取代原先那两个筛选 chip(见 SortMode 的注释)。计数只挂在可升级/新卡上
            // —— 那两个数本来就在原 chip 上、玩家在用;稀有度没有对应的「有几个」。
            Ui.ThemedLabel(bar.transform, Strings.T("collection.sort.label"), 19, Theme.LockGray);
            SortToggle(bar.transform, Strings.T("collection.sort.rarity"), SortMode.Rarity,
                Theme.InkSoft, Theme.PanelInset, Theme.TextDim);
            SortToggle(bar.transform, Strings.T("collection.sort.upgradable", ("count", upgradable)),
                SortMode.Upgradable, Theme.Jade, Theme.AdGreenBg, Theme.UpgradeText);
            SortToggle(bar.transform, Strings.T("collection.sort.fresh", ("count", fresh)),
                SortMode.Fresh, Theme.Cinnabar, Theme.PanelInset, Theme.TextDim);

            var spring = Ui.Panel(bar.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 量出整条的首选宽(Spring 只有 flex、不占首选宽),作为滚动区的最小排版宽
            var barRect = (RectTransform)bar.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(barRect);
            float minWidth = LayoutUtility.GetPreferredWidth(barRect);
            barContent.GetComponent<FillViewportWidth>().MinWidth = minWidth;
            barContent.sizeDelta = new Vector2(minWidth, 0f);
            barContent.anchoredPosition = new Vector2(_filterScrollX, 0f); // 越界由 Clamped 收回
        }

        /// <summary>一个属性页签:名 + 「已收集/总数」+ 未看过的红点。</summary>
        private void BuildFilterTab(Transform parent, Element? element)
        {
            bool isAll = element == null;
            bool on = isAll ? _filterIsAll : (!_filterIsAll && _filter == element);
            int owned = 0, total = 0;
            bool hasNew = false;
            foreach (var def in _all)
            {
                if (!isAll && def.Element != element) continue;
                total++;
                if (!_meta.OwnedCards.Contains(def.Id)) continue;
                owned++;
                if (MetaRules.IsCardUnseen(_meta, def.Id)) hasNew = true;
            }

            var go = Ui.Panel(parent, isAll ? "Tab_All" : $"Tab_{element}");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(10);
            image.type = Image.Type.Sliced;
            image.color = on
                ? (isAll ? Theme.PanelInset : Theme.ElementSoft(element))
                : new Color(0, 0, 0, 0);
            // 宽度自己算:横排布局组不会替按钮量文字,给 0 就是 0 宽(整条筛选栏会看起来空了)
            string countText = $"{owned}/{total}";
            string tabName = isAll ? Strings.T("collection.filter.all") : CharInfo.ElementName(element.Value);
            Ui.Sized(go, 46 + tabName.Length * 29 + 10 + Ui.ChipWidth(countText, 18), FilterH);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                _filterIsAll = isAll;
                _filter = element;
                Rebuild(keepScroll: false);
            });

            var row = Ui.Row(go.transform, "Row", 10);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(23, 23, 0, 0);
            Ui.Stretch((RectTransform)row.transform);
            var fg = on ? (isAll ? Theme.TextMain : Theme.ElementSoftFg(element)) : Theme.TextDim;
            Ui.ThemedLabel(row.transform, tabName, 29, fg, Theme.TitleFont);
            // 选中「全部」时页签底也是 PanelInset,chip 不带描边就与它糊成一片、
            // 形状整个消失(2026-09-21)。属性页签铺的是 <el>-soft,本来就分得开。
            Ui.Chip(row.transform, countText, Theme.PanelInset, Theme.TextDim, 18,
                border: on && isAll ? Theme.PanelBorder : null);

            // 选中态底下那条属性色的粗线(稿 .ftab.on 的 border-bottom)
            if (on && !isAll)
            {
                var underline = Ui.Panel(go.transform, "Underline");
                underline.AddComponent<Image>().color = Theme.ElementColor(element);
                Ui.Anchor((RectTransform)underline.transform, Vector2.zero, new Vector2(1, 0),
                    Vector2.zero, new Vector2(0, 4));
            }
            if (hasNew)
            {
                var dot = Ui.Panel(go.transform, "Dot");
                var dotImage = dot.AddComponent<Image>();
                dotImage.sprite = Theme.Circle;
                dotImage.color = Theme.Cinnabar;
                dotImage.raycastTarget = false;
                Ui.Anchor((RectTransform)dot.transform, Vector2.one, Vector2.one,
                    new Vector2(-16, -18), new Vector2(-4, -6));
            }
        }

        private void Toggle(Transform parent, string text, bool on,
            Color onBg, Color offBg, Color offFg, Action onClick)
        {
            Ui.RoundButton(parent, text, onClick,
                on ? onBg : offBg, on ? Color.white : offFg, 20,
                new Vector2(Ui.ChipWidth(text, 20) + 20, 46), 23);
        }

        /// <summary>一个拥有态筛选钮。与 <see cref="SortToggle"/> 同款三选一:
        /// 点当前项什么都不发生,不做「点一下取消回全部」那种隐式跳转 ——
        /// 要回全部就点「全部」,那一格一直在旁边。</summary>
        private void OwnToggle(Transform parent, string text, OwnFilter mode,
            Color onBg, Color offBg, Color offFg)
        {
            Toggle(parent, text, _own == mode, onBg, offBg, offFg, () =>
            {
                if (_own == mode) return;
                _own = mode;
                Rebuild(keepScroll: false); // 换了一批内容,停在原滚动位没有意义
            });
        }

        /// <summary>一个排序钮:点已选中的那个不做事(不是三态循环,是三选一) ——
        /// 点当前项要么该什么都不发生、要么该反序,反序稿上没有,那就什么都不发生,
        /// 但**不能**悄悄换成别的排序。</summary>
        private void SortToggle(Transform parent, string text, SortMode mode,
            Color onBg, Color offBg, Color offFg)
        {
            Toggle(parent, text, _sort == mode, onBg, offBg, offFg, () =>
            {
                if (_sort == mode) return;
                _sort = mode;
                Rebuild(keepScroll: false); // 换了序 = 换了一批内容的先后,停在原滚动位没有意义
            });
        }

        // ================= 左:收集网格 =================

        /// <summary>网格内容 = 当前属性页签下的字,按 <see cref="_sort"/> 排。
        ///
        /// **「未拥有沉底」是三种排序共有的第一层**,不受排序钮影响:收集页首先是
        /// 「我手上有什么」,其次才是「还差什么」——把没有的字混进已有的里面排,
        /// 无论按什么排都会让人以为自己有。排序钮换的是它下面那一层主键:
        ///   稀有度(默认) → 直接进稀有度降序
        ///   可升级       → 能升的顶到最前
        ///   新卡         → 未看过的顶到最前
        /// 三者的末两层一律是「稀有度降序 → 字形」,所以主键相同的两张字在三种排序下
        /// 相对位置一致 —— 换排序时视线不会整片乱掉。</summary>
        private List<CharDef> Visible()
        {
            var list = new List<CharDef>();
            foreach (var def in _all)
            {
                if (!_filterIsAll && def.Element != _filter) continue;
                bool has = _meta.OwnedCards.Contains(def.Id);
                if (_own == OwnFilter.Owned && !has) continue;
                if (_own == OwnFilter.Wanted && has) continue;
                list.Add(def);
            }
            list.Sort((a, b) =>
            {
                bool ownedA = _meta.OwnedCards.Contains(a.Id), ownedB = _meta.OwnedCards.Contains(b.Id);
                if (ownedA != ownedB) return ownedA ? -1 : 1;
                if (_sort == SortMode.Upgradable)
                {
                    bool upA = ownedA && MetaRules.CanUpgradeCard(_meta, a.Id, a.Rarity);
                    bool upB = ownedB && MetaRules.CanUpgradeCard(_meta, b.Id, b.Rarity);
                    if (upA != upB) return upA ? -1 : 1;
                }
                else if (_sort == SortMode.Fresh)
                {
                    bool newA = MetaRules.IsCardUnseen(_meta, a.Id), newB = MetaRules.IsCardUnseen(_meta, b.Id);
                    if (newA != newB) return newA ? -1 : 1;
                }
                if (a.Rarity != b.Rarity) return b.Rarity.CompareTo(a.Rarity);
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        private void BuildGrid(Transform parent)
        {
            var wrap = Ui.ScrollList(parent, "Grid", GridGapY, out var content);
            _grid = wrap.GetComponent<ScrollRect>();
            // 左侧吃掉「整宽 − 右栏 − 间距」
            var rect = (RectTransform)wrap.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(-(SideW + MainGap), 0);

            var list = Visible();
            if (list.Count == 0)
            {
                // 拥有态筛选补回来之后这一支**真会走到**:某一系全收集完了、又停在「未拥有」上,
                // 网格就是空的(2026-09-05)。文案因此写成不预设原因的一句 ——
                // 「这一系还没有字」在那种情形下是错的。
                var empty = Ui.ThemedLabel(content, Strings.T("collection.empty.none"), 22, Theme.LockGray);
                Ui.Sized(empty.gameObject, 0, 200, flexWidth: 1);
                return;
            }

            Transform row = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (i % _columns == 0)
                {
                    var rowGo = Ui.Row(content, $"Row{i / _columns}", GridGapX);
                    rowGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                    row = rowGo.transform;
                }
                BuildCell(row, list[i]);
            }

            // 还原滚动位置。必须先把布局算出来:ContentSizeFitter 要等下一帧才给出内容高度,
            // 而 verticalNormalizedPosition 是按「内容高 − 视口高」换算的 —— 高度还是 0 时写进去
            // 会被当场夹回顶部(这一步漏掉的话,上面记住的位置等于白记)。
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            _grid.verticalNormalizedPosition = _gridScroll;
        }

        private void BuildCell(Transform parent, CharDef def)
        {
            bool owned = _meta.OwnedCards.Contains(def.Id);
            int level = MetaRules.CardLevel(_meta, def.Id);
            bool maxed = owned && level >= MetaRules.MaxCardLevel;
            _meta.CardCopies.TryGetValue(def.Id, out int copies);
            int needed = maxed ? 0 : MetaRules.CopiesRequired(level, def.Rarity);
            bool canUpgrade = owned && MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity);

            var cell = Ui.VStack(parent, $"Cell_{def.Id}", 8);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var cellElement = cell.AddComponent<LayoutElement>();
            cellElement.preferredWidth = CardSize.x;
            cellElement.flexibleWidth = 0;

            var tile = Ui.GlyphTile(cell.transform, def, _selected == def.Id,
                () => Select(def.Id), CardSize, locked: !owned);
            CardBadges.Apply(tile.gameObject, CardSize, new CardBadges.Spec
            {
                Rarity = def.Rarity,
                Level = level,
                Maxed = maxed,
                CanUpgrade = canUpgrade,
                IsNew = owned && MetaRules.IsCardUnseen(_meta, def.Id),
                Locked = !owned,
            });
            CardBadges.Foot(cell.transform, CardSize, owned, copies, needed, maxed, canUpgrade);
        }

        /// <summary>点一张牌 = 开详情弹窗 + 销掉新字红旗(稿:「新字的红旗点一下就消」)。
        ///
        /// **先 Rebuild 再开弹窗**:Rebuild 会 Ui.Clear 掉整个根节点,顺序反了弹窗当场没了。
        /// 而这一次 Rebuild 是必须的 —— 红旗刚被销掉,网格里那张牌得跟着去掉角标
        /// (「新卡」排序下它还要换位置)。
        /// _selected 留着只为网格高亮:关掉弹窗后仍看得出刚点的是哪张。</summary>
        private void Select(string cardId)
        {
            _selected = cardId;
            if (MetaRules.IsCardUnseen(_meta, cardId))
            {
                MetaRules.MarkCardSeen(_meta, cardId);
                _save();
            }
            Rebuild();
            if (_graph.TryGet(cardId, out var def)) ShowCharSheet(def);
        }

        // ================= 右栏 =================

        /// <summary>右栏 = **只有卡池概览**(2026-09-05 用户拍板:字卡详情改走弹窗,与开箱/战斗
        /// 那两处拉齐)。此前它是双身份的:没选中画出阵表、选中了整栏换成详情 —— 于是同一块
        /// 面板既是拖拽落点又是详情页,而详情的排版还与弹窗那一份各写了一套。
        /// 现在它只干一件事,详情全部走 <see cref="ShowCharSheet"/>。</summary>
        private void BuildSide(Transform parent)
        {
            var side = Ui.OutlinedPanel(parent, "Side", Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            Ui.Anchor((RectTransform)side.transform, new Vector2(1, 0), Vector2.one,
                new Vector2(-SideW, 0), Vector2.zero);

            // 头:只剩标题(关闭钮随详情一起搬去了弹窗右上角)
            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("collection.side.title_pool"), 19, Theme.LockGray);

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            // 身:内部滚动 —— 卡池概览靠滚动装下,一直铺到栏底
            var body = Ui.ScrollList(side.transform, "Body", 0, out var content);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SidePad), new Vector2(-SidePad, -SideHeadH - 2));
            BuildPoolPanel(content);
        }

        // ---- 字卡详情弹窗(2026-09-05:与开箱 / 战斗共用 CharPreview) ----

        /// <summary>开这张字的详情弹窗(铺满版)。与开箱 / 商城 / 战斗那几处是**同一份实现**
        /// (<see cref="CharPreview"/> + <see cref="CardSheetParts"/>),差别只在脚上多一颗操作钮 ——
        /// 别处保持纯只读。</summary>
        private void ShowCharSheet(CharDef def)
        {
            _modal = CharPreview.Show(transform, def, _graph,
                MetaRules.CardLevel(_meta, def.Id), battle: null, meta: _meta,
                footActions: row => SheetActions(row, def));
        }

        /// <summary>弹窗脚上靠右的那颗钮(Plan E2 稿 CardSheet `.foot .btn`:26pt 高的小钮,取代原先 100 高的钮带)。
        /// 判据(拥有 / 份数 / 墨锭)全留在这里,没有搬进 CharPreview —— 那一屏不该跟着长出一套养成规则。
        /// 升级钮点完会 Rebuild 整页、弹窗随 Ui.Clear 一起消失,这是刻意的:
        /// 升级改了这张字的状态,原地留一张已经过期的详情比关掉更糟。
        /// 底色沿用现状(石青 Primary / 不可升 panel-inset / 满级 gold-soft):稿上是 gold,但 tokens 自 2026-10-04 起
        /// 花墨锭的钮一律石青(同 C2 的理由)。</summary>
        private void SheetActions(Transform parent, CharDef def)
        {
            if (!_meta.OwnedCards.Contains(def.Id))
            {
                // 「去开宝箱」= 回主界面,那是宝箱的唯一入口
                FootButton(parent, Strings.T("collection.button.locked"), () => _onBack(), Theme.ShopNav, Color.white, true);
                return;
            }

            int level = MetaRules.CardLevel(_meta, def.Id);
            bool maxed = level >= MetaRules.MaxCardLevel;
            bool canUpgrade = MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity);
            string upText = maxed
                ? Strings.T("collection.button.maxed")
                : (canUpgrade ? Strings.T("collection.button.upgrade", ("level", level + 1))
                              : Strings.T("collection.button.upgrade_short"));
            FootButton(parent, upText, () => ShowUpgradePreview(def.Id),
                maxed ? Theme.GoldSoft : (canUpgrade ? Theme.Primary : Theme.PanelInset),
                maxed ? Theme.GoldDeep : (canUpgrade ? Color.white : Theme.LockGray),
                canUpgrade);
        }

        private const float FootButtonH = 54f;   // 稿 26pt
        private const float FootTapMin = 92f;    // 44pt 触控下限(C5)

        /// <summary>脚钮:视觉 54 高(字 25、左右 33),触控靠 <see cref="Ui.PadTapHeight"/> 的透明扩边补到 92。</summary>
        private static void FootButton(Transform parent, string text, System.Action onClick, Color bg, Color fg, bool interactable)
        {
            var button = Ui.PillButton(parent, text, onClick, bg, fg, 25,
                new Vector2(Ui.EstimateWidth(text, 25) + 66f, FootButtonH));
            button.interactable = interactable;
            Ui.PadTapHeight(button.gameObject, FootButtonH, FootTapMin);
        }

        // ---- 右栏 · 卡池概览(2026-09-06,取代出阵编组) ----

        /// <summary>按稀有度分档列出卡池:档位 / 字数 / 起手抽中率。
        ///
        /// 抽中率是**近似值**:真实的起手是「五行各一张 + 最高档保底一张」,各系内部独立加权,
        /// 严格算要按系分开。这里给的是「全池加权抽一张时该档的命中率」——
        /// 玩家要看的是「开箱怎么影响我的起手」这个方向,不是精确概率。</summary>
        private void BuildPoolPanel(Transform parent)
        {
            var playable = MetaRules.PlayableCards(_meta, _graph);
            var section = CharSheetSections.Section(parent,
                Strings.T("collection.side.section.pool", ("count", playable.Count)));

            // 各档字数
            var counts = new int[MetaRules.RarityWeights.Length];
            foreach (var id in playable)
                counts[(int)_graph.Get(id).Rarity - 1]++;

            // 分母只算「池里真有字」的档 —— 空档不参与加权
            int total = 0;
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0) total += MetaRules.RarityWeights[i];

            // 从高到低列:玩家最关心的是顶上那几档
            for (int i = counts.Length - 1; i >= 0; i--)
            {
                if (counts[i] == 0) continue;
                var rarity = (CardRarity)(i + 1);
                int permille = total > 0 ? MetaRules.RarityWeights[i] * 1000 / total : 0;
                BuildPoolRow(section, rarity, counts[i], permille);
            }

            var tip = CharSheetSections.Section(parent, Strings.T("collection.side.section.tip"));
            string tipText = Strings.T("collection.side.pool_tip_body");
            var text = Ui.ThemedLabel(tip, tipText, 19, Theme.TextDim);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(text.gameObject, 0,
                Ui.WrappedTextHeight(tipText, 19, SideW - SidePad * 2), flexWidth: 1);
        }

        /// <summary>概览的一行:稀有度色条 + 档名 + 字数 + 抽中率。</summary>
        private void BuildPoolRow(Transform parent, CardRarity rarity, int count, int permille)
        {
            var row = Ui.Row(parent, $"PoolRow{(int)rarity}", 10);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 34, flexWidth: 1);

            var swatch = Ui.Panel(row.transform, "Swatch");
            swatch.AddComponent<Image>().color = Theme.RarityColor(rarity);
            Ui.Sized(swatch, 8, 22);

            Ui.ThemedLabel(row.transform, CharInfo.RarityName(rarity), 19, Theme.TextMain,
                null, TextAnchor.MiddleLeft);
            var spring = Ui.Panel(row.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(row.transform,
                Strings.T("collection.side.pool_row",
                    ("count", count), ("percent", $"{permille / 10}.{permille % 10}")),
                19, Theme.TextDim, null, TextAnchor.MiddleRight);
        }

        // ---- 右栏 · 选中:字牌详情 ----

        // ================= 动作 =================

        /// <summary>升级确认弹窗(2026-09-04 重写;Plan E2 改版,稿 <c>docs/design/drafts/traits/UpgradePreview.dc.html</c>,
        /// 组件卡 <c>component/UpgradeDialog</c>)。
        ///
        /// 骨架(头 / 牌行 / 两栏 / 两钮)、差行 <see cref="DeltaRow"/> 的「旧 → 新 (+差)」读法、主钮石青、警示句都不动(C2 / C3)。
        /// E2 换的是两栏的内容:
        /// - 左栏「数值提升」按面逐格列(<see cref="FaceStats.Deltas"/>,先攻后五行),行首挂 xs 面印 —— 两面一起升,
        ///   不标面就分不清涨的是哪一面的「伤害」。
        /// - 右栏这一级有新特性 →「新解锁」小卡(Lv5 / Lv8 攻、五行面各一张,同在一圈墨晕里;Lv3 / 4 / 6 一张);
        ///   没有 →「这一级没有新特性」+ 下一个解锁级的全部条目(<see cref="CardSheetRules.NextUnlock"/>)。
        /// - 两栏各进一层 ScrollList(Q14):两面一起升级最多 6 条差,而 Lv8 一对小卡每张 3 行正文,竖向都只有约 210。
        /// - 「不变」= 等级 ≤ 当前级的已解锁特性名(Q15,Lv1 没有效果、不列),挂在两栏之下单独一行(稿 .same)——
        ///   右栏现在会滚动,挂在栏尾就可能被滚出视口,而它回答的是「我会不会丢掉什么」。
        /// 局外没有「本场」,这里不画 spent 态(Q19)。
        ///
        /// ⚠ **不印重复卡与墨锭消耗**(2026-09-04 用户拍板):不够根本走不到这一页 ——
        /// 详情页那颗按钮就是灰的、点不动。印出来只是把「你付得起」再说一遍。</summary>
        private void ShowUpgradePreview(string cardId)
        {
            var def = _graph.Get(cardId);
            int level = MetaRules.CardLevel(_meta, cardId);
            int next = level + 1;

            if (_modal != null) Destroy(_modal);
            var overlay = Ui.Sheet(transform, "UpgradeSheet", UpgradeW, UpgradeH,
                dismissable: true, replaceSameName: true, Theme.Scrim, out var stack);
            _modal = overlay;
            var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
            stackLayout.childAlignment = TextAnchor.UpperCenter;
            stackLayout.childForceExpandWidth = true;

            var head = Ui.Row(stack, "Head", 15);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.Sized(head, 0, 40, flexWidth: 1);
            Ui.ThemedLabel(head.transform, Strings.T("collection.modal.upgrade_title", ("cardId", cardId)),
                34, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(head.transform, Strings.T("collection.modal.upgrade_warn"), 19, Theme.LockGray);

            // 牌行:左边那张压暗 —— 「这是它现在的样子」
            var step = Ui.Row(stack, "Step", 29);
            step.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Sized(step, 0, UpgradeTile.y, flexWidth: 1);
            var before = Ui.MiniGlyphTile(step.transform, def, UpgradeTile);
            before.GetComponent<Image>().color = Theme.LockedPaper;
            Ui.ThemedLabel(step.transform, Strings.T("collection.side.recipe_to"), 29, Theme.ExitPink);
            Ui.MiniGlyphTile(step.transform, def, UpgradeTile);
            Ui.Chip(step.transform, Strings.T("collection.button.upgrade", ("level", next)),
                Theme.Jade, Color.white, 21);

            var deltas = FaceStats.Deltas(def, level, next);
            var body = Ui.Row(stack, "Body", UpgradeBodyGap);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, flexWidth: 1, flexHeight: 1);
            BuildUpgradeNumbers(body.transform, def, deltas);
            BuildUpgradeTraits(body.transform, def, next);
            BuildUnchanged(stack, def, level, deltas);

            var buttons = Ui.Row(stack, "Buttons", 21);
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(buttons, 0, 71, flexWidth: 1);
            var confirm = Ui.PillButton(buttons.transform, Strings.T("collection.modal.confirm_upgrade_button"), () =>
            {
                Destroy(overlay); // 先关弹窗:Upgrade 会 Rebuild 清根,顺序反了会留残影
                Upgrade(cardId);
            }, Theme.Primary, Color.white, 24, new Vector2(0, 71));
            confirm.GetComponent<LayoutElement>().flexibleWidth = 1;
            // 两钮等宽对半分(2026-10-04 用户反馈:原「再想想」定宽 250、主钮弹性,比例不对等)
            var cancel = Ui.PillButton(buttons.transform, Strings.T("common.reconsider"), () => Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 24, new Vector2(0, 71));
            cancel.GetComponent<LayoutElement>().flexibleWidth = 1;
        }

        // ---- 升级预览的尺寸(稿 UpgradePreview,pt × 2.093) ----

        private const float UpgradeBodyGap = 25f;
        /// <summary>弹窗内容宽 = 1088 − 描边 2×1.5 − 内边距 2×24 = 1037。弹窗定宽,与屏比无关。</summary>
        private const float UpgradeInnerW = UpgradeW - 2f * Ui.SheetBorder - 2f * Ui.SheetPad;
        /// <summary>每栏定宽 (1037 − 25) / 2 = 506。两栏写死同宽:只给弹性宽时,布局按各自节头文字的
        /// preferred 宽分配富余,「新解锁 · 两面各一条」那栏会比「数值提升」那栏宽出一截。</summary>
        private const float UpgradeColW = (UpgradeInnerW - UpgradeBodyGap) / 2f;
        private const float UpgradeEmptyH = 40f;      // 「这一级没有…」占位行
        // 新解锁小卡(稿 .nus / .nu)
        private const float NewGroupPad = 6f;         // .nus padding 3pt
        private const int NewGroupRadius = 17;        // .nus 圆角 8pt
        private const float NewCardGap = 8.4f;        // .nus gap 4pt
        private const int NewCardRadius = 13;         // .nu 圆角 6pt(12.6 取整)
        private const float NewHeadGap = 12.6f;       // .where gap 6pt
        private const int NewWhereFont = 23;          // .wb 11pt
        private const int NewNameFont = 27;           // .tb 13pt
        private const int NewKindFont = 18;           // .tk 8.5pt
        private const int NewTagFont = 17;            // .newtag 8pt;高 14pt ≈ 17 + ChipPadY 12 = 29
        private const int NewTagPadX = 25;            // .newtag 左右各 6pt
        // 下一级预告框(稿 .next)
        private const int NextPadV = 15, NextPadH = 19;
        private const float NextGap = 8.4f;           // gap 4pt
        private const int NextKeyFont = 18;           // .k 8.5pt
        private const int NextWhereFont = 20;         // .r 9.5pt
        private const int NextNameFont = 23;          // .r b 11pt
        private const float NextRowGap = 10.5f;       // .r gap 5pt

        /// <summary>一栏:节头 + 吃满余高的 ScrollList(Q14)。返回列表的 content。</summary>
        private static Transform UpgradeColumn(Transform parent, string name, string title)
        {
            var col = Ui.VStack(parent, name, 11);
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            Ui.Sized(col, UpgradeColW, flexHeight: 1);
            var section = CharSheetSections.Section(col.transform, title);
            section.GetComponent<LayoutElement>().flexibleHeight = 1;
            var scroll = Ui.ScrollList(section, "Scroll", 11, out var content);
            Ui.Sized(scroll, flexWidth: 1, flexHeight: 1);
            return content;
        }

        /// <summary>左栏「数值提升」:<see cref="FaceStats.Deltas"/> 逐面逐格(只给变了的,先攻后五行),一行一条。
        /// 一条都没有时写一句占位 —— 空白读起来像没加载出来。</summary>
        private static void BuildUpgradeNumbers(Transform parent, CharDef def, IReadOnlyList<StatDelta> deltas)
        {
            var list = UpgradeColumn(parent, "Numbers", Strings.T("collection.modal.section.numbers"));
            foreach (var d in deltas) DeltaRow(list, def, d);
            if (deltas.Count == 0) EmptyLine(list, Strings.T("collection.modal.no_numbers"));
        }

        /// <summary>右栏:这一级解锁了什么(<see cref="CardSheetRules.UnlockedAt"/>);没有就预告下一个。
        /// 节头:两张 =「新解锁 · 两面各一条」,一张 =「新解锁」,没有 =「特性」(稿 rightTitle)。</summary>
        private static void BuildUpgradeTraits(Transform parent, CharDef def, int next)
        {
            var fresh = CardSheetRules.UnlockedAt(def, next);
            string title = fresh.Count >= 2 ? Strings.T("collection.modal.section.new_two")
                : fresh.Count == 1 ? Strings.T("collection.modal.section.new")
                : Strings.T("collection.modal.section.trait");
            var list = UpgradeColumn(parent, "Traits", title);
            if (fresh.Count > 0)
            {
                NewUnlocks(list, def, fresh, next);
                return;
            }
            EmptyLine(list, Strings.T("collection.modal.no_new"));
            var (nextLevel, rows) = CardSheetRules.NextUnlock(def, next);
            if (nextLevel > 0) NextUnlockBox(list, def, nextLevel, rows);
        }

        private static void EmptyLine(Transform parent, string text)
        {
            var label = Ui.ThemedLabel(parent, text, 19, Theme.LockGray);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, 0, UpgradeEmptyH, flexWidth: 1);
        }

        /// <summary>「不变:…」:等级 ≤ 当前级、已解锁的特性名(Q15;Lv1 没有效果,不成行也不列)。
        /// 一条都没有(Lv1 / Lv2)时退回「两面本体的层数与回合」(稿兜底)—— 但 Lv2→3 那一跳层数与回合恰恰在变
        /// (Lv3 本体替换),兜底句会说反,这时整行不出。折行按弹窗内容宽算,不截断:这一行是「我会不会丢掉什么」。</summary>
        private static void BuildUnchanged(Transform parent, CharDef def, int level, IReadOnlyList<StatDelta> deltas)
        {
            var names = KeptTraitNames(def, level);
            string list;
            if (names.Count > 0) list = string.Join(" · ", names);
            else if (deltas.Any(d => !FaceStats.Scales(d.Kind))) return;
            else list = Strings.T("collection.modal.unchanged_body");

            string text = Strings.T("collection.modal.unchanged", ("list", list));
            var label = Ui.ThemedLabel(parent, text, 18, Theme.LockGray);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, 0, Ui.WrappedTextHeight(text, 18, UpgradeInnerW), flexWidth: 1);
        }

        /// <summary>已解锁的特性名:Lv3 / Lv4 的「两面」条 + 两面各自的面特性行,按槽位升序(同级先攻后五行)。
        /// 两面通用的面特性(Face = Both)两面各列一次,按 TraitDef 去重。</summary>
        private static List<string> KeptTraitNames(CharDef def, int level)
        {
            var seen = new HashSet<TraitDef>();
            return CardSheetRules.SharedTraits(def, level)
                .Concat(CardSheetRules.FaceTraits(def, CardFace.Attack, level))
                .Concat(CardSheetRules.FaceTraits(def, CardFace.Feature, level))
                .Where(r => r.Unlocked && seen.Add(r.Defs[0]))
                .OrderBy(r => (int)r.Slot)                  // 稳定排序:同级保持先攻后五行
                .Select(CardSheetParts.NameText)
                .ToList();
        }

        // ---- 新解锁小卡 ----

        /// <summary>一张小卡要画的东西。正文与字卡详情同一口径:面特性行走 <see cref="CardSheetParts.FaceRow"/>,
        /// Lv3 / Lv4 走 <see cref="CardSheetParts.SharedRow"/>(分面且正文不同时两小句各带 xs 面印,Lv3 印本体差)。
        /// 数值按 max(目标等级, 解锁级) 印(Q17)。</summary>
        private sealed class NewCard
        {
            public SheetTrait Row;
            public CardSheetParts.RowModel Model;
            public string Where;
            public string Name;
            public bool ShowWhere, ShowKind;
            public float Height;
        }

        /// <summary>这一级解锁的全部小卡,同在一圈会呼吸的赭金墨晕里(稿 .nus,halo-new 2.6s;整窗唯一会动的东西)。
        /// 墨晕向外洇 <see cref="Theme.HaloPad"/>:外面包一层同宽的留白,不然 ScrollList 视口的 RectMask2D 会把贴边的光裁掉。</summary>
        private static void NewUnlocks(Transform parent, CharDef def, IReadOnlyList<SheetTrait> rows, int next)
        {
            bool two = rows.Count >= 2;
            float groupW = UpgradeColW - 2f * Theme.HaloPad;
            float cardW = groupW - 2f * NewGroupPad;
            var cards = rows.Select(r => NewCardModel(def, r, next, cardW, two)).ToList();
            float groupH = 2f * NewGroupPad + cards.Sum(c => c.Height) + NewCardGap * (cards.Count - 1);

            var wrap = Ui.VStack(parent, "NewWrap", 0);
            var wrapLayout = wrap.GetComponent<VerticalLayoutGroup>();
            wrapLayout.childAlignment = TextAnchor.UpperLeft;
            wrapLayout.padding = new RectOffset(Theme.HaloPad, Theme.HaloPad, Theme.HaloPad, Theme.HaloPad);
            Ui.Sized(wrap, 0, groupH + 2f * Theme.HaloPad, flexWidth: 1);

            var group = Ui.VStack(wrap.transform, "NewUnlocks", NewCardGap);
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            int pad = Mathf.RoundToInt(NewGroupPad);
            layout.padding = new RectOffset(pad, pad, pad, pad);
            Ui.Sized(group, groupW, groupH);

            var halo = Ui.Panel(group.transform, "Halo");
            halo.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = halo.AddComponent<Image>();
            image.sprite = Theme.Halo(NewGroupRadius);
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.raycastTarget = false;
            var rect = (RectTransform)halo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-Theme.HaloPad, -Theme.HaloPad);
            rect.offsetMax = new Vector2(Theme.HaloPad, Theme.HaloPad);
            halo.AddComponent<CardHalo>().Init(image, Theme.RarityGold);   // halo-new #C9A94A

            foreach (var card in cards) NewCardView(group.transform, def, card, cardW, two);
        }

        // .nu padding 6pt 9pt 7pt(两张时 5pt 8pt 6pt):底比顶多 1pt。取整后的值同时用于估高与布局,两边口径一致
        private static int NewPadTop(bool two) => two ? 11 : 13;          // 5pt = 10.5 / 6pt = 12.6
        private static int NewPadBottom(bool two) => two ? 13 : 15;       // 6pt = 12.6 / 7pt = 14.65
        private static int NewPadH(bool two) => two ? 17 : 19;            // .nu 9pt / 两张时 8pt
        private static float NewBodyGap(bool two) => two ? 4f : 6f;       // .nu gap 3pt / 两张时 2pt
        private static int NewBodyFont(bool two) => two ? 20 : 21;        // .tx 10pt / 两张时 9.5pt

        private static string NewWhereText(SheetTrait row) => row.Face switch
        {
            TraitFace.Attack => Strings.T("face.name.attack"),
            TraitFace.Feature => Strings.T("face.name.feature"),
            _ => Strings.T("face.name.both"),
        };

        private static NewCard NewCardModel(CharDef def, SheetTrait row, int next, float cardW, bool two)
        {
            bool shared = row.Slot == TraitSlot.Lv3 || row.Slot == TraitSlot.Lv4;
            var model = shared
                ? CardSheetParts.SharedRow(def, row, next, CardSheetParts.RowState.On)
                : CardSheetParts.FaceRow(def, row, next, CardSheetParts.RowState.On);
            var card = new NewCard { Row = row, Model = model, Where = NewWhereText(row) };

            // 卡头放不下时(木 / 花 Lv3「种·强化 / 成林·强化」这类双名):先丢类别小字,再丢「两面 / 攻击」字样
            // (印已经说了是哪一面),最后才截名字
            float inner = cardW - 2f * NewPadH(two);
            float fixedW = Ui.FaceSealM + Ui.ChipWidth(Strings.T("collection.modal.new_tag"), NewTagFont, NewTagPadX)
                + 3f * NewHeadGap;   // 印、名、弹簧、「新」签 = 4 件 3 道缝
            float where = Ui.EstimateWidth(card.Where, NewWhereFont) + NewHeadGap;
            float kind = Ui.EstimateWidth(model.Kind, NewKindFont) + NewHeadGap;
            float name = Ui.EstimateWidth(model.Name, NewNameFont);
            card.ShowWhere = card.ShowKind = true;
            card.Name = model.Name;
            if (fixedW + where + name + kind > inner)
            {
                card.ShowKind = false;
                if (fixedW + where + name > inner)
                {
                    card.ShowWhere = false;
                    card.Name = Ui.Truncate(model.Name, NewNameFont, inner - fixedW);
                }
            }

            float h = NewPadTop(two) + NewPadBottom(two) + Ui.FaceSealM;
            foreach (var line in model.Lines)
                h += NewBodyGap(two) + CardSheetParts.LineHeight(line.Text, line.Seal != null, NewBodyFont(two), inner);
            card.Height = h;
            return card;
        }

        /// <summary>一张小卡(稿 .nu):卡头「面印 m + 攻击 / 五行面 / 两面 + 名 + 类别 +『新』签」,下接正文。
        /// 五行面那张垫本系淡底(<see cref="Theme.FaceTint"/>,稿 .nu.back),其余 card-face。</summary>
        private static void NewCardView(Transform parent, CharDef def, NewCard card, float cardW, bool two)
        {
            bool back = card.Row.Face == TraitFace.Feature;
            var panel = Ui.CardPanel(parent, "NewTrait", back ? Theme.FaceTint(def.Element) : Theme.CardWhite, NewCardRadius);
            Ui.Sized(panel.gameObject, cardW, card.Height);
            var stack = Ui.VStack(panel.transform, "Stack", NewBodyGap(two));
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(NewPadH(two), NewPadH(two), NewPadTop(two), NewPadBottom(two));
            Ui.Stretch((RectTransform)stack.transform);
            float inner = cardW - 2f * NewPadH(two);

            var head = Ui.Row(stack.transform, "Head", NewHeadGap);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, inner, Ui.FaceSealM);
            NewSeal(head.transform, def, card.Row, Ui.FaceSealM);
            if (card.ShowWhere)
            {
                var where = Ui.ThemedLabel(head.transform, card.Where, NewWhereFont, Theme.TextMain, Theme.TitleFont);
                where.fontStyle = FontStyle.Bold;
            }
            var name = Ui.ThemedLabel(head.transform, card.Name, NewNameFont, Theme.TextMain, Theme.TitleFont);
            name.fontStyle = FontStyle.Bold;
            if (card.ShowKind) Ui.ThemedLabel(head.transform, card.Model.Kind, NewKindFont, Theme.TextDim);
            var spring = Ui.Panel(head.transform, "Spring");
            Ui.Sized(spring, flexWidth: 1);
            Ui.Chip(head.transform, Strings.T("collection.modal.new_tag"), Theme.Gold, Theme.GoldText, NewTagFont, NewTagPadX);

            // 正文(含分面两小句的 xs 面印)与字卡详情同一份画法,高度口径同 NewCardModel 里的 LineHeight
            foreach (var (seal, text) in card.Model.Lines)
                CardSheetParts.BodyLine(stack.transform, def, seal, text, NewBodyFont(two), Theme.TextDim, inner);
        }

        /// <summary>按行的面出印:攻 / 本系五行面字;两面通用(Lv3 / Lv4)出「两」(ink-soft 底,component/FaceSeal);
        /// 字形特性(拆字 / 成字时)出「拆 / 合」(B-Qe1,<see cref="Ui.TraitSeal"/>)—— 它不是两面通用的面效果。
        /// 分面两条的行(Defs 一攻一五行)不能拿 Defs[0] 去 TraitSeal(会印成「攻」),所以只在有字形特性时走 TraitSeal。</summary>
        private static void NewSeal(Transform parent, CharDef def, SheetTrait row, float size)
        {
            var glyph = CardSheetParts.GlyphTrait(row);
            var seal = glyph != null ? Ui.TraitSeal(parent, def, glyph, size) : row.Face switch
            {
                TraitFace.Attack => Ui.FaceSeal(parent, def, CardFace.Attack, size),
                TraitFace.Feature => Ui.FaceSeal(parent, def, CardFace.Feature, size),
                _ => Ui.FaceSeal(parent, Ui.FaceSealBothText(), Theme.InkSoft, size),
            };
            Ui.Sized(seal, size, size);
        }

        // ---- 下一级预告 ----

        /// <summary>「下一级解锁在 LvN」框(稿 .next):该级的全部条目,一条一行「印 s + 攻击 / 五行面 / 两面 · 名」。</summary>
        private static void NextUnlockBox(Transform parent, CharDef def, int level, IReadOnlyList<SheetTrait> rows)
        {
            float keyH = Mathf.Ceil(NextKeyFont * 1.35f);
            float h = 2f * NextPadV + keyH + rows.Count * (NextGap + Ui.FaceSealS);
            var box = Ui.CardPanel(parent, "NextUnlock", Theme.PanelInset, NewGroupRadius);
            Ui.Sized(box.gameObject, 0, h, flexWidth: 1);
            var stack = Ui.VStack(box.transform, "Stack", NextGap);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(NextPadH, NextPadH, NextPadV, NextPadV);
            Ui.Stretch((RectTransform)stack.transform);
            float inner = UpgradeColW - 2f * NextPadH;

            var key = Ui.ThemedLabel(stack.transform, Strings.T("collection.modal.next_unlock", ("level", level)),
                NextKeyFont, Theme.TextDim);
            key.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(key.gameObject, inner, keyH);

            foreach (var row in rows)
            {
                var line = Ui.Row(stack.transform, "Next", NextRowGap);
                line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Sized(line, inner, Ui.FaceSealS);
                NewSeal(line.transform, def, row, Ui.FaceSealS);
                string where = Strings.T("collection.modal.next_where", ("where", NewWhereText(row)));
                Ui.ThemedLabel(line.transform, where, NextWhereFont, Theme.TextDim);
                float nameW = inner - Ui.FaceSealS - Ui.EstimateWidth(where, NextWhereFont) - 2f * NextRowGap;
                var name = Ui.ThemedLabel(line.transform, Ui.Truncate(CardSheetParts.NameText(row), NextNameFont, nameW),
                    NextNameFont, Theme.TextMain, Theme.TitleFont);
                name.fontStyle = FontStyle.Bold;
            }
        }

        /// <summary>差行标签:同数值格标签;「持续」带上它跟的那一格的短标签(「减攻持续」「护甲持续」),
        /// 与字卡详情 Lv3 行本体差同一口径(<see cref="StatDelta.DurationOf"/>)——一面若有两组「效果 + 持续」,
        /// 光写「持续」两行就分不清了。</summary>
        private static string DeltaRowLabel(StatDelta d) =>
            d.Kind == FaceStatKind.Duration && d.DurationOf is { } owner
                ? Strings.T("collection.modal.duration_of", ("owner", CardSheetParts.ShortLabel(owner)))
                : CardSheetParts.StatLabel(d.Kind);

        /// <summary>差行「[面印] 名 ——— 旧 → 新 (+差)」(component/UpgradeDialog 的 DeltaRow,读法不变;
        /// E2 把原先的图标 chip 换成 xs 面印,标这一格属于哪一面)。
        /// 减攻印「−15% → −16%」,再挂「+1」读起来像变弱了,所以减攻不印增量。</summary>
        private static void DeltaRow(Transform parent, CharDef def, StatDelta d)
        {
            var row = Ui.Row(parent, "Delta", 11);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(17, 17, 0, 0);
            var image = row.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = Theme.PanelInset;
            Ui.Sized(row, 0, 54, flexWidth: 1);

            var seal = Ui.FaceSeal(row.transform, def, d.Face, Ui.FaceSealXs);
            Ui.Sized(seal, Ui.FaceSealXs, Ui.FaceSealXs);

            var label = Ui.ThemedLabel(row.transform, DeltaRowLabel(d), 19, Theme.TextDim);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, flexWidth: 1);

            Ui.ThemedLabel(row.transform, CardSheetParts.StatValue(d.Kind, d.From), 25, Theme.LockGray, Theme.TitleFont);
            Ui.ThemedLabel(row.transform, Strings.T("collection.side.recipe_to"), 18, Theme.LockGray);
            Ui.ThemedLabel(row.transform, CardSheetParts.StatValue(d.Kind, d.To), 29, Theme.UpgradeText, Theme.TitleFont);
            if (d.Kind != FaceStatKind.Weaken && d.To > d.From)
                Ui.ThemedLabel(row.transform, Strings.T("collection.modal.delta", ("delta", d.To - d.From)),
                    19, Theme.UpgradeText);
        }

        private void Upgrade(string cardId)
        {
            var def = _graph.Get(cardId);
            if (MetaRules.TryUpgradeCard(_meta, cardId, def.Rarity))
            {
                _save();
                Rebuild();
                ShowCharSheet(def); // 升完把详情重开:新等级/新数值就是这次操作的反馈
                return;
            }

            int level = MetaRules.CardLevel(_meta, cardId);
            Rebuild();
            if (level >= MetaRules.MaxCardLevel)
            {
                ShowAlert(Strings.T("collection.alert.already_maxed_title"),
                    Strings.T("collection.alert.already_maxed_body", ("cardId", cardId), ("maxLevel", MetaRules.MaxCardLevel)));
                return;
            }
            _meta.CardCopies.TryGetValue(cardId, out int copies);
            ShowAlert(Strings.T("collection.alert.upgrade_insufficient_title"),
                Strings.T("collection.alert.upgrade_insufficient_body", ("cardId", cardId), ("nextLevel", level + 1),
                    ("copies", copies), ("needed", MetaRules.CopiesRequired(level, def.Rarity)),
                    ("ink", Ui.InkText(_meta.Ink)), ("inkNeeded", Ui.InkText(MetaRules.InkRequired(level, def.Rarity)))));
        }

        /// <summary>被拒提示统一弹窗(2026-07-19);须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }
    }
}
