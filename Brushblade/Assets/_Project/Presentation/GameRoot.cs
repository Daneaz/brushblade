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
        private static int _committedEventInk; // 本段已即时结进账户的字摊/奇遇净额;防重复入账(2026-07-24)
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

            // 初始收集保底 = 五系各白/绿/蓝一张(2026-08-05 拍板;缺哪张补哪张,唯一来源见 MetaRules)
            foreach (var card in MetaRules.StartingCollection)
                if (!_meta.OwnedCards.Contains(card))
                    MetaRules.AcquireCard(_meta, card);
            // 出阵不足下限 → 播默认五系蓝档(补齐已废止,空出阵 = 空手登塔)
            if (_meta.Deck.Count < MetaRules.DeckMinimum)
                MetaRules.TrySetDeck(_meta, MetaRules.StartingDeck, _graph);

            ShowMap();
        }

        /// <summary>把本段打赢的敌人同步进图鉴(RecordDefeat 自身幂等,可重复调用)。</summary>
        private static void SyncBestiary(RunEngine run)
        {
            foreach (var id in run.DefeatedEnemyIds)
                BestiaryRules.RecordDefeat(_meta, id);
        }

        /// <summary>段中断点的取样器:StartSegment 期间有效,段末/结算时置空。</summary>
        private static System.Func<InProgressRun> _captureProgress;

        /// <summary>保底落盘:切后台、挂起离塔、以及每次玩家行动后都走这里(2026-07-27)。
        /// 段中时连战斗内进度一起写 —— 挂起/杀进程都能原样接着打。</summary>
        public static void SaveNow()
        {
            if (_meta == null) return;
            if (_captureProgress != null && _meta.EndlessV2 != null)
                _meta.EndlessV2.InProgress = _captureProgress();
            MetaStore.Save(_meta);
        }

        /// <summary>离开段中(段末告捷/阵亡/弃塔):断点作废,否则下次登塔会从旧段中间开始。</summary>
        private static void ClearProgress()
        {
            _captureProgress = null;
            if (_meta.EndlessV2 != null) _meta.EndlessV2.InProgress = null;
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
            var snapshot = _meta.EndlessV2;
            bool firstTower = _meta.BestDepth == 0 && snapshot == null;
            if (snapshot == null)
            {
                _meta.EndlessV2 = new EndlessSaveState
                {
                    Depth = 1,
                    // 满血登塔:与战斗配置的 PlayerMaxHp 同一个函数(此前两处各抄一遍表达式)
                    PlayerHp = MetaRules.PlayerMaxHpFor(_meta),
                    Seed = System.Environment.TickCount,
                    Library = new System.Collections.Generic.List<string>(MetaRules.StartingLibrary(_meta)),
                    // 初始部件从出阵表所需部件里随机(2026-08-05):拿到的部件必拼得出手里的字。
                    // 教程不靠这个池——拆演示字本身就产出它的两个部件。
                    Pool = new System.Collections.Generic.List<string>(MetaRules.RollStartingPool(
                        _meta.Deck, _graph, new GameRandom(System.Environment.TickCount))),
                    NormalShield = PerkRules.ShieldBonus(_meta), // 金汤:首段段首护盾
                };
                MetaStore.Save(_meta);
            }
            StartSegment(firstTower);
        }

        private static void StartSegment(bool firstTower)
        {
            var endless = _campaign.Endless;
            var snapshot = _meta.EndlessV2;
            // 段中断点优先:它自带段起点与首塔标记,Depth 会随层清算前进,不能拿来重建本段
            var resume = snapshot.InProgress;
            _committedEventInk = resume?.CommittedEventInk ?? 0; // 不清零会把字摊净额重复入账
            int fromDepth = resume?.FromDepth ?? snapshot.Depth;
            var band = endless.BandFor(fromDepth);
            int segmentEnd = (fromDepth - 1) / endless.BossEvery * endless.BossEvery + endless.BossEvery;

            // 层段首破里程碑(20.3):层段边界都是段首,踏入即发,断点重入不重复
            if (EndlessRules.TryAwardMilestone(_meta, band))
                MetaStore.Save(_meta);

            bool firstTowerSegment = resume?.FirstTowerSegment ?? (firstTower && fromDepth <= 1);
            var runConfig = firstTowerSegment
                ? EndlessGenerator.BuildFirstTowerSegment(endless, snapshot.Seed, _campaign.Events, _campaign.EventChancePercent)
                : EndlessGenerator.BuildSegment(endless, fromDepth, snapshot.Seed, _campaign.Events, _campaign.EventChancePercent);
            // 战利品的字只出自出阵列表(2026-07-20 拍板):补的是自己带上来的弹药,
            // 抽取按稀有度加权(绿 80/蓝 15/紫 5,见 RunEngine.RewardRarityWeights)
            runConfig.RewardPool = _meta.Deck;

            // ⚠ 角色属性一条都不在这里手写(2026-08-12,E-b4/E-b5 T7):Presentation 没有任何
            // 自动化测试,此处漏注入一条属性是**静默**的(实测删掉 PlayerDodge 那行,967 条
            // 测试全绿、零编译错)。整段映射下沉进 MetaRules.BuildBattleConfig,由
            // MetaRulesBattleConfigTests 逐条盯着 —— 新属性加在那边,不要加回这里。
            var battleConfig = MetaRules.BuildBattleConfig(_meta, _campaign.DropTable);
            int maxHp = battleConfig.PlayerMaxHp;
            RunEngine run = null;
            if (resume != null)
            {
                try
                {
                    run = RunEngine.Restore(resume.Run, _graph, runConfig, battleConfig, _meta.CardLevels,
                        startingInk: _meta.Ink, perFloorNormalShield: PerkRules.ShieldBonus(_meta));
                }
                catch (System.InvalidOperationException)
                {
                    // 内容更新在 enemyPool 里加怪(如灯花入池,2026-08-06 C3)会让同种子的层段
                    // 遭遇生成结果整体位移——旧的战斗中断点存档按旧顺序记录的字怪,复原时用新版本
                    // 重放同一颗种子就对不上号,BattleEngine.Restore 会抛 InvalidOperationException。
                    // 任何往 enemyPool 加怪的内容更新都会重演这条,所以这个兜底是长期需要的,
                    // 不是一次性补丁:退化成从本层重开(resume = null 落入下面的 new RunEngine 分支),
                    // 只损失当场战斗进度——段内已结算的楼层不受影响,下面用的是最近一次楼层结算
                    // 后写回 snapshot 的值。
                    resume = null;
                }
            }
            run ??= new RunEngine(_graph, runConfig, battleConfig,
                snapshot.Library, snapshot.Pool,
                seed: unchecked(snapshot.Seed * 17 + fromDepth), cardLevels: _meta.CardLevels,
                startingInk: _meta.Ink, // 字摊/赌博预算 = 账户库存(Option A,2026-07-24):
                // 塔内爬塔滚存不进预算,字摊消费即时结进账户、不随阵亡减半,滚存只留纯爬塔
                startingHp: snapshot.PlayerHp,
                startingNormalShield: snapshot.NormalShield,
                startingPersistShield: snapshot.PersistShield,
                perFloorNormalShield: PerkRules.ShieldBonus(_meta), // 金汤:每关开战补盾(段首由 NormalShield 注入)
                startingSummons: snapshot.CarriedSummons, // 召唤物跨段延续(2026-08-03),与普通盾同口径
                startingStatuses: snapshot.CarriedStatuses); // 减伤跨段延续(2026-08-04),同上
            if (resume == null) // 从断点恢复时这些已在 run 状态里,再调一次会把容量重复抬高
            {
                if (snapshot.LibraryExpanded) run.TryExpandLibrary(); // 断点恢复段内广告扩容
                if (snapshot.PoolExpanded) run.TryExpandPool();
                if (snapshot.Revived) run.MarkRevived();              // 防重进本层二次复活(2026-07-24)
            }

            var tutorial = firstTowerSegment && resume == null ? new Tutorial() : null;
            int baseInk = snapshot.EarnedInk; // 段前滚存

            // 段中断点的取样器:SaveNow 每次都据此把战斗内进度一并写盘
            _captureProgress = () => new InProgressRun
            {
                FromDepth = fromDepth,
                FirstTowerSegment = firstTowerSegment,
                CommittedEventInk = _committedEventInk,
                Run = run.Capture(),
            };

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
                onProgress: SaveNow, // 每次玩家行动后落盘:挂起/闪退都能接着打(2026-07-27)
                onAbandon: () => // 弃塔=阵亡待遇:爬塔半额结算,防绕过安全层撤退决策;字摊净额已即时入账
                {
                    CommitEventInk(run);
                    ClearProgress(); // 弃塔:断点作废
                    // 已清最深层同样看 ClearedBattleIndex:在战利品/奇遇页弃塔时
                    // BattleIndex 还停在刚打完那层,用它减一会把这层的纪录抹掉
                    SettleTower(died: true, fromDepth + run.ClearedBattleIndex, baseInk, abandoned: true);
                });
        }

        /// <summary>广告扩容即时落盘:挂起/杀进程也不丢已看广告换来的容量。
        /// 走 SaveNow 而不是裸 MetaStore.Save(2026-08-18):后者只更新塔级标志,段中断点
        /// (InProgress.Run)还是扩容前取的样 —— 此刻被杀,恢复走 resume 分支会按旧快照把容量
        /// 退回去,且塔级标志随后被 WriteCarriedSnapshot 反向抹掉,战利品页就又按旧上限拒收。</summary>
        private static void OnExpanded(RunEngine run)
        {
            var snapshot = _meta.EndlessV2;
            if (snapshot == null) return;
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            snapshot.Revived = run.Revived; // 复活跟随整次登塔(一次性),结算随快照清除
            SaveNow();
        }

        /// <summary>字摊/赌博净额即时结进账户(Option A,2026-07-24):按本段累计净额与已结额的差值入账,
        /// 全额不减半、结构上不可能让账户变负(RunEngine 已卡消费不超预算)。每个存档点调用,断点续爬不丢也不重复。</summary>
        private static void CommitEventInk(RunEngine run)
        {
            int delta = run.EarnedInk - _committedEventInk;
            if (delta != 0) _meta.Ink += delta;
            _committedEventInk = run.EarnedInk;
        }

        /// <summary>本层战利品取完:立即记账落盘(2026-07-20 拍板)——此前要等下一层开打才写快照,
        /// 在战利品/奇遇页挂起会丢掉本层收益。段末(RunWon)交给 OnSegmentEnded 统一结算。</summary>
        /// <returns>累加本层墨锭后的段前滚存(调用方回写,供后续层与结算使用)。</returns>
        private static int OnFloorCleared(RunEngine run, int fromDepth, int baseInk)
        {
            var snapshot = _meta.EndlessV2;
            if (snapshot == null || run.Phase == RunPhase.RunWon) return baseInk;

            SyncBestiary(run);
            // 基准用 ClearedBattleIndex 而非 BattleIndex:本回调在「离开战利品阶段」时触发,
            // 那一刻若已开下一战,BattleIndex 早跳到下一层了 —— 用它会多记一层,
            // 快照 Depth 越过 Boss 层,挂起再进就把整段白送(2026-07-27 修)
            int cleared = fromDepth + run.ClearedBattleIndex; // 刚打完的层
            if (snapshot.Depth <= cleared)             // 幂等:同一层只记一次账
            {
                _meta.CharacterXp += EndlessRules.XpFor(_campaign.Endless, cleared);
                // 爬塔层墨锭进塔内滚存而非账户(2026-07-21):塔结算时才入账,阵亡随 SettleInk 减半
                baseInk += EndlessRules.FloorInk(_campaign.Endless, cleared);
                snapshot.Depth = cleared + 1;          // 推进后挂起不会重打本层(也就刷不出重复战利品)
            }
            CommitEventInk(run); // 字摊/赌博净额即时结进账户,与爬塔滚存分离
            WriteCarriedSnapshot(run, snapshot, baseInk);
            MetaStore.Save(_meta);
            return baseInk;
        }

        /// <summary>新一层开打:刷新携带态(奇遇结果与新回合掉落)。
        /// 层经验与 Depth 推进已在 OnFloorCleared 记过账,这里不再重复。</summary>
        private static void OnFloorAdvanced(RunEngine run, int baseInk)
        {
            var snapshot = _meta.EndlessV2;
            if (snapshot == null) return;
            CommitEventInk(run); // 字摊/赌博净额即时结进账户,与爬塔滚存分离
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.Battle.Library);
            snapshot.Pool = new System.Collections.Generic.List<string>(run.Battle.Pool);
            snapshot.EarnedInk = baseInk; // 滚存只留纯爬塔累加(字摊已即时入账)
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            snapshot.Revived = run.Revived; // 复活跟随整次登塔(一次性),结算随快照清除
            snapshot.CarriedSummons = new System.Collections.Generic.List<SummonSnapshot>(run.CarriedSummons);
            MetaStore.Save(_meta);
        }

        /// <summary>写入战斗之间的携带态(战利品已并入其中)。字摊净额的提交由调用方负责。</summary>
        private static void WriteCarriedSnapshot(RunEngine run, EndlessSaveState snapshot, int baseInk)
        {
            snapshot.PlayerHp = run.Battle.PlayerHp;
            snapshot.Library = new System.Collections.Generic.List<string>(run.CarriedLibrary);
            snapshot.Pool = new System.Collections.Generic.List<string>(run.CarriedPool);
            snapshot.EarnedInk = baseInk; // 滚存只留纯爬塔累加(字摊已即时入账)
            snapshot.LibraryExpanded = run.LibraryExpanded;
            snapshot.PoolExpanded = run.PoolExpanded;
            snapshot.Revived = run.Revived; // 复活跟随整次登塔(一次性),结算随快照清除
            snapshot.NormalShield = run.CarriedNormalShield;
            snapshot.PersistShield = run.CarriedPersistShield;
            snapshot.CarriedSummons = new System.Collections.Generic.List<SummonSnapshot>(run.CarriedSummons);
            snapshot.CarriedStatuses = new System.Collections.Generic.List<StatusEffect>(run.CarriedStatuses);
        }

        private static void OnSegmentEnded(RunEngine run, int fromDepth, int segmentEnd, int baseInk, bool won)
        {
            var endless = _campaign.Endless;
            CommitEventInk(run);       // 字摊/赌博净额即时结进账户,不进滚存、不随阵亡减半
            ClearProgress();           // 本段已了结,断点作废
            int totalEarned = baseInk; // 滚存 = 纯爬塔累加
            if (!won)
            {
                int clearedDepth = fromDepth + run.ClearedBattleIndex; // 同上:已清最深层
                SettleTower(died: true, clearedDepth, totalEarned);
                return;
            }

            // Boss 层告捷:经验 + 纪录;宝箱改为整次爬塔结算时按最高 Boss 层发一个(2026-07-22),
            // 此处只记录已破的最高 Boss 层
            SyncBestiary(run); // Boss 层走 RunWon,不经过 OnFloorCleared
            _meta.CharacterXp += EndlessRules.XpFor(endless, segmentEnd);
            totalEarned += EndlessRules.FloorInk(endless, segmentEnd); // Boss 层墨锭(层清算走 OnFloorCleared,段末不经手)
            EndlessRules.UpdateBest(_meta, segmentEnd);
            var snapshot = _meta.EndlessV2;
            snapshot.TopBossDepth = segmentEnd; // 逐段递增,即本次已破最高 Boss 层
            snapshot.Depth = segmentEnd + 1;
            snapshot.PlayerHp = run.Battle.PlayerHp;
            // 用携带态而非 Battle:Boss 层战利品(2026-07-20)加在携带态上,读 Battle 会把它丢掉
            snapshot.Library = new System.Collections.Generic.List<string>(run.CarriedLibrary); // 出字即消耗,无回归(v0.7)
            snapshot.Pool = new System.Collections.Generic.List<string>(run.CarriedPool);
            snapshot.EarnedInk = totalEarned;
            snapshot.LibraryExpanded = run.LibraryExpanded; // 扩容跟随整次登塔(一局一次),结算随快照清除
            snapshot.PoolExpanded = run.PoolExpanded;
            snapshot.Revived = run.Revived; // 复活跟随整次登塔(一次性),结算随快照清除
            // 段末护盾照常延续(2026-07-26 拍板:盾叠加本场爬塔通吃,不再 5 关一清),
            // 与挂起快照同口径;金汤每关另补,见 RunEngine
            snapshot.NormalShield = run.CarriedNormalShield;
            snapshot.PersistShield = run.CarriedPersistShield;
            snapshot.CarriedSummons = new System.Collections.Generic.List<SummonSnapshot>(run.CarriedSummons);
            snapshot.CarriedStatuses = new System.Collections.Generic.List<StatusEffect>(run.CarriedStatuses);
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
            int chestDepth = EndlessRules.SettleChestDepth(_meta.EndlessV2?.TopBossDepth ?? 0);
            _meta.EndlessV2 = null;
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
