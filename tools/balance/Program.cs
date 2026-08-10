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
        private static readonly string[] FireCards =
            { "灯", "炎", "烧", "燃", "灼", "炽", "焚", "焱", "燚", "炑", "燥", "灱" };

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            var profiles = new[]
            {
                new Profile("新手(灯,1级,HP50)", new[] { "灯" },
                    new Dictionary<string, int>(), MetaRules.MaxHpFor(1)),
                new Profile("小成长(灼炎烧灯,卡3级,HP54)", new[] { "灼", "炎", "烧", "灯" },
                    FireCards.ToDictionary(c => c, _ => 3), MetaRules.MaxHpFor(3)),
                new Profile("养成(焚炽灼燚,卡5级,HP68)", new[] { "焚", "炽", "灼", "燚" },
                    FireCards.ToDictionary(c => c, _ => 5), MetaRules.MaxHpFor(10)),
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
                    UnlockedChars = FireCards,
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
