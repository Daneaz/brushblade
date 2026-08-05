using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗状态机:第 3 章 3.3/3.5/3.7 + 第 10 章 10.1/10.2 + wuxing-reference 规格例。</summary>
    public class BattleEngineTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }), // 部件直出(10.3.1)
            new CharDef("土", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 3) }), // 部件直出(10.3.6)
            new CharDef("辟", Element.Metal),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6), new EffectDef(EffectKind.BurnSingle, 1) }),
            new CharDef("丁", null),
            new CharDef("燃", Element.Fire, new[] { "火", "然" },
                effects: new[] { new EffectDef(EffectKind.BurnAll, 3) }),
            new CharDef("然", null),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18), new EffectDef(EffectKind.BurnAll, 1) }),
            // 壁(土系,辟金+土):土生金是「我生他」→ 不吃相生,盾 8
            new CharDef("壁", Element.Earth, new[] { "辟", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 8) }),
            // 锢(金系,辟金+土):土生金 = 「他生我」→ 吃相生,盾 8×3 = 24
            new CharDef("锢", Element.Metal, new[] { "辟", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 8) }),
            new CharDef("灼", Element.Fire, new[] { "火", "勺" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 8, doubleVsBurning: true) }),
            new CharDef("勺", null),
            new CharDef("炽", Element.Fire, new[] { "火", "只" },
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 1) }),
            new CharDef("只", null),
            new CharDef("堡", Element.Earth, new[] { "呆", "土" }, rarity: CardRarity.Purple,
                effects: new[] { new EffectDef(EffectKind.Shield, 10, persistOnce: true) }),
            new CharDef("呆", null),
        });

        private static BattleConfig Config(params string[] dropTable) => new()
        {
            DropTable = dropTable.Length > 0 ? dropTable : new[] { "木" },
        };

        private static EnemyDef MetalBoss(int hp = 200) => new("锈", Element.Metal, hp, 5);
        private static EnemyDef WoodMinion(int hp = 12) => new("枯", Element.Wood, hp, 3);

        private static BattleEngine Engine(
            string[] library = null, string[] pool = null, EnemyDef[] enemies = null,
            BattleConfig config = null, int seed = 42)
        {
            return new BattleEngine(Graph(), config ?? Config(),
                library ?? Array.Empty<string>(), pool ?? Array.Empty<string>(),
                enemies ?? new[] { MetalBoss() }, seed);
        }

        // ---- 构造与初始化 ----

        [Test]
        public void Constructor_InjectsInitialShield() // 段初始护盾注入(B)
        {
            var engine = new BattleEngine(Graph(), Config(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { MetalBoss() }, seed: 42, startingHp: null, cardLevels: null,
                startingNormalShield: 5, startingPersistShield: 2);
            Assert.That(engine.ShieldNormal, Is.EqualTo(5));
            Assert.That(engine.ShieldPersist, Is.EqualTo(2));
            Assert.That(engine.PlayerShield, Is.EqualTo(7));
        }

        [Test]
        public void Shield_PersistsThroughEnemyTurn() // 段内持久:普通护盾不再回合末全清
        {
            // 单敌攻击 3;玩家不出手,EndTurn 后敌方攻击被护盾吸收
            var engine = new BattleEngine(Graph(), Config(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { WoodMinion(hp: 100) }, seed: 42, startingHp: 50, cardLevels: null,
                startingNormalShield: 5);
            engine.EndTurn();                 // 敌方攻击 3,护盾吸收
            Assert.That(engine.PlayerShield, Is.EqualTo(2)); // 旧逻辑会清 0;段内持久剩 2
            Assert.That(engine.PlayerHp, Is.EqualTo(50));    // 护盾垫住,血未掉
        }

        // ---- 减伤(土系,2026-08-03):乘法叠加、同字不叠、段内持久 ----

        private static RecipeGraph ArmorGraph() => new(new[]
        {
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DamageReduction, 20) }),
            new CharDef("崟", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.DamageReduction, 15) }),
        });

        [Test]
        public void DamageReduction_MultipliesAcrossDifferentChars()
        {
            var engine = new BattleEngine(ArmorGraph(), Config(), new[] { "铠", "崟" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 10) }, 42);
            engine.Cast("铠");
            engine.Cast("崟");

            Assert.That(engine.DamageReductionMultiplier, Is.EqualTo(0.68f).Within(0.001f),
                "0.8 × 0.85 = 0.68");
        }

        [Test]
        public void DamageReduction_SameCharDoesNotStack()
        {
            var engine = new BattleEngine(ArmorGraph(), Config(), new[] { "铠", "铠" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 10) }, 42);
            engine.Cast("铠");
            engine.Cast("铠");

            Assert.That(engine.DamageReductionMultiplier, Is.EqualTo(0.8f).Within(0.001f),
                "同字重复施放只刷新,不叠加");
        }

        [Test]
        public void DamageReduction_AppliesToIncomingDamage()
        {
            var engine = new BattleEngine(ArmorGraph(), Config(), new[] { "铠" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 10) }, 42);
            engine.Cast("铠");
            int hpBefore = engine.PlayerHp;

            engine.EndTurn();

            Assert.That(hpBefore - engine.PlayerHp, Is.EqualTo(8), "10 伤减 20% = 8");
        }

        [Test]
        public void DamageReduction_InjectedViaConstructor_AppliesImmediately() // 跨战斗结转的构造入口
        {
            var engine = new BattleEngine(ArmorGraph(), Config(), Array.Empty<string>(),
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 10) }, seed: 42,
                startingHp: null, cardLevels: null, startingNormalShield: 0, startingPersistShield: 0,
                startingSummons: null,
                startingStatuses: new[] { new StatusEffect
                {
                    Kind = StatusKind.DamageReduction, Polarity = StatusPolarity.Buff,
                    Magnitude = 20, TurnsLeft = -1, SourceId = "铠",
                } });

            Assert.That(engine.DamageReductionMultiplier, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(engine.PlayerStatuses.Find(StatusKind.DamageReduction).Magnitude, Is.EqualTo(20));
        }

        // ---- 回合开始(3.5 步骤 1) ----

        [Test]
        public void TurnStart_GrantsApAndDropsOneChar() // 回合掉字改造(2026-08-04):部件掉落 → 字掉落,2/回合 → 1/回合
        {
            var engine = Engine(config: new BattleConfig { UnlockedChars = new[] { "木" } });
            Assert.That(engine.Turn, Is.EqualTo(1));
            Assert.That(engine.Ap, Is.EqualTo(3));
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "木" })); // 出战牌组只有木;1/回合
        }

        [Test]
        public void ApPerTurn_ReflectsConfig_AndSeedsStartingAp() // 一气 +AP 后 UI 满格数联动:每回合上限透传
        {
            var config = Config();
            config.ApPerTurn = 4;
            var engine = new BattleEngine(Graph(), config,
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { MetalBoss() }, seed: 42);
            Assert.That(engine.ApPerTurn, Is.EqualTo(4));
            Assert.That(engine.Ap, Is.EqualTo(4)); // 起始满格 = 每回合上限
        }

        [Test]
        public void TurnStart_DropsFillLibraryWithoutExceedingCapacity() // 掉字改造(2026-08-04):库满不再溢出;基准 6(2026-07-06 拍板)
        {
            var library = Enumerable.Repeat("灯", 5).ToArray();
            var engine = Engine(library: library, config: new BattleConfig { UnlockedChars = new[] { "木" } });
            Assert.That(engine.Library.Count, Is.EqualTo(6)); // 5 + 1,填满不越界
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // 填满当次不触发决议
        }

        [Test]
        public void SameSeed_SameDrops() // 掉字改造(2026-08-04):同种子应摇出同一个字入库
        {
            var config = new BattleConfig { UnlockedChars = new[] { "林", "灯", "焚" } };
            var a = Engine(config: config, seed: 7);
            var b = Engine(config: config, seed: 7);
            Assert.That(a.Library, Is.EqualTo(b.Library));
        }

        // ---- AP 经济(3.3) ----

        [Test]
        public void Dismantle_CostsNoAp_AndDelegatesToForge() // 拆免 AP(2026-08-03 拍板)
        {
            var engine = Engine(library: new[] { "焚" });
            var error = engine.Dismantle("焚");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Ap, Is.EqualTo(3)); // 拆不耗 AP
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "林" })); // 字回库(2026-07-22)
            Assert.That(engine.Pool, Does.Contain("火").And.Not.Contain("林")); // 部件回池
        }

        [Test]
        public void Compose_Costs1Ap()
        {
            var engine = Engine(pool: new[] { "木", "木" }); // 掉字改造(2026-08-04):部件不再靠回合掉落,直接给
            var error = engine.Compose("林");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Ap, Is.EqualTo(2));
            Assert.That(engine.Library, Does.Contain("林"));
        }

        [Test]
        public void PurpleRarityCast_Costs1Ap() // 紫字不再是「高阶」2 AP,一律 1(2026-08-03 拍板)
        {
            var engine = Engine(library: new[] { "焚" });
            engine.Cast("焚");
            Assert.That(engine.Ap, Is.EqualTo(2));
        }

        [Test]
        public void ApCost_IsOneForEveryRarity()
        {
            foreach (CardRarity rarity in Enum.GetValues(typeof(CardRarity)))
                Assert.That(CharDef.ApCostFor(rarity), Is.EqualTo(1), $"{rarity} 的 AP 应为 1");
        }

        [Test]
        public void Dismantle_CostsNoAp()
        {
            // 灯 = 火 + 丁,拆完部件回池
            var engine = Engine(library: new[] { "灯" });
            int apBefore = engine.Ap;

            var error = engine.Dismantle("灯");

            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Ap, Is.EqualTo(apBefore), "拆解不应消耗 AP");
        }

        // ---- AP 消耗一律 1,与稀有度解耦(2026-08-03 拍板;取代 07-26 的紫橙2/红3 阶梯)----

        [Test]
        public void ApCost_DerivedFromRarity() // 名字沿用,但阶梯已废:所有具名稀有度都是 1
        {
            Assert.That(CharDef.ApCostFor(CardRarity.White), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Green), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Blue), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Purple), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Gold), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Orange), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Red), Is.EqualTo(1));
        }

        [Test]
        public void CharDef_ApCost_FollowsItsRarity() // 唯一来源:建出来就带对的 AP,无处可写错
        {
            Assert.That(new CharDef("甲", Element.Metal).ApCost, Is.EqualTo(1)); // 缺省白
            Assert.That(new CharDef("乙", Element.Metal, rarity: CardRarity.Blue).ApCost, Is.EqualTo(1));
            Assert.That(new CharDef("丙", Element.Metal, rarity: CardRarity.Purple).ApCost, Is.EqualTo(1));
            Assert.That(new CharDef("丁", Element.Metal, rarity: CardRarity.Red).ApCost, Is.EqualTo(1));
        }

        [Test]
        public void LoadGraph_ApCostFromRarity_NotFromConfig() // 配置里遗留的 apCost 一律不作数
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [
                { ""id"": ""甲"", ""element"": ""Metal"", ""rarity"": ""Purple"", ""apCost"": 2 },
                { ""id"": ""乙"", ""element"": ""Metal"", ""rarity"": ""Blue"", ""apCost"": 2 },
                { ""id"": ""丙"", ""element"": ""Metal"" } ] }");
            Assert.That(graph.Get("甲").ApCost, Is.EqualTo(1)); // 紫也是 1,配置写 2 也没用
            Assert.That(graph.Get("乙").ApCost, Is.EqualTo(1)); // 蓝 = 1
            Assert.That(graph.Get("丙").ApCost, Is.EqualTo(1)); // 无稀有度 = 白 = 1
        }

        [Test]
        public void ShippedChars_ApCostMatchesRarity() // 首发字表:一眼看穿有没有跑偏
        {
            foreach (var id in new[] { "火", "炎", "焱", "燚" })
            {
                var def = Graph().TryGet(id, out var d) ? d : null;
                if (def == null) continue;
                Assert.That(def.ApCost, Is.EqualTo(CharDef.ApCostFor(def.Rarity)), $"「{id}」AP 与稀有度不符");
            }
        }

        [Test]
        public void Action_WithoutEnoughAp_Rejected()
        {
            // 出字一律 1 AP(2026-08-03):每回合 3 AP,连出 3 张耗尽后第 4 张应被拒绝
            var engine = Engine(library: new[] { "灯", "壁", "炽", "灼" });
            engine.Cast("灯");  // 3→2
            engine.Cast("壁");  // 2→1
            engine.Cast("炽");  // 1→0
            Assert.That(engine.Cast("灼"), Is.EqualTo(BattleError.NotEnoughAp)); // AP 耗尽
            Assert.That(engine.Ap, Is.EqualTo(0));
        }

        [Test]
        public void ForgeRejection_DoesNotConsumeAp()
        {
            var engine = Engine(); // 字库空
            Assert.That(engine.Dismantle("焚"), Is.EqualTo(BattleError.ForgeFailed));
            Assert.That(engine.LastForgeError, Is.EqualTo(ForgeError.NotInLibrary));
            Assert.That(engine.Ap, Is.EqualTo(3));
        }

        // ---- 出字与生克结算(wuxing-reference 规格例) ----

        [Test]
        public void Cast_Fen_VsMetal_Deals81() // 焚:floor(18×3×1.5)=81
        {
            var engine = Engine(library: new[] { "焚" }, enemies: new[] { MetalBoss(200) });
            var error = engine.Cast("焚");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 81));
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1)); // 附带灼烧层数为平值
        }

        [Test]
        public void Cast_CharConsumed_NotReusable() // 3.8.1 v0.7:出字即消耗
        {
            var engine = Engine(library: new[] { "焚" }, enemies: new[] { MetalBoss(500) });
            engine.Cast("焚");
            engine.EndTurn(); // 回到玩家回合,AP 重置
            Assert.That(engine.Library, Does.Not.Contain("焚"));
            Assert.That(engine.Cast("焚"), Is.EqualTo(BattleError.NotCastable));
        }

        [Test]
        public void Cast_ComponentDirectFromPool_ConsumesIt() // 部件直出(4.5 第二层)
        {
            var engine = Engine(pool: new[] { "火" }, enemies: new[] { MetalBoss(200) },
                config: Config("木"));
            var error = engine.Cast("火", 0);
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 6)); // floor(4×1.5)=6,火克金
            Assert.That(engine.Pool, Does.Not.Contain("火"));
        }

        [Test]
        public void Cast_Shield_UsesShengMultiplier() // 锢(金系,辟金+土):土生金 = 他生我,护盾 8×3 = 24
        {
            var engine = Engine(library: new[] { "锢" });
            engine.Cast("锢");
            Assert.That(engine.PlayerShield, Is.EqualTo(24));
        }

        [Test]
        public void Cast_Shield_SelfGeneratesOther_NoTriple() // 壁(土系,同配方):土生金是「我生他」→ 不翻倍
        {
            var engine = Engine(library: new[] { "壁" });
            engine.Cast("壁");
            Assert.That(engine.PlayerShield, Is.EqualTo(8));
        }

        [Test]
        public void Cast_SingleTarget_RequiresValidTarget_WhenMultipleAlive()
        {
            var engine = Engine(library: new[] { "灯" }, enemies: new[] { WoodMinion(), WoodMinion() });
            Assert.That(engine.Cast("灯", 5), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.Cast("灯"), Is.EqualTo(BattleError.InvalidTarget)); // 多敌未选目标
        }

        [Test]
        public void Cast_NoTarget_AutoTargetsSoleAliveEnemy() // 3.8.3 优化:单敌免选
        {
            var engine = Engine(library: new[] { "灯" }, enemies: new[] { MetalBoss(200) });
            Assert.That(engine.Cast("灯"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 9)); // floor(6×1.5) 火克金
        }

        [Test]
        public void Cast_AutoTarget_SkipsDeadEnemies()
        {
            var engine = Engine(library: new[] { "焚", "灯" },
                enemies: new[] { WoodMinion(), MetalBoss(200) });
            engine.Cast("焚");            // 木怪死,只剩金怪
            Assert.That(engine.Cast("灯"), Is.EqualTo(BattleError.None)); // 免选自动锁定金怪
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(200 - 81 - 9));
        }

        // ---- 回合末结算(3.7:灼烧先行;10.2:X层 → X×2 伤,然后 −1) ----

        [Test]
        public void EndTurn_BurnTicks_ThenDecays()
        {
            var engine = Engine(library: new[] { "燃" }, enemies: new[] { MetalBoss(200) });
            engine.Cast("燃"); // 全体 3 层灼烧
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 9)); // floor(3×2×1.5),火克金
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void BurnTick_AppliesKeMultiplierAsFire()
        {
            AssertBurnTick(Element.Metal, 9);   // 火克金:floor(6 × 1.5)
            AssertBurnTick(Element.Water, 3);   // 水克火:floor(6 × 0.5)
            AssertBurnTick(Element.Heart, 6);   // 心中立:×1.0
        }

        private static void AssertBurnTick(Element enemyElement, int expected)
        {
            var enemy = new EnemyDef("桩", enemyElement, 500, 0);   // 攻 0:不干扰玩家血量
            var engine = new BattleEngine(Graph(), Config(), new[] { "燃" },
                Array.Empty<string>(), new[] { enemy }, 42);

            engine.Cast("燃");                                       // 全体 3 层灼烧
            int hpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();

            Assert.That(hpBefore - engine.Enemies[0].Hp, Is.EqualTo(expected),
                $"对 {enemyElement} 的灼烧结算应为 {expected}");
        }

        [Test]
        public void EndTurn_EnemyAttacks_ShieldAbsorbsFirst_ShieldPersists() // 护盾段内持久,不再回合末全清
        {
            var engine = Engine(library: new[] { "锢" }, enemies: new[] { MetalBoss() }); // 攻 5
            engine.Cast("锢"); // 护盾 24
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50)); // 24 盾吸收攻 5,不掉血
            Assert.That(engine.PlayerShield, Is.EqualTo(19)); // 护盾段内持久,剩余 19
            Assert.That(engine.Turn, Is.EqualTo(2));
            Assert.That(engine.Ap, Is.EqualTo(3)); // AP 不跨回合保留,重置为 3
        }

        [Test]
        public void EnemyAttack_CarriesShieldAbsorbedPortion() // 事件分账:盾吸多少/血掉多少,表现层据此双条扣减
        {
            var engine = new BattleEngine(Graph(), Config(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("怔", Element.Heart, 100, 8) }, seed: 42, startingHp: 50, cardLevels: null,
                startingNormalShield: 5);
            engine.EndTurn(); // 攻 8:盾吃 5,血掉 3
            var hit = engine.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(hit.Amount, Is.EqualTo(8));
            Assert.That(hit.Absorbed, Is.EqualTo(5));
            Assert.That(engine.PlayerHp, Is.EqualTo(47));
        }

        [Test]
        public void EnemyAttack_NoShield_AbsorbedIsZero()
        {
            var engine = Engine(enemies: new[] { new EnemyDef("怔", Element.Heart, 100, 8) });
            engine.EndTurn();
            Assert.That(engine.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack).Absorbed, Is.EqualTo(0));
        }

        [Test]
        public void EnemyAttack_FullyAbsorbed_AbsorbedEqualsDamage()
        {
            var engine = Engine(library: new[] { "壁" }, enemies: new[] { MetalBoss() }); // 攻 5
            engine.Cast("壁");  // 护盾 24
            engine.EndTurn();
            var hit = engine.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(hit.Absorbed, Is.EqualTo(5));
            Assert.That(hit.Amount, Is.EqualTo(5));
        }

        [Test]
        public void Shield_StacksWithinTurn() // 同回合多次筑盾累加
        {
            var engine = Engine(library: new[] { "锢" }, pool: new[] { "土" }, enemies: new[] { MetalBoss() });
            engine.Cast("锢");            // 24(土生金 ×3)
            engine.Cast("土");            // 部件直出 +3(无配方,无相生)
            Assert.That(engine.PlayerShield, Is.EqualTo(27));
        }

        // ---- 兜底出字(4.5 第二层「防卡手地板,永不 brick」):无效果的部件/字均可打出弱一击 ----

        [Test]
        public void Fallback_ComponentWithoutEffects_CastsWeakHit()
        {
            var engine = Engine(pool: new[] { "木" }, enemies: new[] { new EnemyDef("怔", Element.Heart, 100, 3) },
                config: Config("丁"));
            var error = engine.Cast("木", 0);
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(97)); // 兜底单体 3(心系目标无生克)
            Assert.That(engine.Pool, Does.Not.Contain("木"));   // 部件被消耗
        }

        [Test]
        public void Fallback_AppliesWuxing() // 木 vs 土怪:木克土 ×1.5 → floor(4.5)=4
        {
            var engine = Engine(pool: new[] { "木" },
                enemies: new[] { new EnemyDef("夯", Element.Earth, 100, 3) }, config: Config("丁"));
            engine.Cast("木", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(96));
        }

        [Test]
        public void Fallback_MaterialCharInLibrary_Castable() // 林(无效果材料字)也能兜底出手
        {
            var engine = Engine(library: new[] { "林" },
                enemies: new[] { new EnemyDef("怔", Element.Heart, 100, 3) });
            var error = engine.Cast("林", 0);
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(97));
            Assert.That(engine.Library, Does.Not.Contain("林")); // 出手即消耗
        }

        [Test]
        public void Fallback_NeedsTarget_WhenMultipleAlive()
        {
            var engine = Engine(library: new[] { "林" }, enemies: new[] { WoodMinion(), WoodMinion() });
            Assert.That(engine.Cast("林"), Is.EqualTo(BattleError.InvalidTarget)); // 多敌必须选
            Assert.That(BattleEngine.NeedsTarget(new CharDef("木", Element.Wood)), Is.True);
        }

        // ---- 丢弃(3.8.2 防卡手;免 AP) ----

        [Test]
        public void Discard_FromLibrary_RemovesPermanently_NoApCost()
        {
            var engine = Engine(library: new[] { "焚", "灯" });
            var error = engine.Discard("灯");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "焚" }));
            Assert.That(engine.Ap, Is.EqualTo(3)); // 免 AP
        }

        [Test]
        public void Discard_FromPool_RemovesOneInstance()
        {
            var engine = Engine(pool: new[] { "火", "火" }, config: Config("丁"));
            var error = engine.Discard("火");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Pool.Count(x => x == "火"), Is.EqualTo(1));
        }

        [Test]
        public void Discard_NotPresent_Rejected()
        {
            var engine = Engine();
            Assert.That(engine.Discard("焚"), Is.EqualTo(BattleError.NotCastable));
        }

        [Test]
        public void Discard_AfterBattleOver_Rejected()
        {
            var engine = Engine(library: new[] { "焚", "灯" }, enemies: new[] { WoodMinion() });
            engine.Cast("焚"); // 清场获胜
            Assert.That(engine.Discard("灯"), Is.EqualTo(BattleError.BattleOver));
        }

        // ---- 条件效果(10.3.1 灼/炽、10.3.6 堡) ----

        [Test]
        public void Zhuo_DoublesBaseValue_VsBurningTarget() // 灼:8 → 对带灼烧者 16
        {
            var heart = new EnemyDef("怔", Element.Heart, 100, 3); // 心系目标,排除相克干扰
            var engine = Engine(library: new[] { "灯", "灼" }, enemies: new[] { heart });
            engine.Cast("灯", 0);  // 6 伤 + 1 层灼烧 → 94
            engine.Cast("灼", 0);  // 目标带灼烧:基础 8→16 → 78
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100 - 6 - 16));
        }

        [Test]
        public void Zhuo_NormalValue_VsCleanTarget()
        {
            var heart = new EnemyDef("怔", Element.Heart, 100, 3);
            var engine = Engine(library: new[] { "灼" }, enemies: new[] { heart });
            engine.Cast("灼", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(92)); // 无灼烧,仍是 8
        }

        [Test]
        public void Chi_RaisesBurnTick_Stackable() // 炽:每层结算 2→3;两个炽 → 4
        {
            var engine = Engine(library: new[] { "燃", "炽" }, enemies: new[] { MetalBoss(200) });
            engine.Cast("燃");   // 3 层灼烧
            engine.Cast("炽");   // 结算系数 2→3
            engine.EndTurn();    // floor(3×3×1.5)=13,火克金
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 13));
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void Bao_ShieldPersistsThroughTurns() // 堡:护盾段内持久,多轮保留
        {
            var engine = Engine(library: new[] { "堡" }, enemies: new[] { WoodMinion(hp: 200) }); // 攻 3
            engine.Cast("堡");   // 护盾 10(呆中性+土,无相生)
            engine.EndTurn();    // 吸收 3 → 7,护盾段内持久
            Assert.That(engine.PlayerShield, Is.EqualTo(7));
            engine.EndTurn();    // 吸收 3 → 4,护盾继续持久
            Assert.That(engine.PlayerShield, Is.EqualTo(4));
            Assert.That(engine.PlayerHp, Is.EqualTo(50)); // 两轮攻击全被盾挡
        }

        // ---- 胜负(3.8.4) ----

        [Test]
        public void AllEnemiesDead_Won_FurtherActionsRejected()
        {
            var engine = Engine(library: new[] { "焚" }, enemies: new[] { WoodMinion(), WoodMinion() });
            engine.Cast("焚"); // AOE 54 清场(木怪 12 血)
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(engine.Cast("火", 0), Is.EqualTo(BattleError.BattleOver));
        }

        [Test]
        public void BurnKill_AtEndTurn_CountsAsWin()
        {
            var engine = Engine(library: new[] { "燃" }, enemies: new[] { WoodMinion(hp: 5) });
            engine.Cast("燃"); // 3 层灼烧
            engine.EndTurn();  // 6 伤 ≥ 5 血
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void PlayerHpZero_Lost()
        {
            var engine = Engine(enemies: new[] { new EnemyDef("讹影", Element.Heart, 100, 60) });
            engine.EndTurn(); // 敌方攻 60 > 50 血
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(engine.PlayerHp, Is.EqualTo(0)); // 不为负
        }

        // ---- 广告复活(2026-07-24):满血续战 + 补给注入当前战斗 ----

        [Test]
        public void Revive_RestoresFullHp_AndGivesPlayerTurn()
        {
            var engine = Engine(enemies: new[] { new EnemyDef("讹影", Element.Heart, 100, 60) });
            engine.EndTurn(); // 打到败北
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));

            engine.Revive();
            Assert.That(engine.PlayerHp, Is.EqualTo(50));            // 回满
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.Ap, Is.EqualTo(engine.ApPerTurn));   // 刷了 AP,接着打
        }

        [Test]
        public void Revive_OnlyFromLost() // 非败北态无效(幂等守卫)
        {
            var engine = Engine();
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            engine.Revive();
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // 未被复活逻辑扰动
            Assert.That(engine.PlayerHp, Is.EqualTo(50));
        }

        [Test]
        public void Revive_DoesNotGrantExtraStatusTurn() // 全分支评审 Important 3(2026-08-05)锁定测试:
        // 状态回合递减必须排在 PlayerHp<=0 早退之前,否则玩家阵亡那回合被跳过的递减会拖到
        // 复活后的下一回合才补,变相让状态多续一回合。玩家上限 10、敌方攻 4、流血固定 3 回合:
        // T1 10→6、T2 6→2、T3 2→-2(阵亡)。修复后流血应在阵亡的第 3 个 EndTurn 内就已递减到期消失,
        // 而不是拖到复活后的第 4 个 EndTurn。
        {
            var engine = new BattleEngine(BleedGraph(), new BattleConfig { PlayerMaxHp = 10 },
                new[] { "锯" }, Array.Empty<string>(),
                new[] { new EnemyDef("桩", Element.Metal, 500, 4) }, 42);
            engine.Cast("锯"); // 施加 3 回合流血

            engine.EndTurn(); // T1:10-4=6
            Assert.That(engine.PlayerHp, Is.EqualTo(6));
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.True);

            engine.EndTurn(); // T2:6-4=2
            Assert.That(engine.PlayerHp, Is.EqualTo(2));
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.True);

            engine.EndTurn(); // T3:2-4<0,阵亡
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.False,
                "阵亡当回合状态也要照常递减,不能拖到复活后");

            engine.Revive();
            engine.EndTurn(); // 复活后再打一回合,流血不该"复活"或多续一回合
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.False);
        }

        [Test]
        public void GrantLibraryChar_AddsWhenRoom_RespectsCapacity()
        {
            var engine = new BattleEngine(Graph(), new BattleConfig { LibraryCapacity = 2, DropTable = Array.Empty<string>() },
                Array.Empty<string>(), Array.Empty<string>(), new[] { MetalBoss() }, seed: 42);
            Assert.That(engine.GrantLibraryChar("焚"), Is.True);
            Assert.That(engine.GrantLibraryChar("灯"), Is.True);
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "焚", "灯" }));
            Assert.That(engine.GrantLibraryChar("燃"), Is.False); // 满了,不入
            Assert.That(engine.Library.Count, Is.EqualTo(2));
        }

        [Test]
        public void GrantPoolComponent_AddsWhenRoom_RespectsCapacity()
        {
            var engine = new BattleEngine(Graph(), new BattleConfig { PoolCapacity = 2, DropTable = Array.Empty<string>() },
                Array.Empty<string>(), Array.Empty<string>(), new[] { MetalBoss() }, seed: 42);
            Assert.That(engine.GrantPoolComponent("火"), Is.True);
            Assert.That(engine.GrantPoolComponent("土"), Is.True);
            Assert.That(engine.GrantPoolComponent("木"), Is.False); // 满池不入
            Assert.That(engine.Pool.Count, Is.EqualTo(2));
        }

        [Test]
        public void DeadEnemy_DoesNotAttack_CorpseClickRedirectsToSoleAlive()
        {
            var engine = Engine(library: new[] { "焚", "灯" },
                enemies: new[] { WoodMinion(), MetalBoss(200) });
            engine.Cast("焚"); // 木怪(12)死于 54,金怪 200-81=119
            Assert.That(engine.Cast("灯", 0), Is.EqualTo(BattleError.None)); // 点尸体 → 自动转向唯一存活
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(119 - 9));
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(45)); // 只有金怪(攻5)打了一下
        }

        [Test]
        public void Corpse_InvalidTarget_WhenMultipleAlive()
        {
            var engine = Engine(library: new[] { "灯", "灯" },
                enemies: new[] { WoodMinion(hp: 4), MetalBoss(200), MetalBoss(200) });
            engine.Cast("灯", 0); // 6 伤击杀 4 血木怪
            Assert.That(engine.Cast("灯", 0), Is.EqualTo(BattleError.InvalidTarget)); // 两个存活,点尸体无效
        }

        // ---- 攻击模式(2026-07-26):拖到敌人身上出字 = 攻击,水/土 改用 AttackEffects ----

        private static RecipeGraph DragGraph() => new(new[]
        {
            new CharDef("水", Element.Water, effects: new[] { new EffectDef(EffectKind.HealSelf, 3) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("土", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 3) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("火", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("沐", Element.Water, effects: new[] { new EffectDef(EffectKind.HealSelf, 10) }),
        });

        private static BattleEngine DragEngine(string[] pool, EnemyDef enemy = null, int? hp = null) =>
            new(DragGraph(), new BattleConfig { PlayerMaxHp = 50 }, Array.Empty<string>(), pool,
                new[] { enemy ?? new EnemyDef("怔", Element.Heart, 100, 0) }, seed: 1, startingHp: hp);

        [Test]
        public void Cast_AttackMode_WaterDealsDamageInsteadOfHealing()
        {
            var engine = DragEngine(new[] { "水" }, hp: 30);
            engine.Cast("水", 0, attackMode: true);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(96)); // 水 vs 心 = ×1.0
            Assert.That(engine.PlayerHp, Is.EqualTo(30));      // 没治疗
        }

        [Test]
        public void Cast_AttackMode_EarthDealsDamageInsteadOfShield()
        {
            var engine = DragEngine(new[] { "土" });
            engine.Cast("土", 0, attackMode: true);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(96));
            Assert.That(engine.PlayerShield, Is.EqualTo(0));
        }

        [Test]
        public void Cast_NormalMode_KeepsDefaultBehaviour() // 双击照旧:治疗/加盾
        {
            var engine = DragEngine(new[] { "水", "土" }, hp: 30);
            engine.Cast("水", 0);
            engine.Cast("土", 0);
            Assert.That(engine.PlayerHp, Is.EqualTo(33));
            Assert.That(engine.PlayerShield, Is.EqualTo(3));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100));
        }

        [Test]
        public void Cast_AttackMode_CharWithoutAttackEffects_UsesNormalEffects() // 全系可拖:没有专属攻击效果就照常出
        {
            var engine = DragEngine(new[] { "火" });
            engine.Cast("火", 0, attackMode: true);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(96));
        }

        [Test]
        public void Cast_AttackMode_PureHealChar_StillHeals() // 沐无攻击效果:拖过去也只能治疗,不平白造伤
        {
            var engine = DragEngine(new[] { "沐" }, hp: 30);
            engine.Cast("沐", 0, attackMode: true);
            Assert.That(engine.PlayerHp, Is.EqualTo(40));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100));
        }

        [Test]
        public void NeedsTarget_AttackMode_TrueForWaterAndEarth() // UI 据此判断拖放是否要落在敌人身上
        {
            var graph = DragGraph();
            Assert.That(BattleEngine.NeedsTarget(graph.Get("水")), Is.False);                    // 双击:治疗,不选目标
            Assert.That(BattleEngine.NeedsTarget(graph.Get("水"), attackMode: true), Is.True);   // 拖拽:单体攻击
            Assert.That(BattleEngine.NeedsTarget(graph.Get("土"), attackMode: true), Is.True);
            Assert.That(BattleEngine.NeedsTarget(graph.Get("沐"), attackMode: true), Is.False);  // 无攻击效果,仍不选目标
        }

        [Test]
        public void LoadGraph_ParsesAttackEffects()
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [
                { ""id"": ""水"", ""element"": ""Water"",
                  ""effects"": [ { ""kind"": ""HealSelf"", ""value"": 3 } ],
                  ""attackEffects"": [ { ""kind"": ""DamageSingle"", ""value"": 4 } ] },
                { ""id"": ""火"", ""element"": ""Fire"",
                  ""effects"": [ { ""kind"": ""DamageSingle"", ""value"": 4 } ] } ] }");
            var water = graph.Get("水");
            Assert.That(water.Effects.Single().Kind, Is.EqualTo(EffectKind.HealSelf));
            Assert.That(water.AttackEffects.Single().Kind, Is.EqualTo(EffectKind.DamageSingle));
            Assert.That(water.AttackEffects.Single().Value, Is.EqualTo(4));
            Assert.That(graph.Get("火").AttackEffects, Is.Empty); // 没写就是空,拖放与双击同效
        }

        [Test]
        public void Cast_AttackMode_MultipleEnemies_HitsTheDroppedOne()
        {
            var engine = new BattleEngine(DragGraph(), new BattleConfig { PlayerMaxHp = 50 },
                Array.Empty<string>(), new[] { "水" },
                new[] { new EnemyDef("甲", Element.Heart, 100, 0), new EnemyDef("乙", Element.Heart, 100, 0) },
                seed: 1);
            engine.Cast("水", 1, attackMode: true);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100));
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(96));
        }

        // ---- 五系特色(2026-07-19 拍板):水主治疗 / 木主召唤(前排抗伤+反击) / 土盾附攻 ----

        private static RecipeGraph IdentityGraph() => new(new[]
        {
            new CharDef("沐", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 10) }),
            new CharDef("林", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("木", Element.Wood),
        });

        private static BattleEngine IdentityEngine(string[] library, EnemyDef[] enemies, int? startingHp = null) =>
            new(IdentityGraph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, System.Array.Empty<string>(), enemies, seed: 1, startingHp: startingHp);

        [Test]
        public void HealSelf_HealsPlayer_CapsAtMaxHp()
        {
            var engine = IdentityEngine(new[] { "沐", "沐" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 3) }, startingHp: 35);
            engine.Cast("沐");
            Assert.That(engine.PlayerHp, Is.EqualTo(45)); // +10
            engine.Cast("沐");
            Assert.That(engine.PlayerHp, Is.EqualTo(50)); // 封顶不溢出
        }

        [Test]
        public void Summon_SpawnsTrees_ThatTankAndFightBack()
        {
            var engine = IdentityEngine(new[] { "林" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 5) });
            engine.Cast("林");
            Assert.That(engine.Summons.Count, Is.EqualTo(2));
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(6));
            Assert.That(engine.Summons[0].Char, Is.EqualTo("木"));

            int hpBefore = engine.PlayerHp;
            engine.EndTurn();
            // 树反击:2×2 伤(木 vs 心 1.0);敌攻 5 打在首棵树上,玩家无伤
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100 - 4));
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(1)); // 6 - 5
            Assert.That(engine.PlayerHp, Is.EqualTo(hpBefore));
        }

        [Test]
        public void Summon_DeadTree_NextAttackHitsPlayer()
        {
            var engine = IdentityEngine(new[] { "林" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 8) });
            engine.Cast("林");
            engine.EndTurn(); // 敌攻 8:树 6 血阵亡(不溢出)
            Assert.That(engine.Summons[0].Alive, Is.False);
            Assert.That(engine.PlayerHp, Is.EqualTo(50));
            engine.EndTurn(); // 第二棵树 6 血也被 8 攻击倒
            engine.EndTurn(); // 无树,打到玩家
            Assert.That(engine.PlayerHp, Is.EqualTo(50 - 8));
        }

        [Test]
        public void SummonHit_CarriesTargetSummonIndex() // 承伤者下标随坦克前移:0 号阵亡后打 1 号(动效定位用)
        {
            var engine = IdentityEngine(new[] { "林" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 8) });
            engine.Cast("林");  // 召 2 棵树(各 6 血)
            engine.EndTurn();   // 敌攻 8 → 0 号树阵亡
            Assert.That(engine.LastEvents.Single(e => e.Kind == BattleEventKind.SummonHit).SecondIndex, Is.EqualTo(0));
            engine.EndTurn();   // 顶前排前移到 1 号树承伤
            Assert.That(engine.LastEvents.Single(e => e.Kind == BattleEventKind.SummonHit).SecondIndex, Is.EqualTo(1));
        }

        [Test]
        public void SummonAttack_CarriesSourceSummonIndex() // 发起者下标:飞牌起点定位用
        {
            var engine = IdentityEngine(new[] { "林" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 0) });
            engine.Cast("林"); // 召 2 棵树
            engine.EndTurn();  // 两棵树各反击一次
            var attacks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).ToList();
            Assert.That(attacks.Count, Is.EqualTo(2));
            Assert.That(attacks[0].SecondIndex, Is.EqualTo(0));
            Assert.That(attacks[1].SecondIndex, Is.EqualTo(1));
        }

        // 焦痕(受击存活加攻)会在每记伤害后追一条 EnemyBuff。表现层要按「每记召唤反击」逐记播,
        // 靠事件流自身的结构切段,所以这两条契约必须由 Core 保证。
        private static BattleEngine SummonsVsScorch()
        {
            var engine = IdentityEngine(new[] { "林", "林" },
                new[] { new EnemyDef("焦痕", Element.Fire, 100, 4, EnemyAbility.Scorch) });
            engine.Cast("林");
            engine.Cast("林"); // 4 只召唤在场
            return engine;
        }

        [Test]
        public void EndTurn_EnemyTurnBegan_SeparatesSummonPhaseFromEnemyPhase()
        {
            var engine = SummonsVsScorch();
            engine.EndTurn();
            var kinds = engine.LastEvents.Select(e => e.Kind).ToList();

            int split = kinds.IndexOf(BattleEventKind.EnemyTurnBegan);
            Assert.That(kinds.Count(k => k == BattleEventKind.EnemyTurnBegan), Is.EqualTo(1));
            Assert.That(kinds.LastIndexOf(BattleEventKind.SummonAttack), Is.LessThan(split));  // 召唤段全在前
            Assert.That(kinds.IndexOf(BattleEventKind.SummonHit), Is.GreaterThan(split));      // 敌方段全在后
        }

        [Test]
        public void EndTurn_EveryLivingSummonGetsItsOwnStrike() // 4 只都要出手,一只都不能少
        {
            var engine = SummonsVsScorch();
            engine.EndTurn();
            var attacks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).ToList();
            Assert.That(attacks.Count, Is.EqualTo(4));
            Assert.That(attacks.Select(e => e.SecondIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void EndTurn_ScorchBuff_StaysInsideItsOwnStrike() // 加攻紧跟它那记伤害,不越到下一记
        {
            var engine = SummonsVsScorch();
            engine.EndTurn();
            var kinds = engine.LastEvents.Select(e => e.Kind)
                .TakeWhile(k => k != BattleEventKind.EnemyTurnBegan).ToList();
            // 召唤段应为 4 组「SummonAttack, Damage, EnemyBuff」
            for (int n = 0; n < 4; n++)
            {
                Assert.That(kinds[n * 3], Is.EqualTo(BattleEventKind.SummonAttack), $"第 {n + 1} 记");
                Assert.That(kinds[n * 3 + 1], Is.EqualTo(BattleEventKind.Damage), $"第 {n + 1} 记");
                Assert.That(kinds[n * 3 + 2], Is.EqualTo(BattleEventKind.EnemyBuff), $"第 {n + 1} 记");
            }
        }

        [Test]
        public void Summon_CapsAtFourAlive()
        {
            var engine = IdentityEngine(new[] { "林", "林", "林" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 0) });
            engine.Cast("林");
            engine.Cast("林"); // 已满 4
            int alive = 0;
            foreach (var summon in engine.Summons)
                if (summon.Alive) alive++;
            Assert.That(alive, Is.EqualTo(4));
        }

        // ---- 前排满员强阻断(2026-07-25):不吃 AP/不消耗字,由 UI 弹窗确认后走替换 ----

        /// <summary>可区分的召唤源:甲召 1 只「A」10 血,乙召 1 只「B」20 血,丙召 2 只「C」。</summary>
        private static BattleEngine ReplaceEngine(string[] library) => new(
            new RecipeGraph(new[]
            {
                new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "A") }),
                new CharDef("乙", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 20, summonCount: 1, summonAttack: 0, summonChar: "B") }),
                new CharDef("丙", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 2, summonAttack: 0, summonChar: "C") }),
            }),
            new BattleConfig { PlayerMaxHp = 50, ApPerTurn = 9, LibraryCapacity = 9 },
            library, Array.Empty<string>(),
            new[] { new EnemyDef("怔", Element.Heart, 100, 0) }, seed: 1);

        [Test]
        public void Cast_SummonAtCap_IsBlocked_KeepsApAndChar()
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "甲", "乙" });
            for (int i = 0; i < 6; i++) engine.Cast("甲");
            int apBefore = engine.Ap;

            Assert.That(engine.Cast("乙"), Is.EqualTo(BattleError.SummonCapFull));
            Assert.That(engine.Ap, Is.EqualTo(apBefore));   // 强阻断:AP 不吃
            Assert.That(engine.Library, Contains.Item("乙")); // 字不消耗
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));
        }

        [Test]
        public void Cast_SummonAtCap_ReplaceMode_ReplacesFirstSlot()
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "甲", "乙" });
            for (int i = 0; i < 6; i++) engine.Cast("甲");

            Assert.That(engine.Cast("乙", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6)); // 不超编
            Assert.That(engine.Summons[0].Char, Is.EqualTo("B"));
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(20));   // 新召唤满血入场
            Assert.That(engine.Summons[1].Char, Is.EqualTo("A")); // 其余不动
        }

        [Test]
        public void SummonCountOf_SumsAcrossEffects_CapsAtSummonCapacity() // UI 文案「顶掉最前 N 只」的 N
        {
            var engine = ReplaceEngine(new[] { "甲" });
            var graph = new RecipeGraph(new[]
            {
                new CharDef("甲", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "A") }),
                new CharDef("丙", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 2, summonAttack: 0, summonChar: "C") }),
                new CharDef("丁", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.DamageAll, 5),
                    new EffectDef(EffectKind.Summon, 5, summonCount: 4, summonAttack: 0, summonChar: "D"),
                    new EffectDef(EffectKind.Summon, 5, summonCount: 4, summonAttack: 0, summonChar: "D"),
                }),
                new CharDef("戊", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            Assert.That(engine.SummonCountOf(graph.Get("甲")), Is.EqualTo(1));
            Assert.That(engine.SummonCountOf(graph.Get("丙")), Is.EqualTo(2));
            Assert.That(engine.SummonCountOf(graph.Get("丁")), Is.EqualTo(6)); // 4+4=8 被砍到上限 6
            Assert.That(engine.SummonCountOf(graph.Get("戊")), Is.EqualTo(0)); // 不召唤
        }

        [Test]
        public void Cast_SummonAtCap_ReplaceMode_MultiSummon_AdvancesFromFirst() // 一次召 2:顶掉最前两只,不重复顶自己
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 6; i++) engine.Cast("甲");

            engine.Cast("丙", replaceSummon: true);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));
            Assert.That(engine.Summons[0].Char, Is.EqualTo("C"));
            Assert.That(engine.Summons[1].Char, Is.EqualTo("C"));
            Assert.That(engine.Summons[2].Char, Is.EqualTo("A"));
        }

        [Test]
        public void Cast_SummonOverflowsCap_IsBlocked() // 未满但放不下也拦:5/6 时召 2 只会溢出 1
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 5; i++) engine.Cast("甲");

            Assert.That(engine.Cast("丙"), Is.EqualTo(BattleError.SummonCapFull));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(5)); // 一只都没进
        }

        [Test]
        public void Cast_SummonExactlyFits_NotBlocked() // 刚好填满不拦
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 4; i++) engine.Cast("甲");

            Assert.That(engine.Cast("丙"), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));
            Assert.That(engine.Summons[0].Char, Is.EqualTo("A")); // 有空位就不该顶谁
        }

        [Test]
        public void Cast_SummonOverflow_ReplaceMode_FillsGapThenReplacesFromFirst() // 5/6 召 2:先占空位,溢出的才顶最前
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 5; i++) engine.Cast("甲");

            engine.Cast("丙", replaceSummon: true);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));
            Assert.That(engine.Summons[5].Char, Is.EqualTo("C")); // 第 1 只填空位
            Assert.That(engine.Summons[0].Char, Is.EqualTo("C")); // 第 2 只顶掉最前
            Assert.That(engine.Summons[1].Char, Is.EqualTo("A")); // 其余不动
            Assert.That(engine.Summons[2].Char, Is.EqualTo("A"));
        }

        [Test]
        public void SummonReplaceCountOf_CountsOnlyTheOverflow() // 弹窗文案「顶掉最前 N 只」的 N
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "甲", "丙" });
            var bing = new RecipeGraph(new[]
            {
                new CharDef("丙", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 2, summonAttack: 0, summonChar: "C") }),
                new CharDef("戊", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(0)); // 0/6:空位够
            engine.Cast("甲");
            engine.Cast("甲");
            engine.Cast("甲");
            engine.Cast("甲");
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(0)); // 4/6:刚好填满
            engine.Cast("甲");
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(1)); // 5/6:溢出 1
            Assert.That(engine.SummonReplaceCountOf(bing.Get("戊")), Is.EqualTo(0)); // 不召唤的字永远 0
        }

        [Test]
        public void Cast_NonSummonChar_NeverBlockedByCap() // 满员只拦召唤字,伤害/护盾字照出
        {
            var engine = IdentityEngine(new[] { "林", "林", "林", "沐" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 0) }, startingHp: 30);
            engine.Cast("林");
            engine.Cast("林");
            engine.Cast("林"); // 满 6(3×2)
            engine.EndTurn();  // 刷新 AP,用下一回合来验证满编时非召唤字照样不拦
            Assert.That(engine.Cast("沐"), Is.EqualTo(BattleError.None));
        }

        private static BattleEngine TankEngine(EnemyDef enemy)
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("林", Element.Wood, effects: new[]
                {
                    new EffectDef(EffectKind.Summon, 20, summonCount: 1, summonAttack: 0, summonChar: "木"),
                }),
            });
            return new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                new[] { "林" }, Array.Empty<string>(), new[] { enemy }, seed: 1);
        }

        [Test]
        public void Summon_TankHit_MetalAmplifiedByWuxing() // 金克木:召唤顶前排受 ×1.5(修复:此前原样吃伤)
        {
            var engine = TankEngine(new EnemyDef("锈", Element.Metal, 100, 6));
            engine.Cast("林");
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(11)); // 20 - floor(6×1.5)=9
        }

        [Test]
        public void Minion_DamageTaken_ReducesDamage() // 小怪级承伤减免(墨渍):非成语怪也可承伤打折
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("击", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "击" }, Array.Empty<string>(),
                new[] { new EnemyDef("湿", Element.Water, 100, 0, damageTaken: 0.5f) }, seed: 1);
            engine.Cast("击");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(95)); // floor(10 × 1.0(心) × 0.5)=5
        }

        [Test]
        public void Fortify_BrokenByElementCounter() // 坚壁遇属性克制失效:被克(×1.5)按克制结算,不再乘承伤减免
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("斫", Element.Wood, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "斫" }, Array.Empty<string>(),
                new[] { new EnemyDef("垒", Element.Earth, 100, 0, damageTaken: 0.75f) }, seed: 1);
            engine.Cast("斫"); // 木克土 ×1.5,坚壁失效:floor(10 × 1.5)=15,而非 floor(10 × 1.5 × 0.75)=11
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(85));
        }

        [Test]
        public void Fortify_AppliesWhenCountered() // 坚壁只被「克制」打穿:自己被克(×0.5)时减免照常生效
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("涓", Element.Water, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "涓" }, Array.Empty<string>(),
                new[] { new EnemyDef("垒", Element.Earth, 100, 0, damageTaken: 0.5f) }, seed: 1);
            engine.Cast("涓"); // 土克水:水打土被克 ×0.5,坚壁仍生效:floor(10 × 0.5 × 0.5)=2
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(98));
        }

        // ---- 承伤结算(2026-08-05):减免遭克失效,加成始终生效 ----

        [Test]
        public void DamageTaken_AboveOne_SurvivesElementCounter()
        {
            // 承伤 1.25 的金系敌人,挨火系克制攻击(火克金 ×1.5):
            // 加成不该被「减免遭克失效」那条规则连坐吃掉
            var armored = new EnemyDef("锈", Element.Metal, 500, 0,
                EnemyAbility.None, null, 1.25f);
            var engine = new BattleEngine(Graph(), Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { armored }, 42);
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("灯", 0); // 灯:DamageSingle 6,火系

            // floor(6 × 1.5 克制 × 1.25 加成) = 11
            Assert.That(hp0 - engine.Enemies[0].Hp, Is.EqualTo(11));
        }

        [Test]
        public void DamageTaken_BelowOne_StillLostToElementCounter() // 既有行为,不许变
        {
            var tough = new EnemyDef("锈", Element.Metal, 500, 0,
                EnemyAbility.None, null, 0.5f);
            var engine = new BattleEngine(Graph(), Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { tough }, 42);
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("灯", 0);

            // 减免遭克失效:floor(6 × 1.5) = 9,不再 ×0.5
            Assert.That(hp0 - engine.Enemies[0].Hp, Is.EqualTo(9));
        }

        // ---- 破甲(2026-08-05):承伤 +25%,不叠层,持续 2 回合 ----

        /// <summary>破甲测试专用:碎 = DamageSingle 4 + ArmorBreak 2,打无减免的中立敌人。</summary>
        private static BattleEngine ArmorBreakEngine()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("石", Element.Earth),
                new CharDef("卒", null),
                new CharDef("碎", Element.Earth, new[] { "石", "卒" }, effects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 4),
                    new EffectDef(EffectKind.ArmorBreak, 2),
                }),
            });
            return new BattleEngine(graph, Config(), new[] { "碎", "碎" },
                Array.Empty<string>(), new[] { new EnemyDef("桩", Element.Heart, 500, 0) }, 42);
        }

        [Test]
        public void ArmorBreak_RaisesDamageTakenByQuarter()
        {
            var engine = ArmorBreakEngine();     // 见 Step 3
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("碎", 0);                 // DamageSingle 4 + ArmorBreak 2
            int firstHit = hp0 - engine.Enemies[0].Hp;
            Assert.That(firstHit, Is.EqualTo(4), "第一击本身不吃破甲(破甲在伤害之后施加)");

            int hp1 = engine.Enemies[0].Hp;
            engine.Cast("碎", 0);                 // 第二张碎:目标已破甲
            Assert.That(hp1 - engine.Enemies[0].Hp, Is.EqualTo(5), "floor(4 × 1.25) = 5");
        }

        [Test]
        public void ArmorBreak_DoesNotStack_OnlyRefreshes()
        {
            var engine = ArmorBreakEngine();
            engine.Cast("碎", 0);
            engine.Cast("碎", 0);

            var bag = engine.Enemies[0].Statuses;
            int count = 0;
            foreach (var s in bag.All) if (s.Kind == StatusKind.ArmorBreak) count++;
            Assert.That(count, Is.EqualTo(1), "不叠层:只有一条");
            Assert.That(bag.TotalMagnitude(StatusKind.ArmorBreak), Is.EqualTo(25), "恒 25,不累加");
        }

        [Test]
        public void ArmorBreak_IsDebuffPolarity() // 为子项目 A 的 Cleanse 铺路
        {
            var engine = ArmorBreakEngine();
            engine.Cast("碎", 0);
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.ArmorBreak).Polarity,
                Is.EqualTo(StatusPolarity.Debuff));
        }

        [Test]
        public void ArmorBreak_ExpiresAfterTwoTurns()
        {
            var engine = ArmorBreakEngine();
            engine.Cast("碎", 0);
            engine.EndTurn();
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.ArmorBreak), Is.False);
        }

        // ---- 穿甲(2026-08-05):忽略目标减免 + 固定 +15%,后者是保底价值 ----

        /// <summary>穿甲测试专用:锥 = DamageSingle 9 带 ignoreArmor;碎 = DamageSingle 4 + 破甲。</summary>
        private static BattleEngine PierceEngine(EnemyDef enemy)
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("钅", Element.Metal),
                new CharDef("隹", null),
                new CharDef("石", Element.Earth),
                new CharDef("卒", null),
                new CharDef("锥", Element.Metal, new[] { "钅", "隹" }, effects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 9, ignoreArmor: true),
                }),
                new CharDef("碎", Element.Earth, new[] { "石", "卒" }, effects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 4),
                    new EffectDef(EffectKind.ArmorBreak, 2),
                }),
            });
            return new BattleEngine(graph, Config(), new[] { "锥", "碎", "锥" },
                Array.Empty<string>(), new[] { enemy }, 42);
        }

        [Test]
        public void IgnoreArmor_BypassesReductionAndAddsFlatBonus()
        {
            // 减免 0.5 的心系敌人(心不参与生克,排除克制干扰)
            var tough = new EnemyDef("桩", Element.Heart, 500, 0, EnemyAbility.None, null, 0.5f);
            var engine = PierceEngine(tough);
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("锥", 0);   // DamageSingle 9,ignoreArmor

            // 忽略减免 → 1.0,再 +15% → floor(9 × 1.15) = 10
            Assert.That(hp0 - engine.Enemies[0].Hp, Is.EqualTo(10));
        }

        [Test]
        public void IgnoreArmor_FlatBonusAppliesToUnarmoredToo() // 口径 9 的保底价值
        {
            var plain = new EnemyDef("桩", Element.Heart, 500, 0);
            var engine = PierceEngine(plain);
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("锥", 0);

            Assert.That(hp0 - engine.Enemies[0].Hp, Is.EqualTo(10), "floor(9 × 1.15) = 10");
        }

        [Test]
        public void IgnoreArmor_StacksWithArmorBreak() // 口径 6:只忽略减免,不忽略破甲加成
        {
            var plain = new EnemyDef("桩", Element.Heart, 500, 0);
            var engine = PierceEngine(plain);
            engine.Cast("碎", 0);            // 先破甲
            int hp1 = engine.Enemies[0].Hp;

            engine.Cast("锥", 0);

            // 1 + 0.15 穿甲 + 0.25 破甲 = 1.40 → floor(9 × 1.4) = 12
            Assert.That(hp1 - engine.Enemies[0].Hp, Is.EqualTo(12));
        }

        [Test]
        public void NonPiercing_GetsNoFlatBonus() // 证明 15% 只属于穿甲
        {
            var plain = new EnemyDef("桩", Element.Heart, 500, 0);
            var engine = PierceEngine(plain);
            int hp0 = engine.Enemies[0].Hp;

            engine.Cast("碎", 0);   // 非穿甲,DamageSingle 4

            Assert.That(hp0 - engine.Enemies[0].Hp, Is.EqualTo(4));
        }

        [Test]
        public void Scorch_GainsAttackOnSurvivingHit() // 焦痕自燃:每次被击中且存活,攻 +2
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("击", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "击", "击" }, Array.Empty<string>(),
                new[] { new EnemyDef("焦", Element.Fire, 100, 4, EnemyAbility.Scorch) }, seed: 1);
            engine.Cast("击"); // 命中存活:攻 4→6
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6));
            engine.Cast("击"); // 再命中:攻 6→8
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(8));
        }

        [Test]
        public void Scorch_KillingBlow_NoAttackGain() // 一击秒杀不加攻(已死)
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("击", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "击" }, Array.Empty<string>(),
                new[] { new EnemyDef("焦", Element.Fire, 3, 4, EnemyAbility.Scorch) }, seed: 1);
            engine.Cast("击"); // 5 伤秒杀 3 血
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(4)); // 未加攻
        }

        [Test]
        public void Summon_TankHit_EarthReducedByWuxing() // 木反克土:召唤顶前排受 ×0.5
        {
            var engine = TankEngine(new EnemyDef("垚", Element.Earth, 100, 6));
            engine.Cast("林");
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(17)); // 20 - floor(6×0.5)=3
        }

        [Test]
        public void LoadGraph_ParsesHealAndSummon()
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [
                { ""id"": ""木"" },
                { ""id"": ""沐"", ""element"": ""Water"", ""effects"": [ { ""kind"": ""HealSelf"", ""value"": 10 } ] },
                { ""id"": ""林"", ""element"": ""Wood"", ""effects"": [
                    { ""kind"": ""Summon"", ""value"": 6, ""count"": 2, ""attack"": 2, ""summonChar"": ""木"" } ] }
            ] }");
            var summon = graph.Get("林").Effects[0];
            Assert.That(summon.Kind, Is.EqualTo(EffectKind.Summon));
            Assert.That(summon.SummonCount, Is.EqualTo(2));
            Assert.That(summon.SummonAttack, Is.EqualTo(2));
            Assert.That(summon.SummonChar, Is.EqualTo("木"));
            Assert.That(graph.Get("沐").Effects[0].Kind, Is.EqualTo(EffectKind.HealSelf));
        }

        [Test]
        public void CardLevel_ScalesActualDamage() // 探针:等级是否真的进了结算
        {
            var enemy = new EnemyDef("木桩", Element.Heart, 500, 0);
            var lv1 = new BattleEngine(Graph(), Config(), new[] { "灼" }, Array.Empty<string>(),
                new[] { enemy }, seed: 1);
            var lv3 = new BattleEngine(Graph(), Config(), new[] { "灼" }, Array.Empty<string>(),
                new[] { enemy }, seed: 1, cardLevels: new System.Collections.Generic.Dictionary<string, int> { ["灼"] = 3 });

            lv1.Cast("灼", 0);
            lv3.Cast("灼", 0);
            int dmg1 = 500 - lv1.Enemies[0].Hp;
            int dmg3 = 500 - lv3.Enemies[0].Hp;
            TestContext.WriteLine($"Lv1={dmg1} Lv3={dmg3}");
            Assert.That(dmg3, Is.GreaterThan(dmg1));
        }

        [Test]
        public void SummonCapacity_IsSix()
        {
            Assert.That(Engine().SummonCapacity, Is.EqualTo(6));
        }

        private static RecipeGraph BleedGraph() => new(new[]
        {
            new CharDef("锯", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.Bleed, 3) }),
        });

        [Test]
        public void Statuses_BurnAndBleed_QueryableByKind()
        {
            var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { new EnemyDef("桩", Element.Metal, 500, 0) }, 42);
            engine.Cast("锯");

            var bag = engine.Enemies[0].Statuses;
            Assert.That(bag.Has(StatusKind.Bleed), Is.True);
            Assert.That(bag.TotalMagnitude(StatusKind.Bleed), Is.EqualTo(3));
            Assert.That(bag.Find(StatusKind.Bleed).Polarity, Is.EqualTo(StatusPolarity.Debuff));
        }

        [Test]
        public void Bleed_IgnoresElementMultipliers()
        {
            foreach (var element in new[] { Element.Metal, Element.Water, Element.Heart })
            {
                var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                    Array.Empty<string>(), new[] { new EnemyDef("桩", element, 500, 0) }, 42);
                engine.Cast("锯");
                int hpBefore = engine.Enemies[0].Hp;

                engine.EndTurn();

                Assert.That(hpBefore - engine.Enemies[0].Hp, Is.EqualTo(3),
                    $"流血对 {element} 应等值 3");
            }
        }

        [Test]
        public void Bleed_ExpiresAfterThreeTurns()
        {
            var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { new EnemyDef("桩", Element.Metal, 500, 0) }, 42);
            engine.Cast("锯");

            for (int i = 0; i < 3; i++) engine.EndTurn();
            int afterExpiry = engine.Enemies[0].Hp;
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(afterExpiry), "流血到期后不再掉血");
        }

        [Test]
        public void Bleed_EmitsTickEvent() // 灼烧发 BurnTick,流血也得发 —— 否则表现层看不到,血条会无故下降
        {
            var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { new EnemyDef("桩", Element.Metal, 500, 0) }, 42);
            engine.Cast("锯");

            engine.EndTurn();

            var tick = engine.LastEvents.Single(e => e.Kind == BattleEventKind.BleedTick);
            Assert.That(tick.TargetIndex, Is.EqualTo(0));
            Assert.That(tick.Amount, Is.EqualTo(3));
        }

        [Test]
        public void Bleed_TriggersBossPhase() // 流血打过阶段阈值也要换阶段(灼烧段有 CheckBossPhase,流血段漏了)
        {
            // 阶段血量各 2 → 总血 4、一阶段阈值 2;流血 3 一回合即打穿
            var boss = new EnemyDef("成语", Element.Metal, 4, 0, EnemyAbility.None, new[]
            {
                new BossPhaseDef("成", Element.Metal, 2, 0, 1f, BossSkill.None),
                new BossPhaseDef("语", Element.Metal, 2, 0, 1f, BossSkill.None),
            });
            var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { boss }, 42);
            engine.Cast("锯");

            engine.EndTurn();

            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase),
                "流血把 Boss 打过阶段阈值,应触发换阶段");
        }

        [Test]
        public void Bleed_SurvivesSnapshotRoundTrip() // 状态字段加进 EnemyState 必须同步进快照,否则续爬会悄悄回退
        {
            var enemyDef = new EnemyDef("桩", Element.Metal, 500, 0);
            var engine = new BattleEngine(BleedGraph(), Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.Cast("锯");

            var snapshot = engine.Capture();
            var restored = BattleEngine.Restore(snapshot, BleedGraph(), Config(), null,
                new System.Collections.Generic.Dictionary<string, EnemyDef> { ["桩"] = enemyDef });

            var bleed = restored.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed.Magnitude, Is.EqualTo(3));
            Assert.That(bleed.TurnsLeft, Is.EqualTo(3));

            int hpBefore = restored.Enemies[0].Hp;
            restored.EndTurn();
            Assert.That(hpBefore - restored.Enemies[0].Hp, Is.EqualTo(3), "读档后流血应继续正常结算");
        }

        // ---- 治疗补格(2026-08-03):群体即时(HealAll) / 持续单体或群体(HealOverTime) ----

        private static RecipeGraph HealGraph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("林", Element.Wood, new[] { "木", "木" },
                effects: new[] { new EffectDef(EffectKind.Summon, 11, summonCount: 2,
                                               summonAttack: 5, summonChar: "木") }),
            new CharDef("淋", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealAll, 9) }),
            new CharDef("沐", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealOverTime, 3, turns: 3) }),
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DamageReduction, 20) }),
        });

        [Test]
        public void PlayerStatuses_HotAndReduction_QueryableByPolarity()
        {
            var engine = new BattleEngine(HealGraph(), Config(), new[] { "沐", "铠" },
                Array.Empty<string>(), new[] { WoodMinion() }, 42);
            engine.Cast("沐");
            engine.Cast("铠");

            var buffs = 0;
            foreach (var s in engine.PlayerStatuses.All)
                if (s.Polarity == StatusPolarity.Buff) buffs++;
            Assert.That(buffs, Is.EqualTo(2)); // HoT + 减伤
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DamageReduction), Is.EqualTo(20));
        }

        [Test]
        public void HealAll_HealsPlayerAndSummons()
        {
            var engine = new BattleEngine(HealGraph(), Config(), new[] { "林", "淋" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);
            engine.Cast("林");                    // 召 2 只顶前排
            engine.EndTurn();                     // 敌人打召唤物,玩家也可能掉血
            int summonHpBefore = engine.Summons[0].Hp;

            engine.Cast("淋");

            Assert.That(engine.Summons[0].Hp, Is.GreaterThan(summonHpBefore),
                "群疗应治召唤物");
        }

        [Test]
        public void HealOverTime_HealsEachTurnThenExpires()
        {
            var engine = new BattleEngine(HealGraph(), Config(), new[] { "沐" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);
            engine.EndTurn();                     // 先挨一下,腾出治疗空间
            engine.Cast("沐");

            int hp0 = engine.PlayerHp;
            engine.EndTurn();
            int gain1 = engine.PlayerHp - hp0 + 6;   // 加回本回合挨的 6 点
            Assert.That(gain1, Is.EqualTo(3), "第 1 回合应回复 3");

            engine.EndTurn();
            engine.EndTurn();
            int hpAfterExpiry = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hpAfterExpiry - 6),
                "HoT 到期后只挨打、不再回复");
        }

        [Test]
        public void HealOverTime_SameCharCastTwice_Stacks() // 「滋」可叠(技能机制详表):同字连放
                                                              // 应是两条独立倒计时,不是刷新成一条
        {
            var engine = new BattleEngine(HealGraph(), Config(), new[] { "沐", "沐" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);
            engine.EndTurn();                     // 先挨一下,腾出治疗空间
            engine.Cast("沐");
            engine.Cast("沐");

            int hotCount = 0;
            foreach (var s in engine.PlayerStatuses.All)
                if (s.Kind == StatusKind.HealOverTime) hotCount++;
            Assert.That(hotCount, Is.EqualTo(2), "同字连放不应互相覆盖");

            int hp0 = engine.PlayerHp;
            engine.EndTurn();
            int gain = engine.PlayerHp - hp0 + 6;   // 加回本回合挨的 6 点
            Assert.That(gain, Is.EqualTo(6), "两条各回 3,治疗量应是单条的两倍——证明确实可叠而不只是数据结构里躺了两条");
        }

        [Test]
        public void HealOverTime_SurvivesSnapshotRoundTrip() // HoT 挂在 BattleEngine._playerStatuses 上,不在 EnemyState/SummonState 里,
                                                              // 得单独确认 Capture/Restore 有往返(Digest() 不会自动覆盖新字段)
        {
            var enemyDef = new EnemyDef("锈", Element.Metal, 500, 6);
            var engine = new BattleEngine(HealGraph(), Config(), new[] { "沐" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.EndTurn();
            engine.Cast("沐");

            var snapshot = engine.Capture();
            var restored = BattleEngine.Restore(snapshot, HealGraph(), Config(), null,
                new System.Collections.Generic.Dictionary<string, EnemyDef> { ["锈"] = enemyDef });

            int hp0 = restored.PlayerHp;
            restored.EndTurn();
            int gain1 = restored.PlayerHp - hp0 + 6;
            Assert.That(gain1, Is.EqualTo(3), "读档后 HoT 应继续按回合回复");

            restored.EndTurn();
            restored.EndTurn();
            int hpAfterExpiry = restored.PlayerHp;
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(hpAfterExpiry - 6), "读档后到期照样停止回复");
        }

        // ---- Freeze:冻结跳过整回合(2026-08-03;藤的「束缚」也走这个 Kind) ----

        private static RecipeGraph FreezeGraph() => new(new[]
        {
            new CharDef("冻", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Freeze, 1) }),
        });

        [Test]
        public void Freeze_SkipsEnemyTurnThenResumes()
        {
            var engine = new BattleEngine(FreezeGraph(), Config(), new[] { "冻", "冻" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);
            engine.Cast("冻");
            int hp0 = engine.PlayerHp;

            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "冻结回合敌人不出手");

            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 6), "解冻后恢复出手");
        }

        [Test]
        public void Freeze_SurvivesSnapshotRoundTrip() // 状态字段加进 EnemyState 必须同步进快照,否则续爬会悄悄回退
        {
            var enemyDef = new EnemyDef("锈", Element.Metal, 500, 6);
            var engine = new BattleEngine(FreezeGraph(), Config(), new[] { "冻", "冻" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.Cast("冻");

            var snapshot = engine.Capture();
            var restored = BattleEngine.Restore(snapshot, FreezeGraph(), Config(), null,
                new System.Collections.Generic.Dictionary<string, EnemyDef> { ["锈"] = enemyDef });

            Assert.That(restored.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(1));

            int hp0 = restored.PlayerHp;
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(hp0), "读档后冻结回合仍不出手");

            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.EqualTo(hp0 - 6), "读档后解冻仍恢复出手");
        }

        // ---- Slow:半速,每 2 回合才行动一次(2026-08-03) ----

        private static RecipeGraph SlowGraph() => new(new[]
        {
            new CharDef("冷", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Slow, 4) }),
        });

        [Test]
        public void Slow_EnemyActsEveryOtherTurn()
        {
            var engine = new BattleEngine(SlowGraph(), Config(), new[] { "冷" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);
            engine.Cast("冷");

            int hp0 = engine.PlayerHp;
            engine.EndTurn();                       // 减速第 1 回合:跳过
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0));

            engine.EndTurn();                       // 第 2 回合:行动
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 6));

            engine.EndTurn();                       // 第 3 回合:跳过
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 6));
        }

        [Test]
        public void Slow_SurvivesSnapshotRoundTrip_RhythmContinues() // 半速节奏读档后要接续,不能从头重来
        {
            var enemyDef = new EnemyDef("锈", Element.Metal, 500, 6);
            var engine = new BattleEngine(SlowGraph(), Config(), new[] { "冷" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.Cast("冷");
            engine.EndTurn(); // 第 1 回合:跳过(计量器攒到 50,不足 100;SpeedModifier 4→3 回合)

            var snapshot = engine.Capture();
            Assert.That(snapshot.Enemies[0].ActionMeter, Is.EqualTo(50));
            Assert.That(snapshot.Enemies[0].Statuses.Single(s => s.Kind == StatusKind.SpeedModifier).TurnsLeft,
                Is.EqualTo(3));

            var restored = BattleEngine.Restore(snapshot, SlowGraph(), Config(), null,
                new System.Collections.Generic.Dictionary<string, EnemyDef> { ["锈"] = enemyDef });

            Assert.That(restored.Enemies[0].ActionMeter, Is.EqualTo(50));
            Assert.That(restored.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(3));

            int hp0 = restored.PlayerHp;
            restored.EndTurn(); // 读档后第 2 回合:应接续为"行动"而不是从头跳过
            Assert.That(restored.PlayerHp, Is.EqualTo(hp0 - 6), "读档后应接续节奏,行动而非重新跳过");

            restored.EndTurn(); // 第 3 回合:跳过
            Assert.That(restored.PlayerHp, Is.EqualTo(hp0 - 6));
        }

        // ---- Freeze + Slow 组合(review finding,2026-08-03):冻结中减速节拍应原地暂停 ----

        private static RecipeGraph FreezeSlowGraph() => new(new[]
        {
            new CharDef("冻", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Freeze, 2) }),
            new CharDef("冷", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Slow, 4) }),
        });

        [Test]
        public void Slow_PausesDuringFreeze_ThenResumesFromSamePoint()
        {
            var engine = new BattleEngine(FreezeSlowGraph(), Config(), new[] { "冷", "冻" },
                Array.Empty<string>(), new[] { new EnemyDef("锈", Element.Metal, 500, 6) }, 42);

            engine.Cast("冷"); // 先减速,单敌免选自动锁定目标
            int hp0 = engine.PlayerHp;

            engine.EndTurn(); // 减速第 1 回合:跳过(计量器攒到 50,不足 100;SpeedModifier 4→3 回合)
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0));
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(50));
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(3));

            engine.Cast("冻"); // 再冻结 2 回合,打断减速节拍
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.Freeze).TurnsLeft, Is.EqualTo(2));

            engine.EndTurn(); // 冻结第 1 回合:不出手,减速节拍应原地暂停(不累积、不递减)
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "冻结中不出手");
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(50), "冻结中计量器不应累积");
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(3),
                "冻结中减速回合数不应被消耗");

            engine.EndTurn(); // 冻结第 2 回合:仍不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "冻结第 2 回合仍不出手");
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(50), "冻结中计量器仍不应累积");
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(3),
                "冻结中减速回合数仍不应被消耗");

            engine.EndTurn(); // 解冻:暂停点是"计量器已有 50"，应直接叠加到 100 出手，不是重新从零跳过
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 6), "解冻后应从暂停点接续,直接进入行动回合");
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(0));
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.SpeedModifier).TurnsLeft, Is.EqualTo(2));

            engine.EndTurn(); // 接续节奏的下一拍:跳过
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0 - 6), "接续节奏的下一拍应跳过");
        }

        // ---- 行动计量器(2026-08-04):Speed 每回合累积,每满 100 行动一次 ----

        /// <summary>挂了 −50 速度修正的敌人(等价于旧的半速 Slow)。</summary>
        private static BattleEngine SlowedEngine()
        {
            var engine = new BattleEngine(Graph(), Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { MetalBoss() }, 42);
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = 99, SourceId = "洼",
            });
            return engine;
        }

        /// <summary>指定基础速度的敌人(测加速与封顶)。</summary>
        private static BattleEngine HastedEngine(int speed)
        {
            var engine = new BattleEngine(Graph(), Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { MetalBoss() }, 42);
            engine.Enemies[0].Speed = speed;
            return engine;
        }

        [Test]
        public void Speed50_MatchesOldSlowRhythm_SkipActSkipAct()
        {
            // 减速敌人:攻 5,玩家 50 血。逐拍验证「跳、动、跳、动」
            var engine = SlowedEngine();           // 见上方辅助
            int hp0 = engine.PlayerHp;

            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "第 1 回合:计量器 50,不足 100,跳过");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.LessThan(hp0), "第 2 回合:累到 100,行动");
        }

        [Test]
        public void Speed200_ActsTwicePerTurn()
        {
            var engine = HastedEngine(speed: 200);
            int hp0 = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(hp0 - engine.PlayerHp, Is.EqualTo(10)); // 攻 5 × 2 次
        }

        [Test]
        public void Speed300_CappedAtTwoActions_NoCarryOver()
        {
            var engine = HastedEngine(speed: 300);
            int hp0 = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(hp0 - engine.PlayerHp, Is.EqualTo(10));            // 封顶 2 次
            Assert.That(engine.Enemies[0].ActionMeter, Is.EqualTo(0));     // 余额清零,不留到下回合
        }

        [Test]
        public void SpeedModifiers_StackAdditively()
        {
            var engine = SlowedEngine();
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -25, TurnsLeft = 3, SourceId = "凝",
            });
            // 基准 100 − 50 − 25 = 25 → 每 4 回合行动一次
            int hp0 = engine.PlayerHp;
            engine.EndTurn(); engine.EndTurn(); engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(hp0), "前 3 回合累计 75,不足 100");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.LessThan(hp0), "第 4 回合到 100");
        }

        [Test]
        public void ActionMeter_SurvivesRoundTrip_RhythmContinues()
        {
            var graph = Graph();
            var enemyDef = new EnemyDef("枯", Element.Wood, 100, 5);
            var engine = new BattleEngine(graph, Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = 99, SourceId = "洼",
            });
            engine.EndTurn(); // 计量器攒到 50,未行动

            var restored = BattleEngine.Restore(engine.Capture(), graph, Config(), null,
                new Dictionary<string, EnemyDef> { ["枯"] = enemyDef });

            Assert.That(restored.Enemies[0].ActionMeter, Is.EqualTo(50));
            int hp0 = restored.PlayerHp;
            restored.EndTurn();
            Assert.That(restored.PlayerHp, Is.LessThan(hp0), "续爬后下一回合就该行动,不从零重攒");
        }

        // ---- 回合掉字(2026-08-04):从出战牌组掉 1 字,满库停下决议 ----

        /// <summary>掉落专用引擎:库位留空,牌组只有「林」以便断言掉的是什么。</summary>
        private static BattleEngine DropEngine(int libraryCount, params string[] deck)
        {
            var library = new List<string>();
            for (int i = 0; i < libraryCount; i++) library.Add("灯");
            return new BattleEngine(Graph(),
                new BattleConfig { LibraryCapacity = 3, DropsPerTurn = 1, UnlockedChars = deck },
                library, Array.Empty<string>(), new[] { WoodMinion() }, 42);
        }

        private static EnemyDef Strong() => new("讹影", Element.Heart, 100, 60);

        [Test]
        public void Drop_LibraryNotFull_EntersLibraryDirectly()
        {
            var engine = DropEngine(libraryCount: 1, deck: "林");
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.Library, Does.Contain("林"));
            Assert.That(engine.PendingDrop, Is.Null);
        }

        [Test]
        public void Drop_LibraryFull_EntersDropChoice()
        {
            var engine = DropEngine(libraryCount: 3, deck: "林"); // 3/3 满
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice));
            Assert.That(engine.PendingDrop, Is.EqualTo("林"));
            Assert.That(engine.Library.Count, Is.EqualTo(3));
        }

        [Test]
        public void Drop_NoDeck_DoesNotDropNorSwitchPhase() // 工装与旧调用:UnlockedChars 为 null
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { LibraryCapacity = 3, DropsPerTurn = 1 },
                new[] { "灯" }, Array.Empty<string>(), new[] { WoodMinion() }, 42);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.PendingDrop, Is.Null);
            Assert.That(engine.Library.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResolveDrop_ReplacesChosenSlot()
        {
            var engine = DropEngine(libraryCount: 3, deck: "林");
            Assert.That(engine.ResolveDrop(0), Is.EqualTo(BattleError.None));

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.PendingDrop, Is.Null);
            Assert.That(engine.Library.Count, Is.EqualTo(3));
            Assert.That(engine.Library, Does.Contain("林"));
        }

        [Test]
        public void SkipDrop_KeepsLibraryUnchanged()
        {
            var engine = DropEngine(libraryCount: 3, deck: "林");
            Assert.That(engine.SkipDrop(), Is.EqualTo(BattleError.None));

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(engine.PendingDrop, Is.Null);
            Assert.That(engine.Library, Does.Not.Contain("林"));
        }

        [Test]
        public void ResolveDrop_OutOfRange_Rejected()
        {
            var engine = DropEngine(libraryCount: 3, deck: "林");
            Assert.That(engine.ResolveDrop(9), Is.EqualTo(BattleError.NotCastable));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice)); // 仍卡在决议
        }

        [Test]
        public void DropChoice_BlocksCastAndEndTurn() // 阶段机强制决议:操作入口自动拒绝
        {
            var engine = DropEngine(libraryCount: 3, deck: "林");
            Assert.That(engine.Cast("灯"), Is.EqualTo(BattleError.BattleOver));
            engine.EndTurn();
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice)); // EndTurn 被守卫挡住
        }

        // ---- 阶段回归:StartTurn 的三个调用点都可能切进 DropChoice ----

        [Test]
        public void EndTurn_FullLibrary_EntersDropChoice_NotMistakenForBattleOver()
        {
            // 库位 2/3 起手,掉 1 字后满库;打完这回合的 EndTurn 末尾再掉一次 → 应进决议而非被当成战斗结束
            var engine = DropEngine(libraryCount: 2, deck: "林");
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.PlayerTurn)); // 首回合还有位,直接入库
            Assert.That(engine.Library.Count, Is.EqualTo(3));

            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice));
            Assert.That(engine.PendingDrop, Is.EqualTo("林"));
            Assert.That(engine.Enemies[0].Alive, Is.True); // 敌人还活着,确实不是战斗结束
        }

        [Test]
        public void Revive_FullLibrary_EntersDropChoice() // Revive 也走 StartTurn
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { LibraryCapacity = 3, DropsPerTurn = 1, UnlockedChars = new[] { "林" } },
                new[] { "灯", "灯", "灯" }, Array.Empty<string>(), new[] { Strong() }, 42);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice)); // 开局就满库
            engine.SkipDrop();
            while (engine.Phase == BattlePhase.PlayerTurn) engine.EndTurn(); // 挨打到死
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));

            engine.Revive();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice));
            Assert.That(engine.PendingDrop, Is.EqualTo("林"));
        }

        // ---- 加攻改为可驱散的 AttackBuff(2026-08-04):标点小妖/焦痕的加攻可被驱散还原;
        // 缺笔妖的补全是形态变化,不可驱散 ----

        private static BattleEngine RegrowEngine() =>
            new(Graph(), Config(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("缺笔妖", Element.Metal, 30, 2, EnemyAbility.Regrow) }, seed: 42);

        [Test]
        public void AttackBuff_RemovableAndRestoresBaseAttack()
        {
            var engine = new BattleEngine(Graph(), Config(),
                new[] { "灯" }, Array.Empty<string>(), new[] { MetalBoss() }, 42);
            var enemy = engine.Enemies[0];
            int baseAttack = enemy.Attack;

            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 7, TurnsLeft = -1, SourceId = "点",
            });
            Assert.That(enemy.Attack, Is.EqualTo(baseAttack + 7));

            enemy.Statuses.RemoveAll(StatusPolarity.Buff);   // 驱散
            Assert.That(enemy.Attack, Is.EqualTo(baseAttack), "驱散后还原到基础攻击");
        }

        [Test]
        public void RegrowGrowth_IsNotDispellable() // 口径 5:缺笔妖补全是形态变化,不是增益
        {
            var engine = RegrowEngine();
            int before = engine.Enemies[0].Attack;
            engine.EndTurn();              // 触发一次补全成长
            int after = engine.Enemies[0].Attack;
            Assert.That(after, Is.GreaterThan(before));

            engine.Enemies[0].Statuses.RemoveAll(StatusPolarity.Buff);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(after), "成长不该被驱散抹掉");
        }

        [Test]
        public void RegrowFinalDouble_DoesNotAmplifyExternalBuff() // 全分支评审 Important 2(2026-08-05)锁定测试:
        // 缺笔妖(攻 3)+ 标点小妖(攻 2)同场,标点小妖每回合给缺笔妖叠 AttackBuff。
        // 第 3 回合缺笔妖补全完成触发 BaseAttack ×2 —— 只翻自己的基础值,不该连标点小妖给的
        // 增益一起翻倍。手算:T1 BaseAttack 3→5、Buff+2;T2 BaseAttack 5→7、Buff+2(共4);
        // T3 BaseAttack (7+2)×2=18、Buff+2(共6) → Attack = 18+6 = 24。
        {
            var engine = new BattleEngine(Graph(), Config(), Array.Empty<string>(), Array.Empty<string>(),
                new[]
                {
                    new EnemyDef("缺笔妖", Element.Metal, 30, 3, EnemyAbility.Regrow),
                    new EnemyDef("标点小妖", Element.Heart, 8, 2, EnemyAbility.Buff),
                }, seed: 42);

            engine.EndTurn();
            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(24),
                "补全 ×2 不应放大标点小妖给的外部增益");
        }

        [Test]
        public void AttackBuff_SurvivesRoundTrip_WithoutDoubling()
        {
            var graph = Graph();
            var enemyDef = new EnemyDef("枯", Element.Wood, 100, 5);
            var engine = new BattleEngine(graph, Config(), new[] { "灯" },
                Array.Empty<string>(), new[] { enemyDef }, 42);
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 7, TurnsLeft = -1, SourceId = "点#1",
            });
            int expected = engine.Enemies[0].Attack; // 5 + 7 = 12

            var restored = BattleEngine.Restore(engine.Capture(), graph, Config(), null,
                new Dictionary<string, EnemyDef> { ["枯"] = enemyDef });

            Assert.That(restored.Enemies[0].Attack, Is.EqualTo(expected)); // 不是 19
        }
    }
}
