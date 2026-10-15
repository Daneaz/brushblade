using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>格挡消耗事件 BlockUsed(格挡反击动效稿 drafts/counterfx,2026-10-15):
    /// 表现层据此演「挡 / 扣」两拍 —— 反击被预算削到 0 或攻击者已死时,没有反击伤害事件,仍要知道这次挡过。
    /// TargetIndex = 攻击者,SecondIndex = 木灵槽位 / −1 玩家,Amount = 剩余次数;紧跟受击事件之后、反击伤害之前。</summary>
    public class BlockUsedEventTests
    {
        // 攻击面 100 → 反击 30;五行面格挡 charges 次
        private static CharDef Guard(int charges) => new("铠", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Block, charges) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static CharDef Mirror => new("镜", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Reflect, 60, turns: 2) });

        private static CharDef Summoner => new("垛", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 0, summonChar: "木") });

        private static BattleEngine Battle(EnemyDef mob, int charges = 1)
        {
            var defs = new[] { Guard(charges), Mirror, Summoner, new CharDef("木", Element.Wood) };
            return new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "铠", "铠", "镜", "垛" }, Array.Empty<string>(), new[] { mob }, seed: 1);
        }

        private static List<BattleEvent> Events(BattleEngine b) => b.LastEvents.ToList();

        private static int IndexOf(List<BattleEvent> ev, Func<BattleEvent, bool> pred) => ev.FindIndex(e => pred(e));

        private static bool IsCounter(BattleEvent e) =>
            e.Kind == BattleEventKind.Damage && e.Source == EffectSource.BlockCounter;

        [Test]
        public void PlayerBlock_EmitsBlockUsed_AfterEnemyAttack_BeforeCounter()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 100), charges: 2);
            b.Cast("铠", -1, attackMode: false);
            b.EndTurn();
            var ev = Events(b);
            int attack = IndexOf(ev, e => e.Kind == BattleEventKind.EnemyAttack);
            int used = IndexOf(ev, e => e.Kind == BattleEventKind.BlockUsed);
            int counter = IndexOf(ev, IsCounter);
            Assert.That(used, Is.GreaterThan(attack), "发在 EnemyAttack 之后");
            Assert.That(attack, Is.GreaterThanOrEqualTo(0));
            Assert.That(counter, Is.GreaterThan(used), "反击伤害之前");
            Assert.That(ev[used].TargetIndex, Is.EqualTo(0), "攻击者");
            Assert.That(ev[used].SecondIndex, Is.EqualTo(-1), "玩家格挡");
            Assert.That(ev[used].Amount, Is.EqualTo(1), "剩余次数 2 → 1");
            Assert.That(ev.Count(e => e.Kind == BattleEventKind.BlockUsed), Is.EqualTo(1));
        }

        [Test]
        public void SummonBlock_EmitsBlockUsed_WithSlot_AndZeroRemaining()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 100));
            b.Cast("垛");
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);
            b.Cast("铠", -1, attackMode: false, allySlot: slot);
            b.EndTurn();
            var ev = Events(b);
            int hit = IndexOf(ev, e => e.Kind == BattleEventKind.SummonHit);
            int used = IndexOf(ev, e => e.Kind == BattleEventKind.BlockUsed);
            int counter = IndexOf(ev, IsCounter);
            Assert.That(hit, Is.GreaterThanOrEqualTo(0), "木灵承伤(否则本测试空转)");
            Assert.That(used, Is.GreaterThan(hit), "发在 SummonHit 之后");
            Assert.That(counter, Is.GreaterThan(used), "反击伤害之前");
            Assert.That(ev[used].TargetIndex, Is.EqualTo(0));
            Assert.That(ev[used].SecondIndex, Is.EqualTo(slot), "木灵槽位");
            Assert.That(ev[used].Amount, Is.EqualTo(0), "扣到 0");
        }

        [Test]
        public void CounterClippedToZero_StillEmitsBlockUsed_NoCounterDamage()
        {
            // 敌攻 1 → 格挡后 0,60% 预算 0:反击被削光
            var b = Battle(RebalanceFixture.Mob(attack: 1));
            b.Cast("铠", -1, attackMode: false);
            b.EndTurn();
            var ev = Events(b);
            Assert.That(ev.Count(e => e.Kind == BattleEventKind.BlockUsed), Is.EqualTo(1));
            Assert.That(ev.Any(IsCounter), Is.False, "反击为 0 不发伤害");
            Assert.That(b.PlayerStatuses.Has(StatusKind.Block), Is.False, "格挡照样扣掉");
        }

        [Test]
        public void AttackerKilledBeforeCounter_StillEmitsBlockUsed_NoCounterDamage()
        {
            // 敌攻 100 → 格挡后 60;镜 60% 反 36 > 血 10:攻击者在反击前已死
            var b = Battle(RebalanceFixture.Mob(hp: 10, attack: 100));
            b.Cast("镜", -1, attackMode: false);
            b.Cast("铠", -1, attackMode: false);
            b.EndTurn();
            var ev = Events(b);
            Assert.That(b.Enemies[0].Alive, Is.False, "镜先把攻击者反死(否则本测试空转)");
            Assert.That(ev.Count(e => e.Kind == BattleEventKind.BlockUsed), Is.EqualTo(1));
            Assert.That(ev.Any(IsCounter), Is.False, "攻击者已死不反击");
        }

        [Test]
        public void BlockUsed_AppendedAtEnd()
        {
            Assert.That((int)BattleEventKind.BlockUsed, Is.EqualTo((int)BattleEventKind.EndureSaved + 1), "只在末尾追加");
        }

        [Test]
        public void NoBlock_NoBlockUsed()
        {
            var b = Battle(RebalanceFixture.Mob(attack: 100));
            b.EndTurn();
            Assert.That(Events(b).Any(e => e.Kind == BattleEventKind.BlockUsed), Is.False);
        }
    }
}
