using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能页(环形星域,spec 2026-09-08):**一张 1120×1120 的极坐标画布**,
    /// 屏幕只是它的视口 —— 可平移、可缩放。
    ///
    /// 取代 2026-09-07 的三页签规则网格。换掉的理由不是「网格不好看」,而是那一版把
    /// 43 个节点塞进 350px 的垂直空间里,所有布局努力都在和空间搏斗;把画布做得比屏幕大,
    /// 拥挤问题就从根上没有了。
    ///
    /// ⚠ 左下四个胶囊是**跳转锚点,不是页签**。跨树节点本来就跨在两棵树的扇区交界上,
    /// 切页会把它切碎 —— 那正是这次重做要消灭的东西。画布始终是同一张,四个胶囊只是
    /// 把视口平移过去。
    ///
    /// 节点五态(与设计规格 §8 一致):已点亮 / 可解锁 / 墨锭不足 / 前置未点 / 等级未到——
    /// 逐态给不同的底色/描边,判据全部走 <see cref="PerkRules"/> 现成的 <c>IsUnlocked</c>/
    /// <c>PrereqMet</c>,不在这一层另起一套解锁逻辑。点任意状态的节点一律先开
    /// <see cref="PerkNodeSheet"/> 详情弹窗,解锁动作在弹窗里 —— 43 个节点、单次不可撤销地
    /// 花几百到几千墨锭,卡面误触代价太高。
    ///
    /// <see cref="NodeState"/>/<see cref="StateOf"/>/<see cref="BranchColor"/>/
    /// <see cref="BranchSoft"/>/<see cref="BranchName"/>/<see cref="TreeName"/> 是 internal:
    /// <see cref="PerkNodeSheet"/> 复用同一份判据与配色,不在那边另起一套。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        // 视口内的画布缩放范围。捏合到底(MinZoom)时 43 个节点一屏看完,
        // 此时**不画图标与名字**(spec §8.2)—— 52px 的节点缩到 20px,画什么都是糊的。
        private const float MinZoom = 0.36f;
        private const float MaxZoom = 1.15f;
        private const float DetailZoom = 0.55f;   // 低于此值只画色块

        // 节点圆角取 20 而不是「半径 = 26 的正圆」:Theme.Rounded 是 9-slice,
        // radius 26 的九宫格边框合计 56 > 节点的 52,拉伸时四角会互相盖住糊成一团。
        // 20 的边框合计 44,在 52 见方里画出来已经近乎正圆。
        private const int NodeRadius = 20;
        private const float NodeIconSize = 22f;
        private const int NodeNameSize = 13;

        private const float LineWidth = 3f;
        private const float DashRun = 10f;
        private const float DashGap = 7f;

        private const float CrossRingPad = 7f;    // 虚线环离节点边沿多远
        private const float CrossRingDot = 5f;
        private const int CrossRingDashes = 16;

        private const float TopBarH = 76f;
        private const float JumpPillW = 72f;
        private const float JumpPillH = 38f;
        private const float JumpGap = 8f;
        private const float EdgePad = 20f;

        private const float MinimapSize = 156f;
        private const float MinimapDot = 5f;

        private const float LegendSwatchSize = 18f;
        private const int LegendSwatchRadius = 5;
        // 图例「可解锁」用比「已点亮」更粗的描边代表节点实际的「描边 + 主色外发光」双重强调——
        // 缩略图画不出发光,用描边粗细近似那份额外强调,不要求两处线宽字节相同。
        private const float LegendBoldBorder = 3.5f;
        private const float LegendW = 740f;
        private const float LegendH = 34f;

        private MetaState _meta;
        private Action _save;
        private Action _onBack;

        private RectTransform _viewport;   // 裁剪框
        private RectTransform _canvas;     // 1120×1120 的内容层,平移缩放作用在它上面

        /// <summary>画布缩放。**写入点只有两处**:<see cref="Build"/> 的初值,与
        /// <see cref="OnZoomChanged"/>(手势唯一的回写口)。读取点见
        /// <see cref="UpdateMinimapFrame"/> 与 <see cref="JumpTo"/> 的注释。</summary>
        private float _zoom = 0.7f;

        /// <summary>重建前记下的视口落点。<see cref="Build"/> 会把整棵层级删掉重建
        /// (解锁一个节点后要刷新墨锭数与五态),不记这一下,玩家每点亮一个节点
        /// 视野就被弹回中心。**只在 Build 里读写**,不参与运行期的任何判断。</summary>
        private Vector2 _pan = Vector2.zero;

        private bool _detailShown = true;
        private readonly List<GameObject> _detailBits = new();
        private RectTransform _minimapFrame;

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
            // 重建前先把视口落点收进 _pan —— Ui.Clear 之后 _canvas 就读不到了
            if (_canvas != null) _pan = _canvas.anchoredPosition;
            _detailBits.Clear();
            _minimapFrame = null;
            _canvas = null;
            _viewport = null;

            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            var card = Ui.CardPanel(transform, "Panel");
            Ui.Anchor((RectTransform)card.transform,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), Vector2.zero, Vector2.zero);

            BuildCanvasViewport(card.transform);   // 先铺画布,其余四块浮在它之上
            BuildTopBar(card.transform);
            BuildJumpAnchors(card.transform);
            BuildLegend(card.transform);
            BuildMinimap(card.transform);
        }

        private void OnNodeChanged()
        {
            _save();
            Build(); // 成功后刷新(墨锭计数 / 五态 / 连线 / 缩略图全部同步)
        }

        // ================= 画布与手势 =================

        private void BuildCanvasViewport(Transform parent)
        {
            var viewport = Ui.Panel(parent, "Viewport");
            Ui.Stretch((RectTransform)viewport.transform);
            // ⚠ 空白处也要接得到射线,否则「按在节点与节点之间的缝上拖不动」——
            // uGUI 的拖拽是从**按下的那个物件**往上冒泡找 IDragHandler 的,射线什么都没打中
            // 就根本不会产生事件(同 Ui.ScrollList 的 catcher)。而星域上绝大部分面积是空白。
            var catcher = viewport.AddComponent<Image>();
            catcher.color = Color.clear;
            catcher.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();
            _viewport = (RectTransform)viewport.transform;

            var canvas = Ui.Panel(viewport.transform, "Canvas");
            _canvas = (RectTransform)canvas.transform;
            _canvas.anchorMin = _canvas.anchorMax = new Vector2(0.5f, 0.5f);
            _canvas.pivot = new Vector2(0.5f, 0.5f);
            _canvas.sizeDelta = new Vector2(PerkLayout.CanvasSize, PerkLayout.CanvasSize);
            _canvas.localScale = Vector3.one * _zoom;
            _canvas.anchoredPosition = _pan;

            // 拖拽平移 + 双指缩放。挂在 viewport 上而不是 canvas 上 ——
            // 挂在 canvas 上时,拖到边缘会因为 canvas 本身移出视口而丢失后续事件。
            var pan = viewport.AddComponent<PerkCanvasPan>();
            pan.Init(_viewport, _canvas, MinZoom, MaxZoom, OnZoomChanged);

            BuildConnectors(canvas.transform);   // 连线在下
            BuildHub(canvas.transform);
            BuildRoots(canvas.transform);
            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            foreach (var def in PerkRules.Nodes)
                BuildNode(canvas.transform, def, charLevel);   // 节点在上

            _detailShown = _zoom >= DetailZoom;
            ApplyDetail(_detailShown);
        }

        /// <summary>手势改完缩放后的唯一回写口:<see cref="_zoom"/> 只在这里跟着走。
        ///
        /// ⚠ **只在跨过 <see cref="DetailZoom"/> 阈值时切一次**图标与名字的显隐,
        /// 不是每帧判断,更不是重建节点 —— 捏合中途重建 GameObject 会掉帧,
        /// 也会把玩家手指底下那个节点整个换掉(CLAUDE.md「拖拽期间不许重绘」)。</summary>
        private void OnZoomChanged(float zoom)
        {
            _zoom = zoom;
            bool want = _zoom >= DetailZoom;
            if (want == _detailShown) return;
            _detailShown = want;
            ApplyDetail(want);
        }

        private void ApplyDetail(bool shown)
        {
            foreach (var bit in _detailBits)
                if (bit != null) bit.SetActive(shown);
        }

        /// <summary>PerkLayout 的画布坐标(左上原点,y 向下)→ canvas 的 anchoredPosition
        /// (中心原点,y 向上)。两处翻 y —— 漏翻的症状是整棵树上下颠倒,一眼能看出来。</summary>
        private static Vector2 ToAnchored(Vector2 canvasPoint) => new Vector2(
            canvasPoint.x - PerkLayout.Center,
            PerkLayout.Center - canvasPoint.y);

        /// <summary>把一个居中锚定的方块摆到画布上的某点。</summary>
        private static void PlaceOnCanvas(RectTransform rect, Vector2 canvasPoint, float diameter)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(diameter, diameter);
            rect.anchoredPosition = ToAnchored(canvasPoint);
        }

        // ================= 中心「斗」与三个树根 =================

        private void BuildHub(Transform parent)
        {
            var go = Ui.Panel(parent, "Hub");
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Circle;
            image.color = Theme.Ink;
            image.raycastTarget = false;
            PlaceOnCanvas((RectTransform)go.transform, PerkLayout.HubPlace(), PerkLayout.HubRadius * 2f);

            var label = Ui.ThemedLabel(go.transform, Strings.T("perk.view.hub"), 34,
                Theme.Paper, Theme.TitleFont);
            label.raycastTarget = false;
            Ui.Stretch(label.rectTransform);
        }

        /// <summary>三个树根:**纯分区标记,不是可解锁节点**(spec §2.4)。每棵树 L1 的连线
        /// 连到它,跨树节点的两条虚线也连到它 —— 它是「这一片属于哪棵树」的锚,不吃点击。</summary>
        private void BuildRoots(Transform parent)
        {
            foreach (var tree in new[] { PerkTree.Wuxing, PerkTree.Passive, PerkTree.Mechanic })
            {
                var go = Ui.Panel(parent, $"Root_{tree}");
                var image = go.AddComponent<Image>();
                image.sprite = Theme.Circle;
                image.color = Theme.PanelInset;
                image.raycastTarget = false;
                PlaceOnCanvas((RectTransform)go.transform, PerkLayout.RootPlace(tree), 64f);

                var label = Ui.ThemedLabel(go.transform, RootName(tree), 18,
                    Theme.TextDim, Theme.TitleFont);
                label.raycastTarget = false;
                Ui.Stretch(label.rectTransform);
                _detailBits.Add(label.gameObject);
            }
        }

        // ================= 连线 =================

        /// <summary>每个节点连到它的前置。普通节点连同枝上一层(Depth 1 连到本树根);
        /// **跨树节点连到两棵树的树根**,虚线 —— 它的前置是谓词(「任一五行 L3」),
        /// 连到某个具体节点是撒谎。</summary>
        private void BuildConnectors(Transform parent)
        {
            foreach (var def in PerkRules.Nodes)
            {
                var to = PerkLayout.Place(def);
                if (def.Tree == PerkTree.Cross)
                {
                    var crossColor = PerkRules.PrereqMet(_meta, def)
                        ? BranchColor(def) : Theme.PanelBorder;
                    foreach (var req in def.Prereq)
                        DrawLine(parent, PerkLayout.RootPlace(req.Tree), to, crossColor, dashed: true);
                    continue;
                }

                Vector2 from;
                bool lit;
                if (def.Depth <= 1)
                {
                    from = PerkLayout.RootPlace(def.Tree);
                    lit = PerkRules.IsUnlocked(_meta, def.Id);
                }
                else
                {
                    var prev = PerkRules.Get($"{def.Branch}_{def.Depth - 1}");
                    from = PerkLayout.Place(prev);
                    lit = PerkRules.IsUnlocked(_meta, prev.Id);
                }
                DrawLine(parent, from, to, lit ? BranchColor(def) : Theme.PanelBorder, dashed: false);
            }
        }

        /// <summary>画布上的一条线段:一张按长度定宽、按方向旋转的细长 Image。
        /// 虚线拆成若干短段 —— uGUI 没有内建虚线,而这里的线长各不相同(极坐标上的
        /// 每一条都不一样长),没法像 <see cref="DashedPanel"/> 那样按分数锚点铺。</summary>
        private static void DrawLine(Transform parent, Vector2 fromCanvas, Vector2 toCanvas,
            Color color, bool dashed)
        {
            var a = ToAnchored(fromCanvas);
            var b = ToAnchored(toCanvas);
            var delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f) return;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var dir = delta / length;

            if (!dashed)
            {
                Segment(parent, a + dir * (length / 2f), length, angle, color);
                return;
            }

            float step = DashRun + DashGap;
            for (float start = 0f; start < length; start += step)
            {
                float run = Mathf.Min(DashRun, length - start);
                if (run <= 0.5f) break;
                Segment(parent, a + dir * (start + run / 2f), run, angle, color);
            }
        }

        private static void Segment(Transform parent, Vector2 center, float length, float angle, Color color)
        {
            var go = Ui.Panel(parent, "Seg");
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(length, LineWidth);
            rect.anchoredPosition = center;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ================= 节点 =================

        internal enum NodeState { Owned, CanUnlock, PoorInk, GatedPrereq, GatedLevel }

        /// <summary>判据顺序与 <see cref="PerkRules.CanUnlock"/> 内部完全一致(已点 → 前置 →
        /// 等级 → 墨锭),只是把「不能点」拆成三种理由分别显示——同源判据,不是另一套规则。
        /// internal static(而非 private 实例方法):<see cref="PerkNodeSheet"/> 的底部操作钮
        /// 要用同一份判据决定「解锁 / 置灰 + 理由」,不能另起一份读 meta 的逻辑。
        ///
        /// ⚠ 前置判定走 <see cref="PerkRules.PrereqMet"/> 而**不是**在这里重抄一份同枝推导:
        /// 跨树节点的 Depth 恒为 1,抄来的 `def.Depth > 1 && …` 那行对它恒不成立,
        /// 会把前置一个都没满足的相济显示成「可解锁」,点下去毫无反应(2026-09-08)。</summary>
        internal static NodeState StateOf(MetaState meta, PerkNodeDef def, int charLevel)
        {
            if (PerkRules.IsUnlocked(meta, def.Id)) return NodeState.Owned;
            if (!PerkRules.PrereqMet(meta, def)) return NodeState.GatedPrereq;
            if (charLevel < def.UnlockLevel) return NodeState.GatedLevel;
            if (meta.Ink < def.InkCost) return NodeState.PoorInk;
            return NodeState.CanUnlock;
        }

        private void BuildNode(Transform parent, PerkNodeDef def, int charLevel)
        {
            var state = StateOf(_meta, def, charLevel);
            var main = BranchColor(def);
            var soft = BranchSoft(def);

            GameObject cell;        // 摆到画布上的那个物件
            GameObject clickTarget; // 真正吃点击的物件(OutlinedPanel 的 face 描边为 0 射线,按钮得挂外层)
            Image face;             // 内容(图标/名字)挂载点

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

            PlaceOnCanvas((RectTransform)cell.transform, PerkLayout.Place(def), PerkLayout.NodeDiameter);

            bool gated = state == NodeState.GatedPrereq || state == NodeState.GatedLevel;

            // 跨树节点:外圈虚线环(表示「跨」)。环画在节点**之外**,不占内部那 48px。
            // 两个 gated 态一起压灰 —— 只压 GatedPrereq 会让「等级未到」的灰节点顶着一圈
            // 亮色环,读起来像是可以点。
            if (def.Tree == PerkTree.Cross)
                AddCrossRing(cell.transform, gated ? Theme.LockGray : main);

            var button = clickTarget.AddComponent<Button>();
            button.targetGraphic = face;
            // 五态统一打开详情弹窗——解锁动作也在弹窗里,卡面本身不再直接出手扣钱。
            // ⚠ 挂在 transform(本视图根)而不是 transform.root:挂到 Canvas 根上时,
            // 从技能页退回地图会把视图删掉、而弹窗留在屏幕上。
            button.onClick.AddListener(() =>
                PerkNodeSheet.Show(transform, _meta, def, OnNodeChanged));

            var content = Ui.VStack(face.transform, "Content", 1);
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(3, 2), new Vector2(-3, -2));

            AddIcon(content.transform, PerkNodeIcons.KeyFor(def), gated ? Theme.LockGray : main);
            AddName(content.transform, PerkInfo.Name(def), gated ? Theme.LockGray : Theme.TextMain);
        }

        /// <summary>节点里的那枚图标。PNG 取不到就回落成汉字徽章(<see cref="Icons.Fallback"/>)——
        /// 两条路占同样的宽,布局不受资产有无影响。</summary>
        private void AddIcon(Transform parent, string key, Color tint)
        {
            var go = Ui.Panel(parent, "Icon");
            Ui.Sized(go, width: NodeIconSize, height: NodeIconSize);
            var sprite = Icons.Get(key);
            if (sprite != null)
            {
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                image.color = tint;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
            {
                var glyph = Ui.ThemedLabel(go.transform, Icons.Fallback(key),
                    Mathf.RoundToInt(NodeIconSize * 0.85f), tint, Theme.TitleFont);
                glyph.raycastTarget = false;
                Ui.Stretch(glyph.rectTransform);
            }
            _detailBits.Add(go);
        }

        private void AddName(Transform parent, string text, Color color)
        {
            var label = Ui.ThemedLabel(parent, text, NodeNameSize, color, Theme.TitleFont);
            label.raycastTarget = false;
            _detailBits.Add(label.gameObject);
        }

        /// <summary>跨树节点的外圈虚线环:一圈小圆点。用点而不是弧段 —— uGUI 画不出弧,
        /// 而 16 个点在 52px 的节点外圈上已经读得出是「虚的一圈」。</summary>
        private static void AddCrossRing(Transform parent, Color color)
        {
            float radius = PerkLayout.NodeDiameter / 2f + CrossRingPad;
            for (int i = 0; i < CrossRingDashes; i++)
            {
                float rad = 360f / CrossRingDashes * i * Mathf.Deg2Rad;
                var dot = Ui.Panel(parent, "Ring");
                var image = dot.AddComponent<Image>();
                image.sprite = Theme.Circle;
                image.color = color;
                image.raycastTarget = false;
                var rect = (RectTransform)dot.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(CrossRingDot, CrossRingDot);
                rect.anchoredPosition = new Vector2(radius * Mathf.Cos(rad), radius * Mathf.Sin(rad));
            }
        }

        // ================= 顶栏(浮在画布之上) =================

        /// <summary>⚠ 顶栏**浮在画布之上,不是并排** —— 并排就等于又把画布压回一块小区域,
        /// 这次重做的前提(屏幕只是视口)也就没了。底下垫两层半透明宣纸当渐隐
        /// (uGUI 没有渐变,也不在这个 Task 里往 Theme 新造资源),免得节点滑到标题
        /// 后面糊成一团(spec §8.4)。</summary>
        private void BuildTopBar(Transform parent)
        {
            var bar = Ui.Panel(parent, "TopBar");
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -TopBarH), Vector2.zero);

            Fade(bar.transform, 0.90f, 0.45f, 1f);   // 上半段近乎实底
            Fade(bar.transform, 0.45f, 0f, 0.45f);   // 下半段淡出

            var row = Ui.Row(bar.transform, "Bar", 14);
            Ui.Anchor((RectTransform)row.transform, Vector2.zero, Vector2.one,
                new Vector2(EdgePad, 6f), new Vector2(-EdgePad, -6f));
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            var titles = Ui.VStack(row.transform, "Titles", 2);
            titles.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var title = Ui.ThemedLabel(titles.transform, Strings.T("perk.view.title"), 26,
                Theme.TextMain, Theme.TitleFont);
            title.alignment = TextAnchor.MiddleLeft;
            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            var subtitle = Ui.ThemedLabel(titles.transform,
                Strings.T("perk.view.subtitle", ("level", charLevel)), 14, Theme.TextDim);
            subtitle.alignment = TextAnchor.MiddleLeft;

            var spring = Ui.Panel(row.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 手势提示:画布能拖能缩这件事本身没有任何视觉线索,不说玩家不会试
            Ui.ThemedLabel(row.transform, Strings.T("perk.view.zoom_hint"), 13, Theme.TextDim);

            Ui.InkCounter(row.transform, _meta.Ink, 20);
            Ui.PillButton(row.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 18, new Vector2(150, 44));
        }

        private static void Fade(Transform parent, float alpha, float anchorMinY, float anchorMaxY)
        {
            var go = Ui.Panel(parent, "Fade");
            var image = go.AddComponent<Image>();
            image.color = new Color(Theme.Paper.r, Theme.Paper.g, Theme.Paper.b, alpha);
            image.raycastTarget = false;
            Ui.Anchor((RectTransform)go.transform, new Vector2(0f, anchorMinY),
                new Vector2(1f, anchorMaxY), Vector2.zero, Vector2.zero);
        }

        // ================= 跳转锚点(左下四个胶囊) =================

        /// <summary>左下四个胶囊 = 跳转锚点,点一下把视口平移过去。
        ///
        /// ⚠ **不是页签**。跨树节点本来就跨在两棵树的边界上,切页会把它切碎 ——
        /// 那正是这次重做要消灭的东西。</summary>
        private void BuildJumpAnchors(Transform parent)
        {
            var row = Ui.Row(parent, "Jump", JumpGap);
            float rowW = 4f * JumpPillW + 3f * JumpGap;
            Ui.Anchor((RectTransform)row.transform, Vector2.zero, Vector2.zero,
                new Vector2(EdgePad, EdgePad), new Vector2(EdgePad + rowW, EdgePad + JumpPillH));

            foreach (var tree in new[] { PerkTree.Wuxing, PerkTree.Passive,
                                         PerkTree.Mechanic, PerkTree.Cross })
            {
                var t = tree;
                Ui.PillButton(row.transform, JumpName(t), () => JumpTo(t),
                    Theme.PanelPaper, Theme.TextMain, 16, new Vector2(JumpPillW, JumpPillH));
            }
        }

        /// <summary>把视口居中到目标点:canvas 反向平移 目标点 × 当前缩放。
        ///
        /// ⚠ 乘的是 <see cref="_zoom"/> 的**当前值**,不是初值 —— 缩放后再跳转,
        /// 用旧倍率算出来的落点会偏(偏移量正比于缩放变化率)。<see cref="_zoom"/>
        /// 由 <see cref="OnZoomChanged"/> 在手势改完的同一帧同步回来,这里读到的一定是新值。</summary>
        private void JumpTo(PerkTree tree)
        {
            if (_canvas == null) return;
            var target = ToAnchored(PerkLayout.JumpTarget(tree));
            _canvas.anchoredPosition = -target * _zoom;
            // 缩略图的红框下一帧由 LateUpdate 照读 _canvas.anchoredPosition 更新,
            // 不需要在这里再通知一次(见 UpdateMinimapFrame 的注释)。
        }

        // ================= 缩略图(右下圆形) =================

        /// <summary>右下圆形缩略图 = 全局位置。只画 43 个圆点 + 一个视口框,不画图标名字 ——
        /// 它回答的是「我在哪」,不是「这里有什么」。</summary>
        private void BuildMinimap(Transform parent)
        {
            var go = Ui.Panel(parent, "Minimap");
            var disc = go.AddComponent<Image>();
            disc.sprite = Theme.Circle;
            disc.color = new Color(Theme.PanelPaper.r, Theme.PanelPaper.g, Theme.PanelPaper.b, 0.92f);
            disc.raycastTarget = false;
            // 右下角贴边:anchorMin == anchorMax == (1,0),offset 就是相对那个角的像素偏移
            Ui.Anchor((RectTransform)go.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-MinimapSize - EdgePad, EdgePad), new Vector2(-EdgePad, MinimapSize + EdgePad));
            // 视口框大过画布时(视口比画布宽的那些机型)要裁掉,别糊到胶囊上
            go.AddComponent<RectMask2D>();

            float k = MinimapSize / PerkLayout.CanvasSize;
            foreach (var def in PerkRules.Nodes)
            {
                var dot = Ui.Panel(go.transform, $"Dot_{def.Id}");
                var image = dot.AddComponent<Image>();
                image.sprite = Theme.Circle;
                image.color = PerkRules.IsUnlocked(_meta, def.Id) ? BranchColor(def) : Theme.PaperDim;
                image.raycastTarget = false;
                var dotRect = (RectTransform)dot.transform;
                dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(MinimapDot, MinimapDot);
                dotRect.anchoredPosition = ToAnchored(PerkLayout.Place(def)) * k;
            }

            var frame = Ui.Panel(go.transform, "Frame");
            var frameImage = frame.AddComponent<Image>();
            frameImage.sprite = Theme.Rounded(4);
            frameImage.type = Image.Type.Sliced;
            frameImage.fillCenter = false;   // 空心 = 只剩一圈边,正好当视口框
            frameImage.color = Theme.Cinnabar;
            frameImage.raycastTarget = false;
            _minimapFrame = (RectTransform)frame.transform;
            _minimapFrame.anchorMin = _minimapFrame.anchorMax = new Vector2(0.5f, 0.5f);
            _minimapFrame.pivot = new Vector2(0.5f, 0.5f);
            UpdateMinimapFrame();
        }

        /// <summary>视口框 = 「此刻屏幕上看得见的那块画布」。
        ///
        /// ⚠ 它是 <see cref="_zoom"/> 与 <c>_canvas.anchoredPosition</c> 这两份共享状态的
        /// **第三个读取点**(另两个是手势与跳转锚点,它们都是写入方)。放在
        /// <see cref="LateUpdate"/> 里逐帧照读,而不是让两个写入点各自记得通知一次 ——
        /// 漏通知的症状是「点跳转后红框还停在原处」「缩放后红框大小不变」,
        /// 离线编译一个都抓不到,而这里逐帧读就不存在漏通知这回事。
        /// 每帧的代价是两次 RectTransform 赋值,不重建任何 GameObject。
        ///
        /// <c>_viewport.rect</c> 要等一次布局才有值,逐帧读顺带把「建好那一帧还是 0」
        /// 也兜住了。</summary>
        private void UpdateMinimapFrame()
        {
            if (_minimapFrame == null || _canvas == null || _viewport == null) return;
            float zoom = Mathf.Max(0.01f, _zoom);
            float k = MinimapSize / PerkLayout.CanvasSize;
            var visible = _viewport.rect.size / zoom;                 // 可见范围,画布单位
            var center = -_canvas.anchoredPosition / zoom;            // 视口中心落在画布的哪
            _minimapFrame.sizeDelta = new Vector2(
                Mathf.Min(visible.x, PerkLayout.CanvasSize) * k,
                Mathf.Min(visible.y, PerkLayout.CanvasSize) * k);
            _minimapFrame.anchoredPosition = center * k;
        }

        private void LateUpdate() => UpdateMinimapFrame();

        // ================= 图例 =================

        /// <summary>2026-09-07 收尾波(review 修 Critical):五态图例此前给了五个**固定色块**
        /// (绿/金/灰/…),但节点实际是按枝取色(<see cref="BranchColor"/>,16 种)——玩家照
        /// 「已点亮=绿」去认,回头看火脉已点亮的节点是暗红、水脉是蓝,图例在事实层面就是错的。
        ///
        /// 改法:五个色块统一用中性色,靠**边框样式**区分五态,且直接复用 <see cref="BuildNode"/>
        /// 实际画节点用的那几个图元(<see cref="Ui.OutlinedPanel"/> / <see cref="Ui.CardPanel"/> /
        /// <see cref="DashedPanel"/>)—— 保证图例的形状真的是节点的形状,不是另画一套
        /// 「看着差不多」的示意。末尾补一句「颜色随枝而变,形状表示状态」。
        ///
        /// ⚠ 环形改造保留了这一条(而不是随页签一起删掉):节点仍然是形状编码五态,
        /// 删掉图例等于把上面那条 review 结论又退回去。位置从「页面底部一行」改成
        /// 底边正中的浮条,夹在跳转锚点与缩略图之间。</summary>
        private void BuildLegend(Transform parent)
        {
            var block = Ui.CardPanel(parent, "Legend",
                new Color(Theme.PanelPaper.r, Theme.PanelPaper.g, Theme.PanelPaper.b, 0.92f), 16);
            var rect = (RectTransform)block.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(LegendW, LegendH);
            rect.anchoredPosition = new Vector2(0f, EdgePad);
            block.raycastTarget = false;

            var row = Ui.Row(block.transform, "Items", 16);
            Ui.Stretch((RectTransform)row.transform);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

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

            var note = Ui.ThemedLabel(row.transform, Strings.T("perk.legend.note"), 12, Theme.TextDim);
            note.raycastTarget = false;
        }

        private static void LegendItem(Transform parent, Func<Transform, Image> swatch, string text)
        {
            var item = Ui.Row(parent, "Item", 5);
            var image = swatch(item.transform);
            image.raycastTarget = false;
            Ui.Sized(image.gameObject, width: LegendSwatchSize, height: LegendSwatchSize);
            var label = Ui.ThemedLabel(item.transform, text, 12, Theme.TextDim);
            label.raycastTarget = false;
        }

        /// <summary>虚线描边:圆角底 + 沿四边分数锚点摆的短线段。uGUI 没有内建虚线描边,
        /// 四条边都按**分数**锚点摆(而不是像素偏移)——不管最终分到多宽,虚线段都跟着
        /// 等比重新分布,不需要等一帧布局出结果再摆。<paramref name="dashCountH"/>/
        /// <paramref name="dashCountV"/> 分开传:节点是正方,图例小色块两个方向都给更少的段数。
        ///
        /// <see cref="BuildLegend"/> 与 <see cref="BuildNode"/> 的 <c>GatedPrereq</c> 分支共用
        /// 这一份实现(只有 dashCount 不同)——图例画的虚线因此是节点真实的虚线,不是另一套
        /// 「看着像」的示意。</summary>
        private static Image DashedPanel(Transform parent, string name, Color fill, Color border,
            int radius, int dashCountH = 4, int dashCountV = 4)
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

        // internal:PerkNodeSheet 的面包屑同样要读树名,理由同 BranchName。
        internal static string TreeName(PerkTree tree) => tree switch
        {
            PerkTree.Wuxing => Strings.T("perk.tree.wuxing"),
            PerkTree.Passive => Strings.T("perk.tree.passive"),
            PerkTree.Cross => Strings.T("perk.tree.cross"),
            _ => Strings.T("perk.tree.mechanic"),
        };

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
            // 跨树三枝(2026-09-08)。不补这三条,详情弹窗的面包屑会印出英文 branch 名。
            "xvigor" => Strings.T("perk.branch.xvigor"),
            "xedge" => Strings.T("perk.branch.xedge"),
            "xdraw" => Strings.T("perk.branch.xdraw"),
            _ => branch,
        };

        /// <summary>跳转胶囊上的字。逐条写字面 key 而不是 <c>Strings.T($"perk.view.jump.{…}")</c>——
        /// 字符串表对账只认字面量,拼出来的 key 会让那四条被判成孤儿(CLAUDE.md)。</summary>
        private static string JumpName(PerkTree tree) => tree switch
        {
            PerkTree.Wuxing => Strings.T("perk.view.jump.wuxing"),
            PerkTree.Passive => Strings.T("perk.view.jump.passive"),
            PerkTree.Mechanic => Strings.T("perk.view.jump.mechanic"),
            _ => Strings.T("perk.view.jump.cross"),
        };

        /// <summary>树根标记上的字。同上,逐条字面 key。</summary>
        private static string RootName(PerkTree tree) => tree switch
        {
            PerkTree.Wuxing => Strings.T("perk.view.root.wuxing"),
            PerkTree.Passive => Strings.T("perk.view.root.passive"),
            _ => Strings.T("perk.view.root.mechanic"),
        };
    }

    /// <summary>技能树画布的平移与缩放手势(2026-09-08)。只服务 <see cref="PerkView"/> 一个视图,
    /// 所以与它同文件 —— 单独开一个文件是过度拆分(同 SafeArea.cs 里的 SafeAreaFitter)。
    ///
    /// ⚠ **不要把画布塞进 ScrollRect**。看上去 ScrollRect 白送平移,但那会立刻撞上
    /// 「拖拽与滚动共用一根手指」那个老问题:节点上的 Button 与外层 ScrollRect 抢手势,
    /// 要么点不动节点、要么滑不动画布。这里画布是唯一可拖的东西、没有嵌套滚动,
    /// 直接实现 IDragHandler 反而干净。
    ///
    /// ⚠ **拖拽期间不改任何 GameObject**:OnDrag 只写 anchoredPosition。缩放同理,
    /// 只写 localScale + anchoredPosition,「画不画图标名字」的切换交给
    /// <see cref="PerkView.OnZoomChanged"/> 在跨阈值时切一次。</summary>
    public sealed class PerkCanvasPan : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IScrollHandler
    {
        /// <summary>滚轮一格换算成多少倍率(编辑器里替代双指捏合)。</summary>
        private const float ScrollZoomStep = 0.08f;

        private RectTransform _viewport;
        private RectTransform _canvas;
        private float _min = 0.36f;
        private float _max = 1.15f;
        private Action<float> _onZoom;

        /// <summary>上一帧两指间距;0 = 此刻不在捏合。</summary>
        private float _pinchPrev;

        public void Init(RectTransform viewport, RectTransform canvas,
            float min, float max, Action<float> onZoom)
        {
            _viewport = viewport;
            _canvas = canvas;
            _min = min;
            _max = max;
            _onZoom = onZoom;
        }

        // 空实现是必需的:uGUI 只把拖拽事件发给同时实现了 IDragHandler 的对象,
        // 少了 OnBeginDrag 这一条链路照样起得来,但保留它是为了与 SwipePager 一样
        // 明示「这个物件吃拖拽」,并挡住冒泡到更外层的可能。
        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (_canvas == null) return;
            // 两指在屏时把平移让给捏合:两根手指各自的 delta 会把画布拽出一段
            // 与缩放无关的位移,松手后位置就飘了
            if (Input.touchCount >= 2) return;
            _canvas.anchoredPosition += eventData.delta / ScaleFactor();
        }

        public void OnScroll(PointerEventData eventData) =>
            ApplyZoom(1f + eventData.scrollDelta.y * ScrollZoomStep);

        private void Update()
        {
            if (_canvas == null || Input.touchCount != 2) { _pinchPrev = 0f; return; }
            float dist = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
            if (_pinchPrev > 1f) ApplyZoom(dist / _pinchPrev);
            _pinchPrev = dist;
        }

        /// <summary>以**视口中心**为不动点缩放:anchoredPosition 与 zoom 同比走,
        /// 中心底下那块画布不动。不动点取两指中点会让双指微抖把整张图带着漂,
        /// 而这块画布只有 1120 见方、缩放范围也窄,取中心足够。</summary>
        private void ApplyZoom(float factor)
        {
            if (_canvas == null || factor <= 0f) return;
            float before = _canvas.localScale.x;
            float after = Mathf.Clamp(before * factor, _min, _max);
            if (Mathf.Approximately(after, before)) return;
            _canvas.localScale = Vector3.one * after;
            _canvas.anchoredPosition *= after / before;
            _onZoom?.Invoke(after);
        }

        /// <summary>屏幕像素 → 逻辑单位。CanvasScaler 按 1600×900 缩放,
        /// eventData.delta 是**屏幕像素**,不除这一下画布会跟手跟得过快或过慢。</summary>
        private float ScaleFactor()
        {
            var canvas = _viewport != null ? _viewport.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }
    }
}
