using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>词条(Glossary,Plan E4 附录 §C)的文案与正文上色。文案唯一来源是 spec §3,字符串表每条一个 key
    /// (term.&lt;id&gt;.name / .desc / .alias,id 为 ASCII,C-Q11)。
    ///
    /// ⚠ 三个 switch 逐条写字面量 key,不拼 key:字符串表的对账扫描只认紧跟取词函数的字面量,
    /// 拼出来的 key 会被当成孤儿(记忆 strings_table_scan_is_literal_only)。
    /// 「每个术语都有 name / desc」由 Tests/GlossaryStringsTests 按约定拼 key 兜底。
    ///
    /// 扫描与排除上下文在 Data(<see cref="TermMarkup"/>),这里只把字符串表喂进去。
    /// 测量与截断一律吃纯文本;<see cref="Rich"/> 只在最后赋 Text.text 时调用。</summary>
    public static class GlossaryText
    {
        public static string Name(GlossaryTerm term) => term switch
        {
            GlossaryTerm.Burn => Strings.T("term.burn.name"),
            GlossaryTerm.Detonate => Strings.T("term.detonate.name"),
            GlossaryTerm.Freeze => Strings.T("term.freeze.name"),
            GlossaryTerm.FrostResist => Strings.T("term.frost_resist.name"),
            GlossaryTerm.IceStall => Strings.T("term.ice_stall.name"),
            GlossaryTerm.Slow => Strings.T("term.slow.name"),
            GlossaryTerm.Morale => Strings.T("term.morale.name"),
            GlossaryTerm.Block => Strings.T("term.block.name"),
            GlossaryTerm.Wellspring => Strings.T("term.wellspring.name"),
            GlossaryTerm.Heft => Strings.T("term.heft.name"),
            GlossaryTerm.Shield => Strings.T("term.shield.name"),
            GlossaryTerm.Armor => Strings.T("term.armor.name"),
            GlossaryTerm.ArmorBreak => Strings.T("term.armor_break.name"),
            GlossaryTerm.Hot => Strings.T("term.hot.name"),
            GlossaryTerm.Seed => Strings.T("term.seed.name"),
            GlossaryTerm.Spirit => Strings.T("term.spirit.name"),
            GlossaryTerm.Nature => Strings.T("term.nature.name"),
            GlossaryTerm.Sapling => Strings.T("term.sapling.name"),
            GlossaryTerm.Graft => Strings.T("term.graft.name"),
            GlossaryTerm.Taunt => Strings.T("term.taunt.name"),
            GlossaryTerm.Execute => Strings.T("term.execute.name"),
            GlossaryTerm.Bleed => Strings.T("term.bleed.name"),
            GlossaryTerm.Doom => Strings.T("term.doom.name"),
            GlossaryTerm.Charm => Strings.T("term.charm.name"),
            GlossaryTerm.Mark => Strings.T("term.mark.name"),
            GlossaryTerm.Weaken => Strings.T("term.weaken.name"),
            GlossaryTerm.DamageCut => Strings.T("term.damage_cut.name"),
            GlossaryTerm.CounterBoost => Strings.T("term.counter_boost.name"),
            GlossaryTerm.Endure => Strings.T("term.endure.name"),
            GlossaryTerm.Thorns => Strings.T("term.thorns.name"),
            GlossaryTerm.Crit => Strings.T("term.crit.name"),
            GlossaryTerm.Trickle => Strings.T("term.trickle.name"),
            GlossaryTerm.Surge => Strings.T("term.surge.name"),
            GlossaryTerm.Revive => Strings.T("term.revive.name"),
            GlossaryTerm.Speed => Strings.T("term.speed.name"),
            GlossaryTerm.SpendHeft => Strings.T("term.spend_heft.name"),
            GlossaryTerm.SpendWellspring => Strings.T("term.spend_wellspring.name"),
            GlossaryTerm.Kindle => Strings.T("term.kindle.name"),   // spec C §5.3
            GlossaryTerm.AreaSingle => Strings.T("term.area_single.name"),
            GlossaryTerm.AreaAdjacent => Strings.T("term.area_adjacent.name"),
            GlossaryTerm.AreaRow => Strings.T("term.area_row.name"),
            GlossaryTerm.AreaColumn => Strings.T("term.area_column.name"),
            GlossaryTerm.AreaScatter => Strings.T("term.area_scatter.name"),
            GlossaryTerm.AreaChain => Strings.T("term.area_chain.name"),
            GlossaryTerm.AreaAll => Strings.T("term.area_all.name"),
            GlossaryTerm.Battle => Strings.T("term.battle.name"),
            GlossaryTerm.Carry => Strings.T("term.carry.name"),
            GlossaryTerm.Band => Strings.T("term.band.name"),
            _ => term.ToString(),   // 漏分支印枚举名:宁可难看,不要静默(同 CharInfo.EffectsText 的兜底约定)
        };

        public static string Desc(GlossaryTerm term) => term switch
        {
            GlossaryTerm.Burn => Strings.T("term.burn.desc"),
            GlossaryTerm.Detonate => Strings.T("term.detonate.desc"),
            GlossaryTerm.Freeze => Strings.T("term.freeze.desc"),
            GlossaryTerm.FrostResist => Strings.T("term.frost_resist.desc"),
            GlossaryTerm.IceStall => Strings.T("term.ice_stall.desc"),
            GlossaryTerm.Slow => Strings.T("term.slow.desc"),
            GlossaryTerm.Morale => Strings.T("term.morale.desc"),
            GlossaryTerm.Block => Strings.T("term.block.desc"),
            GlossaryTerm.Wellspring => Strings.T("term.wellspring.desc"),
            GlossaryTerm.Heft => Strings.T("term.heft.desc"),
            GlossaryTerm.Shield => Strings.T("term.shield.desc"),
            GlossaryTerm.Armor => Strings.T("term.armor.desc"),
            GlossaryTerm.ArmorBreak => Strings.T("term.armor_break.desc"),
            GlossaryTerm.Hot => Strings.T("term.hot.desc"),
            GlossaryTerm.Seed => Strings.T("term.seed.desc"),
            GlossaryTerm.Spirit => Strings.T("term.spirit.desc"),
            GlossaryTerm.Nature => Strings.T("term.nature.desc"),
            GlossaryTerm.Sapling => Strings.T("term.sapling.desc"),
            GlossaryTerm.Graft => Strings.T("term.graft.desc"),
            GlossaryTerm.Taunt => Strings.T("term.taunt.desc"),
            GlossaryTerm.Execute => Strings.T("term.execute.desc"),
            GlossaryTerm.Bleed => Strings.T("term.bleed.desc"),
            GlossaryTerm.Doom => Strings.T("term.doom.desc"),
            GlossaryTerm.Charm => Strings.T("term.charm.desc"),
            GlossaryTerm.Mark => Strings.T("term.mark.desc"),
            GlossaryTerm.Weaken => Strings.T("term.weaken.desc"),
            GlossaryTerm.DamageCut => Strings.T("term.damage_cut.desc"),
            GlossaryTerm.CounterBoost => Strings.T("term.counter_boost.desc"),
            GlossaryTerm.Endure => Strings.T("term.endure.desc"),
            GlossaryTerm.Thorns => Strings.T("term.thorns.desc"),
            GlossaryTerm.Crit => Strings.T("term.crit.desc"),
            GlossaryTerm.Trickle => Strings.T("term.trickle.desc"),
            GlossaryTerm.Surge => Strings.T("term.surge.desc"),
            GlossaryTerm.Revive => Strings.T("term.revive.desc"),
            GlossaryTerm.Speed => Strings.T("term.speed.desc"),
            GlossaryTerm.SpendHeft => Strings.T("term.spend_heft.desc"),
            GlossaryTerm.SpendWellspring => Strings.T("term.spend_wellspring.desc"),
            GlossaryTerm.Kindle => Strings.T("term.kindle.desc"),
            GlossaryTerm.AreaSingle => Strings.T("term.area_single.desc"),
            GlossaryTerm.AreaAdjacent => Strings.T("term.area_adjacent.desc"),
            GlossaryTerm.AreaRow => Strings.T("term.area_row.desc"),
            GlossaryTerm.AreaColumn => Strings.T("term.area_column.desc"),
            GlossaryTerm.AreaScatter => Strings.T("term.area_scatter.desc"),
            GlossaryTerm.AreaChain => Strings.T("term.area_chain.desc"),
            GlossaryTerm.AreaAll => Strings.T("term.area_all.desc"),
            GlossaryTerm.Battle => Strings.T("term.battle.desc"),
            GlossaryTerm.Carry => Strings.T("term.carry.desc"),
            GlossaryTerm.Band => Strings.T("term.band.desc"),
            _ => term.ToString(),
        };

        /// <summary>正文里的别名(多个用「|」隔开):机械拼句的写法与词条名不同的那几条(附录 §C 2.1)。null = 无别名。</summary>
        private static string Alias(GlossaryTerm term) => term switch
        {
            GlossaryTerm.Burn => Strings.T("term.burn.alias"),
            GlossaryTerm.Hot => Strings.T("term.hot.alias"),
            GlossaryTerm.Spirit => Strings.T("term.spirit.alias"),
            GlossaryTerm.Weaken => Strings.T("term.weaken.alias"),
            GlossaryTerm.Thorns => Strings.T("term.thorns.alias"),
            _ => null,
        };

        /// <summary>气泡里词名后印的别名(第一个,执行期决定 D15);无别名 = null。</summary>
        public static string AliasShown(GlossaryTerm term)
        {
            string aliases = Alias(term);
            if (string.IsNullOrEmpty(aliases)) return null;
            int bar = aliases.IndexOf('|');
            return bar < 0 ? aliases : aliases.Substring(0, bar);
        }

        private static IReadOnlyList<TermSurface> _lexicon;

        /// <summary>词表(显示名 + 别名 + 排除上下文)。字符串表在启动时载入一次、之后不变,所以缓存。</summary>
        public static IReadOnlyList<TermSurface> Lexicon() => _lexicon ??= TermMarkup.BuildLexicon(
            Enum.GetValues(typeof(GlossaryTerm)).Cast<GlossaryTerm>().Select(t => (t, Name(t), Alias(t))));

        public static TermScan Scan(string plain) => TermMarkup.Scan(plain, Lexicon());

        /// <summary>一行(可能分两小句)的术语:按首次出现的顺序合并去重(R5)。</summary>
        public static IReadOnlyList<GlossaryTerm> TermsOf(IEnumerable<string> plains)
        {
            var terms = new List<GlossaryTerm>();
            foreach (var plain in plains)
                foreach (var t in Scan(plain).Terms)
                    if (!terms.Contains(t)) terms.Add(t);
            return terms;
        }

        /// <summary>同 <see cref="TermsOf(IEnumerable{string})"/>,但 <paramref name="lead"/> 排在最前(Lv1 行的「本命」「润泽」,spec B §1.4)。</summary>
        public static IReadOnlyList<GlossaryTerm> TermsOf(IEnumerable<string> plains, IEnumerable<GlossaryTerm> lead)
        {
            var terms = new List<GlossaryTerm>();
            foreach (var t in lead)
                if (!terms.Contains(t)) terms.Add(t);
            foreach (var t in TermsOf(plains))
                if (!terms.Contains(t)) terms.Add(t);
            return terms;
        }

        /// <summary>正文上色(R1–R3):on = 术语加粗 <paramref name="termColor"/>(缺省 ink-soft;字卡详情传本系 -fg,abilitytext A 案)、
        /// 数字加粗 text-main;<paramref name="muted"/>(off / spent)只保留加粗,颜色跟随整句。入参必须是纯文本。</summary>
        public static string Rich(string plain, bool muted, Color? termColor = null)
        {
            var scan = Scan(plain);
            if (muted) return TermMarkup.Rich(plain, scan, "<b>", "</b>", "<b>", "</b>");
            string termOpen = "<b><color=#" + ColorUtility.ToHtmlStringRGBA(termColor ?? Theme.InkSoft) + ">";
            return TermMarkup.Rich(plain, scan, termOpen, "</color></b>", NumOpen, "</color></b>");
        }

        private static string NumOpen => "<b><color=#" + ColorUtility.ToHtmlStringRGBA(Theme.TextMain) + ">";
    }
}
