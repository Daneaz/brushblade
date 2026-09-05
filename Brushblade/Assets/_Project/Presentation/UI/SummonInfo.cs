using System.Collections.Generic;
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
        // ============ 结构化详情(2026-08-31,单位详情轮二 Task 3) ============

        /// <summary>召唤物详情弹窗(稿 UnitAlly.dc.html)。**唯一**方法 —— 此前还有一整套整段
        /// 文本 API(Title/Detail/PassiveText/StatusText/WuxingText),Task 5 把
        /// OnSummonClicked 从 Ui.Modal(SummonInfo.Title/Detail) 改成 UnitSheet.Show 之后那是它们
        /// 唯一的调用点,五个方法随之全部归零调用,2026-09-01 review 删除(全仓库 grep 复核过)。
        /// 数值口径见 task-3-report.md 的逐条对照。</summary>
        public static UnitDetail Sheet(SummonState summon)
        {
            return new UnitDetail
            {
                PortraitPrefix = null, // 召唤物没有立绘管线,稿子自己也写着「立绘待补·现用字牌位」
                FaceChar = summon.Char,
                Element = summon.Element,
                ElementUnknown = false, // 召唤物属性恒为已知(SummonState.Element 不可空),此字段定恒为 false
                Name = summon.Char,
                // 「召唤物」+ 射程(2026-09-03 用户拍板:与敌人详情同模板)。
                // 稿上第二枚标签原是排位(「后排」)+ 稀有度(「白」):排位这一半连同敌人详情
                // 那一半一起去掉了(战场上一眼就能看见站哪,写进标签只是重复,还挤掉真正
                // 要紧的射程 —— 理由见 EnemyInfo.BuildTags);稀有度在 CharDef 上,
                // SummonState 这个运行时实体不携带,Sheet(SummonState) 这个签名够不到。
                Tags = new[]
                {
                    Strings.T("summon.detail.tag_type"),
                    StatusText.OfSummonRange(summon.Passive?.Ranged ?? false).Name,
                },
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
            // 老文本(Detail(SummonState))这里是无条件二选一(action_attack / action_passive_only),
            // 第一版这里只映射了 Attack==0 那一支,Attack>0 那句「攒满行动条时攻击最前的敌人」
            // 静默消失了(2026-09-01 review 抓到)。补上——Attack>0 直接复用现成的
            // summon.detail.action_attack(去掉老文本拼接用的尾部换行符),不新开一个短 key,
            // 因为没有稿子例子撑腰,拟一句更短的反而是发明。
            string attackNote = summon.Attack == 0
                ? Strings.T("summon.detail.figure_attack_passive_note")
                : Strings.T("summon.detail.action_attack").TrimEnd('\n');

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

        /// <summary>「特性 · 被动」列:被动(至多一条,取第一个非零项)+ 射程(无条件)
        /// + 出手形状(偏离单体时才出)。
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
                else if (passive.Regen > 0)
                    name = Strings.T("summon.passive.regen", ("regen", passive.Regen));
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

            // 射程(2026-09-03 用户拍板补:此前召唤物详情整个没有近战/远程标识,
            // 而这正是玩家排兵布阵最要紧的一条 —— 与敌人详情的第一张卡同位置、同模板)。
            // 无条件出条目,与 EnemyInfo.BuildAbilities 消费 OfRange 同理:近战不是「默认到
            // 不值一提」,它决定这只召唤物够不够得着后排的怪。
            var range = StatusText.OfSummonRange(summon.Passive?.Ranged ?? false);
            list.Add(new AbilityEntry
            {
                IconKey = range.IconKey, ChipColor = UnitDetailChip.Positioning,
                Name = range.Name, Desc = range.Desc,
            });

            // 出手目标形状(2026-09-01 review 补:StatusText.OfShape 计划由本方法消费,
            // 但初版漏接,5 条 char.shape.*.desc 文案因此没有入口显示)。OfShape(Single) 返回
            // 全 null 的 None——与 EnemyInfo.BuildAbilities 消费 OfFocus 同一条规则,没有特殊
            // 形状的召唤物(绝大多数)不出这一条,只有横扫/溅射/贯穿/连发/弹射才挂。
            var shape = StatusText.OfShape(summon.Passive?.Shape ?? TargetShape.Single);
            if (shape.Name != null)
                list.Add(new AbilityEntry
                {
                    IconKey = shape.IconKey, ChipColor = UnitDetailChip.Positioning,
                    Name = shape.Name, Desc = shape.Desc,
                });

            // 2026-09-03 用户拍板:删掉老文本 front_role 那条「顶前排:排在最前时替你挡下
            // 敌人的整次攻击」。它讲的是**当前排位**能干什么,而排位一格一格地写在战场上,
            // 详情里重复一遍没有增量;真正该占这个位置的是上面那条射程。
            return list;
        }
    }
}
