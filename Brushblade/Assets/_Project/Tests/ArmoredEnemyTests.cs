using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>逐怪出场深度闸(2026-09-02)。层段(band)整段共用一个 enemyPool,
    /// 表达不了段内深度差 —— 「低阶护甲怪 1-5 层不出、6 层起可出」这种需求只能靠给
    /// EnemyDef 加一个 MinDepth,编成时按当前深度过滤候选池(Endless.WithinDepth)。</summary>
    public sealed class ArmoredEnemyTests
    {
        // ---- 真实配置读取(照 DefenseValuesTests 抄) ----

        private static string ConfigDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static RecipeGraph RealGraph() =>
            ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(ConfigDir(), "chars.json")));

        private static EndlessConfig LoadRealEndlessConfig() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), RealGraph())
                .Endless;

        private static CampaignConfig LoadRealCampaign() =>
            ConfigLoader.LoadCampaign(File.ReadAllText(Path.Combine(ConfigDir(), "enemies.json")), RealGraph());

        /// <summary>enemies.json 里出现过的全部敌人(去重;照 DefenseValuesTests.AllEnemies 抄)——
        /// 以 endless.bands 的 enemyPool/bossPool 为准,因为无尽层段是 v0.7 的唯一核心玩法。</summary>
        private static List<EnemyDef> AllEnemies()
        {
            var campaign = LoadRealCampaign();
            var seen = new Dictionary<string, EnemyDef>();
            foreach (var band in campaign.Endless?.Bands ?? (IReadOnlyList<BandDef>)Array.Empty<BandDef>())
            {
                foreach (var enemy in band.EnemyPool) seen[enemy.Id] = enemy;
                foreach (var boss in band.BossPool) seen[boss.Id] = boss;
            }
            return seen.Values.ToList();
        }

        [Test]
        public void ArmoredEnemies_CoverAllFiveElementsAtBothTiers()
        {
            var armored = AllEnemies().Where(e => e.Defense > 0).ToList();
            foreach (var element in new[]
                     {
                         Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water,
                     })
            {
                // 低/高阶分界随护甲整列重标定一起换算(2026-09-16 裁定 I)。
                // ⚠ 刻意**不去换算旧的 30 点分界**:那需要一把给无锚点档位配的尺子,
                // 而裁定 E 的直线正是栽在这上面。分界只要落在「最高的低阶怪」与
                // 「最低的高阶怪」之间即可 —— 换算后低阶是 27/27/31/31/35、高阶是
                // 50/50/54/60/60,取 40 落在 35 与 50 之间,离两边都远。
                const int tierSplit = 40;
                var low = armored.Where(e => e.Element == element && e.Defense < tierSplit).ToList();
                var high = armored.Where(e => e.Element == element && e.Defense >= tierSplit).ToList();
                Assert.That(low.Count, Is.GreaterThanOrEqualTo(1), $"{element} 缺低阶护甲怪");
                Assert.That(high.Count, Is.GreaterThanOrEqualTo(1), $"{element} 缺高阶护甲怪");
            }
        }

        [Test]
        public void LowTierArmoredEnemies_HaveMinDepthSix()
        {
            var all = AllEnemies();
            foreach (var id in new[] { "枯笔", "火漆", "砚台", "铜钤", "墨渍" })
            {
                var enemy = all.First(e => e.Id == id);
                Assert.That(enemy.MinDepth, Is.GreaterThanOrEqualTo(6),
                    $"{id} 是低阶护甲怪,不该在 1-5 层出现");
            }
        }

        [Test]
        public void EveryFloor_HasAtMostOneArmoredEnemy()
        {
            // WithoutArmor 闸(既有)在补齐 10 只后仍须成立。
            // ⚠ 2026-09-16 护甲百分比化后,「点数护甲对 AOE 有 N 倍惩罚、带甲成群会把 AOE
            // 流派打废」这条原始理由已经失效(减伤成了乘区,每个目标各折各的)。
            // 闸子与本条判据保留原样,但它现在没有数值上的理由撑着 —— 见 Endless.BuildFloor 的注释。
            var config = LoadRealEndlessConfig();
            for (int depth = 1; depth <= 60; depth++)
            {
                var segment = EndlessGenerator.BuildSegment(config, depth, seed: depth * 7919);
                foreach (var floor in segment.Encounters)
                    Assert.That(floor.Count(e => e.Defense > 0), Is.LessThanOrEqualTo(1),
                        $"第 {depth} 层出现了多只带甲怪");
            }
        }

        [Test]
        public void MinDepth_FiltersEnemiesOutOfEarlyFloors()
        {
            var config = LoadRealEndlessConfig();
            // 1-5 层不出任何带甲怪。2026-09-02 起 enemies.json 里 5 只低阶护甲怪
            // (枯笔/火漆/砚台/铜钤/墨渍)都配了 minDepth: 6,这条在真实配置上
            // 已经是深度闸真正生效的判据,不再是空操作。
            for (int depth = 1; depth <= 5; depth++)
            {
                // Boss 层直接 return,不走深度闸(见 Endless.BuildFloor 的 IsBossDepth 分支)。
                // Boss 的护甲挂在 BossPhaseDef.Defense 上,EnemyDef.Defense 本身仍是 0 ——
                // 断言在 Boss 层碰巧也成立,但那是巧合不是深度闸的功劳,显式跳过不测。
                if (config.IsBossDepth(depth)) continue;

                var floor = EndlessGenerator.BuildFloor(config, depth, new GameRandom(12345 + depth));
                foreach (var enemy in floor)
                    Assert.That(enemy.Defense, Is.EqualTo(0),
                        $"第 {depth} 层不该有带甲怪:{enemy.Id}");
            }
        }

        [Test]
        public void MinDepth_IsPreservedThroughScale()
        {
            // Scale 会重建 EnemyDef —— 漏传 MinDepth 不会报错,只会静默失效
            var def = new EnemyDef("测", Element.Earth, 100, 10, minDepth: 6);
            var scaled = CampaignConfig.Scale(def, 2.0f);
            Assert.That(scaled.MinDepth, Is.EqualTo(6));
        }

        [Test]
        public void MinDepth_GateActuallyExcludesEnemyBeforeItsDepth()
        {
            // 自造一个 2 怪的池子:一只随时可出,一只 6 层才解锁且带甲。
            // BossEvery 拉到 100 让 1-5 层全部避开 Boss 分支,专测深度闸本身。
            var open = new EnemyDef("拓", Element.Earth, 50, 5);
            var gated = new EnemyDef("甲", Element.Earth, 50, 5, defense: 5, minDepth: 6);
            var config = new EndlessConfig
            {
                BossEvery = 100,
                Bands = new[]
                {
                    new BandDef { Name = "测试段", FromDepth = 1,
                        EnemyPool = new[] { open, gated }, BossPool = new[] { open } },
                },
            };

            for (int depth = 1; depth <= 5; depth++)
                for (int seed = 0; seed < 20; seed++)
                {
                    var floor = EndlessGenerator.BuildFloor(config, depth, new GameRandom(seed));
                    Assert.That(floor.Any(e => e.Id == gated.Id), Is.False,
                        $"第 {depth} 层不该出「{gated.Id}」(minDepth=6):seed {seed}");
                }
        }

        [Test]
        public void FirstTowerSegment_StillUsesTheThreeTutorialEnemies()
        {
            // 首塔前 3 层取 Bands[0].EnemyPool 的下标 0/1/2。
            // 新怪必须追加在池尾,否则会挤掉引导用的那三只。
            var config = LoadRealEndlessConfig();
            var segment = EndlessGenerator.BuildFirstTowerSegment(config, seed: 1);
            Assert.That(segment.Encounters[0][0].Id, Is.EqualTo("错字鬼"));
            Assert.That(segment.Encounters[0][0].Defense, Is.EqualTo(0));
        }

        // ---- 护甲折算:百分比减伤(2026-09-16,推翻 E-b4 的点数减法) ----

        [Test]
        public void ApplyDefense_IsPercentageReduction_NotSubtraction()
        {
            // DR = 甲/(甲+100):甲 100 → 减半;甲 0 → 不减
            Assert.That(BattleEngine.ApplyDefense(200, 100), Is.EqualTo(100), "甲 100 应减伤 50%");
            Assert.That(BattleEngine.ApplyDefense(200, 0), Is.EqualTo(200), "甲 0 应不减伤");
            Assert.That(BattleEngine.ApplyDefense(200, 300), Is.EqualTo(50), "甲 300 应减伤 75%");
        }

        [Test]
        public void ApplyDefense_NeverReachesZero_EvenWithHugeArmor()
        {
            // 这是选百分比减伤的全部理由:甲可以随便叠,伤害永远进得去
            Assert.That(BattleEngine.ApplyDefense(1000, 100000), Is.GreaterThan(0),
                "百分比减伤永远到不了 100%,10 万点甲也要让 1000 伤害留下至少 1 点");
        }

        [Test]
        public void ApplyDefense_ClampsNegativeArmorToZero()
        {
            Assert.That(BattleEngine.ApplyDefense(200, -50), Is.EqualTo(200), "负护甲不许倒贴增伤");
        }

        // ---- 碾(TrueDamage,2026-09-16 土):跳过整条 DR,但仍吃护盾 ----
        //
        // ⚠ 本文件其余测试只读真实配置(chars.json/enemies.json)或直接调 ApplyDefense 这个静态
        // 方法,没有可复用的「造一局战斗」夹具 —— 下面这套 Graph()/Engine() 是新起的,照抄
        // EnemyShieldTests.cs 的既有写法(心系测试字 + Element.Heart 保证 KeMultiplier 恒为
        // 1.0,不搅动生克;PlayerCritChance 默认 0,RollCrit() 恒为 false,不搅动暴击)。

        // 甲:100 伤,心系中立,不带碾 —— 验护甲照常生效。
        // 碾:100 伤,心系中立,trueDamage:true —— 验完全跳过护甲但护盾照吃。
        private static RecipeGraph TrueDamageGraph() => new(new[]
        {
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("碾", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, trueDamage: true) }),
        });

        private static BattleEngine TrueDamageEngine(EnemyDef enemy) =>
            new(TrueDamageGraph(), new BattleConfig { PlayerMaxHp = 1000 },
                new[] { "甲", "碾" }, Array.Empty<string>(), new[] { enemy }, seed: 1);

        [Test]
        public void TrueDamage_SkipsArmorEntirely_ButStillEatsShield()
        {
            // 敌人甲 100(DR 50%)、盾 30。碾:伤害 100 全额进入 → 盾吃 30 → 血扣 70
            var engine = TrueDamageEngine(new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100));
            engine.Enemies[0].Shield = 30;
            engine.Cast("碾", 0);
            Assert.That(engine.Enemies[0].Shield, Is.EqualTo(0), "30 点盾被吃满");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 70),
                "碾跳过护甲但不跳过护盾:100 − 30 盾 = 70");
        }

        [Test]
        public void WithoutTrueDamage_ArmorStillApplies()
        {
            // 敌人甲 100、无盾 → 100 × 100/(100+100) = 50
            var engine = TrueDamageEngine(new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100));
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 50), "不带碾时护甲照常生效");
        }

        // ---- 镇压(ArmorStrikePercent,2026-09-16 土):按玩家自己的有效护甲加码伤害 ----
        //
        // 心系测试字,道理同上一节:KeMultiplier 恒为 1.0,不搅动生克;PlayerCritChance
        // 默认 0,RollCrit() 恒为 false,不搅动暴击。

        // 镇:100 伤,心系中立,镇压 50% —— 主伤害之后按玩家自己的 EffectivePlayerDefense 加码。
        private static RecipeGraph ArmorStrikeGraph() => new(new[]
        {
            new CharDef("镇", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, armorStrikePercent: 50) }),
        });

        private static BattleEngine ArmorStrikeEngine(EnemyDef enemy, int playerDefense) =>
            new(ArmorStrikeGraph(), new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = playerDefense },
                new[] { "镇" }, Array.Empty<string>(), new[] { enemy }, seed: 1);

        [Test]
        public void ArmorStrike_AddsDamageFromOwnArmor_NotAffectedByWuxingOrTargetArmor()
        {
            // 玩家甲 40、敌人甲 100(DR 50%)、无盾。
            // 主伤害 100×100/(100+100)=50,镇压 40×50%=20 全额(不吃敌方护甲、不走生克)→ 共 70
            var engine = ArmorStrikeEngine(new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100),
                playerDefense: 40);
            engine.Cast("镇", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 70),
                "主伤害 50 + 镇压 20(全额,不吃敌方护甲)= 70");
        }

        [Test]
        public void ArmorStrike_DamageEvents_MainHitUntagged_BonusTaggedArmorStrike()
        {
            var engine = ArmorStrikeEngine(new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100),
                playerDefense: 40);
            engine.Cast("镇", 0);
            var hits = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[0].Source, Is.EqualTo(DamageSource.None), "主伤害是普通挥击,不标来源");
            Assert.That(hits[0].Amount, Is.EqualTo(50));
            Assert.That(hits[1].Source, Is.EqualTo(DamageSource.ArmorStrike));
            Assert.That(hits[1].Amount, Is.EqualTo(20));
        }

        [Test]
        public void ArmorStrike_IsNoOp_WhenPlayerHasNoArmor()
        {
            // 玩家甲 0 → 镇压额度 0×50%=0,只剩主伤害 50,空转不报错
            var engine = ArmorStrikeEngine(new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100),
                playerDefense: 0);
            engine.Cast("镇", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 50), "无甲时镇压空转,主伤害照常");
        }

        [Test]
        public void ArmorStrike_WithVolley_NoTargetCast_AppliesOnceOnFirstShotTarget()
        {
            // 塔(2026-09-16):连发2 + 镇压50。连发不选目标(NeedsTarget 排除 Volley),
            // targetIndex 恒为 -1 —— 镇压曾直接读 _enemies[targetIndex] 越界崩溃(balance 仿真撞出)。
            // 口径:镇压只在目标表首项结算**一次**,不按发数翻倍(价目「镇压50」只计一次)。
            // 玩家甲 40、敌人甲 100:每发 100×100/200=50,两发 100,镇压 40×50%=20 → 共 120
            var graph = new RecipeGraph(new[]
            {
                new CharDef("塔", Element.Heart, effects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 100, shape: TargetShape.Volley, shots: 2,
                        armorStrikePercent: 50),
                }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 40 },
                new[] { "塔" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Earth, 1000, 0, defense: 100) }, seed: 1);
            Assert.That(BattleEngine.NeedsTarget(graph.Get("塔")), Is.False, "前提:连发不选目标");
            Assert.That(engine.Cast("塔"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 120), "两发 50×2 + 镇压 20 只结算一次");
        }
    }
}
