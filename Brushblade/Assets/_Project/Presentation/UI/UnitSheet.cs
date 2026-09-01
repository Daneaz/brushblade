using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>单位详情弹窗骨架(2026-08-31,单位详情轮二 Task 4)。三张权威稿
    /// <c>UnitFoe.dc.html</c>/<c>UnitAlly.dc.html</c>/<c>UnitMe.dc.html</c> 是同一张骨架,
    /// 内容差异全部靠 <see cref="UnitDetail"/> 的字段为 null 表达——本类**只读 UnitDetail**,
    /// 不认识 <c>EnemyState</c>/<c>SummonState</c>/<c>BattleEngine</c>,内部也不写任何
    /// 「如果是敌人/召唤物/执笔人」的分支。
    ///
    /// **刷新策略(稿上写死「数值随战斗实时刷新,不暂停」)**:<see cref="Show"/> 每次调用都
    /// 整体重建、不保留状态。
    /// ⚠ 2026-09-01 review 修:此前这里的论据是「BattleView.cs 里所有 Draw* 方法都整屏重画,
    /// 没有任何一处做增量更新」——这句不成立,反例就在同一个文件里:<c>BattleView.cs</c> 的
    /// <c>_playerActionBar</c>/<c>_enemyActionBars</c>/<c>_summonActionBarByCore</c> 几个缓存
    /// 字段 + <c>ActionBar()</c>/<c>FillActionBars()</c>/<c>SetActionBar()</c> 是专门为行动条
    /// 抽出来的**增量**刷新路径,正是因为「高频连续变化的数值不适合全量重建」。
    /// 真实理由是:详情是玩家偶尔点开、看完就关的浮层,不是每帧刷新的战场本体,重建一次的
    /// 开销可以接受;换成「返回句柄供外部逐字段 patch」需要在 <c>UnitSheet</c> 里额外保留一堆
    /// Text/Image 引用只为支持部分刷新,复杂度换来的收益对这一层不值当。
    /// 刷新频率跟随 <c>BattleView.Refresh</c>(事件驱动,不是每帧)。
    /// 调用方(Task 5)要做的事只是:每次想要刷新数值时,拿新的 <see cref="UnitDetail"/>
    /// 重新调用一次 <see cref="Show"/>——本方法内部会自己找到并销毁上一个同名实例,调用方
    /// 不需要先手动 Destroy。全量重建有一个代价:<see cref="Ui.ScrollList"/> 的 Content 带
    /// <c>ContentSizeFitter</c>,重建会把左列滚动条弹回顶部——玩家正翻到第 5 条以后会被
    /// 弹回顶端。<see cref="Show"/> 因此在重建前记下旧实例的滚动位置,重建后原样恢复。</summary>
    public static class UnitSheet
    {
        private const float SheetWidth = 1280f;
        private const float SheetHeight = 760f;
        private const float SheetPadding = 24f;
        private const float RootSpacing = 14f;
        private const float PortraitSize = 148f;
        private const float HeaderGap = 16f;
        private const float InfoWidth = SheetWidth - SheetPadding * 2 - PortraitSize - HeaderGap;
        private const float ColumnGap = 18f;
        // 2026-09-01 用户报「详情页排版混乱」后补的三个宽度。病根统一是一条:这一层原先
        // 大量依赖 flexWidth 决定列宽,而 flexWidth 只在**有富余**时才分配 —— 富余从哪来?
        // 从「这一件的 preferredWidth 比可用宽窄」来。可这些列的 preferredWidth 全部由
        // 里面的 Text 报,而 **Text.preferredWidth 报的是不换行时的整句宽度**,一段长说明
        // 就是好几百,列永远处在「不缺反超」的状态,flex 从来轮不上,布局退化成「谁的字长
        // 谁就宽」。所以三处一律改成算得出的定宽。
        private const float BodyWidth = SheetWidth - SheetPadding * 2;                       // 1232
        // 稿 .body 两列 flex 比 1.06 : 1,减掉列间距后按这个比例分。
        private const float StatusColumnWidth = (BodyWidth - ColumnGap) * 1.06f / 2.06f;     // ≈624
        private const float AbilityColumnWidth = BodyWidth - ColumnGap - StatusColumnWidth;  // ≈589
        private const string SheetName = "UnitSheet";

        /// <summary>建一张详情弹窗并挂到 <paramref name="root"/> 下。内部自动查找并销毁上一个
        /// 同名实例(保留其滚动位置),调用方不需要手动 Destroy——若调用方自己再 Destroy 一次,
        /// 会打破滚动位置保留机制,让下一次 Show 的 root.Find() 扑空。返回新建的根节点以供
        /// 玩家点关闭/确定钮时销毁。</summary>
        public static GameObject Show(Transform root, UnitDetail detail)
        {
            // 刷新即整体重建(见类注释),但左列 ScrollRect 不该跟着弹回顶部——重建前找一下
            // root 下是否已有一张旧的详情弹窗,记下它的滚动位置再销毁它,新面板建完后原样
            // 恢复。这样调用方不必自己保留/销毁上一个 GameObject,直接反复调 Show 即可。
            Vector2? savedScroll = null;
            var previous = root.Find(SheetName);
            if (previous != null)
            {
                var previousScroll = previous.GetComponentInChildren<ScrollRect>();
                if (previousScroll != null) savedScroll = previousScroll.normalizedPosition;
                Object.Destroy(previous.gameObject);
            }

            var overlay = new GameObject(SheetName, typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(root, false);
            var mask = overlay.GetComponent<Image>();
            mask.color = Theme.Scrim;
            Ui.Stretch((RectTransform)overlay.transform);
            var maskButton = overlay.AddComponent<Button>();
            maskButton.targetGraphic = mask;
            maskButton.onClick.AddListener(() => Object.Destroy(overlay));

            var outer = Ui.OutlinedPanel(overlay.transform, "Sheet", Theme.PanelPaper, Theme.PanelBorder,
                18, 1.5f, out var face);
            Ui.Anchor((RectTransform)outer.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-SheetWidth / 2f, -SheetHeight / 2f), new Vector2(SheetWidth / 2f, SheetHeight / 2f));
            // 点面板本体不该关闭(只有遮罩/×/知道了三处能关)——面板是 overlay 的子物体,
            // 天生会挡住遮罩按钮的射线,补一个空 Button 吃掉点击,不让它透到遮罩上。
            // ⚠ 2026-09-01 review 修:Button 必须挂在 outer 上、targetGraphic 指向 face——
            // Ui.OutlinedPanel 对 face 无条件设了 raycastTarget = false(Ui.cs:153),
            // raycastTarget = false 的 Graphic 根本不会注册进 GraphicRegistry,挂在 face 自己
            // 身上的 Button 永远吃不到点击,点击会直接穿透 face 命中 outer、再冒泡到遮罩的
            // 关闭按钮上,把整个弹窗关掉——正好是这段注释想避免的效果。仓库其余同款按钮
            // (Ui.cs:146 的文档约定、MapView.cs 页签、Ui.cs 另外两处)全部是这个挂法。
            outer.gameObject.AddComponent<Button>().targetGraphic = face;

            var stack = Ui.VStack(face.transform, "Stack", RootSpacing);
            Ui.Stretch((RectTransform)stack.transform);
            var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
            stackLayout.childAlignment = TextAnchor.UpperCenter;
            stackLayout.padding = new RectOffset((int)SheetPadding, (int)SheetPadding,
                (int)SheetPadding, (int)SheetPadding);

            BuildHeader(stack.transform, detail, overlay);
            BuildBody(stack.transform, detail);
            BuildFoot(stack.transform, overlay);

            if (savedScroll is { } scrollPos)
            {
                // ScrollRect.normalizedPosition 依赖 Content 的 ContentSizeFitter 已经量出高度——
                // 刚建完的这一帧布局还没跑过,这里先强制跑一遍再赋值,否则要么读到旧高度、
                // 要么被随后的布局重排盖掉。
                Canvas.ForceUpdateCanvases();
                var scrollRect = overlay.GetComponentInChildren<ScrollRect>();
                if (scrollRect != null) scrollRect.normalizedPosition = scrollPos;
            }

            return overlay;
        }

        // ---------------------------------------------------------------- 头部(.hdr)

        private static void BuildHeader(Transform parent, UnitDetail detail, GameObject overlay)
        {
            // 第四个 Ui.UnitBlock 调用点(它的文档注释就是为这里预留的):立绘方块在左、
            // 信息列在右,三类单位共用同一条形状,靠里面塞的内容各自不同。
            Ui.UnitBlock(parent, "Header", PortraitSize, InfoWidth, HeaderGap,
                out var portrait, out var info);
            info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            BuildPortrait(portrait, detail);

            BuildNameRow(info, detail, overlay);
            if (detail.Flavor != null)
            {
                // 定宽 + 换行(2026-09-01 修)。此前这里写的是 rectTransform.sizeDelta =
                // (InfoWidth, 20) —— **那一行是死的**:info 是 VerticalLayoutGroup,子物体的
                // RectTransform 由布局组接管,手写的 sizeDelta 下一次布局就被覆盖。实际生效的
                // 是 Text 自己报的 preferredWidth(整句不换行的宽度),一句长点的 flavor 就
                // 横着捅出 info 列、捅出弹窗右缘。
                var flavor = Ui.ThemedLabel(info, detail.Flavor, 13, Theme.TextDim, align: TextAnchor.UpperLeft);
                flavor.horizontalOverflow = HorizontalWrapMode.Wrap;
                flavor.verticalOverflow = VerticalWrapMode.Overflow;
                Ui.Sized(flavor.gameObject, width: InfoWidth);
            }
            BuildBars(info, detail);
            BuildFigures(info, detail);
        }

        /// <summary>立绘方块:有形象资产(仅敌人可能有)叠层画,否则退化成属性底色 + 单字,
        /// 并在底部补一条「立绘待补」——这条判据只看资产有没有,三类单位一视同仁,
        /// 不需要知道自己是敌人/召唤物/执笔人。</summary>
        private static void BuildPortrait(Transform mount, UnitDetail detail)
        {
            var bg = mount.gameObject.AddComponent<Image>();
            bg.sprite = Theme.Rounded(12);
            bg.type = Image.Type.Sliced;
            bg.color = Theme.ElementSoft(detail.Element);

            bool drewArt = false;
            if (detail.PortraitPrefix != null)
            {
                foreach (var layer in MobAssets.Layers)
                {
                    var sprite = MobAssets.Layer(detail.PortraitPrefix, layer);
                    if (sprite == null) continue;
                    drewArt = true;
                    var layerGo = Ui.Panel(mount, "Layer_" + layer);
                    var image = layerGo.AddComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    Ui.Stretch((RectTransform)layerGo.transform);
                }
            }

            if (!drewArt)
            {
                var glyph = Ui.ThemedLabel(mount, detail.FaceChar ?? "", 56,
                    Theme.GlyphColor(detail.Element), Theme.TitleFont);
                Ui.Stretch(glyph.rectTransform);

                var todo = Ui.Panel(mount, "Todo");
                var todoBg = todo.AddComponent<Image>();
                todoBg.color = new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.55f);
                Ui.Anchor((RectTransform)todo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    Vector2.zero, new Vector2(0f, 16f));
                var todoLabel = Ui.ThemedLabel(todo.transform, Strings.T("unit.detail.portrait_placeholder"),
                    9, Color.white);
                Ui.Stretch(todoLabel.rectTransform);
            }

            if (detail.Shield > 0)
            {
                string shieldText = detail.Shield.ToString();
                var badge = Ui.Chip(mount, shieldText, Theme.RarityColor(CardRarity.Gold),
                    Theme.GoldText, 11, 6, 4, "shield");
                // 手动摆位(mount 不是布局组的托管子物体),尺寸要照抄 Chip 内部的算法——
                // 图标(Icons.Size)+ 间隙(Icons.Gap)+ 文字宽,和 Chip 自己挂的 LayoutElement
                // 用的是同一条公式,否则命中区/可见区会和画出来的胶囊对不上。
                float badgeWidth = Ui.ChipWidth(shieldText, 11, 6) + Icons.Size + Icons.Gap;
                float badgeHeight = Ui.ChipHeight(11, 4);
                Ui.Anchor((RectTransform)badge.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(3f, -3f - badgeHeight), new Vector2(3f + badgeWidth, -3f));
            }
        }

        /// <summary>名字行:名 + 属性徽章(执笔人不画,见 <see cref="UnitDetail.ElementUnknown"/>
        /// 的字段注释)+ 标签 chip + 弹性空 + × 关闭钮。</summary>
        private static void BuildNameRow(Transform parent, UnitDetail detail, GameObject overlay)
        {
            var row = Ui.Row(parent, "NameRow", 8);
            Ui.Sized(row, width: InfoWidth);

            Ui.ThemedLabel(row.transform, detail.Name, 22, Theme.TextMain, Theme.TitleFont);

            // 2026-09-01 review 修:此前靠比对 Tags 里有没有「无五行」那条文案来分辨
            // Element == null 的两种含义,脆弱且与 UnitDetail.Element 的字段注释自相矛盾——
            // 现在直接读 ElementUnknown 这个显式信号,不用猜。
            if (detail.Element != null || detail.ElementUnknown)
            {
                string text = detail.Element is { } el ? CharInfo.ElementName(el) : Strings.T("char.element.unknown");
                Ui.Chip(row.transform, text, Theme.ElementColor(detail.Element), Color.white, 13, 8, 4);
            }

            if (detail.Tags != null)
                foreach (var tag in detail.Tags)
                    Ui.Chip(row.transform, tag, Theme.PaperDim, Theme.TextDim, 12);

            var spacer = Ui.Panel(row.transform, "Spacer");
            Ui.Sized(spacer, flexWidth: 1f);

            // minWidth 钉死(2026-09-01 修):名字 + 属性 + 若干 tag chip 加起来偶尔会超过
            // InfoWidth(生僻怪的 tag 最多),一超预算这一行就等比压缩,关闭钮会跟着缩成
            // 一个点不准的小方块 —— 而它恰恰是这张弹窗最不能缩的那一件。
            var close = Ui.RoundButton(row.transform, "×", () => Object.Destroy(overlay),
                Theme.PaperDim, Theme.TextDim, 14, new Vector2(28, 28), 14);
            var closeElement = close.GetComponent<LayoutElement>();
            if (closeElement != null) { closeElement.minWidth = 28f; closeElement.minHeight = 28f; }
        }

        /// <summary>血条(带 x/y 叠字)+ 盾条 + 行动条。盾条的填充比例借用 BattleView 里
        /// 敌人那一条的归一算法(满值 = 自身血量上限的 1/4)——UnitSheet 看不到「玩家/召唤物
        /// 盾条另有一套绝对刻度 200」这件事(那是 BattleView 的私有常量,而且按单位类型
        /// 二选一正是这里不许写的分支),统一用同一条公式换取「三类单位一套账」。</summary>
        private static void BuildBars(Transform parent, UnitDetail detail)
        {
            var bars = Ui.VStack(parent, "Bars", 3);
            Ui.Sized(bars, width: InfoWidth);
            bars.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            float hpFrac = detail.MaxHp > 0 ? Mathf.Clamp01(detail.Hp / (float)detail.MaxHp) : 0f;
            var hpBar = Ui.Bar(bars.transform, hpFrac, Theme.Cinnabar, new Vector2(InfoWidth, 12));
            var hpText = Ui.ThemedLabel(hpBar.transform, $"{detail.Hp} / {detail.MaxHp}", 10, Theme.TextMain);
            Ui.Anchor(hpText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-70f, -8f), new Vector2(-4f, 8f));
            hpText.alignment = TextAnchor.MiddleRight;

            float shieldFrac = detail.MaxHp > 0 ? Mathf.Min(1f, detail.Shield * 4f / detail.MaxHp) : 0f;
            Ui.Bar(bars.transform, shieldFrac, Theme.RarityColor(CardRarity.Gold), new Vector2(InfoWidth, 5));

            float actionFrac = Mathf.Clamp01(detail.ActionMeter / (float)TurnScheduler.Threshold);
            Ui.Bar(bars.transform, actionFrac, Theme.InkSoft, new Vector2(InfoWidth, 5));
        }

        /// <summary>四格数值(攻/甲/……,具体是哪四项由调用方通过 Figures 决定,本类不关心
        /// 标签内容)。每格「label 值」+ 可选小字 note,格间一条竖分隔线。</summary>
        private static void BuildFigures(Transform parent, UnitDetail detail)
        {
            if (detail.Figures == null || detail.Figures.Length == 0) return;
            const float figGap = 12f;
            const float dividerW = 1.5f;
            var row = Ui.Row(parent, "Figs", figGap);
            Ui.Sized(row, width: InfoWidth);

            // 每格等分定宽(2026-09-01 修):格里的 note 是算出来的小字(例:执笔人的
            // 「攻 = (基础+增益)×士气」那一串),长度不受控。不定宽的话每格按自己 note 的
            // 整句宽度排,四格宽窄不一;整行一超预算,HorizontalLayoutGroup 还会把四格连同
            // 中间的分隔线一起等比压回去,格子大小变成「谁的小字长谁就宽」。
            int count = detail.Figures.Length;
            float itemWidth = (InfoWidth
                - figGap * (count * 2 - 2)          // 格与分隔线之间的间距:2n−2 个
                - dividerW * (count - 1)) / count;

            bool first = true;
            foreach (var (label, value, note) in detail.Figures)
            {
                if (!first)
                {
                    var divider = Ui.Panel(row.transform, "Divider");
                    var divImage = divider.AddComponent<Image>();
                    divImage.color = Theme.PanelBorder;
                    Ui.Sized(divider, width: dividerW, height: 22f).minWidth = dividerW;
                }
                first = false;

                var item = Ui.VStack(row.transform, "Fig", 1);
                var itemLayout = item.GetComponent<VerticalLayoutGroup>();
                itemLayout.childAlignment = TextAnchor.UpperLeft;
                itemLayout.childForceExpandWidth = true;   // note 要按格宽换行,得先拿到格宽
                Ui.Sized(item, width: itemWidth).minWidth = itemWidth;
                var line = Ui.Row(item.transform, "Line", 4);
                line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                Ui.ThemedLabel(line.transform, label, 11, Theme.TextDim);
                Ui.ThemedLabel(line.transform, value, 15, Theme.TextMain, Theme.TitleFont);
                if (note != null)
                {
                    var noteLabel = Ui.ThemedLabel(item.transform, note, 10, Theme.TextDim,
                        align: TextAnchor.UpperLeft);
                    noteLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
                    noteLabel.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
        }

        // ---------------------------------------------------------------- 主体(.body)

        private static void BuildBody(Transform parent, UnitDetail detail)
        {
            // 三处定宽(2026-09-01 修,见 BodyWidth 的说明)。body 自己也要定宽:外面的
            // stack 是 Ui.VStack(childForceExpandWidth = false),不定宽的话 body 只有内容
            // 那么宽、还被 UpperCenter 居中 —— 头部(1232)和底部(1232)是通栏的,中间这段
            // 却按内容宽居中,三段左右边缘对不齐,这是「排版混乱」最显眼的一条。
            var body = Ui.Row(parent, "Body", ColumnGap);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, width: BodyWidth, flexHeight: 1f);

            var left = Ui.VStack(body.transform, "StatusColumn", 8);
            left.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(left, width: StatusColumnWidth, flexHeight: 1f);
            BuildStatusColumn(left.transform, detail);

            var right = Ui.VStack(body.transform, "AbilityColumn", 8);
            right.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(right, width: AbilityColumnWidth, flexHeight: 1f);
            BuildAbilityColumn(right.transform, detail);
        }

        /// <summary>左列「身上的状态 N」,用轮一新增的 <see cref="Ui.ScrollList"/>——仓库唯一
        /// 一处 ScrollRect,超过 4 条自然溢出滚动,不需要自己判断「够不够 4 条才挂滚动组件」:
        /// 内容少时 ScrollRect 只是不会动,不多花一行特判代码。</summary>
        private static void BuildStatusColumn(Transform parent, UnitDetail detail)
        {
            // 2026-09-01 用户拍板:标题就叫「状态 N」,后面那句「超过 4 条这一列滚动」删掉
            // —— 能不能滚是玩家一划就知道的事,写出来只占地方。
            int count = detail.Statuses?.Count ?? 0;
            Ui.ThemedLabel(parent, Strings.T("unit.detail.statuses_title", ("count", count)),
                15, Theme.TextMain, Theme.TitleFont, TextAnchor.UpperLeft);

            // ⚠ 宽度必须显式给(2026-09-01 修):Ui.ScrollList 返回的是一个只挂了 ScrollRect
            // 的 Panel —— 没有布局组、没有 Graphic,ScrollRect 也不实现 ILayoutElement,
            // 所以它报出的 preferredWidth 是 **0**。父列是 childForceExpandWidth = false 的
            // VStack,于是这块列表被排成 0 宽,Viewport 的 RectMask2D 把里面的状态条目
            // 整个裁没 —— 左列看上去是空的。
            var scroll = Ui.ScrollList(parent, "StatusScroll", 8, out var content);
            Ui.Sized(scroll, width: StatusColumnWidth, flexHeight: 1f);

            if (detail.Statuses != null)
                foreach (var status in detail.Statuses)
                    BuildStatusRow(content, status);
        }

        private const float StatusRowGap = 8f;
        private const int StatusChipFont = 12;
        private const int StatusChipPadX = 6;
        private const int StatusChipPadY = 4;

        private static void BuildStatusRow(Transform parent, StatusEntry status)
        {
            var row = Ui.Row(parent, "Status", StatusRowGap);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            // IconKey 可能是 null——那是「走文字 chip」,不是「跳过整条」(StatusText 的契约)。
            // Ui.Chip 本身已经处理了 iconKey == null 的分支(只画文字),这里不需要再判断一次。
            var chip = Ui.Chip(row.transform, status.ChipText, status.ChipColor, Color.white,
                StatusChipFont, StatusChipPadX, StatusChipPadY, status.IconKey);
            // chip 宽度按 Ui 自己那套纯函数算一遍(与 Ui.Chip 内部挂 LayoutElement 用的是同一
            // 条公式),好把剩余宽度算给右边的文字列 —— 见下面那段注释。
            float chipWidth = Ui.ChipWidth(status.ChipText, StatusChipFont, StatusChipPadX)
                + (status.IconKey != null
                    ? Icons.Size + (string.IsNullOrEmpty(status.ChipText) ? 0f : Icons.Gap)
                    : 0f);
            var chipElement = chip.GetComponent<LayoutElement>();
            chipElement.minWidth = chipWidth;                                  // 不许被压缩
            chipElement.minHeight = chipElement.preferredHeight;               // 也不许被拉长

            var textCol = Ui.VStack(row.transform, "Text", 1);
            var textColLayout = textCol.GetComponent<VerticalLayoutGroup>();
            textColLayout.childAlignment = TextAnchor.UpperLeft;
            // 显式开 childForceExpandWidth:说明文字要按这一列的实际宽度换行,不开的话
            // Text 拿不到宽度、算不出该在哪里断行——与 BattleView.cs 里 pickedInfoLayout 那处
            // 换行同一个套路。
            textColLayout.childForceExpandWidth = true;
            // 定宽而不是 flexWidth:1(2026-09-01 修,同 BodyWidth 的说明)。原先这一行让
            // 说明文字的整句宽度去和左边的 chip 争:整行超预算,HorizontalLayoutGroup 就把
            // **所有**子物体等比压回去,连状态图标 chip 都跟着缩水 —— 同一列里各行的 chip
            // 大小还不一样,取决于那一行的说明有多长。
            Ui.Sized(textCol, width: StatusColumnWidth - chipWidth - StatusRowGap);

            var nameLine = Ui.Row(textCol.transform, "Name", 6);
            // 左对齐(2026-09-01 修):Ui.Row 默认 MiddleCenter,而 textCol 开了
            // childForceExpandWidth —— 名字行被撑成整列宽再居中,于是「暴击 本场持久」
            // 飘在列中间,底下的说明却贴着左边,一条状态读起来像两条。
            nameLine.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.ThemedLabel(nameLine.transform, status.Name, 13, Theme.TextMain, align: TextAnchor.UpperLeft);
            Ui.ThemedLabel(nameLine.transform, status.Duration, 10, Theme.TextDim, align: TextAnchor.UpperLeft);

            // 说明文字长度不受控,左列虽在 ScrollList 的 Viewport 内(有 RectMask2D)会被裁掉、
            // 不至于糊到弹窗外,但裁掉同样会丢字——开换行才是真正的修法(2026-09-01 review)。
            var desc = Ui.ThemedLabel(textCol.transform, status.Desc, 11, Theme.TextDim, align: TextAnchor.UpperLeft);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>右列「特性 · 技能」:能力卡列表 + 生克行(执笔人没有,Wuxing 为 null 时
        /// 整行不画)。这一列不滚动——brief 只点名左列要滚,这里维持自然高度。
        ///
        /// 2026-09-01 review 修:执笔人的「养成技能 · 局外」四条(永久生效的局外加成)此前
        /// 与「护盾」这类随时会消失的实时资源混排在同一列表、同款卡片、无任何视觉区分,
        /// 丢了「这两类性质不同」这层信息。现在靠 <see cref="AbilityEntry.Section"/>(可空)
        /// 分组:Section 从上一条切到一个不同的非空取值时画一个小标题,连续同取值的条目
        /// 共享同一个标题,不逐条重画。</summary>
        private static void BuildAbilityColumn(Transform parent, UnitDetail detail)
        {
            Ui.ThemedLabel(parent, Strings.T("unit.detail.abilities_title"), 15, Theme.TextMain, Theme.TitleFont,
                TextAnchor.UpperLeft);

            if (detail.Abilities != null)
            {
                string currentSection = null;
                foreach (var ability in detail.Abilities)
                {
                    if (ability.Section != null && ability.Section != currentSection)
                        Ui.ThemedLabel(parent, ability.Section, 12, Theme.TextDim, Theme.TitleFont,
                            TextAnchor.UpperLeft);
                    currentSection = ability.Section;
                    BuildAbilityCard(parent, ability);
                }
            }

            if (detail.Wuxing is { } wx)
                BuildWuxingRow(parent, detail.Element.Value, wx.beats, wx.beatenBy);
        }

        /// <summary>一张能力/特性卡片:图标 chip(可能没有)+ 名 + 可选说明。</summary>
        private static void BuildAbilityCard(Transform parent, AbilityEntry ability)
        {
            var card = Ui.OutlinedPanel(parent, "Ability", Theme.PanelPaper, Theme.PanelBorder, 8, 1f, out var face);
            // 定宽(2026-09-01 修):右列是 childForceExpandWidth = false 的 VStack,卡片不
            // 定宽就各自按自己那段文字的整句宽度排 —— 一列卡片宽窄参差、右缘呈锯齿状,
            // 长的那几张还会直接捅出弹窗。定宽之后卡片右缘齐平,里面的说明按卡宽换行。
            Ui.Sized(card.gameObject, width: AbilityColumnWidth);
            var stack = Ui.VStack(face.transform, "Stack", 3);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(8, 8, 6, 6);
            // 显式开 childForceExpandWidth:说明文字要按这张卡片的实际宽度换行,不开的话
            // Text 拿不到宽度、算不出该在哪里断行——与 BattleView.cs 里 pickedInfoLayout 那处
            // 换行同一个套路(那里的注释详细解释了为什么需要这一行)。
            layout.childForceExpandWidth = true;

            var header = Ui.Row(stack.transform, "Header", 6);
            // 左对齐(2026-09-01 修):同状态名字行那条 —— Ui.Row 默认 MiddleCenter,
            // 而外面的 stack 开了 childForceExpandWidth,头行被撑满卡宽再把图标和名字
            // 一起挤到卡片正中,一列卡片的文字各自居中、左缘参差,读起来最乱。
            header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            if (ability.IconKey != null)
                Ui.Chip(header.transform, "", ability.ChipColor, Color.white, 12, 6, 4, ability.IconKey);
            Ui.ThemedLabel(header.transform, ability.Name, 14, Theme.TextMain, Theme.TitleFont,
                TextAnchor.UpperLeft);

            if (ability.Desc != null)
            {
                // 说明文字长度不受控(战斗数值随时可能拼出很长的 note),这一列从自身到 Face
                // 全程没有 Mask/RectMask2D——不开换行会不被裁剪地溢出到卡片外甚至弹窗外
                // (2026-09-01 review 抓到)。
                var desc = Ui.ThemedLabel(stack.transform, ability.Desc, 11, Theme.TextDim,
                    align: TextAnchor.UpperLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                desc.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        /// <summary>生克行:倍率现取 <see cref="WuxingResolver.KeMultiplier"/>,不写死 1.5——
        /// 那个方法自己的文档就说了「不写死」,规则唯一来源是 wuxing-reference.md。
        /// 「被 X 克」这一侧稿子没有画倍率(玩家自己吃亏的那一半,交给战斗结算的伤害数字
        /// 本身去体现,这里只提示关系),照抄。</summary>
        private static void BuildWuxingRow(Transform parent, Element self, Element beats, Element beatenBy)
        {
            // 两行(2026-09-01 用户拍板):一行「克 X ×1.5」,一行「被 Y 克,承伤 ×1.5」。
            // 原先挤在一行、且「被克」那半只说关系不给倍率 —— 玩家最想知道的恰恰是
            // 「被克我要多吃多少」,那个数是现成的,没有理由让他自己去推。
            // 两边的倍率各取各的方向:我打它走 KeMultiplier(self, beats),它打我走
            // KeMultiplier(beatenBy, self)。不写死 1.5,规则唯一来源是 wuxing-reference.md。
            var column = Ui.VStack(parent, "Wuxing", 2);
            column.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            Ui.ThemedLabel(column.transform,
                Strings.T("unit.detail.wx_beats", ("element", CharInfo.ElementName(beats)),
                    ("mult", WuxingResolver.KeMultiplier(self, beats).ToString("0.0"))),
                12, Theme.Cinnabar, align: TextAnchor.UpperLeft);
            Ui.ThemedLabel(column.transform,
                Strings.T("unit.detail.wx_beaten_by", ("element", CharInfo.ElementName(beatenBy)),
                    ("mult", WuxingResolver.KeMultiplier(beatenBy, self).ToString("0.0"))),
                12, new Color(0.055f, 0.31f, 0.526f), // 与 Theme.ElementSoftFg(Water) 同色,稿上「被克」用的水系蓝
                align: TextAnchor.UpperLeft);
        }

        // ---------------------------------------------------------------- 底部(.foot)

        private static void BuildFoot(Transform parent, GameObject overlay)
        {
            var foot = Ui.Row(parent, "Foot", 12);
            Ui.Sized(foot, width: SheetWidth - SheetPadding * 2);

            // 定宽走 LayoutElement,不写 rectTransform.sizeDelta(2026-09-01 修):foot 是
            // HorizontalLayoutGroup,子物体的 RectTransform 由布局组接管,手写的 sizeDelta
            // 下一次布局就被覆盖,那一行等于没写 —— 与 Flavor 那处同一个坑。
            var footHint = Ui.ThemedLabel(foot.transform, Strings.T("unit.detail.foot_hint"), 11,
                Theme.TextDim, align: TextAnchor.MiddleLeft);
            Ui.Sized(footHint.gameObject, width: SheetWidth * 0.6f, height: 20f);
            var spacer = Ui.Panel(foot.transform, "Spacer");
            Ui.Sized(spacer, flexWidth: 1f);
            Ui.PillButton(foot.transform, Strings.T("common.ok"), () => Object.Destroy(overlay),
                Theme.InkSoft, Color.white, 18, new Vector2(120, 44));
        }
    }
}
