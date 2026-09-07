using System;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能节点详情弹窗(设计稿 artboard <c>SkillNode</c>,2026-09-07 收尾波补齐——
    /// review 标出这是漏做的一张设计稿)。
    ///
    /// 起因:节点卡是定高栅格(<see cref="PerkView"/> 的 <c>NodeH</c>),`descLabel` 开了
    /// <c>VerticalOverflow.Truncate</c>,最长的几条效果描述(五行 L1「X 系起手格额外抽
    /// {value} 次,取最好的」、慧眼 L1)在 5 列网格的列宽下有被裁切的风险,而旧版「点行看详情」
    /// 的 <see cref="Ui.Alert"/> 兜底路径已被整个删掉——真截断了玩家没有任何办法看到完整文案。
    /// 这张弹窗把完整效果文案摊开,同时补一段「这条效果实际意味着什么」的人话解释
    /// (<see cref="PerkInfo.DetailText"/>),卡面截断也就无所谓了。
    ///
    /// 触发:<see cref="PerkView.BuildNode"/> 里五态节点**统一**点开这张弹窗,解锁动作也搬
    /// 进了弹窗底部的主操作钮——不再是卡面直接点一下就扣钱。40 个节点、单次不可撤销地花
    /// 几百到几千墨锭,卡面直接出手的误触代价太高(2026-09-07 拍板)。
    ///
    /// 结构基本套 <see cref="CharPreview"/> 用的外壳(<see cref="Ui.Sheet"/>:遮罩 + 宣纸描边卡),
    /// 五态判据 / 按枝配色全部复用 <see cref="PerkView"/> 已 internal 出来的那几个静态成员——
    /// 保证这张弹窗与网格上的节点读到的是**同一份**状态与颜色,不是另起一套判断。</summary>
    public static class PerkNodeSheet
    {
        private const float SheetW = 620f;
        private const float SheetH = 700f;
        private const float HeaderH = 150f;
        private const float ChainCircle = 34f;
        private const float FooterH = 56f;
        private const float CloseW = 120f;

        // 内容可用宽度(与 CharPreview.ContentW 同一套算法:扣描边内缩 + 内容内边距)
        private const float ContentW = SheetW - 2f * 1.5f - 2f * 24f;

        public static GameObject Show(Transform root, MetaState meta, PerkNodeDef def, Action onChanged)
        {
            var overlay = Ui.Sheet(root, "PerkNodeSheet", SheetW, SheetH,
                dismissable: true, replaceSameName: true, Theme.Scrim, out var content);

            BuildHeader(content, def);

            SectionLabel(content, Strings.T("perk.detail.section.effect"));
            BuildParagraph(content, PerkInfo.Desc(def), 18, Theme.TextMain);

            SectionLabel(content, Strings.T("perk.detail.section.explain"));
            BuildParagraph(content, PerkInfo.DetailText(def), 15, Theme.TextDim);

            SectionLabel(content, Strings.T("perk.detail.section.chain"));
            BuildChain(content, meta, def);

            SectionLabel(content, Strings.T("perk.detail.section.requirement"));
            BuildRequirements(content, meta, def);

            BuildFooter(content, meta, def, overlay, onChanged);

            return overlay;
        }

        // ================= 头部 =================

        /// <summary>该枝主色的浅色底 + 右下角代表字水印 + 面包屑 + 大字节点名 + 两个 chip
        /// (「专属」只给五行树的节点——被动/机制树的效果本就不分系,挂这个 chip 反而是在
        /// 说一件表里没有的事;「Lv.{UnlockLevel}」五态都挂,已点亮的节点也在告诉玩家
        /// 「这条原本是几级门槛」)。</summary>
        private static void BuildHeader(Transform parent, PerkNodeDef def)
        {
            var header = Ui.Panel(parent, "Header");
            var bg = header.AddComponent<Image>();
            bg.sprite = Theme.Rounded(16);
            bg.type = Image.Type.Sliced;
            bg.color = PerkView.BranchSoft(def);
            Ui.Sized(header, flexWidth: 1, height: HeaderH);

            // 水印大字:branch 主色本身,只调低 alpha——不是新色值,是同一个 Theme 色的透明变体
            // (CoachOverlay 的 ScrimAlpha 也是这个手法),所以仍算「只引用 Theme 语义色」。
            // ⚠ 锚点必须留在 [0,1] 之内:header 上没有 RectMask2D,超出锚点的文字不会被裁掉,
            // 而是原样溢出到 header 外面(可能盖住下面的「效果」段)。靠 TextAnchor.LowerRight
            // 在安全锚框里把字顶到右下角,不是靠把锚点本身推出框外。
            var main = PerkView.BranchColor(def);
            var watermark = Ui.ThemedLabel(header.transform, WatermarkChar(def), 88,
                new Color(main.r, main.g, main.b, 0.16f), Theme.TitleFont, TextAnchor.LowerRight);
            watermark.raycastTarget = false;
            Ui.Anchor(watermark.rectTransform, new Vector2(0.55f, 0.02f), new Vector2(0.97f, 0.98f),
                Vector2.zero, Vector2.zero);

            var info = Ui.VStack(header.transform, "Info", 6);
            Ui.Anchor((RectTransform)info.transform, Vector2.zero, Vector2.one,
                new Vector2(20, 14), new Vector2(-20, -14));
            info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            var crumb = Ui.ThemedLabel(info.transform, Strings.T("perk.detail.breadcrumb",
                ("tree", PerkView.TreeName(def.Tree)), ("branch", PerkView.BranchName(def.Branch)),
                ("depth", def.Depth)), 13, Theme.TextDim);
            crumb.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(crumb.gameObject, flexWidth: 1, height: 18);

            var nameRow = Ui.Row(info.transform, "NameRow", 10);
            nameRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(nameRow, flexWidth: 1, height: 42);
            Ui.ThemedLabel(nameRow.transform, PerkInfo.Name(def), 30, Theme.TextMain, Theme.TitleFont);

            var spring = Ui.Panel(nameRow.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (def.Tree == PerkTree.Wuxing)
                Ui.Chip(nameRow.transform, Strings.T("perk.detail.chip.exclusive"),
                    Theme.PanelInset, main, 13);
            Ui.Chip(nameRow.transform, Strings.T("perk.detail.chip.level", ("level", def.UnlockLevel)),
                Theme.PanelInset, Theme.TextDim, 13);
        }

        /// <summary>五行树的代表字就是它的元素本身(如金脉 → 「金」);被动/机制树没有元素,
        /// 取枝名的首字(「养元」枝名字符串是「元」,「博闻」是「博闻」取首字「博」)。
        /// 不是新文案——直接截取已经在字符串表里的 <see cref="PerkView.BranchName"/>。</summary>
        private static string WatermarkChar(PerkNodeDef def)
        {
            if (def.Element is { } el) return CharInfo.ElementName(el);
            var name = PerkView.BranchName(def.Branch);
            return name.Length > 0 ? name.Substring(0, 1) : "";
        }

        // ================= 效果 / 说明段 =================

        private static void SectionLabel(Transform parent, string text)
        {
            var label = Ui.ThemedLabel(parent, text, 14, Theme.TextDim, Theme.TitleFont);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, flexWidth: 1, height: 20);
        }

        private static void BuildParagraph(Transform parent, string text, int fontSize, Color color)
        {
            var label = Ui.ThemedLabel(parent, text, fontSize, color);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow; // 弹窗就是为了不截断,不设上限
            Ui.Sized(label.gameObject, flexWidth: 1, height: Ui.WrappedTextHeight(text, fontSize, ContentW));
        }

        // ================= 前置链 =================

        /// <summary>该枝从 L1 到本节点所在树的最深层,一串圆点:已点亮 = 主色实心圆 + 勾号,
        /// 当前这个 = 空心圆 + 主色粗描边,未点 = 灰色实心圆。</summary>
        private static void BuildChain(Transform parent, MetaState meta, PerkNodeDef def)
        {
            int maxDepth = BranchMaxDepth(def.Branch);
            var main = PerkView.BranchColor(def);

            var row = Ui.Row(parent, "Chain", 4);
            Ui.Sized(row, flexWidth: 1, height: 78);

            for (int d = 1; d <= maxDepth; d++)
            {
                var node = PerkRules.Get($"{def.Branch}_{d}");
                bool unlocked = PerkRules.IsUnlocked(meta, node.Id);
                bool current = d == def.Depth;

                var cell = Ui.VStack(row.transform, $"Step_{d}", 4);
                Ui.Sized(cell, flexWidth: 1);

                if (current)
                {
                    // 空心圆 + 主色粗描边:Theme.Circle 是没有 9-slice border 的简单圆形贴图
                    // (Image.Type.Simple),整张等比缩放——内圈同心缩小仍然是圆,不会像
                    // Ui.OutlinedPanel 那样把圆角矩形的裁切规则套歪成方圆不分。
                    var circle = Ui.Panel(cell.transform, "Circle");
                    var ring = circle.AddComponent<Image>();
                    ring.sprite = Theme.Circle;
                    ring.color = main;
                    Ui.Sized(circle, width: ChainCircle, height: ChainCircle);
                    var innerHole = Ui.Panel(circle.transform, "Hole");
                    var holeImage = innerHole.AddComponent<Image>();
                    holeImage.sprite = Theme.Circle;
                    holeImage.color = Theme.CardWhite;
                    holeImage.raycastTarget = false;
                    Ui.Anchor((RectTransform)innerHole.transform, Vector2.zero, Vector2.one,
                        new Vector2(4f, 4f), new Vector2(-4f, -4f));
                }
                else
                {
                    var circle = Ui.Panel(cell.transform, "Circle");
                    var img = circle.AddComponent<Image>();
                    img.sprite = Theme.Circle;
                    img.color = unlocked ? main : Theme.LockedBg;
                    Ui.Sized(circle, width: ChainCircle, height: ChainCircle);

                    if (unlocked)
                    {
                        var check = Ui.ThemedLabel(circle.transform, "✓", 18, Theme.CardWhite);
                        Ui.Anchor((RectTransform)check.transform, Vector2.zero, Vector2.one,
                            Vector2.zero, Vector2.zero);
                        check.raycastTarget = false;
                    }
                }

                var label = Ui.ThemedLabel(cell.transform, PerkInfo.Name(node), 12,
                    unlocked || current ? Theme.TextMain : Theme.LockGray);
                Ui.Sized(label.gameObject, flexWidth: 1);
            }
        }

        private static int BranchMaxDepth(string branch)
        {
            int max = 0;
            foreach (var n in PerkRules.Nodes)
                if (n.Branch == branch) max = Math.Max(max, n.Depth);
            return max;
        }

        // ================= 解锁条件 =================

        /// <summary>角色等级 / 墨锭拆成两格分别判(spec 要求「各自标够不够」)——此前卡面上
        /// 只有一句「需角色 N 级」或「墨锭不足」,玩家看不出**两个条件里哪个卡住了、
        /// 各自还差多少**,尤其是两条都不满足时。</summary>
        private static void BuildRequirements(Transform parent, MetaState meta, PerkNodeDef def)
        {
            var row = Ui.Row(parent, "Requirements", 14);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, flexWidth: 1, height: 68);

            int charLevel = MetaRules.CharacterLevel(meta.CharacterXp);
            bool levelMet = charLevel >= def.UnlockLevel;
            bool inkMet = meta.Ink >= def.InkCost;

            BuildRequirementBox(row.transform, Strings.T("perk.detail.req.level.label"),
                levelMet
                    ? Strings.T("perk.detail.req.level.met", ("level", def.UnlockLevel))
                    : Strings.T("perk.detail.req.level.gap",
                        ("level", def.UnlockLevel), ("gap", def.UnlockLevel - charLevel)),
                levelMet);

            BuildRequirementBox(row.transform, Strings.T("perk.detail.req.ink.label"),
                inkMet
                    ? Strings.T("perk.detail.req.ink.met", ("cost", def.InkCost))
                    : Strings.T("perk.detail.req.ink.gap",
                        ("cost", def.InkCost), ("gap", def.InkCost - meta.Ink)),
                inkMet);
        }

        private static void BuildRequirementBox(Transform parent, string label, string status, bool met)
        {
            var box = Ui.CardPanel(parent, "Req", Theme.PanelInset, 12);
            Ui.Sized(box.gameObject, flexWidth: 1, flexHeight: 1);

            var stack = Ui.VStack(box.transform, "Stack", 4);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 10);
            layout.childAlignment = TextAnchor.UpperLeft;

            var labelText = Ui.ThemedLabel(stack.transform, label, 13, Theme.TextDim);
            labelText.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(labelText.gameObject, flexWidth: 1);

            var statusText = Ui.ThemedLabel(stack.transform, status, 16,
                met ? Theme.DoneGreen : Theme.ExitPink, Theme.TitleFont);
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(statusText.gameObject, flexWidth: 1, flexHeight: 1);
        }

        // ================= 底部 =================

        /// <summary>主操作钮 = 解锁(可解锁时启用、其余四态置灰并写明原因,文案复用节点卡
        /// 已有的那几条,不新造一套「原因文案」)+ 关闭钮。宽度是算出来的定值——
        /// <see cref="Ui.PillButton"/> 只接受固定 size,不支持 flexWidth,索性直接按
        /// <see cref="ContentW"/> 减去间距与关闭钮宽度算好,不需要额外的 LayoutElement 叠加
        /// (在 PillButton 已经挂好的 LayoutElement 上再 <see cref="Ui.Sized"/> 一次会在同一个
        /// GameObject 上叠两个 LayoutElement,谁生效不确定,不能这么干)。</summary>
        private static void BuildFooter(Transform parent, MetaState meta, PerkNodeDef def,
            GameObject overlay, Action onChanged)
        {
            var row = Ui.Row(parent, "Footer", 14);
            Ui.Sized(row, flexWidth: 1, height: FooterH);

            int charLevel = MetaRules.CharacterLevel(meta.CharacterXp);
            var state = PerkView.StateOf(meta, def, charLevel);
            bool canUnlock = state == PerkView.NodeState.CanUnlock;

            float primaryW = ContentW - 14f - CloseW;
            var primary = Ui.PillButton(row.transform, FooterButtonText(state, def), () =>
            {
                if (!PerkRules.TryUnlock(meta, def.Id)) return; // 五态可能在弹窗开着时被别处改变,双保险
                UnityEngine.Object.Destroy(overlay);
                onChanged();
            }, canUnlock ? Theme.Gold : Theme.LockedBg, canUnlock ? Color.white : Theme.LockGray,
                18, new Vector2(primaryW, FooterH));
            primary.interactable = canUnlock;

            Ui.PillButton(row.transform, Strings.T("common.close"),
                () => UnityEngine.Object.Destroy(overlay),
                Theme.PanelInset, Theme.TextDim, 18, new Vector2(CloseW, FooterH));
        }

        private static string FooterButtonText(PerkView.NodeState state, PerkNodeDef def) => state switch
        {
            PerkView.NodeState.Owned => Strings.T("perk.node.badge.owned"),
            PerkView.NodeState.CanUnlock => Strings.T("perk.view.unlock_button", ("cost", def.InkCost)),
            PerkView.NodeState.PoorInk => Strings.T("perk.detail.footer.poor_ink", ("cost", def.InkCost)),
            PerkView.NodeState.GatedPrereq => Strings.T("perk.node.badge.gated_prereq"),
            _ => Strings.T("perk.view.locked_requirement", ("level", def.UnlockLevel)), // GatedLevel
        };
    }
}
