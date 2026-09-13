using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火脉 L2「余烬」(spec 2026-09-13 §2.2):敌人**以任何方式**死亡时,
    /// 剩余灼烧层数转给一名随机存活敌人。
    ///
    /// 钩子挂在 ResolveDefeat —— 灼烧致死、流血致死、引爆致死、斩杀、普通伤害致死
    /// 五条路径唯一的汇流点。这里逐条守。
    ///
    /// ⚠ 引爆(Detonate)在调 ResolveDefeat **之前**已经清空了灼烧层数,所以引爆致死
    /// 不蔓延。那是**正确**的:那些层数的伤害已经一次性兑现过了,再蔓延等于收两遍钱。</summary>
    public class BurnSpreadTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("燃", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 5) }),
            new CharDef("刺", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("灱", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Detonate, 0) }),
        });

        /// <summary>脆皮敌人 ×n:一发「刺」就能打死,便于把死亡这件事单独摘出来测。</summary>
        private static BattleEngine Engine(int spreadPercent, int enemyCount = 2, int enemyHp = 10) =>
            new(Graph(), new BattleConfig
                {
                    DropTable = new[] { "火" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    BurnSpreadPercent = spreadPercent,
                },
                new[] { "燃", "燃", "刺", "刺", "灱" }, Array.Empty<string>(),
                Enumerable.Range(0, enemyCount)
                    .Select(i => new EnemyDef($"靶{i}", Element.Heart, enemyHp, 0))
                    .ToArray(),
                seed: 1);

        private static int BurnOn(BattleEngine engine, int index) =>
            engine.Enemies[index].Statuses.TotalMagnitude(StatusKind.Burn);

        [Test]
        public void KilledByPlayerDamage_SpreadsRemainingBurn()
        {
            var engine = Engine(100);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(BurnOn(engine, 0), Is.EqualTo(5), "前提:0 号身上 5 层");
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:0 号被打死");
            Assert.That(BurnOn(engine, 1), Is.EqualTo(5), "5 层全额转给唯一的存活者");
        }

        [Test]
        public void KilledByPlayerDamage_ClearsBurnOffTheCorpse()
        {
            // 转移不是复制(2026-09-13 review):死者身上不该再留一份灼烧数据
            var engine = Engine(100);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:0 号被打死");
            Assert.That(BurnOn(engine, 0), Is.EqualTo(0), "尸体上不该留着死数据");
            Assert.That(BurnOn(engine, 1), Is.EqualTo(5), "接手的那份不受影响");
        }

        [Test]
        public void PerkOff_BurnJustDisappears()
        {
            var engine = Engine(0);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(BurnOn(engine, 1), Is.EqualTo(0), "未点亮,层数随尸体一起消失");
        }

        [Test]
        public void PerkOff_DoesNotConsumeRandomness()
        {
            // 恒等性硬线:关掉时一次随机都不许摇。断 RandomState 而不是数掉字 ——
            // 出牌本身会改库存,数掉字断不住。
            //
            // ⚠ enemyCount 必须 ≥ 3:GameRandom.Next(max) 在 max ≤ 1 时短路返回 0、
            // 不消耗随机状态(GameRandomTests 有专门守卫)。enemyCount = 2 时打死 0 号
            // 只剩 1 个存活,PickRandomLivingEnemy 里的 Next(1) 本来就不摇随机——开关
            // 关闭与打开都不消耗,这条测试会恒真、测不出任何东西。enemyCount = 3 时打死
            // 一个还剩 2 个存活,Next(2) 才会真正推进随机流,才守得住「关闭时一次都不摇」。
            //
            // (玩家攻击不走 AttackHits、PlayerCritChance 缺省 0 时 RollCritWith 短路,
            //  所以「燃 + 刺 + 敌人死亡」这一串在关闭状态下本该零消耗。)
            var engine = Engine(0, enemyCount: 3);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            uint before = engine.Capture().RandomState;
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:确实死了");
            Assert.That(engine.Capture().RandomState, Is.EqualTo(before),
                "未点亮时敌人死亡不该消耗随机数");
        }

        [Test]
        public void PerkOn_ConsumesADrawToPickTheTarget()
        {
            // PerkOff_DoesNotConsumeRandomness 的对照组:同样 3 个敌人(死后剩 2 个存活,
            // Next(2) 不会短路),开关打开时敌人死亡必须真的摇一次随机去挑蔓延目标。
            var engine = Engine(100, enemyCount: 3);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            uint before = engine.Capture().RandomState;
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:确实死了");
            Assert.That(engine.Capture().RandomState, Is.Not.EqualTo(before),
                "点亮时敌人死亡要摇一次随机去挑蔓延目标");
        }

        [Test]
        public void SpreadStacksOntoWhatTheTargetAlreadyHas()
        {
            var engine = Engine(100);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("燃", 1), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(BurnOn(engine, 1), Is.EqualTo(10), "自己的 5 层 + 转移来的 5 层");
        }

        [Test]
        public void NoOtherLivingEnemy_SilentlyDropsTheStacks()
        {
            var engine = Engine(100, enemyCount: 1);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("刺", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "场上没有别人可以接手");
            // 不抛异常就是通过;顺带确认战斗已收口
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void DetonateKill_DoesNotSpread()
        {
            // 引爆先清空层数再判死 —— 那些伤害已经兑现,不该再蔓延
            var engine = Engine(100, enemyHp: 10);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("灱", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:引爆把它炸死了");
            Assert.That(BurnOn(engine, 1), Is.EqualTo(0), "引爆致死不蔓延");
        }

        [Test]
        public void BurnTickKill_SpreadsWhatIsLeftAfterTheTickDecrement()
        {
            // 灼烧结算先 Magnitude -= 1 再判死,所以蔓延出去的是**已减 1 之后**的层数
            var engine = Engine(100, enemyHp: 10);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(BurnOn(engine, 0), Is.EqualTo(5));
            // 用 AdvanceOnce() 而不是 EndTurn():enemyCount 默认 2,EndTurn() 会把本轮
            // 所有敌人的回合都跑完——0 号死后把 4 层转给 1 号,1 号若在同一轮还没轮到自己的
            // SettleBurnOn,就会紧接着被这刚转来的 4 层烧死(floor(4×20)=80 > 10 血),
            // 连锁死亡后又蔓延一次,把这条测试原本想钉住的「转移后层数原样是 4」污染成 3。
            // AdvanceOnce() 只推进**一个**非玩家行动者,精确停在 0 号死亡+蔓延这一步,
            // 1 号本回合不会再被结算,避免连锁掩盖本测试要验的东西。
            engine.AdvanceOnce();   // 敌方段结算灼烧:5 × 20 = 100 伤害,10 血必死
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:被自己身上的火烧死");
            Assert.That(BurnOn(engine, 1), Is.EqualTo(4), "刚结算过的那一层不跟着蔓延");
        }

        /// <summary>斩杀(<see cref="EffectDef.ExecuteKills"/>)走的是与普通伤害不同的分支——
        /// 直接把 Hp 清零再 ResolveDefeat(TryExecuteKill),不经过 DamageEnemy 的死亡判定。
        /// 五条死亡路径(灼烧/流血/引爆/斩杀/普通伤害)里独缺这一条的覆盖,单独补。</summary>
        private static RecipeGraph KillGraph() => new(new[]
        {
            new CharDef("燃", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 5) }),
            // 凿:80 点普通伤害,不带斩杀——只用来把目标削到执行阈值以下
            new CharDef("凿", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 80) }),
            // 斩:HP<30% 时直接击杀(非 Boss)
            new CharDef("斩", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 1,
                    executeBelowPercent: 30, executeKills: true) }),
        });

        private static BattleEngine KillEngine(int spreadPercent, int enemyCount = 2, int enemyHp = 100) =>
            new(KillGraph(), new BattleConfig
                {
                    DropTable = new[] { "火" }, PlayerMaxHp = 500, ApPerTurn = 20,
                    BurnSpreadPercent = spreadPercent,
                },
                new[] { "燃", "凿", "斩" }, Array.Empty<string>(),
                Enumerable.Range(0, enemyCount)
                    .Select(i => new EnemyDef($"靶{i}", Element.Heart, enemyHp, 0))
                    .ToArray(),
                seed: 1);

        [Test]
        public void ExecuteKill_AlsoSpreadsRemainingBurn()
        {
            var engine = KillEngine(100);
            Assert.That(engine.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(BurnOn(engine, 0), Is.EqualTo(5), "前提:0 号身上 5 层");
            Assert.That(engine.Cast("凿", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.True, "前提:削进阈值但还没打死");
            Assert.That(engine.Cast("斩", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False, "前提:被斩杀直接清零");
            Assert.That(BurnOn(engine, 1), Is.EqualTo(5), "斩杀致死同样要走 ResolveDefeat 的蔓延");
        }
    }
}
