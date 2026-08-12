using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>点数制护甲的**接线**(E-b4/E-b5 的 T2,2026-08-12)。
    ///
    /// 本步只把减法层接进结算点,**不给任何字或敌人配点数** —— 生产配置里护甲恒 0,
    /// 于是 <c>max(0, x − max(0, 0 − 0)) == x</c>,黄金轨迹与接线之前逐字节相同,
    /// 887 条既有断言一条都不该红。这就是把「接线」与「配值」拆成两步的全部理由:
    /// **T2 的任何一条红都 100% 是接线 bug**,而不是折算率算错。
    ///
    /// 代价是接线的正确性在生产配置上不可观测(全是 0),所以本文件的测试**刻意与生产配置脱钩**:
    /// 护甲/穿透一律由测试自己经 <c>EnemyDef</c> 的构造参数、<c>BattleConfig.PlayerDefense</c>、
    /// 或直接注入状态条目造出来。否则接线对不对要等 T3 才知道,拆分就白拆了。
    ///
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests / AttackStatTests):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字就是减法本身。</summary>
    public sealed class DefenseWiringTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("乙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 50) }),
            // 丙 = 50 伤 ×2 段(剁 的形状):每段各减一次护甲
            new CharDef("丙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 50, hitCount: 2) }),
            new CharDef("丁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) }),
            new CharDef("戊", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 30) }),
            new CharDef("己", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Bleed, 40) }),
            new CharDef("庚", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            new CharDef("辛", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
            // 壬 = 反弹 50%,2 回合(镜)
            new CharDef("壬", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Reflect, 50, turns: 2) }),
            // 癸 = 攻 20 的召唤(召唤物出手**吃**敌人护甲)
            new CharDef("癸", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 300, summonCount: 1,
                    summonAttack: 20, summonChar: "木") }),
            // 子 = 攻 0 + 反伤 30 的召唤(荆;反伤**不吃**护甲)
            new CharDef("子", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 300, summonCount: 1,
                    summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Thorns = 30 }) }),
            // 丑 = 100 伤 + 穿透 99(錰 的形状,量级由 T3 定)
            new CharDef("丑", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, pierce: 99) }),
            // 寅 = 100 伤,HP<25% 且非 Boss 直接击杀(斩):抹血不走伤害,护甲挡不住
            new CharDef("寅", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100,
                    executeBelowPercent: 25, executeKills: true) }),
            // 卯 = 破甲(锤):T2 只用它验「护甲点数永不被写」,不验数值
            new CharDef("卯", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ArmorBreak, 2) }),
        });

        private static EnemyDef Armored(int defense, int hp = 1000, int attack = 0) =>
            new("锈", Element.Heart, hp, attack, defense: defense);

        private static BattleEngine Battle(EnemyDef[] enemies, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 1000 },
                library, Array.Empty<string>(), enemies, seed: 1);

        private static BattleEngine Battle(BattleConfig config, EnemyDef[] enemies,
            IReadOnlyList<StatusEffect> startingStatuses, params string[] library) =>
            new(Graph(), config, library, Array.Empty<string>(), enemies, seed: 1,
                startingHp: null, cardLevels: null, startingNormalShield: 0, startingPersistShield: 0,
                startingSummons: null, startingStatuses: startingStatuses);

        // ---- 恒等性硬线:T2 的验收判据 ----

        [Test]
        public void BaselineDefense_IsZeroEverywhere()
        {
            // 全部新字段的缺省值都是 0 —— 这是「黄金轨迹逐字节相同」的全部依据。
            // 任何一个缺省值不是 0,恒等就没了,而且是静默没的。
            Assert.That(new BattleConfig().PlayerDefense, Is.EqualTo(0), "玩家护甲基准 0");
            Assert.That(new EnemyDef("怔", Element.Heart, 10, 1).Defense, Is.EqualTo(0), "小怪护甲基准 0");
            Assert.That(new BossPhaseDef("山", Element.Earth, 10, 1).Defense, Is.EqualTo(0), "Boss 阶段护甲基准 0");
            Assert.That(new EffectDef(EffectKind.DamageSingle, 10).Pierce, Is.EqualTo(0), "穿透基准 0");
        }

        [Test]
        public void ZeroDefense_LeavesDamageByteIdentical()
        {
            var engine = Battle(new[] { Armored(0) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100),
                "护甲 0 时点数层是恒等变换:max(0, 100 − max(0, 0 − 0)) == 100");
        }

        // ---- 减法层本身 ----

        [Test]
        public void Defense_SubtractsFlatFromEachHit()
        {
            var engine = Battle(new[] { Armored(30) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 70), "100 − 30 = 70,点数不是百分比");
        }

        [Test]
        public void Defense_ClampsAtZero_NeverHealsTheEnemy()
        {
            // 裁定 10:堆甲可以把伤害打到 0,但**只是归零**。
            // 缺了外层 max(0, …),200 护甲挨 100 伤会打出 −100 —— 给敌人回血,而且全程无声。
            var engine = Battle(new[] { Armored(200, hp: 500) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500), "打出 0,不是负伤害");
        }

        [Test]
        public void Order_CritBeforeDefense()
        {
            // 结算式是 floor(基础 × 生克 × 暴击) − 护甲(spec §4.1)。
            // 反过来(先减护甲再暴击)= (100 − 30) × 1.5 = 105,等价于「暴击时护甲变薄」。
            // ⚠ 这条搬错在生产配置上**不会有任何测试变红**(T2 全场护甲 0),
            // 只有这里显式构造的非 0 护甲能把它逼出来。
            // 暴击率 100 走 RollCrit 的上端短路:必暴且一次随机都不摇。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerCritChance = 100 },
                new[] { Armored(30) }, null, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 120), "floor(100 × 1.5) − 30 = 120");
        }

        [Test]
        public void Aoe_SubtractsDefensePerTarget()
        {
            // spec §4.4(a):打 N 个目标就各减各的,不是总量只减一次。
            // 两只护甲不同的怪:只减一次的写法会让第二只吃到第一只的数(或干脆不减)。
            var engine = Battle(new[] { Armored(10), Armored(30) }, "乙");
            engine.Cast("乙");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 40), "50 − 10");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(1000 - 20), "50 − 30,各减各的");
        }

        [Test]
        public void MultiHit_SubtractsDefensePerSegment()
        {
            // 裁定 4:每段各减一次,与既有「每段完全独立过生克/破甲/斩杀」同口径。
            // 只在整发之后减一次的话是 100 − 10 = 90。
            var engine = Battle(new[] { Armored(10) }, "丙");
            engine.Cast("丙", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 80), "(50 − 10) × 2 = 80");
        }

        // ---- 负向清单:什么**不**吃护甲(spec §4.2)----

        [Test]
        public void Defense_DoesNotAffectBurnTick()
        {
            // 硬约束(spec §4.2):点数减法作用在「每层一跳」这个小数字上是**开关**不是削减 ——
            // 每层 20、护甲 30 时,2 层打 0、6 层还是 0,火系对带甲怪整条归零。
            // 缺这条测试,把护甲加到灼烧上不会有任何一条断言变红。
            var engine = Battle(new[] { Armored(30) }, "庚");
            engine.Cast("庚", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(60), "3 层 × 每层 20,不减护甲");
        }

        [Test]
        public void Defense_DoesNotAffectBleedTick()
        {
            var engine = Battle(new[] { Armored(30) }, "己");
            engine.Cast("己", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(40), "流血每跳 40,不减护甲");
        }

        [Test]
        public void Defense_DoesNotAffectDetonate()
        {
            // 引爆是把剩余层数的未来伤害一次兑现,总量口径与逐跳结算相同 —— 那边不减,这边也不减,
            // 否则「先引爆」与「烧完」会因为护甲而不等价。
            var engine = Battle(new[] { Armored(30) }, "庚", "辛");
            engine.Cast("庚", 0);
            int before = engine.Enemies[0].Hp;
            engine.Cast("辛", 0);
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(120), "3 层 → 6 层·回合 × 20");
        }

        [Test]
        public void Defense_DoesNotAffectReflect()
        {
            // 反弹是把落到我方身上的伤害原样折返,不是我方发起的挥击 —— 再让对方的皮挡一次是错位。
            var engine = Battle(new[] { Armored(30, hp: 1000, attack: 100) }, "壬");
            engine.Cast("壬");
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(50), "100 的 50% 照回去,不减护甲");
        }

        [Test]
        public void Defense_DoesNotAffectThorns()
        {
            // 荆的反伤 30 对上护甲 30:吃护甲的话恰好归零 —— 判别力最强的一组数。
            var engine = Battle(new[] { Armored(30, hp: 1000, attack: 10) }, "子");
            engine.Cast("子", replaceSummon: true);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(30), "反伤 30 全额扎进去");
        }

        [Test]
        public void Defense_DoesNotBlockExecuteKill()
        {
            // 斩杀是抹血不是伤害,不经减法层。护甲 999 也照斩。
            var engine = Battle(new[] { Armored(999, hp: 100) }, "甲", "寅");
            engine.Cast("甲", 0);                                   // 100 → 0 伤(被护甲吃光),血不动
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100), "前置:护甲把伤害吃光了");

            var engine2 = Battle(new[] { Armored(999, hp: 1000) }, "寅");
            engine2.Enemies[0].Hp = 100;                            // 10% < 25% 阈值
            engine2.Cast("寅", 0);
            Assert.That(engine2.Enemies[0].Hp, Is.EqualTo(0), "斩杀不走伤害链路,护甲挡不住");
        }

        [Test]
        public void Defense_DoesNotAffectShieldOrHeal()
        {
            // 护盾与治疗是我方资源,与「敌人的皮多厚」无关 —— 防的是减法层泄漏到防御资源上。
            var engine = new BattleEngine(Graph(), new BattleConfig { PlayerMaxHp = 1000 },
                new[] { "丁", "戊" }, Array.Empty<string>(), new[] { Armored(30) }, seed: 1,
                startingHp: 100);
            engine.Cast("丁");
            Assert.That(engine.PlayerShield, Is.EqualTo(10), "护盾拿满值");
            engine.Cast("戊");
            Assert.That(engine.PlayerHp, Is.EqualTo(130), "治疗拿满值");
        }

        // ---- 吃护甲的那一侧 ----

        [Test]
        public void SummonAttack_EatsEnemyDefense()
        {
            // spec §4.2:召唤物出手是一次挥击,吃**敌人**的护甲(而召唤物挨打不吃护甲 ——
            // 它自己没有护甲,也不借用玩家的)。
            var engine = Battle(new[] { Armored(5) }, "癸");
            engine.Cast("癸", replaceSummon: true);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(15), "召唤物攻 20 − 护甲 5");
        }

        // ---- 穿透 ----

        [Test]
        public void Pierce_OnlyOffsets_NeverOverflows()
        {
            // 穿透只把护甲抵掉,穿过头**不倒贴增伤**:护甲 5 + 穿透 99 打 100,
            // 打出的是 100 而不是 100 + 94。外层 max(0, …) 就是干这个的。
            var engine = Battle(new[] { Armored(5) }, "丑");
            engine.Cast("丑", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100), "max(0, 5 − 99) = 0,不是 −94");
        }

        [Test]
        public void PierceBuff_Status_OffsetsDefenseToo()
        {
            // 锐 的通道(本场持续的穿透):与效果自带的穿透相加,一起从同一个基础护甲里减。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000 }, new[] { Armored(30) },
                new[] { new StatusEffect
                {
                    Kind = StatusKind.PierceBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = 20, TurnsLeft = -1, SourceId = "锐",
                } }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 90), "100 − max(0, 30 − 20) = 90");
        }

        // ---- 玩家侧 ----

        [Test]
        public void PlayerDefense_SubtractsFromEnemyAttack()
        {
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 3 },
                new[] { Armored(0, attack: 10) }, null);
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(before - engine.PlayerHp, Is.EqualTo(7), "10 − 3");
        }

        [Test]
        public void PlayerDefense_ClampsAtZero_NeverHealsThePlayer()
        {
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 50 },
                new[] { Armored(0, attack: 10) }, null);
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(before), "打出 0,不是负伤害倒着回血");
        }

        [Test]
        public void DefenseBuff_Status_AddsToPlayerDefense()
        {
            // 局内护甲增益走状态(spec §4.5.3 的硬约束:基础属性不可变,变动量全在 StatusBag 里)。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 3 },
                new[] { Armored(0, attack: 10) },
                new[] { new StatusEffect
                {
                    Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = 5, TurnsLeft = -1, SourceId = "铠",
                } });
            Assert.That(engine.EffectivePlayerDefense, Is.EqualTo(8), "3 + 5");
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(before - engine.PlayerHp, Is.EqualTo(2), "10 − 8");
        }

        [Test]
        public void ArmorBreak_OnPlayer_ReducesPlayerDefense()
        {
            // spec §4.5.4:本批把「敌人破甲玩家」这条通道打通但不出敌人 —— 第八章配技能时直接可用。
            // 玩家身上的破甲只有这一个读取方,量纲就是点数(敌人侧那一项要等 T3,见
            // EffectiveEnemyDefense 的注释)。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 10 },
                new[] { Armored(0, attack: 10) },
                new[] { new StatusEffect
                {
                    Kind = StatusKind.ArmorBreak, Polarity = StatusPolarity.Debuff,
                    Magnitude = 4, TurnsLeft = -1, SourceId = "熔",
                } });
            Assert.That(engine.EffectivePlayerDefense, Is.EqualTo(6), "10 − 4");
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(before - engine.PlayerHp, Is.EqualTo(4), "10 − 6");
        }

        [Test]
        public void PlayerDefense_AppliesBeforeShield()
        {
            // 顺序口径:护甲决定有多少真正落到身上,护盾是把落下来的那部分吃掉的资源。
            // 反过来的话护盾会替护甲挡掉本就不该进来的伤害(这里会剩 0 而不是 4)。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 4 },
                new[] { Armored(0, attack: 10) }, null, "丁");
            engine.Cast("丁");
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerShield, Is.EqualTo(4), "护盾 10 只被吃掉 6");
            Assert.That(engine.PlayerHp, Is.EqualTo(before), "血没掉");
        }

        // ---- 硬约束:点数层只放属性,战斗中永不被写(spec §4.5.3)----

        [Test]
        public void Defense_IsNeverMutatedDuringBattle()
        {
            // 这条是「零新增快照字段」的全部依据。护甲一旦在战斗中可写,它就是战中可变状态,
            // 必须补一个 EnemySnapshot 字段 —— 而漏补是静默的。
            // 实现上靠类型系统兜:EnemyState.Defense 是计算属性,连 internal setter 都没有。
            var config = new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 7 };
            var engine = Battle(config, new[] { Armored(30, attack: 10) }, null, "卯", "甲");
            engine.Cast("卯", 0);
            engine.Cast("甲", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Defense, Is.EqualTo(30), "破甲/挨打/过回合都不写它");
            Assert.That(config.PlayerDefense, Is.EqualTo(7), "玩家侧同理:变动量全走 StatusBag");
        }

        [Test]
        public void Defense_SurvivesSnapshotRoundTrip_WithoutANewField()
        {
            // 敌人护甲按 DefId 从配置侧查回,玩家护甲随 config 传回 —— 两边都不进快照。
            var config = new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 3 };
            var engine = Battle(config, new[] { Armored(30) }, null, "甲");
            var defs = new Dictionary<string, EnemyDef> { ["锈"] = Armored(30) };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(), config, null, defs);
            Assert.That(restored.Enemies[0].Defense, Is.EqualTo(30));
            Assert.That(restored.EffectivePlayerDefense, Is.EqualTo(3));
            restored.Cast("甲", 0);
            Assert.That(restored.Enemies[0].Hp, Is.EqualTo(1000 - 70), "复原后减法照旧");
        }

        [Test]
        public void BossDefense_ComesFromTheCurrentPhase()
        {
            // Boss 的护甲挂在阶段上(「山」= 坚壁)。换阶时读数会变,但那是 PhaseIndex 变了、
            // 不是护甲被写 —— PhaseIndex 早就在快照里,所以仍然零新增字段。
            var boss = new EnemyDef("成语", Element.Heart, 1, 0, EnemyAbility.None, new[]
            {
                new BossPhaseDef("甲", Element.Heart, 500, 0, 1f, BossSkill.None, defense: 20),
                new BossPhaseDef("山", Element.Heart, 500, 0, 1f, BossSkill.Bulwark, defense: 60),
            });
            var engine = Battle(new[] { boss }, "甲");
            Assert.That(engine.Enemies[0].Defense, Is.EqualTo(20), "首阶段的护甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 80), "100 − 20");
        }
    }
}
