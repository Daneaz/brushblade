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
        private static readonly string[] WaterChars =
        {
            "溃", "冻", "海", "冷", "浴", "湮", "沏", "澡",
            "沝", "冰", "沐", "淼", "淡", "淋", "㵘",
        };

        /// <summary>做双方向的土系字。**不含召唤字**(碉/堡/塔)——2026-09-02 用户拍板:
        /// 召唤本身就是「把防御摆到场上」,再叠一个护盾面是同一件事收两次钱;而且召唤要选
        /// 落位槽,与「点敌人=攻 / 点我方=护」的目标语义打架。三张召唤字保持单方向。
        /// 由 <see cref="SummonChars_StayOneDirectional"/> 反向钉住,防止哪天又被顺手加回来。</summary>
        private static readonly string[] EarthChars =
        {
            "砸", "碾", "垒", "壁", "崩", "碎", "圭", "杜", "垚", "㙓",
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

        [Test]
        public void WaterCharValues_MatchRarityAnchors()
        {
            // 锚点表(spec §4.1):带附加特性的面 x0.7,纯效果取满值。
            var graph = LoadRealGraph();
            Assert.That(HealValueOf(graph, "沝"), Is.EqualTo(340), "金档满值");
            Assert.That(HealValueOf(graph, "㵘"), Is.EqualTo(540), "红档满值");
            Assert.That(HealValueOf(graph, "浴"), Is.EqualTo(77), "蓝档 110 x0.7(带净化)");
            Assert.That(HealValueOf(graph, "沏"), Is.EqualTo(180), "紫档 150 x1.2(相生取消后的补偿)");
        }

        /// <summary>攻击面必须真的能打人 —— 全是伤害类效果(单体/全体),不是挂个状态就算数。
        /// 钉的是「形状」:溃/冻/海/冷/浴/湮/沏/澡/沝/冰/沐/淼/淡/淋/㵘 的攻击面都带伤害,
        /// 与 ConfigLoaderTests.ShippedCharsJson_StackedWaterAndEarth_BothDefendAndStrike
        /// 守的是同一条不变量,只是这条覆盖全部 15 字而不只是 沝。</summary>
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
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "沝" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 0) }, seed: 1);
            int before = battle.Enemies[0].Hp;
            battle.Cast("沝", 0, attackMode: true);
            Assert.That(battle.Enemies[0].Hp, Is.LessThan(before), "攻击面应打伤敌人");
        }

        /// <summary>护/治面用 Cast(默认 attackMode: false)真的能回血,与攻击面互斥 ——
        /// 钉住默认路径没有被攻击面悄悄顶替。</summary>
        [Test]
        public void Cast_SupportMode_HealsSelf()
        {
            var graph = LoadRealGraph();
            var battle = new BattleEngine(graph,
                new BattleConfig { PlayerMaxHp = 1000, PlayerAttack = 100 },
                new[] { "沝" }, System.Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100000, 800) }, seed: 1);
            battle.EndTurn();   // 挨一记,腾出治疗空间
            int before = battle.PlayerHp;
            battle.Cast("沝", 0);   // 默认 attackMode: false = 治疗面
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

        [Test]
        public void EarthCharValues_MatchRarityAnchors()
        {
            var graph = LoadRealGraph();
            // 2026-09-04 用户拍板:土系护盾面盾量砍半(满值 340/540 → 170/270)。
            // 攻击面不动 —— 砍的是「一次加多少盾」,不是这一系的整体强度。
            Assert.That(ShieldValueOf(graph, "圭"), Is.EqualTo(170), "金档满值 340 砍半");
            Assert.That(ShieldValueOf(graph, "㙓"), Is.EqualTo(270), "红档满值 540 砍半");
            Assert.That(ShieldValueOf(graph, "杜"), Is.EqualTo(119), "金档 170 x0.7(带免疫)");
            Assert.That(graph.Get("圭").AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(340), "攻击面不在砍半范围内");
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
        /// 与水系那条(沝)同一目的,覆盖土系的接线。</summary>
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
            Assert.That(battle.PlayerShield, Is.EqualTo(170));
        }

        [Test]
        public void FireOrangeAndRed_HaveCorrectTierOrdering()
        {
            // 修档位倒挂:燚(红) 的 AOE 100 曾低于 焱(橙) 的 120。
            var graph = LoadRealGraph();
            int yan = graph.Get("焱").Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            int yi = graph.Get("燚").Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            int fen = graph.Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll).Value;
            Assert.That(yan, Is.EqualTo(120));
            Assert.That(fen, Is.EqualTo(120), "相生取消后的等值改写");
            Assert.That(yi, Is.EqualTo(180), "红档 AOE 锚点 250 x0.7(带灼烧)");
            Assert.That(yi, Is.GreaterThan(yan), "红档必须强于橙档");

            // 焚 与 焱 数值相同,靠灼烧层数区分(焚 需要 林+火 跨系配方,更难合)
            int fenBurn = graph.Get("焚").Effects.First(e => e.Kind == EffectKind.BurnAll).Value;
            int yanBurn = graph.Get("焱").Effects.First(e => e.Kind == EffectKind.BurnAll).Value;
            Assert.That(fenBurn, Is.EqualTo(4));
            Assert.That(yanBurn, Is.EqualTo(3));
        }
    }
}
