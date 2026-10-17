using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Balance.Bot;
using Brushblade.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Brushblade.Balance.Tests
{
    /// <summary>完备性的另一个方向(审查 Important 1):暴力枚举 Cast / 拆 / 合的参数组合,在副本上执行,
    /// Core 受理(None)的组合按「执行后的局面」归并成等价类(如全体面换 target 落到同一局面),
    /// 断言每个等价类都有 Generate 的某条候选落到同一局面。
    ///
    /// 局面指纹 = 执行后 BattleSnapshot 的 JSON(含三条 RNG 流、字库、池、召唤物、状态)。
    /// 本工程不进 Unity 程序集,直接用 Newtonsoft 序列化快照不受 Tests asmdef 的限制。</summary>
    public class CandidatesOracleTests
    {
        private static string Fingerprint(BattleEngine b) => JsonConvert.SerializeObject(b.Capture());

        private sealed class Probe
        {
            public string Label;
            public Func<BattleEngine, BattleError> Run;
        }

        private static IEnumerable<Probe> OracleProbes(TestBattle t)
        {
            var b = t.Battle;
            int enemies = b.Enemies.Count;
            var hand = b.Library.Concat(b.Pool).Distinct().ToList();
            foreach (var id in hand)
                foreach (bool attackMode in new[] { false, true })
                    for (int target = -1; target < enemies; target++)
                        for (int ally = Targeting.PlayerTarget; ally < BattleEngine.MaxSummonSlots; ally++)
                        {
                            if (TraitOnlyAllyOnDeadSlot(b, id, attackMode, ally)) continue;
                            foreach (SummonDropMode drop in Enum.GetValues(typeof(SummonDropMode)))
                                foreach (bool replace in new[] { false, true })
                                {
                                    string cid = id; bool am = attackMode; int tg = target, al = ally; var dm = drop; bool rp = replace;
                                    yield return new Probe
                                    {
                                        Label = $"Cast {cid} atk={am} t={tg} a={al} {dm} R={rp}",
                                        Run = e => e.Cast(cid, tg, rp, am, -1, null, al, dm),
                                    };
                                }
                        }

            foreach (var def in Fixture.Graph.All)
            {
                string id = def.Id;
                yield return new Probe { Label = $"Dismantle {id}", Run = e => e.Dismantle(id) };
                // 无配方的字(部件 / 独体)不是合字目标:ForgeEngine.TryCompose 对空配方会「无中生有」放行,
                // 但玩家拿不到这个入口(拆合台只列 Suggest,Suggest 跳过 IsLeaf)。不算可达动作,不枚举。
                if (def.IsLeaf) continue;
                yield return new Probe { Label = $"Compose {id}", Run = e => e.Compose(id) };
                // 复合「弃 + 合」只在直接合不成时才算一种动作(能直接合还先弃一张 = 弃字,不属于本动作集)
                foreach (var discard in hand)
                {
                    string dc = discard;
                    yield return new Probe
                    {
                        Label = $"Discard {dc} + Compose {id}",
                        Run = e =>
                        {
                            var probe = t.Clone();
                            if (probe.Compose(id) == BattleError.None) return BattleError.InvalidTarget; // 不需要腾位:跳过
                            var r = e.Discard(dc);
                            return r != BattleError.None ? r : e.Compose(id);
                        },
                    };
                }
            }
            yield return new Probe { Label = "EndTurn", Run = e => { e.EndTurn(); return BattleError.None; } };
        }

        /// <summary>玩家做不出的组合:本体不选友方、只有特性折入的效果选友方(如 淋 Lv6 淋透的 HoT),
        /// 却落在不是活单位的槽(空格 / 尸体)。Core 对特性折入的友方效果不校验 allySlot(另记 Plan F),
        /// 受理后得到一条指向空槽的 HoT —— 拖放 UI 只许落活友方,不算可达动作,不枚举。
        /// 召唤 / 复活面落空格 / 尸体是正当落点,不在此列。</summary>
        private static bool TraitOnlyAllyOnDeadSlot(BattleEngine b, string id, bool attackMode, int ally)
        {
            if (ally < 0 || b.SlotOccupancy(ally) == SlotState.Alive) return false;
            if (!Fixture.Graph.TryGet(id, out var def)) return false;
            var body = attackMode && def.AttackEffects.Count > 0 ? def.AttackEffects : def.Effects;
            var cast = b.CastEffectsNow(id, attackMode);
            if (body.Any(BattleEngine.EffectNeedsAllyTarget) || !cast.Any(BattleEngine.EffectNeedsAllyTarget)) return false;
            return !cast.Any(e => e.Kind == EffectKind.Summon || e.Kind == EffectKind.SummonSapling || e.Kind == EffectKind.Revive);
        }

        [Test]
        public void EveryAcceptedCombination_IsCoveredByAGeneratedCandidate()
        {
            int scenes = 0, accepted = 0;
            var misses = new List<string>();
            foreach (var t in CandidatesTests.Scenes())
            {
                scenes++;
                var covered = new HashSet<string>();
                foreach (var a in t.Generate())
                {
                    var copy = t.Clone();
                    if (a.ApplyTo(copy) == BattleError.None) covered.Add(Fingerprint(copy));
                }

                var seenClasses = new HashSet<string>();
                foreach (var p in OracleProbes(t))
                {
                    var copy = t.Clone();
                    if (p.Run(copy) != BattleError.None) continue;
                    accepted++;
                    string fp = Fingerprint(copy);
                    if (!seenClasses.Add(fp)) continue;
                    if (!covered.Contains(fp)) misses.Add($"场景{scenes}: {p.Label}");
                }
            }
            TestContext.WriteLine($"{scenes} 个场景,Core 受理 {accepted} 个组合");
            Assert.That(misses, Is.Empty, "以下等价类没有候选覆盖(前 30 条):\n" + string.Join("\n", misses.Take(30)));
        }
    }
}
