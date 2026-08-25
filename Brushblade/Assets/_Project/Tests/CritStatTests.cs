using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>暴击(E-b2,2026-08-12):玩家属性集里第一条**带随机性**的轴。
    ///
    /// 用户裁定(与设计草案的推荐不同):暴击率**不随等级成长**,基准恒 0,
    /// 只靠字(锋)与将来的养成技能给;倍率 ×1.5;只有 DamageSingle / DamageAll 吃;
    /// 敌人没有暴击;每记伤害独立摇,且摇点排在 TryExecuteKill 之后。
    ///
    /// 测试字一律用 <see cref="Element.Heart"/> 且不给配方,理由同 AttackStatTests:
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字
    /// 就是暴击缩放本身,不掺生克。
    ///
    /// 随机性靠 <see cref="BattleEngine"/> 两端短路控住:暴击率 0 必不暴、100 必暴,
    /// 两者都**一次随机都不摇**。于是正负向断言全部可以写死数字,不需要为测试
    /// 开放注入 RNG 的构造重载。</summary>
    public sealed class CritStatTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            // 锋 的真实配置(《技能机制详表》金系 BUFF 表):本场暴击 +20%,可叠加
            new CharDef("锋", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 20) }),
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("乙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
            new CharDef("丙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 7) }),
            new CharDef("丁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 9) }),
            new CharDef("己", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Bleed, 4) }),
            new CharDef("庚", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 20, summonCount: 1,
                    summonAttack: 6, summonChar: "木") }),
            new CharDef("辛", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            new CharDef("壬", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
            // 癸 = 基础 6:乘法顺序专用,数字不能随手改(见 CritLandsAfterAttackScalingFloor)
            new CharDef("癸", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6) }),
            // 子 = 破甲 2 **点**(2026-08-12,E-b4 T3:value 从回合数变成削减的护甲点数)
            new CharDef("子", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ArmorBreak, 2) }),
            // 丑 = 10 伤 ×2 段(剁)
            new CharDef("丑", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 2) }),
            // 寅 = 反弹 50%,2 回合(镜)
            new CharDef("寅", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Reflect, 50, turns: 2) }),
            // 卯 = 攻 0 + 反伤 3 的召唤(荆)
            new CharDef("卯", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 1,
                    summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Thorns = 50 }) }),   // 2026-08-25:单位改百分比
            // 辰 = 20 伤,HP<25% 且非 Boss 直接击杀(斩)
            new CharDef("辰", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 20,
                    executeBelowPercent: 25, executeKills: true) }),
        });

        private static EnemyDef Dummy(int hp = 500) => new("怔", Element.Heart, hp, 0);
        private static EnemyDef Attacker(int attack = 8) => new("靶", Element.Heart, 500, attack);

        private static BattleEngine Battle(int crit, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerCritChance = crit, PlayerMaxHp = 100 },
                library, Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        private static BattleEngine Battle(BattleConfig config, EnemyDef[] enemies,
            params string[] library) =>
            new(Graph(), config, library, Array.Empty<string>(), enemies, seed: 1);

        private static int CritCount(BattleEngine engine)
        {
            int n = 0;
            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.Damage && e.Crit) n++;
            return n;
        }

        // ---- 恒等性硬线 ----

        [Test]
        public void BaselineCrit_LeavesDamageByteIdentical()
        {
            // 这条是整个子项目的地基:基准值(暴击 0)下伤害必须与引入暴击之前一模一样。
            var engine = Battle(0, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
        }

        [Test]
        public void DefaultConfig_CritChanceIsZero()
        {
            // 基准恒 0(2026-08-12 用户裁定:暴击不随等级成长)—— 它同时是
            // GameRoot 的生产路径值,所以恒等性在生产路径上一并成立。
            Assert.That(new BattleConfig().PlayerCritChance, Is.EqualTo(0));
            Assert.That(BattleConfig.CritMultiplierPercent, Is.EqualTo(150), "×1.5");
        }

        // ---- 一次随机都不摇(两端短路)----

        [Test]
        public void ZeroCrit_NeverTouchesRandomStream()
        {
            // 恒等性的**可执行**形式:E-b1 时只能写在注释里,现在能断言。
            // UnlockedChars 默认 null(不掉字)、Dummy 无分阶段无伪装(构造不摇),
            // 于是这段时间里 _random 的唯一潜在消费方就是暴击判定。
            var engine = Battle(0, "甲", "甲", "甲");
            uint before = engine.Capture().RandomState;
            engine.Cast("甲", 0);
            engine.Cast("甲", 0);
            engine.Cast("甲", 0);
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "暴击率 0 直接短路返回 false,一次随机都不摇");
        }

        [Test]
        public void FullCrit_NeverTouchesRandomStream()
        {
            // 上端对称短路:必暴时摇不摇结果都一样,不摇才能让「暴击叠满」的玩法路径
            // 同样不扰动随机流。
            var engine = Battle(100, "甲", "甲", "甲");
            uint before = engine.Capture().RandomState;
            engine.Cast("甲", 0);
            engine.Cast("甲", 0);
            engine.Cast("甲", 0);
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before));
        }

        [Test]
        public void MidCrit_ConsumesRandomStream()
        {
            // 中间档必须真的摇 —— 否则「两端短路」会退化成「永远不摇」。
            var engine = Battle(50, "甲");
            uint before = engine.Capture().RandomState;
            engine.Cast("甲", 0);
            Assert.That(engine.Capture().RandomState, Is.Not.EqualTo(before));
        }

        [Test]
        public void MidCrit_ProducesBothOutcomesOverManyCasts()
        {
            // 摇点没被写死成必中/必不中:50% 摇 20 次,暴击数严格落在开区间 (0, 20)。
            var config = new BattleConfig { PlayerCritChance = 50, PlayerMaxHp = 100, ApPerTurn = 20 };
            var library = new string[20];
            for (int i = 0; i < library.Length; i++) library[i] = "甲";
            var engine = Battle(config, new[] { Dummy(5000) }, library);
            int crits = 0;
            for (int i = 0; i < 20; i++)
            {
                engine.Cast("甲", 0);
                crits += CritCount(engine);
            }
            Assert.That(crits, Is.GreaterThan(0), "20 次全不暴 = 摇点被写死成 false");
            Assert.That(crits, Is.LessThan(20), "20 次全暴 = 摇点被写死成 true");
        }

        [Test]
        public void NegativeCritChance_ClampsToZeroAndDoesNotRoll()
        {
            // 下钳位:Clamp 的下界没了的话 _random.Next(100) < -50 恒 false,行为看似相同,
            // 但那是**摇过一次**的 false —— 随机流被平移,恒等性静默失守。
            var engine = Battle(-50, "甲");
            uint before = engine.Capture().RandomState;
            Assert.That(engine.EffectiveCrit, Is.EqualTo(0));
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before));
        }

        // ---- 倍率与乘法顺序 ----

        [Test]
        public void FullCrit_MultipliesSingleTargetDamage()
        {
            var engine = Battle(100, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "20 × 150 ÷ 100 = 30");
        }

        [Test]
        public void CritAndAttack_MultiplyWithoutSwallowingEachOther()
        {
            var config = new BattleConfig { PlayerCritChance = 100, PlayerAttack = 150, PlayerMaxHp = 100 };
            var engine = Battle(config, new[] { Dummy() }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 45), "20 × 1.5(ATK)× 1.5(暴击)= 45");
        }

        [Test]
        public void CritLandsAfterAttackScalingFloor()
        {
            // **守乘法顺序的唯一一条**。生克全 1.0x 的测试字上,暴击放在哪一步结果都一样,
            // 其余测试全绿也守不住顺序;必须在暴击与一次**截断**之间夹一个非 1 的系数。
            //
            // ⚠ 2026-08-12(E-b4 T3):原先夹的是破甲的承伤 ×1.25 —— 那条乘法层已随点数护甲
            // 一起删除(守方侧从此没有任何乘数)。改夹 ScaleByAttack 的整数除,它同样是
            // 「非 1 系数 + 截断」,三组数字与原设计完全同构:
            //   本设计(暴击在最末、整数除):6 × 125 ÷ 100 = 7 → 7 × 150 ÷ 100 = 10
            //   变异 ① 暴击挪到 ScaleByAttack 之前:6 × 150 ÷ 100 = 9 → 9 × 125 ÷ 100 = 11
            //   变异 ② 暴击折进浮点一起算:        floor(6 × 1.25 × 1.5) = floor(11.25) = 11
            // 基础值必须是 6,系数必须是 125 —— 换成别的会有一种变异分不开。
            // **这条测试的数字不能随手改。**
            var engine = Battle(
                new BattleConfig { PlayerCritChance = 100, PlayerAttack = 125, PlayerMaxHp = 100 },
                new[] { Dummy() }, "癸");
            engine.Cast("癸", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 10),
                "6 × 125 ÷ 100 = 7,再 × 150 ÷ 100 = 10(不是 11)");
        }

        // ---- 覆盖面:单体 / 群体 / 多段 ----

        [Test]
        public void FullCrit_CritsEveryAoeTarget()
        {
            var engine = Battle(new BattleConfig { PlayerCritChance = 100, PlayerMaxHp = 100 },
                new[] { Dummy(), Dummy() }, "乙");
            engine.Cast("乙");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 15), "10 × 150 ÷ 100 = 15");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(500 - 15));
            Assert.That(CritCount(engine), Is.EqualTo(2));
        }

        [Test]
        public void AoeDamage_RollsOncePerTarget()
        {
            // 「逐个目标判定」的可执行形式:两只敌人的一发 AOE,与分别打两下单体
            // 消耗的随机数应当**一样多** —— 同种子同起点,末态相同即证明摇了 2 次。
            var config = new BattleConfig { PlayerCritChance = 50, PlayerMaxHp = 100 };
            var aoe = Battle(config, new[] { Dummy(), Dummy() }, "乙");
            var single = Battle(config, new[] { Dummy(), Dummy() }, "甲", "甲");
            aoe.Cast("乙");
            single.Cast("甲", 0);
            single.Cast("甲", 1);
            Assert.That(aoe.Capture().RandomState, Is.EqualTo(single.Capture().RandomState),
                "一发 AOE 打 2 只 = 摇 2 次");
        }

        [Test]
        public void FullCrit_CritsEveryHitOfMultiHit()
        {
            var engine = Battle(100, "丑");
            engine.Cast("丑", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "两段各 10 × 1.5 = 15");
            Assert.That(CritCount(engine), Is.EqualTo(2), "两段各发一条暴击伤害事件");
        }

        [Test]
        public void MultiHit_RollsOncePerHit()
        {
            // 「每记伤害独立摇」:一张 2 段字与两张单段字,从同一起点出发消耗的随机数相同。
            // 变成「每张牌只摇一次」的话 2 段字只摇 1 次,末态就对不上了。
            var config = new BattleConfig { PlayerCritChance = 50, PlayerMaxHp = 100 };
            var multi = Battle(config, new[] { Dummy() }, "丑");
            var twice = Battle(config, new[] { Dummy() }, "甲", "甲");
            multi.Cast("丑", 0);
            twice.Cast("甲", 0);
            twice.Cast("甲", 0);
            Assert.That(multi.Capture().RandomState, Is.EqualTo(twice.Capture().RandomState),
                "2 段 = 摇 2 次");
        }

        // ---- 摇点排在处决之后 ----

        [Test]
        public void ExecuteKill_RollsNoCritAtAll()
        {
            // 目标直接归零,没有可乘的伤害数 —— 白摇一次会让「这一发消耗几个随机数」
            // 取决于目标够不够斩杀线,复现与调试都会变成噩梦。
            var engine = Battle(50, "辰");
            engine.Enemies[0].Hp = 10;   // 10 / 500 = 2% < 25%,进斩杀线
            uint before = engine.Capture().RandomState;
            engine.Cast("辰", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(0), "被处决");
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "摇点排在 TryExecuteKill 之后");
        }

        // ---- 锋:局内暴击增益 ----

        [Test]
        public void Feng_RaisesEffectiveCritByTwenty()
        {
            var engine = Battle(0, "锋");
            engine.Cast("锋");
            Assert.That(engine.EffectiveCrit, Is.EqualTo(20));
        }

        [Test]
        public void Feng_StacksWhenCastTwice()
        {
            // SourceId 铸唯一序号才能叠(StatusEffect.SourceId 的用法 2);误传裸字 ID
            // 会让第二张锋覆盖第一张,静默退化成「刷新」。
            var engine = Battle(0, "锋", "锋");
            engine.Cast("锋");
            engine.Cast("锋");
            Assert.That(engine.EffectiveCrit, Is.EqualTo(40), "两张锋叠加,不是刷新");
        }

        [Test]
        public void Feng_StackedPastHundred_ClampsToHundred()
        {
            // 6 张锋 = 120 → 钳到 100。上钳位不只是数值卫生:它是 RollCrit 的
            // >= 100 短路能被真实玩法路径抵达的前提。
            var config = new BattleConfig { PlayerCritChance = 0, PlayerMaxHp = 100, ApPerTurn = 10 };
            var engine = Battle(config, new[] { Dummy() },
                "锋", "锋", "锋", "锋", "锋", "锋", "甲");
            for (int i = 0; i < 6; i++) engine.Cast("锋");
            Assert.That(engine.EffectiveCrit, Is.EqualTo(100), "120 钳到 100");
            uint before = engine.Capture().RandomState;
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "必暴");
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before), "钳到 100 后走上端短路");
        }

        [Test]
        public void Feng_AddsOntoConfigCritChance()
        {
            var engine = Battle(90, "锋");
            engine.Cast("锋");
            Assert.That(engine.EffectiveCrit, Is.EqualTo(100), "90 + 20 钳到 100,不是 110");
        }

        // ---- 事件:表现层拿得到 ----

        [Test]
        public void DamageEvent_CarriesCritFlag()
        {
            var crit = Battle(100, "甲");
            crit.Cast("甲", 0);
            Assert.That(CritCount(crit), Is.EqualTo(1));
            var plain = Battle(0, "甲");
            plain.Cast("甲", 0);
            Assert.That(CritCount(plain), Is.EqualTo(0), "不暴的伤害事件 Crit == false");
        }

        // ---- 负向:6 个 DamageEnemy 调用点里另外 4 个一律不暴 ----

        [Test]
        public void FullCrit_DoesNotCritSummonCounterAttack()
        {
            // 召唤物是独立实体,暴击是**玩家**属性。这条守的是 DamageEnemy 的调用点 3:
            // 把暴击判定写进 DamageEnemy 内部而不是做成参数,这里立刻红。
            var engine = Battle(100, "庚");
            engine.Cast("庚");
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 6), "召唤物攻 6,不暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritMirrorReflect()
        {
            // 调用点 4(DamagePlayerDirect 的反弹):按挨到的伤害照原样反,不是玩家输出。
            var engine = Battle(new BattleConfig { PlayerCritChance = 100, PlayerMaxHp = 100 },
                new[] { Attacker(8) }, "寅");
            engine.Cast("寅");
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 4), "8 的 50% = 4,不暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritThornsOrSummonSideReflect()
        {
            // 调用点 5(荆的反伤)与 6(召唤物顶前排时的镜反弹):一发敌人攻击同时走这两条。
            // 反伤 3 + 反弹 floor(8 × 50%) = 4,合计 7;任一条误暴都会打出 8 或 9。
            var engine = Battle(new BattleConfig { PlayerCritChance = 100, PlayerMaxHp = 100 },
                new[] { Attacker(8) }, "卯", "寅");
            engine.Cast("卯");
            engine.Cast("寅");
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8),
                "反伤 50% × 8 = 4,加反弹 4,都不暴击(暴击若漏进来会是 12)");
        }

        [Test]
        public void FullCrit_DoesNotCritEnemyAttackOnPlayer()
        {
            // 敌人没有暴击(用户裁定):加了就要进 EnemySnapshot,破掉「零新增快照字段」。
            var engine = Battle(new BattleConfig { PlayerCritChance = 100, PlayerMaxHp = 100 },
                new[] { Attacker(8) }, "甲");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(100 - 8), "玩家的暴击率不会反过来砸自己");
        }

        // ---- 负向:DoT / 召唤 / 护盾 / 治疗一律不吃 ----

        [Test]
        public void FullCrit_DoesNotCritBurnTick()
        {
            // 灼烧是全场标量 × 层数、回溯生效,根本没有「一次挥击」。
            var engine = Battle(100, "辛");
            engine.Cast("辛", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 60), "3 层 × 每层 20 = 60,不暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritDetonate()
        {
            // 引爆是灼烧层数的**提前兑现**,总量口径与回合末结算同一条。
            // 3 层:3×4/2 = 6 → × 每层 20 = 120。
            var engine = Battle(100, "辛", "壬");
            engine.Cast("辛", 0);
            engine.Cast("壬", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 120), "引爆 120,不暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritBleed()
        {
            // 出牌时快照的是 Magnitude,一次暴击会把三回合全部放大且全程无提示。
            var engine = Battle(100, "己");
            engine.Cast("己", 0);
            var bleed = engine.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed, Is.Not.Null);
            Assert.That(bleed.Magnitude, Is.EqualTo(4), "流血每回合量不吃暴击");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 4), "结算时也不暴");
        }

        [Test]
        public void FullCrit_DoesNotCritSummonAttackValue()
        {
            var engine = Battle(100, "庚");
            engine.Cast("庚");
            Assert.That(engine.Summons[0].Attack, Is.EqualTo(6), "召唤物攻击力不吃暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritShield()
        {
            var engine = Battle(100, "丙");
            engine.Cast("丙");
            Assert.That(engine.PlayerShield, Is.EqualTo(7), "护盾不吃暴击");
        }

        [Test]
        public void FullCrit_DoesNotCritHeal()
        {
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerCritChance = 100, PlayerMaxHp = 100 },
                new[] { "丁" }, Array.Empty<string>(), new[] { Dummy() }, seed: 1,
                startingHp: 50);
            engine.Cast("丁");
            Assert.That(engine.PlayerHp, Is.EqualTo(59), "治疗不吃暴击");
        }

        [Test]
        public void FullCrit_DoesNotScaleBurnStacksOrArmorBreakPoints()
        {
            // 层数与护甲削减点数都不是伤害,乘 1.5 是另一回事。
            // (2026-08-12,E-b4 T3:破甲的 value 从回合数变成点数,负向口径不变)
            var engine = Battle(100, "辛", "子");
            engine.Cast("辛", 0);
            engine.Cast("子", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn),
                Is.EqualTo(3), "灼烧层数不吃暴击");
            var armorBreak = engine.Enemies[0].Statuses.Find(StatusKind.ArmorBreak);
            Assert.That(armorBreak, Is.Not.Null);
            Assert.That(armorBreak.Magnitude, Is.EqualTo(2), "破甲削减点数不吃暴击");
            Assert.That(armorBreak.TurnsLeft, Is.EqualTo(-1), "破甲本场持久");
        }

        // ---- 快照:目标是零新增字段 ----

        [Test]
        public void CritBuff_SurvivesSnapshotRoundTrip()
        {
            var engine = Battle(0, "锋");
            engine.Cast("锋");
            var defs = new Dictionary<string, EnemyDef> { ["怔"] = Dummy() };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerMaxHp = 100 }, null, defs);
            Assert.That(restored.EffectiveCrit, Is.EqualTo(20),
                "局内增益存在 PlayerStatuses 里,快照本来就在存 —— 零新增字段");
        }

        // ---- 存档兼容:枚举序号锁值 ----

        [Test]
        public void StatusKindOrdinals_AreLockedForSaveCompatibility()
        {
            // StatusEffect 随 EndlessSaveState.CarriedStatuses / BattleSnapshot.PlayerStatuses
            // 进 JSON,而 SaveSerializer **没有注册 StringEnumConverter** —— Newtonsoft 默认
            // 把枚举序列化成**整数**。在中间插一个新值会让所有旧存档里的状态整体错位
            // (减伤变成破甲之类),而且是**静默**的:全部单元测试都建新对象,没有一条读旧 JSON 字节。
            //
            // 这条测试就是那道防线:**新枚举值一律追加在末尾**,改动这里的任何一个数字
            // 都等于宣布旧存档作废。
            Assert.That((int)StatusKind.Burn, Is.EqualTo(0));
            // 序号 5 是**废弃占位**(2026-08-12,E-b4 T3):乘法减伤层删除后它没有载体了,
            // 但**不能删也不能复用** —— 删了 6 以后全部前移,复用则单位从百分点变成点数
            // 而序号不变,两条都是静默存档损坏。新载体是末尾的 DefenseBuff(18)。
            Assert.That((int)StatusKind.ObsoleteDamageReduction, Is.EqualTo(5), "废弃占位,占着不许动");
            Assert.That((int)StatusKind.AttackBuff, Is.EqualTo(6));
            Assert.That((int)StatusKind.ArmorBreak, Is.EqualTo(7));
            Assert.That((int)StatusKind.Immunity, Is.EqualTo(10));
            Assert.That((int)StatusKind.Reflect, Is.EqualTo(13));
            Assert.That((int)StatusKind.Morale, Is.EqualTo(15));
            Assert.That((int)StatusKind.ApBoost, Is.EqualTo(16));
            Assert.That((int)StatusKind.CritBuff, Is.EqualTo(17), "新值必须追加在末尾");
            // E-b4 T2(2026-08-12):点数护甲的两条通道,按落地顺序接在 CritBuff 之后。
            // spec 曾把 18/19 预留给 DodgeBuff/PierceBuff,但 DodgeBuff 是 T4 的活、比这两个晚到 ——
            // spec 自己写明「序号跟着实际合流顺序走,锁值测试写实际值」,于是 DodgeBuff 顺延到 20。
            Assert.That((int)StatusKind.DefenseBuff, Is.EqualTo(18), "新值必须追加在末尾");
            Assert.That((int)StatusKind.PierceBuff, Is.EqualTo(19), "新值必须追加在末尾");
            // E-b4 T4(2026-08-12):玩家闪避的局内增益通道。spec §11.3 原本预留 18 给它,
            // 实际合流顺序是 T2 的两条先到,于是顺延到 20 —— 按 spec 自己的规定「写实际值」。
            Assert.That((int)StatusKind.DodgeBuff, Is.EqualTo(20), "新值必须追加在末尾");
        }
    }
}
