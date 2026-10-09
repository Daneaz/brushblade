using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 2 第一步(附录 J1):玩家侧 DamagePlayerDirect 与木灵侧 DamageSummon 的格挡反击段
    /// 抽成共用 ResolveCounter 的**纯重构**对照。trace 覆盖不到格挡反击,所以这里把同种子下的事件流、
    /// 敌我血量与三条随机流状态拼成指纹,与抽取前的代码逐位对照(期望值是抽取前在 54a49cdb 上跑出来的)。
    ///
    /// 覆盖:玩家格挡反击(含镜先用、预算钳制、致盲摇号)、木灵格挡反击(荆棘 → 镜 → 反击)、Boss 攻击者 + 反击增强。</summary>
    public class CounterRefactorIdentityTests
    {
        private static CharDef Guard() => new("铠", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 2) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static CharDef Mirror() => RebalanceFixture.Char("镜", new EffectDef(EffectKind.Reflect, 20, turns: 3));

        private static CharDef Boost() => RebalanceFixture.Char("戈", new EffectDef(EffectKind.CounterBoost, 100));

        private static CharDef Summoner() => new("棘", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 400, summonCount: 1, summonAttack: 0, summonChar: "木",
                passive: new SummonPassive { Thorns = 20 }) });

        private static BattleEngine Battle(CharDef[] defs, params EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs.Append(new CharDef("木", Element.Wood)).ToArray()),
                new BattleConfig { PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 20 },
                defs.SelectMany(d => new[] { d.Id, d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 7);

        /// <summary>指纹:每拍事件(种类 / 下标 / 量 / 吸收 / 来源)+ 敌我血量 + 三条随机流。FNV-1a 64。</summary>
        private static string Fingerprint(BattleEngine b, Action<BattleEngine> eachTurn, int turns)
        {
            var sb = new StringBuilder();
            for (int t = 0; t < turns; t++)
            {
                eachTurn(b);
                b.EndTurn();
                foreach (var e in b.LastEvents)
                    sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source};");
                sb.Append($"|P{b.PlayerHp}");
                foreach (var en in b.Enemies) sb.Append($",E{en.Hp}");
                foreach (var s in b.Summons) if (s != null) sb.Append($",S{s.Hp}");
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            ulong h = 14695981039346656037UL;
            foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
            string raw = sb.ToString();
            Assert.That(raw.Contains(":BlockCounter;"), Is.True, "场景确实打出了格挡反击(否则对照空转)");
            return h.ToString("x16") + " " + sb.Length;
        }

        [Test]
        public void PlayerBlock_MirrorFirst_BudgetClamp_BlindRoll_Identical()
        {
            var b = Battle(new[] { Guard(), Mirror() },
                RebalanceFixture.Mob(attack: 100), RebalanceFixture.Mob(attack: 15), RebalanceFixture.Mob(attack: 60));
            b.Enemies[1].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Blind, Polarity = StatusPolarity.Debuff, Magnitude = 50, TurnsLeft = -1 });
            string fp = Fingerprint(b, x =>
            {
                x.Cast("铠", -1, attackMode: false);
                x.Cast("镜");
            }, 4);
            Assert.That(fp, Is.EqualTo(PlayerExpected), fp);
        }

        [Test]
        public void SummonBlock_ThornsMirrorCounter_Identical()
        {
            var b = Battle(new[] { Guard(), Mirror(), Summoner() },
                RebalanceFixture.Mob(attack: 100), RebalanceFixture.Mob(attack: 20));
            Assert.That(b.Cast("棘"), Is.EqualTo(BattleError.None));
            int slot = Array.FindIndex(b.Summons.ToArray(), s => s != null && s.Alive);
            string fp = Fingerprint(b, x =>
            {
                x.Cast("铠", -1, attackMode: false, allySlot: slot);
                x.Cast("镜");
            }, 3);
            Assert.That(fp, Is.EqualTo(SummonExpected), fp);
        }

        [Test]
        public void BossAttacker_CounterBoost_Identical()
        {
            var b = Battle(new[] { Guard(), Boost() }, RebalanceFixture.Boss(attack: 120), RebalanceFixture.Mob(attack: 30));
            string fp = Fingerprint(b, x =>
            {
                x.Cast("铠", -1, attackMode: false);
                x.Cast("戈");
            }, 4);
            Assert.That(fp, Is.EqualTo(BossExpected), fp);
        }

        private const string PlayerExpected = "c10fcc0de3ed1d37 1412";
        private const string SummonExpected = "f06d11689e1359f9 1086";
        private const string BossExpected = "6f0225ab1048a318 972";
    }
}
