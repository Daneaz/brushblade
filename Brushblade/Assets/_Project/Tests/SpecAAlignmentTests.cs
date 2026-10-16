using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A(2026-10-15 字卡两面对齐与数值归一)§9 验收,读真实 chars.json。</summary>
    public class SpecAAlignmentTests
    {
        private static List<CharDef> Playable() =>
            CharTableTests.RealGraph().All.Where(d => !d.IsComponent && d.Effects.Count > 0).ToList();

        private static readonly CardRarity[] Tiers =
            { CardRarity.White, CardRarity.Green, CardRarity.Blue, CardRarity.Purple, CardRarity.Gold, CardRarity.Orange, CardRarity.Red };
        private static readonly int[] Anchor = { 60, 88, 129, 190, 278, 409, 600 };
        private static int Tier(CardRarity r) => System.Array.IndexOf(Tiers, r);
        /// <summary>锚点 × 0.7 × 范围系数,四舍五入;全程整数避开 0.7 的浮点误差。</summary>
        private static int Lv1(CardRarity r, int areaPercent) => (Anchor[Tier(r)] * 7 * areaPercent + 500) / 1000;

        [Test]
        public void Formula_ReproducesSpecTable()
        {
            Assert.That(Tiers.Select(t => Lv1(t, 100)), Is.EqualTo(new[] { 42, 62, 90, 133, 195, 286, 420 }));
            Assert.That(Tiers.Select(t => Lv1(t, 75)), Is.EqualTo(new[] { 32, 46, 68, 100, 146, 215, 315 }));
            Assert.That(Tiers.Select(t => Lv1(t, 50)), Is.EqualTo(new[] { 21, 31, 45, 67, 97, 143, 210 }));
        }

        [Test]
        public void AttackDamage_IsAnchorTimesSeventy_ByArea()   // §9.1
        {
            var bad = new List<string>();
            foreach (var d in Playable())
            {
                var dmg = d.AttackEffects.First(e => e.Kind == EffectKind.DamageSingle);
                int want = Lv1(d.Rarity, dmg.Shape == TargetArea.All ? 50 : 100);
                if (dmg.Value != want || dmg.HitCount > 1) bad.Add($"{d.Id}:{dmg.Value}≠{want}");
            }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        [Test]
        public void FeatureHealShieldSummon_FollowSpec()   // §2 / §4.3 / §4.5
        {
            var bad = new List<string>();
            foreach (var d in Playable())
            {
                int single = Lv1(d.Rarity, 100), all = Lv1(d.Rarity, 50);
                bool revive = d.Effects.Any(e => e.Kind == EffectKind.Revive);
                foreach (var e in d.Effects)
                {
                    int? want = e.Kind switch
                    {
                        EffectKind.HealSelf => revive ? (single * 50 + 50) / 100 : single,
                        EffectKind.HealAll => all,
                        EffectKind.HealOverTime => (single * 40 + 50) / 100,
                        EffectKind.Shield => single,
                        EffectKind.ShieldAll => all,
                        EffectKind.Summon => d.Id == "荆" || d.Id == "柘" ? single * 3 : (single * 15 + 5) / 10,
                        _ => (int?)null,
                    };
                    if (want.HasValue && e.Value != want.Value) bad.Add($"{d.Id} {e.Kind}:{e.Value}≠{want}");
                    if (e.Kind == EffectKind.Summon)
                    {
                        int atk = d.Id == "荆" || d.Id == "柘" ? 0 : (single * 35 + 50) / 100;
                        if (e.SummonAttack != atk) bad.Add($"{d.Id} 攻:{e.SummonAttack}≠{atk}");
                        if (e.Passive != null && e.Passive.Speed != 0) bad.Add($"{d.Id} 本命仍带速度");
                    }
                }
            }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        [TestCase("爆")] [TestCase("炸")] [TestCase("烈")] [TestCase("焚")] [TestCase("焱")] [TestCase("燚")]
        [TestCase("溃")] [TestCase("㵘")] [TestCase("崩")]
        public void AllAttackFace_MeansAllFeatureFace(string id)   // §9.2
        {
            var d = CharTableTests.RealGraph().Get(id);
            Assert.That(d.AttackEffects.First(e => e.Kind == EffectKind.DamageSingle).Shape, Is.EqualTo(TargetArea.All));
            Assert.That(CardSheetRules.AreaOf(d, CardFace.Feature, 1), Is.EqualTo(TargetArea.All));
        }

        [TestCase("爆")] [TestCase("炸")] [TestCase("烈")] [TestCase("焚")] [TestCase("焱")] [TestCase("燚")]
        public void AllFireFeatureFaces_NeedNoTarget_UpToLv8(string id)   // Review Focus 4
        {
            var d = CharTableTests.RealGraph().Get(id);
            for (int lv = 1; lv <= 8; lv++)
                Assert.That(BattleEngine.NeedsTarget(d, false, lv), Is.False, $"{id} Lv{lv}");
        }

        [Test]
        public void EverySlot_HasBothFaces()   // §9.3
        {
            var bad = new List<string>();
            foreach (var d in Playable())
                foreach (var slot in new[] { TraitSlot.Lv1, TraitSlot.Lv3, TraitSlot.Lv4, TraitSlot.Lv5, TraitSlot.Lv6, TraitSlot.Lv8 })
                {
                    var at = d.Traits.Where(t => t.Slot == slot).ToList();
                    if (!at.Any(t => t.AppliesTo(CardFace.Attack)) || !at.Any(t => t.AppliesTo(CardFace.Feature)))
                        bad.Add($"{d.Id} {slot}");
                }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        private static readonly (int Pct, int Turns)[] Weaken = { (15, 2), (15, 2), (20, 2), (20, 3), (25, 3), (30, 3), (35, 3) };
        private static readonly (int Count, int Counter)[] Block = { (1, 30), (1, 35), (2, 40), (2, 45), (2, 50), (3, 60), (3, 70) };
        private static readonly (int Points, int Turns)[] Armor = { (10, 2), (15, 2), (20, 2), (25, 2), (35, 3), (50, 3), (70, 3) };

        // 2026-10-16 拍板:燃意 单体 = 梯度 +5;全体 = 梯度 ×0.5(四舍五入)+5
        private static (int Pct, int Turns) KindleExpected((int Pct, int Turns) w, bool all)
            => (all ? (w.Pct + 1) / 2 + 5 : w.Pct + 5, w.Turns);

        [Test]
        public void FeatureGradients_AndFixedRiders()   // §9.4
        {
            var bad = new List<string>();
            foreach (var d in Playable())
            {
                int t = Tier(d.Rarity);
                foreach (var e in d.Effects)
                {
                    // spec C:火面本体不再带灼 / 慑,只有燃意(灼改由燃意命中时附加)
                    if (d.Element == Element.Fire && e.Kind != EffectKind.Kindle) bad.Add($"{d.Id} 火面本体多出 {e.Kind}");
                    if (e.Kind == EffectKind.Morale && e.Value != 1) bad.Add($"{d.Id} 战意 {e.Value}");
                    // spec C §5.1:火面本体的慑换成燃意,同一张 (Pct, Turns) 梯度表
                    if (e.Kind == EffectKind.Kindle && (e.Value, e.Turns) != KindleExpected(Weaken[t], e.Pick == EffectPick.AllAllies)) bad.Add($"{d.Id} 燃意 {e.Value}/{e.Turns}");
                    if (e.Kind == EffectKind.Block && (e.Value, e.CounterPercent) != Block[t]) bad.Add($"{d.Id} 格挡 {e.Value}/{e.CounterPercent}");
                    if (e.Kind == EffectKind.DefenseBuff && (e.Value, e.Turns) != Armor[t]) bad.Add($"{d.Id} 护甲 {e.Value}/{e.Turns}");
                }
            }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        private static readonly Dictionary<string, string> WaterMode = new()
        {
            ["冷"] = "涓", ["冻"] = "涓", ["澡"] = "涓", ["淋"] = "涓",
            ["海"] = "涌", ["溃"] = "涌", ["湮"] = "涌", ["冰"] = "涌", ["淼"] = "涌",
            ["沐"] = "苏", ["㵘"] = "苏",
        };

        [Test]
        public void Water_OneModePerChar_NamedByMode()   // §4.3
        {
            var g = CharTableTests.RealGraph();
            foreach (var (id, mode) in WaterMode)
            {
                var d = g.Get(id);
                var kinds = d.Effects.Select(e => e.Kind).ToList();
                if (mode == "涓") Assert.That(kinds, Is.EqualTo(new[] { EffectKind.HealOverTime }), id);
                if (mode == "涌") Assert.That(kinds.Count == 1 && (kinds[0] == EffectKind.HealSelf || kinds[0] == EffectKind.HealAll), Is.True, id);
                if (mode == "苏") Assert.That(kinds[0], Is.EqualTo(EffectKind.Revive), id);
                Assert.That(d.Traits.Single(t => t.Slot == TraitSlot.Lv1 && t.Face == TraitFace.Feature).Name, Is.EqualTo(mode), id);
                Assert.That(d.Traits.Single(t => t.Slot == TraitSlot.Lv3 && t.Face == TraitFace.Feature).Name, Is.EqualTo(mode + "·强化"), id);
            }
        }

        [Test]
        public void Flower_AttackIsSeed_NoCharm()   // §9.6
        {
            var a = CharTableTests.RealGraph().Get("花").AttackEffects;
            Assert.That(a.Select(e => e.Kind), Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.Seed }));
        }

        [Test]
        public void Lv3_ReplacesOnlyKindsPresentInBody()   // Review Focus 2
        {
            var bad = new List<string>();
            foreach (var d in Playable())
                foreach (bool attack in new[] { true, false })
                {
                    var face = BattleEngine.FaceOf(d, attack);
                    var body = BattleEngine.EffectsOf(d, attack).Select(e => e.Kind).ToList();
                    foreach (var t in d.Traits.Where(t => t.Slot == TraitSlot.Lv3 && t.AppliesTo(face)))
                        foreach (var e in t.Effects)
                            if (!TraitRules.IsModifier(e.Kind) && !(e.Kind == EffectKind.Summon && e.Value == 0) && !body.Contains(e.Kind))
                                bad.Add($"{d.Id} {face} {e.Kind}");
                }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        [Test]
        public void Lv6_DoesNotLeakAcrossFaces()   // Review Focus 3
        {
            var bad = new List<string>();
            foreach (var d in Playable())
                foreach (bool attack in new[] { true, false })
                {
                    var face = BattleEngine.FaceOf(d, attack);
                    string otherKey = BattleEngine.TraitKey(d.Id, TraitSlot.Lv6, attack ? TraitFace.Feature : TraitFace.Attack);
                    if (TraitRules.CastEffects(d, face, 6).Any(e => e.TraitKey == otherKey)) bad.Add($"{d.Id} {face}");
                }
            Assert.That(bad, Is.Empty, string.Join(",", bad));
        }

        [Test]
        public void RealWaterAttack_AccruesWellspring_SpendCharsDoNot()   // §9.5
        {
            var g = CharTableTests.RealGraph();
            int Gain(string id)
            {
                var b = new BattleEngine(g, new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 }, new[] { id },
                    System.Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1);
                b.Cast(id, 0, attackMode: true);
                return b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring);
            }
            Assert.That(Gain("冰"), Is.EqualTo(1), "195 → 1 层");
            Assert.That(Gain("溃"), Is.EqualTo(0));
        }

        // ---- 治疗改形判据(终审 I1):卡面文案与 TraitRules.ApplyReshape 共用 HealReshapeIndex ----

        [Test]
        public void HealReshapeIndex_PicksHealSelf_ThenFirstSingleHoT_NeverOnDamageOrHealAll()
        {
            var heal = new EffectDef(EffectKind.HealSelf, 10);
            var hot = new EffectDef(EffectKind.HealOverTime, 5, turns: 3);
            var hotAll = new EffectDef(EffectKind.HealOverTime, 5, turns: 3, targetAll: true);
            Assert.That(TraitRules.HealReshapeIndex(new[] { hot, heal }), Is.EqualTo(1), "HealSelf 优先");
            Assert.That(TraitRules.HealReshapeIndex(new[] { hot }), Is.EqualTo(0), "只有 HoT");
            Assert.That(TraitRules.HealReshapeIndex(new[] { hotAll }), Is.EqualTo(-1), "HoT 已是全体");
            Assert.That(TraitRules.HealReshapeIndex(new[] { new EffectDef(EffectKind.HealAll, 10) }), Is.EqualTo(-1), "HealAll 空转");
            Assert.That(TraitRules.HealReshapeIndex(new[] { new EffectDef(EffectKind.DamageSingle, 10), heal }), Is.EqualTo(-1), "有伤害");
        }

        [Test]
        public void HealReshapeIndex_RealTable_LinHot_YesHuiHealAll_No()
        {
            var g = CharTableTests.RealGraph();
            var lin = TraitRules.CastEffects(g.Get("淋"), CardFace.Feature, 4);
            var hui = TraitRules.CastEffects(g.Get("㵘"), CardFace.Feature, 4);
            Assert.That(TraitRules.HealReshapeIndex(lin), Is.GreaterThanOrEqualTo(0), "淋五行面只有 HoT,细雨改它");
            Assert.That(TraitRules.HealReshapeIndex(hui), Is.EqualTo(-1), "㵘五行面是 HealAll,泽被空转");
        }
    }
}
