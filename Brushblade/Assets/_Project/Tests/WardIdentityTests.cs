using System;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 3 第一步(附录 W3 / W4 前置):ApplyStatus 是 trace 每局必经 —— 标点小妖给同伴挂 AttackBuff、
    /// 焦痕受击给自己挂 AttackBuff、灯花经 RefreshBurn 给玩家 / 木灵挂灼(含「刷新到不高于现有层数」那一支)、Boss 倾覆给玩家挂封字。
    /// W3 / W4 要在 ApplyStatus 开头加拦截段,袋里没有 BuffBlock / DebuffWard 时必须逐位恒等。
    ///
    /// 指纹 = 每拍事件(种类 / 下标 / 量 / 吸收 / 来源)+ 出字结果 + StatusApplied 钩子序列 + 敌人 / 玩家 / 木灵状态袋
    /// (种类 / 量 / 剩余回合,按袋序)+ 敌我血量 / 护盾 + 三条随机流。期望值是 W3 / W4 改动前(HEAD a9b93663)跑出来的。</summary>
    public class WardIdentityTests
    {
        private static readonly CharDef Striker = new("击", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) });
        private static readonly CharDef Sprout = new("苗", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 1000, summonCount: 1, summonAttack: 0) });
        private static readonly CharDef Guard = new("护", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 60) });

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied)
                    Sb.Append($"A{args.Subject.Side}{args.Subject.Index}<{args.Other.Side}{args.Other.Index}:{args.Status}:{args.Amount};");
            }
        }

        private static EnemyDef Toppler() =>
            new("覆", Element.Heart, 200000, 30, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 200000, 30, skill: BossSkill.Topple) });

        private static BattleEngine Battle(HookLog log)
        {
            var defs = new[] { Striker, Sprout, Guard };
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 50000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60 },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 15)).ToArray(), Array.Empty<string>(),
                new[]
                {
                    RebalanceFixture.Mob(attack: 20, ability: EnemyAbility.Buff),    // 标点小妖:给同伴挂 AttackBuff
                    RebalanceFixture.Mob(attack: 15, ability: EnemyAbility.Scorch),  // 焦痕:受击给自己挂 AttackBuff
                    RebalanceFixture.Mob(attack: 10, ability: EnemyAbility.Sear),    // 灯花:RefreshBurn 给玩家 / 木灵
                    Toppler(),                                                        // 倾覆:给玩家挂封字
                },
                seed: 23);
            // 暴击 40%:让出字伤害摇号,随机流状态才有区分度
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
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

        [Test]
        public void ApplyStatus_NoWardNoBlock_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            var sb = new StringBuilder();
            for (int t = 0; t < 12; t++)
            {
                if (t == 0) Do(b, "苗", -1, log);
                Do(b, "击", 1, log);   // 焦痕受击 → 自燃加攻
                Do(b, "击", 1, log);
                if (t % 3 == 1) Do(b, "护", -1, log);
                sb.Append("|C").Append(b.Ap);
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/{b.ShieldNormal}/{b.ShieldPersist}");
                Bag(sb, b.PlayerStatuses);
                foreach (var s in b.Summons)
                    if (s != null) { sb.Append($",S{s.Hp}/{s.Shield}"); Bag(sb, s.Statuses); }
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
            // 场景确实走到了每一条要守的施加路径(否则对照空转)
            Assert.That(raw.Contains("AEnemy0<Enemy1:AttackBuff") || raw.Contains("AEnemy2<Enemy0:AttackBuff"), Is.True, "标点小妖加攻");
            Assert.That(raw.Contains("AEnemy1<Enemy1:AttackBuff"), Is.True, "焦痕自燃");
            Assert.That(raw.Contains("APlayer-1<Enemy2:Burn"), Is.True, "灯花给玩家上灼");
            Assert.That(raw.Contains("ASummon0<Enemy2:Burn"), Is.True, "灯花给木灵上灼");
            Assert.That(raw.Contains(":Seal:"), Is.True, "倾覆封字");
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            string fp = h.ToString("x16") + " " + raw.Length;
            Assert.That(fp, Is.EqualTo(Expected), fp + "\n" + raw);
        }

        private const string Expected = "cd6122d2ddf1c22e 16581";
    }
}
