using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 4:敌人 / 回合侧 —— G5 OverkillSplash(粉身碎骨)、G6 SkillCut(杜塞 · Boss)、G7 ApCost(杜门不出)。
    /// 口径见 plan「已定口径」Q2 / Q3 / Q9 / Q21 与附录 G5–G7;字表数据归 Task 6,这里一律手搓 CharDef。</summary>
    public class EarthEnemyTurnTests
    {
        private const int MaxHp = 5000;

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int apPerTurn = 200, int level = 1)
        {
            var all = new[] { new CharDef("木", Element.Wood) }.Concat(defs).ToArray();
            return new BattleEngine(RebalanceFixture.Graph(all),
                new BattleConfig
                {
                    PlayerMaxHp = MaxHp, PlayerAttack = 100, ApPerTurn = apPerTurn, LibraryCapacity = 200,
                    BossPhaseJitterPercent = 0,
                },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies, seed: 3, cardLevels: defs.ToDictionary(d => d.Id, _ => level));
        }

        private static List<BattleEvent> Damage(BattleEngine b, EffectSource src) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == src).ToList();

        // ---------------- G5 OverkillSplash ----------------

        /// <summary>碎:攻击面单体 300,击杀时 = 粉身碎骨(+ 可选的战意 1,用来数击杀时入队了几次)。</summary>
        private static CharDef Shatter(int percent = 100, bool morale = false)
        {
            var effects = new List<EffectDef> { new EffectDef(EffectKind.OverkillSplash, percent) };
            if (morale) effects.Add(new EffectDef(EffectKind.Morale, 1));
            return new CharDef("碎", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 300) },
                traits: new[]
                {
                    new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Passive, null, "粉身碎骨", effects, TraitTrigger.OnKill),
                });
        }

        private static int Lost(BattleEngine b, int i) => b.Enemies[i].MaxHp - b.Enemies[i].Hp;

        /// <summary>致死一击对主目标的实际伤害(Damage 事件 Amount − Absorbed)。</summary>
        private static int KillingBlow(BattleEngine b, int victim) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == victim && e.Source == EffectSource.None)
            .Select(e => e.Amount - e.Absorbed).Single();

        [Test]
        public void OverkillSplash_SplitsEvenly_AmongOtherLiving()
        {
            var b = Battle(new[] { Shatter() }, new[]
            {
                RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(), RebalanceFixture.Mob(), RebalanceFixture.Mob(), RebalanceFixture.Mob(hp: 50),
            }, level: 8);
            b.Enemies[4].Hp = 0;   // 已死的不分
            Assert.That(b.Cast("碎", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            int overkill = KillingBlow(b, 0) - 10;
            Assert.That(overkill, Is.GreaterThan(30), "前提:有可观的溢出");
            int share = overkill / 3;
            foreach (int i in new[] { 1, 2, 3 })
                Assert.That(Lost(b, i), Is.EqualTo(share), $"敌人 {i} 分到 溢出 / 3(整数除,余数丢弃)");
            var splash = Damage(b, EffectSource.OverkillSplash);
            Assert.That(splash.Count, Is.EqualTo(3));
            Assert.That(splash.All(e => e.Attacker == Element.Earth && !e.Crit), Is.True, "土属性、不暴击");
        }

        [Test]
        public void OverkillSplash_Percent_AndShieldOnVictim_CountsActualDamageOnly()
        {
            var b = Battle(new[] { Shatter(percent: 50) }, new[] { RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob() }, level: 8);
            b.Enemies[0].Shield = 40;
            b.Cast("碎", 0, attackMode: true);
            var blow = b.LastEvents.Single(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == 0);
            Assert.That(blow.Absorbed, Is.EqualTo(40), "前提:敌盾吃掉了一部分");
            int overkill = blow.Amount - 40 - 10;
            Assert.That(Lost(b, 1), Is.EqualTo(overkill * 50 / 100), "溢出 = 过敌盾后的实际伤害 − 击杀前生命,再 × 50%");
        }

        [Test]
        public void OverkillSplash_GoesThroughArmorAndWuxing()
        {
            var b = Battle(new[] { Shatter() }, new[]
            {
                RebalanceFixture.Mob(hp: 10),
                RebalanceFixture.Mob(armor: 100),                           // 心:生克 1.0,吃护甲(100 → 减伤 50%)
                new EnemyDef("水", Element.Water, 100000, 0, defense: 100),  // 土克水:×1.5,相克即破甲
                new EnemyDef("林", Element.Wood, 100000, 0),                 // 木克土:×0.5
            }, level: 8);
            b.Cast("碎", 0, attackMode: true);
            int share = (KillingBlow(b, 0) - 10) / 3;
            Assert.That(Lost(b, 1), Is.EqualTo(share * 100 / 200), "过护甲");
            Assert.That(Lost(b, 2), Is.EqualTo(share * 3 / 2), "土克水 ×1.5,无视护甲");
            Assert.That(Lost(b, 3), Is.EqualTo(share / 2), "木克土 ×0.5");
        }

        [Test]
        public void OverkillSplash_SplashKills_DoNotChain()
        {
            var b = Battle(new[] { Shatter(morale: true) }, new[]
            {
                RebalanceFixture.Mob(hp: 10), RebalanceFixture.Mob(hp: 5), RebalanceFixture.Mob(),
            }, level: 8);
            b.Cast("碎", 0, attackMode: true);
            int share = (KillingBlow(b, 0) - 10) / 2;
            Assert.That(b.Enemies[1].Alive, Is.False, "前提:溅射打死了 1 号(它也有溢出)");
            Assert.That(Lost(b, 2), Is.EqualTo(share), "2 号只吃第一份;溅射击杀不再入队击杀时,不连锁");
            Assert.That(Damage(b, EffectSource.OverkillSplash).Count, Is.EqualTo(2));
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(1), "击杀时只为主目标入队一次(R4)");
        }

        [Test]
        public void OverkillSplash_ExactKill_NoSplash()
        {
            var b = Battle(new[] { Shatter() }, new[] { RebalanceFixture.Mob(hp: 100000), RebalanceFixture.Mob() }, level: 8);
            b.Cast("碎", 0, attackMode: true);
            int dealt = KillingBlow(b, 0);
            // 另起一场,把主目标血量设成正好打死
            var exact = Battle(new[] { Shatter() }, new[] { RebalanceFixture.Mob(hp: dealt), RebalanceFixture.Mob() }, level: 8);
            exact.Cast("碎", 0, attackMode: true);
            Assert.That(exact.Enemies[0].Alive, Is.False);
            Assert.That(Damage(exact, EffectSource.OverkillSplash), Is.Empty, "溢出 0 不溅射");
            Assert.That(Lost(exact, 1), Is.EqualTo(0));
        }

        [Test]
        public void OverkillSplash_BigBoss_CountsAsOneShare()
        {
            var boss = new EnemyDef("巨", Element.Heart, 100000, 0, columnSpan: 2, rowSpan: 2,
                phases: new[] { new BossPhaseDef("巨", Element.Heart, 100000, 0) });
            var b = Battle(new[] { Shatter() }, new[] { RebalanceFixture.Mob(hp: 10), boss, RebalanceFixture.Mob() }, level: 8);
            b.Cast("碎", 0, attackMode: true);
            int share = (KillingBlow(b, 0) - 10) / 2;
            Assert.That(Lost(b, 1), Is.EqualTo(share), "跨排 Boss 算 1 份,只吃一次");
            Assert.That(Lost(b, 2), Is.EqualTo(share));
        }

        [Test]
        public void OverkillSplash_OnlyLastEnemy_NothingToSplash()
        {
            var b = Battle(new[] { Shatter() }, new[] { RebalanceFixture.Mob(hp: 10) }, level: 8);
            Assert.That(b.Cast("碎", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(Damage(b, EffectSource.OverkillSplash), Is.Empty);
        }

        // ---------------- G6 SkillCut ----------------

        private const int BossAttack = 40;

        private static EnemyDef SkillBoss(BossSkill skill) => new("试炼", Element.Heart, 100000, BossAttack,
            phases: new[] { new BossPhaseDef("甲", Element.Heart, 100000, BossAttack, skill: skill) });

        // 杜塞:主目标 Boss 挂 SkillCut 50(`if IsBoss`)
        private static readonly CharDef Block = RebalanceFixture.Char("塞",
            new EffectDef(EffectKind.SkillCut, 50, onlyIf: DamageCondition.IsBoss));
        private static readonly CharDef Grove = RebalanceFixture.Char("林",
            new EffectDef(EffectKind.Summon, 1000, summonCount: 2, summonAttack: 0, summonChar: "木"));
        private static readonly CharDef Keep = RebalanceFixture.Char("留", new EffectDef(EffectKind.Shield, 500, persistOnce: true));

        /// <summary>开场:可选召木灵 / 留存盾 / 杜塞,再推两个敌方回合(普攻 → 蓄力),停在大招前。</summary>
        private static BattleEngine BeforeSkill(BossSkill skill, bool cut, bool summons = true, bool shield = false)
        {
            var b = Battle(new[] { Block, Grove, Keep }, new[] { SkillBoss(skill) });
            if (summons) Assert.That(b.Cast("林", -1), Is.EqualTo(BattleError.None));
            b.EndTurn();   // 普攻
            b.EndTurn();   // 蓄力
            Assert.That(b.Enemies[0].IsCharging, Is.True, "前提:下一个敌方回合放大招");
            if (shield) b.Cast("留", -1);
            if (cut)
            {
                Assert.That(b.Cast("塞", 0), Is.EqualTo(BattleError.None));
                Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.True);
            }
            return b;
        }

        private static (int Player, int[] Summons) SkillHit(BattleEngine b)
        {
            int hp = b.PlayerHp;
            var summons = b.Summons.Select(s => s?.Hp ?? 0).ToArray();
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "前提:这一拍放了大招");
            return (hp - b.PlayerHp, b.Summons.Select((s, i) => summons[i] - (s?.Hp ?? 0)).ToArray());
        }

        [Test]
        public void SkillCut_Deluge_HalvesPlayerAndSummonDamage()
        {
            var plain = SkillHit(BeforeSkill(BossSkill.Deluge, cut: false));
            var b = BeforeSkill(BossSkill.Deluge, cut: true);
            var halved = SkillHit(b);
            Assert.That(plain.Player, Is.EqualTo(BossAttack * 2));
            Assert.That(halved.Player, Is.EqualTo(BossAttack * 2 * 50 / 100));
            Assert.That(plain.Summons.Take(2), Is.EqualTo(new[] { BossAttack, BossAttack }));
            Assert.That(halved.Summons.Take(2), Is.EqualTo(new[] { BossAttack / 2, BossAttack / 2 }));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "放完即移除");
        }

        [Test]
        public void SkillCut_Impale_HalvesFrontSummonAndPlayer()
        {
            var plain = SkillHit(BeforeSkill(BossSkill.Impale, cut: false));
            var halved = SkillHit(BeforeSkill(BossSkill.Impale, cut: true));
            Assert.That(plain.Player, Is.EqualTo(BossAttack * 2));
            Assert.That(halved.Player, Is.EqualTo(BossAttack));
            Assert.That(plain.Summons.Sum(), Is.EqualTo(BossAttack));
            Assert.That(halved.Summons.Sum(), Is.EqualTo(BossAttack / 2));
        }

        [Test]
        public void SkillCut_Topple_HalvesDamage_ShieldClearAndSealStillApply()
        {
            var b = BeforeSkill(BossSkill.Topple, cut: true, summons: false, shield: true);
            int shieldBefore = b.ShieldNormal + b.ShieldPersist;
            Assert.That(shieldBefore, Is.GreaterThan(BossAttack), "前提:留存盾够吸这一击");
            b.EndTurn();
            var hit = b.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(hit.Amount, Is.EqualTo(BossAttack), "倾覆伤害 Attack × 2 × 50%");
            Assert.That(b.ShieldNormal + b.ShieldPersist, Is.EqualTo(0), "清盾照常");
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.ShieldBroken).Amount, Is.EqualTo(shieldBefore - BossAttack));
            Assert.That(b.LastEvents.Single(e => e.Kind == BattleEventKind.ShieldBroken).Source, Is.EqualTo(EffectSource.None),
                "倾覆清盾不带来源标签 —— 表现层据此只给倾覆播「护盾被掀空」并把护盾条归零(D2-土 Task 5)");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "倾覆"), Is.True, "封字照常");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False);
        }

        [Test]
        public void SkillCut_Devour_StillKillsFrontSummon_AndIsConsumed()
        {
            var b = BeforeSkill(BossSkill.Devour, cut: true);
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Summons.Count(s => s != null && s.Alive), Is.EqualTo(1), "吞噬秒杀不受影响");
            Assert.That(b.PlayerHp, Is.EqualTo(hp), "吞到了就不打玩家");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "放完即移除");
        }

        [Test]
        public void SkillCut_Devour_NoSummon_FallbackAttackHalved()
        {
            var plain = SkillHit(BeforeSkill(BossSkill.Devour, cut: false, summons: false));
            var halved = SkillHit(BeforeSkill(BossSkill.Devour, cut: true, summons: false));
            Assert.That(plain.Player, Is.EqualTo(BossAttack));
            Assert.That(halved.Player, Is.EqualTo(BossAttack / 2));
        }

        [Test]
        public void SkillCut_OnlyNextSkill_ThenFullDamage()
        {
            var b = BeforeSkill(BossSkill.Deluge, cut: true, summons: false);
            Assert.That(SkillHit(b).Player, Is.EqualTo(BossAttack));
            b.EndTurn();   // 普攻
            b.EndTurn();   // 蓄力
            Assert.That(SkillHit(b).Player, Is.EqualTo(BossAttack * 2), "第二次大招不再削减");
        }

        [Test]
        public void SkillCut_NotConsumed_OnAttackChargeOrIceStallPushedBeats()
        {
            // SkillCut 只在 CastBossSkill 开头取用:普攻拍、蓄力拍、被冰滞推后没轮到出手的那一拍都不消耗
            var freeze = new CharDef("冻", Element.Water, effects: new[] { new EffectDef(EffectKind.Freeze, 1, stallPushPercent: 100) });   // 坚冰式加推:共后退 1.5 格
            var b = Battle(new[] { Block, freeze }, new[] { SkillBoss(BossSkill.Deluge) });
            Assert.That(b.Cast("塞", 0), Is.EqualTo(BattleError.None));

            b.EndTurn();   // 普攻
            var attack = b.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(attack.Amount, Is.EqualTo(BossAttack), "普攻不吃削减");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.True, "普攻拍不消耗");

            b.EndTurn();   // 蓄力
            Assert.That(b.Enemies[0].IsCharging, Is.True, "前提:这一拍在蓄力");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.True, "蓄力拍不消耗");

            Assert.That(b.Cast("冻", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.True, "前提:Boss 进冰滞、行动条后退半格");
            int hp = b.PlayerHp;
            b.EndTurn();   // 被推后:本该放大招的这一拍没轮到
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.False, "前提:大招被推后");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.True, "被推后的拍不消耗");

            int guard = 0;
            while (!b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast) && guard++ < 5) { hp = b.PlayerHp; b.EndTurn(); }
            Assert.That(hp - b.PlayerHp, Is.EqualTo(BossAttack * 2 * 50 / 100), "留到真正放大招时才削减");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "放完即移除");
        }

        [Test]
        public void ApCost_StacksWithToppleSeal_ApMinusTwo()
        {
            // 杜门不出的 AP 代价(Buff 极性 Seal)与倾覆的封字(Debuff 极性 Seal)来源不同,两条同时在场:下回合 AP −2
            var b = Battle(new[] { Door() }, new[] { SkillBoss(BossSkill.Topple) }, apPerTurn: 4);
            b.EndTurn();   // 普攻
            b.EndTurn();   // 蓄力
            Assert.That(b.Cast("杜", -1), Is.EqualTo(BattleError.None));
            b.EndTurn();   // 倾覆
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "前提:倾覆放出来了");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "倾覆"), Is.True, "倾覆封字在场");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "杜"), Is.True, "AP 代价在场");
            Assert.That(b.Ap, Is.EqualTo(2), "两条 Seal 叠加:AP −2");
        }

        [Test]
        public void SkillCut_MultipleSources_TakeLargest()
        {
            var b = BeforeSkill(BossSkill.Deluge, cut: true, summons: false);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.SkillCut, Polarity = StatusPolarity.Debuff, Magnitude = 75, TurnsLeft = -1, SourceId = "别" });
            Assert.That(SkillHit(b).Player, Is.EqualTo(BossAttack * 2 * 25 / 100));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "整类移除");
        }

        [Test]
        public void SkillCut_OnMob_NotApplied()
        {
            var cut = RebalanceFixture.Char("截", new EffectDef(EffectKind.SkillCut, 50));
            var b = Battle(new[] { cut }, new[] { RebalanceFixture.Mob() });
            Assert.That(b.Cast("截", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "杂兵没有大招,不挂");
        }

        [Test]
        public void SkillCut_OnlyIfIsBoss_SkipsMobs_PickAll()
        {
            var cut = RebalanceFixture.Char("截", new EffectDef(EffectKind.SkillCut, 50, onlyIf: DamageCondition.IsBoss, pick: EffectPick.All));
            var b = Battle(new[] { cut }, new[] { RebalanceFixture.Mob(), SkillBoss(BossSkill.Deluge) });
            Assert.That(b.Cast("截", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Statuses.Find(StatusKind.SkillCut).Magnitude, Is.EqualTo(50));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False);
        }

        // ---------------- G7 ApCost ----------------

        private static CharDef Door(int ap = 1) => RebalanceFixture.Char("杜",
            new EffectDef(EffectKind.DamageCut, 60), new EffectDef(EffectKind.ApCost, ap));

        [Test]
        public void ApCost_NextTurnApMinusN_ThenRestored()
        {
            var b = Battle(new[] { Door() }, new[] { RebalanceFixture.Mob() }, apPerTurn: 4);
            Assert.That(b.Cast("杜", -1), Is.EqualTo(BattleError.None));
            var seal = b.PlayerStatuses.Find(StatusKind.Seal);
            Assert.That(seal.Polarity, Is.EqualTo(StatusPolarity.Buff), "代价不算减益(R4)");
            Assert.That(seal.TurnsLeft, Is.EqualTo(2));
            Assert.That(seal.Magnitude, Is.EqualTo(1));
            Assert.That(b.Ap, Is.EqualTo(3), "本回合只扣出字的 1 点");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(3), "下回合 AP −1");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(4), "再下回合恢复");
            Assert.That(b.PlayerStatuses.Has(StatusKind.Seal), Is.False);
        }

        [Test]
        public void DebuffWard_BlocksToppleSeal_ButNotApCost()
        {
            var b = Battle(new[] { Door() }, new[] { SkillBoss(BossSkill.Topple) });
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.DebuffWard, Polarity = StatusPolarity.Buff, Magnitude = 0, TurnsLeft = 10, SourceId = "杜绝" });
            b.Cast("杜", -1);
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "杜"), Is.True, "杜绝不拦 AP 代价");
            b.EndTurn();   // 普攻
            b.EndTurn();   // 蓄力
            b.EndTurn();   // 倾覆
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "前提:倾覆放出来了");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "倾覆"), Is.False, "杜绝拦下倾覆的封字");

            // 对照:没有杜绝时倾覆照常封字(否则上面那条断言空转)
            var ctrl = Battle(new[] { Door() }, new[] { SkillBoss(BossSkill.Topple) });
            ctrl.EndTurn(); ctrl.EndTurn(); ctrl.EndTurn();
            Assert.That(ctrl.PlayerStatuses.All.Any(s => s.Kind == StatusKind.Seal && s.SourceId == "倾覆"), Is.True);
        }

        [Test]
        public void Cleanse_DoesNotRemoveApCost()
        {
            var wash = RebalanceFixture.Char("洗", new EffectDef(EffectKind.Cleanse, 0, pick: EffectPick.Self));
            var b = Battle(new[] { Door(), wash }, new[] { RebalanceFixture.Mob() }, apPerTurn: 4);
            b.Cast("杜", -1);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 2, SourceId = "倾覆" });
            Assert.That(b.Cast("洗", -1), Is.EqualTo(BattleError.None));
            var seals = b.PlayerStatuses.All.Where(s => s.Kind == StatusKind.Seal).ToList();
            Assert.That(seals.Count, Is.EqualTo(1), "净化只清掉减益那条(对照)");
            Assert.That(seals[0].SourceId, Is.EqualTo("杜"), "AP 代价洗不掉");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(3));
        }

        [Test]
        public void ApCost_BattleLimit_SecondCastOnlyBody()
        {
            var trait = new TraitDef(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Active, null, "杜门不出",
                new[] { new EffectDef(EffectKind.DamageCut, 60), new EffectDef(EffectKind.ApCost, 1) }, maxPerBattle: 1);
            var door = new CharDef("门", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 10) }, traits: new[] { trait });
            var b = Battle(new[] { door }, new[] { RebalanceFixture.Mob() }, apPerTurn: 4, level: 8);
            b.Cast("门", -1);
            b.Cast("门", -1);
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Seal), Is.EqualTo(1), "同一回合第二次出字不再进 Fold");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(3));
            b.Cast("门", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.DamageCut), Is.False, "整条特性(减伤与 AP 代价)每场一次");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(4), "第三次出字只剩本体,不再扣 AP");
        }

        // ---------------- 分类 / Loader ----------------

        [Test]
        public void Enums_AppendedAtTail()
        {
            Assert.That((int)EffectKind.OverkillSplash, Is.EqualTo((int)EffectKind.ShieldShatter + 1));
            Assert.That((int)EffectKind.SkillCut, Is.EqualTo((int)EffectKind.ShieldShatter + 2));
            Assert.That((int)EffectKind.ApCost, Is.EqualTo((int)EffectKind.ShieldShatter + 3));
            Assert.That((int)StatusKind.SkillCut, Is.EqualTo((int)StatusKind.ShieldShatter + 1));
            Assert.That((int)EffectSource.OverkillSplash, Is.EqualTo((int)EffectSource.ShieldShatter + 1));
        }

        [Test]
        public void Classification()
        {
            foreach (var k in new[] { EffectKind.OverkillSplash, EffectKind.SkillCut, EffectKind.ApCost })
                Assert.That(MetaRules.ScalesWithCardLevel(k), Is.False, k.ToString());
            Assert.That(EffectPickRules.Supports(EffectKind.SkillCut), Is.True);
            Assert.That(EffectPickRules.Supports(EffectKind.OverkillSplash), Is.False);
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.OverkillSplash), Is.True);
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.SkillCut), Is.True);
            Assert.That(CardFaceRules.HostileKinds.Contains(EffectKind.ApCost), Is.False);
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.SkillCut, 50)), Is.True, "单写时挂主目标");
        }

        private const string Body = @"""effects"":[{""kind"":""Shield"",""value"":10}],""attackEffects"":[{""kind"":""DamageSingle"",""value"":30}]";

        private static RecipeGraph LoadTrait(string trigger, string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""碎"",""element"":""Earth""," + Body + @",""traits"":[{""slot"":""Lv8"",""face"":""Attack"",""form"":""Passive"",""trigger"":"""
            + trigger + @""",""name"":""粉"",""effects"":[" + effect + "]}]}]}");

        private static RecipeGraph Load(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""杜"",""element"":""Earth"",""effects"":[" + effect + "]}]}");

        [Test]
        public void Loader_OverkillSplash_OnlyInOnKillTrait()
        {
            Assert.That(LoadTrait("OnKill", @"{""kind"":""OverkillSplash"",""value"":100}").Get("碎").Traits[0].Effects[0].Kind,
                Is.EqualTo(EffectKind.OverkillSplash));
            var ex = Assert.Throws<ConfigException>(() => LoadTrait("OnCrit", @"{""kind"":""OverkillSplash"",""value"":100}"));
            Assert.That(ex.Message, Does.Contain("OnKill"));
            ex = Assert.Throws<ConfigException>(() => Load(@"{""kind"":""OverkillSplash"",""value"":100}"));
            Assert.That(ex.Message, Does.Contain("OnKill"));
        }

        [Test]
        public void Loader_OverkillSplash_RejectedInPerHit()
        {
            // 顶层检查只看 Effects 本身;写进 perHit 子效果(每击附带)也得拒 —— 即便宿主是 OnKill 特性
            const string nested = @"{""kind"":""DamageSingle"",""value"":10,""perHit"":[{""kind"":""OverkillSplash"",""value"":100}]}";
            var ex = Assert.Throws<ConfigException>(() => LoadTrait("OnKill", nested));
            Assert.That(ex.Message, Does.Contain("OverkillSplash"));
            ex = Assert.Throws<ConfigException>(() => Load(nested));
            Assert.That(ex.Message, Does.Contain("OverkillSplash"));
        }

        [TestCase(@"{""kind"":""SkillCut"",""value"":50,""onlyIf"":""IsBoss""}")]
        [TestCase(@"{""kind"":""ApCost"",""value"":1}")]
        public void Loader_Accepts(string effect) =>
            Assert.That(Load(@"{""kind"":""DamageSingle"",""value"":10}," + effect).Get("杜").Effects.Count, Is.EqualTo(2));

        [TestCase(@"{""kind"":""SkillCut"",""value"":0}", "SkillCut")]
        [TestCase(@"{""kind"":""SkillCut"",""value"":101}", "SkillCut")]
        [TestCase(@"{""kind"":""ApCost"",""value"":0}", "ApCost")]
        public void Loader_Rejects(string effect, string needle)
        {
            var ex = Assert.Throws<ConfigException>(() => Load(@"{""kind"":""DamageSingle"",""value"":10}," + effect));
            Assert.That(ex.Message, Does.Contain(needle));
        }

        [Test]
        public void Loader_OverkillSplash_PercentRange()
        {
            var ex = Assert.Throws<ConfigException>(() => LoadTrait("OnKill", @"{""kind"":""OverkillSplash"",""value"":0}"));
            Assert.That(ex.Message, Does.Contain("OverkillSplash"));
        }
    }
}
