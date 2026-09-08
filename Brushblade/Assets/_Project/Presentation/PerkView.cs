using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能页(环形星域,spec 2026-09-08):**一张 1220×1220 的极坐标画布**,
    /// 屏幕只是它的视口 —— 可平移、可缩放。
    ///
    /// 取代 2026-09-07 的三页签规则网格。换掉的理由不是「网格不好看」,而是那一版把
    /// 43 个节点塞进 350px 的垂直空间里,所有布局努力都在和空间搏斗;把画布做得比屏幕大,
    /// 拥挤问题就从根上没有了。
    ///
    /// ⚠ **没有页签,也没有跳转胶囊与状态图例**(2026-09-08 用户裁定:DAG 本身一目了然,
    /// 左下角那两块是多余的解释)。画布始终是同一张,靠拖拽 / 捏合 / 右下缩略图导航。
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

        // 80 = BestiaryView / CollectionView 的 TopH,同一套顶栏就用同一个高度。
        private const float TopBarH = 80f;
        private const float EdgePad = 20f;

        private const float MinimapSize = 156f;
        private const float MinimapDot = 5f;

        private MetaState _meta;
        private Action _save;
        private Action _onBack;

        private RectTransform _viewport;   // 裁剪框
        private RectTransform _canvas;     // 1220×1220 的内容层,平移缩放作用在它上面

        /// <summary>画布缩放。**写入点只有两处**:<see cref="Build"/> 的初值,
        /// 与 <see cref="OnZoomChanged"/>(手势唯一的回写口)。读取点只有
        /// <see cref="UpdateMinimapFrame"/>。跳转胶囊删掉后,非手势改缩放的那条路径
        /// (ZoomTo / FitCrossZoom)一并没了。</summary>
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

            // ⚠ **满屏铺开,不套内缩的 CardPanel**(2026-09-08 用户指出排版对不齐)。
            // 此前外面裹了一层 0.06~0.94 / 0.05~0.95 的卡,于是这一页的「左上角」其实落在
            // 屏幕的 6% / 5% 处,标题与返回键跟图鉴/收藏/商城差了一截。
            // 宣纸底由 GameRoot.NewView 全屏铺好了,这里不需要再垫一张卡。
            // 结构与 BestiaryView / CollectionView 逐字同款:SafeArea 补差额 → 全幅 Content。
            var (padSide, padBottom) = SafeArea.MissingInset();
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frame = content.transform;

            BuildCanvasViewport(frame);   // 先铺画布,其余四块浮在它之上
            BuildTopBar(frame);
            BuildMinimap(frame);
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
                // 跨树节点**一条连线都不画**(2026-09-08 用户裁定)。此前是从它所连两棵树的
                // 树根(半径 100,紧挨中心)各拉一条虚线到半径 545 —— 那两条线要横穿整个扇区,
                // 把沿途每个节点都串一遍,画面被切得稀碎。
                //
                // 前置关系改由详情面板的「跨树前置」两栏表达,那也更准:它的前置是谓词
                // (「五行任一 L3」),连到树根只是把「跟这棵树有关」画成了一条假的依赖边。
                // 它们现在靠**位置**(贴着取材最多的那棵树的外沿另起一组)与**外圈虚线环**
                // 说明自己是另一类,见 PerkLayout.CrossAngle 的注释。
                if (def.Tree == PerkTree.Cross) continue;

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
        /// 每一条都不一样长),没法像四边描边那样按分数锚点均匀铺。</summary>
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
                // PoorInk / GatedPrereq / GatedLevel 三档**共用同一种画法**(2026-09-08 用户裁定
                // 「只需要区分三类」)。此前它们各画各的(常规底 / 灰底虚线描边 / 纯灰底),
                // 加上前两态一共五种,图例得排五格,画布上也读不出层次。
                //
                // ⚠ NodeState 仍保留五个值 —— **点不了的具体原因不在画布上说,在详情面板里说**
                // (等级差几级、还差多少墨、缺哪一侧前置)。那是一次点击的距离,而画布要的是
                // 「能点 / 不能点 / 已经点了」这一眼的层次。
                default: // 未解锁(PoorInk / GatedPrereq / GatedLevel):纯灰底,不描边
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
        /// <summary>顶栏:标题左上、墨锭与返回键右上 —— **与图鉴 / 收藏 / 商城同一套排布**
        /// (2026-09-08 用户指出对不齐)。行距 21、childAlignment MiddleLeft、
        /// 标题 40 + TitleFont、副标题 23 TextDim、右侧 Spring → InkCounter 25 →
        /// 返回键 25/(130,63),全部照抄 <c>BestiaryView.BuildTopBar</c>,一个数都别自己改。
        ///
        /// 与那两页唯一的不同是**底下垫一道宣纸渐隐**:那两页顶栏下面是留白,这一页下面是
        /// 会滑动的画布,不垫的话节点滑到标题背后会糊成一团。</summary>
        private void BuildTopBar(Transform parent)
        {
            var bar = Ui.Panel(parent, "TopBar");
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -TopBarH), Vector2.zero);

            Fade(bar.transform, 0.90f, 0.45f, 1f);   // 上半段近乎实底
            Fade(bar.transform, 0.45f, 0f, 0.45f);   // 下半段淡出

            var top = Ui.Row(bar.transform, "Top", 21);
            Ui.Stretch((RectTransform)top.transform);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            var title = Ui.ThemedLabel(top.transform, Strings.T("perk.view.title"), 40,
                Theme.TextMain, Theme.TitleFont);
            title.raycastTarget = false;   // 顶栏三个标签都不吃射线,否则按在标题上拖不动画布

            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            var subtitle = Ui.ThemedLabel(top.transform,
                Strings.T("perk.view.subtitle", ("level", charLevel)), 23, Theme.TextDim);
            subtitle.raycastTarget = false;

            // 手势提示:画布能拖能缩这件事本身没有任何视觉线索,不说玩家不会试。
            // 摆在左簇尾巴上(而不是右侧),右边留给「墨锭 + 返回」那对固定组合 ——
            // 那对在每一页的位置都必须一样,中间插东西就破了。字号取图鉴筛选行提示的同款 19。
            var zoomHint = Ui.ThemedLabel(top.transform, Strings.T("perk.view.zoom_hint"),
                19, Theme.LockGray);
            zoomHint.raycastTarget = false;

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            // ⚠ 墨锭字号与返回键规格**与其余顶栏一致**,别为了给画布腾地方就在这里缩一档:
            // 图鉴 / 收藏 / 地图都是 InkCounter 25,图鉴 / 收藏的返回键都是 25 + (130, 63)。
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63));
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
        /// 而这块画布只有 1220 见方、缩放范围也窄,取中心足够。</summary>
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
