using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>怪物图鉴(2026-07-22 起;2026-09-03 按稿重写)。版式基线 =
    /// <c>docs/design/ui/scenes/Bestiary.dc.html</c>:顶栏统计 / 层段+Boss 筛选栏 / 左网格右详情 / 底部领赏。
    ///
    /// ⚠ 尺寸常量都是**逻辑单位**,由稿子的 pt 换算而来(1pt = 2.093,见 Device.dc.html)。
    /// 与 <see cref="CollectionView"/> 同一套骨架 —— 两屏的稿子本来就是照着彼此画的。
    ///
    /// 此前是每页 8 只的翻页网格,点条目即弹窗并**当场发赏钱**;现在:
    /// · 网格内部滚动,不再翻页;
    /// · 详情不弹窗,常驻右栏 —— 没选中时是收录总览(层段进度 + 赏钱规则);
    /// · 赏钱改成**显式领取**:点条目只是选中,底部按钮按下去才入账(稿 .claim / 一键领取)。
    ///
    /// 稿上有而这里**刻意改掉**的两处:
    /// · 怪牌角标写「后排」——2026-09-03 已拍板详情只留射程不留站位(见 EnemyInfo.BuildTags),
    ///   同一条理由适用于这里,改成「远程」(数据上全部后排怪都是远程,信息量不减);
    /// · 克制那格写「×1.5 且无视护甲」——「克制让减免失效」那条补丁随乘法层一起退场了
    ///   (见 EnemyInfo.DefenseText),只留 ×1.5。</summary>
    public sealed class BestiaryView : MonoBehaviour
    {
        // ---- 稿上的骨架尺寸(pt → 逻辑单位) ----
        private const float TopH = 80f;        // 顶栏 38pt
        private const float FilterH = 56f;     // 筛选栏 27pt
        private const float Gap = 13f;         // 行间距 6pt
        private const float SideW = 502f;      // 右栏 240pt
        private const float MainGap = 19f;     // 网格与右栏之间 9pt
        private const float SideHeadH = 54f;   // 右栏头 26pt
        private const float SideFootH = 105f;  // 右栏底(8pt 内边距 ×2 + 34pt 按钮)
        private const float SidePad = 21f;     // 右栏内边距 10pt

        // 网格:4 列,立绘块 80×80pt(mob 素材是 1:1);列距 8pt、行距 12pt
        private const int GridColumns = 4;
        private const float GridGapX = 17f;
        private const float GridGapY = 25f;
        private const float BlockSize = 167f;
        private const float CellWidth = 195f;
        private const float HeroBlock = 159f;  // 详情大立绘 76pt

        /// <summary>筛选栏的两个特殊档:全部(不筛)与 Boss(跨层段单看)。
        /// 其余取值 ≥ 0 = 层段下标 —— Boss 不是第五个层段,它在每个层段的末层都可能出,
        /// 所以是**另一条轴**(稿 note-bestiary)。</summary>
        private const int FilterAll = -1;
        private const int FilterBoss = -2;

        /// <summary>层段页签的配色。层段本身没有五行属性,这只是给四段各一个可辨识的色相
        /// (稿 BAND_EL:字林木 / 词渊水 / 文山土 / 墨海金)。</summary>
        private static readonly Element[] BandTint =
        {
            Element.Wood, Element.Water, Element.Earth, Element.Metal,
        };

        private EndlessConfig _endless;
        private MetaState _meta;
        private Action _onBack;
        private Action _save;

        private List<EnemyDef> _all;                    // 图鉴全集(按配置顺序)
        private readonly Dictionary<string, int> _band = new();   // 怪 id → 首现层段下标
        private readonly Dictionary<string, int> _depth = new();  // 怪 id → 首现层号
        private readonly Dictionary<string, int> _order = new();  // 怪 id → 配置顺序(排序兜底)

        private int _filter = FilterAll;
        private string _selected;
        private int _phase;             // 选中的是 Boss 时,看第几相
        /// <summary>网格滚动位置(1 = 顶部)。整页全量重建,不记着它的话点第三行某只怪
        /// 会当场弹回顶部(卡组页 2026-09-03 实机反馈,同一个坑)。</summary>
        private float _gridScroll = 1f;
        private ScrollRect _grid;

        public void Init(CampaignConfig campaign, MetaState meta, Action save, Action onBack)
        {
            _endless = campaign.Endless;
            _meta = meta;
            _save = save;
            _onBack = onBack;
            _all = CollectEnemies(campaign);
            BuildIndex(campaign);
            Rebuild();
        }

        /// <summary>图鉴全集 = 各层段的杂兵池 + Boss 池 + 成语 Boss(按 id 去重,保持配置顺序)。</summary>
        internal static List<EnemyDef> CollectEnemies(CampaignConfig campaign)
        {
            var all = new List<EnemyDef>();
            var seen = new HashSet<string>();
            void Add(EnemyDef def)
            {
                if (def != null && seen.Add(def.Id)) all.Add(def);
            }

            if (campaign.Endless?.Bands != null)
                foreach (var band in campaign.Endless.Bands)
                {
                    foreach (var enemy in band.EnemyPool) Add(enemy);
                    foreach (var boss in band.BossPool) Add(boss);
                    foreach (var idiom in band.IdiomBossPool)
                        Add(EndlessGenerator.BuildIdiomBoss(idiom));
                }
            return all;
        }

        /// <summary>每只怪的**首现**层段与层号。层段的 enemyPool 是累积的(词渊那份含字林全部),
        /// 所以「它属于哪一段」= 第一个收了它的段;层号还要跟 <see cref="EnemyDef.MinDepth"/> 取大
        /// (墨渍在字林池里,但配了 minDepth 6,前 5 层根本不会出)。
        /// Boss 只在末层出,故取该段内第一个 Boss 层。</summary>
        private void BuildIndex(CampaignConfig campaign)
        {
            _band.Clear();
            _depth.Clear();
            _order.Clear();
            for (int i = 0; i < _all.Count; i++) _order[_all[i].Id] = i;
            if (campaign.Endless?.Bands == null) return;

            var bands = campaign.Endless.Bands;
            for (int b = 0; b < bands.Count; b++)
            {
                var band = bands[b];
                foreach (var enemy in band.EnemyPool)
                    Note(enemy.Id, b, Mathf.Max(band.FromDepth, enemy.MinDepth));
                int bossDepth = FirstBossDepth(band.FromDepth);
                foreach (var boss in band.BossPool) Note(boss.Id, b, bossDepth);
                foreach (var idiom in band.IdiomBossPool) Note(idiom.Chars, b, bossDepth);
            }

            void Note(string id, int bandIndex, int fromDepth)
            {
                if (_band.ContainsKey(id)) return;
                _band[id] = bandIndex;
                _depth[id] = fromDepth;
            }
        }

        private int BandCount => _endless?.Bands?.Count ?? 0;

        /// <summary>该层段内第一个 Boss 层(层号是 BossEvery 的倍数)。</summary>
        private int FirstBossDepth(int fromDepth)
        {
            int every = Mathf.Max(1, _endless.BossEvery);
            return (Mathf.Max(1, fromDepth) - 1) / every * every + every;
        }

        // ================= 骨架 =================

        /// <param name="keepScroll">false = 网格归顶。筛选变了才传 false:那时列表换了一批内容。</param>
        private void Rebuild(bool keepScroll = true)
        {
            _gridScroll = keepScroll && _grid != null && !float.IsNaN(_grid.verticalNormalizedPosition)
                ? _grid.verticalNormalizedPosition
                : 1f;
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

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

            int known = 0, pending = 0;
            foreach (var def in _all)
            {
                if (!Known(def)) continue;
                known++;
                if (!Claimed(def)) pending++;
            }

            Ui.ThemedLabel(top.transform, Strings.T("common.bestiary_title"), 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(top.transform,
                Strings.T("bestiary.header.stats", ("unlocked", known), ("total", _all.Count)),
                23, Theme.TextDim);
            if (pending > 0)
                Ui.Chip(top.transform, Strings.T("bestiary.header.unclaimed_chip", ("count", pending)),
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

            BuildFilterTab(bar.transform, FilterAll, Strings.T("bestiary.filter.all"));
            for (int b = 0; b < BandCount; b++)
                BuildFilterTab(bar.transform, b, _endless.Bands[b].Name);
            BuildFilterTab(bar.transform, FilterBoss, Strings.T("bestiary.filter.boss"));

            var spring = Ui.Panel(bar.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(bar.transform, Strings.T("bestiary.sort_hint"), 19, Theme.LockGray);
        }

        /// <summary>一个筛选页签:名 + 「已录/总数」+ 有待领赏时的红点。
        /// Boss 档是墨底金字,与怪牌右上的「四相」标同一套 —— 提醒它不是第五个层段。
        ///
        /// 一只怪也没有的层段**不出页签**:层段的 enemyPool 是累积的,深段完全可能一只新怪
        /// 都不引进(眼下的墨海就与文山同池)。硬画一个「0/0」的空页签既没内容也说不清为什么空,
        /// 那一段真配上专属怪的当天它自己就会长回来。</summary>
        private void BuildFilterTab(Transform parent, int filter, string name)
        {
            bool on = _filter == filter;
            bool isBossTab = filter == FilterBoss;
            int known = 0, total = 0;
            bool hasBounty = false;
            foreach (var def in _all)
            {
                if (!Matches(def, filter)) continue;
                total++;
                if (!Known(def)) continue;
                known++;
                if (!Claimed(def)) hasBounty = true;
            }
            if (total == 0 && filter >= 0) return;

            Element? tint = filter >= 0 && filter < BandTint.Length ? BandTint[filter] : null;
            var go = Ui.Panel(parent, $"Tab_{filter}");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(10);
            image.type = Image.Type.Sliced;
            image.color = on
                ? (isBossTab ? Theme.Ink : (tint is { } t ? Theme.ElementSoft(t) : Theme.PanelInset))
                : new Color(0, 0, 0, 0);
            // 宽度自己算:横排布局组不会替按钮量文字,给 0 就是 0 宽
            string countText = $"{known}/{total}";
            Ui.Sized(go, 32 + name.Length * 29 + 10 + Ui.ChipWidth(countText, 18), FilterH);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { _filter = filter; Rebuild(keepScroll: false); });

            var row = Ui.Row(go.transform, "Row", 10);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 0, 0);
            Ui.Stretch((RectTransform)row.transform);
            Color fg = isBossTab
                ? (on ? Theme.Gold : Theme.GoldBorder)
                : (on ? (tint is { } t2 ? Theme.ElementSoftFg(t2) : Theme.TextMain) : Theme.TextDim);
            Ui.ThemedLabel(row.transform, name, 29, fg, Theme.TitleFont);
            Ui.Chip(row.transform, countText,
                isBossTab && on ? Theme.GoldBorder : Theme.PanelInset,
                isBossTab && on ? Theme.Gold : Theme.TextDim, 18);

            if (on)
            {
                var underline = Ui.Panel(go.transform, "Underline");
                underline.AddComponent<Image>().color = isBossTab
                    ? Theme.Gold
                    : (tint is { } t3 ? Theme.ElementColor(t3) : Theme.InkSoft);
                Ui.Anchor((RectTransform)underline.transform, Vector2.zero, new Vector2(1, 0),
                    Vector2.zero, new Vector2(0, 4));
            }
            if (hasBounty)
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

        // ================= 左:怪物网格 =================

        private bool Matches(EnemyDef def, int filter) => filter switch
        {
            FilterAll => true,
            FilterBoss => IsBoss(def),
            _ => _band.TryGetValue(def.Id, out int b) && b == filter,
        };

        /// <summary>排序:待领赏 › 已录 › 未遭遇(稿 .sorthint)。与卡组的「可升级优先、
        /// 未拥有沉底」同一套排序观 —— 玩家进图鉴多半是来领赏的。</summary>
        private List<EnemyDef> Visible()
        {
            var list = new List<EnemyDef>();
            foreach (var def in _all)
                if (Matches(def, _filter))
                    list.Add(def);
            list.Sort((a, b) =>
            {
                bool claimA = CanClaim(a), claimB = CanClaim(b);
                if (claimA != claimB) return claimA ? -1 : 1;
                bool knownA = Known(a), knownB = Known(b);
                if (knownA != knownB) return knownA ? -1 : 1;
                return _order[a.Id].CompareTo(_order[b.Id]); // List.Sort 不稳定,兜一手配置顺序
            });
            return list;
        }

        private void BuildGrid(Transform parent)
        {
            var wrap = Ui.ScrollList(parent, "Grid", GridGapY, out var content);
            _grid = wrap.GetComponent<ScrollRect>();
            var rect = (RectTransform)wrap.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(-(SideW + MainGap), 0);

            var list = Visible();
            if (list.Count == 0)
            {
                var empty = Ui.ThemedLabel(content, Strings.T("bestiary.empty"), 22, Theme.LockGray);
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
            // 高度还是 0 时写进去会被当场夹回顶部(卡组页同一处注释)。
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
            _grid.verticalNormalizedPosition = _gridScroll;
        }

        private void BuildCell(Transform parent, EnemyDef def)
        {
            bool known = Known(def);
            bool claimable = CanClaim(def);

            var cell = Ui.VStack(parent, $"Cell_{def.Id}", 6);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var cellElement = cell.AddComponent<LayoutElement>();
            cellElement.preferredWidth = CellWidth;
            cellElement.flexibleWidth = 0;

            var block = MobBlock(cell.transform, def, BlockSize, 0, known, claimable,
                selected: _selected == def.Id, badges: true, face: out var face);
            // Button 挂外层、targetGraphic 指填充层:OutlinedPanel 对 face 设了
            // raycastTarget = false(点击靠外层收),而按下去的染色染在 3px 的描边上看不见
            var button = block.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => Select(def.Id));

            Ui.ThemedLabel(cell.transform,
                known ? def.Id : Strings.T("bestiary.card.unmet"), 21,
                known ? Theme.TextMain : Theme.LockGray);
        }

        /// <summary>立绘块:属性浅底 + 属性描边,里面是分层字怪(缺资产则回落到大字)。
        /// 角标:左上属性、左下远程、右下锁;右上那个槽位待领赏的角旗优先,四相标让到右下。
        /// 详情栏的大立绘走同一份 —— 稿上两处本来就是同一块,只是尺寸不同。</summary>
        private Image MobBlock(Transform parent, EnemyDef def, float size, int phase,
            bool known, bool claimable, bool selected, bool badges, out Image face)
        {
            var element = PhaseElement(def, phase);
            // 描边一条线上要表达三件事,优先级:选中 > 待领赏 > 属性
            Color border = selected ? Theme.Ink
                : (known ? (claimable ? Theme.Cinnabar : Theme.ElementColor(element)) : Theme.LockedBg);
            var block = Ui.OutlinedPanel(parent, "Blk",
                known ? Theme.ElementSoft(element) : Theme.LockedPaper, border,
                14, selected ? 5 : 3, out face);
            Ui.Sized(block.gameObject, size, size);

            bool placed = false;
            if (known)
            {
                string prefix = MobAssets.PrefixFor(def, phase);
                if (MobAssets.Layer(prefix, "body") != null)
                {
                    var portrait = new GameObject($"Mob_{def.Id}", typeof(RectTransform));
                    portrait.transform.SetParent(block.transform, false);
                    var mob = portrait.AddComponent<MobView>();
                    mob.Init(prefix, size * 0.82f);
                    // 图鉴展示机制特征(缺笔的残笔、通假的面具、生僻的墨雾、焦痕的火芯):
                    // 战斗里这一层由实际状态驱动,这里只是静态露出
                    mob.SetStateAmount(0.55f);
                    placed = true;
                }
            }
            if (!placed)
            {
                var glyph = Ui.ThemedLabel(block.transform,
                    known ? EnemyInfo.FaceChar(def, phase) : Strings.T("char.element.unknown"),
                    Mathf.RoundToInt(size * 0.5f),
                    known ? Theme.GlyphColor(element) : Theme.LockedGlyph, Theme.TitleFont);
                Ui.Stretch(glyph.rectTransform);
            }

            if (!badges) return block;

            if (known)
                Corner(block.transform, "El", CharInfo.ElementName(element), 19,
                    Theme.ElementColor(element), Color.white, new Vector2(0, 1), new Vector2(8, -8));
            if (known && def.Range == AttackRange.Ranged)
                Corner(block.transform, "Range", Strings.T("enemy.range.ranged.name"), 15,
                    Theme.Scrim, Color.white, Vector2.zero, new Vector2(8, 8));
            if (!known)
                LockIcon(block.transform);
            // 右上是一个槽位:待领赏的角旗优先占,四相标让到右下 —— 两个都钉在右上会叠住
            if (claimable)
                Corner(block.transform, "Flag",
                    Strings.T("bestiary.card.bounty_chip", ("bounty", BountyOf(def))), 16,
                    Theme.Cinnabar, Color.white, Vector2.one, new Vector2(-8, -8));
            if (known && IsBoss(def))
                Corner(block.transform, "Boss", Strings.T("bestiary.card.boss_badge"), 16,
                    Theme.Ink, Theme.Gold,
                    claimable ? new Vector2(1, 0) : Vector2.one,
                    claimable ? new Vector2(-8, 8) : new Vector2(-8, -8));
            return block;
        }

        /// <summary>角标:定在块的某个角上。<paramref name="anchor"/> 同时当 pivot,
        /// 于是 <paramref name="offset"/> 的方向恒是「离开那个角」—— 调用方按各自的角写好符号。</summary>
        private static void Corner(Transform parent, string name, string text, int fontSize,
            Color bg, Color fg, Vector2 anchor, Vector2 offset)
        {
            var go = Ui.CardPanel(parent, name, bg, 8).gameObject;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = new Vector2(Ui.ChipWidth(text, fontSize, 12), fontSize + 12);
            // pivot 与 anchor 同点时,anchoredPosition 的正方向恒是「离开该角」——
            // 左上角要往右下推,故 y 取负;右下角要往左上推,故 x 取负。调用方给的
            // offset 已按各自的角写好符号,这里原样用。
            rect.anchoredPosition = offset;
            var label = Ui.ThemedLabel(go.transform, text, fontSize, fg, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
        }

        /// <summary>未遭遇的锁标(稿右下角)。用状态图标里的 seal(封印)那把锁,
        /// 资产缺失时 <see cref="Icons"/> 会回落成汉字,不会开天窗。</summary>
        private static void LockIcon(Transform parent)
        {
            var badge = Ui.CardPanel(parent, "Lock", Theme.LockedBg, 20).gameObject;
            var rect = (RectTransform)badge.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.sizeDelta = new Vector2(34, 34);
            rect.anchoredPosition = new Vector2(-8, 8);

            var sprite = Icons.Get("seal");
            if (sprite != null)
            {
                var icon = Ui.Panel(badge.transform, "Icon");
                var image = icon.AddComponent<Image>();
                image.sprite = sprite;
                image.color = Theme.LockGray;
                image.preserveAspect = true;
                image.raycastTarget = false;
                Ui.Anchor((RectTransform)icon.transform, Vector2.zero, Vector2.one,
                    new Vector2(7, 7), new Vector2(-7, -7));
            }
            else
            {
                var glyph = Ui.ThemedLabel(badge.transform, Icons.Fallback("seal"), 18, Theme.LockGray);
                Ui.Stretch(glyph.rectTransform);
            }
        }

        private void Select(string enemyId)
        {
            _selected = enemyId;
            _phase = 0;
            Rebuild();
        }

        // ================= 右栏 =================

        private EnemyDef Selected()
        {
            if (_selected == null) return null;
            foreach (var def in _all)
                if (def.Id == _selected)
                    return def;
            return null;
        }

        private void BuildSide(Transform parent)
        {
            var selected = Selected();
            bool known = selected != null && Known(selected);

            var side = Ui.OutlinedPanel(parent, "Side", Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            Ui.Anchor((RectTransform)side.transform, new Vector2(1, 0), Vector2.one,
                new Vector2(-SideW, 0), Vector2.zero);

            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            string title = selected == null
                ? Strings.T("bestiary.side.title_overview")
                : (known ? Strings.T("bestiary.side.title_detail")
                         : Strings.T("bestiary.side.title_detail_locked"));
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            var headSpring = Ui.Panel(head.transform, "Spring");
            headSpring.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (selected != null)
                Ui.RoundButton(head.transform, Strings.T("bestiary.side.close"),
                    () => { _selected = null; Rebuild(); }, new Color(0, 0, 0, 0), Theme.TextDim, 22,
                    new Vector2(44, 44));

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            var body = Ui.ScrollList(side.transform, "Body", 0, out var content);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SideFootH), new Vector2(-SidePad, -SideHeadH - 2));
            if (selected == null) BuildOverview(content);
            else BuildDetail(content, selected, known);

            BuildSideFoot(side.transform, selected, known);
        }

        private void BuildSideFoot(Transform parent, EnemyDef selected, bool known)
        {
            var foot = Ui.Row(parent, "Foot", 13);
            var footLayout = foot.GetComponent<HorizontalLayoutGroup>();
            footLayout.padding = new RectOffset(17, 17, 17, 17);
            footLayout.childForceExpandWidth = true;
            Ui.Anchor((RectTransform)foot.transform, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, SideFootH));

            if (selected == null)
            {
                int pending = 0;
                foreach (var def in _all)
                    if (CanClaim(def))
                        pending++;
                var all = Ui.PillButton(foot.transform,
                    pending > 0 ? Strings.T("bestiary.button.claim_all", ("count", pending))
                                : Strings.T("bestiary.button.claim_all_none"),
                    ClaimAll,
                    pending > 0 ? Theme.Cinnabar : Theme.PanelInset,
                    pending > 0 ? Color.white : Theme.LockGray, 24, new Vector2(0, 71));
                all.GetComponent<LayoutElement>().flexibleWidth = 1;
                all.interactable = pending > 0;
                return;
            }

            if (!known)
            {
                var locked = Ui.PillButton(foot.transform, Strings.T("bestiary.button.locked"),
                    () => { }, Theme.PanelInset, Theme.LockGray, 22, new Vector2(0, 71));
                locked.GetComponent<LayoutElement>().flexibleWidth = 1;
                locked.interactable = false;
                return;
            }

            bool claimable = CanClaim(selected);
            var button = Ui.PillButton(foot.transform,
                claimable
                    ? Strings.T("bestiary.button.claim", ("bounty", BountyOf(selected)))
                    : Strings.T("bestiary.button.claimed", ("bounty", BountyOf(selected))),
                () => Claim(selected),
                claimable ? Theme.Cinnabar : Theme.PanelInset,
                claimable ? Color.white : Theme.LockGray, 24, new Vector2(0, 71));
            button.GetComponent<LayoutElement>().flexibleWidth = 1;
            button.interactable = claimable;
        }

        // ---- 右栏 · 没选中:收录总览 ----

        private void BuildOverview(Transform parent)
        {
            var progress = Section(parent, Strings.T("bestiary.side.section.progress"));
            for (int b = 0; b < BandCount; b++)
                BuildProgressBar(progress, _endless.Bands[b].Name,
                    b < BandTint.Length ? Theme.ElementColor(BandTint[b]) : Theme.InkSoft, b);
            BuildProgressBar(progress, Strings.T("bestiary.filter.boss"), Theme.Gold, FilterBoss);

            var bounty = Section(parent, Strings.T("bestiary.side.section.bounty"));
            Tip(bounty, Strings.T("bestiary.side.bounty_tip",
                ("minion", BestiaryRules.MinionBounty), ("boss", BestiaryRules.BossBounty)));

            var howto = Section(parent, Strings.T("bestiary.side.section.howto"));
            Tip(howto, Strings.T("bestiary.side.howto_tip"));
        }

        private void BuildProgressBar(Transform parent, string name, Color color, int filter)
        {
            int known = 0, total = 0;
            foreach (var def in _all)
            {
                if (!Matches(def, filter)) continue;
                total++;
                if (Known(def)) known++;
            }
            if (total == 0) return;

            var row = Ui.Row(parent, $"Progress_{filter}", 15);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 34, flexWidth: 1);
            var label = Ui.ThemedLabel(row.transform, name, 23, color, Theme.TitleFont);
            Ui.Sized(label.gameObject, 70, 34);
            var bar = Ui.Bar(row.transform, (float)known / total, color, new Vector2(0, 15));
            bar.GetComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(row.transform, $"{known}/{total}", 20,
                known == total ? Theme.DoneGreen : Theme.TextDim);
        }

        // ---- 右栏 · 选中:条目详情 ----

        private void BuildDetail(Transform parent, EnemyDef def, bool known)
        {
            BuildHero(parent, def, known);
            if (known && IsBoss(def)) BuildPhases(parent, def);
            BuildStats(parent, def, known);
            if (known) BuildAbility(parent, def);
            if (known) BuildWuxing(parent, PhaseElement(def, _phase));
        }

        private void BuildHero(Transform parent, EnemyDef def, bool known)
        {
            var row = Ui.Row(parent, "Hero", 21);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            float metaWidth = SideW - SidePad * 2 - HeroBlock - 21;
            string where = WhereText(def, known);
            float whereHeight = Ui.WrappedTextHeight(where, 19, metaWidth);
            // 名字 40 + chip 区两行 62 + 出没文字 + 底部余量:chip 区按**两行**留高,
            // 一行时多出来的空白由 VStack 吃掉;按一行留则「远程 + 锁人 + 护甲」那几只会被压扁
            Ui.Sized(row, 0, Mathf.Max(HeroBlock, 40 + 62 + whereHeight + 24), flexWidth: 1);

            MobBlock(row.transform, def, HeroBlock, _phase, known,
                claimable: false, selected: false, badges: false, face: out _);

            var meta = Ui.VStack(row.transform, "Meta", 12);
            meta.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            meta.AddComponent<LayoutElement>().flexibleWidth = 1;

            var name = Ui.ThemedLabel(meta.transform,
                known ? def.Id : Strings.T("bestiary.side.unmet_entry"), 33,
                known ? Theme.TextMain : Theme.LockGray, Theme.TitleFont);
            name.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(name.gameObject, metaWidth, 40);

            if (known)
            {
                var chips = new List<Ui.ChipSpec>();
                var element = PhaseElement(def, _phase);
                chips.Add(new Ui.ChipSpec(
                    Strings.T("collection.side.element_chip", ("element", CharInfo.ElementName(element))),
                    Theme.ElementColor(element), Color.white));
                if (def.Range == AttackRange.Ranged)
                    chips.Add(new Ui.ChipSpec(Strings.T("enemy.range.ranged.name"),
                        Theme.InkSoft, Color.white, "ranged"));
                if (def.Focus == AttackFocus.Player)
                    chips.Add(new Ui.ChipSpec(Strings.T("enemy.focus.player.name"),
                        Theme.InkSoft, Color.white, "focus"));
                int armor = PhaseDefense(def, _phase);
                if (armor > 0)
                    chips.Add(new Ui.ChipSpec(armor.ToString(), Theme.InkSoft, Color.white, "defense"));
                Ui.ChipFlow(meta.transform, "Chips", chips, metaWidth, 16, 2);
            }

            var whereLabel = Ui.ThemedLabel(meta.transform, where, 19, Theme.LockGray);
            whereLabel.alignment = TextAnchor.UpperLeft;
            whereLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(whereLabel.gameObject, metaWidth, whereHeight);
        }

        /// <summary>出没:哪一段、从第几层起。Boss 只在段末出,且自该段起进入 Boss 池轮替。</summary>
        private string WhereText(EnemyDef def, bool known)
        {
            if (!_band.TryGetValue(def.Id, out int band)) return "";
            string name = _endless.Bands[band].Name;
            if (IsBoss(def))
                return Strings.T("bestiary.side.where_boss", ("band", name)) + "\n"
                    + Strings.T("bestiary.side.where_note_boss");
            // 两支各写各的 Strings.T(字面量 key):StringsTableTests 只认紧跟在 T( 后面的
            // 字符串字面量,key 从三元表达式传进去会被判成孤儿(EnemyInfo 那里有同一条注释)
            string note = known
                ? Strings.T("bestiary.side.where_note_minion")
                : Strings.T("bestiary.side.where_note_locked");
            return Strings.T("bestiary.side.where_from", ("band", name), ("depth", _depth[def.Id]))
                + "\n" + note;
        }

        /// <summary>四相:点相看该阶段的属性、血攻甲与克制。四个阶段在配置里是完全公开的
        /// 静态数据,没有理由藏着 —— 玩家要按「下一段是什么属性」决定现在留哪张克制的字。</summary>
        private void BuildPhases(Transform parent, EnemyDef def)
        {
            var section = Section(parent, Strings.T("bestiary.side.section.phases"),
                Strings.T("bestiary.side.phases_hint"));
            var row = Ui.Row(section, "Phases", 8);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 84, flexWidth: 1);

            for (int i = 0; i < def.Phases.Count; i++)
            {
                int index = i; // 闭包捕获:直接用 i 会让所有按钮都指向末位
                var phase = def.Phases[i];
                bool on = i == _phase;
                var box = Ui.OutlinedPanel(row.transform, $"Phase{i}",
                    on ? Theme.ElementSoft(phase.Element) : Theme.CardWhite,
                    on ? Theme.ElementColor(phase.Element) : Theme.PanelBorder, 14, on ? 3 : 2,
                    out var face);
                Ui.Sized(box.gameObject, 0, 84, flexWidth: 1);
                var button = box.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                button.onClick.AddListener(() => { _phase = index; Rebuild(); });

                var stack = Ui.VStack(box.transform, "Stack", 2);
                Ui.Stretch((RectTransform)stack.transform);
                Ui.ThemedLabel(stack.transform, phase.Char, 33,
                    Theme.GlyphColor(phase.Element), Theme.TitleFont);
                Ui.ThemedLabel(stack.transform, CharInfo.ElementName(phase.Element), 15, Theme.LockGray);
            }
        }

        /// <summary>数值:按该怪**首现层段**的深度缩放换算,底下标出基准值。
        /// 缩放走 <see cref="CampaignConfig.Scale"/> 而不是自己乘 —— 护甲是半速缩放,
        /// 自己乘一遍必定和战斗里对不上(Campaign.ScaledDefense 的注释)。</summary>
        private void BuildStats(Transform parent, EnemyDef def, bool known)
        {
            float scale = ScaleOf(def);
            string note = known && _band.TryGetValue(def.Id, out int band)
                ? Strings.T("bestiary.side.scale_note",
                    ("band", _endless.Bands[band].Name), ("scale", scale.ToString("0.0")))
                : Strings.T("bestiary.side.scale_unknown");
            var section = Section(parent, Strings.T("bestiary.side.section.stats"), note);

            var row = Ui.Row(section, "Stats", 11);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 104, flexWidth: 1);

            var scaled = CampaignConfig.Scale(def, scale);
            int hp = IsBoss(def) ? scaled.Phases[_phase].MaxHp : scaled.MaxHp;
            int attack = IsBoss(def) ? scaled.Phases[_phase].Attack : scaled.Attack;
            int armor = IsBoss(def) ? scaled.Phases[_phase].Defense : scaled.Defense;
            int baseHp = IsBoss(def) ? def.Phases[_phase].MaxHp : def.MaxHp;
            int baseAttack = IsBoss(def) ? def.Phases[_phase].Attack : def.Attack;

            // 同上:key 一律写成字面量,不走三元表达式
            string armorNote = armor > 0
                ? Strings.T("bestiary.side.def_note")
                : Strings.T("bestiary.side.def_none");
            string unknown = Strings.T("char.element.unknown");
            StatBox(row.transform, Strings.T("map.hero.stat.hp"),
                known ? hp.ToString() : unknown, Theme.CinnabarDark,
                known ? Strings.T("bestiary.side.stat_base", ("base", baseHp)) : "");
            StatBox(row.transform, Strings.T("bestiary.side.stat.attack"),
                known ? attack.ToString() : unknown, Theme.ShopNav,
                known ? Strings.T("bestiary.side.stat_base", ("base", baseAttack)) : "");
            StatBox(row.transform, Strings.T("bestiary.side.stat.defense"),
                known ? armor.ToString() : unknown,
                armor > 0 ? Theme.InkSoft : Theme.LockGray,
                known ? armorNote : "");
        }

        private static void StatBox(Transform parent, string key, string value, Color valueColor, string note)
        {
            var box = Ui.OutlinedPanel(parent, "Stat", Theme.CardWhite, Theme.PanelBorder, 14, 2);
            Ui.Sized(box.gameObject, 0, 104, flexWidth: 1);
            var stack = Ui.VStack(box.transform, "Stack", 2);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, key, 17, Theme.LockGray);
            Ui.ThemedLabel(stack.transform, value, 36, valueColor, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform, note, 16, Theme.LockGray);
        }

        /// <summary>能力:小怪走天生能力,Boss 走当前相的蓄力技能。两边的文案都取自
        /// <c>enemy.ability.*</c> / <c>enemy.skill.*</c>,与战斗详情同一份真相源。</summary>
        private void BuildAbility(Transform parent, EnemyDef def)
        {
            var section = Section(parent, Strings.T("bestiary.side.section.ability"));
            string name, text, iconKey = null;
            if (IsBoss(def))
            {
                var phase = def.Phases[_phase];
                name = EnemyInfo.BossSkillName(phase.Skill);
                // 大招说明后面必须跟蓄力节拍:光说「放大招」答不了「隔几回合放一次」,
                // 而那是玩家排 Boss 战节奏时唯一要算的东西(旧的图鉴弹窗一直印着这句)
                text = EnemyInfo.BossSkillText(phase.Skill) + "\n" + EnemyInfo.ChargeRuleText();
            }
            else
            {
                var info = StatusText.OfAbility(def.Ability);
                name = info.Name;
                text = info.Desc;
                iconKey = info.IconKey;
            }

            if (string.IsNullOrEmpty(name))
            {
                // Boss 说的是「这一相没大招」(复用战斗详情同一句),小怪说的是「这只怪没机制」——
                // 两码事,不能共用一句话
                string plain = IsBoss(def)
                    ? Strings.T("enemy.phase.no_ultimate").TrimStart('\n') + "\n"
                        + Strings.T("bestiary.side.no_ability_boss")
                    : Strings.T("bestiary.side.no_ability");
                Tip(section, plain);
                return;
            }

            float width = SideW - SidePad * 2;
            var box = Ui.OutlinedPanel(section, "Ability", Theme.CardWhite, Theme.PanelBorder, 14, 2);
            Ui.Sized(box.gameObject, 0,
                Mathf.Max(80, Ui.WrappedTextHeight(text, 21, width - 40) + 66), flexWidth: 1);

            // 左边一条属性色的粗边(稿 .ability 的 border-left)
            var edge = Ui.Panel(box.transform, "Edge");
            edge.AddComponent<Image>().color = Theme.ElementColor(PhaseElement(def, _phase));
            Ui.Anchor((RectTransform)edge.transform, Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(6, 0));

            var stack = Ui.VStack(box.transform, "Stack", 6);
            var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
            stackLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.Anchor((RectTransform)stack.transform, Vector2.zero, Vector2.one,
                new Vector2(19, 13), new Vector2(-13, -13));

            var head = Ui.Row(stack.transform, "Head", 8);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, 0, 34, flexWidth: 1);
            Ui.Chip(head.transform, name, Theme.Cinnabar, Color.white, 24, iconKey: iconKey);

            var label = Ui.ThemedLabel(stack.transform, text, 21, Theme.TextMain);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, width - 40, Ui.WrappedTextHeight(text, 21, width - 40));
        }

        /// <summary>克制:两格写清「用什么打它」与「它克谁」—— 图鉴的实际用处是
        /// 下次遇到它该出什么字,光有血攻答不上来(稿 note-bestiary)。</summary>
        private void BuildWuxing(Transform parent, Element element)
        {
            var section = Section(parent, Strings.T("bestiary.side.section.wuxing"));
            var counter = WuxingResolver.Counter(element); // 打它 ×1.5 的那一系
            var victim = WuxingResolver.Victim(element);   // 它克谁,那一系出手 ×0.5
            if (counter == null && victim == null)
            {
                Tip(section, Strings.T("bestiary.side.neutral"));
                return;
            }

            var row = Ui.Row(section, "Wuxing", 11);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 84, flexWidth: 1);
            if (counter is { } c)
                WuxingBox(row.transform, c, Theme.AdGreenBg, Theme.UpgradeText,
                    Strings.T("bestiary.side.ke", ("element", CharInfo.ElementName(c))));
            if (victim is { } v)
                WuxingBox(row.transform, v, Theme.WarnBg, Theme.WarnText,
                    Strings.T("bestiary.side.bei", ("element", CharInfo.ElementName(v))));
        }

        private static void WuxingBox(Transform parent, Element element, Color bg, Color fg, string text)
        {
            var box = Ui.CardPanel(parent, "Wx", bg, 14);
            Ui.Sized(box.gameObject, 0, 84, flexWidth: 1);
            var row = Ui.Row(box.transform, "Row", 13);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 12, 0, 0);
            Ui.Stretch((RectTransform)row.transform);

            var dot = Ui.CardPanel(row.transform, "Dot", Theme.ElementColor(element), 10);
            Ui.Sized(dot.gameObject, 36, 36);
            Ui.ThemedLabel(dot.transform, CharInfo.ElementName(element), 21, Color.white, Theme.TitleFont);

            var label = Ui.ThemedLabel(row.transform, text, 19, fg);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, 0, 84, flexWidth: 1);
        }

        // ================= 小工具 =================

        /// <summary>右栏里的一个小节:标题 +(可选)右侧小注 + 一条横线,下面是竖排容器(返回值)。</summary>
        private static Transform Section(Transform parent, string title, string note = null)
        {
            var head = Ui.Row(parent, "SectionHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            if (!string.IsNullOrEmpty(note))
            {
                var noteLabel = Ui.ThemedLabel(head.transform, note, 18, Theme.LockGray);
                Ui.Sized(noteLabel.gameObject, Ui.ChipWidth(note, 18), 44);
            }
            var rule = Ui.Panel(head.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, 0, 2, flexWidth: 1);

            var stack = Ui.VStack(parent, "Section", 11);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            // ⚠ 只给弹性宽,**不要**设 preferredHeight —— LayoutElement 的 layoutPriority 压过
            // 布局组自己算出来的高,写个 0 会把整节静默压没(卡组页同一处注释)
            Ui.Sized(stack, flexWidth: 1);
            return stack.transform;
        }

        private static void Tip(Transform parent, string text)
        {
            float width = SideW - SidePad * 2;
            var label = Ui.ThemedLabel(parent, text, 20, Theme.TextDim);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, 0, Ui.WrappedTextHeight(text, 20, width), flexWidth: 1);
        }

        private bool Known(EnemyDef def) => BestiaryRules.IsUnlocked(_meta, def.Id);
        private bool Claimed(EnemyDef def) => BestiaryRules.IsClaimed(_meta, def.Id);
        private bool CanClaim(EnemyDef def) => Known(def) && !Claimed(def);
        private static bool IsBoss(EnemyDef def) => def.Phases.Count > 0;

        private static int BountyOf(EnemyDef def) =>
            IsBoss(def) ? BestiaryRules.BossBounty : BestiaryRules.MinionBounty;

        private static Element PhaseElement(EnemyDef def, int phase) =>
            def.Phases.Count > 0 ? def.Phases[Mathf.Clamp(phase, 0, def.Phases.Count - 1)].Element : def.Element;

        private static int PhaseDefense(EnemyDef def, int phase) =>
            def.Phases.Count > 0 ? def.Phases[Mathf.Clamp(phase, 0, def.Phases.Count - 1)].Defense : def.Defense;

        /// <summary>该怪首次能被遇到的那一层的缩放倍率。图鉴列的是「你第一次撞见它时它多硬」——
        /// 更深的层它当然更硬,但那是深度的事,不是这只怪的身份。</summary>
        private float ScaleOf(EnemyDef def) =>
            _depth.TryGetValue(def.Id, out int depth) ? _endless.ScaleFor(depth) : 1f;

        // ================= 领赏 =================

        private void Claim(EnemyDef def)
        {
            if (BestiaryRules.TryClaim(_meta, def) > 0) _save();
            Rebuild();
        }

        private void ClaimAll()
        {
            bool any = false;
            foreach (var def in _all)
                if (BestiaryRules.TryClaim(_meta, def) > 0)
                    any = true;
            if (any) _save();
            Rebuild();
        }
    }
}
