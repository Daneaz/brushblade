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
            new GameObject("SaveOnSuspend").AddComponent<SaveOnSuspend>(); // 切后台保底落盘

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

        /// <summary>把本段打赢的敌人同步进图鉴(RecordDefeat 自身幂等,可重复调用)。</summary>
        private static void SyncBestiary(RunEngine run)
        {
            foreach (var id in run.DefeatedEnemyIds)
                BestiaryRules.RecordDefeat(_meta, id);
        }

        /// <summary>保底落盘:切后台/退出时由 SaveOnSuspend 调用(引导未完成则无档可存)。</summary>
        public static void SaveNow()
        {
            if (_meta != null) MetaStore.Save(_meta);
        }

        public static void ShowMap(string message = null)
        {
            var view = NewView("MapView");
            view.AddComponent<MapView>().Init(_graph, _campaign, _meta, Time, StartTower, () => MetaStore.Save(_meta), message,
                onOpenCollection: ShowCollection, onOpenShop: ShowShop, onOpenBestiary: ShowBestiary, onOpenPerks: ShowPerks);
        }

        private static void ShowCollection()
        {
            var view = NewView("CollectionView");
            view.AddComponent<CollectionView>().Init(_graph, _meta, () => MetaStore.Save(_meta), () => ShowMap());
        }

        private static void ShowBestiary()
        {
            var view = NewView("BestiaryView");
            view.AddComponent<BestiaryView>().Init(_campaign, _meta, () => MetaStore.Save(_meta), () => ShowMap());
        }

        private static void ShowPerks()
        {
            var view = NewView("PerkView");
            view.AddComponent<PerkView>().Init(_meta, () => MetaStore.Save(_meta), () => ShowMap());
        }

        private static void ShowShop()
        {
            var cardPool = ShopCardPool();
            // 旧货架可能还摆着部件(2026-07-19 下架):作废重摆,不必等跨日
            if (_meta.Shop.CardSlots.Exists(id => _graph.TryGet(id, out var def) && def.IsLeaf))
                _meta.Shop.DayStamp = -1;
            if (ShopRules.EnsureShelf(_meta, cardPool, Time, new GameRandom(System.Environment.TickCount)))
                MetaStore.Save(_meta);
            var view = NewView("ShopView");
            view.AddComponent<ShopView>().Init(_graph, _meta, cardPool, ChestCardPool(), Time,
                () => MetaStore.Save(_meta), () => ShowMap());
        }

        /// <summary>商城卡位池 = 已拥有的字(2026-07-19:部件每回合白掉,上架无意义已下架;
        /// 未拥有的字只能开宝箱)。买重复卡用于升级。</summary>
        private static System.Collections.Generic.List<string> ShopCardPool()
        {
            var pool = new System.Collections.Generic.List<string>();
            foreach (var card in _meta.OwnedCards)
                if (_graph.TryGet(card, out var def) && !def.IsLeaf && !pool.Contains(card))
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
                    PlayerHp = MetaRules.MaxHpFor(level) + PerkRules.HpBonus(_meta),
                    Seed = System.Environment.TickCount,
                    Library = new System.Collections.Generic.List<string>(MetaRules.StartingLibrary(_meta)),
                    Pool = new System.Collections.Generic.List<string> { "火", "火" }, // 教程连招原料:拆炎→合炎→合焱
                    NormalShield = PerkRules.ShieldBonus(_meta), // 金汤:首段段首护盾
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
            // 战利品的字只出自出阵列表(2026-07-20 拍板):补的是自己带上来的弹药,
            // 抽取按稀有度加权(绿 80/蓝 15/紫 5,见 RunEngine.RewardRarityWeights)
            runConfig.RewardPool = _meta.Deck;

            int maxHp = MetaRules.MaxHpFor(MetaRules.CharacterLevel(_meta.CharacterXp))
                + PerkRules.HpBonus(_meta);
            var battleConfig = new BattleConfig
            {
                DropTable = _campaign.DropTable,
                PlayerMaxHp = maxHp,
                UnlockedChars = _meta.Deck, // 只能合出阵列表里的字(2026-07-20;与战利品同源)
                ApPerTurn = 3 + PerkRules.ApBonus(_meta), // 一气
                LibraryCapacity = MetaRules.StartingLibrarySize + PerkRules.LibraryBonus(_meta), // 博闻:字库容量上限 +1/级(与起手数量同源;广告 +2 在其上叠加)
            };
            var run = new RunEngine(_graph, runConfig, battleConfig,
                snapshot.Library, snapshot.Pool,
                seed: unchecked(snapshot.Seed * 17 + fromDepth), cardLevels: _meta.CardLevels,
                startingInk: _meta.Ink + snapshot.EarnedInk, // 字摊预算 = 库存 + 塔内滚存
                startingHp: snapshot.PlayerHp,
                startingNormalShield: snapshot.NormalShield,
                startingPersistShield: snapshot.PersistShield,
                perFloorNormalShield: PerkRules.ShieldBonus(_meta)); // 金汤:每关开战补盾(段首由 NormalShield 注入)
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
                onNewFloor: () => OnFloorAdvanced(run, baseInk),
                onFloorCleared: () => baseInk = OnFloorCleared(run, fromDepth, baseInk),
                onExit: () => // 挂起离塔:此前只切视图不落盘,靠上一次写盘兜底(2026-07-22 补)
                {
                    SaveNow();
                    ShowMap("登塔已挂起,随时回来继续");
                },
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

        /// <summary>本层战利品取完:立即记账落盘(2026-07-20 拍板)——此前要等下一层开打才写快照,
        /// 在战利品/奇遇页挂起会丢掉本层收益。段末(RunWon)交给 OnSegmentEnded 统一结算。</summary>
        /// <returns>累加本层墨锭后的段前滚存(调用方回写,供后续层与结算使用)。</returns>
        private static int OnFloorCleared(RunEngine run, int fromDepth, int baseInk)
        {
            var snapshot = _meta.Endless;
            if (snapshot == null || run.Phase == RunPhase.RunWon) return baseInk;

            SyncBestiary(run);
            int cleared = fromDepth + run.BattleIndex; // 刚打完的层
            if (snapshot.Depth <= cleared)             // 幂等:同一层只记一次账
            {
                _meta.CharacterXp += EndlessRules.XpFor(_campaign.Endless, cleared);
                // 层墨锭进塔内滚存而非账户(2026-07-21):塔结算时才入账,阵亡随 SettleInk 减半
                baseInk += EndlessRules.FloorInk(_campaign.Endless, cleared);
                snapshot.Depth = cleared + 1;          // 推进后挂起不会重打本层(也就刷不出重复战利品)
            }
            WriteCarriedSnapshot(run, snapshot, baseInk);
            MetaStore.Save(_meta);
            return baseInk;
        }

        /// <summary>新一层开打:刷新携带态(奇遇结果与新回合掉落)。
        /// 层经验与 Depth 推进已在 OnFloorCleared 记过账,这里不再重复。</summary>
        private static void OnFloorAdvanced(RunEngine run, int baseInk)
        {
            var snapshot = _meta.Endless;
            if (snapshot == null) return;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.Battle.Library);
            snapshot.Pool = new System.Collections.Generic.List<string>(run.Battle.Pool);
            snapshot.EarnedInk = baseInk + run.EarnedInk;
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            MetaStore.Save(_meta);
        }

        /// <summary>写入战斗之间的携带态(战利品已并入其中)。</summary>
        private static void WriteCarriedSnapshot(RunEngine run, EndlessSaveState snapshot, int baseInk)
        {
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.CarriedLibrary);
            snapshot.Pool = new System.Collections.Generic.List<string>(run.CarriedPool);
            snapshot.EarnedInk = baseInk + run.EarnedInk;
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            snapshot.NormalShield = run.CarriedNormalShield;
            snapshot.PersistShield = run.CarriedPersistShield;
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

            // Boss 层告捷:经验 + 纪录;宝箱改为整次爬塔结算时按最高 Boss 层发一个(2026-07-22),
            // 此处只记录已破的最高 Boss 层
            SyncBestiary(run); // Boss 层走 RunWon,不经过 OnFloorCleared
            _meta.CharacterXp += EndlessRules.XpFor(endless, segmentEnd);
            totalEarned += EndlessRules.FloorInk(endless, segmentEnd); // Boss 层墨锭(层清算走 OnFloorCleared,段末不经手)
            EndlessRules.UpdateBest(_meta, segmentEnd);
            var snapshot = _meta.Endless;
            snapshot.TopBossDepth = segmentEnd; // 逐段递增,即本次已破最高 Boss 层
            snapshot.Depth = segmentEnd + 1;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            // 用携带态而非 Battle:Boss 层战利品(2026-07-20)加在携带态上,读 Battle 会把它丢掉
            snapshot.Library = new System.Collections.Generic.List<string>(run.CarriedLibrary); // 出字即消耗,无回归(v0.7)
            snapshot.Pool = new System.Collections.Generic.List<string>(run.CarriedPool);
            snapshot.EarnedInk = totalEarned;
            snapshot.LibraryExpanded = run.LibraryExpanded; // 扩容跟随整次登塔(一局一次),结算随快照清除
            snapshot.PoolExpanded = run.PoolExpanded;
            // 段末:普通护盾重置为金汤(下一段段首注入;段内每关另补,见 RunEngine),堡型跨段保留
            snapshot.NormalShield = PerkRules.ShieldBonus(_meta);
            snapshot.PersistShield = run.CarriedPersistShield;
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

        /// <summary>安全层(20.5):继续深入 or 收官撤退的主动抉择。
        /// 塔内休整(段间调整字库)已废止(2026-07-20 拍板):字库只在登塔前定,塔内靠拆合与战利品经营。</summary>
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
            Ui.Anchor((RectTransform)card.transform, new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 10);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, $"安全层 · 第 {depth} 层告捷", 28, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                $"段位「{EndlessRules.RankTitle(_meta.BestDepth)}」 · 最高第 {_meta.BestDepth} 层", 16, Theme.TextDim);
            Ui.IngotLabel(stack.transform, $"滚存 {totalEarned}", 18);
            Ui.ThemedLabel(stack.transform,
                "继续:滚存收益带入更深层,阵亡墨锭减半;撤退:立即全额结算墨锭", 14, Theme.TextDim);
            Ui.PillButton(stack.transform, $"深入「{nextBand.Name}」第 {depth + 1}~{depth + endless.BossEvery} 层",
                () => StartSegment(firstTower: false), Theme.Cinnabar, Color.white, 19, new Vector2(340, 52));
            Ui.PillButton(stack.transform, "收官撤退(全额结算)",
                () => SettleTower(died: false, depth, totalEarned), Theme.InkSoft, Color.white, 19, new Vector2(340, 52));
        }

        /// <summary>塔结算(20.5):墨锭撤退全额/阵亡与弃塔半额;宝箱一场一个,按本次最高
        /// Boss 层档位发(2026-07-22 拍板,原「每 Boss 层即发」废止),阵亡照发不降档,
        /// 一个 Boss 都没破则无箱。结算弹窗与墨锭一并呈现。</summary>
        private static void SettleTower(bool died, int clearedDepth, int totalEarned, bool abandoned = false)
        {
            int chestDepth = EndlessRules.SettleChestDepth(_meta.Endless?.TopBossDepth ?? 0);
            _meta.Endless = null;
            EndlessRules.UpdateBest(_meta, clearedDepth);
            int ink = EndlessRules.SettleInk(totalEarned, died);
            _meta.Ink += ink;

            string chestNote = null;
            if (chestDepth > 0)
            {
                var tier = EndlessRules.ChestTierFor(chestDepth, new GameRandom(System.Environment.TickCount));
                if (ChestRules.TryAwardChest(_meta, tier, ChestCardPool(), Time))
                    chestNote = $"获得{ChestRules.TierName(tier)}(第 {chestDepth} 层 Boss 战利)";
                else
                {
                    _meta.PendingChests.Add(tier); // 满位不丢:暂存,回地图开箱腾位后自动入位
                    chestNote = $"{ChestRules.TierName(tier)}暂存——箱位已满,开掉一只即可领取";
                }
            }

            string headline = abandoned
                ? $"于第 {clearedDepth + 1} 层弃塔……墨锭 {ink}(半额)入账,纪录保留"
                : died
                    ? $"卒于第 {clearedDepth + 1} 层……墨锭 {ink}(半额)入账"
                    : $"第 {clearedDepth} 层收官!墨锭 {ink} 入账";
            MetaStore.Save(_meta);
            ShowTowerSettle(headline, ink, chestNote);
        }

        /// <summary>塔结算弹窗(2026-07-22):墨锭 + 宝箱一并呈现,确认后回地图。</summary>
        private static void ShowTowerSettle(string headline, int ink, string chestNote)
        {
            var view = NewView("TowerSettleView");
            Ui.Stretch((RectTransform)view.transform);
            var card = Ui.CardPanel(view.transform, "Panel");
            Ui.Anchor((RectTransform)card.transform, new Vector2(0.22f, 0.2f), new Vector2(0.78f, 0.8f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 14);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, "登 塔 结 算", 30, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform, headline, 17, Theme.TextDim);
            Ui.IngotLabel(stack.transform, ink.ToString(), 24);
            Ui.ThemedLabel(stack.transform,
                chestNote ?? "本次未破 Boss,无宝箱", 18,
                chestNote != null ? Theme.GoldBorder : Theme.TextDim, Theme.TitleFont);
            Ui.PillButton(stack.transform, "回到地图", () => ShowMap(), Theme.Cinnabar, Color.white, 20, new Vector2(280, 56));
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
