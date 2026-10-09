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
        // ---------------- J5:致命(割喉「目标获得致命,持续 2 回合(Boss:首次受伤 ×2)」) ----------------

        private const int DoomHp = 1000;
        /// <summary>致命判定线以上一点(30.5%):任何一次掉血都会把它压到 30% 以下,而那一下本身打不死它。</summary>
        private const int NearLine = 305;

        private static void Doom(BattleEngine b, int i, int turns = 5) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Doom, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = turns, SourceId = "刲" });

        private static readonly CharDef Hitter = new("斩", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) });

        private static readonly CharDef Boom = new("煸", Element.Heart, effects: new[] { new EffectDef(EffectKind.Detonate, 0) });

        private static void SetBurn(BattleEngine b, int i, int stacks) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = 100 });

        private static void Bleed(BattleEngine b, int i) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Bleed, Polarity = StatusPolarity.Debuff, Magnitude = 10, TurnsLeft = 3 });

        private static void Mine(BattleEngine b, int i) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Mine, Polarity = StatusPolarity.Debuff, Magnitude = 10, TurnsLeft = -1, SourceId = "雷" });

        /// <summary>同一条掉血路径跑两遍:没有致命时那一下打不死、只压到 30% 以下;有致命时被斩杀。</summary>
        private static void AssertDoomExecutesVia(Func<BattleEngine> make, Action<BattleEngine> hurt, int victim = 0)
        {
            var control = make();
            control.Enemies[victim].Hp = NearLine;
            hurt(control);
            Assert.That(control.Enemies[victim].Alive, Is.True, "对照:这一下本身打不死");
            Assert.That(control.Enemies[victim].Hp * 100, Is.LessThan(DoomHp * 30), "对照:这一下把它压到了 30% 以下");

            var b = make();
            b.Enemies[victim].Hp = NearLine;
            Doom(b, victim);
            hurt(b);
            Assert.That(b.Enemies[victim].Alive, Is.False, "致命:掉血后低于 30% 直接斩杀");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyDied && e.TargetIndex == victim), Is.EqualTo(1));
        }

        [Test]
        public void Doom_DamageEnemy_Executes()
        {
            AssertDoomExecutesVia(() => Battle(new[] { Hitter }, 1, Mob(hp: DoomHp), Mob()), b => b.Cast("斩", 0));
        }

        [Test]
        public void Doom_Burn_Executes()
        {
            AssertDoomExecutesVia(() => Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp), Mob()), b =>
            {
                SetBurn(b, 0, 1);
                b.EndTurn();
            });
        }

        [Test]
        public void Doom_Bleed_Executes()
        {
            AssertDoomExecutesVia(() => Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp), Mob()), b =>
            {
                Bleed(b, 0);
                b.EndTurn();
            });
        }

        [Test]
        public void Doom_Detonate_Executes()
        {
            AssertDoomExecutesVia(() => Battle(new[] { Boom }, 1, Mob(hp: DoomHp), Mob()), b =>
            {
                SetBurn(b, 0, 1);
                b.Cast("煸", 0);
            });
        }

        [Test]
        public void Doom_Mine_Executes()
        {
            AssertDoomExecutesVia(() => Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp), Mob()), b =>
            {
                Mine(b, 0);
                b.EndTurn();
            });
        }

        [Test]
        public void Doom_BurnBurst_Executes()
        {
            var igniter = new CharDef("燃", Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.BurnSingle, 1),
                new EffectDef(EffectKind.BurnBurst, 0, pick: EffectPick.BurnedByThisCast, riderOf: StatusKind.Burn),
            });
            var killer = new CharDef("杀", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 1000) });
            // 0 号带焚城,被打死时对全体结算 1 层灼:1 号吃这一下
            AssertDoomExecutesVia(() =>
            {
                var b = Battle(new[] { igniter, killer }, 1, Mob(hp: 100), Mob(hp: DoomHp), Mob());
                b.Cast("燃", 0);
                return b;
            }, b => b.Cast("杀", 0), victim: 1);
        }

        [Test]
        public void Doom_Split_Executes()
        {
            // 叠字怪首次受击存活 → 分裂成两个半血:那一半也是掉血
            var b = Battle(new[] { Hitter }, 1, new EnemyDef("叠", Element.Heart, DoomHp, 0, EnemyAbility.Split), Mob());
            b.Enemies[0].Hp = 600;
            Doom(b, 0);
            b.Cast("斩", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemySplit), Is.True, "前提:分裂了");
            Assert.That(b.Enemies[0].Alive, Is.False, "分裂后只剩不到一半 → 低于 30% 斩杀");
        }

        [Test]
        public void Doom_AboveLine_NoExecute()
        {
            var b = Battle(new[] { Hitter }, 1, Mob(hp: DoomHp), Mob());
            b.Enemies[0].Hp = 900;
            Doom(b, 0);
            b.Cast("斩", 0);
            Assert.That(b.Enemies[0].Alive, Is.True);
        }

        private static CharDef Throat(int turns = 2, EffectPick pick = EffectPick.Primary,
            DamageCondition onlyIf = DamageCondition.None, params TraitDef[] traits) => new("刲", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Doom, turns, pick: pick, onlyIf: onlyIf) },
            traits: traits);

        [Test]
        public void DoomEffect_AppliesStatus_ForTurns()
        {
            var b = Battle(Throat(), Mob(hp: DoomHp), Mob());
            b.Cast("刲", 0);
            var doom = b.Enemies[0].Statuses.Find(StatusKind.Doom);
            Assert.That(doom, Is.Not.Null);
            Assert.That(doom.TurnsLeft, Is.EqualTo(2), "Value = 回合数,不吃卡等级");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Doom), Is.False);
        }

        [Test]
        public void DoomEffect_AlreadyBelowLine_ExecutesOnApply_TriggersOnExecute()
        {
            var b = Battle(Throat(traits: IronRule()), Mob(hp: DoomHp), Mob());
            b.Enemies[0].Hp = 250;
            b.Cast("刲", 0);
            Assert.That(b.Enemies[0].Alive, Is.False, "施加时已低于 30% 立即斩杀");
            Assert.That(Morale(b), Is.EqualTo(2), "致命的斩杀也是斩杀(source Execute):铁则入队");
        }

        [Test]
        public void DoomEffect_PickAll_OnlyIf()
        {
            var b = Battle(Throat(pick: EffectPick.All, onlyIf: DamageCondition.TargetHpAbove70), Mob(hp: DoomHp), Mob(hp: DoomHp));
            b.Enemies[1].Hp = 500;
            b.Cast("刲", -1);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Doom), Is.True);
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Doom), Is.False, "条件不满足的跳过");
        }

        [Test]
        public void Doom_Expires()
        {
            var b = Battle(new[] { Throat(turns: 1), Hitter }, 1, Mob(hp: DoomHp), Mob());
            b.Cast("刲", 0);
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Doom), Is.False, "回合数按敌人行动递减,到期移除");
            b.Enemies[0].Hp = NearLine;
            b.Cast("斩", 0);
            Assert.That(b.Enemies[0].Alive, Is.True);
        }

        [Test]
        public void Doom_LastEnemyByBleedInEnemyTurn_Wins()
        {
            var b = Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp));
            b.Enemies[0].Hp = NearLine;
            Doom(b, 0);
            Bleed(b, 0);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "斩掉最后一名敌人当场判胜");
        }

        [Test]
        public void Doom_Boss_FirstDamageEnemyDoubled_ThenRemoved()
        {
            var b = Battle(new[] { Throat(), Hitter }, 1, RebalanceFixture.Boss(hp: 10000), Mob());
            b.Cast("刲", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Doom), Is.True, "Boss 也挂上");
            b.Enemies[0].Hp = 2000;   // 20%:杂兵会被斩,Boss 不斩
            int before = b.Enemies[0].Hp;
            b.Cast("斩", 0);
            int first = before - b.Enemies[0].Hp;
            Assert.That(b.Enemies[0].Alive, Is.True, "Boss 不被致命斩杀");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Doom), Is.False, "×2 用掉即移除");
            before = b.Enemies[0].Hp;
            b.Cast("斩", 0);
            Assert.That(first, Is.EqualTo((before - b.Enemies[0].Hp) * 2), "首次受伤 ×2,第二下照常");
        }

        [Test]
        public void Doom_Boss_MultipliesWithMark()
        {
            var b = Battle(new[] { Hitter }, 1, RebalanceFixture.Boss(hp: 10000), Mob());
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Vulnerable, Polarity = StatusPolarity.Debuff, Magnitude = 50, TurnsLeft = 5, SourceId = "标" });
            int before = b.Enemies[0].Hp;
            b.Cast("斩", 0);
            int marked = before - b.Enemies[0].Hp;
            Doom(b, 0);
            before = b.Enemies[0].Hp;
            b.Cast("斩", 0);
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(marked * 2), "与标记相乘");
        }

        [Test]
        public void Doom_Boss_BleedDoesNotConsume()
        {
            var b = Battle(new[] { Hitter }, 1, RebalanceFixture.Boss(hp: 10000, attack: 1), Mob());
            Doom(b, 0);
            Bleed(b, 0);
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Doom), Is.True, "Boss 版只认 DamageEnemy");
        }

        [Test]
        public void Doom_MineHpLoss_PreviewsBossDouble()
        {
            var b = Battle(new[] { Hitter }, 1, RebalanceFixture.Boss(hp: 10000, attack: 1), Mob());
            Mine(b, 0);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(10));
            Doom(b, 0);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(20), "预扣与 DamageEnemy 同口径");
            b.EndTurn();
            Assert.That(10000 - b.Enemies[0].Hp, Is.EqualTo(20));
        }

        [Test]
        public void Doom_MineHpLoss_PredictsMobExecute()
        {
            // 杂兵带致命:雷(10)把 305 压到 295 < 300 → 实际被斩杀,预扣应是整截(= 当前生命)
            var b = Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp), Mob());
            b.Enemies[0].Hp = NearLine;
            Mine(b, 0);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(10), "无致命:只扣雷的伤害");
            Doom(b, 0);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(NearLine), "带致命:雷压到 30% 以下 → 预测斩杀,整截");
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "与实际结算一致");
        }

        [Test]
        public void Doom_MineHpLoss_StaysAboveLine_NoExecutePrediction()
        {
            var b = Battle(new[] { Hitter }, 1, Mob(attack: 1, hp: DoomHp), Mob());
            b.Enemies[0].Hp = 900;
            Mine(b, 0);
            Doom(b, 0);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(10), "压不到线下:照常只扣雷的伤害");
        }

        [Test]
        public void StatusEffect_Clone_KeepsDoom()
        {
            var s = new StatusEffect { Kind = StatusKind.Doom, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 2, SourceId = "刲" };
            var c = s.Clone();
            Assert.That((c.Kind, c.Magnitude, c.TurnsLeft, c.SourceId), Is.EqualTo((StatusKind.Doom, 1, 2, "刲")));
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

        [Test]
        public void ConfigLoader_ParsesDoom()
        {
            var g = Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""刲"",""element"":""Metal"",""effects"":[{""kind"":""Doom"",""value"":2,""pick"":""All""}],"
                + @"""attackEffects"":[{""kind"":""DamageSingle"",""value"":40}]}]}");
            var e = g.Get("刲").Effects[0];
            Assert.That((e.Kind, e.Value, e.Pick), Is.EqualTo((EffectKind.Doom, 2, EffectPick.All)));
        }

        [Test]
        public void ConfigLoader_RejectsDoomWithoutTurns()
        {
            Assert.Throws<Brushblade.Data.ConfigException>(() => Brushblade.Data.ConfigLoader.LoadGraph(
                @"{""chars"":[{""id"":""刲"",""element"":""Metal"",""effects"":[{""kind"":""Doom"",""value"":0,""pick"":""All""}],"
                + @"""attackEffects"":[{""kind"":""DamageSingle"",""value"":40}]}]}"));
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
