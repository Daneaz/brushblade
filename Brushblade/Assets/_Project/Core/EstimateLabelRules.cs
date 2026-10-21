using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>预估签上一段写什么(Plan E4 Task 4)。文案由 Presentation 按种类取字符串表。</summary>
    public enum EstimateSegKind
    {
        Damage,         // 伤 N
        Burn,           // 灼 +N(Burn 事件实际层数)
        Shield,         // 护盾 N(实际入账,盾满 0)
        Heal,           // 治疗 N(实际回复,满血 0)
        Morale,         // 战意 N(出字前后净差)
        Block,          // 格挡 +N(次数)
        BlockPerMorale, // 格挡 = 战意(次数出字后才定)
        DamageCut,      // 受伤 −N%
        HealOverTime,   // 润泽
        Taunt,          // 嘲讽
        Immunity,       // 免疫
        Slow,           // 减速
        Freeze,         // 冻结
        ArmorBreak,     // 破甲
        Seed,           // 种
        Weaken,         // 减攻
        Vulnerable,     // 标记
        Blind,          // 致盲
        Silence,        // 封禁
        Charm,          // 魅惑
        Bleed,          // 流血
        Sunder,         // 破甲(2026-10-21;旧 ArmorBreak 段改称削甲)
    }

    public readonly struct EstimateSeg
    {
        public EstimateSegKind Kind { get; }
        /// <summary>量;只写名的种类不用。</summary>
        public int Amount { get; }

        public EstimateSeg(EstimateSegKind kind, int amount = 0)
        {
            Kind = kind;
            Amount = amount;
        }
    }

    /// <summary>一枚预估签的结构:[全体] 主段[ ×倍率][ · N 段][ · 状态段] + 压浅的「X已用」段。</summary>
    public sealed class EstimateLabel
    {
        /// <summary>打全体:开头写「全体」(A-Q7)。</summary>
        public bool All;
        /// <summary>主段;只有已用段时为 null。</summary>
        public EstimateSeg? Main;
        /// <summary>主段是伤害且吃到相克 / 被克(倍率 ≠ 1 才写)。</summary>
        public bool Ke, Countered;
        /// <summary>段数;≥ 2 才写(A-Q6),否则 0。</summary>
        public int Hits;
        /// <summary>至多 1 个状态段(A-Q8)。</summary>
        public EstimateSeg? Status;
        /// <summary>至多 1 个「X已用」段(白 55%,A-Q9 / A-Q10);没有为 null。</summary>
        public TraitDef Spent;
    }

    /// <summary>预估签的纯取段规则(已定口径 §A):输入 <see cref="CastEstimate"/>,输出签的结构。
    /// 主数 + 至多 1 个状态段 + 至多 1 个已用段;状态段按效果表顺序取第一条可写的;
    /// 认不得的效果一律不出段(R5:不许把枚举名印到签上)。</summary>
    public static class EstimateLabelRules
    {
        /// <summary>悬停在第 <paramref name="enemyIndex"/> 只敌人上的签。被拒或无可写内容 → null。
        /// 数字 = 打在这一只身上的合计;友方侧的效果(护盾 / 治疗……)不进敌人签(A-Q15)。</summary>
        public static EstimateLabel ForEnemy(CastEstimate est, int enemyIndex)
        {
            if (est == null || est.Error != BattleError.None) return null;
            TargetHit hit = default;
            bool found = false;
            foreach (var h in est.Enemies)
                if (h.Index == enemyIndex) { hit = h; found = true; break; }

            var label = new EstimateLabel { All = est.AllEnemies, Spent = SpentOf(est) };
            if (found && (hit.Damage > 0 || hit.Hits > 0))
            {
                label.Main = new EstimateSeg(EstimateSegKind.Damage, hit.Damage);
                label.Ke = hit.Ke && !hit.Countered;
                label.Countered = hit.Countered && !hit.Ke;
                label.Hits = hit.Hits >= 2 ? hit.Hits : 0;
            }

            // 这一下就打死了:不会再挂上任何状态,只写伤害(fix round 1 Minor 2)
            bool killed = found && hit.Killed && label.Main != null;
            var seen = new HashSet<EstimateSegKind>();
            foreach (var e in killed ? Array.Empty<EffectDef>() : est.Effects)
            {
                var seg = EnemySegOf(e, found ? hit.Burn : 0, est.MoraleGain);
                if (seg == null || !seen.Add(seg.Value.Kind)) continue;
                if (label.Main == null) label.Main = seg;
                else { label.Status = seg; break; }
            }
            return label.Main == null && label.Spent == null ? null : label;
        }

        /// <summary>落在友方(玩家 / 木灵)上的签:护盾、治疗按实际入账写(盾满 = 护盾 0,A-Q4)。被拒或无可写内容 → null。</summary>
        public static EstimateLabel ForAlly(CastEstimate est)
        {
            if (est == null || est.Error != BattleError.None) return null;
            var label = new EstimateLabel { Spent = SpentOf(est) };
            var seen = new HashSet<EstimateSegKind>();
            foreach (var e in est.Effects)
            {
                var seg = AllySegOf(e, est);
                if (seg == null || !seen.Add(seg.Value.Kind)) continue;
                if (label.Main == null) label.Main = seg;
                else { label.Status = seg; break; }
            }
            return label.Main == null && label.Spent == null ? null : label;
        }

        /// <summary>签上的「X已用」段取第一条本场用尽的特性;没有 → null。</summary>
        public static TraitDef SpentOf(CastEstimate est) =>
            est != null && est.SpentTraits.Count > 0 ? est.SpentTraits[0] : null;

        private static EstimateSeg? EnemySegOf(EffectDef e, int burn, int moraleGain)
        {
            switch (e.Kind)
            {
                case EffectKind.BurnSingle:
                case EffectKind.BurnAll:
                    return burn > 0 ? new EstimateSeg(EstimateSegKind.Burn, burn) : (EstimateSeg?)null;
                case EffectKind.Morale:
                    return moraleGain > 0 ? new EstimateSeg(EstimateSegKind.Morale, moraleGain) : (EstimateSeg?)null;
                case EffectKind.Slow: return new EstimateSeg(EstimateSegKind.Slow);
                case EffectKind.Freeze: return new EstimateSeg(EstimateSegKind.Freeze);
                case EffectKind.ArmorBreak: return new EstimateSeg(EstimateSegKind.ArmorBreak);
                case EffectKind.Sunder: return new EstimateSeg(EstimateSegKind.Sunder);
                case EffectKind.Seed: return new EstimateSeg(EstimateSegKind.Seed);
                case EffectKind.Weaken: return new EstimateSeg(EstimateSegKind.Weaken);
                case EffectKind.Vulnerable: return new EstimateSeg(EstimateSegKind.Vulnerable);
                case EffectKind.Blind: return new EstimateSeg(EstimateSegKind.Blind);
                case EffectKind.Silence: return new EstimateSeg(EstimateSegKind.Silence);
                case EffectKind.Charm: return new EstimateSeg(EstimateSegKind.Charm);
                case EffectKind.Bleed: return new EstimateSeg(EstimateSegKind.Bleed);
                default: return null;
            }
        }

        private static EstimateSeg? AllySegOf(EffectDef e, CastEstimate est)
        {
            switch (e.Kind)
            {
                case EffectKind.Shield:
                case EffectKind.ShieldAll:
                    return new EstimateSeg(EstimateSegKind.Shield, est.ShieldGranted);
                case EffectKind.HealSelf:
                case EffectKind.HealAll:
                    return new EstimateSeg(EstimateSegKind.Heal, est.HealAmount);
                case EffectKind.Morale:
                    return est.MoraleGain > 0 ? new EstimateSeg(EstimateSegKind.Morale, est.MoraleGain) : (EstimateSeg?)null;
                case EffectKind.Block:
                    // 按战意计次(D2-金 E10)出字后才定,写「= 战意」不写死数字(同落木灵签)
                    return e.ScaleBy == ScaleBasis.Morale
                        ? new EstimateSeg(EstimateSegKind.BlockPerMorale)
                        : new EstimateSeg(EstimateSegKind.Block, e.Value);
                case EffectKind.DamageCut: return new EstimateSeg(EstimateSegKind.DamageCut, e.Value);
                case EffectKind.HealOverTime: return new EstimateSeg(EstimateSegKind.HealOverTime);
                case EffectKind.Taunt: return new EstimateSeg(EstimateSegKind.Taunt);
                case EffectKind.Immunity: return new EstimateSeg(EstimateSegKind.Immunity);
                default: return null;
            }
        }
    }
}
