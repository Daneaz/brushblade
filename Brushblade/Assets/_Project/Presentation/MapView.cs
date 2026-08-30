using System;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>主界面(20.2 无尽外层)。版式基线 = <c>docs/design/ui/scenes/Home.dc.html</c>:
    /// 顶栏 / 三栏主体(角色 · 书塔 · 宝箱)/ 底部四页签导航。
    ///
    /// ⚠ 尺寸常量都是**逻辑单位**,由稿子的 pt 换算而来(CanvasScaler 1600×900 按高匹配,
    /// iPhone 16 Pro Max 上 1pt = 2.093 逻辑单位;见 Device.dc.html)。改稿子就同步改这里,
    /// 别只改一边 —— 稿与实现两边都可能过时,但任何一边动了都要留痕(scenes/README.md)。
    ///
    /// 根节点已在 SafeArea 之内(GameRoot.NewView),所以这里的 0/1 边界就是稿上的 .safe 框。</summary>
    public sealed class MapView : MonoBehaviour
    {
        // ---- 稿上的骨架尺寸(pt → 逻辑单位) ----
        private const float TopH = 80f;        // 顶栏 38pt
        private const float NavH = 92f;        // 底部导航 44pt
        private const float Gap = 21f;         // 栏间距 10pt
        // 2026-08-28 反馈两轮:先把两侧收窄让宽给书塔(两侧都是「看一眼就够」的信息,
        // 书塔才是每次进主界面要用的),再把宝箱栏还回去一截 —— 408 时格内净宽只够
        // 两颗 mini 按钮挤在一起,「18墨」顶出按钮。净结果书塔仍比稿宽:
        // 16 Pro Max 上 736u → 785u。
        private const float HeroW = 392f;      // 角色栏 187pt(稿 214pt)
        private const float ChestW = 486f;     // 宝箱栏 232pt(稿原 228pt)
        private const float SideInset = 123f;  // 稿上 .safe 左右各 59pt
        private const float BottomInset = 44f; // 稿上 .safe 下 21pt(Home Indicator)
        // 箱位里的立绘 40pt(2026-08-30 接立绘:原先是 33×25pt 的色块,方图塞进 4:3 会压扁)
        private const float ChestArtSize = 84f;
        private const float ResultArtSize = 201f;  // 开箱结果面板左栏 96pt

        private RecipeGraph _graph;
        private CampaignConfig _campaign;
        private MetaState _meta;
        private ITimeSource _time;
        private Action _onStartTower;
        private Action _save;
        private Action _onOpenCollection;
        private Action _onOpenShop;
        private Action _onOpenBestiary;
        private Action _onOpenPerks;
        private string _message;
        private System.Collections.Generic.List<EnemyDef> _enemies; // 图鉴全集(页签计数用),口径与图鉴页同源

        // 计时中箱位的倒计时/加速价标签引用:Tick 只改文本不重建,避免按钮每秒被销毁点不中
        private readonly System.Collections.Generic.List<(int index, Text countdown, Text skipCost)> _countdowns = new();
        private GameObject _resultPanel; // 开箱结果面板;打开期间禁止整页重建
        private GameObject _modal;       // 当前告知弹窗(同屏仅一个)

        public void Init(RecipeGraph graph, CampaignConfig campaign, MetaState meta, ITimeSource time,
            Action onStartTower, Action save, string message, Action onOpenCollection, Action onOpenShop,
            Action onOpenBestiary, Action onOpenPerks)
        {
            _graph = graph;
            _onOpenShop = onOpenShop;
            _campaign = campaign;
            _meta = meta;
            _time = time;
            _onStartTower = onStartTower;
            _save = save;
            _onOpenCollection = onOpenCollection;
            _onOpenBestiary = onOpenBestiary;
            _onOpenPerks = onOpenPerks;
            _message = message ?? "";
            _enemies = BestiaryView.CollectEnemies(campaign);
            Rebuild();
            InvokeRepeating(nameof(Tick), 1f, 1f); // 倒计时刷新
        }

        private void Tick()
        {
            // 计时中:只更新倒计时与加速价文本;跃迁到就绪才整页重建(结果面板打开时押后到关闭)
            bool becameReady = false;
            foreach (var (index, countdown, skipCost) in _countdowns)
            {
                if (index >= _meta.Chests.Count) continue;
                var chest = _meta.Chests[index];
                if (!chest.Timing) continue;
                if (ChestRules.IsReady(chest, _time))
                {
                    becameReady = true;
                    continue;
                }
                long remaining = ChestRules.RemainingSeconds(chest, _time);
                if (countdown != null) countdown.text = Format(remaining);
                if (skipCost != null) skipCost.text = Strings.T("map.chest.skip_cost", ("cost", ChestRules.InkCostToSkip(remaining)));
            }
            if (becameReady && _resultPanel == null)
                Rebuild();
        }

        /// <summary>宝箱卡池 = 全部可收集字(带配方的字);与 GameRoot.ChestCardPool 同逻辑。</summary>
        private System.Collections.Generic.List<string> ChestCardPool()
        {
            var pool = new System.Collections.Generic.List<string>();
            foreach (var def in _graph.All)
                if (!def.IsLeaf)
                    pool.Add(def.Id);
            return pool;
        }

        private void Rebuild()
        {
            // 暂存箱补进空出的箱位(2026-07-22):开箱腾位后 OpenChest→Rebuild 会走到这里
            if (ChestRules.DrainPendingChests(_meta, ChestCardPool(), _time) > 0)
                _save();

            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            // 稿上 .safe 的内缩;弹窗仍挂在 transform 上,铺满整屏
            var (padSide, padBottom) = MissingInset();
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frame = content.transform;

            BuildTopBar(frame);

            // 三栏主体:左右定宽、中间吃掉余量(稿上 214 / 弹性 / 228)
            var body = Ui.Row(frame, "Body", Gap);
            var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(0, NavH + Gap), new Vector2(0, -TopH));

            BuildHeroPanel(body.transform);
            BuildTowerPanel(body.transform);
            BuildChestPanel(body.transform);

            BuildNavBar(frame);
        }

        /// <summary>稿上 .safe 的内缩里,**设备安全区还没给够的那一部分**。
        ///
        /// 根节点已在 <see cref="SafeAreaFitter"/> 之内:真机横屏的 59pt 边和 21pt Home Indicator
        /// 已经让出来了,这里再叠一次就会缩两回。但编辑器与无刘海机上 <c>Screen.safeArea</c> = 整屏,
        /// 不补的话内容直接贴着屏幕边 —— 所以按差额补,两边都对得上稿。
        ///
        /// 左右取两侧的较小值:横屏左右旋转时刘海会换边,取 min 才不会随旋转跳动。</summary>
        private static (float side, float bottom) MissingInset()
        {
            float scale = Screen.height / 900f; // CanvasScaler referenceResolution 1600×900,match = 1(按高)
            if (scale <= 0f) return (SideInset, BottomInset);
            var safe = Screen.safeArea;
            float given = Mathf.Min(safe.xMin, Screen.width - safe.xMax) / scale;
            return (Mathf.Max(0f, SideInset - given), Mathf.Max(0f, BottomInset - safe.yMin / scale));
        }

        // ---- 顶栏 ----

        private void BuildTopBar(Transform parent)
        {
            var top = Ui.Row(parent, "Top", 21);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)top.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -TopH), Vector2.zero);

            Ui.ThemedLabel(top.transform, Strings.T("map.header.game_title"), 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(top.transform,
                Strings.T("map.header.subtitle", ("rank", EndlessRules.RankTitle(_meta.BestDepth)), ("depth", _meta.BestDepth)),
                23, Theme.TextDim);
            Spring(top.transform);
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            // 设置界面尚未实现(2026-08-28 拍板):先占位,点了说明去向,不留死按钮
            Ui.RoundButton(top.transform, Strings.T("map.header.settings"),
                () => ShowAlert(Strings.T("map.settings.soon_title"), Strings.T("map.settings.soon_body")),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63), 16);
        }

        // ---- 左栏:角色 ----

        private void BuildHeroPanel(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Hero", Theme.PanelPaper, Theme.PanelBorder, 21);
            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = HeroW;
            element.flexibleWidth = 0;

            var stack = Ui.VStack(panel.transform, "Stack", 19);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(20, 20, 20, 20);
            Ui.Stretch((RectTransform)stack.transform);

            int level = MetaRules.LevelProgress(_meta.CharacterXp, out int into, out int need);

            // 头像 + 名 + 等级/段位
            var avatar = Ui.Row(stack.transform, "Avatar", 19);
            avatar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.CircleGlyph(avatar.transform, Strings.T("map.hero.face"), Theme.Ink, Theme.Paper, 88);
            var names = Ui.VStack(avatar.transform, "Names", 4);
            names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(names.transform, Strings.T("map.hero.name"), 33, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(names.transform,
                Strings.T("map.hero.level_line", ("level", level), ("rank", EndlessRules.RankTitle(_meta.BestDepth))),
                21, Theme.TextDim);

            // 经验条:等级与进度同源于 MetaRules.LevelProgress,不在这里另算曲线
            var xp = Ui.Row(stack.transform, "Xp", 12);
            xp.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(xp.transform, Strings.T("map.hero.xp"), 19, Theme.TextDim);
            var xpBar = Ui.Bar(xp.transform, need > 0 ? (float)into / need : 0f, Theme.Gold, new Vector2(120, 9));
            xpBar.GetComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(xp.transform, Strings.T("map.hero.xp_value", ("into", into), ("need", need)), 19, Theme.TextDim);

            // 六项属性上屏。读的是**战斗真正吃到的那份配置**,不在这里另算一遍 ——
            // 否则会出现「屏上写着护甲 5、局内其实是 0」这种最难查的偏差。
            // ⚠ 暴击不上屏:基准恒 0 且不随等级成长,写「暴击 0%」没有信息量
            // (2026-08-12 E-b2 就地决定:改由局内 BattleView 出 chip)。
            var stats = MetaRules.BuildBattleConfig(_meta, _campaign.DropTable);
            var grid = Ui.VStack(stack.transform, "Attrs", 6);
            var gridLayout = grid.GetComponent<VerticalLayoutGroup>();
            gridLayout.childForceExpandWidth = true;
            StatRow(grid.transform,
                (Strings.T("map.hero.stat.hp"), stats.PlayerMaxHp.ToString(), Theme.Cinnabar),
                (Strings.T("map.hero.stat.attack"), stats.PlayerAttack.ToString(), Theme.TextMain));
            StatRow(grid.transform,
                (Strings.T("map.hero.stat.defense"), stats.PlayerDefense.ToString(), Theme.TextMain),
                (Strings.T("map.hero.stat.dodge"), Strings.T("map.hero.stat_percent", ("value", stats.PlayerDodge)), Theme.TextMain));
            StatRow(grid.transform,
                (Strings.T("map.hero.stat.speed"), stats.PlayerSpeed.ToString(), Theme.TextMain),
                (Strings.T("map.hero.stat.ap"), stats.ApPerTurn.ToString(), Theme.TextMain));

            Spring(stack.transform, vertical: true); // 出阵预览钉在面板底(稿上 margin-top:auto)
            BuildDeckMini(stack.transform);
        }

        private static void StatRow(Transform parent,
            (string name, string value, Color color) left, (string name, string value, Color color) right)
        {
            var row = Ui.Row(parent, "StatRow", 17);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            StatCell(row.transform, left);
            StatCell(row.transform, right);
        }

        /// <summary>一格属性:名靠左、值靠右(两条都铺满整格,靠对齐分左右)。</summary>
        private static void StatCell(Transform parent, (string name, string value, Color color) stat)
        {
            var cell = Ui.Panel(parent, "Stat");
            var element = cell.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.preferredHeight = 30;
            var name = Ui.ThemedLabel(cell.transform, stat.name, 21, Theme.TextDim, null, TextAnchor.MiddleLeft);
            Ui.Stretch(name.rectTransform);
            var value = Ui.ThemedLabel(cell.transform, stat.value, 27, stat.color, Theme.TitleFont, TextAnchor.MiddleRight);
            Ui.Stretch(value.rectTransform);
        }

        /// <summary>出阵预览:属性色小格,**全量铺开**、每排 6 个折行(2026-08-28 反馈:不折叠成「+N」)。
        /// 点不动,只是提示带了什么上塔。出阵上限 15,最多三排。</summary>
        private void BuildDeckMini(Transform parent)
        {
            const int PerRow = 6;    // 50×6 + 8×5 = 340,正好塞进角色栏 392 − 左右各 20 − 描边的净宽
            const float TileW = 50f; // 角色栏收窄后跟着缩(2026-08-28),排数与折行规则不变
            const float TileH = 62f;

            Ui.ThemedLabel(parent,
                Strings.T("map.hero.deck_title", ("count", _meta.Deck.Count), ("limit", MetaRules.DeckLimit)),
                19, Theme.LockGray, null, TextAnchor.MiddleLeft);

            var rows = Ui.VStack(parent, "DeckRows", 8);
            rows.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;

            Transform row = null;
            int shown = 0;
            foreach (string id in _meta.Deck)
            {
                if (!_graph.TryGet(id, out var def)) continue;
                if (shown % PerRow == 0)
                {
                    var rowGo = Ui.Row(rows.transform, $"DeckRow{shown / PerRow}", 8);
                    rowGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    row = rowGo.transform;
                }
                var tile = Ui.CardPanel(row, $"Dm_{def.Id}", Theme.ElementSoft(def.Element), 10);
                var element = tile.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = TileW;
                element.preferredHeight = TileH;
                var glyph = Ui.ThemedLabel(tile.transform, def.Id, 31, Theme.GlyphColor(def.Element), Theme.TitleFont);
                Ui.Stretch(glyph.rectTransform);
                shown++;
            }
        }

        // ---- 中栏:无尽书塔 ----

        private void BuildTowerPanel(Transform parent)
        {
            var endless = _campaign.Endless;
            var snapshot = _meta.EndlessV2;
            int depthNow = snapshot?.Depth ?? Mathf.Max(1, _meta.BestDepth + 1);
            int bandIndex = 0;
            for (int i = 0; i < endless.Bands.Count; i++)
                if (endless.Bands[i].FromDepth <= depthNow)
                    bandIndex = i;

            var panel = Ui.OutlinedPanel(parent, "Tower", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 内衬当前层段的巨字水印(林/渊/山/海)
            var bandName = endless.Bands[bandIndex].Name;
            var mark = Ui.Label(panel.transform, bandName.Substring(bandName.Length - 1), 500);
            mark.color = Theme.BandWatermark(bandIndex);
            mark.raycastTarget = false;
            Ui.Stretch(mark.rectTransform);

            var stack = Ui.VStack(panel.transform, "Stack", 14);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.MiddleCenter; // 整块内容在面板里居中(2026-08-28 反馈)
            layout.padding = new RectOffset(34, 34, 25, 29);
            Ui.Stretch((RectTransform)stack.transform);

            // 结算提示(2026-08-28 拍板:落在书塔面板顶部);有内容才占位
            if (!string.IsNullOrEmpty(_message))
            {
                var banner = Ui.Row(stack.transform, "Banner");
                var pill = Ui.CardPanel(banner.transform, "Pill", Theme.AdGreenBg, 20);
                var pillElement = pill.gameObject.AddComponent<LayoutElement>();
                pillElement.preferredWidth = 470;
                pillElement.preferredHeight = 42;
                var text = Ui.ThemedLabel(pill.transform, _message, 19, Theme.AdGreenText);
                Ui.Stretch(text.rectTransform);
            }

            Ui.ThemedLabel(stack.transform, Strings.T("map.tower.title"), 44, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                Strings.T("map.tower.rank_line", ("rank", EndlessRules.RankTitle(_meta.BestDepth)), ("depth", _meta.BestDepth)),
                23, Theme.TextDim);

            BuildBands(stack.transform, endless, depthNow, bandIndex);

            // 按钮上只放动作。层段与层数是**详情**,搬到按钮下面(2026-08-28 反馈:
            // 「继续 · 「字林」第 1 层」在 523u 的胶囊里排不下,字被挤成一条)
            // ⚠ 两个 key 各写成字面量传给 Strings.T:StringsTableTests 是**正则扫源码**认调用点的,
            // 写成 Strings.T(cond ? "a" : "b") 会让两条都被判成孤儿 key(已栽过一次)
            string label = snapshot == null
                ? Strings.T("map.tower.start_button")
                : Strings.T("map.tower.resume_button");
            var resumeRow = Ui.Row(stack.transform, "Resume");
            Ui.PillButton(resumeRow.transform, label, () => _onStartTower(),
                Theme.Cinnabar, Color.white, 38, new Vector2(523, 109));

            if (snapshot == null)
            {
                Ui.ThemedLabel(stack.transform, Strings.T("map.tower.hint"), 21, Theme.TextDim);
                return;
            }

            Ui.ThemedLabel(stack.transform,
                Strings.T("map.tower.resume_detail",
                    ("bandName", endless.BandFor(snapshot.Depth).Name), ("depth", snapshot.Depth)),
                25, Theme.TextMain, Theme.TitleFont);

            // 进行中:血条 + 三项本趟账目 + 断点说明
            int maxHp = MetaRules.PlayerMaxHpFor(_meta);
            var hpRow = Ui.Row(stack.transform, "RunBar");
            Ui.Bar(hpRow.transform, maxHp > 0 ? (float)snapshot.PlayerHp / maxHp : 0f,
                Theme.CinnabarDark, new Vector2(523, 10));

            var run = Ui.Row(stack.transform, "RunState", 25);
            Ui.ThemedLabel(run.transform, Strings.T("map.tower.run_hp", ("hp", snapshot.PlayerHp), ("maxHp", maxHp)), 21, Theme.TextDim);
            Ui.ThemedLabel(run.transform, Strings.T("map.tower.run_ink", ("ink", snapshot.EarnedInk)), 21, Theme.TextDim);
            int capacity = MetaRules.LibraryCapacityFor(_meta) + (snapshot.LibraryExpanded ? RunEngine.ExpandBonus : 0);
            Ui.ThemedLabel(run.transform,
                Strings.T("map.tower.run_library", ("count", snapshot.Library.Count), ("capacity", capacity)), 21, Theme.TextDim);

            Ui.ThemedLabel(stack.transform, Strings.T("map.tower.resume_hint"), 21, Theme.LockGray);
        }

        /// <summary>层段进度:已过的段填满灰蓝,当前段按层数在段内的占比填朱砂,未至的段留空。</summary>
        private static void BuildBands(Transform parent, EndlessConfig endless, int depthNow, int bandIndex)
        {
            var row = Ui.Row(parent, "Bands", 6);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;

            for (int i = 0; i < endless.Bands.Count; i++)
            {
                var band = endless.Bands[i];
                bool done = i < bandIndex;
                bool now = i == bandIndex;
                float frac = done ? 1f : 0f;
                if (now)
                {
                    // 段末 = 下一段的起层;最后一段没有下一段,拿它自己的跨度当分母
                    int spanEnd = i + 1 < endless.Bands.Count
                        ? endless.Bands[i + 1].FromDepth
                        : band.FromDepth + Mathf.Max(1, band.FromDepth);
                    frac = Mathf.Clamp01((float)(depthNow - band.FromDepth) / Mathf.Max(1, spanEnd - band.FromDepth));
                }

                var cell = Ui.VStack(row.transform, $"Band{i}", 4);
                var cellLayout = cell.GetComponent<VerticalLayoutGroup>();
                cellLayout.childForceExpandWidth = true;
                cell.AddComponent<LayoutElement>().flexibleWidth = 1;

                Ui.ThemedLabel(cell.transform, band.Name, 19,
                    now ? Theme.TextMain : done ? Theme.TextDim : Theme.LockGray,
                    now ? Theme.TitleFont : null);
                var bar = Ui.Bar(cell.transform, frac, now ? Theme.Cinnabar : Theme.LockGray, new Vector2(60, 13));
                bar.GetComponent<LayoutElement>().flexibleWidth = 1;
                Ui.ThemedLabel(cell.transform, band.FromDepth.ToString(), 17, Theme.LockGray);
            }
        }

        // ---- 右栏:宝箱(19.5) ----

        private void BuildChestPanel(Transform parent)
        {
            _countdowns.Clear(); // 旧标签随 Ui.Clear 已销毁,重建时重新登记

            var panel = Ui.OutlinedPanel(parent, "Chests", Theme.PanelPaper, Theme.PanelBorder, 21);
            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = ChestW;
            element.flexibleWidth = 0;

            var stack = Ui.VStack(panel.transform, "Stack", 15);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(17, 17, 16, 16);
            Ui.Stretch((RectTransform)stack.transform);

            var header = Ui.Row(stack.transform, "Head", 13);
            header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(header.transform,
                Strings.T("map.chest.panel_title", ("count", _meta.Chests.Count), ("limit", ChestRules.SlotLimit)),
                19, Theme.LockGray, Theme.TitleFont);
            if (_meta.PendingChests.Count > 0) // 暂存箱等腾位(2026-07-22)
                Ui.Chip(header.transform, Strings.T("map.chest.pending_chip", ("count", _meta.PendingChests.Count)),
                    Theme.Cinnabar, Color.white, 15);

            // 2×2 网格(稿上 .cgrid):两行各两格,行列都吃满剩下的空间
            var grid = Ui.VStack(stack.transform, "Grid", 13);
            var gridLayout = grid.GetComponent<VerticalLayoutGroup>();
            gridLayout.childForceExpandWidth = true;
            gridLayout.childForceExpandHeight = true;
            grid.AddComponent<LayoutElement>().flexibleHeight = 1;

            for (int r = 0; r < 2; r++)
            {
                var row = Ui.Row(grid.transform, $"Row{r}", 13);
                var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = true;
                row.AddComponent<LayoutElement>().flexibleHeight = 1;
                for (int c = 0; c < 2; c++)
                {
                    int index = r * 2 + c;
                    if (index >= ChestRules.SlotLimit) continue;
                    if (index >= _meta.Chests.Count) DrawEmptySlot(row.transform);
                    else DrawChest(row.transform, index);
                }
            }
        }

        private void DrawChest(Transform parent, int index)
        {
            var chest = _meta.Chests[index];
            bool ready = chest.Timing && ChestRules.IsReady(chest, _time);

            var card = Ui.OutlinedPanel(parent, $"Chest{index}", Theme.CardWhite, Theme.PanelBorder, 17);
            var cardElement = card.gameObject.AddComponent<LayoutElement>();
            cardElement.flexibleWidth = 1;
            cardElement.flexibleHeight = 1;

            var stack = Ui.VStack(card.transform, "Stack", 6);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(11, 11, 12, 12);
            Ui.Stretch((RectTransform)stack.transform);

            // 箱型立绘(Chests.dc.html):七只箱是七种材质,不是七种颜色
            ChestArt(stack.transform, chest.Tier,
                ready ? ChestView.State.Ready : chest.Timing ? ChestView.State.Timing : ChestView.State.Idle,
                ChestArtSize);

            Ui.ThemedLabel(stack.transform, ChestRules.TierName(chest.Tier), 21, Theme.TextMain, Theme.TitleFont);

            if (!chest.Timing)
            {
                // 未开始:先亮出这一档要等多久,再给排队按钮(同时只能开一个)
                Ui.ThemedLabel(stack.transform, Format(ChestRules.DurationSeconds[(int)chest.Tier - 1]), 23, Theme.LockGray);
                var actions = Ui.Row(stack.transform, "Acts", 7);
                actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                var start = Ui.RoundButton(actions.transform, Strings.T("map.chest.start_button"),
                    () => Do(() => ChestRules.TryStartOpening(_meta, index, _time)),
                    Theme.InkSoft, Color.white, 19, new Vector2(150, 46), 14);
                start.interactable = !AnyChestTiming();
            }
            else if (ready)
            {
                Ui.ThemedLabel(stack.transform, Strings.T("map.chest.ready"), 23, Theme.UpgradeText, Theme.TitleFont);
                var actions = Ui.Row(stack.transform, "Acts", 7);
                actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                Ui.RoundButton(actions.transform, Strings.T("map.chest.open_button"), () => OpenChest(index),
                    Theme.Gold, Theme.GoldText, 22, new Vector2(150, 50), 14);
            }
            else
            {
                long remaining = ChestRules.RemainingSeconds(chest, _time);
                var countdown = Ui.ThemedLabel(stack.transform, Format(remaining), 23, Theme.TextDim);
                var actions = Ui.Row(stack.transform, "Acts", 7);
                actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                if (!chest.AdUsed)
                {
                    long cut = ChestRules.AdReductionSeconds[(int)chest.Tier - 1];
                    Ui.AdBadge(actions.transform, $"-{cut / 60}m", // 原型:直接生效,广告 SDK 后接
                        () => Do(() => ChestRules.TryApplyAdBoost(chest)), new Vector2(72, 46));
                }
                var skip = Ui.RoundButton(actions.transform, Strings.T("map.chest.skip_cost", ("cost", ChestRules.InkCostToSkip(remaining))),
                    () => Do(() => ChestRules.TrySkipWithInk(_meta, index, _time), Strings.T("map.chest.skip_fail_title"),
                        Strings.T("map.chest.skip_fail_body",
                            ("needed", ChestRules.InkCostToSkip(ChestRules.RemainingSeconds(chest, _time))),
                            ("ink", _meta.Ink))),
                    Theme.Gold, Theme.GoldText, 19, new Vector2(72, 46), 14);
                _countdowns.Add((index, countdown, skip.GetComponentInChildren<Text>()));
            }
        }

        /// <summary>宝箱立绘 + 三态。素材缺失时回落成档位色块 + 首字(与 Icons.cs 的双轨同理):
        /// 信息一点不丢,换 PNG 也不需要动这里一行。</summary>
        private static void ChestArt(Transform parent, ChestTier tier, ChestView.State state, float size)
        {
            var row = Ui.Row(parent, "Art", 0);
            if (ChestAssets.Has(tier))
            {
                var go = new GameObject("ChestArt", typeof(RectTransform));
                go.transform.SetParent(row.transform, false);
                var element = go.AddComponent<LayoutElement>();
                element.preferredWidth = size;
                element.preferredHeight = size;
                go.AddComponent<ChestView>().Init(tier, state, size);
                return;
            }

            var icon = Ui.CardPanel(row.transform, "Body", Theme.ChestColor(tier), 10);
            var iconElement = icon.gameObject.AddComponent<LayoutElement>();
            iconElement.preferredWidth = size;
            iconElement.preferredHeight = size;
            var glyph = Ui.ThemedLabel(icon.transform, ChestRules.TierName(tier).Substring(0, 1),
                Mathf.RoundToInt(size * 0.44f), Color.white, Theme.TitleFont);
            Ui.Stretch(glyph.rectTransform);
        }

        private static void DrawEmptySlot(Transform parent)
        {
            var slot = Ui.CardPanel(parent, "Empty", Theme.LockedBg, 14);
            var slotElement = slot.gameObject.AddComponent<LayoutElement>();
            slotElement.flexibleWidth = 1;
            slotElement.flexibleHeight = 1;
            var label = Ui.ThemedLabel(slot.transform, Strings.T("map.chest.empty_slot"), 21, Theme.LockGray);
            Ui.Stretch(label.rectTransform);
        }

        // ---- 底部导航 ----

        private void BuildNavBar(Transform parent)
        {
            var nav = Ui.Row(parent, "Nav", 17);
            var layout = nav.GetComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            Ui.Anchor((RectTransform)nav.transform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, NavH));

            int unlockedEnemies = 0;
            foreach (var def in _enemies)
                if (BestiaryRules.IsUnlocked(_meta, def.Id)) unlockedEnemies++;
            int unlockedPerks = 0;
            foreach (var perk in PerkRules.All)
                if (PerkRules.PerkLevel(_meta, perk.Id) > 0) unlockedPerks++;

            // 红点各有各的判据,都问 Core:亮着而点进去无事可做是最烦人的那种假消息
            NavTab(nav.transform, "nav_deck", Strings.T("map.nav.collection"),
                Strings.T("map.nav.collection_sub", ("count", _meta.OwnedCards.Count)),
                () => _onOpenCollection(), AnyCardUpgradable(), Theme.DeckTab);
            NavTab(nav.transform, "nav_bestiary", Strings.T("map.nav.bestiary"),
                Strings.T("map.nav.bestiary_sub", ("unlocked", unlockedEnemies), ("total", _enemies.Count)),
                () => _onOpenBestiary(), BestiaryRules.HasUnclaimed(_meta), Theme.BestiaryTab);
            NavTab(nav.transform, "nav_perks", Strings.T("map.nav.perks"),
                Strings.T("map.nav.perks_sub", ("unlocked", unlockedPerks), ("total", PerkRules.All.Count)),
                () => _onOpenPerks(), PerkRules.HasUpgradable(_meta), Theme.PerkTab);
            NavTab(nav.transform, "nav_shop", Strings.T("map.nav.shop"), Strings.T("map.nav.shop_sub"),
                () => _onOpenShop(), ShopRules.HasRedDot(_meta, _time), Theme.ShopTab);
        }

        /// <summary>一个页签:图标 + 名 + 副标题(+ 红点)。四格各一套配色(2026-08-28 反馈),
        /// 三支色同源于一个属性色,见 <see cref="Theme.TabPalette"/>。</summary>
        private static void NavTab(Transform parent, string iconKey, string name, string sub,
            Action onClick, bool dot, Theme.TabPalette palette)
        {
            // 描边卡而不是纯色块:稿上页签靠那条 1pt 边线从宣纸底里立起来(2026-08-28 反馈)
            var tab = Ui.OutlinedPanel(parent, "Tab", palette.Bg, palette.Border, 19, 2f, out var face);
            var button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = face; // 按下要染填充面,染那条边线看不出来
            button.onClick.AddListener(() => onClick());
            var element = tab.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.preferredHeight = NavH;

            var row = Ui.Row(tab.transform, "Content", 15);
            Ui.Stretch((RectTransform)row.transform);
            NavIcon(row.transform, iconKey, palette.Fg);
            Ui.ThemedLabel(row.transform, name, 29, palette.Fg, Theme.TitleFont);
            Ui.ThemedLabel(row.transform, sub, 19, Theme.LockGray);
            if (dot) RedDot(tab.transform);
        }

        /// <summary>页签的线性图标(稿上 17pt)。PNG 缺失时**什么都不画** —— 名字就在旁边,
        /// 补 <see cref="Icons.Fallback"/> 那个兜底汉字反而挤,还会跟页签名连成一串读不断。</summary>
        private static void NavIcon(Transform parent, string key, Color color)
        {
            var sprite = Icons.Get(key);
            if (sprite == null) return;
            var go = Ui.Panel(parent, "Icon");
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color; // 图形是白的,用前景色染
            image.preserveAspect = true;
            image.raycastTarget = false;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 36;
            element.preferredHeight = 36;
        }

        /// <summary>吃掉剩余空间的透明占位(稿上的 .grow / margin-top:auto)。</summary>
        private static void Spring(Transform parent, bool vertical = false)
        {
            var go = Ui.Panel(parent, "Spring");
            var element = go.AddComponent<LayoutElement>();
            if (vertical) element.flexibleHeight = 1;
            else element.flexibleWidth = 1;
        }

        /// <summary>导航红点:钉在按钮右上角,不拦点击。</summary>
        private static void RedDot(Transform button)
        {
            var dot = Ui.Panel(button, "Dot");
            var dotImage = dot.AddComponent<Image>();
            dotImage.sprite = Theme.Circle;
            dotImage.color = Theme.Cinnabar;
            dotImage.raycastTarget = false;
            Ui.Anchor((RectTransform)dot.transform, Vector2.one, Vector2.one,
                new Vector2(-30, -30), new Vector2(-16, -16));
        }

        private bool AnyCardUpgradable()
        {
            foreach (var id in _meta.OwnedCards)
                if (MetaRules.CanUpgradeCard(_meta, id, _graph.Get(id).Rarity))
                    return true;
            return false;
        }

        private bool AnyChestTiming()
        {
            foreach (var chest in _meta.Chests)
                if (chest.Timing && !ChestRules.IsReady(chest, _time))
                    return true;
            return false;
        }

        private void OpenChest(int index)
        {
            var tier = _meta.Chests[index].Tier;
            var ownedBefore = new System.Collections.Generic.HashSet<string>(_meta.OwnedCards);
            if (ChestRules.TryOpen(_meta, index, _time, new GameRandom(Environment.TickCount), out var rewards, _graph))
            {
                _save();
                _message = "";
                Rebuild();
                ShowChestResult(tier, rewards, ownedBefore);
                return;
            }
            Rebuild();
        }

        // ---- 开箱结果面板(ChestOpen.dc.html):左栏交代「哪只箱、多少墨」,右栏整块留给牌 ----
        //
        // 2026-08-30 按稿改成左右分栏。原先是竖排(标题 → 墨锭 → 卡行 → 收下)+ 0.16~0.84 的
        // 比例锚点:横屏下放不进 12 张 —— 两行牌加牌脚要 282pt,光标题与按钮就吃掉 100pt;
        // 而比例锚点会随机型漂,这一屏的排布却是按牌数算死的。现在改成**安全区内定尺**:
        // 根节点已在安全区之内(GameRoot.NewView),所以左右吃满、上下各留稿上的 15pt。

        private const float ResultInsetY = 31f;   // 稿上面板距安全区上下各 15pt
        private const float ResultLeftW = 377f;   // 左栏 180pt
        private const float ResultPad = 33f;      // 面板内边距 16pt

        // 牌按张数在两档之间取(稿:12 张走 6 列 96 宽,16 张走 8 列 70 宽)。
        // 这两个尺寸是**上限**不是定值:格子在行里可被压窄(HorizontalLayoutGroup 在
        // 总预期宽超过可用宽时按比例收),所以比 16:9 更「方」的屏(iPad 4:3)上牌会一起变小
        // 而不是溢出屏外。牌与牌脚同缩,靠的是格内 VStack 的 childForceExpandWidth。
        private const int ResultWideColumns = 6;
        private const int ResultNarrowColumns = 8;
        private static readonly Vector2 ResultWideCard = new(174f, 218f);
        private static readonly Vector2 ResultNarrowCard = new(126f, 158f);

        private void ShowChestResult(ChestTier tier, ChestRewards rewards,
            System.Collections.Generic.HashSet<string> ownedBefore)
        {
            var scrim = Ui.Panel(transform, "ChestResult");
            _resultPanel = scrim;
            var scrimImage = scrim.AddComponent<Image>();
            scrimImage.color = Theme.Scrim; // raycastTarget 默认 true:挡住底下所有点击
            Ui.Stretch((RectTransform)scrim.transform);

            var card = Ui.CardPanel(scrim.transform, "Panel");
            Ui.Anchor((RectTransform)card.transform, Vector2.zero, Vector2.one,
                new Vector2(0f, ResultInsetY), new Vector2(0f, -ResultInsetY));

            var columns = Ui.Row(card.transform, "Columns", ResultPad);
            var columnsLayout = columns.GetComponent<HorizontalLayoutGroup>();
            columnsLayout.childForceExpandHeight = true;
            columnsLayout.padding = new RectOffset((int)ResultPad, (int)ResultPad, (int)ResultPad, (int)ResultPad);
            Ui.Stretch((RectTransform)columns.transform);

            // 「新字 N」要先数出来给左栏用,而右栏逐张翻卡时还要再判一次新旧 ——
            // 两处各拿一份快照,别在同一个集合上边数边判(同一张字在同一箱里可能出两次)
            var counted = new System.Collections.Generic.HashSet<string>(ownedBefore);
            int fresh = 0;
            foreach (var id in rewards.Cards)
                if (counted.Add(id)) fresh++;
            var seen = new System.Collections.Generic.HashSet<string>(ownedBefore);

            BuildResultLeft(columns.transform, tier, rewards, fresh);
            // 两栏之间那条竖线(稿上左栏的 border-right)。不走 CardPanel:Rounded(0) 会算出
            // 一张 50% 半透的图,细线上看着像掉了色
            var separator = Ui.Panel(columns.transform, "Rule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            var separatorElement = separator.AddComponent<LayoutElement>();
            separatorElement.preferredWidth = 2;
            separatorElement.flexibleHeight = 1;

            var tiles = BuildResultGrid(columns.transform, rewards, seen);
            StartCoroutine(RevealTiles(tiles));
        }

        /// <summary>左栏:哪只箱、多少墨、几张新字。立绘用的是与箱位同一张素材。</summary>
        private void BuildResultLeft(Transform parent, ChestTier tier, ChestRewards rewards, int fresh)
        {
            var left = Ui.VStack(parent, "Left", 8);
            var leftLayout = left.GetComponent<VerticalLayoutGroup>();
            leftLayout.childAlignment = TextAnchor.UpperCenter;
            var leftElement = left.AddComponent<LayoutElement>();
            leftElement.preferredWidth = ResultLeftW;
            leftElement.flexibleWidth = 0;

            Ui.ThemedLabel(left.transform,
                Strings.T("map.chest.result_title", ("tierName", ChestRules.TierName(tier))),
                26, Theme.TextMain, Theme.TitleFont);
            // 开箱后箱已移除,这里画的是「刚才那只」——三态取 Ready,金光晕正是开箱的语气
            ChestArt(left.transform, tier, ChestView.State.Ready, ResultArtSize);

            var inkPill = Ui.CardPanel(left.transform, "Ink", Theme.GoldSoft, 15);
            var inkElement = inkPill.gameObject.AddComponent<LayoutElement>();
            inkElement.preferredWidth = 190;
            inkElement.preferredHeight = 63; // 稿上 30pt
            var inkRow = Ui.IngotLabel(inkPill.transform, $"+{rewards.Ink}", 22, true);
            Ui.Stretch((RectTransform)inkRow.transform);
            inkRow.GetComponentInChildren<Text>().color = Theme.GoldDeep;

            Ui.ThemedLabel(left.transform,
                Strings.T("map.chest.result_count", ("count", rewards.Cards.Count), ("fresh", fresh)),
                18, Theme.TextDim);

            var spacer = Ui.Panel(left.transform, "Spacer");
            spacer.AddComponent<LayoutElement>().flexibleHeight = 1; // 提示贴左栏底(稿上 margin-top:auto)
            var hint = Ui.ThemedLabel(left.transform, Strings.T("map.chest.result_hint"), 15, Theme.LockGray);
            hint.alignment = TextAnchor.LowerCenter;
        }

        /// <summary>右栏:卡网格 + 收下。返回逐张翻卡要用的格子(先隐藏)。</summary>
        private System.Collections.Generic.List<GameObject> BuildResultGrid(Transform parent,
            ChestRewards rewards, System.Collections.Generic.HashSet<string> seen)
        {
            var right = Ui.VStack(parent, "Right", 17);
            var rightLayout = right.GetComponent<VerticalLayoutGroup>();
            rightLayout.childAlignment = TextAnchor.UpperCenter;
            rightLayout.childForceExpandWidth = true;
            var rightElement = right.AddComponent<LayoutElement>();
            rightElement.flexibleWidth = 1;

            bool narrow = rewards.Cards.Count > ResultWideColumns * 2;
            int columns = narrow ? ResultNarrowColumns : ResultWideColumns;
            var cardSize = narrow ? ResultNarrowCard : ResultWideCard;

            var tiles = new System.Collections.Generic.List<GameObject>();
            Transform row = null;
            for (int i = 0; i < rewards.Cards.Count; i++)
            {
                if (i % columns == 0) row = Ui.Row(right.transform, $"CardRow{i / columns}", 17).transform;
                string cardId = rewards.Cards[i];
                var def = _graph.Get(cardId);
                bool isNew = seen.Add(cardId);

                var cell = Ui.VStack(row, $"Reward_{cardId}_{i}", 6);
                // 牌与牌脚一起吃格宽:格被压窄时两者同缩,牌脚才不会比牌宽出去
                cell.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
                // ⚠ 上面那行会让格子**向外**也报出弹性宽:uGUI 的 GetChildSizes 里
                // `if (childForceExpand) flexible = Mathf.Max(flexible, 1)`,于是格子的
                // VerticalLayoutGroup 对外 flexibleWidth = 1,行里多出来的宽全分给了它们。
                // 12 张时余量小看不出来,3 张(素纸匣)时三格瓜分整条右栏 —— 牌被拉成
                // 宽高比 1.7 的横条(2026-08-30 试玩发现)。这里钉死:preferred = 牌宽、
                // flexible = 0(LayoutElement 的 layoutPriority 1 压过布局组的 0)。
                // minWidth 故意不设 —— 留着 0,窄屏上仍可整排一起压窄。
                var cellElement = cell.AddComponent<LayoutElement>();
                cellElement.preferredWidth = cardSize.x;
                cellElement.flexibleWidth = 0;
                // 点卡看详情(2026-08-17):与商城/收集同款 CharPreview,弹在结果面板之上
                Ui.GlyphTile(cell.transform, def, false, () => ShowRewardPreview(cardId), cardSize);
                ResultFoot(cell.transform, cardId, def, isNew, cardSize.x);
                cell.SetActive(false);
                tiles.Add(cell);
            }

            // 收下贴右栏底(稿上 margin-top:auto):撑开的是这块空白,不是按钮行本身 ——
            // 把 flexibleHeight 挂在按钮行上会让行变高、钮浮到半空
            var gridSpacer = Ui.Panel(right.transform, "Spacer");
            gridSpacer.AddComponent<LayoutElement>().flexibleHeight = 1;

            var claim = Ui.Row(right.transform, "Claim", 21);
            claim.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var claimElement = claim.AddComponent<LayoutElement>();
            claimElement.preferredHeight = 84;
            claimElement.flexibleHeight = 0;
            Ui.ThemedLabel(claim.transform, Strings.T("map.chest.result_tip"), 15, Theme.LockGray);
            var claimSpacer = Ui.Panel(claim.transform, "Spacer");
            claimSpacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.PillButton(claim.transform, Strings.T("map.chest.claim_button"), () =>
            {
                Destroy(_resultPanel);
                _resultPanel = null;
                Rebuild(); // 面板期间押后的就绪跃迁在此补上
            }, Theme.Cinnabar, Color.white, 20, new Vector2(180, 50));

            return tiles;
        }

        /// <summary>牌脚三态(稿:一张牌只回答一件事 —— 它对你意味着什么)。
        /// 新字走实心胭脂底,是这一屏最该被看见的东西;重复字凑满转翠玉底,
        /// 「这张现在就能升」和「还差几张」是两件事;满级不写进度,别拿填不满的分母吊着人。</summary>
        private void ResultFoot(Transform parent, string cardId, CharDef def, bool isNew, float width)
        {
            GameObject chip;
            if (isNew)
            {
                chip = Ui.Chip(parent, Strings.T("map.chest.new_badge"), Theme.ExitPink, Color.white, 15);
            }
            else
            {
                int level = MetaRules.CardLevel(_meta, cardId);
                _meta.CardCopies.TryGetValue(cardId, out int copies);
                if (level >= MetaRules.MaxCardLevel)
                {
                    chip = Ui.Chip(parent, Strings.T("common.maxed"), Theme.GoldSoft, Theme.GoldDeep, 15);
                }
                else
                {
                    int needed = MetaRules.CopiesRequired(level, def.Rarity);
                    bool upgradable = copies >= needed;
                    chip = Ui.Chip(parent,
                        Strings.T("map.chest.upgrade_progress", ("copies", copies), ("needed", needed)),
                        upgradable ? Theme.AdGreenBg : Theme.PaperDim,
                        upgradable ? Theme.UpgradeText : Theme.TextDim, 15);
                }
            }
            var element = chip.GetComponent<LayoutElement>();
            element.preferredWidth = width; // 牌脚与牌同宽:三态一眼可比,不受文本长短影响
            element.preferredHeight = 31;   // 稿上 15pt
        }

        /// <summary>开箱奖励卡详情:开箱已入账,等级/进度按入账后的现值显示。</summary>
        private void ShowRewardPreview(string cardId)
        {
            if (_modal != null) Destroy(_modal);
            _modal = CharPreview.Show(transform, _graph.Get(cardId), _graph, MetaRules.CardLevel(_meta, cardId));
        }

        private System.Collections.IEnumerator RevealTiles(System.Collections.Generic.List<GameObject> tiles)
        {
            foreach (var tile in tiles)
            {
                if (tile == null) yield break; // 面板已被「收下」关闭
                tile.SetActive(true);
                var rect = (RectTransform)tile.transform;
                float t = 0;
                while (t < 0.12f)
                {
                    if (rect == null) yield break;
                    t += Time.unscaledDeltaTime;
                    rect.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, t / 0.12f);
                    yield return null;
                }
                if (rect != null) rect.localScale = Vector3.one;
                yield return new WaitForSecondsRealtime(0.08f);
            }
        }

        private void Do(Func<bool> action, string failTitle = null, string failBody = null)
        {
            if (action())
            {
                _save();
                Rebuild();
                return;
            }
            Rebuild();
            if (failTitle != null) // 无原因可给的(按钮已 disable 拦住)保持静默
                ShowAlert(failTitle, failBody);
        }

        /// <summary>被拒提示统一弹窗(2026-07-19);须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }

        private static string Format(long seconds) =>
            seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }
}
