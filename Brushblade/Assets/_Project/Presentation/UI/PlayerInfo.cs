using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>执笔人详情弹窗的唯一来源(2026-08-31,单位详情轮二 Task 3)。执笔人此前**没有
    /// 任何详情入口**——战斗页只画血条,没有点开看详情这回事,这个类是全新的。
    /// 稿 <c>UnitMe.dc.html</c> 是权威。
    ///
    /// 玩家没有五行属性(五行长在字上,不长在人身上):<see cref="UnitDetail.Element"/> 与
    /// <see cref="UnitDetail.Wuxing"/> 恒为 null——<c>DamagePlayerDirect</c> 收的是算好的伤害,
    /// 不过 <see cref="WuxingResolver.KeMultiplier"/>。</summary>
    public static class PlayerInfo
    {
        /// <summary>稿上「养成技能 · 局外」那四条(每回合行动点/字库容量/起始生命上限/每关护盾)
        /// 需要 <see cref="MetaState"/>——等级、技能等级都不在 BattleEngine 上,战斗引擎只吃
        /// 养成算好的最终数值,不认识「哪一级」「哪个技能」这些养成层概念。</summary>
        public static UnitDetail Sheet(BattleEngine battle, MetaState meta)
        {
            return new UnitDetail
            {
                PortraitPrefix = null, // 执笔人没有立绘管线,稿子写着「立绘待补·现用墨底字块」
                FaceChar = Strings.T("battle.label.player_face"), // 复用战场小名牌同一个字(墨)
                Element = null,
                Name = Strings.T("battle.label.player_name"), // 复用战场小名牌同一个词(执笔人)
                Tags = BuildTags(battle),
                Flavor = Strings.T("player.detail.flavor"),
                Hp = battle.PlayerHp,
                MaxHp = battle.MaxHp,
                Shield = battle.PlayerShield,
                ActionMeter = battle.PlayerActionMeter,
                Figures = BuildFigures(battle, meta),
                Statuses = UnitDetailChip.BuildStatuses(battle.PlayerStatuses),
                Abilities = BuildAbilities(battle, meta),
                Wuxing = null,
            };
        }

        private static string[] BuildTags(BattleEngine battle) => new[]
        {
            Strings.T("player.detail.tag_no_element"),
            Strings.T("player.detail.tag_ap", ("ap", battle.Ap), ("max", battle.ApPerTurn)),
            // 出字 AP 消耗按稀有度定(见 CharDef.ApCostFor),眼下五档统一是 1——
            // 读函数取值而不是硬编码字面量 1,免得哪天稀有度分档后这里悄悄读错。
            Strings.T("player.detail.tag_ap_cost", ("cost", CharDef.ApCostFor(CardRarity.White))),
        };

        /// <summary>战意每层的攻击加成(百分点)。与 <c>BattleEngine</c> 私有常量
        /// <c>MoralePercentPerStack</c> 数值必须保持一致,但那个常量是 private,这里拿不到——
        /// 复制这个数字是有意的,不是偷懒:既有文案 status.morale.desc 里已经把「每层 +10% 攻击」
        /// 写成了玩家可见的事实(等于说这个数字本来就是公开的),PlayerInfo 这里再抄一份不算
        /// 新泄露信息,只是同一个已公开事实又写了一份。⚠ 这个数字改的话,BattleEngine.cs 的
        /// MoralePercentPerStack、strings 表的 status.morale.desc、这里三处都要一起改。</summary>
        private const int MoralePercentPerStack = 10;

        /// <summary>攻/甲/暴击/速四格。攻直接读 <see cref="BattleEngine.EffectiveAttack"/>——
        /// 稿子点名要求的口径,不在这里重新拼一遍公式。基准值(角色成长曲线)另算,
        /// 因为 BattleEngine 不对外报 config 里的原始 PlayerAttack/PlayerDefense/PlayerSpeed——
        /// 战斗引擎只暴露「有效值」,这几条基准借 MetaRules 的同名成长函数复原
        /// (与 MetaRules.BuildBattleConfig 当初写进 config 的是同一条公式,单一来源,不会漂)。
        ///
        /// ⚠ 判据是"note 的各项拼起来要能推出大字"(2026-09-01 review 定的线,敌人那格已经
        /// 满足:BaseAttack × (100+buff%−curse%)/100)。玩家这格第一版没做到,已改:
        /// EffectiveAttack 的真实公式是 flat = PlayerAttack + AttackBuff(点数,不是百分比!)
        /// 然后 × (100 + 战意% ) / 100(见 BattleEngine.EffectiveAttack 的源码)。
        /// 所以 note 现在拼成「基准 X · 攻击 +N(点数)· 战意 +M%」,读的人能自己算:
        /// (X+N) × (100+M) / 100(整数除)= 显示的大字,与敌人那格同一个可验证标准。</summary>
        private static (string, string, string)[] BuildFigures(BattleEngine battle, MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            var statuses = battle.PlayerStatuses;

            // AttackBuff 在玩家侧是加点数(与 PlayerAttack 同一个数量级相加),不是百分比——
            // 这是玩家侧 EffectiveAttack 与敌人侧 EnemyState.Attack 唯一不同的地方,虽然两边
            // 用的是同一个 StatusKind.AttackBuff,口径却不一样,不能照抄敌人那边的格式化。
            int attackBuffPts = statuses.TotalMagnitude(StatusKind.AttackBuff);
            int moraleLayers = statuses.TotalMagnitude(StatusKind.Morale);
            int moralePercent = moraleLayers * MoralePercentPerStack;
            string attackNote = UnitDetailChip.BaseNote(MetaRules.AttackFor(level),
                UnitDetailChip.DeltaBuffPts(Strings.T("status.attack.name"), attackBuffPts),
                UnitDetailChip.DeltaBuffPct(Strings.T("status.morale.name"), moralePercent));

            int defenseBuff = statuses.TotalMagnitude(StatusKind.DefenseBuff);
            int armorBreak = statuses.TotalMagnitude(StatusKind.ArmorBreak);
            string defenseNote = UnitDetailChip.BaseNote(MetaRules.DefenseFor(level),
                UnitDetailChip.DeltaBuffPts(Strings.T("status.defense.name"), defenseBuff),
                UnitDetailChip.DeltaDebuffPts(Strings.T("status.armorbreak.name"), armorBreak));

            int speedMod = statuses.TotalMagnitude(StatusKind.SpeedModifier);
            string speedNote = speedMod == 0 ? null : UnitDetailChip.BaseNote(MetaRules.SpeedFor(level),
                speedMod < 0
                    ? UnitDetailChip.DeltaDebuffPts(Strings.T("status.slow.name"), -speedMod)
                    : UnitDetailChip.DeltaBuffPts(Strings.T("status.speed.name"), speedMod));

            // 甲带 "+" 前缀(有值才带),与召唤物那格统一——UnitMe.dc.html 稿上写的就是「甲 +18」。
            int defenseValue = battle.EffectivePlayerDefense;
            string defenseText = defenseValue > 0 ? "+" + defenseValue : "0";

            return new[]
            {
                (Strings.T("char.stat.attack"), battle.EffectiveAttack.ToString(), attackNote),
                (Strings.T("char.stat.defense"), defenseText, defenseNote),
                (Strings.T("status.crit.name"), battle.EffectiveCrit + "%",
                    Strings.T("player.detail.crit_multiplier_note")),
                (Strings.T("char.stat.speed"), battle.EffectivePlayerSpeed.ToString(), speedNote),
            };
        }

        /// <summary>「特性 · 技能」列:护盾说明(资源,不是状态)+ 养成技能 · 局外四条
        /// (每回合行动点/字库容量/起始生命上限/每关护盾)。四条各自独立一张卡,
        /// 稿上共享一个「养成技能 · 局外」小标题——那个分组标题怎么画是 Task 4 的事,
        /// 这里只按顺序把四条数据吐出来。</summary>
        private static List<AbilityEntry> BuildAbilities(BattleEngine battle, MetaState meta)
        {
            var list = new List<AbilityEntry>();
            if (battle.PlayerShield > 0)
                list.Add(new AbilityEntry
                {
                    IconKey = "shield", ChipColor = Theme.RarityColor(CardRarity.Gold),
                    Name = Strings.T("player.detail.shield_name", ("value", battle.PlayerShield)),
                    Desc = Strings.T("player.detail.shield_desc"),
                });

            // 四条各自直写 Strings.T(字面量 key, ...)、不抽 (perkId, descKey) 参数化的共用方法——
            // StringsTableTests 扫的是紧跟在 T( 后面的字符串字面量,key 从变量传进去它认不出来,
            // 会被判成没人用的孤儿(StatusText.cs 的注释早就点过这个坑;第一版这里图省事把
            // key 做成了参数,工装跑一遍就红,改回逐条直写)。
            // 等级读 PerkRules.PerkLevel;数值走各自的 MetaRules/PerkRules 公式——与
            // MetaRules.BuildBattleConfig 当初把这些值写进战斗配置时用的是同一条公式,不另起
            // 一套算法。稿上的 Lv.3/Lv.4/Lv.6/Lv.2 只是画图时的示例数字,这里一律读 meta 现算。
            list.Add(new AbilityEntry
            {
                IconKey = null, ChipColor = UnitDetailChip.Ability,
                Name = Strings.T("player.detail.perk_ap_name"),
                Desc = Strings.T("player.detail.perk_ap_desc",
                    ("level", PerkRules.PerkLevel(meta, "yiqi")),
                    ("value", MetaRules.BaseApPerTurn + PerkRules.ApBonus(meta))),
            });
            list.Add(new AbilityEntry
            {
                IconKey = null, ChipColor = UnitDetailChip.Ability,
                Name = Strings.T("player.detail.perk_library_name"),
                Desc = Strings.T("player.detail.perk_library_desc",
                    ("level", PerkRules.PerkLevel(meta, "bowen")),
                    ("value", MetaRules.LibraryCapacityFor(meta))),
            });
            list.Add(new AbilityEntry
            {
                IconKey = null, ChipColor = UnitDetailChip.Ability,
                Name = Strings.T("player.detail.perk_hp_name"),
                Desc = Strings.T("player.detail.perk_hp_desc",
                    ("level", PerkRules.PerkLevel(meta, "yangyuan")),
                    ("value", MetaRules.PlayerMaxHpFor(meta))),
            });
            list.Add(new AbilityEntry
            {
                IconKey = null, ChipColor = UnitDetailChip.Ability,
                Name = Strings.T("player.detail.perk_shield_name"),
                Desc = Strings.T("player.detail.perk_shield_desc",
                    ("level", PerkRules.PerkLevel(meta, "jintang")),
                    ("value", PerkRules.ShieldBonus(meta))),
            });
            return list;
        }
    }
}
