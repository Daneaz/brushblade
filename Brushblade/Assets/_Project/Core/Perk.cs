using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>三棵技能树(spec 2026-09-07)+ 跨树节点(spec 2026-09-08 §3.0)。
    ///
    /// ⚠ 「机制树」= 改玩法交互逻辑(字库/起手/稀有度/AP),「被动树」= 被动数值强化
    /// (生命/攻击/暴击/护甲)。用户 2026-09-07 明确对调过一次命名,勿按字面直觉互换。
    ///
    /// Cross 不是第四棵树,是**不属于任何一棵树**的三个咬合节点。它们的 Depth 恒为 1、
    /// Branch 各自独立,前置走 PerkNodeDef.Prereq 的显式谓词而不是同枝推导。</summary>
    public enum PerkTree { Wuxing, Passive, Mechanic, Cross }

    /// <summary>节点效果类别。前四条落在既有 BattleConfig 字段上;Element* 三条按元素筛选;
    /// 末尾五条对应五行 L4,各自挂靠 BattleEngine 里一个原本硬编码的天花板。</summary>
    public enum PerkEffect
    {
        // 被动树:落在既有 BattleConfig 字段
        MaxHp, AttackPercent, CritChance, Defense,
        // 机制树
        LibraryCapacity, StartingCards, DrawRolls, LootDrawRolls, Ap,
        // 五行树 L1/L2/L3(按元素筛选)
        ElementDrawRolls, ElementLootGuarantee, ElementEffectPercent,
        // 五行树 L4(各系专属天花板)
        MoraleCap, SummonSpeed, WellspringCap, BurnPerStack, HeftCap,
    }

    /// <summary>效果值的缩放方式(spec 2026-09-08 §5)。None = 值就是 Value(全部 40 个普通节点)。
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

    /// <summary>一个技能节点。**单级** —— 点一次即满,没有等级维度。
    ///
    /// 前置关系不存字段:同 <see cref="Tree"/> 同 <see cref="Branch"/> 的
    /// <c>Depth − 1</c> 即前置。每枝是一条直链、无交叉,显式依赖图是给任意 DAG 用的
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

        /// <summary>显式前置谓词;**null = 走「同枝 Depth−1」的隐式推导**(40 个普通节点)。
        /// 空列表 ≠ null:空列表表示「显式声明了没有前置」。</summary>
        public IReadOnlyList<PerkRequirement> Prereq { get; }

        public PerkNodeDef(string id, PerkTree tree, string branch, Element? element,
            int depth, int unlockLevel, int inkCost, PerkEffect effect, int value)
            : this(id, tree, branch, element, depth, unlockLevel, inkCost, effect, value,
                   baseValue: 0, scaling: PerkScaling.None, prereq: null)
        {
        }

        public PerkNodeDef(string id, PerkTree tree, string branch, Element? element,
            int depth, int unlockLevel, int inkCost, PerkEffect effect, int value,
            int baseValue, PerkScaling scaling, IReadOnlyList<PerkRequirement> prereq)
        {
            Id = id; Tree = tree; Branch = branch; Element = element;
            Depth = depth; UnlockLevel = unlockLevel; InkCost = inkCost;
            Effect = effect; Value = value;
            BaseValue = baseValue; Scaling = scaling; Prereq = prereq;
        }
    }

    /// <summary>技能树的表与规则(spec 2026-09-07)。纯函数,状态进出。</summary>
    public static class PerkRules
    {
        // 门槛:步长统一 6,三树错开 2 级。被动 Lv2/8/14、五行 Lv4/10/16/22、机制 Lv6/12。
        // 每 2 级亮一批新节点;Lv22 全开,留 4 级余量到 Lv26 的属性封顶。
        private static readonly int[] WuxingGates = { 4, 10, 16, 22 };
        private static readonly int[] PassiveGates = { 2, 8, 14 };
        private static readonly int[] MechanicGates = { 6, 12 };

        // 定价:按层深翻倍。机制树单价是同层的 2.7 倍 —— 它层数最少但每个节点的边际影响
        // 最大(起手 +1 张 ≈ 白拿一张字),用价格而不是层数承载分量。
        private static readonly int[] WuxingCosts = { 300, 600, 1200, 2400 };
        private static readonly int[] PassiveCosts = { 300, 600, 1200 };
        private static readonly int[] MechanicCosts = { 800, 1600 };

        public static readonly IReadOnlyList<PerkNodeDef> Nodes = BuildNodes();

        private static List<PerkNodeDef> BuildNodes()
        {
            var list = new List<PerkNodeDef>();

            // ---- 五行树:5 枝 × 4 层 ----
            // 每枝结构对称:L1/L2 治供给(改造抽卡随机)、L3/L4 谈强化。
            // 这个顺序是必须的 —— 起手强制五系各一张、战利品不筛元素,直接加成「某系字」
            // 覆盖率只有 1/5 且玩家不可控(spec §1.3)。
            AddWuxing(list, "metal", Element.Metal, PerkEffect.MoraleCap, 2);      // 战意上限 5→7
            AddWuxing(list, "wood",  Element.Wood,  PerkEffect.SummonSpeed, 40);   // 木系召唤速度 +40
            AddWuxing(list, "water", Element.Water, PerkEffect.WellspringCap, 4);  // 泉上限 10→14
            AddWuxing(list, "fire",  Element.Fire,  PerkEffect.BurnPerStack, 8);   // 灼烧每层 20→28
            AddWuxing(list, "earth", Element.Earth, PerkEffect.HeftCap, 4);        // 厚上限 10→14

            // ---- 被动树:4 枝 × 3 层 ----
            // 数值锚点:不压过等级曲线(Lv1→26 给 HP +500、ATK +50%、DEF 0→12)。
            AddPassive(list, "vigor", PerkEffect.MaxHp,         100, 200, 300); // 累计 +600 HP
            AddPassive(list, "power", PerkEffect.AttackPercent,   5,  10,  15); // 累计 +30%
            AddPassive(list, "edge",  PerkEffect.CritChance,      5,  10,  15); // 累计 +30 百分点
            AddPassive(list, "guard", PerkEffect.Defense,         2,   3,   5); // 累计 +10 点

            // ---- 机制树:4 枝 × 2 层 ----
            AddMechanic(list, "lore",    PerkEffect.LibraryCapacity, 1, 1); // 容量 7→9
            AddMechanic(list, "wide",    PerkEffect.StartingCards,   1, 1); // 起手 6→8
            // 慧眼:L1 给起手全部格 +1 次抽取,L2 给战利品候选 +1 次 —— 两层是**不同**效果。
            list.Add(new PerkNodeDef("insight_1", PerkTree.Mechanic, "insight", null,
                1, MechanicGates[0], MechanicCosts[0], PerkEffect.DrawRolls, 1));
            list.Add(new PerkNodeDef("insight_2", PerkTree.Mechanic, "insight", null,
                2, MechanicGates[1], MechanicCosts[1], PerkEffect.LootDrawRolls, 1));
            // 一气单列定价 1500/4000(沿用 19.2.3 的现价):AP 是全局资源,+2 相当于每回合
            // 多打两张牌,它的贵是既有的硬平衡线,不因改版变便宜。封顶 2 层同样是硬线。
            list.Add(new PerkNodeDef("qi_1", PerkTree.Mechanic, "qi", null,
                1, MechanicGates[0], 1500, PerkEffect.Ap, 1));
            list.Add(new PerkNodeDef("qi_2", PerkTree.Mechanic, "qi", null,
                2, MechanicGates[1], 4000, PerkEffect.Ap, 1));

            return list;
        }

        /// <summary>一条五行枝:L1 该系起手格抽取次数 +1、L2 战利品保底 1 张该系、
        /// L3 该系字效果值 +15%、L4 该系专属天花板。</summary>
        private static void AddWuxing(List<PerkNodeDef> list, string branch, Element element,
            PerkEffect topEffect, int topValue)
        {
            var effects = new[]
            {
                PerkEffect.ElementDrawRolls, PerkEffect.ElementLootGuarantee,
                PerkEffect.ElementEffectPercent, topEffect,
            };
            var values = new[] { 1, 1, 15, topValue };
            for (int i = 0; i < 4; i++)
                list.Add(new PerkNodeDef($"{branch}_{i + 1}", PerkTree.Wuxing, branch, element,
                    i + 1, WuxingGates[i], WuxingCosts[i], effects[i], values[i]));
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

        /// <summary>同枝上一层的 id;第一层返回 null。仅用于隐式前置的节点。</summary>
        private static string PrerequisiteOf(PerkNodeDef def) =>
            def.Depth <= 1 ? null : $"{def.Branch}_{def.Depth - 1}";

        /// <summary>该节点的前置是否已满足(spec 2026-09-08 §4.1)。
        ///
        /// public 而非 private:<c>Presentation.PerkView.StateOf</c> 要用**同一份**判据把
        /// 「点不了」拆成理由显示。此前那边抄了一份同枝推导,跨树节点(Depth == 1)会被它
        /// 误判成可解锁 —— 同一份逻辑两条路径,是这一层最常见的静默 bug。</summary>
        public static bool PrereqMet(MetaState meta, PerkNodeDef def)
        {
            if (def.Prereq == null)
            {
                var prereq = PrerequisiteOf(def);
                return prereq == null || IsUnlocked(meta, prereq);
            }
            foreach (var req in def.Prereq)
                if (CountOwned(meta, req.Tree, req.MinDepth) < req.Count) return false;
            return true;
        }

        /// <summary>已点亮的、属于该树且 Depth ≥ minDepth 的节点数。
        ///
        /// ⚠ 遍历固定顺序的 <see cref="Nodes"/> 而不是 <c>meta.UnlockedPerks</c> ——
        /// 后者的顺序取决于玩家点技能的先后。这里只是计数、顺序不影响结果,但与
        /// <c>MetaRules.GuaranteedLootElements</c> 保持同一条遍历习惯,免得下一个人重新推一遍。
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
            meta.Ink -= def.InkCost;
            meta.UnlockedPerks.Add(id);
            return true;
        }

        /// <summary>已点节点里该效果的值之和(不分元素)。
        ///
        /// ⚠ Scaling == None 那一支与改前**逐字相同**(sum += def.Value)—— 恒等性硬线
        /// 就靠这一行:40 个普通节点走的还是原来那条路。</summary>
        public static int Bonus(MetaState meta, PerkEffect effect)
        {
            int sum = 0;
            foreach (var id in meta.UnlockedPerks)
                if (ById.TryGetValue(id, out var def) && def.Effect == effect)
                    sum += def.Scaling == PerkScaling.None
                        ? def.Value
                        : def.BaseValue + def.Value * ScaleCountOf(meta, def);
            return sum;
        }

        /// <summary>缩放节点的计数;非缩放节点恒返回 1。
        ///
        /// public 而非 private:详情面板要把「基础 +8% / 已点亮 2 个 → +6%」拆成两行显示,
        /// 得拿到分量。**别让 Presentation 自己重算一遍计数** —— 那就是同一份逻辑两条路径。</summary>
        public static int ScaleCountOf(MetaState meta, PerkNodeDef def) => def.Scaling switch
        {
            // 已点亮的五行 L3/L4 **节点**个数(只数深层,L1/L2 是供给不是强化)
            PerkScaling.PerDeepWuxingNode => CountOwned(meta, PerkTree.Wuxing, 3),
            // 已点亮的机制树节点个数(该树只有两层,全算)
            PerkScaling.PerMechanicNode => CountOwned(meta, PerkTree.Mechanic, 1),
            // 点到 L3 或更深的五行**系**数,夹上限 2。
            // ⚠ 上限夹在这里而不是调用点:起手抽取次数有四个来源(基础 1 + 慧眼 +
            // 该系五行 L1 + 博采),夹在调用点会漏掉其中几条。
            PerkScaling.PerDeepElement => System.Math.Min(2, CountDeepElements(meta)),
            _ => 1,
        };

        /// <summary>已点节点里该效果**且属于该元素**的值之和。五行树 L1/L2/L3 与
        /// 五个 L4 都走这条 —— 它们的作用域是单系,拿 <see cref="Bonus"/> 求和会串系。
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
