using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Balance
{
    /// <summary>无尽难度仿真(20.4 校准):机器人永不撤退一路深入,量「卒于第几层」分布。
    /// 机器人弱于人类,数据当难度地板读;关卡制已废止(v0.7),旧关卡口径删除。
    ///
    /// 缺省跑 Plan F1 的搜索机器人(<c>Bot/</c>):快档 30 种子 × {五系随机 + 单系五个} × 卡 Lv5;
    /// <c>--slow</c> 慢档 30 种子 × 全部 10 画像 × Lv1/5/8(K=12 / 叶子 4096),另跑同画像快档 Lv5 出差列。
    /// 进度走 stderr(stdout 报告不变);<c>--max-minutes N</c> 到时不再开新局,报告只统计跑完的种子。
    /// 命令行:<c>dotnet run -c Release -- [--slow] [--profile 名] [--seeds N] [--card-level N] [--max-minutes N] [--no-board-carry]</c>
    /// (<c>--no-board-carry</c> 仅供诊断,见 <c>--help</c>)。</summary>
    public static class Program
    {
        private const int StallTurns = 60;
        private const int DepthCap = 300;

        /// <summary>画像统一的角色养成档:角色 10 级(沿用 2026-09 系列画像的中段养成口径)。
        /// 卡等级由搜索机器人按档位定(快档 Lv5、慢档 Lv1/5/8),<c>--card-level N</c> 覆盖。</summary>
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

        private const string Usage =
            "用法:dotnet run -c Release -- [选项]\n" +
            "  (缺省)            快档:30 种子 × {五系随机 + 单系五个} × 卡 Lv5,K=4 / 叶子 256\n" +
            "  --slow            慢档:30 种子 × 10 画像 × 卡 Lv1/5/8,K=12 / 叶子 4096,另出快档 Lv5 差列\n" +
            "  --profile 名      只跑匹配的画像(先精确,再子串)\n" +
            "  --seeds N         覆盖种子数\n" +
            "  --card-level N    覆盖卡等级\n" +
            "  --max-minutes N   到时不再开新局(已开的跑完),报告只统计跑完的种子并在表头注明\n" +
            "  --no-board-carry  【仅供诊断】跨段不带护盾 / 木灵 / 厚泉等状态(生产会带);\n" +
            "                    只用于拆分「搜索更聪明」与「携带对齐生产」各自的涨幅,读数不代表游戏\n" +
            "  --help            打印本说明";

        public static void Main(string[] args)
        {
            bool slow = false, noBoardCarry = false;
            int? seedsArg = null, levelArg = null;
            double? maxMinutes = null;
            string profileArg = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--card-level" && i + 1 < args.Length && int.TryParse(args[i + 1], out var lv) && lv >= 1)
                    { levelArg = lv; i++; }
                else if (args[i] == "--seeds" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n >= 1)
                    { seedsArg = n; i++; }
                else if (args[i] == "--max-minutes" && i + 1 < args.Length
                         && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm > 0)
                    { maxMinutes = mm; i++; }
                else if (args[i] == "--profile" && i + 1 < args.Length)
                    { profileArg = args[i + 1]; i++; }
                else if (args[i] == "--slow") slow = true;
                else if (args[i] == "--no-board-carry") noBoardCarry = true;
                else if (args[i] == "--help" || args[i] == "-h") { Console.WriteLine(Usage); return; }
                else
                    throw new ArgumentException($"未知参数:{args[i]}\n{Usage}");
            }
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
            if (profileArg != null)
            {
                var exact = profiles.Where(p => p.Name == profileArg).ToList();
                profiles = exact.Count > 0 ? exact : profiles.Where(p => p.Name.Contains(profileArg)).ToList();
                if (profiles.Count == 0) throw new ArgumentException($"没有画像匹配「{profileArg}」");
            }

            RunSearchBot(graph, campaign, endless, profiles, slow, seedsArg, levelArg, profileArg != null, noBoardCarry, maxMinutes);
        }

        private sealed class Profile
        {
            public string Name;
            /// <summary>已解锁卡池 = BattleConfig.UnlockedChars = 战后奖励池:回合掉字的抽取源,
            /// 同时锁死合成目标(2026-07-20)。</summary>
            public IReadOnlyList<string> OwnedCards;
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


        // ---- F1 搜索机器人(Bot/) ----

        /// <summary>同旧工装的角色属性口径(UnlockedChars 由 BotRunner 按卡池填)。</summary>
        private static BattleConfig NewBattleConfig(CampaignConfig campaign, Profile profile) => new BattleConfig
        {
            DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp,
            PlayerAttack = profile.Attack, PlayerDefense = profile.Defense, PlayerDodge = profile.Dodge,
        };

        /// <summary>起手字库与部件池:与旧工装同一颗种子、同一套抽法,新旧两版同种子起手一致。</summary>
        private static (IReadOnlyList<string>, IReadOnlyList<string>) StartingHand(RecipeGraph graph, Profile profile, int seed)
        {
            var deckRandom = new GameRandom(unchecked(seed * 104729 + 3));
            var library = DrawStartingLibrary(graph, profile, deckRandom);
            var pool = MetaRules.RollStartingPool(library, graph, deckRandom);
            return (library, pool);
        }

        /// <summary>快档画像:五系随机(核心)+ 单系五个(2026-10-14 用户拍板)。</summary>
        private static bool InFastSet(Profile p) => p.Name == "五系随机(核心)" || p.Name.StartsWith("单系·");

        private sealed class Row
        {
            public Profile Profile;
            public int Level;
            public ClimbSetup Setup;
            public ClimbResult[] Results;
            /// <summary>每颗种子单局的墙钟秒数;行的「种子·秒」= 合计(各行并发,行墙钟无意义)。</summary>
            public double[] SeedSeconds;
            /// <summary>跑完的种子(超时截断时少于 <see cref="Results"/> 槽数;不截断时就是全部)。</summary>
            public ClimbResult[] Done => Truncation.Completed(Results);
            public double AvgDepth => Done.Length == 0 ? double.NaN : Done.Average(r => r.DeathDepth);
            public string Label => $"{Profile.Name} Lv{Level}";
        }

        /// <summary>建一行(画像 × 卡等级 × 档位)的爬塔配置;各行的种子由 <see cref="RunRows"/> 摊平后一起并发。</summary>
        private static Row NewRow(RecipeGraph graph, CampaignConfig campaign, Profile profile, int level,
            int seeds, SearchOptions opt, CardAppraiser appraiser, bool noBoardCarry = false)
        {
            var setup = new ClimbSetup
            {
                Graph = graph, Campaign = campaign, OwnedCards = profile.OwnedCards,
                CardLevels = profile.OwnedCards.ToDictionary(c => c, _ => level),
                NewBattleConfig = () => NewBattleConfig(campaign, profile),
                Search = opt, Appraiser = appraiser, DepthCap = DepthCap, StallTurns = StallTurns,
                CarryBoard = !noBoardCarry,
            };
            return new Row { Profile = profile, Level = level, Setup = setup, Results = new ClimbResult[seeds], SeedSeconds = new double[seeds] };
        }

        /// <summary>把所有行的(行, 种子)摊平进一个负载均衡的 Parallel.ForEach:逐行串行时每行要等最慢的那颗种子,
        /// 8 核只用上 2–3 核。每局的结果写回自己行的种子下标,与调度无关(确定性同 <see cref="BotRunner.ClimbMany"/>)。
        /// 按种子优先排(先各行的种子 0,再各行的种子 1…),超时截断时各行完成数相近。
        /// 进度走 stderr(<see cref="ProgressGate"/>);<paramref name="deadline"/> 到了就不再开新局(<see cref="RunBatch"/>)。</summary>
        private static void RunRows(RecipeGraph graph, IReadOnlyList<Row> rows, TimeSpan? deadline)
        {
            int maxSeeds = rows.Max(r => r.Results.Length);
            var jobs = Enumerable.Range(0, maxSeeds)
                .SelectMany(i => rows.Where(r => i < r.Results.Length).Select(r => (row: r, seed: i))).ToList();
            var clock = Stopwatch.StartNew();
            int done = 0, lastDone = 0;
            double lastElapsed = 0;
            var gate = new object();
            void Report(bool force)
            {
                lock (gate)
                {
                    int d = Volatile.Read(ref done);
                    double t = clock.Elapsed.TotalSeconds;
                    if (!force && !ProgressGate.ShouldReport(d, jobs.Count, t, lastDone, lastElapsed)) return;
                    Console.Error.WriteLine(ProgressGate.Line(d, jobs.Count, t));
                    lastDone = d; lastElapsed = t;
                }
            }
            using (new Timer(_ => Report(false), null, 1000, 1000))
            {
                RunBatch.Run(jobs.Count, k =>
                {
                    var job = jobs[k];
                    var watch = Stopwatch.StartNew();
                    var (library, pool) = StartingHand(graph, job.row.Profile, job.seed);
                    job.row.Results[job.seed] = BotRunner.Climb(job.row.Setup, job.seed, library, pool);
                    job.row.SeedSeconds[job.seed] = watch.Elapsed.TotalSeconds;
                    Interlocked.Increment(ref done);
                    Report(false);
                }, expired: () => deadline.HasValue && clock.Elapsed >= deadline.Value);
            }
            lock (gate) if (done == lastDone) return; // 最后一局已报过
            Report(true);
        }

        private static void RunSearchBot(RecipeGraph graph, CampaignConfig campaign, EndlessConfig endless,
            List<Profile> profiles, bool slow, int? seedsArg, int? levelArg, bool profileChosen, bool noBoardCarry,
            double? maxMinutes)
        {
            int seeds = seedsArg ?? 30;
            var levels = levelArg.HasValue ? new[] { levelArg.Value } : slow ? new[] { 1, 5, 8 } : new[] { 5 };
            if (!slow && !profileChosen) profiles = profiles.Where(InFastSet).ToList();
            var opt = slow ? SearchOptions.Slow : SearchOptions.Fast;
            var any = profiles[0];
            var appraiser = new CardAppraiser(graph, endless, () => NewBattleConfig(campaign, any)); // 各画像角色属性相同

            if (noBoardCarry) Console.WriteLine("⚠ 诊断模式 --no-board-carry:跨段不带护盾 / 木灵 / 状态,读数不代表游戏\n");
            Console.WriteLine($"搜索机器人 · {(slow ? "慢档" : "快档")} K={opt.K} 叶子≤{opt.LeafCap} · {seeds} 种子 · 并发 {Environment.ProcessorCount} 核"
                + $" · 卡 Lv{string.Join("/", levels)} · 角色 {CharacterLevel} 级 · 起手 6 张随机"
                + $" · scalePerDepth={endless.ScalePerDepth} bossBonus={endless.BossScaleBonus}\n");

            var total = Stopwatch.StartNew();
            var rows = new List<Row>();
            foreach (var profile in profiles)
                foreach (int level in levels)
                    rows.Add(NewRow(graph, campaign, profile, level, seeds, opt, appraiser, noBoardCarry));

            // 慢档差列:同画像、同种子的快档 Lv5(spec §5;2026-10-14 拍板只比 Lv5)。估值与档位无关,共用同一个估值器
            Dictionary<string, Row> fastLv5 = null;
            if (slow && levels.Contains(5))
                fastLv5 = profiles.ToDictionary(p => p.Name,
                    p => NewRow(graph, campaign, p, 5, seeds, SearchOptions.Fast, appraiser, noBoardCarry));
            var allRows = fastLv5 == null ? rows : rows.Concat(fastLv5.Values).ToList();
            RunRows(graph, allRows, maxMinutes.HasValue ? TimeSpan.FromMinutes(maxMinutes.Value) : (TimeSpan?)null);
            double mainSeconds = rows.Sum(r => r.SeedSeconds.Sum());
            double fastSeconds = fastLv5?.Values.Sum(r => r.SeedSeconds.Sum()) ?? 0;

            if (maxMinutes.HasValue)
            {
                string note = Truncation.Note(allRows.Select(r => (r.Label, r.Done.Length, r.Results.Length)), maxMinutes.Value);
                if (note != null) Console.WriteLine(note + "\n");
            }

            // 末两组列是机器人自检:重搜 / 本体拒(应恒 0)。拆 / 合 / 嫁接 / 替换按每局计。
            Console.WriteLine($"| 画像 | 卡Lv | 卡池 | 均卒层 | P50 | P90 | 最深 | 达朱砂(11) | 达金石(31) | 达词渊(51) | 攻面出字/局 | 召唤出字/局 | 僵局判死/{seeds} | 反击总伤/局 | 反击被削% "
                + "| 拆/局 | 合/局 | 嫁接/局 | 替换/局 | 有拆合局% | 有嫁接或替换局% | 两面字 攻:五行 | 回合/局 | 重搜/回合 | 本体拒 | 种子·秒 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var row in rows) PrintRow(row);

            Console.WriteLine();
            Console.WriteLine("| 画像 | " + string.Join(" | ", levels.Select(l => $"均卒层 Lv{l}")) + (fastLv5 != null ? " | 快档 Lv5 | 慢−快(Lv5) |" : " |"));
            Console.WriteLine("|---|" + string.Concat(levels.Select(_ => "---|")) + (fastLv5 != null ? "---|---|" : ""));
            foreach (var profile in profiles)
            {
                var line = $"| {profile.Name} | " + string.Join(" | ",
                    levels.Select(l => rows.First(r => r.Profile == profile && r.Level == l).AvgDepth.ToString("F1")));
                if (fastLv5 != null)
                {
                    double slowLv5 = rows.First(r => r.Profile == profile && r.Level == 5).AvgDepth;
                    double fast = fastLv5[profile.Name].AvgDepth;
                    line += $" | {fast:F1} | {slowLv5 - fast:+0.0;-0.0;0.0}";
                }
                Console.WriteLine(line + " |");
            }

            // 卡池小于 20 张的画像(单系)取 min(10, 池/2),Top 与 Bottom 不重叠
            Console.WriteLine("\n出字 Top / Bottom 各 min(10, 卡池/2) 张(每局次数,只计卡池里的字):\n");
            foreach (var row in rows)
            {
                var done = row.Done;
                if (done.Length == 0) { Console.WriteLine($"- {row.Label}:无跑完的种子"); continue; }
                var perGame = row.Profile.OwnedCards.Distinct()
                    .Select(id => (id, n: done.Sum(r => r.CastsByChar.TryGetValue(id, out int c) ? c : 0) / (double)done.Length))
                    .ToList();
                string Fmt(IEnumerable<(string id, double n)> xs) => string.Join(" ", xs.Select(x => $"{x.id}{x.n:F1}"));
                int take = Math.Min(10, perGame.Count / 2);
                var top = perGame.OrderByDescending(x => x.n).ThenBy(x => x.id, StringComparer.Ordinal).Take(take);
                var bottom = perGame.OrderBy(x => x.n).ThenBy(x => x.id, StringComparer.Ordinal).Take(take);
                Console.WriteLine($"- {row.Profile.Name} Lv{row.Level}:Top {Fmt(top)} · Bottom {Fmt(bottom)}");
            }

            Console.WriteLine($"\n耗时:墙钟 {total.Elapsed.TotalSeconds:F1}s(各行种子摊平并发)· {(slow ? "慢档" : "快档")} {mainSeconds:F0} 种子·秒" +
                (fastLv5 != null ? $" · 快档对照(Lv5){fastSeconds:F0} 种子·秒" : ""));
        }

        /// <summary>一行读数;超时截断时只算跑完的种子(<c>seeds</c> = 跑完数,不截断时即全部)。</summary>
        private static void PrintRow(Row row)
        {
            var results = row.Done;
            int seeds = results.Length;
            if (seeds == 0)
            {
                Console.WriteLine($"| {row.Profile.Name} | {row.Level} | {row.Profile.OwnedCards.Count} | 无跑完的种子 |");
                return;
            }
            var deaths = results.Select(r => r.DeathDepth).OrderBy(d => d).ToList();
            double avg = deaths.Average();
            int p50 = deaths[deaths.Count / 2];
            int p90 = deaths[(int)(deaths.Count * 0.9)];
            string Reach(int band) => $"{deaths.Count(d => d >= band) * 100 / deaths.Count}%";
            double PerGame(Func<ClimbResult, double> f) => results.Sum(f) / seeds;
            int Count(ClimbResult r, string kind) => r.Stats.ActionCounts.TryGetValue(kind, out int n) ? n : 0;
            int Share(Func<ClimbResult, bool> f) => results.Count(f) * 100 / results.Length;
            long dealt = results.Sum(r => r.CounterDealt), clipped = results.Sum(r => r.CounterClipped);
            int dualAttack = results.Sum(r => r.DualAttackCasts), dualFeature = results.Sum(r => r.DualFeatureCasts);
            int dual = dualAttack + dualFeature;
            int turns = results.Sum(r => r.Stats.Turns);
            Console.WriteLine($"| {row.Profile.Name} | {row.Level} | {row.Profile.OwnedCards.Count} | {avg:F1} | {p50} | {p90} | {deaths[^1]} " +
                              $"| {Reach(11)} | {Reach(31)} | {Reach(51)} " +
                              $"| {PerGame(r => r.AttackFaceCasts):F1} | {PerGame(r => r.SummonCasts):F1} " +
                              $"| {results.Count(r => r.Stalled)} " +
                              $"| {dealt / (double)seeds:F0} " +
                              $"| {(dealt + clipped == 0 ? 0 : clipped * 100.0 / (dealt + clipped)):F0}% " +
                              $"| {PerGame(r => Count(r, "Dismantle")):F1} | {PerGame(r => Count(r, "Compose")):F1} " +
                              $"| {PerGame(r => Count(r, "Graft")):F1} | {PerGame(r => Count(r, "Replace")):F1} " +
                              // spec §7-3「每局有」:至少一次的局占比(拆或合 / 嫁接或替换)
                              $"| {Share(r => Count(r, "Dismantle") + Count(r, "Compose") > 0)}% " +
                              $"| {Share(r => Count(r, "Graft") + Count(r, "Replace") > 0)}% " +
                              $"| {(dual == 0 ? "—" : $"{dualAttack * 100 / dual}:{100 - dualAttack * 100 / dual}")} " +
                              $"| {turns / (double)seeds:F1} " +
                              $"| {(turns == 0 ? 0 : results.Sum(r => r.Stats.Replans) / (double)turns):F2} " +
                              $"| {results.Sum(r => r.Stats.BodyRejected)} " +
                              $"| {row.SeedSeconds.Sum():F0} |");
        }
    }
}
