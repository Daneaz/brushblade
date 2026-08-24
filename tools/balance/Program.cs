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

        // 三画像共享的"火系"出阵卡组(2026-08-04):补 UnlockedChars 时用它兜底——见下方
        // ClimbUntilDeath 里的说明。
        // 2026-08-10(task-6 二轮):追加 炑/燥/灱——此前这三个新字不在这张表里,导致回合掉字
        // (StartTurn 只从 UnlockedChars 抽)、合成(Compose 同样锁 UnlockedChars)都摸不到它们,
        // 仿真对火系 DOT 三分化完全没有判别力(见 task-6-report.md 第二节)。燃/炽 已经在表里,
        // 不用重复加。真实游戏的战利品池 = 玩家出阵列表(enemies.json 的 endless.rewardPool
        // 是 v0.7 前的废弃字段,不该填),所以这里直接扩这张"画像出阵表",不动游戏配置。
        // ⚠ 2026-08-12:原表里的「灯」是个幽灵 —— ids.txt 有它的拆解,但《技能机制详表》
        // 里根本没有它这一行,管线从没产出过它,进不了 RecipeGraph。而它当时正是「新手」
        // 画像的**唯一**起手字,于是那一档量的是空手打(只能靠回合掉部件 + 兜底一击),
        // 与画像名声称的东西无关。换成 灼(白档,单攻 60,对灼烧目标翻倍)。
        // ⚠ 2026-08-12(E-b4 T5,spec §10.5):追加 锐 —— 不扩这张表就又重演 E-a 的
        // 「工装看不见新字」。锐 是金系而这三档画像是火系,故它是这张表里唯一的异色字:
        // 表的语义是「画像的出阵卡组」,真实卡组本来就可以混色,而穿透那条轴在纯火表里
        // 一个观测点都没有。⚠ 光加进表还不够,Power() 必须同时给 PierceBuff 记分 ——
        // 记 0 分的字机器人永远不会出,那与没加进表**完全等价**(这正是 焰 变异检查
        // 轨迹毫无反应的那次踩过的坑)。
        //
        // 兑 **不加**:它是部件不是字,而 StartTurn 的回合掉字明确只掉字
        // (「五行部件只能靠拆字获得」,BattleEngine.cs:984),把叶子塞进 UnlockedChars
        // 等于给工装造一条生产里不存在的获取路径。这个机器人也从不拆字,兑 在工装里
        // 本就没有可达路径 —— 它的可达性由 PierceBuffCharTests 的
        // RealConfig_Dui_IsReachable_ThroughRuiInTheDeck 在真实规则上钉住,不靠仿真。
        private static readonly string[] FireCards =
        // ⚠ 2026-08-25 字表重构:整表按现行火系 16 字重列。原表里 燃 自 2026-08-14 起
        // 就是幽灵字(那批裁定把它移出了详表,而这张表没跟着改),炽/炑/灱 则随本次重构移出 ——
        // 幽灵字进不了 RecipeGraph,机器人永远摸不到,等于那几档观测点是空的
        // (与上面「灯」那次同型的坑,已第二次踩)。
            { "灼", "焦", "灭", "热", "烧", "爆", "炸", "燥", "烈", "熣", "蒸", "炎", "灿", "焚", "焱", "燚", "锐" };

        // ---- 阳性对照探针(spec §10.5,2026-08-12 E-b4/E-b5 T7)----
        // 这两张卡组**不是平衡目标,是仪器的自检**:先让工装证明它能看见 DEF,再用它读数。
        // 判据只有一条:探针按预期方向动了。P50 的绝对值不是通过/失败判据。

        /// <summary>探针的起爬深度 = 词渊段首。带甲小怪墨渍(DEF 20)只在 11 层起的池子里。</summary>
        private const int ProbeStartDepth = 11;

        /// <summary>护甲字。不同字 SourceId 不同 → **加法叠加**;同一个字再来只刷新。
        ///
        /// ⚠ **2026-08-25 起这档探针已经量不到「叠加」了** —— 磐/巍 早在 2026-08-14 移出字表
        /// (这张表当时没跟着改,一直是幽灵字),漜/崊/崟 随本次字表重构移出,DefenseBuff
        /// 只剩 铠 一个载体。一张字挂不出「加法叠加」,探针退化成「挂了 5 点甲」。
        /// 留着是因为它仍能证明 DefenseBuff 接进了伤害链路;**但它不再是叠加的证据**。</summary>
        private static readonly string[] ArmorCards = { "铠" };

        /// <summary>土系堆甲探针的**起手四张** = 四张最厚的护甲字(铠12 漜15 崊12 崟9,
        /// 卡 5 级后 17+21+17+13 = 68 点)。第一个回合 3 AP 就能挂上三张,DefenseBuff 是
        /// <c>TurnsLeft = -1</c> 且 RunEngine 把它列进 CarriedStatuses,所以整段持久。
        ///
        /// ⚠ 这里刻意偏离了 spec §10.5 写的「铠漜崊磐巍崟 + 一个输出字」的**全防御卡组**。
        /// 实测那套 P50 = 3,**低于对照组**,方向与预期相反 —— 而且不是因为 DefenseBuff
        /// 没接上:六张护甲字占掉 6/7 的回合掉字,同字只刷新不叠加,第二张起全是废牌,
        /// 机器人靠 1/7 的掉字打不动 140×scale 血的小怪,60 回合僵局被记成「卒于当层」。
        /// 那套探针量到的是**僵局判定**,会把「接好了」误报成「接坏了」,比没有探针更糟。
        ///
        /// 现在这套把变量收敛到**一个**:起手四张换成护甲字。出阵卡组仍是 <see cref="FireCards"/>,
        /// 与对照组**逐字相同** —— 掉字流、可合成集合、等级、卡等级、起爬深度全部一致,
        /// 唯一的差别是开局那手牌。于是方向是净的:DefenseBuff 若真进了伤害链路,
        /// 68 点甲对 30~50×scale 的怪攻压得过「少了四张开局输出」;若没接进去,
        /// 剩下的就只有开局吃亏,P50 必然掉到对照组以下(实测正是如此,见任务报告)。</summary>
        private static readonly string[] ArmorHand = { "铠" };

        /// <summary>堆甲探针的卡等级表:得覆盖火系与护甲两边的字,漏掉哪边哪边就退回 1 级。</summary>
        private static readonly string[] ArmorProbeCards = FireCards.Concat(ArmorCards).ToArray();

        /// <summary>AOE 专精:全 DamageAll 且**不带任何附加效果**的字。
        /// 刻意避开 燚/焱/㵘 这类「AOE + 灼烧/治疗」的复合字 —— 混进 DOT 就分不清读数的变化
        /// 来自点数 DEF 的 N 倍惩罚还是来自灼烧,那又是一个「没变化 = 测不出来」的位置。
        ///
        /// ⚠⚠ **这档探针是「因为错误的原因通过的」,T8 不得拿它校准 AOE 轴。**
        /// 它满足 spec §10.5 写的方向(P50 低于对照),但 2026-08-13 实测变异
        /// (墨渍 DEF 20 → 0)只让它从 12.4 动到 12.6 —— 与对照那 ~2 层的差距**主要来自
        /// 字表数值**(AOE 池 50~70 vs 火系 炎 200 / 燚 300),点数 DEF 的 N 倍惩罚
        /// 只值约 **0.2 层**,淹没在噪声里。
        ///
        /// 换句话说:它测的是「AOE 字比单体字弱」,不是「点数 DEF 惩罚 AOE」。
        /// 要真正观测后者,需要一档**数值对齐的单体对照**(同基础值、同稀有度、单体 vs 群体),
        /// 现有字表凑不出来 —— 那是 T8 抬 AOE 数值时要顺带补的。
        /// 留着它是因为删了就连 0.2 层的观测点都没有,但**它的绿不构成任何证据**。</summary>
        // 2026-08-25 字表重构:淹 早已是幽灵字,洪/涛 随本次移出;纯 DamageAll 只剩 海/崩。
        private static readonly string[] AoeCards =
            { "爆", "海", "崩", "剿" };

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            var profiles = new[]
            {
                // 四条角色属性一律由**同一个角色等级**派生(2026-08-11 E-b1 起攻击、
                // 2026-08-12 E-b4 T4 起 DEF 与闪避):画像的等级此前只体现在血量上,
                // 其余恒为基准 —— 那会让 E-b5 重平衡看不见这些成长轴。等级只传一次,
                // 从此不会出现「等级涨了但某条属性忘了跟着涨」。
                new Profile("新手(灼,1级,HP500,ATK100,DEF0,闪0)", new[] { "灼" },
                    new Dictionary<string, int>(), level: 1),
                new Profile("小成长(灼炎烧热,卡3级,3级,HP540,ATK104,DEF1,闪2)", new[] { "灼", "炎", "烧", "热" },
                    FireCards.ToDictionary(c => c, _ => 3), level: 3),
                new Profile("养成(焚炎灼燚,卡5级,10级,HP680,ATK118,DEF4,闪9)", new[] { "焚", "炎", "灼", "燚" },
                    FireCards.ToDictionary(c => c, _ => 5), level: 10),

                // ---- 探针三连(spec §10.5)。三档等级/卡等级/起爬深度逐项相同,只换起手牌与卡组 ----
                // ⚠ 对照这一档是**仪器的一部分**,不是第四个平衡目标:上面三档基线全部从 1 层起爬、
                // 实测「带甲战/次」是 0.0/0.0/0.1 —— 拿它们当参照物,两个探针的方向都无从判起。
                new Profile("探针·对照(火系,深启11)", new[] { "焚", "炎", "灼", "燚" },
                    FireCards.ToDictionary(c => c, _ => 5), level: 10, startDepth: ProbeStartDepth),
                new Profile("探针·土系堆甲(起手四护甲,深启11)", ArmorHand,
                    ArmorProbeCards.ToDictionary(c => c, _ => 5), level: 10,
                    deck: FireCards, startDepth: ProbeStartDepth),
                new Profile("探针·AOE专精(全 DamageAll,深启11)", new[] { "爆", "海", "崩", "剿" },
                    AoeCards.ToDictionary(c => c, _ => 5), level: 10,
                    deck: AoeCards, startDepth: ProbeStartDepth),
            };

            Console.WriteLine($"scalePerDepth={endless.ScalePerDepth} bossBonus={endless.BossScaleBonus} × {Seeds} 种子\n");
            Console.WriteLine("| 画像 | 均卒层 | P50 | P90 | 最深 | 达词渊(11) | 达文山(26) | 达墨海(51) | 带甲战/次 | 带甲多怪战/次 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var profile in profiles)
                SimulateProfile(graph, campaign, endless, profile);
        }

        private sealed class Profile
        {
            public string Name;
            public IReadOnlyList<string> Library;
            /// <summary>出阵卡组 = BattleConfig.UnlockedChars:回合掉字的抽取源,同时锁死合成目标
            /// (2026-07-20)。此前写死成 <see cref="FireCards"/> —— 那样探针画像的起手字会被掉字
            /// 一路稀释成火系,量到的根本不是它声称的那套卡组。</summary>
            public IReadOnlyList<string> Deck;
            public Dictionary<string, int> CardLevels;
            public int MaxHp;
            public int Attack;
            public int Defense;
            public int Dodge;
            /// <summary>起爬深度。三档基线一律从 1 起(它们量的是「一个号能爬多深」);
            /// 探针从 11 起(词渊段首)—— 唯一带甲的小怪墨渍只在 11 层起的池子里,
            /// 从 1 层起爬的画像**根本走不到那里**(实测三档基线的「带甲战/次」是 0.0/0.0/0.1)。
            /// 探针量的不是「能爬多深」而是「某条机制在不在」,所以直接空投到有甲的水域。</summary>
            public int StartDepth;
            public Profile(string name, IReadOnlyList<string> library, Dictionary<string, int> cardLevels,
                int level, IReadOnlyList<string> deck = null, int startDepth = 1)
            {
                Name = name; Library = library; CardLevels = cardLevels; Deck = deck ?? FireCards;
                StartDepth = startDepth;
                MaxHp = MetaRules.MaxHpFor(level);
                Attack = MetaRules.AttackFor(level);
                Defense = MetaRules.DefenseFor(level);
                Dodge = MetaRules.DodgeFor(level);
            }
        }

        /// <summary>一次画像跑完攒下的「见没见到甲」证据(2026-08-12,E-b4/E-b5 T7)。
        ///
        /// ⚠ 为什么非要它:T5 刚踩过 —— 工装能**看见** 锐(三档画像分别真实出牌 332/352/427 次),
        /// 却**量不出**它,因为三档只爬到 10~16 层,唯一带甲的小怪(墨渍,词渊 11 层起)出现太少,
        /// PierceBuff 从 20 改到 5 读数完全不动。「没变化」和「测不出来」在仿真数据里长得一模一样,
        /// 唯一的分辨办法就是**把分母也印出来**:探针到底遇到了几次带甲目标。</summary>
        private sealed class DefExposure
        {
            public int ArmoredBattles;      // 含至少一只带甲敌人的战斗数
            public int ArmoredMultiBattles; // 且同场敌人 ≥2 —— 点数 DEF 的 N 倍惩罚只在这种场里兑现
        }

        private static void SimulateProfile(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile)
        {
            var deaths = new List<int>();
            var exposure = new DefExposure();
            foreach (int seed in Enumerable.Range(0, Seeds))
                deaths.Add(ClimbUntilDeath(graph, campaign, endless, profile, seed, exposure));

            deaths.Sort();
            double avg = deaths.Average();
            int p50 = deaths[deaths.Count / 2];
            int p90 = deaths[(int)(deaths.Count * 0.9)];
            string Reach(int band) => $"{deaths.Count(d => d >= band) * 100 / deaths.Count}%";
            Console.WriteLine($"| {profile.Name} | {avg:F1} | {p50} | {p90} | {deaths[^1]} " +
                              $"| {Reach(11)} | {Reach(26)} | {Reach(51)} " +
                              $"| {exposure.ArmoredBattles / (double)Seeds:F1} " +
                              $"| {exposure.ArmoredMultiBattles / (double)Seeds:F1} |");
        }

        /// <summary>一路深入直到阵亡,返回卒层(= 阵亡所在层)。</summary>
        private static int ClimbUntilDeath(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile, int seed, DefExposure exposure)
        {
            int towerSeed = seed * 7919 + 17;
            int fromDepth = profile.StartDepth;
            IReadOnlyList<string> library = profile.Library;
            IReadOnlyList<string> pool = new[] { "木", "木" };
            int hp = profile.MaxHp;

            while (fromDepth <= DepthCap)
            {
                var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                    campaign.Events, campaign.EventChancePercent);
                // UnlockedChars(2026-08-04 起也是回合掉字的抽取源,见 BattleEngine.StartTurn)。
                // 生产侧口径是 _meta.Deck——玩家自选的出阵卡组(GameRoot.cs)。三个画像没有各自的
                // 出阵卡组概念,只声明了起手 Library + CardLevels,而 CardLevels 已经用 FireCards
                // 这个 9 字火系名单给两个成长画像定过级——用它顶 UnlockedChars 是同一套"这画像
                // 已经练熟的字"口径,数量上也落在真实出阵卡组的 5~15 张区间内(Meta.DeckMinimum/
                // DeckLimit)。注意:UnlockedChars 非空时 ForgeEngine 也会用它锁合成目标(2026-07-20
                // 拍板),即画像现在只能合成 FireCards 里的字——比改造前"不限合成"更贴近生产,
                // 但也是本次顺带激活的口径,如果后续要专门校准合成侧数值,这里可能要再调整。
                var battleConfig = new BattleConfig
                {
                    DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp,
                    PlayerAttack = profile.Attack,
                    PlayerDefense = profile.Defense, PlayerDodge = profile.Dodge,
                    UnlockedChars = profile.Deck,
                };
                var run = new RunEngine(graph, runConfig, battleConfig, library, pool,
                    seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: profile.CardLevels,
                    startingHp: hp);

                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward || run.Phase == RunPhase.Event)
                {
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }

                    var battle = run.Battle;
                    // 「见没见到甲」的分母(每场战斗记一次;这一行不消耗任何随机数)。
                    // ⚠ 只数**开战时**就带甲的敌人 = 小怪墨渍(词渊 11 层起,DEF 20)。
                    // Boss 的带甲阶段(山 60 / 江 30 / 钧 30)不计:它们是单敌战,
                    // 点数 DEF 的 N 倍惩罚在单敌场里根本不兑现,对 AOE 探针没有判别力。
                    if (battle.Enemies.Any(e => e.Defense > 0))
                    {
                        exposure.ArmoredBattles++;
                        if (battle.Enemies.Count >= 2) exposure.ArmoredMultiBattles++;
                    }
                    int turns = 0;
                    while (turns <= StallTurns)
                    {
                        if (battle.Phase == BattlePhase.DropChoice) { ResolveDropChoice(graph, battle); continue; }
                        if (battle.Phase != BattlePhase.PlayerTurn) break;
                        turns++;
                        PlayTurn(graph, battle);
                    }
                    if (turns > StallTurns)
                        return fromDepth + run.BattleIndex; // 僵局计为卒于当前层
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

        private static void PlayTurn(RecipeGraph graph, BattleEngine battle)
        {
            while (battle.Ap >= 2)
            {
                var suggest = ForgeEngine.Suggest(graph, battle.Pool, battle.Library);
                string best = null;
                int bestPower = BestCastablePower(graph, battle);
                foreach (var id in suggest.Composable)
                {
                    int power = Power(graph, id);
                    if (power > bestPower) { bestPower = power; best = id; }
                }
                if (best == null) break;

                if (battle.Compose(best) == BattleError.ForgeFailed)
                {
                    var weakest = battle.Library.OrderBy(id => Power(graph, id)).FirstOrDefault();
                    if (weakest == null || battle.Discard(weakest) != BattleError.None) break;
                    if (battle.Compose(best) != BattleError.None) break;
                }
            }

            while (battle.Phase == BattlePhase.PlayerTurn && battle.Ap > 0)
            {
                string pick = null;
                int pickPower = -1;
                foreach (var id in battle.Library.Concat(battle.Pool.Where(p => IsCastableLeaf(graph, p, battle))))
                {
                    if (!graph.TryGet(id, out var def) || def.ApCost > battle.Ap) continue;
                    int power = Power(graph, id);
                    if (power > pickPower) { pickPower = power; pick = id; }
                }
                if (pick == null) break;

                graph.TryGet(pick, out var pickDef);
                int target = BattleEngine.NeedsTarget(pickDef) ? PickTarget(battle) : -1;
                if (battle.Cast(pick, target) != BattleError.None) break;
            }

            if (battle.Phase == BattlePhase.PlayerTurn)
                battle.EndTurn();
        }

        /// <summary>回合掉字撞满库时的决议策略:掉的字强于库中最弱则换入(ResolveDrop),
        /// 否则跳过(SkipDrop)——与 PickBestReward 的换入判定同一套贪心口径,保持机器人在
        /// 「战利品换入」「掉落换入」两条注入路径上的策略一致(评审建议)。</summary>
        private static void ResolveDropChoice(RecipeGraph graph, BattleEngine battle)
        {
            int droppedPower = Power(graph, battle.PendingDrop);
            int weakest = 0, weakestPower = int.MaxValue;
            for (int i = 0; i < battle.Library.Count; i++)
            {
                int power = Power(graph, battle.Library[i]);
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
                best = Math.Max(best, Power(graph, id));
            return best;
        }

        private static int Power(RecipeGraph graph, string id)
        {
            if (!graph.TryGet(id, out var def)) return 0;
            if (def.Effects.Count == 0) return 3;
            int sum = 0;
            foreach (var e in def.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.DamageSingle: sum += e.Value; break;
                    case EffectKind.DamageAll: sum += e.Value * 3 / 2; break;
                    case EffectKind.BurnSingle: sum += e.Value * 2; break;
                    case EffectKind.BurnAll: sum += e.Value * 3; break;
                    case EffectKind.Shield: sum += e.Value / 2; break;
                    case EffectKind.BurnPotency: sum += e.Value * 2; break;
                    case EffectKind.HealSelf: sum += e.Value / 2; break;
                    case EffectKind.Summon: sum += (e.Value + e.SummonAttack * 3) * e.SummonCount / 2; break;
                    // 穿透(2026-08-12,E-b4 T5,锐):按点数**等价折算成伤害**,不加权。
                    // 它本场持久、每次攻击都兑现,理应比一次性伤害值钱;但全表只有 墨渍(DEF 20)
                    // 与 3 个 Boss 阶段(30/30/60)有甲,对其余敌人它一分钱不值。等价折算是这两头
                    // 之间的保守中点 —— 排在 灼(60)之后,机器人先打伤害再攒穿透。
                    case EffectKind.PierceBuff: sum += e.Value; break;
                    // 护甲(2026-08-12,E-b4/E-b5 T7,土系堆甲探针):按点数 ×2 折算。
                    // ⚠ **系数是多少不重要,是不是 0 才重要**:记 0 分的字机器人永远不会去
                    // 合成它(Compose 那条分支要求 power 严格大于库里最强的),那与「没把它加进
                    // 出阵表」完全等价 —— 正是 焰 变异检查轨迹毫无反应踩过的坑。没有这一条,
                    // 土系堆甲探针就是个装饰品:它会握着一手防御字一张都不出。
                    // ×2 的口径同 BurnSingle:本场持久、每记挥击都兑现,但只在挨打时兑现,
                    // 所以排在同数值的直伤之后(铠 12 → 24 分,仍低于 碾 的 60)。
                    case EffectKind.DefenseBuff: sum += e.Value * 2; break;
                }
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
                    int power = Power(graph, run.RewardOptions[i]);
                    if (power > bestPower) { bestPower = power; best = i; }
                }
                if (run.PickReward(best)) continue;

                int weakest = 0, weakestPower = int.MaxValue;
                for (int i = 0; i < run.CarriedLibrary.Count; i++)
                {
                    int power = Power(graph, run.CarriedLibrary[i]);
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
