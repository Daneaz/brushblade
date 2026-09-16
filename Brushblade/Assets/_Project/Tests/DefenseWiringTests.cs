using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>护甲的**接线**(E-b4/E-b5 的 T2,2026-08-12;
    /// 2026-09-16 护甲由点数减法改为百分比减伤 DR = 甲/(甲+100),本文件的期望值整体重算过,
    /// 接线结构与用例构造一字未动 —— 变的只是最后那一步怎么算)。
    ///
    /// 本步只把减伤层接进结算点,**不给任何字或敌人配点数** —— 当时生产配置里护甲恒 0,
    /// 于是这一层是恒等变换,黄金轨迹与接线之前逐字节相同,
    /// 887 条既有断言一条都不该红。这就是把「接线」与「配值」拆成两步的全部理由:
    /// **T2 的任何一条红都 100% 是接线 bug**,而不是折算率算错。
    ///
    /// 代价是接线的正确性在生产配置上不可观测(全是 0),所以本文件的测试**刻意与生产配置脱钩**:
    /// 护甲/穿透一律由测试自己经 <c>EnemyDef</c> 的构造参数、<c>BattleConfig.PlayerDefense</c>、
    /// 或直接注入状态条目造出来。否则接线对不对要等 T3 才知道,拆分就白拆了。
    ///
    /// 测试字一律 <see cref="Element.Heart"/> 且不给配方(同 CritStatTests / AttackStatTests):
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字就是减伤本身。</summary>
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
                    passive: new SummonPassive { Thorns = 50 }) }),   // 2026-08-25:单位改百分比
            // 丑 = 100 伤 + 穿透 99(錰 的形状,量级由 T3 定)
            new CharDef("丑", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, pierce: 99) }),
            // 寅 = 100 伤,HP<25% 且非 Boss 直接击杀(斩):抹血不走伤害,护甲挡不住
            new CharDef("寅", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100,
                    executeBelowPercent: 25, executeKills: true) }),
            // 卯 = 破甲 2 点(锤 的形状;T3 起 value 就是削减点数)。这里只用它验
            // 「护甲点数永不被写」,破甲的数值/叠加/持久由 BattleEngineTests 那组守
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
                "护甲 0 时减伤层是恒等变换:100 × 100 ÷ (100 + 0) == 100");
        }

        // ---- 减伤层本身 ----

        [Test]
        public void Defense_ScalesDownEachHit()
        {
            var engine = Battle(new[] { Armored(30) }, "甲");
            engine.Cast("甲", 0);
            // 2026-09-16 护甲改百分比减伤(DR = 甲/(甲+100)),推翻 E-b4 的点数减法
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 76), "100 × 100 ÷ 130 = 76");
        }

        [Test]
        public void Defense_StaysPositive_NeverHealsTheEnemy()
        {
            // 2026-09-16 护甲改百分比减伤后,裁定 10 的「堆甲可以把伤害打到 0」**连同「钳到 0」
            // 这个行为本身一起没了**(ApplyDefense 对正伤害的下限是 1,不是 0),测试名随之改掉。
            // 200 甲只是把 100 压到 100 × 100 ÷ 300 = 33。本条改守**伤害永远为正、永不给敌人回血**
            // —— 那正是选百分比减伤的理由(见 BattleEngine.ApplyDefense)。
            var engine = Battle(new[] { Armored(200, hp: 500) }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 33), "打出 33,既不归零也不是负伤害");
        }

        [Test]
        public void Order_CritBeforeDefense()
        {
            // 结算式是 floor(基础 × 生克 × 暴击) × 100 ÷ (100 + 护甲)(spec §4.1;
            // 2026-09-16 护甲改百分比减伤,推翻 E-b4 的点数减法)。
            // 正确:floor(100 × 1.5) = 150 → 150 × 100 ÷ 130 = 115。
            // 反过来(先折护甲再暴击)= floor(100 × 100 ÷ 130) × 1.5 = 76 × 1.5 = 114。
            //
            // ⚠⚠ **2026-09-16 起本条的判别力只剩 1 点血,而且是借来的。** 百分比减伤是乘区,
            // 实数下与暴击可交换 —— 115 对 114 这个差额完全来自整数截断,不是顺序本身的后果。
            // 这里找不到「不受数值大小影响的性质」可断:顺序在新模型下**本就不改变结果**,
            // 只改变截断点。故老实记在这里:
            //   · Task 3 把等级护甲封顶 12 → 30、guard 枝抬到 10/15/25 之后,本条随时可能
            //     因为两边截断到同一个数而**静默失去全部判别力**;
            //   · 谁动这一段(BattleEngine.DamageEnemy 的护甲落点位置),别指望本条兜得住,
            //     要靠读代码和 BattleEngine 那段注释。
            // ⚠ 这条搬错在生产配置上**不会有任何测试变红**(T2 全场护甲 0),
            // 只有这里显式构造的非 0 护甲能把它逼出来。
            // 暴击率 100 走 RollCrit 的上端短路:必暴且一次随机都不摇。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerCritChance = 100 },
                new[] { Armored(30) }, null, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 115), "floor(100 × 1.5) × 100 ÷ 130 = 115");
        }

        [Test]
        public void Aoe_AppliesDefensePerTarget()
        {
            // spec §4.4(a):打 N 个目标就各折各的,不是总量只折一次。
            // 两只护甲不同的怪:只折一次的写法会让第二只吃到第一只的比例(或干脆不折)。
            var engine = Battle(new[] { Armored(10), Armored(30) }, "乙");
            engine.Cast("乙");
            // 2026-09-16 护甲改百分比减伤(DR = 甲/(甲+100)),期望值按新公式重算
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 45), "50 × 100 ÷ 110 = 45");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(1000 - 38), "50 × 100 ÷ 130 = 38,各折各的");
        }

        [Test]
        public void MultiHit_AppliesDefensePerSegment()
        {
            // 裁定 4:每段各折一次,与既有「每段完全独立过生克/破甲/斩杀」同口径。
            //
            // ⚠ **2026-09-16 改断结构不断数值**:百分比减伤是乘区,对多段满足分配律 ——
            // 旧模型下「每段各减」(80)与「整发减一次」(90)数值不同,本条靠那个差值守顺序;
            // 百分比化后两条路径合流(45×2 = 90 与 100×100÷110 = 90 逐位相同),
            // 那个判据永久消失了。本条因此改断**仍然成立的那条性质**:
            // 每一段都各自过一次减伤,基数是段伤不是整发。
            //
            // 判别表(护甲 10,50 伤 ×2 段):
            //   每段各折(正确)    50→45,×2 = 90
            //   只第一段折          45 + 50   = 95   ← 抓得住
            //   一段都不折          50 + 50   = 100  ← 抓得住
            //   整发合并折一次      100→90         ← **抓不住**(分配律,见上)
            var engine = Battle(new[] { Armored(10) }, "丙");
            engine.Cast("丙", 0);

            // 参照组:乙 是 50 伤全体,单只敌人时就是一记 50 —— 与 丙 的**一段**同形。
            // 用它算出「一段过完减伤是多少」,而不是在断言里写死一个算好的数。
            var single = Battle(new[] { Armored(10) }, "乙");
            single.Cast("乙");
            int oneSegment = 1000 - single.Enemies[0].Hp;

            Assert.That(1000 - engine.Enemies[0].Hp, Is.EqualTo(oneSegment * 2),
                "两段各自过一次减伤:总伤 = 单段过甲后的值 × 2");
            Assert.That(oneSegment, Is.EqualTo(45), "夹具基线:50 × 100 ÷ 110 = 45(不是未减的 50)");
        }

        // ---- 负向清单:什么**不**吃护甲(spec §4.2)----

        [Test]
        public void Defense_DoesNotAffectBurnTick()
        {
            // 硬约束(spec §4.2):灼烧跳伤不吃护甲。
            // ⚠ 2026-09-16:原立论(点数减法对「每层一跳」这个小数字是**开关**不是削减,
            // 每层 20 对护甲 30 会 2 层打 0、6 层还是 0,火系对带甲怪整条归零)已随百分比化作废 ——
            // 乘区模型下小数字只会被按比例压薄,何况 ApplyDefense 对正伤害还有下限 1。
            // 规则本身不变(灼烧仍然不吃护甲),但它现在是一条设计选择,不再有「否则归零」撑着。
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
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(5),
                "反伤 50% × 10 = 5 全额扎进去 —— 敌人 30 点护甲一点没吃掉");
        }

        [Test]
        public void Defense_DoesNotBlockExecuteKill()
        {
            // 斩杀是抹血不是伤害,不经减伤层。护甲 999 也照斩。
            // 2026-09-16 护甲改百分比减伤后 999 甲也吃不光伤害(100 × 100 ÷ 1099 = 9),
            // 前置改成「伤害链路被护甲压到只剩 9」;下面那半(斩杀不走伤害链路)才是本条要守的
            var engine = Battle(new[] { Armored(999, hp: 100) }, "甲", "寅");
            engine.Cast("甲", 0);                                   // 100 → 9 伤(被护甲压薄)
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(100 - 9), "前置:护甲把伤害压到 9");

            var engine2 = Battle(new[] { Armored(999, hp: 1000) }, "寅");
            engine2.Enemies[0].Hp = 100;                            // 10% < 25% 阈值
            engine2.Cast("寅", 0);
            Assert.That(engine2.Enemies[0].Hp, Is.EqualTo(0), "斩杀不走伤害链路,护甲挡不住");
        }

        [Test]
        public void Defense_DoesNotAffectShieldOrHeal()
        {
            // 护盾与治疗是我方资源,与「敌人的皮多厚」无关 —— 防的是减伤层泄漏到防御资源上。
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
            // 2026-09-16 护甲改百分比减伤:20 × 100 ÷ 105 = 19
            Assert.That(before - engine.Enemies[0].Hp, Is.EqualTo(19), "召唤物攻 20 过护甲 5 = 19");
        }

        // ---- 穿透 ----

        [Test]
        public void Pierce_OnlyOffsets_NeverOverflows()
        {
            // 穿透只把护甲抵掉,穿过头**不倒贴增伤**:护甲 5 + 穿透 99 打 100,
            // 打出的是 100 而不是 100 + 94。EffectiveEnemyDefense 的 max(0, …) 就是干这个的
            // —— 那一层 2026-09-16 一字未改,穿过头仍然只是把护甲抵到 0。
            var engine = Battle(new[] { Armored(5) }, "丑");
            engine.Cast("丑", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 100), "max(0, 5 − 99) = 0,不是 −94");
        }

        [Test]
        public void PierceBuff_Status_OffsetsDefenseToo()
        {
            // 锐 的通道(本场持续的穿透):与效果自带的穿透相加,一起从同一个基础护甲里减。
            // 2026-09-16 百分比化后这个数**恰好没变**(100 × 100 ÷ 110 = 90,与旧的 100 − 10 同值),
            // 别据此以为这条没受影响 —— 算式换了,见下方断言消息。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000 }, new[] { Armored(30) },
                new[] { new StatusEffect
                {
                    Kind = StatusKind.PierceBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = 20, TurnsLeft = -1, SourceId = "锐",
                } }, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 90), "100 × 100 ÷ (100 + max(0, 30 − 20)) = 90");
        }

        // ---- 玩家侧 ----

        [Test]
        public void PlayerDefense_ScalesDownEnemyAttack()
        {
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 3 },
                new[] { Armored(0, attack: 10) }, null);
            int before = engine.PlayerHp;
            engine.EndTurn();
            // 2026-09-16 护甲改百分比减伤:10 × 100 ÷ 103 = 9
            Assert.That(before - engine.PlayerHp, Is.EqualTo(9), "10 × 100 ÷ 103 = 9");
        }

        [Test]
        public void PlayerDefense_StaysPositive_NeverHealsThePlayer()
        {
            // 2026-09-16 护甲改百分比减伤后,50 点甲只把 10 压到 10 × 100 ÷ 150 = 6,不再归零 ——
            // 「钳到 0」这个行为本身也没了(ApplyDefense 对正伤害的下限是 1),测试名随之改掉。
            // 本条改守**伤害永远为正**:负伤害会先被护盾吸收段吃掉(Math.Min(护盾, −40) = −40,
            // 护盾反而凭空涨 40,血量看上去纹丝不动),所以护盾那条断言才是真正的观测点。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 50 },
                new[] { Armored(0, attack: 10) }, null);
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(before - 6), "掉 6,不是负伤害倒着回血");
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "更不能凭空长出护盾");
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
            // 2026-09-16 护甲改百分比减伤:10 × 100 ÷ 108 = 9
            Assert.That(before - engine.PlayerHp, Is.EqualTo(9), "10 × 100 ÷ 108 = 9");
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
            // 2026-09-16 护甲改百分比减伤:10 × 100 ÷ 106 = 9。⚠ 这个数与「破甲没生效」(甲 8 → 9)
            // 撞在一起,掉血这条断言已无判别力 —— 真正守破甲的是上面 EffectivePlayerDefense == 6。
            Assert.That(before - engine.PlayerHp, Is.EqualTo(9), "10 × 100 ÷ 106 = 9");
        }

        [Test]
        public void PlayerDefense_AppliesBeforeShield()
        {
            // 顺序口径:护甲决定有多少真正落到身上,护盾是把落下来的那部分吃掉的资源。
            // 2026-09-16 护甲改百分比减伤:10 × 100 ÷ 104 = 9,10 点盾被吃掉 9 剩 1。
            // 反过来的话护盾会替护甲挡掉本就不该进来的伤害(这里会剩 0 而不是 1)。
            var engine = Battle(new BattleConfig { PlayerMaxHp = 1000, PlayerDefense = 4 },
                new[] { Armored(0, attack: 10) }, null, "丁");
            engine.Cast("丁");
            int before = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerShield, Is.EqualTo(1), "护盾 10 只被吃掉 9");
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
            // 2026-09-16 护甲改百分比减伤:100 × 100 ÷ 130 = 76
            Assert.That(restored.Enemies[0].Hp, Is.EqualTo(1000 - 76), "复原后折算照旧");
        }

        [Test]
        public void BossDefense_ComesFromTheCurrentPhase()
        {
            // Boss 的护甲挂在阶段上(「山」= 坚壁)。换阶时读数会变,但那是 PhaseIndex 变了、
            // 不是护甲被写 —— PhaseIndex 早就在快照里,所以仍然零新增字段。
            var boss = new EnemyDef("成语", Element.Heart, 1, 0, EnemyAbility.None, new[]
            {
                new BossPhaseDef("甲", Element.Heart, 500, 0, BossSkill.None, defense: 20),
                new BossPhaseDef("山", Element.Heart, 500, 0, BossSkill.Bulwark, defense: 60),
            });
            var engine = Battle(new[] { boss }, "甲");
            Assert.That(engine.Enemies[0].Defense, Is.EqualTo(20), "首阶段的护甲");
            engine.Cast("甲", 0);
            // 2026-09-16 护甲改百分比减伤:100 × 100 ÷ 120 = 83
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(1000 - 83), "100 × 100 ÷ 120 = 83");
        }
    }
}
