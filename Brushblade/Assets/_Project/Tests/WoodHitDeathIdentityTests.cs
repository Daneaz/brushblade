using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 3 第一步:受击 / 阵亡 / 守护 / 标记(M3 秋实·落木·藻荇、M4 披荆·良弓、M5 守护、M6 荆刺)
    /// 要插桩的共享路径,在**无新字段**时逐位恒等。附录 §3 末「trace 不守」第 1、2 项:
    /// <list type="bullet">
    /// <item>荆灵挨打:DamageSummon 荆棘 → 反弹 → 格挡反击全段;荆灵死后玩家直接挨打(DamagePlayerDirect 镜 / 格挡反击 /
    /// 护盾 / 反震全段),M4 / M5 / M6 的落点;</item>
    /// <item>柘灵(嘲讽 + 坚木)挨打与 Boss 吞噬,M3 / M4 / M5 的落点;</item>
    /// <item>木灵阵亡的各条路径(挨打、灼烧、吞噬、小藻阵亡、归根治疗)+ 顶替召唤不算阵亡,M3 的落点。</item>
    /// </list>
    /// 指纹写法同 <see cref="WoodSharedIdentityTests"/> / <see cref="WoodAuraIdentityTests"/>:出字结果与事件(含 Source)、
    /// 每拍事件、钩子序列(含 Other)、玩家血 / 两桶护盾 / 状态袋、木灵血 / 盾 / 状态袋 / 小藻母体、敌人血 / 行动条 / 状态袋、
    /// 部件池、三条 RandomState。期望值在改动前(HEAD 1cf4c25f,D2-木 Task 2 完成态)跑出。</summary>
    public class WoodHitDeathIdentityTests
    {
        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Other.Side}{args.Other.Index}:{args.Status}:{args.Amount}:{args.Source};");
        }

        private static BattleEngine Battle(HookLog log, CharDef[] defs, EnemyDef[] enemies, BattleConfig config = null,
            int seed = 37)
        {
            config ??= new BattleConfig();
            config.PlayerMaxHp = 6000;
            config.PlayerAttack = 100;
            config.ApPerTurn = 200;
            config.LibraryCapacity = 300;
            config.BossPhaseJitterPercent = 0;
            var b = new BattleEngine(RebalanceFixture.Graph(defs), config,
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), new[] { "木", "火" },
                enemies, seed: seed, startingHp: 3000);
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
            foreach (var e in b.LastEvents)
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
                sb.Append($",W{i - 1}:{s.Char}:{s.SourceChar}:{s.Hp}/{s.MaxHp}/{s.Attack}/{s.Shield}/{s.EffectiveAttack}/{s.SproutParentSlot}[");
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
            sb.Append("|Pool").Append(string.Join("", b.Pool));
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

        private static CharDef Wood(string id, params EffectDef[] effects) => new(id, Element.Wood, effects: effects,
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) });

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        // ---------------- 第 1 项:荆灵挨打全段 + 荆灵死后玩家直接挨打(M4 披荆 / 良弓、M5 守护、M6 荆刺的落点)----------------

        [Test]
        public void ThornSummonThenPlayerHit_NoNewState_Identical()
        {
            var thorn = Wood("荆", new EffectDef(EffectKind.Summon, 1400, summonAttack: 10, summonChar: "木",
                passive: new SummonPassive { Thorns = 40, Taunt = true }));
            var mirror = RebalanceFixture.Char("镜", new EffectDef(EffectKind.Reflect, 20, turns: 2));
            var parry = new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Block, 2), new EffectDef(EffectKind.CounterBoost, 50) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 200) });
            var guard = RebalanceFixture.Char("盾", new EffectDef(EffectKind.Shield, 300), new EffectDef(EffectKind.ShieldRecoil, 50));
            var log = new HookLog();
            var b = Battle(log, new[] { thorn, mirror, parry, guard, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 260),
                new EnemyDef("远", Element.Heart, 100000, 140, range: AttackRange.Ranged),
                new EnemyDef("壮", Element.Metal, 100000, 180),
            });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "荆", -1, sb);
                if (t % 3 == 0) Do(x, "镜", -1, sb);
                if (t % 2 == 1) Do(x, "挡", -1, sb, allySlot: t < 4 ? 0 : Targeting.PlayerTarget);
                if (t >= 3) Do(x, "盾", -1, sb);
                Do(x, "击", t % 3, sb);
            }, 10, all);
            string raw = all.ToString();
            foreach (var needle in new[] { ":Thorns:", ":Reflect:", ":BlockCounter:", ":ShieldRecoil:", "SummonHit:", "EnemyAttack:",
                         "SummonDied:Summon0:Enemy" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(10));
            Assert.That(fp, Is.EqualTo(ThornExpected), fp);
        }

        // ---------------- 第 2 项:柘灵(嘲讽 + 坚木)挨打与 Boss 吞噬(M3 / M4 / M5 的落点)----------------

        [Test]
        public void ArmorTauntSummonAndDevour_NoNewState_Identical()
        {
            var zhe = Wood("柘", new EffectDef(EffectKind.Summon, 900, summonAttack: 20, summonChar: "柘",
                summonDefense: 30, passive: new SummonPassive { Taunt = true, Armor = 40 }));
            var grove = Wood("林", new EffectDef(EffectKind.Summon, 300, summonCount: 2, summonAttack: 15, summonChar: "木"));
            var boss = new EnemyDef("噬", Element.Metal, 100000, 220,
                phases: new[] { new BossPhaseDef("噬", Element.Metal, 100000, 220, skill: BossSkill.Devour) });
            var log = new HookLog();
            var b = Battle(log, new[] { zhe, grove, Hit }, new[]
            {
                boss,
                new EnemyDef("远", Element.Fire, 100000, 120, range: AttackRange.Ranged),
            }, new BattleConfig { SummonDeathHealPercent = 20 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "柘", -1, sb);
                if (t == 1 || t == 5) Do(x, "林", -1, sb);
                if (t == 4) Do(x, "柘", -1, sb);
                Do(x, "击", t % 2, sb);
            }, 10, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "BossSkillCast:", "SummonHit:", "SummonDied:Summon", ":SummonDeathHeal:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Contains($"BossSkillCast:0:{(int)BossSkill.Devour}:"), Is.True, "Boss 没放吞噬\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(10));
            Assert.That(fp, Is.EqualTo(DevourExpected), fp);
        }

        // ---------------- 第 3 项:木灵阵亡的各条路径(M3 秋实 / 落木 / 藻荇的落点)+ 顶替不算阵亡 ----------------

        [Test]
        public void SummonDeathPaths_NoNewState_Identical()
        {
            // 藻:丛生分裂小藻(SproutParentSlot);只开 3 格,召满后顶替;每隔几拍给木灵点灼烧,走自焚阵亡
            var algae = Wood("藻", new EffectDef(EffectKind.Summon, 400, summonAttack: 30, summonChar: "藻",
                passive: new SummonPassive { SproutPercent = 40, SproutMax = 2 }));
            var giant = Wood("𣛧", new EffectDef(EffectKind.Summon, 500, summonAttack: 60, summonChar: "木",
                passive: new SummonPassive { AuraAttack = 10 }));
            var reed = Wood("芦", new EffectDef(EffectKind.Summon, 250, summonAttack: 10, summonChar: "木"));
            var log = new HookLog();
            var b = Battle(log, new[] { algae, giant, reed, Hit }, new[]
            {
                RebalanceFixture.Mob(attack: 170),
                new EnemyDef("远", Element.Metal, 100000, 130, range: AttackRange.Ranged),
                new EnemyDef("壮", Element.Heart, 100000, 150),
            }, new BattleConfig { SummonDeathHealPercent = 15, UnlockedSummonSlots = 0b111 });
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "藻", -1, sb);
                if (t == 1) Do(x, "𣛧", -1, sb);
                if (t == 3 || t == 6) Do(x, "𣛧", -1, sb, replace: x.Summons.Count(s => s != null && s.Alive) >= 3);
                if (t == 4) Do(x, "藻", -1, sb, replace: x.Summons.Count(s => s != null && s.Alive) >= 3);
                if (t == 2 || t == 7)
                {
                    // 召满后顶替(顶替不走阵亡,Q15)
                    for (int n = 0; n < 4 && x.Summons.Count(s => s != null && s.Alive) < 3; n++) Do(x, "芦", -1, sb);
                    Do(x, "𣛧", -1, sb, replace: true);
                }
                if (t % 3 == 2)
                    foreach (var s in x.Summons)
                        if (s != null && s.Alive)
                            s.Statuses.Apply(new StatusEffect
                                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 6, TurnsLeft = -1 });
                Do(x, "击", t % 3, sb);
            }, 10, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "SummonDied:Summon", "SummonBurnTick:", ":SummonDeathHeal:", ":None-1:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}\n{raw}");
            Assert.That(raw.Contains("R=None;"), Is.True, "场景没走到顶替召唤\n" + raw);
            // 防空转(Task 3 review Minor 3):藻荇 / 落木的落点 —— 带母体记号的小藻阵亡、𣛧 的木灵阵亡(尸体血 0)
            Assert.That(Regex.IsMatch(raw, @",W\d:苗:藻:0/\d+/\d+/\d+/\d+/[0-9]\["), Is.True, "场景没走到「带母体记号的小藻阵亡」\n" + raw);
            Assert.That(Regex.IsMatch(raw, @",W\d:木:" + Regex.Escape(giant.Id) + ":0/"), Is.True, "场景没走到「𣛧 阵亡」\n" + raw);
            Assert.That(raw.Split(new[] { "SummonDied:" }, StringSplitOptions.None).Length - 1, Is.GreaterThanOrEqualTo(4),
                "阵亡次数不够\n" + raw);
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(10));
            Assert.That(fp, Is.EqualTo(DeathExpected), fp);
        }

        private const string ThornExpected = "5187440cccd34b26 14090";
        private const string DevourExpected = "f0372edb4512881d 15062";
        private const string DeathExpected = "55783f4ee8db30f9 16607";
    }
}
