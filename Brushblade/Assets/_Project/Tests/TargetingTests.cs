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

        // ---- 2026-09-13:够得着的那一段里均匀随机 ----

        /// <summary>同 Line,但 tauntSlots 里的槽位带嘲讽被动。
        /// Passive 是只读属性,只能走构造参数(`SummonState(char, element, hp, attack, passive)`)。</summary>
        private static SummonState[] TauntLine(int[] aliveSlots, params int[] tauntSlots)
        {
            var slots = new SummonState[6];
            foreach (int s in aliveSlots)
                slots[s] = new SummonState($"木{s}", Element.Wood, 100, 10,
                    System.Array.IndexOf(tauntSlots, s) >= 0
                        ? new SummonPassive { Taunt = true } : null);
            return slots;
        }

        [Test]
        public void Melee_HitsTheOnlyFrontRowSummon()
        {
            // 前排只有一只时候选池退化成单元素 —— 守的是「近战被前排拦下」,不是「取槽序最小」。
            // 前排多只时的随机分布另有 Melee_PicksRandomlyAmongFrontRowSummons 守。
            Assert.That(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                Line(1, 4), FrontRow, new GameRandom(1)), Is.EqualTo(1),
                "前排那只挡下这一击");
        }

        [Test]
        public void Melee_PicksRandomlyAmongFrontRowSummons()
        {
            // 改前恒打前排槽序最小的那只(槽 1),前排站两只时第二只永远不挨打。
            var seen = new HashSet<int>();
            var random = new GameRandom(5);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                    Line(1, 2, 4), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(2), "前排两只都摇得到,后排那只不该进池");
            Assert.That(seen.Contains(1), Is.True);
            Assert.That(seen.Contains(2), Is.True);
        }

        [Test]
        public void Ranged_CanHitFrontRowSummons()
        {
            // 改前远程的候选池从 frontRow 起算,前排召唤物一次都打不到(spec §0)。
            var seen = new HashSet<int>();
            var random = new GameRandom(11);
            for (int i = 0; i < 300; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default,
                    Line(0, 1, 5), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(4), "前排两只 + 后排一只 + 玩家,全在池里");
            Assert.That(seen.Contains(0), Is.True, "前排槽 0 也该挨得着");
            Assert.That(seen.Contains(1), Is.True);
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
        }

        [Test]
        public void RangedFocusPlayer_IsPulledByFrontRowTaunt()
        {
            // 用户 2026-09-13 第 5 条诉求:锁人要先吃嘲讽,再打玩家。
            // 改前嘲讽扫描区间是 [frontRow, Count),前排的嘲讽者对远程完全不可见。
            var line = TauntLine(new[] { 0, 5 }, 0);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Player,
                    line, FrontRow, new GameRandom(i + 1)), Is.EqualTo(0),
                    "前排嘲讽把锁人拉过去");
        }

        [Test]
        public void Ranged_PicksRandomlyAmongTauntersAcrossBothRows()
        {
            // 两个嘲讽者分居前后排,远程够得着两个 —— 该在两者之间随机,不能恒定一个。
            var line = TauntLine(new[] { 0, 5 }, 0, 5);
            var seen = new HashSet<int>();
            var random = new GameRandom(13);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Default,
                    line, FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(2), "只在两个嘲讽者之间随机,玩家不进池");
            Assert.That(seen.Contains(0), Is.True);
            Assert.That(seen.Contains(5), Is.True);
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
        public void RangedFocusPlayer_AlwaysHitsPlayer()
        {
            Assert.That(Targeting.PickAllyTarget(AttackRange.Ranged, AttackFocus.Player,
                Line(0, 1, 4), FrontRow, new GameRandom(1)), Is.EqualTo(Targeting.PlayerTarget));
        }

        [Test]
        public void BlockedByFrontRow_ConsumesNoRandomness()
        {
            // 前排有人 → AliveSlots(summons, 0, frontRow) 命中,候选段收窄成前排单元素
            // {0},PickOne 单候选不摇随机数。
            // 用 Line() 全空阵去测「不摇随机数」是重言式:候选池此时必然退化成
            // {PlayerTarget} 一个元素,pool.Count == 1 恒真,测不出短路是否被删掉。
            // 这里前排槽 0 有人、后排槽 4/5 各有人——候选段若不按排位收窄、直接建整场
            // 候选池会有 {0, 4, 5, PlayerTarget} 四个候选,Next(4) 会真的推进 _state。
            // 谁把「先按排位收窄候选段」删掉、变成不分排位直接建整场候选池,这条就会红。
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
        public void Cast_BacklineCleave_SplashesWithinTheBackRow()
        {
            // 砸 = 溅射 + 偷袭(2026-09-02)。两个修饰位并存时的落点:偷袭把主目标解放到后排,
            // 溅射再按**主目标所在那一排**取相邻 —— 溅到的是另一只后排怪,不是前排。
            // RestrictedToFrontRow 里 CanStrikeBackline 判在形状之前,这条口径才成立;
            // 谁把那两句调换顺序,或让溅射改按前排取相邻,这条就会红。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("砸", Element.Heart, effects: new[] {
                    new EffectDef(EffectKind.DamageSingle, 50,
                        shape: TargetShape.Cleave, shapePercent: 50, canStrikeBackline: true) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
                new string[0], new[] { "砸" },
                new[]
                {
                    new EnemyDef("前甲", Element.Heart, 400, 0),
                    new EnemyDef("前乙", Element.Heart, 400, 0),
                    new EnemyDef("后甲", Element.Heart, 400, 0, row: EnemyRow.Back),
                    new EnemyDef("后乙", Element.Heart, 400, 0, row: EnemyRow.Back),
                }, seed: 1);

            Assert.That(engine.Cast("砸", 2), Is.EqualTo(BattleError.None), "偷袭字点得动后排");
            Assert.That(engine.Enemies[2].Hp, Is.EqualTo(350), "主目标吃全额");
            Assert.That(engine.Enemies[3].Hp, Is.EqualTo(375), "同排相邻吃 50%");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(400), "前排一点都不该沾");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(400), "前排一点都不该沾");
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

        /// <summary>同 Grid,但每项还指定元素 —— 择伐的三档裁定要用。</summary>
        private static List<EnemyState> ElementGrid(
            params (EnemyRow Row, int Column, Element Element)[] slots)
        {
            var list = new List<EnemyState>();
            foreach (var (row, column, element) in slots)
            {
                var def = new EnemyDef($"怪{list.Count}", element, 100, 10, row: row);
                list.Add(new EnemyState(def, 0, null) { Row = row, Column = column });
            }
            return list;
        }

        // ---- 2026-09-13:召唤物同排随机 ----

        [Test]
        public void SummonMelee_PicksRandomlyAmongFrontRowEnemies()
        {
            // 改前恒取 _enemies 下标序最小的存活者,前排三只时后两只永远不挨打。
            var enemies = Grid((EnemyRow.Front, 0), (EnemyRow.Front, 1), (EnemyRow.Front, 2));
            var seen = new HashSet<int>();
            var random = new GameRandom(17);
            for (int i = 0; i < 300; i++)
                seen.Add(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random));
            Assert.That(seen.Count, Is.EqualTo(3), "前排三只都摇得到");
        }

        [Test]
        public void SummonRanged_PicksRandomlyAmongBackRowEnemies()
        {
            var enemies = Grid((EnemyRow.Back, 0), (EnemyRow.Back, 1));
            var seen = new HashSet<int>();
            var random = new GameRandom(19);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickEnemyTargetForSummon(enemies, ranged: true, random));
            Assert.That(seen.Count, Is.EqualTo(2), "后排两只都摇得到");
        }

        [Test]
        public void SummonMelee_StillPrefersFrontRow()
        {
            // 排位仍然算数:前排还有人时,近战召唤物一次都不该摸到后排。
            var enemies = Grid((EnemyRow.Front, 0), (EnemyRow.Back, 0), (EnemyRow.Back, 1));
            var random = new GameRandom(23);
            for (int i = 0; i < 100; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random),
                    Is.EqualTo(0), "前排那只挡着,近战够不着后排");
        }

        [Test]
        public void SummonTargeting_SingleCandidate_ConsumesNoRandomness()
        {
            // 单候选短路:场上只有一只敌人时一个随机数都不该摇,
            // 否则每一拍召唤物出手都会推着整条择敌流走,分流的意义打折。
            var enemies = Grid((EnemyRow.Front, 0));
            var a = new GameRandom(42);
            var b = new GameRandom(42);
            Targeting.PickEnemyTargetForSummon(enemies, ranged: false, a);
            Assert.That(a.Next(1000), Is.EqualTo(b.Next(1000)), "单候选不消耗随机数");
        }

        [Test]
        public void SummonTargeting_NoEnemies_ReturnsMinusOne()
        {
            Assert.That(Targeting.PickEnemyTargetForSummon(new List<EnemyState>(),
                ranged: false, new GameRandom(1)), Is.EqualTo(-1));
        }

        [Test]
        public void SummonMelee_PreferUnfrozen_RowStillOverridesPreference()
        {
            // 排位压过筛子(2026-09-13 拍板的核心口径):前排唯一那只已冻结,后排有一只
            // 没冻结。筛子只在「排位选定的那一排」内生效——前排在自己这一排里筛不出
            // 未冻结的,退回前排全体,仍然打前排那只被冻的,绝不会为了躲开冻结目标
            // 越排去打后排。若实现退化成「筛子压过排位」(改前的次序),这条会红:
            // 它会跑去打后排那只没冻的。
            var enemies = Grid((EnemyRow.Front, 0), (EnemyRow.Back, 0));
            enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, TurnsLeft = 3,
            });
            var random = new GameRandom(29);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    preferUnfrozen: true), Is.EqualTo(0), "前排那只挡着,即使已冻结也不该越排去打后排没冻的");
        }

        [Test]
        public void SummonMelee_PreferUnslowed_RowStillOverridesPreference()
        {
            // 与上面 PreferUnfrozen 同一形状,盖 preferUnslowed 那条独立代码路径:
            // 前排唯一那只已减速,后排有一只没减速,排位仍然压过筛子,该打前排那只。
            var enemies = Grid((EnemyRow.Front, 0), (EnemyRow.Back, 0));
            enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = 5, SourceId = "测试",
            });
            var random = new GameRandom(31);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    preferUnslowed: true), Is.EqualTo(0), "前排那只挡着,即使已减速也不该越排去打后排没减速的");
        }

        /// <summary>带列宽的阵:span &gt; 1 的怪横跨 [Column, Column + span) 若干列。
        /// Boss 是目前唯一的 span &gt; 1 —— 编成里 Boss 独占一场,所以这些用例是**构造出来**的,
        /// 真机上跑不到。构造它们正是本条测试的意义:等真给 Boss 配了小怪,裁定已经是对的。</summary>
        private static List<EnemyState> SpanGrid(params (EnemyRow Row, int Column, int Span)[] slots)
        {
            var list = new List<EnemyState>();
            foreach (var (row, column, span) in slots)
            {
                var def = new EnemyDef($"怪{list.Count}", Element.Heart, 100, 10,
                    row: row, columnSpan: span);
                list.Add(new EnemyState(def, 0, null) { Row = row, Column = column });
            }
            return list;
        }

        [Test]
        public void Expand_Skewer_SpanningBossOverlapsEveryColumn()
        {
            // Boss 占满前排 4 列;后排三只各站一列 —— 贯穿从任意一只打上去都串到 Boss
            var grid = SpanGrid((EnemyRow.Front, 0, 4), (EnemyRow.Back, 0, 1),
                (EnemyRow.Back, 2, 1), (EnemyRow.Back, 3, 1));
            Assert.That(Targeting.ExpandTargets(grid, 1, TargetShape.Skewer, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "后排第 0 列 + 跨列 Boss");
            Assert.That(Targeting.ExpandTargets(grid, 3, TargetShape.Skewer, 0),
                Is.EquivalentTo(new[] { 0, 3 }), "后排第 3 列同样串到 Boss");
        }

        [Test]
        public void Expand_Cleave_SpanIsAdjacentOnlyAtItsEdge()
        {
            // Boss 占前排 0..1,小怪站前排 2、3:只有第 2 列贴着 Boss 的右缘
            var grid = SpanGrid((EnemyRow.Front, 0, 2), (EnemyRow.Front, 2, 1),
                (EnemyRow.Front, 3, 1));
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Cleave, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "Boss 只溅到贴着它右缘的那只");
            Assert.That(Targeting.ExpandTargets(grid, 1, TargetShape.Cleave, 0),
                Is.EquivalentTo(new[] { 0, 1, 2 }), "夹在中间那只:左邻 Boss、右邻小怪");
        }

        [Test]
        public void Expand_Chain_SpanUsesCenterDistance()
        {
            // Boss 占前排 0..3(中心 1.5),后排第 0 列(中心 0)与第 2 列(中心 2)
            // 到 Boss 的中心距分别是 1.5 与 0.5 —— 第 2 列更近,先跳
            var grid = SpanGrid((EnemyRow.Front, 0, 4), (EnemyRow.Back, 0, 1),
                (EnemyRow.Back, 2, 1));
            var hit = Targeting.ExpandTargets(grid, 0, TargetShape.Chain, 2);
            Assert.That(hit[0], Is.EqualTo(0), "首项恒为主目标");
            Assert.That(hit[1], Is.EqualTo(2), "第 2 列离 Boss 中心更近");
        }

        [Test]
        public void Expand_Sweep_IgnoresSpanEntirely()
        {
            var grid = SpanGrid((EnemyRow.Front, 0, 2), (EnemyRow.Front, 2, 1),
                (EnemyRow.Back, 0, 1));
            Assert.That(Targeting.ExpandTargets(grid, 0, TargetShape.Sweep, 0),
                Is.EquivalentTo(new[] { 0, 1 }), "整排,与占几列无关;后排那只不中");
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
    
        // ---- 列号分配顺序(2026-08-30):居中往外,与 RowCapacity 的守卫 ----

        [Test]
        public void ColumnOrder_CoversEveryColumnExactlyOnce()
        {
            var seen = new HashSet<int>(Targeting.ColumnOrder);
            Assert.That(Targeting.ColumnOrder.Count, Is.EqualTo(Targeting.RowCapacity));
            Assert.That(seen.Count, Is.EqualTo(Targeting.RowCapacity), "不许重号");
            foreach (int c in Targeting.ColumnOrder)
                Assert.That(c >= 0 && c < Targeting.RowCapacity, Is.True, $"列 {c} 越界");
        }

        // ---- 2026-09-13:择伐(木 L4)三档择敌 ----

        [Test]
        public void CounterTargeting_PrefersTheElementItCounters()
        {
            // 木系召唤物:克土,被金克,对火/水/心中立。三档 = 土 > 火 > 金。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Front, 1, Element.Fire), (EnemyRow.Front, 2, Element.Earth));
            var random = new GameRandom(29);
            for (int i = 0; i < 100; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    counterTargeting: Element.Wood), Is.EqualTo(2), "只打被它克的土");
        }

        [Test]
        public void CounterTargeting_FallsToNeutralWhenNoVictimAlive()
        {
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Front, 1, Element.Fire));
            var random = new GameRandom(31);
            for (int i = 0; i < 100; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    counterTargeting: Element.Wood), Is.EqualTo(1), "没有土就打中立的火,不打克它的金");
        }

        [Test]
        public void CounterTargeting_StillAttacksWhenOnlyCounteredRemain()
        {
            // 三档全空不是「不出手」:只剩克它的敌人时照打。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Front, 1, Element.Metal));
            var seen = new HashSet<int>();
            var random = new GameRandom(37);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    counterTargeting: Element.Wood));
            Assert.That(seen.Count, Is.EqualTo(2), "同档两只之间仍然随机");
        }

        [Test]
        public void CounterTargeting_DoesNotOutrankRowPosition()
        {
            // spec §3.3:排位压过三档。相克的土在后排,近战召唤物仍打前排的金。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Back, 0, Element.Earth));
            var random = new GameRandom(41);
            for (int i = 0; i < 100; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    counterTargeting: Element.Wood), Is.EqualTo(0),
                    "不为了追相克敌人越过前排");
        }

        [Test]
        public void CounterTargeting_HeartSummonHasNoPreference()
        {
            // 心不在生克环内 —— 全部敌人同档,退化成均匀随机。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Front, 1, Element.Earth), (EnemyRow.Front, 2, Element.Water));
            var seen = new HashSet<int>();
            var random = new GameRandom(43);
            for (int i = 0; i < 300; i++)
                seen.Add(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    counterTargeting: Element.Heart));
            Assert.That(seen.Count, Is.EqualTo(3), "三只都摇得到");
        }

        [Test]
        public void CounterTargeting_Off_IsUnchanged()
        {
            // 未点择伐(counterTargeting 缺省 null)时,分布与 Task 3 完全一致。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Metal),
                (EnemyRow.Front, 1, Element.Earth));
            var seen = new HashSet<int>();
            var random = new GameRandom(47);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random));
            Assert.That(seen.Count, Is.EqualTo(2), "不挑元素,两只都摇得到");
        }

        [Test]
        public void CounterTargeting_HardFilterPrecedesPreferUnfrozen()
        {
            // 三档是硬筛(取最高非空档,不退回),preferUnfrozen 是退回式弱筛
            // (筛不出来就退回原池)——三档必须排在前面。
            // 前排两只:被木克的土(已冻结)+ 中立的火(未冻结)。
            // 正确次序:三档先把候选收成「只剩土」,preferUnfrozen 在这唯一候选里
            // 筛不出未冻的 → 退回 → 仍打土。
            // 若次序颠倒(preferUnfrozen 先跑):候选先收成「只剩火」,三档在单候选
            // 里必然通过 → 会打火。这条测试就是钉住「不能颠倒」。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Earth), (EnemyRow.Front, 1, Element.Fire));
            enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, TurnsLeft = 3,
            });
            var random = new GameRandom(53);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    preferUnfrozen: true, counterTargeting: Element.Wood), Is.EqualTo(0),
                    "三档先收成土,preferUnfrozen 在唯一候选里筛不出未冻的就该退回,不该跳去打火");
        }

        [Test]
        public void CounterTargeting_HardFilterPrecedesPreferUnslowed()
        {
            // 与上面同一形状,盖 preferUnslowed 那条独立代码路径。
            var enemies = ElementGrid((EnemyRow.Front, 0, Element.Earth), (EnemyRow.Front, 1, Element.Fire));
            enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                Magnitude = -50, TurnsLeft = 5, SourceId = "测试",
            });
            var random = new GameRandom(59);
            for (int i = 0; i < 50; i++)
                Assert.That(Targeting.PickEnemyTargetForSummon(enemies, ranged: false, random,
                    preferUnslowed: true, counterTargeting: Element.Wood), Is.EqualTo(0),
                    "三档先收成土,preferUnslowed 在唯一候选里筛不出未减速的就该退回,不该跳去打火");
        }

        // ---- 择敌随机流(2026-09-13)----

        [Test]
        public void TargetRandomStream_SurvivesSnapshotRoundTrip()
        {
            // 择敌走独立的一条流,它必须跟 RandomState 一样进存档 —— 不存的话
            // 挂起再进,择敌序列会从头重来,断点续爬前后分叉。
            var snapshot = Trio().Capture();
            Assert.That(snapshot.TargetRandomState, Is.Not.EqualTo(0u),
                "择敌流的状态要真的存进去");
            Assert.That(snapshot.TargetRandomState, Is.Not.EqualTo(snapshot.RandomState),
                "两条流必须是各自独立的状态,不能是同一个对象");
        }
}
}
