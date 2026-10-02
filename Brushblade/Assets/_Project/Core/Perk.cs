using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>三棵技能树(spec 2026-09-07)+ 跨树节点(spec 2026-09-08 §3.0)。
    ///
    /// ⚠ 「机制树」= 改玩法交互逻辑(字库/掉字/战利品/胜利回血,2026-10-02),「被动树」= 被动数值强化
    /// (生命/攻击/暴击/护甲)。用户 2026-09-07 明确对调过一次命名,勿按字面直觉互换。
    ///
    /// Cross 不是第四棵树,是**不属于任何一棵树**的三个咬合节点。它们的 Depth 恒为 1、
    /// Branch 各自独立,前置走 PerkNodeDef.Prereq 的显式谓词而不是同枝推导。</summary>
    public enum PerkTree { Wuxing, Passive, Mechanic, Cross }

    /// <summary>节点效果类别。被动树四条落在既有 BattleConfig 字段上;五行树各条按元素筛选
    /// (走 <see cref="PerkRules.ElementBonus"/>);机制树各条全局。
    ///
    /// ⚠ 删枚举成员在 PerkEffect 上是安全的:它**从不进存档**(存的是 UnlockedPerks 里的
    /// 节点 id 字符串),序号变动不影响任何已有存档。这与 StatusKind / EffectKind 那两条
    /// 「序号锁存档兼容」的纪律不同源,别混用。2026-09-13 删过 ElementLootGuarantee;
    /// 2026-10-02 删了 StartingCards / LootDrawRolls / Ap / ElementDrawRolls / MoraleCap /
    /// WellspringCap / BurnPerStack / HeftCap / ShieldReflectPercent。</summary>
    public enum PerkEffect
    {
        // 被动树:落在既有 BattleConfig 字段(2026-10-02 起每枝不叠加,取最高档)
        MaxHp, AttackPercent, CritChance, Defense,
        // 机制树(全局)
        LibraryCapacity,            // 博闻:字库容量 +N
        DrawRolls,                  // 起手每格抽取次数 +N(2026-10-02 起只剩跨树博采在用)
        EmptyLibraryDraws,          // 广纳/兼收:回合开始掉字时字库为空,本回合掉字 +N
        RewardOptions,              // 慧眼:战利品候选 +N
        RewardRerolls,              // 明察:每轮选字可整组重抽 N 次
        VictoryHealPercent,         // 调息/吐纳:每场战斗胜利后回复最大生命 N%
        // 五行树 L2(三段):本系字效果值 +N%
        ElementEffectPercent,
        // 五行树 L1(三段,2026-10-02):只对打出那张字的元素 = 本系生效
        ElementCritChance,          // 金:金系字暴击率 +N 百分点
        SummonHpPercent,            // 木:木系字召唤物最大生命 +N%
        HealPercent,                // 水:水系字治疗量 +N%
        ShieldPercent,              // 土:土系字护盾量 +N%
        EnemyBurnBonus,             // 火:敌人身上的灼烧每层伤害 +N(只作用于敌人侧结算)
        // 五行树 L3(单段,各系专属机制,spec 2026-09-13;2026-10-02 由 L2 挪到 L3)
        OverhealDamagePercent,      // 水:治疗溢出 ×N% 转伤害
        BurnSpreadPercent,          // 火:敌人死亡时转移剩余灼烧层数的 N%
        MoraleOnCrit,               // 金:暴击 +N 层战意(每张字至多兑现一次)
        SummonDeathHealPercent,     // 木:召唤物阵亡时玩家回复其最大生命的 N%
        ShieldCarryPercent,         // 土:战后护盾保留比例 +N 百分点(50% → 75%)
        // 五行树 L4(单段,各系天花板)
        MoraleRelease,              // 金 断金:战意满层时下一张金系字伤害 +N%
        CounterTargeting,           // 木 择伐:召唤物优先打自己克得动的敌人(开关)
        WellspringSpendPercent,     // 水 涌泉:释放泉的威力 +N%,返还所耗层数 50%
        BurnSpreadAdjacent,         // 火 燎原:灼烧结算后仍有层数则向上下左右扩散(开关)
        HeftSpendPercent,           // 土 积土:释放厚的威力 +N%,返还所耗层数 50%
    }

    /// <summary>效果值的缩放方式(spec 2026-09-08 §5)。None = 值就是 Value(全部 60 个普通段)。
    /// 其余三种:值 = BaseValue + Value × 计数,计数由本枚举决定。
    ///
    /// ⚠ PerDeepWuxingNode 数**节点**,PerDeepElement 数**系** —— 两者都叫 "deep" 但口径不同:
    /// 一系点满 4 层,前者算 2(L3、L4 各一个),后者算 1。Node / Element 这对后缀是刻意的,
    /// 别在别处简写掉。</summary>
    public enum PerkScaling
    {
        None,
        PerDeepWuxingNode,
        PerMechanicNode,
        PerDeepElement,
    }

    /// <summary>跨树节点的一条前置谓词(spec 2026-09-08 §4.1)。
    ///
    /// 是**谓词**而不是「某个具体节点的 id」—— 跨树节点要的是「任一五行 L3」,不是「金脉 L3」。
    /// 写成 id 列表就等于把它绑死在某一枝上,那正是 §3.1 要避免的「第四棵树的固定路径」。</summary>
    public sealed class PerkRequirement
    {
        public PerkTree Tree { get; }
        /// <summary>至少点到第几层(1 起)。</summary>
        public int MinDepth { get; }
        /// <summary>至少几个。</summary>
        public int Count { get; }

        public PerkRequirement(PerkTree tree, int minDepth, int count = 1)
        {
            Tree = tree; MinDepth = minDepth; Count = count;
        }
    }

    /// <summary>一个技能节点的**一段**。单段节点点一次即满;五行 L1/L2 是三段节点
    /// (2026-10-02,spec §1 方案 A):每段一个独立的 PerkNodeDef,同节点三段共用
    /// <see cref="NodeKey"/>,<see cref="Value"/> 存本段增量 —— 点满三段 = 最终值。
    ///
    /// 前置关系不存字段:三段节点第 k 段(k ≥ 2)要同节点第 k−1 段;某层第 1 段要同枝
    /// 上一层任意 ≥1 段。每枝是一条直链、无交叉,显式依赖图是给任意 DAG 用的
    /// 抽象,此处是给单次使用造抽象。</summary>
    public sealed class PerkNodeDef
    {
        public string Id { get; }
        public PerkTree Tree { get; }
        public string Branch { get; }
        /// <summary>五行树枝干对应的元素;其余两棵树为 null。</summary>
        public Element? Element { get; }
        public int Depth { get; }
        public int UnlockLevel { get; }
        public int InkCost { get; }
        public PerkEffect Effect { get; }
        public int Value { get; }

        /// <summary>缩放节点的保底值(spec 2026-09-08 §3.2);Scaling == None 时恒为 0。
        ///
        /// 存在的理由不是「避免 +0」(前置本就保证计数 ≥ 1),而是「纯缩放下的入门档
        /// 配不上定价」—— 相济 1,200 墨锭只换 +2% 攻击,同价的力 L3 给 +15%。</summary>
        public int BaseValue { get; }

        public PerkScaling Scaling { get; }

        /// <summary>显式前置谓词;**null = 走同枝/同节点的隐式推导**(60 个普通段,见 PrereqMet)。
        /// 空列表 ≠ null:空列表表示「显式声明了没有前置」。</summary>
        public IReadOnlyList<PerkRequirement> Prereq { get; }

        /// <summary>第几段(1 起);单段节点恒为 1。</summary>
        public int Stage { get; }

        /// <summary>该节点共几段:1 或 3。</summary>
        public int StageCount { get; }

        /// <summary>同节点各段共用的键(如 <c>metal_1</c>),用于字符串表 key、画布布局、图标。
        /// 单段节点 <c>NodeKey == Id</c>。</summary>
        public string NodeKey { get; }

        public PerkNodeDef(string id, PerkTree tree, string branch, Element? element,
            int depth, int unlockLevel, int inkCost, PerkEffect effect, int value)
            : this(id, tree, branch, element, depth, unlockLevel, inkCost, effect, value,
                   baseValue: 0, scaling: PerkScaling.None, prereq: null)
        {
        }

        public PerkNodeDef(string id, PerkTree tree, string branch, Element? element,
            int depth, int unlockLevel, int inkCost, PerkEffect effect, int value,
            int baseValue, PerkScaling scaling, IReadOnlyList<PerkRequirement> prereq,
            int stage = 1, int stageCount = 1, string nodeKey = null)
        {
            Id = id; Tree = tree; Branch = branch; Element = element;
            Depth = depth; UnlockLevel = unlockLevel; InkCost = inkCost;
            Effect = effect; Value = value;
            BaseValue = baseValue; Scaling = scaling; Prereq = prereq;
            Stage = stage; StageCount = stageCount; NodeKey = nodeKey ?? id;
        }
    }

    /// <summary>技能树的表与规则(spec 2026-09-07)。纯函数,状态进出。</summary>
    public static class PerkRules
    {
        // 门槛:步长统一 6,三树错开 2 级。被动 Lv2/8/14、五行 Lv4/10/16/22、机制 Lv6/12。
        // 每 2 级亮一批新节点;Lv22 全开,留 4 级余量到 Lv26 的属性封顶。
        // 五行 L1/L2 自 2026-10-02 起是三段节点,门槛/定价走下面的 StagedGates/StagedCosts,
        // WuxingGates/WuxingCosts 只用下标 2/3(L3/L4)。
        private static readonly int[] WuxingGates = { 4, 10, 16, 22 };
        private static readonly int[] PassiveGates = { 2, 8, 14 };
        private static readonly int[] MechanicGates = { 6, 12 };

        // 定价:按层深翻倍。机制树单价是同层的 2.7 倍 —— 它层数最少但每个节点的边际影响
        // 最大(起手 +1 张 ≈ 白拿一张字),用价格而不是层数承载分量。
        private static readonly int[] WuxingCosts = { 300, 600, 1200, 2400 };
        private static readonly int[] PassiveCosts = { 300, 600, 1200 };
        private static readonly int[] MechanicCosts = { 800, 1600 };

        // 五行 L1/L2 三段:L1 Lv4/6/8、墨 100/200/300;L2 Lv10/12/14、墨 200/400/600(spec 2026-10-02 §2)
        private static readonly int[][] StagedGates = { new[] { 4, 6, 8 }, new[] { 10, 12, 14 } };
        private static readonly int[][] StagedCosts = { new[] { 100, 200, 300 }, new[] { 200, 400, 600 } };

        public static readonly IReadOnlyList<PerkNodeDef> Nodes = BuildNodes();

        private static List<PerkNodeDef> BuildNodes()
        {
            var list = new List<PerkNodeDef>();

            // ---- 五行树:5 枝 × 4 层(2026-10-02 起 L1/L2 各三段,共 8 段)----
            // L1 三段(新专精)、L2 三段(原 L3:本系效果值)、L3 单段(原 L2 专属机制)、L4 单段。
            // 原 L1「该系起手格额外抽 1 次」已删除。
            AddWuxing(list, "metal", Element.Metal, PerkEffect.ElementCritChance, new[] { 5, 5, 5 },
                PerkEffect.MoraleOnCrit, 1,                // 锋芒:暴击 +1 层战意
                PerkEffect.MoraleRelease, 300);            // 断金:战意满 5 层,下一张金系字伤害 +300%
            AddWuxing(list, "wood", Element.Wood, PerkEffect.SummonHpPercent, new[] { 10, 10, 10 },
                PerkEffect.SummonDeathHealPercent, 30,     // 归根:召唤物阵亡回复其最大生命 30%
                PerkEffect.CounterTargeting, 1);           // 择伐:召唤物优先打自己克得动的敌人
            AddWuxing(list, "water", Element.Water, PerkEffect.HealPercent, new[] { 10, 10, 10 },
                PerkEffect.OverhealDamagePercent, 50,      // 溢流:治疗溢出 ×50% 转伤害
                PerkEffect.WellspringSpendPercent, 150);   // 涌泉:释放威力 +150%,返还所耗层数 50%
            AddWuxing(list, "fire", Element.Fire, PerkEffect.EnemyBurnBonus, new[] { 3, 3, 4 },
                // 余烬:死亡时全额转移剩余灼烧层数。⚠ 改这个数值(或改成非 100)要同步改
                // 字符串表的 perk.detail.burn_spread 与 perk.node.fire_3.desc 两条文案 ——
                // 它们都写死了「全部/原样转移给一名随机敌人」的口径,没有任何测试能抓到
                // 文案与数值不同步(2026-09-13 review 发现过一次「转移给下一个」的旧文案)。
                PerkEffect.BurnSpreadPercent, 100,
                PerkEffect.BurnSpreadAdjacent, 1);         // 燎原:灼烧结算后仍有层数则向上下左右扩散
            AddWuxing(list, "earth", Element.Earth, PerkEffect.ShieldPercent, new[] { 10, 10, 10 },
                PerkEffect.ShieldCarryPercent, 25,         // 固本:战后护盾保留 50% → 75%
                PerkEffect.HeftSpendPercent, 150);         // 积土:释放威力 +150%,返还所耗层数 50%

            // ---- 被动树:4 枝 × 3 层 ----
            // 2026-10-02 用户拍板:被动树**不叠加**,每枝取已点亮的最高一档(见 Bonus)。
            // 元枝点满 +1000 超出旧「不压过等级曲线 +500」锚点 —— 同日用户拍板放开,锚点作废;
            // 力/锋/御的点满值因不叠加而减半,是有意的削弱。
            AddPassive(list, "vigor", PerkEffect.MaxHp,         300, 500, 1000); // 不叠加,取最高档 1000 HP
            AddPassive(list, "power", PerkEffect.AttackPercent,   5,  10,  15); // 不叠加,取最高档 15%
            AddPassive(list, "edge",  PerkEffect.CritChance,      5,  10,  15); // 不叠加,取最高档 15 百分点
            AddPassive(list, "guard", PerkEffect.Defense,        10,  15,  25); // 不叠加,取最高档 25 点

            // ---- 机制树:4 枝 × 2 层 ----
            AddMechanic(list, "lore",    PerkEffect.LibraryCapacity,   1, 1); // 容量 7→9
            AddMechanic(list, "wide",    PerkEffect.EmptyLibraryDraws, 1, 1); // 广纳/兼收:空库掉字 +1/+2
            // 慧眼:L1 战利品候选 +1(5→6),L2 明察每轮整组重抽 1 次 —— 两层是**不同**效果。
            list.Add(new PerkNodeDef("insight_1", PerkTree.Mechanic, "insight", null,
                1, MechanicGates[0], MechanicCosts[0], PerkEffect.RewardOptions, 1));
            list.Add(new PerkNodeDef("insight_2", PerkTree.Mechanic, "insight", null,
                2, MechanicGates[1], MechanicCosts[1], PerkEffect.RewardRerolls, 1));
            AddMechanic(list, "qi",      PerkEffect.VictoryHealPercent, 5, 5); // 调息/吐纳:胜利回血 5%/10%

            // ---- 跨树节点:3 个(spec 2026-09-08 §3)----
            // 各在两棵树的扇区交界上,要两侧各一个前置才开。效果是**缩放器**:
            // 值 = BaseValue + Value × 已投资量。它放大你已有的投资,而不要求特定组合 ——
            // 写死「金脉 L3 + 锋枝 L2 = 某个具体加成」会把它变成第四棵树的固定路径。
            //
            // BaseValue 的存在理由不是「避免 +0」(前置本就保证计数 ≥ 1),而是
            // 「纯缩放下的入门档配不上定价」:相济 1,200 墨只换 +2%,同价的力 L3 给 +15%。
            // 保底值按「下限对标同价位的被动节点」定(spec §3.2)。
            list.Add(new PerkNodeDef("cross_vigor", PerkTree.Cross, "xvigor", null,
                depth: 1, unlockLevel: 16, inkCost: 1200,
                effect: PerkEffect.AttackPercent, value: 3,
                baseValue: 8, scaling: PerkScaling.PerDeepWuxingNode,
                prereq: new[]
                {
                    new PerkRequirement(PerkTree.Wuxing, minDepth: 3),
                    new PerkRequirement(PerkTree.Passive, minDepth: 2),
                }));
            list.Add(new PerkNodeDef("cross_edge", PerkTree.Cross, "xedge", null,
                depth: 1, unlockLevel: 8, inkCost: 900,
                effect: PerkEffect.CritChance, value: 3,
                baseValue: 6, scaling: PerkScaling.PerMechanicNode,
                prereq: new[]
                {
                    new PerkRequirement(PerkTree.Passive, minDepth: 2),
                    new PerkRequirement(PerkTree.Mechanic, minDepth: 1),
                }));
            // ⚠ 前置只要五行 L2,缩放却数 L3 —— 这个错位是刻意的(spec §3.4):
            // L2 口径下 1,800 墨就能吃满 +3 起手抽取,会变成预算内的标配;
            // 而前置若也提到 L3,门槛就撞上相济的 Lv16,三个跨树节点的等级梯度塌掉。
            // 保底 1 正好兜住「刚点亮时缩放计数为 0」这一档。
            list.Add(new PerkNodeDef("cross_draw", PerkTree.Cross, "xdraw", null,
                depth: 1, unlockLevel: 10, inkCost: 900,
                effect: PerkEffect.DrawRolls, value: 1,
                baseValue: 1, scaling: PerkScaling.PerDeepElement,
                prereq: new[]
                {
                    new PerkRequirement(PerkTree.Mechanic, minDepth: 1),
                    new PerkRequirement(PerkTree.Wuxing, minDepth: 2),
                }));

            return list;
        }

        /// <summary>一条五行枝(2026-10-02 层序):L1 专精三段 / L2 本系效果值三段(每段 +5%)/
        /// L3 该系专属机制 / L4 该系天花板。原 L1「该系起手格抽取次数 +1」已删除。
        ///
        /// ⚠ L1、L3、L4 都按枝传入,只有 L2 五枝同构。改这里时各层参数要成对给,别只改一个。</summary>
        private static void AddWuxing(List<PerkNodeDef> list, string branch, Element element,
            PerkEffect tierOneEffect, int[] tierOneStages,
            PerkEffect tierThreeEffect, int tierThreeValue, PerkEffect topEffect, int topValue)
        {
            AddStaged(list, branch, element, 1, tierOneEffect, tierOneStages);
            AddStaged(list, branch, element, 2, PerkEffect.ElementEffectPercent, new[] { 5, 5, 5 });
            list.Add(new PerkNodeDef($"{branch}_3", PerkTree.Wuxing, branch, element,
                3, WuxingGates[2], WuxingCosts[2], tierThreeEffect, tierThreeValue));
            list.Add(new PerkNodeDef($"{branch}_4", PerkTree.Wuxing, branch, element,
                4, WuxingGates[3], WuxingCosts[3], topEffect, topValue));
        }

        /// <summary>一个三段节点:id <c>{branch}_{depth}_s{k}</c>,NodeKey <c>{branch}_{depth}</c>,
        /// Value 存本段增量。按 Stage 升序加入 —— <see cref="StagesOf"/> 依赖这个顺序。</summary>
        private static void AddStaged(List<PerkNodeDef> list, string branch, Element element,
            int depth, PerkEffect effect, int[] stageValues)
        {
            for (int s = 0; s < 3; s++)
                list.Add(new PerkNodeDef($"{branch}_{depth}_s{s + 1}", PerkTree.Wuxing, branch, element,
                    depth, StagedGates[depth - 1][s], StagedCosts[depth - 1][s], effect, stageValues[s],
                    baseValue: 0, scaling: PerkScaling.None, prereq: null,
                    stage: s + 1, stageCount: 3, nodeKey: $"{branch}_{depth}"));
        }

        private static void AddPassive(List<PerkNodeDef> list, string branch, PerkEffect effect,
            int v1, int v2, int v3)
        {
            var values = new[] { v1, v2, v3 };
            for (int i = 0; i < 3; i++)
                list.Add(new PerkNodeDef($"{branch}_{i + 1}", PerkTree.Passive, branch, null,
                    i + 1, PassiveGates[i], PassiveCosts[i], effect, values[i]));
        }

        private static void AddMechanic(List<PerkNodeDef> list, string branch, PerkEffect effect,
            int v1, int v2)
        {
            var values = new[] { v1, v2 };
            for (int i = 0; i < 2; i++)
                list.Add(new PerkNodeDef($"{branch}_{i + 1}", PerkTree.Mechanic, branch, null,
                    i + 1, MechanicGates[i], MechanicCosts[i], effect, values[i]));
        }

        private static readonly Dictionary<string, PerkNodeDef> ById = BuildIndex();

        private static Dictionary<string, PerkNodeDef> BuildIndex()
        {
            var map = new Dictionary<string, PerkNodeDef>();
            foreach (var n in Nodes) map[n.Id] = n;
            return map;
        }

        public static PerkNodeDef Get(string id) => ById[id];

        public static bool IsUnlocked(MetaState meta, string id) => meta.UnlockedPerks.Contains(id);

        /// <summary>每个 NodeKey 一个「面」(取 Stage == 1 那段),顺序同 <see cref="Nodes"/>。
        /// 画布/计数这类按「节点」而不是按「段」看的地方用它。</summary>
        public static readonly IReadOnlyList<PerkNodeDef> Faces = BuildFaces();

        private static List<PerkNodeDef> BuildFaces()
        {
            var faces = new List<PerkNodeDef>();
            foreach (var n in Nodes) if (n.Stage == 1) faces.Add(n);
            return faces;
        }

        /// <summary>同一 NodeKey 的全部段,按 Stage 升序(Nodes 内即按此顺序加入)。</summary>
        public static IReadOnlyList<PerkNodeDef> StagesOf(string nodeKey)
        {
            var stages = new List<PerkNodeDef>();
            foreach (var n in Nodes) if (n.NodeKey == nodeKey) stages.Add(n);
            return stages;
        }

        /// <summary>该节点已点亮几段。</summary>
        public static int OwnedStageCount(MetaState meta, string nodeKey)
        {
            int n = 0;
            foreach (var def in StagesOf(nodeKey)) if (IsUnlocked(meta, def.Id)) n++;
            return n;
        }

        /// <summary>该节点的「当前段」:第一个未点亮的段;全点亮则返回末段。</summary>
        public static PerkNodeDef CurrentStage(MetaState meta, string nodeKey)
        {
            var stages = StagesOf(nodeKey);
            foreach (var def in stages) if (!IsUnlocked(meta, def.Id)) return def;
            return stages[stages.Count - 1];
        }

        /// <summary>同节点 Stage ≤ def.Stage 的各段 Value 之和(点到这一段时的累计值)。</summary>
        public static int CumulativeValue(PerkNodeDef def)
        {
            int sum = 0;
            foreach (var s in StagesOf(def.NodeKey)) if (s.Stage <= def.Stage) sum += s.Value;
            return sum;
        }

        /// <summary>该节点的前置是否已满足(spec 2026-09-08 §4.1)。
        ///
        /// public 而非 private:<c>Presentation.PerkView.StateOf</c> 要用**同一份**判据把
        /// 「点不了」拆成理由显示。此前那边抄了一份同枝推导,跨树节点(Depth == 1)会被它
        /// 误判成可解锁 —— 同一份逻辑两条路径,是这一层最常见的静默 bug。</summary>
        public static bool PrereqMet(MetaState meta, PerkNodeDef def)
        {
            if (def.Prereq == null)
            {
                // 三段节点第 k 段要同节点第 k−1 段;某层第 1 段要同枝上一层任意 ≥1 段
                if (def.Stage > 1) return IsUnlocked(meta, $"{def.NodeKey}_s{def.Stage - 1}");
                if (def.Depth <= 1) return true;
                return OwnedStageCount(meta, $"{def.Branch}_{def.Depth - 1}") > 0;
            }
            foreach (var req in def.Prereq)
                if (CountOwned(meta, req.Tree, req.MinDepth) < req.Count) return false;
            return true;
        }

        /// <summary>已点亮的、属于该树且 Depth ≥ minDepth 的节点数。
        ///
        /// ⚠ 遍历固定顺序的 <see cref="Nodes"/> 而不是 <c>meta.UnlockedPerks</c> ——
        /// 后者的顺序取决于玩家点技能的先后。这里只是计数、顺序不影响结果,但保持固定
        /// 遍历顺序这条习惯,免得下一个人重新推一遍。
        /// 顺带白拿一条:未知 id(改表后的旧档)自动被忽略,不会抛。</summary>
        public static int CountOwned(MetaState meta, PerkTree tree, int minDepth)
        {
            int n = 0;
            foreach (var def in Nodes)
                if (def.Tree == tree && def.Depth >= minDepth && IsUnlocked(meta, def.Id))
                    n++;
            return n;
        }

        /// <summary>点到 L3 或更深的五行**系**数(一系点满 4 层也只算 1)。
        ///
        /// ⚠ 遍历 <see cref="Nodes"/> 去重收集元素,而**不是** <c>Enum.GetValues(typeof(Element))</c> ——
        /// 后者的顺序由枚举声明决定,和表无关;这里要的是「表里真有的五行枝」。
        /// (`MetaRules.StartingElements` 是 private,够不着;它自己也是这个固定顺序。)</summary>
        private static int CountDeepElements(MetaState meta)
        {
            var counted = new List<Element>();
            foreach (var def in Nodes)
            {
                if (def.Tree != PerkTree.Wuxing || def.Element is not { } element) continue;
                if (def.Depth < 3 || !IsUnlocked(meta, def.Id)) continue;
                if (counted.Contains(element)) continue;   // 数系不数节点
                counted.Add(element);
            }
            return counted.Count;
        }

        public static bool CanUnlock(MetaState meta, string id)
        {
            var def = Get(id);
            if (IsUnlocked(meta, id)) return false;                                   // 已点
            if (!PrereqMet(meta, def)) return false;                                  // 前置未满足
            if (MetaRules.CharacterLevel(meta.CharacterXp) < def.UnlockLevel) return false; // 等级不足
            return meta.Ink >= def.InkCost;                                            // 墨锭足够
        }

        /// <summary>有没有任意一个节点**现在就能点**(主界面技能页签的红点)。
        /// 与 <see cref="CanUnlock"/> 同源 —— 红点亮着而点进去一个也点不了,是最烦人的假消息。</summary>
        public static bool HasUpgradable(MetaState meta)
        {
            foreach (var def in Nodes)
                if (CanUnlock(meta, def.Id))
                    return true;
            return false;
        }

        public static bool TryUnlock(MetaState meta, string id)
        {
            if (!CanUnlock(meta, id)) return false;
            var def = Get(id);
            MetaRules.SpendInk(meta, def.InkCost);
            meta.UnlockedPerks.Add(id);
            return true;
        }

        /// <summary>已点节点里该效果的值之和(不分元素)。
        ///
        /// 被动树不叠加(2026-10-02 用户拍板):每枝只取已点亮的最高一档,再与其余节点
        /// (含跨树相济/融会)之和相加。其余节点 Scaling == None 那一支仍是 sum += def.Value。</summary>
        public static int Bonus(MetaState meta, PerkEffect effect)
        {
            int sum = 0;
            Dictionary<string, int> passiveBest = null;
            foreach (var id in meta.UnlockedPerks)
            {
                if (!ById.TryGetValue(id, out var def) || def.Effect != effect) continue;
                if (def.Tree == PerkTree.Passive)
                {
                    // 被动树不叠加(2026-10-02):每枝取已点亮的最高一档
                    passiveBest ??= new Dictionary<string, int>();
                    passiveBest[def.Branch] = passiveBest.TryGetValue(def.Branch, out var cur)
                        ? System.Math.Max(cur, def.Value) : def.Value;
                    continue;
                }
                sum += def.Scaling == PerkScaling.None
                    ? def.Value
                    : def.BaseValue + def.Value * ScaleCountOf(meta, def);
            }
            if (passiveBest != null) foreach (var v in passiveBest.Values) sum += v;
            return sum;
        }

        /// <summary>缩放节点的计数;非缩放节点恒返回 1。
        ///
        /// public 而非 private:详情面板要把「基础 +8% / 已点亮 2 个 → +6%」拆成两行显示,
        /// 得拿到分量。**别让 Presentation 自己重算一遍计数** —— 那就是同一份逻辑两条路径。</summary>
        public static int ScaleCountOf(MetaState meta, PerkNodeDef def) => def.Scaling switch
        {
            // 已点亮的五行 L3/L4 **节点**个数(只数深层;L3/L4 都是单段,按段数即按节点数)
            PerkScaling.PerDeepWuxingNode => CountOwned(meta, PerkTree.Wuxing, 3),
            // 已点亮的机制树节点个数(该树只有两层,全算)
            PerkScaling.PerMechanicNode => CountOwned(meta, PerkTree.Mechanic, 1),
            // 点到 L3 或更深的五行**系**数,夹上限 2。
            // ⚠ 上限夹在这里而不是调用点(2026-10-02 起起手抽取次数只剩基础 1 + 博采)。
            PerkScaling.PerDeepElement => System.Math.Min(2, CountDeepElements(meta)),
            _ => 1,
        };

        /// <summary>已点节点里该效果**且属于该元素**的值之和。五行树四层都走这条 ——
        /// 它们的作用域是单系,拿 <see cref="Bonus"/> 求和会串系。三段节点各段 Value 是增量,
        /// 求和即累计值。
        ///
        /// ⚠ <c>ById.TryGetValue</c> 而不是索引器:存档里可能留着已删除节点的 id
        /// (改表后旧档),索引器会抛 KeyNotFoundException 让整个存档读不出来。</summary>
        public static int ElementBonus(MetaState meta, PerkEffect effect, Element element)
        {
            int sum = 0;
            foreach (var id in meta.UnlockedPerks)
                if (ById.TryGetValue(id, out var def)
                    && def.Effect == effect && def.Element == element)
                    sum += def.Value;
            return sum;
        }
    }
}
