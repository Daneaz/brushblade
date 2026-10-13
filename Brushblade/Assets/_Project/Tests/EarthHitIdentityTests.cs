using System;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 3 第一步:DamagePlayerDirect / DamageSummon 在**没有 HitCap / HurtShield / ShieldShatter**时
    /// 逐位恒等的对照。G2 / G3 / G4 要往这两个承伤函数里插三个挂点,与镜 → 格挡 → 反震 → 冰晶 → 栉风沐雨的顺序交错,
    /// 两份 60% 预算(减伤 / 反伤)也在这里算;trace 杂食画像玩家护盾恒 0,护盾吸收 / 反震 / 冰晶这一段 trace 守不住。
    ///
    /// 玩家侧:护甲 + 格挡 + 减伤 + 镜 + 免疫 + 两桶护盾 + 反震 + 冰晶 + 栉风沐雨,闪避 15(命中判定摇号),
    /// 杂兵近战 / 远程 + 倾覆 Boss(清盾、AP −1)。
    /// 木灵侧:两只木灵(一只带荆棘)+ 木灵格挡 + 群体护盾 + 玩家减伤 / 镜,杂兵近战 / 远程 + 淹没 Boss(玩家 + 全部木灵)。
    ///
    /// 指纹 = 每拍事件(种类 / 下标 / 量 / 副下标 / 吸收 / 来源)+ 钩子序列 + 两桶护盾 + 玩家状态袋 + 木灵血量 / 护盾 / 状态袋
    /// + 敌我血量 + 三条随机流。期望值是 G2 / G3 / G4 改动前(HEAD c5ea7258)跑出来的。</summary>
    public class EarthHitIdentityTests
    {
        private static readonly CharDef Wood = new("木", Element.Wood);

        private static readonly CharDef Guard = RebalanceFixture.Char("护",
            new EffectDef(EffectKind.Shield, 300), new EffectDef(EffectKind.ShieldRecoil, 30));
        private static readonly CharDef Keep = RebalanceFixture.Char("留",
            new EffectDef(EffectKind.Shield, 150));
        private static readonly CharDef Crystal = new("冰", Element.Water, effects: new[]
        {
            new EffectDef(EffectKind.HealSelf, 100), new EffectDef(EffectKind.ShieldFromHeal, 100),
            new EffectDef(EffectKind.ShieldFrost, 1),
        });
        // 格挡反击 = 攻击面本体伤害 × 30%:给一个攻击面撑起反击量
        private static readonly CharDef Parry = new("挡", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 2), new EffectDef(EffectKind.CounterBoost, 50) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 200) });
        private static readonly CharDef Cut = RebalanceFixture.Char("减", new EffectDef(EffectKind.DamageCut, 30));
        private static readonly CharDef Mirror = RebalanceFixture.Char("镜", new EffectDef(EffectKind.Reflect, 20, turns: 2));
        private static readonly CharDef Plate = RebalanceFixture.Char("铠", new EffectDef(EffectKind.DefenseBuff, 40, turns: 2));
        private static readonly CharDef Ward = RebalanceFixture.Char("免", new EffectDef(EffectKind.Immunity, 1));
        private static readonly CharDef Mend = RebalanceFixture.Char("泽", new EffectDef(EffectKind.HurtHeal, 30, turns: 3));
        private static readonly CharDef Grove = RebalanceFixture.Char("林",
            new EffectDef(EffectKind.Summon, 900, summonCount: 2, summonAttack: 20, summonChar: "木",
                passive: new SummonPassive { Thorns = 25 }));
        private static readonly CharDef Wall = RebalanceFixture.Char("墙", new EffectDef(EffectKind.ShieldAll, 60));
        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static readonly CharDef[] Defs =
            { Wood, Guard, Keep, Crystal, Parry, Cut, Mirror, Plate, Ward, Mend, Grove, Wall, Hit };

        private static BattleEngine Battle(HookLog log, EnemyDef[] enemies, int dodge)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Defs),
                new BattleConfig { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 300, PlayerDodge = dodge },
                Defs.Skip(1).SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                enemies, seed: 11, startingHp: 4000);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static EnemyDef Boss(BossSkill skill, int attack) =>
            new("钧", Element.Heart, 100000, attack, phases: new[] { new BossPhaseDef("钧", Element.Heart, 100000, attack, skill) });

        private static void Do(BattleEngine b, string id, int target, StringBuilder sb, int allySlot = Targeting.PlayerTarget) =>
            sb.Append($"#{id}{target}/{allySlot}={b.Cast(id, target, allySlot: allySlot)};");

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int, StringBuilder> eachTurn,
            int turns, StringBuilder all)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                eachTurn(b, t, sb);
                sb.Append($"|C{b.Ap}/S{b.ShieldNormal}+0");
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0[");
                foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                sb.Append(']');
                foreach (var s in b.Summons)
                {
                    if (s == null) continue;
                    sb.Append($",W{s.Hp}/{s.Shield}[");
                    foreach (var st in s.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft} ");
                    sb.Append(']');
                }
                for (int i = 0; i < b.Enemies.Count; i++)
                    sb.Append($",E{b.Enemies[i].Hp}/{b.Enemies[i].ActionMeter}");
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            all.Append(raw);
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        /// <summary>玩家直接挨打:护甲 / 格挡 / 减伤 / 镜 / 免疫 / 两桶护盾 / 反震 / 冰晶 / 栉风沐雨轮流在场,闪避摇号,倾覆清盾。</summary>
        [Test]
        public void PlayerSide_AllHitHooks_Identical()
        {
            var log = new HookLog();
            var b = Battle(log, new[]
            {
                RebalanceFixture.Mob(attack: 160),
                new EnemyDef("远", Element.Heart, 100000, 90, range: AttackRange.Ranged),
                Boss(BossSkill.Topple, 120),
            }, dodge: 15);
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t % 2 == 0) Do(x, "护", -1, sb);
                if (t % 3 == 1) Do(x, "留", -1, sb);
                if (t % 3 == 0) Do(x, "冰", -1, sb);
                if (t % 2 == 1) Do(x, "挡", -1, sb);
                if (t % 4 == 0) Do(x, "减", -1, sb);
                if (t % 3 == 2) Do(x, "镜", -1, sb);
                if (t % 4 == 1) Do(x, "铠", -1, sb);
                if (t == 3) Do(x, "免", -1, sb);
                if (t % 4 == 2) Do(x, "泽", -1, sb);
                Do(x, "击", t % 3, sb);
            }, 9, all);
            string raw = all.ToString();
            // 防空转:场景确实走到了护盾吸收、反震、格挡反击、镜、免疫、打空、倾覆清盾
            foreach (var needle in new[] { ":ShieldRecoil;", ":BlockCounter;", ":Reflect;", "ImmunityBlocked:", "Missed:", "ShieldBroken:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(raw, @"EnemyAttack:\d+:\d+:-1:[1-9]"), Is.True, "没有护盾吸收的挥击");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(9), "玩家撑满 9 回合(没有中途判负让对照缩水)");
            Assert.That(fp, Is.EqualTo(PlayerExpected), fp);
        }

        /// <summary>木灵顶前排:荆棘 / 镜 / 木灵格挡 / 玩家减伤 / 群体护盾,淹没 Boss 同时打玩家与全部木灵。</summary>
        [Test]
        public void SummonSide_AllHitHooks_Identical()
        {
            var log = new HookLog();
            var b = Battle(log, new[]
            {
                RebalanceFixture.Mob(attack: 140),
                new EnemyDef("远", Element.Heart, 100000, 80, range: AttackRange.Ranged),
                Boss(BossSkill.Deluge, 110),
            }, dodge: 0);
            var all = new StringBuilder();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t == 0) Do(x, "林", -1, sb);
                if (t % 2 == 0) Do(x, "墙", -1, sb);
                if (t % 2 == 1) Do(x, "挡", -1, sb, allySlot: 0);
                if (t % 3 == 0) Do(x, "减", -1, sb);
                if (t % 3 == 1) Do(x, "镜", -1, sb);
                if (t % 4 == 2) Do(x, "护", -1, sb);
                Do(x, "击", t % 3, sb);
            }, 8, all);
            string raw = all.ToString();
            foreach (var needle in new[] { "SummonHit:", ":Thorns;", ":Reflect;", ":BlockCounter;", "EnemyAttack:" })
                Assert.That(raw.Contains(needle), Is.True, $"场景没走到 {needle}");
            Assert.That(raw.Count(c => c == '\n'), Is.EqualTo(8), "玩家撑满 8 回合");
            Assert.That(fp, Is.EqualTo(SummonExpected), fp);
        }

        // 2026-10-13(护盾回合保留 Task 2):夹具里的 persistOnce 护盾随留存桶取消改为普通护盾,指纹重取;
        // 同批不带 persistOnce 的指纹(本文件其余常量与其他 *IdentityTests)逐位未变。
        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "49dcdc1888bb2ba4 8627")。
        private const string PlayerExpected = "2bd5bb0befcad03b 8866";
        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "8215fa34257c8abe 10842")。
        private const string SummonExpected = "c3e0a16122629ee6 11140";
    }
}
