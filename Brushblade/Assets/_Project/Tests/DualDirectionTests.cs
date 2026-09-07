using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>水/土两系「纯二选一」双方向字的形状与数值(2026-09-02,水土双方向)。
    ///
    /// 口径:<see cref="CharDef.Effects"/> = 治疗面(双击选「护」),
    /// <see cref="CharDef.AttackEffects"/> = 攻击面(双击选「攻」/拖到敌人身上)。
    ///
    /// 本文件覆盖 Task 10(水系 15 字)与 Task 11(土系 13 字)的用例;Task 12(火系两个数)
    /// 会往同一个文件里加自己的方法,互不冲突(见 progress.md 的 pre-flight 扫描)。</summary>
    public sealed class DualDirectionTests
    {
        // 2026-09-05:沏 / 沝 / 淡 三张水系双方向字随字表调整移出,从下表删去
        // (不找字顶替 —— 剩余 12 字仍覆盖白/绿/蓝/紫/金/红六档)。
        // 2026-09-07 字表重做 P2:浴 也随字表调整移出,同口径删去,不找字顶替 ——
        // 蓝档仍有 溃/海 两张守着,六档覆盖不受影响。
        private static readonly string[] WaterChars =
        {
            "溃", "冻", "海", "冷", "湮", "澡",
            "冰", "沐", "淼", "淋", "㵘",
        };

        /// <summary>做双方向的土系字。**不含召唤字**(碉/堡/塔)——2026-09-02 用户拍板:
        /// 召唤本身就是「把防御摆到场上」,再叠一个护盾面是同一件事收两次钱;而且召唤要选
        /// 落位槽,与「点敌人=攻 / 点我方=护」的目标语义打架。三张召唤字保持单方向。
        /// 由 <see cref="SummonChars_StayOneDirectional"/> 反向钉住,防止哪天又被顺手加回来。</summary>
        // 2026-09-05:砸 / 碾 两张土系双方向字随字表调整移出,从下表删去。
        private static readonly string[] EarthChars =
        {
            "垒", "壁", "崩", "碎", "圭", "杜", "垚", "㙓",
        };

        /// <summary>召唤字必须**没有**攻击面(2026-09-02)。这条与 <see cref="EarthChars"/>
        /// 的注释是一对:那边说「不含召唤字」,这边说「而且不许有」。
        /// 只写在清单注释里挡不住下一个人把它们加回去。</summary>
        private static readonly string[] SummonOnlyChars = { "碉", "堡", "塔" };

        private static RecipeGraph LoadRealGraph() => CharTableTests.RealGraph();

        /// <summary>该字护盾面第一条 Shield 效果的 Value。</summary>
        private static int ShieldValueOf(RecipeGraph graph, string id) =>
            graph.Get(id).Effects.First(e => e.Kind == EffectKind.Shield).Value;

        /// <summary>该字治疗面第一条治疗效果的 Value。</summary>
        private static int HealValueOf(RecipeGraph graph, string id)
        {
            var healKinds = new[] { EffectKind.HealSelf, EffectKind.HealAll, EffectKind.HealOverTime };
            var effect = graph.Get(id).Effects.First(e => healKinds.Contains(e.Kind));
            return effect.Value;
        }

        // ---- 护盾/治疗/攻击的锚点公式(2026-09-07 字表重做 P2,C 类修复)----
        //
        // ⚠ 这几个常量与算法节选自 tools/design/rebalance_2026_09_05.py(那份脚本是数值的
        // 唯一权威源,tools/design/tests/test_rebalance_reconcile.py 在 Python 侧直接 import
        // 它来避免抄数字;C# 侧不能 import Python,所以这里手抄了公式本身——只抄下面两条
        // 测试实际用到的档位/特性,不是整张价目表。**如果这两张字将来换了特性,要先去
        // rebalance 脚本核对新特性的定价再补进 Price;如果 rebalance 脚本自己的
        // SHIELD_F/HEAL_F/K/ANCHOR 常量变了,这里必须手动跟着改 —— 两边没有共享的单一
        // 来源,这条测试挡得住「配置值算错了」,挡不住「公式本身两边不同步」。**
        //
        // 公式(design §1.4/§1.4.1/§2):
        //   护盾 = A[护盾] × SHIELD_F(0.65) × 群体系数 × (1 − 特性预算 ratio)
        //   治疗 = A[治疗] × HEAL_F(1.00) × 群体系数 × (1 − 特性预算 ratio)
        //   攻击 = A[单攻或全体] × (1 − 特性预算 ratio)
        //   ratio = Σ(特性单价 × 该档位系数 K)
        private const double ShieldF = 0.65, HealF = 1.00;

        private static readonly Dictionary<CardRarity, double> RarityK = new()
        {
            [CardRarity.Green] = 1.00,
            [CardRarity.Purple] = 0.80,
            [CardRarity.Gold] = 0.65,
            [CardRarity.Red] = 0.45,
        };

        private static readonly Dictionary<CardRarity, (int Single, int All, int Shield, int Heal)> RarityAnchor = new()
        {
            [CardRarity.Green] = (90, 50, 70, 60),
            [CardRarity.Purple] = (200, 100, 150, 120),
            [CardRarity.Gold] = (400, 200, 300, 240),
            [CardRarity.Red] = (600, 300, 450, 350),
        };

        /// <summary>只收了下面两条测试用到的特性单价,不是全表价目(见上方大注释)。</summary>
        private static readonly Dictionary<string, double> TraitPrice = new()
        {
            ["反伤30"] = 0.22, ["反伤50"] = 0.35, ["对破甲"] = 0.25, ["终极技"] = 0.50,
            ["免一次清盾"] = 0.20, ["护甲"] = 0.33, ["免疫1"] = 0.35,
            ["冻结1"] = 0.35, ["冻结2"] = 0.55, ["对控制"] = 0.25, ["封禁"] = 0.38,
        };

        private static double Ratio(CardRarity rarity, params string[] traits) =>
            traits.Sum(t => TraitPrice[t]) * RarityK[rarity];

        private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        private static int ExpectedShield(CardRarity rarity, params string[] traits) =>
            Round(RarityAnchor[rarity].Shield * ShieldF * (1 - Ratio(rarity, traits)));

        private static int ExpectedHeal(CardRarity rarity, params string[] traits) =>
            Round(RarityAnchor[rarity].Heal * HealF * (1 - Ratio(rarity, traits)));

        private static int ExpectedSingleAttack(CardRarity rarity, params string[] traits) =>
            Round(RarityAnchor[rarity].Single * (1 - Ratio(rarity, traits)));

        [Test]
        public void EveryWaterChar_HasBothDirections()
        {
            var graph = LoadRealGraph();
            foreach (var id in WaterChars)
            {
                var def = graph.Get(id);
                Assert.That(def.Effects.Count, Is.GreaterThan(0), $"{id} 缺治疗面");
                Assert.That(def.AttackEffects.Count, Is.GreaterThan(0), $"{id} 缺攻击面");
            }
        }

        [Test]
        public void EveryWaterChar_HasHealOnSupportSide()
        {
            var graph = LoadRealGraph();
            var healKinds = new[] { EffectKind.HealSelf, EffectKind.HealAll, EffectKind.HealOverTime };
            foreach (var id in WaterChars)
            {
                var def = graph.Get(id);
                bool heals = def.Effects.Any(e => healKinds.Contains(e.Kind));
                Assert.That(heals, Is.True, $"{id} 的治疗面没有治疗效果");
            }
        }

        /// <summary>2026-09-07 字表重做 P2 重写:旧版直接钉「满值 340」「x0.7 带净化」这类
        /// 硬编码结果,那套折算规则(锚点砍半 / 满值 x0.7)已经被 spec §1.4 的公式取代
        /// (护盾/治疗 = 锚点 × F 系数 × 群体系数 × (1 − 特性预算)),继续钉旧结果会把测试
        /// 退化成「抄一遍当前配置」,数值再变就守不住任何规则了。改从**公式**推导期望值 ——
        /// 见上方 ExpectedHeal 一带的大注释。</summary>
        [Test]
        public void WaterCharValues_MatchRarityAnchors()
        {
            var graph = LoadRealGraph();
            Assert.That(HealValueOf(graph, "冰"), Is.EqualTo(ExpectedHeal(CardRarity.Gold, "冻结1", "对控制")),
                "金档:治疗锚点240 × HEAL_F × (1 − (冻结1+对控制)×K金)");
            Assert.That(HealValueOf(graph, "㵘"), Is.EqualTo(ExpectedHeal(CardRarity.Red, "终极技", "冻结2", "对控制")),
                "红档:治疗锚点350 × HEAL_F × (1 − (终极技+冻结2+对控制)×K红)");
            Assert.That(HealValueOf(graph, "湮"), Is.EqualTo(ExpectedHeal(CardRarity.Purple, "封禁", "对控制")),
                "紫档:治疗锚点120 × HEAL_F × (1 − (封禁+对控制)×K紫)");
        }

        /// <summary>攻击面必须真的能打人 —— 全是伤害类效果(单体/全体),不是挂个状态就算数。
        /// 钉的是「形状」:溃/冻/海/冷/浴/湮/澡/冰/沐/淼/淋/㵘 的攻击面都带伤害,
        /// 与 ConfigLoaderTests.ShippedCharsJson_StackedWaterAndEarth_BothDefendAndStrike
        /// 守的是同一条不变量,只是这条覆盖全部 12 字而不只是 冰。
        /// (2026-09-05:沏/沝/淡 随字表调整移出,15 字降到 12 字。)</summary>
        [Test]
        public void EveryWaterChar_AttackSideDealsDamage()
        {
            var graph = LoadRealGraph();
            var damageKinds = new[] { EffectKind.DamageSingle, EffectKind.DamageAll };
            foreach (var id in WaterChars)
            {
                var def = graph.Get(id);
                bool damages = def.AttackEffects.Any(e => damageKinds.Contains(e.Kind));
                Assert.That(damages, Is.True, $"{id} 的攻击面没有伤害效果");
            }
        }

        /// <summary>攻击面用 Cast(attackMode: true) 真的能打到敌人,不只是数据形状对 ——
        /// 钉住 EffectsOf(def, attackMode: true) 接线,回归 attackEffects 被忽略的坑。</summary>
        [Test]
        public void Cast_AttackMode_DealsDamageToEnemy()
        {
            // 2026-09-05:样本字 沝 随字表调整移出,换成同档水系留存字 冰。
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "冰" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 0) }, seed: 1);
            int before = battle.Enemies[0].Hp;
            battle.Cast("冰", 0, attackMode: true);
            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "攻击面应打伤敌人");
        }

        /// <summary>护/治面用 Cast(默认 attackMode: false)真的能回血,与攻击面互斥 ——
        /// 钉住默认路径没有被攻击面悄悄顶替。</summary>
        [Test]
        public void Cast_SupportMode_HealsSelf()
        {
            // 2026-09-05:样本字 沝 随字表调整移出,换成同档水系留存字 冰。
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 1000, PlayerAttack = 100 },
                new[] { "冰" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 800) }, seed: 1);
            battle.EndTurn();   // 挨一记,腾出治疗空间
            int before = battle.PlayerHp;
            battle.Cast("冰", 0);   // 默认 attackMode: false = 治疗面
            Assert.That(battle.PlayerHp, Is.GreaterThan(before), "治疗面应回血");
        }

        // ---- Task 11:土系 13 字 ----

        [Test]
        public void SummonChars_StayOneDirectional()
        {
            // 召唤字不做双方向(2026-09-02 用户拍板)。反向钉住:有了 attackEffects 就是被
            // 顺手加回来了 —— 清单注释挡不住这个,断言才行。
            var graph = LoadRealGraph();
            foreach (var id in SummonOnlyChars)
            {
                var def = graph.Get(id);
                Assert.That(def.AttackEffects.Count, Is.EqualTo(0), $"{id} 是召唤字,不该有攻击面");
                bool summons = def.Effects.Any(e => e.Kind == EffectKind.Summon);
                Assert.That(summons, Is.True, $"{id} 的主效果应当是召唤");
                bool shields = def.Effects.Any(e => e.Kind == EffectKind.Shield);
                Assert.That(shields, Is.False, $"{id} 不该带护盾面");
            }
        }

        [Test]
        public void EveryEarthChar_HasBothDirections()
        {
            var graph = LoadRealGraph();
            foreach (var id in EarthChars)
            {
                var def = graph.Get(id);
                Assert.That(def.Effects.Count, Is.GreaterThan(0), $"{id} 缺加盾面");
                Assert.That(def.AttackEffects.Count, Is.GreaterThan(0), $"{id} 缺攻击面");
            }
        }

        /// <summary>土系每张双方向字的护面都得**真的在加盾**。
        ///
        /// 单体(Shield)与群体(ShieldAll)都算(2026-09-05):崩的护面改成了群体加盾 ——
        /// 它的攻面是全体伤害,两面本就该同一个作用范围。这条测试守的是「护面不是别的东西」,
        /// 不是「护面只能用某一个 Kind」。</summary>
        [Test]
        public void EveryEarthChar_HasShieldOnSupportSide()
        {
            var graph = LoadRealGraph();
            foreach (var id in EarthChars)
            {
                bool shields = graph.Get(id).Effects.Any(e =>
                    e.Kind == EffectKind.Shield || e.Kind == EffectKind.ShieldAll);
                Assert.That(shields, Is.True, $"{id} 的加盾面没有护盾效果");
            }
        }

        /// <summary>2026-09-07 字表重做 P2 重写:旧版钉的是「满值砍半」这条已作废的折算规则,
        /// 现在护盾/攻击都走 spec §1.4 的公式(锚点 × 系数 × (1 − 特性预算))—— 同
        /// WaterCharValues_MatchRarityAnchors 的理由,改成从公式推导,不抄当前配置的
        /// 数字。护盾与攻击面用的是**同一个** ratio(同一张字的特性预算只算一次),
        /// 这条测试顺带钉住了这一点:两个数不是各自独立拍的。</summary>
        [Test]
        public void EarthCharValues_MatchRarityAnchors()
        {
            var graph = LoadRealGraph();
            Assert.That(ShieldValueOf(graph, "圭"), Is.EqualTo(ExpectedShield(CardRarity.Gold, "反伤50", "对破甲")),
                "金档:护盾锚点300 × SHIELD_F × (1 − (反伤50+对破甲)×K金)");
            Assert.That(ShieldValueOf(graph, "㙓"), Is.EqualTo(ExpectedShield(CardRarity.Red, "终极技", "免一次清盾", "护甲")),
                "红档:护盾锚点450 × SHIELD_F × (1 − (终极技+免一次清盾+护甲)×K红)");
            Assert.That(ShieldValueOf(graph, "杜"), Is.EqualTo(ExpectedShield(CardRarity.Gold, "免疫1", "护甲")),
                "金档:护盾锚点300 × SHIELD_F × (1 − (免疫1+护甲)×K金)");
            Assert.That(graph.Get("圭").AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(ExpectedSingleAttack(CardRarity.Gold, "反伤50", "对破甲")),
                "攻击面与护盾面用同一个特性预算 ratio,只是套的是单攻锚点不是护盾锚点");
        }

        /// <summary>引爆每系两张载体(中档 + 红档):只挂红档五系四叠字的话,
        /// 大部分玩家一局都摸不到这个机制。</summary>
        [Test]
        public void HeftDetonators_AreOnGreenAndRed()
        {
            Assert.That(LoadRealGraph().Get("崩").AttackEffects.Any(e => e.Kind == EffectKind.SpendHeft),
                Is.True, "崩(绿)是前期就能拿到的引爆载体");
            Assert.That(LoadRealGraph().Get("㙓").AttackEffects.Any(e => e.Kind == EffectKind.SpendHeft),
                Is.True);
        }

        /// <summary>攻击面用 Cast(attackMode: true) 真的能打到敌人,不只是数据形状对 ——
        /// 与水系那条(冰)同一目的,覆盖土系的接线。</summary>
        [Test]
        public void Cast_AttackMode_DealsDamageToEnemy_Earth()
        {
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "圭" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 0) }, seed: 1);
            int before = battle.Enemies[0].Hp;
            battle.Cast("圭", 0, attackMode: true);
            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "攻击面应打伤敌人");
        }

        /// <summary>护盾面用 Cast(默认 attackMode: false)真的能加盾,与攻击面互斥 ——
        /// 钉住默认路径没有被攻击面悄悄顶替。</summary>
        [Test]
        public void Cast_SupportMode_GrantsShield_Earth()
        {
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "圭" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 0) }, seed: 1);
            battle.Cast("圭", -1);   // 默认 attackMode: false = 护盾面
            // 2026-09-07 字表重做 P2:圭 的护盾按 spec §1.4 公式重新标定,170 → 119
            // (见 EarthCharValues_MatchRarityAnchors 的 ExpectedShield 推导)。
            Assert.That(battle.PlayerShield, Is.EqualTo(119));
        }

        /// <summary>修档位倒挂:燚(红) 的 AOE 曾低于 焱(橙)。
        ///
        /// 2026-09-07 字表重做 P2:焱/燚/焚 现在带的灼烧层数各不相同(焱 3 层 + 灼烧增威、
        /// 燚 5 层 + 全体引爆、焚 4 层),灼烧层数越多、当面直接伤害的预算就被扣得越多
        /// (design §1.4:每层灼烧按三角数折成等价伤害、从直接伤害预算里倒扣)——于是三张字
        /// 的 DamageAll 数字本身**不再可比**:红档 燚 的当面数字反而比橙档 焱 低,这不是
        /// 倒挂,是它把强度大头压在灼烧总当量上而不是当面数字上。
        ///
        /// 继续钉「DamageAll 必须红 > 橙」会把这条测试变成谎言,得换成
        /// tools/design/rebalance_2026_09_05.py 的 total() 用的口径:总当量 = 直接伤害 +
        /// 灼烧层数按三角数(dot_equiv)折算的等价伤害。这才是这条测试原本想守住的
        /// 不变量本身。</summary>
        [Test]
        public void FireOrangeAndRed_HaveCorrectTierOrdering()
        {
            var graph = LoadRealGraph();
            var yanDef = graph.Get("焱");
            var yiDef = graph.Get("燚");
            var fenDef = graph.Get("焚");
            int yan = yanDef.Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            int yi = yiDef.Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            int fen = fenDef.Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            int yanBurn = yanDef.Effects.First(e => e.Kind == EffectKind.BurnAll).Value;
            int yiBurn = yiDef.Effects.First(e => e.Kind == EffectKind.BurnAll).Value;
            int fenBurn = fenDef.Effects.First(e => e.Kind == EffectKind.BurnAll).Value;
            Assert.That(yan, Is.EqualTo(126));
            Assert.That(fen, Is.EqualTo(108), "相生取消后的等值改写");
            Assert.That(yi, Is.EqualTo(86),
                "红档当面数字比橙档低——强度大头压在灼烧总当量上,不是当面数字,见类方法文档");
            Assert.That(yanBurn, Is.EqualTo(3));
            Assert.That(fenBurn, Is.EqualTo(4));
            Assert.That(yiBurn, Is.EqualTo(5));

            // 真正的档位不变量:总当量(直接伤害 + 灼烧层数按三角数折算的等价伤害)
            // 必须红档 > 橙档 —— dot_equiv(n) = n(n+1)/2 × 20,与 rebalance 脚本同公式。
            int DotEquiv(int n) => n * (n + 1) / 2 * 20;
            int yanTotal = yan + DotEquiv(yanBurn);
            int yiTotal = yi + DotEquiv(yiBurn);
            Assert.That(yiTotal, Is.GreaterThan(yanTotal), "红档总当量(含灼烧)必须强于橙档");
        }
    }
}
