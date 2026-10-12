using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情弹窗。版式基线 = <c>docs/design/drafts/scenes/CharSheet.dc.html</c>
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
    /// 2026-10-11 Plan E2 改版(拍板稿 <c>drafts/traits/CardSheet</c> / <c>CardSheetBattle</c>):非部件那一支换成
    /// 「头(牌 + 等级条)+ 正左背右两面 + 两面条 + 右栏」,部件那一支不动。
    /// · 卡组 / 开箱 / 商城走铺满版:右栏是配方与生克;卡组页脚上挂升级钮;
    /// · 战斗长按走战斗版:不印成本,朝上那一面描环 +「朝上」签,未解锁的特性收成一行,右栏「此刻的场面」
    ///   ——**这一击对场上每只敌人各是多少倍**;奖励阶段没有活敌,退回静态生克(Q16)。
    /// 「显示哪几行、取哪几格」在 Core(CardSheetRules / FaceStats),部件在 <see cref="CardSheetParts"/>。
    ///
    /// ⚠ 与单位详情(<see cref="UnitSheet"/>)同一族:墨遮罩 + 宣纸圆角卡 + 右上角关闭。
    /// **默认只读**:「长按只看不出手」(<see cref="HoldToPreview"/> 松手不补发点击)是既有语义,
    /// 在战斗里给这张弹窗加钮就等于把长按变成出手的第二条路。唯一的例外是卡组页 ——
    /// 它传 footActions 在脚上挂「升级」钮,那一处本来就是拿来改配置的,
    /// 而且没有长按这条路径。</summary>
    public static class CharPreview
    {
        /// <summary>战斗侧的上下文。为 null 时(开箱 / 商城)右栏退回卡组页那种静态生克对照。</summary>
        public sealed class BattleContext
        {
            /// <summary>部件池(部件牌的「能凑出什么」按它判缺料)。</summary>
            public IReadOnlyList<string> Pool;
            /// <summary>本场能合出的字(已解锁卡池 + 拆出来的中间字);口径同拆合台。</summary>
            public IReadOnlyCollection<string> Craftable;
            /// <summary>场上的敌人(Plan E2 右栏「此刻的场面」)。没有活着的(奖励阶段)就退回静态生克(Q16)。
            /// 属性一律读 <see cref="EnemyState.ApparentElement"/>,见 <see cref="BuildField"/>。</summary>
            public IReadOnlyList<EnemyState> Foes;
            /// <summary>这张牌在字库里此刻朝上的面;没有(奖励阶段的字库、部件池、战利品候选)= null,不标「朝上」。</summary>
            public CardFace? UpFace;
            /// <summary>这条每场限次的特性本场是否已用尽(<see cref="BattleEngine.TraitSpentThisBattle"/>);null = 不查。</summary>
            public System.Func<TraitDef, bool> TraitSpent;
            /// <summary>这个部件本回合挂着的印记(<see cref="BattleEngine.PartMarksOf"/>,Plan E3);null / 空 = 不画那一行。</summary>
            public IReadOnlyList<PartMarkView> PartMarks;
            /// <summary>印记来源字的本场卡等级(印记效果按来源字等级印);PartMarks 非空时必给。</summary>
            public System.Func<string, int> CardLevelOf;
        }

        // ---- 稿上的骨架尺寸(pt → 逻辑单位,1pt = 2.093) ----
        // 宽是**上限**(基准机的稿宽),实际宽 = min(SheetW, SafeArea.FrameWidth()),见 Show。
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
        private const float FootH = 31f;        // 15pt

        /// <param name="meta">养成外层存档。给了就画「等级(含升级成本)」或「怎么获得」那一段
        /// —— 战斗里传 null:局内不能升级、也不谈获取,那一处的等级靠头上的角标交代。</param>
        /// <param name="footActions">脚上的操作钮。只有卡组页传(升级),
        /// 其余入口留 null 保持**纯只读** —— 「长按只看不出手」是既有语义,
        /// 在战斗里给这张弹窗加钮就等于把长按变成出手的第二条路。
        /// 收 <c>System.Action&lt;Transform&gt;</c> 而不是一串按钮参数:哪个钮能点、点了做什么,
        /// 判据全在卡组页那边(份数、墨锭),搬进来只会让这里跟着长出一套规则。</param>
        public static GameObject Show(Transform root, CharDef def, RecipeGraph graph, int cardLevel = 1,
            BattleContext battle = null, MetaState meta = null, System.Action<Transform> footActions = null)
        {
            // 战斗里保持稿上那张矮卡:长按的那张牌得留在浮层下面看得见(「我按的是哪张」
            // 与「这张字什么用」要能对上)。其余三处底下没有这层关系,铺满。
            bool tall = battle == null;
            // 宽按屏比夹(2026-10-04 屏比验算):CanvasScaler 按高匹配,画布宽随屏比变。
            //   基准机 932×430pt:.safe 框 ≈ 1705 → 1591 原样,两侧各留 ~57;
            //   16:9 无刘海机:画布 1600、.safe 框 1354 → 1591 比框宽 237、两侧各探进安全区 ~118,
            //     几乎贴屏边 —— 收到 1354,刚好落在稿上的 .safe 框里(同 BattleView 换字面板
            //     min(1633, FrameWidth()) 的口径,不另留边距:.safe 那 123 本身就是边距)。
            // 部件那一支内部按 contentW 反算(两档宽都算过):contentW = 1591/1354 − 3 − 48 = 1540/1303,
            //   两栏 0.46/0.54 = 696·817 / 587·689;名字行最挤的是部件(≈690 ≤ infoW 1138)。
            // 字卡(Plan E2 改版)的逐行账写在 BuildSheet。
            float sheetW = Mathf.Min(SheetW, SafeArea.FrameWidth());
            var overlay = Ui.Sheet(root, "CharSheet", sheetW, tall ? TallSheetH : SheetH,
                dismissable: true, replaceSameName: true, Theme.Scrim,
                tall ? TallSheetLift : SheetLift, out var content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;

            if (!def.IsComponent)
            {
                // Plan E2 改版(稿 CardSheet / CardSheetBattle):头 + 两面 + 两面条 + 右栏 + 脚
                BuildSheet(content, layout, overlay, def, graph, cardLevel, battle, meta, footActions, sheetW);
                return overlay;
            }

            // ---- 部件:Plan E2 不动这一支(「单独出手 + 能凑出什么」)----
            float contentW = sheetW - 2f * Ui.SheetBorder - 2f * Ui.SheetPad;  // 扣描边与内边距
            BuildHeader(content, def, battle, overlay, contentW);

            float bodyW = contentW - ColGap;
            // 部件那两栏是**填满**式的:左栏效果整句、右栏「能凑出什么」各自带内滚动
            // (见 DescPanel / BuildCraftColumn),内容再多也不会把这一层撑高,
            // 所以照旧直接铺在卡里、吃掉剩下的高度。
            var body = BodyRow(content, fill: true);
            BuildSoloColumn(body.transform, def, graph, cardLevel, battle, bodyW * 0.46f);
            BuildCraftColumn(body.transform, def, graph, battle, bodyW * 0.54f);

            var foot = Ui.ThemedLabel(content, battle == null ? Strings.T("charsheet.foot.outside")
                : Strings.T("charsheet.foot.part"), 19, Theme.LockGray);
            foot.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(foot.gameObject, flexWidth: 1, height: FootH);
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

        // ================= 头 =================

        /// <summary>部件的头(Plan E2 之前的版式,部件那一支不动):牌 / 名 / 释义 /「部件没有等级」。</summary>
        private static void BuildHeader(Transform parent, CharDef def, BattleContext battle,
            GameObject overlay, float contentW)
        {
            var header = Ui.Row(parent, "Header", 23);
            header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(header, flexWidth: 1, height: HeaderH);

            // 部件没有等级也没有稀有度,角标一个都不挂 —— 挂了就是在说一件字表里没有的事
            Ui.GlyphTile(header.transform, def, false, null, TileSize);

            var info = Ui.VStack(header.transform, "Info", 11);
            info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(info, flexWidth: 1, flexHeight: 1);
            float infoW = contentW - TileSize.x - 23;

            NameRow(info.transform, def, battle, overlay, 46, 40, 22);

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
            Ui.ThemedLabel(lvRow.transform, Strings.T("charsheet.lv.part"), 19, Theme.LockGray);
            Ui.ThemedLabel(lvRow.transform, Strings.T("charsheet.lv.part_note"), 19, Theme.LockGray);
        }

        /// <summary>名字行:名 + 拼音 + chip(稀有度 / 属性 / AP / 战斗里的来源)+ 右上角关闭。两版与部件共用。</summary>
        private static void NameRow(Transform parent, CharDef def, BattleContext battle, GameObject overlay,
            float height, int nameSize, int pinyinSize)
        {
            var nameRow = Ui.Row(parent, "Name", 15);
            nameRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(nameRow, flexWidth: 1, height: height);
            Ui.ThemedLabel(nameRow.transform, def.Id, nameSize, Theme.TextMain, Theme.TitleFont);
            if (!string.IsNullOrEmpty(def.Pinyin))
                Ui.ThemedLabel(nameRow.transform, def.Pinyin, pinyinSize, Theme.TextDim);
            foreach (var chip in HeaderChips(def, battle))
                Ui.Chip(nameRow.transform, chip.text, chip.bg, chip.fg, 20);
            var spring = Ui.Panel(nameRow.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.RoundButton(nameRow.transform, Strings.T("common.close"),
                () => Object.Destroy(overlay), Theme.PanelInset, Theme.TextDim, 22, new Vector2(46, 46), 23);
        }

        /// <summary>头部 chip。「双方向」那枚已删(Q18:55 张字全是两面字,不再有信息量)。</summary>
        private static List<(string text, Color bg, Color fg)> HeaderChips(CharDef def, BattleContext battle)
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

        // ================= 字卡(Plan E2 改版,稿 CardSheet / CardSheetBattle) =================
        //
        // 横向的账(两档宽按 Ui 的估宽口径算过,逐条见 task-3-report「两档宽验算」):
        //   contentW = sheetW − 描边 3 − 内边距 2×29 = 1530(基准机)/ 1293(16:9);
        //   两面区 = contentW − 右栏 306 − 21 = 1203 / 966;每面 (两面区 − 19) / 2 = 592 / 473;面内宽 550 / 431。
        //   · 头部等级行:16:9 下信息区 1140,Lv 大字 + 等级条 454 + 两格成本是临界 —— 成本格放不下时整体缩字号(CostBoxes);
        //   · 面头落点句按剩余宽截断(木生面已换短句 Q12,两档都不截);特性行头放不下先丢类别小字再截名(CardSheetParts.FitHead)。
        // 纵向:两面区(连同两面条,Q13)进 ScrollList,两面等高,行高按实际面宽算(Review Focus 1)。

        private const float SidePad = 29f;     // 稿 pad 左右 14pt
        private const float SideW = 306f;      // 右栏 146pt
        private const float BodyGap = 21f;     // 两面区与右栏 10pt
        private const float PairGap = 19f;     // 两面之间 9pt
        private const float SideTitleH = 25f;

        private static void BuildSheet(Transform content, VerticalLayoutGroup layout, GameObject overlay, CharDef def,
            RecipeGraph graph, int cardLevel, BattleContext battle, MetaState meta,
            System.Action<Transform> footActions, float sheetW)
        {
            bool tall = battle == null;
            var z = tall ? CardSheetParts.Sizes.Tall : CardSheetParts.Sizes.BattleSheet;
            // 稿 pad 11/14/10pt、gap 8pt(铺满);10/14/9pt、gap 7pt(战斗)
            layout.padding = tall ? new RectOffset(29, 29, 23, 21) : new RectOffset(29, 29, 21, 19);
            layout.spacing = tall ? 17f : 15f;
            float contentW = sheetW - 2f * Ui.SheetBorder - 2f * SidePad;

            // 未拥有(Q8):等级条全空、成本位换成「怎么获得」、特性行全部按未解锁画;数值格仍按 1 级本体
            bool owned = meta == null || meta.OwnedCards.Contains(def.Id);
            int level = Mathf.Max(1, cardLevel);
            int traitLevel = owned ? level : 0;

            BuildSheetHeader(content, def, battle, meta, overlay, contentW, owned, level, z);

            var body = Ui.Row(content, "Body", BodyGap);
            var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            bodyLayout.childForceExpandHeight = true;
            Ui.Sized(body, flexWidth: 1, flexHeight: 1);

            float facesW = contentW - SideW - BodyGap;
            var scroll = Ui.ScrollList(body.transform, "Faces", tall ? 15f : 12.6f, out var faces);
            Ui.Sized(scroll, facesW, flexHeight: 1);
            BuildFaces(faces, def, level, traitLevel, battle, facesW, z);

            var side = Ui.ScrollList(body.transform, "Side", tall ? 12.6f : 8.4f, out var sideContent);
            Ui.Sized(side, SideW, flexHeight: 1);
            var foes = LiveFoes(battle);
            if (foes.Count > 0) BuildField(sideContent, def, level, battle, foes);
            else BuildStaticSide(sideContent, def, graph);

            if (tall)
            {
                var foot = Ui.Row(content, "Foot", 21);
                foot.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Sized(foot, flexWidth: 1, height: 54);
                var hint = Ui.ThemedLabel(foot.transform, Strings.T("charsheet.foot.outside"), 19, Theme.LockGray);
                hint.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(hint.gameObject, flexWidth: 1);
                footActions?.Invoke(foot.transform);
            }
            else
            {
                var foot = Ui.ThemedLabel(content, Strings.T("charsheet.foot.battle"), 18, Theme.TextWarm);
                foot.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(foot.gameObject, flexWidth: 1, height: 27);
            }
        }

        /// <summary>场上活着的敌人;奖励阶段 / 非战斗为空表 → 右栏退回静态生克(Q16)。</summary>
        private static List<EnemyState> LiveFoes(BattleContext battle)
        {
            var list = new List<EnemyState>();
            if (battle?.Foes == null) return list;
            foreach (var foe in battle.Foes)
                if (foe != null && foe.Alive) list.Add(foe);
            return list;
        }

        // ---- 头 ----

        /// <summary>头:牌(不挂角标,C4;战斗版按朝上面)/ 名 + chip / 释义 / 等级行(Lv 大字 + 等级条 + 成本位)。</summary>
        private static void BuildSheetHeader(Transform parent, CharDef def, BattleContext battle, MetaState meta,
            GameObject overlay, float contentW, bool owned, int level, CardSheetParts.Sizes z)
        {
            bool tall = !z.Battle;
            var tileSize = tall ? new Vector2(130f, 163f) : new Vector2(100f, 126f);   // 62×78pt / 48×60pt
            float gap = tall ? 23f : 21f;
            var header = Ui.Row(parent, "Header", gap);
            header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(header, flexWidth: 1, height: tileSize.y);

            // 牌上的印:铺满版印「攻」,战斗版印此刻朝上的那一面(背面自带本系淡底);m 档(component/FaceSeal)
            Ui.GlyphTile(header.transform, def, false, null, tileSize, locked: !owned,
                face: battle?.UpFace ?? CardFace.Attack, sealSize: Ui.FaceSealM);

            var info = Ui.VStack(header.transform, "Info", 8);
            var infoLayout = info.GetComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.UpperLeft;
            infoLayout.childForceExpandWidth = true;
            Ui.Sized(info, flexWidth: 1, flexHeight: 1);
            float infoW = contentW - tileSize.x - gap;

            NameRow(info.transform, def, battle, overlay, tall ? 46 : 40, tall ? 40 : 38, tall ? 22 : 21);
            if (!string.IsNullOrEmpty(def.Gloss))
            {
                int glossSize = tall ? 21 : 20;
                var gloss = Ui.ThemedLabel(info.transform, def.Gloss, glossSize, Theme.TextWarm);
                gloss.alignment = TextAnchor.UpperLeft;
                gloss.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(gloss.gameObject, infoW, Ui.WrappedTextHeight(def.Gloss, glossSize, infoW));
            }
            var spring = Ui.Panel(info.transform, "Spring");
            Ui.Sized(spring, flexHeight: 1);

            var lvRow = Ui.Row(info.transform, "Level", tall ? 21f : 19f);
            lvRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Ui.Sized(lvRow, infoW, Mathf.Max(z.LadderHeight, tall ? 54f : 30f));

            if (!tall)
            {
                // 战斗版:「本场等级 Lv.N」+ 等级条,不印成本(局内不能升级)
                Ui.ThemedLabel(lvRow.transform, Strings.T("charsheet.lv.current", ("level", level)), 19, Theme.TextDim);
                CardSheetParts.LevelLadder(lvRow.transform, def, level, false, z);
                return;
            }

            float used = 0f;
            if (owned)
            {
                string big = Strings.T("charsheet.lv.big", ("level", level));
                string max = Strings.T("charsheet.lv.max", ("max", MetaRules.MaxCardLevel));
                var lvBig = Ui.Row(lvRow.transform, "LvBig", 4);
                lvBig.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
                var bigLabel = Ui.ThemedLabel(lvBig.transform, big, 36, Theme.TextMain, Theme.TitleFont);
                bigLabel.fontStyle = FontStyle.Bold;
                Ui.ThemedLabel(lvBig.transform, max, 21, Theme.LockGray);
                used = Ui.EstimateWidth(big, 36) + 4f + Ui.EstimateWidth(max, 21) + 21f;
            }
            bool canUp = owned && meta != null && MetaRules.CanUpgradeCard(meta, def.Id, def.Rarity);
            CardSheetParts.LevelLadder(lvRow.transform, def, owned ? level : 0, canUp, z);
            float rest = infoW - used - z.LadderWidth - 21f;

            if (!owned)
            {
                string text = Strings.T("collection.side.get.chest", ("hint", ChestHint(def.Rarity)));
                var get = Ui.ThemedLabel(lvRow.transform, text, 18, Theme.TextDim);
                get.alignment = TextAnchor.LowerLeft;
                get.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(get.gameObject, rest, Ui.WrappedTextHeight(text, 18, rest));
            }
            else if (meta != null && level < MetaRules.MaxCardLevel)
                CostBoxes(lvRow.transform, def, meta, level, rest - 21f);   // 成本靠右:前面垫的弹簧又占一道行距
        }

        /// <summary>这一档从哪只匣子起开得出。口径同 <see cref="ChestRules"/> 的稀有度权重表。</summary>
        private static string ChestHint(CardRarity rarity) => rarity switch
        {
            CardRarity.White => Strings.T("collection.chest_hint.common"),
            CardRarity.Green => Strings.T("collection.chest_hint.common"),
            CardRarity.Blue => Strings.T("collection.chest_hint.blue"),
            CardRarity.Purple => Strings.T("collection.chest_hint.purple"),
            CardRarity.Gold => Strings.T("collection.chest_hint.gold"),
            CardRarity.Orange => Strings.T("collection.chest_hint.orange"),
            _ => Strings.T("collection.chest_hint.red"),
        };

        /// <summary>两格成本(重复卡 / 墨锭):够 = ad-green-bg + upgrade-text,不够 = panel-inset + cinnabar-dark。
        /// 16:9 下等级行只余约 26(E2 附录 §4.1 临界),墨锭位数一多就放不下 —— 按可用宽 <paramref name="width"/>
        /// 整体等比缩字号(下限 16 / 14),等级条定宽不缩(component/LevelLadder)。</summary>
        private static void CostBoxes(Transform parent, CharDef def, MetaState meta, int level, float width)
        {
            meta.CardCopies.TryGetValue(def.Id, out int copies);
            int needed = MetaRules.CopiesRequired(level, def.Rarity);
            int ink = MetaRules.InkRequired(level, def.Rarity);
            var boxes = new[]
            {
                (Key: Strings.T("collection.side.cost.copies"), Value: $"{copies} / {needed}", Ok: copies >= needed),
                (Key: Strings.T("collection.side.cost.ink"), Value: $"{Ui.InkText(meta.Ink)} / {Ui.InkText(ink)}", Ok: meta.Ink >= ink),
            };
            const float pad = 19f, inner = 12.6f, between = 12.6f;
            float Natural(int keySize, int valueSize) =>
                boxes.Sum(b => 2f * pad + Ui.EstimateWidth(b.Key, keySize) + inner + Ui.EstimateWidth(b.Value, valueSize)) + between;
            int keyFont = 18, valueFont = 24;
            float natural = Natural(keyFont, valueFont);
            if (natural > width)
            {
                float k = width / natural;
                keyFont = Mathf.Max(14, Mathf.FloorToInt(18 * k));
                valueFont = Mathf.Max(16, Mathf.FloorToInt(24 * k));
            }

            var spring = Ui.Panel(parent, "Spring");
            Ui.Sized(spring, flexWidth: 1);
            var row = Ui.Row(parent, "Costs", between);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerRight;
            foreach (var b in boxes)
            {
                var box = Ui.CardPanel(row.transform, "Cost", b.Ok ? Theme.AdGreenBg : Theme.PanelInset, 14);
                Ui.Sized(box.gameObject, 2f * pad + Ui.EstimateWidth(b.Key, keyFont) + inner + Ui.EstimateWidth(b.Value, valueFont), 54);
                var line = Ui.Row(box.transform, "Line", inner);
                line.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                Ui.Stretch((RectTransform)line.transform);
                Ui.ThemedLabel(line.transform, b.Key, keyFont, Theme.TextDim);
                var value = Ui.ThemedLabel(line.transform, b.Value, valueFont, b.Ok ? Theme.UpgradeText : Theme.CinnabarDark);
                value.fontStyle = FontStyle.Bold;
            }
        }

        // ---- 两面 + 两面条 ----

        /// <summary>正左背右两面(等宽、等高)+ 底部「两面」条。战斗版:朝上那面描环 + 签;面里只列已解锁行,
        /// 未解锁的收成一行;每场限次且本场已用尽的行走 spent 态;两面条只列已解锁的、压成单行。</summary>
        private static void BuildFaces(Transform parent, CharDef def, int level, int traitLevel,
            BattleContext battle, float facesW, CardSheetParts.Sizes z)
        {
            float faceW = (facesW - PairGap) / 2f;
            // 正左背右:CardFace 的枚举值是 Feature = 0、Attack = 1,不能按枚举排序,这里写死顺序
            var models = new List<CardSheetParts.FaceModel>();
            foreach (var face in new[] { CardFace.Attack, CardFace.Feature })
                models.Add(FaceModel(def, face, level, traitLevel, battle));
            float pairH = 0f;
            foreach (var model in models) pairH = Mathf.Max(pairH, CardSheetParts.FaceHeight(model, faceW, z));

            var pair = Ui.Row(parent, "Pair", PairGap);
            pair.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(pair, facesW, pairH);
            foreach (var model in models)
                CardSheetParts.FacePanel(pair.transform, def, model, faceW, pairH, z);

            var rows = new List<CardSheetParts.RowModel>();
            foreach (var row in CardSheetRules.SharedTraits(def, traitLevel))
            {
                if (z.Battle && !row.Unlocked) continue;
                rows.Add(CardSheetParts.SharedRow(def, row, traitLevel,
                    row.Unlocked ? CardSheetParts.RowState.On : CardSheetParts.RowState.Off));
            }
            if (rows.Count == 0) return;
            float rowW = CardSheetParts.SharedRowWidth(facesW, rows.Count, z);
            CardSheetParts.SharedStrip(parent, def, rows, facesW, CardSheetParts.SharedHeight(rows, rowW, z), z);
        }

        private static CardSheetParts.FaceModel FaceModel(CharDef def, CardFace face, int level, int traitLevel,
            BattleContext battle)
        {
            var model = new CardSheetParts.FaceModel
            {
                Face = face,
                Role = CardSheetParts.RoleText(face),
                Landing = CardSheetParts.LandingText(def, face, level),
                Up = battle?.UpFace == face,
                Stats = FaceStats.Body(def, face, level),
            };
            var locked = new List<string>();
            foreach (var row in CardSheetRules.FaceTraits(def, face, traitLevel))
            {
                if (battle != null && !row.Unlocked)
                {
                    locked.Add(Strings.T("charsheet.locked_item", ("level", (int)row.Slot),
                        ("name", CardSheetParts.NameText(row))));
                    continue;
                }
                var state = !row.Unlocked ? CardSheetParts.RowState.Off
                    : battle?.TraitSpent != null && battle.TraitSpent(row.Defs[0]) ? CardSheetParts.RowState.Spent
                    : CardSheetParts.RowState.On;
                model.Rows.Add(CardSheetParts.FaceRow(def, row, traitLevel, state));
            }
            if (locked.Count > 0)
                model.LockedLine = Strings.T("charsheet.locked_line",
                    ("list", string.Join(Strings.T("charsheet.locked_sep"), locked)));
            return model;
        }

        // ---- 右栏 ----

        private static void SideTitle(Transform parent, string title)
        {
            var row = Ui.Row(parent, "SideTitle", 12.6f);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, SideW, SideTitleH);
            var label = Ui.ThemedLabel(row.transform, title, 21, Theme.TextDim, Theme.TitleFont);
            label.fontStyle = FontStyle.Bold;
            var rule = Ui.Panel(row.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, height: 2, flexWidth: 1);
        }

        /// <summary>卡组版右栏(及战斗里没有活敌时,Q16):配方紧凑版 + 生克两行 + 注。</summary>
        private static void BuildStaticSide(Transform parent, CharDef def, RecipeGraph graph)
        {
            if (!def.IsLeaf)
            {
                SideTitle(parent, Strings.T("collection.side.section.recipe"));
                var recipe = Ui.Row(parent, "Recipe", 10.5f);
                recipe.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Sized(recipe, SideW, 54);
                for (int i = 0; i < def.Recipe.Count; i++)
                {
                    string part = def.Recipe[i];
                    RecipePart(recipe.transform, part, graph.TryGet(part, out var partDef) ? partDef.Element : null, false);
                    Ui.ThemedLabel(recipe.transform,
                        i == def.Recipe.Count - 1 ? Strings.T("collection.side.recipe_to")
                                                  : Strings.T("collection.side.recipe_plus"),
                        21, Theme.TextWarm);
                }
                RecipePart(recipe.transform, def.Id, def.Element, true);
            }

            if (def.Element is not { } element || element == Element.Heart) return;
            SideTitle(parent, Strings.T("collection.side.section.wuxing"));
            if (WuxingResolver.Victim(element) is { } victim)
                WxRow(parent, victim, Strings.T("charsheet.wx.ke_who", ("element", CharInfo.ElementName(victim))),
                    WuxingResolver.KeMultiplier(element, victim), up: true);
            if (WuxingResolver.Counter(element) is { } counter)
                WxRow(parent, counter, Strings.T("charsheet.wx.bei_who", ("element", CharInfo.ElementName(counter))),
                    WuxingResolver.KeMultiplier(element, counter), up: false);
            Note(parent, Strings.T("charsheet.wx.note", ("element", CharInfo.ElementName(element))), 18);
        }

        private static void RecipePart(Transform parent, string id, Element? element, bool isOutput)
        {
            var go = isOutput
                ? Ui.OutlinedPanel(parent, "Part", Theme.ElementSoft(element), Theme.ElementColor(element), 12, 4).gameObject
                : Ui.CardPanel(parent, "Part", Theme.ElementSoft(element), 12).gameObject;
            Ui.Sized(go, 54, 54);
            var label = Ui.ThemedLabel(go.transform, id, 29, Theme.ElementSoftFg(element), Theme.TitleFont);
            label.fontStyle = FontStyle.Bold;
            Ui.Stretch(label.rectTransform);
        }

        /// <summary>生克一行(静态):克的那行 danger-bg 底、倍率 cinnabar-dark;被克的 panel-inset 底、倍率 text-dim。</summary>
        private static void WxRow(Transform parent, Element other, string who, float multiplier, bool up)
        {
            var row = SideRow(parent, "Wx", 50f, 15, 12.6f, up ? Theme.DangerBg : Theme.PanelInset);   // 稿 .wxr gap 6pt
            Dot(row, other, 31f, 19);
            var label = Ui.ThemedLabel(row, who, 19, Theme.TextMain);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, flexWidth: 1);
            var mul = Ui.ThemedLabel(row, Strings.T("charsheet.wx.mult", ("mult", multiplier.ToString("0.0"))), 24,
                up ? Theme.CinnabarDark : Theme.TextDim, Theme.TitleFont);
            mul.fontStyle = FontStyle.Bold;
        }

        /// <summary>「此刻的场面」(C1,稿 CardSheetBattle):这一击对场上每只活着的敌人各是多少倍,下面一句按朝上的面说。
        ///
        /// ⚠ 属性一律读 <see cref="EnemyState.ApparentElement"/>:伪装怪显示的是**假**属性、
        /// 生僻字受击两次前是 null(未现形)。读 Element 会让详情弹窗替玩家把底掀了,
        /// 而这条错误是静默的 —— 屏上只是多出一个「克」标签,没有任何测试会红。</summary>
        private static void BuildField(Transform parent, CharDef def, int level, BattleContext battle,
            IReadOnlyList<EnemyState> foes)
        {
            SideTitle(parent, Strings.T("charsheet.section.field"));
            var attacker = def.Element ?? Element.Heart;   // 中性字视作心:全 1.0x,与引擎同口径
            foreach (var foe in foes)
            {
                var apparent = foe.ApparentElement;
                float multiplier = apparent is { } shown ? WuxingResolver.KeMultiplier(attacker, shown) : 1f;
                bool unknown = apparent == null;
                bool ke = !unknown && multiplier > 1f, bei = !unknown && multiplier < 1f;
                var row = SideRow(parent, "Foe", 44f, 13, 10.5f, ke ? Theme.DangerBg : Theme.PanelInset);   // 战斗稿 gap 5pt
                Dot(row, apparent, 29f, 18);
                Color tone = ke ? Theme.CinnabarDark : Theme.TextDim;
                string mulText = unknown ? "" : Strings.T("charsheet.wx.mult", ("mult", multiplier.ToString("0.0")));
                string tag = unknown ? Strings.T("charsheet.wx.unknown")
                    : ke ? Strings.T("charsheet.wx.ke") : bei ? Strings.T("charsheet.wx.bei") : Strings.T("charsheet.wx.flat");
                float whoW = SideW - 2f * 13f - 29f - 3f * 10.5f - Ui.EstimateWidth(mulText, 23) - 50f;
                var who = Ui.ThemedLabel(row, Ui.Truncate(foe.Def.Id, 19, whoW), 19, Theme.TextMain);
                who.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(who.gameObject, flexWidth: 1);
                if (!unknown)
                {
                    var mul = Ui.ThemedLabel(row, mulText, 23, tone, Theme.TitleFont);
                    mul.fontStyle = FontStyle.Bold;
                }
                var tagLabel = Ui.ThemedLabel(row, tag, 17, ke || bei ? tone : Theme.TextWarm);
                tagLabel.alignment = TextAnchor.MiddleRight;
                Ui.Sized(tagLabel.gameObject, 50f, 44f);
            }

            // 底注按朝上的面说(没有朝上面时按攻击面):作用于我方的面不吃倍率;打敌人的面按上表结算
            var face = battle.UpFace ?? CardFace.Attack;
            string seal = Ui.FaceSealText(def, face);
            string note = CardSheetRules.TargetOf(def, face, level) != FaceTarget.Enemy
                ? Strings.T("charsheet.wx.note_ally", ("seal", seal))
                : face == CardFace.Attack ? Strings.T("charsheet.wx.note_attack")
                : Strings.T("charsheet.wx.note_feature", ("seal", seal),
                    ("what", def.Element == Element.Fire ? Strings.T("charsheet.wx.what_fire") : Strings.T("charsheet.wx.what")));
            Note(parent, note, 17);
        }

        private static Transform SideRow(Transform parent, string name, float height, int padX, float gap, Color bg)
        {
            var row = Ui.Row(parent, name, gap);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(padX, padX, 0, 0);
            var image = row.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = bg;
            Ui.Sized(row, SideW, height);
            return row.transform;
        }

        /// <summary>属性色点(本系字形色底 + 白字;未现形 = 「?」)。</summary>
        private static void Dot(Transform parent, Element? element, float size, int font)
        {
            var dot = Ui.CardPanel(parent, "Dot", Theme.GlyphColor(element), 8);
            Ui.Sized(dot.gameObject, size, size);
            var label = Ui.ThemedLabel(dot.transform,
                element is { } e ? CharInfo.ElementName(e) : Strings.T("char.element.unknown"),
                font, Color.white, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
        }

        private static void Note(Transform parent, string text, int size)
        {
            var note = Ui.ThemedLabel(parent, text, size, Theme.TextWarm);
            note.alignment = TextAnchor.UpperLeft;
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(note.gameObject, SideW, Ui.WrappedTextHeight(text, size, SideW));
        }

        // ================= 部件专属两栏 =================

        private static void BuildSoloColumn(Transform parent, CharDef def, RecipeGraph graph, int cardLevel,
            BattleContext battle, float width)
        {
            var col = Column(parent, "Solo", width);
            SectionTitle(col, Strings.T("charsheet.section.solo"));
            DescPanel(col, def, CharInfo.EffectsText(def, cardLevel), width);
            // 部件印记(PartsAndGraft 稿):「印记 · 双焰:本回合出手附带灼 2(还剩 1 次)」,一条印记一行,赭金字
            if (battle?.PartMarks != null)
                foreach (var mark in battle.PartMarks)
                {
                    if (!graph.TryGet(mark.SourceCharId, out var source)) continue;
                    int level = battle.CardLevelOf?.Invoke(source.Id) ?? 1;
                    string text = Strings.T("charsheet.part.mark", ("trait", mark.Trait.Name),
                        ("effects", CharInfo.SideEffectsText(mark.Trait.Effects, source, level)),
                        ("count", mark.Remaining));
                    var line = Ui.ThemedLabel(col, text, 20, Theme.GoldDeep);
                    line.alignment = TextAnchor.UpperLeft;
                    line.horizontalOverflow = HorizontalWrapMode.Wrap;
                    Ui.Sized(line.gameObject, flexWidth: 1, height: Ui.WrappedTextHeight(text, 20, width));
                }
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
