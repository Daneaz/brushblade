using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Balance
{
    /// <summary>难度仿真(F1 调平):贪心机器人无头跑满 3 章 × 5 关 × N 种子,
    /// 量化每关胜率/回合数/剩余血——机器人弱于人类,数据当"难度地板"读。</summary>
    public static class Program
    {
        private const int Seeds = 500;
        private const int StallTurns = 60;

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var newbie = new Profile("新手(灯,1级,HP50)",
                library: new[] { "灯" }, cardLevels: new Dictionary<string, int>(), maxHp: MetaRules.MaxHpFor(1));

            var fireCards = new[] { "灯", "炎", "烧", "燃", "灼", "炽", "焚", "焱", "燚" };
            var veteran = new Profile("养成(焚炽灼燚,卡5级,HP68)",
                library: new[] { "焚", "炽", "灼", "燚" },
                cardLevels: fireCards.ToDictionary(c => c, _ => 5), maxHp: MetaRules.MaxHpFor(10));

            var early = new Profile("小成长(灼炎烧灯,卡3级,HP54)",
                library: new[] { "灼", "炎", "烧", "灯" },
                cardLevels: fireCards.ToDictionary(c => c, _ => 3), maxHp: MetaRules.MaxHpFor(3));

            foreach (var profile in new[] { newbie, early, veteran })
            {
                Console.WriteLine($"\n## {profile.Name} × {Seeds} 种子\n");
                Console.WriteLine("| 关卡 | 胜率 | 均回合(胜) | 均剩血%(胜) | 均阵亡场次(负) | 焚均成型回合 |");
                Console.WriteLine("|---|---|---|---|---|---|");
                for (int c = 0; c < campaign.Chapters.Count; c++)
                    for (int s = 0; s < campaign.Chapters[c].Stages.Count; s++)
                        SimulateStage(graph, campaign, c, s, profile);
            }
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

        private static void SimulateStage(RecipeGraph graph, CampaignConfig campaign, int chapter, int stage, Profile profile)
        {
            int wins = 0, stalls = 0;
            long winTurns = 0, winHp = 0, lostAtBattle = 0, fenTurnSum = 0;
            int losses = 0, fenRuns = 0;

            for (int seed = 0; seed < Seeds; seed++)
            {
                var battleConfig = new BattleConfig { DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp };
                var run = new RunEngine(graph,
                    campaign.BuildRunConfig(chapter, stage, new GameRandom(seed * 7919 + 17)),
                    battleConfig, profile.Library, new[] { "木", "木" }, seed, profile.CardLevels);

                int totalTurns = 0, firstFenTurn = -1;
                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward || run.Phase == RunPhase.Event)
                {
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }

                    var battle = run.Battle;
                    int turnsThisBattle = 0;
                    while (battle.Phase == BattlePhase.PlayerTurn)
                    {
                        PlayTurn(graph, battle, totalTurns + turnsThisBattle + 1, ref firstFenTurn);
                        turnsThisBattle++;
                        if (turnsThisBattle > StallTurns) break;
                    }
                    totalTurns += turnsThisBattle;
                    if (turnsThisBattle > StallTurns) { stalls++; break; }
                    run.AdvanceAfterBattle();
                }

                if (run.Phase == RunPhase.RunWon)
                {
                    wins++;
                    winTurns += totalTurns;
                    winHp += run.Battle.PlayerHp * 100 / profile.MaxHp;
                }
                else
                {
                    losses++;
                    lostAtBattle += run.BattleIndex + 1;
                }
                if (firstFenTurn > 0) { fenRuns++; fenTurnSum += firstFenTurn; }
            }

            string stageName = $"{chapter + 1}-{stage + 1}";
            string winRate = $"{wins * 100 / Seeds}%" + (stalls > 0 ? $"(僵{stalls})" : "");
            string avgTurns = wins > 0 ? (winTurns / (double)wins).ToString("F1") : "—";
            string avgHp = wins > 0 ? $"{winHp / wins}%" : "—";
            string avgLost = losses > 0 ? (lostAtBattle / (double)losses).ToString("F1") : "—";
            string fen = fenRuns > 0 ? (fenTurnSum / (double)fenRuns).ToString("F1") : "—";
            Console.WriteLine($"| {stageName} | {winRate} | {avgTurns} | {avgHp} | {avgLost} | {fen} |");
        }

        /// <summary>贪心回合:留 1 AP 出字的前提下优先合成更强的字,然后按威力出字,末了结束回合。</summary>
        private static void PlayTurn(RecipeGraph graph, BattleEngine battle, int turnNo, ref int firstFenTurn)
        {
            // 合成:目标威力要高于手上最强,且合完还出得起字
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
                    // 字库满:丢最弱的一个再试一次
                    var weakest = battle.Library.OrderBy(id => Power(graph, id)).FirstOrDefault();
                    if (weakest == null || battle.Discard(weakest) != BattleError.None) break;
                    if (battle.Compose(best) != BattleError.None) break;
                }
                if (best == "焚" && firstFenTurn < 0) firstFenTurn = turnNo;
            }

            // 出字:威力降序,单体优先打残血
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

        /// <summary>粗略威力:伤害/灼烧数值求和(AOE ×1.5),无效果按兜底 3;仅用于机器人排序。</summary>
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

        /// <summary>目标:最低血的存活敌人(能补刀就补刀)。</summary>
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

        /// <summary>奇遇:按粗略收益挑最优且付得起的选项(收益 = 墨锭 + 血×2 + 得字5 + 部件数 − 消费)。</summary>
        private static void ChooseBestEvent(RunEngine run)
        {
            var options = run.CurrentEvent.Options;
            var order = Enumerable.Range(0, options.Count).OrderByDescending(i =>
                options[i].Ink + options[i].HpDelta * 2 + (options[i].GainChar != null ? 5 : 0)
                + options[i].GainComponents.Count - options[i].InkCost);
            foreach (int i in order)
                if (run.ChooseEventOption(i)) return;
            run.ChooseEventOption(0); // 理论不可达:事件至少有免费选项
        }
    }
}
