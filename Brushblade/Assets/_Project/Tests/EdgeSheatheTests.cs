using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>金系基础规则「锋刃回鞘」(spec 2026-10-19):战意满 5 层时打出金系字的攻击面(带伤害)→
    /// 本张伤害 +100%(断金再 +N%)、吸血 35%(按实际 HP 损失)、击杀回库 1 张、结算后清空战意。</summary>
    public class EdgeSheatheTests
    {
        private const int Full = BattleConfig.BaseMoraleCap;

        private static RecipeGraph Graph() => new(new[]
        {
            // 攻击面单体伤害;五行面也带伤害(验「五行面不触发」)
            new CharDef("刀", Element.Metal, isComponent: false,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("斩", Element.Metal, isComponent: false,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 5) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) }),
            new CharDef("砺", Element.Metal, isComponent: false,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 5) },
                attackEffects: new[] { new EffectDef(EffectKind.CritBuff, 5) }),
            new CharDef("火刀", Element.Fire, isComponent: false,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 5) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("金", Element.Metal, isComponent: true,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
        });

        private static EnemyDef Target(int hp = 900000) => new("靶", Element.Heart, hp, 0);

        private static BattleEngine Engine(int morale, string[] library, string[] pool = null,
            EnemyDef[] enemies = null, int release = 0, int? hp = null, int maxHp = 5000,
            Action<BattleConfig> tweak = null)
        {
            var config = new BattleConfig
            {
                DropTable = new[] { "金" }, PlayerMaxHp = maxHp, ApPerTurn = 20, MoraleReleasePercent = release,
            };
            tweak?.Invoke(config);
            return new BattleEngine(Graph(), config, library, pool ?? Array.Empty<string>(),
                enemies ?? new[] { Target() }, seed: 1, startingHp: hp,
                startingStatuses: morale > 0 ? new[] { new StatusEffect
                {
                    Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = morale, TurnsLeft = -1,
                } } : null);
        }

        private static int Morale(BattleEngine e) => e.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        /// <summary>出一张攻击面,返回 0 号敌人掉的血。</summary>
        private static int Hit(BattleEngine e, string card, bool attack = true)
        {
            int before = e.Enemies[0].Hp;
            Assert.That(e.Cast(card, 0, attackMode: attack), Is.EqualTo(BattleError.None));
            return before - e.Enemies[0].Hp;
        }

        private static bool Fired(BattleEngine e) => e.LastEvents.Any(x => x.Kind == BattleEventKind.EdgeSheathe);

        // ================= 触发 / 不触发 =================

        [Test]
        public void FullMorale_MetalAttackFace_DoublesDamage_AndClears()
        {
            // 对照:同战意的火字攻击面(同值、心靶无生克)—— 战意的攻击加成两边一样
            int plain = Hit(Engine(Full, new[] { "火刀" }), "火刀");
            var e = Engine(Full, new[] { "刀" });
            int sheathed = Hit(e, "刀");
            Assert.That(sheathed, Is.EqualTo(plain * 2), "锋刃回鞘 +100%");
            Assert.That(Morale(e), Is.EqualTo(0), "结算后清空战意");
            Assert.That(Fired(e), Is.True, "发锋刃回鞘名签事件");
        }

        [Test]
        public void BelowCap_DoesNotTrigger()
        {
            int plain = Hit(Engine(Full - 1, new[] { "火刀" }), "火刀");
            var e = Engine(Full - 1, new[] { "刀" });
            Assert.That(Hit(e, "刀"), Is.EqualTo(plain));
            Assert.That(Morale(e), Is.EqualTo(Full - 1));
            Assert.That(Fired(e), Is.False);
        }

        [Test]
        public void FeatureFace_WithDamage_DoesNotTrigger()
        {
            var e = Engine(Full, new[] { "刀" });
            Hit(e, "刀", attack: false);
            Assert.That(Morale(e), Is.EqualTo(Full), "五行面不触发、不消耗战意");
            Assert.That(Fired(e), Is.False);
        }

        [Test]
        public void PureBuffAttackFace_DoesNotTrigger()
        {
            var e = Engine(Full, new[] { "砺" });
            Assert.That(e.Cast("砺", attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(Morale(e), Is.EqualTo(Full));
            Assert.That(Fired(e), Is.False);
        }

        [Test]
        public void Component_DoesNotTrigger()
        {
            var e = Engine(Full, Array.Empty<string>(), pool: new[] { "金" });
            Hit(e, "金");
            Assert.That(Morale(e), Is.EqualTo(Full), "部件不算");
            Assert.That(Fired(e), Is.False);
        }

        [Test]
        public void NonMetal_DoesNotTrigger()
        {
            var e = Engine(Full, new[] { "火刀" });
            Hit(e, "火刀");
            Assert.That(Morale(e), Is.EqualTo(Full));
            Assert.That(Fired(e), Is.False);
        }

        [Test]
        public void DetachedEffects_DoNotTrigger()
        {
            // 开局效果 / 特性反应走 ApplyDetachedEffects(非顶层出字):不吃 +100%、不清战意
            var e = Engine(Full, new[] { "刀" });
            int before = e.Enemies[0].Hp;
            e.ApplyDetachedEffects("刀", Element.Metal, new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) });
            int plain = Hit(Engine(Full, new[] { "火刀" }), "火刀");
            Assert.That(before - e.Enemies[0].Hp, Is.EqualTo(plain));
            Assert.That(Morale(e), Is.EqualTo(Full));
        }

        // ================= 与断金叠加 =================

        [Test]
        public void WithMoraleRelease_StacksAdditively()
        {
            // 断金 +300%:锋刃回鞘 +(100 + 300)% = ×5
            int plain = Hit(Engine(Full, new[] { "火刀" }, release: 300), "火刀");
            var e = Engine(Full, new[] { "刀" }, release: 300);
            Assert.That(Hit(e, "刀"), Is.EqualTo(plain * 5));
            Assert.That(Morale(e), Is.EqualTo(0));
        }

        [Test]
        public void WithMoraleRelease_FeatureFace_NoLongerReleases()
        {
            // 口径统一(spec §2):断金也只认攻击面,旧口径「任何带伤害的金字面」作废
            int plain = Hit(Engine(Full, new[] { "火刀" }, release: 300), "火刀");
            var e = Engine(Full, new[] { "刀" }, release: 300);
            Assert.That(Hit(e, "刀", attack: false), Is.EqualTo(plain));
            Assert.That(Morale(e), Is.EqualTo(Full));
        }

        // ================= 吸血 =================

        [Test]
        public void Lifesteal_35PercentOfHpLoss()
        {
            var e = Engine(Full, new[] { "刀" }, hp: 1000);
            int dealt = Hit(e, "刀");
            Assert.That(dealt, Is.GreaterThan(0));
            Assert.That(e.PlayerHp, Is.EqualTo(1000 + dealt * BattleEngine.EdgeSheatheLifestealPercent / 100));
            var heal = e.LastEvents.Where(x => x.Kind == BattleEventKind.Heal && x.Source == EffectSource.EdgeSheathe).ToList();
            Assert.That(heal.Count, Is.EqualTo(1), "走治疗事件、带锋刃回鞘来源");
        }

        [Test]
        public void Lifesteal_ExcludesEnemyShieldAbsorption()
        {
            var e = Engine(Full, new[] { "刀" }, hp: 1000);
            e.Enemies[0].Shield = 50;
            int dealt = Hit(e, "刀");   // 只量 HP:盾吃掉的不在里面
            Assert.That(e.Enemies[0].Shield, Is.EqualTo(0), "前置:盾被打空");
            Assert.That(e.PlayerHp, Is.EqualTo(1000 + dealt * BattleEngine.EdgeSheatheLifestealPercent / 100));
        }

        [Test]
        public void Lifesteal_SumsAcrossTargets()
        {
            var e = Engine(Full, new[] { "斩" }, enemies: new[] { Target(), Target() }, hp: 1000);
            int b0 = e.Enemies[0].Hp, b1 = e.Enemies[1].Hp;
            Assert.That(e.Cast("斩", 0, attackMode: true), Is.EqualTo(BattleError.None));
            int dealt = b0 - e.Enemies[0].Hp + (b1 - e.Enemies[1].Hp);
            Assert.That(e.PlayerHp, Is.EqualTo(1000 + dealt * BattleEngine.EdgeSheatheLifestealPercent / 100));
        }

        [Test]
        public void Lifesteal_CappedAtMaxHp()
        {
            var e = Engine(Full, new[] { "刀" }, hp: 4999, maxHp: 5000);
            Hit(e, "刀");
            Assert.That(e.PlayerHp, Is.EqualTo(5000));
        }

        [Test]
        public void NoTrigger_NoLifesteal()
        {
            var e = Engine(Full - 1, new[] { "刀" }, hp: 1000);
            Hit(e, "刀");
            Assert.That(e.PlayerHp, Is.EqualTo(1000));
        }

        // ================= 击杀回库 =================

        [Test]
        public void Kill_ReturnsCardToLibrary()
        {
            var e = Engine(Full, new[] { "刀", "砺" }, enemies: new[] { Target(10), Target() });
            Assert.That(e.Cast("刀", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Enemies[0].Alive, Is.False, "前置:击杀");
            Assert.That(e.Library.Count(c => c == "刀"), Is.EqualTo(1), "刀回到字库");
            var granted = e.LastEvents.Where(x => x.Kind == BattleEventKind.CharGranted && x.Source == EffectSource.EdgeSheathe).ToList();
            Assert.That(granted.Count, Is.EqualTo(1));
            Assert.That(granted[0].TargetIndex, Is.EqualTo(0), "从阵亡那一格飞回");
            Assert.That(e.Library[granted[0].Amount], Is.EqualTo("刀"), "Amount = 入库下标");
        }

        [Test]
        public void NoKill_DoesNotReturn()
        {
            var e = Engine(Full, new[] { "刀" });
            Hit(e, "刀");
            Assert.That(e.Library.Contains("刀"), Is.False);
        }

        [Test]
        public void KillWithoutTrigger_DoesNotReturn()
        {
            var e = Engine(Full - 1, new[] { "刀" }, enemies: new[] { Target(10), Target() });
            Assert.That(e.Cast("刀", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Enemies[0].Alive, Is.False);
            Assert.That(e.Library.Contains("刀"), Is.False);
        }

        [Test]
        public void MultiKill_ReturnsOnlyOne()
        {
            var e = Engine(Full, new[] { "斩" }, enemies: new[] { Target(10), Target(10), Target() });
            Assert.That(e.Cast("斩", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Enemies[0].Alive || e.Enemies[1].Alive, Is.False, "前置:两杀");
            Assert.That(e.Library.Count(c => c == "斩"), Is.EqualTo(1), "每次触发至多回 1 张");
        }

        [Test]
        public void LibraryFull_DoesNotReturn()
        {
            // 斩获(必掉 1 张)先把出字腾出的空位填上 → 回鞘时字库已满,不回
            var e = Engine(Full, new[] { "刀", "砺", "砺", "砺", "砺", "砺" }, enemies: new[] { Target(10), Target() },
                tweak: c => { c.KillDraws = 1; c.KillDrawChancePercent = 100; c.UnlockedChars = new HashSet<string> { "砺" }; c.DropsPerTurn = 0; });
            Assert.That(e.Cast("刀", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Library.Count, Is.EqualTo(e.LibraryCapacity), "前置:斩获补满");
            Assert.That(e.Library.Contains("刀"), Is.False, "满库不回");
            Assert.That(e.LastEvents.Any(x => x.Kind == BattleEventKind.CharGranted && x.Source == EffectSource.EdgeSheathe),
                Is.False);
        }

        [Test]
        public void Return_DoesNotUseKillDrawQuota()
        {
            // 回鞘不占斩获的每回合额度:回鞘 1 次后,斩获本回合仍能掉满 KillDrawPerTurnCap 张
            var e = Engine(Full, new[] { "刀", "火刀", "火刀" }, enemies: new[] { Target(10), Target(10), Target(10), Target() },
                tweak: c => { c.KillDraws = 1; c.KillDrawChancePercent = 100; c.UnlockedChars = new HashSet<string> { "砺" }; c.DropsPerTurn = 0; });
            Assert.That(e.Cast("刀", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Library.Contains("刀"), Is.True, "回鞘");
            Assert.That(e.Cast("火刀", 1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Cast("火刀", 2, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(e.Library.Count(c => c == "砺"), Is.EqualTo(BattleEngine.KillDrawPerTurnCap));
        }

        // ================= 预估 =================

        [Test]
        public void Estimate_MatchesActual_AndDoesNotTouchEngine()
        {
            var e = Engine(Full, new[] { "刀" }, enemies: new[] { Target(10), Target() }, hp: 1000);
            var est = e.EstimateCast("刀", 0, attackMode: true);
            Assert.That(est.Error, Is.EqualTo(BattleError.None));
            Assert.That(Morale(e), Is.EqualTo(Full), "预估不清本体战意");
            Assert.That(e.PlayerHp, Is.EqualTo(1000), "预估不回本体血");
            Assert.That(e.Library.Count, Is.EqualTo(1), "预估不动本体字库");

            var big = Engine(Full, new[] { "刀" });
            var bigEst = big.EstimateCast("刀", 0, attackMode: true);
            int actual = Hit(big, "刀");
            Assert.That(bigEst.Enemies.Single(h => h.Index == 0).Damage, Is.EqualTo(actual), "预估含 +100%");
        }
    }
}
