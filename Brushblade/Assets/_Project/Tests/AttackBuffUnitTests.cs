using System;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>AttackBuff 的单位统一(E-b4+E-b5 的 T0.5,2026-08-12)。
    ///
    /// 改动前 <see cref="StatusKind.AttackBuff"/> 在敌我两侧是**两种单位**:敌人侧是
    /// 伤害点数的加数、玩家侧是以 100 为基准的百分点。T1 要把全局数值量级 ×10,
    /// 「数量乘、比值不乘」这条规则就得带一条「敌人的 AttackBuff 乘、玩家的不乘」的特例
    /// —— 特例会被后人忘掉。统一成百分点后规则零例外。
    ///
    /// 本组测试守三件事:
    /// 1. 焦痕的**零行为变化**(它自己 BaseAttack 恰好是 4,50% × 4 = 2,逐位等价);
    /// 2. 标点小妖改成按比例后的新行为;
    /// 3. 新算式的两条纪律 —— 钳最终值不钳单项、整数算式不掺浮点。</summary>
    public sealed class AttackBuffUnitTests
    {
        // ---- 1. 焦痕:零行为变化 ----

        [Test]
        public void Scorch_AttackSequence_IsUnchangedByThePercentRewrite()
        {
            // 改动前 ScorchGain = 2(点),改动后 = 50(%),而焦痕 BaseAttack = 4:
            //   旧:4 + 2n      新:4 × (100 + 50n) ÷ 100 = 4 + 2n     对任意 n 逐位相同。
            // 这条是整次改动的自检器 —— 它红了就说明百分比换算写错了,不许改断言迁就。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("击", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 5) }),
            });
            var engine = new BattleEngine(graph, new BattleConfig { DropTable = new[] { "木" } },
                new[] { "击", "击", "击" }, Array.Empty<string>(),
                new[] { new EnemyDef("焦痕", Element.Fire, 100, 4, EnemyAbility.Scorch) }, seed: 1);

            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(4), "n = 0");
            engine.Cast("击");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6), "n = 1");
            engine.Cast("击");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(8), "n = 2");
            engine.Cast("击");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(10), "n = 3");
        }

        // ---- 2. 标点小妖:按比例而非固定值 ----

        private static BattleEngine BufferAgainst(params EnemyDef[] targets)
        {
            var graph = new RecipeGraph(new[] { new CharDef("木", Element.Wood) });
            var all = new EnemyDef[targets.Length + 1];
            all[0] = new EnemyDef("标点小妖", Element.Heart, 8, 2, EnemyAbility.Buff);
            Array.Copy(targets, 0, all, 1, targets.Length);
            // 血厚到打不死:本组只看攻击力,别让玩家被打死提前结算
            return new BattleEngine(graph, new BattleConfig { PlayerMaxHp = 999 },
                Array.Empty<string>(), Array.Empty<string>(), all, seed: 1);
        }

        [Test]
        public void PunctuationBuff_ScalesWithTargetBaseAttack_NotAFlatAmount()
        {
            // 改动前 Magnitude = 施加者自身攻击力 = 固定 +2,对攻 2 的怪是 +100%、
            // 对攻 8 的怪只有 +25%,同一个能力有 4 倍偏差。改成固定 +50% 后两边同比例。
            var engine = BufferAgainst(
                new EnemyDef("小", Element.Heart, 99, 2),
                new EnemyDef("大", Element.Heart, 99, 8));

            engine.EndTurn();

            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(3), "2 × 150 ÷ 100 = 3");
            Assert.That(engine.Enemies[2].Attack, Is.EqualTo(12), "8 × 150 ÷ 100 = 12");
        }

        [Test]
        public void PunctuationBuff_EventAmountReportsThePercent()
        {
            // 事件 amount 是表现层飘的数字;它若还带着旧的「点数」语义,飘出来的数就与实际效果对不上。
            var engine = BufferAgainst(new EnemyDef("大", Element.Heart, 99, 8));

            engine.EndTurn();

            foreach (var e in engine.LastEvents)
                if (e.Kind == BattleEventKind.EnemyBuff)
                    Assert.That(e.Amount, Is.EqualTo(50), "百分点,不是施加者的攻击力");
        }

        [Test]
        public void PunctuationBuff_StacksAdditivelyOnThePercentAxis()
        {
            var engine = BufferAgainst(new EnemyDef("大", Element.Heart, 99, 8));

            engine.EndTurn();
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(12), "8 × 150 ÷ 100");
            engine.EndTurn();
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(16), "两层:8 × 200 ÷ 100,百分点相加不叠乘");
        }

        // ---- 3. 新算式:同轴相消 / 钳最终值 / 整数算式 ----

        private static EnemyState Target(int baseAttack)
        {
            var graph = new RecipeGraph(new[] { new CharDef("木", Element.Wood) });
            var engine = new BattleEngine(graph, new BattleConfig(),
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 50, baseAttack) }, seed: 1);
            return engine.Enemies[0];
        }

        private static EnemyState Target(int baseAttack, int buffPercent, int cursePercent)
        {
            var enemy = Target(baseAttack);
            if (buffPercent != 0)
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = buffPercent, TurnsLeft = -1, SourceId = "妖#1",
                });
            if (cursePercent != 0)
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff,
                    Magnitude = cursePercent, TurnsLeft = 2, SourceId = "诅咒",
                });
            return enemy;
        }

        [Test]
        public void BuffAndCurse_ShareOneAxis_AndCancelExactly()
        {
            // 刻意的语义变化(2026-08-12):改动前是乘法交互 (base + buff) × (100 − curse) ÷ 100,
            // +4 点与 −50% 不互消;现在两者是同一根百分点轴上的加减,+50% 与 −50% 精确抵消。
            Assert.That(Target(8, 50, 50).Attack, Is.EqualTo(8), "+50% 与 −50% 回到基础攻击");
            Assert.That(Target(7, 50, 50).Attack, Is.EqualTo(7), "不整除的基础值同样精确回原");
        }

        [Test]
        public void Clamp_IsOnTheFinalPercent_NotOnCurseAlone()
        {
            // 与 BattleEngine.AttackHits 的钳位同型(那条 2026-08-08 订正过):**钳最终值,不钳单项**。
            // 单项钳(旧写法 Math.Min(100, curse))会把 curse 120 先削成 100,
            // 于是 +50% − 120% 被算成 +50% 的净增益 —— 诅咒越重反而越划算。
            Assert.That(Target(8, 50, 120).Attack, Is.EqualTo(2), "100 + 50 − 120 = 30 → 8 × 30 ÷ 100 = 2");
            Assert.That(Target(8, 0, 250).Attack, Is.EqualTo(0), "净百分比转负,下钳到 0 而不是负攻击");
            Assert.That(Target(8, 20, 250).Attack, Is.EqualTo(0), "增益抵不过时同样是 0");
        }

        [Test]
        public void IntegerMath_HasNoFloatPrecisionLoss()
        {
            // 10% / 30% 是浮点写法的照妖镜:1 − 0.1f = 0.89999997,10 × 它 floor 到 8 而不是 9
            // (2026-08-06 M1 的既有纪律,统一算式后继续守)。25% 二进制精确,测不出来。
            Assert.That(Target(10, 0, 10).Attack, Is.EqualTo(9), "10 × 90 ÷ 100 = 9");
            Assert.That(Target(10, 0, 30).Attack, Is.EqualTo(7), "10 × 70 ÷ 100 = 7");
            Assert.That(Target(10, 10, 20).Attack, Is.EqualTo(9), "净 −10%,走加减轴同样精确");
        }

        // ---- 4. 玩家侧一行未变 ----

        [Test]
        public void PlayerSide_Empower_IsUntouchedByTheEnemySideUnification()
        {
            // 剡的 Empower 50 本来就是百分点(基准 100),敌人侧统一到同一单位后它一行都不用改。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("剡", Element.Heart, effects: new[] { new EffectDef(EffectKind.Empower, 50) }),
                new CharDef("甲", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { PlayerAttack = BattleConfig.AttackBaseline, PlayerMaxHp = 100 },
                new[] { "剡", "甲" }, Array.Empty<string>(),
                new[] { new EnemyDef("怔", Element.Heart, 500, 0) }, seed: 1);

            engine.Cast("剡");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(150), "100 + 50");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "20 × 150 ÷ 100 = 30");
        }
    }
}
