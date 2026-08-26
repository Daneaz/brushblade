using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>定向裁定(2026-08-20,spec §4.1)。纯函数,不碰引擎状态。</summary>
    public class TargetingTests
    {
        private const int FrontRow = 3;

        /// <summary>摆一个 6 槽阵:aliveSlots 里的槽放一只满血召唤物,其余留空。</summary>
        private static SummonState[] Line(params int[] aliveSlots)
        {
            var slots = new SummonState[6];
            foreach (int s in aliveSlots)
                slots[s] = new SummonState($"木{s}", Element.Wood, 100, 10);
            return slots;
        }

        [Test]
        public void Melee_HitsFrontmostFrontRowSummon()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                Line(1, 2, 4), FrontRow, new GameRandom(1)), Is.EqualTo(1),
                "前排里槽序最小的那只");
        }

        [Test]
        public void Melee_IgnoresCorpsesInFrontRow()
        {
            var line = Line(2);
            line[0] = new SummonState("尸", Element.Wood, 0, 0); // Hp 0 = 尸体,占槽但不挡刀
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                line, FrontRow, new GameRandom(1)), Is.EqualTo(2));
        }

        [Test]
        public void Melee_WithEmptyFront_PicksUniformlyFromPoolNotCoinFlip()
        {
            // 候选池 = 后排存活召唤物(4、5)∪ 玩家,spec §4.1 要求三个候选放进**同一个池**均匀抽一个,
            // 而不是先五五开决定「打后排还是打玩家」、再在后排里抽。后一种实现在 200 次抽样下
            // 同样能让 seen 摸到全部 3 个候选,单看「三个都出现过」测不出这条区别——
            // 必须直接数 PlayerTarget 出现的次数:三选一期望约 1/3,五五开期望约 1/2。
            var seen = new HashSet<int>();
            int playerHits = 0;
            var random = new GameRandom(7);
            for (int i = 0; i < 200; i++)
            {
                int target = Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                    Line(4, 5), FrontRow, random);
                seen.Add(target);
                if (target == Targeting.PlayerTarget) playerHits++;
            }
            Assert.That(seen.Count, Is.EqualTo(3), "三个候选都摇得到,一个不多");
            Assert.That(seen.Contains(4), Is.True);
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
            // 200 次里期望值:三选一 ≈ 67,五五开 ≈ 100。带宽给宽,别让偶发方差拍红。
            Assert.That(playerHits, Is.LessThan(90), "玩家挨打概率应贴近 1/3,不是五五开的 1/2");
        }

        [Test]
        public void Melee_WithNothingLeft_HitsPlayer()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                Line(), FrontRow, new GameRandom(1)), Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void MeleeFocusPlayer_IsStillBlockedByFrontRow()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Player,
                Line(0, 4), FrontRow, new GameRandom(1)), Is.EqualTo(0), "前排还在就拦得住");
        }

        [Test]
        public void MeleeFocusPlayer_WithEmptyFront_AlwaysHitsPlayer()
        {
            var random = new GameRandom(3);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Player,
                    Line(4, 5), FrontRow, random), Is.EqualTo(Targeting.PlayerTarget),
                    "后排还有人也不管,死盯玩家");
        }

        [Test]
        public void Ranged_IgnoresFrontRow()
        {
            var seen = new HashSet<int>();
            var random = new GameRandom(11);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default,
                    Line(0, 1, 2, 5), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(2), "前排三只全被跳过");
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
        }

        [Test]
        public void RangedFocusPlayer_AlwaysHitsPlayer()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Player,
                Line(0, 1, 4), FrontRow, new GameRandom(1)), Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void BlockedByFrontRow_ConsumesNoRandomness()
        {
            // 前排有人 → FirstAliveSlot 命中后提前返回,根本走不到候选池构造那一步。
            // 用 Line() 全空阵去测「不摇随机数」是重言式:候选池此时必然退化成
            // {PlayerTarget} 一个元素,pool.Count == 1 恒真,测不出短路是否被删掉。
            // 这里前排槽 0 有人、后排槽 4/5 各有人——候选池若真被构造出来会有
            // {4, 5, PlayerTarget} 三个候选,Next(3) 会真的推进 _state。
            // 谁把「先判前排提前返回」重构成「先建池再判前排」,这条就会红。
            var a = new GameRandom(42);
            var b = new GameRandom(42);
            Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default, Line(0, 4, 5), FrontRow, a);
            Assert.That(a.Next(1000), Is.EqualTo(b.Next(1000)), "被前排拦下时一个随机数都没消耗");
        }

        [Test]
        public void FrontmostSummon_PrefersFrontRowThenBack()
        {
            Assert.That(Targeting.FrontmostSummon(Line(2, 3), FrontRow), Is.EqualTo(2));
            Assert.That(Targeting.FrontmostSummon(Line(4, 5), FrontRow), Is.EqualTo(4), "前排空则取后排");
            Assert.That(Targeting.FrontmostSummon(Line(), FrontRow), Is.EqualTo(-1));
        }

        /// <summary>四张叶子字直出:剑=纯直伤、刺=带偷袭的直伤、藤=纯冻结、湮=直伤+驱散(混合字)。</summary>
        private static RecipeGraph DamageGraph() => new(new[]
        {
            new CharDef("剑", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.DamageSingle, 50) }),
            new CharDef("刺", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.DamageSingle, 50, canStrikeBackline: true) }),
            new CharDef("藤", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.Freeze, 2) }),
            new CharDef("湮", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.DamageSingle, 20), new EffectDef(EffectKind.Dispel, 1) }),
        });

        /// <summary>前甲(厚)/ 前乙(40 血,一剑即死)/ 后手。敌人攻 0,不会回手。</summary>
        private static BattleEngine Trio() => new(DamageGraph(),
            new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
            new string[0], new[] { "剑", "剑", "刺", "藤", "湮" },
            new[]
            {
                new EnemyDef("前甲", Element.Heart, 400, 0),
                new EnemyDef("前乙", Element.Heart, 40, 0),
                new EnemyDef("后手", Element.Heart, 400, 0, row: EnemyRow.Back),
            }, seed: 1);

        [Test]
        public void Cast_SingleDamage_RejectsBackRow_WhileTwoFrontAlive()
        {
            var engine = Trio();
            int ap = engine.Ap;
            int backHp = engine.Enemies[2].Hp;
            Assert.That(engine.Cast("剑", 2), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.Enemies[2].Hp, Is.EqualTo(backHp), "被拒的这次一点伤害也不该落下");
            Assert.That(engine.Ap, Is.EqualTo(ap), "AP 不扣");
        }

        [Test]
        public void Cast_ControlEffect_ReachesBackRow_EvenWithFrontAlive()
        {
            var engine = Trio();
            Assert.That(engine.Cast("藤", 2), Is.EqualTo(BattleError.None), "控制类不受排位限制");
            Assert.That(engine.Enemies[2].Statuses.Has(StatusKind.Freeze), Is.True);
        }

        [Test]
        public void Cast_BackstabDamage_ReachesBackRow()
        {
            var engine = Trio();
            Assert.That(engine.Cast("刺", 2), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[2].Hp, Is.LessThan(400), "偷袭字够得着后排");
        }

        [Test]
        public void Cast_MixedCard_TakesTheStrictestRule()
        {
            var engine = Trio();
            Assert.That(engine.Cast("湮", 2), Is.EqualTo(BattleError.InvalidTarget),
                "含单体直伤就受限,哪怕它还带一条驱散");
        }

        [Test]
        public void Cast_AutoLocks_WhenExactlyOneLegalTargetRemains()
        {
            var engine = Trio();
            engine.Cast("剑", 1);                       // 50 伤打死 40 血的前乙
            Assert.That(engine.Enemies[1].Alive, Is.False);
            // 现在存活的有两只(前甲、后手),但**合法的**只有前甲一只 → 不指定目标应自动锁它
            Assert.That(engine.Cast("剑"), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(350), "自动锁的是前甲");
            Assert.That(engine.Enemies[2].Hp, Is.EqualTo(400), "后手没被碰到");
        }

        private static RecipeGraph SummonRangeGraph() => new(new[]
        {
            new CharDef("松", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 30, summonChar: "松") }),
            new CharDef("灶", Element.Heart, effects: new[] {
                new EffectDef(EffectKind.Summon, 200, summonCount: 1, summonAttack: 30, summonChar: "灶",
                    passive: new SummonPassive { Ranged = true }) }),
        });

        /// <summary>一前一后两只怪(攻 0),字库里放指定的召唤字。</summary>
        private static BattleEngine SummonRangeDuel(string summonChar) => new(
            SummonRangeGraph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
            new string[0], new[] { summonChar, summonChar },
            new[]
            {
                new EnemyDef("前卫", Element.Heart, 400, 0),
                new EnemyDef("后手", Element.Heart, 400, 0, row: EnemyRow.Back),
            }, seed: 1);

        [Test]
        public void MeleeSummon_HitsFrontRow()
        {
            var engine = SummonRangeDuel("松");
            engine.Cast("松", summonSlots: new[] { 0 });
            engine.EndTurn();   // 新召唤物上场即满格,这一拍就出手
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(400), "近战打前排");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(400), "后排一滴不掉");
        }

        [Test]
        public void RangedSummon_PrefersBackRow()
        {
            var engine = SummonRangeDuel("灶");
            engine.Cast("灶", summonSlots: new[] { 0 });
            engine.EndTurn();
            Assert.That(engine.Enemies[1].Hp, Is.LessThan(400), "远程越过前排点后排");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(400));
        }

        /// <summary>前排两只都是 40 血(一剑即死),专为「打光前排后能不能够到后排」这条钉设的
        /// 夹具——不复用 Trio()(前甲 400 血,得多发好几张「剑」外加跨回合才能打穿,拖慢用例
        /// 却不改变判别力)。</summary>
        private static BattleEngine ThinFrontTrio() => new(DamageGraph(),
            new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
            new string[0], new[] { "剑", "剑", "剑" },
            new[]
            {
                new EnemyDef("前甲", Element.Heart, 40, 0),
                new EnemyDef("前乙", Element.Heart, 40, 0),
                new EnemyDef("后手", Element.Heart, 400, 0, row: EnemyRow.Back),
            }, seed: 1);

        [Test]
        public void Cast_SingleDamage_ReachesBackRow_AfterFrontRowCleared()
        {
            // CanPlayerHit 里 FirstAliveInRow(Front) < 0 的取反分支——「前排已清空」与
            // 「前排从未有过」被同等对待——此前全仓库零覆盖,评审 Minor 破格补上。
            var engine = ThinFrontTrio();
            Assert.That(engine.Cast("剑", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Cast("剑", 1), Is.EqualTo(BattleError.None));
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Enemies[1].Alive, Is.False);
            int backHp = engine.Enemies[2].Hp;
            Assert.That(engine.Cast("剑", 2), Is.EqualTo(BattleError.None), "前排已清空,单体直伤该够得着后排");
            Assert.That(engine.Enemies[2].Hp, Is.LessThan(backHp));
        }

        /// <summary>整场没有前排(两只都是 EnemyRow.Back),钉「前排从未有过」这条口径——
        /// 与上面「前排已清空」是 CanPlayerHit 里同一句判断的两种成因,分开钉才不会漏。</summary>
        private static BattleEngine AllBackRowDuel() => new(DamageGraph(),
            new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
            new string[0], new[] { "剑" },
            new[]
            {
                new EnemyDef("后甲", Element.Heart, 400, 0, row: EnemyRow.Back),
                new EnemyDef("后乙", Element.Heart, 400, 0, row: EnemyRow.Back),
            }, seed: 1);

        [Test]
        public void Cast_SingleDamage_ReachesBackRow_WhenFrontRowNeverExisted()
        {
            var engine = AllBackRowDuel();
            int hp = engine.Enemies[0].Hp;
            Assert.That(engine.Cast("剑", 0), Is.EqualTo(BattleError.None), "整场没有前排,直伤全场可点");
            Assert.That(engine.Enemies[0].Hp, Is.LessThan(hp));
        }

        /// <summary>摆一个敌阵:每项 (row, column),按顺序建满血敌人。</summary>
        private static List<EnemyState> Grid(params (EnemyRow Row, int Column)[] slots)
        {
            var list = new List<EnemyState>();
            foreach (var (row, column) in slots)
            {
                var def = new EnemyDef($"怪{list.Count}", Element.Heart, 100, 10, row: row);
                var state = new EnemyState(def, 0, null) { Row = row, Column = column };
                list.Add(state);
            }
            return list;
        }

        [Test]
        public void Expand_Single_ReturnsOnlyPrimary()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1));
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Single, 0),
                Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Expand_Sweep_TakesWholeRow_PrimaryFirst()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1),
                (EnemyRow.Front, 2), (EnemyRow.Back, 0));
            var hit = Targeting.ExpandTargets(grid, 1, TargetShape.Sweep, 0);
            Assert.That(hit[0], Is.EqualTo(1), "首项恒为主目标");
            Assert.That(hit, Is.EquivalentTo(new[] { 0, 1, 2 }), "整排三只,后排那只不中");
        }

        [Test]
        public void Expand_Cleave_TakesAdjacentColumnsOnly()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1), (EnemyRow.Front, 2));
            Assert.That(Targeting.ExpandTargets(grid, 1, TargetShape.Cleave, 0),
                Is.EquivalentTo(new[] { 0, 1, 2 }), "打中间:两侧都溅到");
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Cleave, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "打边格:只溅一侧,不递补");
        }

        [Test]
        public void Expand_Cleave_DoesNotJumpOverEmptyColumn()
        {
            // 1 号列空着:0 号打不到 2 号 —— 形状是几何,不是「保证打满 K 个」
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 2));
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Cleave, 0),
                Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Expand_Skewer_TakesColumnAcrossRows()
        {
            var grid = Grid((EnemyRow.Front, 1), (EnemyRow.Back, 1), (EnemyRow.Back, 0));
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Skewer, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "同列的前后两只,别的列不中");
        }

        [Test]
        public void Expand_SkipsCorpses()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1), (EnemyRow.Front, 2));
            grid[2].Hp = 0;
            Assert.That(Targeting.ExpandTargets(grid, 1, TargetShape.Sweep, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "尸体不吃形状伤害");
        }

        [Test]
        public void Expand_Volley_PrefersBackRowByColumn()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Back, 1), (EnemyRow.Back, 0));
            Assert.That(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 3),
                Is.EqualTo(new[] { 2, 1, 0 }), "后排按列序在先(列 0 的下标 2、列 1 的下标 1),再轮到前排");
        }

        [Test]
        public void Expand_Volley_CyclesWhenTargetsFewerThanShots()
        {
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1));
            Assert.That(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 4),
                Is.EqualTo(new[] { 0, 1, 0, 1 }), "不足 N 循环补足,表里允许重复下标");
        }

        [Test]
        public void Expand_Volley_SoleEnemy_TakesAllShots()
        {
            // 单敌 Boss 战:连发退化为满额 N 倍单体(spec §3.3 的已知后果,配值时按 N 发全中定基础值)
            var grid = Grid((EnemyRow.Front, 0));
            Assert.That(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 3),
                Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void Expand_Volley_NoAliveEnemy_ReturnsEmpty()
        {
            var grid = Grid((EnemyRow.Front, 0));
            grid[0].Hp = 0;
            Assert.That(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 3), Is.Empty);
        }

        [Test]
        public void Expand_IsDeterministic()
        {
            // 形状展开必须是确定性的:同一输入连续两次调用返回完全相同的表。
            // 「不消耗外部随机流」这条更强的保证由方法签名结构性给出(ExpandTargets 不接受
            // GameRandom,本代码库也没有全局 RNG),真正可观测的守卫是 Task 4 接进引擎后
            // 「既有全量测试一条不红」—— 那才跑得出差别,不是这一层能断言的东西。
            var grid = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1), (EnemyRow.Back, 0));

            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Sweep, 0),
                Is.EqualTo(Targeting.ExpandTargets(grid, 0, TargetShape.Sweep, 0)));
            Assert.That(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 5),
                Is.EqualTo(Targeting.ExpandTargets(grid, -1, TargetShape.Volley, 5)));
        }
    
        // ---- 排的格位数(2026-08-26):表现层铺几格由这里定,列对齐靠它 ----

        [Test]
        public void RowCells_SingleEnemyEncounter_CollapsesToOneCell()
        {
            // 2026-08-23 实机反馈:全场只有一只怪时铺三格会把它顶到最左。
            // 两排都 ≤1 只时没有对齐对象,各自居中不损失任何信息。
            Assert.That(Targeting.RowCells(1, 0), Is.EqualTo(1));
            Assert.That(Targeting.RowCells(1, 1), Is.EqualTo(1), "两排各一只,双双居中仍然对齐");
        }

        [Test]
        public void RowCells_KeepsFullGridWhenTheOtherRowHasTwoOrMore()
        {
            // 前排 2 只(列 0、1)+ 后排 1 只(列 0):后排若折叠成一格会被居中到视觉第 2 位,
            // 而引擎认定它与前排列 0 同列 —— 贯穿(枪)于是看起来打了错位的一只。
            Assert.That(Targeting.RowCells(1, 2), Is.EqualTo(Targeting.RowCapacity));
            Assert.That(Targeting.RowCells(1, 3), Is.EqualTo(Targeting.RowCapacity));
        }

        [Test]
        public void RowCells_MultiEnemyRow_AlwaysFillsTheGrid()
        {
            Assert.That(Targeting.RowCells(2, 1), Is.EqualTo(Targeting.RowCapacity));
            Assert.That(Targeting.RowCells(3, 3), Is.EqualTo(Targeting.RowCapacity));
            Assert.That(Targeting.RowCells(0, 3), Is.EqualTo(Targeting.RowCapacity), "空排照旧撑满");
            Assert.That(Targeting.RowCells(0, 0), Is.EqualTo(Targeting.RowCapacity));
        }
}
}
