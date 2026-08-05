using System;
using System.Collections.Generic;
using System.Linq;
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
            // 盾:召 2 只 + 出字给全场召唤物各 6 盾(桂)
            new CharDef("盾", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 2, summonAttack: 0, summonChar: "木",
                    summonShield: 6) }),
            // 荫:攻 0 + 每回合己方回血 3(桃)
            new CharDef("荫", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { HealAlly = 3 }) }),
            // 棘:攻 0 + 反伤 3(荆)
            new CharDef("棘", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Thorns = 3 }) }),
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
            // 敌人在召唤物出手前已经死亡(被同回合更早出手的召唤物打死):后出手的召唤物
            // 找不到存活目标,直接跳出,不该对尸体挂状态、也不该抛异常
            var engine = Engine(new[] { "燎", "焰" }, new[] { Dummy(hp: 5) });
            engine.Cast("燎"); // 攻 5,先手打死唯一的敌人
            engine.Cast("焰"); // 攻 0 + 单体灼烧 2,轮到它出手时已经无人可打
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "后手召唤物没有敌人可打,不该对尸体挂灼烧");
        }

        // ---- 光环治疗 ----

        [Test]
        public void HealAlly_HealsPlayerAndSummonsEachTurn()
        {
            var engine = Engine(new[] { "荫", "素" }, new[] { new EnemyDef("靶", Element.Heart, 200, 4) });
            engine.Cast("荫");
            engine.Cast("素");
            engine.EndTurn(); // 荫(下标 0,先召先顶前排)被打 4,同时给双方回 3
            int summonHp = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(Math.Min(10, summonHp + 3 - 4)));
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "玩家满血时不溢出");
        }

        [Test]
        public void HealAlly_HealsEvenWithNoLivingEnemy()
        {
            // 回血与出手无关:场上没有可打的目标时也照常回
            var engine = Engine(new[] { "荫" }, new[] { new EnemyDef("靶", Element.Heart, 200, 20) });
            engine.EndTurn();                       // 玩家挨 20 → 30
            Assert.That(engine.PlayerHp, Is.EqualTo(30));
            engine.Cast("荫");
            engine.EndTurn();                       // 回 3、再挨 20(召唤物顶),净 +3
            Assert.That(engine.PlayerHp, Is.EqualTo(33));
        }

        // ---- 反伤 ----

        [Test]
        public void Thorns_ReflectsFlatDamage_IgnoringWuxing()
        {
            // 靶是「心」属性,反伤本就不走生克;这里断言反弹的是平值 3 而不是任何倍数
            var engine = Engine(new[] { "棘" }, new[] { new EnemyDef("靶", Element.Heart, 200, 4) });
            engine.Cast("棘");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(197));
        }

        [Test]
        public void Thorns_KillingLastEnemy_WinsTheBattle()
        {
            // 敌方回合里反伤打死最后一只敌人 —— 敌方段以前从不杀敌,所以原先这里没有判胜
            var engine = Engine(new[] { "棘" }, new[] { new EnemyDef("靶", Element.Heart, 2, 4) });
            engine.Cast("棘");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Thorns_StillReflectsOnTheBlowThatKillsTheSummon()
        {
            // 荆棘扎人不看自己死没死:1 血召唤物挨致命一击,照样反弹
            var engine = Engine(new[] { "棘" }, new[] { new EnemyDef("靶", Element.Heart, 200, 40) });
            engine.Cast("棘");
            engine.EndTurn();
            Assert.That(engine.Summons[0].Alive, Is.False, "30 血挨 40 必死");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(197), "死了也扎");
        }

        [Test]
        public void Thorns_NotTriggeredByDevour()
        {
            // 吞噬直接置 0 血、不经 DamageSummon —— 「无视血量必杀」的既有语义,不该被反伤蹭到
            var boss = new EnemyDef("噬", Element.Heart, 200, 4,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 200, 4, skill: BossSkill.Devour) });
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50, BossChargeEvery = 1 },
                new[] { "棘" }, Array.Empty<string>(), new[] { boss }, seed: 1);
            engine.Cast("棘");
            engine.EndTurn(); // 蓄力
            int hpBeforeDevour = engine.Enemies[0].Hp;
            engine.EndTurn(); // 释放吞噬
            Assert.That(engine.Summons[0].Alive, Is.False);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBeforeDevour), "吞噬不触发反伤");
        }

        // ---- 召唤物护盾 ----

        [Test]
        public void SummonShield_AbsorbsBeforeHp()
        {
            // 敌人攻 4,召唤物 10 血 6 盾:第一击全被盾吃掉,血不掉
            var engine = Engine(new[] { "盾" }, new[] { new EnemyDef("靶", Element.Heart, 200, 4) });
            engine.Cast("盾");
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(6));
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(10), "血量不动");
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(2), "盾从 6 扣到 2");
        }

        [Test]
        public void SummonShield_OnceDepleted_DoesNotRefresh()
        {
            var engine = Engine(new[] { "盾" }, new[] { new EnemyDef("靶", Element.Heart, 200, 4) });
            engine.Cast("盾");
            engine.EndTurn(); // 盾 6 → 2
            engine.EndTurn(); // 盾 2 → 0,溢出的 2 点进血
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0));
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(8));
            engine.EndTurn(); // 不刷新,整 4 点进血
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0), "护盾不随回合补满");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(4));
        }

        [Test]
        public void SummonShield_CoversSummonsAlreadyOnField()
        {
            // 先召一只无盾的素,再出盾:场上两批都该拿到 6 点
            var engine = Engine(new[] { "素", "盾" }, new[] { Dummy() });
            engine.Cast("素");
            engine.Cast("盾");
            Assert.That(engine.Summons.Count, Is.EqualTo(3));
            foreach (var summon in engine.Summons)
                Assert.That(summon.Shield, Is.EqualTo(6), "先在场的那只也要吃到");
        }

        [Test]
        public void SummonHit_ReportsAbsorbedAmount()
        {
            var engine = Engine(new[] { "盾" }, new[] { new EnemyDef("靶", Element.Heart, 200, 4) });
            engine.Cast("盾");
            engine.EndTurn();
            var hit = engine.LastEvents.First(e => e.Kind == BattleEventKind.SummonHit);
            Assert.That(hit.Amount, Is.EqualTo(4), "Amount 仍报吃到的总伤害");
            Assert.That(hit.Absorbed, Is.EqualTo(4), "全被盾吸走");
        }
    }
}
