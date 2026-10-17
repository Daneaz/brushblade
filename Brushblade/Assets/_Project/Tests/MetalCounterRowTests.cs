using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec C §1 扫锋:格挡反击打攻击者 100% + 同排其余 70%,共用 60% 预算;与贯穿同时有时每个敌人只吃一次。
    /// 夹具同 MetalBlockRidersTests:心属性、攻击面 DamageSingle 100、Lv1 → 反击 30;攻击 1000 → 预算 600(格挡前起算)。</summary>
    public class MetalCounterRowTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig { PlayerMaxHp = 100000, PlayerAttack = 100, ApPerTurn = 20 };

        private static CharDef Guard(bool row, bool column = false) => new("扫", Element.Heart,
            effects: new[]
            {
                new EffectDef(EffectKind.Block, 9),
                new EffectDef(EffectKind.BlockMod, 0, counterRow: row, counterColumn: column),
            },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        /// <summary>0–2 前排、3 后排;只有 attackerSlot 有攻击。</summary>
        private static EnemyDef[] FourMobs(int attackerSlot) => Enumerable.Range(0, 4).Select(i =>
            new EnemyDef("怔" + i, Element.Heart, Hp, i == attackerSlot ? 1000 : 0,
                row: i == 3 ? EnemyRow.Back : EnemyRow.Front)).ToArray();

        private static BattleEngine Battle(CharDef def, EnemyDef[] enemies, IReadOnlyList<SummonSnapshot> summons = null) =>
            new(RebalanceFixture.Graph(def, new CharDef("木", Element.Wood)), Config,
                new[] { def.Id, def.Id, def.Id }, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = 1 }, startingSummons: summons);

        private static int Lost(BattleEngine b, int i) => b.Enemies[i].MaxHp - b.Enemies[i].Hp;

        private static int FrontMateOfBack(BattleEngine probe)
        {
            var back = probe.Enemies[3];
            for (int i = 0; i < 3; i++)
                if (probe.Enemies[i].Column < back.ColumnEnd && back.Column < probe.Enemies[i].ColumnEnd) return i;
            Assert.Fail("夹具:后排没有同列的前排");
            return -1;
        }

        [Test]
        public void Row_HitsAttackerFull_ThenSameRowAt70Percent_BackRowUntouched()
        {
            var b = Battle(Guard(row: true), FourMobs(1));
            Assert.That(b.Cast("扫", -1, attackMode: false), Is.EqualTo(BattleError.None));
            b.EndTurn();
            Assert.That(Lost(b, 1), Is.EqualTo(30), "攻击者 100%");
            Assert.That(Lost(b, 0), Is.EqualTo(21), "同排其余 = 30 × 70%");
            Assert.That(Lost(b, 2), Is.EqualTo(21));
            Assert.That(Lost(b, 3), Is.EqualTo(0), "后排不在同排");
        }

        [Test]
        public void RowAndColumn_EachEnemyHitOnce()   // Review Focus 5
        {
            int attacker = FrontMateOfBack(Battle(Guard(row: false), FourMobs(-1)));
            var b = Battle(Guard(row: true, column: true), FourMobs(attacker));
            b.Cast("扫", -1, attackMode: false);
            b.EndTurn();
            Assert.That(Lost(b, attacker), Is.EqualTo(30), "攻击者只吃一次");
            Assert.That(Lost(b, 3), Is.EqualTo(21), "同列 70%");
            for (int i = 0; i < 3; i++)
                if (i != attacker) Assert.That(Lost(b, i), Is.EqualTo(21), $"同排 {i} 只吃一次 70%");
        }

        [Test]
        public void Row_OnSummonBlock_SameRule()
        {
            var taunter = new SummonSnapshot
            {
                Slot = 0, Char = "木", Element = Element.Wood, Hp = Hp, MaxHp = Hp, Attack = 0, Speed = 100,
                Passive = new SummonPassive { Taunt = true },
            };
            var b = Battle(Guard(row: true), FourMobs(1), new[] { taunter });
            Assert.That(b.Cast("扫", -1, attackMode: false, allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.Block), Is.True, "夹具自检:格挡挂在木灵身上");
            b.EndTurn();
            Assert.That(Lost(b, 1), Is.EqualTo(30));
            Assert.That(Lost(b, 0) + Lost(b, 2), Is.EqualTo(42), "同排其余各 21");
        }

        [Test]
        public void Merge_KeepsRowFromEitherSide()
        {
            var b = Battle(Guard(row: true), FourMobs(1));
            b.Cast("扫", -1, attackMode: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Block).CounterRow, Is.True);
        }
    }
}
