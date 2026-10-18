using System;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 2 第一步(附录 G9 前置):BeginPlayerTurn 回合开始段(护盾 / 反震 / 冰晶 / 金气 / 回合脉冲)逐位恒等的对照。
    /// trace 杂食画像从不出土的固面(Shield 事件 0 条),这一段 trace 守不住,只能靠这条对照。
    /// 2026-10-13(护盾回合保留 Task 4):回合清盾(U1)删除,护盾与载体跨回合保留;两条指纹按新口径重取。
    ///
    /// 场景:普通盾 + 反震、第二份护盾、冰晶、金气(战意满)、回合脉冲、木灵群体护盾,三只敌人每轮挥击;
    /// 第二条只有护盾 + 载体,钉住「护盾没被打空时载体跨回合仍在」。
    ///
    /// 指纹 = 每拍事件(种类 / 下标 / 量 / 副下标 / 吸收 / 来源)+ 钩子序列 + 两桶护盾 + 玩家状态袋(种类 / 量 / 剩余回合)
    /// + 木灵护盾 / 血量 + 敌我血量 + 三条随机流。期望值是 G9 改动前(HEAD d8af3773)跑出来的。</summary>
    public class EarthTurnShieldIdentityTests
    {
        private static readonly CharDef Wood = new("木", Element.Wood);

        // 普通盾 + 反震
        private static readonly CharDef Guard = RebalanceFixture.Char("护",
            new EffectDef(EffectKind.Shield, 300), new EffectDef(EffectKind.ShieldRecoil, 30));
        // 第二张普通盾(字名「留」沿用,不动指纹)
        private static readonly CharDef Keep = RebalanceFixture.Char("留",
            new EffectDef(EffectKind.Shield, 200));
        // 冰晶(治疗转盾后挂)
        private static readonly CharDef Crystal = new("冰", Element.Water, effects: new[]
        {
            new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.ShieldFromHeal, 100),
            new EffectDef(EffectKind.ShieldFrost, 1),
        });
        // 金气 + 战意补满:每个回合开始加盾
        private static readonly CharDef Aura = RebalanceFixture.Char("气",
            new EffectDef(EffectKind.Morale, 5), new EffectDef(EffectKind.MoraleShield, 40));
        // 回合脉冲:回合开始加盾
        private static readonly CharDef Pulse = new("淋", Element.Water, effects: new[]
        {
            new EffectDef(EffectKind.TurnPulse, 0, turns: 3, perHit: new[] { new EffectDef(EffectKind.Shield, 25) }),
        });
        // 木灵 + 群体护盾
        private static readonly CharDef Grove = RebalanceFixture.Char("林",
            new EffectDef(EffectKind.Summon, 800, summonCount: 1, summonAttack: 20, summonChar: "木"));
        private static readonly CharDef Wall = RebalanceFixture.Char("墙", new EffectDef(EffectKind.ShieldAll, 60));
        // 出字伤害摇暴击
        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static BattleEngine Battle(HookLog log)
        {
            var defs = new[] { Wood, Guard, Keep, Crystal, Aura, Pulse, Grove, Wall, Hit };
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 200 },
                defs.Skip(1).SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                new[] { RebalanceFixture.Mob(attack: 60), RebalanceFixture.Mob(attack: 35), RebalanceFixture.Boss(attack: 90) },
                seed: 7, startingHp: 3000);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Do(BattleEngine b, string id, int target, StringBuilder sb) =>
            sb.Append($"#{id}{target}={b.Cast(id, target)};");

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int, StringBuilder> eachTurn, int turns)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns; t++)
            {
                eachTurn(b, t, sb);
                sb.Append($"|C{b.Ap}/S{b.ShieldNormal}+0");
                b.EndTurn();
                foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0[");
                foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                sb.Append(']');
                foreach (var s in b.Summons)
                    if (s != null) sb.Append($",W{s.Hp}/{s.Shield}");
                for (int i = 0; i < b.Enemies.Count; i++)
                    sb.Append($",E{b.Enemies[i].Hp}/{b.Enemies[i].ActionMeter}");
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        /// <summary>护盾 + 全部载体:护盾跨回合累加,金气与回合脉冲在回合开始加盾。</summary>
        [Test]
        public void BothBuckets_WithCarriers_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            bool sawNormalAtEnd = false;
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) { Do(x, "林", -1, sb); Do(x, "气", -1, sb); Do(x, "淋", -1, sb); }
                if (t % 3 != 2) Do(x, "留", -1, sb);
                Do(x, "护", -1, sb);
                Do(x, "冰", -1, sb);
                Do(x, "墙", -1, sb);
                Do(x, "击", t % 3, sb);
                if (x.ShieldNormal > 0) sawNormalAtEnd = true;
            }, 7);
            Assert.That(sawNormalAtEnd, Is.True, "场景确实带着护盾进下一回合(否则对照空转)");
            Assert.That(fp, Is.EqualTo(BothExpected), fp);
        }

        /// <summary>只有护盾 + 载体(无金气 / 脉冲补盾):护盾没被打空时,反震 / 冰晶跨回合保留(2026-10-13 取消 U1)。</summary>
        [Test]
        public void NormalOnly_CarriersKept_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            // 防空转:上回合末带着载体与护盾进敌方段,敌方挥击没把盾打空,
            // 本回合开始护盾仍在、载体也仍在 —— 回合开始不再清盾。
            bool prevCarriers = false;
            int prevNormal = 0;
            bool sawCarriersKept = false;
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t > 0 && prevCarriers)
                {
                    int absorbed = x.LastEvents.Where(e => e.Kind == BattleEventKind.EnemyAttack).Sum(e => e.Absorbed);
                    if (prevNormal - absorbed > 0 && x.PlayerShield == prevNormal - absorbed
                        && x.PlayerStatuses.Has(StatusKind.ShieldRecoil) && x.PlayerStatuses.Has(StatusKind.ShieldFrost))
                        sawCarriersKept = true;
                }
                if (t % 2 == 0) { Do(x, "护", -1, sb); Do(x, "冰", -1, sb); }
                Do(x, "击", t % 3, sb);
                prevCarriers = x.PlayerStatuses.Has(StatusKind.ShieldRecoil) || x.PlayerStatuses.Has(StatusKind.ShieldFrost);
                prevNormal = x.ShieldNormal;
            }, 6);
            Assert.That(sawCarriersKept, Is.True, "场景确实走到「护盾没打空、载体跨回合保留」(否则对照空转)");
            Assert.That(fp, Is.EqualTo(NormalOnlyExpected), fp);
        }

        // 2026-10-13(护盾回合保留 Task 2):夹具里的 persistOnce 护盾随留存桶取消改为普通护盾,指纹重取;
        // 同批不带 persistOnce 的指纹(本文件其余常量与其他 *IdentityTests)逐位未变。
        // 2026-10-13(同 plan Task 4):删回合清盾,两条指纹重取。
        private const string BothExpected = "987867169f66d64b 7102";
        private const string NormalOnlyExpected = "699d659d29a59d42 4673";
    }
}
