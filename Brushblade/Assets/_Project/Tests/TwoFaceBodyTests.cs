using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>出货字表的两面本体(spec v7 §2.1 / §4 的 Lv1 写法,D1 Task 12)。
    /// 21 张原单面字(火 11、金 9、花)拆成攻击 + 五行面;55 字两面本体按 §4 重写;
    /// 字卡原有的附加效果作废(只改数据、不删枚举,D6);形状按用户拍板 U2 —— §9「本体」列写「—」的一律单体。
    /// 读真实 chars.json(仓库根用 TestContext.CurrentContext.TestDirectory 定位,见 CharTableTests.RealGraph)。</summary>
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
        public void Fire_AttackIsDamagePlusBurn_FeatureIsBurnPlusWeaken()
        {
            foreach (var d in OfElement(Element.Fire))
            {
                int n = BurnStacks(d.Rarity);
                var burnA = d.AttackEffects.Where(e => IsBurn(e.Kind)).ToList();
                Assert.That(burnA.Count, Is.EqualTo(1), $"{d.Id} 攻:灼");
                Assert.That(burnA[0].Value, Is.EqualTo(n), $"{d.Id} 攻:灼层数按档");
                Assert.That(d.AttackEffects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.DamageSingle, burnA[0].Kind }), $"{d.Id} 攻 = 伤害 + 灼");

                Assert.That(d.Effects.Any(e => e.Kind == EffectKind.DamageSingle), Is.False, $"{d.Id} 燃面不该有伤害");
                var burnF = d.Effects.Where(e => IsBurn(e.Kind)).ToList();
                Assert.That(burnF.Count, Is.EqualTo(1), $"{d.Id} 燃:灼");
                Assert.That(burnF[0].Value, Is.EqualTo(n), $"{d.Id} 燃:灼层数按档");
                var weaken = Only(d.Effects, EffectKind.Weaken, d.Id);
                Assert.That(weaken.Value, Is.EqualTo(15), $"{d.Id} 燃:攻击 −15%");
                Assert.That(weaken.Turns, Is.EqualTo(2), $"{d.Id} 燃:2 回合");
                Assert.That(d.Effects.Count, Is.EqualTo(2), $"{d.Id} 燃 = 灼 + 减攻");
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
                Assert.That(Only(d.Effects, EffectKind.Morale, d.Id).Value, Is.EqualTo(n), $"{d.Id} 铠:战意按档");
                Assert.That(Only(d.Effects, EffectKind.Block, d.Id).Value, Is.EqualTo(1), $"{d.Id} 铠:格挡 1 次");
            }
        }

        [Test]
        public void Water_AttackHasControl_FeatureIsHealSelfPlusHoT()
        {
            var freeze = new HashSet<string> { "冻", "湮", "冰", "淼", "㵘" };
            foreach (var d in OfElement(Element.Water))
            {
                var control = freeze.Contains(d.Id) ? EffectKind.Freeze : EffectKind.Slow;
                Assert.That(Only(d.AttackEffects, control, d.Id).Value, Is.EqualTo(1), $"{d.Id} 攻:{control} 1 回合");
                Assert.That(d.Effects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.HealSelf, EffectKind.HealOverTime }), $"{d.Id} 润 = 治疗 + 润泽");
                var heal = Only(d.Effects, EffectKind.HealSelf, d.Id);
                var hot = Only(d.Effects, EffectKind.HealOverTime, d.Id);
                Assert.That(hot.Turns, Is.EqualTo(2), $"{d.Id} 润泽 2 回合");
                Assert.That(hot.Value, Is.EqualTo((int)System.Math.Round(heal.Value * 0.2, System.MidpointRounding.AwayFromZero)),
                    $"{d.Id} 润泽 = 治疗 × 20%");
                Assert.That(heal.Shape, Is.EqualTo(TargetArea.Single), $"{d.Id} 润:单体(U2)");
            }
            Assert.That(Only(Char("沐").Effects, EffectKind.HealSelf, "沐").Value, Is.EqualTo(86), "沐:H = 原持续治疗 43 × 2");
        }

        [Test]
        public void Earth_AttackHasArmorBreak_FeatureIsShieldPlusDefense()
        {
            foreach (var d in OfElement(Element.Earth))
            {
                Assert.That(Only(d.AttackEffects, EffectKind.ArmorBreak, d.Id).Turns, Is.EqualTo(2), $"{d.Id} 攻:破甲 2 回合");
                Assert.That(d.Effects.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { EffectKind.Shield, EffectKind.DefenseBuff }), $"{d.Id} 固 = 护盾 + 护甲");
                Assert.That(Only(d.Effects, EffectKind.DefenseBuff, d.Id).Turns, Is.EqualTo(2), $"{d.Id} 固:护甲 2 回合");
                Assert.That(Only(d.Effects, EffectKind.Shield, d.Id).PersistOnce, Is.False, $"{d.Id} 固:无豁免");
            }
        }

        [Test]
        public void Wood_AttackHasSeed_FeatureSummons_FlowerCharms()
        {
            foreach (var d in OfElement(Element.Wood))
            {
                Assert.That(Only(d.Effects, EffectKind.Summon, d.Id).SummonCount, Is.EqualTo(1), $"{d.Id} 生:召唤 1 只");
                if (d.Id == "花") continue;
                Assert.That(Only(d.AttackEffects, EffectKind.Seed, d.Id).Turns, Is.EqualTo(2), $"{d.Id} 攻:种 2 回合");
            }
            var hua = Char("花");
            Assert.That(hua.AttackEffects.Select(e => e.Kind).ToList(),
                Is.EqualTo(new[] { EffectKind.DamageSingle, EffectKind.Charm }), "花 攻 = 伤害 + 魅惑");
            Assert.That(Only(hua.AttackEffects, EffectKind.DamageSingle, "花").Value, Is.EqualTo(36));
            Assert.That(Only(hua.AttackEffects, EffectKind.Charm, "花").Turns, Is.EqualTo(1), "花 魅惑 1 回合");
            var flowerSpirit = Only(hua.Effects, EffectKind.Summon, "花");
            Assert.That(flowerSpirit.Value, Is.EqualTo(72), "花灵 血 = 攻击伤害 × 2");
            Assert.That(flowerSpirit.SummonAttack, Is.EqualTo(18), "花灵 攻 = 攻击伤害 × 50%");
            Assert.That(flowerSpirit.Passive.OnHitCharmChance, Is.EqualTo(20), "花灵本命迷香(D2-0 E7,取代 D8 的「无本命」)");
        }

        [Test]
        public void VoidedRiders_AppearInNoBody()
        {
            var voided = new[]
            {
                EffectKind.Quench, EffectKind.BurnPotency, EffectKind.Detonate, EffectKind.Haste,
                EffectKind.Unseal, EffectKind.Revive, EffectKind.Silence, EffectKind.Dispel,
                EffectKind.Reflect, EffectKind.Immunity, EffectKind.Empower, EffectKind.CritBuff,
                EffectKind.Bleed, EffectKind.HealAll, EffectKind.ShieldAll,
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
                    Assert.That(e.PersistOnce, Is.False, $"{d.Id}:{e.Kind} 仍带豁免");
                    Assert.That(e.Shape == TargetArea.Single || e.Shape == TargetArea.All, Is.True,
                        $"{d.Id}:{e.Kind} 形状 {e.Shape} 不是单体 / 全体(U2)");
                }
        }

        [Test]
        public void U2_Shapes()
        {
            TargetArea AttackShape(string id) => Only(Char(id).AttackEffects, EffectKind.DamageSingle, id).Shape;

            foreach (var id in new[] { "灿", "剑", "锥", "鑫", "焱", "淋", "海", "㙓" })
                Assert.That(AttackShape(id), Is.EqualTo(TargetArea.Single), $"{id} 攻击单体(U2)");
            foreach (var id in new[] { "爆", "炸", "烈", "焚", "燚", "溃", "㵘", "崩" })
                Assert.That(AttackShape(id), Is.EqualTo(TargetArea.All), $"{id} 攻击全体(本体列)");

            // 焱:燃面全体
            var yan3 = Char("焱");
            Assert.That(Only(yan3.Effects, EffectKind.BurnAll, "焱").Value, Is.EqualTo(3));
            Assert.That(Only(yan3.Effects, EffectKind.Weaken, "焱").Pick, Is.EqualTo(EffectPick.All), "焱 燃:减攻全体");
            Assert.That(Only(yan3.AttackEffects, EffectKind.BurnSingle, "焱").Value, Is.EqualTo(3), "焱 攻:单体灼");

            // 全体控制 / 全体破甲 + 释放
            Assert.That(Only(Char("溃").AttackEffects, EffectKind.Slow, "溃").Pick, Is.EqualTo(EffectPick.All));
            Assert.That(Only(Char("㵘").AttackEffects, EffectKind.Freeze, "㵘").Pick, Is.EqualTo(EffectPick.All));
            Assert.That(Only(Char("崩").AttackEffects, EffectKind.ArmorBreak, "崩").Pick, Is.EqualTo(EffectPick.All));
            Assert.That(Only(Char("溃").AttackEffects, EffectKind.SpendWellspring, "溃").Value, Is.EqualTo(13));
            Assert.That(Only(Char("㵘").AttackEffects, EffectKind.SpendWellspring, "㵘").Value, Is.EqualTo(60));
            Assert.That(Only(Char("崩").AttackEffects, EffectKind.SpendHeft, "崩").Value, Is.EqualTo(13));
            Assert.That(Only(Char("㙓").AttackEffects, EffectKind.SpendHeft, "㙓").Value, Is.EqualTo(60));

            // 海:两面都不弹射
            Assert.That(Only(Char("海").Effects, EffectKind.HealSelf, "海").Shape, Is.EqualTo(TargetArea.Single));
        }

        [Test]
        public void SplitChars_KeepTheirOldDamageValue()
        {
            var expected = new Dictionary<string, int>
            {
                ["热"] = 45, ["爆"] = 4, ["炸"] = 42, ["烈"] = 44, ["燥"] = 112, ["蒸"] = 112,
                ["炎"] = 168, ["灿"] = 216, ["焚"] = 77, ["焱"] = 99, ["燚"] = 86,
                ["剑"] = 94, ["锥"] = 106, ["剿"] = 112, ["铡"] = 122, ["剁"] = 61,
                ["鍂"] = 98, ["鑫"] = 297, ["刲"] = 159, [Ju] = 384,
            };
            foreach (var kv in expected)
                Assert.That(Only(Char(kv.Key).AttackEffects, EffectKind.DamageSingle, kv.Key).Value,
                    Is.EqualTo(kv.Value), $"{kv.Key} 攻击伤害沿用原值");
        }

        [Test]
        public void Seed_StacksByTier()
        {
            var expected = new Dictionary<string, int>
            {
                ["藤"] = 12, ["箭"] = 15, ["楸"] = 18, ["荆"] = 18, ["林"] = 21, ["柘"] = 21,
                ["桂"] = 25, ["藻"] = 25, ["森"] = 25, [Ju4] = 30,
            };
            foreach (var kv in expected)
                Assert.That(Only(Char(kv.Key).AttackEffects, EffectKind.Seed, kv.Key).Value, Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void Earth_ArmorValues_ExistingOrByTier()
        {
            // 现有值照用(堡 / 碎 / 垚 的破甲,垒 / 堡 / 杜 / 垚 / 㙓 的护甲),没有的按档:白 10 … 红 40
            var armorBreak = new Dictionary<string, int>
            {
                ["碉"] = 10, ["垒"] = 15, ["壁"] = 15, ["堡"] = 20, ["崩"] = 20, ["碎"] = 20,
                ["塔"] = 25, ["圭"] = 30, ["杜"] = 30, ["垚"] = 35, ["㙓"] = 40,
            };
            var defense = new Dictionary<string, int>
            {
                ["碉"] = 10, ["垒"] = 20, ["壁"] = 15, ["堡"] = 28, ["崩"] = 20, ["碎"] = 20,
                ["塔"] = 25, ["圭"] = 30, ["杜"] = 55, ["垚"] = 75, ["㙓"] = 100,
            };
            foreach (var kv in armorBreak)
                Assert.That(Only(Char(kv.Key).AttackEffects, EffectKind.ArmorBreak, kv.Key).Value, Is.EqualTo(kv.Value), $"{kv.Key} 破甲");
            foreach (var kv in defense)
                Assert.That(Only(Char(kv.Key).Effects, EffectKind.DefenseBuff, kv.Key).Value, Is.EqualTo(kv.Value), $"{kv.Key} 护甲");
        }

        [Test]
        public void Targeting_OnSplitData()
        {
            // Review Focus 2:火字攻击面选敌、燃面(单体上灼)也选敌,焱 燃面全体不选;金铠面不选
            foreach (var d in OfElement(Element.Fire))
            {
                Assert.That(BattleEngine.FaceOf(d, attackMode: true), Is.EqualTo(CardFace.Attack), d.Id);
                Assert.That(BattleEngine.FaceOf(d, attackMode: false), Is.EqualTo(CardFace.Feature), d.Id);
                bool attackAll = d.AttackEffects[0].Shape == TargetArea.All;
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: true), Is.EqualTo(!attackAll), $"{d.Id} 攻");
                Assert.That(BattleEngine.NeedsTarget(d, attackMode: false), Is.EqualTo(d.Id != "焱"), $"{d.Id} 燃");
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
