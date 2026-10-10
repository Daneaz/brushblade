using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 6:水系 42 格专属特性的数据落表 —— 读真实 chars.json。
    /// 先用一张表钉死「每条特性落在正确的 (槽, 面, 形态),效果按书写顺序、关键字段逐条对得上特性表那一行」,
    /// 再按机制类别各取至少一条做集成测试(冰冻三尺 4 回合、坚冰 Boss 满格、怀山逐拍、寒彻 / 冰水冻结结束、
    /// 洗尽铅华挡标点小妖、泽及四方补满泉、大雨滂沱三次、栉风沐雨每回合一次、冷却每 Boss 一次)。
    /// 机制本身的单元级测试在 Water* 系列;这里只证明「那一行写出来的配置在真字上接上了」。
    ///
    /// 夹具:敌人一律心属性(水打心 1.0×)、0 甲;玩家攻击 100、暴击率 0。</summary>
    public class WaterExclusiveDataTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(string id, int level, int? startHp, params EnemyDef[] enemies) =>
            new(Graph, Config, Enumerable.Repeat(id, 6).ToArray(), Array.Empty<string>(), enemies, seed: 1,
                startingHp: startHp, cardLevels: new Dictionary<string, int> { [id] = level });

        private static BattleEngine Battle(string id, int level, params EnemyDef[] enemies) => Battle(id, level, null, enemies);

        private static EnemyDef Mob(int attack = 0, EnemyAbility ability = EnemyAbility.None) =>
            new("怔", Element.Heart, 100000, attack, ability);

        private static EnemyDef Boss() => RebalanceFixture.Boss();

        private static void Cast(BattleEngine b, string id, int target, bool attack) =>
            Assert.That(b.Cast(id, target, attackMode: attack), Is.EqualTo(BattleError.None), id);

        private static List<BattleEvent> RiderHits(BattleEngine b, int i) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.FreezeRider && e.TargetIndex == i).ToList();

        private static StatusEffect Find(BattleEngine b, int i, StatusKind kind) => b.Enemies[i].Statuses.Find(kind);

        private static bool Slowed(BattleEngine b, int i) =>
            b.Enemies[i].Statuses.All.Any(s => s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0);

        /// <summary>按书写顺序把一条效果的种类、数值与非缺省字段压成一行;特性表那一行的每个 token 都在这里留痕。</summary>
        private static string Sig(EffectDef e)
        {
            var p = new List<string> { $"{e.Kind} {e.Value}" };
            if (e.Turns != 0) p.Add($"turns {e.Turns}");
            if (e.Pick != EffectPick.Primary) p.Add($"pick {e.Pick}");
            if (e.OnlyIf != DamageCondition.None) p.Add($"if {e.OnlyIf}");
            if (e.Kind == EffectKind.Amplify) p.Add($"scope {e.Scope}");
            if (e.Shape != TargetArea.Single) p.Add($"shape {e.Shape}");
            if (e.ShapePercent != 100) p.Add($"shapePct {e.ShapePercent}");
            if (e.Shots != 0) p.Add($"shots {e.Shots}");
            if (e.ShotPercent != 100) p.Add($"shotPct {e.ShotPercent}");
            if (e.HitCount != 1) p.Add($"hits {e.HitCount}");
            if (e.HitPercent != 100) p.Add($"hitPct {e.HitPercent}");
            if (e.ExecuteBelowPercent > 0) p.Add($"exec {e.ExecuteBelowPercent}" + (e.ExecuteKills ? " kill" : ""));
            if (e.ExecuteIf != DamageCondition.None) p.Add($"execIf {e.ExecuteIf}");
            if (e.Kind == EffectKind.Augment) p.Add($"of {e.AugmentKind}.{e.AugmentField}");
            if (e.RiderOf != null) p.Add($"rider {e.RiderOf}");
            if (e.BodyPercent != 0) p.Add($"body {e.BodyPercent}");
            if (e.OpeningBattles != 0) p.Add($"battles {e.OpeningBattles}");
            if (e.ScaleBy != ScaleBasis.None) p.Add($"per {e.ScaleBy}");
            if (e.Fill) p.Add("fill");
            if (e.Extend) p.Add("extend");
            if (e.OfHeal) p.Add("ofHeal");
            if (e.WhileSlowed) p.Add("whileSlowed");
            if (e.TargetAll) p.Add("all");
            if (e.WardOf != null) p.Add($"wardOf {e.WardOf}");
            if (e.WardCount != 0) p.Add($"wardCount {e.WardCount}");
            if (e.PerHit.Count > 0) p.Add("perHit[" + string.Join(";", e.PerHit.Select(Sig)) + "]");
            return string.Join(" ", p);
        }

        // ================= 表驱动:42 格 47 行 =================

        private const TraitSlot L4 = TraitSlot.Lv4, L5 = TraitSlot.Lv5, L6 = TraitSlot.Lv6, L8 = TraitSlot.Lv8;
        private const TraitFace Atk = TraitFace.Attack, Ftr = TraitFace.Feature, Both = TraitFace.Both;

        // (字, 槽, 面, 名, 效果签名 —— 按 chars.json 落表顺序,「|」分隔;空串 = 空效果行)。
        // 管线按格内书写顺序落表(Ruling 13);Reshape / Amplify / Augment 是折叠期修饰,位置不影响结算。
        // 濯身的清除写在「每清 1 条回复」之前,顺序即语义。数值是 (F) 起值,改表必须同步这里
        private static readonly (string Ch, TraitSlot Slot, TraitFace Face, string Name, string Sigs)[] Table =
        {
            ("冷", L8, Atk, "冷却", "ChargeDelay 1|Weaken 50 turns 1 if NotBoss"),
            ("冻", L8, Atk, "冰冻三尺", "Augment 1 of Freeze.Turns|Augment 1 if Slowed of Freeze.Turns"),
            ("海", L5, Atk, "百川", "Reshape 0 shape Chain shapePct 50 shots 3"),
            ("海", L8, Ftr, "海纳百川", "Reshape 0 shape All"),
            ("溃", L5, Ftr, "溃围", "Slow 1 pick All"),
            ("溃", L8, Atk, "溃不成军", "Weaken 25 turns 2 pick All"),
            ("湮", L4, Both, "湮灭", ""),
            ("湮", L5, Ftr, "沉渊", "Retaliate 1 perHit[Freeze 1]"),
            ("湮", L8, Atk, "湮灭无踪", "Reshape 0 exec 25 kill execIf Frozen"),
            ("湮", L8, Ftr, "潜流", "Retaliate 0 turns 2 perHit[Slow 1]"),
            ("澡", L4, Both, "洗涤", "Cleanse 1 pick Self"),
            ("澡", L5, Atk, "洗尽铅华", "Dispel -1|BuffBlock 0 turns 2"),
            ("澡", L8, Atk, "涤荡", "Reshape 0 shape All shapePct 60|Dispel 1 all"),
            ("澡", L8, Ftr, "濯身", "Cleanse 0|HealSelf 50 per Cleansed|DebuffWard 0 turns 1"),
            ("冰", L4, Atk, "冰封", "Vulnerable 30 pick FrozenByThisCast"),
            ("冰", L4, Ftr, "冰封", "Amplify 21 scope All"),
            ("冰", L5, Ftr, "冰甲", "ShieldFromHeal 50"),
            ("冰", L6, Atk, "寒彻", "ThawStrike 0 pick FrozenByThisCast rider Freeze body 50"),
            ("冰", L8, Atk, "坚冰", "Augment 50 of Freeze.StallPush|Freeze 2 pick AdjacentOne"),
            ("冰", L8, Ftr, "冰晶", "ShieldFromHeal 100|ShieldFrost 1"),
            ("沐", L4, Atk, "沐恩", "Amplify 21 scope All"),
            ("沐", L4, Ftr, "沐恩", "HealSummons 50 ofHeal"),
            ("沐", L5, Atk, "新沐", "Cleanse 1 pick Self"),
            ("沐", L6, Ftr, "春雨", "HealOverTime 17 turns 2 battles 3"),
            ("沐", L8, Atk, "沐雨", "HealSummons 63"),
            ("沐", L8, Ftr, "栉风沐雨", "HurtHeal 50 turns 3"),
            ("淋", L4, Atk, "淋漓", "Seed 30 pick SlowedByThisCast whileSlowed"),
            ("淋", L4, Ftr, "淋漓", "Amplify 25 scope All"),
            ("淋", L5, Atk, "倾盆", "Reshape 0 hits 3 hitPct 40 perHit[Slow 1 extend]"),
            ("淋", L5, Ftr, "细雨", "Reshape 0 shape All shapePct 50"),
            ("淋", L6, Ftr, "浇熄", "DebuffWard 50 turns 3 wardOf Burn"),
            ("淋", L8, Atk, "暴雨", "Reshape 0 shape Scatter shots 5 shotPct 30 perHit[Slow 1]"),
            ("淋", L8, Ftr, "大雨滂沱", "TurnPulse 0 turns 3 perHit[Slow 1 pick All;HealSelf 45]"),
            ("淼", L4, Atk, "汪洋", "Amplify 25 scope All"),
            ("淼", L4, Ftr, "汪洋", "AddWellspring 1"),
            ("淼", L5, Atk, "同寒", "Freeze 2 pick Column"),
            ("淼", L5, Ftr, "涵养", "AddWellspring 2"),
            ("淼", L6, Atk, "冰水", "ThawSlow 2 pick FrozenByThisCast rider Freeze"),
            ("淼", L8, Atk, "浩瀚", "Reshape 0 shape All shapePct 70|Freeze 1 pick HighestHp"),
            ("淼", L8, Ftr, "水大无际", "Reshape 0 shape All|Cleanse 0 pick AllAllies|HealOverTime 40 turns 2 battles 5"),
            ("㵘", L4, Atk, "怀山", "FrostBite 0 pick FrozenByThisCast rider Freeze body 20"),
            ("㵘", L4, Ftr, "怀山", "Amplify 30 scope All"),
            ("㵘", L5, Atk, "洪峰", "Amplify 10 scope Damage per Wellspring"),
            ("㵘", L5, Ftr, "泽被", "Reshape 0 shape All|Cleanse 1"),
            ("㵘", L6, Atk, "浩荡", "Vulnerable 25 pick FrozenByThisCast"),
            ("㵘", L8, Atk, "滔天", "Amplify 100 if Frozen scope Damage"),
            ("㵘", L8, Ftr, "泽及四方", "Reshape 0 shape All|Amplify 50 scope Heal|AddWellspring 0 fill"),
        };

        [Test]
        public void EveryExclusiveTrait_LoadsAtItsSlotAndFace_WithTheTableEffects()
        {
            Assert.That(Table.Length, Is.EqualTo(47), "42 格,冰封 / 怀山 / 汪洋 / 沐恩 / 淋漓拆成两行(Q2)");
            Assert.That(Table.Select(r => (r.Ch, r.Slot, r.Name)).Distinct().Count(), Is.EqualTo(42));
            foreach (var (ch, slot, face, name, sigs) in Table)
            {
                var t = Graph.Get(ch).Traits.SingleOrDefault(x => x.Slot == slot && x.Face == face && x.Name == name);
                Assert.That(t, Is.Not.Null, $"{ch} {name}({slot}/{face})");
                bool passive = slot == L4 || slot == L6;
                Assert.That((t.Form, t.Trigger), Is.EqualTo((passive ? TraitForm.Passive : TraitForm.Active, TraitTrigger.Cast)),
                    $"{ch} {name} 形态");
                Assert.That(t.Replaces.HasValue, Is.False, $"{ch} {name} 不替换");
                Assert.That(string.Join("|", t.Effects.Select(Sig)), Is.EqualTo(sigs), $"{ch} {name}({slot}/{face})效果");
            }
        }

        /// <summary>冰晶(Q15 / Task 4 裁定 8):ShieldFrost 读本次出字已给的护盾,折叠后必须排在同面所有 ShieldFromHeal 之后。</summary>
        [Test]
        public void BingJing_ShieldFrost_FoldsAfterEveryShieldFromHeal()
        {
            var cast = TraitRules.CastEffects(Graph.Get("冰"), CardFace.Feature, 8).ToList();
            int frost = cast.FindIndex(e => e.Kind == EffectKind.ShieldFrost);
            Assert.That(frost, Is.GreaterThanOrEqualTo(0));
            var shields = Enumerable.Range(0, cast.Count).Where(i => cast[i].Kind == EffectKind.ShieldFromHeal).ToList();
            Assert.That(shields.Select(i => cast[i].Value).ToList(), Is.EqualTo(new[] { 50, 100 }), "冰甲 50 + 冰晶 100");
            Assert.That(shields.All(i => i < frost), Is.True, "ShieldFrost 在同面全部 ShieldFromHeal 之后");

            // 接上:受伤后出冰 Lv8 润面 → 拿到护盾,挂上冰晶
            var b = Battle("冰", 8, 200, Mob());
            Cast(b, "冰", -1, attack: false);
            Assert.That(b.ShieldNormal, Is.GreaterThan(0));
            Assert.That(b.PlayerStatuses.Find(StatusKind.ShieldFrost)?.OnHit.Single().Value, Is.EqualTo(1), "冻结攻击者 1 回合");
        }

        // ================= 冰冻三尺:本体 2 + 1 +(出字前已减速 ? 1 : 0)= 最多 4(Q6) =================

        [Test]
        public void BingDongSanChi_FreezeThree_FourIfSlowedBeforeCast()
        {
            int FreezeTurns(int level, bool slowed)
            {
                var b = Battle("冻", level, Mob());
                if (slowed)
                    b.Enemies[0].Statuses.Apply(new StatusEffect
                        { Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff, Magnitude = -50, TurnsLeft = 2 });
                Cast(b, "冻", 0, attack: true);
                var f = Find(b, 0, StatusKind.Freeze);
                Assert.That(f.Magnitude, Is.EqualTo(f.TurnsLeft), "霜抗等长(R1)");
                return f.TurnsLeft;
            }
            Assert.That(FreezeTurns(6, slowed: true), Is.EqualTo(2), "对照:Lv6 只有 Lv3 本体 2 回合");
            Assert.That(FreezeTurns(8, slowed: false), Is.EqualTo(3));
            Assert.That(FreezeTurns(8, slowed: true), Is.EqualTo(4));
        }

        // ================= 坚冰:Boss 冰滞后退满格;杂兵冻相邻一名(E16:也吃 Lv4 冰封 / Lv6 寒彻) =================

        [Test]
        public void JianBing_BossIceStall_PushesFullMeter()
        {
            int Pushed(int level)
            {
                var b = Battle("冰", level, Boss());
                int before = b.Enemies[0].ActionMeter;
                Cast(b, "冰", 0, attack: true);
                Assert.That(Find(b, 0, StatusKind.IceStall), Is.Not.Null);
                return before - b.Enemies[0].ActionMeter;
            }
            Assert.That(Pushed(6), Is.EqualTo(TurnScheduler.Threshold * BattleConfig.IceStallPushPercent / 100), "对照:半格");
            Assert.That(Pushed(8), Is.EqualTo(TurnScheduler.Threshold), "坚冰:满格");
        }

        [Test]
        public void JianBing_FreezesAdjacentOne_WhichAlsoGetsLv4MarkAndLv6Rider()
        {
            var b = Battle("冰", 8, Mob(), Mob(), Mob());
            Cast(b, "冰", 1, attack: true);
            var left = Find(b, 0, StatusKind.Freeze);
            Assert.That(left?.TurnsLeft, Is.EqualTo(2), "先左:左邻冻 2 回合(同本体)");
            Assert.That(Find(b, 2, StatusKind.Freeze), Is.Null, "只冻 1 名");
            foreach (int i in new[] { 0, 1 })
            {
                // 冰封 30 吃卡等级:Lv8 → ScaleEffectValue(Vulnerable, 30, 8) = 43
                Assert.That(Find(b, i, StatusKind.Vulnerable)?.Magnitude,
                    Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.Vulnerable, 30, 8)).And.EqualTo(43), $"#{i} 吃冰封(E16)");
                Assert.That(Find(b, i, StatusKind.Vulnerable)?.TurnsLeft, Is.EqualTo(2), $"#{i} 冰封回合 = 本次冻结回合");
                Assert.That(Find(b, i, StatusKind.ThawStrike), Is.Not.Null, $"#{i} 挂寒彻");
            }
        }

        // ================= 怀山:冻结中每拍开始打本体 20% × 卡等级(Q18) =================

        [Test]
        public void HuaiShan_HitsEveryFrozenBeat_ForBodyTwentyPercent()
        {
            int body = Graph.Get("㵘").AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value;
            int expected = MetaRules.ScaleEffectValue(EffectKind.FrostBite, body * 20 / 100, 4);
            Assert.That(expected, Is.EqualTo(MetaRules.ScaleByCardLevel(34, 4)), "174 × 20% = 34");
            var b = Battle("㵘", 4, Mob(), Mob());
            Cast(b, "㵘", 0, attack: true);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(Find(b, i, StatusKind.Freeze)?.TurnsLeft, Is.EqualTo(2), "Lv3 全体冻 2 回合");
                Assert.That(Find(b, i, StatusKind.FrostBite)?.Magnitude, Is.EqualTo(expected));
            }
            for (int beat = 1; beat <= 3; beat++)
            {
                b.EndTurn();
                for (int i = 0; i < 2; i++)
                    Assert.That(RiderHits(b, i).Select(e => e.Amount).ToList(),
                        Is.EqualTo(beat <= 2 ? new[] { expected } : Array.Empty<int>()), $"第 {beat} 拍 #{i}");
            }
        }

        // ================= 寒彻 / 冰水:冻结结束时结算一次 =================

        [Test]
        public void HanChe_FiresOnceWhenFreezeEnds_ForBodyFiftyPercent()
        {
            int body = Graph.Get("冰").AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value;
            int expected = MetaRules.ScaleEffectValue(EffectKind.ThawStrike, body * 50 / 100, 6);
            var b = Battle("冰", 6, Mob());
            Cast(b, "冰", 0, attack: true);
            Assert.That(Find(b, 0, StatusKind.ThawStrike)?.Magnitude, Is.EqualTo(expected));
            var perBeat = new List<int>();
            for (int beat = 1; beat <= 3; beat++)
            {
                b.EndTurn();
                perBeat.Add(RiderHits(b, 0).Sum(e => e.Amount));
            }
            // 冰封易伤 TurnsLeft = 冻结回合,与冻结同一次 TickTurns 到期 —— 寒彻那一下吃不到
            Assert.That(perBeat, Is.EqualTo(new[] { 0, expected, 0 }), "冻 2 回合:第 2 拍到期时打一次");
            Assert.That(Find(b, 0, StatusKind.ThawStrike), Is.Null, "附着随载体移除");
        }

        [Test]
        public void BingShui_FreezeEnd_FrostResistThenSlowTwo()
        {
            var b = Battle("淼", 6, Mob());
            Cast(b, "淼", 0, attack: true);
            Assert.That(Find(b, 0, StatusKind.ThawSlow)?.Magnitude, Is.EqualTo(2));
            b.EndTurn();
            Assert.That(Slowed(b, 0), Is.False, "冻结中");
            b.EndTurn();
            Assert.That(Find(b, 0, StatusKind.Freeze), Is.Null);
            Assert.That(Find(b, 0, StatusKind.FrostResist)?.TurnsLeft, Is.EqualTo(2), "霜抗等长");
            var slow = b.Enemies[0].Statuses.All.Single(s => s.Kind == StatusKind.SpeedModifier);
            Assert.That(slow.TurnsLeft, Is.EqualTo(2), "冰水:减速 2 回合,与霜抗并存(Q19)");
        }

        // ================= 洗尽铅华:挡标点小妖的加攻 =================

        [Test]
        public void XiJinQianHua_BlocksPunctuationAura_OnTargetOnly()
        {
            var b = Battle("澡", 5, Mob(), Mob(), Mob(ability: EnemyAbility.Buff));
            Cast(b, "澡", 0, attack: true);
            Assert.That(Find(b, 0, StatusKind.BuffBlock)?.TurnsLeft, Is.EqualTo(2));
            b.EndTurn();
            int Buffs(int i) => b.Enemies[i].Statuses.All.Count(s => s.Kind == StatusKind.AttackBuff);
            Assert.That(Buffs(0), Is.EqualTo(0), "带禁增益的拿不到");
            Assert.That(Buffs(1), Is.EqualTo(1), "旁边那只照拿");
        }

        // ================= 濯身:清除在前、按清掉的条数回复(Cleanse 0 落表顺序) =================

        /// <summary>澡 Lv8 润面:Lv4 洗涤 `Cleanse 1 pick Self` 先清 1 条,濯身 `Cleanse 0` 清剩下的,
        /// `HealSelf 50 per Cleansed` 按本次出字清掉的总条数回复(E22b 读 _cast.Cleansed)。玩家身上 3 条减益 → 回复 3 份;
        /// 没有减益 → 0 份(不发这条回复)。管线若把 HealSelf 排到 Cleanse 之前,这里会读到 0。</summary>
        [Test]
        public void ZhuoShen_HealsPerCleansedDebuff_CleanseFoldsFirst()
        {
            List<int> Heals(int debuffs)
            {
                var b = Battle("澡", 8, 1, Mob());   // 1 血起手:四条回复加起来不顶到上限 500
                var kinds = new[] { StatusKind.Burn, StatusKind.Seal, StatusKind.Curse };
                for (int k = 0; k < debuffs; k++)
                    b.PlayerStatuses.Apply(new StatusEffect
                        { Kind = kinds[k], Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 3 });
                Cast(b, "澡", -1, attack: false);
                Assert.That(b.PlayerStatuses.All.Any(s => s.Polarity == StatusPolarity.Debuff), Is.False, $"{debuffs} 条全部清掉");
                Assert.That(b.PlayerStatuses.Find(StatusKind.DebuffWard)?.TurnsLeft, Is.EqualTo(1), "1 回合免疫减益");
                return b.LastEvents.Where(e => e.Kind == BattleEventKind.Heal && e.SecondIndex == Targeting.PlayerTarget)
                    .Select(e => e.Amount).ToList();
            }
            var none = Heals(0);
            var one = Heals(1);
            var three = Heals(3);
            Assert.That(one.Count, Is.EqualTo(none.Count + 1), "洗涤清 1 条 → 多出一条濯身回复");
            Assert.That(three.Take(none.Count).ToList(), Is.EqualTo(none), "本体治疗不受减益条数影响");
            Assert.That(three.Count, Is.EqualTo(none.Count + 1));
            // 濯身那一下在泉放大之后:放大倍率与条数无关(本体治疗相同 → 泉层相同),所以 3 条 = 1 条的 3 倍(整数截断差 ≤ 2)
            Assert.That(three.Last(), Is.EqualTo(3 * one.Last()).Within(2), "按清掉的条数线性回复");
            int perCleansed = MetaRules.ScaleEffectValue(EffectKind.HealSelf, 50, 8);
            Assert.That(perCleansed, Is.EqualTo(71), "50 按 Lv8 缩放");
            // 濯身结算时泉 2 层(本体治疗攒的)→ 放大 110%:1 条 71 × 110% = 78;3 条 213 × 110% = 234
            Assert.That(AmplifiedBy(one.Last(), perCleansed), Is.EqualTo(110));
            Assert.That((one.Last(), three.Last()), Is.EqualTo((78, 234)), "钉值");
        }

        private static int AmplifiedBy(int healed, int raw) => (int)Math.Round(healed * 100.0 / raw / 5) * 5;

        // ================= 泽及四方:泉补满 =================

        [Test]
        public void ZeJiSiFang_FillsWellspringToCap()
        {
            var control = Battle("㵘", 6, 200, Mob());
            Cast(control, "㵘", -1, attack: false);
            Assert.That(control.WellspringStacks, Is.LessThan(CombatCaps.WellspringStacks), "对照:Lv6 只按治疗攒泉");
            var b = Battle("㵘", 8, 200, Mob());
            Cast(b, "㵘", -1, attack: false);
            Assert.That(b.WellspringStacks, Is.EqualTo(CombatCaps.WellspringStacks));
        }

        // ================= 大雨滂沱:之后 3 个玩家回合开始各一次全体减速 =================

        [Test]
        public void DaYuPangTuo_PulsesThreeTurns_ThenStops()
        {
            var b = Battle("淋", 8, 200, Mob(), Mob());
            Cast(b, "淋", -1, attack: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse)?.TurnsLeft, Is.EqualTo(3));
            Assert.That(Slowed(b, 0) || Slowed(b, 1), Is.False, "施加当回合不触发");
            for (int round = 1; round <= 4; round++)
            {
                b.EndTurn();
                bool pulse = round <= 3;
                for (int i = 0; i < 2; i++)
                    Assert.That(Slowed(b, i), Is.EqualTo(pulse), $"第 {round} 回合开始 #{i}");
            }
            Assert.That(b.PlayerStatuses.Has(StatusKind.TurnPulse), Is.False, "3 次后移除");
        }

        // ================= 栉风沐雨:每回合第一次受击回复 50% =================

        [Test]
        public void ZhiFengMuYu_HealsHalfOfFirstHitEachTurn()
        {
            var b = Battle("沐", 8, 200, Mob(attack: 40), Mob(attack: 40));
            Cast(b, "沐", -1, attack: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.HurtHeal)?.TurnsLeft, Is.EqualTo(3));
            for (int round = 1; round <= 4; round++)
            {
                b.EndTurn();
                Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyAttack), Is.EqualTo(2), $"第 {round} 轮两只都打中");
                Assert.That(HealsRightAfterHits(b), Is.EqualTo(round <= 3 ? new[] { 20 } : Array.Empty<int>()),
                    $"第 {round} 轮:只回第一下 40 的 50%");
            }
        }

        /// <summary>紧跟在敌人挥击之后(下一次挥击之前、且不越过回合初的结算)的玩家回复 —— 回合初的润泽不算。</summary>
        private static List<int> HealsRightAfterHits(BattleEngine b)
        {
            var heals = new List<int>();
            bool afterHit = false;
            foreach (var e in b.LastEvents)
            {
                if (e.Kind == BattleEventKind.EnemyAttack) { afterHit = true; continue; }
                if (e.Kind == BattleEventKind.Heal && e.SecondIndex == Targeting.PlayerTarget && afterHit) heals.Add(e.Amount);
                else if (e.Kind != BattleEventKind.Damage) afterHit = false;
            }
            return heals;
        }

        // ================= Ruling 14(终审修订):Reshape 改散射时重置前一条的多段 =================

        /// <summary>折叠后的 DamageSingle:<see cref="Sig"/> 之外再补上 ApplyReshape 会写的其余字段。</summary>
        private static string FoldedDamageSig(string ch)
        {
            var d = TraitRules.CastEffects(Graph.Get(ch), CardFace.Attack, 8).First(e => e.Kind == EffectKind.DamageSingle);
            var p = new List<string> { Sig(d) };
            if (d.PerHitFrom != 1) p.Add($"perHitFrom {d.PerHitFrom}");
            if (d.ForceCrit) p.Add("crit");
            if (d.ArmorIgnorePercent > 0) p.Add($"armorIgnore {d.ArmorIgnorePercent}");
            if (d.ShieldStrikePercent > 0) p.Add($"shieldStrike {d.ShieldStrikePercent}");
            if (d.ArmorStrikePercent > 0) p.Add($"armorStrike {d.ArmorStrikePercent}");
            if (d.ExecuteSplashPercent > 0) p.Add($"execSplash {d.ExecuteSplashPercent}");
            return string.Join(" ", p);
        }

        /// <summary>淋 Lv8 攻面:倾盆(Lv5,hits 3 / 40% / 每击减速延长)之后,暴雨(Lv8)改成散射 5 发 × 30%。
        /// 散射只有 t == 0 吃主目标多段,混着倾盆就成了「首发拆 3 段,其余 4 发整发」(共 197)。
        /// 修订后的 Ruling 14:改散射且自己没写 hits 时,多段字段重置 → 纯暴雨。</summary>
        [Test]
        public void LinLv8_Downpour_ScatterResetsEarlierMultiHit()
        {
            var d = TraitRules.CastEffects(Graph.Get("淋"), CardFace.Attack, 8).First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That((d.Shape, d.Shots, d.ShotPercent), Is.EqualTo((TargetArea.Scatter, 5, 30)));
            Assert.That((d.HitCount, d.HitPercent, d.PerHitFrom), Is.EqualTo((1, 100, 1)), "倾盆的多段被暴雨的散射重置");
            Assert.That(d.PerHit.Select(Sig).ToList(), Is.EqualTo(new[] { "Slow 1" }), "每发减速取暴雨的,不带倾盆的 extend");
        }

        [Test]
        public void LinLv8_Downpour_SingleTargetDealsFiveShotsOfThirtyEight()
        {
            var b = Battle("淋", 8, Mob());
            Cast(b, "淋", 0, attack: true);
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == 0).Select(e => e.Amount).ToList();
            Assert.That(hits, Is.EqualTo(new[] { 38, 38, 38, 38, 38 }), "5 发各 38");
            Assert.That(hits.Sum(), Is.EqualTo(190));
        }

        /// <summary>守护:其余同面多条 Reshape 的金 / 土 / 水字 Lv8 攻面折叠结果与修订前逐字段一致(期望值取自修订前代码)。
        /// Reshape 的叠加是 D2-金 / D2-土 的设计,只有「改散射」才重置多段。</summary>
        [TestCase("海", "DamageSingle 88 shape All shapePct 60 shots 3")]
        [TestCase("剁", "DamageSingle 61 hits 2 hitPct 35 per Morale perHit[Bleed 35 turns 3] perHitFrom 2")]
        [TestCase("锥", "DamageSingle 106 shape Column shapePct 70 hits 2 hitPct 60 armorIgnore 100")]
        [TestCase("锋", "DamageSingle 62 hits 2 hitPct 60 crit")]
        [TestCase("鑫", "DamageSingle 297 shape Adjacent hits 3 hitPct 50")]
        [TestCase("\uE626", "DamageSingle 384 hits 4 hitPct 30 perHit[ArmorBreak 15 turns 3] armorIgnore 100")]
        [TestCase("鍂", "DamageSingle 98 hits 2 hitPct 60 perHit[Morale 1]")]
        [TestCase("碉", "DamageSingle 60 shape Row shapePct 50 shieldStrike 40")]
        [TestCase("壁", "DamageSingle 60 shape Row shapePct 50 shieldStrike 40")]
        [TestCase("垒", "DamageSingle 63 shape Row shapePct 50 armorStrike 300")]
        public void StackedReshapes_OtherLv8AttackFaces_Unchanged(string ch, string expected)
        {
            Assert.That(FoldedDamageSig(ch), Is.EqualTo(expected), ch);
        }

        // ================= 冷却:每 Boss 每场 1 次;小怪下次攻击 −50% =================

        [Test]
        public void LengQue_OncePerBoss_MobWeakened()
        {
            var b = Battle("冷", 8, Boss(), Boss(), Mob());
            Cast(b, "冷", 0, attack: true);
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(-1));
            Cast(b, "冷", 0, attack: true);
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(-1), "同一 Boss 第二次不再推迟");
            Cast(b, "冷", 1, attack: true);
            Assert.That(b.Enemies[1].ChargeCounter, Is.EqualTo(-1), "另一只 Boss 各有一次");
            Assert.That(Find(b, 1, StatusKind.Curse), Is.Null, "Boss 不吃小怪那一半");
            Cast(b, "冷", 2, attack: true);
            var curse = Find(b, 2, StatusKind.Curse);
            // 减攻 50 吃卡等级(同池·余震):Lv8 → 71;回合数不吃等级
            Assert.That((curse?.Magnitude, curse?.TurnsLeft),
                Is.EqualTo(((int?)MetaRules.ScaleEffectValue(EffectKind.Weaken, 50, 8), (int?)1)).And.EqualTo(((int?)71, (int?)1)),
                "小怪攻击 −50%(Lv8 缩放),1 回合");
        }
    }
}
