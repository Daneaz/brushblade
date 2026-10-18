using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>spec A §6.1 鑫·金汇(BlockGain)/ 㵘·泽润(ReviveShield)/ 森·森生(击杀时幼苗 fromBody),D7 / D8。
    /// 夹具字无配方,IsComponent 缺省为 true 会被当部件直出 —— 显式 isComponent: false。</summary>
    public class SpecABlockReviveKindsTests
    {
        private static BattleEngine Battle(CharDef[] defs, int level, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                defs.SelectMany(d => new[] { d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { RebalanceFixture.Mob() }, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static CharDef Gain(TraitDef[] traits, params EffectDef[] attack) => new("汇", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 2, counterPercent: 60) }, attackEffects: attack, traits: traits,
            isComponent: false);

        [Test]
        public void BlockGain_AddsToExistingCharges_KeepsCounter()
        {
            var b = Battle(new[] { Gain(null, new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.BlockGain, 1)) }, 1);
            b.Cast("汇", -1);
            b.Cast("汇", 0, attackMode: true);
            var block = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.Magnitude, Is.EqualTo(3));
            Assert.That(block.CounterDamage, Is.EqualTo(60));
        }

        [Test]
        public void BlockGain_WithoutBlock_CreatesOne_WithCardsOwnCounterPercent()
        {
            var b = Battle(new[] { Gain(null, new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.BlockGain, 1)) }, 1);
            b.Cast("汇", 0, attackMode: true);
            var block = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.Magnitude, Is.EqualTo(1));
            Assert.That(block.CounterDamage, Is.EqualTo(60), "攻面本体 100 × 本字五行面格挡 60%");
        }

        [Test]
        public void BlockGain_OnKillTrait_OnlyWhenTheCastKills()
        {
            var onKill = new[] { new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "金汇",
                new[] { new EffectDef(EffectKind.BlockGain, 1) }, TraitTrigger.OnKill) };
            var def = Gain(onKill, new EffectDef(EffectKind.DamageSingle, 100));
            var kill = Battle(new[] { def }, 6, RebalanceFixture.Mob(hp: 50), RebalanceFixture.Mob());
            kill.Cast("汇", 0, attackMode: true);
            Assert.That(kill.PlayerStatuses.Find(StatusKind.Block)?.Magnitude, Is.EqualTo(1));
            var miss = Battle(new[] { def }, 6);
            miss.Cast("汇", 0, attackMode: true);
            Assert.That(miss.PlayerStatuses.Has(StatusKind.Block), Is.False);
        }

        [Test]
        public void ReviveShield_OnlyRevivedThisCast_GetMaxHpPercentShield()
        {
            var sprout = new CharDef("灵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 2, summonChar: "灵") }, isComponent: false);
            var su = new CharDef("苏", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Revive, 0, reviveHpPercent: 60),
                new EffectDef(EffectKind.ReviveShield, 30),
            }, isComponent: false);
            var b = Battle(new[] { sprout, su }, 1);
            Assert.That(b.Cast("灵", -1), Is.EqualTo(BattleError.None));
            b.KillSummonForTest(0);
            Assert.That(b.Cast("苏", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(60));
            Assert.That(b.Summons[0].Shield, Is.EqualTo(30));
            Assert.That(b.Summons[1].Shield, Is.EqualTo(0), "没被复活的不给");
        }

        // ---- 复活清状态(spec 2026-10-18 木灵尸体标识 §2):阵亡前挂的状态不跟着回来 ----

        private static void LoadStatuses(StatusBag bag)
        {
            bag.Apply(new StatusEffect { Kind = StatusKind.Block, Polarity = StatusPolarity.Buff, Magnitude = 2, CounterDamage = 30, TurnsLeft = -1 });
            bag.Apply(new StatusEffect { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = 3, TurnsLeft = -1 });
            bag.Apply(new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 4, TurnsLeft = -1, SourceId = "炎" });
            bag.Apply(new StatusEffect { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = 9, TurnsLeft = -1,
                SourceId = BattleEngine.HardenSourceId });   // 磐固
        }

        private static (BattleEngine b, CharDef su) TwoSprouts(params EffectDef[] suEffects)
        {
            var sprout = new CharDef("灵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 2, summonChar: "灵") }, isComponent: false);
            var su = new CharDef("苏", Element.Heart, effects: suEffects, isComponent: false);
            var b = Battle(new[] { sprout, su }, 1);
            Assert.That(b.Cast("灵", -1), Is.EqualTo(BattleError.None));
            return (b, su);
        }

        [Test]
        public void Revive_ClearsAllStatuses_ShieldStillGranted_OthersUntouched()
        {
            var (b, _) = TwoSprouts(new EffectDef(EffectKind.Revive, 0, reviveHpPercent: 60), new EffectDef(EffectKind.ReviveShield, 30));
            LoadStatuses(b.Summons[0].Statuses);
            LoadStatuses(b.Summons[1].Statuses);
            b.KillSummonForTest(0);
            Assert.That(b.Summons[0].Statuses.All.Count, Is.EqualTo(4), "清在复活那一刻,阵亡时状态还在");
            Assert.That(b.Cast("苏", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].Alive, Is.True);
            Assert.That(b.Summons[0].Statuses.All.Count, Is.EqualTo(0), "格挡 / 战意 / 灼 / 磐固全清");
            Assert.That(b.Summons[0].Shield, Is.EqualTo(30), "泽润在清空之后施加,不被清掉");
            Assert.That(b.Summons[1].Statuses.All.Count, Is.EqualTo(4), "只清复活的那只");
        }

        [Test]
        public void Revive_PickedCorpse_ClearsStatuses()
        {
            var (b, _) = TwoSprouts(new EffectDef(EffectKind.Revive, 1));
            LoadStatuses(b.Summons[1].Statuses);
            b.KillSummonForTest(1);
            Assert.That(b.Cast("苏", -1, allySlot: 1), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[1].Alive, Is.True);
            Assert.That(b.Summons[1].Statuses.All.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnKill_SaplingFromBody_Spawns()  // 森·森生的前置核对:击杀反应里的 fromBody 幼苗能落地
        {
            var traits = new[] { new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "森生",
                new[] { new EffectDef(EffectKind.SummonSapling, 20, fromBody: true) }, TraitTrigger.OnKill) };
            var def = new CharDef("森", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 400, summonAttack: 100, summonChar: "森") },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }, traits: traits, isComponent: false);
            var b = Battle(new[] { def }, 6, RebalanceFixture.Mob(hp: 50), RebalanceFixture.Mob());
            b.Cast("森", 0, attackMode: true);
            var alive = b.Summons.Where(s => s != null && s.Alive).ToList();
            Assert.That(alive.Count, Is.EqualTo(1));
            Assert.That(alive[0].MaxHp, Is.LessThan(400), "幼苗按本体 20% 取值");
        }

        // ---- group review 遗留:BlockMod 卡面「原为 N%」取本面本体 Block 的反击 %,D10 只抬不降 ----

        [Test]
        public void FaceBlockCounterPercent_FirstBlocksOwnPercent()
        {
            var face = new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Block, 1, counterPercent: 70) };
            Assert.That(TraitRules.FaceBlockCounterPercent(face), Is.EqualTo(70));
        }

        [Test]
        public void FaceBlockCounterPercent_UnwrittenOrMissing_FallsBackToDefault()
        {
            Assert.That(TraitRules.FaceBlockCounterPercent(new[] { new EffectDef(EffectKind.Block, 1) }),
                Is.EqualTo(BattleConfig.BlockCounterPercent));
            Assert.That(TraitRules.FaceBlockCounterPercent(new[] { new EffectDef(EffectKind.HealSelf, 10) }),
                Is.EqualTo(BattleConfig.BlockCounterPercent));
            Assert.That(TraitRules.FaceBlockCounterPercent(null), Is.EqualTo(BattleConfig.BlockCounterPercent));
        }

        [Test]
        public void BlockModCounterPercent_OnlyRaises()
        {
            var mod50 = new EffectDef(EffectKind.BlockMod, 0, counterPercent: 50);
            Assert.That(TraitRules.BlockModCounterPercent(70, mod50), Is.EqualTo(70), "千锤 50 < 本字 70:不降");
            Assert.That(TraitRules.BlockModCounterPercent(30, mod50), Is.EqualTo(50));
            Assert.That(TraitRules.BlockModCounterPercent(30, new EffectDef(EffectKind.BlockMod, 0, blockBleed: 50)),
                Is.EqualTo(30), "不改反击的 BlockMod 原样");
        }
        // ---- C7(review I1 / I2):反击基准读按卡等级折叠后的五行面,不是 Lv1 本体 ----

        /// <summary>鑫式夹具:五行面本体 Block 反击 60,Lv3 强化替换为 70;Lv5 五行面 BlockMod(counter 由参数给);
        /// Lv6 攻面击杀时金汇。</summary>
        private static CharDef Folded(int modPercent) => new("汇", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 2, counterPercent: 60) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
            traits: new[]
            {
                new TraitDef(TraitSlot.Lv3, TraitFace.Feature, TraitForm.Active, TraitSlot.Lv1, "格挡·强化",
                    new[] { new EffectDef(EffectKind.Block, 2, counterPercent: 70) }),
                new TraitDef(TraitSlot.Lv5, TraitFace.Feature, TraitForm.Passive, null, "剑意",
                    new[] { new EffectDef(EffectKind.BlockMod, 0, counterPercent: modPercent) }),
                new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "金汇",
                    new[] { new EffectDef(EffectKind.BlockGain, 1) }, TraitTrigger.OnKill),
            },
            isComponent: false);

        [Test]
        public void BlockGain_AtLv6_NewBlockUsesFoldedFaceCounter()
        {
            var b = Battle(new[] { Folded(50) }, 6, RebalanceFixture.Mob(hp: 50), RebalanceFixture.Mob());
            b.Cast("汇", 0, attackMode: true);
            var block = b.PlayerStatuses.Find(StatusKind.Block);
            Assert.That(block.CounterDamage, Is.EqualTo(MetaRules.ScaleByCardLevel(100, 6) * 70 / 100),
                "Lv3 已把五行面格挡反击抬到 70,不是本体 60");
        }

        [Test]
        public void TraitFaceBlockCounterPercent_IsFoldedFaceAtUnlock_ExcludingThisTrait()
        {
            var def = Folded(50);
            var mod = def.Traits[1];
            Assert.That(TraitRules.FaceBlockCounterPercent(def, mod, 1), Is.EqualTo(70),
                "卡 Lv1 时也按特性解锁等级(Lv5)折叠:Lv3 的 70");
            Assert.That(TraitRules.FaceBlockCounterPercent(def, Folded(90).Traits[1], 8), Is.EqualTo(70),
                "不含本条 BlockMod 自己");
            Assert.That(TraitRules.BlockModCounterPercent(TraitRules.FaceBlockCounterPercent(def, mod, 5), mod.Effects[0]),
                Is.EqualTo(70), "剑意 50 不高于折叠后 70:卡面不印「改为」");
        }
    }
}
