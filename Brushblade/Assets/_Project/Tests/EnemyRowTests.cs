using System.Collections.Generic;
using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>敌方排位(2026-08-20):Row/Range/Focus 三个正交字段,
    /// 每排上限 <see cref="Targeting.RowCapacity"/>(2026-08-27:3 → 4),溢出改判到另一排。</summary>
    [TestFixture]
    public class EnemyRowTests
    {
        private static BattleEngine MakeEngine(params EnemyDef[] enemies)
        {
            var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            return new BattleEngine(graph, config, new string[0], new string[0],
                new List<EnemyDef>(enemies), seed: 1);
        }

        private static EnemyDef Mob(string id, EnemyRow row = EnemyRow.Front) =>
            new(id, Element.Earth, 100, 0, row: row);

        /// <summary>召唤字「梅」:1 只 200 血、攻 0 的召唤物。攻 0 是为了让它绝不反击,
        /// 敌人血量因此恒定,断言只需要盯玩家与召唤物的血。</summary>
        private static RecipeGraph SummonGraph() => new(new[]
        {
            new CharDef("梅", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 200,
                    summonCount: 1, summonAttack: 0, summonChar: "梅") }),
        });

        /// <summary>「梅」放部件池里直出(叶子字免配方),敌人由调用方给。</summary>
        private static BattleEngine SummonEngine(params EnemyDef[] enemies) =>
            new(SummonGraph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
                new string[0], new[] { "梅", "梅", "梅", "梅" }, enemies, seed: 1);

        [Test]
        public void EnemyDef_DefaultsToFrontMeleeDefault()
        {
            var def = new EnemyDef("错字鬼", Element.Wood, 140, 40);
            Assert.That(def.Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(def.Range, Is.EqualTo(AttackRange.Melee));
            Assert.That(def.Focus, Is.EqualTo(AttackFocus.Default));
        }

        [Test]
        public void Rows_HonourPreference_WhenWithinCap()
        {
            var engine = MakeEngine(Mob("a"), Mob("b", EnemyRow.Back), Mob("c"));
            Assert.That(engine.Enemies[0].Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(engine.Enemies[1].Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(engine.Enemies[2].Row, Is.EqualTo(EnemyRow.Front));
        }

        [Test]
        public void Rows_OverflowToTheOtherRow_WhenPreferredIsFull()
        {
            // 五只都想站后排,后排只有 4 格 —— 第五只改判前排
            var engine = MakeEngine(Mob("a", EnemyRow.Back), Mob("b", EnemyRow.Back),
                Mob("c", EnemyRow.Back), Mob("d", EnemyRow.Back), Mob("e", EnemyRow.Back));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(back, Is.EqualTo(Targeting.RowCapacity), "后排不超过每排上限");
            Assert.That(front, Is.EqualTo(1), "溢出的改判前排");
            Assert.That(engine.Enemies[4].Row, Is.EqualTo(EnemyRow.Front), "改判的是排在后面的那只");
        }

        [Test]
        public void EightEnemies_NeverExceedRowCapacityPerRow()
        {
            var engine = MakeEngine(Mob("a"), Mob("b"), Mob("c"), Mob("d"),
                Mob("e"), Mob("f"), Mob("g"), Mob("h"));
            int back = 0, front = 0;
            foreach (var e in engine.Enemies)
                if (e.Row == EnemyRow.Back) back++; else front++;
            Assert.That(front, Is.EqualTo(Targeting.RowCapacity));
            Assert.That(back, Is.EqualTo(Targeting.RowCapacity));
        }

        [Test]
        public void Scale_PreservesRowRangeFocus()
        {
            var def = new EnemyDef("悬针", Element.Metal, 90, 45,
                row: EnemyRow.Back, range: AttackRange.Ranged, focus: AttackFocus.Player);
            var scaled = CampaignConfig.Scale(def, 2.0f);
            Assert.That(scaled.Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(scaled.Range, Is.EqualTo(AttackRange.Ranged));
            Assert.That(scaled.Focus, Is.EqualTo(AttackFocus.Player));
            Assert.That(scaled.MaxHp, Is.GreaterThan(90), "缩放本身照常生效");
        }

        [Test]
        public void MeleeEnemy_IsBlockedByFrontRow()
        {
            var engine = SummonEngine(new EnemyDef("错字鬼", Element.Wood, 500, 40));
            engine.Cast("梅", summonSlots: new[] { 1 });
            int playerBefore = engine.PlayerHp;
            int summonBefore = engine.Summons[1].Hp;
            engine.EndTurn();
            Assert.That(engine.Summons[1].Hp, Is.LessThan(summonBefore), "前排替玩家挨了这一下");
            Assert.That(engine.PlayerHp, Is.EqualTo(playerBefore), "玩家一滴不掉");
        }

        [Test]
        public void RangedEnemy_SkipsFrontRow()
        {
            // 后排没人 → 远程的候选池只剩玩家 → 必打玩家(确定性,不摇随机)
            var sniper = new EnemyDef("墨溅", Element.Water, 500, 40,
                row: EnemyRow.Back, range: AttackRange.Ranged);
            var engine = SummonEngine(sniper);
            engine.Cast("梅", summonSlots: new[] { 0 });
            int playerBefore = engine.PlayerHp;
            int summonBefore = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(summonBefore), "前排被整个跳过");
            Assert.That(engine.PlayerHp, Is.LessThan(playerBefore));
        }

        [Test]
        public void MeleeAssassin_DivesForPlayer_WhenFrontIsEmpty()
        {
            var assassin = new EnemyDef("败笔", Element.Fire, 500, 40, focus: AttackFocus.Player);
            var engine = SummonEngine(assassin);
            engine.Cast("梅", summonSlots: new[] { 4 });   // 只站后排,前排空
            int playerBefore = engine.PlayerHp;
            int summonBefore = engine.Summons[4].Hp;
            engine.EndTurn();
            Assert.That(engine.Summons[4].Hp, Is.EqualTo(summonBefore), "后排还有人也不管");
            Assert.That(engine.PlayerHp, Is.LessThan(playerBefore));
        }

        [Test]
        public void MeleeAssassin_IsStillBlockedByFrontRow()
        {
            var assassin = new EnemyDef("败笔", Element.Fire, 500, 40, focus: AttackFocus.Player);
            var engine = SummonEngine(assassin);
            engine.Cast("梅", summonSlots: new[] { 0 });
            int playerBefore = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(playerBefore), "刺客也得先过前排这一关");
            Assert.That(engine.Summons[0].Hp, Is.LessThan(200));
        }

        // ---- 分裂克隆继承排位(2026-08-20,spec §3.3) ----

        private static RecipeGraph SplitGraph() => new(new[]
        {
            new CharDef("火", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 40) }),
        });

        private static EnemyDef Splitter(EnemyRow row) =>
            new("叠字怪", Element.Wood, 160, 50, EnemyAbility.Split, row: row);

        private static BattleEngine SplitEngine(params EnemyDef[] enemies) =>
            new(SplitGraph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
                new[] { "火" }, new string[0], enemies, seed: 1);

        [Test]
        public void Split_CloneInheritsBackRow_WhenBackRowNotFull()
        {
            // 母体独自站后排(1 只 < 上限 3);前排从未有过,单体直伤全场可点(不受排位限制)
            var engine = SplitEngine(Splitter(EnemyRow.Back));
            engine.Cast("火", 0); // 火 vs 木中立 → 40 伤,160→120,存活分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(2));
            Assert.That(engine.Enemies[1].Row, Is.EqualTo(EnemyRow.Back), "后排未满,克隆应跟着母体落后排");
        }

        [Test]
        public void Split_CloneFallsToFrontRow_WhenBackRowIsFull()
        {
            // 四只都站后排,恰好占满每排上限;前排从未有过,直伤照样够得着
            var engine = SplitEngine(Splitter(EnemyRow.Back), Splitter(EnemyRow.Back),
                Splitter(EnemyRow.Back), Splitter(EnemyRow.Back));
            engine.Cast("火", 0); // 打第一只,存活分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(5));
            Assert.That(engine.Enemies[4].Row, Is.EqualTo(EnemyRow.Front), "后排已满 4,克隆改判前排");
        }

        // ---- 三只排位怪入库(2026-08-20) ----

        private static string ConfigDir()
        {
            // ⚠ 锚点必须是 TestContext.CurrentContext.TestDirectory,不能用 AppContext.BaseDirectory
            // (后者在 Unity Test Runner 下指向编辑器安装目录,见 DefenseValuesTests 的注释)。
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static EnemyDef FindInPools(EndlessConfig endless, string id)
        {
            foreach (var band in endless.Bands)
                foreach (var enemy in band.EnemyPool)
                    if (enemy.Id == id) return enemy;
            return null;
        }

        [Test]
        public void RealConfig_HasThreeRowAwareMobs()
        {
            var configDir = ConfigDir();
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var moJian = FindInPools(campaign.Endless, "墨溅");
            Assert.That(moJian, Is.Not.Null, "墨溅应已入库到某个层段怪池");
            Assert.That(moJian.Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(moJian.Range, Is.EqualTo(AttackRange.Ranged));
            Assert.That(moJian.Focus, Is.EqualTo(AttackFocus.Default));

            var xuanZhen = FindInPools(campaign.Endless, "悬针");
            Assert.That(xuanZhen, Is.Not.Null, "悬针应已入库到某个层段怪池");
            Assert.That(xuanZhen.Row, Is.EqualTo(EnemyRow.Back));
            Assert.That(xuanZhen.Range, Is.EqualTo(AttackRange.Ranged));
            Assert.That(xuanZhen.Focus, Is.EqualTo(AttackFocus.Player));

            var baiBi = FindInPools(campaign.Endless, "败笔");
            Assert.That(baiBi, Is.Not.Null, "败笔应已入库到某个层段怪池");
            Assert.That(baiBi.Row, Is.EqualTo(EnemyRow.Front));
            Assert.That(baiBi.Range, Is.EqualTo(AttackRange.Melee));
            Assert.That(baiBi.Focus, Is.EqualTo(AttackFocus.Player));
        }

        [Test]
        public void BuildFloor_AlwaysStartsWithAFrontRowMob()
        {
            var configDir = ConfigDir();
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            for (int depth = 1; depth <= 200; depth++)
            {
                if (campaign.Endless.IsBossDepth(depth)) continue;
                var floor = EndlessGenerator.BuildFloor(campaign.Endless, depth,
                    EndlessGenerator.FloorRandom(seed: 12345, depth));
                Assert.That(floor[0].Row, Is.EqualTo(EnemyRow.Front), $"第 {depth} 层首位必须是前排");
            }
        }

        [Test]
        public void BuildFloor_NeverPutsMoreThanRowCapacityInEitherRow()
        {
            var configDir = ConfigDir();
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            for (int depth = 1; depth <= 200; depth++)
            {
                if (campaign.Endless.IsBossDepth(depth)) continue;
                var floor = EndlessGenerator.BuildFloor(campaign.Endless, depth,
                    EndlessGenerator.FloorRandom(seed: 54321, depth));
                var engine = new BattleEngine(graph,
                    new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
                    new string[0], new string[0], floor, seed: 1);

                int back = 0, front = 0;
                foreach (var e in engine.Enemies)
                    if (e.Row == EnemyRow.Back) back++; else front++;
                Assert.That(back, Is.LessThanOrEqualTo(Targeting.RowCapacity), $"第 {depth} 层后排超员");
                Assert.That(front, Is.LessThanOrEqualTo(Targeting.RowCapacity), $"第 {depth} 层前排超员");
            }
        }

        // ---- 列坐标(2026-08-22,spec §6.1) ----

        /// <summary>列坐标(2026-08-22,spec §6.1):每排内按占位顺序 0..RowCapacity−1。
        /// 贯穿形状与 UI 固定格位都读它。</summary>
        [Test]
        public void AssignSlots_GivesEachEnemyAColumnWithinItsRow()
        {
            var engine = MakeEngine(Mob("前1"), Mob("前2"), Mob("前3"), Mob("前4"),
                Mob("后1", EnemyRow.Back));

            var front = new List<int>();
            var back = new List<int>();
            foreach (var e in engine.Enemies)
                (e.Row == EnemyRow.Front ? front : back).Add(e.Column);

            Assert.That(front, Is.EquivalentTo(new[] { 0, 1, 2, 3 }), "前排四只各占一列,不重号");
            Assert.That(back, Is.EquivalentTo(new[] { 1 }),
                "2026-08-30 居中往外:单怪落列 1(ColumnOrder{1,2,0,3}的第一个),不再是列 0");
        }

        [Test]
        public void AssignSlots_OverflowToOtherRow_StillGetsFreeColumn()
        {
            // 第 5 只偏好前排但前排已满 → 改判后排,列号要在**后排**里重新算,不能沿用前排的计数
            var engine = MakeEngine(Mob("前1"), Mob("前2"), Mob("前3"), Mob("前4"), Mob("前5"));

            var back = new List<int>();
            foreach (var e in engine.Enemies)
            {
                Assert.That(e.Column, Is.InRange(0, Targeting.RowCapacity - 1));
                if (e.Row == EnemyRow.Back) back.Add(e.Column);
            }
            Assert.That(back, Is.EquivalentTo(new[] { 1 }),
                "被改判到后排的那只从后排的空列起算 —— 2026-08-30 居中往外后空排的第一个空列是 1,不是 0");
        }

        [Test]
        public void AssignSlots_FillsBothRowsToFourBeforeAnyColumnRepeats()
        {
            // 2026-08-27 四列改造:8 只怪恰好排满 4 + 4,列号在各自排里 0..3 不重号
            var engine = MakeEngine(Mob("前1"), Mob("前2"), Mob("前3"), Mob("前4"),
                Mob("后1", EnemyRow.Back), Mob("后2", EnemyRow.Back),
                Mob("后3", EnemyRow.Back), Mob("后4", EnemyRow.Back));

            var front = new List<int>();
            var back = new List<int>();
            foreach (var e in engine.Enemies)
                (e.Row == EnemyRow.Front ? front : back).Add(e.Column);

            Assert.That(front, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            Assert.That(back, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        }

        /// <summary>同一个 DefId 的两只怪可能站不同列,而 Restore 是按 Id 查 Def 的 ——
        /// 不进快照就会在读档时被合并成同一列(与 Row 同一条理由)。</summary>
        [Test]
        public void EnemyColumn_SurvivesSnapshotRoundTrip()
        {
            var a = Mob("甲");
            var b = Mob("乙");
            var engine = MakeEngine(a, b);
            var before = new List<int>();
            foreach (var e in engine.Enemies) before.Add(e.Column);

            var graph = new RecipeGraph(new List<CharDef> { new("木", Element.Wood) });
            var config = new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) };
            var defs = new Dictionary<string, EnemyDef> { [a.Id] = a, [b.Id] = b };
            var restored = BattleEngine.Restore(engine.Capture(), graph, config, null, defs);

            var after = new List<int>();
            foreach (var e in restored.Enemies) after.Add(e.Column);
            Assert.That(after, Is.EqualTo(before));
        }
    }
}
