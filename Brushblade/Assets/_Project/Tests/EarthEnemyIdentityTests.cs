using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 4 第一步(附录 G5 / G6 前置):DamageEnemy 致死分支、CastBossSkill 在**没有 OverkillSplash / SkillCut**
    /// 时逐位恒等的对照。G5 要在致死分支记溢出、在击杀时入队处解析;G6 要在 CastBossSkill 开头取出减伤并改写四种技能的伤害。
    /// 两处都是 trace 热路径,这里再补一层带护甲 / 敌盾 / 暴击 / 击杀时特性 / 四种大招的场景。
    ///
    /// 指纹 = 每拍事件(种类 / 下标 / 量 / 副下标 / 吸收 / 来源)+ 钩子序列 + 两桶护盾 + 玩家状态袋 + 木灵血量 / 护盾
    /// + 敌人血量 / 护盾 / 状态袋 + 三条随机流。期望值是 G5 / G6 改动前(HEAD 37944a55)跑出来的。</summary>
    public class EarthEnemyIdentityTests
    {
        private static readonly CharDef Wood = new("木", Element.Wood);

        // 全体伤害 + 击杀时特性(战意 + 全体余波):击杀入队、反应里再杀(R4 不再入队)
        private static readonly CharDef Burst = new("爆", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 45, shape: TargetArea.All) },
            traits: new[]
            {
                new TraitDef(TraitSlot.Lv8, TraitFace.Attack, TraitForm.Passive, null, "连",
                    new[] { new EffectDef(EffectKind.Morale, 1), new EffectDef(EffectKind.DamageSingle, 0, shape: TargetArea.All, bodyPercent: 40) },
                    TraitTrigger.OnKill),
            });
        // 单体重击
        private static readonly CharDef Smash = RebalanceFixture.Char("斩", new EffectDef(EffectKind.DamageSingle, 90));
        // 木灵 + 护盾 + 护甲(大招的四种落点都有对象)
        private static readonly CharDef Grove = RebalanceFixture.Char("林",
            new EffectDef(EffectKind.Summon, 300, summonCount: 2, summonAttack: 10, summonChar: "木"));
        private static readonly CharDef Keep = RebalanceFixture.Char("留",
            new EffectDef(EffectKind.Shield, 120));
        private static readonly CharDef Armor = RebalanceFixture.Char("铠", new EffectDef(EffectKind.DefenseBuff, 20, turns: 2));
        private static readonly CharDef Poke = RebalanceFixture.Char("戳", new EffectDef(EffectKind.DamageSingle, 15));

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args) =>
                Sb.Append($"{args.Kind}:{args.Subject.Side}{args.Subject.Index}:{args.Status}:{args.Amount};");
        }

        private static readonly CharDef[] Defs = { Wood, Burst, Smash, Grove, Keep, Armor, Poke };

        private static BattleEngine Battle(HookLog log, IReadOnlyList<EnemyDef> enemies, int seed)
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Defs),
                new BattleConfig { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 200, BossPhaseJitterPercent = 0 },
                Defs.Skip(1).SelectMany(d => Enumerable.Repeat(d.Id, 20)).ToArray(), Array.Empty<string>(),
                enemies, seed: seed, startingHp: 4000, cardLevels: Defs.ToDictionary(d => d.Id, _ => 8));
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        /// <summary>出字并把这一次的事件记进指纹与总表(LastEvents 每次动作都会重置)。</summary>
        private static void Do(BattleEngine b, string id, int target, StringBuilder sb, List<BattleEvent> all, bool attack = false)
        {
            sb.Append($"#{id}{target}={b.Cast(id, target, attackMode: attack)};");
            Dump(sb, b.LastEvents);
            all.AddRange(b.LastEvents);
        }

        private static void Dump(StringBuilder sb, IReadOnlyList<BattleEvent> events)
        {
            foreach (var e in events.Where(x => x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source}:{e.Crit};");
        }

        private static string Fingerprint(BattleEngine b, HookLog log, Action<BattleEngine, int, StringBuilder> eachTurn, int turns,
            List<BattleEvent> all)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns && b.Phase != BattlePhase.Won && b.Phase != BattlePhase.Lost; t++)
            {
                eachTurn(b, t, sb);
                sb.Append($"|C{b.Ap}/S{b.ShieldNormal}+0");
                if (b.Phase == BattlePhase.Won) break;
                b.EndTurn();
                Dump(sb, b.LastEvents);
                all.AddRange(b.LastEvents);
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0[");
                foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft}:{s.Polarity} ");
                sb.Append(']');
                foreach (var s in b.Summons)
                    if (s != null) sb.Append($",W{s.Hp}/{s.Shield}");
                for (int i = 0; i < b.Enemies.Count; i++)
                {
                    var e = b.Enemies[i];
                    sb.Append($",E{e.Hp}/{e.Shield}/{e.ActionMeter}/{e.ChargeCounter}/{e.IsCharging}[");
                    foreach (var s in e.Statuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
                    sb.Append(']');
                }
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            sb.Append($"#END{b.Phase}/{b.Turn}");
            string raw = sb.ToString();
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        private static EnemyDef Boss(string id, BossSkill skill, int attack) => new(id, Element.Heart, 3000, attack,
            phases: new[]
            {
                new BossPhaseDef(id, Element.Heart, 1500, attack, skill: skill, defense: 10),
                new BossPhaseDef(id + "二", Element.Heart, 1500, attack + 5, skill: skill),
            });

        /// <summary>致死分支:护甲、敌盾、暴击、击杀时特性(反应里的全体余波再杀,R4 不再入队)、单体溢出击杀、清场判胜。</summary>
        [Test]
        public void KillBranch_Identical()
        {
            var log = new HookLog();
            var enemies = new[]
            {
                RebalanceFixture.Mob(hp: 250, attack: 20, armor: 20),
                RebalanceFixture.Mob(hp: 400, attack: 25),
                RebalanceFixture.Mob(hp: 180, attack: 15, armor: 40),
                RebalanceFixture.Mob(hp: 600, attack: 30, ability: EnemyAbility.Split),
                RebalanceFixture.Mob(hp: 800, attack: 10, armor: 10),
                RebalanceFixture.Mob(hp: 350, attack: 20),
            };
            var b = Battle(log, enemies, seed: 11);
            b.Enemies[1].Shield = 40;
            b.Enemies[4].Shield = 90;
            var all = new List<BattleEvent>();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                Do(x, "爆", 0, sb, all, attack: true);
                for (int i = 0; i < x.Enemies.Count; i++)
                    if (x.Enemies[i].Alive) { Do(x, "斩", i, sb, all); break; }
            }, 12, all);
            int kills = all.Count(e => e.Kind == BattleEventKind.EnemyDied);
            Assert.That(kills, Is.GreaterThanOrEqualTo(6), "场景确实走了多次致死分支(否则对照空转)");
            Assert.That(all.Any(e => e.Kind == BattleEventKind.Damage && e.Absorbed > 0), Is.True, "敌盾吸收过");
            Assert.That(all.Any(e => e.Kind == BattleEventKind.Damage && e.Crit), Is.True, "暴击过");
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "清场判胜");
            Assert.That(fp, Is.EqualTo(KillExpected), fp);
        }

        /// <summary>四种大招:淹没(玩家 + 全体木灵)、洞穿(最前木灵 + 玩家)、倾覆(吸盾后清盾 + 封字)、吞噬(秒杀最前木灵 / 无木灵退化普攻)。</summary>
        [Test]
        public void BossSkills_Identical()
        {
            var log = new HookLog();
            var enemies = new[]
            {
                Boss("淹", BossSkill.Deluge, 40), Boss("穿", BossSkill.Impale, 45),
                Boss("覆", BossSkill.Topple, 35), Boss("吞", BossSkill.Devour, 30),
            };
            var b = Battle(log, enemies, seed: 5);
            var all = new List<BattleEvent>();
            string fp = Fingerprint(b, log, (x, t, sb) =>
            {
                if (t % 2 == 0) Do(x, "林", -1, sb, all);
                Do(x, "留", -1, sb, all);
                Do(x, "铠", -1, sb, all);
                Do(x, "戳", t % 4, sb, all);
            }, 10, all);
            foreach (var skill in new[] { BossSkill.Deluge, BossSkill.Impale, BossSkill.Topple, BossSkill.Devour })
                Assert.That(all.Count(e => e.Kind == BattleEventKind.BossSkillCast && e.Amount == (int)skill), Is.GreaterThanOrEqualTo(2),
                    $"{skill} 至少放了两次(否则对照空转)");
            Assert.That(all.Any(e => e.Kind == BattleEventKind.ShieldBroken), Is.True, "倾覆清过盾");
            Assert.That(all.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "木灵挨过大招");
            Assert.That(b.Phase, Is.Not.EqualTo(BattlePhase.Lost), "玩家没有中途判负");
            Assert.That(fp, Is.EqualTo(BossExpected), fp);
        }

        // 2026-10-18 战场改 3×2(敌每排 3 列、召唤槽 6、前排 3 格)后重录,原值 "d9d7f014f6dc5655 6469"
        private const string KillExpected = "4a8bb5b18660b32e 6665";
        // 2026-10-13(护盾回合保留 Task 2):夹具里的 persistOnce 护盾随留存桶取消改为普通护盾,指纹重取;
        // 同批不带 persistOnce 的指纹(本文件其余常量与其他 *IdentityTests)逐位未变。
        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "d4aa3156089da689 20729")。
        // 2026-10-18 战场改 3×2(敌每排 3 列、召唤槽 6、前排 3 格)后重录,原值 "bce33c438e5110c1 20746"
        private const string BossExpected = "d3a853b077e250ed 20671";
    }
}
