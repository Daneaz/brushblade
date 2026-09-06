using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>卡组(收集与出阵,19.3)。版式基线 = <c>docs/design/ui/scenes/Main.dc.html</c>:
    /// 顶栏 / 六系筛选栏 / 左网格右详情。
    ///
    /// ⚠ 尺寸常量都是**逻辑单位**,由稿子的 pt 换算而来(1pt = 2.093,见 Device.dc.html)。
    /// 改稿子就同步改这里 —— 两边都可能过时,但任何一边动了都要留痕(scenes/README.md)。
    ///
    /// 2026-09-03 按稿重写。此前是每页 12 张的翻页网格,只列**已拥有**的字;现在:
    /// · 74 张可收集字全列出来,没拿到的走锁态沉底 —— 收集页的另一半是「还差什么」;
    /// · 网格内部滚动,不再翻页;
    /// · 右栏常驻卡池概览(2026-09-06,取代出阵编组:出阵概念随抽卡池化一并废止)。
    ///   栏底原有一个「去升级 N 张」钮,2026-09-05 用户拍板移除:升级是**某一张字**的事,
    ///   入口已经在那张字的详情弹窗底部(<see cref="SheetActions"/>),
    ///   而「可升级」排序本来就把能升的顶到网格最前 —— 同一件事的第三个入口。
    ///
    /// 2026-09-05 用户拍板:**字卡详情改走弹窗**(<see cref="CharPreview"/>),与开箱结果、
    /// 战斗里长按看到的是同一屏。此前右栏是双身份的 —— 没选中画出阵表、选中了整栏换成详情,
    /// 而那份详情的排版还与弹窗那一份各写了一套(配方 / 生克 / 数值格三段各有两个实现),
    /// 同一张字在两处读出来不一样。段落现在只有一份,在 <see cref="CharSheetSections"/>。
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

        // 网格:5 列,每张 103×128pt;列距 8pt、行距 14pt
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
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            // 稿上 .safe 的内缩;弹窗仍挂在 transform 上,铺满整屏
            var (padSide, padBottom) = SafeArea.MissingInset();
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

        private void BuildTopBar(Transform parent)
        {
            var top = Ui.Row(parent, "Top", 21);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)top.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -TopH), Vector2.zero);

            int owned = _meta.OwnedCards.Count;
            int locked = _all.Count - owned;
            int unseen = MetaRules.UnseenCount(_meta);

            Ui.ThemedLabel(top.transform, Strings.T("collection.header.title"), 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(top.transform,
                Strings.T("collection.header.stats", ("owned", owned), ("total", _all.Count)),
                23, Theme.TextDim);
            if (locked > 0)
                Ui.Chip(top.transform, Strings.T("collection.header.locked_chip", ("count", locked)),
                    Theme.PanelInset, Theme.LockGray, 20);
            if (unseen > 0)
                Ui.Chip(top.transform, Strings.T("collection.header.new_chip", ("count", unseen)),
                    Theme.Cinnabar, Color.white, 20);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63));
        }

        private void BuildFilters(Transform parent)
        {
            var bar = Ui.Row(parent, "Filters", 4);
            bar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -(TopH + FilterH)), new Vector2(0, -TopH));

            foreach (var tab in FilterTabs) BuildFilterTab(bar.transform, tab);

            var separator = Ui.Panel(bar.transform, "Rule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(separator, 2, FilterH * 0.5f);

            int upgradable = 0, fresh = 0, owned = 0;
            foreach (var def in _all)
            {
                if (!_meta.OwnedCards.Contains(def.Id)) continue;
                owned++;
                if (MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity)) upgradable++;
                if (MetaRules.IsCardUnseen(_meta, def.Id)) fresh++;
            }
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
            Ui.Chip(row.transform, countText, Theme.PanelInset, Theme.TextDim, 18);

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
                if (i % GridColumns == 0)
                {
                    var rowGo = Ui.Row(content, $"Row{i / GridColumns}", GridGapX);
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

        /// <summary>右栏 = **只有出阵编组**(2026-09-05 用户拍板:字卡详情改走弹窗,与开箱/战斗
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

        /// <summary>开这张字的详情弹窗。段落与开箱 / 战斗那两处**同一份实现**
        /// (<see cref="CharSheetSections"/>),差别只在脚上多一条操作钮带 ——
        /// 那两处保持纯只读。</summary>
        private void ShowCharSheet(CharDef def)
        {
            _modal = CharPreview.Show(transform, def, _graph,
                MetaRules.CardLevel(_meta, def.Id), battle: null, meta: _meta,
                footActions: row => SheetActions(row, def));
        }

        /// <summary>弹窗底部那条操作钮带。判据(拥有 / 出阵 / 份数 / 墨锭)全留在这里,
        /// 没有搬进 CharPreview —— 那一屏不该跟着长出一套养成规则。
        /// 两个钮点完都会 Rebuild 整页、弹窗随 Ui.Clear 一起消失,这是刻意的:
        /// 编入出阵与升级都改了这张字的状态,原地留一张已经过期的详情比关掉更糟。</summary>
        private void SheetActions(Transform parent, CharDef def)
        {
            if (!_meta.OwnedCards.Contains(def.Id))
            {
                // 「去开宝箱」= 回主界面,那是宝箱的唯一入口
                var locked = Ui.PillButton(parent, Strings.T("collection.button.locked"),
                    () => _onBack(), Theme.ShopNav, Color.white, 24, new Vector2(0, 75));
                locked.GetComponent<LayoutElement>().flexibleWidth = 1;
                return;
            }

            int level = MetaRules.CardLevel(_meta, def.Id);
            bool maxed = level >= MetaRules.MaxCardLevel;
            bool canUpgrade = MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity);
            string upText = maxed
                ? Strings.T("collection.button.maxed")
                : (canUpgrade ? Strings.T("collection.button.upgrade", ("level", level + 1))
                              : Strings.T("collection.button.upgrade_short"));
            var upButton = Ui.PillButton(parent, upText,
                () => ShowUpgradePreview(def.Id),
                maxed ? Theme.GoldSoft : (canUpgrade ? Theme.Jade : Theme.PanelInset),
                maxed ? Theme.GoldDeep : (canUpgrade ? Color.white : Theme.LockGray),
                24, new Vector2(0, 75));
            upButton.GetComponent<LayoutElement>().flexibleWidth = 1;
            upButton.interactable = canUpgrade;
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
            Ui.Panel(row.transform, "Spring");
            Ui.ThemedLabel(row.transform,
                Strings.T("collection.side.pool_row",
                    ("count", count), ("percent", $"{permille / 10}.{permille % 10}")),
                19, Theme.TextDim, null, TextAnchor.MiddleRight);
        }

        // ---- 右栏 · 选中:字牌详情 ----

        // ================= 动作 =================

        /// <summary>升级确认弹窗(稿 <c>docs/design/ui/scenes/Upgrade.dc.html</c>)。
        ///
        /// 2026-09-04 重写。原来是「Lv.1 → Lv.2」一行,加上前后两句 <see cref="CharInfo.EffectsText"/>
        /// 全文对着看 —— 玩家得自己在两串长句子里找哪个数变了。现在拆成两段:
        /// **数值提升**(攻/治/盾,一行一条「旧 → 新 (+差)」)与**层数与特性**(灼烧几层、致盲几成,
        /// 同一条读法,前面挂图标 chip);没变的收成底下一行灰字,它回答的是「我会不会丢掉什么」。
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

            var body = Ui.Row(stack, "Body", 25);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, flexWidth: 1, flexHeight: 1);
            BuildUpgradeNumbers(body.transform, def, level, next);
            BuildUpgradeTraits(body.transform, def, level, next);

            var buttons = Ui.Row(stack, "Buttons", 21);
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(buttons, 0, 71, flexWidth: 1);
            var confirm = Ui.PillButton(buttons.transform, Strings.T("collection.modal.confirm_upgrade_button"), () =>
            {
                Destroy(overlay); // 先关弹窗:Upgrade 会 Rebuild 清根,顺序反了会留残影
                Upgrade(cardId);
            }, Theme.Jade, Color.white, 24, new Vector2(0, 71));
            confirm.GetComponent<LayoutElement>().flexibleWidth = 1;
            Ui.PillButton(buttons.transform, Strings.T("common.reconsider"), () => Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 24, new Vector2(250, 71));
        }

        /// <summary>数值提升:一行一条「旧 → 新 (+差)」。没有量级变化的字这一栏留空,
        /// 整段不占位 —— 焦(只有灼烧)那类字升级动的本来就只有层数。</summary>
        private void BuildUpgradeNumbers(Transform parent, CharDef def, int level, int next)
        {
            var col = Ui.VStack(parent, "Numbers", 11);
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            Ui.Sized(col, flexWidth: 1, flexHeight: 1);
            var section = CharSheetSections.Section(col.transform, Strings.T("collection.modal.section.numbers"));

            var was = CollectionStats.Of(def, level);
            var now = CollectionStats.Of(def, next);
            int shown = 0;
            for (int i = 0; i < was.Count && i < now.Count; i++)
            {
                if (was[i].Value == now[i].Value) continue;
                shown++;
                DeltaRow(section, null, null, was[i].Label,
                    was[i].Value.ToString(), now[i].Value.ToString(),
                    Strings.T("collection.modal.delta", ("delta", now[i].Value - was[i].Value)));
            }
            if (shown == 0)
                Ui.Sized(Ui.ThemedLabel(section, Strings.T("collection.modal.no_numbers"),
                    19, Theme.LockGray).gameObject, 0, 40, flexWidth: 1);
        }

        /// <summary>层数与特性:与数值同一条读法,只是前面挂图标 chip。
        /// 灼烧几层、致盲几成这类「功能强度」的变化,和伤害数字一样是玩家买单的理由。</summary>
        private void BuildUpgradeTraits(Transform parent, CharDef def, int level, int next)
        {
            var col = Ui.VStack(parent, "Traits", 11);
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            Ui.Sized(col, flexWidth: 1, flexHeight: 1);
            var section = CharSheetSections.Section(col.transform, Strings.T("collection.modal.section.traits"));

            var was = CardTraits.Of(def, level);
            var now = CardTraits.Of(def, next);
            var unchanged = new List<string>();
            int shown = 0;
            for (int i = 0; i < was.Count && i < now.Count; i++)
            {
                if (was[i].Amount == now[i].Amount)
                {
                    unchanged.Add(now[i].Name);
                    continue;
                }
                shown++;
                DeltaRow(section, now[i].IconKey, now[i].Word, now[i].Name,
                    was[i].Amount, now[i].Amount, null);
            }
            if (shown == 0)
                Ui.Sized(Ui.ThemedLabel(section, Strings.T("collection.modal.no_traits"),
                    19, Theme.LockGray).gameObject, 0, 40, flexWidth: 1);
            if (unchanged.Count == 0) return;

            // 没变的收成一行灰字 —— 它回答的是「我会不会丢掉什么」
            string keep = Strings.T("collection.modal.unchanged", ("list", string.Join(" · ", unchanged)));
            var label = Ui.ThemedLabel(section, keep, 18, Theme.LockGray);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, 0,
                Ui.WrappedTextHeight(keep, 18, UpgradeW * 0.5f - 50), flexWidth: 1);
        }

        /// <summary>「[chip] 名 旧 → 新 (+差)」一行。两段共用,读法因此完全一致。</summary>
        private GameObject DeltaRow(Transform parent, string iconKey, string word, string name,
            string was, string now, string delta)
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

            if (iconKey != null)
                Ui.Chip(row.transform, "", CardTraits.ChipColor(iconKey), Color.white, 18, iconKey: iconKey);
            else if (word != null)
                Ui.Chip(row.transform, word, Theme.LockedBg, Theme.TextDim, 18);

            var label = Ui.ThemedLabel(row.transform, name, 19, Theme.TextDim);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, flexWidth: 1);

            Ui.ThemedLabel(row.transform, was, 25, Theme.LockGray, Theme.TitleFont);
            Ui.ThemedLabel(row.transform, Strings.T("collection.side.recipe_to"), 18, Theme.LockGray);
            Ui.ThemedLabel(row.transform, now, 29, Theme.UpgradeText, Theme.TitleFont);
            if (delta != null)
                Ui.ThemedLabel(row.transform, delta, 19, Theme.Jade);
            return row;
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
                    ("ink", _meta.Ink), ("inkNeeded", MetaRules.InkRequired(level, def.Rarity))));
        }

        /// <summary>被拒提示统一弹窗(2026-07-19);须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }
    }
}
