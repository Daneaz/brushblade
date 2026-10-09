using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 3:斩杀族。J3 斩杀时(TraitTrigger.OnExecute,铁则)、J4 斩杀溅射(铡刀落)、J5 致命(割喉)。
    ///
    /// 夹具:字一律 Element.Heart;卡等级 4(铁则 Lv4 槽解锁);PlayerAttack 100。</summary>
    public class MetalExecuteTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 100000, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, int level, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()), Config,
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) => Battle(new[] { def }, 4, enemies);

        private static EnemyDef Mob(int attack = 0, int hp = Hp) => RebalanceFixture.Mob(hp: hp, attack: attack);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        // ---------------- J3:斩杀时(铁则「斩杀时战意 +2」) ----------------

        private static TraitDef IronRule() => new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "铁则",
            new[] { new EffectDef(EffectKind.Morale, 2) }, TraitTrigger.OnExecute);

        /// <summary>攻击面:伤害 10 + 斩杀 35%(直接击杀);五行面:格挡 + 立威 20%。</summary>
        private static CharDef Guillotine(bool ironRule = true, bool killsOutright = true) => new("铡", Element.Heart,
            effects: new[]
            {
                new EffectDef(EffectKind.Block, 9),
                new EffectDef(EffectKind.BlockMod, 0, counterExecuteBelow: 20),
            },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10, executeBelowPercent: 35, executeKills: killsOutright) },
            traits: ironRule ? new[] { IronRule() } : Array.Empty<TraitDef>());

        [Test]
        public void OnExecute_InCast_ExecuteKillTriggers()
        {
            var b = Battle(Guillotine(), Mob(hp: 1000), Mob());
            b.Enemies[0].Hp = 300;   // 30% < 35%
            b.Cast("铡", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:被斩杀");
            Assert.That(Morale(b), Is.EqualTo(2), "出字内斩杀 → 铁则入队,出字末尾兑现");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void OnExecute_PlainKill_DoesNotTrigger()
        {
            var plain = Battle(new CharDef("铡", Element.Heart,
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                traits: new[] { IronRule() }), Mob(hp: 1000), Mob());
            plain.Enemies[0].Hp = 1;
            plain.Cast("铡", 0, attackMode: true);
            Assert.That(plain.Enemies[0].Alive, Is.False, "前提:伤害击杀");
            Assert.That(Morale(plain), Is.EqualTo(0), "普通击杀不是斩杀");
        }

        [Test]
        public void OnExecute_BossDouble_IsNotExecute()
        {
            var b = Battle(Guillotine(killsOutright: true), RebalanceFixture.Boss(hp: 10000), Mob());
            b.Enemies[0].Hp = 1000;   // 10%:Boss 改吃 ×2
            b.Cast("铡", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.True);
            Assert.That(Morale(b), Is.EqualTo(0), "对 Boss 的 ×2 不算斩杀(Q6)");
        }

        [Test]
        public void OnExecute_EnemyTurnCounterExecute_Triggers_ViaSourceChar()
        {
            var b = Battle(Guillotine(), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 150;   // 15% < 20%
            b.Cast("铡", -1, attackMode: false);
            Assert.That(Morale(b), Is.EqualTo(0));
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:立威斩杀");
            Assert.That(Morale(b), Is.EqualTo(2), "敌人回合的立威斩杀按 ExecuteSourceCharId 回查铁则,ActOneEnemy 安全点兑现");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "已在安全点排空");
        }

        [Test]
        public void OnExecute_CastAndEnemyTurn_EachTriggersOnce()
        {
            var b = Battle(Guillotine(), Mob(hp: 1000), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 300;
            b.Enemies[1].Hp = 150;
            b.Cast("铡", 0, attackMode: true);
            Assert.That(Morale(b), Is.EqualTo(2), "出字内一次");
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[1].Alive, Is.False);
            Assert.That(Morale(b), Is.EqualTo(4), "敌人回合再一次");
        }

        [Test]
        public void OnExecute_CounterExecute_WithoutIronRule_NoReaction()
        {
            var b = Battle(Guillotine(ironRule: false), Mob(attack: 1, hp: 1000), Mob());
            b.Enemies[0].Hp = 150;
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(Morale(b), Is.EqualTo(0));
        }

        [Test]
        public void OnExecute_CounterExecute_LastEnemy_Wins()
        {
            var b = Battle(Guillotine(), Mob(attack: 1, hp: 1000));
            b.Enemies[0].Hp = 150;
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "斩掉最后一名敌人当场判胜");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "已分胜负:反应丢弃");
        }

        // ---------------- J4:斩杀溅射(铡刀落「斩杀后,相邻敌人受到被斩者最大生命 20% 的伤害」) ----------------

        private static CharDef Splasher(int splash = 20, params TraitDef[] traits) => new("铡", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 1) },
            attackEffects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 10, executeBelowPercent: 35, executeKills: true, executeSplashPercent: splash),
            },
            traits: traits);

        /// <summary>前排三只(第 3 只在后排同列):返回前排里左右都有邻居的那只的下标及其邻居。</summary>
        private static (int victim, int[] neighbors) MiddleOfFront(BattleEngine b)
        {
            var front = Enumerable.Range(0, b.Enemies.Count).Where(i => b.Enemies[i].Row == EnemyRow.Front).ToList();
            foreach (int i in front)
            {
                var n = front.Where(j => j != i && (b.Enemies[j].ColumnEnd == b.Enemies[i].Column
                    || b.Enemies[i].ColumnEnd == b.Enemies[j].Column)).ToArray();
                if (n.Length == 2) return (i, n);
            }
            Assert.Fail("夹具:前排没有左右都有邻居的那只");
            return (-1, null);
        }

        private static EnemyDef[] Line(int armor = 0) => new[]
        {
            new EnemyDef("怔0", Element.Heart, Hp, 0, defense: armor),
            new EnemyDef("怔1", Element.Heart, Hp, 0, defense: armor),
            new EnemyDef("怔2", Element.Heart, Hp, 0, defense: armor),
            new EnemyDef("怔3", Element.Heart, Hp, 0, row: EnemyRow.Back),
        };

        [Test]
        public void ExecuteSplash_HitsSameRowNeighbors_VictimMaxHpPercent()
        {
            var b = Battle(Splasher(), Line());
            var (victim, neighbors) = MiddleOfFront(b);
            b.Enemies[victim].Hp = b.Enemies[victim].MaxHp * 30 / 100;
            b.Cast("铡", victim, attackMode: true);
            Assert.That(b.Enemies[victim].Alive, Is.False, "前提:被斩杀");
            int splash = b.Enemies[victim].MaxHp * 20 / 100;
            foreach (int n in neighbors)
                Assert.That(b.Enemies[n].MaxHp - b.Enemies[n].Hp, Is.EqualTo(splash), $"同排左右 {n} 吃死者最大生命 20%");
            Assert.That(b.Enemies[3].Hp, Is.EqualTo(b.Enemies[3].MaxHp), "后排(上下)不吃");
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.ExecuteSplash).ToList();
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits.All(e => !e.Crit), Is.True, "不暴击");
        }

        [Test]
        public void ExecuteSplash_PassesArmor()
        {
            var b = Battle(Splasher(), Line(armor: 100));
            var (victim, neighbors) = MiddleOfFront(b);
            b.Enemies[victim].Hp = b.Enemies[victim].MaxHp * 30 / 100;
            b.Cast("铡", victim, attackMode: true);
            int splash = b.Enemies[victim].MaxHp * 20 / 100;
            Assert.That(b.Enemies[neighbors[0]].MaxHp - b.Enemies[neighbors[0]].Hp, Is.EqualTo(splash / 2), "护甲 100 → 减伤 50%");
        }

        [Test]
        public void ExecuteSplash_NoExecute_NoSplash()
        {
            var b = Battle(Splasher(), Line());
            var (victim, neighbors) = MiddleOfFront(b);
            b.Cast("铡", victim, attackMode: true);   // 满血:不斩杀
            foreach (int n in neighbors) Assert.That(b.Enemies[n].Hp, Is.EqualTo(b.Enemies[n].MaxHp));
        }

        [Test]
        public void ExecuteSplash_Boss_DoesNotTrigger()
        {
            var b = Battle(Splasher(), RebalanceFixture.Boss(hp: 10000), Mob(), Mob());
            b.Enemies[0].Hp = 1000;
            b.Cast("铡", 0, attackMode: true);
            Assert.That(b.LastEvents.Any(e => e.Source == EffectSource.ExecuteSplash), Is.False, "Boss 不被斩杀,不溅射(Q18)");
        }

        [Test]
        public void ExecuteSplash_KillsDoNotEnqueueOnKillOrOnExecute()
        {
            var onKill = new TraitDef(TraitSlot.Lv6, TraitFace.Both, TraitForm.Passive, null, "迎",
                new[] { new EffectDef(EffectKind.Morale, 1) }, TraitTrigger.OnKill);
            var b = Battle(new[] { Splasher(20, IronRule(), onKill) }, 6, Line());
            var (victim, neighbors) = MiddleOfFront(b);
            b.Enemies[victim].Hp = b.Enemies[victim].MaxHp * 30 / 100;
            foreach (int n in neighbors) b.Enemies[n].Hp = 1;
            b.Cast("铡", victim, attackMode: true);
            foreach (int n in neighbors) Assert.That(b.Enemies[n].Alive, Is.False, "前提:溅射打死邻居");
            Assert.That(Morale(b), Is.EqualTo(3), "只算被斩者:击杀 +1、斩杀 +2;溅射击杀不入队(R4)");
        }

        [Test]
        public void ExecuteSplash_CarriedByReshape()
        {
            var reshape = new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Active, null, "铡刀落",
                new[] { new EffectDef(EffectKind.Reshape, 0, executeBelowPercent: 35, executeKills: true, executeSplashPercent: 20) });
            var def = new CharDef("铡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                traits: new[] { reshape });
            var b = Battle(new[] { def }, 8, Line());
            var (victim, neighbors) = MiddleOfFront(b);
            b.Enemies[victim].Hp = b.Enemies[victim].MaxHp * 30 / 100;
            b.Cast("铡", victim, attackMode: true);
            Assert.That(b.Enemies[victim].Alive, Is.False);
            Assert.That(b.Enemies[neighbors[0]].MaxHp - b.Enemies[neighbors[0]].Hp, Is.EqualTo(b.Enemies[victim].MaxHp * 20 / 100));
        }
        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string attackEffects) =>
            Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""铡"",""element"":""Metal"",""effects"":[{""kind"":""Shield"",""value"":5}],""attackEffects"":["
                + attackEffects + "]}]}");

        [Test]
        public void ConfigLoader_ParsesExecuteSplash()
        {
            var g = Load(@"{""kind"":""DamageSingle"",""value"":40,""executeBelowPercent"":35,""executeKills"":true,""executeSplashPercent"":20}");
            Assert.That(g.Get("铡").AttackEffects[0].ExecuteSplashPercent, Is.EqualTo(20));
            var r = Load(@"{""kind"":""DamageSingle"",""value"":40},{""kind"":""Reshape"",""executeBelowPercent"":35,""executeKills"":true,""executeSplashPercent"":20}");
            Assert.That(TraitRules.CastEffects(r.Get("铡"), CardFace.Attack, 1)[0].ExecuteSplashPercent, Is.EqualTo(20), "Reshape 照抄(E7)");
        }

        [TestCase(@"{""kind"":""DamageSingle"",""value"":40,""executeSplashPercent"":20}")]                                       // 没有斩杀
        [TestCase(@"{""kind"":""DamageSingle"",""value"":40,""executeBelowPercent"":35,""executeSplashPercent"":20}")]             // 残血加伤不是斩杀
        [TestCase(@"{""kind"":""DamageSingle"",""value"":40,""executeBelowPercent"":35,""executeKills"":true,""executeSplashPercent"":101}")]
        [TestCase(@"{""kind"":""DamageSingle"",""value"":40,""executeBelowPercent"":35,""executeKills"":true,""executeSplashPercent"":-1}")]
        public void ConfigLoader_RejectsBadExecuteSplash(string effect)
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Load(effect));
        }
    }
}
