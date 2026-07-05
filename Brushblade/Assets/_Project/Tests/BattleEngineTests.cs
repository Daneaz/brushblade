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
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, apCost: 2,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 18), new EffectDef(EffectKind.BurnAll, 1) }),
            new CharDef("壁", Element.Earth, new[] { "辟", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 8) }),
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

        // ---- 回合开始(3.5 步骤 1) ----

        [Test]
        public void TurnStart_GrantsApAndDropsTwoComponents()
        {
            var engine = Engine();
            Assert.That(engine.Turn, Is.EqualTo(1));
            Assert.That(engine.Ap, Is.EqualTo(3));
            Assert.That(engine.Pool, Is.EquivalentTo(new[] { "木", "木" })); // 掉落表只有木
        }

        [Test]
        public void TurnStart_DropsStopAtPoolCapacity() // 池满则不掉
        {
            var pool = Enumerable.Repeat("木", 11).ToArray();
            var engine = Engine(pool: pool);
            Assert.That(engine.Pool.Count, Is.EqualTo(12)); // 11 + 1,第二个不掉
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
            Assert.That(engine.Library, Is.Empty);
            Assert.That(engine.Pool, Does.Contain("林").And.Contain("火"));
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
        public void Cast_UsedCharLeavesLibrary_NotReusable() // 3.8.1
        {
            var engine = Engine(library: new[] { "焚" }, enemies: new[] { MetalBoss(500) });
            engine.Cast("焚");
            engine.EndTurn(); // 回到玩家回合,AP 重置
            Assert.That(engine.UsedChars, Does.Contain("焚"));
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
        public void Cast_SingleTarget_RequiresValidTarget()
        {
            var engine = Engine(library: new[] { "灯" });
            Assert.That(engine.Cast("灯", 5), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.Cast("灯"), Is.EqualTo(BattleError.InvalidTarget)); // 未选目标
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
        public void EndTurn_EnemyAttacks_ShieldAbsorbsFirst_ThenClears() // 10.2:敌方行动后全清
        {
            var engine = Engine(library: new[] { "壁" }, enemies: new[] { MetalBoss() }); // 攻 5
            engine.Cast("壁"); // 护盾 24
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50)); // 24 盾吸收攻 5,不掉血
            Assert.That(engine.PlayerShield, Is.EqualTo(0)); // 剩余 19 在敌方行动后全清
            Assert.That(engine.Turn, Is.EqualTo(2));
            Assert.That(engine.Ap, Is.EqualTo(3)); // AP 不跨回合保留,重置为 3
        }

        [Test]
        public void Shield_StacksWithinTurn() // 同回合多次筑盾累加
        {
            var engine = Engine(library: new[] { "壁" }, pool: new[] { "土" }, enemies: new[] { MetalBoss() });
            engine.Cast("壁");            // 24(土生金 ×3)
            engine.Cast("土");            // 部件直出 +3(无配方,无相生)
            Assert.That(engine.PlayerShield, Is.EqualTo(27));
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

        [Test]
        public void DeadEnemy_DoesNotAttack_AndIsInvalidTarget()
        {
            var engine = Engine(library: new[] { "焚", "灯" },
                enemies: new[] { WoodMinion(), MetalBoss(200) });
            engine.Cast("焚"); // 木怪(12)死于 54,金怪 200-81=119
            Assert.That(engine.Cast("灯", 0), Is.EqualTo(BattleError.InvalidTarget)); // 尸体不可选
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(45)); // 只有金怪(攻5)打了一下
        }
    }
}
