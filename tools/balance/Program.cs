using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Balance
{
    /// <summary>无尽难度仿真(20.4 校准):贪心机器人永不撤退一路深入,量「卒于第几层」分布。
    /// 机器人弱于人类,数据当难度地板读;关卡制已废止(v0.7),旧关卡口径删除。</summary>
    public static class Program
    {
        private const int Seeds = 300;
        private const int StallTurns = 60;
        private const int DepthCap = 300;

        /// <summary>画像统一的养成档:卡 5 级、角色 10 级(沿用 2026-09 系列画像的中段养成口径)。</summary>
        private const int CardLevel = 5;
        private const int CharacterLevel = 10;

        /// <summary>起手抽卡的五行顺序,与 <c>MetaRules.StartingElements</c> 一致。</summary>
        private static readonly Element[] Elements =
            { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth };

        private static readonly Dictionary<Element, string> ElementName = new()
        {
            [Element.Metal] = "金", [Element.Wood] = "木", [Element.Water] = "水",
            [Element.Fire] = "火", [Element.Earth] = "土",
        };

        /// <summary>某系全部**可出牌字**,从 chars.json 现读(2026-09-17 画像重做)。
        ///
        /// ⚠ 为什么不再写死字名表:旧的 FireCards/WaterCards/… 每次字表调整都要手改,
        /// 漏改就成幽灵字 —— 「灯」(2026-08-12)、「燃」(2026-08-25)两次都是这样当了半个月空观测点,
        /// 而且写死的表只能在当前字表上跑,拿不到改动前的基线做对比。
        /// 判据与 CharTableTests 的「可出牌字」同口径:有效果即是字,部件没有效果。</summary>
        private static List<string> CardsOf(RecipeGraph graph, params Element[] elements) =>
            graph.All.Where(d => d.Effects.Count > 0 && !d.IsComponent && d.Element is { } el && elements.Contains(el))
                .Select(d => d.Id).ToList();

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            // 画像三组(2026-09-17 用户拍板重做,取代此前手挑起手字的 16 档):
            //   1. 单系 ×5 —— 基线,不是核心评估;
            //   2. 木 + 其余四系 ×4 —— 木系是字表里唯一的召唤(前排拦截)来源,
            //      这一组量「别的系配上前排」各自能到哪;
            //   3. 五系混合 —— 核心评估点,起手照搬游戏真实逻辑(MetaRules.StartingLibrary)。
            // 三组的起手都是 6 张、每个种子各抽一次,牌数与稀有度分布一致,读数可以横向比。
            var profiles = new List<Profile>();
            foreach (var e in Elements)
                profiles.Add(new Profile($"单系·{ElementName[e]}", CardsOf(graph, e),
                    Enumerable.Repeat(e, 5).Select(x => new[] { x }).ToArray()));
            foreach (var e in Elements.Where(x => x != Element.Wood))
                profiles.Add(new Profile($"木混·木{ElementName[e]}", CardsOf(graph, Element.Wood, e),
                    new[] { new[] { Element.Wood }, new[] { e }, new[] { Element.Wood }, new[] { e },
                            new[] { Element.Wood, e } }));
            profiles.Add(new Profile("五系随机(核心)", CardsOf(graph, Elements), slots: null));

            Console.WriteLine($"scalePerDepth={endless.ScalePerDepth} bossBonus={endless.BossScaleBonus} × {Seeds} 种子"
                + $" · 卡 {CardLevel} 级 · 角色 {CharacterLevel} 级 · 起手 6 张随机\n");
            // 末两列是**机器人自检**,不是平衡指标(见 BotProbe):攻面出字恒 0 = 双方向字
            // 的攻面又断了;僵局判死高企 = 机器人打不死人、靠 60 回合上限判死收场。
            Console.WriteLine("| 画像 | 卡池 | 均卒层 | P50 | P90 | 最深 | 达词渊(11) | 达文山(26) | 达墨海(51) | 攻面出字/局 | 召唤出字/局 | 僵局判死/300 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var profile in profiles)
                SimulateProfile(graph, campaign, endless, profile);
        }

        private sealed class Profile
        {
            public string Name;
            /// <summary>已解锁卡池 = BattleConfig.UnlockedChars = 战后奖励池:回合掉字的抽取源,
            /// 同时锁死合成目标(2026-07-20)。</summary>
            public IReadOnlyList<string> OwnedCards;
            public Dictionary<string, int> CardLevels;
            /// <summary>起手前 5 张每张从哪几系里加权抽;第 6 张固定从卡池最高档保底。
            /// null = 五系混合,直接走 <c>MetaRules.StartingLibrary</c>(游戏真实逻辑)。</summary>
            public Element[][] Slots;
            public int MaxHp;
            public int Attack;
            public int Defense;
            public int Dodge;
            public Profile(string name, IReadOnlyList<string> ownedCards, Element[][] slots)
            {
                Name = name; OwnedCards = ownedCards; Slots = slots;
                CardLevels = ownedCards.ToDictionary(c => c, _ => CardLevel);
                MaxHp = MetaRules.MaxHpFor(CharacterLevel);
                Attack = MetaRules.AttackFor(CharacterLevel);
                Defense = MetaRules.DefenseFor(CharacterLevel);
                Dodge = MetaRules.DodgeFor(CharacterLevel);
            }
        }

        /// <summary>起手 6 张(2026-09-17)。五系混合调游戏本体的 <c>MetaRules.StartingLibrary</c>;
        /// 单系/木混没有「五系各一」可言,改为按 <see cref="Profile.Slots"/> 逐格加权抽
        /// (<c>MetaRules.DrawWeighted</c>,同一张 RarityWeights),第 6 张同样从最高档保底 ——
        /// 与真实逻辑只差「每格限定哪几系」这一处。全程不去重,同真实逻辑。</summary>
        private static List<string> DrawStartingLibrary(RecipeGraph graph, Profile profile, GameRandom random)
        {
            if (profile.Slots == null)
                return MetaRules.StartingLibrary(new MetaState { OwnedCards = profile.OwnedCards.ToList() },
                    graph, random).ToList();

            var library = new List<string>();
            foreach (var slot in profile.Slots)
            {
                var pick = MetaRules.DrawWeighted(
                    profile.OwnedCards.Where(id => graph.Get(id).Element is { } el && slot.Contains(el)).ToList(), graph, random);
                if (pick != null) library.Add(pick);
            }
            var top = profile.OwnedCards.GroupBy(id => graph.Get(id).Rarity)
                .OrderByDescending(g => g.Key).First().ToList();
            library.Add(top[random.Next(top.Count)]);
            return library;
        }

        /// <summary>机器人自身行为的探针(2026-09-08,P3 attackMode 缺口)。
        ///
        /// ⚠ 为什么非要它:2026-09-07 之前这份仿真的机器人从未以 attackMode=true 调用过
        /// <c>BattleEngine.Cast</c>,双方向字(水系 11 张全是、土系 8/12 张是)的攻面
        /// **一次都没被执行过**——而读数看上去只是「水系偏弱」,谁也看不出机器人握着半套
        /// 机制没用。<see cref="AttackFaceCasts"/> 就是这件事的分母:它为 0 就说明攻面
        /// 又断了,不必再靠猜某档读数「是不是有点低」。
        /// <see cref="Stalls"/> 同理——「60 回合分不出胜负」和「被打死」在卒层数字里长得
        /// 一模一样,只有把僵局单独数出来,才看得见「只会奶不会打」这种残废打法。</summary>
        private sealed class BotProbe
        {
            public int AttackFaceCasts; // 以 attackMode=true 出字的次数(全部种子累计)
            public int Stalls;          // 以「僵局判死」告终的局数(分母 = Seeds)
            // 出召唤字的次数(2026-09-17):土系交出召唤位后,前排只剩木系能给 ——
            // 这一列让「这档到底有没有前排」直接可读,不用从卡池反推。
            public int SummonCasts;
        }

        private static void SimulateProfile(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile)
        {
            var deaths = new List<int>();
            var probe = new BotProbe();
            foreach (int seed in Enumerable.Range(0, Seeds))
                deaths.Add(ClimbUntilDeath(graph, campaign, endless, profile, seed, probe));

            deaths.Sort();
            double avg = deaths.Average();
            int p50 = deaths[deaths.Count / 2];
            int p90 = deaths[(int)(deaths.Count * 0.9)];
            string Reach(int band) => $"{deaths.Count(d => d >= band) * 100 / deaths.Count}%";
            Console.WriteLine($"| {profile.Name} | {profile.OwnedCards.Count} | {avg:F1} | {p50} | {p90} | {deaths[^1]} " +
                              $"| {Reach(11)} | {Reach(26)} | {Reach(51)} " +
                              $"| {probe.AttackFaceCasts / (double)Seeds:F1} " +
                              $"| {probe.SummonCasts / (double)Seeds:F1} " +
                              $"| {probe.Stalls} |");
        }

        /// <summary>一路深入直到阵亡,返回卒层(= 阵亡所在层)。</summary>
        private static int ClimbUntilDeath(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile, int seed, BotProbe probe)
        {
            int towerSeed = seed * 7919 + 17;
            int fromDepth = 1;
            // 起手字库与部件池都按种子现抽(与塔种子错开,不共用随机流)
            var deckRandom = new GameRandom(unchecked(seed * 104729 + 3));
            IReadOnlyList<string> library = DrawStartingLibrary(graph, profile, deckRandom);
            IReadOnlyList<string> pool = MetaRules.RollStartingPool(library, graph, deckRandom);
            int hp = profile.MaxHp;

            while (fromDepth <= DepthCap)
            {
                var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                    campaign.Events, campaign.EventChancePercent);
                // 2026-09-07 补:生产侧 GameRoot.StartSegment 自 2026-07-20 起就无条件覆盖这一项
                // (层段写死的那份从来没生效过,见 Endless.cs 的 BandDef.RewardPool 注释)。
                // 此前工装漏了这行 —— RewardPool 恒为空池,于是 RollRewardOptions 遍历空表、
                // _rewardOptions 恒空、PickBestReward 那段循环一次都没跑过,
                // 所有历史读数都不含「战后 5 选 2」这条成长路径。
                runConfig.RewardPool = profile.OwnedCards;
                // UnlockedChars(2026-08-04 起也是回合掉字的抽取源,见 BattleEngine.StartTurn)。
                // 生产侧口径是 _meta.OwnedCards——玩家已解锁的整个卡池(2026-09-06 出阵废止后
                // MetaRules.BuildBattleConfig 直接读它),画像的卡池就是它。
                // 注意:UnlockedChars 非空时 ForgeEngine 也会用它锁合成目标(2026-07-20 拍板)。
                var battleConfig = new BattleConfig
                {
                    DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp,
                    PlayerAttack = profile.Attack,
                    PlayerDefense = profile.Defense, PlayerDodge = profile.Dodge,
                    UnlockedChars = profile.OwnedCards,
                };
                var run = new RunEngine(graph, runConfig, battleConfig, library, pool,
                    seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: profile.CardLevels,
                    startingHp: hp);

                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward || run.Phase == RunPhase.Event)
                {
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }

                    var battle = run.Battle;
                    int turns = 0;
                    while (turns <= StallTurns)
                    {
                        if (battle.Phase == BattlePhase.DropChoice) { ResolveDropChoice(graph, battle); continue; }
                        if (battle.Phase != BattlePhase.PlayerTurn) break;
                        turns++;
                        PlayTurn(graph, battle, probe);
                    }
                    if (turns > StallTurns)
                    {
                        probe.Stalls++;
                        return fromDepth + run.BattleIndex; // 僵局计为卒于当前层
                    }
                    run.AdvanceAfterBattle();
                }

                if (run.Phase != RunPhase.RunWon)
                    return fromDepth + run.BattleIndex;

                // 安全层:永不撤退,携带状态深入下一段(同 GameRoot.OnSegmentEnded;出字即消耗无回归 v0.7)
                library = new List<string>(run.Battle.Library);
                pool = new List<string>(run.Battle.Pool);
                hp = run.Battle.PlayerHp;
                fromDepth += endless.BossEvery;
            }
            return DepthCap;
        }

        // ---- 贪心机器人(与关卡制版同策略) ----

        private static void PlayTurn(RecipeGraph graph, BattleEngine battle, BotProbe probe)
        {
            while (battle.Ap >= 2)
            {
                var suggest = ForgeEngine.Suggest(graph, battle.Pool, battle.Library);
                string best = null;
                int bestPower = BestCastablePower(graph, battle);
                foreach (var id in suggest.Composable)
                {
                    int power = CardValue(graph, id);
                    if (power > bestPower) { bestPower = power; best = id; }
                }
                if (best == null) break;

                if (battle.Compose(best) == BattleError.ForgeFailed)
                {
                    var weakest = battle.Library.OrderBy(id => CardValue(graph, id)).FirstOrDefault();
                    if (weakest == null || battle.Discard(weakest) != BattleError.None) break;
                    if (battle.Compose(best) != BattleError.None) break;
                }
            }

            while (battle.Phase == BattlePhase.PlayerTurn && battle.Ap > 0)
            {
                // ---- 双方向字的选面规则(2026-09-08,P3;design §10.5)----
                // 水/土 的双方向字有两面:护面(CharDef.Effects,治疗/护盾,挂自己)和
                // 攻面(CharDef.AttackEffects,伤害/控制,打敌人)。真人玩家拖给自己出护面、
                // 拖给敌人出攻面;此前这个机器人**只有护面这一条路**(从不给 Cast 传
                // attackMode),于是水系 11 张双方向字整场只会奶,60 回合僵局判死 259/300。
                //
                // 规则整句如下,只此一条,每次出字前重新判一次:
                //
                //   **血量还在半血以上 → 双方向字出攻面(打输出);
                //     血量掉到半血或以下 → 双方向字出护面(治疗/护盾),
                //     把血救回半血以上,下一次出字自然又切回攻面。**
                //
                // 刻意不写成「两面各打分、取分高的那面」:那种规则的读数没人解释得清,
                // 权重一动全盘变,也没人维护得动。半血这条线是真人的打法(血线安全就输出、
                // 告急就补),读数变了立刻知道是哪一条在动。
                // 只有一面的字(纯攻击字/纯护盾字/召唤字)不受影响 —— 它们 AttackEffects 为空,
                // Power/EffectsOf 都会退回唯一的那一面。
                // 2026-09-17(用户拍板):补一条「身上已有盾 → 出攻面」。土系护面只加盾不回血,
                // 原规则下一掉到半血就永远只出盾不出手(土系画像僵局判死 187/300),量的是机器人
                // 自缚而不是土系强弱。水系护面回血会把血线拉回来,这一条对它基本不触发。
                bool preferAttackFace = battle.PlayerHp * 2 > battle.MaxHp || battle.PlayerShield > 0;

                string pick = null;
                int pickPower = -1;
                bool pickAttackFace = false;
                foreach (var id in battle.Library.Concat(battle.Pool.Where(p => IsCastableLeaf(graph, p, battle))))
                {
                    if (!graph.TryGet(id, out var def) || def.ApCost > battle.Ap) continue;
                    // 先按上面那条规则定面,再用**这一面**的分去排序:用另一面的分排序会造出
                    // 「按攻面挑的牌、按护面结算」的错位(brief 点名的那类静默 bug)。
                    bool attackFace = preferAttackFace && def.AttackEffects.Count > 0;
                    int power = Power(graph, id, attackFace);
                    if (power > pickPower) { pickPower = power; pick = id; pickAttackFace = attackFace; }
                }
                if (pick == null) break;

                graph.TryGet(pick, out var pickDef);
                // ⚠ NeedsTarget 必须跟着传同一个 attackMode:护面通常不需要选敌人、攻面需要,
                // 传错就会「想打敌人却按护面判定成不用选目标」,targetIndex 停在 −1,
                // ApplyEffects 里 ExpandTargets 返回空表 —— 静默打空(BattleEngine.cs:1382 那条注释)。
                int target = BattleEngine.NeedsTarget(pickDef, pickAttackFace) ? PickTarget(battle) : -1;
                if (battle.Cast(pick, target, attackMode: pickAttackFace) != BattleError.None) break;
                if (pickAttackFace) probe.AttackFaceCasts++;
                // 取面口径同 Power():有攻面且选了攻面才读 AttackEffects
                var castEffects = pickAttackFace && pickDef.AttackEffects.Count > 0 ? pickDef.AttackEffects : pickDef.Effects;
                if (castEffects.Any(e => e.Kind == EffectKind.Summon))
                    probe.SummonCasts++;
            }

            if (battle.Phase == BattlePhase.PlayerTurn)
                battle.EndTurn();
        }

        /// <summary>回合掉字撞满库时的决议策略:掉的字强于库中最弱则换入(ResolveDrop),
        /// 否则跳过(SkipDrop)——与 PickBestReward 的换入判定同一套贪心口径,保持机器人在
        /// 「战利品换入」「掉落换入」两条注入路径上的策略一致(评审建议)。</summary>
        private static void ResolveDropChoice(RecipeGraph graph, BattleEngine battle)
        {
            int droppedPower = CardValue(graph, battle.PendingDrop);
            int weakest = 0, weakestPower = int.MaxValue;
            for (int i = 0; i < battle.Library.Count; i++)
            {
                int power = CardValue(graph, battle.Library[i]);
                if (power < weakestPower) { weakestPower = power; weakest = i; }
            }
            if (droppedPower > weakestPower)
                battle.ResolveDrop(weakest);
            else
                battle.SkipDrop();
        }

        private static bool IsCastableLeaf(RecipeGraph graph, string id, BattleEngine battle) =>
            graph.TryGet(id, out var def) && def.IsLeaf && !battle.Library.Contains(id);

        private static int BestCastablePower(RecipeGraph graph, BattleEngine battle)
        {
            int best = 0;
            foreach (var id in battle.Library)
                best = Math.Max(best, CardValue(graph, id));
            return best;
        }

        /// <summary>这张字**在库里值多少** = 两面里更值钱的那一面(2026-09-08,P3)。
        /// 「值不值得合成 / 是不是库中最弱该弃掉 / 战利品换不换」这几处判定用它:玩家两面
        /// 都用得上,只按护面估值会让「攻面强、护面薄」的双方向字被当成库中最弱优先弃掉、
        /// 永远不去合成 —— 与 §2.8「0 分的字机器人永远不会去出/合成」同一个坑的另一面。
        ///
        /// ⚠ 出字时**不**用它选面:选哪一面由 <see cref="PlayTurn"/> 里那条血量规则决定
        /// (取分高的那面是打分调参,brief 明确不要)。这里的 Max 只用来给「留哪张字」排序。</summary>
        private static int CardValue(RecipeGraph graph, string id) =>
            Math.Max(Power(graph, id, attackMode: false), Power(graph, id, attackMode: true));

        /// <summary>一张字**某一面**的威力评分。
        /// attackMode=true 读 <c>CharDef.AttackEffects</c>(双方向字的攻面),该字没有攻面时
        /// 退回 <c>Effects</c> —— 与 <c>BattleEngine.EffectsOf</c> 逐字同口径。两边口径必须一致,
        /// 否则会出现「按攻面算的分、按护面结算的效果」。
        /// ⚠ 2026-09-08 之前这个函数只扫 <c>def.Effects</c>,attackMode 这一路根本不存在,
        /// AttackEffects 这个字段在整份仿真里从未被读过一次(交接文档 §0)。</summary>
        private static int Power(RecipeGraph graph, string id, bool attackMode = false)
        {
            if (!graph.TryGet(id, out var def)) return 0;
            var effects = attackMode && def.AttackEffects.Count > 0 ? def.AttackEffects : def.Effects;
            if (effects.Count == 0) return 3;
            int sum = 0;
            foreach (var e in effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.DamageSingle: sum += e.Value; break;
                    case EffectKind.DamageAll: sum += e.Value * 3 / 2; break;
                    case EffectKind.BurnSingle: sum += e.Value * 2; break;
                    case EffectKind.BurnAll: sum += e.Value * 3; break;
                    // 2026-09-02:护盾/治疗改记全额 —— 它们现在攒势/水势,
                    // 折半记分是「防御没有进攻价值」那个旧模型的残留。
                    case EffectKind.Shield: sum += e.Value; break;
                    case EffectKind.BurnPotency: sum += e.Value * 2; break;
                    case EffectKind.HealSelf: sum += e.Value; break;
                    case EffectKind.HealAll: sum += e.Value; break;
                    case EffectKind.Summon: sum += (e.Value + e.SummonAttack * 3) * e.SummonCount / 2; break;
                    // 穿透(2026-08-12,E-b4 T5,锐):按点数**等价折算成伤害**,不加权。
                    // 它本场持久、每次攻击都兑现,理应比一次性伤害值钱;但全表只有 墨渍(DEF 31)
                    // 与 3 个 Boss 阶段(30/30/60)有甲,对其余敌人它一分钱不值。等价折算是这两头
                    // 之间的保守中点 —— 排在 灼(60)之后,机器人先打伤害再攒穿透。
                    case EffectKind.PierceBuff: sum += e.Value; break;
                    // 护甲(2026-08-12,E-b4/E-b5 T7,土系堆甲探针):按点数 ×2 折算。
                    // ⚠ **系数是多少不重要,是不是 0 才重要**:记 0 分的字机器人永远不会去
                    // 合成它(Compose 那条分支要求 power 严格大于库里最强的),那与「没把它加进
                    // 卡池表」完全等价 —— 正是 焰 变异检查轨迹毫无反应踩过的坑。没有这一条,
                    // 土系堆甲探针就是个装饰品:它会握着一手防御字一张都不出。
                    // ×2 的口径同 BurnSingle:本场持久、每记挥击都兑现,但只在挨打时兑现,
                    // 所以排在同数值的直伤之后(铠 12 → 24 分,仍低于 碾 的 60)。
                    case EffectKind.DefenseBuff: sum += e.Value * 2; break;
                    // 厚/泉的终极技(2026-09-02;2026-09-04 由 SpendMomentum/SpendWaterPower 改名 ——
                    // 工装没跟着改,自那天起整个仿真编译不过、无人可跑,2026-09-05 发现)。
                    // 按满层折算:机器人不模拟攒层过程,给个中位估值让它至少会去出这张字。
                    // 系数是多少不重要,**是不是 0 才重要**。
                    case EffectKind.SpendHeft: sum += e.Value * 5; break;
                    case EffectKind.SpendWellspring: sum += e.Value * 5; break;
                    // 群体护盾(2026-09-05,崩):不记分的话 崩 的护面是 0 分,
                    // 土系画像会握着它一张都不出 —— 与「没加进卡池表」等价。
                    case EffectKind.ShieldAll: sum += e.Value; break;
                    // 魅惑(2026-09-05,花):按「一次敌人攻击转成对敌伤害」估值。
                    // 系数是多少不重要,**是不是 0 才重要** —— 记 0 分的字机器人永远不会出,
                    // 那与「没把它加进出阵表」完全等价(注释里那条踩过三次的坑)。
                    case EffectKind.Charm: sum += 60; break;

                    // ================== 2026-09-08(T5 头号任务)补齐控制类/增益类计分 ==================
                    // 在此之前,下面这些 kind 一律落到 switch 的默认分支、贡献 0 分——Power() 自己的
                    // 三条既有注释都点名过同一条规矩:「系数是多少不重要,是不是 0 才重要」。0 分的
                    // 字机器人永远不会去合成(Compose 要求 power 严格大于库存最强的)、永远优先被
                    // 当「库中最弱」弃掉(ResolveDropChoice/PickTarget 前那个弃字循环)。这里统一借用
                    // 已有的 Charm 案例反推一把换算尺:Charm 定价 0.40(design §2.2)记 60 分,
                    // 即 **150 分 / 1.0 价格单位**,下面每一条都用这同一把尺子换算 design §2.2/§1.4.1
                    // 的价目表,只求方向对、非零,不追求跟实付价目分毫不差。
                    //
                    // ⚠ 这批 case 全部只读 e.Value/e.Turns 这些**字段**,不读“这条效果具体挂在哪张
                    // 字身上”——所以在 2026-09-07 那天,它们对**双方向字的攻击面**(AttackEffects,
                    // 水系 11 张的 Silence/Freeze/Slow/ArmorBreak 全在这里)一点作用都没有:当时
                    // Power() 的 foreach 只扫 `def.Effects`,而机器人也从不以 attackMode=true 调用
                    // battle.Cast(交接文档 §0)。
                    // 2026-09-08(P3)两处一起补上了:本函数改成按 attackMode 选面扫,PlayTurn 按
                    // 血量规则真的会出攻面 —— 于是下面这批控制类计分从这天起才真正开始生效。

                    // 冻结(design §2.2「冻结 1 回合 0.35」)≈52 分/回合,按 Value(跳过的回合数)计。
                    case EffectKind.Freeze: sum += e.Value * 50; break;
                    // 减速(design §2.2「减速 2 回合 0.30」≈45 分对应 2 回合,即 ~22 分/回合)。
                    case EffectKind.Slow: sum += e.Value * 25; break;
                    // 破甲:与 DefenseBuff 同一条价目轴(design §2.2 两条都是 0.33),对称给
                    // 同样的 ×2 折算——护甲增/减本质是同一个点数机制的正负两面。
                    case EffectKind.ArmorBreak: sum += e.Value * 2; break;
                    // 免疫(design §2.2「免疫 1 次 0.35 · 2 次 0.60」),按挡住的次数计,
                    // 第 1 次≈52、第 2 次边际下降到≈38,这里不追求分段精确,按每次 50 折算。
                    case EffectKind.Immunity: sum += e.Value * 50; break;
                    // 复活(design §2.2「复活 1 名 0.40」)与 Charm 同一档价格,直接复用 60 分/名。
                    case EffectKind.Revive: sum += e.Value * 60; break;
                    // 致盲(design §2.2「致盲 0.30」,当前 0 载体——按 CritBuff 同款「每点命中率
                    // 换 3 分」估值,预防将来复活时又落回 0 分)。
                    case EffectKind.Blind: sum += e.Value * 3; break;
                    // 封禁(design §2.2「封禁 1 回合 / 2 回合 0.38 / 0.52」)。现存载体 Value 恒为 0
                    // (强度全在 Turns 上),按回合数分段估值:1 回合≈57、2 回合≈78。
                    case EffectKind.Silence: sum += e.Turns >= 2 ? 78 : 57; break;
                    // 反弹(design §2.2「反弹 50%×2 0.35」≈52.5,对应 圭 的 Value=50/Turns=2 那一档,
                    // 折算成 Value×Turns/2,壁 的 30%×2 按同公式给 30 分)。
                    case EffectKind.Reflect: sum += e.Value * Math.Max(1, e.Turns) / 2; break;
                    // 引爆:design §2.2 没有单独定价这一条(它是「灼烧威力」轴上的收网动作,
                    // 真正的伤害当量已经由同一张字前面铺的 BurnAll/BurnPotency 计过分)。
                    // 这里给一个适中的固定估值,只为避免「引爆」这个 kind 本身挂零分。
                    case EffectKind.Detonate: sum += 40; break;
                    // 增攻(design §2.2「攻击 +50 0.45」≈67.5/50 ≈ 1.35 分每点,取 1.5 就近折算)。
                    case EffectKind.Empower: sum += e.Value * 3 / 2; break;
                    // 战意(design §2.2「战意(印记外)0.10 每层」≈15 分/层,Value = 施放当下
                    // 立即获得的层数)。
                    case EffectKind.Morale: sum += e.Value * 15; break;
                    // 暴击(design §2.2「暴击 +20% 0.40」,20%→60 分,与 Charm 同一价格,
                    // 即每点百分比 3 分)。
                    case EffectKind.CritBuff: sum += e.Value * 3; break;
                    // 流血:每回合固定伤害、本场持久直到清理,同 BurnSingle 的 ×2 口径
                    // (一次性数值但反复兑现,理应比一次性同值的直伤更值钱)。
                    case EffectKind.Bleed: sum += e.Value * 2; break;
                    // 持续治疗:总收益 = 每回合值 × 持续回合数,与 HealSelf 记账口径一致
                    // (HealSelf 是「一次性治疗全额记分」,HealOverTime 只是把同一笔账摊开在多回合)。
                    // ⚠ 沐(水系金档)目前 effects 只有 HealOverTime+Revive、没有 HealSelf——
                    // 这一条修好之前 沐 的 Power() 恒为 0,机器人会把它当全表最弱、优先弃掉/
                    // 永不合成,与「没有这张字」等价。
                    case EffectKind.HealOverTime: sum += e.Value * Math.Max(1, e.Turns); break;
                    // 下面三条(design §2.2「本次休眠清单」)当前全表 0 载体,只为将来复活时
                    // 不再落回 0 分预先占位,估值全部粗放:
                    case EffectKind.Dispel: sum += e.Value < 0 ? 90 : e.Value * 30; break; // -1=清全部
                    case EffectKind.Cleanse: sum += 50; break; // 净化玩家自身全部减益,Value 不用
                    // AP 上限(design §2.2「AP 上限 +1 0.50」≈75 分/点)。
                    case EffectKind.ApBoost: sum += e.Value * 75; break;
                    // 不灭 / 立即结算(design 未单列价,取模拟 Detonate 的量级,只求非零)。
                    case EffectKind.BurnNoDecay: sum += 40; break;
                    case EffectKind.BurnSettleNow: sum += 30; break;

                    // ================== 2026-09-17 土水系重做的新机制补计分 ==================
                    // 同一把尺子(150 分 / 1.0 价格单位)。新机制在 rebalance 脚本里多数**没有单独价**,
                    // 只有「合并键」(如 减速1+加速 0.25 vs 减速1 0.18)—— 取「合并价 − 单独价」的边际换算。
                    // 此前这些全落默认分支记 0:热 的蓄热面、冷/冻/冰/淼 守面的加速、湮/澡/沐/淋 守面的
                    // 解封在机器人眼里一文不值,土系镇压/碾字的攻面只按裸伤害算。
                    //
                    // 蓄热(热,单独价 0.25 ≈ 38):夺层数的实际收益取决于目标身上挂了几层,机器人不模拟,
                    // 按价目给固定值。
                    case EffectKind.Quench: sum += 38; break;
                    // 加速/急速:边际 0.07~0.13 ≈ 10~20 分。Value(50/100) × 回合数 ÷ 6,
                    // 加速1回合 8、加速2回合 16、急速1回合 16、急速2回合 33 —— 与边际价同序。
                    case EffectKind.Haste: sum += e.Value * Math.Max(1, e.Turns) / 6; break;
                    // 解封:边际 0.04~0.12 ≈ 6~18 分,纯随机重掷(可能变差),取中间 10。
                    case EffectKind.Unseal: sum += 10; break;
                }
                // 挂在伤害/治疗效果**字段**上的新机制(不是独立 kind,switch 覆盖不到):
                // 碾(免疫1+碾 0.55 vs 免疫1 0.35,边际 0.20 ≈ 30 分)
                if (e.TrueDamage) sum += 30;
                // 镇压:实际加码 = 玩家有效护甲 × N%,Power 没有战斗上下文,按价目边际
                // (镇压30 边际 0.10 ≈ 15、镇压50 边际 0.05 ≈ 8)取 N÷4 就近折算,只求非零、同量级。
                if (e.ArmorStrikePercent > 0) sum += e.ArmorStrikePercent / 4;
                // 治疗弹射(海,弹射3+治疗弹射3 0.35 vs 弹射3 0.25,边际 0.10 ≈ 15 分)
                if ((e.Kind == EffectKind.HealSelf || e.Kind == EffectKind.HealAll) && e.Shape == TargetShape.Chain)
                    sum += 15;
            }
            return sum;
        }

        private static int PickTarget(BattleEngine battle)
        {
            int pick = -1, pickHp = int.MaxValue;
            for (int i = 0; i < battle.Enemies.Count; i++)
            {
                var enemy = battle.Enemies[i];
                if (!enemy.Alive) continue;
                if (enemy.Hp < pickHp) { pickHp = enemy.Hp; pick = i; }
            }
            return pick;
        }

        private static void PickBestReward(RecipeGraph graph, RunEngine run)
        {
            // 字 5 选 2:按威力取;满库替换最弱库存,不占优则不换
            // 部件那一路已删(2026-08-04:五行部件改为只能靠拆字获得)
            while (run.Phase == RunPhase.Reward && run.CharPicksLeft > 0 && run.RewardOptions.Count > 0)
            {
                int best = 0, bestPower = -1;
                for (int i = 0; i < run.RewardOptions.Count; i++)
                {
                    int power = CardValue(graph, run.RewardOptions[i]);
                    if (power > bestPower) { bestPower = power; best = i; }
                }
                if (run.PickReward(best)) continue;

                int weakest = 0, weakestPower = int.MaxValue;
                for (int i = 0; i < run.CarriedLibrary.Count; i++)
                {
                    int power = CardValue(graph, run.CarriedLibrary[i]);
                    if (power < weakestPower) { weakestPower = power; weakest = i; }
                }
                if (bestPower <= weakestPower || !run.PickRewardReplacing(best, weakest))
                    break;
            }
            if (run.Phase == RunPhase.Reward)
                run.SkipReward();
        }

        private static void ChooseBestEvent(RunEngine run)
        {
            var options = run.CurrentEvent.Options;
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i =>
                options[i].Ink + options[i].HpDelta * 2 + (options[i].GainChar != null ? 5 : 0)
                + options[i].GainComponents.Count - options[i].InkCost - options[i].ComponentCost);
            foreach (int i in order)
            {
                var picks = options[i].ComponentCost > 0
                    ? Enumerable.Range(0, options[i].ComponentCost).ToArray() : null; // 机器人:抵价取前 N 个
                if (run.ChooseEventOption(i, picks)) return;
            }
            run.ChooseEventOption(0);
        }
    }
}
