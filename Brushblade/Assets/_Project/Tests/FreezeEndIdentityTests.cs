using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 2 第一步(附录 W1 前置):冻结的两个结束点 —— ActEnemyTurn 冻结分支 tick 后的自然到期、
    /// BattleEngine.Fire 的 ThawOn —— 在**没有冻结附着**时逐位恒等的对照。W1 要把两处收拢进 OnFreezeEnd,trace 里冰 / 淼 Lv1
    /// 每局都走这两处,霜抗挂载时点一点都不能动。
    ///
    /// 指纹 = 每拍事件(种类 / 下标 / 量 / 吸收 / 来源)+ StatusApplied 钩子序列(受方 / 状态 / 量)+ 每名敌人的状态袋
    /// (种类 / 量 / 剩余回合,按袋内顺序)+ 敌我血量 + 三条随机流。期望值是 W1 改动前(HEAD 32e71780)跑出来的。</summary>
    public class FreezeEndIdentityTests
    {
        private static readonly CharDef Freezer = new("冻", Element.Water,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 30), new EffectDef(EffectKind.Freeze, 2) });
        private static readonly CharDef Slower = new("缓", Element.Water,
            effects: new[] { new EffectDef(EffectKind.Slow, 2, pick: EffectPick.All) });
        private static readonly CharDef Thawer = new("融", Element.Fire,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 20), new EffectDef(EffectKind.Thaw, 0, pick: EffectPick.All) });

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied)
                    Sb.Append($"A{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
            }
        }

        private static BattleEngine Battle(HookLog log)
        {
            var defs = new[] { Freezer, Slower, Thawer };
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 15)).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 40), RebalanceFixture.Mob(attack: 25), RebalanceFixture.Boss(attack: 50) },
                seed: 11);
            // 暴击 40%:让出字伤害摇号,随机流状态才有区分度
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, HookLog log) =>
            log.Sb.Append($"#{id}{target}={b.Cast(id, target)};");

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int> eachTurn, int turns)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns; t++)
            {
                eachTurn(b, t);
                sb.Append("|C").Append(b.Ap);
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}");
                for (int i = 0; i < b.Enemies.Count; i++)
                {
                    var en = b.Enemies[i];
                    sb.Append($",E{en.Hp}/{en.ActionMeter}[");
                    foreach (var s in en.Statuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                    sb.Append(']');
                }
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            Assert.That(raw.Contains(":FrostResist:"), Is.True, "场景确实走到了霜抗挂载(否则对照空转)");
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        /// <summary>冻结自然到期(杂兵,冻 2 → 霜抗 2)+ Boss 冰滞到期 + 减速并存。</summary>
        [Test]
        public void NaturalExpiry_NoRider_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            string fp = Fingerprint(b, log, (x, t) =>
            {
                Do(x, "冻", 0, log);
                Do(x, "冻", 2, log);
                if (t % 2 == 1) Do(x, "缓", -1, log);
            }, 7);
            Assert.That(fp, Is.EqualTo(NaturalExpected), fp);
        }

        /// <summary>被解冻(ThawOn:冻结 → 等长霜抗、减速移除、冰滞 → 霜抗 N+1)。</summary>
        [Test]
        public void Thaw_NoRider_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            string fp = Fingerprint(b, log, (x, t) =>
            {
                if (t % 2 == 0)
                {
                    Do(x, "冻", 0, log);
                    Do(x, "冻", 1, log);
                    Do(x, "冻", 2, log);
                }
                else
                {
                    Do(x, "缓", -1, log);
                    Do(x, "融", 1, log);
                }
            }, 7);
            Assert.That(fp, Is.EqualTo(ThawExpected), fp);
        }

        private const string NaturalExpected = "6d3a2acf5c7d469b 2382";
        private const string ThawExpected = "edf04b566ea2ec5c 2575";
    }
}
