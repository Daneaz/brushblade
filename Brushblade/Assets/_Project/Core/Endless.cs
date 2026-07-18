using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>层段(20.3):深度轴上一段有名字有风味的区间,首破=里程碑。</summary>
    public sealed class BandDef
    {
        public string Name { get; set; }
        public int FromDepth { get; set; }
        public IReadOnlyList<EnemyDef> EnemyPool { get; set; }
        public IReadOnlyList<EnemyDef> BossPool { get; set; }
        public IReadOnlyList<IdiomBossDef> IdiomBossPool { get; set; } = System.Array.Empty<IdiomBossDef>();
        public IReadOnlyList<string> RewardPool { get; set; }
        public int MilestoneInk { get; set; }
    }

    /// <summary>成语 Boss 定义(20.7):四字成语 → 四阶段,逐字属性由配置指定。</summary>
    public sealed class IdiomBossDef
    {
        public string Chars { get; set; }
        public IReadOnlyList<Element> Elements { get; set; }
    }

    /// <summary>无尽模式配置(20.2/20.4):5 层一段第 5 层 Boss,深度线性缩放。</summary>
    public sealed class EndlessConfig
    {
        public IReadOnlyList<BandDef> Bands { get; set; }
        public int BossEvery { get; set; } = 5;
        public float ScalePerDepth { get; set; } = 0.10f;
        public float BossScaleBonus { get; set; } = 1f;

        /// <summary>该深度所在层段(FromDepth 升序,取最后一个不超过 depth 的)。</summary>
        public BandDef BandFor(int depth)
        {
            BandDef band = Bands[0];
            foreach (var candidate in Bands)
            {
                if (candidate.FromDepth > depth) break;
                band = candidate;
            }
            return band;
        }

        public bool IsBossDepth(int depth) => depth % BossEvery == 0;

        /// <summary>数值缩放(2026-07-17 仿真校准):杂兵 1 + k×(depth−1);
        /// Boss 层滞后 1 + k×(depth−BossEvery)——四阶段 Boss@1.0 ≈ 杂兵@2.0 难度,
        /// 首 Boss 必须回到 1.0 否则第 5 层即全员墙(仿真实证)。</summary>
        public float ScaleFor(int depth)
        {
            if (IsBossDepth(depth))
                return (1f + ScalePerDepth * (depth - BossEvery)) * BossScaleBonus;
            return 1f + ScalePerDepth * (depth - 1);
        }
    }

    /// <summary>遭遇生成(20.4):带种子按深度组队,同种子同编成。</summary>
    public static class EndlessGenerator
    {
        /// <summary>组装一段连战(20.2):fromDepth 至段末 Boss 层;逐层独立随机流,
        /// 断点续爬从段中恢复时后续层编成与整段生成一致(20.6)。</summary>
        public static RunConfig BuildSegment(EndlessConfig config, int fromDepth, int seed,
            IReadOnlyList<EventDef> events = null, int eventChancePercent = 0)
        {
            int segmentEnd = ((fromDepth - 1) / config.BossEvery + 1) * config.BossEvery;
            var encounters = new List<IReadOnlyList<EnemyDef>>();
            for (int depth = fromDepth; depth <= segmentEnd; depth++)
                encounters.Add(BuildFloor(config, depth, FloorRandom(seed, depth)));

            return new RunConfig
            {
                Encounters = encounters,
                RewardPool = config.BandFor(fromDepth).RewardPool,
                EventPool = events ?? Array.Empty<EventDef>(),
                EventChancePercent = eventChancePercent,
            };
        }

        /// <summary>层专属随机流:同 (塔种子, 深度) 永远同编成。</summary>
        public static GameRandom FloorRandom(int seed, int depth) =>
            new(unchecked(seed * 31 + depth * 7919));

        /// <summary>首塔剧本段(20.10 初次登入剧本化):前 3 层固定编成保证引导七拍可达成
        /// (第 1 层单敌教出字,第 2 层双敌供焚清场),4~5 层回归随机与 Boss。</summary>
        public static RunConfig BuildFirstTowerSegment(EndlessConfig config, int seed,
            IReadOnlyList<EventDef> events = null, int eventChancePercent = 0)
        {
            var segment = BuildSegment(config, 1, seed, events, eventChancePercent);
            var pool = config.Bands[0].EnemyPool;
            var lead = pool[0];
            var scripted = new List<IReadOnlyList<EnemyDef>>(segment.Encounters);
            scripted[0] = Scaled(config, 1, lead);
            scripted[1] = Scaled(config, 2, lead, lead);
            scripted[2] = Scaled(config, 3, pool[1 % pool.Count], pool[2 % pool.Count]);
            segment.Encounters = scripted;
            return segment;
        }

        /// <summary>成语 → 四阶段 Boss(20.7):数值模板对齐排山倒海——
        /// 首字均衡(12/6)、次字坚壁(15/4,承伤 0.5)、三字强攻(12/8)、末字狂攻(16/10)。</summary>
        public static EnemyDef BuildIdiomBoss(IdiomBossDef idiom)
        {
            var phases = new List<BossPhaseDef>
            {
                new(idiom.Chars[0].ToString(), idiom.Elements[0], 12, 6),
                new(idiom.Chars[1].ToString(), idiom.Elements[1], 15, 4, 0.5f),
                new(idiom.Chars[2].ToString(), idiom.Elements[2], 12, 8),
                new(idiom.Chars[3].ToString(), idiom.Elements[3], 16, 10),
            };
            return new EnemyDef(idiom.Chars, idiom.Elements[0], 12, 6, EnemyAbility.None, phases);
        }

        private static IReadOnlyList<EnemyDef> Scaled(EndlessConfig config, int depth, params EnemyDef[] enemies)
        {
            var floor = new List<EnemyDef>();
            foreach (var enemy in enemies)
                floor.Add(CampaignConfig.Scale(enemy, config.ScaleFor(depth)));
            return floor;
        }

        /// <summary>敌人数量:第 1 层单敌,每 4 层 +1,上限 4;Boss 层只出 Boss。</summary>
        public static IReadOnlyList<EnemyDef> BuildFloor(EndlessConfig config, int depth, GameRandom random)
        {
            var band = config.BandFor(depth);
            float scale = config.ScaleFor(depth);
            var floor = new List<EnemyDef>();

            if (config.IsBossDepth(depth))
            {
                int total = band.BossPool.Count + band.IdiomBossPool.Count;
                int pick = random.Next(total);
                var boss = pick < band.BossPool.Count
                    ? band.BossPool[pick]
                    : BuildIdiomBoss(band.IdiomBossPool[pick - band.BossPool.Count]);
                floor.Add(CampaignConfig.Scale(boss, scale));
                return floor;
            }

            // 辅助型(Buff)每场最多 1 只:被占用后从非辅助子池抽
            var nonSupport = new List<EnemyDef>();
            foreach (var enemy in band.EnemyPool)
                if (enemy.Ability != EnemyAbility.Buff)
                    nonSupport.Add(enemy);

            int count = 1 + Math.Min(3, (depth - 1) / 4);
            bool hasSupport = false;
            for (int i = 0; i < count; i++)
            {
                var pool = hasSupport && nonSupport.Count > 0 ? nonSupport : band.EnemyPool;
                var pick = pool[random.Next(pool.Count)];
                if (pick.Ability == EnemyAbility.Buff)
                    hasSupport = true;
                floor.Add(CampaignConfig.Scale(pick, scale));
            }
            return floor;
        }
    }

    /// <summary>断点续爬快照(20.6):层粒度,进层前写入;战斗中退出重进从当前层重打。</summary>
    public sealed class EndlessSaveState
    {
        public int Depth { get; set; }
        public int PlayerHp { get; set; }
        public List<string> Library { get; set; } = new();
        public List<string> Pool { get; set; } = new();
        public int EarnedInk { get; set; }
        public int Seed { get; set; }
        public bool LibraryExpanded { get; set; }
        public bool PoolExpanded { get; set; }
    }

    /// <summary>结算与里程碑(20.5/20.3):撤退全额、阵亡半额;首破奖励一次性、永远全额。</summary>
    public static class EndlessRules
    {
        public static int SettleInk(int earned, bool died) => died ? earned / 2 : earned;

        /// <summary>结算宝箱档位=f(结算层数)(20.8):区间内两档随机取一。</summary>
        public static ChestTier ChestTierFor(int depth, GameRandom random)
        {
            var (low, high) = depth switch
            {
                < 5 => (ChestTier.Paper, ChestTier.Bamboo),
                < 10 => (ChestTier.Bamboo, ChestTier.Celadon),
                < 20 => (ChestTier.Celadon, ChestTier.Rosewood),
                < 35 => (ChestTier.Rosewood, ChestTier.Gilded),
                < 50 => (ChestTier.Gilded, ChestTier.Gilded),
                _ => (ChestTier.Gilded, ChestTier.Crimson),
            };
            return random.Next(2) == 0 ? low : high;
        }

        /// <summary>角色经验(20.8):每层 10,Boss 层 50。</summary>
        public static int XpFor(EndlessConfig config, int depth) =>
            config.IsBossDepth(depth) ? 50 : 10;

        /// <summary>书法段位称号(11.3.2 并入 20.3):按最高层数分档。</summary>
        public static string RankTitle(int bestDepth) => bestDepth switch
        {
            < 10 => "白丁",
            < 25 => "学童",
            < 50 => "秀才",
            < 75 => "举人",
            < 100 => "进士",
            _ => "翰林",
        };

        public static void UpdateBest(MetaState meta, int depth) =>
            meta.BestDepth = Math.Max(meta.BestDepth, depth);

        /// <summary>层段首破奖励(墨锭部分;宝箱在结算层发,20.8)。已领过返回 false。</summary>
        public static bool TryAwardMilestone(MetaState meta, BandDef band)
        {
            if (meta.BandMilestones.Contains(band.Name))
                return false;
            meta.BandMilestones.Add(band.Name);
            meta.Ink += band.MilestoneInk;
            return true;
        }
    }
}
