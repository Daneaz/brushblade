using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情弹窗。版式基线 = <c>docs/design/ui/scenes/CharSheet.dc.html</c>
    /// (及同族的 <c>CharSheetDual</c> / <c>CharSheetPart</c>)。
    ///
    /// 六个入口共用:战斗里长按字库牌 / 部件池牌 / 战利品候选牌,开箱结果、商城的字卡,
    /// 以及卡组页点一张牌(2026-09-05 从右侧栏搬进来的,见下)。
    ///
    /// 2026-09-03 按稿重写。此前是「一张放大的牌 + <see cref="CharInfo.Detail"/> 那一整串文字
    /// + 知道了」——拼音、释义、稀有度、属性、配方、等级、效果全挤在一个文本块里。现在:
    /// · 头把身份信息摊开(牌 / 名 / 释义 / 稀有度 / 属性 / AP / 来源 / 本场等级);
    /// · 部件没有等级与稀有度,右栏换成「能凑出什么」,与拆合台的可合成列表同一套读法。
    ///
    /// 2026-09-05 用户拍板「三处详情都采用卡组字卡详情的排版」:非部件那一支的两栏内容
    /// 全部换成 <see cref="CharSheetSections"/> 的段落 —— 与卡组页**同一份实现**。
    /// 原先这里另有一套「数值格 + 效果整句」,而卡组页把同一件事拆成了「攻击模式」+
    /// 「特性 · 技能」两段结构化的读法,同一张字在两处读出来不一样。
    /// · 左栏是这张字本身:等级(或未拥有时的「怎么获得」)/ 召唤 / 数值 / 攻击模式;
    /// · 右栏是它与外界的关系:特性 · 技能 / 配方 / 生克,战斗里生克换成「此刻的场面」
    ///   ——**这一击对场上每只敌人各是多少倍**,卡组页只能说「金克木」,这一屏直接说对谁有用,
    ///   玩家在战斗里点开详情多半就为这个。
    ///
    /// ⚠ 与单位详情(<see cref="UnitSheet"/>)同一族:墨遮罩 + 宣纸圆角卡 + 右上角关闭。
    /// **默认只读**:「长按只看不出手」(<see cref="HoldToPreview"/> 松手不补发点击)是既有语义,
    /// 在战斗里给这张弹窗加钮就等于把长按变成出手的第二条路。唯一的例外是卡组页 ——
    /// 它传 footActions 在脚上挂「编入出阵 / 升级」两个钮,那一处本来就是拿来改配置的,
    /// 而且没有长按这条路径。</summary>
    public static class CharPreview
    {
        /// <summary>战斗侧的上下文。为 null 时(开箱 / 商城)右栏退回卡组页那种静态生克对照。</summary>
        public sealed class BattleContext
        {
            /// <summary>部件池(部件牌的「能凑出什么」按它判缺料)。</summary>
            public IReadOnlyList<string> Pool;
            /// <summary>本场能合出的字(出阵表 + 拆出来的中间字);口径同拆合台。</summary>
            public IReadOnlyCollection<string> Craftable;
        }

        // ---- 稿上的骨架尺寸(pt → 逻辑单位,1pt = 2.093) ----
        private const float SheetW = 1591f;     // 760pt
        private const float SheetH = 670f;      // 320pt
        private const float SheetLift = 90f;    // 卡心比屏心高 43pt:底下要留出手牌行

        // ---- 非战斗入口的「铺满」版(2026-09-05 用户拍板:「下方还有不少空间可以利用」)----
        //
        // 稿上那张 760×320pt 是按**战斗**画的:底下压着手牌行,卡不能长。而卡组 / 开箱 /
        // 商城这三处下面没有要留着看的东西,670 高摆在 900 的屏上,底下白扔掉 200 多 ——
        // 而这一屏偏偏是最不够用的那张(特性 · 技能一多就得滚)。
        //
        // 高度与上抬量都从边距**推**出来,不各写一个数:抬多少完全由「上下各留多少」决定,
        // 两个数分开写迟早对不上(改了高度忘了改 lift,卡就偏出屏幕)。
        private const float ScreenH = 900f;          // CanvasScaler.referenceResolution 的高,match = 1(按高)
        private const float TallTopMargin = 30f;
        private const float TallBottomMargin = 52f;  // ≥ SafeArea.BottomInset(44 = Home Indicator)
        private const float TallSheetH = ScreenH - TallTopMargin - TallBottomMargin;
        private const float TallSheetLift = (TallBottomMargin - TallTopMargin) / 2f;
        private const float HeaderH = 178f;     // 牌 85pt
        private static readonly Vector2 TileSize = new(142f, 178f);  // 68×85pt
        private const float ColGap = 27f;       // 两栏间 13pt
        private const float SectionTitleH = 27f;
        private const float WxRowH = 46f;       // 22pt
        private const float FootH = 31f;        // 15pt
        private const float ActionsH = 100f;    // 卡组入口的操作钮带(同右栏底原来那条 48pt)
        private const float ContentW = SheetW - 2f * 1.5f - 2f * 24f;  // 扣描边与内边距

        /// <param name="meta">养成外层存档。给了就画「等级(含升级成本)」或「怎么获得」那一段
        /// —— 战斗里传 null:局内不能升级、也不谈获取,那一处的等级靠头上的角标交代。</param>
        /// <param name="footActions">脚上的操作钮。只有卡组页传(编入出阵 / 升级),
        /// 其余入口留 null 保持**纯只读** —— 「长按只看不出手」是既有语义,
        /// 在战斗里给这张弹窗加钮就等于把长按变成出手的第二条路。
        /// 收 <c>System.Action&lt;Transform&gt;</c> 而不是一串按钮参数:哪个钮能点、点了做什么,
        /// 判据全在卡组页那边(出阵配额、份数、墨锭),搬进来只会让这里跟着长出一套规则。</param>
        public static GameObject Show(Transform root, CharDef def, RecipeGraph graph, int cardLevel = 1,
            BattleContext battle = null, MetaState meta = null, System.Action<Transform> footActions = null)
        {
            // 战斗里保持稿上那张矮卡:长按的那张牌得留在浮层下面看得见(「我按的是哪张」
            // 与「这张字什么用」要能对上)。其余三处底下没有这层关系,铺满。
            bool tall = battle == null;
            var overlay = Ui.Sheet(root, "CharSheet", SheetW, tall ? TallSheetH : SheetH,
                dismissable: true, replaceSameName: true, Theme.Scrim,
                tall ? TallSheetLift : SheetLift, out var content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;

            bool dual = def.AttackEffects.Count > 0;
            BuildHeader(content, def, cardLevel, dual, battle, overlay);

            float bodyW = ContentW - ColGap;
            if (def.IsComponent)
            {
                // 部件那两栏是**填满**式的:左栏效果整句、右栏「能凑出什么」各自带内滚动
                // (见 DescPanel / BuildCraftColumn),内容再多也不会把这一层撑高,
                // 所以照旧直接铺在卡里、吃掉剩下的高度。
                var body = BodyRow(content, fill: true);
                BuildSoloColumn(body.transform, def, cardLevel, bodyW * 0.46f);
                BuildCraftColumn(body.transform, def, graph, battle, bodyW * 0.54f);
            }
            else
            {
                // 2026-09-05:两栏塞进滚动视口。这一支的段落高度**由内容定**
                // (特性 · 技能一条一张小卡,带召唤物被动的字能摞到七八条),而卡是定高的
                // —— 直接铺在 VerticalLayoutGroup 里,超出的部分不会溢出、会按比例
                // **反压所有兄弟**:头那一行(牌 / 名 / 释义)跟着一起被压扁,
                // 字牌在卡组页尤其明显(那里脚上还多一条 100 高的操作钮带)。
                // 改成滚动之后这一块对外只报 0 高、吃剩余空间,头与钮带各自守住自己的高度。
                var scroll = Ui.ScrollList(content, "BodyScroll", 0, out var scrolled);
                Ui.Sized(scroll, flexWidth: 1, flexHeight: 1);
                // 两栏等宽:卡组页那套段落是竖排的一长条,摆进横版时按「先算清自己、
                // 再看场面」切开 —— 左栏是这张字本身(等级/召唤/数值/打谁),
                // 右栏是它与外界的关系(特性/配方/生克或此刻的场面)。
                var body = BodyRow(scrolled, fill: false);
                BuildSelfColumn(body.transform, def, cardLevel, meta, bodyW * 0.5f);
                BuildFieldColumn(body.transform, def, graph, cardLevel, bodyW * 0.5f);
            }

            if (footActions != null)
            {
                var actions = Ui.Row(content, "Actions", 17);
                actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                Ui.Sized(actions, flexWidth: 1, height: ActionsH);
                footActions(actions.transform);
            }
            else
            {
                var foot = Ui.ThemedLabel(content, FootText(def, dual, battle), 19, Theme.LockGray);
                foot.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(foot.gameObject, flexWidth: 1, height: FootH);
            }
            return overlay;
        }

        /// <param name="fill">true = 吃掉卡里剩下的高度(部件那一支);
        /// false = 高度由两栏内容撑,交给外面的滚动视口去裁。</param>
        private static GameObject BodyRow(Transform parent, bool fill)
        {
            var body = Ui.Row(parent, "Body", ColGap);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, flexWidth: 1, flexHeight: fill ? 1 : 0);
            return body;
        }

        private static string FootText(CharDef def, bool dual, BattleContext battle)
        {
            if (battle == null) return Strings.T("charsheet.foot.outside");
            if (def.IsComponent) return Strings.T("charsheet.foot.part");
            return dual ? Strings.T("charsheet.foot.dual") : Strings.T("charsheet.foot.normal");
        }

        // ================= 头 =================

        private static void BuildHeader(Transform parent, CharDef def, int cardLevel,
            bool dual, BattleContext battle, GameObject overlay)
        {
            var header = Ui.Row(parent, "Header", 23);
            header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(header, flexWidth: 1, height: HeaderH);

            var tile = Ui.GlyphTile(header.transform, def, false, null, TileSize);
            // 部件没有等级也没有稀有度,角标一个都不挂 —— 挂了就是在说一件字表里没有的事
            if (!def.IsComponent)
                CardBadges.Apply(tile.gameObject, TileSize, new CardBadges.Spec
                {
                    Rarity = def.Rarity,
                    Level = cardLevel,
                    Maxed = cardLevel >= MetaRules.MaxCardLevel,
                });

            var info = Ui.VStack(header.transform, "Info", 11);
            info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(info, flexWidth: 1, flexHeight: 1);
            float infoW = ContentW - TileSize.x - 23;

            var nameRow = Ui.Row(info.transform, "Name", 15);
            nameRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(nameRow, flexWidth: 1, height: 46);
            Ui.ThemedLabel(nameRow.transform, def.Id, 40, Theme.TextMain, Theme.TitleFont);
            if (!string.IsNullOrEmpty(def.Pinyin))
                Ui.ThemedLabel(nameRow.transform, def.Pinyin, 22, Theme.TextDim);
            foreach (var chip in HeaderChips(def, dual, battle))
                Ui.Chip(nameRow.transform, chip.text, chip.bg, chip.fg, 20);
            var spring = Ui.Panel(nameRow.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.RoundButton(nameRow.transform, Strings.T("common.close"),
                () => Object.Destroy(overlay), Theme.PanelInset, Theme.TextDim, 22, new Vector2(46, 46), 23);

            if (!string.IsNullOrEmpty(def.Gloss))
            {
                var gloss = Ui.ThemedLabel(info.transform, def.Gloss, 21, Theme.LockGray);
                gloss.alignment = TextAnchor.UpperLeft;
                gloss.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(gloss.gameObject, infoW, Ui.WrappedTextHeight(def.Gloss, 21, infoW));
            }

            var lvRow = Ui.Row(info.transform, "Level", 15);
            lvRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(lvRow, flexWidth: 1, height: 30);
            if (def.IsComponent)
            {
                Ui.ThemedLabel(lvRow.transform, Strings.T("charsheet.lv.part"), 19, Theme.LockGray);
                Ui.ThemedLabel(lvRow.transform, Strings.T("charsheet.lv.part_note"), 19, Theme.LockGray);
                return;
            }
            Ui.ThemedLabel(lvRow.transform,
                Strings.T("charsheet.lv.current", ("level", cardLevel)), 19, Theme.TextDim);
            // 缩放倍数照 MetaRules.ScaleByCardLevel 的定义写,别另算一份
            Ui.ThemedLabel(lvRow.transform,
                Strings.T("charsheet.lv.scale", ("mult", (1 + 0.1 * (cardLevel - 1)).ToString("0.#"))),
                19, Theme.LockGray);
            Ui.ThemedLabel(lvRow.transform,
                dual ? Strings.T("charsheet.lv.note_dual") : Strings.T("charsheet.lv.note"),
                19, Theme.LockGray);
        }

        private static List<(string text, Color bg, Color fg)> HeaderChips(CharDef def,
            bool dual, BattleContext battle)
        {
            var chips = new List<(string, Color, Color)>();
            if (def.IsComponent)
            {
                chips.Add((Strings.T("charsheet.chip.part"), Theme.PanelInset, Theme.LockGray));
            }
            else
            {
                chips.Add((CharInfo.RarityName(def.Rarity), Theme.RarityColor(def.Rarity), Color.white));
            }
            chips.Add((def.Element is { } element
                    ? Strings.T("collection.side.element_chip", ("element", CharInfo.ElementName(element)))
                    : Strings.T("char.element.neutral"),
                Theme.ElementSoft(def.Element), Theme.ElementSoftFg(def.Element)));
            chips.Add((Strings.T("charsheet.chip.ap", ("cost", def.ApCost)), Theme.PanelInset, Theme.InkSoft));
            if (dual)
                chips.Add((Strings.T("charsheet.chip.dual"), Theme.PanelInset, Theme.InkSoft));
            if (battle != null)
                chips.Add((def.IsComponent ? Strings.T("charsheet.src.pool") : Strings.T("charsheet.src.library"),
                    Theme.PanelInset, Theme.LockGray));
            return chips;
        }

        // ================= 左栏 =================


        /// <summary>效果整句(<see cref="CharInfo.EffectsText"/>),左边一条属性色粗边。
        /// 放进滚动容器:字表里最长的那几条(带召唤物被动的)在 232pt 宽下能到四五行。</summary>
        private static void DescPanel(Transform parent, CharDef def, string text, float width)
        {
            var panel = Ui.OutlinedPanel(parent, "Desc", Theme.CardWhite, Theme.PanelBorder, 14, 2);
            Ui.Sized(panel.gameObject, flexWidth: 1, flexHeight: 1);
            var edge = Ui.Panel(panel.transform, "Edge");
            edge.AddComponent<Image>().color = Theme.ElementColor(def.Element);
            Ui.Anchor((RectTransform)edge.transform, Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(6, 0));

            var scroll = Ui.ScrollList(panel.transform, "DescScroll", 0, out var content);
            Ui.Anchor((RectTransform)scroll.transform, Vector2.zero, Vector2.one,
                new Vector2(19, 11), new Vector2(-13, -11));
            float inner = width - 32;
            var label = Ui.ThemedLabel(content, text, 21, Theme.TextMain);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, flexWidth: 1, height: Ui.WrappedTextHeight(text, 21, inner));
        }

        // ================= 左栏:这张字本身 =================

        /// <summary>段落全部走 <see cref="CharSheetSections"/> —— 与卡组页**同一份实现**
        /// (2026-09-05 用户拍板统一)。此前这里另有一套「效果整句 + 数值格」,
        /// 而卡组页把同一件事拆成了「攻击模式」+「特性 · 技能」两段结构化的读法,
        /// 同一张字在两处读出来不一样;现在只有卡组页那一套。</summary>
        private static void BuildSelfColumn(Transform parent, CharDef def, int cardLevel,
            MetaState meta, float width)
        {
            var col = Column(parent, "Self", width);
            // meta 为 null(战斗里)时按「已拥有」走:局内手上这张字当然是有的,
            // 而「怎么获得」在战斗中间弹出来是答非所问。
            bool owned = meta == null || meta.OwnedCards.Contains(def.Id);
            if (meta != null)
            {
                if (owned) CharSheetSections.Level(col, def, meta);
                else CharSheetSections.HowToGet(col, def, width);
            }
            bool summon = CollectionStats.SummonCount(def) > 0;
            if (summon) CharSheetSections.Summon(col, def);
            CharSheetSections.Stats(col, def, cardLevel, owned, summon);
            CharSheetSections.Modes(col, def);
        }

        // ================= 右栏:它与外界的关系 =================

        /// <summary>特性 · 技能 → 配方 → 生克。
        ///
        /// 2026-09-05 用户拍板**移除「此刻的场面」那一段**(这一击对场上每只敌人各是多少倍)。
        /// 它此前在战斗里顶掉静态生克,于是同一张字在战斗内外读到的右栏是两样东西 ——
        /// 而这一轮统一详情的整个诉求就是「三处一致」。生克那一段本来也够用:
        /// 玩家要判的是「我这一击对不对属性」,而每只怪的属性就摆在它自己的头行上。</summary>
        private static void BuildFieldColumn(Transform parent, CharDef def, RecipeGraph graph,
            int cardLevel, float width)
        {
            var col = Column(parent, "Field", width);
            CharSheetSections.Traits(col, def, cardLevel, CollectionStats.SummonCount(def) > 0, width);
            if (!def.IsLeaf) CharSheetSections.Recipe(col, def, graph);
            if (def.Element is { } element && element != Element.Heart)
                CharSheetSections.Wuxing(col, element);
        }


        // ================= 部件专属两栏 =================

        private static void BuildSoloColumn(Transform parent, CharDef def, int cardLevel, float width)
        {
            var col = Column(parent, "Solo", width);
            SectionTitle(col, Strings.T("charsheet.section.solo"));
            DescPanel(col, def, CharInfo.EffectsText(def, cardLevel), width);
            SectionTitle(col, Strings.T("charsheet.section.howto"));
            string howto = Strings.T("charsheet.solo.howto");
            var note = Ui.ThemedLabel(col, howto, 20, Theme.TextDim);
            note.alignment = TextAnchor.UpperLeft;
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(note.gameObject, flexWidth: 1, height: Ui.WrappedTextHeight(howto, 20, width));
        }

        /// <summary>「能凑出什么」:一行一条「部件 ＋ 部件 → 字」,与拆合台的可合成列表同一套读法。
        /// 缺料的标出**缺哪一个** —— 部件详情要回答的就是「它现在有什么用」。</summary>
        private static void BuildCraftColumn(Transform parent, CharDef def, RecipeGraph graph,
            BattleContext battle, float width)
        {
            var col = Column(parent, "Craft", width);
            SectionTitle(col, Strings.T("charsheet.section.craft"));
            var scroll = Ui.ScrollList(col, "CraftRows", 10, out var content);
            Ui.Sized(scroll, flexWidth: 1, flexHeight: 1);

            int drawn = 0;
            if (battle?.Craftable != null)
                foreach (var id in battle.Craftable)
                {
                    if (!graph.TryGet(id, out var target)) continue;
                    if (!Uses(target, def.Id)) continue;
                    drawn++;
                    BuildCraftRow(content, def, target, graph, battle);
                }
            if (drawn == 0)
                Ui.ThemedLabel(content, Strings.T("charsheet.craft.empty"), 19, Theme.LockGray);
        }

        private static bool Uses(CharDef target, string partId)
        {
            foreach (var ingredient in target.Recipe)
                if (ingredient == partId) return true;
            return false;
        }

        private static void BuildCraftRow(Transform parent, CharDef part, CharDef target,
            RecipeGraph graph, BattleContext battle)
        {
            // 缺料按**部件池的实际存量**判:同一个部件要两份时,池里只有一份仍是缺
            var remaining = new List<string>(battle.Pool ?? new List<string>());
            var missing = new List<string>();
            foreach (var ingredient in target.Recipe)
                if (!remaining.Remove(ingredient)) missing.Add(ingredient);

            var row = Ui.Row(parent, $"Craft_{target.Id}", 10);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            var image = row.AddComponent<Image>();
            image.sprite = Theme.Rounded(14);
            image.type = Image.Type.Sliced;
            image.color = missing.Count == 0 ? Theme.AdGreenBg : Theme.PanelInset;
            Ui.Sized(row, flexWidth: 1, height: 63);

            for (int i = 0; i < target.Recipe.Count; i++)
            {
                string ingredient = target.Recipe[i];
                bool have = !missing.Contains(ingredient);
                var chip = Ui.CardPanel(row.transform, "Ing",
                    have ? Theme.ElementSoft(graph.TryGet(ingredient, out var d) ? d.Element : null) : Theme.LockedBg, 10);
                Ui.Sized(chip.gameObject, 44, 44);
                Ui.ThemedLabel(chip.transform, ingredient, 25,
                    have ? Theme.TextMain : Theme.LockGray, Theme.TitleFont);
                if (i < target.Recipe.Count - 1)
                    Ui.ThemedLabel(row.transform, Strings.T("collection.side.recipe_plus"), 19, Theme.LockGray);
            }
            Ui.ThemedLabel(row.transform, Strings.T("collection.side.recipe_to"), 19, Theme.LockGray);

            var outBox = Ui.OutlinedPanel(row.transform, "Out", Theme.CardWhite,
                Theme.RarityColor(target.Rarity), 11, 3);
            Ui.Sized(outBox.gameObject, 52, 52);
            Ui.ThemedLabel(outBox.transform, target.Id, 29, Theme.GlyphColor(target.Element), Theme.TitleFont);

            var spring = Ui.Panel(row.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.ThemedLabel(row.transform,
                missing.Count == 0 ? Strings.T("charsheet.craft.can")
                                   : Strings.T("charsheet.craft.missing", ("parts", string.Join("", missing))),
                18, missing.Count == 0 ? Theme.UpgradeText : Theme.LockGray);
        }

        // ================= 小件 =================

        private static Transform Column(Transform parent, string name, float width)
        {
            var col = Ui.VStack(parent, name, 12);
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            var element = col.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0;
            element.flexibleHeight = 1;
            return col.transform;
        }

        private static void SectionTitle(Transform parent, string title)
        {
            var row = Ui.Row(parent, "SectionTitle", 13);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, flexWidth: 1, height: SectionTitleH);
            Ui.ThemedLabel(row.transform, title, 19, Theme.LockGray);
            var rule = Ui.Panel(row.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, height: 2, flexWidth: 1);
        }
    }
}
