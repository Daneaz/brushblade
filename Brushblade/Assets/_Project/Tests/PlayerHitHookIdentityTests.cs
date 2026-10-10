using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 4 第一步(附录 W5 / W6 / W7 前置):DamagePlayerDirect(每次敌人挥击)与 BeginPlayerTurn(每回合)
    /// 是 trace 每局必经。W5 受击回复、W6 冰晶、W7 回合脉冲都要挂在这两处,袋里没有对应状态时必须逐位恒等。
    ///
    /// 场景覆盖:普通挥击打护盾(两桶,含打空)、致盲摇号打空、镜反弹、减伤、反震(打空即移除)、受击回敬入队、
    /// 铁画反噬(allowReflect = false)、灯花上灼、Boss 倾覆清盾、回合初清盾 + 金气加盾。
    /// 指纹 = 每拍事件 + 出字结果 + 钩子(PlayerHit / StatusApplied)+ 玩家 / 敌人状态袋 + 血量 / 两桶护盾 + 三条随机流。
    /// 期望值是 W5–W7 改动前(HEAD 81b708c4)跑出来的。
    ///
    /// 拆成两份(Task 4 fix round 1,Ruling 12):受击回敬(Retaliate)改为在攻击者这一拍收尾兑现之后,
    /// 回敬挂的 turns 型减益不再被同拍 TickTurns 吃掉一回合 —— 带回敬的那份按 Ruling 12 重建基线;
    /// 不带回敬的那份期望值仍取自 81b708c4 的导出,守住其余路径逐位不变。</summary>
    public class PlayerHitHookIdentityTests
    {
        private static readonly CharDef Striker = new("击", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) });
        private static readonly CharDef Guard = new("护", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 200) });
        private static readonly CharDef Bulwark = new("垒", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 40, persistOnce: true) });

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied)
                    Sb.Append($"A{args.Subject.Side}{args.Subject.Index}<{args.Other.Side}{args.Other.Index}:{args.Status}:{args.Amount};");
                else if (args.Kind == HookKind.PlayerHit)
                    Sb.Append($"H{args.Other.Index}:{args.Amount}:{args.Absorbed}:{args.Source}:{args.Depth};");
            }
        }

        private static EnemyDef Toppler() =>
            new("覆", Element.Heart, 200000, 30, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 200000, 30, skill: BossSkill.Topple) });

        private static BattleEngine Battle(HookLog log, bool withRetaliate)
        {
            var defs = new[] { Striker, Guard, Bulwark };
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 50000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 15)).ToArray(), Array.Empty<string>(),
                new[]
                {
                    RebalanceFixture.Mob(attack: 25),
                    RebalanceFixture.Mob(attack: 15, ability: EnemyAbility.Barb),   // 铁画:反噬走 allowReflect = false
                    RebalanceFixture.Mob(attack: 12, ability: EnemyAbility.Sear),   // 灯花:挥击 + 上灼
                    Toppler(),                                                       // 倾覆:挥击后清盾
                },
                seed: 31);
            var p = b.PlayerStatuses;
            p.Apply(new StatusEffect { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            p.Apply(new StatusEffect { Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff, Magnitude = 20, TurnsLeft = -1 });
            p.Apply(new StatusEffect { Kind = StatusKind.DamageCut, Polarity = StatusPolarity.Buff, Magnitude = 10, TurnsLeft = -1 });
            p.Apply(new StatusEffect { Kind = StatusKind.ShieldRecoil, Polarity = StatusPolarity.Buff, Magnitude = 50, TurnsLeft = -1, SourceId = "护" });
            if (withRetaliate) p.Apply(new StatusEffect
            {
                Kind = StatusKind.Retaliate, Polarity = StatusPolarity.Buff, Magnitude = 2, TurnsLeft = -1, SourceId = "护",
                OnHit = new List<OpeningEffect>
                    { new OpeningEffect { SourceCharId = "护", Element = Element.Earth, Kind = EffectKind.Weaken, Value = 10, Turns = 1 } },
            });
            // 金气:战意 ≥ 上限 → 每回合清盾之后加盾
            p.Apply(new StatusEffect { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = 99, TurnsLeft = -1 });
            p.Apply(new StatusEffect { Kind = StatusKind.MoraleShield, Polarity = StatusPolarity.Buff, Magnitude = 30, TurnsLeft = -1 });
            // 致盲让命中判定摇号(打空那一支)
            b.Enemies[0].Statuses.Apply(new StatusEffect { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 30, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, HookLog log) =>
            log.Sb.Append($"#{id}{target}={b.Cast(id, target)};");

        private static void Bag(StringBuilder sb, StatusBag bag)
        {
            sb.Append('[');
            foreach (var s in bag.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft}:{s.Polarity} ");
            sb.Append(']');
        }

        /// <summary>不带受击回敬:期望值取自 81b708c4 的导出,Ruling 12 前后都不变。</summary>
        [Test]
        public void PlayerHitAndTurnStart_NoNewStatuses_Identical() =>
            Assert.That(Fingerprint(withRetaliate: false), Is.EqualTo(ExpectedNoRetaliate));

        /// <summary>带受击回敬(Weaken 10 turns 1)。Ruling 12 重建基线:回敬改在攻击者拍尾兑现,减攻不再当拍到期。</summary>
        [Test]
        public void PlayerHitAndTurnStart_WithRetaliate_Baseline() =>
            Assert.That(Fingerprint(withRetaliate: true), Is.EqualTo(ExpectedWithRetaliate));

        private static string Fingerprint(bool withRetaliate)
        {
            var log = new HookLog();
            var b = Battle(log, withRetaliate);
            var sb = new StringBuilder();
            for (int t = 0; t < 12; t++)
            {
                Do(b, "击", 1, log);   // 铁画受击 → 反噬
                if (t % 4 != 3) Do(b, "护", -1, log);
                if (t % 3 == 1) Do(b, "垒", -1, log);
                sb.Append("|C").Append(b.Ap);
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/{b.ShieldNormal}/{b.ShieldPersist}");
                Bag(sb, b.PlayerStatuses);
                for (int i = 0; i < b.Enemies.Count; i++)
                {
                    var en = b.Enemies[i];
                    sb.Append($",E{en.Hp}/{en.ActionMeter}");
                    Bag(sb, en.Statuses);
                }
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            // 场景确实走到了每一条要守的路径(否则对照空转)
            Assert.That(raw.Contains("Missed:0"), Is.True, "致盲打空");
            Assert.That(raw.Contains(":IronBarb:"), Is.True, "铁画反噬");
            Assert.That(raw.Contains(":Reflect;"), Is.True, "镜反弹");
            Assert.That(raw.Contains(":ShieldRecoil;"), Is.True, "反震");
            Assert.That(raw.Contains("ShieldBroken"), Is.True, "倾覆清盾");
            if (withRetaliate) Assert.That(raw.Contains(":Curse:"), Is.True, "受击回敬落到攻击者");
            Assert.That(raw.Contains("APlayer-1<Enemy2:Burn"), Is.True, "灯花上灼");
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            string fp = h.ToString("x16") + " " + raw.Length;
            TestContext.WriteLine(fp);
            return fp;
        }

        private const string ExpectedNoRetaliate = "d4b67847f51d3143 9643";
        private const string ExpectedWithRetaliate = "a7f9faceb52ee1f7 10935";
    }
}
