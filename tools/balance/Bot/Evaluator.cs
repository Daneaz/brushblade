using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>局面打分(spec §3)。只读本体;<see cref="Full"/> 的敌人回合只在副本上跑。</summary>
    public static class Evaluator
    {
        public const double WinScore = 1_000_000;
        public const double LossScore = -1_000_000;
        public const double DamageWeight = 100;     // 敌人损失血量占比 × 100
        public const double KillBonus = 50;         // 每打倒一只
        public const double SurvivalHigh = 80;      // 玩家血线 > 50% 时的 W
        public const double SurvivalLow = 200;      // 玩家血线 ≤ 50% 时的 W
        /// <summary>无释放字在字库时,厚 / 泉每层按此「最低档释放字每层值」(崩 / 溃 = 13)再 × <see cref="NoReleaserFactor"/> 计。</summary>
        public const double FallbackReleasePerStack = 13;
        public const double NoReleaserFactor = 0.5;

        /// <summary>快速打分(spec §3.2):不跑敌人回合,按当前局面记分。只给剪枝排序用。</summary>
        public static double Quick(BattleEngine b, RecipeGraph g, IReadOnlyDictionary<string, int> cardLevels)
        {
            if (b.Phase == BattlePhase.Won) return WinScore - b.Turn;
            if (b.Phase == BattlePhase.Lost) return LossScore;
            return Terms(b, g, cardLevels);
        }

        /// <summary><see cref="Quick"/> 去掉胜负两档后的「进攻 + 生存 + 蓄能」本身。局外估值用它:
        /// 参考局面里一张字清场时,按打掉的血与击杀记分,而不是记成 1e6 的「已赢」。</summary>
        public static double Terms(BattleEngine b, RecipeGraph g, IReadOnlyDictionary<string, int> cardLevels)
        {
            double offense = 0;
            foreach (var e in b.Enemies)
            {
                if (e.MaxHp > 0) offense += (double)(e.MaxHp - Math.Max(0, e.Hp)) / e.MaxHp * DamageWeight;
                if (!e.Alive) offense += KillBonus;
            }

            int maxHp = b.MaxHp;
            double w = b.PlayerHp * 2 > maxHp ? SurvivalHigh : SurvivalLow; // 血线(不含盾)> 50%
            double survival = maxHp > 0 ? (double)(b.PlayerHp + b.PlayerShield) / maxHp * w : 0;
            foreach (var s in b.Summons)
                if (s != null && s.Alive && s.MaxHp > 0)
                    survival += (double)(s.Hp + s.Shield) / s.MaxHp * (w / 2);

            return offense + survival + StoredValue(b, g, cardLevels);
        }

        /// <summary>蓄能分(spec 2026-10-16 §6 / Plan F A1):把增益折成「预计兑现伤害」,再按 offense 同口径
        /// (÷ 活敌总最大生命 × <see cref="DamageWeight"/>)记分。只读局面与字表,不摇任何随机数。
        /// - 燃意(玩家):剩余回合 × (燃意% × 每回合预计输出 + 每回合命中段数 × 一层灼);
        /// - 燃意(各存活木灵):剩余回合 × (燃意% 折出的攻击增量 + 一段命中的一层灼);
        /// - 战意 / 厚 的攻击乘区:层数 × 每层% × 一回合预计输出;
        /// - 厚 / 泉 的释放:层数 × 释放字每层值 × 活敌数(字库里没有对应释放字 → 按最低档 × 0.5)。
        /// 「每回合预计输出」= 字库里有伤害面的字的平均单次伤害(按「先加」攻击、不含战意 / 厚 / 燃意乘区)
        /// × min(每回合 AP, 字库张数)。合计封顶活敌剩余血量(兑现不了比敌人血更多的伤害)。</summary>
        public static double StoredValue(BattleEngine b, RecipeGraph g, IReadOnlyDictionary<string, int> cardLevels)
        {
            var bag = b.PlayerStatuses;
            var kindle = bag.Find(StatusKind.Kindle);
            int morale = bag.TotalMagnitude(StatusKind.Morale);
            int heft = bag.TotalMagnitude(StatusKind.Heft);
            int well = bag.TotalMagnitude(StatusKind.Wellspring);
            bool summonKindle = false;
            foreach (var su in b.Summons)
                if (su != null && su.Alive && su.Statuses.Find(StatusKind.Kindle) != null) summonKindle = true;
            if (kindle == null && !summonKindle && morale == 0 && heft == 0 && well == 0) return 0;

            int alive = 0;
            double aliveMaxHp = 0, aliveHp = 0;
            foreach (var e in b.Enemies)
                if (e.Alive) { alive++; aliveMaxHp += e.MaxHp; aliveHp += e.Hp; }
            if (alive == 0 || aliveMaxHp <= 0) return 0;

            // 「先加」攻击(不含战意 / 厚 / 燃意乘区)相对基准的倍数
            int percent = 100 + morale * BattleConfig.MoralePercentPerStack + heft * BattleConfig.HeftPercentPerStack
                          + (kindle?.Magnitude ?? 0);
            double flat = percent > 0 ? b.EffectiveAttack * 100.0 / percent / BattleConfig.AttackBaseline : 0;

            double rawSum = 0, hitSum = 0, heftRelease = 0, wellRelease = 0;
            int damageChars = 0;
            foreach (string id in b.Library)
            {
                var prof = ProfileOf(g, id, CardAppraiser.LevelOf(cardLevels, id));
                if (prof == null) continue;
                heftRelease = Math.Max(heftRelease, prof.HeftRelease);
                wellRelease = Math.Max(wellRelease, prof.WellRelease);
                double bestRaw = 0, bestHits = 0;
                foreach (var face in prof.Faces)
                {
                    double raw = 0, hits = 0;
                    foreach (var (e, value) in face)
                    {
                        double n = Math.Max(1, e.HitCount) * TargetsOf(e, alive);
                        raw += value * n;
                        hits += n;
                    }
                    if (raw > bestRaw) { bestRaw = raw; bestHits = hits; }
                }
                if (bestRaw <= 0) continue;
                damageChars++;
                rawSum += bestRaw;
                hitSum += bestHits;
            }
            double casts = damageChars == 0 ? 0 : Math.Min(b.ApPerTurn, b.Library.Count);
            double perTurn = damageChars == 0 ? 0 : rawSum / damageChars * casts * flat;   // 每回合预计输出(先加口径)
            double hitsPerTurn = damageChars == 0 ? 0 : hitSum / damageChars * casts;
            double burnLayer = BattleConfig.BaseBurnPerStack * flat;                       // 一层灼的 DOT 当量(一跳)

            double damage = 0;
            if (kindle != null)
                damage += kindle.TurnsLeft * (kindle.Magnitude / 100.0 * perTurn + hitsPerTurn * BattleEngine.KindleBurnPerHit * burnLayer);
            foreach (var su in b.Summons)
            {
                var k = su != null && su.Alive ? su.Statuses.Find(StatusKind.Kindle) : null;
                if (k == null) continue;
                int sp = su.PlayerAttackPercent + k.Magnitude;
                double gain = sp > 0 ? (double)su.EffectiveAttack * k.Magnitude / sp : 0;
                damage += k.TurnsLeft * (gain + BattleEngine.KindleBurnPerHit * burnLayer);
            }
            damage += (morale * BattleConfig.MoralePercentPerStack + heft * BattleConfig.HeftPercentPerStack) / 100.0 * perTurn;
            double scale = b.EffectiveAttack / (double)BattleConfig.AttackBaseline;
            damage += heft * ReleasePerStack(heftRelease) * alive * scale;
            damage += well * ReleasePerStack(wellRelease) * alive * scale;

            return Math.Min(damage, aliveHp) / aliveMaxHp * DamageWeight;
        }

        /// <summary>一张字两面的直伤条目与释放值(折叠特性后),按「字表 × 字 × 卡等级」缓存:只依赖静态字表,与局面无关。</summary>
        private sealed class CharProfile
        {
            public (EffectDef effect, int value)[][] Faces;   // value = 已套卡等级缩放(与 BattleEngine.ResolveEffect 同口径)
            public double HeftRelease, WellRelease;
        }

        private static readonly ConcurrentDictionary<(RecipeGraph, string, int), CharProfile> Profiles =
            new ConcurrentDictionary<(RecipeGraph, string, int), CharProfile>();

        private static CharProfile ProfileOf(RecipeGraph g, string id, int level) =>
            Profiles.GetOrAdd((g, id, level), key =>
            {
                if (!key.Item1.TryGet(key.Item2, out var def)) return null;
                var prof = new CharProfile();
                var faces = new List<(EffectDef, int)[]>();
                int level = key.Item3;
                foreach (bool attackMode in new[] { false, true })
                {
                    if (attackMode && def.AttackEffects.Count == 0) continue;
                    var effects = TraitRules.CastEffects(def, BattleEngine.FaceOf(def, attackMode), level);
                    faces.Add(effects.Where(e => e.Kind == EffectKind.DamageSingle)
                        .Select(e => (e, MetaRules.ScaleEffectValue(e.Kind, e.Value, level))).ToArray());
                    foreach (var e in effects)
                    {
                        int v = MetaRules.ScaleEffectValue(e.Kind, e.Value, level);
                        if (e.Kind == EffectKind.SpendHeft) prof.HeftRelease = Math.Max(prof.HeftRelease, v);
                        else if (e.Kind == EffectKind.SpendWellspring) prof.WellRelease = Math.Max(prof.WellRelease, v);
                    }
                }
                prof.Faces = faces.ToArray();
                return prof;
            });

        private static double ReleasePerStack(double inLibrary) =>
            inLibrary > 0 ? inLibrary : FallbackReleasePerStack * NoReleaserFactor;

        /// <summary>一条直伤打到几个目标(近似):全体 = 活敌数,散射 = 发数,其余非单体按 min(2, 活敌数)。</summary>
        private static double TargetsOf(EffectDef e, int alive) => e.Shape switch
        {
            TargetArea.Single => 1,
            TargetArea.All => alive,
            TargetArea.Scatter => Math.Max(1, e.Shots),
            _ => Math.Min(2, alive),
        };

        /// <summary>完整打分(spec §3.1):在换了固定种子的副本上「结束回合 → 敌人回合 → 停在下一个玩家回合开始」
        /// 再 <see cref="Quick"/>。副本 RNG 只由 <paramref name="leafSeed"/> 派生,与本体 RNG 无关(spec §2.4)。
        /// 下一回合开头若因满库挂起掉字(DropChoice),副本里一律 <see cref="BattleEngine.SkipDrop"/>:
        /// 字库不进打分,跳过不改分数,只为让相位回到玩家回合。
        /// 副本置 <see cref="BattleEngine.SuppressCritRolls"/>:玩家侧暴击按不暴击算(与搜索副本同口径)。</summary>
        public static double Full(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, ulong leafSeed)
        {
            var copy = CloneWithSeed(b, g, cfg, cardLevels, leafSeed);
            copy.SuppressCritRolls = true; // spec §2.3:副本暴击按不暴击算(必暴照算);不进快照,克隆后显式置位
            if (copy.Phase == BattlePhase.DropChoice) copy.SkipDrop();
            if (copy.Phase == BattlePhase.PlayerTurn) copy.EndTurn(); // 内部推进到下一个玩家回合开始或分出胜负
            if (copy.Phase == BattlePhase.DropChoice) copy.SkipDrop();
            return Quick(copy, g, cardLevels);
        }

        /// <summary>经 <c>Capture → Restore</c> 复制出独立副本。<paramref name="seed"/> 非空时把三条随机流
        /// (主流 / 择敌流 / 特性流)替换成由它派生的固定状态;为空时照抄本体的随机流。
        /// 敌人定义从本体的 <see cref="EnemyState.Def"/> 收集(分裂克隆与本体共用一个 Def)。</summary>
        public static BattleEngine CloneWithSeed(BattleEngine b, RecipeGraph g, BattleConfig cfg,
            IReadOnlyDictionary<string, int> cardLevels, ulong? seed)
        {
            var snap = b.Capture();
            if (seed.HasValue)
            {
                ulong x = seed.Value;
                snap.RandomState = NonZero(SplitMix64(ref x));
                snap.TargetRandomState = NonZero(SplitMix64(ref x));
                snap.TraitRandomState = NonZero(SplitMix64(ref x));
            }
            return BattleEngine.Restore(snap, g, cfg, cardLevels, EnemyDefsOf(b));
        }

        public static Dictionary<string, EnemyDef> EnemyDefsOf(BattleEngine b)
        {
            var defs = new Dictionary<string, EnemyDef>(StringComparer.Ordinal);
            foreach (var e in b.Enemies) defs[e.Def.Id] = e.Def;
            return defs;
        }

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                ulong z = x += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // xorshift 的 0 是吸收态;TraitRandomState == 0 还会被 Restore 当成「旧快照缺字段」
        private static uint NonZero(ulong v)
        {
            uint u = unchecked((uint)(v ^ (v >> 32)));
            return u == 0 ? 0x9E3779B9u : u;
        }
    }
}
