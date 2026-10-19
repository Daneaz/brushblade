using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>斩获终极技(ult_killdraw_s1/2,spec 2026-10-18「斩获终极技」§1):每击杀一名敌人按概率掉 N 张字进字库
    /// (一级 50% 掉 1、二级 70% 掉 2),两级每回合都至多 <see cref="BattleEngine.KillDrawPerTurnCap"/> 张;
    /// 满库不掉(不进 DropChoice);任何来源的击杀都算;概率与抽字都走 _traitRandom,主随机序列不平移;
    /// 未中不发任何事件。计数在玩家回合开始(StartTurn)清零,
    /// 敌人回合与其前一玩家回合共用一个计数。</summary>
    public class KillDrawTests
    {
        private static readonly string[] Owned = { "刀", "枪", "戟", "矛" };

        private static RecipeGraph Graph() => new(Owned.Select(id =>
            new CharDef(id, Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) })));

        private static EnemyDef Weak() => new("弱", Element.Heart, 1, 0);
        private static EnemyDef Tank() => new("坚", Element.Heart, 900000, 0);

        private static BattleEngine Engine(int killDraws, EnemyDef[] enemies, int capacity = 20, int drops = 0,
            string[] library = null, IReadOnlyList<SummonSnapshot> summons = null, int seed = 1, int chance = 100) =>
            new(Graph(), new BattleConfig
                {
                    PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 20, LibraryCapacity = capacity,
                    UnlockedChars = Owned, DropsPerTurn = drops, KillDraws = killDraws,
                    KillDrawChancePercent = chance,
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
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharDrawn), Is.False, "斩获不再发 CharDrawn(那是回合掉字的动画源)");
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
        public void Miss_GrantsNothingAndEmitsNoEvent()
        {
            var e = Engine(1, new[] { Weak(), Tank() }, chance: 0);
            int before = e.Library.Count;
            e.Cast("刀", 0);
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before - 1), "未中不掉");
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharGranted || ev.Kind == BattleEventKind.CharGrantBlocked), Is.False, "未中不发事件");
        }

        [Test]
        public void HalfChance_HitsRoughlyHalfTheKills()
        {
            int hits = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var e = Engine(1, new[] { Weak(), Tank() }, seed: seed, chance: 50);
                int before = e.Library.Count;
                e.Cast("刀", 0);
                if (e.Library.Count == before) hits++;
                else Assert.That(e.Library.Count, Is.EqualTo(before - 1), $"seed {seed}:要么掉 1 要么不掉");
            }
            Assert.That(hits, Is.InRange(70, 130), $"200 次击杀命中 {hits} 次,不像 50%");
        }

        [Test]
        public void LevelTwo_DropsTwoAtOnce_ThenCapped()
        {
            var e = Engine(2, new[] { Weak(), Weak(), Tank() });
            int before = e.Library.Count;
            e.Cast("刀", 0);
            Assert.That(e.Library.Count, Is.EqualTo(before - 1 + 2), "二级一次掉 2 张");
            Assert.That(e.LastEvents.Count(ev => ev.Kind == BattleEventKind.CharGranted), Is.EqualTo(2));
            e.Cast("刀", FirstAlive(e));
            Assert.That(e.Enemies[1].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(before - 2 + 2), "一次掉 2 张就到顶,本回合不再掉");
        }

        [Test]
        public void LevelTwo_TruncatedByCap_WhenOneAlreadyDropped()
        {
            // 满库起手:第一杀打出 1 张、只有 1 格空 → 只掉 1(计数 1);再打两张进坦克腾出 2 格,
            // 第二杀二级想掉 2,但本回合只剩 1 张额度 → 只掉 1
            var e = Engine(2, new[] { Weak(), Weak(), Tank() }, capacity: 6);
            int tank = 2;
            e.Cast("刀", 0);
            Assert.That(e.Library.Count, Is.EqualTo(6), "第一杀被满库截成 1 张");
            e.Cast("刀", tank);
            e.Cast("刀", tank);
            Assert.That(e.Library.Count, Is.EqualTo(4));
            e.Cast("刀", 1);
            Assert.That(e.Enemies[1].Alive, Is.False);
            Assert.That(e.Library.Count, Is.EqualTo(3 + 1), "上限 2 截断:本回合已掉 1 张,只再掉 1 张");
            Assert.That(e.LastEvents.Count(ev => ev.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
        }

        [Test]
        public void MainRandomSequence_Unshifted()
        {
            // 两场同种子:一场开斩获(二级 70% 掉 2)一场不开,回合掉字(主随机 _random)抽到的字序列必须一致
            List<string> Drops(int killDraws)
            {
                var e = Engine(killDraws, new[] { Weak(), Weak(), Weak(), Weak(), Tank() }, capacity: 99, drops: 1, seed: 7, chance: 70);
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
            Assert.That(Drops(2), Is.EqualTo(Drops(0)));
        }

        // ---------------- 掉字飞入的事件口径(spec 2026-10-18 §2,killdrop 稿)----------------

        /// <summary>群攻字:一次出字连杀多只(字库满 / 上限到顶的情形要靠它在同一次出字里凑出来)。</summary>
        private static BattleEngine SweepEngine(int killDraws, int weak, int capacity, int chance = 100) =>
            new(new RecipeGraph(Owned.Select(id => new CharDef(id, Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }))
                    .Append(new CharDef("扫", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }))),
                new BattleConfig
                {
                    PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 20, LibraryCapacity = capacity,
                    UnlockedChars = Owned, DropsPerTurn = 0, KillDraws = killDraws, KillDrawChancePercent = chance,
                },
                Enumerable.Repeat("扫", 6).ToArray(), Array.Empty<string>(),
                Enumerable.Range(0, weak).Select(_ => Weak()).Append(Tank()).ToArray(), seed: 1);

        [Test]
        public void Kill_EmitsCharGranted_WithLibraryIndexAndDeadEnemy()
        {
            var e = Engine(1, new[] { Tank(), Weak() });
            e.Cast("刀", 1);
            Assert.That(e.Enemies[1].Alive, Is.False);
            var granted = e.LastEvents.Where(ev => ev.Kind == BattleEventKind.CharGranted).ToList();
            Assert.That(granted.Count, Is.EqualTo(1));
            Assert.That(granted[0].TargetIndex, Is.EqualTo(1), "TargetIndex = 阵亡敌人下标(token 从这一格冒出)");
            Assert.That(granted[0].Amount, Is.EqualTo(e.Library.Count - 1), "Amount = 落位的字库下标");
            Assert.That(granted[0].Source, Is.EqualTo(EffectSource.KillDraw), "Source 标斩获,与折桂区分名签");
            int died = e.LastEvents.ToList().FindIndex(ev => ev.Kind == BattleEventKind.EnemyDied);
            int grant = e.LastEvents.ToList().FindIndex(ev => ev.Kind == BattleEventKind.CharGranted);
            Assert.That(died, Is.LessThan(grant), "先阵亡、后得字");
        }

        [Test]
        public void EnemyTurnKill_EmitsCharGranted_NotCharDrawn()
        {
            var e = Engine(1, new[] { Weak(), Tank() }, summons: new[] { Striker(0) });
            e.EndTurn();
            Assert.That(e.Enemies[0].Alive, Is.False);
            var granted = e.LastEvents.Where(ev => ev.Kind == BattleEventKind.CharGranted).ToList();
            Assert.That(granted.Count, Is.EqualTo(1), "敌人回合的击杀同样发 CharGranted");
            Assert.That(granted[0].TargetIndex, Is.EqualTo(0));
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharDrawn), Is.False, "不再并进回合掉字的 CharDrawn");
        }

        [Test]
        public void FullLibrary_EmitsCharGrantBlocked_OncePerTurn()
        {
            // 字库 6/6:扫出 1 张 → 5;第一杀补 1 → 满;第二杀被满库挡下 → 发 Blocked;第三杀本回合已提示过 → 不再发
            var e = SweepEngine(1, weak: 3, capacity: 6);
            e.Cast("扫", 0);
            Assert.That(e.Enemies.Count(x => !x.Alive), Is.EqualTo(3));
            Assert.That(e.LastEvents.Count(ev => ev.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
            var blocked = e.LastEvents.Where(ev => ev.Kind == BattleEventKind.CharGrantBlocked).ToList();
            Assert.That(blocked.Count, Is.EqualTo(1), "每回合只提示一次");
            Assert.That(blocked[0].TargetIndex, Is.EqualTo(1), "TargetIndex = 被挡下的那只阵亡敌人");
        }

        [Test]
        public void FullLibrary_PreRoll_EmitsBlocked_WithoutRolling()
        {
            // 满库时不掷概率(chance 0 也照样提示:满库是规则不是运气)
            var e = Engine(1, new[] { Weak(), Tank() }, capacity: 6, summons: new[] { Striker(0) }, chance: 0);
            e.EndTurn();
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.LastEvents.Count(ev => ev.Kind == BattleEventKind.CharGrantBlocked), Is.EqualTo(1));
        }

        [Test]
        public void CapReached_ThenFull_EmitsNothing()
        {
            // 字库 6/7:扫出 1 → 5;两杀补满 2 张(→ 7 满、上限到顶);第三、四杀上限优先 → 不发任何事件
            var e = SweepEngine(1, weak: 4, capacity: 7);
            e.Cast("扫", 0);
            Assert.That(e.Enemies.Count(x => !x.Alive), Is.EqualTo(4));
            Assert.That(e.LastEvents.Count(ev => ev.Kind == BattleEventKind.CharGranted), Is.EqualTo(2));
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharGrantBlocked), Is.False, "上限到顶不提示");
        }

        [Test]
        public void Off_FullLibrary_EmitsNoBlocked()
        {
            var e = Engine(0, new[] { Weak(), Tank() }, capacity: 6, summons: new[] { Striker(0) });
            e.EndTurn();
            Assert.That(e.LastEvents.Any(ev => ev.Kind == BattleEventKind.CharGrantBlocked), Is.False, "没点斩获不提示");
        }

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)BattleEventKind.CharGrantBlocked, Is.EqualTo((int)BattleEventKind.Behead + 1));
            Assert.That(Enum.GetValues(typeof(BattleEventKind)).Cast<int>().Max(), Is.EqualTo((int)BattleEventKind.CharGrantBlocked));
            Assert.That((int)EffectSource.KillDraw, Is.EqualTo((int)EffectSource.BurnGrow + 1));
            // 2026-10-19 其后追加 Harden(磐固上状态的事件标签,见 StatusAppliedEventTests.Harden_Event_TaggedHardenSource)
            Assert.That((int)EffectSource.Harden, Is.EqualTo((int)EffectSource.KillDraw + 1));
            Assert.That(Enum.GetValues(typeof(EffectSource)).Cast<int>().Max(), Is.EqualTo((int)EffectSource.Harden));
        }
    }
}
