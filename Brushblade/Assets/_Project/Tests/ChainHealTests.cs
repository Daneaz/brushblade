using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>治疗弹射(2026-09-16,水,海/澡对偶攻面「弹射」的那一条)。HealSelf 配
    /// <see cref="TargetShape.Chain"/>:主目标满额治疗,再弹给至多 Shots-1 个 HP 不满的我方
    /// 召唤物,各按 ShapePercent 打一次折(不像伤害弹射那样逐跳累乘衰减)。落点按槽位升序、
    /// 不摇随机数——同种子同结果。</summary>
    public class ChainHealTests
    {
        /// <summary>召:召 1 只 100 血、攻 0 的召唤物(攻 0 = 绝不反击);巨:召 1 只 300 血,
        /// 用来造「主目标本身也没满」的夹具。海:HealSelf 100,Chain 3 跳,ShapePercent 50 ——
        /// 与设计稿海/澡对偶的攻面同一组形状参数。全用 Element.Heart(心中立、全 1.0x),
        /// 断言不受生克干扰。</summary>
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("召", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 0) }),
            new CharDef("巨", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 300, summonCount: 1, summonAttack: 0) }),
            new CharDef("海", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 100,
                    shape: TargetShape.Chain, shots: 3, shapePercent: 50) }),
        });

        /// <summary>攻 0 的靶子:敌人不还手,血量变化只可能来自玩家出字。</summary>
        private static EnemyDef Dummy() => new("靶", Element.Heart, 200, 0);

        /// <summary>PlayerMaxHp 抬到 200、startingHp 定在 100(⚠ 不能取 0——PlayerHp ≤ 0 会被判定
        /// Phase = Lost,后续 Cast 一律吃 BattleOver):「海」满额治疗 +100 后正好落在 200(满血,
        /// 不裁切也不早死)。ApPerTurn 抬到 6(一回合内够铺 3 只召唤物 + 1 次治疗)。</summary>
        private static BattleEngine Engine(int seed = 1) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = 200, ApPerTurn = 6 },
                System.Array.Empty<string>(),
                new[] { "召", "召", "召", "巨", "海" },
                new[] { Dummy() }, seed: seed, startingHp: 100);

        [Test]
        public void ChainHeal_HealsMainTargetFull_ThenBouncesAtPercent()
        {
            var engine = Engine();
            engine.Cast("召", summonSlots: new[] { 0 });
            engine.Cast("召", summonSlots: new[] { 1 });
            engine.Summons[0].Hp = 50;
            engine.Summons[1].Hp = 50;

            Assert.That(engine.Cast("海"), Is.EqualTo(BattleError.None));

            Assert.That(engine.PlayerHp, Is.EqualTo(200), "主目标(玩家)满额治疗(100→200,满血)");
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(100), "槽 0 按 50% 弹射,50 → 100");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(100), "槽 1 同上");
        }

        [Test]
        public void ChainHeal_SkipsFullHpSummons()
        {
            var engine = Engine();
            engine.Cast("召", summonSlots: new[] { 0 }); // 满血,不该被弹到
            engine.Cast("召", summonSlots: new[] { 1 });
            engine.Summons[1].Hp = 50;

            engine.Cast("海");

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(100), "满血的不参与弹射,一分不多回");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(100), "唯一 HP 不满的召唤物吃到弹射");
        }

        [Test]
        public void ChainHeal_BouncesFewerTimes_WhenNotEnoughTargets()
        {
            var engine = Engine();
            engine.Cast("召", summonSlots: new[] { 0 });
            engine.Summons[0].Hp = 50;

            Assert.DoesNotThrow(() => engine.Cast("海"));

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(100), "只有 1 个候选,少弹一下也不报错");
        }

        [Test]
        public void ChainHeal_BounceOrder_IsAscendingBySlot_NotRandom()
        {
            // shots 3 → 至多 2 跳;槽 0、1、2 都不满,应恰好按升序取槽 0、1,槽 2 跳数用完落空。
            var engine = Engine();
            engine.Cast("召", summonSlots: new[] { 0 });
            engine.Cast("召", summonSlots: new[] { 1 });
            engine.Cast("召", summonSlots: new[] { 2 });
            engine.Summons[0].Hp = 50;
            engine.Summons[1].Hp = 50;
            engine.Summons[2].Hp = 50;

            engine.Cast("海");

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(100), "升序取第一个");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(100), "升序取第二个");
            Assert.That(engine.Summons[2].Hp, Is.EqualTo(50), "跳数已用完,槽 2 落空——不是不确定的随机结果");
        }

        [Test]
        public void ChainHeal_EachBounceAccumulatesWellspring()
        {
            // 主 100(名义)+ 弹 50 + 弹 50 = 200 名义值,阈值恰好 200 → 1 层、余数清零。
            // 若弹射两跳没有各自攒泉,总名义值只有 100,凑不满一层——这条测试能吃出漏记账。
            var engine = Engine();
            engine.Cast("召", summonSlots: new[] { 0 });
            engine.Cast("召", summonSlots: new[] { 1 });
            engine.Summons[0].Hp = 50;
            engine.Summons[1].Hp = 50;

            engine.Cast("海");

            Assert.That(engine.WellspringStacks, Is.EqualTo(1), "主 100 + 两跳各 50 = 200,凑满一层阈值");
            Assert.That(engine.HealAccum, Is.EqualTo(0), "满层后余数清零(GainStacks 既有口径)");
        }

        [Test]
        public void ChainHeal_ExcludesMainTargetFromBounceCandidates()
        {
            // 「巨」maxHp 300,主治疗 +100 后自己仍未满 —— 弹射不能把主目标自己也算进候选,
            // 否则它会在满额之外又吃一次弹射折扣,而别的 HP 不满的召唤物反而被落下。
            var engine = Engine();
            engine.Cast("巨", summonSlots: new[] { 0 });
            engine.Summons[0].Hp = 50;
            engine.Cast("召", summonSlots: new[] { 1 });
            engine.Summons[1].Hp = 50;

            Assert.That(engine.Cast("海", allySlot: 0), Is.EqualTo(BattleError.None));

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(150), "主目标只吃主治疗那一份(+100)");
            Assert.That(engine.Summons[1].Hp, Is.EqualTo(100), "弹射落到另一个 HP 不满的召唤物身上");
        }
    }
}
