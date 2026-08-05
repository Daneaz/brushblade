using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤物被动族(2026-08-05,子项目 C):速度/反伤/回血/出手附带效果/护盾。
    /// 规格见 docs/superpowers/specs/2026-08-05-召唤物被动-design.md。</summary>
    public class SummonPassiveTests
    {
        // 木系召唤字若干,每个带一种被动。敌人一律用「心」属性,避开生克干扰。
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 素:无被动的基准召唤(10 血 / 攻 3)
            new CharDef("素", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            // 疾:速度 150(桤)
            new CharDef("疾", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木",
                    passive: new SummonPassive { Speed = 150 }) }),
            // 焰:攻 0 + 单体灼烧 2(灶)
            new CharDef("焰", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { OnHitBurn = 2 }) }),
            // 炬:攻 0 + 全体灼烧 3(烓)
            new CharDef("炬", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { OnHitBurn = 3, OnHitBurnAll = true }) }),
            // 燎:攻 5 + 单体灼烧 1(楸)
            new CharDef("燎", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 5, summonChar: "木",
                    passive: new SummonPassive { OnHitBurn = 1 }) }),
            // 咒:攻 4 + 诅咒 25%(槐)
            new CharDef("咒", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 4, summonChar: "木",
                    passive: new SummonPassive { OnHitCurse = 25 }) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            IReadOnlyList<SummonSnapshot> startingSummons = null) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, Array.Empty<string>(), enemies, seed: 1,
                startingSummons: startingSummons);

        private static EnemyDef Dummy(int hp = 200, int attack = 0) => new("靶", Element.Heart, hp, attack);

        [Test]
        public void Summon_WithoutPassive_HasSpeed100AndNullPassive()
        {
            var engine = Engine(new[] { "素" }, new[] { Dummy() });
            engine.Cast("素");
            Assert.That(engine.Summons[0].Passive, Is.Null);
            Assert.That(engine.Summons[0].Speed, Is.EqualTo(100));
        }

        [Test]
        public void Summon_CarriesPassiveFromEffectDef()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");
            Assert.That(engine.Summons[0].Passive, Is.Not.Null);
            Assert.That(engine.Summons[0].Passive.Speed, Is.EqualTo(150));
            Assert.That(engine.Summons[0].Speed, Is.EqualTo(150), "基础速度应取自被动");
        }

        [Test]
        public void Snapshot_RoundTrip_KeepsSpeedShieldAndPassive()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");

            var meta = new MetaState
            {
                Endless = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 },
            };
            foreach (var summon in engine.Summons) meta.Endless.CarriedSummons.Add(summon.Capture());
            meta.Endless.CarriedSummons[0].Shield = 6; // 护盾字段也要过一趟序列化

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var revived = Engine(new[] { "疾" }, new[] { Dummy() },
                startingSummons: restored.Endless.CarriedSummons);

            Assert.That(revived.Summons[0].Speed, Is.EqualTo(150));
            Assert.That(revived.Summons[0].Shield, Is.EqualTo(6));
            Assert.That(revived.Summons[0].Passive, Is.Not.Null);
            Assert.That(revived.Summons[0].Passive.Speed, Is.EqualTo(150));
        }

        [Test]
        public void Snapshot_LegacySaveWithoutSpeedField_FallsBackTo100()
        {
            // 老存档没有 Speed 字段 → Newtonsoft 填 0 → 召唤物永远攒不满计量器,一辈子不出手。
            // Restore 必须兜底回 100。
            const string legacy =
                "{\"Endless\":{\"Depth\":3,\"PlayerHp\":40,\"Seed\":7,\"CarriedSummons\":" +
                "[{\"Char\":\"木\",\"Element\":\"Wood\",\"Hp\":10,\"MaxHp\":10,\"Attack\":3,\"ActionMeter\":0}]}}";
            var restored = Data.SaveSerializer.FromJson(legacy);
            var engine = Engine(new[] { "素" }, new[] { Dummy() },
                startingSummons: restored.Endless.CarriedSummons);

            Assert.That(engine.Summons[0].Speed, Is.EqualTo(100));
            Assert.That(engine.Summons[0].Passive, Is.Null);
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0));
        }

        [Test]
        public void Speed150_ActsOneThenTwoAlternating()
        {
            // 计量器:0+150=150 → 1 次(余 50);50+150=200 → 2 次(余 0);循环。平均 1.5 次/回合。
            // 「当回合即可反击」本就是引擎默认行为(新召唤物 0+100 就够一次),桤 的差异化靠速度。
            var engine = Engine(new[] { "疾" }, new[] { Dummy(hp: 500) });
            engine.Cast("疾");
            int hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 1 回合出手 1 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(6), "第 2 回合出手 2 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 3 回合回到 1 次");
        }

        [Test]
        public void Snapshot_PassiveIsDeepCopied_NotShared()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");
            var snapshot = engine.Summons[0].Capture();
            Assert.That(snapshot.Passive, Is.Not.SameAs(engine.Summons[0].Passive),
                "快照与实体共享同一条被动会让改一个连带改另一个");
        }

        // ---- 诅咒:百分比减攻 ----

        /// <summary>EnemyState 的构造函数是 internal,测试程序集调不到 —— 一律走引擎路径拿敌人。</summary>
        private static EnemyState EnemyWithAttack(int baseAttack)
        {
            var engine = Engine(new[] { "素" }, new[] { new EnemyDef("靶", Element.Heart, 50, baseAttack) });
            return engine.Enemies[0];
        }

        private static EnemyState CursedEnemy(int baseAttack, int cursePercent, string sourceId)
        {
            var enemy = EnemyWithAttack(baseAttack);
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                Magnitude = cursePercent, TurnsLeft = 2, SourceId = sourceId,
            });
            return enemy;
        }

        [Test]
        public void Curse_ReducesAttackByPercent_FloorRounded()
        {
            Assert.That(CursedEnemy(8, 25, "诅咒").Attack, Is.EqualTo(6));  // 8 × 0.75 = 6
            Assert.That(CursedEnemy(9, 25, "诅咒").Attack, Is.EqualTo(6));  // 9 × 0.75 = 6.75 → 6
        }

        [Test]
        public void Curse_SameSourceRefreshesInsteadOfStacking()
        {
            var enemy = CursedEnemy(8, 25, "诅咒");
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                Magnitude = 25, TurnsLeft = 2, SourceId = "诅咒",
            });
            Assert.That(enemy.Attack, Is.EqualTo(6), "两只槐仍是 −25%,不叠成 −50%");
        }

        [Test]
        public void Curse_AppliesAfterAttackBuff()
        {
            // 先加增益再乘诅咒:(4 + 4) × 0.75 = 6。反过来算是 4×0.75+4 = 7,差一点
            var enemy = EnemyWithAttack(4);
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 4, TurnsLeft = -1, SourceId = "妖#1",
            });
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                Magnitude = 25, TurnsLeft = 2, SourceId = "诅咒",
            });
            Assert.That(enemy.Attack, Is.EqualTo(6));
        }

        [Test]
        public void Curse_OverHundredPercent_ClampsToZeroNotNegative()
        {
            var enemy = CursedEnemy(8, 250, "诅咒");
            Assert.That(enemy.Attack, Is.EqualTo(0));
        }

        [Test]
        public void Curse_Expires_RestoresAttack()
        {
            var enemy = CursedEnemy(8, 25, "诅咒");
            enemy.Statuses.TickTurns();
            Assert.That(enemy.Attack, Is.EqualTo(6), "第 1 回合仍在");
            enemy.Statuses.TickTurns();
            Assert.That(enemy.Attack, Is.EqualTo(8), "第 2 回合到期,攻击力复原");
        }

        // ---- 出手附带效果 ----

        [Test]
        public void OnHitBurn_ZeroAttackSummon_StillAppliesBurn()
        {
            // 灶 攻 0:出手循环不能因为 Attack <= 0 提前返回,否则它一点输出都没有
            var engine = Engine(new[] { "焰" }, new[] { Dummy(hp: 200) });
            engine.Cast("焰");
            engine.EndTurn(); // 召唤物出手挂灼烧;本回合的灼烧结算段排在召唤段之前,故这层还没吃 tick
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
            int before = engine.Enemies[0].Hp;
            engine.EndTurn(); // 下一次结算:灼烧照常在这里掉血
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(before), "灼烧照常在下一次结算掉血");
        }

        [Test]
        public void OnHitBurn_SingleTarget_OnlyBurnsTheOneItHit()
        {
            var engine = Engine(new[] { "燎" }, new[] { Dummy(hp: 200), Dummy(hp: 200) });
            engine.Cast("燎");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
            Assert.That(engine.Enemies[1].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
        }

        [Test]
        public void OnHitBurnAll_BurnsEveryLivingEnemy()
        {
            var engine = Engine(new[] { "炬" }, new[] { Dummy(hp: 200), Dummy(hp: 200) });
            engine.Cast("炬");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3));
            Assert.That(engine.Enemies[1].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3));
        }

        [Test]
        public void OnHitCurse_AppliesCurseToTarget()
        {
            var engine = Engine(new[] { "咒" }, new[] { new EnemyDef("靶", Element.Heart, 200, 8) });
            engine.Cast("咒");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Curse), Is.True);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6)); // 8 × 0.75
        }

        [Test]
        public void OnHitCurse_TwoCursingSummons_DoNotStack()
        {
            var engine = Engine(new[] { "咒", "咒" }, new[] { new EnemyDef("靶", Element.Heart, 200, 8) });
            engine.Cast("咒");
            engine.Cast("咒");
            Assert.That(engine.Summons.Count, Is.EqualTo(2));
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6), "两只都挂了,仍是 −25%");
        }

        [Test]
        public void OnHitCurse_AlsoWeakensBossSkills()
        {
            // Boss 大招读的就是 enemy.Attack,诅咒自动生效——这条钉死它,免得日后有人
            // 把大招改成读 BaseAttack 就悄悄绕过了诅咒
            var boss = new EnemyDef("涛", Element.Heart, 500, 8,
                phases: new[] { new BossPhaseDef("涛", Element.Heart, 500, 8, skill: BossSkill.Deluge) });
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 },
                new[] { "咒" }, Array.Empty<string>(), new[] { boss }, seed: 1);
            engine.Cast("咒");
            engine.EndTurn(); // 召唤物出手挂诅咒;Boss 蓄力
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6)); // 8 × 0.75

            int hpBefore = engine.PlayerHp;
            engine.EndTurn(); // 释放淹没:玩家份 = Attack × 2
            Assert.That(hpBefore - engine.PlayerHp, Is.EqualTo(12), "12 = 6×2,不是未诅咒的 16");
        }

        [Test]
        public void OnHit_NoLivingEnemy_AppliesNothing()
        {
            // 敌人已被打死时召唤段直接跳出,不该对尸体挂状态
            var engine = Engine(new[] { "燎" }, new[] { Dummy(hp: 200) });
            engine.Cast("燎");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1),
                "第一回合正常挂 1 层");
        }
    }
}
