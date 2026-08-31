using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>一条状态(稿 UnitFoe.dc.html 的 .st)。IconKey 可能为 null(缺笔/标点/通假/
    /// AP 上限四条走文字 chip)——按 StatusText 的契约,条目本身恒存在,调用方按「有没有
    /// 图标」分支画即可。</summary>
    public sealed class StatusEntry
    {
        public string IconKey;    // null = 走文字 chip
        public string ChipText;   // chip 上的数字或短字(「3」/「−25%」/「缺笔 2/3」)
        public Color ChipColor;
        public string Name;       // 「灼烧 3 层」
        public string Duration;   // 「层数即时长」/「剩 2 回合」/「本场持久」
        public string Desc;
    }

    /// <summary>一条特性或技能(稿的 .abil)。</summary>
    public sealed class AbilityEntry
    {
        public string IconKey;
        public Color ChipColor;
        public string Name;
        public string Desc;
    }

    /// <summary>一张详情的全部内容。三类单位(敌人/召唤物/执笔人)共用,字段为 null 表达
    /// 「这类单位没有这项」——Task 4 的 UnitSheet 只认这一份结构,不认识 EnemyState/SummonState。</summary>
    public sealed class UnitDetail
    {
        public string PortraitPrefix;   // MobAssets 前缀;null = 用 FaceChar 画圆形字头像
        public string FaceChar;
        public Element? Element;        // null = 执笔人(玩家没有五行属性)
        public string Name;
        public string[] Tags;           // 「前排 · 近战」/「文山 ×3.0」
        public string Flavor;           // 一句风味描述;null = 不画
        public int Hp, MaxHp, Shield, ActionMeter;
        public (string label, string value, string note)[] Figures;  // 攻/甲/速/行动
        public List<StatusEntry> Statuses;
        public List<AbilityEntry> Abilities;
        public (Element beats, Element beatenBy)? Wuxing;  // 生克行;执笔人为 null
    }

    /// <summary>EnemyInfo/SummonInfo/PlayerInfo 共用的取词与配色(2026-08-31,单位详情轮二 Task 3)。
    /// 独立成一个内部类而不是三处各写一份:StatusEntry 的 chip 底色/文案只该有一份口径,
    /// 三个 Info 类分别对着敌人/召唤物/玩家的 StatusBag 抄三遍只会越改越漂。
    ///
    /// 颜色分组抄自 <c>docs/design/ui/scenes/StatusGlossary.dc.html</c> 的六个 --gc 分组
    /// (唯一权威;那份稿本身就是给这 20 个 StatusKind 定颜色用的):
    ///   #C53637 持续伤害与增长的威胁(= Theme.Cinnabar,数值核对过,逐位相同)
    ///   #19507F 控制类 debuff(Theme 里没有现成的,新增一个字面量)
    ///   #2E7D46 我方增益 · 守(Theme 里没有精确匹配,新增一个字面量;DoneGreen 是另一支近似色,
    ///            数值对不上,不能借)
    ///   #C9A94A 我方增益 · 攻(= Theme.RarityColor(CardRarity.Gold),核对过)
    ///   #997C3C 攻击模式与站位(= Theme.ElementColor(Element.Earth),数值巧合相同,核对过)
    ///   #3D4E69 能力与被动(= Theme.InkSoft,核对过;与 Theme.AbilityChipColor 的默认分支同色)</summary>
    internal static class UnitDetailChip
    {
        private static readonly Color Control = new(0.098f, 0.314f, 0.498f); // #19507F
        private static readonly Color Guard = new(0.180f, 0.490f, 0.275f);   // #2E7D46

        /// <summary>攻击模式与站位(远程/横扫/穿刺/锁人……)这一类词条的底色,OfRange/OfShape/
        /// OfFocus 的调用方直接读这个常量,不必逐个 switch。</summary>
        public static readonly Color Positioning = Theme.ElementColor(Element.Earth); // #997C3C

        /// <summary>能力/被动类词条(召唤物反伤/闪避/疾,以及找不到更合适分组时的兜底)。</summary>
        public static readonly Color Ability = Theme.InkSoft; // #3D4E69

        /// <summary>状态 chip 底色。只有 SpeedModifier 需要看符号(减速走控制蓝,速度走守御绿),
        /// 其余 19 个 StatusKind 各自固定在一个分组里,与符号无关。</summary>
        public static Color ColorFor(StatusKind kind, int magnitude) => kind switch
        {
            StatusKind.Burn or StatusKind.BurnNoDecay or StatusKind.Bleed => Theme.Cinnabar,
            StatusKind.Freeze or StatusKind.Blind or StatusKind.Silence or StatusKind.Curse
                or StatusKind.ArmorBreak or StatusKind.Seal => Control,
            StatusKind.SpeedModifier => magnitude < 0 ? Control : Guard,
            StatusKind.DefenseBuff or StatusKind.Immunity or StatusKind.Reflect
                or StatusKind.DodgeBuff or StatusKind.HealOverTime => Guard,
            StatusKind.AttackBuff or StatusKind.Morale or StatusKind.CritBuff
                or StatusKind.PierceBuff => Theme.RarityColor(CardRarity.Gold),
            // AP 上限稿上没有归组(它在文字 chip 那份「两处待拍板」清单里,不在六色分组表里)——
            // 与 morale/attack/pierce 同属「我方增益」大类,按气质就近归到金,而不是新开一支色。
            StatusKind.ApBoost => Theme.RarityColor(CardRarity.Gold),
            _ => Theme.InkSoft, // Obsolete 等占位值:StatusText.Of 已对它们返回 None,调用方会
                                 // 在 Name == null 时跳过整条,这里的颜色实际上不会被用到。
        };

        /// <summary>状态 chip 上的短文案。符号/单位的取舍抄自三张权威稿里能直接看到的例子
        /// (灼烧「3」、冻结「」、减速「−50」、诅咒「−25%」、护甲「+18」、免疫「1」、反弹「30%」、
        /// 战意「3」、穿透「+24」、封字「−1AP」);稿子没画到的其余 StatusKind 按同一套符号规则
        /// 外推(正值增益点数记「+」、负值减益点数记「−」、纯计数不记符号、AP 类挂「AP」后缀)——
        /// 这批外推的口径在 task-3-report.md 里逐条列了,不在这里重复注释。</summary>
        public static string TextFor(StatusKind kind, int magnitude) => kind switch
        {
            StatusKind.Burn or StatusKind.Bleed or StatusKind.Immunity
                or StatusKind.Morale or StatusKind.HealOverTime =>
                Strings.T("detail.chip.plain", ("value", magnitude)),
            StatusKind.BurnNoDecay or StatusKind.Freeze or StatusKind.Silence => "",
            StatusKind.SpeedModifier => magnitude < 0
                ? Strings.T("detail.chip.negative", ("value", -magnitude))
                : Strings.T("detail.chip.positive", ("value", magnitude)),
            StatusKind.Blind or StatusKind.Curse =>
                Strings.T("detail.chip.negative_pct", ("value", magnitude)),
            StatusKind.ArmorBreak =>
                Strings.T("detail.chip.negative", ("value", magnitude)),
            StatusKind.Seal =>
                Strings.T("detail.chip.negative_ap", ("value", magnitude)),
            StatusKind.DefenseBuff or StatusKind.PierceBuff =>
                Strings.T("detail.chip.positive", ("value", magnitude)),
            StatusKind.Reflect =>
                Strings.T("detail.chip.plain_pct", ("value", magnitude)),
            StatusKind.DodgeBuff or StatusKind.AttackBuff or StatusKind.CritBuff =>
                Strings.T("detail.chip.positive_pct", ("value", magnitude)),
            StatusKind.ApBoost =>
                Strings.T("detail.chip.positive_ap", ("value", magnitude)),
            _ => "",
        };

        /// <summary>「身上的状态」列表的共用构建:敌人/召唤物/执笔人的 StatusBag 结构完全一样,
        /// 三处各写一遍循环只会越改越漂。Name == null(StatusText.Of 对占位值的兜底)按契约跳过。</summary>
        public static List<StatusEntry> BuildStatuses(StatusBag statuses)
        {
            var list = new List<StatusEntry>();
            foreach (var effect in statuses.All)
            {
                var info = StatusText.Of(effect.Kind, effect.Magnitude, effect.TurnsLeft);
                if (info.Name == null) continue;
                list.Add(new StatusEntry
                {
                    IconKey = info.IconKey,
                    ChipText = TextFor(effect.Kind, effect.Magnitude),
                    ChipColor = ColorFor(effect.Kind, effect.Magnitude),
                    Name = info.Name,
                    Duration = info.Duration,
                    Desc = info.Desc,
                });
            }
            return list;
        }

        /// <summary>生克提示:「克 X ×1.5 · 被 Y 克」还原成 (beats, beatenBy) 元组。
        /// 五行环(木火土金水)里每个属性必然同时有一个「我克」与一个「克我」;心不在环内,
        /// 两头都摇不到,返回 null——与 SummonInfo.WuxingText 的空串同一条件,只是换了个类型。</summary>
        public static (Element beats, Element beatenBy)? WuxingOf(Element? self)
        {
            if (self == null) return null;
            Element? beats = null, beatenBy = null;
            foreach (Element other in Enum.GetValues(typeof(Element)))
            {
                if (other == self.Value) continue;
                if (WuxingResolver.KeMultiplier(self.Value, other) > 1f) beats = other;
                if (WuxingResolver.KeMultiplier(other, self.Value) > 1f) beatenBy = other;
            }
            return beats == null ? null : (beats.Value, beatenBy!.Value);
        }

        /// <summary>「基准 X」+ 若干偏离说明,拼成 Figures 的 note。deltas 为空时返回 null——
        /// 对应稿上「没有偏离就不画 note」的规则(焦痕的甲 0、速 100 时都不带小字)。</summary>
        public static string BaseNote(int baseValue, params string[] deltas)
        {
            var real = new List<string>();
            foreach (var d in deltas) if (!string.IsNullOrEmpty(d)) real.Add(d);
            if (real.Count == 0) return null;
            var text = Strings.T("detail.figure.base", ("value", baseValue));
            foreach (var d in real) text += " · " + d;
            return text;
        }

        public static string DeltaBuffPct(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_buff_pct", ("name", name), ("value", value));
        public static string DeltaDebuffPct(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_debuff_pct", ("name", name), ("value", value));
        public static string DeltaBuffPts(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_buff_pts", ("name", name), ("value", value));
        public static string DeltaDebuffPts(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_debuff_pts", ("name", name), ("value", value));
    }
}
