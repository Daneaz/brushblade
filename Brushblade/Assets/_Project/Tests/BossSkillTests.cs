using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Boss 技能系统(蓄力预警制):spec 见
    /// docs/superpowers/specs/2026-07-28-boss-skills-design.md</summary>
    public class BossSkillTests
    {
        // 心属性 Boss:对木召唤物 KeMultiplier = 1.0,五行不干扰技能数值断言。
        // 两阶段各 100 血 → 总血 200、阈值 100(jitter=0),玩家打不动就不会换阶。
        private static EnemyDef SkillBoss(BossSkill skill) => new("试炼", Element.Heart, 100, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 100, 5, skill: skill),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        // 首阶段仅 15 血:总血 115、阈值 100(115−15),两发「火」即可推过 —— 专供换阶取消测试。
        // 次阶段技能为 None:换阶后下个敌方回合必是普攻,便于断言"大招没放出来"。
        private static EnemyDef ThinFirstPhaseBoss() => new("薄甲", Element.Heart, 15, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 15, 5, skill: BossSkill.Deluge),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }),
            new CharDef("林", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("盾", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
        });

        private static BattleEngine Engine(BossSkill skill) =>
            new(Graph(), new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(skill) }, seed: 1);

        /// <summary>推进 n 个敌方回合。</summary>
        private static void EndTurns(BattleEngine engine, int n)
        {
            for (int i = 0; i < n; i++) engine.EndTurn();
        }

        [Test]
        public void ChargeCycle_TwoNormalAttacks_ThenSilentChargeTurn()
        {
            var engine = Engine(BossSkill.Deluge);
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 1:普攻
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5));
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(1));

            engine.EndTurn(); // 敌方回合 2:普攻
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10));

            engine.EndTurn(); // 敌方回合 3:蓄力,不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "蓄力回合 Boss 不出手");
            Assert.That(engine.Enemies[0].IsCharging, Is.True);
        }

        [Test]
        public void Deluge_HitsPlayerAndEverySummon()
        {
            // 蓄力前才召唤:否则前两回合的普攻会先把最前一只磨死,淹没就打不到两只了
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 2); // 敌方两回合普攻(此时场上无召唤物,伤害落在玩家身上)
            engine.Cast("林");    // 2 只 6 血木召唤
            Assert.That(engine.Summons.Count, Is.EqualTo(2));
            int full = engine.PlayerHp;

            engine.EndTurn(); // 敌方回合 3:蓄力,不出手
            Assert.That(engine.PlayerHp, Is.EqualTo(full), "蓄力回合 Boss 不出手");

            engine.EndTurn(); // 敌方回合 4:释放淹没

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5), "大招不被召唤物拦截");
            foreach (var summon in engine.Summons)
                Assert.That(summon.Hp, Is.EqualTo(1)); // 6 血挨 5(心对木 ×1.0)
        }

        [Test]
        public void ChargeCounter_ResetsAfterCast()
        {
            var engine = Engine(BossSkill.Deluge);
            EndTurns(engine, 4); // 蓄力 + 释放

            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0));
        }

        [Test]
        public void PhaseDef_CarriesSkill_DefaultsToNone()
        {
            var withSkill = new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge);
            var without = new BossPhaseDef("干", Element.Wood, 12, 6);

            Assert.That(withSkill.Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(without.Skill, Is.EqualTo(BossSkill.None));
        }

        [Test]
        public void Scale_PreservesSkill()
        {
            var boss = new EnemyDef("试炼", Element.Water, 12, 6, phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6, skill: BossSkill.Topple),
                new BossPhaseDef("海", Element.Water, 16, 10, skill: BossSkill.Deluge),
            });

            var scaled = CampaignConfig.Scale(boss, 2f);

            Assert.That(scaled.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));
            Assert.That(scaled.Phases[1].Skill, Is.EqualTo(BossSkill.Deluge));
            Assert.That(scaled.Phases[1].MaxHp, Is.EqualTo(32)); // 数值照常缩放
        }

        [Test]
        public void BulwarkPhase_NeverCharges_AttacksEveryTurn()
        {
            var engine = Engine(BossSkill.Bulwark);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "坚壁阶段冻结计数");
            Assert.That(engine.Enemies[0].IsCharging, Is.False);
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20), "四回合各普攻一次");
        }

        [Test]
        public void NoSkillPhase_NeverCharges()
        {
            var engine = Engine(BossSkill.None);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0));
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20));
        }

        [Test]
        public void CrossingPhaseThreshold_CancelsCharge()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { ThinFirstPhaseBoss() }, seed: 1);
            var boss = engine.Enemies[0];
            Assert.That(boss.Hp, Is.EqualTo(115)); // 15 + 100

            engine.Cast("火", 0); // 火 vs 心 ×1.0 = 10 → 105,仍在首阶段(阈值 100)
            Assert.That(boss.PhaseIndex, Is.EqualTo(0));

            EndTurns(engine, 3); // 敌方三回合:普攻、普攻、蓄力
            Assert.That(boss.IsCharging, Is.True);

            engine.Cast("火", 0); // 105 → 95 ≤ 100 → 换阶

            Assert.That(boss.PhaseIndex, Is.EqualTo(1));
            Assert.That(boss.IsCharging, Is.False, "换阶取消蓄力");
            Assert.That(boss.ChargeCounter, Is.EqualTo(0));

            int full = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(full - 5), "大招没放出来,只有普攻");
        }

        [Test]
        public void Pierce_HitsFrontSummonAndPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            EndTurns(engine, 2); // 先走掉两回合普攻,免得把最前一只磨死
            engine.Cast("林");    // 2 只 6 血
            int full = engine.PlayerHp;

            EndTurns(engine, 2); // 蓄力 + 释放

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 10), "玩家挨双倍且不被拦截");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(1), "最前一只被穿:6 − 5");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(6), "只穿一条线,第二只不受伤");
        }

        [Test]
        public void Pierce_WithoutSummons_StillHitsPlayerDouble()
        {
            var engine = Engine(BossSkill.Pierce);
            int full = engine.PlayerHp;

            EndTurns(engine, 4);

            Assert.That(engine.PlayerHp, Is.EqualTo(full - 20)); // 普攻 5+5 + 贯穿 10
        }

        [Test]
        public void Topple_ClearsAllShieldAndCutsNextTurnAp()
        {
            var engine = Engine(BossSkill.Topple);
            engine.Cast("盾"); // 土系护盾 20
            Assert.That(engine.PlayerShield, Is.EqualTo(20));

            EndTurns(engine, 4); // 2 普攻(吃盾)+ 蓄力 + 倾覆

            Assert.That(engine.PlayerShield, Is.EqualTo(0), "剩余护盾被清空");
            Assert.That(engine.Ap, Is.EqualTo(2), "下回合 AP 由 3 降为 2");
        }

        [Test]
        public void ToppleApPenalty_LastsOneTurnOnly()
        {
            var engine = Engine(BossSkill.Topple);
            EndTurns(engine, 4);
            Assert.That(engine.Ap, Is.EqualTo(2));

            engine.EndTurn(); // 再过一个回合
            Assert.That(engine.Ap, Is.EqualTo(3), "惩罚只吃一回合");
        }

        [Test]
        public void ToppleApPenalty_NeverDropsBelowOne()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { BossPhaseJitterPercent = 0, ApPerTurn = 1 },
                new string[0], new[] { "火", "林", "盾", "火", "林", "盾" },
                new[] { SkillBoss(BossSkill.Topple) }, seed: 1);

            EndTurns(engine, 4);

            Assert.That(engine.Ap, Is.EqualTo(1), "AP 下限为 1,玩家至少能做一件事");
        }
    }
}
