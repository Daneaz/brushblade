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

        /// <summary>层段字奖励池。⚠️ enemies.json 里**不再配这一项**(2026-08-05 清掉死配置):
        /// 战利品只出自出阵表(2026-07-20 拍板),GameRoot 接线时无条件覆盖为 meta.Deck,
        /// 层段写死的那份从来没生效过。ConfigLoader 对缺失项给空列表(不是 null),别往回填。</summary>
        public IReadOnlyList<string> RewardPool { get; set; } = System.Array.Empty<string>();

        public int MilestoneInk { get; set; }
    }

    /// <summary>成语 Boss 定义(20.7):四字成语 → 四阶段,逐字属性由配置指定。</summary>
    public sealed class IdiomBossDef
    {
        public string Chars { get; set; }
        public IReadOnlyList<Element> Elements { get; set; }
        /// <summary>逐字技能(spec 2026-07-28);ConfigLoader 查表填好,空 = 全 None。</summary>
        public IReadOnlyList<BossSkill> Skills { get; set; } = System.Array.Empty<BossSkill>();
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
                FromDepth = fromDepth,   // 召唤槽位按当前层解锁,RunEngine 靠它换算绝对层号
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
        /// 首字均衡(180/60)、次字坚壁(230/40,护甲 60)、三字强攻(180/80)、末字狂攻(240/100)。
        /// ⚠ 这四组数是 enemies.json 里排山倒海的副本(硬编码),2026-08-12 随全表量级 ×10 一同抬起;
        /// 坚壁那阶段的承伤系数 0.5 同日随乘法层退场,换成护甲 60(E-b4 T3,与 enemies.json 的山同值)。
        /// 血量在 2026-07-29 随三只固定 Boss 一同 ×1.5:原数值下战斗只有 2~3 个敌方回合,
        /// Boss 撑不到放出大招。技能逐字取自 idiom.Skills(spec 2026-07-28),缺省为 None。</summary>
        public static EnemyDef BuildIdiomBoss(IdiomBossDef idiom)
        {
            BossSkill SkillAt(int i) =>
                idiom.Skills != null && i < idiom.Skills.Count ? idiom.Skills[i] : BossSkill.None;

            var phases = new List<BossPhaseDef>
            {
                new(idiom.Chars[0].ToString(), idiom.Elements[0], 180, 60, SkillAt(0)),
                new(idiom.Chars[1].ToString(), idiom.Elements[1], 230, 40, SkillAt(1), defense: 60),
                new(idiom.Chars[2].ToString(), idiom.Elements[2], 180, 80, SkillAt(2)),
                new(idiom.Chars[3].ToString(), idiom.Elements[3], 240, 100, SkillAt(3)),
            };
            return new EnemyDef(idiom.Chars, idiom.Elements[0], 180, 60, EnemyAbility.None, phases,
                columnSpan: Targeting.RowCapacity);
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

            // 带甲每场最多 1 只(2026-08-29,spec §4.4(a) 的完成态)。点数护甲对 AOE 有 N 倍
            // 惩罚 —— 打 N 个目标就损失 N × DEF,同场两只带甲会把群伤字直接打成废牌。
            //
            // 此前这条只靠「全表就配一只带甲杂兵」的配置口径兜着,而 BuildFloor 是**有放回**
            // 抽样:同一只墨渍能在一层里被抽中两次,实测 9.5% 的遭遇早就违反了 §4.4(a),
            // 只是没有任何测试看得见(DefenseValuesTests 那两条注释把这笔账记了半个月)。
            // 2026-08-29 补进第二只、第三只带甲怪(镇纸/铁画)时补上真正的闸,
            // 那两条测试同时从「绊线」升级成硬断言(违反率恒 0)。
            //
            // 闸叠在既有的「首位前排 / 辅助不单独成场」之后:先按老规矩选池,已经有一只带甲了
            // 再把带甲的从这一位的候选里摘掉 —— 顺序反过来会让首位强制前排失效。

            // 辅助型(Buff)每场最多 1 只,且不单独成场(2026-07-19:标点小妖自己不打人,
            // 全辅助场零威胁)——首位强制从非辅助子池抽,保证场上至少 1 只能打的
            var nonSupport = new List<EnemyDef>();
            foreach (var enemy in band.EnemyPool)
                if (enemy.Ability != EnemyAbility.Buff)
                    nonSupport.Add(enemy);

            // 首位强制前排(2026-08-20):不加这条会摇出全员后排的怪场 —— 我方单体直伤
            // 立刻全场可点,排位规则整场失效,后排怪也就失去了「够不到」这个身份。
            var frontOpeners = new List<EnemyDef>();
            foreach (var enemy in nonSupport)
                if (enemy.Row == EnemyRow.Front)
                    frontOpeners.Add(enemy);

            // 上限 8(2026-08-03:4 → 6;2026-08-27:6 → 8 = 前 4 + 后 4)。
            // 每 6 层多一只而不是每 4 层(2026-08-27 用户拍板「提到 8 但放缓节奏」):
            // 总量抬高的同时把满员深度从 21 层推到 43 层,前中期体验接近改前。
            int count = 1 + Math.Min(7, (depth - 1) / 6);
            bool hasSupport = false;
            bool hasArmored = false;
            for (int i = 0; i < count; i++)
            {
                IReadOnlyList<EnemyDef> pool;
                if (i == 0 && frontOpeners.Count > 0) pool = frontOpeners;
                else if ((i == 0 || hasSupport) && nonSupport.Count > 0) pool = nonSupport;
                else pool = band.EnemyPool;
                pool = hasArmored ? WithoutArmor(pool) : pool;
                var pick = pool[random.Next(pool.Count)];
                if (pick.Ability == EnemyAbility.Buff)
                    hasSupport = true;
                if (pick.Defense > 0)
                    hasArmored = true;
                floor.Add(CampaignConfig.Scale(pick, scale));
            }
            return floor;
        }

        /// <summary>摘掉带甲的候选;摘完为空时原样返回(抽不出来比多一只带甲更糟)。
        /// 理论上走不到空:每个层段池里都有大把无甲怪 —— 但这条守卫不能靠「反正走不到」,
        /// 与 <c>FreeColumnIn</c> 那处同一条道理。</summary>
        private static IReadOnlyList<EnemyDef> WithoutArmor(IReadOnlyList<EnemyDef> pool)
        {
            var clean = new List<EnemyDef>(pool.Count);
            foreach (var enemy in pool)
                if (enemy.Defense == 0) clean.Add(enemy);
            return clean.Count > 0 ? clean : pool;
        }
    }

    /// <summary>断点续爬快照(20.6):层粒度,进层前写入;战斗中退出重进从当前层重打。</summary>
    public sealed class EndlessSaveState
    {
        public int Depth { get; set; }
        public int PlayerHp { get; set; }
        public List<string> Library { get; set; } = new();
        public List<string> Pool { get; set; } = new();
        /// <summary>本次登塔累计已挣的墨锭,**纯展示量**(2026-08-30 起)——安全层与结算弹窗
        /// 用它告诉玩家「这趟挣了多少」。钱本身早已随赚随进账户,这个数字再怎么变都不影响余额。
        /// 半额结算取消前它是真账本(塔内滚存,结算时才入账、阵亡减半)。</summary>
        public int EarnedInk { get; set; }
        public int Seed { get; set; }
        public bool LibraryExpanded { get; set; }
        public bool PoolExpanded { get; set; }
        public bool Revived { get; set; }        // 本次登塔已用过广告复活(一次性;2026-07-24)
        public int TopBossDepth { get; set; } // 本次爬塔已破的最高 Boss 层(0=未破);结算宝箱档位据此(2026-07-22)
        /// <summary>**登塔那一刻**的历史最高层(2026-09-02):结算页「新纪录 · 43 → 45 层」左边那个数。
        ///
        /// 为什么不是结算时现读 <see cref="MetaState.BestDepth"/>:段末告捷在弹安全层**之前**就
        /// 跑了 <c>UpdateBest</c>(GameRoot.OnSegmentEnded),等玩家点「收官撤退」走到结算页,
        /// 纪录早被本次成绩刷掉了 —— 现读只会得到 45 → 45,新纪录条在唯一的胜利结局里永远不亮,
        /// 反倒只在阵亡/弃塔时亮(那两条路没有前置 UpdateBest)。语义正好反了。
        ///
        /// 为什么存在快照里而不是 GameRoot 的静态字段:挂起重进(20.6)要能接着爬,
        /// 静态字段进程一没就归零。跟着快照走,结算时快照作废,它也自然一起消失。</summary>
        public int BestDepthBeforeRun { get; set; }
        public int NormalShield { get; set; }   // 普通护盾(断点续爬恢复;整场爬塔延续)
        public int PersistShield { get; set; }   // 堡型护盾(跨段保留)
        /// <summary>携带召唤物(2026-08-03):与普通盾同口径,整场爬塔延续,直到死亡,见 20.2。</summary>
        public List<SummonSnapshot> CarriedSummons { get; set; } = new();

        /// <summary>携带减伤来源(2026-08-04):与普通盾同口径,整场爬塔延续,段末清空。
        /// 字段改名自 CarriedDamageReductions(2026-08-05):原名下 JSON 形状从
        /// Dictionary&lt;string,int&gt; 换成了 List&lt;StatusEffect&gt;,同名旧存档反序列化会
        /// 类型不匹配抛 JsonException,被 SaveSerializer.FromJson 兜底成整份 MetaState 清空
        /// (墨锭/卡等级/图鉴全丢)。改名后旧键变成未知键,Newtonsoft 直接忽略,
        /// 存档降级为「减伤丢失」而非「全清」。</summary>
        public List<StatusEffect> CarriedStatuses { get; set; } = new();

        /// <summary>段中断点(2026-07-27):非空即「上次退出时正打到一半」,读档直接接着打。
        /// 段末结算/塔结算时清空 —— 留着会让下次登塔从旧段中间开始。</summary>
        public InProgressRun InProgress { get; set; }
    }

    /// <summary>结算与里程碑(20.5/20.3)。
    ///
    /// **墨锭已经没有"结算"这一步了**(2026-08-30 用户拍板取消半额):塔内每笔收入
    /// 赚到即入账,塔结算时账上一分不少,撤退与阵亡拿到的完全一样。原先的
    /// `SettleInk(earned, died) => died ? earned / 2 : earned` 随之删除 ——
    /// 留一个恒等函数只会让人以为这里还有档可调。首破奖励照旧一次性、永远全额。</summary>
    public static class EndlessRules
    {

        /// <summary>宝箱档位=f(层数)(20.8):区间内低档 90% / 高一档 10%(2026-07-20 拍板)。</summary>
        public static ChestTier ChestTierFor(int depth, GameRandom random)
        {
            var (low, high) = depth switch
            {
                < 5 => (ChestTier.Paper, ChestTier.Bamboo),
                < 10 => (ChestTier.Bamboo, ChestTier.Celadon),
                < 20 => (ChestTier.Celadon, ChestTier.Rosewood),
                < 35 => (ChestTier.Rosewood, ChestTier.Gilded),
                < 50 => (ChestTier.Gilded, ChestTier.Vermilion),
                < 70 => (ChestTier.Vermilion, ChestTier.Crimson),
                _ => (ChestTier.Crimson, ChestTier.Crimson),
            };
            return random.Next(10) == 0 ? high : low;
        }

        /// <summary>结算宝箱依据层(2026-07-22):一场爬塔一个箱,按本次已破最高 Boss 层
        /// 定档;返回 0 表示一个 Boss 都没破 → 不发箱。阵亡/弃塔照发不降档,
        /// 故本函数不看死亡标志——档位只由 topBossDepth 决定。</summary>
        public static int SettleChestDepth(int topBossDepth) => topBossDepth > 0 ? topBossDepth : 0;

        /// <summary>层清算墨锭(2026-07-21):普通层 2、Boss 层 5,每 10 层翻倍。
        /// 2026-08-30 起**赚到即入账**(走 RunEngine.AddInk 进本段账目,由外层即时结进账户),
        /// 不再攒成滚存等塔结算 —— 那条通道存在的唯一理由是阵亡减半,减半已取消。</summary>
        public static int FloorInk(EndlessConfig config, int depth)
        {
            int tier = (depth - 1) / 10;                  // 1~10 档 0,11~20 档 1……
            int baseInk = config.IsBossDepth(depth) ? 5 : 2;
            return baseInk << tier;
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
