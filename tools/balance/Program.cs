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

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            var fireCards = new[] { "灯", "炎", "烧", "燃", "灼", "炽", "焚", "焱", "燚" };
            var profiles = new[]
            {
                new Profile("新手(灯,1级,HP50)", new[] { "灯" },
                    new Dictionary<string, int>(), MetaRules.MaxHpFor(1)),
                new Profile("小成长(灼炎烧灯,卡3级,HP54)", new[] { "灼", "炎", "烧", "灯" },
                    fireCards.ToDictionary(c => c, _ => 3), MetaRules.MaxHpFor(3)),
                new Profile("养成(焚炽灼燚,卡5级,HP68)", new[] { "焚", "炽", "灼", "燚" },
                    fireCards.ToDictionary(c => c, _ => 5), MetaRules.MaxHpFor(10)),
            };

            Console.WriteLine($"scalePerDepth={endless.ScalePerDepth} bossBonus={endless.BossScaleBonus} × {Seeds} 种子\n");
            Console.WriteLine("| 画像 | 均卒层 | P50 | P90 | 最深 | 达词渊(11) | 达文山(26) | 达墨海(51) |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            foreach (var profile in profiles)
                SimulateProfile(graph, campaign, endless, profile);
        }

        private sealed class Profile
        {
            public string Name;
            public IReadOnlyList<string> Library;
            public Dictionary<string, int> CardLevels;
            public int MaxHp;
            public Profile(string name, IReadOnlyList<string> library, Dictionary<string, int> cardLevels, int maxHp)
            { Name = name; Library = library; CardLevels = cardLevels; MaxHp = maxHp; }
        }

        private static void SimulateProfile(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile)
        {
            var deaths = new List<int>();
            foreach (int seed in Enumerable.Range(0, Seeds))
                deaths.Add(ClimbUntilDeath(graph, campaign, endless, profile, seed));

            deaths.Sort();
            double avg = deaths.Average();
            int p50 = deaths[deaths.Count / 2];
            int p90 = deaths[(int)(deaths.Count * 0.9)];
            string Reach(int band) => $"{deaths.Count(d => d >= band) * 100 / deaths.Count}%";
            Console.WriteLine($"| {profile.Name} | {avg:F1} | {p50} | {p90} | {deaths[^1]} " +
                              $"| {Reach(11)} | {Reach(26)} | {Reach(51)} |");
        }

        /// <summary>一路深入直到阵亡,返回卒层(= 阵亡所在层)。</summary>
        private static int ClimbUntilDeath(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile, int seed)
        {
            int towerSeed = seed * 7919 + 17;
            int fromDepth = 1;
            IReadOnlyList<string> library = profile.Library;
            IReadOnlyList<string> pool = new[] { "木", "木" };
            int hp = profile.MaxHp;

            while (fromDepth <= DepthCap)
            {
                var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                    campaign.Events, campaign.EventChancePercent);
                var battleConfig = new BattleConfig { DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp };
                var run = new RunEngine(graph, runConfig, battleConfig, library, pool,
                    seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: profile.CardLevels,
                    startingHp: hp);

                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward || run.Phase == RunPhase.Event)
                {
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }

                    var battle = run.Battle;
                    int turns = 0;
                    while (battle.Phase == BattlePhase.PlayerTurn && turns++ <= StallTurns)
                        PlayTurn(graph, battle);
                    if (turns > StallTurns)
                        return fromDepth + run.BattleIndex; // 僵局计为卒于当前层
                    run.AdvanceAfterBattle();
                }

                if (run.Phase != RunPhase.RunWon)
                    return fromDepth + run.BattleIndex;

                // 安全层:永不撤退,携带状态深入下一段(同 GameRoot.OnSegmentEnded)
                var carried = new List<string>(run.Battle.Library);
                carried.AddRange(run.Battle.UsedChars);
                library = carried;
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
            int best = 0, bestPower = -1;
            for (int i = 0; i < run.RewardOptions.Count; i++)
            {
                int power = Power(graph, run.RewardOptions[i]);
                if (power > bestPower) { bestPower = power; best = i; }
            }
            run.PickReward(best);
        }

        private static void ChooseBestEvent(RunEngine run)
        {
            var options = run.CurrentEvent.Options;
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i =>
                options[i].Ink + options[i].HpDelta * 2 + (options[i].GainChar != null ? 5 : 0)
                + options[i].GainComponents.Count - options[i].InkCost);
            foreach (int i in order)
                if (run.ChooseEventOption(i)) return;
            run.ChooseEventOption(0);
        }
    }
}
