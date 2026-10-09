using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 3:斩杀族。J3 斩杀时(TraitTrigger.OnExecute,铁则)、J4 斩杀溅射(铡刀落)、J5 致命(割喉)。
    ///
    /// 夹具:字一律 Element.Heart;卡等级 4(铁则 Lv4 槽解锁);PlayerAttack 100。</summary>
    public class MetalExecuteTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 100000, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, int level, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) => Battle(new[] { def }, 4, enemies);

        private static EnemyDef Mob(int attack = 0, int hp = Hp) => RebalanceFixture.Mob(hp: hp, attack: attack);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        // ---------------- J3:斩杀时(铁则「斩杀时战意 +2」) ----------------

        private static TraitDef IronRule() => new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "铁则",
            new[] { new EffectDef(EffectKind.Morale, 2) }, TraitTrigger.OnExecute);

        /// <summary>攻击面:伤害 10 + 斩杀 35%(直接击杀);五行面:格挡 + 立威 20%。</summary>
        private static CharDef Guillotine(bool ironRule = true, bool killsOutright = true) => new("铡", Element.Heart,
            effects: new[]
            {
                new EffectDef(EffectKind.Block, 9),
                new EffectDef(EffectKind.BlockMod, 0, counterExecuteBelow: 20),
            },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10, executeBelowPercent: 35, executeKills: killsOutright) },
            traits: ironRule ? new[] { IronRule() } : Array.Empty<TraitDef>());

        [Test]
        public void OnExecute_InCast_ExecuteKillTriggers()
        {
            var b = Battle(Guillotine(), Mob(hp: 1000), Mob());
            b.Enemies[0].Hp = 300;   // 30% < 35%
            b.Cast("铡", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:被斩杀");
            Assert.That(Morale(b), Is.EqualTo(2), "出字内斩杀 → 铁则入队,出字末尾兑现");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void OnExecute_PlainKill_DoesNotTrigger()
        {
            var plain = Battle(new CharDef("铡", Element.Heart,
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                traits: new[] { IronRule() }), Mob(hp: 1000), Mob());
            plain.Enemies[0].Hp = 1;
            plain.Cast("铡", 0, attackMode: true);
            Assert.That(plain.Enemies[0].Alive, Is.False, "前提:伤害击杀");
            Assert.That(Morale(plain), Is.EqualTo(0), "普通击杀不是斩杀");
        }

        [Test]
        public void OnExecute_BossDouble_IsNotExecute()
        {
            var b = Battle(Guillotine(killsOutright: true), RebalanceFixture.Boss(hp: 10000), Mob());
            b.Enemies[0].Hp = 1000;   // 10%:Boss 改吃 ×2
            b.Cast("铡", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.True);
            Assert.That(Morale(b), Is.EqualTo(0), "对 Boss 的 ×2 不算斩杀(Q6)");
        }

        [Test]
        public void OnExecute_EnemyTurnCounterExecute_Triggers_ViaSourceChar()
        {
            var b = Battle(Guillotine(), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 150;   // 15% < 20%
            b.Cast("铡", -1, attackMode: false);
            Assert.That(Morale(b), Is.EqualTo(0));
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:立威斩杀");
            Assert.That(Morale(b), Is.EqualTo(2), "敌人回合的立威斩杀按 ExecuteSourceCharId 回查铁则,ActOneEnemy 安全点兑现");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "已在安全点排空");
        }

        [Test]
        public void OnExecute_CastAndEnemyTurn_EachTriggersOnce()
        {
            var b = Battle(Guillotine(), Mob(hp: 1000), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 300;
            b.Enemies[1].Hp = 150;
            b.Cast("铡", 0, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(2), "出字内一次");
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[1].Alive, Is.False);
            Assert.That(Morale(b), Is.EqualTo(4), "敌人回合再一次");
        }

        [Test]
        public void OnExecute_CounterExecute_WithoutIronRule_NoReaction()
        {
            var b = Battle(Guillotine(ironRule: false), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 150;
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(Morale(b), Is.EqualTo(0));
        }

        [Test]
        public void OnExecute_CounterExecute_LastEnemy_Wins()
        {
            var b = Battle(Guillotine(), Mob(attack: 1, hp: 1000));
            b.Enemies[0].Hp = 150;
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "斩掉最后一名敌人当场判胜");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "已分胜负:反应丢弃");
        }
    }
}
