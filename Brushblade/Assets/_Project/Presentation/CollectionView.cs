using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// · 右栏常驻:没选中时是出阵编组(15 格 + 每系配额),选中了换成这张字的详情。
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
        private const float SideFootH = 100f;   // 右栏底(8pt 内边距 + 36pt 按钮)
        private const float SidePad = 21f;      // 右栏内边距 10pt

        // 网格:5 列,每张 103×128pt;列距 8pt、行距 14pt
        private const int GridColumns = 5;
        private const float GridGapX = 17f;
        private const float GridGapY = 29f;
        private static readonly Vector2 CardSize = new(216f, 268f);
        private static readonly Vector2 BigCardSize = new(159f, 199f); // 详情大牌 76×95pt
        // 出阵格里的缩小版字卡:0.8 竖版比例(与框素材同比,拉了就变形);格高再加牌下那行等级
        private static readonly Vector2 SlotTile = new(74f, 92f);
        private const float SlotRowH = 117f;

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
        private bool _upOnly;
        private bool _wantOnly;
        /// <summary>网格的滚动位置(1 = 顶部)。整页是全量重建的,不记着它的话,
        /// 点第三行某张牌 → Rebuild → 列表弹回顶部,那张牌当场从眼前消失(2026-09-03 实机反馈)。
        /// 只在筛选变了时才归顶 —— 那时列表内容本来就换了一批。</summary>
        private float _gridScroll = 1f;
        private ScrollRect _grid;
        // 拖拽落点区(2026-09-03):右栏整块就是「出阵表」。拖拽期间**不许重绘** ——
        // uGUI 的拖拽事件只发给起拖的那个对象,它一被销毁 OnEndDrag 就再也不来、字影卡在屏幕上。
        // 所以这几个引用是拿来**就地改色/改字**的,不是拿来重建的。
        private RectTransform _sideRect;
        private RectTransform _gridRect;
        private Image _sideFrame;
        private Color _sideFrameRest;
        private GameObject _dropHint;
        private Text _dropHintLabel;

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
                Strings.T("collection.header.stats", ("owned", owned), ("total", _all.Count),
                    ("deckCount", _meta.Deck.Count), ("deckLimit", MetaRules.DeckLimit)),
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

            int upgradable = 0, locked = 0;
            foreach (var def in _all)
            {
                if (!_meta.OwnedCards.Contains(def.Id)) { locked++; continue; }
                if (MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity)) upgradable++;
            }
            Toggle(bar.transform, Strings.T("collection.filter.upgradable", ("count", upgradable)),
                _upOnly, Theme.Jade, Theme.AdGreenBg, Theme.UpgradeText,
                () => { _upOnly = !_upOnly; _wantOnly = false; Rebuild(keepScroll: false); });
            Toggle(bar.transform, Strings.T("collection.filter.wanted", ("count", locked)),
                _wantOnly, Theme.LockGray, Theme.PanelInset, Theme.TextDim,
                () => { _wantOnly = !_wantOnly; _upOnly = false; Rebuild(keepScroll: false); });

            var spring = Ui.Panel(bar.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(bar.transform, Strings.T("collection.sort_hint"), 19, Theme.LockGray);
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

        // ================= 左:收集网格 =================

        /// <summary>排序:未拥有一律沉底 → 可升级 → 未看过的新字 → 稀有度降序 → 字形。
        /// 收集页首先是「我手上有什么」,其次才是「还差什么」。</summary>
        private List<CharDef> Visible()
        {
            var list = new List<CharDef>();
            foreach (var def in _all)
            {
                if (!_filterIsAll && def.Element != _filter) continue;
                bool owned = _meta.OwnedCards.Contains(def.Id);
                if (_upOnly && !(owned && MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity))) continue;
                if (_wantOnly && owned) continue;
                list.Add(def);
            }
            list.Sort((a, b) =>
            {
                bool ownedA = _meta.OwnedCards.Contains(a.Id), ownedB = _meta.OwnedCards.Contains(b.Id);
                if (ownedA != ownedB) return ownedA ? -1 : 1;
                bool upA = ownedA && MetaRules.CanUpgradeCard(_meta, a.Id, a.Rarity);
                bool upB = ownedB && MetaRules.CanUpgradeCard(_meta, b.Id, b.Rarity);
                if (upA != upB) return upA ? -1 : 1;
                bool newA = MetaRules.IsCardUnseen(_meta, a.Id), newB = MetaRules.IsCardUnseen(_meta, b.Id);
                if (newA != newB) return newA ? -1 : 1;
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
            _gridRect = rect;   // 拖拽落点判定要用:拖出这块 = 编入出阵

            var list = Visible();
            if (list.Count == 0)
            {
                var empty = Ui.ThemedLabel(content, _wantOnly
                    ? Strings.T("collection.empty.all_owned")
                    : Strings.T("collection.empty.no_upgradable"), 22, Theme.LockGray);
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
                InDeck = _meta.Deck.Contains(def.Id),
                CanUpgrade = canUpgrade,
                IsNew = owned && MetaRules.IsCardUnseen(_meta, def.Id),
                Locked = !owned,
            });
            CardBadges.Foot(cell.transform, CardSize, owned, copies, needed, maxed, canUpgrade);

            // 拖到右栏 = 编入出阵(2026-09-03)。没拥有的字不给拖:它连编组的资格都没有。
            // 竖向手势由 DragToDeck 转发给网格的 ScrollRect,列表照旧滚得动。
            if (owned)
                DragToDeck.Attach(tile.gameObject, def.Id, Theme.GlyphColor(def.Element),
                    position => DropFromGrid(def.Id, position),
                    () => ShowDropHint(Strings.T("collection.drag.to_deck"), Theme.Jade));
        }

        /// <summary>网格里的牌松手:落在右栏内 = 编入出阵,落在别处 = 什么都不做。</summary>
        private void DropFromGrid(string cardId, Vector2 screenPosition)
        {
            HideDropHint();
            // 判据是「拖出了网格」,不是「精确落在右栏上」—— 与卸下那条对称,
            // 也省得玩家非要够到那块面板才算数(右栏被详情占满时更难瞄)
            if (Inside(_gridRect, screenPosition)) return;        // 没拖出网格:什么都没发生
            if (_meta.Deck.Contains(cardId)) return;              // 已经在阵上:重复拖入不算错,更不该当成卸下
            ToggleDeck(cardId);
        }

        /// <summary>出阵格松手:拖出右栏 = 卸下,还在栏内 = 什么都不做(误触保护)。</summary>
        private void DropFromSlot(string cardId, Vector2 screenPosition)
        {
            HideDropHint();
            if (Inside(_sideRect, screenPosition)) return;        // 还在栏内:误触保护,不动出阵表
            ToggleDeck(cardId);
        }

        private readonly List<RaycastResult> _dropHits = new();

        /// <summary>松手处在不在 <paramref name="area"/> 这块里。
        ///
        /// ⚠ 走 <see cref="EventSystem.RaycastAll"/> 而不是
        /// <c>RectTransformUtility.RectangleContainsScreenPoint</c>(2026-09-04 修):
        /// 那条几何判断在这一屏上恒为 false —— 拖入怎么都不生效,而拖出(判的是「不在里面」)
        /// 却像好的一样,两个方向的表现正好互补,病根藏得很深。射线判的是**真的点到了谁**,
        /// 不依赖任何坐标系换算;网格视口那张 alpha=0 接射线的图(为「空白处也能拖动列表」加的)
        /// 与右栏的描边底图正好各自铺满自己那块,两块都点得到。</summary>
        private bool Inside(RectTransform area, Vector2 screenPosition)
        {
            if (area == null || EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
            _dropHits.Clear();
            EventSystem.current.RaycastAll(pointer, _dropHits);
            foreach (var hit in _dropHits)
                if (hit.gameObject != null && hit.gameObject.transform.IsChildOf(area))
                    return true;
            return false;
        }

        /// <summary>起拖时点亮右栏并写明松手会发生什么。**就地改色改字**,不重绘 ——
        /// 理由见 <see cref="_sideRect"/> 那组字段的注释。</summary>
        private void ShowDropHint(string text, Color accent)
        {
            if (_sideFrame != null) _sideFrame.color = accent;
            if (_dropHint == null) return;
            _dropHint.SetActive(true);
            if (_dropHintLabel != null)
            {
                _dropHintLabel.text = text;
                _dropHintLabel.color = accent;
            }
        }

        private void HideDropHint()
        {
            if (_sideFrame != null) _sideFrame.color = _sideFrameRest;
            if (_dropHint != null) _dropHint.SetActive(false);
        }

        /// <summary>点一张牌 = 看它 + 销掉新字红旗(稿:「新字的红旗点一下就消」)。</summary>
        private void Select(string cardId)
        {
            _selected = cardId;
            if (MetaRules.IsCardUnseen(_meta, cardId))
            {
                MetaRules.MarkCardSeen(_meta, cardId);
                _save();
            }
            Rebuild();
        }

        // ================= 右栏 =================

        private void BuildSide(Transform parent)
        {
            CharDef selected = null;
            if (_selected != null) _graph.TryGet(_selected, out selected);
            bool owned = selected != null && _meta.OwnedCards.Contains(selected.Id);

            var side = Ui.OutlinedPanel(parent, "Side", Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            Ui.Anchor((RectTransform)side.transform, new Vector2(1, 0), Vector2.one,
                new Vector2(-SideW, 0), Vector2.zero);
            // 右栏整块就是拖拽的落点区:描边在拖拽时换色,底部那条提示带写明松手会发生什么
            _sideRect = (RectTransform)side.transform;
            _sideFrame = side;
            _sideFrameRest = side.color;

            // 头:标题 +(选中时)关闭
            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            string title = selected == null
                ? Strings.T("collection.side.title_deck")
                : (owned ? Strings.T("collection.side.title_detail")
                         : Strings.T("collection.side.title_detail_locked"));
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            var headSpring = Ui.Panel(head.transform, "Spring");
            headSpring.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (selected != null)
                Ui.RoundButton(head.transform, Strings.T("common.close"),
                    () => { _selected = null; Rebuild(); }, new Color(0, 0, 0, 0), Theme.TextDim, 22,
                    new Vector2(44, 44));

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            // 身:内部滚动 —— 详情一条没删,靠滚动装下,两个操作钮固定在栏底不随滚动跑
            var body = Ui.ScrollList(side.transform, "Body", 0, out var content);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SideFootH), new Vector2(-SidePad, -SideHeadH - 2));
            if (selected == null) BuildDeckPanel(content);
            else BuildDetail(content, selected, owned);

            BuildSideFoot(side.transform, selected, owned);
            BuildDropHint(side.transform);
        }

        /// <summary>拖拽提示带:平时藏着,起拖那一刻就地点亮。建在右栏最后 = 画在最上层。</summary>
        private void BuildDropHint(Transform parent)
        {
            var hint = Ui.CardPanel(parent, "DropHint", Theme.PanelInset, 16);
            Ui.Anchor((RectTransform)hint.transform, Vector2.zero, new Vector2(1, 0),
                new Vector2(17, SideFootH), new Vector2(-17, SideFootH + 63));
            _dropHintLabel = Ui.ThemedLabel(hint.transform, "", 21, Theme.Jade, Theme.TitleFont);
            Ui.Stretch(_dropHintLabel.rectTransform);
            _dropHint = hint.gameObject;
            _dropHint.SetActive(false);
        }

        private void BuildSideFoot(Transform parent, CharDef selected, bool owned)
        {
            var foot = Ui.Row(parent, "Foot", 13);
            var footLayout = foot.GetComponent<HorizontalLayoutGroup>();
            footLayout.padding = new RectOffset(17, 17, 17, 17);
            footLayout.childForceExpandWidth = true;
            Ui.Anchor((RectTransform)foot.transform, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, SideFootH));

            if (selected == null)
            {
                int upgradable = FirstUpgradable(out string first);
                var button = Ui.PillButton(foot.transform,
                    Strings.T("collection.button.goto_upgrade", ("count", upgradable)),
                    () =>
                    {
                        // 顺带把筛选清回「全部」:那张字未必在当前这一系里,
                        // 只换 _selected 的话右栏出了详情、左边网格里却找不到它
                        if (first == null) return;
                        _filterIsAll = true;
                        _filter = null;
                        _upOnly = false;
                        _wantOnly = false;
                        _gridScroll = 1f;   // 换了一批内容,停在原位置没有意义
                        Select(first);
                    },
                    upgradable > 0 ? Theme.Jade : Theme.PanelInset,
                    upgradable > 0 ? Color.white : Theme.LockGray, 24, new Vector2(0, 75));
                button.GetComponent<LayoutElement>().flexibleWidth = 1;
                button.interactable = upgradable > 0;
                return;
            }

            if (!owned)
            {
                // 「去开宝箱」= 回主界面,那是宝箱的唯一入口
                var button = Ui.PillButton(foot.transform, Strings.T("collection.button.locked"),
                    () => _onBack(), Theme.ShopNav, Color.white, 24, new Vector2(0, 75));
                button.GetComponent<LayoutElement>().flexibleWidth = 1;
                return;
            }

            bool inDeck = _meta.Deck.Contains(selected.Id);
            var deckButton = Ui.PillButton(foot.transform,
                inDeck ? Strings.T("collection.button.unequip") : Strings.T("collection.button.equip"),
                () => ToggleDeck(selected.Id),
                inDeck ? Theme.LockedBg : Theme.ExitPink,
                inDeck ? Theme.TextMain : Color.white, 24, new Vector2(0, 75));
            deckButton.GetComponent<LayoutElement>().flexibleWidth = 1;

            int level = MetaRules.CardLevel(_meta, selected.Id);
            bool maxed = level >= MetaRules.MaxCardLevel;
            bool canUpgrade = MetaRules.CanUpgradeCard(_meta, selected.Id, selected.Rarity);
            string upText = maxed
                ? Strings.T("collection.button.maxed")
                : (canUpgrade ? Strings.T("collection.button.upgrade", ("level", level + 1))
                              : Strings.T("collection.button.upgrade_short"));
            var upButton = Ui.PillButton(foot.transform, upText,
                () => ShowUpgradePreview(selected.Id),
                maxed ? Theme.GoldSoft : (canUpgrade ? Theme.Jade : Theme.PanelInset),
                maxed ? Theme.GoldDeep : (canUpgrade ? Color.white : Theme.LockGray),
                24, new Vector2(0, 75));
            upButton.GetComponent<LayoutElement>().flexibleWidth = 1;
            upButton.interactable = canUpgrade;
        }

        private int FirstUpgradable(out string cardId)
        {
            cardId = null;
            int count = 0;
            foreach (var def in _all)
            {
                if (!_meta.OwnedCards.Contains(def.Id)) continue;
                if (!MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity)) continue;
                count++;
                cardId ??= def.Id;
            }
            return count;
        }

        // ---- 右栏 · 没选中:出阵编组 ----

        private void BuildDeckPanel(Transform parent)
        {
            var slots = Section(parent, Strings.T("collection.side.section.deck",
                ("count", _meta.Deck.Count), ("limit", MetaRules.DeckLimit)));
            Transform row = null;
            for (int i = 0; i < MetaRules.DeckLimit; i++)
            {
                if (i % 5 == 0)
                {
                    var rowGo = Ui.Row(slots, $"SlotRow{i / 5}", 10);
                    var rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                    rowLayout.childForceExpandWidth = true;
                    rowLayout.childAlignment = TextAnchor.UpperCenter;
                    Ui.Sized(rowGo, 0, SlotRowH, flexWidth: 1);
                    row = rowGo.transform;
                }
                BuildSlot(row, i < _meta.Deck.Count ? _meta.Deck[i] : null);
            }

            var quota = Section(parent, Strings.T("collection.side.section.quota",
                ("limit", MetaRules.DeckPerElementLimit)));
            foreach (var element in new[] { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth })
                BuildQuotaBar(quota, element);

            var tip = Section(parent, Strings.T("collection.side.section.tip"));
            string tipText = Strings.T("collection.side.tip_body",
                ("min", MetaRules.DeckMinimum), ("max", MetaRules.DeckLimit),
                ("perElement", MetaRules.DeckPerElementLimit));
            var text = Ui.ThemedLabel(tip, tipText, 19, Theme.TextDim);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(text.gameObject, 0,
                Ui.WrappedTextHeight(tipText, 19, SideW - SidePad * 2), flexWidth: 1);
        }

        /// <summary>出阵表的一格 = **缩小版字卡**(稀有度框 + 字,不挂动效、不印拼音)+ 牌下等级。
        ///
        /// 2026-09-04:原先是属性色圆角格 + 字 + 等级,只说得出「什么系」,说不出「什么档」——
        /// 而出阵表里最该一眼看见的正是稀有度。牌按 <see cref="SlotTile"/> 的 0.8 竖版比例定死,
        /// 格子的富余宽度让给间距,不去拉牌 —— 拉了框上的纹样就变形。</summary>
        private void BuildSlot(Transform parent, string cardId)
        {
            var cell = Ui.VStack(parent, cardId == null ? "Slot_Free" : $"Slot_{cardId}", 5);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(cell, height: SlotRowH, flexWidth: 1);

            if (cardId == null || !_graph.TryGet(cardId, out var def))
            {
                var free = Ui.OutlinedPanel(cell.transform, "Free", Theme.PanelPaper, Theme.PanelBorder, 14, 2);
                Ui.Sized(free.gameObject, SlotTile.x, SlotTile.y);
                Ui.ThemedLabel(free.transform, Strings.T("collection.side.slot_free"), 24, Theme.LockGray);
                return;
            }

            var tile = Ui.MiniGlyphTile(cell.transform, def, SlotTile);
            var button = tile.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Image>();
            button.onClick.AddListener(() => Select(cardId));
            Ui.ThemedLabel(cell.transform, $"Lv.{MetaRules.CardLevel(_meta, cardId)}", 16, Theme.TextDim);

            // 拖出右栏 = 卸下。这一格坐在右栏的滚动容器里,所以竖向手势照旧交给它滚动,
            // 横着拽才算「把这张字拿出来」——与网格那边同一条分流
            DragToDeck.Attach(tile, def.Id, Theme.GlyphColor(def.Element),
                position => DropFromSlot(cardId, position),
                () => ShowDropHint(Strings.T("collection.drag.off_deck"), Theme.ExitPink));
        }

        private void BuildQuotaBar(Transform parent, Element element)
        {
            int count = 0;
            foreach (var id in _meta.Deck)
                if (_graph.TryGet(id, out var def) && (def.Element ?? Element.Heart) == element)
                    count++;
            bool full = count >= MetaRules.DeckPerElementLimit;

            var row = Ui.Row(parent, $"Quota_{element}", 15);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 34, flexWidth: 1);
            Ui.ThemedLabel(row.transform, CharInfo.ElementName(element), 27,
                Theme.GlyphColor(element), Theme.TitleFont);
            var bar = Ui.Bar(row.transform, (float)count / MetaRules.DeckPerElementLimit,
                Theme.ElementColor(element), new Vector2(0, 13));
            bar.GetComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(row.transform, $"{count}/{MetaRules.DeckPerElementLimit}", 19,
                full ? Theme.CinnabarDark : Theme.TextDim);
        }

        // ---- 右栏 · 选中:字牌详情 ----

        private void BuildDetail(Transform parent, CharDef def, bool owned)
        {
            int level = owned ? MetaRules.CardLevel(_meta, def.Id) : 1;
            BuildIdentity(parent, def, owned);
            if (!owned) BuildHowToGet(parent, def);
            if (owned) BuildLevel(parent, def);
            BuildStats(parent, def, level, owned);
            BuildFunction(parent, def, level);
            if (!def.IsLeaf) BuildRecipe(parent, def);
            if (def.Element is { } element && element != Element.Heart) BuildWuxing(parent, element);
        }

        private void BuildIdentity(Transform parent, CharDef def, bool owned)
        {
            float glossWidth = SideW - SidePad * 2 - BigCardSize.x - 21;
            float glossHeight = string.IsNullOrEmpty(def.Gloss)
                ? 0f : Ui.WrappedTextHeight(def.Gloss, 20, glossWidth) + 12;
            var row = Ui.Row(parent, "Identity", 21);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            // 大牌与右侧信息谁高按谁:长释义不该被牌高截掉
            Ui.Sized(row, 0, Mathf.Max(BigCardSize.y, 48 + 31 + 24 + glossHeight), flexWidth: 1);

            Ui.GlyphTile(row.transform, def, false, null, BigCardSize, locked: !owned);

            var meta = Ui.VStack(row.transform, "Meta", 12);
            meta.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            meta.AddComponent<LayoutElement>().flexibleWidth = 1;
            float metaWidth = glossWidth;

            var nameRow = Ui.Row(meta.transform, "Name", 12);
            nameRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.ThemedLabel(nameRow.transform, def.Id, 42, Theme.TextMain, Theme.TitleFont);
            if (!string.IsNullOrEmpty(def.Pinyin))
                Ui.ThemedLabel(nameRow.transform, def.Pinyin, 21, Theme.TextDim);

            var chips = Ui.Row(meta.transform, "Chips", 8);
            chips.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Chip(chips.transform, CharInfo.RarityName(def.Rarity),
                Theme.RarityColor(def.Rarity), Color.white, 19);
            string elementChip = def.Element is { } e
                ? Strings.T("collection.side.element_chip", ("element", CharInfo.ElementName(e)))
                : Strings.T("char.element.neutral");
            Ui.Chip(chips.transform, elementChip,
                Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element), 19);

            if (!string.IsNullOrEmpty(def.Gloss))
            {
                var gloss = Ui.ThemedLabel(meta.transform, def.Gloss, 20, Theme.TextDim);
                gloss.alignment = TextAnchor.UpperLeft;
                gloss.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(gloss.gameObject, metaWidth, Ui.WrappedTextHeight(def.Gloss, 20, metaWidth));
            }
        }

        /// <summary>未拥有:这张字**怎么才能拿到**。写的是真规则 —— 没收集过的字只出宝箱,
        /// 商城字摊按 ShopView 的池子只卖部件和你已有的字。</summary>
        private void BuildHowToGet(Transform parent, CharDef def)
        {
            var section = Section(parent, Strings.T("collection.side.section.get"));
            GetRow(section, Theme.RarityColor(def.Rarity), Theme.TextDim,
                Strings.T("collection.side.get.chest", ("hint", ChestHint(def.Rarity))));
            GetRow(section, Theme.LockedBg, Theme.LockGray, Strings.T("collection.side.get.shop"));
        }

        private void GetRow(Transform parent, Color iconColor, Color fg, string text)
        {
            var row = Ui.Row(parent, "GetRow", 17);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            float width = SideW - SidePad * 2 - 54 - 17;
            float height = Mathf.Max(54, Ui.WrappedTextHeight(text, 19, width));
            Ui.Sized(row, 0, height, flexWidth: 1);

            var icon = Ui.CardPanel(row.transform, "Icon", iconColor, 12);
            Ui.Sized(icon.gameObject, 54, 54);

            var label = Ui.ThemedLabel(row.transform, text, 19, fg);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, width, height);
        }

        /// <summary>这一档从哪只匣子起开得出。口径同 <see cref="ChestRules"/> 的稀有度权重表。</summary>
        private static string ChestHint(CardRarity rarity) => rarity switch
        {
            CardRarity.White => Strings.T("collection.chest_hint.common"),
            CardRarity.Green => Strings.T("collection.chest_hint.common"),
            CardRarity.Blue => Strings.T("collection.chest_hint.blue"),
            CardRarity.Purple => Strings.T("collection.chest_hint.purple"),
            CardRarity.Gold => Strings.T("collection.chest_hint.gold"),
            CardRarity.Orange => Strings.T("collection.chest_hint.orange"),
            _ => Strings.T("collection.chest_hint.red"),
        };

        private void BuildLevel(Transform parent, CharDef def)
        {
            int level = MetaRules.CardLevel(_meta, def.Id);
            bool maxed = level >= MetaRules.MaxCardLevel;
            _meta.CardCopies.TryGetValue(def.Id, out int copies);
            var section = Section(parent, Strings.T("collection.side.section.level"));

            var row = Ui.Row(section, "LevelRow", 15);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(row.transform, $"Lv.{level}", 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(row.transform, $"/ {MetaRules.MaxCardLevel}", 21, Theme.LockGray);

            var pips = Ui.Row(row.transform, "Pips", 4);
            pips.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(pips, 0, 11, flexWidth: 1);
            bool canUpgrade = MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity);
            for (int i = 1; i <= MetaRules.MaxCardLevel; i++)
            {
                var pip = Ui.Panel(pips.transform, $"Pip{i}");
                var image = pip.AddComponent<Image>();
                image.sprite = Theme.Rounded(5);
                image.type = Image.Type.Sliced;
                image.color = i <= level ? Theme.InkSoft
                    : (i == level + 1 && canUpgrade ? Theme.Jade : Theme.PaperDim);
                Ui.Sized(pip, 0, 11, flexWidth: 1);
            }

            if (maxed) return;
            int needed = MetaRules.CopiesRequired(level, def.Rarity);
            int ink = MetaRules.InkRequired(level, def.Rarity);
            var costs = Ui.Row(section, "Costs", 13);
            costs.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(costs, 0, 75, flexWidth: 1);
            CostBox(costs.transform, Strings.T("collection.side.cost.copies"), $"{copies} / {needed}", copies >= needed);
            CostBox(costs.transform, Strings.T("collection.side.cost.ink"), $"{_meta.Ink} / {ink}", _meta.Ink >= ink);
        }

        private void CostBox(Transform parent, string key, string value, bool ok)
        {
            var box = Ui.CardPanel(parent, "Cost", ok ? Theme.AdGreenBg : Theme.PanelInset, 14);
            Ui.Sized(box.gameObject, 0, 75, flexWidth: 1);
            var stack = Ui.VStack(box.transform, "Stack", 4);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, key, 18, Theme.LockGray);
            Ui.ThemedLabel(stack.transform, value, 25, ok ? Theme.UpgradeText : Theme.CinnabarDark);
        }

        private void BuildStats(Transform parent, CharDef def, int level, bool owned)
        {
            var stats = CollectionStats.Of(def, level);
            if (stats.Count == 0) return;
            var section = Section(parent, owned
                ? Strings.T("collection.side.section.stats", ("level", level))
                : Strings.T("collection.side.section.stats_base"));

            var row = Ui.Row(section, "Stats", 13);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 104, flexWidth: 1);
            foreach (var stat in stats)
            {
                var box = Ui.OutlinedPanel(row.transform, "Stat", Theme.CardWhite, Theme.PanelBorder, 14, 2);
                Ui.Sized(box.gameObject, 0, 104, flexWidth: 1);
                var stack = Ui.VStack(box.transform, "Stack", 2);
                Ui.Stretch((RectTransform)stack.transform);
                Ui.ThemedLabel(stack.transform, stat.Label, 18, Theme.LockGray);
                Ui.ThemedLabel(stack.transform, stat.Value.ToString(), 38, stat.Color, Theme.TitleFont);
                Ui.ThemedLabel(stack.transform, stat.Note, 16, Theme.LockGray);
            }
        }

        private void BuildFunction(Transform parent, CharDef def, int level)
        {
            var section = Section(parent, Strings.T("collection.side.section.func"));
            string text = CharInfo.EffectsText(def, level);
            float width = SideW - SidePad * 2;

            var box = Ui.OutlinedPanel(section, "Func", Theme.CardWhite, Theme.PanelBorder, 14, 2);
            Ui.Sized(box.gameObject, 0,
                Mathf.Max(60, Ui.WrappedTextHeight(text, 21, width - 32) + 30), flexWidth: 1);

            // 左边一条属性色的粗边(稿 .desc 的 border-left)
            var edge = Ui.Panel(box.transform, "Edge");
            edge.AddComponent<Image>().color = Theme.ElementColor(def.Element);
            Ui.Anchor((RectTransform)edge.transform, Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(6, 0));

            var label = Ui.ThemedLabel(box.transform, text, 21, Theme.TextMain);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Anchor(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(19, 15), new Vector2(-13, -15));
        }

        private void BuildRecipe(Transform parent, CharDef def)
        {
            var section = Section(parent, Strings.T("collection.side.section.recipe"));
            var row = Ui.Row(section, "Recipe", 13);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 88, flexWidth: 1);

            for (int i = 0; i < def.Recipe.Count; i++)
            {
                string part = def.Recipe[i];
                Element? element = _graph.TryGet(part, out var partDef) ? partDef.Element : null;
                RecipePart(row.transform, part, element, false);
                Ui.ThemedLabel(row.transform,
                    i == def.Recipe.Count - 1 ? Strings.T("collection.side.recipe_to")
                                              : Strings.T("collection.side.recipe_plus"),
                    24, Theme.LockGray);
            }
            RecipePart(row.transform, def.Id, def.Element, true);
        }

        private void RecipePart(Transform parent, string id, Element? element, bool isOutput)
        {
            var go = isOutput
                ? Ui.OutlinedPanel(parent, "Part", Theme.ElementSoft(element), Theme.ElementColor(element), 16, 3).gameObject
                : Ui.CardPanel(parent, "Part", Theme.ElementSoft(element), 16).gameObject;
            Ui.Sized(go, 88, 88);
            var stack = Ui.VStack(go.transform, "Stack", 2);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, id, 40, Theme.ElementSoftFg(element), Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                element is { } e ? CharInfo.ElementName(e) : Strings.T("char.element.neutral"),
                16, Theme.ElementSoftFg(element));
        }

        private void BuildWuxing(Transform parent, Element element)
        {
            var section = Section(parent, Strings.T("collection.side.section.wuxing"));
            var row = Ui.Row(section, "Wuxing", 13);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 84, flexWidth: 1);

            var victim = WuxingResolver.Victim(element);
            var counter = WuxingResolver.Counter(element);
            if (victim is { } v)
                WuxingBox(row.transform, v, Theme.WarnBg, Theme.WarnText,
                    Strings.T("collection.side.ke", ("element", CharInfo.ElementName(v))));
            if (counter is { } c)
                WuxingBox(row.transform, c, Theme.PanelInset, Theme.TextDim,
                    Strings.T("collection.side.bei", ("element", CharInfo.ElementName(c))));
        }

        private void WuxingBox(Transform parent, Element element, Color bg, Color fg, string text)
        {
            var box = Ui.CardPanel(parent, "Wx", bg, 14);
            Ui.Sized(box.gameObject, 0, 84, flexWidth: 1);
            var row = Ui.Row(box.transform, "Row", 13);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            Ui.Stretch((RectTransform)row.transform);

            var dot = Ui.CardPanel(row.transform, "Dot", Theme.ElementColor(element), 10);
            Ui.Sized(dot.gameObject, 36, 36);
            Ui.ThemedLabel(dot.transform, CharInfo.ElementName(element), 21, Color.white, Theme.TitleFont);

            var label = Ui.ThemedLabel(row.transform, text, 19, fg);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, 0, 84, flexWidth: 1);
        }

        /// <summary>右栏里的一个小节:标题 + 一条横线,下面是一个竖排容器(返回值)。</summary>
        private static Transform Section(Transform parent, string title)
        {
            var head = Ui.Row(parent, "SectionHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            var rule = Ui.Panel(head.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, 0, 2, flexWidth: 1);

            var stack = Ui.VStack(parent, "Section", 11);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            // ⚠ 只给弹性宽,**不要**设 preferredHeight —— LayoutElement 的 layoutPriority 压过
            // 布局组自己算出来的高,写个 0 会把整节压没(而且是静默的:节点还在,高度是 0)
            Ui.Sized(stack, flexWidth: 1);
            return stack.transform;
        }

        // ================= 动作 =================

        private void ToggleDeck(string cardId)
        {
            var deck = new List<string>(_meta.Deck);
            bool removing = deck.Contains(cardId);
            if (removing) deck.Remove(cardId);
            else deck.Add(cardId);

            if (MetaRules.TrySetDeck(_meta, deck, _graph))
            {
                _save();
                Rebuild();
                return;
            }

            Rebuild();
            ShowAlert(Strings.T("collection.alert.deck_limited_title"), removing
                ? Strings.T("collection.alert.deck_min_body", ("min", MetaRules.DeckMinimum))
                : Strings.T("collection.alert.deck_add_fail_body", ("cardId", cardId),
                    ("min", MetaRules.DeckMinimum), ("max", MetaRules.DeckLimit),
                    ("perElementLimit", MetaRules.DeckPerElementLimit)));
        }

        /// <summary>升级前 preview:前后两级效果对比 + 消耗,确认才扣(2026-07-20)。
        /// 稿上没画这一层,但升级是**不可逆支出** —— 弹窗族的口径是「不可逆的都要在按下去之前说清楚」。</summary>
        private void ShowUpgradePreview(string cardId)
        {
            var def = _graph.Get(cardId);
            int level = MetaRules.CardLevel(_meta, cardId);
            _meta.CardCopies.TryGetValue(cardId, out int copies);
            int copiesNeeded = MetaRules.CopiesRequired(level, def.Rarity);
            int inkNeeded = MetaRules.InkRequired(level, def.Rarity);

            if (_modal != null) Destroy(_modal);
            var overlay = Ui.ModalShell(transform, Strings.T("collection.modal.upgrade_title", ("cardId", cardId)),
                new Vector2(340, 275), dismissable: true, out var stack);
            _modal = overlay;

            Ui.GlyphTile(stack, def, false, null, new Vector2(144, 180));
            Ui.ThemedLabel(stack, $"Lv.{level} → Lv.{level + 1}", 21, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack,
                $"{CharInfo.EffectsText(def, level)}\n↓\n{CharInfo.EffectsText(def, level + 1)}",
                17, Theme.TextDim);
            Ui.ThemedLabel(stack,
                Strings.T("collection.modal.upgrade_cost", ("needed", copiesNeeded), ("copies", copies),
                    ("inkNeeded", inkNeeded), ("ink", _meta.Ink)),
                16, Theme.TextDim);

            var buttons = Ui.Row(stack, "Buttons", 14);
            Ui.PillButton(buttons.transform, Strings.T("collection.modal.confirm_upgrade_button"), () =>
            {
                Destroy(overlay); // 先关弹窗:Upgrade 会 Rebuild 清根,顺序反了会留残影
                Upgrade(cardId);
            }, Theme.Jade, Color.white, 18, new Vector2(150, 52));
            Ui.PillButton(buttons.transform, Strings.T("common.reconsider"), () => Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 52));
        }

        private void Upgrade(string cardId)
        {
            var def = _graph.Get(cardId);
            if (MetaRules.TryUpgradeCard(_meta, cardId, def.Rarity))
            {
                _save();
                Rebuild();
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
