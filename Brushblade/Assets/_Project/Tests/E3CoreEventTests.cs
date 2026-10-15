using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E3 Task 1:拍板 chip 落地缺的 Core 信息 —— 拦截事件(DebuffWarded / BuffBlocked)、
    /// 来源标签(BossSkillCast 的 SkillCut)、Graft.Amount = 回复量。
    /// 字形特性事件、印记读口、CastEffectsNow 在 <see cref="GlyphTraitTests"/>,Badges 在 <see cref="PartMarkRulesTests"/>。
    /// 夹具:Element.Heart、PlayerAttack = 100、10 万血靶子。</summary>
    public class E3CoreEventTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 };

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Sprout = new("苗", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 3000, summonCount: 1, summonAttack: 0) });

        private static BattleEngine Battle(CharDef def, params EnemyDef[] enemies)
        {
            var defs = new[] { def, Hit, Sprout }.Distinct().ToArray();
            return new BattleEngine(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { [def.Id] = 1 });
        }

        private static void Cast(BattleEngine b, string id, int target = -1, int allySlot = Targeting.PlayerTarget) =>
            Assert.That(b.Cast(id, target, allySlot: allySlot), Is.EqualTo(BattleError.None), id);

        private static EnemyDef Mob(EnemyAbility ability = EnemyAbility.None, int attack = 0) =>
            new("怔", Element.Heart, Hp, attack, ability);

        /// <summary>直调 RefreshBurn,返回这一次新增的事件。</summary>
        private static List<BattleEvent> Burn(BattleEngine b, StatusBag bag, int stacks, UnitRef target)
        {
            int before = b.LastEvents.Count;
            b.RefreshBurn(bag, stacks, target, UnitRef.Enemy(0));
            return b.LastEvents.Skip(before).ToList();
        }

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That(new[] { BattleEventKind.DebuffWarded, BattleEventKind.BuffBlocked, BattleEventKind.GlyphTrait }.Select(k => (int)k),
                Is.EqualTo(Enumerable.Range((int)BattleEventKind.CharGranted + 1, 3)));
            // E4 Task 1 在其后追加 EndureSaved(E4CoreTests.E4EnumValues_AppendedAtEnd 守它的位置);
            // 格挡反击动效再追加 BlockUsed(BlockUsedEventTests.BlockUsed_AppendedAtEnd 守它的位置)
            Assert.That(Enum.GetValues(typeof(BattleEventKind)).Cast<int>().Max(), Is.EqualTo((int)BattleEventKind.BlockUsed));
            Assert.That((int)EffectSource.SkillCut, Is.EqualTo((int)EffectSource.ReturnFire + 1));
            // E4 Task 1 在其后追加 BurnGrow
            Assert.That(Enum.GetValues(typeof(EffectSource)).Cast<int>().Max(), Is.EqualTo((int)EffectSource.BurnGrow));
        }

        // ================= DebuffWarded =================

        private static readonly CharDef Bathe = RebalanceFixture.Char("濯",
            new EffectDef(EffectKind.Cleanse, 0), new EffectDef(EffectKind.DebuffWard, 0, turns: 1));
        private static readonly CharDef Douse = RebalanceFixture.Char("浇",
            new EffectDef(EffectKind.DebuffWard, 50, turns: 3, wardOf: StatusKind.Burn));

        [Test]
        public void DebuffWarded_FullWard_OnPlayer_NoShield()
        {
            var b = Battle(Bathe);
            Cast(b, "濯");
            var ev = Burn(b, b.PlayerStatuses, 3, UnitRef.Player);
            Assert.That(ev.Count, Is.EqualTo(1), "全量免疫 Magnitude 0:只发拦截,不转盾");
            var e = ev[0];
            Assert.That((e.Kind, e.TargetIndex, e.Amount, e.SecondIndex, e.Absorbed),
                Is.EqualTo((BattleEventKind.DebuffWarded, -1, (int)StatusKind.Burn, -1, 3)));
        }

        [Test]
        public void DebuffWarded_Douse_OnPlayer_BeforeShield()
        {
            var b = Battle(Douse);
            Cast(b, "浇");
            var ev = Burn(b, b.PlayerStatuses, 3, UnitRef.Player);
            Assert.That(ev.Select(e => e.Kind), Is.EqualTo(new[] { BattleEventKind.DebuffWarded, BattleEventKind.Shield }),
                "拦截在前、转盾在后(稿:护盾数字跟在删除线后面)");
            Assert.That((ev[0].TargetIndex, ev[0].Amount, ev[0].SecondIndex, ev[0].Absorbed),
                Is.EqualTo((-1, (int)StatusKind.Burn, (int)StatusKind.Burn, 3)), "SecondIndex = WardOf;Absorbed = 增量层数");
            Assert.That((ev[1].TargetIndex, ev[1].Amount), Is.EqualTo((-1, 150)));

            // 已有 2 层,刷新到 3:增量 1
            b.PlayerStatuses.Apply(new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 2, TurnsLeft = -1 });
            ev = Burn(b, b.PlayerStatuses, 3, UnitRef.Player);
            Assert.That(ev[0].Absorbed, Is.EqualTo(1));
            // 增量 ≤ 0:不算拦截,不发
            ev = Burn(b, b.PlayerStatuses, 1, UnitRef.Player);
            Assert.That(ev.Any(e => e.Kind == BattleEventKind.DebuffWarded), Is.False, "增量 0 不算拦截");
        }

        [Test]
        public void DebuffWarded_OnSummon_TargetIsSlot()
        {
            var b = Battle(Douse);
            Cast(b, "苗");
            Cast(b, "浇", allySlot: 0);
            var ev = Burn(b, b.Summons[0].Statuses, 2, UnitRef.Summon(0));
            Assert.That(ev.Select(e => (e.Kind, e.TargetIndex)),
                Is.EqualTo(new[] { (BattleEventKind.DebuffWarded, 0), (BattleEventKind.Shield, 0) }));
            Assert.That(ev[0].Absorbed, Is.EqualTo(2));
        }

        [Test]
        public void DebuffWarded_EndToEnd_SealBlockedByFullWard()
        {
            var b = Battle(Bathe, new EnemyDef("覆", Element.Heart, Hp, 10, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, Hp, 10, skill: BossSkill.Topple) }));
            bool sawSeal = false;
            for (int round = 0; round < 3 && !sawSeal; round++)
            {
                Cast(b, "濯");
                b.EndTurn();
                sawSeal = b.LastEvents.Any(e => e.Kind == BattleEventKind.DebuffWarded && e.Amount == (int)StatusKind.Seal);
            }
            Assert.That(sawSeal, Is.True, "倾覆的封字被挡,发 DebuffWarded(Amount = Seal)");
        }

        [Test]
        public void DebuffWarded_NoWard_NotEmitted()
        {
            var b = Battle(Hit);
            Cast(b, "击", 0);
            var ev = Burn(b, b.PlayerStatuses, 3, UnitRef.Player);
            Assert.That(ev.Any(e => e.Kind == BattleEventKind.DebuffWarded), Is.False);
            Assert.That(b.PlayerStatuses.Find(StatusKind.Burn)?.Magnitude, Is.EqualTo(3));
        }

        // ================= BuffBlocked =================

        private static readonly CharDef Lock = RebalanceFixture.Char("锁",
            new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 2));

        [Test]
        public void BuffBlocked_ScorchSelfBuff_Emitted()
        {
            var b = Battle(Lock, Mob(EnemyAbility.Scorch));
            Cast(b, "锁", 0);
            Cast(b, "击", 0);
            var blocked = b.LastEvents.Where(e => e.Kind == BattleEventKind.BuffBlocked).ToList();
            Assert.That(blocked.Count, Is.EqualTo(1));
            Assert.That((blocked[0].TargetIndex, blocked[0].Amount), Is.EqualTo((0, (int)StatusKind.AttackBuff)));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyBuff), Is.False);
        }

        [Test]
        public void BuffBlocked_PunctuationAura_OnlyOnBlockedTarget()
        {
            var b = Battle(Lock, Mob(), Mob(), Mob(EnemyAbility.Buff));
            Cast(b, "锁", 0);
            b.EndTurn();
            var blocked = b.LastEvents.Where(e => e.Kind == BattleEventKind.BuffBlocked).Select(e => e.TargetIndex).ToList();
            Assert.That(blocked, Is.EqualTo(new[] { 0 }), "只有带禁增益的那只发");
        }

        [Test]
        public void BuffBlocked_NoBlock_NotEmitted()
        {
            var b = Battle(Hit, Mob(EnemyAbility.Scorch));
            Cast(b, "击", 0);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BuffBlocked), Is.False);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyBuff), Is.True, "前提:自燃照常");
        }

        /// <summary>霜抗豁免禁增益(Q21):冻结到期照挂霜抗,不算「挡下」,不发 BuffBlocked。</summary>
        [Test]
        public void BuffBlocked_FrostResist_NotBlocked_NotEmitted()
        {
            var lockLong = RebalanceFixture.Char("锁",
                new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BuffBlock, 0, turns: 3));
            var chill = RebalanceFixture.Char("冻",
                new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Freeze, 1));
            var defs = new[] { lockLong, chill };
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                new[] { Mob() }, seed: 1);
            Cast(b, "锁", 0);
            Cast(b, "冻", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.True, "前提:冻上");
            b.EndTurn();   // 冻结那拍到期 → 霜抗
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.BuffBlock), Is.True, "前提:禁增益仍在");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostResist), Is.True, "霜抗照挂");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BuffBlocked), Is.False, "霜抗不算挡下,不发");
        }

        // ================= BossSkillCast Source = SkillCut =================

        private static readonly CharDef Plug = RebalanceFixture.Char("塞",
            new EffectDef(EffectKind.SkillCut, 50, onlyIf: DamageCondition.IsBoss));

        private static BattleEvent SkillCast(bool cut)
        {
            var boss = new EnemyDef("试炼", Element.Heart, Hp, 40,
                phases: new[] { new BossPhaseDef("甲", Element.Heart, Hp, 40, skill: BossSkill.Deluge) });
            var b = Battle(Plug, boss);
            b.EndTurn();   // 普攻
            b.EndTurn();   // 蓄力
            Assert.That(b.Enemies[0].IsCharging, Is.True, "前提:下一拍放大招");
            if (cut) Cast(b, "塞", 0);
            b.EndTurn();
            return b.LastEvents.Single(e => e.Kind == BattleEventKind.BossSkillCast);
        }

        [Test]
        public void BossSkillCast_WithSkillCut_TaggedSkillCut()
        {
            var e = SkillCast(cut: true);
            Assert.That(e.Source, Is.EqualTo(EffectSource.SkillCut));
            Assert.That(e.Amount, Is.EqualTo((int)BossSkill.Deluge));
        }

        [Test]
        public void BossSkillCast_WithoutSkillCut_SourceNone()
        {
            Assert.That(SkillCast(cut: false).Source, Is.EqualTo(EffectSource.None));
        }

        // ================= Graft.Amount = 回复量 =================

        [Test]
        public void Graft_Amount_IsHealedHp()
        {
            var graftor = new CharDef("藤", Element.Wood, effects: new[]
                { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 10, summonChar: "木") });
            var b = new BattleEngine(RebalanceFixture.Graph(graftor),
                new BattleConfig { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100 },
                new[] { "藤", "藤" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1,
                startingSummons: new[]
                {
                    new SummonSnapshot { Slot = 0, Char = "木", Element = Element.Wood, Hp = 30, MaxHp = 100, Attack = 10, Speed = 100 },
                });
            Assert.That(b.Cast("藤", -1, allySlot: 0), Is.EqualTo(BattleError.None));
            var e = b.LastEvents.Single(x => x.Kind == BattleEventKind.Graft);
            Assert.That((e.TargetIndex, e.SecondIndex, e.Amount), Is.EqualTo((-1, 0, 70)), "回复量 = 回满前缺的血");
        }
    }
}
