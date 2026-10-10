using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 4:我方受击 / 回合挂点 —— 附录 W5 HurtHeal(栉风沐雨,Q12)、W6 ShieldFrost(冰晶,Q15)、
    /// W7 TurnPulse(大雨滂沱,Q17)。
    ///
    /// 夹具:Element.Heart、PlayerAttack = 100、PlayerMaxHp 5000、开局 2000 血(留出回复空间)、敌人 10 万血。
    /// 敌人同速,每轮各出手一次;攻击力用来控制「这一下打多少」。</summary>
    public class WaterTurnHookTests
    {
        private const int Hp = 100000;
        private const int StartHp = 2000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Guard = RebalanceFixture.Char("护", new EffectDef(EffectKind.Shield, 30));

        private static readonly CharDef Bathe = RebalanceFixture.Char("沐", new EffectDef(EffectKind.HurtHeal, 50, turns: 3));

        private static readonly CharDef Crystal = new("冰", Element.Water, effects: new[]
        {
            new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.ShieldFromHeal, 100),
            new EffectDef(EffectKind.ShieldFrost, 1),
        });

        private static CharDef Downpour(string id = "淋") => new(id, Element.Water, effects: new[]
        {
            new EffectDef(EffectKind.TurnPulse, 0, turns: 3, perHit: new[]
            {
                new EffectDef(EffectKind.Slow, 1, pick: EffectPick.All),
                new EffectDef(EffectKind.HealSelf, 45),
            }),
        });

        private static BattleEngine Battle(CharDef def, int? startHp, int level, params EnemyDef[] enemies)
        {
            var defs = new[] { def, Hit, Guard };
            return new BattleEngine(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, startingHp: startHp, cardLevels: new Dictionary<string, int> { [def.Id] = level });
        }

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies) => Battle(def, StartHp, 1, enemies);

        private static void Cast(BattleEngine b, string id, int target = -1) =>
            Assert.That(b.Cast(id, target), Is.EqualTo(BattleError.None), id);

        private static EnemyDef Mob(int attack = 0, EnemyAbility ability = EnemyAbility.None) =>
            new("怔", Element.Heart, Hp, attack, ability);

        private static EnemyDef Boss(int attack) => RebalanceFixture.Boss(attack: attack);

        private static EnemyDef Toppler(int attack) =>
            new("覆", Element.Heart, Hp, attack, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, Hp, attack, skill: BossSkill.Topple) });

        private static List<int> PlayerHeals(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Heal && e.SecondIndex == Targeting.PlayerTarget)
            .Select(e => e.Amount).ToList();

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<HookArgs> All = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => All.Add(args);
        }

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)StatusKind.HurtHeal, Is.EqualTo((int)StatusKind.DebuffWard + 1));
            Assert.That((int)StatusKind.ShieldFrost, Is.EqualTo((int)StatusKind.DebuffWard + 2));
            Assert.That((int)StatusKind.TurnPulse, Is.EqualTo((int)StatusKind.DebuffWard + 3));
            Assert.That((int)EffectKind.HurtHeal, Is.EqualTo((int)EffectKind.DebuffWard + 1));
            Assert.That((int)EffectKind.ShieldFrost, Is.EqualTo((int)EffectKind.DebuffWard + 2));
            Assert.That((int)EffectKind.TurnPulse, Is.EqualTo((int)EffectKind.DebuffWard + 3));
        }

        // ================= W5 HurtHeal(栉风沐雨) =================

        /// <summary>两只各打 40:每轮只有第一下回复 50% = 20;3 回合 = 之后 3 轮敌方段,第 4 轮不再回复。</summary>
        [Test]
        public void HurtHeal_FirstHitPerTurnOnly_ThreeTurns()
        {
            var b = Battle(Bathe, Mob(40), Mob(40));
            Cast(b, "沐");
            var s = b.PlayerStatuses.Find(StatusKind.HurtHeal);
            Assert.That((s.Magnitude, s.TurnsLeft, s.SourceId), Is.EqualTo((50, 3, "沐")));
            int hp = b.PlayerHp;
            for (int round = 1; round <= 3; round++)
            {
                b.EndTurn();
                Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 20 }), $"第 {round} 轮:每回合只回复一次");
                Assert.That(b.PlayerHp, Is.EqualTo(hp - 80 + 20), $"第 {round} 轮");
                hp = b.PlayerHp;
            }
            Assert.That(b.PlayerStatuses.Has(StatusKind.HurtHeal), Is.False, "3 回合后到期");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.Empty, "到期后不再回复");
        }

        /// <summary>回复量按减伤后、含护盾吸收的伤害算:护盾 30 挡下 40 里的 30,照样回 20。</summary>
        [Test]
        public void HurtHeal_CountsShieldAbsorbedPart()
        {
            var b = Battle(Bathe, Mob(40));
            Cast(b, "沐");
            Cast(b, "护");
            Assert.That(b.ShieldNormal, Is.EqualTo(30), "前提:护盾 30");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 20 }));
            Assert.That(b.PlayerHp, Is.EqualTo(StartHp - 10 + 20));
        }

        [Test]
        public void HurtHeal_NotAfterDefeat()
        {
            var b = Battle(Bathe, 30, 1, Mob(40));
            Cast(b, "沐");
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(PlayerHeals(b), Is.Empty, "判负之后不触发");
            Assert.That(b.PlayerHp, Is.EqualTo(0));
        }

        /// <summary>铁画反噬(allowReflect = false)不触发、也不占每回合那一次;之后敌人的挥击照常回复。</summary>
        [Test]
        public void HurtHeal_IronBarbRecoil_DoesNotTriggerNorConsume()
        {
            var b = Battle(Bathe, Mob(40, EnemyAbility.Barb));
            Cast(b, "沐");
            int hp = b.PlayerHp;
            Cast(b, "击", 0);
            Assert.That(b.PlayerHp, Is.LessThan(hp), "前提:反噬打到玩家");
            Assert.That(PlayerHeals(b), Is.Empty, "反噬不触发");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 20 }), "本回合那一次没被反噬占掉");
        }

        [Test]
        public void HurtHeal_BurnTickAndImmunity_DoNotTrigger()
        {
            var b = Battle(Bathe, Mob(40));
            Cast(b, "沐");
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = -1 });
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Immunity, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = -1 });
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ImmunityBlocked), Is.True, "前提:免疫挡下这一下");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BurnTick), Is.True, "前提:灼烧结算");
            Assert.That(PlayerHeals(b), Is.Empty, "免疫挡下 / 灼烧都不触发");
        }

        /// <summary>多来源取最强:30% 与 50% 两条同时在身上,只回 50%,且整体每回合 1 次。</summary>
        [Test]
        public void HurtHeal_MultipleSources_StrongestOnly_OncePerTurn()
        {
            var weak = RebalanceFixture.Char("风", new EffectDef(EffectKind.HurtHeal, 30, turns: 3));
            var defs = new[] { Bathe, weak };
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config, new[] { "沐", "沐", "风", "风" }, Array.Empty<string>(),
                new[] { Mob(40), Mob(40) }, seed: 1, startingHp: StartHp);
            Cast(b, "风");
            Cast(b, "沐");
            Assert.That(b.PlayerStatuses.All.Count(x => x.Kind == StatusKind.HurtHeal), Is.EqualTo(2), "前提:两条不同来源");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 20 }), "只回最强的 50%,不叠加、不各回一次");
        }

        /// <summary>回复在 R4 触发深度里结算(不吃泉放大、不攒泉)。</summary>
        [Test]
        public void HurtHeal_NoWellspringAmplifyOrGain_RaisedInTrigger()
        {
            var b = Battle(Bathe, Mob(40));
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Wellspring, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = -1 });
            Cast(b, "沐");
            int well = b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring);
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 20 }), "不吃泉放大");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Wellspring), Is.EqualTo(well), "不攒泉");
        }

        // ================= W6 ShieldFrost(冰晶) =================

        /// <summary>治疗并获得等量护盾 → 挂冰晶;敌人挥击把两桶打到 0 → 安全点冻结攻击者 1 回合,冰晶移除。</summary>
        [Test]
        public void ShieldFrost_Broken_FreezesAttackerAtSafePoint()
        {
            var b = Battle(Crystal, Mob(1000), Mob(1));
            var log = new HookLog();
            b.AddHookListener(log);
            Cast(b, "冰");
            Assert.That(b.ShieldNormal, Is.GreaterThan(0), "前提:治疗转盾");
            Assert.That(b.PlayerStatuses.Find(StatusKind.ShieldFrost)?.Magnitude, Is.EqualTo(1));
            b.EndTurn();
            Assert.That(b.ShieldNormal + b.ShieldPersist, Is.EqualTo(0));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Freeze)?.TurnsLeft, Is.EqualTo(1), "攻击者冻结 1 回合(拍末递减之后才施加)");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.Freeze), Is.False, "只冻攻击者");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False, "触发后移除");
            var applied = log.All.Single(a => a.Kind == HookKind.StatusApplied && a.Status == StatusKind.Freeze);
            Assert.That(applied.Depth, Is.EqualTo(1), "反应深度 = 挥击深度 + 1(攻击者这一拍收尾的安全点兑现)");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
            // 被冻的那一拍跳过:下一轮只有 1 号出手
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.PlayerHp, Is.EqualTo(hp - 1), "攻击者被冻,跳过一次行动");
        }

        /// <summary>攻击者在本拍被镜反弹打死:拍尾的冻结反应落空,不报错;冰晶照样随两桶归零移除。</summary>
        [Test]
        public void ShieldFrost_AttackerKilledByReflect_FreezeFizzles()
        {
            var b = Battle(Crystal, new EnemyDef("脆", Element.Heart, 50, 1000, EnemyAbility.None), Mob(0));
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff, Magnitude = 50, TurnsLeft = -1 });
            Cast(b, "冰");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.True);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:镜反弹打死攻击者");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "死者不挂冻结");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False);
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void ShieldFrost_BossAttacker_GetsIceStall()
        {
            var b = Battle(Crystal, Boss(1000));
            Cast(b, "冰");
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.True, "Boss → 冰滞(现有规则)");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False);
        }

        [Test]
        public void ShieldFrost_FullHp_NotArmed()
        {
            var b = Battle(Crystal, null, 1, Mob(1000));
            Cast(b, "冰");
            Assert.That(b.ShieldNormal, Is.EqualTo(0), "满血:治疗量 0 → 护盾 0");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False, "没加上盾就不挂");
        }

        /// <summary>挨打没打穿 → 不冻;回合初清盾把两桶清空 → 冰晶只移除,不触发冻结。</summary>
        [Test]
        public void ShieldFrost_TurnClear_OnlyRemoves()
        {
            var b = Battle(Crystal, Mob(1));
            Cast(b, "冰");
            b.EndTurn();
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.False, "回合初清盾 → 只移除");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "没打穿、清盾都不冻");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostResist), Is.False);
        }

        /// <summary>留存桶还有盾时回合初清普通桶不移除;两桶都空才移除。</summary>
        [Test]
        public void ShieldFrost_TurnClear_KeptWhilePersistRemains()
        {
            var bulwark = RebalanceFixture.Char("垒", new EffectDef(EffectKind.Shield, 40, persistOnce: true));
            var defs = new[] { Crystal, bulwark };
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config, new[] { "冰", "冰", "垒", "垒" }, Array.Empty<string>(),
                new[] { Mob(0) }, seed: 1, startingHp: StartHp);
            Cast(b, "冰");
            Cast(b, "垒");
            Assert.That(b.ShieldPersist, Is.GreaterThan(0));
            b.EndTurn();
            Assert.That(b.ShieldNormal, Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldFrost), Is.True, "留存桶还在 → 冰晶留着");
        }

        [Test]
        public void ShieldFrost_ToppleClear_OnlyRemoves()
        {
            var b = Battle(Crystal, Toppler(1));
            Cast(b, "冰");
            bool toppled = false;
            for (int i = 0; i < 6 && !toppled; i++)
            {
                if (!b.PlayerStatuses.Has(StatusKind.ShieldFrost)) { Cast(b, "冰"); }
                b.EndTurn();
                toppled = b.LastEvents.Any(e => e.Kind == BattleEventKind.ShieldBroken);
            }
            Assert.That(toppled, Is.True, "前提:倾覆掀盾");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.False, "倾覆清盾只移除,不冻");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
        }

        // ================= W7 TurnPulse(大雨滂沱) =================

        [Test]
        public void TurnPulse_FiresAtNextThreePlayerTurns_ThenRemoved()
        {
            var b = Battle(Downpour(), Mob(0), Mob(0));
            Cast(b, "淋");
            var s = b.PlayerStatuses.Find(StatusKind.TurnPulse);
            Assert.That((s.TurnsLeft, s.OnHit.Count), Is.EqualTo((3, 2)));
            Assert.That(b.Enemies.Any(e => e.Statuses.Has(StatusKind.SpeedModifier)), Is.False, "施加当回合不触发");
            Assert.That(b.PlayerHp, Is.EqualTo(StartHp));
            for (int round = 1; round <= 3; round++)
            {
                b.EndTurn();
                Assert.That(PlayerHeals(b), Is.EqualTo(new[] { 45 }), $"第 {round} 次:我方回复 45");
                Assert.That(b.Enemies.All(e => e.Statuses.Find(StatusKind.SpeedModifier)?.TurnsLeft == 1), Is.True,
                    $"第 {round} 次:全体敌人减速 1 回合");
            }
            Assert.That(b.PlayerHp, Is.EqualTo(StartHp + 135));
            Assert.That(b.PlayerStatuses.Has(StatusKind.TurnPulse), Is.False, "3 次后移除");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.Empty, "第 4 回合不再触发");
        }

        /// <summary>载荷按来源字等级在触发时缩放:回复量连续、减速回合离散。</summary>
        [Test]
        public void TurnPulse_PayloadScalesWithSourceLevelAtTrigger()
        {
            var b = Battle(Downpour(), StartHp, 6, Mob(0));
            Cast(b, "淋");
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse).OnHit[1].Value, Is.EqualTo(45), "存的是未缩放值");
            b.EndTurn();
            Assert.That(PlayerHeals(b), Is.EqualTo(new[] { MetaRules.ScaleByCardLevel(45, 6) }));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.SpeedModifier)?.TurnsLeft, Is.EqualTo(1));
        }

        [Test]
        public void TurnPulse_ResolvesAtTurnStartedSafePoint()
        {
            var b = Battle(Downpour(), Mob(0));
            var log = new HookLog();
            b.AddHookListener(log);
            Cast(b, "淋");
            log.All.Clear();
            b.EndTurn();
            var slow = log.All.Single(a => a.Kind == HookKind.StatusApplied && a.Status == StatusKind.SpeedModifier);
            Assert.That(slow.Depth, Is.EqualTo(1));
            int started = log.All.FindIndex(a => a.Kind == HookKind.TurnStarted);
            Assert.That(started, Is.LessThan(log.All.IndexOf(slow)), "入队在 TurnStarted 之前,兑现在紧随的安全点");
        }

        /// <summary>只断分类:三者列进 IsBattleScoped(同 Retaliate / DebuffWard,防御性)。没有走 RunEngine 换场 ——
        /// 玩家侧跨场只带护甲 / 厚 / 泉(RunEngine 战后白名单按 Kind + TurnsLeft &lt; 0 收),三者本就带不过去。</summary>
        [Test]
        public void HookStatuses_AreBattleScoped_ByKind()
        {
            Assert.That(StatusRules.IsBattleScoped(StatusKind.HurtHeal), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.ShieldFrost), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.TurnPulse), Is.True);
        }

        /// <summary>载荷进 StatusEffect.OnHit(OpeningEffect 形态,已可序列化),Clone 带上。</summary>
        [Test]
        public void TurnPulse_CloneKeepsPayload()
        {
            var b = Battle(Downpour(), Mob(0));
            Cast(b, "淋");
            var clone = b.PlayerStatuses.Find(StatusKind.TurnPulse).Clone();
            Assert.That(clone.OnHit.Select(o => (o.Kind, o.Value, o.Pick)),
                Is.EqualTo(new[] { (EffectKind.Slow, 1, EffectPick.All), (EffectKind.HealSelf, 45, EffectPick.Primary) }));
        }

        // ---------------- 分类 ----------------

        [Test]
        public void Classification_DiscreteAndNonHostile()
        {
            foreach (var k in new[] { EffectKind.HurtHeal, EffectKind.ShieldFrost, EffectKind.TurnPulse })
            {
                Assert.That(MetaRules.ScalesWithCardLevel(k), Is.False, $"{k} 离散");
                Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(k, 1, turns: 1)), Is.False, $"{k} 不选敌");
            }
        }

        // ---------------- ConfigLoader ----------------

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""淋"",""element"":""Water"",""effects"":[{""kind"":""HealSelf"",""value"":10}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10}],
              ""traits"":[{""slot"":""Lv8"",""face"":""Feature"",""form"":""Active"",""name"":""雨"",""effects"":[" + traitEffects + @"]}]}]}");

        [Test]
        public void Loader_ReadsHookKinds()
        {
            var pulse = Load(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Slow"",""value"":1,""pick"":""All""},{""kind"":""HealSelf"",""value"":45}]}")
                .Get("淋").Traits[0].Effects[0];
            Assert.That((pulse.Kind, pulse.Turns, pulse.PerHit.Count), Is.EqualTo((EffectKind.TurnPulse, 3, 2)));
            Assert.That(pulse.PerHit[0].Pick, Is.EqualTo(EffectPick.All));
            var hurt = Load(@"{""kind"":""HurtHeal"",""value"":50,""turns"":3}").Get("淋").Traits[0].Effects[0];
            Assert.That((hurt.Kind, hurt.Value, hurt.Turns), Is.EqualTo((EffectKind.HurtHeal, 50, 3)));
            var frost = Load(@"{""kind"":""ShieldFromHeal"",""value"":100},{""kind"":""ShieldFrost"",""value"":1}").Get("淋").Traits[0].Effects[1];
            Assert.That((frost.Kind, frost.Value), Is.EqualTo((EffectKind.ShieldFrost, 1)));
        }

        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""DamageSingle"",""value"":10,""pick"":""All""}]}")]   // 不收伤害
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""BurnSingle"",""value"":2,""pick"":""All""}]}")]    // 不在白名单
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Slow"",""value"":1}]}")]                           // 敌方效果须选全体
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Slow"",""value"":1,""pick"":""All"",""onlyIf"":""Burning""}]}")]   // 不能带条件门
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3}")]                                                                         // 缺载荷
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""perHit"":[{""kind"":""HealSelf"",""value"":45}]}")]                                  // 缺 turns
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Slow"",""value"":1,""pick"":""All"",""extend"":true}]}")]   // OpeningEffect 带不过去,会被静默丢弃:extend
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""Slow"",""value"":1,""pick"":""All"",""whileSlowed"":true}]}")]   // OpeningEffect 带不过去,会被静默丢弃:whileSlowed
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""HealSelf"",""value"":45,""fill"":true}]}")]   // OpeningEffect 带不过去,会被静默丢弃:fill
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""HealSelf"",""value"":45,""ofHeal"":true}]}")]   // OpeningEffect 带不过去,会被静默丢弃:ofHeal
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""HealSelf"",""value"":45,""scaleBy"":""BurnStack""}]}")]   // OpeningEffect 带不过去,会被静默丢弃:scaleBy
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""HealSelf"",""value"":45,""wardOf"":""Burn""}]}")]   // OpeningEffect 带不过去,会被静默丢弃:wardOf
        [TestCase(@"{""kind"":""TurnPulse"",""value"":0,""turns"":3,""perHit"":[{""kind"":""HealSelf"",""value"":45,""wardCount"":2}]}")]   // OpeningEffect 带不过去,会被静默丢弃:wardCount
        [TestCase(@"{""kind"":""HurtHeal"",""value"":50}")]                                                                                      // 缺 turns
        [TestCase(@"{""kind"":""HurtHeal"",""value"":0,""turns"":3}")]                                                                           // 百分比须 ≥ 1
        [TestCase(@"{""kind"":""ShieldFrost"",""value"":0}")]                                                                                    // 冻结回合须 ≥ 1
        public void Loader_RejectsBadHookKinds(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
