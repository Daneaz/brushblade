using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>出字内触发(暴击时 / 击杀时,附录 M23)与附着载体(烟熏挂灼、反震挂护盾,附录 M9 / D9),D1 Task 9。</summary>
    public class TraitTriggerRiderTests
    {
        private static BattleConfig Config => new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 };

        private static BattleEngine Battle(CharDef[] defs, int level, EnemyDef[] enemies, params string[] library) =>
            new BattleEngine(RebalanceFixture.Graph(defs), Config, library, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static TraitDef Passive(TraitFace face, TraitTrigger trigger, params EffectDef[] effects) =>
            new TraitDef(TraitSlot.Lv6, face, TraitForm.Passive, null, "被动", effects, trigger);

        // ---------------- 炽烈:暴击时目标 +灼 2 ----------------

        private static CharDef Blaze(bool forceCrit, int hits = 1) => new("炽", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: hits, forceCrit: forceCrit) },
            traits: new[] { Passive(TraitFace.Attack, TraitTrigger.OnCrit, new EffectDef(EffectKind.BurnSingle, 2)) });

        private static int Burn(BattleEngine b, int i = 0) => b.Enemies[i].Statuses.TotalMagnitude(StatusKind.Burn);

        [Test]
        public void OnCrit_Crit_AddsBurnToCritTarget()
        {
            var b = Battle(new[] { Blaze(true) }, 6, new[] { RebalanceFixture.Mob() }, "炽");
            b.Cast("炽", 0, attackMode: true);
            Assert.That(Burn(b), Is.EqualTo(2), "暴击 → 反应在出字末尾兑现:被暴击的目标 +灼 2");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "Cast 末尾已排空");
        }

        [Test]
        public void OnCrit_NoCrit_DoesNotTrigger()
        {
            var b = Battle(new[] { Blaze(false) }, 6, new[] { RebalanceFixture.Mob() }, "炽");
            b.Cast("炽", 0, attackMode: true);
            Assert.That(Burn(b), Is.EqualTo(0));
        }

        [Test]
        public void OnCrit_NotUnlocked_DoesNotTrigger()
        {
            var b = Battle(new[] { Blaze(true) }, 5, new[] { RebalanceFixture.Mob() }, "炽");
            b.Cast("炽", 0, attackMode: true);
            Assert.That(Burn(b), Is.EqualTo(0), "Lv6 被动在 5 级未解锁");
        }

        [Test]
        public void OnCrit_OtherFace_DoesNotTrigger()
        {
            var b = Battle(new[] { Blaze(true) }, 6, new[] { RebalanceFixture.Mob() }, "炽");
            b.Cast("炽", -1);   // 五行面:护盾,没有伤害
            Assert.That(Burn(b), Is.EqualTo(0));
        }

        [Test]
        public void OnCrit_MultiHit_TriggersPerCrit()
        {
            var b = Battle(new[] { Blaze(true, hits: 3) }, 6, new[] { RebalanceFixture.Mob() }, "炽");
            b.Cast("炽", 0, attackMode: true);
            Assert.That(Burn(b), Is.EqualTo(6), "3 击各暴击 → 各触发一次");
        }

        // ---------------- 迎刃:击杀时战意 +1 ----------------

        private static CharDef Edge(params EffectDef[] onKill) => new("刃", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 20, shape: TargetArea.All) },
            traits: new[] { Passive(TraitFace.Attack, TraitTrigger.OnKill, onKill) });

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        [Test]
        public void OnKill_TwoKills_TwoMorale()
        {
            var b = Battle(new[] { Edge(new EffectDef(EffectKind.Morale, 1)) }, 6,
                new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob() }, "刃");
            b.Cast("刃", -1, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(2), "全体伤害杀 2 名 → 各触发一次");
        }

        [Test]
        public void OnKill_NoKill_NoMorale()
        {
            var b = Battle(new[] { Edge(new EffectDef(EffectKind.Morale, 1)) }, 6,
                new[] { RebalanceFixture.Mob(), RebalanceFixture.Mob() }, "刃");
            b.Cast("刃", -1, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(0));
        }

        [Test]
        public void OnKill_KillByReaction_DoesNotTriggerAgain()
        {
            // 本字杀 1 名(20 血以下)→ 反应:全体 30 + 战意 1;反应打死第 2 名(40 血)不再入队(R4)
            var edge = new CharDef("刃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 20) },
                traits: new[]
                {
                    Passive(TraitFace.Attack, TraitTrigger.OnKill,
                        new EffectDef(EffectKind.DamageSingle, 30, shape: TargetArea.All),
                        new EffectDef(EffectKind.Morale, 1)),
                });
            var b = Battle(new[] { edge }, 1, new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 25), RebalanceFixture.Mob() }, "刃");
            // 1 级:Lv6 未解锁 —— 先确认基线
            b.Cast("刃", 0, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(0));

            var b6 = Battle(new[] { edge }, 6, new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 25), RebalanceFixture.Mob() }, "刃");
            b6.Cast("刃", 0, attackMode: true);
            Assert.That(b6.Enemies[1].Alive, Is.False, "反应的全体伤害打死了第 2 名");
            Assert.That(Morale(b6), Is.EqualTo(1), "反应造成的击杀不再触发击杀时(R4)");
        }

        // ---------------- 烟熏:带本字灼的敌人命中 −15%,灼消失时一并消失 ----------------

        private static CharDef Smoke(bool burns = true) => new("熏", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: burns
                ? new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BurnSingle, 1) }
                : new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: new[]
            {
                new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "烟障",
                    new[] { new EffectDef(EffectKind.Blind, 10, turns: 5) }),
                new TraitDef(TraitSlot.Lv6, TraitFace.Attack, TraitForm.Passive, null, "烟熏",
                    new[] { new EffectDef(EffectKind.Blind, 15, pick: EffectPick.HitTargets, riderOf: StatusKind.Burn) }),
            });

        private static readonly CharDef Boom = RebalanceFixture.Char("煸", new EffectDef(EffectKind.Detonate, 0));

        // 按烟熏(Lv6)的特性键找:D2-火 G11 起 Lv5 烟障的致盲也带自己的 TraitKey(独立来源),不能再用「TraitKey != null」认附着
        private static StatusEffect RiderBlind(BattleEngine b, int i = 0) =>
            b.Enemies[i].Statuses.All.FirstOrDefault(s => s.Kind == StatusKind.Blind
                && s.TraitKey == BattleEngine.TraitKey("熏", TraitSlot.Lv6, TraitFace.Attack));

        private static StatusEffect Rider(BattleEngine b, int i = 0) => b.Enemies[i].Statuses.Find(StatusKind.TraitRider);

        [Test]
        public void Smoke_BurnedTarget_GetsPersistentBlind15_AndHiddenRider()
        {
            var b = Battle(new[] { Smoke() }, 6, new[] { RebalanceFixture.Mob() }, "熏");
            b.Cast("熏", 0, attackMode: true);
            var blind = RiderBlind(b);
            Assert.That(blind, Is.Not.Null);
            Assert.That(blind.Magnitude, Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.Blind, 15, 6)),
                "致盲沿用既有口径:百分点吃卡等级(Blind 在连续表里)");
            Assert.That(blind.TurnsLeft, Is.EqualTo(-1), "随载体存续");
            Assert.That(blind.SourceId, Is.EqualTo("熏"));
            var rider = Rider(b);
            Assert.That(rider, Is.Not.Null);
            Assert.That(rider.Magnitude, Is.EqualTo((int)StatusKind.Burn), "载体 = 灼");
            Assert.That(rider.TraitKey, Is.EqualTo(blind.TraitKey));
            Assert.That(rider.SourceId, Is.EqualTo("熏"));
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Blind),
                Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.Blind, 15, 6) + MetaRules.ScaleEffectValue(EffectKind.Blind, 10, 6)),
                "与烟障的致盲并存,不互相覆盖");
        }

        [Test]
        public void Smoke_TargetWithoutThisCharsBurn_NoBlind()
        {
            var b = Battle(new[] { Smoke(burns: false) }, 6, new[] { RebalanceFixture.Mob() }, "熏");
            b.Cast("熏", 0, attackMode: true);
            Assert.That(RiderBlind(b), Is.Null);
            Assert.That(Rider(b), Is.Null);
        }

        [Test]
        public void Smoke_BurnSettlesToZero_BlindAndRiderGone()
        {
            var b = Battle(new[] { Smoke() }, 6, new[] { RebalanceFixture.Mob() }, "熏");
            b.Cast("熏", 0, attackMode: true);
            b.EndTurn();   // 1 层灼结算后清零
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False);
            Assert.That(RiderBlind(b), Is.Null, "灼烧完,烟熏的致盲一并消失");
            Assert.That(Rider(b), Is.Null);
            Assert.That(b.Enemies[0].Statuses.TotalMagnitude(StatusKind.Blind),
                Is.EqualTo(MetaRules.ScaleEffectValue(EffectKind.Blind, 10, 6)), "烟障的限时致盲不受影响");
        }

        [Test]
        public void Smoke_Detonated_BlindAndRiderGone()
        {
            var b = Battle(new[] { Smoke(), Boom }, 6, new[] { RebalanceFixture.Mob() }, "熏", "煸");
            b.Cast("熏", 0, attackMode: true);
            b.Cast("煸", 0);
            Assert.That(RiderBlind(b), Is.Null, "被引爆后一并消失");
            Assert.That(Rider(b), Is.Null);
        }

        [Test]
        public void Smoke_RiderKey_SurvivesSnapshotRoundTrip()
        {
            var defs = new[] { Smoke() };
            var b = Battle(defs, 6, new[] { RebalanceFixture.Mob() }, "熏");
            b.Cast("熏", 0, attackMode: true);
            string key = Rider(b).TraitKey;
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(defs), Config,
                new Dictionary<string, int> { ["熏"] = 6 },
                new Dictionary<string, EnemyDef> { ["怔"] = RebalanceFixture.Mob() });
            Assert.That(Rider(restored).TraitKey, Is.EqualTo(key));
            Assert.That(RiderBlind(restored).TraitKey, Is.EqualTo(key));
            restored.EndTurn();
            Assert.That(RiderBlind(restored), Is.Null, "读档后灼烧完仍能按键找到附带的致盲");
        }

        [Test]
        public void Smoke_RiderKey_SurvivesRealSaveFile()
        {
            var smoke = Smoke();
            var graph = RebalanceFixture.Graph(smoke);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "熏" } };
            var levels = new Dictionary<string, int> { ["熏"] = 6 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "熏", "熏" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            run.Battle.Cast("熏", 0, attackMode: true);
            string key = Rider(run.Battle).TraitKey;
            Assert.That(key, Is.Not.Null);

            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels,
                startingInk: 50, perFloorNormalShield: 2);
            Assert.That(Rider(restored.Battle).TraitKey, Is.EqualTo(key));
            Assert.That(RiderBlind(restored.Battle).TraitKey, Is.EqualTo(key));
        }

        // ---------------- 反震:本字护盾挨打时反弹吸收量 30%,每回合 1 次 ----------------

        private static CharDef Quake(int shield, bool block = false, bool persist = false) => new("震", Element.Heart,
            effects: block
                ? new[] { new EffectDef(EffectKind.Shield, shield, persistOnce: persist), new EffectDef(EffectKind.Block, 1) }
                : new[] { new EffectDef(EffectKind.Shield, shield, persistOnce: persist) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 50) },
            traits: new[] { Passive(TraitFace.Feature, TraitTrigger.Cast, new EffectDef(EffectKind.ShieldRecoil, 30)) });

        private static int EnemyLost(BattleEngine b) => b.Enemies.Sum(e => e.MaxHp - e.Hp);

        [Test]
        public void Recoil_AppliedOnlyWhenThisCastGrantedShield()
        {
            var b = Battle(new[] { Quake(100) }, 6, new[] { RebalanceFixture.Mob() }, "震", "震");
            b.Cast("震", 0, attackMode: true);   // 攻击面:不加盾
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False);
            b.Cast("震", -1);
            var s = b.PlayerStatuses.Find(StatusKind.ShieldRecoil);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.Magnitude, Is.EqualTo(30), "百分点不吃卡等级");
            Assert.That(s.TurnsLeft, Is.EqualTo(-1));
        }

        [Test]
        public void Recoil_Absorbs100_Reflects30_OncePerTurn_RecoversNextTurn()
        {
            // 护盾 100(1 级):两只怪各打 40,两下都被盾吃;第一下反弹 12,第二下本回合已用过
            var b = Battle(new[] { Quake(100) }, 1, new[] { RebalanceFixture.Mob(attack: 40), RebalanceFixture.Mob(attack: 40) }, "震");
            // 1 级 Lv6 未解锁 → 无反震
            b.Cast("震", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False);

            // 2026-10-04 U1:普通护盾在下一玩家回合开始清空,「下回合恢复」要靠留存护盾才有盾可吃(原用普通桶)
            var b6 = Battle(new[] { Quake(150, persist: true) }, 6, new[] { RebalanceFixture.Mob(attack: 40), RebalanceFixture.Mob(attack: 40) }, "震");
            b6.Cast("震", -1);
            Assert.That(b6.PlayerShield, Is.GreaterThanOrEqualTo(160), "盾够吃 4 下");
            b6.EndTurn();
            Assert.That(EnemyLost(b6), Is.EqualTo(40 * 30 / 100), "同回合第二下不触发");
            b6.EndTurn();
            Assert.That(EnemyLost(b6), Is.EqualTo(2 * (40 * 30 / 100)), "下回合恢复");
        }

        [Test]
        public void Recoil_100Absorbed_Bounces30()
        {
            var b = Battle(new[] { Quake(100) }, 6, new[] { RebalanceFixture.Mob(attack: 150) }, "震");
            b.Cast("震", -1);
            int shield = b.PlayerShield;
            Assert.That(shield, Is.GreaterThanOrEqualTo(100));
            b.EndTurn();
            Assert.That(EnemyLost(b), Is.EqualTo(shield * 30 / 100), "按吸收量 × 30% 反弹");
        }

        [Test]
        public void Recoil_RemovedWhenShieldDepleted()
        {
            var b = Battle(new[] { Quake(10) }, 6, new[] { RebalanceFixture.Mob(attack: 100) }, "震");
            b.Cast("震", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.True);
            b.EndTurn();
            Assert.That(b.PlayerShield, Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldRecoil), Is.False, "两桶护盾归零即移除");
        }

        [Test]
        public void Recoil_SharesReflectBudgetWithBlockCounter()
        {
            // 怪打 100 → 格挡 −40% = 60 落下,全被盾吃;预算 60×60% = 36。
            // 6 级格挡反击 = 攻击面 50×130%×30% = 19,反震只拿剩下的 17(而不是 60×30% = 18)
            var b = Battle(new[] { Quake(200, block: true) }, 1, new[] { RebalanceFixture.Mob(attack: 100) }, "震");
            b.Cast("震", -1);
            b.EndTurn();
            int counterOnly = EnemyLost(b);
            Assert.That(counterOnly, Is.EqualTo(15), "1 级无反震:只有格挡反击(50×30%)");

            var b6 = Battle(new[] { Quake(200, block: true) }, 6, new[] { RebalanceFixture.Mob(attack: 100) }, "震");
            b6.Cast("震", -1);
            int counter = b6.PlayerStatuses.Find(StatusKind.Block).CounterDamage;
            b6.EndTurn();
            int budget = 60 * CombatCaps.ReflectPercent / 100;
            Assert.That(EnemyLost(b6), Is.EqualTo(budget), "格挡反击 + 反震合计钳在 60% 预算");
            Assert.That(counter, Is.LessThan(budget), "反击先用,反震拿剩下的");
            // 终审补测:按 EffectSource 分别断言,钉住「镜 → 格挡 → 反震」的顺序 ——
            // 若反震先用,它会拿满 60×30% = 18,格挡只剩 18;现在是格挡 19、反震 17
            int SourceDamage(EffectSource src) => b6.LastEvents
                .Where(e => e.Kind == BattleEventKind.Damage && e.Source == src).Sum(e => e.Amount);
            Assert.That(SourceDamage(EffectSource.BlockCounter), Is.EqualTo(19), "格挡反击先拿满 19");
            Assert.That(SourceDamage(EffectSource.ShieldRecoil), Is.EqualTo(17), "反震拿剩下的 36 − 19 = 17");
        }
            // ---------------- 字表加载 ----------------

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""熏"",""element"":""Fire"",""effects"":[{""kind"":""Shield"",""value"":5}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":5},{""kind"":""BurnSingle"",""value"":1}],
              ""traits"":[{""slot"":""Lv6"",""face"":""Attack"",""form"":""Passive"",""name"":""烟熏"",""effects"":[" + traitEffects + "]}]}]}");

        [Test]
        public void Config_RiderOfBurn_OnBlind_Parses()
        {
            var e = Load(@"{""kind"":""Blind"",""value"":15,""pick"":""HitTargets"",""riderOf"":""Burn""}").Get("熏").Traits[0].Effects[0];
            Assert.That(e.RiderOf, Is.EqualTo(StatusKind.Burn));
            Assert.That(e.Pick, Is.EqualTo(EffectPick.HitTargets));
            var plain = Load(@"{""kind"":""Blind"",""value"":15,""turns"":2}").Get("熏").Traits[0].Effects[0];
            Assert.That(plain.RiderOf.HasValue, Is.False, "不写 riderOf = 不附着");
        }

        [Test]
        public void Config_RiderOf_OnlyBlindOnBurn()
        {
            // D2-火 Task 3 起附着白名单扩到灼附着族(Weaken / HealBlock / BurnGrow / BurnHold / BurnBurst / BurnBacklash),
            // 名单外的 Kind 照旧拦下
            Assert.Throws<ConfigException>(() => Load(@"{""kind"":""Seed"",""value"":15,""turns"":2,""riderOf"":""Burn""}"));
            Assert.Throws<ConfigException>(() => Load(@"{""kind"":""Blind"",""value"":15,""riderOf"":""Freeze""}"));
            Assert.Throws<ConfigException>(() => Load(@"{""kind"":""Blind"",""value"":15,""riderOf"":""Nope""}"));
        }

        [Test]
        public void Fold_NonAttachedPassiveEffects_StillIgnored_AttachedOnesTagged()
        {
            var def = Smoke();
            var effects = TraitRules.CastEffects(def, CardFace.Attack, 6);
            var rider = effects.Single(e => e.RiderOf == StatusKind.Burn);
            Assert.That(rider.TraitKey, Is.EqualTo(BattleEngine.TraitKey("熏", TraitSlot.Lv6, TraitFace.Attack)));
            Assert.That(TraitRules.CastEffects(Edge(new EffectDef(EffectKind.Morale, 1)), CardFace.Attack, 6)
                .Any(e => e.Kind == EffectKind.Morale), Is.False, "击杀时特性不在出字时执行");
        }
    }
}
