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
        public void Melee_WithEmptyFront_PicksAmongBackRowAndPlayer()
        {
            // 后排站 4、5 两只 + 玩家 = 三个候选,均匀随机
            var seen = new HashSet<int>();
            var random = new GameRandom(7);
            for (int i = 0; i < 200; i++)
                seen.Add(Targeting.PickAllyTarget(AttackRange.Melee, AttackFocus.Default,
                    Line(4, 5), FrontRow, random));
            Assert.That(seen.Count, Is.EqualTo(3), "三个候选都摇得到,一个不多");
            Assert.That(seen.Contains(4), Is.True);
            Assert.That(seen.Contains(5), Is.True);
            Assert.That(seen.Contains(Targeting.PlayerTarget), Is.True);
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
    }
}
