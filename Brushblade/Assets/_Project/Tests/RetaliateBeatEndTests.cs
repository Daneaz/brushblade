using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Ruling 12(D2-水 Task 4 fix round 1):受击回敬(Retaliate)的反应改在攻击者这一拍收尾(EndBeat,TickTurns 之后)兑现。
    /// 此前在拍内安全点兑现,同拍末尾的 TickTurns 会把回敬挂的 turns 型状态吃掉一回合:Slow 1 / Freeze 1 / Weaken … turns 1 当拍即到期
    /// (Freeze 还不挂霜抗),铡关 `Bleed 35 turns 2` 只跳 1 次。灼是层数制,不受影响 —— 由指纹钉住改动前后一致。
    ///
    /// 夹具:Element.Heart、PlayerAttack 100(基准,流血不放大)、玩家 5000 血、敌人 10 万血;回敬本回合有效(玩家回合开始到期)。</summary>
    public class RetaliateBeatEndTests
    {
        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static CharDef Guard(params EffectDef[] onHit) =>
            new("回", Element.Heart, effects: new[] { new EffectDef(EffectKind.Retaliate, 0, perHit: onHit) });

        private static BattleEngine Battle(CharDef guard, params EnemyDef[] enemies)
        {
            var defs = new[] { guard };
            return new BattleEngine(RebalanceFixture.Graph(defs), Config,
                Enumerable.Repeat(guard.Id, 10).ToArray(), Array.Empty<string>(), enemies, seed: 1);
        }

        private static void Arm(BattleEngine b) => Assert.That(b.Cast("回", -1), Is.EqualTo(BattleError.None));

        private static bool Attacked(BattleEngine b, int i) =>
            b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyAttack && e.TargetIndex == i);

        [Test]
        public void RetaliateSlow_CoversAttackersNextAction()
        {
            var b = Battle(Guard(new EffectDef(EffectKind.Slow, 1)), RebalanceFixture.Mob(attack: 10));
            Arm(b);
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.SpeedModifier)?.TurnsLeft, Is.EqualTo(1),
                "回敬减速在攻击者拍尾递减之后才挂上,留到它下一次行动");
            // 减速 −50%:行动条攒得慢,下一次行动被推迟;那一拍结束时到期(回敬已过期,不再续)
            int rounds = 0;
            do { b.EndTurn(); rounds++; } while (!Attacked(b, 0) && rounds < 5);
            Assert.That(rounds, Is.GreaterThan(1), "减速推迟了攻击者的下一次行动");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SpeedModifier), Is.False, "下一次行动结束时到期");
        }

        [Test]
        public void RetaliateFreeze_SkipsAttackersNextBeat_ThenFrostResist()
        {
            var b = Battle(Guard(new EffectDef(EffectKind.Freeze, 1)), RebalanceFixture.Mob(attack: 10));
            Arm(b);
            b.EndTurn();
            Assert.That(Attacked(b, 0), Is.True, "前提:这一拍出手并被回敬");
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Freeze)?.TurnsLeft, Is.EqualTo(1));
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(Attacked(b, 0), Is.False, "下一拍被冻结跳过");
            Assert.That(b.PlayerHp, Is.EqualTo(hp));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostResist), Is.True, "冻结到期挂霜抗(R1)");
        }

        [Test]
        public void RetaliateFreeze_Boss_GetsIceStall()
        {
            var b = Battle(Guard(new EffectDef(EffectKind.Freeze, 1)), RebalanceFixture.Boss(attack: 10));
            Arm(b);
            b.EndTurn();
            Assert.That(Attacked(b, 0), Is.True, "前提:Boss 这一拍普攻");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.True, "Boss → 冰滞");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
        }

        /// <summary>铡关的写法 `Bleed 35 turns 2`:回敬挂上后跳 2 次(此前同拍被递减一次,只跳 1 次)。</summary>
        [Test]
        public void RetaliateBleed_TicksTwice()
        {
            var b = Battle(Guard(new EffectDef(EffectKind.Bleed, 35, turns: 2)), RebalanceFixture.Mob(attack: 10));
            Arm(b);
            int ticks = 0;
            for (int i = 0; i < 4; i++)
            {
                b.EndTurn();
                ticks += b.LastEvents.Count(e => e.Kind == BattleEventKind.BleedTick && e.TargetIndex == 0 && e.Amount == 35);
            }
            Assert.That(ticks, Is.EqualTo(2));
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(100000 - 70));
        }

        /// <summary>回敬灼(烈焰护身 `BurnSingle 2`):层数制,TickTurns 不碰 —— Ruling 12 前后事件流与状态逐位相同。
        /// 期望值取自改动前(HEAD 2339f118)。</summary>
        [Test]
        public void RetaliateBurn_UnchangedByBeatEnd()
        {
            var b = Battle(Guard(new EffectDef(EffectKind.BurnSingle, 2)),
                RebalanceFixture.Mob(attack: 10), RebalanceFixture.Mob(attack: 7));
            var sb = new StringBuilder();
            for (int t = 0; t < 5; t++)
            {
                Arm(b);
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Source};");
                sb.Append($"|P{b.PlayerHp}");
                foreach (var en in b.Enemies)
                {
                    sb.Append($",E{en.Hp}[");
                    foreach (var s in en.Statuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                    sb.Append(']');
                }
                sb.Append('\n');
            }
            string raw = sb.ToString();
            Assert.That(raw.Contains("Burn:0:2"), Is.True, "前提:回敬上灼");
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            Assert.That(h.ToString("x16") + " " + raw.Length, Is.EqualTo(ExpectedBurn), raw);
        }

        private const string ExpectedBurn = "a09488662cba586d 1170";
    }
}
