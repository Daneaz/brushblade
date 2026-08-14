using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.CoreTests
{
    /// <summary>BUFF 组 A(E-b3-a,2026-08-12):剡 / 战 / 戮 / 利 四字的三个新效果
    /// —— Empower(本场攻击)、Morale(战意层数)、ApBoost(每回合 AP 上限)。
    ///
    /// 测试字一律用 <see cref="Element.Heart"/> 且不给配方,理由同 AttackStatTests:
    /// 心对全属性生克都是 1.0x,没有配方就不会触发相生 ×3 —— 断言里看到的数字
    /// 就是增益本身,不掺生克。</summary>
    public sealed class BuffCharTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            // 四个正式字的真实配置(数值与《技能机制详表》金系 BUFF 表一致)
            new CharDef("剡", Element.Heart, rarity: CardRarity.Gold,
                effects: new[] { new EffectDef(EffectKind.Empower, 50) }),
            new CharDef("战", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Morale, 3) }),
            new CharDef("戮", Element.Heart, rarity: CardRarity.Green,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 9),
                                 new EffectDef(EffectKind.Morale, 1) }),
            new CharDef("利", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ApBoost, 1) }),
            // 辅助字:验缩放/回溯用,不对应任何正式字
            new CharDef("甲", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
            new CharDef("辛", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 3) }),
            // ApBoost 1 在 ATK=150 下整数除仍是 1,负向断言看不出差别 —— 用 2 才能
            // 把「误套 ScaleByAttack」(2 × 150 ÷ 100 = 3)与正确值区分开
            new CharDef("壬", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.ApBoost, 2) }),
        });

        private static EnemyDef Dummy(int hp = 500) => new("怔", Element.Heart, hp, 0);

        private static BattleEngine Battle(int attack, params string[] library) =>
            new(Graph(), new BattleConfig { PlayerAttack = attack, PlayerMaxHp = 100 },
                library, Array.Empty<string>(), new[] { Dummy() }, seed: 1);

        // ---- 剡:Empower ----

        [Test]
        public void Yan_RaisesEffectiveAttackByFifty()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "剡", "甲");
            engine.Cast("剡");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(150), "100 + 50");
            engine.Cast("甲", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 30), "20 × 150 ÷ 100 = 30");
        }

        [Test]
        public void Yan_StacksWhenCastTwice()
        {
            // SourceId 铸唯一序号才能叠(StatusEffect.SourceId 的用法 2);误传裸字 ID
            // 会让第二张剡覆盖第一张,静默退化成「刷新」而不是「叠加」。
            var engine = Battle(BattleConfig.AttackBaseline, "剡", "剡");
            engine.Cast("剡");
            engine.Cast("剡");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(200), "两张剡叠加,不是刷新");
        }

        [Test]
        public void Yan_RetroactivelyScalesExistingBurn()
        {
            // 已知且已接受的代价(E-b1 定的口径):灼烧/引爆在**结算时**读 ATK,
            // 于是先挂满层、再出剡,已经在场的灼烧会跟着变强。
            // 这不是 bug —— 每层伤害本来就是 _burnPerStack 这个全局标量,
            // 从来不是出牌时冻结的量(与炽 / BurnPotency 同口径)。
            // 写成测试是为了不让后人把它当 bug「修」掉。
            var engine = Battle(BattleConfig.AttackBaseline, "辛", "剡");
            engine.Cast("辛", 0);
            engine.Cast("剡");
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 90),
                "floor(3 层 × 每层 20 × 1.5) = 90,已挂的层吃新攻击力");
        }

        [Test]
        public void Yan_EmpowerValue_DoesNotScaleWithAttack()
        {
            // 负向:Empower 的 50 是「加多少攻击力」,不是输出 —— 套上 ScaleByAttack
            // 会变成 50 × 150 ÷ 100 = 75,攻击力自我放大,成长曲线直接指数化。
            var engine = Battle(150, "剡");
            engine.Cast("剡");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.AttackBuff),
                Is.EqualTo(50), "加成量本身不吃攻击力");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(200), "150 + 50");
        }

        // ---- 战:Morale ----

        [Test]
        public void Zhan_GrantsThreeMoraleStacks()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "战");
            engine.Cast("战");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(3));
            Assert.That(engine.EffectiveAttack, Is.EqualTo(130), "100 + 3 层 × 每层 10");
        }

        [Test]
        public void Morale_ClampsAtFiveStacks()
        {
            // 3 + 3 = 6 → 钳到 5。没有钳位这里会是 160,战意就没有上限了 ——
            // 上限是它与剡(一次性 +50)保持平衡的唯一约束。
            var engine = Battle(BattleConfig.AttackBaseline, "战", "战");
            engine.Cast("战");
            engine.Cast("战");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(5));
            Assert.That(engine.EffectiveAttack, Is.EqualTo(150), "5 层封顶 = +50,不是 +60");
        }

        [Test]
        public void Morale_DecaysOneStackPerTurnEnd()
        {
            // 2026-08-15 拍板:战意从「本场持久」改为**每回合末消减一层**。
            // 当回合出的 战 先按 3 层生效(EffectiveAttack 130),回合末才掉到 2 ——
            // 递减排在本回合全部结算之后,不是「刚施加就少一层」。
            var engine = Battle(BattleConfig.AttackBaseline, "战");
            engine.Cast("战");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(130), "出牌当回合按 3 层算");

            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(2));
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(1));
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(0),
                "归零后整条状态移除,不留 0 层的空壳");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Morale), Is.False);
            Assert.That(engine.EffectiveAttack, Is.EqualTo(BattleConfig.AttackBaseline));
        }

        [Test]
        public void Morale_OtherBuffsStayPermanent()
        {
            // 负向:只有战意衰减。同为「本场持久」的 剡(Empower)不该被顺手削掉 ——
            // 把衰减写进 TickTurns 或对整袋 Buff 生效,这条会红。
            var engine = Battle(BattleConfig.AttackBaseline, "剡");
            engine.Cast("剡");
            int before = engine.EffectiveAttack;
            engine.EndTurn();
            engine.EndTurn();
            Assert.That(engine.EffectiveAttack, Is.EqualTo(before), "Empower 本场持久,不随回合衰减");
        }

        [Test]
        public void MoraleStacks_DoNotScaleWithAttack()
        {
            // 负向:战意的 Magnitude 是**层数**不是量。套上 ScaleByAttack 会算成
            // 3 × 150 ÷ 100 = 4 层,攻击力越高战意给得越多 —— 又一条自我放大。
            var engine = Battle(150, "战");
            engine.Cast("战");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale),
                Is.EqualTo(3), "层数不吃攻击力");
            Assert.That(engine.EffectiveAttack, Is.EqualTo(180), "150 + 3 × 10");
        }

        // ---- 戮:伤害 + 战意 ----

        [Test]
        public void Lu_DealsDamageAndGrantsOneMoraleStack()
        {
            // 两个效果都要断:只断伤害的话,漏掉 Morale 那条也是绿的。
            var engine = Battle(BattleConfig.AttackBaseline, "戮");
            engine.Cast("戮", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 9));
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(1));
            Assert.That(engine.EffectiveAttack, Is.EqualTo(110));
        }

        [Test]
        public void Lu_AccumulatesOntoZhanStacks()
        {
            // 战 3 + 戮 1 = 4:两个不同的字往同一条战意计数器上加,
            // 而不是各挂各的(各挂各的会绕开上限)。
            var engine = Battle(BattleConfig.AttackBaseline, "战", "戮");
            engine.Cast("战");
            engine.Cast("戮", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(4));
            Assert.That(engine.EffectiveAttack, Is.EqualTo(140));
        }

        [Test]
        public void Lu_AtFullMorale_DoesNotOverflow()
        {
            // 满层时再出戮:层数不动(不溢出到 6),但伤害照常打出。
            var engine = Battle(BattleConfig.AttackBaseline, "战", "战", "戮");
            engine.Cast("战");
            engine.Cast("战");
            engine.Cast("戮", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(5));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(500 - 13), "9 × 150 ÷ 100 = 13");
        }

        // ---- 利:ApBoost(逻辑侧 / 容量上限侧 / UI 显示侧三侧)----

        [Test]
        public void Li_RaisesBothStartTurnApAndApPerTurn()
        {
            // 三侧同断(仓库既有教训:增益只做一半会出现「UI 画 3 格但实际有 4 AP」)。
            // ApPerTurn 是 UI 满格数与提示文案的来源,只改 StartTurn 这条会红。
            var engine = Battle(BattleConfig.AttackBaseline, "利");
            engine.Cast("利");
            Assert.That(engine.ApPerTurn, Is.EqualTo(4), "UI 满格数当场就跟着涨");
            engine.EndTurn();
            Assert.That(engine.Ap, Is.EqualTo(4), "下回合真的多一点 AP");
            Assert.That(engine.ApPerTurn, Is.EqualTo(4));
        }

        [Test]
        public void Li_CastTwice_StacksApBoost()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "利", "利");
            engine.Cast("利");
            engine.Cast("利");
            Assert.That(engine.ApPerTurn, Is.EqualTo(5), "3 + 1 + 1");
            engine.EndTurn();
            Assert.That(engine.Ap, Is.EqualTo(5));
        }

        [Test]
        public void Li_WithSeal_AddsBoostBeforeSubtractingSeal()
        {
            // 封字 2 + ApBoost 1:Math.Max(1, 3 + 1 − 2) = 2。
            // 顺序反了(先钳后加)会变成 Math.Max(1, 3 − 2) + 1 = 2 —— 这一组数字碰巧相同,
            // 所以额外断了封字 5 那组:Math.Max(1, 3 + 1 − 5) = 1,先钳后加会得 2。
            var seal = new List<StatusEffect>
            {
                new() { Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                        Magnitude = 2, TurnsLeft = -1 },
            };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerAttack = BattleConfig.AttackBaseline, PlayerMaxHp = 100 },
                new[] { "利" }, Array.Empty<string>(), new[] { Dummy() }, seed: 1,
                startingStatuses: seal);
            Assert.That(engine.Ap, Is.EqualTo(1), "开局:Math.Max(1, 3 − 2) = 1");
            engine.Cast("利");
            engine.EndTurn();
            Assert.That(engine.Ap, Is.EqualTo(2), "Math.Max(1, 3 + 1 − 2) = 2");
        }

        [Test]
        public void Li_WithHeavySeal_StillFloorsAtOne()
        {
            var seal = new List<StatusEffect>
            {
                new() { Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                        Magnitude = 5, TurnsLeft = -1 },
            };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { PlayerAttack = BattleConfig.AttackBaseline, PlayerMaxHp = 100 },
                new[] { "利" }, Array.Empty<string>(), new[] { Dummy() }, seed: 1,
                startingStatuses: seal);
            engine.Cast("利");
            engine.EndTurn();
            Assert.That(engine.Ap, Is.EqualTo(1), "Math.Max(1, 3 + 1 − 5) = 1,保底不变");
        }

        [Test]
        public void ApBoost_DoesNotScaleWithAttack()
        {
            // 负向:AP 是经济资源,与攻击力无关。壬 = ApBoost 2 —— 用 1 的话
            // 整数除下 1 × 150 ÷ 100 仍是 1,误套 ScaleByAttack 也测不出来。
            var engine = Battle(150, "壬");
            engine.Cast("壬");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.ApBoost),
                Is.EqualTo(2), "AP 上限加成不吃攻击力");
            Assert.That(engine.ApPerTurn, Is.EqualTo(5));
        }

        // ---- 快照:目标是零新增字段 ----

        [Test]
        public void MoraleAndApBoost_SurviveSnapshotRoundTrip()
        {
            var engine = Battle(BattleConfig.AttackBaseline, "战", "利");
            engine.Cast("战");
            engine.Cast("利");
            var defs = new Dictionary<string, EnemyDef> { ["怔"] = Dummy() };
            var restored = BattleEngine.Restore(engine.Capture(), Graph(),
                new BattleConfig { PlayerAttack = BattleConfig.AttackBaseline, PlayerMaxHp = 100 },
                null, defs);
            Assert.That(restored.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(3),
                "战意存在 PlayerStatuses 里,快照本来就在存 —— 零新增字段");
            Assert.That(restored.EffectiveAttack, Is.EqualTo(130));
            Assert.That(restored.ApPerTurn, Is.EqualTo(4), "ApBoost 同理");
        }
    }
}
