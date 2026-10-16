using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>出货字表的两面本体(spec v7 §2.1 / §4 的 Lv1 写法,D1 Task 12)。
    /// 21 张原单面字(火 11、金 9、花)拆成攻击 + 五行面;55 字两面本体按 §4 重写;
    /// 字卡原有的附加效果作废(只改数据、不删枚举,D6);形状按用户拍板 U2 —— §9「本体」列写「—」的一律单体。
    /// 读真实 chars.json(仓库根用 TestContext.CurrentContext.TestDirectory 定位,见 CharTableTests.RealGraph)。
    /// spec A(2026-10-15)起:攻击面全体 ⇒ 五行面也全体(§3);五行面附带灼 / 战意恒 1、主手段按档梯度(§4);
    /// 水改涓 / 涌 / 苏三模式(§4.3);起始数值按锚点 × 70% × 范围系数(§2,逐字断言在 SpecAAlignmentTests)。</summary>
    public class TwoFaceBodyTests
    {
        private const string Ju = "";   // 𨰻 的 PUA 代理码位
        private const string Ju4 = "";  // 𣛧 的 PUA 代理码位

        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static List<CharDef> Playable() =>
            Graph.All.Where(d => !d.IsComponent && d.Element != null).ToList();

        private static List<CharDef> OfElement(Element element) =>
            Playable().Where(d => d.Element == element).ToList();

        private static CharDef Char(string id) => Graph.Get(id);

        private static bool IsBurn(EffectKind k) => k == EffectKind.BurnSingle || k == EffectKind.BurnAll;

        // spec §4 各档 N
        private static int BurnStacks(CardRarity r) => r switch
        {
            CardRarity.White or CardRarity.Green or CardRarity.Blue => 1,
            CardRarity.Purple => 2,
            CardRarity.Gold or CardRarity.Orange => 3,
            _ => 4,
        };

        private static int MoraleStacks(CardRarity r) => r switch
        {
            CardRarity.White or CardRarity.Green or CardRarity.Blue => 1,
            CardRarity.Purple or CardRarity.Gold => 2,
            _ => 3,
        };

        private static readonly CardRarity[] Tiers =
            { CardRarity.White, CardRarity.Green, CardRarity.Blue, CardRarity.Purple, CardRarity.Gold, CardRarity.Orange, CardRarity.Red };
        private static int Tier(CardRarity r) => System.Array.IndexOf(Tiers, r);

        // spec A §4.1 / §4.2 / §4.4 五行面梯度(白 → 红)
        private static readonly (int Pct, int Turns)[] WeakenByTier = { (15, 2), (15, 2), (20, 2), (20, 3), (25, 3), (30, 3), (35, 3) };
        private static readonly (int Count, int Counter)[] BlockByTier = { (1, 30), (1, 35), (2, 40), (2, 45), (2, 50), (3, 60), (3, 70) };
        private static readonly (int Points, int Turns)[] ArmorByTier = { (10, 2), (15, 2), (20, 2), (25, 2), (35, 3), (50, 3), (70, 3) };

        private static EffectDef Only(IReadOnlyList<EffectDef> face, EffectKind kind, string who)
        {
            var found = face.Where(e => e.Kind == kind).ToList();
            Assert.That(found.Count, Is.EqualTo(1), $"{who}:应恰有一条 {kind}");
            return found[0];
        }

        [Test]
        public void All55Chars_HaveBothFaces()
        {
            var all = Playable();
            Assert.That(all.Count, Is.EqualTo(55));
            foreach (var d in all)
            {
                Assert.That(d.Effects.Count, Is.GreaterThan(0), $"{d.Id} 五行面为空");
                Assert.That(d.AttackEffects.Count, Is.GreaterThan(0), $"{d.Id} 攻击面为空");
                Assert.That(d.AttackEffects.Any(e => e.Kind == EffectKind.DamageSingle), Is.True,
                    $"{d.Id} 攻击面没有伤害");
            }
            foreach (var element in new[] { Element.Fire, Element.Metal, Element.Water, Element.Earth, Element.Wood })
                Assert.That(OfElement(element).Count, Is.EqualTo(11), $"{element} 应 11 字");
        }

        [Test]
        public void Fire_AttackIsDamagePlusBurn_FeatureIsKindleOnly()
        {
            foreach (var d in OfElement(Element.Fire))
            {
                int n = BurnStacks(d.Rarity);
                var burnA = d.AttackEffects.Where(e => IsBurn(e.Kind)).ToList();
                Assert.That(burnA.Count, Is.EqualTo(1), $"{d.Id} 攻:灼");
                Assert.That(burnA[0].Value, Is.EqualTo(n), $"{d.Id} 攻:灼层数按档");
                Assert.That(d.AttackEffects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.DamageSingle, burnA[0].Kind }), $"{d.Id} 攻 = 伤害 + 灼");

                // spec C §5:五行面 = 燃意一条(按档梯度);攻击面全体 ⇒ 燃意挂我方全体(AllAllies),单体 ⇒ 默认(玩家 / 被选友方)
                bool all = d.AttackEffects[0].Shape == TargetArea.All;
                Assert.That(d.Effects.Any(e => e.Kind == EffectKind.DamageSingle), Is.False, $"{d.Id} 五行面不该有伤害");
                Assert.That(d.Effects.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Kindle }), $"{d.Id} 五行面本体只有燃意");
                var kindle = d.Effects[0];
                var (basePct, turns) = WeakenByTier[Tier(d.Rarity)];
                // 2026-10-16 拍板:单体 = 梯度 +5;全体 = 梯度 ×0.5(四舍五入)+5
                int pct = all ? (basePct + 1) / 2 + 5 : basePct + 5;
                Assert.That(kindle.Value, Is.EqualTo(pct), $"{d.Id} 燃意 %");
                Assert.That(kindle.Turns, Is.EqualTo(turns), $"{d.Id} 燃意回合");
                Assert.That(kindle.Pick == EffectPick.AllAllies, Is.EqualTo(all), $"{d.Id} 燃意范围随攻击面");
            }
        }

        [Test]
        public void Metal_AttackIsDamagePlusMorale_FeatureIsMoralePlusBlock()
        {
            foreach (var d in OfElement(Element.Metal))
            {
                int n = MoraleStacks(d.Rarity);
                Assert.That(d.AttackEffects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.Morale }), $"{d.Id} 攻 = 伤害 + 战意");
                Assert.That(Only(d.AttackEffects, EffectKind.Morale, d.Id).Value, Is.EqualTo(n), $"{d.Id} 攻:战意按档");
                Assert.That(d.Effects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.Morale, EffectKind.Block }), $"{d.Id} 铠 = 战意 + 格挡");
                // spec A §4.2:五行面战意恒 1;格挡次数 + 反击 % 按档
                Assert.That(Only(d.Effects, EffectKind.Morale, d.Id).Value, Is.EqualTo(1), $"{d.Id} 格挡面:战意恒 1");
                var block = Only(d.Effects, EffectKind.Block, d.Id);
                var (count, counter) = BlockByTier[Tier(d.Rarity)];
                Assert.That(block.Value, Is.EqualTo(count), $"{d.Id} 格挡次数");
                Assert.That(block.CounterPercent, Is.EqualTo(counter), $"{d.Id} 反击 %");
            }
        }

        [Test]
        public void Water_AttackHasControl()
        {
            // 五行面「治疗 + 润泽」混合已由 spec A §4.3 改为涓 / 涌 / 苏单一模式,
            // 逐字断言见 SpecAAlignmentTests.Water_OneModePerChar_NamedByMode;这里只留攻击面控制。
            var freeze = new HashSet<string> { "冻", "湮", "冰", "淼", "㵘" };
            foreach (var d in OfElement(Element.Water))
            {
                // spec C §6:溃 / 㵘 的控制拆成了 涌泉相报 词条,攻击面本体只有伤害 + 释放
                if (d.Id == "溃" || d.Id == "㵘")
                {
                    Assert.That(d.AttackEffects.Select(e => e.Kind).ToList(),
                        Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.SpendWellspring }), $"{d.Id} 攻 = 伤害 + 释放");
                    continue;
                }
                var control = freeze.Contains(d.Id) ? EffectKind.Freeze : EffectKind.Slow;
                Assert.That(Only(d.AttackEffects, control, d.Id).Value, Is.EqualTo(1), $"{d.Id} 攻:{control} 1 回合");
            }
        }

        [Test]
        public void Earth_AttackHasArmorBreak_FeatureIsShieldPlusDefense()
        {
            foreach (var d in OfElement(Element.Earth))
            {
                // spec C §6:崩 / 㙓 的破甲拆成 厚积薄发 词条,攻击面本体只有伤害 + 释放
                if (d.Id == "崩" || d.Id == "㙓")
                    Assert.That(d.AttackEffects.Select(e => e.Kind).ToList(),
                        Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.SpendHeft }), $"{d.Id} 攻 = 伤害 + 释放");
                else
                    Assert.That(Only(d.AttackEffects, EffectKind.ArmorBreak, d.Id).Turns, Is.EqualTo(2), $"{d.Id} 攻:破甲 2 回合");
                // spec A §3:崩攻击面全体 ⇒ 五行面 ShieldAll;§4.4 护甲回合按档
                var shield = d.Id == "崩" ? EffectKind.ShieldAll : EffectKind.Shield;
                Assert.That(d.Effects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { shield, EffectKind.DefenseBuff }), $"{d.Id} 五行面 = 护盾 + 护甲");
                Assert.That(Only(d.Effects, EffectKind.DefenseBuff, d.Id).Turns, Is.EqualTo(ArmorByTier[Tier(d.Rarity)].Turns),
                    $"{d.Id} 护甲回合按档");
            }
        }

        [Test]
        public void Wood_AttackHasSeed_FeatureSummons_FlowerCharms()
        {
            foreach (var d in OfElement(Element.Wood))
            {
                Assert.That(Only(d.Effects, EffectKind.Summon, d.Id).SummonCount, Is.EqualTo(1), $"{d.Id} 生:召唤 1 只");
                Assert.That(Only(d.AttackEffects, EffectKind.Seed, d.Id).Turns, Is.EqualTo(2), $"{d.Id} 攻:种 2 回合");
            }
            // spec A §5:花攻击面魅惑 → 种 9(白档);§4.5 木灵 血 = 单体 42 × 1.5、攻 = 42 × 0.35
            var hua = Char("花");
            Assert.That(hua.AttackEffects.Select(e => e.Kind).ToList(),
                Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.Seed }), "花 攻 = 伤害 + 种");
            Assert.That(Only(hua.AttackEffects, EffectKind.DamageSingle, "花").Value, Is.EqualTo(42));
            Assert.That(Only(hua.AttackEffects, EffectKind.Seed, "花").Value, Is.EqualTo(9), "花 种 9");
            var flowerSpirit = Only(hua.Effects, EffectKind.Summon, "花");
            Assert.That(flowerSpirit.Value, Is.EqualTo(63), "花灵 血 = 42 × 1.5");
            Assert.That(flowerSpirit.SummonAttack, Is.EqualTo(15), "花灵 攻 = 42 × 0.35");
            Assert.That(flowerSpirit.Passive.OnHitCharmChance, Is.EqualTo(20), "花灵本命迷香(D2-0 E7,取代 D8 的「无本命」)");
        }

        [Test]
        public void VoidedRiders_AppearInNoBody()
        {
            var voided = new[]
            {
                EffectKind.Quench, EffectKind.BurnPotency, EffectKind.Detonate, EffectKind.Haste,
                EffectKind.Unseal, EffectKind.Silence, EffectKind.Dispel,
                EffectKind.Reflect, EffectKind.Immunity, EffectKind.Empower, EffectKind.CritBuff,
                EffectKind.Bleed,
                // spec A 起 Revive(苏)、HealAll(溃 / 㵘)、ShieldAll(崩)重新入本体,移出作废名单
            };
            foreach (var d in Playable())
                foreach (var e in d.Effects.Concat(d.AttackEffects))
                {
                    Assert.That(voided.Contains(e.Kind), Is.False, $"{d.Id}:作废的 {e.Kind} 仍在本体里");
                    Assert.That(e.DoubleVs, Is.EqualTo(DamageCondition.None), $"{d.Id}:{e.Kind} 仍带 DoubleVs");
                    Assert.That(e.HitCount, Is.LessThanOrEqualTo(1), $"{d.Id}:{e.Kind} 仍多击");
                    Assert.That(e.ExecuteBelowPercent, Is.EqualTo(0), $"{d.Id}:{e.Kind} 仍带斩杀");
                    Assert.That(e.ArmorStrikePercent, Is.EqualTo(0), $"{d.Id}:{e.Kind} 仍带镇压");
                    Assert.That(e.TrueDamage, Is.False, $"{d.Id}:{e.Kind} 仍带碾");
                    Assert.That(e.Shape == TargetArea.Single || e.Shape == TargetArea.All, Is.True,
                        $"{d.Id}:{e.Kind} 形状 {e.Shape} 不是单体 / 全体(U2)");
                }
        }

        [Test]
        public void U2_Shapes()
        {
            TargetArea AttackShape(string id) => Only(Char(id).AttackEffects, EffectKind.DamageSingle, id).Shape;

            foreach (var id in new[] { "灿", "剑", "锥", "鑫", "淋", "海", "㙓" })
                Assert.That(AttackShape(id), Is.EqualTo(TargetArea.Single), $"{id} 攻击单体(U2)");
            foreach (var id in new[] { "爆", "炸", "烈", "焚", "焱", "燚", "溃", "㵘", "崩" })
                Assert.That(AttackShape(id), Is.EqualTo(TargetArea.All), $"{id} 攻击全体(本体列)");

            // 焱(spec A §3 用户拍板):攻击面改全体 + BurnAll 3;五行面全体,灼恒 1
            var yan3 = Char("焱");
            // spec C §5:五行面 = 燃意(我方全体),不再有灼 / 慑
            Assert.That(Only(yan3.Effects, EffectKind.Kindle, "焱").Pick, Is.EqualTo(EffectPick.AllAllies), "焱 燃意:我方全体");
            Assert.That(Only(yan3.AttackEffects, EffectKind.BurnAll, "焱").Value, Is.EqualTo(3), "焱 攻:全体灼");

            // 两面范围对齐(spec A §3)
            Assert.That(Only(Char("溃").Effects, EffectKind.HealAll, "溃").Value, Is.EqualTo(45));
            Assert.That(Only(Char("㵘").Effects, EffectKind.HealAll, "㵘").Value, Is.EqualTo(210));
            Assert.That(Only(Char("崩").Effects, EffectKind.ShieldAll, "崩").Value, Is.EqualTo(45));

            // spec C §6:控制 / 破甲拆给词条;攻击面本体 = 伤害 + 释放
            Assert.That(Only(Char("溃").AttackEffects, EffectKind.SpendWellspring, "溃").Value, Is.EqualTo(13));
            Assert.That(Only(Char("㵘").AttackEffects, EffectKind.SpendWellspring, "㵘").Value, Is.EqualTo(60));
            Assert.That(Only(Char("崩").AttackEffects, EffectKind.SpendHeft, "崩").Value, Is.EqualTo(13));
            Assert.That(Only(Char("㙓").AttackEffects, EffectKind.SpendHeft, "㙓").Value, Is.EqualTo(60));

            // 海:两面都不弹射
            Assert.That(Only(Char("海").Effects, EffectKind.HealSelf, "海").Shape, Is.EqualTo(TargetArea.Single));
        }

        // SplitChars_KeepTheirOldDamageValue 已删:spec A §2 起攻击伤害一律 = 锚点 × 70% × 范围系数,
        // 逐字断言由 SpecAAlignmentTests.AttackDamage_IsAnchorTimesSeventy_ByArea 取代。

        [Test]
        public void Seed_StacksByTier()
        {
            var expected = new Dictionary<string, int>
            {
                ["花"] = 9, ["藤"] = 12, ["箭"] = 15, ["楸"] = 18, ["荆"] = 18, ["林"] = 21, ["柘"] = 21,
                ["桂"] = 25, ["藻"] = 25, ["森"] = 25, [Ju4] = 30,
            };
            foreach (var kv in expected)
                Assert.That(Only(Char(kv.Key).AttackEffects, EffectKind.Seed, kv.Key).Value, Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void Earth_ArmorValues_ExistingOrByTier()
        {
            // 破甲(攻击面附带)维持现状;护甲点按 spec A §4.4 梯度表(逐字列出,并与 ArmorByTier 对账)
            // spec C:崩 / 㙓 的破甲已拆出本体,不在表内
            var armorBreak = new Dictionary<string, int>
            {
                ["碉"] = 10, ["垒"] = 15, ["壁"] = 15, ["堡"] = 20, ["碎"] = 20,
                ["塔"] = 25, ["圭"] = 30, ["杜"] = 30, ["垚"] = 35,
            };
            var defense = new Dictionary<string, int>
            {
                ["碉"] = 10, ["垒"] = 15, ["壁"] = 15, ["堡"] = 20, ["崩"] = 20, ["碎"] = 20,
                ["塔"] = 25, ["圭"] = 35, ["杜"] = 35, ["垚"] = 50, ["㙓"] = 70,
            };
            foreach (var kv in defense)
                Assert.That(kv.Value, Is.EqualTo(ArmorByTier[Tier(Char(kv.Key).Rarity)].Points), $"{kv.Key} 护甲表与梯度对账");
            foreach (var kv in armorBreak)
                Assert.That(Only(Char(kv.Key).AttackEffects, EffectKind.ArmorBreak, kv.Key).Value, Is.EqualTo(kv.Value), $"{kv.Key} 破甲");
            foreach (var kv in defense)
                Assert.That(Only(Char(kv.Key).Effects, EffectKind.DefenseBuff, kv.Key).Value, Is.EqualTo(kv.Value), $"{kv.Key} 护甲");
        }

        [Test]
        public void Targeting_OnSplitData()
        {
            // 火字攻击面单体才选敌;五行面 = 燃意挂我方,任何火字都不选敌(spec C §5);金格挡面不选
            foreach (var d in OfElement(Element.Fire))
            {
                Assert.That(BattleEngine.FaceOf(d, attackMode: true), Is.EqualTo(CardFace.Attack), d.Id);
                Assert.That(BattleEngine.FaceOf(d, attackMode: false), Is.EqualTo(CardFace.Feature), d.Id);
                bool attackAll = d.AttackEffects[0].Shape == TargetArea.All;
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: true), Is.EqualTo(!attackAll), $"{d.Id} 攻");
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: false), Is.False, $"{d.Id} 五行面(燃意不选敌)");
            }
            foreach (var d in OfElement(Element.Metal))
            {
                Assert.That(BattleEngine.FaceOf(d, attackMode: true), Is.EqualTo(CardFace.Attack), d.Id);
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: true), Is.True, $"{d.Id} 攻");
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: false), Is.False, $"{d.Id} 铠");
            }
            Assert.That(BattleEngine.FaceOf(Char("花"), attackMode: true), Is.EqualTo(CardFace.Attack));
            Assert.That(BattleEngine.NeedsTarget(Char("花"), attackMode: false), Is.False, "花 生:召唤不选敌");
        }
    }
}
