using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>局外养成状态(第 19 章):角色经验/墨锭/卡等级/关卡进度。可序列化为存档。</summary>
    public sealed class MetaState
    {
        public int CharacterXp { get; set; }
        public int Ink { get; set; }                                    // 墨锭
        public Dictionary<string, int> CardLevels { get; set; } = new();  // 缺省 1 级
        public Dictionary<string, int> PerkLevels { get; set; } = new();  // 技能 id → 等级;缺省 0=未解锁
        public Dictionary<string, int> CardCopies { get; set; } = new();  // 待消耗重复卡
        public List<string> OwnedCards { get; set; } = new();             // 收集(首次获得即入)
        public List<string> Deck { get; set; } = new();                   // 出阵卡组(≤4,19.3.4)
        public List<int> ClearedStages { get; set; } = new();             // 每章已通关数
        public List<ChestState> Chests { get; set; } = new();             // 箱位队列(≤4,19.5.2)
        public List<ChestTier> PendingChests { get; set; } = new();        // 结算时箱位满的暂存箱,开箱腾位后入位(2026-07-22)
        public ShopState Shop { get; set; } = new();                      // 每日商城(19.6)
        public int BestDepth { get; set; }                                // 无尽最高层数(20.5)
        public List<string> BandMilestones { get; set; } = new();         // 已领首破奖励的层段(20.3)
        /// <summary>断点续爬快照(20.6);null=无进行中登塔。
        /// 属性改名自 Endless(2026-08-12,E-b4/E-b5 T6):量级 ×10 与「减伤百分比 → 护甲点数」
        /// 让整份登塔快照的数字全部作废(PlayerHp 50 在新上限 500 下是残血、CarriedStatuses 里的
        /// 「减伤 20」会被读成「护甲 +20 点」)。逐字段迁移没有任何旧存档样本可测,丢弃可测。
        /// 沿用 <see cref="EndlessSaveState.CarriedStatuses"/> 那次改名的同一条路径:改键名后旧的
        /// "Endless" 键变成未知键,Newtonsoft 直接忽略 —— 断点作废、回主界面可重新开塔,而
        /// MetaState 顶层的墨锭/卡等级/技能/图鉴/经验照常读出。⚠ 不是抛 JsonException 被
        /// SaveSerializer.FromJson 兜底成整份存档清空。</summary>
        public EndlessSaveState EndlessV2 { get; set; }
        public List<string> DefeatedEnemies { get; set; } = new();        // 图鉴已解锁(击败即入)
        public List<string> ClaimedBestiary { get; set; } = new();        // 图鉴已查阅领赏(主动点开才发)
    }

    /// <summary>养成规则(19.2/19.3 首版基准)。纯函数,状态进出。</summary>
    public static class MetaRules
    {
        public const int MaxCardLevel = 10;
        public const int DeckLimit = 15;          // 出阵列表上限(2026-07-19 拍板:5×3,后续可调)
        public const int DeckMinimum = 5;         // 出阵下限(2026-07-19:起手不得少于 5 字)
        public const int DeckPerElementLimit = 5; // 每属性最多 5 字(属性种类不限,3 系上限已废止)
        public const int StartingLibrarySize = 6; // 起手字库数量
        // 字库容量比起手多一格,留给回合掉字(2026-08-04):否则开局即满库,第一回合必弹 DropChoice
        public const int LibraryCapacitySlack = 1;

        /// <summary>初始收集(2026-08-05 拍板):五系 × 白/绿/蓝各一张,共 15 张 = DeckLimit。
        /// 此前是五系 2 叠紫档字(鍂/林/沝/炎/圭)——玩家一开局就握着紫档,升阶目标感缺失。
        /// 全部要求有配方(可拆可合),否则拆了回不来。</summary>
        public static readonly IReadOnlyList<string> StartingCollection = new[]
        {
            // 2026-08-25 字表重构:镰 / 梅 / 墙 随本轮移出字表,浴 升蓝、柏 降白,
            // 整张表按新的白/绿/蓝重排(spec docs/superpowers/specs/2026-08-25-字表重构-design.md)。
            "劈", "锐", "刺", // 金:攻击线(白 50 伤+战意 / 绿 穿透+40 伤 / 蓝 100 伤+穿透+偷袭)
            "柏", "松", "藤", // 木:召唤线(白 平衡 40 血 / 绿 平衡 60 血 / 蓝 控制,入场冻结)
            "冻", "海", "冷", // 水:控制线(白 冻结+30 伤 / 绿 全体 50 / 蓝 减速+90 伤)
            "灼", "烧", "爆", // 火:灼烧线(白 40 伤+1 层 / 绿 30 伤+2 层 / 蓝 全体 50+全体 1 层)
            "碾", "垒", "碎", // 土:防御与破防(白 60 伤 / 绿 盾 55+20 伤 / 蓝 90 伤+破甲 20)
        };

        /// <summary>默认出阵 = 五系蓝档各一张。恰好 5 张(≥ DeckMinimum 且 ≤ StartingLibrarySize),
        /// 所以默认出阵全部进起手字库——教程要拆的字必在手上(见 <see cref="Tutorial.DemoChar"/>)。
        /// 其余 10 张留在收集里,玩家自行换上(出阵上限 15 格装得下全部)。</summary>
        public static readonly IReadOnlyList<string> StartingDeck = new[]
        {
            "刺", "藤", "冷", "爆", "碎",
        };

        /// <summary>出阵表所需的部件(2026-08-05 拍板):部件的一切来源都从这里取,
        /// 不再是固定金木水火土——出阵表换了,能掉的部件跟着换,拿到的部件永远拼得出手里的字。
        /// 只收叶子(部件);配方里的低阶字不算,那是靠合成得来的。</summary>
        public static IEnumerable<string> DeckComponents(
            IReadOnlyList<string> deck, RecipeGraph graph)
        {
            var seen = new List<string>();
            foreach (var card in deck)
            {
                if (!graph.TryGet(card, out var def)) continue;
                foreach (var part in def.Recipe)
                    if (graph.TryGet(part, out var partDef) && partDef.IsLeaf && !seen.Contains(part))
                        seen.Add(part);
            }
            return seen;
        }

        /// <summary>登塔初始部件池:从出阵表所需部件里随机掷 <paramref name="count"/> 个(可重复)。
        /// 出阵表拼不出任何部件时返回空池——不回退到五行,那会把死牌塞回来。</summary>
        public static IReadOnlyList<string> RollStartingPool(IReadOnlyList<string> deck,
            RecipeGraph graph, GameRandom random, int count = StartingPoolSize)
        {
            var choices = new List<string>(DeckComponents(deck, graph));
            var pool = new List<string>();
            if (choices.Count == 0) return pool;
            for (int i = 0; i < count; i++)
                pool.Add(choices[random.Next(choices.Count)]);
            return pool;
        }

        public const int StartingPoolSize = 2; // 登塔起手部件数(沿用旧的两个)

        /// <summary>战斗字库容量 = 起手数量 + 掉字缓冲 + 博闻加成。「容量比起手多一格」这个关系
        /// 只在这一处定义——GameRoot 接线时调这个,不要在那边散写 +1。</summary>
        public static int LibraryCapacityFor(MetaState meta) =>
            StartingLibrarySize + LibraryCapacitySlack + PerkRules.LibraryBonus(meta);

        /// <summary>集卡升级需求(升到下一级所需同名卡,白卡基准,19.3.3)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] CopiesToUpgrade = { 2, 4, 10, 20, 40, 80, 150, 300, 500 };

        /// <summary>升级墨锭成本(白卡基准)。索引 = 当前等级 − 1。</summary>
        public static readonly int[] InkToUpgrade = { 20, 50, 120, 300, 700, 1500, 3000, 6000, 12000 };

        // 稀有度成本系数(索引 = rarity−1,白→红;19.3.3:越稀有需卡越少、墨锭越贵)
        private static readonly double[] CopiesMultiplier = { 1.0, 0.7, 0.4, 0.25, 0.15, 0.1, 0.05 };
        private static readonly double[] InkMultiplier = { 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0 };

        /// <summary>升到下一级所需同名卡(按稀有度分档:越稀有越少,向上取整,最少 1)。</summary>
        public static int CopiesRequired(int currentLevel, CardRarity rarity)
            => Math.Max(1, (int)Math.Ceiling(
                CopiesToUpgrade[currentLevel - 1] * CopiesMultiplier[(int)rarity - 1]));

        /// <summary>升到下一级所需墨锭(按稀有度分档:越稀有越贵)。</summary>
        public static int InkRequired(int currentLevel, CardRarity rarity)
            => (int)(InkToUpgrade[currentLevel - 1] * InkMultiplier[(int)rarity - 1]);

        /// <summary>角色等级:升到 n+1 级需 100 + 50×(n−1) 经验(19.2.1)。</summary>
        public static int CharacterLevel(int xp)
        {
            int level = 1;
            int cost = 100;
            while (xp >= cost)
            {
                xp -= cost;
                level += 1;
                cost += 50;
            }
            return level;
        }

        /// <summary>等级 + 本级经验进度(主界面经验条):返回等级,<paramref name="intoLevel"/> = 本级已得,
        /// <paramref name="toNextLevel"/> = 升下一级所需。
        ///
        /// ⚠ 与 <see cref="CharacterLevel"/> **共用同一条曲线**,不是另抄一遍 —— 曲线将来一改
        /// (100+50×(n−1) 这一段),两个入口必须同时变;各写各的会出现「屏上条满了却没升级」。</summary>
        public static int LevelProgress(int xp, out int intoLevel, out int toNextLevel)
        {
            int level = 1;
            int cost = 100;
            while (xp >= cost)
            {
                xp -= cost;
                level += 1;
                cost += 50;
            }
            intoLevel = xp;
            toNextLevel = cost;
            return level;
        }

        /// <summary>生命成长:500 + 20×(等级−1),上限 1000。
        /// 2026-08-12(E-b4/T1)全表量级 ×10:整数除 <c>值 × ATK ÷ 100</c> 会吃掉低数值字的
        /// 成长,被乘数必须够大才有分辨率。曲线形状与封顶级(26 级)一字未改。</summary>
        public static int MaxHpFor(int level) => Math.Min(1000, 500 + 20 * (level - 1));

        /// <summary>攻击成长:100 + 2×(等级−1),上限 150(19.2.1 角色属性)。
        /// 与 <see cref="MaxHpFor"/> 同形同封顶级(26 级)——两条角色属性曲线口径一致。
        /// 基准 100:<c>伤害 = 值 × ATK ÷ 100</c>,1 级时恒等于引入攻击力之前。</summary>
        public static int AttackFor(int level) => Math.Min(150, 100 + 2 * (level - 1));

        /// <summary>防御成长:0 + 0.5×(等级−1),上限 12(19.2.1 角色属性,2026-08-12 E-b4 T4)。
        /// 整数除表达 k = 1/2。**起点 0**:护甲是土系字给的,不是白送的 —— 也正因为起点 0,
        /// 1 级角色的战斗行为与引入这条曲线之前逐字节相同(<c>max(0, x − 0) == x</c>)。
        /// 上限 12:对参考打击量 R_in = 60 是 −20%,与 ATK 的 +50%、HP 的 +100% 同一个
        /// 「成长感」量级;再高会让等级压过字表,土系防御字失去存在意义。</summary>
        public static int DefenseFor(int level) => Math.Min(12, (level - 1) / 2);

        /// <summary>闪避成长:0 + 1×(等级−1),上限 25(19.2.1 角色属性,2026-08-12 E-b4 T4)。
        /// 起点 0 与 <see cref="DefenseFor"/> 一致(防御资源不白送,且 0 时命中判定短路、
        /// 一次随机都不摇);k = 1 而非 DEF 的 1/2,因为闪避是概率轴 —— 满级 25% 的期望减伤
        /// 与 DEF 12 对 R_in = 60 的 −20% 同量级,两条防御轴的成长感因此对齐。
        /// 上限 25 是硬要求(spec 8.3):闪避是乘性生存能力,可堆到 60%+ 会出现「摸不到我」的退化局。
        ///
        /// ⚠ 没有对应的 CritFor —— 暴击率**不随等级成长**(2026-08-12 用户裁定),
        /// 只靠字(锋)与养成技能给,见 <c>BattleConfig.PlayerCritChance</c>。</summary>
        public static int DodgeFor(int level) => Math.Min(25, level - 1);

        /// <summary>速度成长:100 + 1×(等级−1),上限 125(2026-08-15,ATB 改造)。
        /// 与其余四条同形同封顶级(26 级)。
        ///
        /// ⚠ **斜率刻意最小**。速度在 CTB 下是唯一同时翻倍「输出」与「资源产出」的属性
        /// (一次行动 = 3 AP + 1 掉字,spec 口径 5)—— 满级 +25% 已经相当于每四拍白拿一整回合,
        /// 再陡会让另外四条属性失去意义。基准 100 = 与敌人同速 = 一人一手,
        /// 1 级角色的战斗节奏与引入这条曲线之前完全一致。</summary>
        public static int SpeedFor(int level) => Math.Min(125, 100 + (level - 1));

        /// <summary>玩家生命上限 = 等级曲线 + 养元加成(19.2.1 + 第 A 章)。**生命是唯一吃技能加成
        /// 的角色属性**,所以只有它需要这层「等级 + meta」的合成,其余三条直接读曲线。
        ///
        /// ⚠ 独立成函数是因为这条表达式有**两个**调用点:战斗配置的 <c>PlayerMaxHp</c>,
        /// 与新开一次登塔时的起始 HP(满血登塔)。此前两处各抄了一遍
        /// <c>MaxHpFor(level) + PerkRules.HpBonus(meta)</c> —— 将来生命再加第二个 Bonus 项,
        /// 改一处漏一处不会有任何东西报错。</summary>
        public static int PlayerMaxHpFor(MetaState meta) =>
            MaxHpFor(CharacterLevel(meta.CharacterXp)) + PerkRules.HpBonus(meta);

        /// <summary>登塔时的战斗配置 = 角色等级派生的属性 + 养成加成 + 出阵表(19.2.1)。
        /// <paramref name="dropTable"/> 是战役内容(不是角色属性),只能由调用方传进来。
        ///
        /// ⚠ **这个函数存在的唯一理由是可测性。** 在它之前,这段映射手写在
        /// <c>GameRoot.StartSegment</c> 里 —— 而 Presentation 层没有任何自动化测试,两个工装
        /// (tools/trace、tools/balance)又各自造 BattleConfig,谁都碰不到 GameRoot。
        /// 结果是**把任意一条属性注入整行删掉,全部单元测试照旧全绿、零编译错**
        /// (2026-08-12 E-b4 T4 做变异检查时实测:删掉 PlayerDodge 那行,967 条测试无一变红)。
        /// 同一个洞覆盖了 PlayerMaxHp / PlayerAttack / PlayerDefense / PlayerDodge 四条。
        ///
        /// 所以:**GameRoot 不得再手写任何一条字段赋值**,新属性一律加在这里 ——
        /// 加在这里就有 <c>MetaRulesBattleConfigTests</c> 逐条盯着。</summary>
        public static BattleConfig BuildBattleConfig(MetaState meta, IReadOnlyList<string> dropTable)
        {
            int level = CharacterLevel(meta.CharacterXp);
            return new BattleConfig
            {
                DropTable = dropTable,
                // 生命是唯一吃养元加成的属性(19.2.1 + 第 A 章技能表)
                PlayerMaxHp = PlayerMaxHpFor(meta),
                PlayerAttack = AttackFor(level),
                PlayerDefense = DefenseFor(level),
                PlayerDodge = DodgeFor(level),
                PlayerSpeed = SpeedFor(level),
                // ⚠ 没有 PlayerCritChance:暴击**不随角色等级成长**(2026-08-12 用户裁定),
                // 缺省 0 让 RollCrit 短路、一次随机都不摇。见 BattleConfig.PlayerCritChance。
                UnlockedChars = meta.Deck, // 只能合出阵列表里的字(2026-07-20;与战利品同源)
                ApPerTurn = BaseApPerTurn + PerkRules.ApBonus(meta), // 一气
                LibraryCapacity = LibraryCapacityFor(meta), // 起手 + 掉字缓冲 + 博闻(广告 +2 在其上叠加)
            };
        }

        /// <summary>每回合基础 AP(10.1);一气技能在其上加。</summary>
        public const int BaseApPerTurn = 3;

        /// <summary>召唤槽位的解锁层(2026-08-27 用户拍板),**按下标**排:
        ///
        /// <code>
        ///   槽位   0    1    2    3        4    5    6    7
        ///   位置  前1  前2  前3  前4      后1  后2  后3  后4
        ///   层数   16    1    1   30       16   11   11   30
        /// </code>
        ///
        /// 从每排中间两格往两侧开:开局前排 2/3 号 → 11 层后排 2/3 号 → 16 层前后排 1 号
        /// → 30 层前后排 4 号。累计 2 / 4 / 6 / 8 格。
        ///
        /// ⚠ 开放集合**不是连续前缀** —— 开局能用的是槽 1、2,槽 0 还锁着。
        /// 引擎里凡是找空位、顶替、携带回位的地方都要按集合判,拿「下标 &lt; 开放数」当判据
        /// 会让第一只召唤物落进锁着的槽 0(SummonSlotUnlockTests 的
        /// OpeningDepth_FillsTheTwoMiddleFrontSlots 守着这条)。
        ///
        /// 一张表喂三个方向:<see cref="UnlockedSlotMask"/>(这层开哪几格)、
        /// <see cref="SummonSlotsFor"/>(这层开几格)、<see cref="UnlockDepthForSlot"/>
        /// (这格第几层开,UI 印它)。写成三串独立常量迟早分叉,而分叉的表现是 UI 提示一个
        /// 错的层数 —— 静默、只有肉眼能发现。</summary>
        private static readonly int[] SlotUnlockDepth = { 16, 1, 1, 30, 16, 11, 11, 30 };

        /// <summary>这一层开放哪几个召唤槽位,按位表示(bit i = 槽 i)。
        ///
        /// 依据是**当前层**,不是段起始层 —— 一段横跨解锁线时,跨过去的那一层就该多两格
        /// (RunEngine.BattleConfigForRun 逐场重算)。
        ///
        /// 层号从 1 起;≤0 一律给最低档 —— 那是调用方失误,给满开比给两格危险得多。</summary>
        public static int UnlockedSlotMask(int depth)
        {
            if (depth < 1) depth = 1;   // 层号从 1 起;≤0 是调用方失误,给最低档不给空集
            int mask = 0;
            for (int slot = 0; slot < SlotUnlockDepth.Length; slot++)
                if (depth >= SlotUnlockDepth[slot]) mask |= 1 << slot;
            return mask;
        }

        /// <summary>这一层开放几个召唤槽位(= 掩码里的位数)。</summary>
        public static int SummonSlotsFor(int depth)
        {
            if (depth < 1) depth = 1;   // 与 UnlockedSlotMask 同一条兜底
            int count = 0;
            foreach (int unlockDepth in SlotUnlockDepth)
                if (depth >= unlockDepth) count++;
            return count;
        }

        /// <summary>这一格召唤槽第几层解锁(表现层在锁着的格子上印它)。
        /// 槽号越界返回最深的那一档 —— 越界不该被读成「已解锁」。</summary>
        public static int UnlockDepthForSlot(int slot)
        {
            if (slot < 0 || slot >= SlotUnlockDepth.Length)
            {
                int deepest = 0;
                foreach (int d in SlotUnlockDepth) if (d > deepest) deepest = d;
                return deepest;
            }
            return SlotUnlockDepth[slot];
        }

        /// <summary>关卡解锁:章内顺序解锁;下一章需上一章全通。</summary>
        public static bool IsStageUnlocked(MetaState meta, CampaignConfig campaign, int chapter, int stage)
        {
            for (int c = 0; c < chapter; c++)
                if (ClearedIn(meta, c) < campaign.Chapters[c].Stages.Count)
                    return false;
            return stage <= ClearedIn(meta, chapter);
        }

        /// <summary>通关结算:首通 +50 经验并推进进度,重复 +10。返回是否首通。</summary>
        public static bool ApplyStageCleared(MetaState meta, int chapter, int stage)
        {
            bool firstClear = stage == ClearedIn(meta, chapter);
            if (firstClear)
            {
                while (meta.ClearedStages.Count <= chapter)
                    meta.ClearedStages.Add(0);
                meta.ClearedStages[chapter] += 1;
            }
            meta.CharacterXp += firstClear ? 50 : 10;
            return firstClear;
        }

        private static int ClearedIn(MetaState meta, int chapter) =>
            chapter < meta.ClearedStages.Count ? meta.ClearedStages[chapter] : 0;

        /// <summary>卡等级(缺省 1)。</summary>
        public static int CardLevel(MetaState meta, string cardId) =>
            meta.CardLevels.TryGetValue(cardId, out var level) ? level : 1;

        public static void AddCardCopies(MetaState meta, string cardId, int count)
        {
            meta.CardCopies.TryGetValue(cardId, out var current);
            meta.CardCopies[cardId] = current + count;
        }

        /// <summary>纯判定:当前重复卡与墨锭是否足以升级(UI 红点/排序用),不动状态。</summary>
        public static bool CanUpgradeCard(MetaState meta, string cardId, CardRarity rarity = CardRarity.White)
        {
            int level = CardLevel(meta, cardId);
            meta.CardCopies.TryGetValue(cardId, out var copies);
            return level < MaxCardLevel
                && copies >= CopiesRequired(level, rarity)
                && meta.Ink >= InkRequired(level, rarity);
        }

        /// <summary>集满 + 墨锭足够 → 消耗并升 1 级;否则返回 false 不动状态。成本按稀有度分档。</summary>
        public static bool TryUpgradeCard(MetaState meta, string cardId, CardRarity rarity = CardRarity.White)
        {
            int level = CardLevel(meta, cardId);
            if (level >= MaxCardLevel)
                return false;

            int copiesNeeded = CopiesRequired(level, rarity);
            int inkNeeded = InkRequired(level, rarity);
            meta.CardCopies.TryGetValue(cardId, out var copies);
            if (copies < copiesNeeded || meta.Ink < inkNeeded)
                return false;

            meta.CardCopies[cardId] = copies - copiesNeeded;
            meta.Ink -= inkNeeded;
            meta.CardLevels[cardId] = level + 1;
            return true;
        }

        /// <summary>收下一张卡:首次获得入收集,再次获得转升级重复卡(19.3.4)。</summary>
        public static void AcquireCard(MetaState meta, string cardId)
        {
            if (!meta.OwnedCards.Contains(cardId))
            {
                meta.OwnedCards.Add(cardId);
                return;
            }
            AddCardCopies(meta, cardId, 1);
        }

        /// <summary>设置出阵列表(2026-07-19 拍板):5~15 字、每属性≤5、全部已收集、无重复,
        /// 否则 false 不动状态。属性种类不限(3 系上限已废止)。无属性字计作心系一类。</summary>
        public static bool TrySetDeck(MetaState meta, IReadOnlyList<string> cards, RecipeGraph graph)
        {
            if (cards.Count > DeckLimit || cards.Count < DeckMinimum)
                return false;
            var seen = new HashSet<string>();
            var perElement = new Dictionary<Element, int>();
            foreach (var card in cards)
            {
                if (!meta.OwnedCards.Contains(card) || !seen.Add(card))
                    return false;
                var element = graph.Get(card).Element ?? Element.Heart;
                perElement.TryGetValue(element, out var count);
                perElement[element] = count + 1;
                if (perElement[element] > DeckPerElementLimit)
                    return false;
            }

            meta.Deck.Clear();
            meta.Deck.AddRange(cards);
            return true;
        }

        /// <summary>登塔起手字库:出阵列表按等级取前 6(StartingLibrarySize,起手数量);
        /// 字库基础容量是 6+1=7(LibraryCapacityFor,多出的 1 格是掉字缓冲),起手不占满。
        /// 只带自选出阵的字——自动补齐已废止(2026-07-19 拍板:没选就不上场)。</summary>
        public static IReadOnlyList<string> StartingLibrary(MetaState meta)
        {
            var roster = new List<string>();
            foreach (var card in meta.Deck)
                if (meta.OwnedCards.Contains(card) && !roster.Contains(card))
                    roster.Add(card);
            SortByLevelDesc(meta, roster);

            int startingCap = StartingLibrarySize + PerkRules.LibraryBonus(meta); // 博闻:+1 格/级(这里是起手数量上限,不吃 LibraryCapacitySlack)
            var library = new List<string>();
            foreach (var card in roster)
            {
                if (library.Count >= startingCap) break;
                library.Add(card);
            }
            return library;
        }

        private static void SortByLevelDesc(MetaState meta, List<string> cards)
        {
            cards.Sort((a, b) =>
            {
                int byLevel = CardLevel(meta, b).CompareTo(CardLevel(meta, a));
                return byLevel != 0 ? byLevel : string.CompareOrdinal(a, b);
            });
        }

        /// <summary>字表裁剪后的存档清洗:移除一切引用已下架字的条目,防启动崩溃。
        /// 货架含下架字 → 整架作废(DayStamp 重置触发重摆);奖池清空的宝箱一并移除。</summary>
        public static void PruneUnknownCards(MetaState meta, RecipeGraph graph)
        {
            bool Known(string id) => graph.TryGet(id, out _);

            meta.OwnedCards.RemoveAll(id => !Known(id));
            meta.Deck.RemoveAll(id => !Known(id));
            RemoveUnknownKeys(meta.CardLevels, Known);
            RemoveUnknownKeys(meta.CardCopies, Known);

            if (meta.Shop.CardSlots.Exists(id => !Known(id)))
            {
                meta.Shop.CardSlots.Clear();
                meta.Shop.CardSold.Clear();
                meta.Shop.DayStamp = -1;
            }

            foreach (var chest in meta.Chests)
                chest.CardPool.RemoveAll(id => !Known(id));
            meta.Chests.RemoveAll(chest => chest.CardPool.Count == 0);

            if (meta.EndlessV2 != null)
            {
                meta.EndlessV2.Library.RemoveAll(id => !Known(id));
                meta.EndlessV2.Pool.RemoveAll(id => !Known(id));
            }
        }

        private static void RemoveUnknownKeys(Dictionary<string, int> map, Func<string, bool> known)
        {
            var stale = new List<string>();
            foreach (var key in map.Keys)
                if (!known(key))
                    stale.Add(key);
            foreach (var key in stale)
                map.Remove(key);
        }

        /// <summary>卡等级数值系数:基础值 × (1 + 0.1 × (等级 − 1)),向上取整(19.3.2;
        /// 2026-07-19 floor→ceiling:低数值字升 1 级即 +1,升级可感)。</summary>
        public static int ScaleByCardLevel(int baseValue, int cardLevel)
        {
            if (cardLevel <= 1) return baseValue;
            return (int)Math.Ceiling(baseValue * (1 + 0.1 * (cardLevel - 1)));
        }

        /// <summary>叠字前置(spec 2026-08-15 Part 2):配方里的**非部件**原料必须都已收集。
        ///
        /// 只查直接原料 —— `㙓 = 土+垚` 只要求 `垚`,而拿到 `垚` 本身就得先有 `圭`,
        /// 链式约束自然成立,不需要递归。
        /// 部件(IsLeaf)不参与:它们靠掉落获得,不存在"解锁"一说。
        ///
        /// 全仓 `OwnedCards` 的写入点只有三处:`Shop.cs`、`Chest.cs`、`GameRoot.cs` 的起始集合
        /// (分支级审查核实:`RunEngine.RollRewardOptions` 只填局内战斗字库,不写
        /// `OwnedCards`)。其中商城池(`GameRoot.ShopCardPool`)= 玩家**已拥有**的非叶子字,
        /// 买卡只是给已拥有的字加重复份,天然不会绕过前置。
        /// 独立成方法仍然有价值:将来若真要给战后奖励或其他产出路径加同一条限制,
        /// 判定逻辑在这里可以直接复用。</summary>
        public static bool PrerequisitesMet(string cardId, RecipeGraph graph,
            IReadOnlyCollection<string> ownedCards)
        {
            if (graph == null || !graph.TryGet(cardId, out var def)) return false;
            foreach (var ingredient in def.Recipe)
            {
                if (!graph.TryGet(ingredient, out var idef) || idef.IsLeaf) continue;
                if (!ownedCards.Contains(ingredient)) return false;
            }
            return true;
        }
    }
}
