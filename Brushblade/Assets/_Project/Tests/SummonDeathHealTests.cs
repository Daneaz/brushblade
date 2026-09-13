using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>木脉 L2「归根」(spec 2026-09-13 §2.4):召唤物阵亡时玩家回复
    /// 该召唤物最大生命的 N%。
    ///
    /// 它是一次**完整治疗**(用户 2026-09-13 裁定):吃泉放大、攒泉,也吃水脉 L2
    /// 的溢流转伤害。所以这里既要断回血量,也要断这三条咬合。
    ///
    /// 召唤物死亡在引擎里有两条路径(自焚 / 挨打),两条都要断 —— 它们此前各自
    /// 调 RefreshSummonAura,是这一层最典型的「同一份逻辑两条路径」。</summary>
    public class SummonDeathHealTests
    {
        private const int SummonHp = 200;

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, SummonHp, summonAttack: 0, summonChar: "木") }),
        });

        private static BattleEngine Engine(int healPercent, int overhealPercent = 0,
            int enemyAttack = 0, int startingHp = 500) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    SummonDeathHealPercent = healPercent,
                    OverhealDamagePercent = overhealPercent,
                },
                new[] { "兵" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 9000, enemyAttack) },
                seed: 1, startingHp: startingHp);

        [Test]
        public void SummonDeath_HealsThePlayer()
        {
            // 汇流点本身:走测试钩子直达 OnSummonDeath,不经 SettleSummonBurn / DamageSummon
            // 任何一条真实死亡路径——那两条各自有专门的测试(见下方 SelfBurn_/KilledByEnemyDamage_)。
            // 玩家缺 100 血,召唤物 200 上限 → 回 40
            var engine = Engine(20, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(440), "400 + 200×20% = 440");
        }

        [Test]
        public void SelfBurn_SummonDeath_HealsThePlayer()
        {
            // 走真实的自焚路径(SettleSummonBurn → OnSummonDeath),不借 KillSummonForTest。
            // 用 AdvanceOnce() 精确停在召唤物那一拍,不用 EndTurn()——它一次跑完所有非玩家
            // 行动者,这里虽只有一只召唤物,仍照 CLAUDE.md 的既有教训统一走 AdvanceOnce。
            var engine = Engine(20, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            // 15 层 × 20/层 = 300 > 200 血上限,一拍烧死
            engine.Summons[slot].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 15, TurnsLeft = -1,
            });

            engine.YieldTurn();
            while (engine.AdvanceOnce() && engine.LastActor.Kind != ActorKind.Summon) { }

            Assert.That(engine.Summons[slot].Alive, Is.False, "前提:被自己身上的火烧死");
            Assert.That(engine.PlayerHp, Is.EqualTo(440), "400 + 200×20% = 440,走的是自焚路径");
        }

        [Test]
        public void KilledByEnemyDamage_HealsThePlayer()
        {
            // 走真实的挨打路径(DamageSummon → OnSummonDeath),不借 KillSummonForTest。
            // enemyAttack=250 一击必杀 200 血的召唤物(靶心元素与木无生克,原样吃满)。
            var engine = Engine(20, enemyAttack: 250, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));

            engine.EndTurn();   // 场上只有一只召唤物、一只敌人,没有低血量夹具连锁的风险

            Assert.That(engine.Summons[slot].Alive, Is.False, "前提:被敌人一击打死");
            Assert.That(engine.PlayerHp, Is.EqualTo(440), "400 + 200×20% = 440,走的是挨打路径");
        }

        [Test]
        public void PerkOff_HealsNothing()
        {
            var engine = Engine(0, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(400), "未点亮,阵亡就只是阵亡");
        }

        [Test]
        public void TheHealGainsWellspring()
        {
            var engine = Engine(20, startingHp: 400);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            Assert.That(engine.WellspringStacks, Is.EqualTo(0), "前提:起手 0 层");
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            // 攒泉阈值 100:一次 40 攒不满一层,但余数要记进去。连死四只才涨一层,
            // 这里只断"确实走了攒泉通道"——余数不外露,故用连续多只验证。
            Assert.That(engine.HealAccum, Is.EqualTo(40),
                "走的是统一治疗入口,余数进 _healAccum");
        }

        [Test]
        public void AmplifiedByWellspring_WhenStacksArePresent()
        {
            // TheHealGainsWellspring 只断了攒泉侧(HealAccum),没有一条在泉**已有**层数时
            // 断放大真的生效 —— 把 HealFromSummonDeath 里的 amplified 换成 healBase 全绿。
            var engine = Engine(20, startingHp: 400);
            engine.GainWellspringForTest(400);   // 阈值 = PlayerMaxHp/5 = 100 → 攒满 4 层
            Assert.That(engine.WellspringStacks, Is.EqualTo(4), "前提:攒够 4 层");
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            // healBase = 200×20% = 40;泉 4 层 × 5%/层 = +20% → 40×120% = 48
            Assert.That(engine.PlayerHp, Is.EqualTo(448), "40 的名义值要经泉放大成 48,不是原样回 40");
        }

        [Test]
        public void FullHpPlayer_OverflowBecomesDamageWhenWaterTierTwoIsOn()
        {
            // 玩家满血 → 归根的 40 全部溢出 → 水脉溢流 50% → 20 伤害
            var engine = Engine(20, overhealPercent: 50, startingHp: 500);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            int before = engine.Enemies[0].Hp;
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(500), "满血,一点没回");
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(20),
                "40 溢出 × 50% = 20,两条 L2 咬合");
        }

        [Test]
        public void FullHpPlayer_NoDamageWhenWaterTierTwoIsOff()
        {
            var engine = Engine(20, overhealPercent: 0, startingHp: 500);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            int before = engine.Enemies[0].Hp;
            engine.KillSummonForTest(slot);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "没点水脉就只是浪费掉");
        }

        // 吞噬(BossSkill.Devour)是召唤物死亡的第三条路径(2026-09-13 补接前,曾绕开
        // OnSummonDeath 只刷光环):Boss 无视血量直接把最前一只砍到 0。这里照
        // BossSkillTests 的写法搭一只单阶段 Devour Boss,断这条路径也接了归根。
        private static EnemyDef DevourBoss() => new("噬", Element.Heart, 200, 5,
            phases: new[] { new BossPhaseDef("甲", Element.Heart, 200, 5, skill: BossSkill.Devour) });

        private static BattleEngine DevourEngine(int healPercent, int startingHp = 400) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    SummonDeathHealPercent = healPercent, BossPhaseJitterPercent = 0,
                },
                new[] { "兵" }, Array.Empty<string>(),
                new[] { DevourBoss() },
                seed: 1, startingHp: startingHp);

        [Test]
        public void DevouredSummon_AlsoHealsThePlayer()
        {
            var engine = DevourEngine(20, startingHp: 400);
            engine.EndTurn();   // 先走掉普攻回合(场上尚无召唤物,吃满攻击),免得召唤物先挨打
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            int beforeDevour = engine.PlayerHp;

            engine.EndTurn();   // 蓄力回合,不出手
            engine.EndTurn();   // 释放吞噬:无视血量必杀最前一只

            Assert.That(engine.Summons[slot].Alive, Is.False, "召唤物被吞噬阵亡");
            Assert.That(engine.PlayerHp, Is.EqualTo(beforeDevour + 40),
                "200×20% = 40,吞噬绕开 DamageSummon 但一样要算阵亡");
        }

        [Test]
        public void DevouredSummon_HealsNothingWhenPerkOff()
        {
            var engine = DevourEngine(0, startingHp: 400);
            engine.EndTurn();
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            int beforeDevour = engine.PlayerHp;

            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.Summons[slot].Alive, Is.False, "召唤物被吞噬阵亡");
            Assert.That(engine.PlayerHp, Is.EqualTo(beforeDevour), "未点亮,吞噬也不回血");
        }

        [Test]
        public void ReviveThenDieAgain_HealsAgain()
        {
            var engine = Engine(20, startingHp: 300);
            Assert.That(engine.Cast("兵"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(engine.Summons.ToArray(), s => s != null);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(340));
            engine.ReviveSummonForTest(slot);
            engine.KillSummonForTest(slot);
            Assert.That(engine.PlayerHp, Is.EqualTo(380), "每一次真实死亡都算一次,不去重");
        }
    }
}
