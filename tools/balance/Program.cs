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

        // 三画像共享的"火系"卡池(2026-08-04):补 UnlockedChars 时用它兜底——见下方
        // ClimbUntilDeath 里的说明。
        // 2026-08-10(task-6 二轮):追加 炑/燥/灱——此前这三个新字不在这张表里,导致回合掉字
        // (StartTurn 只从 UnlockedChars 抽)、合成(Compose 同样锁 UnlockedChars)都摸不到它们,
        // 仿真对火系 DOT 三分化完全没有判别力(见 task-6-report.md 第二节)。燃/炽 已经在表里,
        // 不用重复加。真实游戏的战利品池 = 玩家已解锁卡池(enemies.json 的 endless.rewardPool
        // 是 v0.7 前的废弃字段,不该填),所以这里直接扩这张"画像卡池表",不动游戏配置。
        // ⚠ 2026-08-12:原表里的「灯」是个幽灵 —— ids.txt 有它的拆解,但《技能机制详表》
        // 里根本没有它这一行,管线从没产出过它,进不了 RecipeGraph。而它当时正是「新手」
        // 画像的**唯一**起手字,于是那一档量的是空手打(只能靠回合掉部件 + 兜底一击),
        // 与画像名声称的东西无关。换成 灼(白档,单攻 60,对灼烧目标翻倍)。
        // ⚠ 2026-08-12(E-b4 T5,spec §10.5):追加 锐 —— 不扩这张表就又重演 E-a 的
        // 「工装看不见新字」。锐 是金系而这三档画像是火系,故它是这张表里唯一的异色字:
        // 表的语义是「画像的已解锁卡池」,真实卡池本来就可以混色。
        // ⚠ 2026-09-07 字表重做 P2:锐 随字表删除,PierceBuff 全表归零载体(见
        // task-4b 报告「PierceBuff 死分支」一节)—— 这个探针原本要守的机制本身没了,
        // 不必再找字延续。但**异色字防回归**这件事本身仍然值得留 —— 那是 T5 真踩过的坑
        // (三档画像分别真实出牌 332/352/427 次)。换成本批新增的 花(木系/绿档,
        // DamageSingle 54 + Charm):既是异色字,又顺带验证「新字能被工装看见」这件事本身
        // ——花 是这一批唯一的新增字,选它当异色探针比选一张老字更贴合「防回归」的初衷。
        // Power() 的 DamageSingle/Charm 两条分支都已有记分(Charm 固定 60,魅惑注释见 Power()),
        // 不需要再补新分支。
        //
        // 兑 **不加**:它是部件不是字,而 StartTurn 的回合掉字明确只掉字
        // (「五行部件只能靠拆字获得」,BattleEngine.cs:984),把叶子塞进 UnlockedChars
        // 等于给工装造一条生产里不存在的获取路径。兑 本身也已随 锐 一并从 chars.json 移出。
        private static readonly string[] FireCards =
        // ⚠ 2026-08-25 字表重构:整表按现行火系 16 字重列。原表里 燃 自 2026-08-14 起
        // 就是幽灵字(那批裁定把它移出了详表,而这张表没跟着改),炽/炑/灱 则随本次重构移出 ——
        // 幽灵字进不了 RecipeGraph,机器人永远摸不到,等于那几档观测点是空的
        // (与上面「灯」那次同型的坑,已第二次踩)。
        // 2026-09-05 字表调整:灼/焦/烧/熣 本批移出(BurnNoDecay/DoubleVsBurning/Blind 随之休眠)。
            { "灭", "热", "爆", "炸", "燥", "烈", "蒸", "炎", "灿", "焚", "焱", "燚", "花" };

        /// <summary>水系卡池表(2026-09-02 双方向对照组)。15 张实体字全列 ——
        /// 漏掉的字机器人摸不到(回合掉字与合成都锁 UnlockedChars),那一档观测点就是空的。
        /// 从 chars.json 现读核对过,与 task-13-brief 给的表逐字一致。
        /// 2026-09-05 字表调整:沏/沝/淡 本批移出,不补新字(水系本批未新增)。
        /// 2026-09-07 字表重做 P2:浴 本批移出,同口径不补新字(水系本批也未新增)。</summary>
        private static readonly string[] WaterCards =
            { "溃", "冻", "海", "冷", "湮", "澡", "冰", "沐", "淼", "淋", "㵘" };

        /// <summary>土系卡池表(2026-09-02)。同上核对过,与 brief 一致。
        /// 2026-09-05 字表调整:砸/碾 本批移出(Sweep/Cleave 攻击形状随之休眠)。
        /// 2026-09-07 字表重做 P2:桂 从木系移入土系(design §6:「移入土系;入场护盾=
        /// 土系印记」),12 张实体字全列。</summary>
        private static readonly string[] EarthCards =
            { "碉", "垒", "壁", "崩", "堡", "碎", "塔", "圭", "杜", "垚", "桂", "㙓" };

        /// <summary>土系「有护盾面」子集(2026-09-06,P0 收尾复核 —— 给下面「木土混色」
        /// 画像专用)。从 <see cref="EarthCards"/> 里排除 `塔`/`碉`/`堡`/`桂` —— 四字的
        /// effects[].kind 都是 Summon,不产生护盾,不会攒厚(攒厚只认 Shield / ShieldAll,
        /// 见 BattleEngine 的 GainHeftForTest 调用点);混进来会稀释「护盾字密度」,削弱
        /// 这一档观测厚层数的能力。剩下 8 字全部带 Shield 或 ShieldAll(2026-09-07 数值
        /// 随字表重做 P2 重新标定):垒(30)/壁(35+反弹30)/崩(ShieldAll 21)/碎(46)/
        /// 圭(119+反弹50)/杜(109+免疫2次)/垚(149)/㙓(157,持久一次)。</summary>
        private static readonly string[] EarthShieldCards =
            { "垒", "壁", "崩", "碎", "圭", "杜", "垚", "㙓" };

        /// <summary>木系卡池表(2026-09-05):召唤流此前在仿真里**一个观测点都没有**,
        /// 而「木系每只召唤物必带被动」「召唤物攻击吃战意+厚」两条改动都落在这一系上。
        /// 实体字全列 —— 漏掉的字机器人摸不到(回合掉字与合成都锁 UnlockedChars)。
        /// 2026-09-07 字表重做 P2:葬/桤 本批移出;桂 移入土系(见 EarthCards),从下表删去;
        /// 花 是本批新增的木系字,补进来。11 张。</summary>
        private static readonly string[] WoodCards =
            { "枪", "藤", "箭", "楸", "荆", "林", "柘", "森", "藻", "花", "\ue625" };

        /// <summary>木系「召唤且攻击非 0」子集(2026-09-06,P0 收尾复核 —— 给下面「木土混色」
        /// 画像专用)。从 <see cref="WoodCards"/> 里排除三类字,已用 chars.json 逐字核过
        /// effects[].kind / attack 字段:
        /// - `花`(atk 形态,DamageSingle + Charm)—— 不是召唤字,混进来测不到「厚放大召唤物
        ///   攻击」这条链路;
        /// - `荆`/`柘`(Summon,attack 0)—— 2026-09-07 字表重做 P2 后 柘 也变成了
        ///   attack 恒 0 的纯肉盾(荆棘 + 嘲讽,design §6:tank=100,与 荆 同型),厚把 0
        ///   乘多少倍还是 0,留着会被「不动」稀释读数。
        /// - `箭` 2026-09-07 之前是伤害字(不在这份子集),4a 已把它改成召唤字(design §6:
        ///   「降蓝档补木系空缺;召唤·远程」)—— 现在**应该**进这份子集,注释与代码一起补上,
        ///   这是「注释与代码同时过时」的一个例子。
        /// 剩下 8 字全部是 Summon 且 attack &gt; 0(2026-09-07 数值随字表重做 P2 重新标定):
        /// 枪(14)/藤(21)/箭(28)/楸(39)/林(106)/森(158)/藻(187)/𣛧(130)。</summary>
        private static readonly string[] WoodSummonCards =
            { "枪", "藤", "箭", "楸", "林", "森", "藻", "\ue625" };

        // ---- 阳性对照探针(spec §10.5,2026-08-12 E-b4/E-b5 T7)----
        // 这两张卡组**不是平衡目标,是仪器的自检**:先让工装证明它能看见 DEF,再用它读数。
        // 判据只有一条:探针按预期方向动了。P50 的绝对值不是通过/失败判据。

        /// <summary>探针的起爬深度 = 词渊段首。带甲小怪墨渍(DEF 20)只在 11 层起的池子里。</summary>
        private const int ProbeStartDepth = 11;

        // 2026-09-05:铠 移出字表,点数护甲无载体,护甲画像随之下线。重新装配 DefenseBuff 时恢复。

        /// <summary>AOE 专精:全 DamageAll 且**不带任何附加效果**的字。
        /// 刻意避开 燚/焱/㵘 这类「AOE + 灼烧/治疗」的复合字 —— 混进 DOT 就分不清读数的变化
        /// 来自点数 DEF 的 N 倍惩罚还是来自灼烧,那又是一个「没变化 = 测不出来」的位置。
        ///
        /// ⚠⚠ **这档探针是「因为错误的原因通过的」,T8 不得拿它校准 AOE 轴。**
        /// 它满足 spec §10.5 写的方向(P50 低于对照),但 2026-08-13 实测变异
        /// (墨渍 DEF 20 → 0)只让它从 12.4 动到 12.6 —— 与对照那 ~2 层的差距**主要来自
        /// 字表数值**(AOE 池 50~70 vs 火系 炎 200 / 燚 300),点数 DEF 的 N 倍惩罚
        /// 只值约 **0.2 层**,淹没在噪声里。
        ///
        /// 换句话说:它测的是「AOE 字比单体字弱」,不是「点数 DEF 惩罚 AOE」。
        /// 要真正观测后者,需要一档**数值对齐的单体对照**(同基础值、同稀有度、单体 vs 群体),
        /// 现有字表凑不出来 —— 那是 T8 抬 AOE 数值时要顺带补的。
        /// 留着它是因为删了就连 0.2 层的观测点都没有,但**它的绿不构成任何证据**。</summary>
        // 2026-08-25 字表重构:淹 早已是幽灵字,洪/涛 随本次移出;纯 DamageAll 只剩 海/崩。
        private static readonly string[] AoeCards =
            { "爆", "海", "崩", "剿" };

        public static void Main()
        {
            string configDir = Path.Combine(AppContext.BaseDirectory,
                "../../../../../Brushblade/Assets/StreamingAssets/config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);
            var endless = campaign.Endless ?? throw new InvalidOperationException("enemies.json 缺少 endless 段");

            var profiles = new[]
            {
                // 四条角色属性一律由**同一个角色等级**派生(2026-08-11 E-b1 起攻击、
                // 2026-08-12 E-b4 T4 起 DEF 与闪避):画像的等级此前只体现在血量上,
                // 其余恒为基准 —— 那会让 E-b5 重平衡看不见这些成长轴。等级只传一次,
                // 从此不会出现「等级涨了但某条属性忘了跟着涨」。
                // 2026-09-05 字表调整:灼/烧 本批移出,起手牌换成 灭/灿(见 FireCards 同批注释)。
                new Profile("新手(灭,1级,HP500,ATK100,DEF0,闪0)", new[] { "灭" },
                    new Dictionary<string, int>(), level: 1),
                new Profile("小成长(灭炎爆热,卡3级,3级,HP540,ATK104,DEF1,闪2)", new[] { "灭", "炎", "爆", "热" },
                    FireCards.ToDictionary(c => c, _ => 3), level: 3),
                new Profile("养成(焚炎灿燚,卡5级,10级,HP680,ATK118,DEF4,闪9)", new[] { "焚", "炎", "灿", "燚" },
                    FireCards.ToDictionary(c => c, _ => 5), level: 10),

                // ---- 探针(spec §10.5)。等级/卡等级/起爬深度与上面基线相同,只换起手牌与卡组 ----
                // ⚠ 对照这一档是**仪器的一部分**,不是第四个平衡目标:上面三档基线全部从 1 层起爬、
                // 实测「带甲战/次」是 0.0/0.0/0.1 —— 拿它当参照物,AOE 探针的方向才判得起。
                // 2026-09-05:「探针·土系堆甲」随 ArmorCards/ArmorHand 一并下线(见上方护甲字注释)。
                new Profile("探针·对照(火系,深启11)", new[] { "焚", "炎", "灿", "燚" },
                    FireCards.ToDictionary(c => c, _ => 5), level: 10, startDepth: ProbeStartDepth),
                new Profile("探针·AOE专精(全 DamageAll,深启11)", new[] { "爆", "海", "崩", "剿" },
                    AoeCards.ToDictionary(c => c, _ => 5), level: 10,
                    ownedCards: AoeCards, startDepth: ProbeStartDepth),

                // 2026-09-02 双方向对照组:不加这两档就没有任何观测点能看见水/土的改动。
                // ⚠ ownedCards 必须显式传各自的卡池表 —— Profile.OwnedCards 缺省落回 FireCards
                // (回合掉字 + 合成锁都读它),漏传会重演「幽灵字/摸不到」那个坑,
                // 只是这次是摸到了错误系的字。
                new Profile("水系双方向(冻冰淼㵘,卡5级,10级)", new[] { "冻", "冰", "淼", "㵘" },
                    WaterCards.ToDictionary(c => c, _ => 5), level: 10, ownedCards: WaterCards),
                new Profile("土系双方向(垒圭垚㙓,卡5级,10级)", new[] { "垒", "圭", "垚", "㙓" },
                    EarthCards.ToDictionary(c => c, _ => 5), level: 10, ownedCards: EarthCards),

                // 2026-09-05 召唤流基线:定「召唤物基础攻」要先知道木系现在站在哪。
                // 与水/土两档同参数(卡5级/10级/1层起爬),四系读数才可比。
                // 2026-09-07 字表重做 P2:柘 从「攻 45」变成了「attack 恒 0」的纯肉盾
                // (荆棘+嘲讽,design §6 tank=100,与 荆 同型)——起手换成同为 Summon 且
                // attack > 0 的 森(Orange),桂 已移入土系(见 EarthCards),不再属于木系。
                new Profile("木系召唤(林森藻𣛧,卡5级,10级)", new[] { "林", "森", "藻", "\ue625" },
                    WoodCards.ToDictionary(c => c, _ => 5), level: 10, ownedCards: WoodCards),

                // 2026-09-05(P0 收尾):召唤物接战意+厚之后,纯木系卡组几乎不涨——
                // 因为纯木里既没有金系攻击字(战意来源)也没有土系护盾字(厚来源)。
                // 涨的是混色,而混色此前一个观测点都没有(计划「P0 收尾验收」待办)。
                //
                // ⚠ 2026-09-06 订正:起手牌原写 柘/圭/垚/㙓——后三字与「土系双方向」画像的
                // 起手三字完全重复,那一档实质是「土系 + 一张木字」,测不到「厚放大召唤物攻击」
                // 这条协同链路(读数因此贴着纯土系,而不是介于两者之间偏协同)。改为木系召唤字
                // (攻击非 0)+ 土系护盾字(会攒厚)各半:
                // ⚠ 2026-09-07 字表重做 P2 订正:柘 也变成了 attack 恒 0 的纯肉盾(同 荆),
                // 不再满足「攻击非 0」这条选字标准(见上面 WoodSummonCards 的类文档),换成
                // 同为 Gold 档、attack > 0 的 林;垚 也换配对档位对齐的 森(Orange 配 Orange),
                // 木侧 林(Gold,攻106)/森(Orange,攻158)——都是 Summon 且 attack > 0;
                // 土侧 圭(Gold,Shield 119)/垚(Orange,Shield 149)——都是 Shield 效果、会攒厚。
                // 四字均已用 chars.json 核过 effects[].kind 与 attack 字段,不是凭名字猜的。
                //
                // 卡池收窄成 WoodSummonCards(8 字)+ EarthShieldCards(8 字)= 16 张,
                // 而不是 WoodCards/EarthCards 两张全表拼接——全表拼接会把没被测的
                // 非召唤字(花)、攻 0 召唤字(荆/柘)、纯召唤不攒厚字(塔/碉/堡/桂)也混进
                // 抽卡池,稀释「摸到厚的来源 / 摸到会被厚放大的召唤字」的概率;但也不收窄到
                // 只剩起手那 4 张——那会把「一局里能不能连续摸到厚的来源」这个本身要观测的
                // 东西直接消掉,变成另一种失真。两张子集互不相交,ToDictionary 合并不会因
                // 重复 key 抛 ArgumentException。
                new Profile("木土混色(林森圭垚,卡5级,10级)", new[] { "林", "森", "圭", "垚" },
                    WoodSummonCards.Concat(EarthShieldCards).ToDictionary(c => c, _ => 5), level: 10,
                    ownedCards: WoodSummonCards.Concat(EarthShieldCards).ToArray()),
            };

            Console.WriteLine($"scalePerDepth={endless.ScalePerDepth} bossBonus={endless.BossScaleBonus} × {Seeds} 种子\n");
            Console.WriteLine("| 画像 | 均卒层 | P50 | P90 | 最深 | 达词渊(11) | 达文山(26) | 达墨海(51) | 带甲战/次 | 带甲多怪战/次 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var profile in profiles)
                SimulateProfile(graph, campaign, endless, profile);
        }

        private sealed class Profile
        {
            public string Name;
            public IReadOnlyList<string> Library;
            /// <summary>已解锁卡池 = BattleConfig.UnlockedChars:回合掉字的抽取源,同时锁死合成目标
            /// (2026-07-20)。此前写死成 <see cref="FireCards"/> —— 那样探针画像的起手字会被掉字
            /// 一路稀释成火系,量到的根本不是它声称的那套卡池。</summary>
            public IReadOnlyList<string> OwnedCards;
            public Dictionary<string, int> CardLevels;
            public int MaxHp;
            public int Attack;
            public int Defense;
            public int Dodge;
            /// <summary>起爬深度。三档基线一律从 1 起(它们量的是「一个号能爬多深」);
            /// 探针从 11 起(词渊段首)—— 唯一带甲的小怪墨渍只在 11 层起的池子里,
            /// 从 1 层起爬的画像**根本走不到那里**(实测三档基线的「带甲战/次」是 0.0/0.0/0.1)。
            /// 探针量的不是「能爬多深」而是「某条机制在不在」,所以直接空投到有甲的水域。</summary>
            public int StartDepth;
            public Profile(string name, IReadOnlyList<string> library, Dictionary<string, int> cardLevels,
                int level, IReadOnlyList<string> ownedCards = null, int startDepth = 1)
            {
                Name = name; Library = library; CardLevels = cardLevels; OwnedCards = ownedCards ?? FireCards;
                StartDepth = startDepth;
                MaxHp = MetaRules.MaxHpFor(level);
                Attack = MetaRules.AttackFor(level);
                Defense = MetaRules.DefenseFor(level);
                Dodge = MetaRules.DodgeFor(level);
            }
        }

        /// <summary>一次画像跑完攒下的「见没见到甲」证据(2026-08-12,E-b4/E-b5 T7)。
        ///
        /// ⚠ 为什么非要它:T5 刚踩过 —— 工装能**看见** 锐(三档画像分别真实出牌 332/352/427 次),
        /// 却**量不出**它,因为三档只爬到 10~16 层,唯一带甲的小怪(墨渍,词渊 11 层起)出现太少,
        /// PierceBuff 从 20 改到 5 读数完全不动。「没变化」和「测不出来」在仿真数据里长得一模一样,
        /// 唯一的分辨办法就是**把分母也印出来**:探针到底遇到了几次带甲目标。</summary>
        private sealed class DefExposure
        {
            public int ArmoredBattles;      // 含至少一只带甲敌人的战斗数
            public int ArmoredMultiBattles; // 且同场敌人 ≥2 —— 点数 DEF 的 N 倍惩罚只在这种场里兑现
        }

        private static void SimulateProfile(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile)
        {
            var deaths = new List<int>();
            var exposure = new DefExposure();
            foreach (int seed in Enumerable.Range(0, Seeds))
                deaths.Add(ClimbUntilDeath(graph, campaign, endless, profile, seed, exposure));

            deaths.Sort();
            double avg = deaths.Average();
            int p50 = deaths[deaths.Count / 2];
            int p90 = deaths[(int)(deaths.Count * 0.9)];
            string Reach(int band) => $"{deaths.Count(d => d >= band) * 100 / deaths.Count}%";
            Console.WriteLine($"| {profile.Name} | {avg:F1} | {p50} | {p90} | {deaths[^1]} " +
                              $"| {Reach(11)} | {Reach(26)} | {Reach(51)} " +
                              $"| {exposure.ArmoredBattles / (double)Seeds:F1} " +
                              $"| {exposure.ArmoredMultiBattles / (double)Seeds:F1} |");
        }

        /// <summary>一路深入直到阵亡,返回卒层(= 阵亡所在层)。</summary>
        private static int ClimbUntilDeath(RecipeGraph graph, CampaignConfig campaign,
            EndlessConfig endless, Profile profile, int seed, DefExposure exposure)
        {
            int towerSeed = seed * 7919 + 17;
            int fromDepth = profile.StartDepth;
            IReadOnlyList<string> library = profile.Library;
            IReadOnlyList<string> pool = new[] { "木", "木" };
            int hp = profile.MaxHp;

            while (fromDepth <= DepthCap)
            {
                var runConfig = EndlessGenerator.BuildSegment(endless, fromDepth, towerSeed,
                    campaign.Events, campaign.EventChancePercent);
                // 2026-09-07 补:生产侧 GameRoot.StartSegment 自 2026-07-20 起就无条件覆盖这一项
                // (层段写死的那份从来没生效过,见 Endless.cs 的 BandDef.RewardPool 注释)。
                // 此前工装漏了这行 —— RewardPool 恒为空池,于是 RollRewardOptions 遍历空表、
                // _rewardOptions 恒空、PickBestReward 那段循环一次都没跑过,
                // 所有历史读数都不含「战后 5 选 2」这条成长路径。
                runConfig.RewardPool = profile.OwnedCards;
                // UnlockedChars(2026-08-04 起也是回合掉字的抽取源,见 BattleEngine.StartTurn)。
                // 生产侧口径是 _meta.OwnedCards——玩家已解锁的整个卡池(2026-09-06 出阵废止后
                // MetaRules.BuildBattleConfig 直接读它)。三个画像没有各自的卡池概念,只声明了
                // 起手 Library + CardLevels,而 CardLevels 已经用 FireCards 这个 13 字火系名单
                // 给两个成长画像定过级——用它顶 UnlockedChars 是同一套"这画像已经练熟的字"口径。
                // 注意:UnlockedChars 非空时 ForgeEngine 也会用它锁合成目标(2026-07-20 拍板),
                // 即画像现在只能合成 FireCards 里的字——比改造前"不限合成"更贴近生产,
                // 但也是本次顺带激活的口径,如果后续要专门校准合成侧数值,这里可能要再调整。
                var battleConfig = new BattleConfig
                {
                    DropTable = campaign.DropTable, PlayerMaxHp = profile.MaxHp,
                    PlayerAttack = profile.Attack,
                    PlayerDefense = profile.Defense, PlayerDodge = profile.Dodge,
                    UnlockedChars = profile.OwnedCards,
                };
                var run = new RunEngine(graph, runConfig, battleConfig, library, pool,
                    seed: unchecked(towerSeed * 17 + fromDepth), cardLevels: profile.CardLevels,
                    startingHp: hp);

                while (run.Phase == RunPhase.InBattle || run.Phase == RunPhase.Reward || run.Phase == RunPhase.Event)
                {
                    if (run.Phase == RunPhase.Reward) { PickBestReward(graph, run); continue; }
                    if (run.Phase == RunPhase.Event) { ChooseBestEvent(run); continue; }

                    var battle = run.Battle;
                    // 「见没见到甲」的分母(每场战斗记一次;这一行不消耗任何随机数)。
                    // ⚠ 只数**开战时**就带甲的敌人 = 小怪墨渍(词渊 11 层起,DEF 20)。
                    // Boss 的带甲阶段(山 60 / 江 30 / 钧 30)不计:它们是单敌战,
                    // 点数 DEF 的 N 倍惩罚在单敌场里根本不兑现,对 AOE 探针没有判别力。
                    if (battle.Enemies.Any(e => e.Defense > 0))
                    {
                        exposure.ArmoredBattles++;
                        if (battle.Enemies.Count >= 2) exposure.ArmoredMultiBattles++;
                    }
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
                    // 2026-09-02:护盾/治疗改记全额 —— 它们现在攒势/水势,
                    // 折半记分是「防御没有进攻价值」那个旧模型的残留。
                    case EffectKind.Shield: sum += e.Value; break;
                    case EffectKind.BurnPotency: sum += e.Value * 2; break;
                    case EffectKind.HealSelf: sum += e.Value; break;
                    case EffectKind.HealAll: sum += e.Value; break;
                    case EffectKind.Summon: sum += (e.Value + e.SummonAttack * 3) * e.SummonCount / 2; break;
                    // 穿透(2026-08-12,E-b4 T5,锐):按点数**等价折算成伤害**,不加权。
                    // 它本场持久、每次攻击都兑现,理应比一次性伤害值钱;但全表只有 墨渍(DEF 20)
                    // 与 3 个 Boss 阶段(30/30/60)有甲,对其余敌人它一分钱不值。等价折算是这两头
                    // 之间的保守中点 —— 排在 灼(60)之后,机器人先打伤害再攒穿透。
                    case EffectKind.PierceBuff: sum += e.Value; break;
                    // 护甲(2026-08-12,E-b4/E-b5 T7,土系堆甲探针):按点数 ×2 折算。
                    // ⚠ **系数是多少不重要,是不是 0 才重要**:记 0 分的字机器人永远不会去
                    // 合成它(Compose 那条分支要求 power 严格大于库里最强的),那与「没把它加进
                    // 卡池表」完全等价 —— 正是 焰 变异检查轨迹毫无反应踩过的坑。没有这一条,
                    // 土系堆甲探针就是个装饰品:它会握着一手防御字一张都不出。
                    // ×2 的口径同 BurnSingle:本场持久、每记挥击都兑现,但只在挨打时兑现,
                    // 所以排在同数值的直伤之后(铠 12 → 24 分,仍低于 碾 的 60)。
                    case EffectKind.DefenseBuff: sum += e.Value * 2; break;
                    // 厚/泉的终极技(2026-09-02;2026-09-04 由 SpendMomentum/SpendWaterPower 改名 ——
                    // 工装没跟着改,自那天起整个仿真编译不过、无人可跑,2026-09-05 发现)。
                    // 按满层折算:机器人不模拟攒层过程,给个中位估值让它至少会去出这张字。
                    // 系数是多少不重要,**是不是 0 才重要**。
                    case EffectKind.SpendHeft: sum += e.Value * 5; break;
                    case EffectKind.SpendWellspring: sum += e.Value * 5; break;
                    // 群体护盾(2026-09-05,崩):不记分的话 崩 的护面是 0 分,
                    // 土系画像会握着它一张都不出 —— 与「没加进卡池表」等价。
                    case EffectKind.ShieldAll: sum += e.Value; break;
                    // 魅惑(2026-09-05,花):按「一次敌人攻击转成对敌伤害」估值。
                    // 系数是多少不重要,**是不是 0 才重要** —— 记 0 分的字机器人永远不会出,
                    // 那与「没把它加进出阵表」完全等价(注释里那条踩过三次的坑)。
                    case EffectKind.Charm: sum += 60; break;

                    // ================== 2026-09-08(T5 头号任务)补齐控制类/增益类计分 ==================
                    // 在此之前,下面这些 kind 一律落到 switch 的默认分支、贡献 0 分——Power() 自己的
                    // 三条既有注释都点名过同一条规矩:「系数是多少不重要,是不是 0 才重要」。0 分的
                    // 字机器人永远不会去合成(Compose 要求 power 严格大于库存最强的)、永远优先被
                    // 当「库中最弱」弃掉(ResolveDropChoice/PickTarget 前那个弃字循环)。这里统一借用
                    // 已有的 Charm 案例反推一把换算尺:Charm 定价 0.40(design §2.2)记 60 分,
                    // 即 **150 分 / 1.0 价格单位**,下面每一条都用这同一把尺子换算 design §2.2/§1.4.1
                    // 的价目表,只求方向对、非零,不追求跟实付价目分毫不差。
                    //
                    // ⚠ 这批 case 全部只读 e.Value/e.Turns 这些**字段**,不读“这条效果具体挂在哪张
                    // 字身上”——所以它们对**双方向字的攻击面**(AttackEffects,水系 11 张的 Silence/
                    // Freeze/Slow/ArmorBreak 全在这里)没有任何效果:Power() 的 foreach 只扫
                    // `def.Effects`,双方向字的 AttackEffects 从来不在这个循环里出现过一次。
                    // 这不是本次改动的疏漏,是本次改动能触及的范围的边界——详见 T5 报告「头号任务」
                    // 一节:真正锁死水系输出的是 tools/balance 的机器人从未以 attackMode=true 调用过
                    // battle.Cast,那是 PlayTurn 的选字/施放逻辑,不是 Power() 的计分逻辑,brief 只
                    // 放行「补齐 Power() 的计分」,没有放行改 PlayTurn。

                    // 冻结(design §2.2「冻结 1 回合 0.35」)≈52 分/回合,按 Value(跳过的回合数)计。
                    case EffectKind.Freeze: sum += e.Value * 50; break;
                    // 减速(design §2.2「减速 2 回合 0.30」≈45 分对应 2 回合,即 ~22 分/回合)。
                    case EffectKind.Slow: sum += e.Value * 25; break;
                    // 破甲:与 DefenseBuff 同一条价目轴(design §2.2 两条都是 0.33),对称给
                    // 同样的 ×2 折算——护甲增/减本质是同一个点数机制的正负两面。
                    case EffectKind.ArmorBreak: sum += e.Value * 2; break;
                    // 免疫(design §2.2「免疫 1 次 0.35 · 2 次 0.60」),按挡住的次数计,
                    // 第 1 次≈52、第 2 次边际下降到≈38,这里不追求分段精确,按每次 50 折算。
                    case EffectKind.Immunity: sum += e.Value * 50; break;
                    // 复活(design §2.2「复活 1 名 0.40」)与 Charm 同一档价格,直接复用 60 分/名。
                    case EffectKind.Revive: sum += e.Value * 60; break;
                    // 致盲(design §2.2「致盲 0.30」,当前 0 载体——按 CritBuff 同款「每点命中率
                    // 换 3 分」估值,预防将来复活时又落回 0 分)。
                    case EffectKind.Blind: sum += e.Value * 3; break;
                    // 封禁(design §2.2「封禁 1 回合 / 2 回合 0.38 / 0.52」)。现存载体 Value 恒为 0
                    // (强度全在 Turns 上),按回合数分段估值:1 回合≈57、2 回合≈78。
                    case EffectKind.Silence: sum += e.Turns >= 2 ? 78 : 57; break;
                    // 反弹(design §2.2「反弹 50%×2 0.35」≈52.5,对应 圭 的 Value=50/Turns=2 那一档,
                    // 折算成 Value×Turns/2,壁 的 30%×2 按同公式给 30 分)。
                    case EffectKind.Reflect: sum += e.Value * Math.Max(1, e.Turns) / 2; break;
                    // 引爆:design §2.2 没有单独定价这一条(它是「灼烧威力」轴上的收网动作,
                    // 真正的伤害当量已经由同一张字前面铺的 BurnAll/BurnPotency 计过分)。
                    // 这里给一个适中的固定估值,只为避免「引爆」这个 kind 本身挂零分。
                    case EffectKind.Detonate: sum += 40; break;
                    // 增攻(design §2.2「攻击 +50 0.45」≈67.5/50 ≈ 1.35 分每点,取 1.5 就近折算)。
                    case EffectKind.Empower: sum += e.Value * 3 / 2; break;
                    // 战意(design §2.2「战意(印记外)0.10 每层」≈15 分/层,Value = 施放当下
                    // 立即获得的层数)。
                    case EffectKind.Morale: sum += e.Value * 15; break;
                    // 暴击(design §2.2「暴击 +20% 0.40」,20%→60 分,与 Charm 同一价格,
                    // 即每点百分比 3 分)。
                    case EffectKind.CritBuff: sum += e.Value * 3; break;
                    // 流血:每回合固定伤害、本场持久直到清理,同 BurnSingle 的 ×2 口径
                    // (一次性数值但反复兑现,理应比一次性同值的直伤更值钱)。
                    case EffectKind.Bleed: sum += e.Value * 2; break;
                    // 持续治疗:总收益 = 每回合值 × 持续回合数,与 HealSelf 记账口径一致
                    // (HealSelf 是「一次性治疗全额记分」,HealOverTime 只是把同一笔账摊开在多回合)。
                    // ⚠ 沐(水系金档)目前 effects 只有 HealOverTime+Revive、没有 HealSelf——
                    // 这一条修好之前 沐 的 Power() 恒为 0,机器人会把它当全表最弱、优先弃掉/
                    // 永不合成,与「没有这张字」等价。
                    case EffectKind.HealOverTime: sum += e.Value * Math.Max(1, e.Turns); break;
                    // 下面三条(design §2.2「本次休眠清单」)当前全表 0 载体,只为将来复活时
                    // 不再落回 0 分预先占位,估值全部粗放:
                    case EffectKind.Dispel: sum += e.Value < 0 ? 90 : e.Value * 30; break; // -1=清全部
                    case EffectKind.Cleanse: sum += 50; break; // 净化玩家自身全部减益,Value 不用
                    // AP 上限(design §2.2「AP 上限 +1 0.50」≈75 分/点)。
                    case EffectKind.ApBoost: sum += e.Value * 75; break;
                    // 不灭 / 立即结算(design 未单列价,取模拟 Detonate 的量级,只求非零)。
                    case EffectKind.BurnNoDecay: sum += 40; break;
                    case EffectKind.BurnSettleNow: sum += 30; break;
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
