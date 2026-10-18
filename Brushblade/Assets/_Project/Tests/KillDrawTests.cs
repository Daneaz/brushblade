using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>斩获(wide_2,spec 2026-10-18 §7):击杀敌人时掉 1 张字进字库,每回合至多
    /// <see cref="BattleEngine.KillDrawPerTurnCap"/> 张;满库不掉(不进 DropChoice);任何来源的击杀都算;
    /// 抽字走 _traitRandom,主随机序列不平移。计数在玩家回合开始(StartTurn)清零,
    /// 敌人回合与其前一玩家回合共用一个计数。</summary>
    public class KillDrawTests
    {
        private static readonly string[] Owned = { "刀", "枪", "戟", "矛" };

        private static RecipeGraph Graph() => new(Owned.Select(id =>
            new CharDef(id, Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) })));

        private static EnemyDef Weak() => new("弱", Element.Heart, 1, 0);
        private static EnemyDef Tank() => new("坚", Element.Heart, 900000, 0);

        private static BattleEngine Engine(int killDraws, EnemyDef[] enemies, int capacity = 20, int drops = 0,
            string[] library = null, IReadOnlyList<SummonSnapshot> summons = null, int seed = 1) =>
            new(Graph(), new BattleConfig
                {
                    PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 20, LibraryCapacity = capacity,
                    UnlockedChars = Owned, DropsPerTurn = drops, KillDraws = killDraws,
                },
                library ?? Enumerable.Repeat("刀", 6).ToArray(), Array.Empty<string>(),
                enemies, seed: seed, startingSummons: summons);

        private static SummonSnapshot Striker(int slot) => new()
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 1000, MaxHp = 1000,
            Attack = 100000, Speed = 100, Statuses = new List<StatusEffect>(),
        };

        private static int FirstAlive(BattleEngine e)
        {
            for (int i = 0; i < e.Enemies.Count; i++) if (e.Enemies[i].Alive) return i;
            return -1;
        }

        [Test]
        public void Off_KillGrantsNothing()
        {
            var e = Engine(0, new[] { Weak(), Tank() });
            int before = e.Library.Count;
            e.Cast("刀", 0);
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before - 1));
        }

        [Test]
        public void Kill_GrantsOneFromUnlockedPool()
        {
            var e = Engine(1, new[] { Weak(), Tank() });
            int before = e.Library.Count;
            e.Cast("刀", 0);
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before), "打出 1 张、斩获补 1 张");
            Assert.That(Owned.Contains(e.Library[e.Library.Count - 1]), Is.True);
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharDrawn), Is.True, "斩获入库发 CharDrawn(抽卡动画源)");
        }

        [Test]
        public void PerTurnCap_IsTwo()
        {
            var e = Engine(1, new[] { Weak(), Weak(), Weak(), Tank() });
            int before = e.Library.Count;
            for (int k = 0; k < 3; k++) e.Cast("刀", FirstAlive(e));
            Assert.That(e.Enemies.Count(x => x.Alive), Is.EqualTo(1));
            Assert.That(e.Library.Count, Is.EqualTo(before - 3 + BattleEngine.KillDrawPerTurnCap));
        }

        [Test]
        public void CapResetsNextPlayerTurn()
        {
            var e = Engine(1, new[] { Weak(), Weak(), Weak(), Weak(), Tank() });
            e.Cast("刀", FirstAlive(e));
            e.Cast("刀", FirstAlive(e));
            e.EndTurn();
            Assert.That(e.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            int before = e.Library.Count;
            e.Cast("刀", FirstAlive(e));
            Assert.That(e.Library.Count, Is.EqualTo(before), "新玩家回合计数已清零");
        }

        [Test]
        public void FullLibrary_KillByNonCardSource_DoesNotGrantOrSuspend()
        {
            // 敌人回合由木灵击杀时字库是满的:不掉、不进 DropChoice
            var e = Engine(1, new[] { Weak(), Tank() }, capacity: 6, summons: new[] { Striker(0) });
            Assert.That(e.Library.Count, Is.EqualTo(6));
            e.EndTurn();
            Assert.That(e.Enemies[0].Alive, Is.False, "木灵应在敌人回合打死弱");
            Assert.That(e.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "满库不进 DropChoice");
            Assert.That(e.Library.Count, Is.EqualTo(6));
        }

        [Test]
        public void EnemyTurnKill_BySummon_Grants()
        {
            var e = Engine(1, new[] { Weak(), Tank() }, summons: new[] { Striker(0) });
            int before = e.Library.Count;
            e.EndTurn();
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before + 1), "非出字来源的击杀同样掉字");
        }

        [Test]
        public void EnemyTurnKill_SharesCounterWithPrecedingPlayerTurn()
        {
            var e = Engine(1, new[] { Weak(), Weak(), Weak(), Tank() }, summons: new[] { Striker(0) });
            e.Cast("刀", 0);
            e.Cast("刀", 1);   // 本玩家回合已掉满 2
            int before = e.Library.Count;
            e.EndTurn();       // 木灵在随后的敌人回合打死第三只
            Assert.That(e.Enemies[2].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before), "敌人回合与前一玩家回合共用计数,已满不再掉");
        }

        [Test]
        public void MainRandomSequence_Unshifted()
        {
            // 两场同种子:一场开斩获一场不开,回合掉字(主随机 _random)抽到的字序列必须一致
            List<string> Drops(int killDraws)
            {
                var e = Engine(killDraws, new[] { Weak(), Weak(), Weak(), Weak(), Tank() }, capacity: 99, drops: 1, seed: 7);
                var seq = new List<string>();
                for (int t = 0; t < 4; t++)
                {
                    int target = FirstAlive(e);
                    e.Cast("刀", target);
                    e.EndTurn();
                    seq.Add(e.Library[e.Library.Count - 1]);
                }
                return seq;
            }
            Assert.That(Drops(1), Is.EqualTo(Drops(0)));
        }
    }
}
