using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能页(T9,技能树重构):三页签(五行/被动/机制)+ 树状网格 ——
    /// 枝干为列、深度为行,列内连线表达前置,左侧窄轨标每层等级门槛。
    ///
    /// 取代 T1 Step7b 留下的临时版(单列滚动、节点名直印 def.Id、效果全走一条通用文案)。
    ///
    /// 节点五态(与设计规格 §8 一致):已点亮 / 可解锁 / 墨锭不足 / 前置未点 / 等级未到——
    /// 临时版只把后三态一律「置灰」,玩家看不出点不了的原因;这里逐态给了不同的底色/描边/文案。
    /// 判据全部走 <see cref="PerkRules"/> 现成的 <c>IsUnlocked</c>/<c>CanUnlock</c>,
    /// 不在这一层另起一套解锁逻辑。
    ///
    /// ⚠ 收尾波(2026-09-07):点任意状态的节点卡一律先开 <see cref="PerkNodeSheet"/> 详情弹窗,
    /// 解锁动作搬进了那张弹窗里,卡面本身不再直接出手扣钱 —— 40 个节点、单次不可撤销地花
    /// 几百到几千墨锭,卡面误触代价太高。<see cref="NodeState"/>/<see cref="StateOf"/>/
    /// <see cref="BranchColor"/> 等改成 internal,供 <see cref="PerkNodeSheet"/> 复用同一份
    /// 判据与配色,不在那边另起一套。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        private static readonly PerkTree[] Tabs = { PerkTree.Wuxing, PerkTree.Passive, PerkTree.Mechanic };

        private const float NodeH = 104f;
        private const float ConnectorH = 16f;
        private const float RailW = 86f;
        private const int NodeRadius = 12;

        private MetaState _meta;
        private Action _save;
        private Action _onBack;
        private PerkTree _tab = PerkTree.Wuxing;

        public void Init(MetaState meta, Action save, Action onBack)
        {
            _meta = meta;
            _save = save;
            _onBack = onBack;
            Build();
        }

        // ================= 骨架 =================

        private void Build()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            var card = Ui.CardPanel(transform, "Panel");
            Ui.Anchor((RectTransform)card.transform,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 12);
            Ui.Stretch((RectTransform)stack.transform);
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 18, 18);

            BuildHeader(stack.transform);
            BuildTabs(stack.transform);

            var body = Ui.Row(stack.transform, "Body", 14);
            Ui.Sized(body, flexWidth: 1, flexHeight: 1);
            BuildGateRail(body.transform);
            BuildGrid(body.transform);

            BuildLegend(stack.transform);
            Ui.PillButton(stack.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 20, new Vector2(180, 50));
        }

        private void BuildHeader(Transform parent)
        {
            var header = Ui.Row(parent, "Header", 20);

            var titles = Ui.VStack(header.transform, "Titles", 2);
            titles.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            var title = Ui.ThemedLabel(titles.transform, Strings.T("perk.view.title"), 28,
                Theme.TextMain, Theme.TitleFont);
            title.alignment = TextAnchor.MiddleLeft;
            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            var subtitle = Ui.ThemedLabel(titles.transform,
                Strings.T("perk.view.subtitle", ("level", charLevel)), 15, Theme.TextDim);
            subtitle.alignment = TextAnchor.MiddleLeft;

            var spring = Ui.Panel(header.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            Ui.InkCounter(header.transform, _meta.Ink, 20);
        }

        // ================= 页签 =================

        private void BuildTabs(Transform parent)
        {
            var row = Ui.Row(parent, "Tabs", 10);
            foreach (var tree in Tabs) BuildTab(row.transform, tree);
        }

        private void BuildTab(Transform parent, PerkTree tree)
        {
            bool on = _tab == tree;
            int owned = 0, total = 0;
            bool hasUpgradable = false;
            foreach (var def in PerkRules.Nodes)
            {
                if (def.Tree != tree) continue;
                total++;
                if (PerkRules.IsUnlocked(_meta, def.Id)) owned++;
                // 红点判据与「点得进去」同一函数(CanUnlock),不是自己另判——
                // 避免「红点亮着、点进去一个也点不了」的假消息(spec §8)。
                if (PerkRules.CanUnlock(_meta, def.Id)) hasUpgradable = true;
            }

            var go = Ui.Panel(parent, $"Tab_{tree}");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = on ? Theme.Ink : Theme.PanelInset;
            Ui.Sized(go, flexWidth: 1, height: 54);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                _tab = tree;
                Build();
            });

            var content = Ui.VStack(go.transform, "Content", 2);
            Ui.Stretch((RectTransform)content.transform);
            var fg = on ? Color.white : Theme.TextMain;
            Ui.ThemedLabel(content.transform, TreeName(tree), 20, fg, Theme.TitleFont);
            Ui.ThemedLabel(content.transform, $"{owned}/{total}", 13, on ? Theme.PaperDim : Theme.TextDim);

            if (hasUpgradable)
            {
                var dot = Ui.Panel(go.transform, "Dot");
                var dotImage = dot.AddComponent<Image>();
                dotImage.sprite = Theme.Circle;
                dotImage.color = Theme.Cinnabar;
                dotImage.raycastTarget = false;
                Ui.Anchor((RectTransform)dot.transform, Vector2.one, Vector2.one,
                    new Vector2(-18, -16), new Vector2(-6, -4));
            }
        }

        // internal:PerkNodeSheet 的面包屑同样要读树名,理由同 BranchName。
        internal static string TreeName(PerkTree tree) => tree switch
        {
            PerkTree.Wuxing => Strings.T("perk.tree.wuxing"),
            PerkTree.Passive => Strings.T("perk.tree.passive"),
            _ => Strings.T("perk.tree.mechanic"),
        };

        // ================= 左侧等级门槛轨 =================

        /// <summary>与网格逐行对齐的等级门槛轨:同一深度的门槛等级对整棵树统一
        /// (<see cref="PerkRules"/> 的门槛数组按树不按枝),从 <see cref="PerkRules.Nodes"/>
        /// 里任取一个该深度的节点读 <see cref="PerkNodeDef.UnlockLevel"/> 即可,
        /// 不需要另外把私有的门槛数组搬一份到这里。</summary>
        private void BuildGateRail(Transform parent)
        {
            int maxDepth = MaxDepthOf(_tab);
            var rail = Ui.VStack(parent, "Rail", 0);
            Ui.Sized(rail, width: RailW, flexHeight: 1);

            var headerSpacer = Ui.Panel(rail.transform, "HeaderSpacer");
            Ui.Sized(headerSpacer, flexWidth: 1, height: 26);

            for (int d = 1; d <= maxDepth; d++)
            {
                if (d > 1)
                {
                    var spacer = Ui.Panel(rail.transform, "Spacer");
                    Ui.Sized(spacer, flexWidth: 1, height: ConnectorH);
                }
                int level = GateLevel(_tab, d);
                var cell = Ui.Panel(rail.transform, $"Gate_{d}");
                Ui.Sized(cell, flexWidth: 1, height: NodeH);
                var label = Ui.ThemedLabel(cell.transform,
                    Strings.T("perk.gate.label", ("depth", d), ("level", level)), 14, Theme.TextDim);
                Ui.Stretch(label.rectTransform);
            }
        }

        // ================= 树状网格 =================

        private void BuildGrid(Transform parent)
        {
            var grid = Ui.Row(parent, "Grid", 10);
            Ui.Sized(grid, flexWidth: 1, flexHeight: 1);

            int maxDepth = MaxDepthOf(_tab);
            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            foreach (var branch in BranchesOf(_tab))
                BuildColumn(grid.transform, branch, maxDepth, charLevel);
        }

        private void BuildColumn(Transform parent, string branch, int maxDepth, int charLevel)
        {
            var col = Ui.VStack(parent, $"Col_{branch}", 0);
            Ui.Sized(col, flexWidth: 1, flexHeight: 1);

            var header = Ui.ThemedLabel(col.transform, BranchName(branch), 16, Theme.TextMain, Theme.TitleFont);
            Ui.Sized(header.gameObject, flexWidth: 1, height: 26);

            for (int d = 1; d <= maxDepth; d++)
            {
                var def = PerkRules.Get($"{branch}_{d}");
                if (d > 1)
                {
                    bool lit = PerkRules.IsUnlocked(_meta, $"{branch}_{d - 1}");
                    BuildConnector(col.transform, lit ? BranchColor(def) : Theme.PanelBorder);
                }
                BuildNode(col.transform, def, charLevel);
            }
        }

        private void BuildConnector(Transform parent, Color color)
        {
            var cell = Ui.Panel(parent, "Connector");
            Ui.Sized(cell, flexWidth: 1, height: ConnectorH);
            var line = Ui.Panel(cell.transform, "Line");
            var image = line.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            Ui.Anchor((RectTransform)line.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(-1.5f, 0f), new Vector2(1.5f, 0f));
        }

        internal enum NodeState { Owned, CanUnlock, PoorInk, GatedPrereq, GatedLevel }

        /// <summary>判据顺序与 <see cref="PerkRules.CanUnlock"/> 内部完全一致(已点 → 前置 →
        /// 等级 → 墨锭),只是把「不能点」拆成三种理由分别显示——同源判据,不是另一套规则。
        /// internal static(而非 private 实例方法):<see cref="PerkNodeSheet"/> 的底部操作钮
        /// 要用同一份判据决定「解锁 / 置灰 + 理由」,不能另起一份读 meta 的逻辑。</summary>
        internal static NodeState StateOf(MetaState meta, PerkNodeDef def, int charLevel)
        {
            if (PerkRules.IsUnlocked(meta, def.Id)) return NodeState.Owned;
            if (def.Depth > 1 && !PerkRules.IsUnlocked(meta, $"{def.Branch}_{def.Depth - 1}"))
                return NodeState.GatedPrereq;
            if (charLevel < def.UnlockLevel) return NodeState.GatedLevel;
            if (meta.Ink < def.InkCost) return NodeState.PoorInk;
            return NodeState.CanUnlock;
        }

        private void BuildNode(Transform parent, PerkNodeDef def, int charLevel)
        {
            var state = StateOf(_meta, def, charLevel);
            var main = BranchColor(def);
            var soft = BranchSoft(def);

            GameObject cell;      // 挂 LayoutElement、参与列内布局的那个物件
            GameObject clickTarget; // 真正吃点击的物件(OutlinedPanel 的 face 描边为 0 射线,按钮得挂外层)
            Image face;           // 内容(名称/描述/状态)挂载点

            switch (state)
            {
                case NodeState.Owned:
                    var ownedOuter = Ui.OutlinedPanel(parent, $"Node_{def.Id}", soft, main, NodeRadius, 2f, out face);
                    cell = clickTarget = ownedOuter.gameObject;
                    break;
                case NodeState.CanUnlock:
                    // 主色外发光(设计规格「白底 + 2px 主色描边 + 主色外发光」):
                    // Halo 贴一层比节点本体大 HaloPad 的光晕,再叠一张常规描边卡。
                    var glowHost = Ui.Panel(parent, $"Node_{def.Id}");
                    var glow = Ui.Panel(glowHost.transform, "Glow");
                    var glowImage = glow.AddComponent<Image>();
                    glowImage.sprite = Theme.Halo(NodeRadius);
                    glowImage.color = main;
                    glowImage.raycastTarget = false;
                    Ui.Anchor((RectTransform)glow.transform, Vector2.zero, Vector2.one,
                        new Vector2(-Theme.HaloPad, -Theme.HaloPad), new Vector2(Theme.HaloPad, Theme.HaloPad));
                    var faceOuter = Ui.OutlinedPanel(glowHost.transform, "Face", Theme.CardWhite, main, NodeRadius, 2f, out face);
                    Ui.Stretch((RectTransform)faceOuter.transform);
                    cell = glowHost;
                    clickTarget = faceOuter.gameObject;
                    break;
                case NodeState.PoorInk:
                    var poorPanel = Ui.CardPanel(parent, $"Node_{def.Id}", Theme.PanelPaper, NodeRadius);
                    cell = clickTarget = poorPanel.gameObject;
                    face = poorPanel;
                    break;
                case NodeState.GatedPrereq:
                    // 真虚线描边(2026-09-07 收尾波,回应图例对账 review):此前这里用「深一档实线
                    // 描边」将就,导致图例画出的虚线样例与节点实际渲染对不上——图例必须照抄节点
                    // 真实画法,而不是另画一套「看着差不多」的示意,所以改成 DashedPanel 与
                    // BuildLegend 共用同一份实现(见该方法注释)。
                    face = DashedPanel(parent, $"Node_{def.Id}", Theme.LockedBg, Theme.LockGray, NodeRadius);
                    cell = clickTarget = face.gameObject;
                    break;
                default: // GatedLevel:纯灰底,不描边——与「前置未点」在视觉上刻意分开
                    var levelPanel = Ui.CardPanel(parent, $"Node_{def.Id}", Theme.LockedBg, NodeRadius);
                    cell = clickTarget = levelPanel.gameObject;
                    face = levelPanel;
                    break;
            }

            Ui.Sized(cell, flexWidth: 1, height: NodeH);

            var button = clickTarget.AddComponent<Button>();
            button.targetGraphic = face;
            // 五态统一打开详情弹窗(2026-09-07 收尾波)——解锁动作也在弹窗里,卡面本身
            // 不再直接出手扣钱。之前只有 CanUnlock 可点、其余四态点了没反应,玩家读不到
            // 完整效果文案(卡面定高截断)也看不到「差多少」。
            button.onClick.AddListener(() =>
                PerkNodeSheet.Show(transform, _meta, def, () =>
                {
                    _save();
                    Build(); // 成功后刷新(墨锭计数/页签红点/网格状态全部同步)
                }));

            var content = Ui.VStack(face.transform, "Content", 3);
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(8, 6), new Vector2(-8, -6));

            bool gated = state == NodeState.GatedPrereq || state == NodeState.GatedLevel;
            var textColor = gated ? Theme.LockGray : Theme.TextMain;
            var nameLabel = Ui.ThemedLabel(content.transform, PerkInfo.Name(def), 17, textColor, Theme.TitleFont);
            Ui.Sized(nameLabel.gameObject, flexWidth: 1);

            var descLabel = Ui.ThemedLabel(content.transform, PerkInfo.Desc(def), 12,
                gated ? Theme.LockGray : Theme.TextDim);
            descLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            descLabel.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Sized(descLabel.gameObject, flexWidth: 1, flexHeight: 1);

            BuildStatusLine(content.transform, def, state, main);
        }

        /// <summary>五态各自的底部状态行,文案见设计规格 §8 的五态表。</summary>
        private static void BuildStatusLine(Transform parent, PerkNodeDef def, NodeState state, Color main)
        {
            switch (state)
            {
                case NodeState.Owned:
                    var ownedLabel = Ui.ThemedLabel(parent, Strings.T("perk.node.badge.owned"), 12, main);
                    Ui.Sized(ownedLabel.gameObject, flexWidth: 1);
                    break;
                case NodeState.CanUnlock:
                    var priceLabel = Ui.ThemedLabel(parent,
                        Strings.T("perk.view.unlock_button", ("cost", def.InkCost)), 12, main);
                    Ui.Sized(priceLabel.gameObject, flexWidth: 1);
                    break;
                case NodeState.PoorInk:
                    var grayPrice = Ui.ThemedLabel(parent,
                        Strings.T("perk.view.unlock_button", ("cost", def.InkCost)), 12, Theme.LockGray);
                    Ui.Sized(grayPrice.gameObject, flexWidth: 1);
                    var poorBadge = Ui.ThemedLabel(parent, Strings.T("perk.node.badge.poor_ink"), 11, Theme.Cinnabar);
                    Ui.Sized(poorBadge.gameObject, flexWidth: 1);
                    break;
                case NodeState.GatedPrereq:
                    var prereqBadge = Ui.ThemedLabel(parent, Strings.T("perk.node.badge.gated_prereq"), 12, Theme.LockGray);
                    Ui.Sized(prereqBadge.gameObject, flexWidth: 1);
                    break;
                default: // GatedLevel
                    var levelBadge = Ui.ThemedLabel(parent,
                        Strings.T("perk.view.locked_requirement", ("level", def.UnlockLevel)), 12, Theme.LockGray);
                    Ui.Sized(levelBadge.gameObject, flexWidth: 1);
                    break;
            }
        }

        // ================= 图例 =================

        private const float LegendSwatchSize = 22f;
        private const int LegendSwatchRadius = 6;
        // 图例「可解锁」用比「已点亮」更粗的描边代表节点实际的「描边 + 主色外发光」双重强调——
        // 缩略图画不出发光,用描边粗细近似那份额外强调,不要求两处线宽字节相同。
        private const float LegendBoldBorder = 3.5f;

        /// <summary>2026-09-07 收尾波(review 修 Critical):五态图例此前给了五个**固定色块**
        /// (绿/金/灰/…),但节点实际是按枝取色(<see cref="BranchColor"/>,13 种)——玩家照
        /// 「已点亮=绿」去认,回头看火脉已点亮的节点是暗红、水脉是蓝,图例在事实层面就是错的。
        ///
        /// 改法:五个色块统一用中性色(<see cref="Theme.TextDim"/> 描边 / <see cref="Theme.PanelInset"/>
        /// 系浅底),靠**边框样式**区分五态,且直接复用 <see cref="BuildNode"/> 实际画节点用的那几个
        /// 图元(<see cref="Ui.OutlinedPanel"/> / <see cref="Ui.CardPanel"/> / <see cref="DashedPanel"/>)——
        /// 保证图例的形状真的是节点的形状,不是另画一套「看着差不多」的示意。末尾补一句「颜色随枝而变,
        /// 形状表示状态」,把「为什么这里的颜色是中性的」讲清楚。</summary>
        private void BuildLegend(Transform parent)
        {
            var block = Ui.VStack(parent, "Legend", 8);
            Ui.Sized(block, flexWidth: 1);
            var row = Ui.Row(block.transform, "Items", 22);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            // 已点亮:浅色实心底 + 实线描边(与 NodeState.Owned 同一套 OutlinedPanel)
            LegendItem(row.transform,
                s => Ui.OutlinedPanel(s, "Swatch", Theme.PanelInset, Theme.TextDim, LegendSwatchRadius, 2f),
                Strings.T("perk.legend.owned"));
            // 可解锁:白底 + 粗实线描边(同一套 OutlinedPanel,描边加粗)
            LegendItem(row.transform,
                s => Ui.OutlinedPanel(s, "Swatch", Theme.CardWhite, Theme.TextDim, LegendSwatchRadius, LegendBoldBorder),
                Strings.T("perk.legend.unlockable"));
            // 墨锭不足:常规底,无描边(与 NodeState.PoorInk 同一套 CardPanel)
            LegendItem(row.transform,
                s => Ui.CardPanel(s, "Swatch", Theme.PanelPaper, LegendSwatchRadius),
                Strings.T("perk.legend.poor_ink"));
            // 前置未点:灰底 + 虚线描边(与 NodeState.GatedPrereq 同一套 DashedPanel)
            LegendItem(row.transform,
                s => DashedPanel(s, "Swatch", Theme.LockedBg, Theme.LockGray, LegendSwatchRadius, dashCountH: 3, dashCountV: 2),
                Strings.T("perk.legend.gated_prereq"));
            // 等级未到:灰底,无描边(与 NodeState.GatedLevel 同一套 CardPanel)
            LegendItem(row.transform,
                s => Ui.CardPanel(s, "Swatch", Theme.LockedBg, LegendSwatchRadius),
                Strings.T("perk.legend.gated_level"));

            var note = Ui.ThemedLabel(block.transform, Strings.T("perk.legend.note"), 12, Theme.TextDim);
            note.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(note.gameObject, flexWidth: 1);
        }

        private static void LegendItem(Transform parent, Func<Transform, Image> swatch, string text)
        {
            var item = Ui.Row(parent, "Item", 6);
            var image = swatch(item.transform);
            Ui.Sized(image.gameObject, width: LegendSwatchSize, height: LegendSwatchSize);
            Ui.ThemedLabel(item.transform, text, 12, Theme.TextDim);
        }

        /// <summary>虚线描边:圆角底 + 沿四边分数锚点摆的短线段。uGUI 没有内建虚线描边,
        /// 也没有现成的「变宽仍保持虚线间距」的 9-slice 技巧;节点宽度由
        /// <c>HorizontalLayoutGroup</c> 的 <c>flexWidth</c> 在运行期分配、构建时并不知道
        /// 具体像素宽(高度 <see cref="NodeH"/> 倒是常量),所以四条边都按**分数**锚点摆
        /// (而不是像素偏移)——不管最终分到多宽,虚线段都跟着等比重新分布,不需要等一帧
        /// 布局出结果再摆。<paramref name="dashCountH"/>/<paramref name="dashCountV"/> 分开传:
        /// 节点卡横向明显更宽,图例小色块则两个方向都给更少的段数。
        ///
        /// <see cref="BuildLegend"/> 与 <see cref="BuildNode"/> 的 <c>GatedPrereq</c> 分支共用
        /// 这一份实现(只有 dashCount 不同)——图例画的虚线因此是节点真实的虚线,不是另一套
        /// 「看着像」的示意。</summary>
        private static Image DashedPanel(Transform parent, string name, Color fill, Color border,
            int radius, int dashCountH = 5, int dashCountV = 3)
        {
            var face = Ui.CardPanel(parent, name, fill, radius);

            void Dash(bool horizontal, float crossMin, float crossMax, float along0, float along1)
            {
                var dash = Ui.Panel(face.transform, "Dash");
                var image = dash.AddComponent<Image>();
                image.color = border;
                image.raycastTarget = false;
                var (min, max) = horizontal
                    ? (new Vector2(along0, crossMin), new Vector2(along1, crossMax))
                    : (new Vector2(crossMin, along0), new Vector2(crossMax, along1));
                Ui.Anchor((RectTransform)dash.transform, min, max, Vector2.zero, Vector2.zero);
            }

            const float edge = 0.07f;   // 描边厚度(占短边的分数)
            const float run = 0.7f;     // 每段虚线占自己格位的比例,留 30% 当间隙
            void Edge(bool horizontal, float crossMin, float crossMax, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float slot = 1f / count;
                    Dash(horizontal, crossMin, crossMax, i * slot, i * slot + slot * run);
                }
            }
            Edge(true, 1f - edge, 1f, dashCountH);   // 顶边
            Edge(true, 0f, edge, dashCountH);        // 底边
            Edge(false, 0f, edge, dashCountV);       // 左边
            Edge(false, 1f - edge, 1f, dashCountV);  // 右边

            return face;
        }

        // ================= Core 数据的只读派生(不重复 PerkRules 的私有门槛表) =================

        // internal(而非 private):PerkNodeSheet 详情弹窗的头部水印/chip 要用同一份按枝配色,
        // 不在那边另起一套——否则枝色又会出现「两处不一致」的老问题(见图例那条 review)。
        internal static Color BranchColor(PerkNodeDef def) =>
            def.Element is { } el ? Theme.ElementColor(el) : Theme.PerkBranchColor(def.Branch);

        internal static Color BranchSoft(PerkNodeDef def) =>
            def.Element is { } el ? Theme.ElementSoft(el) : Theme.PerkBranchSoft(def.Branch);

        /// <summary>某树的枝列表,顺序取自 <see cref="PerkRules.Nodes"/> 里各枝首次出现的顺序——
        /// 不在这里另写一份枝名列表,表变了(加枝/调顺序)这里自动跟着变。</summary>
        private static List<string> BranchesOf(PerkTree tree)
        {
            var list = new List<string>();
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == tree && !list.Contains(def.Branch))
                    list.Add(def.Branch);
            return list;
        }

        private static int MaxDepthOf(PerkTree tree)
        {
            int max = 0;
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == tree) max = Math.Max(max, def.Depth);
            return max;
        }

        /// <summary>某树某深度的等级门槛。门槛按树统一、不按枝(<see cref="PerkRules"/> 的门槛数组
        /// 是私有的),故从该深度**任意**一个节点读 <see cref="PerkNodeDef.UnlockLevel"/> 即可。</summary>
        private static int GateLevel(PerkTree tree, int depth)
        {
            foreach (var def in PerkRules.Nodes)
                if (def.Tree == tree && def.Depth == depth) return def.UnlockLevel;
            return 0;
        }

        // internal:PerkNodeSheet 的面包屑(「五行树 · 金脉 · 第 4 层」)要读同一份枝名,
        // 不在那边重抄一份列表(列表变了两处会悄悄漂开)。
        internal static string BranchName(string branch) => branch switch
        {
            "metal" => Strings.T("perk.branch.metal"),
            "wood" => Strings.T("perk.branch.wood"),
            "water" => Strings.T("perk.branch.water"),
            "fire" => Strings.T("perk.branch.fire"),
            "earth" => Strings.T("perk.branch.earth"),
            "vigor" => Strings.T("perk.branch.vigor"),
            "power" => Strings.T("perk.branch.power"),
            "edge" => Strings.T("perk.branch.edge"),
            "guard" => Strings.T("perk.branch.guard"),
            "lore" => Strings.T("perk.branch.lore"),
            "wide" => Strings.T("perk.branch.wide"),
            "insight" => Strings.T("perk.branch.insight"),
            "qi" => Strings.T("perk.branch.qi"),
            _ => branch,
        };
    }
}
