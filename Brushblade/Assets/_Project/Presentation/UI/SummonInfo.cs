using System;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>召唤物文案的唯一来源(2026-08-15):属性/血攻盾/被动/生克。战斗页点召唤物看详情。
    /// 与 <see cref="EnemyInfo"/> 同惯例——文案写精确数值,平衡改动时需同步这里;
    /// 但生克倍率读 <see cref="WuxingResolver.KeMultiplier"/> 现取,不写死。</summary>
    public static class SummonInfo
    {
        /// <summary>弹窗标题:「梅 · 木」。</summary>
        public static string Title(SummonState summon) =>
            $"{summon.Char} · {CharInfo.ElementName(summon.Element)}";

        public static string Detail(SummonState summon)
        {
            var sb = new StringBuilder();
            sb.Append(Strings.T("summon.detail.stats",
                ("hp", summon.Hp), ("maxHp", summon.MaxHp), ("attack", summon.Attack)));
            if (summon.Shield > 0) sb.Append(Strings.T("summon.detail.shield", ("shield", summon.Shield)));
            sb.Append('\n');

            // 顶前排的口径按 BattleEngine:承伤只由**最前的**存活召唤物整个吃下(不溢出),
            // 出手打的是第一个存活敌人。攻 0 的(烓/灶)照常出手,输出全在被动上。
            sb.Append(Strings.T("summon.detail.front_role"));
            sb.Append(summon.Attack > 0
                ? Strings.T("summon.detail.action_attack")
                : Strings.T("summon.detail.action_passive_only"));

            string passive = PassiveText(summon.Passive);
            if (passive.Length > 0) sb.Append(passive).Append('\n');

            string wuxing = WuxingText(summon.Element);
            if (wuxing.Length > 0) sb.Append(wuxing);
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>被动全文。一只召唤物只有一种被动(数据侧如此),故取第一个非零项 ——
        /// 与 BattleView 格子下那行缩写标签(反伤30 / 闪避50%)同一套优先级,只是展开成整句。</summary>
        private static string PassiveText(SummonPassive passive)
        {
            if (passive == null) return "";
            if (passive.OnHitBurn > 0)
                return passive.OnHitBurnAll
                    ? Strings.T("summon.passive.burn_all", ("burn", passive.OnHitBurn))
                    : Strings.T("summon.passive.burn_single", ("burn", passive.OnHitBurn));
            if (passive.Thorns > 0)
                return Strings.T("summon.passive.thorns", ("thorns", passive.Thorns));
            if (passive.HealAlly > 0)
                return Strings.T("summon.passive.heal_ally", ("heal", passive.HealAlly));
            if (passive.OnHitCurse > 0)
                return Strings.T("summon.passive.curse", ("curse", passive.OnHitCurse));
            if (passive.Dodge > 0)
                return Strings.T("summon.passive.dodge", ("dodge", passive.Dodge));
            if (passive.Speed > 100)
                return Strings.T("summon.passive.haste", ("speed", passive.Speed));
            return "";
        }

        /// <summary>生克提示:「克土 ×1.5 · 被金克」。心不在生克环内,返回空串。
        /// 两头都成立 —— 它反击时按克制加成打,敌人打它时也按克制吃伤(见 DamageSummon)。</summary>
        private static string WuxingText(Element self)
        {
            var sb = new StringBuilder();
            foreach (Element other in Enum.GetValues(typeof(Element)))
            {
                if (other == self) continue;
                if (WuxingResolver.KeMultiplier(self, other) > 1f)
                    sb.Append(Strings.T("summon.wuxing.counters",
                        ("element", CharInfo.ElementName(other)),
                        ("multiplier", WuxingResolver.KeMultiplier(self, other).ToString("0.##"))));
            }
            foreach (Element other in Enum.GetValues(typeof(Element)))
            {
                if (other == self) continue;
                if (WuxingResolver.KeMultiplier(other, self) > 1f)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(Strings.T("summon.wuxing.countered_by", ("element", CharInfo.ElementName(other))));
                }
            }
            return sb.ToString();
        }
    }
}
