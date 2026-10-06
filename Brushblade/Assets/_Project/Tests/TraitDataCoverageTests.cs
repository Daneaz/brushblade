using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>五系 Lv1 / Lv3 / 池词条数据落表的自检(D1 Task 13)。读真实 chars.json。
    /// 专属格(spec §9 加粗名)留给 D2,这里不查也不许有:每字特性总数 = 1(Lv1)+ Lv3 + 池格数。</summary>
    public class TraitDataCoverageTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        // 增补平面字在 chars.json 里走 PUA 代理码位:𣛧(U+236E7)/ 𨰻(U+28C3B)不在 BMP,
        // 字表里的 id 是 BMP 私用区码位(U+E625 / U+E626,返回值是不可见的 PUA 字符,不是空串)。
        // 只换这两个;其余字原样返回。新增增补平面字要在这里与 EveryPoolCell_… 里的反向映射同步加。
        private static string Id(string ch) => ch == "𣛧" ? "" : ch == "𨰻" ? "" : ch;

        private static List<CharDef> Playable() =>
            Graph.All.Where(d => !d.IsComponent && d.Element != null).ToList();

        private static List<CharDef> OfElement(Element e) => Playable().Where(d => d.Element == e).ToList();

        private static List<TraitDef> At(CharDef d, TraitSlot slot) => d.Traits.Where(t => t.Slot == slot).ToList();

        private static IReadOnlyList<EffectDef> BodyOf(CharDef d, TraitFace face) =>
            face == TraitFace.Attack ? d.AttackEffects : d.Effects;

        private static string KeywordOf(CharDef d)
        {
            switch (d.Element)
            {
                case Element.Fire: return "灼";
                case Element.Metal: return "战意";
                case Element.Earth: return "破甲";
                case Element.Wood: return d.Id == "花" ? "魅惑" : "种";
                default:
                    return d.AttackEffects.Any(e => e.Kind == EffectKind.Freeze) ? "冻结" : "减速";
            }
        }

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

        private static bool IsBurn(EffectKind k) => k == EffectKind.BurnSingle || k == EffectKind.BurnAll;

        private static EffectDef FirstOf(IReadOnlyList<EffectDef> list, System.Func<EffectKind, bool> pred) =>
            list.First(e => pred(e.Kind));

        // ---- spec §9 池格清单(照表手抄:字 槽 面 名;专属格不在其中) ----
        // 炸 Lv5·攻「引燃」、溃 Lv5·攻「凝冰」:池条目落到全体面,管线自动补 pick All(Ruling 17)。
        private static readonly string[] PoolCells =
        {
            "热 Lv4 Both 先声", "热 Lv5 Attack 引燃", "热 Lv5 Feature 续火", "热 Lv6 Feature 烟熏", "热 Lv8 Feature 火雨",
            "爆 Lv4 Both 精进", "爆 Lv5 Attack 爆燃", "爆 Lv5 Feature 续火", "爆 Lv6 Attack 炽烈", "爆 Lv8 Feature 焦热",
            "炸 Lv4 Both 克敌", "炸 Lv5 Attack 引燃","炸 Lv6 Feature 烟熏", "炸 Lv8 Feature 火雨", "烈 Lv5 Attack 爆燃",
            "烈 Lv6 Attack 炽烈", "燥 Lv5 Feature 续火", "燥 Lv6 Feature 烟熏", "蒸 Lv5 Feature 烟障", "蒸 Lv6 Attack 炽烈",
            "炎 Lv5 Feature 续火", "灿 Lv5 Attack 引燃", "利 Lv4 Both 先声", "利 Lv5 Attack 破甲", "利 Lv5 Feature 砥砺",
            "利 Lv6 Feature 回锋", "利 Lv8 Attack 重斩", "锋 Lv4 Both 精进", "锋 Lv5 Attack 连斩", "锋 Lv5 Feature 砥砺",
            "锋 Lv6 Attack 迎刃", "锋 Lv8 Feature 金钟", "剑 Lv4 Both 克敌", "剑 Lv5 Attack 破甲", "剑 Lv6 Attack 迎刃",
            "剑 Lv8 Feature 反戈", "锥 Lv4 Both 补刀", "锥 Lv5 Attack 连斩", "锥 Lv6 Feature 回锋", "锥 Lv8 Feature 金钟",
            "剿 Lv4 Both 化险", "剿 Lv5 Attack 破甲", "剿 Lv6 Attack 迎刃", "剿 Lv8 Feature 反戈", "剁 Lv5 Attack 连斩",
            "剁 Lv6 Attack 迎刃", "铡 Lv5 Attack 破甲", "铡 Lv6 Feature 回锋", "鍂 Lv5 Feature 蓄势", "冷 Lv4 Both 克敌",
            "冷 Lv5 Attack 凝冰", "冷 Lv5 Feature 涓流", "冷 Lv6 Feature 护持", "冷 Lv8 Feature 甘露", "冻 Lv4 Both 精进",
            "冻 Lv5 Attack 激流", "冻 Lv5 Feature 寒泉", "冻 Lv6 Attack 冰缚", "冻 Lv8 Feature 蓄泉", "海 Lv4 Both 化险",
            "海 Lv5 Feature 涓流", "海 Lv6 Feature 护持", "海 Lv8 Attack 怒涛", "溃 Lv4 Both 先声", "溃 Lv5 Attack 凝冰",
            "溃 Lv6 Feature 护持", "溃 Lv8 Feature 甘露", "湮 Lv5 Attack 激流", "湮 Lv6 Attack 冰缚", "澡 Lv5 Feature 寒泉",
            "澡 Lv6 Feature 护持", "冰 Lv5 Attack 凝冰", "沐 Lv5 Feature 涓流", "碉 Lv4 Both 先声", "碉 Lv5 Attack 碎石",
            "碉 Lv5 Feature 加固", "碉 Lv6 Feature 反震", "碉 Lv8 Attack 崩岩", "垒 Lv4 Both 化险", "垒 Lv5 Attack 震地",
            "垒 Lv5 Feature 垒土", "垒 Lv6 Feature 反震", "垒 Lv8 Attack 劈山", "壁 Lv4 Both 精进", "壁 Lv5 Attack 碎石",
            "壁 Lv5 Feature 加固", "壁 Lv6 Attack 余震", "壁 Lv8 Attack 崩岩", "堡 Lv4 Both 克敌", "堡 Lv5 Feature 垒土",
            "堡 Lv6 Feature 反震", "堡 Lv8 Attack 劈山", "崩 Lv4 Both 破敌", "崩 Lv5 Attack 震地", "崩 Lv6 Attack 余震",
            "崩 Lv8 Feature 金汤", "碎 Lv4 Both 补刀", "碎 Lv5 Attack 碎石", "碎 Lv6 Attack 余震", "碎 Lv8 Feature 坚壁",
            "塔 Lv5 Feature 加固", "塔 Lv6 Feature 反震", "杜 Lv5 Feature 垒土", "圭 Lv5 Feature 加固", "花 Lv4 Both 先声",
            "花 Lv5 Attack 寄生", "花 Lv5 Feature 新芽", "花 Lv6 Feature 扎根", "花 Lv8 Feature 繁生", "藤 Lv4 Both 精进",
            "藤 Lv5 Attack 寄生", "藤 Lv5 Feature 沃土", "藤 Lv6 Attack 汲取", "藤 Lv8 Attack 缠缚", "箭 Lv4 Both 破敌",
            "箭 Lv5 Attack 蔓刺", "箭 Lv6 Feature 扎根", "箭 Lv8 Feature 灵荫", "楸 Lv5 Feature 新芽", "楸 Lv6 Feature 扎根",
            "荆 Lv5 Feature 沃土", "荆 Lv6 Feature 扎根", "林 Lv5 Feature 新芽", "柘 Lv5 Feature 沃土"
        };

        [Test]
        public void PoolCellList_Has124Cells()
        {
            Assert.That(PoolCells.Length, Is.EqualTo(124));
        }

        [Test]
        public void All55Chars_HaveLv1_BothFaces_ActiveNamedByKeyword_NoEffects()
        {
            var all = Playable();
            Assert.That(all.Count, Is.EqualTo(55));
            foreach (var d in all)
            {
                var lv1 = At(d, TraitSlot.Lv1);
                Assert.That(lv1.Count, Is.EqualTo(1), $"{d.Id} 应恰有一条 Lv1");
                Assert.That(lv1[0].Face, Is.EqualTo(TraitFace.Both), $"{d.Id} Lv1 面");
                Assert.That(lv1[0].Form, Is.EqualTo(TraitForm.Active), $"{d.Id} Lv1 形态");
                Assert.That(lv1[0].Replaces.HasValue, Is.False, $"{d.Id} Lv1 不替换");
                Assert.That(lv1[0].Name, Is.EqualTo(KeywordOf(d)), $"{d.Id} Lv1 名");
                Assert.That(lv1[0].Effects.Count, Is.EqualTo(0), $"{d.Id} Lv1 效果在本体里,特性行只放名字");
            }
        }

        [Test]
        public void All55Chars_HaveLv3_ReplacingLv1_NamedStrengthen()
        {
            foreach (var d in Playable())
            {
                var lv3 = At(d, TraitSlot.Lv3);
                bool woodNoFeature = d.Element == Element.Wood;
                Assert.That(lv3.Count, Is.EqualTo(woodNoFeature ? 1 : 2), $"{d.Id} Lv3 行数");
                Assert.That(lv3.Any(t => t.Face == TraitFace.Attack), Is.True, $"{d.Id} 缺 Lv3 攻");
                Assert.That(lv3.Any(t => t.Face == TraitFace.Feature), Is.EqualTo(!woodNoFeature), $"{d.Id} Lv3 五行面");
                foreach (var t in lv3)
                {
                    Assert.That(t.Replaces, Is.EqualTo(TraitSlot.Lv1), $"{d.Id} Lv3 替换 Lv1");
                    Assert.That(t.Form, Is.EqualTo(TraitForm.Active), $"{d.Id} Lv3 主动");
                    Assert.That(t.Name, Is.EqualTo(KeywordOf(d) + "·强化"), $"{d.Id} Lv3 名");
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} Lv3 要有效果");
                    var body = BodyOf(d, t.Face);
                    foreach (var e in t.Effects)
                        Assert.That(body.Any(b => b.Kind == e.Kind), Is.True,
                            $"{d.Id} Lv3 {t.Face} 的 {e.Kind} 在本体同面里找不到同 Kind(会被追加而不是替换)");
                }
            }
        }

        [Test]
        public void Fire_Lv3_BurnPlusOne_FeatureWeakenThreeTurns()
        {
            foreach (var d in OfElement(Element.Fire))
            {
                int n = BurnStacks(d.Rarity);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { FirstOf(d.AttackEffects, IsBurn).Kind }), $"{d.Id} 攻");
                Assert.That(a[0].Value, Is.EqualTo(n + 1), $"{d.Id} 攻 灼 N+1");
                Assert.That(f.Select(e => e.Kind).ToList(),
                    Is.EqualTo(new[] { FirstOf(d.Effects, IsBurn).Kind, EffectKind.Weaken }), $"{d.Id} 燃");
                Assert.That(f[0].Value, Is.EqualTo(n + 1), $"{d.Id} 燃 灼 N+1");
                var bodyWeaken = d.Effects.Single(e => e.Kind == EffectKind.Weaken);
                Assert.That(f[1].Value, Is.EqualTo(15));
                Assert.That(f[1].Turns, Is.EqualTo(3), $"{d.Id} 燃 减攻 3 回合");
                Assert.That(f[1].Pick, Is.EqualTo(bodyWeaken.Pick), $"{d.Id} 减攻选择器随本体");
            }
            // 焱:燃面全体灼 + 全体减攻
            var yan = Graph.Get("焱");
            var yf = At(yan, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
            Assert.That(yf[0].Kind, Is.EqualTo(EffectKind.BurnAll));
            Assert.That(yf[1].Pick, Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Metal_Lv3_MoralePlusOne_FeatureBlockTwo()
        {
            foreach (var d in OfElement(Element.Metal))
            {
                int n = MoraleStacks(d.Rarity);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Morale }), $"{d.Id} 攻");
                Assert.That(a[0].Value, Is.EqualTo(n + 1), $"{d.Id} 攻 战意 N+1");
                Assert.That(f.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Morale, EffectKind.Block }), $"{d.Id} 铠");
                Assert.That(f[0].Value, Is.EqualTo(n + 1), $"{d.Id} 铠 战意 N+1");
                Assert.That(f[1].Value, Is.EqualTo(2), $"{d.Id} 铠 格挡 2 次");
            }
        }

        [Test]
        public void Water_Lv3_ControlPlusOneTurn_HealOverTimeThreeTurns()
        {
            foreach (var d in OfElement(Element.Water))
            {
                var ctrl = d.AttackEffects.Single(e => e.Kind == EffectKind.Freeze || e.Kind == EffectKind.Slow);
                var hot = d.Effects.Single(e => e.Kind == EffectKind.HealOverTime);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Count, Is.EqualTo(1), $"{d.Id} 攻");
                Assert.That(a[0].Kind, Is.EqualTo(ctrl.Kind), $"{d.Id} 攻 控制种类随本体");
                Assert.That(a[0].Value, Is.EqualTo(ctrl.Value + 1), $"{d.Id} 攻 控制 +1 回合");
                Assert.That(a[0].Pick, Is.EqualTo(ctrl.Pick), $"{d.Id} 攻 选择器随本体");
                Assert.That(f.Count, Is.EqualTo(1), $"{d.Id} 润");
                Assert.That(f[0].Kind, Is.EqualTo(EffectKind.HealOverTime));
                Assert.That(f[0].Value, Is.EqualTo(hot.Value), $"{d.Id} 润泽数值照本体");
                Assert.That(f[0].Turns, Is.EqualTo(3), $"{d.Id} 润泽 3 回合");
            }
            Assert.That(At(Graph.Get("溃"), TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects[0].Pick,
                Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Earth_Lv3_FourTurns()
        {
            foreach (var d in OfElement(Element.Earth))
            {
                var ab = d.AttackEffects.Single(e => e.Kind == EffectKind.ArmorBreak);
                var db = d.Effects.Single(e => e.Kind == EffectKind.DefenseBuff);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Count, Is.EqualTo(1), $"{d.Id} 攻");
                Assert.That(a[0].Kind, Is.EqualTo(EffectKind.ArmorBreak));
                Assert.That(a[0].Value, Is.EqualTo(ab.Value), $"{d.Id} 破甲点数照本体");
                Assert.That(a[0].Turns, Is.EqualTo(4), $"{d.Id} 破甲 4 回合");
                Assert.That(a[0].Pick, Is.EqualTo(ab.Pick), $"{d.Id} 选择器随本体");
                Assert.That(f.Count, Is.EqualTo(1), $"{d.Id} 固");
                Assert.That(f[0].Kind, Is.EqualTo(EffectKind.DefenseBuff));
                Assert.That(f[0].Value, Is.EqualTo(db.Value), $"{d.Id} 护甲点数照本体");
                Assert.That(f[0].Turns, Is.EqualTo(4), $"{d.Id} 护甲 4 回合");
            }
            Assert.That(At(Graph.Get("崩"), TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects[0].Pick,
                Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Wood_Lv3_SeedThreeTurns_FlowerCharmTwoTurns_NoFeatureRow()
        {
            foreach (var d in OfElement(Element.Wood))
            {
                var a = At(d, TraitSlot.Lv3).Single().Effects;
                Assert.That(At(d, TraitSlot.Lv3).Single().Face, Is.EqualTo(TraitFace.Attack), $"{d.Id} Lv3 只有攻");
                Assert.That(a.Count, Is.EqualTo(1));
                if (d.Id == "花")
                {
                    Assert.That(a[0].Kind, Is.EqualTo(EffectKind.Charm));
                    Assert.That(a[0].Turns, Is.EqualTo(2), "花 魅惑 2 回合");
                }
                else
                {
                    var seed = d.AttackEffects.Single(e => e.Kind == EffectKind.Seed);
                    Assert.That(a[0].Kind, Is.EqualTo(EffectKind.Seed));
                    Assert.That(a[0].Value, Is.EqualTo(seed.Value), $"{d.Id} 种的量照本体");
                    Assert.That(a[0].Turns, Is.EqualTo(3), $"{d.Id} 种 3 回合");
                }
            }
        }

        [Test]
        public void EveryPoolCell_FromSpecSection9_IsPresent_AndNothingElseBeyondLv3()
        {
            var expected = new Dictionary<string, List<(TraitSlot slot, TraitFace face, string name)>>();
            foreach (var cell in PoolCells)
            {
                var p = cell.Split(' ');
                var slot = (TraitSlot)int.Parse(p[1].Substring(2));
                var face = (TraitFace)System.Enum.Parse(typeof(TraitFace), p[2]);
                if (!expected.TryGetValue(p[0], out var list))
                    expected[p[0]] = list = new List<(TraitSlot, TraitFace, string)>();
                list.Add((slot, face, p[3]));
            }
            foreach (var d in Playable())
            {
                var real = d.Id == "" ? "𣛧" : d.Id == "" ? "𨰻" : d.Id;
                var want = expected.TryGetValue(real, out var w) ? w : new List<(TraitSlot, TraitFace, string)>();
                var got = d.Traits.Where(t => (int)t.Slot >= 4).ToList();
                foreach (var (slot, face, name) in want)
                    Assert.That(got.Any(t => t.Slot == slot && t.Face == face && t.Name == name), Is.True,
                        $"{real} 缺 {slot}/{face}/{name}");
                Assert.That(got.Count, Is.EqualTo(want.Count), $"{real} Lv4+ 特性数应等于 spec §9 池格数(专属格留 D2)");
            }
            Assert.That(expected.Values.Sum(l => l.Count), Is.EqualTo(PoolCells.Length));
        }

        [Test]
        public void PoolTraits_HaveSlotFormAndPoolShape()
        {
            foreach (var d in Playable())
                foreach (var t in d.Traits.Where(t => (int)t.Slot >= 4))
                {
                    if (t.Slot == TraitSlot.Lv4)
                    {
                        Assert.That(t.Face, Is.EqualTo(TraitFace.Both), $"{d.Id} {t.Name}");
                        Assert.That(t.Form, Is.EqualTo(TraitForm.Passive), $"{d.Id} {t.Name}");
                    }
                    else if (t.Slot == TraitSlot.Lv6)
                        Assert.That(t.Form, Is.EqualTo(TraitForm.Passive), $"{d.Id} {t.Name}");
                    else
                        Assert.That(t.Face, Is.Not.EqualTo(TraitFace.Both), $"{d.Id} {t.Name} Lv5/Lv8 要有作用面");
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} {t.Name}");
                }
        }

        private static EffectDef PoolEffect(string ch, TraitSlot slot, TraitFace face, string name, EffectKind kind)
        {
            var t = Graph.Get(Id(ch)).Traits.Single(x => x.Slot == slot && x.Face == face && x.Name == name);
            return t.Effects.First(e => e.Kind == kind);
        }

        [Test]
        public void PoolValues_ScaleByRarity_BaseTimesMultiplier()
        {
            // 白 1.0:利(白)Lv5 破甲 X=15 → 15;热(白)先声 X=15 → 15
            Assert.That(PoolEffect("利", TraitSlot.Lv5, TraitFace.Attack, "破甲", EffectKind.ArmorBreak).Value, Is.EqualTo(15));
            Assert.That(PoolEffect("热", TraitSlot.Lv4, TraitFace.Both, "先声", EffectKind.Amplify).Value, Is.EqualTo(15));
            // 绿 1.2:冻(绿)精进 10 → 12;垒(绿)劈山 ArmorBreak 15 → 18;藤(绿)沃土 30 → 36
            Assert.That(PoolEffect("冻", TraitSlot.Lv4, TraitFace.Both, "精进", EffectKind.Amplify).Value, Is.EqualTo(12));
            Assert.That(PoolEffect("垒", TraitSlot.Lv8, TraitFace.Attack, "劈山", EffectKind.ArmorBreak).Value, Is.EqualTo(18));
            Assert.That(PoolEffect("藤", TraitSlot.Lv5, TraitFace.Feature, "沃土", EffectKind.HealSummons).Value, Is.EqualTo(36));
            // 蓝 1.45:剿(蓝)化险 20 → 29;崩(蓝)破敌 15 → 22(21.75 入);堡(蓝)克敌 写死 15 不放大
            Assert.That(PoolEffect("剿", TraitSlot.Lv4, TraitFace.Both, "化险", EffectKind.Amplify).Value, Is.EqualTo(29));
            Assert.That(PoolEffect("崩", TraitSlot.Lv4, TraitFace.Both, "破敌", EffectKind.Amplify).Value, Is.EqualTo(22));
            Assert.That(PoolEffect("堡", TraitSlot.Lv4, TraitFace.Both, "克敌", EffectKind.Amplify).Value, Is.EqualTo(15));
            // 写死数字不放大:爆(绿)爆燃 +30%
            Assert.That(PoolEffect("爆", TraitSlot.Lv5, TraitFace.Attack, "爆燃", EffectKind.Amplify).Value, Is.EqualTo(30));
        }

        // ---- 终审 Critical:Lv6 被动(出字时机)的非修饰效果要进出字效果表 ----

        [Test]
        public void Lv6PassiveCastEffects_RealData_AreInCastEffects()
        {
            var beng = TraitRules.CastEffects(Graph.Get("崩"), CardFace.Attack, 8);
            Assert.That(beng.Any(e => e.Kind == EffectKind.Weaken && e.Pick == EffectPick.HitTargets), Is.True,
                "崩 Lv8 攻面应含余震(Weaken pick HitTargets)");
            Assert.That(TraitRules.CastEffects(Graph.Get("冷"), CardFace.Feature, 8)
                .Any(e => e.Kind == EffectKind.ShieldFromHeal), Is.True, "冷 Lv8 润面应含护持(ShieldFromHeal)");
            Assert.That(TraitRules.CastEffects(Graph.Get("冻"), CardFace.Attack, 8)
                .Any(e => e.Kind == EffectKind.Vulnerable), Is.True, "冻 Lv8 攻面应含冰缚(Vulnerable)");
            Assert.That(TraitRules.CastEffects(Graph.Get("箭"), CardFace.Feature, 8)
                .Any(e => e.Kind == EffectKind.Endure), Is.True, "箭 Lv8 生面应含扎根(Endure)");
            // 未解锁时不在
            Assert.That(TraitRules.CastEffects(Graph.Get("崩"), CardFace.Attack, 5)
                .Any(e => e.Kind == EffectKind.Weaken), Is.False, "Lv5 不解锁 Lv6");
        }
    }
}
