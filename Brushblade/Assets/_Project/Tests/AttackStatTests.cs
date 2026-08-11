using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>玩家攻击力(19.2.1 角色属性)进入伤害链路。
    ///
    /// 测试字一律用 <see cref="Element.Heart"/> 且不给配方:心对全属性生克都是 1.0x,
    /// 又没有配方就不会触发相生 ×3 —— 于是断言里看到的数字就是 ATK 缩放本身,
    /// 不掺生克。用五行属性的字会让 ×1.5/×3 混进来,算错了也看不出是哪一层的错。</summary>
    public sealed class AttackStatTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("乙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 10) }),
            new CharDef("丙", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 7) }),
            new CharDef("丁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 9) }),
            new CharDef("戊", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Freeze, 2) }),
            new CharDef("己", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Bleed, 4) }),
            new CharDef("庚", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 20, summonCount: 1,
                    summonAttack: 6, summonChar: "木") }),
            new CharDef("辛", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            new CharDef("壬", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
            new CharDef("癸", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 4) }),
            new CharDef("子", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Reflect, 30, turns: 2) }),
            new CharDef("丑", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Immunity, 3) }),
            new CharDef("寅", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealOverTime, 8, turns: 3) }),
            new CharDef("卯", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Slow, 4) }),
            new CharDef("辰", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 40, turns: 2) }),
            new CharDef("巳", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Silence, 0, turns: 3) }),
        });

        private static EnemyDef Dummy(int hp = 500) => new("怔", Element.Heart, hp, 0);

        private static BattleEngine Battle(int attack, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerAttack = attack, PlayerMaxHp = 100 },
                library, Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        // ---- 恒等性硬线 ----

        [Test]
        public void BaselineAttack_LeavesDamageByteIdentical()
        {
            // 这条是整个子项目的地基:基准值下伤害必须与引入攻击力之前一模一样。
            // 它红了意味着 792 条现有断言里会有一批跟着红,实现方向就是错的。
            var engine = Battle(BattleConfig.AttackBaseline, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
        }

        [Test]
        public void DefaultConfig_AttackIsBaseline()
        {
            Assert.That(new BattleConfig().PlayerAttack, Is.EqualTo(BattleConfig.AttackBaseline));
            Assert.That(BattleConfig.AttackBaseline, Is.EqualTo(100));
        }

        // ---- 直接伤害吃 ATK ----

        [Test]
        public void HigherAttack_ScalesSingleTargetDamage()
        {
            var engine = Battle(150, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "20 × 150 ÷ 100 = 30");
        }

        [Test]
        public void HigherAttack_ScalesAoeDamage()
        {
            var engine = Battle(150, "乙");
            engine.Cast("乙");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 15), "10 × 150 ÷ 100 = 15");
        }

        [Test]
        public void IntegerDivision_TruncatesSmallValues()
        {
            // 已知副作用(spec 第十节):整数除会吃掉低数值字的加成。
            // 20 × 102 ÷ 100 = 20(不是 20.4 也不是 21)。
            // 写成测试是为了让它成为**有意的行为**而不是某天被人当 bug「修」掉 ——
            // 真解法是 E-b5 抬高字表数值量级,不是在这里改成 ceil。
            var engine = Battle(102, "甲");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 20));
        }

        // ---- 局内增益 ----

        [Test]
        public void InCombatAttackBuff_AddsToConfigAttack()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "甲");
            engine.ApplyPlayerAttackBuff(50);
            Assert.That(engine.EffectiveAttack, Is.EqualTo(150));
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30));
        }

        // ---- 负向:不该吃的没吃 ----

        [Test]
        public void HighAttack_DoesNotScaleShield()
        {
            // 只写正向断言的话,把护盾也乘上 ATK 不会有任何测试发现 ——
            // 子项目 D 的白名单方向性教训,同一类漏洞换了个形状。
            var engine = Battle(150, "丙");
            engine.Cast("丙");
            Assert.That(engine.PlayerShield, Is.EqualTo(7), "护盾不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleHeal()
        {
            // PlayerHp 是 { get; private set; },不能用对象初始化器设 ——
            // 起始血量只能走构造参数 startingHp
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerAttack = 150, PlayerMaxHp = 100 },
                new[] { "丁" }, Array.Empty<string>(), new[] { Dummy() }, seed: 1,
                startingHp: 50);
            engine.Cast("丁");
            Assert.That(engine.PlayerHp, Is.EqualTo(59), "治疗不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleFreezeTurns()
        {
            var engine = Battle(150, "戊");
            engine.Cast("戊", 0);
            var freeze = engine.Enemies[0].Statuses.Find(StatusKind.Freeze);
            Assert.That(freeze, Is.Not.Null);
            Assert.That(freeze.TurnsLeft, Is.EqualTo(2), "回合数不吃攻击力");
        }

        // ---- 流血:施加时吃 ATK ----

        [Test]
        public void HigherAttack_ScalesBleedAtApplyTime()
        {
            var engine = Battle(150, "己");
            engine.Cast("己", 0);
            var bleed = engine.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed, Is.Not.Null);
            Assert.That(bleed.Magnitude, Is.EqualTo(6), "4 × 150 ÷ 100 = 6");
        }

        [Test]
        public void Bleed_DoesNotRetroactivelyScale()
        {
            // 出牌时快照:挂上之后再抬攻击力,已挂的流血不变。
            // 没有这条,把 ScaleByAttack 挪到流血结算处(实时读)也不会有测试红。
            var engine = Battle(BattleConfig.AttackBaseline, "己");
            engine.Cast("己", 0);
            engine.ApplyPlayerAttackBuff(100);
            var bleed = engine.Enemies[0].Statuses.Find(StatusKind.Bleed);
            Assert.That(bleed.Magnitude, Is.EqualTo(4), "已挂的流血不回溯");
        }

        // ---- 召唤物:创建时吃 ATK ----

        [Test]
        public void HigherAttack_ScalesSummonAttackAtBirth()
        {
            var engine = Battle(150, "庚");
            engine.Cast("庚");
            Assert.That(engine.Summons.Count, Is.EqualTo(1));
            Assert.That(engine.Summons[0].Attack, Is.EqualTo(9), "6 × 150 ÷ 100 = 9");
        }

        [Test]
        public void SummonHp_DoesNotScaleWithAttack()
        {
            // 召唤物的**血量**是防御资源,不吃攻击力;只有它的攻击力吃。
            var engine = Battle(150, "庚");
            engine.Cast("庚");
            Assert.That(engine.Summons[0].MaxHp, Is.EqualTo(20), "召唤物血量不吃攻击力");
        }

        [Test]
        public void ExistingSummon_DoesNotRetroactivelyScale()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "庚");
            engine.Cast("庚");
            engine.ApplyPlayerAttackBuff(100);
            Assert.That(engine.Summons[0].Attack, Is.EqualTo(6), "已在场的召唤物不回溯");
        }

        // ---- 快照 ----

        [Test]
        public void AttackBuff_SurvivesSnapshotRoundTrip()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "甲");
            engine.ApplyPlayerAttackBuff(50);
            var defs = new System.Collections.Generic.Dictionary<string, EnemyDef> { ["怔"] = Dummy() };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerAttack = BattleConfig.AttackBaseline, PlayerMaxHp = 100 },
                null, defs);
            Assert.That(restored.EffectiveAttack, Is.EqualTo(150),
                "局内增益存在 PlayerStatuses 里,快照本来就在存 —— 零新增字段");
        }

        // ---- 灼烧:结算时读,回溯生效(与炽同款口径)----

        [Test]
        public void BaselineAttack_LeavesBurnTickIdentical()
        {
            // 恒等性硬线在灼烧这条链上的对应物。
            // 3 层 × 每层 2 = 6,与引入攻击力之前一致
            var engine = Battle(BattleConfig.AttackBaseline, "辛");
            engine.Cast("辛", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 6));
        }

        [Test]
        public void HigherAttack_ScalesBurnTick()
        {
            var engine = Battle(150, "辛");
            engine.Cast("辛", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 9), "floor(3 × 2 × 1.5) = 9");
        }

        [Test]
        public void Burn_RetroactivelyScalesWithAttack()
        {
            // 与流血/召唤物**相反**:灼烧回溯。先挂满层再抬攻击力,已挂的层照样变强。
            // 这不是不一致,是沿用炽/BurnPotency 已确立的口径 —— 每层伤害本来就是
            // _burnPerStack 这个全局标量,从来不是出牌时冻结的量。
            var engine = Battle(BattleConfig.AttackBaseline, "辛");
            engine.Cast("辛", 0);
            engine.ApplyPlayerAttackBuff(50);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 9), "已挂的层吃新攻击力");
        }

        [Test]
        public void BurnStackCount_DoesNotScaleWithAttack()
        {
            // 只放大每层伤害,不放大层数(spec 第三节)。
            // 层数被放大的话总伤害会按 N(N+1)/2 平方级膨胀,那是另一回事。
            var engine = Battle(150, "辛");
            engine.Cast("辛", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn),
                Is.EqualTo(3), "层数不吃攻击力");
        }

        [Test]
        public void HigherAttack_ScalesDetonate()
        {
            // 引爆 = N(N+1)/2 × 每层伤害,同口径吃攻击力。
            // 3 层:3×4/2 = 6 → 6 × 2 = 12 → ×1.5 = 18
            var engine = Battle(150, "辛", "壬");
            engine.Cast("辛", 0);
            engine.Cast("壬", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 18));
        }

        // ---- 负向补齐(E-b1 终审 M1):增益/控制类的量一律不吃 ATK ----

        [Test]
        public void HighAttack_DoesNotScaleBurnPotency()
        {
            // 这条是负向里最要紧的一条。炽抬的是 _burnPerStack 这个**乘数**,
            // 而灼烧结算已经在乘 ATK 了 —— 给 `_burnPerStack += value` 也套上 ScaleByAttack,
            // 火系爆发就变成 ATK 的平方级增长,而现有 819 条断言一条都不会红。
            var baseline = Battle(BattleConfig.AttackBaseline, "癸");
            baseline.Cast("癸");
            var buffed = Battle(150, "癸");
            buffed.Cast("癸");
            Assert.That(buffed.Capture().BurnPerStack, Is.EqualTo(2 + 4),
                "炽的每层加成是乘数不是输出,不吃攻击力");
            Assert.That(buffed.Capture().BurnPerStack,
                Is.EqualTo(baseline.Capture().BurnPerStack), "与 ATK=100 时完全相同");
        }

        [Test]
        public void BurnPotency_DoesNotCompoundWithAttackOnBurnTick()
        {
            // 上一条的端到端对照:炽 +4 → 每层 6,3 层在 ATK=150 下结算。
            // floor(3 × 6 × 1.5) = 27 是**线性**结果;若炽也吃了 ATK(每层变 2+6=8),
            // 这里会是 floor(3 × 8 × 1.5) = 36 —— 差额就是那一层平方。
            var engine = Battle(150, "癸", "辛");
            engine.Cast("癸");
            engine.Cast("辛", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 27), "只乘一次 ATK,不平方");
        }

        [Test]
        public void HighAttack_DoesNotScaleReflect()
        {
            // 反弹是百分比,乘 ATK 等于把「照原样反射」变成「反射得比挨的还多」。
            var engine = Battle(150, "子");
            engine.Cast("子");
            var reflect = engine.PlayerStatuses.Find(StatusKind.Reflect);
            Assert.That(reflect, Is.Not.Null);
            Assert.That(reflect.Magnitude, Is.EqualTo(30), "反弹百分比不吃攻击力");
            Assert.That(reflect.TurnsLeft, Is.EqualTo(2), "持续回合数不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleImmunityCharges()
        {
            // 免疫的 Magnitude 是**次数**,不是量 —— 乘 ATK 就是凭空发稀缺资源。
            var engine = Battle(150, "丑");
            engine.Cast("丑");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity),
                Is.EqualTo(3), "免疫次数不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleHealOverTime()
        {
            // 与 HealSelf 同理:治疗是防御资源。HoT 每回合量走生克但不走 ATK。
            var engine = Battle(150, "寅");
            engine.Cast("寅");
            var hot = engine.PlayerStatuses.Find(StatusKind.HealOverTime);
            Assert.That(hot, Is.Not.Null);
            Assert.That(hot.Magnitude, Is.EqualTo(8), "持续治疗每回合量不吃攻击力");
            Assert.That(hot.TurnsLeft, Is.EqualTo(3), "持续回合数不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleSlowAndSilenceTurns()
        {
            // 控制时长是「节奏」,不是「资源」—— 与召唤被动同口径,不随任何成长轴变长。
            var engine = Battle(150, "卯", "巳");
            engine.Cast("卯", 0);
            engine.Cast("巳", 0);
            var slow = engine.Enemies[0].Statuses.Find(StatusKind.SpeedModifier);
            Assert.That(slow, Is.Not.Null);
            Assert.That(slow.TurnsLeft, Is.EqualTo(4), "减速回合数不吃攻击力");
            Assert.That(slow.Magnitude, Is.EqualTo(-50), "减速幅度是常量,不吃攻击力");
            var silence = engine.Enemies[0].Statuses.Find(StatusKind.Silence);
            Assert.That(silence, Is.Not.Null);
            Assert.That(silence.TurnsLeft, Is.EqualTo(3), "沉默回合数不吃攻击力");
        }

        [Test]
        public void HighAttack_DoesNotScaleBlind()
        {
            var engine = Battle(150, "辰");
            engine.Cast("辰", 0);
            var blind = engine.Enemies[0].Statuses.Find(StatusKind.Blind);
            Assert.That(blind, Is.Not.Null);
            Assert.That(blind.Magnitude, Is.EqualTo(40), "命中惩罚百分比不吃攻击力");
            Assert.That(blind.TurnsLeft, Is.EqualTo(2), "致盲回合数不吃攻击力");
        }

        // ---- 钳位(E-b1 终审 M2):EffectiveAttack 的 Math.Max(0, ...) ----

        [Test]
        public void DeepNegativeAttackBuff_ClampsEffectiveAttackToZeroNotNegative()
        {
            // Math.Max(0, ...) 此前零覆盖。去掉它:EffectiveAttack = 100 − 500 = −400,
            // ScaleByAttack(20) = −80,DamageEnemy 收到负数 → 敌人**回血**,而且全程无声。
            var engine = Battle(BattleConfig.AttackBaseline, "甲");
            engine.ApplyPlayerAttackBuff(-500);
            Assert.That(engine.EffectiveAttack, Is.EqualTo(0), "攻击力钳到 0,不会为负");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500), "伤害为 0,不是负数(不给敌人回血)");
        }
    }
}
