using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>点数制护甲的**配值**(E-b4/E-b5 的 T3,2026-08-12)。
    ///
    /// T2 守的是接线(全场护甲 0、逐字节恒等),本文件守的是**第六节那张迁移映射表**:
    /// 6 个护甲字、4 处敌人护甲、3 个穿透字的具体点数对不对。
    ///
    /// 手法是 spec §10.3 的**定向对照**:对每一条映射,把「旧乘法模型在参考打击量下的等价值」
    /// 与「新点数模型的实际输出」摆在一起,带宽 ±15%。带宽刻意宽松 —— 折算本来就是近似,
    /// 测试守的是「量级没错」而不是「数字精确」。**一条超出带宽 = 折算率算错或参考量选错**,
    /// 那是要人来判的设计问题,不是代码 bug。
    ///
    /// 参考打击量(spec §6.1,设计取值不是测量值):
    /// R_in = 60(玩家挨的一击)、R_mob = 85(玩家打小怪)、R_boss = 120(玩家打 Boss)。
    ///
    /// ⚠ 本文件**读真实配置**(StreamingAssets/config/*.json),与 DefenseWiringTests 刻意相反 ——
    /// 那个文件为了让接线可观测而与生产配置脱钩,这个文件的全部意义就是盯住生产配置的数字。</summary>
    public sealed class DefenseValuesTests
    {
        // ---- 真实配置读取 ----

        private static string ConfigDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static RecipeGraph RealGraph() =>
            ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")));

        private static CampaignConfig RealCampaign() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), RealGraph());

        private static EnemyDef RealEnemy(string id)
        {
            var found = AllEnemies().FirstOrDefault(e => e.Id == id);
            Assert.That(found, Is.Not.Null, $"enemies.json 里找不到「{id}」");
            return found;
        }

        /// <summary>enemies.json 里出现过的全部敌人(去重)。
        /// **无尽层段是 v0.7 的唯一核心玩法**(第 20 章),所以以 bands 的 enemyPool / bossPool
        /// 为准;章节编成一并扫进来兜底,免得哪只怪只在废止的章节制里出现而被漏掉。</summary>
        private static List<EnemyDef> AllEnemies()
        {
            var campaign = RealCampaign();
            var seen = new Dictionary<string, EnemyDef>();
            foreach (var band in campaign.Endless?.Bands ?? (IReadOnlyList<BandDef>)Array.Empty<BandDef>())
            {
                foreach (var enemy in band.EnemyPool) seen[enemy.Id] = enemy;
                foreach (var boss in band.BossPool) seen[boss.Id] = boss;
            }
            foreach (var chapter in campaign.Chapters ?? (IReadOnlyList<ChapterDef>)Array.Empty<ChapterDef>())
                foreach (var stage in chapter.Stages)
                    foreach (var group in stage.Encounters)
                        foreach (var enemy in group)
                            if (!ReferenceEquals(enemy, CampaignConfig.BossPlaceholder))
                                seen[enemy.Id] = enemy;
            return seen.Values.ToList();
        }

        /// <summary>探针字表:一张可配基础值/穿透的中立(心)伤害字 + 一张空部件。
        /// 心对全属性都是 1.0x,于是断言里看到的差额就是护甲那一层。</summary>
        private static RecipeGraph ProbeGraph(int damage, int pierce = 0) => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("探", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, damage, pierce: pierce) }),
        });

        /// <summary>一记 <paramref name="damage"/> 的中立攻击打在这只敌人身上,实际掉多少血。</summary>
        private static int HitFor(EnemyDef enemy, int damage, int pierce = 0)
        {
            var probe = new EnemyDef(enemy.Id, enemy.Element, 100000, 0,
                enemy.Ability, enemy.Phases, enemy.Defense);
            var engine = new BattleEngine(ProbeGraph(damage, pierce),
                new BattleConfig { PlayerMaxHp = 1000, DropTable = new[] { "木" } },
                new[] { "探" }, Array.Empty<string>(), new[] { probe }, seed: 1);
            int before = engine.Enemies[0].Hp;
            engine.Cast("探", 0);
            return before - engine.Enemies[0].Hp;
        }

        /// <summary>Boss 的某个阶段单拎出来当靶子(阶段护甲挂在 BossPhaseDef 上)。</summary>
        private static int HitBossPhaseFor(string bossId, string phaseChar, int damage)
        {
            var boss = RealEnemy(bossId);
            var phase = boss.Phases.First(p => p.Char == phaseChar);
            var target = new EnemyDef(phaseChar, phase.Element, 100000, 0, EnemyAbility.None, null, phase.Defense);
            return HitFor(target, damage);
        }

        /// <summary>玩家出一张真实的护甲字,再挨**一记**这只靶子的普攻,实际掉多少血。
        ///
        /// 靶子属性取心(生克 1.0x),攻击力就是参考打击量 R_in。逐回合推进而不是只 EndTurn 一次:
        /// 漜 自带减速 2 回合,靶子头一个敌方回合根本不出手 —— 写死一次 EndTurn 会量到 0,
        /// 那不是护甲的功劳。这里取**第一记真正落下来的攻击**。</summary>
        private static int PlayerHitAfterCasting(string armorChar, int attack)
        {
            var engine = new BattleEngine(RealGraph(),
                new BattleConfig { PlayerMaxHp = 100000, DropTable = new[] { "木" } },
                new[] { armorChar }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, attack) }, seed: 1);
            engine.Cast(armorChar);
            for (int turn = 0; turn < 6; turn++)
            {
                int before = engine.PlayerHp;
                engine.EndTurn();
                if (engine.PlayerHp < before) return before - engine.PlayerHp;
            }
            Assert.Fail($"「{armorChar}」之后 6 个回合都没挨到一记攻击,这条对照量不到东西");
            return 0;
        }

        // ============================================================
        // 网 3:12 条定向对照(spec §10.3)
        // 每条的「旧等价值」= 旧乘法模型在对应参考打击量下的输出,不是历史数据而是可复算的算式。
        // ============================================================

        // ---- 敌人侧 4 条 ----

        [Test]
        public void Calibration_MoZhi_Defense20_AgainstMobReference()
        {
            // 旧:承伤 0.7 → floor(85 × 0.7) = 59。新:85 − 20 = 65。
            // ⚠ 20 是被 T3-V4 判据从折算值 25 压下来的(spec §6.3.2),对照带宽刚好容得下。
            Assert.That(RealEnemy("墨渍").Defense, Is.EqualTo(20));
            Assert.That(HitFor(RealEnemy("墨渍"), 85), Is.EqualTo(59).Within(9));
        }

        [Test]
        public void Calibration_ShanPhase_Defense60_AgainstBossReference()
        {
            // 旧:承伤 0.5 → floor(120 × 0.5) = 60。新:120 − 60 = 60。
            Assert.That(RealEnemy("排山倒海").Phases.First(p => p.Char == "山").Defense, Is.EqualTo(60));
            Assert.That(HitBossPhaseFor("排山倒海", "山", 120), Is.EqualTo(60).Within(9));
        }

        [Test]
        public void Calibration_JiangPhase_Defense30_AgainstBossReference()
        {
            // 旧:承伤 0.75 → floor(120 × 0.75) = 90。新:120 − 30 = 90。
            Assert.That(RealEnemy("翻江倒海").Phases.First(p => p.Char == "江").Defense, Is.EqualTo(30));
            Assert.That(HitBossPhaseFor("翻江倒海", "江", 120), Is.EqualTo(90).Within(9));
        }

        [Test]
        public void Calibration_JunPhase_Defense30_AgainstBossReference()
        {
            // 旧:承伤 0.75 → floor(120 × 0.75) = 90。新:120 − 30 = 90。
            Assert.That(RealEnemy("雷霆万钧").Phases.First(p => p.Char == "钧").Defense, Is.EqualTo(30));
            Assert.That(HitBossPhaseFor("雷霆万钧", "钧", 120), Is.EqualTo(90).Within(9));
        }

        // ---- 玩家侧 5 条(R_in = 60)----

        [TestCase("巍", 5, 57)]   // 旧 floor(60 × 0.95) = 57;新 60 − 3
        [TestCase("磐", 10, 54)]  // 旧 floor(60 × 0.90) = 54;新 60 − 6
        [TestCase("崟", 15, 51)]  // 旧 floor(60 × 0.85) = 51;新 60 − 9
        [TestCase("铠", 20, 48)]  // 旧 floor(60 × 0.80) = 48;新 60 − 12
        [TestCase("漜", 25, 45)]  // 旧 floor(60 × 0.75) = 45;新 60 − 15
        public void Calibration_DefenseChars_AgainstIncomingReference(
            string charId, int legacyReductionPercent, int legacyEquivalent)
        {
            Assert.That(legacyEquivalent, Is.EqualTo((int)Math.Floor(60 * (1 - legacyReductionPercent / 100.0))),
                "旧等价值是可复算的算式,不是抄来的常数");
            Assert.That(PlayerHitAfterCasting(charId, 60), Is.EqualTo(legacyEquivalent).Within(9));
        }

        // ---- 穿透 3 条(打墨渍,DEF 20)----

        [Test]
        public void Calibration_Zuan_Pierce30_AgainstMoZhi()
        {
            // 旧:穿甲把承伤提回 1 再 +15% → floor(400 × 1.15) = 460。
            // 新:460 − max(0, 20 − 30) = 460。**精确相等** —— +15% 固化进基础值是等价变换。
            var hit = RealGraph().Get("錰").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(HitFor(RealEnemy("墨渍"), hit.Value, hit.Pierce), Is.EqualTo(460));
        }

        [Test]
        public void Calibration_Ci_Pierce15_AgainstMoZhi()
        {
            // 旧:floor(130 × 1.15) = 149。新:150 − max(0, 20 − 15) = 145。
            var hit = RealGraph().Get("刺").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(HitFor(RealEnemy("墨渍"), hit.Value, hit.Pierce), Is.EqualTo(149).Within(15));
        }

        [Test]
        public void Calibration_Zhui_Pierce10_AgainstMoZhi()
        {
            // 旧:floor(90 × 1.15) = 103。新:105 − max(0, 20 − 10) = 95。
            var hit = RealGraph().Get("锥").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(HitFor(RealEnemy("墨渍"), hit.Value, hit.Pierce), Is.EqualTo(103).Within(15));
        }

        // ============================================================
        // 裁定 11:护甲**半速**缩放(spec §6.3.1 / §6.3.2)
        // ============================================================

        /// <summary>T3-V4 的前一半:半速本身。深度 20 的墨渍护甲 = 39,不是同速的 58。
        /// 同速会让深层的低伤字全部归零,字库多样性被护甲单方面掐死。</summary>
        [Test]
        public void Scale_HalvesDefenseGrowth()
        {
            var scaled = CampaignConfig.Scale(RealEnemy("墨渍"), DepthScale(20));
            Assert.That(scaled.Defense, Is.EqualTo(39),
                "defScale = 1 + (2.9 − 1)/2 = 1.95 → ceil(20 × 1.95) = 39;同速会是 ceil(20 × 2.9) = 58");
            Assert.That(scaled.MaxHp, Is.EqualTo(406), "血量照常**全速**:140 × 2.9");
        }

        /// <summary>T3-V4:**半速缩放的可执行判据**(spec §6.3.2)。
        ///
        /// 深度 20 时,字表里**最低伤害档**的字打在带甲小怪身上仍要有非零输出。
        /// 这条判据不是装饰:三组数里只有「墨渍 20 + 半速」通过 ——
        ///   墨渍 25(纯折算值)+ 半速 → ceil(25×1.95) = 49 → 输出 0 ❌
        ///   墨渍 20 + **同速**      → ceil(20×2.9)  = 58 → 输出 0 ❌
        ///   墨渍 20 + 半速          → ceil(20×1.95) = 39 → 输出 8 ✅
        /// 把 Campaign 的 defScale 改成同速、或把墨渍写回 25,这条都会变红。
        ///
        /// 判据的作用域是**小怪**,不含 Boss:「用白字磨 Boss」不是要保护的玩法,
        /// 但小怪要能被任意字清掉,否则杂兵战会卡死。</summary>
        [Test]
        public void LowestTierChar_StillDentsArmoredMobAtDepth20()
        {
            // 典型玩家画像(spec §6.3.2):角色 12 级 → ATK 122;卡等级 4 → ×1.3
            const int playerLevel = 12;
            const int cardLevel = 4;

            // 「最低伤害档」取真实字表里最小的 DamageSingle 基础值(今天是 蒸 的 30);
            // 但打出去的那一记用**中立(心)**探针字 —— 与 spec §6.3.2 的推导同口径。
            // 不直接出 蒸:它是火系带配方的字,对水系的墨渍会吃到生克乘数(实测 ×1.5),
            // 量到的就不再是「最低档 vs 护甲」而是「最低档 × 运气好的属性」,判据会被生克糊掉。
            int lowestTier = RealGraph().All
                .SelectMany(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Where(e => e.Kind == EffectKind.DamageSingle && e.Pierce == 0)
                    .Select(e => e.Value))
                .Min();
            Assert.That(lowestTier, Is.EqualTo(30), "字表最低伤害档;它变了这条判据要重新标定");

            var mob = CampaignConfig.Scale(RealEnemy("墨渍"), DepthScale(20));
            Assert.That(mob.Defense, Is.EqualTo(39));

            var engine = new BattleEngine(ProbeGraph(lowestTier),
                new BattleConfig
                {
                    PlayerMaxHp = 100000,
                    PlayerAttack = MetaRules.AttackFor(playerLevel),
                    DropTable = new[] { "木" },
                },
                new[] { "探" }, Array.Empty<string>(),
                new[] { new EnemyDef(mob.Id, mob.Element, 100000, 0, mob.Ability, mob.Phases, mob.Defense) },
                seed: 1,
                startingHp: null,
                cardLevels: new Dictionary<string, int> { ["探"] = cardLevel });
            Assert.That(engine.EffectiveAttack, Is.EqualTo(122), "12 级角色的攻击力");

            int before = engine.Enemies[0].Hp;
            engine.Cast("探", 0);
            int dealt = before - engine.Enemies[0].Hp;

            Assert.That(dealt, Is.GreaterThan(0),
                "深度 20 的带甲小怪必须还能被最低档的字磨动 —— 归零就等于护甲把字库掐死了");
            Assert.That(dealt, Is.EqualTo(8),
                "ceil(30×1.3) = 39 → 39×122/100 = 47 → 47 − 39 = 8(spec §6.3.2 的推导)");
        }

        /// <summary>无尽深度缩放系数(<c>Endless.cs</c> 的 <c>1 + 0.1×(depth−1)</c>)。
        /// 这里不引 EndlessConfig 是为了让判据只依赖一个可读的算式。</summary>
        private static float DepthScale(int depth) => 1f + 0.1f * (depth - 1);

        // ============================================================
        // 配置口径守卫
        // ============================================================

        /// <summary>spec §4.4(a):**带甲怪不成群**。点数护甲对 AOE 有 N 倍惩罚
        /// (打 N 个目标就损失 N × DEF),代价靠配置口径兜而不是靠代码闸。
        /// 交付时 13 只小怪里带甲的只有墨渍 1 只;Boss 的护甲挂在阶段上,而 Boss 永远单只。
        ///
        /// ⚠ 第八章补装甲怪时这条要相应改成「任一 enemyPool 抽样的遭遇里不超过 1 只」——
        /// 那里要求的是**总数**变多,本条约束的是**同一次遭遇里的并发数**,两条不矛盾。</summary>
        [Test]
        public void RealConfig_ArmoredEnemiesAreRare()
        {
            var minions = AllEnemies().Where(e => e.Phases.Count == 0).ToList();
            var armored = minions.Where(e => e.Defense > 0).Select(e => e.Id).ToList();
            Assert.That(armored.Count, Is.LessThanOrEqualTo(1),
                $"小怪级带甲的应不超过 1 只,实际:{string.Join("/", armored)}");
            Assert.That(armored, Does.Contain("墨渍"), "墨渍就是那一只");
        }

        /// <summary>其余 Boss 阶段一律无甲(spec §6.3:只有 山 60 / 江 30 / 钧 30 三处)。
        /// 漏配是静默的 —— 多给一个阶段配上甲不会有任何别的测试变红。</summary>
        [Test]
        public void RealConfig_OnlyThreeBossPhasesCarryArmor()
        {
            var armored = AllEnemies()
                .SelectMany(e => e.Phases.Select(p => (Boss: e.Id, p.Char, p.Defense)))
                .Where(x => x.Defense > 0)
                .OrderBy(x => x.Char)
                .ToList();
            Assert.That(armored.Select(x => $"{x.Char}{x.Defense}").ToArray(),
                Is.EqualTo(new[] { "山60", "江30", "钧30" }));
        }
    }
}
