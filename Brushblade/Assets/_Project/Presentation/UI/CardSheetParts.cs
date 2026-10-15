using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情改版(Plan E2)的部件:等级条 <see cref="LevelLadder"/>、一面 <see cref="FacePanel"/>
    /// (面头 + 数值格 + 本面特性行)、特性行 <see cref="TraitRow"/>(on / off / spent 三态 + 待补占位)。
    /// 全平铺:两面逐级同高(spec B,稿 <c>docs/design/drafts/abilitytext</c>)—— 每一级两面各一格,同级行高取两面较大值。
    /// 稿 <c>docs/design/drafts/traits/CardSheet.dc.html</c> / <c>CardSheetBattle.dc.html</c>;
    /// 组件卡 <c>component/TraitRow</c>、<c>component/LevelLadder</c>、<c>component/FaceSeal</c>。
    ///
    /// 「显示哪几行、取哪几格」全在 Core(<see cref="CardSheetRules"/>、<see cref="FaceStats"/>),这里只排版。
    /// 高度一律**先算后画**(<see cref="LevelHeights"/> / <see cref="FaceHeight"/>):两面要等高、同级行要等高,
    /// 行高要按实际面宽折行(16:9 下面内宽只有 431),靠布局组事后撑高做不到「两面取较高的那一面」。
    ///
    /// 点击只有一种:有术语的特性行 / 单术语数值格点了开词条气泡(Plan E4 Glossary,<see cref="GlossaryBubble"/>),
    /// 回调由调用方传(<see cref="TermsTap"/>,null = 不挂)。战斗长按版也挂(C-Q1):点行只开气泡、不出字,
    /// 不破坏「长按只看不出手」(<see cref="HoldToPreview"/>)。没有术语的行 / 格不挂、也没有按下态(R7)。
    /// 正文 <see cref="RowModel.Lines"/> 始终是**纯文本**:测量、截断、比较都吃它,上色只在最后赋 Text.text 时做。</summary>
    public static class CardSheetParts
    {
        /// <summary>两版尺寸(稿 pt × 2.093 = 逻辑单位)。铺满版 = 卡组 / 开箱 / 商城;战斗版 = 长按。</summary>
        public sealed class Sizes
        {
            public bool Battle;
            // 等级条(component/LevelLadder)
            public float Pip, PipGap = 6f, Bar, BarRadius, Mark, Mark2, MarkGap, LadderVGap, LadderNum;
            // 一面(.face / .fh)
            public float FacePadV, FacePadH = 21f, FaceGap = 10.5f, FaceRadius = 19f;
            public float HeadH, Seal, HeadGap;
            public int Role, Landing;
            // 数值格(.stats / .sb)
            public float StatH = 65f, StatGap;
            public int StatK, StatV = 31, StatUnit, StatPadH;
            // 特性行(component/TraitRow;战斗版按 CardSheetBattle 稿缩档)
            public float RowPadV, RowPadH, RowHead, RowGap, BodyGap = 4f, LvTagH;
            public int Name, Body, LvTagFont, KindFont;
            /// <summary>特性行与数值格的圆角(铺满 7pt → 15,战斗 6pt → 12.6 取整 13)。</summary>
            public int CellRadius;
            /// <summary>行头图标 chip 边长(稿 13pt;战斗版收到行头高 25,执行期决定 D18)。</summary>
            public float Chip;

            public static readonly Sizes Tall = new()
            {
                Battle = false,
                Pip = 40f, Bar = 10.5f, BarRadius = 5f, Mark = 15f, Mark2 = 12.6f, MarkGap = 3f, LadderVGap = 6f, LadderNum = 15f,
                FacePadV = 17f, HeadH = 50f, Seal = Ui.FaceSealL, HeadGap = 15f, Role = 25, Landing = 19,
                StatGap = 12.6f, StatK = 17, StatUnit = 18, StatPadH = 15,
                RowPadV = 8f, RowPadH = 17f, RowHead = 29f, RowGap = 6f, LvTagH = 25f, LvTagFont = 16, KindFont = 17,
                Name = 24, Body = 20, CellRadius = 15, Chip = 27f,
            };

            public static readonly Sizes BattleSheet = new()
            {
                Battle = true,
                Pip = 36f, Bar = 8f, BarRadius = 4f, Mark = 12.6f, Mark2 = 10.5f, MarkGap = 2f, LadderVGap = 4f, LadderNum = 0f,
                FacePadV = 15f, HeadH = 42f, Seal = Ui.FaceSealM, HeadGap = 12.6f, Role = 24, Landing = 18,
                StatGap = 10.5f, StatK = 16, StatUnit = 17, StatPadH = 13,
                RowPadV = 6f, RowPadH = 15f, RowHead = 25f, RowGap = 8.4f, LvTagH = 23f, LvTagFont = 15, KindFont = 16,
                Name = 22, Body = 19, CellRadius = 13, Chip = 25f,
            };

            /// <summary>等级条整条宽:10 格 + 9 道格距(铺满版 454)。</summary>
            public float LadderWidth => MetaRules.MaxCardLevel * Pip + (MetaRules.MaxCardLevel - 1) * PipGap;
            /// <summary>等级条高:记号 + 间距 + 条 + (铺满版)间距 + 级数。</summary>
            public float LadderHeight => Mark + LadderVGap + Bar + (LadderNum > 0 ? LadderVGap + LadderNum : 0f);
        }

        // 行头里几件小东西的尺寸,两版相同(component/TraitRow);Lv 标高 / 字号、类别字号随版走(Sizes)
        private const float LvTagPadX = 8f;
        private const int TailFont = 17;
        private const float TailIcon = 17f;
        private const float HeadItemGap = 10.5f;    // 行头各件之间 5pt
        private const float LineSealGap = 6f;       // 两小句前缀 xs 面印与字之间
        private const float UpTagFont = 17f;
        private const float MissingH = 46f;         // 待补占位高(稿 .void min-height 22pt)

        /// <summary>Missing = 这一面缺这一级(画待补占位,spec A 合并后不应出现)。</summary>
        public enum RowState { On, Off, Spent, Missing }

        /// <summary>点了有术语的行 / 格:<paramref name="target"/> = 被点的那块(选中描环挂在它上),<paramref name="radius"/> = 它的圆角,
        /// <paramref name="title"/> = 行名或格名(气泡标题「名 · 词条」),<paramref name="terms"/> = 按出现顺序去重的术语。</summary>
        public delegate void TermsTap(RectTransform target, int radius, string title, IReadOnlyList<GlossaryTerm> terms);

        /// <summary>一条特性行要画的东西。<see cref="Lines"/> 每句可带一枚 xs 面印前缀(两面效果不同时分两小句,Q3)。</summary>
        public sealed class RowModel
        {
            public int Level;
            public string Name;
            public string Kind;
            public RowState State;
            /// <summary>正文,一律纯文本(不带富文本标签)。</summary>
            public readonly List<(CardFace? Seal, string Text)> Lines = new();
            /// <summary>这一行出现的术语(各句合并,按首次出现顺序去重,R5);空 = 这一行不给点。</summary>
            public IReadOnlyList<GlossaryTerm> Terms = Array.Empty<GlossaryTerm>();
            /// <summary>字形特性(拆字 / 成字时)的行首 xs 印「拆 / 合」(B-Qe1);null = 不印。</summary>
            public TraitDef HeadSeal;
            /// <summary>行头图标 chip(spec B §1.5):本命行取本命 chip,其余取正文里第一个有图标的词条;null = 不画。</summary>
            public string Icon;
            public ChipTone IconTone;
            /// <summary>木系五行面 Lv1 本命行:左缘 2pt 朱砂(稿 .tr.nat)。</summary>
            public bool Nature;
        }

        /// <summary>一面要画的东西。</summary>
        public sealed class FaceModel
        {
            public CardFace Face;
            public string Role;
            public string Landing;
            public bool Up;
            public IReadOnlyList<FaceStat> Stats;
            public readonly List<RowModel> Rows = new();
            /// <summary>战斗版:未解锁的特性收成的一行(null = 没有)。</summary>
            public string LockedLine;
        }

        // ================= 文案 =================

        /// <summary>行头类别:主动 / 被动,Lv8 加「 · 招牌」。</summary>
        public static string KindText(TraitDef trait, bool signature)
        {
            string kind = FormText(trait.Form);
            return signature ? Strings.T("charsheet.trait.signature", ("kind", kind)) : kind;
        }

        /// <summary>升级预览两面合并行的类别(<see cref="SharedRow"/>):按每条 Def 自己的类别(<see cref="SheetTrait.Forms"/>,已去重),
        /// 两条不同时印「主动 / 被动」(final review Minor 2),同则只印一个。</summary>
        public static string KindText(SheetTrait row) =>
            row.Forms.Count >= 2
                ? Strings.T("charsheet.trait.kind_pair", ("a", FormText(row.Forms[0])), ("b", FormText(row.Forms[1])))
                : FormText(row.Forms[0]);

        private static string FormText(TraitForm form) =>
            form == TraitForm.Passive ? Strings.T("charsheet.trait.passive") : Strings.T("charsheet.trait.active");

        /// <summary>行名:同名只印一个,不同名印「名 A / 名 B」(Q3;<see cref="SheetTrait.Names"/> 已去重)。</summary>
        public static string NameText(SheetTrait row) =>
            row.Names.Count >= 2
                ? Strings.T("charsheet.trait.name_pair", ("a", row.Names[0]), ("b", row.Names[1]))
                : row.Names[0];

        /// <summary>面头那句「落点 · 范围」。召唤面只印落点(Q12 短句)。</summary>
        public static string LandingText(CharDef def, CardFace face, int level)
        {
            var target = CardSheetRules.TargetOf(def, face, level);
            string where = target switch
            {
                FaceTarget.Enemy => Strings.T("charsheet.landing.enemy"),
                FaceTarget.Ally => Strings.T("charsheet.landing.ally"),
                FaceTarget.SelfOnly => Strings.T("charsheet.landing.self"),
                _ => Strings.T("charsheet.landing.summon"),
            };
            if (target == FaceTarget.Summon) return where;
            var area = CardSheetRules.AreaOf(def, face, level) ?? TargetArea.Single;
            return Strings.T("charsheet.landing.line", ("target", where), ("area", CharInfo.ShapeLabel(area)));
        }

        /// <summary>面名:攻击 / 五行面。</summary>
        public static string RoleText(CardFace face) =>
            face == CardFace.Attack ? Strings.T("face.name.attack") : Strings.T("face.name.feature");

        /// <summary>数值格标签。Kind 漏分支时印枚举名(同 CharInfo.EffectsText 的兜底约定:宁可难看,不要静默)。</summary>
        public static string StatLabel(FaceStatKind kind) => kind switch
        {
            FaceStatKind.Damage => Strings.T("charsheet.stat.damage"),
            FaceStatKind.Heal => Strings.T("collection.stat.heal"),
            FaceStatKind.Shield => Strings.T("collection.stat.shield"),
            FaceStatKind.SummonHp => Strings.T("charsheet.stat.summon_hp"),
            FaceStatKind.SummonAttack => Strings.T("collection.stat.attack"),
            FaceStatKind.Burn => Strings.T("charsheet.stat.burn"),
            FaceStatKind.Morale => Strings.T("charsheet.stat.morale"),
            FaceStatKind.Block => Strings.T("charsheet.stat.block"),
            FaceStatKind.Counter => Strings.T("charsheet.stat.counter"),
            FaceStatKind.Weaken => Strings.T("charsheet.stat.weaken"),
            FaceStatKind.Armor => Strings.T("charsheet.stat.armor"),
            FaceStatKind.Duration => Strings.T("charsheet.stat.duration"),
            FaceStatKind.Freeze => Strings.T("charsheet.stat.freeze"),
            FaceStatKind.Slow => Strings.T("charsheet.stat.slow"),
            FaceStatKind.Seed => Strings.T("charsheet.stat.seed"),
            FaceStatKind.Charm => Strings.T("charsheet.stat.charm"),
            FaceStatKind.HealOverTime => Strings.T("charsheet.stat.hot"),
            FaceStatKind.ArmorBreak => Strings.T("charsheet.stat.armorbreak"),
            FaceStatKind.Nature => Strings.T("charsheet.stat.nature"),
            FaceStatKind.SpendWellspring => Strings.T("charsheet.stat.spend_wellspring"),
            FaceStatKind.SpendHeft => Strings.T("charsheet.stat.spend_heft"),
            FaceStatKind.CounterPercent => Strings.T("charsheet.stat.counter"),
            FaceStatKind.ReviveHp => Strings.T("charsheet.stat.revive"),
            _ => kind.ToString(),
        };

        /// <summary>数值格单位(层 / 次 / 回合;量没有单位)。</summary>
        public static string StatUnit(FaceStatKind kind) => kind switch
        {
            FaceStatKind.Burn or FaceStatKind.Morale => Strings.T("charsheet.unit.layer"),
            FaceStatKind.Block => Strings.T("charsheet.unit.count"),
            FaceStatKind.Duration or FaceStatKind.Freeze or FaceStatKind.Slow or FaceStatKind.Seed
                or FaceStatKind.Charm or FaceStatKind.HealOverTime or FaceStatKind.ArmorBreak => Strings.T("charsheet.unit.turn"),
            _ => "",
        };

        private static string StatValue(FaceStat stat) => StatValue(stat.Kind, stat.Value);

        /// <summary>格里的数:减攻印「−15%」、护甲 / 战意印「+N」(稿)、耗泉 / 耗厚印「×N」(每层伤害,同旧
        /// char.effect.spend* 的「层数×N」),其余原样。升级预览的差行(<see cref="StatDelta"/>)同一读法。</summary>
        internal static string StatValue(FaceStatKind kind, int value) => kind switch
        {
            FaceStatKind.Weaken => Strings.T("charsheet.stat.minus_pct", ("value", value)),
            FaceStatKind.Armor or FaceStatKind.Morale => Strings.T("charsheet.stat.plus", ("value", value)),
            FaceStatKind.SpendWellspring or FaceStatKind.SpendHeft => Strings.T("charsheet.stat.per_stack", ("value", value)),
            _ => value.ToString(),
        };

        /// <summary>Lv3 行的「本体差」(Q2):「灼 3→4 层；减攻 15→20%；减攻 2→3 回合」。木五行面本命强化只改被动(差为空),
        /// 这时退回那条特性的 <see cref="CharInfo.TraitEffectsText"/>。
        /// 口径:差里一律用**短标签**(减攻 / 护甲 / 灼…,不用数值格上的「目标攻击」),持续差按它跟的那一格起名
        /// (<see cref="StatDelta.DurationOf"/>,一面有两组时各配各的);百分号紧贴数字,层 / 次 / 回合前空一格。</summary>
        private static string Lv3Text(CharDef def, CardFace face, TraitDef trait, int level)
        {
            var deltas = FaceStats.Lv3Change(def, face, level);
            if (deltas.Count == 0)
                return CharInfo.TraitEffectsText(trait, def, Math.Max(level, trait.UnlockLevel));
            return string.Join(Strings.T("charsheet.lv3.sep"), deltas.Select(DeltaText));
        }

        private static string DeltaText(StatDelta d)
        {
            string label = DeltaLabel(d);
            if (d.Kind == FaceStatKind.Weaken)
                return Strings.T("charsheet.lv3.delta_pct", ("label", label), ("from", d.From), ("to", d.To));
            string unit = StatUnit(d.Kind);
            return unit.Length == 0
                ? Strings.T("charsheet.lv3.delta_plain", ("label", label), ("from", d.From), ("to", d.To))
                : Strings.T("charsheet.lv3.delta", ("label", label), ("from", d.From), ("to", d.To), ("unit", unit));
        }

        /// <summary>差的短标签:减攻(数值格上写「目标攻击」)、持续按归属格起名(减攻 / 护甲),其余同数值格。</summary>
        private static string DeltaLabel(StatDelta d) =>
            ShortLabel(d.Kind == FaceStatKind.Duration && d.DurationOf is { } owner ? owner : d.Kind);

        /// <summary>短标签:减攻写「减攻」,其余同数值格标签。Lv3 本体差与升级预览差行的「持续」前缀共用。</summary>
        internal static string ShortLabel(FaceStatKind kind) =>
            kind == FaceStatKind.Weaken ? Strings.T("charsheet.stat.weaken_short") : StatLabel(kind);

        private static CardFace FaceOfTrait(TraitDef trait) =>
            trait.Face == TraitFace.Feature ? CardFace.Feature : CardFace.Attack;

        /// <summary>一条面特性行(Lv5 / Lv6 / Lv8)。正文按 max(卡等级, 解锁级) 印(Q17)。</summary>
        public static RowModel FaceRow(CharDef def, SheetTrait row, int level, RowState state)
        {
            var trait = row.Defs[0];
            var model = new RowModel
            {
                Level = (int)row.Slot,
                Name = NameText(row),
                Kind = KindText(trait, row.IsSignature),
                State = state,
            };
            model.Lines.Add((null, CharInfo.TraitEffectsText(trait, def, Math.Max(level, trait.UnlockLevel))));
            return WithTerms(model);
        }

        private static RowModel WithTerms(RowModel model)
        {
            model.Terms = GlossaryText.TermsOf(model.Lines.Select(l => l.Text));
            return model;
        }

        /// <summary>这一行里的字形特性(拆字 / 成字时触发,<see cref="TraitCueRules.SealOf"/> = 拆 / 合);没有 = null。
        /// 字卡详情各面行首印(<see cref="LevelCell"/>)与升级预览卡头印共用。</summary>
        public static TraitDef GlyphTrait(SheetTrait row) =>
            row.Defs.FirstOrDefault(t => TraitCueRules.SealOf(t) is TraitSealKind.Dismantle or TraitSealKind.Compose);

        /// <summary>升级预览(CollectionView)两面合并的一行(Lv3 / Lv4;字卡详情已改全平铺,不再用)。分面的两条:正文相同合成一句;不同则两小句各带 xs 面印(Q2 / Q3)。
        /// Review Focus 2:两条 Defs 都要画到,不能只取第一条。</summary>
        public static RowModel SharedRow(CharDef def, SheetTrait row, int level, RowState state)
        {
            var model = new RowModel
            {
                Level = (int)row.Slot,
                Name = NameText(row),
                Kind = KindText(row),
                State = state,
                HeadSeal = GlyphTrait(row),
            };
            int textLevel = Math.Max(level, (int)row.Slot);
            if (row.Defs.Count == 1)
            {
                model.Lines.Add((null, CharInfo.TraitEffectsText(row.Defs[0], def, textLevel)));
                return WithTerms(model);
            }
            var parts = row.Defs.Select(t => (Face: FaceOfTrait(t),
                Text: row.Slot == TraitSlot.Lv3 ? Lv3Text(def, FaceOfTrait(t), t, level)
                                                : CharInfo.TraitEffectsText(t, def, textLevel))).ToList();
            if (parts.All(p => p.Text == parts[0].Text))
                model.Lines.Add((null, parts[0].Text));
            else
                foreach (var p in parts) model.Lines.Add((p.Face, p.Text));
            return WithTerms(model);
        }

        /// <summary>全平铺的一格(spec B §1.2):Lv1 印本体关键词效果(木五行面印本命),Lv3 印本体差,其余同 <see cref="FaceRow"/>。
        /// <paramref name="cell"/> = null → 待补占位。字形特性的「拆 / 合」xs 印搬进各面的行(执行期决定 D14)。</summary>
        public static RowModel LevelCell(CharDef def, TraitSlot slot, CardFace face, SheetTrait cell, int level, RowState state)
        {
            if (cell == null) return new RowModel { Level = (int)slot, Name = "", Kind = "", State = RowState.Missing };
            var model = slot switch
            {
                TraitSlot.Lv1 => Lv1Row(def, face, cell, level, state),
                TraitSlot.Lv3 => Lv3Row(def, face, cell, level, state),
                _ => FaceRow(def, cell, level, state),
            };
            model.HeadSeal ??= GlyphTrait(cell);
            PickIcon(model);
            return model;
        }

        private static RowModel Lv1Row(CharDef def, CardFace face, SheetTrait cell, int level, RowState state)
        {
            var trait = cell.Defs[0];
            int lv = Math.Max(1, level);
            var effects = CardSheetRules.Lv1Effects(def, face, lv);
            var summon = face == CardFace.Feature
                ? effects.FirstOrDefault(e => e.Kind == EffectKind.Summon && e.Passive != null) : null;
            bool water = face == CardFace.Feature && def.Element == Element.Water;
            var model = new RowModel
            {
                Level = 1,
                Name = trait.Name,
                State = state,
                Nature = summon != null,
                Kind = summon != null ? Strings.T("charsheet.trait.nature_kind")
                    : water ? Strings.T("charsheet.trait.active_hot")
                    : FormText(trait.Form),
            };
            string text = summon != null ? CharInfo.NatureText(summon.Passive)
                : effects.Count > 0 ? CharInfo.SideEffectsText(effects, def, lv) : null;
            if (!string.IsNullOrEmpty(text)) model.Lines.Add((null, text));
            var lead = new List<GlossaryTerm>();
            if (summon != null) lead.Add(GlossaryTerm.Nature);   // 本命行的气泡固定带「本命」(稿 extra)
            if (water) lead.Add(GlossaryTerm.Hot);               // 水五行面 Lv1 带「润泽」总称
            model.Terms = GlossaryText.TermsOf(new[] { trait.Name }.Concat(model.Lines.Select(l => l.Text)), lead);
            if (summon != null)
            {
                model.Icon = SummonInfo.NatureChipIcon(summon.Passive);   // 无图标的本命(花 / 藤 / 箭 / 柘)= null,不画 chip
                model.IconTone = ChipTone.Threat;                         // 本命一律朱砂
            }
            return model;
        }

        private static RowModel Lv3Row(CharDef def, CardFace face, SheetTrait cell, int level, RowState state)
        {
            var trait = cell.Defs[0];
            bool nature = face == CardFace.Feature && trait.Effects.Any(e => e.Kind == EffectKind.Summon && e.Passive != null);
            var model = new RowModel
            {
                Level = 3,
                Name = trait.Name,
                State = state,
                Kind = nature ? Strings.T("charsheet.trait.nature_up") : FormText(trait.Form),
            };
            model.Lines.Add((null, Lv3Text(def, face, trait, level)));
            return WithTerms(model);
        }

        /// <summary>行头图标:本命行已在 <see cref="Lv1Row"/> 定好(无图标就不画);其余取第一个有 chip 的词条。</summary>
        private static void PickIcon(RowModel row)
        {
            if (row.Icon != null || row.Nature) return;
            foreach (var t in row.Terms)
                if (GlossaryRules.ChipOf(t) is { } chip)
                {
                    row.Icon = chip.Icon;
                    row.IconTone = chip.Tone;
                    return;
                }
        }

        // ================= 等级条 =================

        /// <summary>10 格进度 + 解锁记号(component/LevelLadder)。记号由 <see cref="CardSheetRules.LadderMarks"/> 给,
        /// 这里不自己推。<paramref name="level"/> = 0 时(未拥有,Q8)整条全空。
        /// <paramref name="nextJade"/>:下一格是否画成可升的铜绿(只在铺满版、材料够时)。</summary>
        public static GameObject LevelLadder(Transform parent, CharDef def, int level, bool nextJade, Sizes z)
        {
            var ladder = Ui.Row(parent, "LevelLadder", z.PipGap);
            ladder.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(ladder, z.LadderWidth, z.LadderHeight);
            for (int i = 1; i <= MetaRules.MaxCardLevel; i++)
            {
                var pip = Ui.VStack(ladder.transform, $"Pip{i}", z.LadderVGap);
                pip.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                Ui.Sized(pip, z.Pip, z.LadderHeight);

                var marks = CardSheetRules.LadderMarks(def, i);
                var markRow = Ui.Row(pip.transform, "Marks", z.MarkGap);
                Ui.Sized(markRow, z.Pip, z.Mark);
                float alpha = i <= level ? 1f : 0.35f;
                for (int m = 0; m < marks.Count; m++)
                    Mark(markRow.transform, marks[m], def.Element, m == 0 ? z.Mark : z.Mark2, z.Mark, alpha);

                var bar = Ui.Panel(pip.transform, "Bar");
                var barImage = bar.AddComponent<Image>();
                barImage.sprite = Theme.Rounded(Mathf.RoundToInt(z.BarRadius));
                barImage.type = Image.Type.Sliced;
                barImage.color = i <= level ? Theme.InkSoft : (i == level + 1 && nextJade ? Theme.Jade : Theme.PaperDim);
                Ui.Sized(bar, z.Pip, z.Bar);

                if (z.LadderNum > 0)
                {
                    // 级数只印在有记号的格下(1 / 3 / 4 / 5 / 6 / 8;Lv1 的记号是「两面」,见 LadderMarks),其余留空,高度照占
                    var num = Ui.ThemedLabel(pip.transform, marks.Count > 0 ? i.ToString() : "",
                        Mathf.RoundToInt(z.LadderNum), Theme.LockGray);
                    Ui.Sized(num.gameObject, z.Pip, z.LadderNum);
                }
            }
            return ladder;
        }

        /// <summary>一枚解锁记号:墨实 = 攻击面,本系字形色实 = 五行面,ink-soft 空心 = 两面。未到达的 35%。</summary>
        private static void Mark(Transform parent, TraitFace face, Element? element, float width, float height, float alpha)
        {
            var go = Ui.Panel(parent, $"Mark_{face}");
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            Color color;
            if (face == TraitFace.Both)
            {
                image.sprite = Theme.RoundedRing(3, 2);
                color = Theme.InkSoft;
            }
            else
            {
                image.sprite = Theme.Rounded(3);
                color = face == TraitFace.Attack ? Theme.Ink : Theme.GlyphColor(element);
            }
            image.type = Image.Type.Sliced;
            color.a = alpha;
            image.color = color;
            Ui.Sized(go, width, height);
        }

        // ================= 高度(先算后画) =================

        /// <summary>行头放不下时先丢类别小字、再截名字(component/TraitRow:「off 态放不下时去掉类别小字,保留锁尾标」)。</summary>
        private static (string Name, bool ShowKind) FitHead(RowModel row, float width, Sizes z)
        {
            float tag = TagWidth(row.Level, z);
            float tail = TailWidth(row);
            float name = Ui.EstimateWidth(row.Name, z.Name);
            float kind = Ui.EstimateWidth(row.Kind, z.KindFont);
            float fixedPart = tag + HeadItemGap + (tail > 0 ? HeadItemGap + tail : 0f)
                + (row.HeadSeal != null ? Ui.FaceSealXs + HeadItemGap : 0f)
                + (row.Icon != null ? z.Chip + HeadItemGap : 0f);
            if (fixedPart + name + HeadItemGap + kind <= width) return (row.Name, true);
            return (Ui.Truncate(row.Name, z.Name, width - fixedPart), false);
        }

        private static string LvText(int level) => Strings.T("charsheet.trait.lv", ("level", level));

        private static float TagWidth(int level, Sizes z) => Ui.EstimateWidth(LvText(level), z.LvTagFont) + 2f * LvTagPadX;

        private static float TailWidth(RowModel row) => row.State switch
        {
            RowState.Off => TailIcon + 4f + Ui.EstimateWidth(LockedTail(row.Level), TailFont),
            RowState.Spent => Ui.EstimateWidth(Strings.T("charsheet.trait.used"), 16) + 21f,
            _ => 0f,
        };

        private static string LockedTail(int level) => Strings.T("charsheet.trait.locked", ("level", level));

        /// <summary>正文一句的高(带 xs 面印前缀时扣掉印宽与印距,且不矮于印)。与 <see cref="BodyLine"/> 同一口径,
        /// 字卡详情与升级预览小卡(CollectionView)共用 —— 先算后画,两边不许各抄一份。</summary>
        internal static float LineHeight(string text, bool seal, int font, float width) =>
            Mathf.Max(seal ? Ui.FaceSealXs : 0f,
                Ui.WrappedTextHeight(text, font, width - (seal ? Ui.FaceSealXs + LineSealGap : 0f)));

        /// <summary>一条特性行的高。<paramref name="width"/> = 行本身的宽(含内边距)。</summary>
        public static float RowHeight(RowModel row, float width, Sizes z)
        {
            if (row.State == RowState.Missing) return MissingH;
            float textW = width - 2f * z.RowPadH;
            float h = 2f * z.RowPadV + z.RowHead;
            // 行内竖排的间距 BodyGap 落在每一句之前(行头与第一句之间、两小句之间)
            foreach (var line in row.Lines) h += z.BodyGap + LineHeight(line.Text, line.Seal != null, z.Body, textW);
            return h;
        }

        private static float LockedLineH(Sizes z) => Mathf.Ceil(TailFont * 1.35f);

        /// <summary>两面逐级的行高:同一级取两面较高者(稿 sheet-view.js:两面 = 同一张网格的两列)。两面行数由构造保证相等。
        /// <paramref name="faceW"/> = 面的外宽。</summary>
        public static float[] LevelHeights(FaceModel a, FaceModel b, float faceW, Sizes z)
        {
            float inner = faceW - 2f * z.FacePadH;
            int n = Math.Max(a.Rows.Count, b.Rows.Count);
            var heights = new float[n];
            for (int i = 0; i < n; i++)
                heights[i] = Mathf.Max(
                    i < a.Rows.Count ? RowHeight(a.Rows[i], inner, z) : 0f,
                    i < b.Rows.Count ? RowHeight(b.Rows[i], inner, z) : 0f);
            return heights;
        }

        /// <summary>一面的内容高(两面等高,取较高的那一面)。<paramref name="rowHeights"/> = <see cref="LevelHeights"/>。</summary>
        public static float FaceHeight(FaceModel face, float faceW, Sizes z, IReadOnlyList<float> rowHeights)
        {
            float h = 2f * z.FacePadV + z.HeadH + z.FaceGap + z.StatH;
            for (int i = 0; i < rowHeights.Count; i++)
                h += (i == 0 ? z.FaceGap : z.RowGap) + rowHeights[i];
            if (face.LockedLine != null) h += z.FaceGap + LockedLineH(z);
            return h;
        }

        // ================= 画 =================

        /// <summary>一面:正面(攻击)纸白底 + panel-border 内描边;背面(五行面)<see cref="Theme.FaceTint"/> 底 + 本系字形色 22% 内描边。
        /// 战斗版朝上的那一面换 1.5pt 墨色描环 +「朝上」签(稿 .face.up / .uptag)。</summary>
        public static void FacePanel(Transform parent, CharDef def, FaceModel face, float faceW, float faceH, Sizes z,
            IReadOnlyList<float> rowHeights, TermsTap onTerms = null)
        {
            bool back = face.Face == CardFace.Feature;
            Color border = face.Up ? Theme.Ink
                : back ? WithAlpha(Theme.GlyphColor(def.Element), 0.22f) : Theme.PanelBorder;
            var panel = Ui.OutlinedPanel(parent, back ? "FaceBack" : "FaceFront",
                back ? Theme.FaceTint(def.Element) : Theme.CardWhite, border,
                Mathf.RoundToInt(z.FaceRadius), face.Up ? 3f : 2f);
            Ui.Sized(panel.gameObject, faceW, faceH);

            var stack = Ui.VStack(panel.transform, "Stack", 0);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            int padV = Mathf.RoundToInt(z.FacePadV), padH = Mathf.RoundToInt(z.FacePadH);
            layout.padding = new RectOffset(padH, padH, padV, padV);
            Ui.Stretch((RectTransform)stack.transform);
            float inner = faceW - 2f * z.FacePadH;

            FaceHead(stack.transform, def, face, inner, z);
            Gap(stack.transform, z.FaceGap);
            StatCells(stack.transform, def, face, back, z, onTerms);
            for (int i = 0; i < face.Rows.Count; i++)
            {
                Gap(stack.transform, i == 0 ? z.FaceGap : z.RowGap);
                var row = face.Rows[i];
                if (row.State == RowState.Missing) MissingCell(stack.transform, row, inner, rowHeights[i], z);
                else TraitRow(stack.transform, def, row, back, inner, z, minHeight: rowHeights[i], onTerms: onTerms);
            }
            if (face.LockedLine != null)
            {
                // 锁行贴底(稿 .lockln margin-top: auto):中间垫一块吃掉富余高度的弹簧
                var spring = Ui.Panel(stack.transform, "Spring");
                Ui.Sized(spring, flexHeight: 1);
                Gap(stack.transform, z.FaceGap);
                LockedLine(stack.transform, face.LockedLine, back, inner);
            }
        }

        private static void FaceHead(Transform parent, CharDef def, FaceModel face, float inner, Sizes z)
        {
            var head = Ui.Row(parent, "Head", z.HeadGap);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, inner, z.HeadH);
            var seal = Ui.FaceSeal(head.transform, def, face.Face, z.Seal);
            Ui.Sized(seal, z.Seal, z.Seal);
            var role = Ui.ThemedLabel(head.transform, face.Role, z.Role, Theme.TextMain, Theme.TitleFont);
            role.fontStyle = FontStyle.Bold;

            float upTag = face.Up ? Ui.ChipWidth(Strings.T("charsheet.face_up"), (int)UpTagFont, 21) + z.HeadGap : 0f;
            float landingW = inner - z.Seal - Ui.EstimateWidth(face.Role, z.Role) - 2f * z.HeadGap - upTag;
            var landing = Ui.ThemedLabel(head.transform, Ui.Truncate(face.Landing, z.Landing, landingW),
                z.Landing, Theme.TextDim);
            landing.alignment = TextAnchor.MiddleLeft;

            if (!face.Up) return;
            var spring = Ui.Panel(head.transform, "Spring");
            Ui.Sized(spring, flexWidth: 1);
            Ui.Chip(head.transform, Strings.T("charsheet.face_up"), Theme.Ink, Color.white, (int)UpTagFont, 21, 12);
        }

        /// <summary>数值格(<see cref="FaceStats.Body"/>,每面 1–3 格)。生面「本命」格印本命 chip 的图标,没有图标就省掉这一格(Q7)。
        /// 正面格底 panel-paper、背面格底 card-face(稿 .face.front .sb)。
        /// 带术语的格(<see cref="GlossaryRules.TermOf"/>)标签按术语样式画(加粗 ink-soft),点格开气泡(R6);其余格不挂。</summary>
        private static void StatCells(Transform parent, CharDef def, FaceModel face, bool back, Sizes z, TermsTap onTerms)
        {
            var row = Ui.Row(parent, "Stats", z.StatGap);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, height: z.StatH, flexWidth: 1);
            foreach (var stat in face.Stats)
            {
                string icon = stat.Kind == FaceStatKind.Nature ? SummonInfo.NatureChipIcon(stat.Passive) : null;
                if (stat.Kind == FaceStatKind.Nature && icon == null) continue;

                var cell = Ui.OutlinedPanel(row.transform, "Stat", back ? Theme.CardWhite : Theme.PanelPaper,
                    Theme.PanelBorder, z.CellRadius, 2);
                Ui.Sized(cell.gameObject, height: z.StatH, flexWidth: 1);
                var stack = Ui.VStack(cell.transform, "Stack", 0);
                var layout = stack.GetComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.padding = new RectOffset(z.StatPadH, z.StatPadH, 6, 6);
                Ui.Stretch((RectTransform)stack.transform);

                var term = GlossaryRules.TermOf(stat.Kind);
                string keyText = StatLabel(stat.Kind);
                var key = Ui.ThemedLabel(stack.transform, keyText, z.StatK, term != null ? Theme.ElementSoftFg(def.Element) : Theme.TextDim);
                key.alignment = TextAnchor.MiddleLeft;
                if (term != null)
                {
                    key.fontStyle = FontStyle.Bold;
                    if (onTerms != null)
                    {
                        var terms = new[] { term.Value };
                        var cellRect = (RectTransform)cell.transform;
                        TapTarget(cell, () => onTerms(cellRect, z.CellRadius, keyText, terms));
                    }
                }
                Ui.Sized(key.gameObject, height: z.StatK + 2f, flexWidth: 1);

                var value = Ui.Row(stack.transform, "Value", 4);
                value.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
                Ui.Sized(value, height: z.StatV + 2f, flexWidth: 1);
                if (icon != null)
                {
                    var sprite = Icons.Get(icon);
                    if (sprite != null)
                    {
                        var image = Ui.Panel(value.transform, "Icon").AddComponent<Image>();
                        image.sprite = sprite;
                        image.preserveAspect = true;
                        image.color = Theme.ElementSoftFg(def.Element);
                        Ui.Sized(image.gameObject, z.StatV, z.StatV);
                    }
                    else
                        Ui.ThemedLabel(value.transform, Icons.Fallback(icon), z.StatV, Theme.TextMain, Theme.TitleFont);
                    continue;
                }
                var number = Ui.ThemedLabel(value.transform, StatValue(stat), z.StatV, Theme.TextMain, Theme.TitleFont);
                number.fontStyle = FontStyle.Bold;
                string unit = StatUnit(stat.Kind);
                if (unit.Length > 0) Ui.ThemedLabel(value.transform, unit, z.StatUnit, Theme.TextDim);
            }
        }

        /// <summary>一条特性行(component/TraitRow)。on:底 = 正面 panel-paper / 背面 card-face + panel-border 内描边,Lv 标墨底白字;
        /// off / spent:去底、描边 80%、字压 text-faint(背面 text-dim 78%),尾标锁 +「Lv{n} 解锁」或「本场已用」胶囊。
        /// <paramref name="width"/> = 行的外宽。</summary>
        /// <param name="minHeight">同级两面的行要等高,传 <see cref="LevelHeights"/> 的那一格;0 = 按自己的内容高。</param>
        /// <param name="onTerms">点行开词条气泡;null 或这一行没有术语时不挂(R7)。</param>
        public static void TraitRow(Transform parent, CharDef def, RowModel row, bool back, float width, Sizes z,
            float minHeight = 0f, TermsTap onTerms = null)
        {
            bool on = row.State == RowState.On, spent = row.State == RowState.Spent;
            Color offColor = back ? WithAlpha(Theme.TextDim, 0.78f) : Theme.LockGray;
            Color nameColor = on ? Theme.TextMain : spent ? Theme.TextDim : offColor;
            Color bodyColor = on ? Theme.TextQuiet : spent ? Theme.TextDim : offColor;   // A 案:正文 text-quiet
            Color kindColor = on || spent ? Theme.TextDim : offColor;

            float height = Mathf.Max(minHeight, RowHeight(row, width, z));
            // on:实底 + 描边;off / spent:去底,只剩 80% 的一圈描线(OutlinedPanel 是「描边色垫底 + 内层填充」,
            // 内层给透明会露出整块描边色,所以去底的一律用空心环)
            var card = on
                ? Ui.OutlinedPanel(parent, "TraitRow", back ? Theme.CardWhite : Theme.PanelPaper,
                    Theme.PanelBorder, z.CellRadius, 2).gameObject
                : Ring(parent, "TraitRow", WithAlpha(Theme.PanelBorder, 0.8f), z.CellRadius, 2);
            Ui.Sized(card, width, height);
            if (row.Nature)
            {
                // 本命行左缘 2pt 朱砂(稿 .tr.nat;off / spent 40%),与木灵详情里「战场上那枚」本命卡同一记号
                var edge = Ui.Panel(card.transform, "NatureEdge");
                var edgeImage = edge.AddComponent<Image>();
                edgeImage.color = on ? Theme.Cinnabar : WithAlpha(Theme.Cinnabar, 0.4f);
                edgeImage.raycastTarget = false;
                edge.AddComponent<LayoutElement>().ignoreLayout = true;
                Ui.Anchor((RectTransform)edge.transform, Vector2.zero, new Vector2(0f, 1f),
                    new Vector2(0f, z.CellRadius / 2f), new Vector2(4f, -z.CellRadius / 2f));
            }
            if (onTerms != null && row.Terms.Count > 0)
            {
                var cardRect = (RectTransform)card.transform;
                TapTarget(card.GetComponent<Image>(), () => onTerms(cardRect, z.CellRadius, row.Name, row.Terms));
            }
            var stack = Ui.VStack(card.transform, "Stack", z.BodyGap);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            int padV = Mathf.RoundToInt(z.RowPadV), padH = Mathf.RoundToInt(z.RowPadH);
            layout.padding = new RectOffset(padH, padH, padV, padV);
            Ui.Stretch((RectTransform)stack.transform);
            float textW = width - 2f * z.RowPadH;

            var (name, showKind) = FitHead(row, textW, z);
            var head = Ui.Row(stack.transform, "Head", HeadItemGap);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, textW, z.RowHead);
            LvTag(head.transform, row.Level, row.State, z);
            if (row.HeadSeal != null) HeadSeal(head.transform, def, row.HeadSeal, on);
            if (row.Icon != null)
                IconChip(head.transform, row.Icon, row.IconTone, z.Chip, on ? 1f : spent ? 0.6f : 0.4f);
            var nameLabel = Ui.ThemedLabel(head.transform, name, z.Name, nameColor, Theme.TitleFont);
            nameLabel.fontStyle = FontStyle.Bold;
            if (showKind) Ui.ThemedLabel(head.transform, row.Kind, z.KindFont, kindColor);
            if (!on)
            {
                var spring = Ui.Panel(head.transform, "Spring");
                Ui.Sized(spring, flexWidth: 1);
                if (row.State == RowState.Off) LockTail(head.transform, LockedTail(row.Level), Theme.LockGray);
                else Ui.Chip(head.transform, Strings.T("charsheet.trait.used"), Theme.InkBar, Theme.TextWarm, 16, 21, 9);
            }

            foreach (var line in row.Lines)
                BodyLine(stack.transform, def, line.Seal, line.Text, z.Body, bodyColor, textW, muted: !on,
                    termColor: Theme.ElementSoftFg(def.Element));
        }

        /// <summary>字形特性行首的 xs「拆 / 合」印(<see cref="Ui.TraitSeal"/>);off / spent 跟正文一起压到 α .78(附录 §B Q-e4)。</summary>
        private static void HeadSeal(Transform parent, CharDef def, TraitDef trait, bool on)
        {
            var seal = Ui.TraitSeal(parent, def, trait, Ui.FaceSealXs);
            Ui.Sized(seal, Ui.FaceSealXs, Ui.FaceSealXs);
            if (!on) seal.AddComponent<CanvasGroup>().alpha = 0.78f;
        }

        /// <summary>把一块已有的图挂成「点了开气泡」:不做按下态(只读弹窗里没有可按的控件感)。</summary>
        private static void TapTarget(Image graphic, Action onTap)
        {
            var button = graphic.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = graphic;
            button.onClick.AddListener(() => onTap());
        }

        /// <summary>图标 chip(spec B §1.5):= 战场状态 chip 的方形缩小版,同图标、同底色(<see cref="UnitDetailChip.ToneColor"/>)。
        /// 稿 13pt 见方、圆角 3.5pt、图标 9.5pt;赭金底配 gold-text 图标,其余白。PNG 取不到时印兜底字。
        /// 行头与词条气泡共用。<paramref name="alpha"/>:off .4 / spent .6(稿 .tr.off .ic / .tr.spent .ic)。</summary>
        internal static GameObject IconChip(Transform parent, string icon, ChipTone tone, float size, float alpha = 1f)
        {
            var chip = Ui.CardPanel(parent, "IconChip", UnitDetailChip.ToneColor(tone), Mathf.RoundToInt(size * 3.5f / 13f));
            chip.raycastTarget = false;
            Ui.Sized(chip.gameObject, size, size);
            Color fg = tone == ChipTone.Boost ? Theme.GoldText : Color.white;
            float inner = Mathf.Round(size * 9.5f / 13f);
            var sprite = Icons.Get(icon);
            if (sprite != null)
            {
                var image = Ui.Panel(chip.transform, "Icon").AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = fg;
                image.raycastTarget = false;
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(inner, inner);
            }
            else
            {
                var glyph = Ui.ThemedLabel(chip.transform, Icons.Fallback(icon), Mathf.RoundToInt(inner), fg);
                Ui.Stretch(glyph.rectTransform);
            }
            if (alpha < 1f) chip.gameObject.AddComponent<CanvasGroup>().alpha = alpha;
            return chip.gameObject;
        }

        /// <summary>正文一句:无印 = 折行文字;有印 = xs 面印 + 折行文字。高度取 <see cref="LineHeight"/>(吃纯文本)。
        /// <paramref name="text"/> 必须是纯文本;术语 / 数字的样式在这里、赋 Text.text 那一刻才套上(<see cref="GlossaryText.Rich"/>)。
        /// <paramref name="muted"/>:off / spent 态,术语与数字只加粗、不换色(R3)。</summary>
        internal static void BodyLine(Transform parent, CharDef def, CardFace? seal, string text, int font, Color color, float width,
            bool muted = false, Color? termColor = null)
        {
            float h = LineHeight(text, seal != null, font, width);
            string rich = GlossaryText.Rich(text, muted, termColor);
            if (seal == null)
            {
                WrapLabel(parent, rich, font, color, width, h);
                return;
            }
            var line = Ui.Row(parent, "Line", LineSealGap);
            line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(line, width, h);
            SealXs(line.transform, def, seal.Value);
            WrapLabel(line.transform, rich, font, color, width - Ui.FaceSealXs - LineSealGap, h);
        }

        /// <summary>两小句前缀的 xs 面印(component/FaceSeal xs 档)。</summary>
        private static void SealXs(Transform parent, CharDef def, CardFace face)
        {
            var seal = Ui.FaceSeal(parent, def, face, Ui.FaceSealXs);
            Ui.Sized(seal, Ui.FaceSealXs, Ui.FaceSealXs);
        }

        private static Text WrapLabel(Transform parent, string text, int font, Color color, float width, float height)
        {
            var label = Ui.ThemedLabel(parent, text, font, color);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, width, height);
            return label;
        }

        private static void LvTag(Transform parent, int level, RowState state, Sizes z)
        {
            string text = LvText(level);
            float w = TagWidth(level, z);
            GameObject tag = state switch
            {
                RowState.On => Ui.CardPanel(parent, "LvTag", Theme.Ink, 12).gameObject,
                RowState.Spent => Ui.CardPanel(parent, "LvTag", Theme.TextDim, 12).gameObject,   // 稿 .tr.spent .lvt
                _ => Ring(parent, "LvTag", Theme.LockGray, 12, 2),                               // 去底:text-faint 描线(稿 .tr.off .lvt)
            };
            Ui.Sized(tag, w, z.LvTagH);
            var label = Ui.ThemedLabel(tag.transform, text, z.LvTagFont,
                state == RowState.Off ? Theme.LockGray : Color.white);
            Ui.Stretch(label.rectTransform);
        }

        private static void LockTail(Transform parent, string text, Color color)
        {
            var tail = Ui.Row(parent, "LockTail", 4);
            tail.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
            LockIcon(tail.transform, color, TailIcon);
            Ui.ThemedLabel(tail.transform, text, TailFont, color);
        }

        /// <summary>挂锁(icon_lock,Task 2 走管线;取不到 PNG 时用兜底字)。</summary>
        private static void LockIcon(Transform parent, Color color, float size)
        {
            var sprite = Icons.Get("lock");
            if (sprite != null)
            {
                var image = Ui.Panel(parent, "Lock").AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = color;
                image.raycastTarget = false;
                Ui.Sized(image.gameObject, size, size);
            }
            else
                Ui.ThemedLabel(parent, Icons.Fallback("lock"), Mathf.RoundToInt(size), color);
        }

        /// <summary>战斗版:未解锁的特性收成一行(「Lv5 杜塞 · Lv8 堵截 未解锁」),超宽截断补省略号。</summary>
        private static void LockedLine(Transform parent, string text, bool back, float width)
        {
            Color color = back ? Theme.TextDim : Theme.LockGray;
            var row = Ui.Row(parent, "LockedLine", 6);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, width, Mathf.Ceil(TailFont * 1.35f));
            LockIcon(row.transform, color, TailIcon);
            Ui.ThemedLabel(row.transform, Ui.Truncate(text, TailFont, width - TailIcon - 6f), TailFont, color);
        }

        /// <summary>待补占位(执行期决定 D12):panel-border 空心环 + text-mute「Lv6 · 这一面待补」。spec A 合并后不应出现。</summary>
        private static void MissingCell(Transform parent, RowModel row, float width, float height, Sizes z)
        {
            var ring = Ring(parent, "Missing", Theme.PanelBorder, z.CellRadius, 2);
            Ui.Sized(ring, width, height);
            var label = Ui.ThemedLabel(ring.transform, Strings.T("charsheet.trait.missing", ("level", row.Level)), 18, Theme.TextMute);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(z.RowPadH, 0f);
            label.rectTransform.offsetMax = new Vector2(-z.RowPadH, 0f);
        }

        /// <summary>空心圆角描环(<see cref="Theme.RoundedRing"/>),给「去底」的态用。</summary>
        private static GameObject Ring(Transform parent, string name, Color color, int radius, int thickness)
        {
            var go = Ui.Panel(parent, name);
            var image = go.AddComponent<Image>();
            image.sprite = Theme.RoundedRing(radius, thickness);
            image.type = Image.Type.Sliced;
            image.color = color;
            return go;
        }

        private static void Gap(Transform parent, float height)
        {
            var gap = Ui.Panel(parent, "Gap");
            Ui.Sized(gap, height: height);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
