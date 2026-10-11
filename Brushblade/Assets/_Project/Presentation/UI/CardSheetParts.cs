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
    /// (面头 + 数值格 + 本面特性行)、特性行 <see cref="TraitRow"/>(on / off / spent 三态)、两面条 <see cref="SharedStrip"/>。
    /// 稿 <c>docs/design/drafts/traits/CardSheet.dc.html</c> / <c>CardSheetBattle.dc.html</c>;
    /// 组件卡 <c>component/TraitRow</c>、<c>component/LevelLadder</c>、<c>component/FaceSeal</c>。
    ///
    /// 「显示哪几行、取哪几格」全在 Core(<see cref="CardSheetRules"/>、<see cref="FaceStats"/>),这里只排版。
    /// 高度一律**先算后画**(<see cref="FaceHeight"/> / <see cref="SharedHeight"/>):两面要等高,
    /// 行高要按实际面宽折行(16:9 下面内宽只有 431),靠布局组事后撑高做不到「两面取较高的那一面」。
    ///
    /// ⚠ 一律不挂点击:组件卡给特性行预留了词条气泡(Glossary)的点击位,E2 不挂;
    /// 战斗长按版挂了就破坏「长按只看不出手」(<see cref="HoldToPreview"/>)。</summary>
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
            // 两面条
            public float SharedLabelW, SharedRowH;
            public int SharedLabelFont, SharedText;

            public static readonly Sizes Tall = new()
            {
                Battle = false,
                Pip = 40f, Bar = 10.5f, BarRadius = 5f, Mark = 15f, Mark2 = 12.6f, MarkGap = 3f, LadderVGap = 6f, LadderNum = 15f,
                FacePadV = 17f, HeadH = 50f, Seal = Ui.FaceSealL, HeadGap = 15f, Role = 25, Landing = 19,
                StatGap = 12.6f, StatK = 17, StatUnit = 18, StatPadH = 15,
                RowPadV = 8f, RowPadH = 17f, RowHead = 29f, RowGap = 6f, LvTagH = 25f, LvTagFont = 16, KindFont = 17,
                Name = 24, Body = 20, CellRadius = 15,
                SharedLabelW = 71f, SharedLabelFont = 21,
            };

            public static readonly Sizes BattleSheet = new()
            {
                Battle = true,
                Pip = 36f, Bar = 8f, BarRadius = 4f, Mark = 12.6f, Mark2 = 10.5f, MarkGap = 2f, LadderVGap = 4f, LadderNum = 0f,
                FacePadV = 15f, HeadH = 42f, Seal = Ui.FaceSealM, HeadGap = 12.6f, Role = 24, Landing = 18,
                StatGap = 10.5f, StatK = 16, StatUnit = 17, StatPadH = 13,
                RowPadV = 6f, RowPadH = 15f, RowHead = 25f, RowGap = 8.4f, LvTagH = 23f, LvTagFont = 15, KindFont = 16,
                Name = 22, Body = 19, CellRadius = 13,
                SharedLabelW = 63f, SharedLabelFont = 20, SharedRowH = 46f, SharedText = 18,
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

        public enum RowState { On, Off, Spent }

        /// <summary>一条特性行要画的东西。<see cref="Lines"/> 每句可带一枚 xs 面印前缀(两面效果不同时分两小句,Q3)。</summary>
        public sealed class RowModel
        {
            public int Level;
            public string Name;
            public string Kind;
            public RowState State;
            public readonly List<(CardFace? Seal, string Text)> Lines = new();
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
            string kind = trait.Form == TraitForm.Passive
                ? Strings.T("charsheet.trait.passive")
                : Strings.T("charsheet.trait.active");
            return signature ? Strings.T("charsheet.trait.signature", ("kind", kind)) : kind;
        }

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

        /// <summary>格里的数:减攻印「−15%」、护甲 / 战意印「+N」(稿),其余原样。升级预览的差行(<see cref="StatDelta"/>)同一读法。</summary>
        internal static string StatValue(FaceStatKind kind, int value) => kind switch
        {
            FaceStatKind.Weaken => Strings.T("charsheet.stat.minus_pct", ("value", value)),
            FaceStatKind.Armor or FaceStatKind.Morale => Strings.T("charsheet.stat.plus", ("value", value)),
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
            return model;
        }

        /// <summary>两面条的一行(Lv3 / Lv4)。分面的两条:正文相同合成一句;不同则两小句各带 xs 面印(Q2 / Q3)。
        /// Review Focus 2:两条 Defs 都要画到,不能只取第一条。</summary>
        public static RowModel SharedRow(CharDef def, SheetTrait row, int level, RowState state)
        {
            var model = new RowModel
            {
                Level = (int)row.Slot,
                Name = NameText(row),
                Kind = KindText(row.Defs[0], false),
                State = state,
            };
            int textLevel = Math.Max(level, (int)row.Slot);
            if (row.Defs.Count == 1)
            {
                model.Lines.Add((null, CharInfo.TraitEffectsText(row.Defs[0], def, textLevel)));
                return model;
            }
            var parts = row.Defs.Select(t => (Face: FaceOfTrait(t),
                Text: row.Slot == TraitSlot.Lv3 ? Lv3Text(def, FaceOfTrait(t), t, level)
                                                : CharInfo.TraitEffectsText(t, def, textLevel))).ToList();
            if (parts.All(p => p.Text == parts[0].Text))
                model.Lines.Add((null, parts[0].Text));
            else
                foreach (var p in parts) model.Lines.Add((p.Face, p.Text));
            return model;
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
                    // 级数只印在有记号的格下(3 / 4 / 5 / 6 / 8),其余留空,高度照占
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
            float fixedPart = tag + HeadItemGap + (tail > 0 ? HeadItemGap + tail : 0f);
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
            float textW = width - 2f * z.RowPadH;
            float h = 2f * z.RowPadV + z.RowHead;
            // 行内竖排的间距 BodyGap 落在每一句之前(行头与第一句之间、两小句之间)
            foreach (var line in row.Lines) h += z.BodyGap + LineHeight(line.Text, line.Seal != null, z.Body, textW);
            return h;
        }

        private static float LockedLineH(Sizes z) => Mathf.Ceil(TailFont * 1.35f);

        /// <summary>一面的内容高(两面取较高的那一面,两面等高)。<paramref name="faceW"/> = 面的外宽。</summary>
        public static float FaceHeight(FaceModel face, float faceW, Sizes z)
        {
            float inner = faceW - 2f * z.FacePadH;
            float h = 2f * z.FacePadV + z.HeadH + z.FaceGap + z.StatH;
            for (int i = 0; i < face.Rows.Count; i++)
                h += (i == 0 ? z.FaceGap : z.RowGap) + RowHeight(face.Rows[i], inner, z);
            if (face.LockedLine != null) h += z.FaceGap + LockedLineH(z);
            return h;
        }

        /// <summary>两面条的高(各行并排、等高)。<paramref name="rowW"/> = 每一行的外宽。</summary>
        public static float SharedHeight(IReadOnlyList<RowModel> rows, float rowW, Sizes z)
        {
            if (z.Battle) return z.SharedRowH;
            float h = 0f;
            foreach (var row in rows) h = Mathf.Max(h, RowHeight(row, rowW, z));
            return h;
        }

        // ================= 画 =================

        /// <summary>一面:正面(攻击)纸白底 + panel-border 内描边;背面(五行面)<see cref="Theme.FaceTint"/> 底 + 本系字形色 22% 内描边。
        /// 战斗版朝上的那一面换 1.5pt 墨色描环 +「朝上」签(稿 .face.up / .uptag)。</summary>
        public static void FacePanel(Transform parent, CharDef def, FaceModel face, float faceW, float faceH, Sizes z)
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
            StatCells(stack.transform, def, face, back, z);
            for (int i = 0; i < face.Rows.Count; i++)
            {
                Gap(stack.transform, i == 0 ? z.FaceGap : z.RowGap);
                TraitRow(stack.transform, def, face.Rows[i], back, inner, z);
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
        /// 正面格底 panel-paper、背面格底 card-face(稿 .face.front .sb)。</summary>
        private static void StatCells(Transform parent, CharDef def, FaceModel face, bool back, Sizes z)
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

                var key = Ui.ThemedLabel(stack.transform, StatLabel(stat.Kind), z.StatK, Theme.TextDim);
                key.alignment = TextAnchor.MiddleLeft;
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
        /// <param name="minHeight">两面条里并排的行要等高,传整条的高;面里的行传 0(按自己的内容高)。</param>
        /// <param name="onFill">on 态的底色;缺省按所在面取(正面 panel-paper / 背面 card-face)。
        /// 两面条不在任何一面之下,传 card-face(稿 .shared .tr)。</param>
        public static void TraitRow(Transform parent, CharDef def, RowModel row, bool back, float width, Sizes z,
            float minHeight = 0f, Color? onFill = null)
        {
            bool on = row.State == RowState.On;
            Color nameColor = on ? Theme.TextMain : back ? WithAlpha(Theme.TextDim, 0.78f) : Theme.LockGray;
            Color dimColor = on ? Theme.TextDim : back ? WithAlpha(Theme.TextDim, 0.78f) : Theme.LockGray;

            float height = Mathf.Max(minHeight, RowHeight(row, width, z));
            // on:实底 + 描边;off / spent:去底,只剩 80% 的一圈描线(OutlinedPanel 是「描边色垫底 + 内层填充」,
            // 内层给透明会露出整块描边色,所以去底的一律用空心环)
            var card = on
                ? Ui.OutlinedPanel(parent, "TraitRow", onFill ?? (back ? Theme.CardWhite : Theme.PanelPaper),
                    Theme.PanelBorder, z.CellRadius, 2).gameObject
                : Ring(parent, "TraitRow", WithAlpha(Theme.PanelBorder, 0.8f), z.CellRadius, 2);
            Ui.Sized(card, width, height);
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
            LvTag(head.transform, row.Level, on, z);
            var nameLabel = Ui.ThemedLabel(head.transform, name, z.Name, nameColor, Theme.TitleFont);
            nameLabel.fontStyle = FontStyle.Bold;
            if (showKind) Ui.ThemedLabel(head.transform, row.Kind, z.KindFont, dimColor);
            if (!on)
            {
                var spring = Ui.Panel(head.transform, "Spring");
                Ui.Sized(spring, flexWidth: 1);
                if (row.State == RowState.Off) LockTail(head.transform, LockedTail(row.Level), Theme.LockGray);
                else Ui.Chip(head.transform, Strings.T("charsheet.trait.used"), Theme.InkBar, Theme.TextWarm, 16, 21, 9);
            }

            foreach (var line in row.Lines)
                BodyLine(stack.transform, def, line.Seal, line.Text, z.Body, dimColor, textW);
        }

        /// <summary>正文一句:无印 = 折行文字;有印 = xs 面印 + 折行文字。高度取 <see cref="LineHeight"/>。</summary>
        internal static void BodyLine(Transform parent, CharDef def, CardFace? seal, string text, int font, Color color, float width)
        {
            float h = LineHeight(text, seal != null, font, width);
            if (seal == null)
            {
                WrapLabel(parent, text, font, color, width, h);
                return;
            }
            var line = Ui.Row(parent, "Line", LineSealGap);
            line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(line, width, h);
            SealXs(line.transform, def, seal.Value);
            WrapLabel(line.transform, text, font, color, width - Ui.FaceSealXs - LineSealGap, h);
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

        private static void LvTag(Transform parent, int level, bool on, Sizes z)
        {
            string text = LvText(level);
            float w = TagWidth(level, z);
            GameObject tag;
            if (on)
                tag = Ui.CardPanel(parent, "LvTag", Theme.Ink, 12).gameObject;
            else
                tag = Ring(parent, "LvTag", Theme.LockGray, 12, 2);   // 去底:只剩一圈 text-faint 描线(稿 .tr.off .lvt)
            Ui.Sized(tag, w, z.LvTagH);
            var label = Ui.ThemedLabel(tag.transform, text, z.LvTagFont, on ? Color.white : Theme.LockGray);
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

        /// <summary>两面条:左一格竖排「两面」(Unity Text 不能竖排,写成「两\n面」),右侧本卡的 Lv3 / Lv4 行并排、等高。
        /// 铺满版每行照常折行;战斗版每行压成单行(Lv 标 + 名 + 截断后的正文,不印类别)。</summary>
        public static void SharedStrip(Transform parent, CharDef def, IReadOnlyList<RowModel> rows, float width, float height, Sizes z)
        {
            var strip = Ui.Row(parent, "Shared", 17f);
            var stripLayout = strip.GetComponent<HorizontalLayoutGroup>();
            stripLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(strip, width, height);

            var label = Ui.CardPanel(strip.transform, "SharedLabel", Theme.PanelInset, 15);
            Ui.Sized(label.gameObject, z.SharedLabelW, height);
            var text = Ui.ThemedLabel(label.transform, Strings.T("charsheet.shared"), z.SharedLabelFont,
                Theme.TextDim, Theme.TitleFont);
            text.fontStyle = FontStyle.Bold;
            Ui.Stretch(text.rectTransform);

            float rowW = SharedRowWidth(width, rows.Count, z);
            foreach (var row in rows)
            {
                if (z.Battle) CompactRow(strip.transform, def, row, rowW, height, z);
                else TraitRow(strip.transform, def, row, false, rowW, z, height, Theme.CardWhite);
            }
        }

        /// <summary>两面条里每一行的外宽:条宽 − 左标 − 间距,余下按行数均分。</summary>
        public static float SharedRowWidth(float width, int count, Sizes z) =>
            count <= 0 ? 0f : (width - z.SharedLabelW - 17f * count) / count;

        /// <summary>战斗版两面条的一行:压成单行(稿 .shared .tr 高 22pt)。两小句各带 xs 面印,宽度均分后各自截断。</summary>
        private static void CompactRow(Transform parent, CharDef def, RowModel row, float width, float height, Sizes z)
        {
            var card = Ui.OutlinedPanel(parent, "TraitRow", Theme.CardWhite, Theme.PanelBorder, z.CellRadius, 2);
            Ui.Sized(card.gameObject, width, height);
            var line = Ui.Row(card.transform, "Line", 12.6f);
            var layout = line.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            int padH = Mathf.RoundToInt(z.RowPadH);
            layout.padding = new RectOffset(padH, padH, 0, 0);
            Ui.Stretch((RectTransform)line.transform);

            LvTag(line.transform, row.Level, true, z);
            float inner = width - 2f * z.RowPadH;
            string name = Ui.Truncate(row.Name, z.Name, inner * 0.4f);
            var nameLabel = Ui.ThemedLabel(line.transform, name, z.Name, Theme.TextMain, Theme.TitleFont);
            nameLabel.fontStyle = FontStyle.Bold;

            float rest = inner - TagWidth(row.Level, z) - Ui.EstimateWidth(name, z.Name) - 2f * 12.6f;
            float each = rest / Mathf.Max(1, row.Lines.Count) - (row.Lines.Count > 1 ? 12.6f : 0f);
            foreach (var (seal, text) in row.Lines)
            {
                float textW = each;
                if (seal != null)
                {
                    SealXs(line.transform, def, seal.Value);
                    textW -= Ui.FaceSealXs + 12.6f;
                }
                Ui.ThemedLabel(line.transform, Ui.Truncate(text, z.SharedText, textW), z.SharedText, Theme.TextDim);
            }
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
