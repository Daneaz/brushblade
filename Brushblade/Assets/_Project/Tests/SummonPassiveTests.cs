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
                EndlessV2 = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 },
            };
            for (int s = 0; s < engine.Summons.Count; s++)
                if (engine.Summons[s] != null) meta.EndlessV2.CarriedSummons.Add(engine.Summons[s].Capture(s));
            meta.EndlessV2.CarriedSummons[0].Shield = 6; // 护盾字段也要过一趟序列化

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var revived = Engine(new[] { "疾" }, new[] { Dummy() },
                startingSummons: restored.EndlessV2.CarriedSummons);

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
                "{\"EndlessV2\":{\"Depth\":3,\"PlayerHp\":40,\"Seed\":7,\"CarriedSummons\":" +
                "[{\"Char\":\"木\",\"Element\":\"Wood\",\"Hp\":10,\"MaxHp\":10,\"Attack\":3,\"ActionMeter\":0}]}}";
            var restored = Data.SaveSerializer.FromJson(legacy);
            var engine = Engine(new[] { "素" }, new[] { Dummy() },
                startingSummons: restored.EndlessV2.CarriedSummons);

            Assert.That(engine.Summons[0].Speed, Is.EqualTo(100));
            Assert.That(engine.Summons[0].Passive, Is.Null);
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0));
        }

        [Test]
        public void Speed150_ActsOneThenTwoAlternating()
        {
            // 2026-08-17 召唤物上场即满格:出生那一格是现成的 100,不是靠攒来的,所以召出后的
            // 第 1 回合它拿的是这 100(动 1 次、余 0),攒速要到第 2 回合才开始表达。此后
            // 计量器:0+150=150 → 1 次(余 50);50+150=200 → 2 次(余 0);两回合一循环,
            // 平均 1.5 次/回合 —— 这才是速度 150 的差异化。「当回合即可反击」照旧成立(第 1 回合)。
            var engine = Engine(new[] { "疾" }, new[] { Dummy(hp: 500) });
            engine.Cast("疾");
            int hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 1 回合:吃掉出生那一格,1 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 2 回合:0+150,1 次(余 50)");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(6), "第 3 回合:50+150 = 200,2 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 4 回合回到 1 次 —— 两回合一循环");
        }

        [Test]
        public void Snapshot_PassiveIsDeepCopied_NotShared()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");
            var snapshot = engine.Summons[0].Capture(0);
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
        public void Curse_IntegerMath_AvoidsFloatPrecisionLoss()
        {
            // ⚠ 2026-08-12 订正:这条测试原本用 10%/30%,是**纸老虎** —— 把实现改回
            // float 版 (1 - curse/100f) 它照样绿。2026-08-06 M1 那条注释举的例子
            // 「1 - 0.1f = 0.89999997,10 × 它会 floor 到 8」在 .NET 8 上不成立:
            // 1 - 0.1f 落在 0.9f 上,10 × 0.9f 的乘法又舍回恰好 9.0f,整数/float/double
            // 三者同为 9。30% 同理三者同为 7。
            //
            // 穷举出的真分歧点是**大 curse**:80% 时整数算式给 2,float 与 double 都给 1。
            // 换成它,把 float 与 double 两种误写一并罩住。
            Assert.That(CursedEnemy(10, 80, "诅咒").Attack, Is.EqualTo(2),
                "整数 10×20/100 = 2;float/double 的 10×(1−0.8) 会 floor 到 1");
            Assert.That(CursedEnemy(20, 80, "诅咒").Attack, Is.EqualTo(4),
                "整数 20×20/100 = 4;浮点同样 floor 到 3");
            // 保留两条不分歧的档位当回归基线:它们不证明整数算式必要,但证明它没算错
            Assert.That(CursedEnemy(10, 10, "诅咒").Attack, Is.EqualTo(9));  // 10 × 0.9 = 9
            Assert.That(CursedEnemy(10, 30, "诅咒").Attack, Is.EqualTo(7));  // 10 × 0.7 = 7
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
        public void Curse_ShareOneAxisWithAttackBuff() // 原名 Curse_AppliesAfterAttackBuff
        {
            // 2026-08-12(E-b4 T0.5)**刻意的语义变化**:AttackBuff 从加数改成百分点后,
            // 与 Curse 落在同一根轴上直接加减,「先加再乘」这个顺序问题本身消失了(加法可交换)。
            // 旧口径:(4 + 4) × 0.75 = 6;新口径:4 × (100 + 50 − 25) ÷ 100 = 5。
            var enemy = EnemyWithAttack(4);
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 50, TurnsLeft = -1, SourceId = "妖#1",
            });
            enemy.Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                Magnitude = 25, TurnsLeft = 2, SourceId = "诅咒",
            });
            Assert.That(enemy.Attack, Is.EqualTo(5), "4 × (100 + 50 − 25) ÷ 100 = 5");
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

        // 2026-08-16 CTB 改造:原为「施放当回合灼烧层数为 2」,现为 1——召唤物在 CTB 调度里的
        // 优先级(1)比敌人(3)高,同速时几乎总是先出手;这只敌人紧接着在同一次 EndTurn() 内
        // 轮到自己,把召唤物刚挂上的灼烧当场结算掉 1 层(SettleBurnOn 每次只 -1)。旧模型下
        // 全场敌人的灼烧结算集中在 YieldTurn 一开始跑完,召唤物这回合刚挂的新灼烧要等下一次
        // EndTurn 才会被吃 tick;新模型下每个敌人只在轮到自己时结算,顺序因此提前(见
        // task-8-red-list.md,已经 controller 复核确认)。
        [Test]
        public void OnHitBurn_ZeroAttackSummon_StillAppliesBurn()
        {
            // 灶 攻 0:出手循环不能因为 Attack <= 0 提前返回,否则它一点输出都没有
            var engine = Engine(new[] { "焰" }, new[] { Dummy(hp: 200) });
            engine.Cast("焰");
            engine.EndTurn(); // 召唤物出手挂灼烧;这只敌人紧接着在同一次 EndTurn 内轮到自己,当场结算掉 1 层
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
            int before = engine.Enemies[0].Hp;
            engine.EndTurn(); // 下一次结算:灼烧照常在这里掉血
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(before), "灼烧照常在下一次结算掉血");
        }

        // 2026-08-16 CTB 改造:同上一条因果链——单体灼烧只有 1 层,当场就被结算到 0。
        [Test]
        public void OnHitBurn_SingleTarget_OnlyBurnsTheOneItHit()
        {
            var engine = Engine(new[] { "燎" }, new[] { Dummy(hp: 200), Dummy(hp: 200) });
            engine.Cast("燎");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
            Assert.That(engine.Enemies[1].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
        }

        // 2026-08-16 CTB 改造:原为「施放当回合灼烧层数为 3」,现为 2——同上一条因果链,
        // 两只敌人都在召唤物出手后的同一次 EndTurn 内轮到自己,各自当场结算掉 1 层。
        [Test]
        public void OnHitBurnAll_BurnsEveryLivingEnemy()
        {
            var engine = Engine(new[] { "炬" }, new[] { Dummy(hp: 200), Dummy(hp: 200) });
            engine.Cast("炬");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
            Assert.That(engine.Enemies[1].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
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
            Assert.That(engine.AliveSummonCount, Is.EqualTo(2));
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

        // ---- 光环灼烧:刷新到 N 层,不是累加(2026-08-06 I1) ----

        // 2026-08-16 CTB 改造:原为「稳态 3 层」,现为「稳态 2 层」——与 OnHitBurnAll_BurnsEveryLivingEnemy
        // 同一条因果链:召唤物在 CTB 调度里的优先级(1)比敌人(3)高,同速时先出手;这只敌人
        // 紧接着在同一次 EndTurn() 内轮到自己,把召唤物刚刷新到的 3 层当场结算掉 1 层
        // (SettleBurnOn 每次只 -1,不是清零),稳态从 3 变成 2。不影响本条真正要守的"不雪球"
        // 不变量:若 RefreshBurn 退化回累加语义,层数会一路涨过 2,而不是稳稳停在 2。
        [Test]
        public void OnHitBurnAll_RepeatedAcrossTurns_DoesNotAccumulate()
        {
            // 烓(炬)连续 3 个回合出手,层数该稳定在 2,不该像 ApplyBurn 那样一路涨上去
            // —— 这正是本轮修复要堵的失控口子。
            var engine = Engine(new[] { "炬" }, new[] { Dummy(hp: 500) });
            engine.Cast("炬");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2), "第 1 回合刷新到 3 层,当场结算掉 1 层,剩 2");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2), "第 2 回合仍是 2,不继续涨");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2), "第 3 回合仍是 2");
        }

        [Test]
        public void OnHitBurn_RefreshDoesNotLowerAnExistingHigherStack()
        {
            // Math.Max 的下半边:出字灼烧先堆起来的高层数,光环(灶,OnHitBurn 2)不该把它削低。
            var engine = Engine(new[] { "焰" }, new[] { Dummy(hp: 500) });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 5, TurnsLeft = -1,
            });
            engine.Cast("焰");
            engine.EndTurn(); // 灼烧先结算(5→4 层,-1 衰减),随后召唤物出手 RefreshBurn(2):max(4,2)=4

            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(4),
                "光环打上去仍是 4(衰减后的已有层数),不会被削到 2");
        }

        // ---- 攻 0 召唤物出手不再走 DamageEnemy(2026-08-06 I2/I3) ----

        [Test]
        public void ZeroAttackSummon_DoesNotFeedScorchOrForceSplit()
        {
            // 焦痕:受击存活即 +2 攻,若攻 0 召唤物的出手仍算一次"命中",会白送敌人攻击力。
            var scorchEnemy = new EnemyDef("焦", Element.Heart, 200, 0, EnemyAbility.Scorch);
            var scorchEngine = Engine(new[] { "焰" }, new[] { scorchEnemy });
            scorchEngine.Cast("焰");
            for (int i = 0; i < 3; i++) scorchEngine.EndTurn();
            // 断 AttackBuff 而不是断 Attack:自燃改成百分点后(2026-08-12),攻 0 的怪加多少
            // 百分比都还是 0,断 Attack 会静默退化成一条永远为真的断言。
            Assert.That(scorchEngine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(0),
                "攻 0 召唤物出手不该喂焦痕自燃");
            Assert.That(scorchEngine.Enemies[0].Attack, Is.EqualTo(0));

            // 叠字怪:首次受击存活即分裂,若攻 0 召唤物的出手仍算一次"命中",会无条件替敌人触发分裂。
            var splitEnemy = new EnemyDef("叠", Element.Heart, 200, 0, EnemyAbility.Split);
            var splitEngine = Engine(new[] { "焰" }, new[] { splitEnemy });
            splitEngine.Cast("焰");
            for (int i = 0; i < 3; i++) splitEngine.EndTurn();
            Assert.That(splitEngine.Enemies.Count, Is.EqualTo(1), "攻 0 召唤物出手不该强制叠字怪分裂");
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
        public void HealAlly_StillHealsOnTheTurnSummonsClearTheField()
        {
            // 光环治疗排在出手循环**之前**,所以召唤物清场的那一拍照样回血。
            // 这条钉的就是位置:治疗若挪到出手之后,清场触发的 CheckWin + return 会把它整个吞掉。
            var engine = Engine(new[] { "荫", "素" }, new[] { new EnemyDef("靶", Element.Heart, 3, 20) });
            engine.EndTurn();                       // 场上没召唤物,玩家挨 20 → 30
            Assert.That(engine.PlayerHp, Is.EqualTo(30));

            engine.Cast("荫");                       // 攻 0,只负责回血
            engine.Cast("素");                       // 攻 3,正好收掉 3 血的靶
            engine.EndTurn();

            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won), "召唤段就该清场");
            Assert.That(engine.PlayerHp, Is.EqualTo(33), "清场那一拍的回血不能被判胜早退吞掉");
        }

        // ---- 反伤 ----

        [Test]
        public void Thorns_ReflectsFlatDamage_IgnoringWuxing()
        {
            // 敌人取金属性(而不是中立的「心」)才有判别力:召唤物是木,金克木,
            // 反伤若误用 summon.Element 结算就会吃 0.5 的反克 → floor(3×0.5)=1 → 199。
            // 用「心」当攻击方则恒 1.0x,反弹平值 3 → 197。
            var engine = Engine(new[] { "棘" }, new[] { new EnemyDef("锈", Element.Metal, 200, 4) });
            engine.Cast("棘");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(197), "反伤不走生克,反弹平值 3");
        }

        [Test]
        public void Thorns_TriggeringSplit_DoesNotOverrunTheEnemyActionBudget()
        {
            // 反伤是敌方行动段里**唯一**会打敌人的路径,它打破了「敌方段内 _enemies 不变」
            // 这个从未言明的前提:叠字怪首次受击存活即分裂扩表,而 actionCount 数组是循环前
            // 按当时敌人数预分配的 —— 循环上界若跟着 _enemies.Count 走,就会 IndexOutOfRange。
            // 前排放个非分裂的靶:让两只敌人同回合各行动一次,叠字怪排第二个触发分裂,好验证
            // 扩表发生在循环中段、上界仍按 actionCount.Length 走不越界(2026-08-06 更新:I2/I3
            // 改完后攻 0 召唤物出手不再走 DamageEnemy,靶已经吃不到那记「0 伤也计受击」了——
            // 召唤段现在对它俩都是纯摆设,真正触发分裂的仍是敌方段的反伤,断言不受影响)。
            var engine = Engine(new[] { "棘" }, new[]
            {
                new EnemyDef("靶", Element.Heart, 200, 4),
                new EnemyDef("叠", Element.Heart, 200, 4, EnemyAbility.Split),
            });
            engine.Cast("棘");
            engine.EndTurn();

            Assert.That(engine.Enemies.Count, Is.EqualTo(3), "叠字怪应被反伤打出分裂");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(22),
                "只该挨两记(4+4):分裂出的新怪没有本回合的行动配额,不许当回合就出手");
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
            // 荆棘扎人不看自己死没死:30 血召唤物挨 40 的致命一击,照样反弹
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
            Assert.That(engine.AliveSummonCount, Is.EqualTo(3));
            foreach (var summon in engine.Summons)
            {
                if (summon == null) continue;
                Assert.That(summon.Shield, Is.EqualTo(6), "先在场的那只也要吃到");
            }
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
