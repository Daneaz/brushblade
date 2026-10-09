using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 4:敌人出手前挂点(附录 N7:埋雷 Mine、焚身 BurnBacklashMark;G4)与
    /// 受击回敬(N8,跨计划 Q23 通用形态 Retaliate;火 = 烈焰护身)。另补 Task 3 遗留:焚身 / 埋雷致死不触发焚城(R4),
    /// 敌人自己回合被灼烧死时焚城照常结算并能判胜、回合收尾后反应队列为空。
    ///
    /// 夹具口径同 FireRiderTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、卡 Lv1(火力 100)、玩家 0 甲。</summary>
    public class FireHookTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int level = 1) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static EnemyDef Mob(int hp = Hp, int attack = 0, EnemyAbility ability = EnemyAbility.None) =>
            new("怔", Element.Heart, hp, attack, ability);

        /// <summary>Boss:两阶段各 100 血(总 200),甲阶段带淹没。BossChargeEvery = 2:普攻 → 蓄力 → 释放。</summary>
        private static EnemyDef SkillBoss() => new("试炼", Element.Heart, 100, 5,
            phases: new[]
            {
                new BossPhaseDef("甲", Element.Heart, 100, 5, skill: BossSkill.Deluge),
                new BossPhaseDef("乙", Element.Heart, 100, 5),
            });

        private static EffectDef Rider(EffectKind kind) =>
            new(kind, 0, pick: EffectPick.BurnedByThisCast, riderOf: StatusKind.Burn);

        private static CharDef Igniter(int burn, params EffectDef[] riders) =>
            new("燃", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnSingle, burn) }.Concat(riders).ToArray());

        private static readonly CharDef Boom = new("煸", Element.Heart, effects: new[] { new EffectDef(EffectKind.Detonate, 0) });

        private static CharDef Miner(int value, string id = "炸") =>
            new(id, Element.Heart, effects: new[] { new EffectDef(EffectKind.Mine, value) });

        /// <summary>烈焰护身的通用写法:受击回敬 [BurnSingle 2]。cap = 每回合上限(0 = 不限)。</summary>
        private static CharDef Guard(int cap = 0, string id = "烈", params EffectDef[] onHit) =>
            new(id, Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Retaliate, cap,
                    perHit: onHit.Length > 0 ? onHit : new[] { new EffectDef(EffectKind.BurnSingle, 2) }),
            });

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        private static void SetMine(BattleEngine b, int i, int magnitude, string source = "炸") =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Mine, Polarity = StatusPolarity.Debuff, Magnitude = magnitude, TurnsLeft = -1, SourceId = source });

        private static int PerStack()
        {
            var b = Battle(new[] { Boom }, new[] { Mob() });
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1, Potency = 100 });
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("煸", 0), Is.EqualTo(BattleError.None));
            return before - b.Enemies[0].Hp;
        }

        // ================= 埋雷:施加 =================

        [Test]
        public void Mine_AppliesSnapshotMagnitude_SameSourceKeepsStrongest()
        {
            var b = Battle(new[] { Miner(50), Miner(80, "爆") }, new[] { Mob() });
            Assert.That(b.Cast("炸", 0), Is.EqualTo(BattleError.None));
            var mine = b.Enemies[0].Statuses.Find(StatusKind.Mine);
            Assert.That(mine, Is.Not.Null, "埋雷挂在目标身上");
            Assert.That((mine.Magnitude, mine.TurnsLeft, mine.SourceId), Is.EqualTo((50, -1, "炸")), "攻击力 100 下 = Value,持续到触发");
            mine.Magnitude = 70;   // 同源再埋:取大,不叠
            b.Cast("炸", 0);
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.Mine), Is.EqualTo(1));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Mine).Magnitude, Is.EqualTo(70), "同源取大");
            b.Cast("爆", 0);
            Assert.That(b.Enemies[0].Statuses.All.Count(s => s.Kind == StatusKind.Mine), Is.EqualTo(2), "不同来源并存");
        }

        // ================= 埋雷:G4 四种情形 =================

        [Test]
        public void Mine_ExplodesBeforeNormalAttack_ThenAttackStillLands()
        {
            var b = Battle(new[] { Miner(50) }, new[] { Mob(hp: 1000, attack: 10) });
            b.Cast("炸", 0);
            int player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(950), "出手前爆炸,受 Magnitude 点伤害");
            Assert.That(b.PlayerHp, Is.EqualTo(player - 10), "没炸死就照常出手");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Mine), Is.False, "地雷一次性");
            var ev = b.LastEvents.ToList();
            int blast = ev.FindIndex(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Mine);
            int attack = ev.FindIndex(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(blast, Is.GreaterThanOrEqualTo(0), "爆炸发 Damage 事件,来源 Mine");
            Assert.That(blast, Is.LessThan(attack), "先爆炸、后出手");
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(950), "第二次攻击不再爆炸");
        }

        [Test]
        public void Mine_FrozenBeatDoesNotTrigger()
        {
            var b = Battle(new[] { Miner(50) }, new[] { Mob(hp: 1000, attack: 10) });
            b.Cast("炸", 0);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(1000), "冻结跳过的那一拍不算攻击");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Mine), Is.True);
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(950), "解冻后第一次攻击才爆炸");
        }

        [Test]
        public void Mine_CharmedStrikeDoesNotTrigger()
        {
            var b = Battle(new[] { Miner(50) }, new[] { Mob(hp: 1000, attack: 10), Mob(hp: 1000) });
            b.Cast("炸", 0);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Charm, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            b.EndTurn();
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(990), "被魅惑的那一击打队友");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(1000), "被魅惑那一击不算攻击(它不打我方)");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Mine), Is.True);
        }

        [Test]
        public void Mine_BossChargeTurnDoesNotTrigger_SkillReleaseDoes()
        {
            var b = Battle(new[] { Miner(30) }, new[] { SkillBoss() });
            b.EndTurn();   // 普攻
            SetMine(b, 0, 30);
            b.EndTurn();   // 蓄力:不出手
            Assert.That(b.Enemies[0].IsCharging, Is.True);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Mine), Is.True, "蓄力回合不出手,地雷不炸");
            int bossHp = b.Enemies[0].Hp, player = b.PlayerHp;
            b.EndTurn();   // 释放淹没
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(bossHp - 30), "Boss 技能也算攻击");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "没炸死,技能照放");
            Assert.That(b.PlayerHp, Is.EqualTo(player - 10));
            var ev = b.LastEvents.ToList();
            Assert.That(ev.FindIndex(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Mine),
                Is.LessThan(ev.FindIndex(e => e.Kind == BattleEventKind.BossSkillCast)), "技能之前爆炸");
        }

        [Test]
        public void Mine_KillsBossBeforeRelease_SkillCancelled_AndWins()
        {
            var b = Battle(new[] { Miner(30) }, new[] { SkillBoss() });
            b.EndTurn();
            b.EndTurn();
            SetMine(b, 0, 1000);
            int player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.False, "被炸死:技能取消");
            Assert.That(b.PlayerHp, Is.EqualTo(player));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "炸死最后一名敌人当场判胜");
        }

        [Test]
        public void Mine_KillsAttacker_AttackCancelled_OthersStillAct()
        {
            var b = Battle(new[] { Miner(50) }, new[] { Mob(hp: 30, attack: 10), Mob(hp: 1000, attack: 7) });
            b.Cast("炸", 0);
            int player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "被自己的地雷炸死");
            Assert.That(b.PlayerHp, Is.EqualTo(player - 7), "炸死的那一下取消,另一只照常出手");
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        // ================= 焚身(Task 3 载体,出手前结算) =================

        [Test]
        public void Backlash_SettlesBurnBeforeAttack_TwicePerRound()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBacklash)) },
                new[] { Mob(attack: 1), Mob(attack: 1), Mob(attack: 1) });
            b.Cast("燃", 0);
            b.Cast("燃", 1);
            b.Cast("燃", 2);
            int hp0 = b.Enemies[0].Hp;
            b.EndTurn();
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2) }, Is.EqualTo(new[] { 3, 3, 4 }),
                "前两只:自身结算 + 焚身结算各减一层;每回合 2 次,第三只只有自身结算");
            Assert.That(hp0 - b.Enemies[0].Hp, Is.EqualTo(5 * per + 4 * per), "焚身按正常灼烧公式结算");
            b.EndTurn();
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2) }, Is.EqualTo(new[] { 1, 1, 3 }),
                "玩家回合开始重置次数(第三只这一轮仍排在 2 次之后)");
        }

        [Test]
        public void Backlash_FrozenOrCharmedBeatDoesNotTrigger()
        {
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBacklash)) }, new[] { Mob(attack: 1), Mob() });
            b.Cast("燃", 0);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(4), "冻结:只有自身结算");
            b.Enemies[0].Statuses.Remove(StatusKind.FrostResist);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Charm, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(3), "魅惑那一击:只有自身结算");
        }

        [Test]
        public void Backlash_KillsAttacker_CancelsAttack_AndDoesNotTriggerBurnBurst_R4()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBacklash), Rider(EffectKind.BurnBurst)) },
                new[] { Mob(attack: 10), Mob() });
            b.Cast("燃", 0);
            b.Enemies[0].Hp = 5 * per + 1;   // 自身结算剩 1 血,焚身结算烧死
            int hp1 = b.Enemies[1].Hp, player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "被焚身烧死");
            Assert.That(b.PlayerHp, Is.EqualTo(player), "烧死则取消这次出手");
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(hp1), "R4:焚身致死不触发焚城");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void Mine_KillDoesNotTriggerBurnBurst_R4()
        {
            var b = Battle(new[] { Igniter(3, Rider(EffectKind.BurnBurst)) }, new[] { Mob(attack: 10), Mob() });
            b.Cast("燃", 0);
            SetMine(b, 0, 2 * Hp);
            int hp1 = b.Enemies[1].Hp, player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "被地雷炸死(带 2 层灼与焚城标记)");
            Assert.That(b.PlayerHp, Is.EqualTo(player));
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(hp1), "R4:地雷致死不触发焚城");
        }

        [Test]
        public void MineThenBacklash_Order_MineFirst()
        {
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBacklash)) }, new[] { Mob(attack: 10) });
            b.Cast("燃", 0);
            SetMine(b, 0, 2 * Hp);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.BurnTick), Is.EqualTo(1),
                "先结算地雷:炸死后焚身不再结算(只剩回合开始那一次灼烧)");
        }

        // ================= Task 3 遗留:敌人自己回合被灼烧死 → 焚城照常 =================

        [Test]
        public void BurnBurst_OnEnemyOwnTurnBurnDeath_ResolvesInBeat_QueueEmptyAtSave()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBurst)) }, new[] { Mob(attack: 10), Mob() });
            b.Cast("燃", 0);
            b.Enemies[0].Hp = 5 * per;   // 自身回合开始的灼烧结算烧死(深度 0)
            int hp1 = b.Enemies[1].Hp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(hp1 - b.Enemies[1].Hp, Is.EqualTo(4 * per), "焚城按死者剩余 4 层结算");
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "回合收尾排空,存档时队列为空");
            Assert.DoesNotThrow(() => b.Capture());
        }

        [Test]
        public void BurnBurst_OnEnemyOwnTurnBurnDeath_KillsRest_Wins()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(5, Rider(EffectKind.BurnBurst)) }, new[] { Mob(attack: 10), Mob(hp: 4 * per, attack: 10) });
            b.Cast("燃", 0);
            b.Enemies[0].Hp = 5 * per;
            int player = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies.Any(e => e.Alive), Is.False, "焚城烧死剩下的那只");
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "敌人回合收尾路径也判胜");
            Assert.That(b.PlayerHp, Is.EqualTo(player), "1 号已死,不再出手");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        // ================= 受击回敬(通用,烈焰护身 = [BurnSingle 2]) =================

        [Test]
        public void Retaliate_PlayerHit_BurnsAttacker_ExpiresAtPlayerTurn()
        {
            var b = Battle(new[] { Guard() }, new[] { Mob(attack: 10) });
            Assert.That(b.Cast("烈", -1), Is.EqualTo(BattleError.None));
            var s = b.PlayerStatuses.Find(StatusKind.Retaliate);
            Assert.That(s, Is.Not.Null);
            Assert.That((s.TurnsLeft, s.SourceId, s.OnHit.Count), Is.EqualTo((1, "烈", 1)));
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(2), "受击后攻击者 +灼 2");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.Burn && e.TargetIndex == 0), Is.True);
            Assert.That(b.PlayerStatuses.Has(StatusKind.Retaliate), Is.False, "本回合有效:玩家回合开始到期");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(1), "到期后不再回敬(只剩自身结算减一层)");
        }

        [Test]
        public void Retaliate_SummonHit_BurnsAttacker()
        {
            var sprout = new CharDef("林", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 500, summonCount: 1, summonAttack: 0, summonChar: "木") });
            var b = Battle(new[] { Guard(), sprout }, new[] { Mob(attack: 10) });
            b.Cast("林", -1);
            b.Cast("烈", -1);
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "前提:这一下打在木灵身上");
            Assert.That(Burn(b, 0), Is.EqualTo(2), "木灵被命中也回敬");
        }

        [Test]
        public void Retaliate_BossDevourOnSummon_Triggers()
        {
            // Ruling 7:吞噬绕开 DamageSummon,但对被吞的木灵来说仍是「被敌人命中」,一样回敬
            var sprout = new CharDef("林", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 500, summonCount: 1, summonAttack: 0, summonChar: "木") });
            var devourer = new EnemyDef("噬", Element.Heart, 100000, 5,
                phases: new[] { new BossPhaseDef("甲", Element.Heart, 100000, 5, skill: BossSkill.Devour) });
            var b = Battle(new[] { Guard(), sprout }, new[] { devourer });
            b.EndTurn();   // 普攻(场上无召唤物)
            b.EndTurn();   // 蓄力
            b.Cast("林", -1);
            b.Cast("烈", -1);
            int slot = Array.FindIndex(b.Summons.ToArray(), x => x != null && x.Alive);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "前提:木灵在场");
            b.EndTurn();   // 释放吞噬
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "前提:这一拍放的是吞噬");
            Assert.That(b.Summons[slot].Alive, Is.False, "前提:木灵被吞");
            Assert.That(Burn(b, 0), Is.EqualTo(2), "吞噬也算受击:攻击者 +灼 2");
        }

        [Test]
        public void Retaliate_AttackerDiesBeforeResolve_NoOp_PerTurnCountStillSpent()
        {
            // 回敬在攻击者这次动作结束后兑现;镜的反弹先把它打死 → 反应落空(不挂灼、不报错),
            // 但每回合计数在入队时就扣了(cap 1:第二个敌人不再回敬)
            var b = Battle(new[] { Guard(cap: 1) }, new[] { Mob(hp: 3, attack: 10), Mob(attack: 10) });
            b.Cast("烈", -1);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff, Magnitude = 60, TurnsLeft = 5, SourceId = "镜" });
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:反弹打死了攻击者");
            Assert.That(Burn(b, 0), Is.EqualTo(0), "死者不挂灼");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
            Assert.That(Burn(b, 1), Is.EqualTo(0), "计数已被落空的那次用掉");
        }

        [Test]
        public void Retaliate_SameSourceRecast_Overwrites_NotStacks()
        {
            var b = Battle(new[] { Guard() }, new[] { Mob(attack: 10) });
            b.Cast("烈", -1);
            b.Cast("烈", -1);
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.Retaliate), Is.EqualTo(1), "同源只留一条");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(2), "一次命中只回敬一次");
        }

        [Test]
        public void Retaliate_MissDoesNotTrigger_ImmunityBlockDoes()
        {
            var b = Battle(new[] { Guard() }, new[] { Mob(attack: 10), Mob(attack: 10) });
            b.Cast("烈", -1);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 100, TurnsLeft = 5 });
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Immunity, Polarity = StatusPolarity.Buff, Magnitude = 1, TurnsLeft = -1, SourceId = "杜" });
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(0), "打空:攻击没落到身上,不回敬");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ImmunityBlocked), Is.True);
            Assert.That(Burn(b, 1), Is.EqualTo(2), "免疫挡下也算命中");
        }

        [Test]
        public void Retaliate_PerTurnCap()
        {
            var b = Battle(new[] { Guard(cap: 1) }, new[] { Mob(attack: 10), Mob(attack: 10) });
            b.Cast("烈", -1);
            b.EndTurn();
            Assert.That(new[] { Burn(b, 0), Burn(b, 1) }, Is.EqualTo(new[] { 2, 0 }), "每回合 1 次");
        }

        [Test]
        public void Retaliate_BossSkill_EachHitTriggers()
        {
            var b = Battle(new[] { Guard(onHit: new EffectDef(EffectKind.Bleed, 3)) }, new[] { SkillBoss() });
            b.EndTurn();
            b.EndTurn();
            b.Cast("烈", -1);
            b.EndTurn();   // 淹没
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Bleed)?.Magnitude, Is.EqualTo(3), "通用形态:回敬任意效果(金:流血)");
        }

        [Test]
        public void Retaliate_IronBarbRecoilDoesNotTrigger()
        {
            var hit = new CharDef("斩", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var b = Battle(new[] { Guard(), hit }, new[] { Mob(ability: EnemyAbility.Barb) });
            b.Cast("烈", -1);
            int player = b.PlayerHp;
            b.Cast("斩", 0);
            Assert.That(b.PlayerHp, Is.LessThan(player), "前提:铁画反噬打到了玩家");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
            Assert.That(Burn(b, 0), Is.EqualTo(0), "反噬不是敌人的挥击,不回敬");
        }

        [Test]
        public void Retaliate_ScalesByCardLevel_AtTrigger()
        {
            var b = Battle(new[] { Guard(onHit: new EffectDef(EffectKind.Bleed, 100)) }, new[] { Mob(attack: 10) }, level: 5);
            b.Cast("烈", -1);
            b.EndTurn();
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Bleed)?.Magnitude,
                Is.EqualTo(MetaRules.ScaleByCardLevel(100, 5)), "回敬效果按来源字等级缩放(结算时)");
        }

        [Test]
        public void Retaliate_SurvivesSnapshotRoundTrip()
        {
            var defs = new[] { Guard() };
            var b = Battle(defs, new[] { Mob(attack: 10) });
            b.Cast("烈", -1);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(defs), Config,
                new Dictionary<string, int> { ["烈"] = 1 }, new Dictionary<string, EnemyDef> { ["怔"] = Mob(attack: 10) });
            var s = restored.PlayerStatuses.Find(StatusKind.Retaliate);
            Assert.That(s?.OnHit?.Count, Is.EqualTo(1));
            Assert.That(s.OnHit[0].Kind, Is.EqualTo(EffectKind.BurnSingle));
            restored.EndTurn();
            Assert.That(Burn(restored, 0), Is.EqualTo(2), "读档后照常回敬");
        }

        [Test]
        public void StatusEffect_Clone_DeepCopiesOnHit()
        {
            var s = new StatusEffect { Kind = StatusKind.Retaliate, OnHit = new List<OpeningEffect> { new OpeningEffect { Kind = EffectKind.BurnSingle, Value = 2 } } };
            var c = s.Clone();
            c.OnHit[0].Value = 9;
            Assert.That(s.OnHit[0].Value, Is.EqualTo(2));
        }

        [Test]
        public void Retaliate_SurvivesRealSaveFile()
        {
            var guard = Guard();
            var graph = RebalanceFixture.Graph(guard);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "烈" } };
            var levels = new Dictionary<string, int> { ["烈"] = 1 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "烈", "烈" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            Assert.That(run.Battle.Cast("烈", -1), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels,
                startingInk: 50, perFloorNormalShield: 2);
            var s = restored.Battle.PlayerStatuses.Find(StatusKind.Retaliate);
            Assert.That(s, Is.Not.Null, "受击回敬进存档 JSON 往返");
            Assert.That((s.OnHit[0].Kind, s.OnHit[0].Value, s.OnHit[0].SourceCharId), Is.EqualTo((EffectKind.BurnSingle, 2, "烈")));
        }

        // ================= 无状态时逐位恒等(特征测试:数值取自实现前的引擎) =================

        /// <summary>杂兵 + 涂改 + 淹没 Boss + 木灵 + 致盲 50%(摇 _random)+ 近战择敌(摇 _targetRandom),推进 4 个敌方回合。
        /// 事件流按 (Kind, Target, Amount, Second, Absorbed, Source) 拼串;期望值是 Task 4 动 ActOneEnemy 之前跑出来的。</summary>
        [Test]
        public void NoHookStatuses_EnemyPhase_EventStreamAndRandomState_Identical()
        {
            var sprout = new CharDef("林", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Summon, 40, summonCount: 2, summonAttack: 3, summonChar: "木") });
            var b = Battle(new[] { sprout }, new[]
            {
                Mob(hp: 900, attack: 12), SkillBoss(), Mob(hp: 700, attack: 9, ability: EnemyAbility.Mend),
            });
            b.Cast("林", -1);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 50, TurnsLeft = 9 });
            b.Enemies[0].Hp -= 100;
            var log = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    log.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
            }
            var snap = b.Capture();
            string digest = $"{Fnv(log.ToString()):x16}|{snap.RandomState}|{snap.TargetRandomState}|{b.PlayerHp}";
            Assert.That(digest, Is.EqualTo("1656f69995100993|1383597714|43070208|490"), "没有埋雷 / 焚身 / 回敬时,敌方段与实现前逐位一致");
        }

        private static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        // ================= 字表加载 =================

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""烈"",""element"":""Fire"",""effects"":[{""kind"":""DamageSingle"",""value"":40}],
              ""traits"":[{""slot"":""Lv8"",""face"":""Feature"",""form"":""Active"",""name"":""护"",""effects"":[" + traitEffects + "]}]}]}");

        private static EffectDef LoadOne(string effect) => Load(effect).Get("烈").Traits[0].Effects[0];

        [Test]
        public void Config_RetaliateAndMine_Parse()
        {
            var r = LoadOne(@"{""kind"":""Retaliate"",""value"":2,""perHit"":[{""kind"":""BurnSingle"",""value"":2}]}");
            Assert.That((r.Kind, r.Value, r.PerHit.Count, r.PerHit[0].Kind), Is.EqualTo((EffectKind.Retaliate, 2, 1, EffectKind.BurnSingle)));
            var m = LoadOne(@"{""kind"":""Mine"",""value"":0,""bodyPercent"":200}");
            Assert.That((m.Kind, m.BodyPercent), Is.EqualTo((EffectKind.Mine, 200)));
            var flat = LoadOne(@"{""kind"":""Mine"",""value"":30,""pick"":""All""}");
            Assert.That((flat.Value, flat.Pick), Is.EqualTo((30, EffectPick.All)));
        }

        [Test]
        public void Config_RetaliateAndMine_RejectBadShapes()
        {
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Retaliate"",""value"":0}"), "受击回敬必须写回敬的效果");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Retaliate"",""value"":-1,""perHit"":[{""kind"":""BurnSingle"",""value"":2}]}"));
            foreach (var bad in new[]
            {
                @"{""kind"":""DamageSingle"",""value"":5}", @"{""kind"":""Detonate"",""value"":0}",
                @"{""kind"":""BurnSettleNow"",""value"":0}", @"{""kind"":""Retaliate"",""value"":0,""perHit"":[{""kind"":""BurnSingle"",""value"":1}]}",
                @"{""kind"":""Weaken"",""value"":5,""turns"":2,""onlyIf"":""Burning""}",
                @"{""kind"":""Blind"",""value"":5,""pick"":""BurnedByThisCast"",""riderOf"":""Burn""}",
            })
                Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Retaliate"",""value"":0,""perHit"":[" + bad + "]}"),
                    $"回敬里不能有伤害 / 嵌套 / 条件门 / 附着:{bad}");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Mine"",""value"":0}"), "地雷没有伤害量");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""BurnSingle"",""value"":2,""bodyPercent"":200}"));
        }
    }
}
