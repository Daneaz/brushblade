using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>字卡详情一面的数值格种类(Plan E2 Q6)。按真实字表的本体效果补齐;标签 / 单位的映射在 Presentation。
    /// 「量」(随卡等级缩放)与「层 / 回合 / 次数」(不缩放)的分界见 <see cref="FaceStats.Scales"/>。</summary>
    public enum FaceStatKind
    {
        // ---- 主量(优先排前,随卡等级缩放)----
        Damage,         // DamageSingle 的伤害
        Heal,           // HealSelf / HealAll 的治疗量
        Shield,         // Shield / ShieldAll 的护盾量
        SummonHp,       // Summon 的木灵生命
        SummonAttack,   // Summon 的木灵攻击(为 0 不出格)
        // ---- 关键状态 ----
        Burn,           // 灼:层(BurnSingle / BurnAll)
        Morale,         // 战意:层
        Block,          // 格挡:次
        Counter,        // 格挡反击伤害(攻面本体 × 反击百分比,缩放)
        Weaken,         // 减攻:百分点(缩放);回合在紧跟的 Duration 格
        Armor,          // 护甲:点(DefenseBuff,缩放);回合在紧跟的 Duration 格
        Duration,       // 前一格(减攻 / 护甲)的持续回合
        Freeze,         // 冻结:回合
        Slow,           // 减速:回合
        Seed,           // 种:回合
        Charm,          // 魅惑:回合
        HealOverTime,   // 润泽(持续治疗):回合
        ArmorBreak,     // 破甲:回合
        Nature,         // 生面「本命」(Q7):Value 恒 0,数据在 FaceStat.Passive;图标与措辞归 Presentation
    }

    /// <summary>一格数值。</summary>
    public readonly struct FaceStat
    {
        public FaceStatKind Kind { get; }
        public int Value { get; }
        /// <summary>来自哪条本体效果。</summary>
        public EffectKind Source { get; }
        /// <summary>只在 <see cref="FaceStatKind.Nature"/> 格非 null:折叠后(含 Lv3 本命强化)的召唤被动。</summary>
        public SummonPassive Passive { get; }
        /// <summary>只在 <see cref="FaceStatKind.Duration"/> 格非 null:这条持续跟的是哪一格(减攻 / 护甲),
        /// 一面同时有两组时各配各的前缀。</summary>
        public FaceStatKind? DurationOf { get; }

        public FaceStat(FaceStatKind kind, int value, EffectKind source, SummonPassive passive = null,
            FaceStatKind? durationOf = null)
        {
            Kind = kind;
            Value = value;
            Source = source;
            Passive = passive;
            DurationOf = durationOf;
        }
    }

    /// <summary>一格的升级差。</summary>
    public readonly struct StatDelta
    {
        public CardFace Face { get; }
        public FaceStatKind Kind { get; }
        public int From { get; }
        public int To { get; }
        /// <summary>同 <see cref="FaceStat.DurationOf"/>:持续差跟的是哪一格;其余为 null。</summary>
        public FaceStatKind? DurationOf { get; }

        public StatDelta(CardFace face, FaceStatKind kind, int from, int to, FaceStatKind? durationOf = null)
        {
            Face = face;
            Kind = kind;
            From = from;
            To = to;
            DurationOf = durationOf;
        }
    }

    /// <summary>字卡详情 / 升级预览的每面数值格(Plan E2)。纯函数。
    /// 口径 Q5:本体 = Lv1 本体 + Lv3 替换(<see cref="TraitRules.Fold"/> 跳过 Lv4+),按卡等级缩放;
    /// Q6:每面至多 3 格,主量(伤害 / 治疗 / 护盾 / 木灵血攻)在前,其余按本体效果顺序,每面至少 1 格。</summary>
    public static class FaceStats
    {
        public const int MaxCells = 3;

        /// <summary>这一种格的数值是否随卡等级缩放(层 / 回合 / 次数不缩放,spec v7 §1)。
        /// 格种类是按效果合并的(治疗 = HealSelf / HealAll,护盾 = Shield / ShieldAll),反击 / 木灵攻击直接走
        /// <see cref="MetaRules.ScaleByCardLevel"/>,所以不能逐条从 <see cref="MetaRules.ScalesWithCardLevel"/> 推导;
        /// 两份分类由 <c>FaceStatsTests.Scales_ReconcilesWithEngineScaling</c> 用真实字表的 L1→L2 对账。</summary>
        public static bool Scales(FaceStatKind kind) => kind switch
        {
            FaceStatKind.Damage or FaceStatKind.Heal or FaceStatKind.Shield
                or FaceStatKind.SummonHp or FaceStatKind.SummonAttack
                or FaceStatKind.Counter or FaceStatKind.Weaken or FaceStatKind.Armor => true,
            _ => false,
        };

        /// <summary>本体数值格(1–3 格)。</summary>
        public static IReadOnlyList<FaceStat> Body(CharDef def, CardFace face, int cardLevel) =>
            AllCells(def, face, cardLevel, cardLevel, TraitSlot.Lv4).Take(MaxCells).ToList();

        /// <summary>升级前后逐面逐格的差,只返回变了的;先攻面后五行面,面内按格序。</summary>
        public static IReadOnlyList<StatDelta> Deltas(CharDef def, int fromLevel, int toLevel)
        {
            var list = new List<StatDelta>();
            foreach (var face in FaceOrder)
                AddDiffs(list, face,
                    AllCells(def, face, fromLevel, fromLevel, TraitSlot.Lv4),
                    AllCells(def, face, toLevel, toLevel, TraitSlot.Lv4));
            return list;
        }

        /// <summary>Lv3 的「本体差」(Q2):同一级(按 max(卡等级, 3) 缩放,Q17)比较「不含 Lv3」与「含 Lv3」的本体格,
        /// 只返回变了的。等级系数不会混进来;本命强化只改被动(木生面),返回空表。</summary>
        public static IReadOnlyList<StatDelta> Lv3Change(CharDef def, CardFace face, int cardLevel = 3)
        {
            int level = Math.Max(cardLevel, (int)TraitSlot.Lv3);
            var list = new List<StatDelta>();
            AddDiffs(list, face,
                AllCells(def, face, level, level, TraitSlot.Lv3),
                AllCells(def, face, level, level, TraitSlot.Lv4));
            return list;
        }

        // ---- 内部 ----

        private static readonly CardFace[] FaceOrder = { CardFace.Attack, CardFace.Feature };

        /// <summary>按位置比对(同面同级的格种类序列不随等级变;不一致时按 (Kind, 第几次出现) 配对)。</summary>
        private static void AddDiffs(List<StatDelta> into, CardFace face, List<FaceStat> from, List<FaceStat> to)
        {
            var seen = new Dictionary<FaceStatKind, int>();
            foreach (var cell in to)
            {
                seen.TryGetValue(cell.Kind, out int nth);
                seen[cell.Kind] = nth + 1;
                var before = from.Where(c => c.Kind == cell.Kind).Skip(nth).Cast<FaceStat?>().FirstOrDefault();
                if (before == null) continue;   // 新出现的格不算「差」(现行数据不会出现)
                if (before.Value.Value != cell.Value)
                    into.Add(new StatDelta(face, cell.Kind, before.Value.Value, cell.Value, cell.DurationOf));
            }
        }

        /// <summary>全部格(未截断),主量在前、其余按本体顺序。internal 只为测试钉住「真实字表每面不超过
        /// <see cref="MaxCells"/> 格」(Body 截断与 Deltas 不截断才是同一口径)。<paramref name="unlockLevel"/> 决定哪些特性已解锁,
        /// <paramref name="skipFrom"/> 及以上的槽位整条跳过;<paramref name="scaleLevel"/> 决定缩放。</summary>
        internal static List<FaceStat> AllCells(CharDef def, CardFace face, int unlockLevel, int scaleLevel, TraitSlot skipFrom)
        {
            bool attack = face == CardFace.Attack;
            var actual = BattleEngine.FaceOf(def, attack);
            var effects = TraitRules.Fold(BattleEngine.EffectsOf(def, attack), def, actual, unlockLevel,
                skip: t => t.Slot >= skipFrom);
            var primary = new List<FaceStat>();
            var rest = new List<FaceStat>();
            foreach (var e in effects)
                AddCells(def, e, scaleLevel, primary, rest);
            primary.AddRange(rest);
            return primary;
        }

        private static int Scaled(EffectDef e, int level) => MetaRules.ScaleEffectValue(e.Kind, e.Value, level);

        private static void AddCells(CharDef def, EffectDef e, int level, List<FaceStat> primary, List<FaceStat> rest)
        {
            switch (e.Kind)
            {
                case EffectKind.DamageSingle:
                    primary.Add(new FaceStat(FaceStatKind.Damage, Scaled(e, level), e.Kind));
                    break;
                case EffectKind.HealSelf:
                case EffectKind.HealAll:
                    primary.Add(new FaceStat(FaceStatKind.Heal, Scaled(e, level), e.Kind));
                    break;
                case EffectKind.Shield:
                case EffectKind.ShieldAll:
                    primary.Add(new FaceStat(FaceStatKind.Shield, Scaled(e, level), e.Kind));
                    break;
                case EffectKind.Summon:
                    primary.Add(new FaceStat(FaceStatKind.SummonHp, Scaled(e, level), e.Kind));
                    if (e.SummonAttack > 0)
                        primary.Add(new FaceStat(FaceStatKind.SummonAttack, MetaRules.ScaleByCardLevel(e.SummonAttack, level), e.Kind));
                    if (e.Passive != null)
                        rest.Add(new FaceStat(FaceStatKind.Nature, 0, e.Kind, e.Passive));
                    break;
                case EffectKind.BurnSingle:
                case EffectKind.BurnAll:
                    rest.Add(new FaceStat(FaceStatKind.Burn, e.Value, e.Kind));
                    break;
                case EffectKind.Morale:
                    rest.Add(new FaceStat(FaceStatKind.Morale, e.Value, e.Kind));
                    break;
                case EffectKind.Block:
                    rest.Add(new FaceStat(FaceStatKind.Block, e.Value, e.Kind));
                    // 反击 = 攻面本体伤害(吃等级)× 反击百分比(与 BattleEngine 的 Block 分支同式)
                    int counter = MetaRules.ScaleByCardLevel(BattleEngine.AttackBaseOf(def), level)
                        * (e.CounterPercent > 0 ? e.CounterPercent : BattleConfig.BlockCounterPercent) / 100;
                    if (counter > 0) rest.Add(new FaceStat(FaceStatKind.Counter, counter, e.Kind));
                    break;
                case EffectKind.Weaken:
                    rest.Add(new FaceStat(FaceStatKind.Weaken, Scaled(e, level), e.Kind));
                    rest.Add(new FaceStat(FaceStatKind.Duration, TraitRules.TurnsOf(e), e.Kind, durationOf: FaceStatKind.Weaken));
                    break;
                case EffectKind.DefenseBuff:
                    rest.Add(new FaceStat(FaceStatKind.Armor, Scaled(e, level), e.Kind));
                    rest.Add(new FaceStat(FaceStatKind.Duration, TraitRules.TurnsOf(e), e.Kind, durationOf: FaceStatKind.Armor));
                    break;
                case EffectKind.Freeze:
                    rest.Add(new FaceStat(FaceStatKind.Freeze, TraitRules.TurnsOf(e), e.Kind));
                    break;
                case EffectKind.Slow:
                    rest.Add(new FaceStat(FaceStatKind.Slow, TraitRules.TurnsOf(e), e.Kind));
                    break;
                case EffectKind.Seed:
                    rest.Add(new FaceStat(FaceStatKind.Seed, TraitRules.TurnsOf(e), e.Kind));
                    break;
                case EffectKind.Charm:
                    rest.Add(new FaceStat(FaceStatKind.Charm, TraitRules.TurnsOf(e), e.Kind));
                    break;
                case EffectKind.HealOverTime:
                    rest.Add(new FaceStat(FaceStatKind.HealOverTime, TraitRules.TurnsOf(e), e.Kind));
                    break;
                case EffectKind.ArmorBreak:
                    rest.Add(new FaceStat(FaceStatKind.ArmorBreak, TraitRules.TurnsOf(e), e.Kind));
                    break;
                // 其余(厚积薄发 / 涌泉相报这类按资源结算的)不出格
            }
        }
    }
}
