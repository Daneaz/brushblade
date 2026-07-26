using System;
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
            new CharDef("壁", Element.Earth, new[] { "辟", "土" },
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

        // ---- 回合开始(3.5 步骤 1) ----

        [Test]
        public void TurnStart_GrantsApAndDropsTwoComponents()
        {
            var engine = Engine();
            Assert.That(engine.Turn, Is.EqualTo(1));
            Assert.That(engine.Ap, Is.EqualTo(3));
            Assert.That(engine.Pool, Is.EquivalentTo(new[] { "木", "木" })); // 掉落表只有木;2/回合(2026-07-19)
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
        public void TurnStart_DropsStopAtPoolCapacity() // 池满则不掉;基准 10(2026-07-06 拍板)
        {
            var pool = Enumerable.Repeat("木", 9).ToArray();
            var engine = Engine(pool: pool);
            Assert.That(engine.Pool.Count, Is.EqualTo(10)); // 9 + 1,第二个不掉
        }

        [Test]
        public void SameSeed_SameDrops()
        {
            var config = Config("木", "火", "土", "辟");
            var a = Engine(config: config, seed: 7);
            var b = Engine(config: config, seed: 7);
            Assert.That(a.Pool, Is.EqualTo(b.Pool));
        }

        // ---- AP 经济(3.3) ----

        [Test]
        public void Dismantle_Costs1Ap_AndDelegatesToForge()
        {
            var engine = Engine(library: new[] { "焚" });
            var error = engine.Dismantle("焚");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Ap, Is.EqualTo(2));
            Assert.That(engine.Library, Is.EquivalentTo(new[] { "林" })); // 字回库(2026-07-22)
            Assert.That(engine.Pool, Does.Contain("火").And.Not.Contain("林")); // 部件回池
        }

        [Test]
        public void Compose_Costs1Ap()
        {
            var engine = Engine(); // 回合开始掉 木×2
            var error = engine.Compose("林");
            Assert.That(error, Is.EqualTo(BattleError.None));
            Assert.That(engine.Ap, Is.EqualTo(2));
            Assert.That(engine.Library, Does.Contain("林"));
        }

        [Test]
        public void HighTierCast_Costs2Ap()
        {
            var engine = Engine(library: new[] { "焚" });
            engine.Cast("焚");
            Assert.That(engine.Ap, Is.EqualTo(1));
        }

        // ---- AP 消耗 = 稀有度的函数(2026-07-26 拍板;配置不再逐字写 apCost)----

        [Test]
        public void ApCost_DerivedFromRarity()
        {
            Assert.That(CharDef.ApCostFor(CardRarity.White), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Green), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Blue), Is.EqualTo(1));
            Assert.That(CharDef.ApCostFor(CardRarity.Purple), Is.EqualTo(2));
            Assert.That(CharDef.ApCostFor(CardRarity.Orange), Is.EqualTo(2));
            Assert.That(CharDef.ApCostFor(CardRarity.Red), Is.EqualTo(3));
        }

        [Test]
        public void CharDef_ApCost_FollowsItsRarity() // 唯一来源:建出来就带对的 AP,无处可写错
        {
            Assert.That(new CharDef("甲", Element.Metal).ApCost, Is.EqualTo(1)); // 缺省白
            Assert.That(new CharDef("乙", Element.Metal, rarity: CardRarity.Blue).ApCost, Is.EqualTo(1));
            Assert.That(new CharDef("丙", Element.Metal, rarity: CardRarity.Purple).ApCost, Is.EqualTo(2));
            Assert.That(new CharDef("丁", Element.Metal, rarity: CardRarity.Red).ApCost, Is.EqualTo(3));
        }

        [Test]
        public void LoadGraph_ApCostFromRarity_NotFromConfig() // 配置里遗留的 apCost 一律不作数
        {
            var graph = Brushblade.Data.ConfigLoader.LoadGraph(@"{ ""chars"": [
                { ""id"": ""甲"", ""element"": ""Metal"", ""rarity"": ""Purple"", ""apCost"": 1 },
                { ""id"": ""乙"", ""element"": ""Metal"", ""rarity"": ""Blue"", ""apCost"": 2 },
                { ""id"": ""丙"", ""element"": ""Metal"" } ] }");
            Assert.That(graph.Get("甲").ApCost, Is.EqualTo(2)); // 紫 = 2,配置写 1 也没用
            Assert.That(graph.Get("乙").ApCost, Is.EqualTo(1)); // 蓝 = 1
            Assert.That(graph.Get("丙").ApCost, Is.EqualTo(1)); // 无稀有度 = 白
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
            var engine = Engine(library: new[] { "焚", "灯" });
            engine.Dismantle("灯");            // AP 3→2,池得 火+丁
            engine.Compose("林");              // 2→1(回合开始已掉 木木)
            Assert.That(engine.Cast("焚"), Is.EqualTo(BattleError.NotEnoughAp)); // 需 2 AP,只剩 1
            Assert.That(engine.Cast("火", 0), Is.EqualTo(BattleError.None));     // 部件直出 1 AP
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
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(1)); // 附带灼烧层数为平值
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
        public void Cast_Shield_UsesShengMultiplier() // 壁:护盾 8×3(土生金)= 24
        {
            var engine = Engine(library: new[] { "壁" });
            engine.Cast("壁");
            Assert.That(engine.PlayerShield, Is.EqualTo(24));
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
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 6)); // 3×2
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(2));
        }

        [Test]
        public void EndTurn_EnemyAttacks_ShieldAbsorbsFirst_ShieldPersists() // 护盾段内持久,不再回合末全清
        {
            var engine = Engine(library: new[] { "壁" }, enemies: new[] { MetalBoss() }); // 攻 5
            engine.Cast("壁"); // 护盾 24
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
            var engine = Engine(library: new[] { "壁" }, pool: new[] { "土" }, enemies: new[] { MetalBoss() });
            engine.Cast("壁");            // 24(土生金 ×3)
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
            engine.EndTurn();    // 3×3 = 9
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(200 - 9));
            Assert.That(engine.Enemies[0].Burn, Is.EqualTo(2));
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
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "乙" });
            for (int i = 0; i < 4; i++) engine.Cast("甲");
            int apBefore = engine.Ap;

            Assert.That(engine.Cast("乙"), Is.EqualTo(BattleError.SummonCapFull));
            Assert.That(engine.Ap, Is.EqualTo(apBefore));   // 强阻断:AP 不吃
            Assert.That(engine.Library, Contains.Item("乙")); // 字不消耗
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4));
        }

        [Test]
        public void Cast_SummonAtCap_ReplaceMode_ReplacesFirstSlot()
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "乙" });
            for (int i = 0; i < 4; i++) engine.Cast("甲");

            Assert.That(engine.Cast("乙", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4)); // 不超编
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
                    new EffectDef(EffectKind.Summon, 5, summonCount: 3, summonAttack: 0, summonChar: "D"),
                    new EffectDef(EffectKind.Summon, 5, summonCount: 3, summonAttack: 0, summonChar: "D"),
                }),
                new CharDef("戊", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            Assert.That(engine.SummonCountOf(graph.Get("甲")), Is.EqualTo(1));
            Assert.That(engine.SummonCountOf(graph.Get("丙")), Is.EqualTo(2));
            Assert.That(engine.SummonCountOf(graph.Get("丁")), Is.EqualTo(4)); // 3+3 封顶到上限 4
            Assert.That(engine.SummonCountOf(graph.Get("戊")), Is.EqualTo(0)); // 不召唤
        }

        [Test]
        public void Cast_SummonAtCap_ReplaceMode_MultiSummon_AdvancesFromFirst() // 一次召 2:顶掉最前两只,不重复顶自己
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 4; i++) engine.Cast("甲");

            engine.Cast("丙", replaceSummon: true);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4));
            Assert.That(engine.Summons[0].Char, Is.EqualTo("C"));
            Assert.That(engine.Summons[1].Char, Is.EqualTo("C"));
            Assert.That(engine.Summons[2].Char, Is.EqualTo("A"));
        }

        [Test]
        public void Cast_SummonOverflowsCap_IsBlocked() // 未满但放不下也拦:3/4 时召 2 只会溢出 1
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 3; i++) engine.Cast("甲");

            Assert.That(engine.Cast("丙"), Is.EqualTo(BattleError.SummonCapFull));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(3)); // 一只都没进
        }

        [Test]
        public void Cast_SummonExactlyFits_NotBlocked() // 刚好填满不拦
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "丙" });
            for (int i = 0; i < 2; i++) engine.Cast("甲");

            Assert.That(engine.Cast("丙"), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4));
            Assert.That(engine.Summons[0].Char, Is.EqualTo("A")); // 有空位就不该顶谁
        }

        [Test]
        public void Cast_SummonOverflow_ReplaceMode_FillsGapThenReplacesFromFirst() // 3/4 召 2:先占空位,溢出的才顶最前
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "丙" });
            for (int i = 0; i < 3; i++) engine.Cast("甲");

            engine.Cast("丙", replaceSummon: true);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(4));
            Assert.That(engine.Summons[3].Char, Is.EqualTo("C")); // 第 1 只填空位
            Assert.That(engine.Summons[0].Char, Is.EqualTo("C")); // 第 2 只顶掉最前
            Assert.That(engine.Summons[1].Char, Is.EqualTo("A")); // 其余不动
            Assert.That(engine.Summons[2].Char, Is.EqualTo("A"));
        }

        [Test]
        public void SummonReplaceCountOf_CountsOnlyTheOverflow() // 弹窗文案「顶掉最前 N 只」的 N
        {
            var engine = ReplaceEngine(new[] { "甲", "甲", "甲", "丙" });
            var bing = new RecipeGraph(new[]
            {
                new CharDef("丙", Element.Wood, effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 2, summonAttack: 0, summonChar: "C") }),
                new CharDef("戊", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(0)); // 0/4:空位够
            engine.Cast("甲");
            engine.Cast("甲");
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(0)); // 2/4:刚好填满
            engine.Cast("甲");
            Assert.That(engine.SummonReplaceCountOf(bing.Get("丙")), Is.EqualTo(1)); // 3/4:溢出 1
            Assert.That(engine.SummonReplaceCountOf(bing.Get("戊")), Is.EqualTo(0)); // 不召唤的字永远 0
        }

        [Test]
        public void Cast_NonSummonChar_NeverBlockedByCap() // 满员只拦召唤字,伤害/护盾字照出
        {
            var engine = IdentityEngine(new[] { "林", "林", "沐" },
                new[] { new EnemyDef("怔", Element.Heart, 100, 0) }, startingHp: 30);
            engine.Cast("林");
            engine.Cast("林"); // 满 4
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
    }
}
