using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>预估签的文案(Plan E4 Task 4,HandFlip / WaterEarthCues / PartsAndGraft 稿)。
    /// 取哪几段由 Core 的 <see cref="EstimateLabelRules"/> 定(coretests 覆盖),这里只把段落换成字符串表文案:
    /// 主串 = 「[全体 ]主段[ ×倍率][ · N 段][ · 状态段]」,压浅串 = 「[ · ]X已用」(白 55%,单独一个 Text)。
    /// 两串分开给,宽度由调用方按两串拼起来的纯文本算(不用 rich text,R6)。</summary>
    internal static class EstimateText
    {
        /// <summary>悬停敌人那一格的签;不出签 → null。</summary>
        public static (string Main, string Dim)? ForEnemy(CastEstimate est, int hoverIndex) =>
            Build(EstimateLabelRules.ForEnemy(est, hoverIndex));

        /// <summary>落在友方(玩家 / 木灵)那一格的签;不出签 → null。</summary>
        public static (string Main, string Dim)? ForAlly(CastEstimate est) =>
            Build(EstimateLabelRules.ForAlly(est));

        /// <summary>只要「X已用」段(落木灵的结果签后面追加,A-Q11);没有已用 → null。</summary>
        public static string SpentOnly(CastEstimate est, bool afterMain)
        {
            var spent = EstimateLabelRules.SpentOf(est);
            return spent == null ? null : SpentText(spent, afterMain);
        }

        public static (string Main, string Dim)? Build(EstimateLabel label)
        {
            if (label == null) return null;
            string main = null;
            if (label.Main is { } head)
            {
                string first = Seg(head);
                if (first != null)
                {
                    main = first;
                    if (label.Ke) main += " " + Strings.T("battle.estimate.ke");
                    else if (label.Countered) main += " " + Strings.T("battle.estimate.countered");
                    if (label.Hits >= 2) main += Strings.T("battle.estimate.sep") + Strings.T("battle.estimate.hits", ("n", label.Hits));
                }
            }
            if (main != null && label.Status is { } status && Seg(status) is { } statusText)
                main += Strings.T("battle.estimate.sep") + statusText;
            if (main != null && label.All) main = Strings.T("char.shape.all") + " " + main;
            string dim = label.Spent == null ? null : SpentText(label.Spent, afterMain: main != null);
            if (main == null && dim == null) return null;
            return (main, dim);
        }

        private static string SpentText(TraitDef trait, bool afterMain) =>
            (afterMain ? Strings.T("battle.estimate.sep") : "") + Strings.T("battle.estimate.spent", ("name", trait.Name));

        /// <summary>一段的文案。⚠ default 返回 null(不出段),不许回落到枚举名(R5)。</summary>
        private static string Seg(EstimateSeg seg)
        {
            switch (seg.Kind)
            {
                case EstimateSegKind.Damage: return Strings.T("battle.estimate.damage", ("n", seg.Amount));
                case EstimateSegKind.Burn: return Strings.T("battle.estimate.burn", ("n", seg.Amount));
                case EstimateSegKind.Shield: return Strings.T("battle.estimate.shield", ("n", seg.Amount));
                case EstimateSegKind.Heal: return Strings.T("battle.estimate.heal", ("n", seg.Amount));
                case EstimateSegKind.Morale: return Strings.T("battle.estimate.morale", ("n", seg.Amount));
                case EstimateSegKind.Block: return Strings.T("battle.landing.block", ("n", seg.Amount));
                case EstimateSegKind.BlockPerMorale: return Strings.T("battle.landing.block.morale");
                case EstimateSegKind.DamageCut: return Strings.T("battle.estimate.damagecut", ("n", seg.Amount));
                case EstimateSegKind.HealOverTime: return Strings.T("battle.estimate.hot");
                case EstimateSegKind.Taunt: return Strings.T("status.taunt.name");
                case EstimateSegKind.Immunity: return Strings.T("status.immunity.name");
                case EstimateSegKind.Slow: return Strings.T("status.slow.name");
                case EstimateSegKind.Freeze: return Strings.T("status.freeze.name");
                case EstimateSegKind.ArmorBreak: return Strings.T("status.armorbreak.name");
                case EstimateSegKind.Sunder: return Strings.T("status.sunder.name");
                case EstimateSegKind.Seed: return Strings.T("status.seed.name");
                case EstimateSegKind.Weaken: return Strings.T("status.curse.name");
                case EstimateSegKind.Vulnerable: return Strings.T("status.mark.name");
                case EstimateSegKind.Blind: return Strings.T("status.blind.name");
                case EstimateSegKind.Silence: return Strings.T("status.silence.name");
                case EstimateSegKind.Charm: return Strings.T("status.charm.name");
                case EstimateSegKind.Bleed: return Strings.T("status.bleed.name");
                default: return null;
            }
        }
    }
}
