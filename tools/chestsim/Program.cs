using System;
using System.Collections.Generic;
using System.IO;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.ChestSim
{
    /// <summary>宝箱产出仿真:量「保底后实际到手的期望张数」与「至少 1 张的箱占比」,
    /// 即《第19章》19.5.1 那两处读数的来源。
    ///
    /// 跑的是**真实的** <see cref="ChestRules.TryOpen"/> —— 权重、单箱保底、跨箱计数保底
    /// 全部走线上那一份代码,这里不另抄一套概率模型。抄一套的话,规则一改这张表就会
    /// 悄悄失真而没人发现(它此前正是这么过期的:字表从 74 字重构到 60 字,表还写着 74)。
    ///
    /// 口径:
    /// · 卡池 = 全部可收集字(非部件),同 <c>GameRoot.ChestCardPool</c>;
    /// · 起手**全收集**(OwnedCards 预置整池)—— 文档写的是「满收集池」,而叠字前置
    ///   (<c>MetaRules.PrerequisitesMet</c>)会在收集不全时收窄候选池,那量到的是新手
    ///   开局的产出,不是这张表要说的稳态;
    /// · 计数保底(金/橙/红)的计数器**跨箱累计**,所以同一档连开 N 箱,不是 N 个独立样本
    ///   —— 这正是要量的东西。</summary>
    public static class Program
    {
        private const int Chests = 200_000;
        private const int Seed = 20260905;

        private static readonly CardRarity[] Rarities =
        {
            CardRarity.White, CardRarity.Green, CardRarity.Blue, CardRarity.Purple,
            CardRarity.Gold, CardRarity.Orange, CardRarity.Red,
        };

        private sealed class FixedTime : ITimeSource
        {
            public long NowUnixSeconds => 1_000_000_000;
        }

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));

            var pool = new List<string>();
            foreach (var def in graph.All)
                if (!def.IsComponent)
                    pool.Add(def.Id);

            var poolByRarity = new Dictionary<CardRarity, int>();
            foreach (var id in pool)
                poolByRarity[graph.Get(id).Rarity] = poolByRarity.GetValueOrDefault(graph.Get(id).Rarity) + 1;

            Console.WriteLine($"卡池:{pool.Count} 个可收集字 / {Chests:N0} 箱 / seed {Seed}");
            Console.Write("  分档:");
            foreach (var rarity in Rarities)
                Console.Write($"{Name(rarity)} {poolByRarity.GetValueOrDefault(rarity)}  ");
            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("| 宝箱 | 白 | 绿 | 蓝 | 紫 | 金 | 橙 | 红 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            var atLeastOne = new Dictionary<ChestTier, double[]>();
            foreach (ChestTier tier in Enum.GetValues(typeof(ChestTier)))
            {
                var (mean, once) = Simulate(graph, pool, tier);
                atLeastOne[tier] = once;
                Console.Write($"| {ChestRules.TierName(tier)} |");
                for (int i = 0; i < Rarities.Length; i++)
                    Console.Write(mean[i] < 0.005 ? " — |" : $" {mean[i]:0.00} |");
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("「至少 1 张」的箱占比(金 / 橙 / 红):");
            foreach (var tier in new[] { ChestTier.Crimson, ChestTier.Vermilion, ChestTier.Gilded })
            {
                var once = atLeastOne[tier];
                Console.WriteLine($"  {ChestRules.TierName(tier)}  金 {once[4]:P1} / 橙 {once[5]:P1} / 红 {once[6]:P1}");
            }
        }

        private static (double[] Mean, double[] AtLeastOne) Simulate(
            RecipeGraph graph, List<string> pool, ChestTier tier)
        {
            var time = new FixedTime();
            var random = new GameRandom(Seed + (int)tier);
            var meta = new MetaState();
            meta.OwnedCards.AddRange(pool); // 满收集池:叠字前置一律满足,候选池恒为整池

            var totals = new long[Rarities.Length];
            var hits = new long[Rarities.Length];
            var seen = new bool[Rarities.Length];

            for (int n = 0; n < Chests; n++)
            {
                meta.Chests.Clear();
                meta.Chests.Add(new ChestState { Tier = tier, CardPool = pool, StartedAtUnix = 0 });
                if (!ChestRules.TryOpen(meta, 0, time, random, out var rewards, graph))
                    throw new InvalidOperationException("开箱失败 —— 箱应当恒为就绪态");

                Array.Clear(seen, 0, seen.Length);
                foreach (var card in rewards.Cards)
                {
                    int index = (int)graph.Get(card).Rarity - 1;
                    totals[index]++;
                    seen[index] = true;
                }
                for (int i = 0; i < seen.Length; i++)
                    if (seen[i]) hits[i]++;
            }

            var mean = new double[Rarities.Length];
            var once = new double[Rarities.Length];
            for (int i = 0; i < Rarities.Length; i++)
            {
                mean[i] = (double)totals[i] / Chests;
                once[i] = (double)hits[i] / Chests;
            }
            return (mean, once);
        }

        private static string Name(CardRarity rarity) => rarity switch
        {
            CardRarity.White => "白",
            CardRarity.Green => "绿",
            CardRarity.Blue => "蓝",
            CardRarity.Purple => "紫",
            CardRarity.Gold => "金",
            CardRarity.Orange => "橙",
            _ => "红",
        };
    }
}
