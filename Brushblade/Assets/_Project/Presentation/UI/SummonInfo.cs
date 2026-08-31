using System;
using System.Collections.Generic;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

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
            // 攻击显示**有效值**(2026-08-28):玩家可以给它挂 战(Empower),显示基础值会
            // 与实际打出的伤害对不上。读 SummonState.EffectiveAttack 而不是在这里自己加 ——
            // 规则只有一份,见那个属性的文档。
            sb.Append(Strings.T("summon.detail.stats",
                ("hp", summon.Hp), ("maxHp", summon.MaxHp), ("attack", summon.EffectiveAttack)));
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

            string statuses = StatusText(summon);
            if (statuses.Length > 0) sb.Append(statuses).Append('\n');

            string wuxing = WuxingText(summon.Element);
            if (wuxing.Length > 0) sb.Append(wuxing);
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>身上现挂着的状态(2026-08-28,增益改单体之后)。格子右翼那一列只放得下
        /// 一个「益+N」汇总 chip(58px / 字号 8),完整清单在这里 —— 与被动缩写标签同一套分工。
        ///
        /// 只列**引擎真读得到**的那几条:护甲/暴击/穿透/免疫/反弹是玩家能挂上来的七条增益里
        /// 有数值可报又不重复的五条(净化不留状态,攻击已并进上面那行的有效值),灼烧是敌人挂的。战意/利/燥 是玩家专属,
        /// 召唤物身上不会有,不必列。
        /// 攻击增益不在这里重复报 —— 上面那行的 attack 已经是含它的有效值。</summary>
        /// ⚠ 逐条直写而不是抽个 (kind, key) 的 helper:StringsTableTests 扫的是**紧跟在
        /// 取词函数后面的字符串字面量**,key 从变量传进去它认不出来,那几个 key 会被当成
        /// 没人用的孤儿(2026-08-28 已被逮过一次)。玩家侧 DrawPlayerStats 也是逐条 if。
        /// 连这段注释里都不能写出那个形状 —— 扫描器不分辨注释与代码,会把它当成真调用,
        /// 反过来报「调用了表里没有的 key」(同一次也栽过)。
        private static string StatusText(SummonState summon)
        {
            var sb = new StringBuilder();
            int defense = summon.Statuses.TotalMagnitude(StatusKind.DefenseBuff);
            if (defense > 0) sb.Append(Strings.T("summon.status.defense", ("value", defense)));
            int crit = summon.Statuses.TotalMagnitude(StatusKind.CritBuff);
            if (crit > 0) sb.Append(Strings.T("summon.status.crit", ("value", crit)));
            int pierce = summon.Statuses.TotalMagnitude(StatusKind.PierceBuff);
            if (pierce > 0) sb.Append(Strings.T("summon.status.pierce", ("value", pierce)));
            int immunity = summon.Statuses.TotalMagnitude(StatusKind.Immunity);
            if (immunity > 0) sb.Append(Strings.T("summon.status.immunity", ("value", immunity)));
            int reflect = summon.Statuses.TotalMagnitude(StatusKind.Reflect);
            if (reflect > 0) sb.Append(Strings.T("summon.status.reflect", ("value", reflect)));
            int burn = summon.Statuses.TotalMagnitude(StatusKind.Burn);
            if (burn > 0) sb.Append(Strings.T("summon.status.burn", ("value", burn)));
            return sb.Length == 0 ? "" : Strings.T("summon.detail.status_header") + sb;
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

        // ============ 结构化详情(2026-08-31,单位详情轮二 Task 3) ============

        /// <summary>召唤物详情弹窗(稿 UnitAlly.dc.html)。**追加**方法,<see cref="Detail(SummonState)"/>
        /// 以上那个整段文本版本一个不动(战斗页点召唤物看老版详情的调用点还在用)。
        /// 数值口径与老文本一致,逐条对照见 task-3-report.md。</summary>
        public static UnitDetail StructuredDetail(SummonState summon)
        {
            return new UnitDetail
            {
                PortraitPrefix = null, // 召唤物没有立绘管线,稿子自己也写着「立绘待补·现用字牌位」
                FaceChar = summon.Char,
                Element = summon.Element,
                Name = summon.Char,
                // 稿上第二枚标签是排位(「后排」)+ 稀有度(「白」),两者 SummonState 都没有——
                // 排位是 BattleEngine._summons 的数组下标,稀有度在 CharDef 上,SummonState 一个
                // 运行时战斗实体两样都不携带,Detail(SummonState) 这个签名够不到,只给类型标签。
                Tags = new[] { Strings.T("summon.detail.tag_type") },
                Flavor = null, // CharDef.Gloss(短释义)是候选来源,但要查 RecipeGraph,签名拿不到
                Hp = summon.Hp,
                MaxHp = summon.MaxHp,
                Shield = summon.Shield,
                ActionMeter = summon.ActionMeter,
                Figures = BuildFigures(summon),
                Statuses = UnitDetailChip.BuildStatuses(summon.Statuses),
                Abilities = BuildAbilities(summon),
                Wuxing = UnitDetailChip.WuxingOf(summon.Element),
            };
        }

        /// <summary>攻/盾/甲/速四格。攻的口径与老文本 <see cref="Detail(SummonState)"/> 一致
        /// (读 EffectiveAttack,不是 Attack)——这是稿子标出来的既有规则(见那个属性的文档),
        /// 这里不重新推一遍。</summary>
        private static (string, string, string)[] BuildFigures(SummonState summon)
        {
            string attackNote = summon.Attack == 0
                ? Strings.T("summon.detail.figure_attack_passive_note")
                : null; // Attack>0 时老文本也没有额外口径可对照,不外推一个没有例子撑腰的说法

            int defenseValue = summon.EffectiveDefense;
            string defenseNote = defenseValue > 0
                ? Strings.T("summon.detail.figure_defense_note")
                : null; // 召唤物没有基础护甲字段,EffectiveDefense 全部来自增益,这句恒成立

            return new[]
            {
                (Strings.T("char.stat.attack"), summon.EffectiveAttack.ToString(), attackNote),
                (Strings.T("char.stat.shield"), summon.Shield.ToString(), (string)null),
                (Strings.T("char.stat.defense"),
                    defenseValue > 0 ? "+" + defenseValue : "0", defenseNote),
                (Strings.T("char.stat.speed"), summon.Speed.ToString(), (string)null),
            };
        }

        /// <summary>「特性 · 被动」列:被动(至多一条,取第一个非零项,与 <see cref="PassiveText"/>
        /// 同一优先级)+ 顶前排(老文本无条件追加,这里同理)。
        ///
        /// 被动的 Name 直接复用现成的 summon.passive.* 整句(标签+冒号+说明合一),Desc 留空——
        /// 那几个 key 本来就是「一句话」的形状,拆 Name/Desc 要么另开新 key、要么在运行时切
        /// 字符串,Ruling ⑥「直接读字段配现成 key」批的是复用,没有批准拆分,所以整句放 Name。</summary>
        private static List<AbilityEntry> BuildAbilities(SummonState summon)
        {
            var list = new List<AbilityEntry>();
            var passive = summon.Passive;
            if (passive != null)
            {
                string iconKey = null;
                string name = null;
                if (passive.OnHitBurn > 0)
                    name = passive.OnHitBurnAll
                        ? Strings.T("summon.passive.burn_all", ("burn", passive.OnHitBurn))
                        : Strings.T("summon.passive.burn_single", ("burn", passive.OnHitBurn));
                else if (passive.Thorns > 0)
                {
                    iconKey = "thorns";
                    name = Strings.T("summon.passive.thorns", ("thorns", passive.Thorns));
                }
                else if (passive.HealAlly > 0)
                    name = Strings.T("summon.passive.heal_ally", ("heal", passive.HealAlly));
                else if (passive.OnHitCurse > 0)
                    name = Strings.T("summon.passive.curse", ("curse", passive.OnHitCurse));
                else if (passive.Dodge > 0)
                {
                    iconKey = "dodge";
                    name = Strings.T("summon.passive.dodge", ("dodge", passive.Dodge));
                }
                else if (passive.Speed > 100)
                {
                    iconKey = "speed";
                    name = Strings.T("summon.passive.haste", ("speed", passive.Speed));
                }

                if (name != null)
                    list.Add(new AbilityEntry
                    {
                        IconKey = iconKey, ChipColor = UnitDetailChip.Ability, Name = name, Desc = null,
                    });
            }

            // 顶前排是老文本 front_role 的原句,同样只放 Name——那句话本身就是「标签:说明」
            // 一体的形状(见上面的理由),稿上还带一句「它现在在后排」,SummonState 没有排位信息
            // (槽位是 BattleEngine._summons 的数组下标),够不到,略去。
            list.Add(new AbilityEntry
            {
                IconKey = null, ChipColor = UnitDetailChip.Ability,
                Name = Strings.T("summon.detail.front_role").TrimEnd('\n'), Desc = null,
            });
            return list;
        }
    }
}
