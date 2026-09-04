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
    /// · 右栏常驻出阵编组(15 格 + 每系配额)+ 一个「去升级」钮。
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
        private const float SideFootH = 100f;   // 右栏底(8pt 内边距 + 36pt 按钮)
        private const float SidePad = 21f;      // 右栏内边距 10pt

        // 网格:5 列,每张 103×128pt;列距 8pt、行距 14pt
        private const int GridColumns = 5;
        private const float GridGapX = 17f;
        private const float GridGapY = 29f;
        private static readonly Vector2 CardSize = new(216f, 268f);
        // 出阵格里的缩小版字卡:0.8 竖版比例(与框素材同比,拉了就变形);格高再加牌下那行等级
        private static readonly Vector2 SlotTile = new(74f, 92f);
        // 升级确认弹窗:520×320pt(稿 Upgrade.dc.html)。两段并排,再挤就要把
        // 「变化前 → 变化后」压成一行文字,而那正是这一屏的全部意义
        private const float UpgradeW = 1088f;
        private const float UpgradeH = 670f;
        private static readonly Vector2 UpgradeTile = new(130f, 163f);
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
        /// <summary>网格排序(2026-09-05 用户拍板,取代原先「仅看可升级」/「只看未拥有」
        /// 两个筛选 chip):三选一,点击即换。
        ///
        /// 排序而不是筛选,是因为筛选把其余的字**藏起来** —— 而收集页的底色是「我手上有什么、
        /// 还差什么」,藏起来正好把这一页最想让人看见的东西拿掉了。排序把要紧的顶到最前,
        /// 其余仍在后面看得到,同一个诉求代价小得多。</summary>
        private enum SortMode { Rarity, Upgradable, Fresh }

        private SortMode _sort = SortMode.Rarity; // 默认稀有度(用户拍板)
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

            int upgradable = 0, fresh = 0;
            foreach (var def in _all)
            {
                if (!_meta.OwnedCards.Contains(def.Id)) continue;
                if (MetaRules.CanUpgradeCard(_meta, def.Id, def.Rarity)) upgradable++;
                if (MetaRules.IsCardUnseen(_meta, def.Id)) fresh++;
            }
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
            _gridRect = rect;   // 拖拽落点判定要用:拖出这块 = 编入出阵

            var list = Visible();
            if (list.Count == 0)
            {
                // 排序取代筛选之后(2026-09-05)这一支现实中已经到不了:网格只受属性页签影响,
                // 而每一系都有字。留着是给数据兜底 —— 哪天字表里某一系被清空,空白网格
                // 比一句话更难判断是「没有」还是「加载失败」。旧的两条文案随筛选一起下架。
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
            // 也省得玩家非要够到那块面板才算数
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
            // 右栏整块就是拖拽的落点区:描边在拖拽时换色,底部那条提示带写明松手会发生什么
            _sideRect = (RectTransform)side.transform;
            _sideFrame = side;
            _sideFrameRest = side.color;

            // 头:只剩标题(关闭钮随详情一起搬去了弹窗右上角)
            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("collection.side.title_deck"), 19, Theme.LockGray);

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            // 身:内部滚动 —— 15 格出阵表 + 每系配额靠滚动装下,「去升级」钮固定在栏底
            var body = Ui.ScrollList(side.transform, "Body", 0, out var content);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SideFootH), new Vector2(-SidePad, -SideHeadH - 2));
            BuildDeckPanel(content);

            BuildSideFoot(side.transform);
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

        /// <summary>右栏底:只剩「去升级」。字卡自己的两个钮(编入出阵 / 升级)随详情
        /// 搬去了弹窗底部(<see cref="SheetActions"/>)—— 那两个钮作用于**某一张字**,
        /// 而这一栏现在只讲出阵表,把它们留在这儿就没有主语了。</summary>
        private void BuildSideFoot(Transform parent)
        {
            var foot = Ui.Row(parent, "Foot", 13);
            var footLayout = foot.GetComponent<HorizontalLayoutGroup>();
            footLayout.padding = new RectOffset(17, 17, 17, 17);
            footLayout.childForceExpandWidth = true;
            Ui.Anchor((RectTransform)foot.transform, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, SideFootH));

            int upgradable = FirstUpgradable(out string first);
            var button = Ui.PillButton(foot.transform,
                Strings.T("collection.button.goto_upgrade", ("count", upgradable)),
                () =>
                {
                    // 顺带把属性页签清回「全部」:那张字未必在当前这一系里,
                    // 弹窗开出来了、关掉之后左边网格里却找不到它。
                    // 排序也一并切到「可升级」(2026-09-05):这个钮的意图就是「带我去那张字」,
                    // 而那个排序恰好把它顶到网格最前 —— 清了页签还要在几十张里找,等于没带到。
                    if (first == null) return;
                    _filterIsAll = true;
                    _filter = null;
                    _sort = SortMode.Upgradable;
                    _gridScroll = 1f;   // 换了一批内容,停在原位置没有意义
                    Select(first);
                },
                upgradable > 0 ? Theme.Jade : Theme.PanelInset,
                upgradable > 0 ? Color.white : Theme.LockGray, 24, new Vector2(0, 75));
            button.GetComponent<LayoutElement>().flexibleWidth = 1;
            button.interactable = upgradable > 0;
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

            bool inDeck = _meta.Deck.Contains(def.Id);
            var deckButton = Ui.PillButton(parent,
                inDeck ? Strings.T("collection.button.unequip") : Strings.T("collection.button.equip"),
                // 重开弹窗:ToggleDeck 内部会 Rebuild,而 Rebuild 会 Ui.Clear 掉这张详情 ——
                // 原先详情在右栏里,改完状态它还在,玩家当场看得见「已编入」。改走弹窗之后
                // 不补这一句,点完钮整屏就退回网格,反馈全丢了。
                // 拖拽那两条路径(DropFromGrid / DropFromSlot)刻意不重开:那时玩家在拖牌,
                // 手上没有详情这一屏,凭空弹一张出来是打断。
                () => { ToggleDeck(def.Id); ShowCharSheet(def); },
                inDeck ? Theme.LockedBg : Theme.ExitPink,
                inDeck ? Theme.TextMain : Color.white, 24, new Vector2(0, 75));
            deckButton.GetComponent<LayoutElement>().flexibleWidth = 1;

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
            var slots = CharSheetSections.Section(parent, Strings.T("collection.side.section.deck",
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

            var quota = CharSheetSections.Section(parent, Strings.T("collection.side.section.quota",
                ("limit", MetaRules.DeckPerElementLimit)));
            foreach (var element in new[] { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth })
                BuildQuotaBar(quota, element);

            var tip = CharSheetSections.Section(parent, Strings.T("collection.side.section.tip"));
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
