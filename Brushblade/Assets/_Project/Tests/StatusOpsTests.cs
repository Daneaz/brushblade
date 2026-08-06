using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>状态操作族(2026-08-06,子项目 A):驱散/净化/免疫/斩杀/复活,
    /// 以及配套的玩家侧减益(封字 / 玩家灼烧)。
    /// 规格见 docs/superpowers/specs/2026-08-06-状态操作族-design.md。</summary>
    public class StatusOpsTests
    {
        // 出字库:每个字只带一种待测机制,敌人一律用「心」属性避开生克干扰。
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("素", Element.Wood,   // 无被动的基准召唤(10 血 / 攻 3)
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木") }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int? startingHp = null) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, Array.Empty<string>(), enemies, seed: 1, startingHp: startingHp);

        private static EnemyDef Dummy(int hp = 200, int attack = 0) => new("靶", Element.Heart, hp, attack);

        /// <summary>带倾覆大招的 Boss。BossChargeEvery = 1 → 第一个敌方回合蓄力,第二个释放。</summary>
        private static EnemyDef ToppleBoss(int attack = 4) =>
            new("覆", Element.Heart, 300, attack,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, attack, skill: BossSkill.Topple) });

        private static BattleConfig BossConfig() =>
            new() { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };

        // ---- 封字:倾覆的 AP 惩罚 ----

        [Test]
        public void Topple_AppliesSealStatus_NotABareField()
        {
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            engine.EndTurn();  // 蓄力
            engine.EndTurn();  // 释放倾覆
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.True, "倾覆应挂上封字");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Seal), Is.EqualTo(1));
        }

        [Test]
        public void Seal_SurvivesTheSameEndTurnAndCutsNextTurnAp()
        {
            // TurnsLeft 必须填 2:倾覆在敌方段挂上,而同一个 EndTurn 的「状态回合递减」排在
            // StartTurn 之前。填 1 会被当场减到 0 移除,StartTurn 读到 0 —— 效果凭空消失。
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            int fullAp = engine.Ap;
            engine.EndTurn();  // 蓄力
            Assert.That(engine.Ap, Is.EqualTo(fullAp), "蓄力回合不该扣 AP");
            engine.EndTurn();  // 释放倾覆 → 本次 StartTurn 就该少 1 点
            Assert.That(engine.Ap, Is.EqualTo(fullAp - 1));
        }

        [Test]
        public void Seal_ExpiresAfterExactlyOnePenalizedTurn()
        {
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            int fullAp = engine.Ap;
            engine.EndTurn();  // 蓄力
            engine.EndTurn();  // 释放 → AP 少 1
            Assert.That(engine.Ap, Is.EqualTo(fullAp - 1));
            engine.EndTurn();  // 下一轮蓄力 → 封字到期
            Assert.That(engine.Ap, Is.EqualTo(fullAp), "只该罚一个回合");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False);
        }

        [Test]
        public void Seal_SurvivesSaveRoundTrip()
        {
            // 既有 bug:_apPenaltyNextTurn 从来没进过 BattleSnapshot,倾覆后存档续爬白丢惩罚。
            // 状态化后它跟着 PlayerStatuses 一起存。
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            engine.EndTurn();
            engine.EndTurn();  // 封字已挂
            var snapshot = engine.Capture();
            Assert.That(snapshot.PlayerStatuses.Any(s => s.Kind == StatusKind.Seal), Is.True,
                "封字必须进快照");
        }
    }
}
