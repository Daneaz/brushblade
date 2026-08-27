using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤物状态容器(2026-08-26)。此前 SummonState 没有 Statuses ——
    /// 灯花无论打到谁都只烧玩家,召唤物身上永远挂不住任何东西。
    ///
    /// 结算口径**照抄玩家侧** <c>SettlePlayerBurn</c>:层数 × BurnPerStack,不吃攻击力、
    /// 不吃生克。敌人侧那套(SettleBurnOn)要吃玩家攻击与火克,两边本来就是两个口径,
    /// 别把这里改成跟敌人一样。</summary>
    public class SummonStatusTests
    {
        private const int BurnPerStack = 20;

        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 盾兵:10 血 / 攻 3,无被动。站前排替玩家挡近战
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            // 厚盾:200 血,烧不死,用来单看层数递减
            new CharDef("垛", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 3, summonChar: "木") }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies, int seed = 1) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 },
                library, Array.Empty<string>(), enemies, seed);

        /// <summary>灯花:每次攻击附 1 层灼烧。攻 0 = 只挂灼烧不掉血,便于把两件事分开看。</summary>
        private static EnemyDef Sear(int attack = 0) =>
            new("灯花", Element.Fire, 3000, attack, EnemyAbility.Sear);

        // ---- 灯花:打谁烧谁 ----

        // 断的是**事件**而不是残余层数:两边的灼烧都会在各自那一拍被结算掉一层
        // (玩家在 BeginPlayerTurn,召唤物在 ActSummonTurn),残余量取决于一个 EndTurn 里
        // 谁先谁后 —— 那是调度顺序的事,不是「烧到谁」的事。事件流没有这层歧义。

        [Test]
        public void Sear_BurnsTheSummonItHits_NotThePlayer()
        {
            // 近战被前排召唤物拦下 → 灼烧该落在那只召唤物身上。
            // 改前:RefreshBurn 写死 _playerStatuses,灯花打召唤物却烧玩家。
            var engine = Engine(new[] { "垛" }, new[] { Sear() });
            engine.Cast("垛");
            engine.EndTurn();

            var burns = engine.LastEvents.Where(e => e.Kind == BattleEventKind.SummonBurn).ToList();
            Assert.That(burns.Count, Is.EqualTo(1), "挨打的是召唤物,灼烧就该挂在它身上");
            Assert.That(burns[0].TargetIndex, Is.EqualTo(0), "TargetIndex 是槽位");
            Assert.That(burns[0].Amount, Is.EqualTo(1));
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex < 0),
                Is.False, "玩家没挨这一下,不该被烧");
        }

        [Test]
        public void Sear_StillBurnsPlayerWhenItHitsPlayer()
        {
            // 负向:场上没有召唤物时口径不变,仍烧玩家
            var engine = Engine(Array.Empty<string>(), new[] { Sear() });
            engine.EndTurn();

            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex < 0),
                Is.True);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.SummonBurn), Is.False);
        }

        [Test]
        public void Sear_RefreshesInsteadOfStacking()
        {
            // 与玩家侧同一条 RefreshBurn 语义:连续多回合刷新到 N 层,不累积
            var engine = Engine(new[] { "垛" }, new[] { Sear() });
            engine.Cast("垛");
            engine.EndTurn();
            engine.EndTurn();
            engine.EndTurn();

            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1),
                "刷新到 1 层,不是攒到 3 层");
        }

        // ---- 召唤物自身的灼烧结算 ----

        [Test]
        public void SummonBurn_SettlesAtItsOwnTurnAndDecaysOneStack()
        {
            var engine = Engine(new[] { "垛" }, new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("垛");
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 3, TurnsLeft = -1,
            });
            int before = engine.Summons[0].Hp;

            engine.EndTurn();

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(before - 3 * BurnPerStack),
                "层数 × BurnPerStack,不吃攻击力也不吃生克(与玩家侧同口径)");
            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "结算后自减一层");
        }

        [Test]
        public void SummonBurn_KillsTheSummon_AndItDoesNotStrike()
        {
            var engine = Engine(new[] { "兵" }, new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("兵"); // 10 血
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = -1,
            });

            engine.EndTurn();

            Assert.That(engine.Summons[0].Alive, Is.False, "1 层 × 20 > 10 血");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.SummonAttack), Is.False,
                "烧死在出手之前,这一拍不该再挥一刀");
        }

        [Test]
        public void SummonStatuses_TickTurnsAtEndOfItsOwnTurn()
        {
            // 带回合数的状态照常递减 —— ActSummonTurn 里那条「暂无内容」的占位补上了
            var engine = Engine(new[] { "垛" }, new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("垛");
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -30, TurnsLeft = 2, SourceId = "测试",
            });

            engine.EndTurn();
            Assert.That(engine.Summons[0].Statuses.Find(StatusKind.SpeedModifier)?.TurnsLeft, Is.EqualTo(1));

            engine.EndTurn();
            Assert.That(engine.Summons[0].Statuses.Has(StatusKind.SpeedModifier), Is.False, "归零即移除");
        }

        // ---- 存档往返 ----

        [Test]
        public void Snapshot_RoundTrip_KeepsSummonStatuses()
        {
            // 只走「实体 → 快照 → JSON → 快照 → 实体」这一条链,不再塞回引擎:
            // BattleEngine 的构造函数会跑开场调度,携带的召唤物可能当场行动一拍,
            // 把灼烧结算掉一层 —— 那是正确行为,但会遮住这条测试真正要看的往返。
            var engine = Engine(new[] { "垛" }, new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("垛");
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 2, TurnsLeft = -1,
            });

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 },
            };
            meta.EndlessV2.CarriedSummons.Add(engine.Summons[0].Capture(0));

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            Assert.That(restored.EndlessV2.CarriedSummons[0].Statuses.Count, Is.EqualTo(1),
                "状态表要过得了 JSON");

            var revived = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 },
                new[] { "垛" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed: 1,
                startingSummons: restored.EndlessV2.CarriedSummons);

            // 开场那一拍已经结算并自减了一层,所以是 1 而不是 2 —— 断言这个数就是
            // 在断言「状态确实活着进了新一场战斗」,层数为 0 才说明整条链断了
            Assert.That(revived.Summons[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
            Assert.That(revived.LastEvents.Any(e => e.Kind == BattleEventKind.SummonBurnTick)
                        || revived.OpeningSteps.Any(st => st.Events.Any(
                            e => e.Kind == BattleEventKind.SummonBurnTick)),
                Is.True, "开场那一拍要发出结算事件,表现层才飘得出字");
        }

        [Test]
        public void Snapshot_DoesNotShareStatusEntriesWithTheLiveSummon()
        {
            // 浅拷会让快照与实体共享同一条 StatusEffect —— 改一边动两边(与 EnemyState.Capture 同一条教训)
            var engine = Engine(new[] { "垛" }, new[] { new EnemyDef("靶", Element.Heart, 3000, 0) });
            engine.Cast("垛");
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 2, TurnsLeft = -1,
            });

            var snapshot = engine.Summons[0].Capture(0);
            snapshot.Statuses[0].Magnitude = 99;

            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }
    }
}
