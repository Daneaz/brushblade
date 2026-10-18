using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 4 第一步:木灵编队与敌人侧(M7 攀附、M8 绿潮、M9 四木成林、M10 折桂、M11 水草)要插桩的
    /// 共享路径,在**无新状态**时逐位恒等:
    /// <list type="bullet">
    /// <item>ActEnemyTurn 种之后段(攀附的落点):种治疗、冻结跳过、魅惑反打、Boss 吞噬、木灵挨打阵亡(OnSummonDeath)、
    /// 缠绕本命(OnSummonFreeze / OnHitFreezeChance)的冻结摇号;</item>
    /// <item>CastCore 出字收尾(水草的落点)与击杀时反应(折桂的落点):HealSelf / HealAll / HealSummons / 单体治木灵 /
    /// 归根 / 桂香光环 / 丛生小藻 / 顶替召唤,攻面击杀时特性(battleLimit,E49 入队记次)。</item>
    /// </list>
    /// 指纹写法同 <see cref="WoodHitDeathIdentityTests"/>,另记字库与特性次数表。期望值在改动前(HEAD 2f68fa89,
    /// D2-木 Task 3 完成态)跑出。</summary>
    public class WoodRosterIdentityTests
    {
        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Other.Side}{args.Other.Index}:{args.Status}:{args.Amount}:{args.Source};");
        }

        private static BattleEngine Battle(HookLog log, CharDef[] defs, EnemyDef[] enemies, BattleConfig config = null,
            int seed = 41, IReadOnlyDictionary<string, int> levels = null)
        {
            config ??= new BattleConfig();
            config.PlayerMaxHp = 6000;
            config.PlayerAttack = 100;
            config.ApPerTurn = 200;
            config.LibraryCapacity = 300;
            config.BossPhaseJitterPercent = 0;
            var b = new BattleEngine(RebalanceFixture.Graph(defs), config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), new[] { "木", "水" },
                enemies, seed: seed, startingHp: 2500, cardLevels: levels);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, StringBuilder sb, bool attack = false,
            int allySlot = Targeting.PlayerTarget, bool replace = false)
        {
            sb.Append($"#{id}{target}/{allySlot}{(attack ? "A" : "")}{(replace ? "R" : "")}=");
            sb.Append(b.Cast(id, target, replaceSummon: replace, attackMode: attack, allySlot: allySlot)).Append(';');
            AppendEvents(sb, b);
        }

        private static void AppendEvents(StringBuilder sb, BattleEngine b)
        {
            foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source}:{e.Crit};");
        }

        private static void AppendState(StringBuilder sb, BattleEngine b)
        {
            sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0[");
            foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft}:{s.TargetSlot} ");
            sb.Append(']');
            int i = 0;
            foreach (var s in b.Summons)
            {
                i++;
                if (s == null) continue;
                sb.Append($",W{i - 1}:{s.Char}:{s.SourceChar}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.Shield}/{s.EffectiveAttack}/{s.SproutParentSlot}/{s.ActionMeter}[");
                foreach (var st in s.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft}:{st.SourceId} ");
                sb.Append(']');
            }
            for (int e = 0; e < b.Enemies.Count; e++)
            {
                var en = b.Enemies[e];
                sb.Append($",E{en.Hp}/{en.ActionMeter}[");
                foreach (var st in en.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft}:{st.TargetSlot} ");
                sb.Append(']');
            }
            sb.Append("|Pool").Append(string.Join("", b.Pool));
            sb.Append("|Lib").Append(b.Library.Count).Append(string.Join("", b.Library.Distinct().OrderBy(x => x, StringComparer.Ordinal)));
            var snap = b.Capture();
            sb.Append("|U");
            foreach (var kv in snap.TraitUsesThisBattle.OrderBy(kv => kv.Key, StringComparer.Ordinal)) sb.Append($"{kv.Key}={kv.Value} ");
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
                AppendEvents(sb, b);
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

        private static CharDef Wood(string id, EffectDef[] feature, EffectDef[] attack = null, TraitDef[] traits = null) =>
            new(id, Element.Wood, effects: feature,
                attackEffects: attack ?? new[] { new EffectDef(EffectKind.DamageSingle, 40) }, traits: traits);

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        // ---------------- 第 1 项:ActEnemyTurn 种之后段(攀附落点)----------------

        [Test]
        public void EnemyTurnAfterSeeds_NoNewState_Identical()
        {
            // 藤:入场冻结 + 出手冻结摇号(缠绕本命);藻:丛生小藻;种 / 冻 / 花:种治疗、冻结跳过、魅惑反打
            var vine = Wood("藤", new[] { new EffectDef(EffectKind.Summon, 700, summonAttack: 40, summonChar: "藤",
                passive: new SummonPassive { OnSummonFreeze = 1, OnHitFreezeChance = 30, OnHitFreezeTurns = 1 }) });
            var algae = Wood("藻", new[] { new EffectDef(EffectKind.Summon, 400, summonAttack: 25, summonChar: "藻",
                passive: new SummonPassive { SproutPercent = 40, SproutMax = 2 }) });
            var seed = RebalanceFixture.Char("种", new EffectDef(EffectKind.Seed, 60, turns: 3));
            var frost = RebalanceFixture.Char("冻", new EffectDef(EffectKind.DamageSingle, 20), new EffectDef(EffectKind.Freeze, 1));
            var flower = RebalanceFixture.Char("花", new EffectDef(EffectKind.Charm, 0, turns: 1));
            var boss = new EnemyDef("噬", Element.Metal, 100000, 260,
                phases: new[] { new BossPhaseDef("噬", Element.Metal, 100000, 260, skill: BossSkill.Devour) });
            var log = new HookLog();
            var b = Battle(log, new[] { vine, algae, seed, frost, flower, Hit }, new[]
            {
                boss,
                new EnemyDef("远", Element.Fire, 100000, 150, range: AttackRange.Ranged),
                new EnemyDef("壮", Element.Heart, 9000, 200),
            }, new BattleConfig { SummonDeathHealPercent = 10 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0 || t == 5) Do(x, "藤", -1, sb, replace: x.Summons.Count(s => s != null && s.Alive) >= 6);
                if (t == 1 || t == 6) Do(x, "藻", -1, sb, replace: x.Summons.Count(s => s != null && s.Alive) >= 6);
                if (t % 2 == 0) Do(x, "种", t % 3, sb);
                if (t % 3 == 1) Do(x, "冻", 1, sb);
                if (t % 4 == 2) Do(x, "花", 2, sb);
                Do(x, "击", t % 3, sb);
            }, 12, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "Seed:", "Freeze:", "CharmedAttack:", "SummonHit:", "SummonDied:Summon", "Heal:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Contains($"BossSkillCast:0:{(int)BossSkill.Devour}:"), Is.True, "Boss 没放吞噬\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(12));
            Assert.That(fp, Is.EqualTo(EnemyTurnExpected), fp);
        }

        // ---------------- 第 2 项:CastCore 出字收尾(水草落点)+ 击杀时反应(折桂落点)----------------

        [Test]
        public void CastEndHealsAndKillReactions_NoNewState_Identical()
        {
            var laurelTrait = new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Passive, null, "折",
                new[] { new EffectDef(EffectKind.Morale, 1) }, trigger: TraitTrigger.OnKill, maxPerBattle: 2);
            var laurel = Wood("桂", new[] { new EffectDef(EffectKind.Summon, 500, summonAttack: 20, summonChar: "桂",
                    passive: new SummonPassive { HealAlly = 40 }) },
                new[] { new EffectDef(EffectKind.DamageSingle, 120, shape: TargetArea.All) }, new[] { laurelTrait });
            var algae = Wood("藻", new[] { new EffectDef(EffectKind.Summon, 400, summonAttack: 30, summonChar: "藻",
                passive: new SummonPassive { SproutPercent = 40, SproutMax = 2, Regen = 15 }) });
            var spring = RebalanceFixture.Char("泉", new EffectDef(EffectKind.HealSelf, 300));
            var rain = RebalanceFixture.Char("雨", new EffectDef(EffectKind.HealAll, 120));
            var tend = RebalanceFixture.Char("润", new EffectDef(EffectKind.HealSummons, 90));
            var mend = RebalanceFixture.Char("补", new EffectDef(EffectKind.HealSelf, 200));   // 单体:allySlot 点木灵
            var log = new HookLog();
            var b = Battle(log, new[] { laurel, algae, spring, rain, tend, mend, Hit }, new[]
            {
                new EnemyDef("壮", Element.Heart, 100000, 240),
                new EnemyDef("弱", Element.Heart, 300, 120),
                new EnemyDef("远", Element.Metal, 100000, 180, range: AttackRange.Ranged),
                new EnemyDef("弱", Element.Heart, 500, 90),
            }, new BattleConfig { SummonDeathHealPercent = 20, UnlockedSummonSlots = 0b1111 },
                levels: new Dictionary<string, int> { ["桂"] = 5 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                int alive = x.Summons.Count(s => s != null && s.Alive);
                if (t == 0) Do(x, "藻", -1, sb);
                if (t == 1 || t == 6) Do(x, "桂", -1, sb, replace: alive >= 4);
                if (t == 4 || t == 8) Do(x, "藻", -1, sb, replace: alive >= 4);
                if (t == 2 || t == 7) Do(x, "桂", 0, sb, attack: true);
                Do(x, t % 2 == 0 ? "泉" : "雨", -1, sb);
                if (t % 3 == 0) Do(x, "润", -1, sb);
                int wounded = Array.FindIndex(x.Summons.ToArray(), s => s != null && s.Alive && s.Hp < s.MaxHp);
                if (wounded >= 0) Do(x, "补", -1, sb, allySlot: wounded);
                Do(x, "击", t % 3 == 2 ? 2 : 0, sb);
            }, 12, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "Heal:", "SummonDied:Summon", ":SummonDeathHeal:", "EnemyDied:", "场次:桂/5/Attack=" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Contains(":苗:藻:"), Is.True, "场景没有丛生小藻\n" + raw);
            Assert.That(raw.Contains("R=None;"), Is.True, "场景没走到顶替召唤\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(12));
            Assert.That(fp, Is.EqualTo(CastEndExpected), fp);
        }

        // 2026-10-18 战场改 3×2(敌每排 3 列、召唤槽 6、前排 3 格)后重录,原值 "4b49c5b2b25a84a0 23247"
        private const string EnemyTurnExpected = "6dc4dfee7251b217 22742";
        // 2026-10-18 战场改 3×2(敌每排 3 列、召唤槽 6、前排 3 格)后重录,原值 "bc633e03db115a97 24117"
        private const string CastEndExpected = "86e472227e222f56 25267";
    }
}
