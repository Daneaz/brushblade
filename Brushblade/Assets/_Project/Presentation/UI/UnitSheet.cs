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
    /// 整体重建、不保留状态——与本文件之外 <c>BattleView.cs</c> 里 28 处 <c>Ui.Clear</c> +
    /// 重建的 <c>Draw*</c> 方法同一套写法(该文件 <c>Refresh()</c> 每次状态变化整屏重画,
    /// 没有任何一处做增量更新)。调用方(Task 5)要做的事只是:每次想要刷新数值时,
    /// 销毁上一次 <see cref="Show"/> 返回的 GameObject,再拿新的 <see cref="UnitDetail"/>
    /// 重新调用一次——不必学一套新的更新 API,和调用其余 Draw* 方法的心智负担一样。
    /// 选它而不是「返回句柄供外部逐字段 patch」,是因为后者要在 <c>UnitSheet</c> 里额外
    /// 保留一堆 Text/Image 引用只为支持部分刷新,复杂度换来的收益是「重建一次弹窗的开销」——
    /// 而这只是一个偶尔点开的详情层,不是每帧刷新的战场本体,省不下这个成本没有意义。</summary>
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

        /// <summary>建一张详情弹窗并挂到 <paramref name="root"/> 下,返回根节点供调用方
        /// 销毁(下一次刷新,或玩家点关闭时它自己已经销毁自己)。</summary>
        public static GameObject Show(Transform root, UnitDetail detail)
        {
            var overlay = new GameObject("UnitSheet", typeof(RectTransform), typeof(Image));
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
            face.gameObject.AddComponent<Button>().targetGraphic = face;

            var stack = Ui.VStack(face.transform, "Stack", RootSpacing);
            Ui.Stretch((RectTransform)stack.transform);
            var stackLayout = stack.GetComponent<VerticalLayoutGroup>();
            stackLayout.childAlignment = TextAnchor.UpperCenter;
            stackLayout.padding = new RectOffset((int)SheetPadding, (int)SheetPadding,
                (int)SheetPadding, (int)SheetPadding);

            BuildHeader(stack.transform, detail, overlay);
            BuildBody(stack.transform, detail);
            BuildFoot(stack.transform, overlay);

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
                Ui.ThemedLabel(info, detail.Flavor, 13, Theme.TextDim, align: TextAnchor.UpperLeft)
                    .rectTransform.sizeDelta = new Vector2(InfoWidth, 20);
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

        /// <summary>名字行:名 + 属性徽章(可能不画,见 <see cref="ShouldShowElementBadge"/>)+
        /// 标签 chip + 弹性空 + × 关闭钮。</summary>
        private static void BuildNameRow(Transform parent, UnitDetail detail, GameObject overlay)
        {
            var row = Ui.Row(parent, "NameRow", 8);
            Ui.Sized(row, width: InfoWidth);

            Ui.ThemedLabel(row.transform, detail.Name, 22, Theme.TextMain, Theme.TitleFont);

            if (ShouldShowElementBadge(detail))
            {
                string text = detail.Element is { } el ? CharInfo.ElementName(el) : Strings.T("char.element.unknown");
                Ui.Chip(row.transform, text, Theme.ElementColor(detail.Element), Color.white, 13, 8, 4);
            }

            if (detail.Tags != null)
                foreach (var tag in detail.Tags)
                    Ui.Chip(row.transform, tag, Theme.PaperDim, Theme.TextDim, 12);

            var spacer = Ui.Panel(row.transform, "Spacer");
            Ui.Sized(spacer, flexWidth: 1f);

            Ui.RoundButton(row.transform, "×", () => Object.Destroy(overlay),
                Theme.PaperDim, Theme.TextDim, 14, new Vector2(28, 28), 14);
        }

        /// <summary><see cref="UnitDetail.Element"/> 为 null 有两种含义(执笔人没有五行 /
        /// 生僻字未读懂),<see cref="UnitDetail.Wuxing"/> 在两种情形下都同为 null,没有
        /// 区分度(task-3-report.md 第 4 节已核实,不能靠 Wuxing 反推)。这里改用
        /// <see cref="UnitDetail.Tags"/> 里有没有「无五行」那条来分辨——那条 tag
        /// (<c>player.detail.tag_no_element</c>)是执笔人独有的、敌人/召唤物从不会带上的
        /// 内容,用它做信号不需要新增字段,也不需要按单位类型分支。</summary>
        private static bool ShouldShowElementBadge(UnitDetail detail)
        {
            if (detail.Element != null) return true;
            if (detail.Tags == null) return true;
            string noElementTag = Strings.T("player.detail.tag_no_element");
            foreach (var tag in detail.Tags)
                if (tag == noElementTag) return false;
            return true;
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
            if (detail.Figures == null) return;
            var row = Ui.Row(parent, "Figs", 12);
            Ui.Sized(row, width: InfoWidth);

            bool first = true;
            foreach (var (label, value, note) in detail.Figures)
            {
                if (!first)
                {
                    var divider = Ui.Panel(row.transform, "Divider");
                    var divImage = divider.AddComponent<Image>();
                    divImage.color = Theme.PanelBorder;
                    Ui.Sized(divider, width: 1.5f, height: 22f);
                }
                first = false;

                var item = Ui.VStack(row.transform, "Fig", 1);
                item.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                var line = Ui.Row(item.transform, "Line", 4);
                Ui.ThemedLabel(line.transform, label, 11, Theme.TextDim);
                Ui.ThemedLabel(line.transform, value, 15, Theme.TextMain, Theme.TitleFont);
                if (note != null)
                    Ui.ThemedLabel(item.transform, note, 10, Theme.TextDim, align: TextAnchor.UpperLeft);
            }
        }

        // ---------------------------------------------------------------- 主体(.body)

        private static void BuildBody(Transform parent, UnitDetail detail)
        {
            var body = Ui.Row(parent, "Body", ColumnGap);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, flexHeight: 1f);

            var left = Ui.VStack(body.transform, "StatusColumn", 8);
            left.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(left, flexWidth: 1.06f, flexHeight: 1f);
            BuildStatusColumn(left.transform, detail);

            var right = Ui.VStack(body.transform, "AbilityColumn", 8);
            right.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(right, flexWidth: 1f, flexHeight: 1f);
            BuildAbilityColumn(right.transform, detail);
        }

        /// <summary>左列「身上的状态 N」,用轮一新增的 <see cref="Ui.ScrollList"/>——仓库唯一
        /// 一处 ScrollRect,超过 4 条自然溢出滚动,不需要自己判断「够不够 4 条才挂滚动组件」:
        /// 内容少时 ScrollRect 只是不会动,不多花一行特判代码。</summary>
        private static void BuildStatusColumn(Transform parent, UnitDetail detail)
        {
            int count = detail.Statuses?.Count ?? 0;
            var title = Ui.Row(parent, "Title", 8);
            Ui.ThemedLabel(title.transform, Strings.T("unit.detail.statuses_title", ("count", count)),
                15, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(title.transform, Strings.T("unit.detail.statuses_hint"), 10, Theme.TextDim);

            var scroll = Ui.ScrollList(parent, "StatusScroll", 8, out var content);
            Ui.Sized(scroll, flexHeight: 1f);

            if (detail.Statuses != null)
                foreach (var status in detail.Statuses)
                    BuildStatusRow(content, status);
        }

        private static void BuildStatusRow(Transform parent, StatusEntry status)
        {
            var row = Ui.Row(parent, "Status", 8);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;

            // IconKey 可能是 null——那是「走文字 chip」,不是「跳过整条」(StatusText 的契约)。
            // Ui.Chip 本身已经处理了 iconKey == null 的分支(只画文字),这里不需要再判断一次。
            Ui.Chip(row.transform, status.ChipText, status.ChipColor, Color.white, 12, 6, 4, status.IconKey);

            var textCol = Ui.VStack(row.transform, "Text", 1);
            textCol.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(textCol, flexWidth: 1f);

            var nameLine = Ui.Row(textCol.transform, "Name", 6);
            Ui.ThemedLabel(nameLine.transform, status.Name, 13, Theme.TextMain, align: TextAnchor.UpperLeft);
            Ui.ThemedLabel(nameLine.transform, status.Duration, 10, Theme.TextDim, align: TextAnchor.UpperLeft);

            Ui.ThemedLabel(textCol.transform, status.Desc, 11, Theme.TextDim, align: TextAnchor.UpperLeft);
        }

        /// <summary>右列「特性 · 技能」:能力卡列表 + 生克行(执笔人没有,Wuxing 为 null 时
        /// 整行不画)。这一列不滚动——brief 只点名左列要滚,这里维持自然高度。</summary>
        private static void BuildAbilityColumn(Transform parent, UnitDetail detail)
        {
            Ui.ThemedLabel(parent, Strings.T("unit.detail.abilities_title"), 15, Theme.TextMain, Theme.TitleFont,
                TextAnchor.UpperLeft);

            if (detail.Abilities != null)
                foreach (var ability in detail.Abilities)
                    BuildAbilityCard(parent, ability);

            if (detail.Wuxing is { } wx)
                BuildWuxingRow(parent, detail.Element.Value, wx.beats, wx.beatenBy);
        }

        /// <summary>「养成技能 · 局外」那四条(每回合行动点/字库容量/起始生命上限/每关护盾)
        /// 在 <see cref="UnitDetail.Abilities"/> 里就是四条普通 <see cref="AbilityEntry"/>,
        /// 与「护盾」「自燃」「顶前排」等条目**同一种卡片渲染**,不额外画分组小标题——
        /// AbilityEntry 没有携带「这条属于哪个分组」的字段(brief 明确不让为此加字段),
        /// 而唯一能用来猜测「这是养成条目」的信号(IconKey == null)同时也是「无机制」
        /// 「无大招」等纯文字卡片的信号,拿来分组会把不相关的卡片也框进同一个标题下,
        /// 比不分组更容易读错。四条各自的 Name/Desc(如「每回合行动点」+「Lv.3 · AP 3」)
        /// 已经完整、独立成句,不靠共享标题也读得懂,所以选择不分组——不丢信息,只是
        /// 少了稿子上那一条「养成技能 · 局外」的视觉分隔线。</summary>
        private static void BuildAbilityCard(Transform parent, AbilityEntry ability)
        {
            Ui.OutlinedPanel(parent, "Ability", Theme.PanelPaper, Theme.PanelBorder, 8, 1f, out var face);
            var stack = Ui.VStack(face.transform, "Stack", 3);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(8, 8, 6, 6);

            var header = Ui.Row(stack.transform, "Header", 6);
            if (ability.IconKey != null)
                Ui.Chip(header.transform, "", ability.ChipColor, Color.white, 12, 6, 4, ability.IconKey);
            Ui.ThemedLabel(header.transform, ability.Name, 14, Theme.TextMain, Theme.TitleFont,
                TextAnchor.UpperLeft);

            if (ability.Desc != null)
                Ui.ThemedLabel(stack.transform, ability.Desc, 11, Theme.TextDim, align: TextAnchor.UpperLeft);
        }

        /// <summary>生克行:倍率现取 <see cref="WuxingResolver.KeMultiplier"/>,不写死 1.5——
        /// 那个方法自己的文档就说了「不写死」,规则唯一来源是 wuxing-reference.md。
        /// 「被 X 克」这一侧稿子没有画倍率(玩家自己吃亏的那一半,交给战斗结算的伤害数字
        /// 本身去体现,这里只提示关系),照抄。</summary>
        private static void BuildWuxingRow(Transform parent, Element self, Element beats, Element beatenBy)
        {
            var row = Ui.Row(parent, "Wuxing", 10);
            Ui.ThemedLabel(row.transform, Strings.T("unit.detail.wx_label"), 11, Theme.TextDim);

            float multiplier = WuxingResolver.KeMultiplier(self, beats);
            Ui.ThemedLabel(row.transform,
                Strings.T("unit.detail.wx_beats", ("element", CharInfo.ElementName(beats)),
                    ("mult", multiplier.ToString("0.0"))),
                12, Theme.Cinnabar);
            Ui.ThemedLabel(row.transform,
                Strings.T("unit.detail.wx_beaten_by", ("element", CharInfo.ElementName(beatenBy))),
                12, new Color(0.055f, 0.31f, 0.526f)); // 与 Theme.ElementSoftFg(Water) 同色,稿上「被克」用的水系蓝
        }

        // ---------------------------------------------------------------- 底部(.foot)

        private static void BuildFoot(Transform parent, GameObject overlay)
        {
            var foot = Ui.Row(parent, "Foot", 12);
            Ui.Sized(foot, width: SheetWidth - SheetPadding * 2);

            Ui.ThemedLabel(foot.transform, Strings.T("unit.detail.foot_hint"), 11, Theme.TextDim,
                align: TextAnchor.MiddleLeft).rectTransform.sizeDelta = new Vector2(SheetWidth * 0.6f, 20);
            var spacer = Ui.Panel(foot.transform, "Spacer");
            Ui.Sized(spacer, flexWidth: 1f);
            Ui.PillButton(foot.transform, Strings.T("common.ok"), () => Object.Destroy(overlay),
                Theme.InkSoft, Color.white, 18, new Vector2(120, 44));
        }
    }
}
