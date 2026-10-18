using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 2 第一步:光环族(M1 / M2 / 参天之力 / 馥郁)要插桩的共享热路径,在**无新状态**时逐位恒等。
    /// 覆盖 RefreshSummonAura / SummonState.EffectiveAttack(光环、成林、剡、战意乘区、远程选目标)、DamageSummon 减伤段
    /// (格挡 + 本回合减伤 + 铜墙铁壁)、DamagePlayerDirect 减伤段(同上)、木灵暴击不摇号(附录 §3 末第 7 项)。
    ///
    /// 指纹写法同 <see cref="WoodSharedIdentityTests"/>,另记每只木灵的 EffectiveAttack / 光环 / 成林加成。
    /// 期望值在改动前(HEAD 1f007de8,D2-木 Task 1 完成态)跑出。</summary>
    public class WoodAuraIdentityTests
    {
        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static BattleConfig Config() => new()
            { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 300 };

        private static BattleEngine Battle(HookLog log, CharDef[] defs, EnemyDef[] enemies, bool playerCrit = true,
            IReadOnlyDictionary<string, int> levels = null)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(defs), Config(),
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                enemies, seed: 31, startingHp: 3000, cardLevels: levels);
            if (playerCrit)
                b.PlayerStatuses.Apply(new StatusEffect
                    { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, StringBuilder sb, bool attack = false,
            int allySlot = Targeting.PlayerTarget, bool replace = false)
        {
            sb.Append($"#{id}{target}/{allySlot}{(attack ? "A" : "")}={b.Cast(id, target, replaceSummon: replace, attackMode: attack, allySlot: allySlot)};");
            // BlockUsed(格挡反击动效,2026-10-15)是纯追加的表现事件,不进恒等指纹 —— 其余事件流仍逐位对照
            foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.BlockUsed && x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source}:{e.Crit};");
        }

        private static void AppendState(StringBuilder sb, BattleEngine b)
        {
            sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0/C{b.EffectiveCrit}[");
            foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft}:{s.TargetSlot} ");
            sb.Append(']');
            int i = 0;
            foreach (var s in b.Summons)
            {
                i++;
                if (s == null) continue;
                sb.Append($",W{i - 1}:{s.Char}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.Shield}/{s.EffectiveAttack}/{s.AuraAttackBonus}"
                    + $"/{s.PerAllyAttackBonus}/{s.PlayerAttackPercent}/{s.ActionMeter}[");
                foreach (var st in s.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft}:{st.SourceId} ");
                sb.Append(']');
            }
            for (int e = 0; e < b.Enemies.Count; e++)
            {
                var en = b.Enemies[e];
                sb.Append($",E{en.Hp}/{en.ActionMeter}[");
                foreach (var st in en.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft} ");
                sb.Append(']');
            }
            var snap = b.Capture();
            sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
        }

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int, StringBuilder> eachTurn,
            int turns, StringBuilder all)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                eachTurn(b, t, sb);
                sb.Append($"|C{b.Ap}/S{b.ShieldNormal}+0");
                b.EndTurn();
                // BlockUsed(格挡反击动效,2026-10-15)是纯追加的表现事件,不进恒等指纹 —— 其余事件流仍逐位对照
                foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.BlockUsed && x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source}:{e.Crit};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                AppendState(sb, b);
            }
            string raw = sb.ToString();
            all.Append(raw);
            return Hash(raw);
        }

        private static string Hash(string raw)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        private static CharDef Wood(string id, params EffectDef[] effects) => new(id, Element.Wood, effects: effects,
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) });

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        // ---------------- RefreshSummonAura / EffectiveAttack(M1 光环、参天之力的落点;远程选目标是 SummonRanged 的落点)----------------

        [Test]
        public void AuraAttackPath_NoNewState_Identical()
        {
            var grove = Wood("林", new EffectDef(EffectKind.Summon, 500, summonCount: 2, summonAttack: 30, summonChar: "木",
                passive: new SummonPassive { PerAllyAttackPercent = 10, AuraAttack = 15 }));
            var arrow = Wood("箭", new EffectDef(EffectKind.Summon, 300, summonAttack: 40, summonChar: "箭",
                passive: new SummonPassive { Ranged = true, BackRowBonusPercent = 50 }));
            var spur = RebalanceFixture.Char("剡", new EffectDef(EffectKind.Empower, 25));
            var war = RebalanceFixture.Char("战", new EffectDef(EffectKind.Morale, 1));
            var log = new HookLog();
            var b = Battle(log, new[] { grove, arrow, spur, war, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 90),
                new EnemyDef("壮", Element.Fire, 100000, 70),
                new EnemyDef("远", Element.Heart, 100000, 60, row: EnemyRow.Back, range: AttackRange.Ranged),
            }, levels: new Dictionary<string, int> { ["林"] = 3 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "林", -1, sb);
                if (t == 1) Do(x, "箭", -1, sb);
                if (t == 2) Do(x, "剡", -1, sb, allySlot: 0);
                if (t % 2 == 1) Do(x, "战", -1, sb);
                Do(x, "击", t % 3, sb);
            }, 8, all);
            string raw = all.ToString();
            Assert.That(raw.Contains("SummonAttack:"), Is.True, "场景没走到木灵出手\n" + raw);
            Assert.That(raw.Contains("AttackBuff:25"), Is.True, "剡没挂到木灵上\n" + raw);
            Assert.That(raw.Contains("SummonAttack:2:"), Is.True, "远程木灵没打到后排\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(8));
            Assert.That(fp, Is.EqualTo(AuraAttackExpected), fp);
        }

        // ---------------- DamageSummon / DamagePlayerDirect 减伤段(M2 林荫 / 森严的落点,Q8)----------------

        [Test]
        public void CutSections_NoNewState_Identical()
        {
            var tank = Wood("桩", new EffectDef(EffectKind.Summon, 1500, summonAttack: 20, summonChar: "木"));
            var parry = new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 2) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });
            var cut = RebalanceFixture.Char("减", new EffectDef(EffectKind.DamageCut, 25));
            var wall = RebalanceFixture.Char("铜", new EffectDef(EffectKind.HitCap, 4));
            var log = new HookLog();
            var b = Battle(log, new[] { tank, parry, cut, wall, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 400),
                new EnemyDef("远", Element.Fire, 100000, 330, range: AttackRange.Ranged),
            });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "桩", -1, sb);
                if (t % 3 == 1) Do(x, "挡", -1, sb, allySlot: t % 2 == 1 ? 0 : Targeting.PlayerTarget);
                if (t % 3 == 2) Do(x, "挡", -1, sb);
                if (t % 2 == 0) Do(x, "减", -1, sb);
                if (t % 4 == 3) Do(x, "铜", -1, sb);
                Do(x, "击", t % 2, sb);
            }, 9, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "SummonHit:", "EnemyAttack:", ":BlockCounter:", "DamageCut:25", "HitCap:4" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(9));
            Assert.That(fp, Is.EqualTo(CutExpected), fp);
        }

        // ---------------- 木灵暴击不摇号(附录 §3 末第 7 项,Ruling 18)----------------

        [Test]
        public void SummonCrit_NoCritAura_DoesNotRoll()
        {
            // 没有掉字(UnlockedChars 为空)、玩家不出伤害字、命中率 100、单敌 —— 这几拍里 _random 唯一可能的消费方就是木灵暴击
            var grove = Wood("林", new EffectDef(EffectKind.Summon, 800, summonCount: 3, summonAttack: 30, summonChar: "木"));
            var log = new HookLog();
            var b = Battle(log, new[] { grove }, new[] { RebalanceFixture.Mob(attack: 50) }, playerCrit: false);
            var all = new StringBuilder();
            Do(b, "林", -1, all);
            uint before = b.Capture().RandomState;
            for (int t = 0; t < 5 && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                b.EndTurn();
                // BlockUsed(格挡反击动效,2026-10-15)是纯追加的表现事件,不进恒等指纹 —— 其余事件流仍逐位对照
                foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.BlockUsed && x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                    all.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Crit};");
                AppendState(all, b);
                Assert.That(b.Capture().RandomState, Is.EqualTo(before), $"第 {t} 拍木灵出手摇了 _random");
            }
            string raw = all.ToString();
            Assert.That(raw.Split(new[] { "SummonAttack:" }, StringSplitOptions.None).Length - 1, Is.GreaterThanOrEqualTo(10),
                "场景没走到足够多的木灵出手\n" + raw);
            Assert.That(Hash(raw), Is.EqualTo(SummonCritExpected), raw);
        }

        private const string AuraAttackExpected = "6a8c2b1fe5c28832 10712";
        private const string CutExpected = "c9d0b88acb3e6231 7192";
        private const string SummonCritExpected = "8a820a0f0b24e9ad 2410";
    }
}
