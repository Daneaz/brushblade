using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>真实字表 + 手搭战斗的夹具。仓库根只用 TestContext.TestDirectory 定位(CLAUDE.md)。</summary>
    internal static class Fixture
    {
        private static RecipeGraph _graph;

        public static RecipeGraph Graph
        {
            get
            {
                if (_graph != null) return _graph;
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade", "Assets")))
                    dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("找不到仓库根(含 Brushblade/Assets 的目录)");
                string path = Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config", "chars.json");
                _graph = ConfigLoader.LoadGraph(File.ReadAllText(path));
                return _graph;
            }
        }

        public static readonly EnemyDef Foe = new EnemyDef("怔", Element.Heart, 100000, 0);

        public static SummonSnapshot WoodSummon(int slot) => WoodSummon(slot, 1000);

        /// <summary>hp = 0 即占槽的尸体。</summary>
        public static SummonSnapshot WoodSummon(int slot, int hp) => new SummonSnapshot
        {
            Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood,
            Hp = hp, MaxHp = 1000, Attack = 40, Speed = 100, Statuses = new List<StatusEffect>(),
        };
    }

    /// <summary>一场手搭的战斗 + 复原副本所需的全部配置。</summary>
    internal sealed class TestBattle
    {
        public readonly BattleConfig Config;
        public readonly Dictionary<string, EnemyDef> EnemyDefs = new Dictionary<string, EnemyDef> { [Fixture.Foe.Id] = Fixture.Foe };
        public readonly IReadOnlyDictionary<string, int> CardLevels;
        public BattleEngine Battle;

        public TestBattle(string[] library, string[] pool = null, int enemies = 2, int? ap = null,
            SummonSnapshot[] summons = null, int libraryCapacity = 6, int cardLevel = 1)
        {
            CardLevels = cardLevel == 1 ? null
                : Fixture.Graph.All.ToDictionary(d => d.Id, _ => cardLevel);
            Config = new BattleConfig
            {
                PlayerMaxHp = 5000, ApPerTurn = 3, LibraryCapacity = libraryCapacity, PoolCapacity = 10,
                BossPhaseJitterPercent = 0, DropsPerTurn = 0,
            };
            Battle = new BattleEngine(Fixture.Graph, Config, library, pool ?? Array.Empty<string>(),
                Enumerable.Repeat(Fixture.Foe, enemies).ToArray(), seed: 7, cardLevels: CardLevels, startingSummons: summons);
            Assert.That(Battle.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "夹具:开场应轮到玩家");
            if (ap.HasValue)
            {
                var snap = Battle.Capture();
                snap.Ap = ap.Value;
                Battle = BattleEngine.Restore(snap, Fixture.Graph, Config, CardLevels, EnemyDefs);
            }
        }

        public BattleEngine Clone() => BattleEngine.Restore(Battle.Capture(), Fixture.Graph, Config, CardLevels, EnemyDefs);

        public List<BotAction> Generate() => Candidates.Generate(Battle, Fixture.Graph);
    }

    public class CandidatesTests
    {
        private static List<BotAction> Casts(List<BotAction> all, string id, bool attackMode) =>
            all.Where(a => a.Kind == ActionKind.Cast && a.CharId == id && a.AttackMode == attackMode).ToList();

        [Test]
        public void TwoFaceChar_ListsBothFaces()
        {
            var t = new TestBattle(new[] { "冰" }, enemies: 2);
            Assert.That(CardFaceRules.HasTwoFaces(Fixture.Graph.Get("冰")), Is.True, "前提:冰是两面字");
            var all = t.Generate();
            Assert.That(Casts(all, "冰", true).Count, Is.GreaterThan(0), "攻击面");
            Assert.That(Casts(all, "冰", false).Count, Is.GreaterThan(0), "五行面");
        }

        [Test]
        public void SingleTargetAttackFace_OnePerLiveEnemy()
        {
            var t = new TestBattle(new[] { "炎" }, enemies: 3);
            var targets = Casts(t.Generate(), "炎", true).Select(a => a.TargetIndex).OrderBy(i => i).ToList();
            Assert.That(targets, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void AllEnemiesAttackFace_OnlyOne()
        {
            var t = new TestBattle(new[] { "焚" }, enemies: 3);
            Assert.That(t.Battle.HitsAllEnemiesNow("焚", true), Is.True, "前提:焚的攻击面打全体");
            var casts = Casts(t.Generate(), "焚", true);
            Assert.That(casts.Count, Is.EqualTo(1));
            Assert.That(casts[0].TargetIndex, Is.EqualTo(-1));
        }

        [Test]
        public void DuplicateLibraryChar_ListedOnce()
        {
            var t = new TestBattle(new[] { "炎", "炎" }, enemies: 1);
            Assert.That(Casts(t.Generate(), "炎", true).Count, Is.EqualTo(1));
        }

        [Test]
        public void HealFace_ListsPlayerAndEachLiveSummon()
        {
            var t = new TestBattle(new[] { "冰" }, enemies: 1, summons: new[] { Fixture.WoodSummon(1) });
            Assert.That(BattleEngine.NeedsAllyTarget(Fixture.Graph.Get("冰"), false), Is.True, "前提:冰的五行面选友方");
            var slots = Casts(t.Generate(), "冰", false).Select(a => a.AllySlot).OrderBy(i => i).ToList();
            Assert.That(slots, Is.EqualTo(new[] { Targeting.PlayerTarget, 1 }));
        }

        [Test]
        public void WoodFeatureFace_SummonOnEmptySlots_GraftAndReplaceOnLiveSummon()
        {
            var t = new TestBattle(new[] { "林" }, enemies: 1, summons: new[] { Fixture.WoodSummon(0) });
            var feature = Casts(t.Generate(), "林", false);

            var graft = feature.Where(a => a.DropMode == SummonDropMode.Graft).ToList();
            var replace = feature.Where(a => a.DropMode == SummonDropMode.Replace).ToList();
            Assert.That(graft.Count, Is.EqualTo(1), "活木灵格:嫁接一条");
            Assert.That(graft[0].AllySlot, Is.EqualTo(0));
            Assert.That(replace.Count, Is.EqualTo(1), "活木灵格:替换一条");
            Assert.That(replace[0].AllySlot, Is.EqualTo(0));

            var summon = feature.Where(a => a.DropMode == SummonDropMode.Auto).ToList();
            Assert.That(summon.Count, Is.GreaterThan(0), "空格:召唤");
            foreach (var a in summon)
            {
                Assert.That(a.AllySlot, Is.EqualTo(Targeting.PlayerTarget));
                Assert.That(a.SummonSlots, Is.Not.Null);
                Assert.That(t.Battle.SlotOccupancy(a.SummonSlots[0]), Is.Not.EqualTo(SlotState.Alive), "召唤首格落在空格");
                Assert.That(t.Battle.IsSlotOpen(a.SummonSlots[0]), Is.True);
            }
            int openNonAlive = Enumerable.Range(0, BattleEngine.MaxSummonSlots)
                .Count(s => t.Battle.IsSlotOpen(s) && t.Battle.SlotOccupancy(s) != SlotState.Alive);
            Assert.That(summon.Select(a => a.SummonSlots[0]).Distinct().Count(), Is.EqualTo(openNonAlive), "每个可落空格一条");
        }

        [Test]
        public void WoodFeatureFace_NoLiveSummon_NoGraftNoReplace()
        {
            var t = new TestBattle(new[] { "林" }, enemies: 1);
            var feature = Casts(t.Generate(), "林", false);
            Assert.That(feature.Any(a => a.DropMode != SummonDropMode.Auto), Is.False);
        }

        [Test]
        public void DismantleAndCompose_FollowForgeSuggest()
        {
            var t = new TestBattle(new[] { "林", "炎" }, pool: new[] { "火", "木" }, enemies: 1);
            var all = t.Generate();
            var expectedCompose = ForgeEngine.Suggest(Fixture.Graph, t.Battle.Pool, t.Battle.Library, t.Battle.ComposableChars)
                .Composable.OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.That(expectedCompose.Count, Is.GreaterThan(0), "前提:有可合的字");
            var composes = all.Where(a => a.Kind == ActionKind.Compose).ToList();
            Assert.That(composes.Select(a => a.CharId).OrderBy(s => s, StringComparer.Ordinal).ToList(), Is.EqualTo(expectedCompose));
            Assert.That(composes.All(a => a.DiscardForCompose == null), Is.True, "字库没满,不需腾位");

            var dismantles = all.Where(a => a.Kind == ActionKind.Dismantle).Select(a => a.CharId)
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.That(dismantles, Is.EqualTo(new[] { "林", "炎" }.OrderBy(s => s, StringComparer.Ordinal).ToList()));
        }

        [Test]
        public void Compose_LibraryFull_IsCompoundWithDiscard()
        {
            // 林 = 木 + 木 全取自池,字库已满(容量 2) → 必须先弃一张
            var t = new TestBattle(new[] { "炎", "焚" }, pool: new[] { "木", "木" }, enemies: 1, libraryCapacity: 2);
            var lin = t.Generate().Where(a => a.Kind == ActionKind.Compose && a.CharId == "林").ToList();
            Assert.That(lin.Any(a => a.DiscardForCompose == null), Is.False, "满库不能直接合");
            Assert.That(lin.Select(a => a.DiscardForCompose).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Is.EqualTo(new[] { "焚", "炎" }.OrderBy(s => s, StringComparer.Ordinal).ToList()));
        }

        [Test]
        public void NotEnoughAp_NoCastNoCompose_DismantleAndEndTurnRemain()
        {
            var t = new TestBattle(new[] { "林", "炎" }, pool: new[] { "火", "木" }, enemies: 1, ap: 0);
            var all = t.Generate();
            Assert.That(all.Any(a => a.Kind == ActionKind.Cast), Is.False);
            Assert.That(all.Any(a => a.Kind == ActionKind.Compose), Is.False);
            Assert.That(all.Any(a => a.Kind == ActionKind.Dismantle), Is.True, "拆免 AP");
            Assert.That(all.Count(a => a.Kind == ActionKind.EndTurn), Is.EqualTo(1));
        }

        [Test]
        public void EndTurn_AlwaysExactlyOnce()
        {
            foreach (var t in Scenes())
                Assert.That(t.Generate().Count(a => a.Kind == ActionKind.EndTurn), Is.EqualTo(1));
        }

        [Test]
        public void SummonOntoCorpseSlot_IsListed()
        {
            var t = new TestBattle(new[] { "林" }, enemies: 1,
                summons: new[] { Fixture.WoodSummon(0), Fixture.WoodSummon(1, hp: 0) });
            Assert.That(t.Battle.SlotOccupancy(1), Is.EqualTo(SlotState.Corpse), "前提:槽 1 是尸体");
            var firstSlots = Casts(t.Generate(), "林", false).Where(a => a.DropMode == SummonDropMode.Auto)
                .Select(a => a.SummonSlots[0]).ToList();
            Assert.That(firstSlots.Contains(1), Is.True, "尸体格可召唤覆盖");
        }

        [Test]
        public void PoolComponent_IsCastable()
        {
            var t = new TestBattle(Array.Empty<string>(), pool: new[] { "火" }, enemies: 2);
            var casts = t.Generate().Where(a => a.Kind == ActionKind.Cast && a.CharId == "火").ToList();
            Assert.That(casts.Count, Is.GreaterThan(0));
            Assert.That(casts.All(a => a.LibraryIndex == -1), Is.True);
        }

        [Test]
        public void EveryCandidate_AppliesCleanlyOnACopy()
        {
            foreach (var t in Scenes())
            {
                var all = t.Generate();
                Assert.That(all.Count, Is.GreaterThan(1));
                foreach (var a in all)
                {
                    var copy = t.Clone();
                    int apBefore = copy.Ap;
                    Assert.That(a.ApplyTo(copy), Is.EqualTo(BattleError.None),
                        $"{a.Kind} {a.CharId} atk={a.AttackMode} tgt={a.TargetIndex} ally={a.AllySlot} drop={a.DropMode} discard={a.DiscardForCompose}");
                    if (a.Kind == ActionKind.Cast)
                        Assert.That(copy.Ap, Is.LessThan(apBefore), "出字真的扣了 AP(不是被静默拒掉)");
                }
            }
        }

        [Test]
        public void Generate_DoesNotTouchTheBattle()
        {
            foreach (var t in Scenes())
            {
                var before = t.Battle.Capture();
                t.Generate();
                var after = t.Battle.Capture();
                Assert.That((after.Ap, after.RandomState, after.TargetRandomState, after.TraitRandomState, after.PlayerHp),
                    Is.EqualTo((before.Ap, before.RandomState, before.TargetRandomState, before.TraitRandomState, before.PlayerHp)));
                Assert.That(after.Library, Is.EqualTo(before.Library));
                Assert.That(after.Pool, Is.EqualTo(before.Pool));
                Assert.That(after.Summons.Count, Is.EqualTo(before.Summons.Count));
            }
        }

        private static IEnumerable<TestBattle> Scenes()
        {
            yield return new TestBattle(new[] { "冰", "炎", "焚" }, enemies: 3);
            yield return new TestBattle(new[] { "林", "冰" }, enemies: 2, summons: new[] { Fixture.WoodSummon(0) });
            yield return new TestBattle(new[] { "林", "炎" }, pool: new[] { "火", "木" }, enemies: 1);
            yield return new TestBattle(new[] { "炎", "焚" }, pool: new[] { "木", "木" }, enemies: 1, libraryCapacity: 2);
            yield return new TestBattle(new[] { "林", "炎" }, pool: new[] { "火" }, enemies: 2, ap: 0);
            yield return new TestBattle(Array.Empty<string>(), pool: new[] { "火" }, enemies: 2);
            // 召唤位全满:只能嫁接 / 替换,普通召唤不列
            yield return new TestBattle(new[] { "林", "森" }, enemies: 1,
                summons: Enumerable.Range(0, BattleEngine.MaxSummonSlots).Select(s => Fixture.WoodSummon(s)).ToArray());
            // 有尸体占槽:召唤可落尸体格
            yield return new TestBattle(new[] { "林", "冰" }, enemies: 1,
                summons: new[] { Fixture.WoodSummon(0), Fixture.WoodSummon(1, hp: 0) });
            // 全部可出牌字各一张(字库容量放开),兜住各类面与落点;Lv1 / Lv5 / Lv8 三档(特性会改形状、加召唤、加复活)
            var cards = Fixture.Graph.All.Where(d => !d.IsComponent && d.Effects.Count > 0).Select(d => d.Id).ToArray();
            foreach (int level in new[] { 1, 5, 8 })
                yield return new TestBattle(cards, enemies: 3, libraryCapacity: cards.Length + 2, cardLevel: level,
                    summons: new[] { Fixture.WoodSummon(0), Fixture.WoodSummon(3), Fixture.WoodSummon(4, hp: 0) });
        }
    }
}
