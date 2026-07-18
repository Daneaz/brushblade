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

            // 初始收集 = 火系初始卡组(19.3.4;人工筛选后替换,当前原型为灯)
            if (_meta.OwnedCards.Count == 0)
                MetaRules.AcquireCard(_meta, "灯");

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
            var pool = UnlockedRewardPool();
            if (ShopRules.EnsureShelf(_meta, pool, Time, new GameRandom(System.Environment.TickCount)))
                MetaStore.Save(_meta);
            var view = NewView("ShopView");
            view.AddComponent<ShopView>().Init(_graph, _meta, pool, Time, () => MetaStore.Save(_meta), () => ShowMap());
        }

        /// <summary>已踏入层段的字池并集(F3 分层段投放:商城不上架未解锁层段的字,20.8)。</summary>
        private static System.Collections.Generic.List<string> UnlockedRewardPool()
        {
            var pool = new System.Collections.Generic.List<string>();
            foreach (var band in _campaign.Endless.Bands)
            {
                if (band.FromDepth > 1 && _meta.BestDepth < band.FromDepth) break;
                foreach (var card in band.RewardPool)
                    if (!pool.Contains(card))
                        pool.Add(card);
            }
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
                    Pool = new System.Collections.Generic.List<string> { "木", "木" },
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
                onExit: () => ShowMap("登塔已挂起,随时回来继续"));
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

            // Boss 层告捷:经验 + 纪录 + 快照推进到下一段首层(安全层挂起点)
            _meta.CharacterXp += EndlessRules.XpFor(endless, segmentEnd);
            EndlessRules.UpdateBest(_meta, segmentEnd);
            var snapshot = _meta.Endless;
            snapshot.Depth = segmentEnd + 1;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            var library = new System.Collections.Generic.List<string>(run.Battle.Library);
            library.AddRange(run.Battle.UsedChars); // 出过的字回归(3.8.1)
            snapshot.Library = library;
            snapshot.Pool = new System.Collections.Generic.List<string>(run.Battle.Pool);
            snapshot.EarnedInk = totalEarned;
            snapshot.LibraryExpanded = false; // 段内广告扩容一段一次,过段恢复
            snapshot.PoolExpanded = false;
            MetaStore.Save(_meta);
            ShowSafeLayer(segmentEnd, totalEarned);
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

        /// <summary>安全层(20.5):继续深入 or 收官撤退的主动抉择。</summary>
        private static void ShowSafeLayer(int depth, int totalEarned)
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
            Ui.Anchor((RectTransform)card.transform, new Vector2(0.24f, 0.18f), new Vector2(0.76f, 0.82f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 16);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, $"安全层 · 第 {depth} 层告捷", 30, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                $"段位「{EndlessRules.RankTitle(_meta.BestDepth)}」 · 最高第 {_meta.BestDepth} 层", 18, Theme.TextDim);
            Ui.IngotLabel(stack.transform, $"滚存 {totalEarned}", 20);
            Ui.ThemedLabel(stack.transform,
                $"继续:滚存收益带入更深层,阵亡墨锭减半、宝箱退回本层\n撤退:立即全额结算(宝箱按第 {depth} 层档位)", 16, Theme.TextDim);

            Ui.PillButton(stack.transform, $"深入「{nextBand.Name}」第 {depth + 1}~{depth + endless.BossEvery} 层",
                () => StartSegment(firstTower: false), Theme.Cinnabar, Color.white, 20, new Vector2(340, 58));
            Ui.PillButton(stack.transform, "收官撤退(全额结算)",
                () => SettleTower(died: false, depth, totalEarned), Theme.InkSoft, Color.white, 20, new Vector2(340, 58));
        }

        /// <summary>塔结算(20.5):撤退全额/阵亡半额;宝箱按结算层(阵亡退回最后安全层)。</summary>
        private static void SettleTower(bool died, int clearedDepth, int totalEarned)
        {
            var endless = _campaign.Endless;
            _meta.Endless = null;
            EndlessRules.UpdateBest(_meta, clearedDepth);
            int ink = EndlessRules.SettleInk(totalEarned, died);
            _meta.Ink += ink;
            int chestDepth = died ? clearedDepth / endless.BossEvery * endless.BossEvery : clearedDepth;

            string message = died
                ? $"卒于第 {clearedDepth + 1} 层……墨锭 {ink}(半额)入账"
                : $"第 {clearedDepth} 层收官!墨锭 {ink} 入账";
            if (chestDepth >= endless.BossEvery)
            {
                var tier = EndlessRules.ChestTierFor(chestDepth, new GameRandom(System.Environment.TickCount));
                if (ChestRules.TryAwardChest(_meta, tier, endless.BandFor(chestDepth).RewardPool, Time))
                    message += $",获得{ChestRules.TierName(tier)}";
                else
                    message += ",箱位已满未获宝箱";
            }
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
