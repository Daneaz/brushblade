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
    /// 不在这一层另起一套解锁逻辑。</summary>
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

        private static string TreeName(PerkTree tree) => tree switch
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

        private enum NodeState { Owned, CanUnlock, PoorInk, GatedPrereq, GatedLevel }

        /// <summary>判据顺序与 <see cref="PerkRules.CanUnlock"/> 内部完全一致(已点 → 前置 →
        /// 等级 → 墨锭),只是把「不能点」拆成三种理由分别显示——同源判据,不是另一套规则。</summary>
        private NodeState StateOf(PerkNodeDef def, int charLevel)
        {
            if (PerkRules.IsUnlocked(_meta, def.Id)) return NodeState.Owned;
            if (def.Depth > 1 && !PerkRules.IsUnlocked(_meta, $"{def.Branch}_{def.Depth - 1}"))
                return NodeState.GatedPrereq;
            if (charLevel < def.UnlockLevel) return NodeState.GatedLevel;
            if (_meta.Ink < def.InkCost) return NodeState.PoorInk;
            return NodeState.CanUnlock;
        }

        private void BuildNode(Transform parent, PerkNodeDef def, int charLevel)
        {
            var state = StateOf(def, charLevel);
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
                    // uGUI 没有内建虚线描边;用比等级门槛态更深一档的实线描边 + 专属文案区分两种
                    // 「点不了」,核心诉求(玩家能分清原因)已经满足,虚线本身是可接受的简化。
                    var prereqOuter = Ui.OutlinedPanel(parent, $"Node_{def.Id}", Theme.LockedBg, Theme.LockGray, NodeRadius, 1.5f, out face);
                    cell = clickTarget = prereqOuter.gameObject;
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
            button.interactable = state == NodeState.CanUnlock;
            if (state == NodeState.CanUnlock)
                button.onClick.AddListener(() =>
                {
                    if (PerkRules.TryUnlock(_meta, def.Id))
                    {
                        _save();
                        Build(); // 成功后刷新(墨锭计数/页签红点/网格状态全部同步)
                    }
                });

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

        private void BuildLegend(Transform parent)
        {
            var row = Ui.Row(parent, "Legend", 20);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            LegendItem(row.transform, Theme.DoneGreen, Strings.T("perk.legend.owned"));
            LegendItem(row.transform, Theme.Gold, Strings.T("perk.legend.unlockable"));
            LegendItem(row.transform, Theme.LockGray, Strings.T("perk.legend.poor_ink"));
            LegendItem(row.transform, Theme.InkSoft, Strings.T("perk.legend.gated_prereq"));
            LegendItem(row.transform, Theme.PanelBorder, Strings.T("perk.legend.gated_level"));
        }

        private static void LegendItem(Transform parent, Color color, string text)
        {
            var item = Ui.Row(parent, "Item", 6);
            var swatch = Ui.Panel(item.transform, "Swatch");
            var image = swatch.AddComponent<Image>();
            image.sprite = Theme.Rounded(4);
            image.type = Image.Type.Sliced;
            image.color = color;
            Ui.Sized(swatch, width: 14, height: 14);
            Ui.ThemedLabel(item.transform, text, 12, Theme.TextDim);
        }

        // ================= Core 数据的只读派生(不重复 PerkRules 的私有门槛表) =================

        private static Color BranchColor(PerkNodeDef def) =>
            def.Element is { } el ? Theme.ElementColor(el) : Theme.PerkBranchColor(def.Branch);

        private static Color BranchSoft(PerkNodeDef def) =>
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

        private static string BranchName(string branch) => branch switch
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
