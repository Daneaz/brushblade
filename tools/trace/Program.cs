using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Trace
{
    /// <summary>黄金轨迹工装(E-b4+E-b5 的 T0,spec §10.2「网 1」)。
    ///
    /// 为什么是独立工装而不是 <c>tools/balance --trace</c>:
    /// spec 的改动面表里 <c>tools/balance/Program.cs</c> 自己就是 T7 的交付物(要加 Defense/Dodge、
    /// 加两档探针画像、扩 FireCards)。轨迹是 T1~T4 的**验收基线**,基线的采样器绝不能跟着
    /// 被验收的对象一起改 —— 共用一份机器人代码的话,T7 动一下 FireCards 就会把 baseline 挪位,
    /// 而那种挪位看起来跟真 bug 一模一样。所以这里**刻意**复制一份贪心机器人并钉死,
    /// 与 balance 各自演化。
    ///
    /// 用法:
    ///   dotnet run -c Release -- --out out/baseline.txt
    ///   dotnet run -c Release -- --out out/strict.txt --seeds 0-15 --max-depth 30 --flat-scale
    ///   dotnet run -c Release -- --compare out/baseline.txt out/after.txt --scale 10
    ///
    /// 两条基线各管一件事:
    /// - <c>--flat-scale</c> 的那条是**严格恒等基线**(网 1 的判据本体),深度缩放压平后
    ///   精确 ×10 在任意深度成立;
    /// - 不带 <c>--flat-scale</c> 的那条是**生产口径基线**,带着真实的深度缩放,
    ///   用来看 T1 之后真实难度曲线偏了多少(缩放里的 ceil 天生不与 ×10 交换,见下)。</summary>
    public static class Program
    {
        // ---- 钉死的采样参数(改动即等于换尺子,baseline 必须重跑)----

        private const int StallTurns = 60;
        private const int DefaultDepthCap = 300;
        private static readonly int[] DefaultSeeds = { 0, 1, 2, 3, 4, 5, 6, 7 };

        /// <summary>出阵表 = 可合成集 + 回合掉字的抽取源。**钉死的副本**,不与
        /// <c>tools/balance</c> 的 FireCards 共享(见类注释)。</summary>
        /// ⚠ 2026-08-12:原表里的「灯」是幽灵字 —— ids.txt 有拆解但《技能机制详表》没有它,
        /// 管线从没产出过,进不了 RecipeGraph。而它是「基准1级」画像的唯一起手字,那一档
        /// 因此是空手打。已移除;基准画像改用 灼(白档,单攻 60)。
        private static readonly string[] FireCards =
            { "炎", "烧", "燃", "灼", "炽", "焚", "焱", "燚", "炑", "燥", "灱" };

        /// <summary>杂食出阵表:纯火系打不出护盾/治疗/召唤/流血这几路事件,量级 ×10
        /// 在那些路径上就没有观测点。这张表专门为**事件种类覆盖**而配。
        ///
        /// ⚠ 刻意排除三类字:暴击(锋)、减伤(铠漜崊崟磐巍)、破甲(熔溃溶锤破碎)——
        /// 它们都走**乘法**(×1.5 / ×0.7 / ×1.25),而 floor(10x·k) ≠ 10·floor(x·k),
        /// 会往基线里掺进与「接线出错」无从区分的舍入噪声。这三路的验收另有归属:
        /// 暴击是 E-b2 已经守住的,减伤/破甲是 T3 的网 3 要专测的。</summary>
        private static readonly string[] MixedCards =
            { "塔", "城", "堡", "治", "滋", "淋", "森", "林", "桃", "柳", "锯", "冰", "洼", "锁", "淼", "海", "崩" };

        /// <summary>卡等级全 1:spec §10.2 的「基准切片」要求。<see cref="MetaRules.ScaleByCardLevel"/>
        /// 在 level &gt; 1 时带 ceil,ceil(10x·k) ≠ 10·ceil(x·k),等级一上去 ×10 恒等立刻不成立。</summary>
        private static readonly Dictionary<string, int> AllLevelOne = new();

        public static int Main(string[] args)
        {
            // 非确定性来源之一:数字/字符串格式化受宿主 locale 影响。整个进程钉在不变文化上。
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var compare = Take(args, "--compare", 2);
            if (compare != null)
            {
                long scale = long.Parse(Take(args, "--scale", 1)?[0] ?? "10", CultureInfo.InvariantCulture);
                return TraceCompare.Run(compare[0], compare[1], scale);
            }

            string outPath = Take(args, "--out", 1)?[0]
                             ?? Path.Combine(ToolDir, "out", "trace.txt");
            int[] seeds = ParseSeeds(Take(args, "--seeds", 1)?[0]) ?? DefaultSeeds;
            int maxDepth = int.Parse(Take(args, "--max-depth", 1)?[0]
                                     ?? DefaultDepthCap.ToString(CultureInfo.InvariantCulture),
                                     CultureInfo.InvariantCulture);
            bool flatScale = args.Contains("--flat-scale");

            string configDir = Path.Combine(ToolDir, "../../Brushblade/Assets/StreamingAssets/config");
            string charsJson = File.ReadAllText(Path.Combine(configDir, "chars.json"));
            string enemiesJson = File.ReadAllText(Path.Combine(configDir, "enemies.json"));
            var graph = ConfigLoader.LoadGraph(charsJson);
            var campaign = ConfigLoader.LoadCampaign(enemiesJson, graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            // --flat-scale:把深度缩放压平成恒 1.0。
            // ⚠ 这不是「让数据更好看」,是网 1 能不能成立的前提。CampaignConfig.Scale 走
            // (int)Math.Ceiling(base × scale),而 ceil(10a·s) ≠ 10·ceil(a·s) —— 实测本仓库
            // 19 个敌人基础值 × 30 层里有 63% 的组合不满足。缩放一开,depth ≥ 2 的敌人血量/攻击
            // 就不是精确 ×10,轨迹的骨架会跟着结构性发散(某只怪早一回合死),
            // 而那种发散与「T1 改错了」长得一模一样。
            // 压平之后 scale 恒 1.0 → ceil 是恒等 → 精确 ×10 在**任意深度**成立,
            // 于是能在整段爬塔(含 Boss、含深层段)上跑严格恒等判据,而不是只在第 1 层。
            if (flatScale)
            {
                endless.ScalePerDepth = 0f;
                endless.BossScaleBonus = 1f;
            }

            // 三档画像**全部落在基准切片上**(ATK = AttackFor(1) = 100、卡等级全 1):
            // 只有这条切片上「量级 ×10 = 行为不变」才成立(spec §10.2 的盲区声明)。
            // 血量不受这条约束(它自己也是要 ×10 的量),所以拿它来换取更深的覆盖面:
            // 耐久档能爬得更深,深层档直接空降到 26 层去够到「文山」段与成语 Boss。
            var profiles = new[]
            {
                new Profile("基准1级", new[] { "灼" }, MetaRules.MaxHpFor(1), 1, FireCards),
                new Profile("基准耐久", new[] { "焚", "炽", "灼", "燚" }, MetaRules.MaxHpFor(26), 1, FireCards),
                new Profile("基准深层", new[] { "焚", "炽", "灼", "燚" }, MetaRules.MaxHpFor(26), 26, FireCards),
                new Profile("基准杂食", new[] { "塔", "治", "森", "锯", "冰", "淼" },
                    MetaRules.MaxHpFor(26), 1, MixedCards),
            };

            using (var rec = new TraceRecorder(outPath))
            {
                rec.Comment("brushblade golden trace v1(E-b4+E-b5 T0;spec §10.2)");
                rec.Comment("以 # 开头的行不参与比对 —— 表头里的血量等数字本身就是 T1 要 ×10 的量");
                rec.Comment("R seed profile startDepth");
                rec.Comment("S seed fromDepth runRandomState");
                rec.Comment("B seed depth battleIndex enemyIds enemyMaxHp battleRandomState");
                rec.Comment("T seed depth battleIndex turn randomState playerHp shield library pool enemyHp");
                rec.Comment("D seed depth battleIndex turn droppedChar action");
                rec.Comment("E seed depth battleIndex turn kind target second crit amount absorbed");
                rec.Comment("W seed depth battleIndex result turns randomState playerHp");
                rec.Comment("K seed depth battleIndex rewardOptions picked");
                rec.Comment("V seed depth eventId optionIndex");
                rec.Comment("Z seed deathDepth reason");
                rec.Comment($"seeds = {string.Join(",", seeds)}  maxDepth = {maxDepth}  stallTurns = {StallTurns}");
                rec.Comment($"flatScale = {flatScale}  scalePerDepth = {endless.ScalePerDepth}  " +
                            $"bossScaleBonus = {endless.BossScaleBonus}");
                rec.Comment($"attack = {MetaRules.AttackFor(1)}(基准 {BattleConfig.AttackBaseline})  cardLevels = 全 1");
                foreach (var p in profiles)
                    rec.Comment($"profile {p.Name}: hp={p.MaxHp} startDepth={p.StartDepth} " +
                                $"lib={string.Join("|", p.Library)} deck={string.Join("|", p.Deck)}");
                rec.Comment($"chars.json sha256 = {Sha256(charsJson)}");
                rec.Comment($"enemies.json sha256 = {Sha256(enemiesJson)}");

                foreach (var profile in profiles)
                    foreach (int seed in seeds)
                        Climb(graph, campaign, endless, profile, seed, maxDepth, rec);

                Console.WriteLine($"轨迹已写出:{outPath}");
                Console.WriteLine($"  行数 {rec.LineCount}  事件 {rec.EventCount}  " +
                                  $"种子 {seeds.Length}  画像 {profiles.Length}");
            }

            Console.WriteLine($"  sha256 {FileSha256(outPath)}");
            return 0;
        }

        private static string ToolDir => Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../"));

        private sealed class Profile
        {
            public readonly string Name;
            public readonly IReadOnlyList<string> Library;
            public readonly int MaxHp;
            public readonly int StartDepth;
            public readonly IReadOnlyCollection<string> Deck;

            public Profile(string name, IReadOnlyList<string> library, int maxHp, int startDepth,
                IReadOnlyCollection<string> deck)
            {
                Name = name;
                Library = library;
                MaxHp = maxHp;
                StartDepth = startDepth;
                Deck = deck;
            }
        }

        // ---- 爬塔(与 tools/balance 的 ClimbUntilDeath 同策略,钉死的副本)----

        private static void Climb(RecipeGraph graph, CampaignConfig campaign, EndlessConfig endless,
            Profile profile, int seed, int maxDepth, TraceRecorder rec)
        {
            int towerSeed = seed * 7919 + 17;
            int fromDepth = profile.StartDepth;
            IReadOnlyList<string> library = profile.Library;
            IReadOnlyList<string> pool = new[] { "木", "木" };
            int hp = profile.MaxHp;
            rec.Run(seed, profile.Name, profile.StartDepth);

            while (fromDepth <= maxDepth)
            {
                var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                    campaign.Events, campaign.EventChancePercent);
                var battleConfig = new BattleConfig
                {
                    DropTable = campaign.DropTable,
                    PlayerMaxHp = profile.MaxHp,
                    PlayerAttack = MetaRules.AttackFor(1),
                    UnlockedChars = profile.Deck,
                };
                var run = new RunEngine(graph, runConfig, battleConfig, library, pool,
                    seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: AllLevelOne,
                    startingHp: hp);
                rec.Segment(seed, fromDepth, run.Capture().RandomState);

                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward ||
                       run.Phase == RunPhase.Event)
                {
                    int depth = fromDepth + run.BattleIndex;
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run, seed, depth, rec); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run, seed, depth, rec); continue; }

                    var battle = run.Battle;
                    rec.BattleStart(seed, depth, run.BattleIndex, battle.Enemies, battle.Capture().RandomState);

                    int turns = 0;
                    while (turns <= StallTurns)
                    {
                        if (battle.Phase == BattlePhase.DropChoice)
                        {
                            ResolveDropChoice(graph, battle, seed, depth, run.BattleIndex, rec);
                            continue;
                        }
                        if (battle.Phase != BattlePhase.PlayerTurn) break;
                        turns++;
                        rec.Turn(seed, depth, run.BattleIndex, battle.Turn, battle.Capture().RandomState,
                            battle.PlayerHp, battle.PlayerShield, battle.Library, battle.Pool, battle.Enemies);
                        PlayTurn(graph, battle, seed, depth, run.BattleIndex, rec);
                    }

                    bool stalled = turns > StallTurns;
                    rec.BattleEnd(seed, depth, run.BattleIndex, stalled ? "Stall" : battle.Phase.ToString(),
                        turns, battle.Capture().RandomState, battle.PlayerHp);
                    if (stalled) { rec.RunEnd(seed, depth, "Stall"); return; }

                    run.AdvanceAfterBattle();
                    if (depth >= maxDepth) { rec.RunEnd(seed, depth, "MaxDepth"); return; }
                }

                if (run.Phase != RunPhase.RunWon)
                {
                    rec.RunEnd(seed, fromDepth + run.BattleIndex, run.Phase.ToString());
                    return;
                }

                // 安全层:永不撤退,携带状态深入下一段(同 GameRoot.OnSegmentEnded)
                library = new List<string>(run.Battle.Library);
                pool = new List<string>(run.Battle.Pool);
                hp = run.Battle.PlayerHp;
                fromDepth += endless.BossEvery;
            }
            rec.RunEnd(seed, maxDepth, "DepthCap");
        }

        // ---- 贪心机器人 ----

        private static void PlayTurn(RecipeGraph graph, BattleEngine battle, int seed, int depth,
            int battleIndex, TraceRecorder rec)
        {
            while (battle.Ap >= 2)
            {
                var suggest = ForgeEngine.Suggest(graph, battle.Pool, battle.Library);
                string best = null;
                int bestPower = BestCastablePower(graph, battle);
                // ⚠ 确定性:Suggest 的返回顺序来自 RecipeGraph 内部 Dictionary 的枚举序,
                // 而下面是「严格大于才换」的贪心 —— 同威力时选谁完全由枚举序决定。
                // 先按序数排序把这条依赖切断(.NET 的字符串哈希在极端情况下会切到随机化路径)。
                foreach (var id in Ordered(suggest.Composable))
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
                int turn = battle.Turn;
                if (battle.Cast(pick, target) != BattleError.None) break;
                Flush(rec, seed, depth, battleIndex, turn, battle);
            }

            if (battle.Phase == BattlePhase.PlayerTurn)
            {
                int turn = battle.Turn;
                battle.EndTurn();
                Flush(rec, seed, depth, battleIndex, turn, battle);
            }
        }

        /// <summary>把这一次动作产生的结算事件落进轨迹。<see cref="BattleEngine.LastEvents"/>
        /// 只在 Cast / EndTurn 开头清空,所以**只能**紧跟这两个调用读 —— 跟在 Compose/Discard
        /// 后面读会把上一次动作的事件重复计一遍。</summary>
        private static void Flush(TraceRecorder rec, int seed, int depth, int battleIndex, int turn,
            BattleEngine battle)
        {
            foreach (var e in battle.LastEvents)
                rec.Event(seed, depth, battleIndex, turn, e);
        }

        private static void ResolveDropChoice(RecipeGraph graph, BattleEngine battle, int seed, int depth,
            int battleIndex, TraceRecorder rec)
        {
            string dropped = battle.PendingDrop;
            int droppedPower = Power(graph, dropped);
            int weakest = 0, weakestPower = int.MaxValue;
            for (int i = 0; i < battle.Library.Count; i++)
            {
                int power = Power(graph, battle.Library[i]);
                if (power < weakestPower) { weakestPower = power; weakest = i; }
            }
            if (droppedPower > weakestPower)
            {
                rec.Drop(seed, depth, battleIndex, battle.Turn, dropped, "replace" + weakest);
                battle.ResolveDrop(weakest);
            }
            else
            {
                rec.Drop(seed, depth, battleIndex, battle.Turn, dropped, "skip");
                battle.SkipDrop();
            }
        }

        private static void PickBestReward(RecipeGraph graph, RunEngine run, int seed, int depth,
            TraceRecorder rec)
        {
            var options = new List<string>(run.RewardOptions);
            var picked = new List<string>();
            while (run.Phase == RunPhase.Reward && run.CharPicksLeft > 0 && run.RewardOptions.Count > 0)
            {
                int best = 0, bestPower = -1;
                for (int i = 0; i < run.RewardOptions.Count; i++)
                {
                    int power = Power(graph, run.RewardOptions[i]);
                    if (power > bestPower) { bestPower = power; best = i; }
                }
                string candidate = run.RewardOptions[best];
                if (run.PickReward(best)) { picked.Add(candidate); continue; }

                int weakest = 0, weakestPower = int.MaxValue;
                for (int i = 0; i < run.CarriedLibrary.Count; i++)
                {
                    int power = Power(graph, run.CarriedLibrary[i]);
                    if (power < weakestPower) { weakestPower = power; weakest = i; }
                }
                if (bestPower <= weakestPower || !run.PickRewardReplacing(best, weakest)) break;
                picked.Add(candidate);
            }
            rec.Reward(seed, depth, run.BattleIndex, options, picked);
            if (run.Phase == RunPhase.Reward) run.SkipReward();
        }

        private static void ChooseBestEvent(RunEngine run, int seed, int depth, TraceRecorder rec)
        {
            string eventId = run.CurrentEvent.Id;
            var options = run.CurrentEvent.Options;
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i =>
                options[i].Ink + options[i].HpDelta * 2 + (options[i].GainChar != null ? 5 : 0)
                + options[i].GainComponents.Count - options[i].InkCost - options[i].ComponentCost);
            foreach (int i in order)
            {
                var picks = options[i].ComponentCost > 0
                    ? Enumerable.Range(0, options[i].ComponentCost).ToArray() : null;
                if (run.ChooseEventOption(i, picks)) { rec.Adventure(seed, depth, eventId, i); return; }
            }
            run.ChooseEventOption(0);
            rec.Adventure(seed, depth, eventId, 0);
        }

        private static IEnumerable<string> Ordered(IEnumerable<string> ids) =>
            ids.OrderBy(id => id, StringComparer.Ordinal);

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
            if (id == null || !graph.TryGet(id, out var def)) return 0;
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

        // ---- 参数与摘要 ----

        private static string[] Take(string[] args, string flag, int count)
        {
            int at = Array.IndexOf(args, flag);
            if (at < 0) return null;
            if (at + count >= args.Length)
                throw new ArgumentException($"{flag} 需要 {count} 个参数");
            return args.Skip(at + 1).Take(count).ToArray();
        }

        /// <summary>种子写法:<c>1,2,3</c> 或 <c>0-7</c>(闭区间)。</summary>
        private static int[] ParseSeeds(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            if (spec.Contains('-'))
            {
                var ends = spec.Split('-');
                int from = int.Parse(ends[0], CultureInfo.InvariantCulture);
                int to = int.Parse(ends[1], CultureInfo.InvariantCulture);
                return Enumerable.Range(from, to - from + 1).ToArray();
            }
            return spec.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        }

        private static string Sha256(string text) => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        private static string FileSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
