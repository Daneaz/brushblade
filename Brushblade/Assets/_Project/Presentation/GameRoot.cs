using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>游戏根:配置与存档加载、地图/战斗视图切换、通关结算(19.1 双层结构)。
    /// 按 Play 即在任意场景运行,原型期免场景资产。</summary>
    public static class GameRoot
    {
        private static GameObject _viewRoot;
        private static RecipeGraph _graph;
        private static CampaignConfig _campaign;
        private static MetaState _meta;
        private static readonly SyncedTimeSource Time = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // 横屏 only(2026-07-11 拍板):运行时兜底,与 ProjectSettings 双保险
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Application.targetFrameRate = 60; // 移动端默认可能锁 30,点按反馈要跟手

            EnsureSceneInfrastructure();

            // 启动校时(19.9):失败则本会话退化为设备时间
            new GameObject("TimeSync").AddComponent<TimeSyncFetcher>().Begin(Time);

            string configDir = Path.Combine(Application.streamingAssetsPath, "config");
            _graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            _campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), _graph);
            _meta = MetaStore.Load();
            MetaRules.PruneUnknownCards(_meta, _graph); // 字表裁剪后清洗旧存档引用

            // 初始收集保底 = 五系 2 叠字各一张(2026-07-19 拍板;缺哪张补哪张,教程依赖 炎)
            foreach (var card in new[] { "鍂", "林", "沝", "炎", "圭" })
                if (!_meta.OwnedCards.Contains(card))
                    MetaRules.AcquireCard(_meta, card);
            // 出阵不足下限 → 播默认五系(补齐已废止,空出阵 = 空手登塔)
            if (_meta.Deck.Count < MetaRules.DeckMinimum)
                MetaRules.TrySetDeck(_meta, new[] { "炎", "鍂", "沝", "林", "圭" }, _graph);

            ShowMap();
        }

        public static void ShowMap(string message = null)
        {
            var view = NewView("MapView");
            view.AddComponent<MapView>().Init(_graph, _campaign, _meta, Time, StartTower, () => MetaStore.Save(_meta), message,
                onOpenCollection: ShowCollection, onOpenShop: ShowShop);
        }

        private static void ShowCollection()
        {
            var view = NewView("CollectionView");
            view.AddComponent<CollectionView>().Init(_graph, _meta, () => MetaStore.Save(_meta), () => ShowMap());
        }

        private static void ShowShop()
        {
            var cardPool = ShopCardPool();
            if (ShopRules.EnsureShelf(_meta, cardPool, Time, new GameRandom(System.Environment.TickCount)))
                MetaStore.Save(_meta);
            var view = NewView("ShopView");
            view.AddComponent<ShopView>().Init(_graph, _meta, cardPool, ChestCardPool(), Time,
                () => MetaStore.Save(_meta), () => ShowMap());
        }

        /// <summary>商城卡位池 = 部件 + 已拥有的字(2026-07-19 拍板:未拥有的字只能开宝箱)。</summary>
        private static System.Collections.Generic.List<string> ShopCardPool()
        {
            var pool = new System.Collections.Generic.List<string>(RunEngine.ComponentRewardChoices);
            foreach (var card in _meta.OwnedCards)
                if (!pool.Contains(card))
                    pool.Add(card);
            return pool;
        }

        /// <summary>宝箱卡池 = 全部可收集字(带配方的 15 字):3/4 叠唯一收集渠道,
        /// 箱级越高稀有度权重越偏高阶(ChestRules.CardRarityWeights + 保底)。</summary>
        private static System.Collections.Generic.List<string> ChestCardPool()
        {
            var pool = new System.Collections.Generic.List<string>();
            foreach (var def in _graph.All)
                if (!def.IsLeaf)
                    pool.Add(def.Id);
            return pool;
        }

        // ---- 无尽塔流程(第 20 章):登塔/续爬 → 逐段连战 → 安全层抉择 → 结算 ----

        private static void StartTower()
        {
            var snapshot = _meta.Endless;
            bool firstTower = _meta.BestDepth == 0 && snapshot == null;
            if (snapshot == null)
            {
                int level = MetaRules.CharacterLevel(_meta.CharacterXp);
                _meta.Endless = new EndlessSaveState
                {
                    Depth = 1,
                    PlayerHp = MetaRules.MaxHpFor(level),
                    Seed = System.Environment.TickCount,
                    Library = new System.Collections.Generic.List<string>(MetaRules.StartingLibrary(_meta)),
                    Pool = new System.Collections.Generic.List<string> { "火", "火" }, // 教程连招原料:拆炎→合炎→合焱
                };
                MetaStore.Save(_meta);
            }
            StartSegment(firstTower);
        }

        private static void StartSegment(bool firstTower)
        {
            var endless = _campaign.Endless;
            var snapshot = _meta.Endless;
            int fromDepth = snapshot.Depth;
            var band = endless.BandFor(fromDepth);
            int segmentEnd = (fromDepth - 1) / endless.BossEvery * endless.BossEvery + endless.BossEvery;

            // 层段首破里程碑(20.3):层段边界都是段首,踏入即发,断点重入不重复
            if (EndlessRules.TryAwardMilestone(_meta, band))
                MetaStore.Save(_meta);

            var runConfig = firstTower && fromDepth <= 1
                ? EndlessGenerator.BuildFirstTowerSegment(endless, snapshot.Seed, _campaign.Events, _campaign.EventChancePercent)
                : EndlessGenerator.BuildSegment(endless, fromDepth, snapshot.Seed, _campaign.Events, _campaign.EventChancePercent);

            int maxHp = MetaRules.MaxHpFor(MetaRules.CharacterLevel(_meta.CharacterXp));
            var battleConfig = new BattleConfig { DropTable = _campaign.DropTable, PlayerMaxHp = maxHp };
            var run = new RunEngine(_graph, runConfig, battleConfig,
                snapshot.Library, snapshot.Pool,
                seed: unchecked(snapshot.Seed * 17 + fromDepth), cardLevels: _meta.CardLevels,
                startingInk: _meta.Ink + snapshot.EarnedInk, // 字摊预算 = 库存 + 塔内滚存
                startingHp: snapshot.PlayerHp);
            if (snapshot.LibraryExpanded) run.TryExpandLibrary(); // 断点恢复段内广告扩容
            if (snapshot.PoolExpanded) run.TryExpandPool();

            var tutorial = firstTower && fromDepth <= 1 ? new Tutorial() : null;
            int baseInk = snapshot.EarnedInk; // 段前滚存

            // 每段换景(20.2):层段基色 + 段内逐段加深 + 巨字水印(林/渊/山/海)
            int bandIndex = BandIndexFor(fromDepth);
            var paper = Theme.BandPaper(bandIndex, (fromDepth - band.FromDepth) / endless.BossEvery);
            var view = NewView("BattleView", paper, band.Name.Substring(band.Name.Length - 1), bandIndex);
            view.AddComponent<BattleView>().Init(_graph, run,
                won => OnSegmentEnded(run, fromDepth, segmentEnd, baseInk, won),
                tutorial, $"「{band.Name}」第 {fromDepth}~{segmentEnd} 层", maxHp,
                onNewFloor: () => OnFloorAdvanced(run, fromDepth, baseInk),
                onExit: () => ShowMap("登塔已挂起,随时回来继续"),
                onExpanded: () => OnExpanded(run),
                onAbandon: () => SettleTower(died: true, // 弃塔=阵亡待遇:半额结算,防绕过安全层撤退决策
                    fromDepth + run.BattleIndex - 1, baseInk + run.EarnedInk, abandoned: true));
        }

        /// <summary>广告扩容即时落盘:挂起/杀进程也不丢已看广告换来的容量。</summary>
        private static void OnExpanded(RunEngine run)
        {
            var snapshot = _meta.Endless;
            if (snapshot == null) return;
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            MetaStore.Save(_meta);
        }

        /// <summary>新一层开打:层粒度断点快照(20.6)+ 层经验 + 层段首破里程碑(20.3)。</summary>
        private static void OnFloorAdvanced(RunEngine run, int fromDepth, int baseInk)
        {
            var endless = _campaign.Endless;
            int depth = fromDepth + run.BattleIndex;
            _meta.CharacterXp += EndlessRules.XpFor(endless, depth - 1); // 刚打完的层

            var snapshot = _meta.Endless;
            snapshot.Depth = depth;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.Battle.Library);
            snapshot.Pool = new System.Collections.Generic.List<string>(run.Battle.Pool);
            snapshot.EarnedInk = baseInk + run.EarnedInk;
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            MetaStore.Save(_meta);
        }

        private static void OnSegmentEnded(RunEngine run, int fromDepth, int segmentEnd, int baseInk, bool won)
        {
            var endless = _campaign.Endless;
            int totalEarned = baseInk + run.EarnedInk;
            if (!won)
            {
                int clearedDepth = fromDepth + run.BattleIndex - 1;
                SettleTower(died: true, clearedDepth, totalEarned);
                return;
            }

            // Boss 层告捷:经验 + 纪录 + 即发宝箱(2026-07-19 拍板,原「结算发箱」废止)
            _meta.CharacterXp += EndlessRules.XpFor(endless, segmentEnd);
            EndlessRules.UpdateBest(_meta, segmentEnd);
            var tier = EndlessRules.ChestTierFor(segmentEnd, new GameRandom(System.Environment.TickCount));
            string chestNote = ChestRules.TryAwardChest(_meta, tier, ChestCardPool(), Time)
                ? $"获得{ChestRules.TierName(tier)}!"
                : "箱位已满,宝箱与你擦肩……";
            var snapshot = _meta.Endless;
            snapshot.Depth = segmentEnd + 1;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.Battle.Library); // 出字即消耗,无回归(v0.7)
            snapshot.Pool = new System.Collections.Generic.List<string>(run.Battle.Pool);
            snapshot.EarnedInk = totalEarned;
            snapshot.LibraryExpanded = run.LibraryExpanded; // 扩容跟随整次登塔(一局一次),结算随快照清除
            snapshot.PoolExpanded = run.PoolExpanded;
            MetaStore.Save(_meta);
            ShowSafeLayer(segmentEnd, totalEarned, chestNote);
        }

        /// <summary>该深度所在层段的下标(背景色板索引)。</summary>
        private static int BandIndexFor(int depth)
        {
            var bands = _campaign.Endless.Bands;
            int index = 0;
            for (int i = 0; i < bands.Count; i++)
                if (bands[i].FromDepth <= depth)
                    index = i;
            return index;
        }

        /// <summary>安全层(20.5):继续深入 or 收官撤退的主动抉择;
        /// 休整(2026-07-19 拍板):可用出阵列表调整字库(应对下一段敌情)。</summary>
        private static void ShowSafeLayer(int depth, int totalEarned, string chestNote = null)
        {
            var endless = _campaign.Endless;
            var nextBand = endless.BandFor(depth + 1);
            var band = endless.BandFor(depth);
            int bandIndex = BandIndexFor(depth);
            var view = NewView("SafeLayerView",
                Theme.BandPaper(bandIndex, (depth - band.FromDepth) / endless.BossEvery),
                band.Name.Substring(band.Name.Length - 1), bandIndex);
            Ui.Stretch((RectTransform)view.transform);

            var card = Ui.CardPanel(view.transform, "Panel");
            Ui.Anchor((RectTransform)card.transform, new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 10);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, $"安全层 · 第 {depth} 层告捷", 28, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                $"段位「{EndlessRules.RankTitle(_meta.BestDepth)}」 · 最高第 {_meta.BestDepth} 层", 16, Theme.TextDim);
            Ui.IngotLabel(stack.transform, $"滚存 {totalEarned}", 18);
            if (chestNote != null)
                Ui.ThemedLabel(stack.transform, $"◆ 破关战利:{chestNote}", 18, Theme.GoldBorder, Theme.TitleFont);

            DrawRest(stack.transform, depth, totalEarned, chestNote);

            Ui.ThemedLabel(stack.transform,
                "继续:滚存收益带入更深层,阵亡墨锭减半;撤退:立即全额结算墨锭", 14, Theme.TextDim);
            Ui.PillButton(stack.transform, $"深入「{nextBand.Name}」第 {depth + 1}~{depth + endless.BossEvery} 层",
                () => StartSegment(firstTower: false), Theme.Cinnabar, Color.white, 19, new Vector2(340, 52));
            Ui.PillButton(stack.transform, "收官撤退(全额结算)",
                () => SettleTower(died: false, depth, totalEarned), Theme.InkSoft, Color.white, 19, new Vector2(340, 52));
        }

        /// <summary>休整区:字库(点移出)+ 备选出阵列表(点加入),即时写快照。</summary>
        private static void DrawRest(Transform parent, int depth, int totalEarned, string chestNote)
        {
            var snapshot = _meta.Endless;
            if (snapshot == null) return;
            int libraryCap = MetaRules.StartingLibrarySize + (snapshot.LibraryExpanded ? 2 : 0);

            Ui.ThemedLabel(parent, $"休整 · 字库 {snapshot.Library.Count}/{libraryCap}(点字移出)", 15, Theme.TextDim, Theme.TitleFont);
            var libraryRow = Ui.Row(parent, "RestLibrary", 6);
            foreach (var id in snapshot.Library)
            {
                string charId = id;
                var def = _graph.Get(charId);
                Ui.RoundButton(libraryRow.transform, charId, () =>
                {
                    snapshot.Library.Remove(charId);
                    MetaStore.Save(_meta);
                    ShowSafeLayer(depth, totalEarned, chestNote);
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element), 20, new Vector2(46, 46), 10);
            }

            // 备选 = 出阵列表中不在字库的字(补齐已废止:未出阵的字不上场,2026-07-19)
            var roster = _meta.Deck;
            var reserve = new System.Collections.Generic.List<string>();
            foreach (var id in roster)
                if (!snapshot.Library.Contains(id))
                    reserve.Add(id);
            if (reserve.Count == 0) return;

            Ui.ThemedLabel(parent, snapshot.Library.Count >= libraryCap
                ? "备选(字库已满——先点上方的字移出,才能换新的进来)"
                : "备选(出阵列表,点字加入)", 15, Theme.TextDim, Theme.TitleFont);
            var reserveRow = Ui.Row(parent, "RestReserve", 6);
            foreach (var id in reserve)
            {
                string charId = id;
                var def = _graph.Get(charId);
                var button = Ui.RoundButton(reserveRow.transform, charId, () =>
                {
                    if (snapshot.Library.Count >= libraryCap) return;
                    snapshot.Library.Add(charId);
                    MetaStore.Save(_meta);
                    ShowSafeLayer(depth, totalEarned, chestNote);
                }, Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element), 20, new Vector2(46, 46), 10);
                button.interactable = snapshot.Library.Count < libraryCap;
            }
        }

        /// <summary>塔结算(20.5):撤退全额/阵亡与弃塔半额;宝箱已随每个 Boss 层即时发放,结算不再发。</summary>
        private static void SettleTower(bool died, int clearedDepth, int totalEarned, bool abandoned = false)
        {
            _meta.Endless = null;
            EndlessRules.UpdateBest(_meta, clearedDepth);
            int ink = EndlessRules.SettleInk(totalEarned, died);
            _meta.Ink += ink;

            string message = abandoned
                ? $"于第 {clearedDepth + 1} 层弃塔……墨锭 {ink}(半额)入账,纪录保留"
                : died
                    ? $"卒于第 {clearedDepth + 1} 层……墨锭 {ink}(半额)入账"
                    : $"第 {clearedDepth} 层收官!墨锭 {ink} 入账";
            MetaStore.Save(_meta);
            ShowMap(message);
        }

        private static GameObject NewView(string name, Color? paper = null, string watermark = null, int bandIndex = 0)
        {
            if (_viewRoot != null) Object.Destroy(_viewRoot);
            _viewRoot = new GameObject("ViewRoot");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(_viewRoot.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 1f; // 横屏按高度匹配:20:9 长条屏不放大纵向占位

            // 全屏宣纸底:不依赖场景相机设置;层段可染色(20.2 每段换景)
            var backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGo.transform.SetParent(canvasGo.transform, false);
            backgroundGo.GetComponent<Image>().color = paper ?? Theme.Paper;
            backgroundGo.GetComponent<Image>().raycastTarget = false;
            Ui.Stretch((RectTransform)backgroundGo.transform);

            // 层段巨字水印(林/渊/山/海):近乎透明的墨痕,进新层段的第一体感
            if (watermark != null)
            {
                var mark = Ui.Label(canvasGo.transform, watermark, 520);
                mark.color = Theme.BandWatermark(bandIndex);
                mark.raycastTarget = false;
                Ui.Stretch(mark.rectTransform);
            }

            // 安全区容器:内容避开刘海/挖孔,宣纸底仍全屏
            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(canvasGo.transform, false);
            safeGo.AddComponent<SafeAreaFitter>();

            var viewGo = new GameObject(name, typeof(RectTransform));
            viewGo.transform.SetParent(safeGo.transform, false);
            return viewGo;
        }

        private static void EnsureSceneInfrastructure()
        {
            if (Camera.main == null)
            {
                var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraGo.tag = "MainCamera";
            }
            // 无论相机来自场景还是代码,统一宣纸底(设计板主题)
            var main = Camera.main;
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = Theme.Paper;
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }
    }
}
