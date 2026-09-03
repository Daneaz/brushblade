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
    /// 五个入口共用:战斗里长按字库牌 / 部件池牌 / 战利品候选牌,以及开箱结果与商城的字卡。
    ///
    /// 2026-09-03 按稿重写。此前是「一张放大的牌 + <see cref="CharInfo.Detail"/> 那一整串文字
    /// + 知道了」——拼音、释义、稀有度、属性、配方、等级、效果全挤在一个文本块里。现在:
    /// · 头把身份信息摊开(牌 / 名 / 释义 / 稀有度 / 属性 / AP / 来源 / 本场等级);
    /// · 左栏「出手会怎样」:数值格 + 效果整句;双方向字换成攻、护两块并排;
    /// · 右栏「此刻的场面」:**这一击对场上每只敌人各是多少倍** —— 卡组页只能说「金克木」,
    ///   这一屏能直接说对谁有用,玩家点开详情多半就为这个;
    /// · 部件没有等级与稀有度,右栏换成「能凑出什么」,与拆合台的可合成列表同一套读法。
    ///
    /// ⚠ 与单位详情(<see cref="UnitSheet"/>)同一族:墨遮罩 + 宣纸圆角卡 + 右上角关闭,**只读**。
    /// 「长按只看不出手」(<see cref="HoldToPreview"/> 松手不补发点击)是既有语义,
    /// 所以卡里一个操作钮都没有 —— 加了就等于把长按变成出手的第二条路。</summary>
    public static class CharPreview
    {
        /// <summary>战斗侧的上下文。为 null 时(开箱 / 商城)右栏退回卡组页那种静态生克对照。</summary>
        public sealed class BattleContext
        {
            /// <summary>场上敌人。属性一律读 <see cref="EnemyState.ApparentElement"/> ——
            /// 伪装怪(通假字)与生僻字没现形之前那是 null 或**假**属性,详情弹窗不能替玩家掀底。</summary>
            public IReadOnlyList<EnemyState> Foes;
            /// <summary>部件池(部件牌的「能凑出什么」按它判缺料)。</summary>
            public IReadOnlyList<string> Pool;
            /// <summary>本场能合出的字(出阵表 + 拆出来的中间字);口径同拆合台。</summary>
            public IReadOnlyCollection<string> Craftable;
        }

        // ---- 稿上的骨架尺寸(pt → 逻辑单位,1pt = 2.093) ----
        private const float SheetW = 1591f;     // 760pt
        private const float SheetH = 670f;      // 320pt
        private const float SheetLift = 90f;    // 卡心比屏心高 43pt:底下要留出手牌行
        private const float HeaderH = 178f;     // 牌 85pt
        private static readonly Vector2 TileSize = new(142f, 178f);  // 68×85pt
        private const float ColGap = 27f;       // 两栏间 13pt
        private const float SectionTitleH = 27f;
        private const float StatBoxH = 96f;     // 46pt
        private const float WxRowH = 46f;       // 22pt
        private const float RecipeH = 63f;      // 30pt
        private const float FootH = 31f;        // 15pt
        private const float ContentW = SheetW - 2f * 1.5f - 2f * 24f;  // 扣描边与内边距

        public static GameObject Show(Transform root, CharDef def, RecipeGraph graph, int cardLevel = 1,
            BattleContext battle = null)
        {
            var overlay = Ui.Sheet(root, "CharSheet", SheetW, SheetH,
                dismissable: true, replaceSameName: true, Theme.Scrim, SheetLift, out var content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;

            bool dual = def.AttackEffects.Count > 0;
            BuildHeader(content, def, cardLevel, dual, battle, overlay);

            var body = Ui.Row(content, "Body", ColGap);
            body.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Sized(body, flexWidth: 1, flexHeight: 1);

            float bodyW = ContentW - ColGap;
            if (def.IsComponent)
            {
                BuildSoloColumn(body.transform, def, cardLevel, bodyW * 0.46f);
                BuildCraftColumn(body.transform, def, graph, battle, bodyW * 0.54f);
            }
            else
            {
                BuildEffectColumn(body.transform, def, cardLevel, dual, bodyW * 0.53f);
                BuildFieldColumn(body.transform, def, graph, battle, bodyW * 0.47f);
            }

            var foot = Ui.ThemedLabel(content, FootText(def, dual, battle), 19, Theme.LockGray);
            foot.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(foot.gameObject, flexWidth: 1, height: FootH);
            return overlay;
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

        private static void BuildEffectColumn(Transform parent, CharDef def, int cardLevel,
            bool dual, float width)
        {
            var col = Column(parent, "Effect", width);
            SectionTitle(col, dual
                ? Strings.T("charsheet.section.effect_dual")
                : Strings.T("charsheet.section.effect"));

            if (dual) BuildDualBlocks(col, def, cardLevel, width);
            else BuildStatBoxes(col, def, cardLevel);

            DescPanel(col, def, CharInfo.EffectsText(def, cardLevel), width);
        }

        /// <summary>数值格。前几格与卡组页同源(<see cref="CollectionStats"/>),后面补两格
        /// **战斗才用得上**的:溅射比例与穿透点数。
        ///
        /// ⚠ 这是与 StatMapping 那条「穿甲、偷袭这些没量级的信息留在功能行里」的**刻意分歧**:
        /// 那条口径是给卡组页定的,那里你在挑牌;这里你在**选目标**,溅多少、穿几点护甲
        /// 正是这一下要算的账。功能行照旧把两件事都写全,数值格只是把数字挑出来先看见。</summary>
        private static void BuildStatBoxes(Transform parent, CharDef def, int cardLevel)
        {
            var boxes = new List<(string label, string value, string note, Color color)>();
            foreach (var stat in CollectionStats.Of(def, cardLevel))
                boxes.Add((stat.Label, stat.Value.ToString(), stat.Note, stat.Color));

            foreach (var effect in def.Effects)
            {
                if (boxes.Count >= 3) break;
                if (effect.Kind != EffectKind.DamageSingle && effect.Kind != EffectKind.DamageAll) continue;
                if (effect.Shape != TargetShape.Single && effect.Shape != TargetShape.Volley
                    && effect.ShapePercent > 0 && effect.ShapePercent != 100)
                    boxes.Add((Strings.T("charsheet.stat.splash"), $"{effect.ShapePercent}%",
                        Strings.T("charsheet.stat.splash_note"), Theme.InkSoft));
                if (boxes.Count < 3 && effect.Pierce > 0)
                    boxes.Add((Strings.T("charsheet.stat.pierce"), effect.Pierce.ToString(),
                        Strings.T("charsheet.stat.pierce_note"), Theme.GlyphColor(Element.Metal)));
            }
            if (boxes.Count == 0) return;

            var row = Ui.Row(parent, "Stats", 15);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, flexWidth: 1, height: StatBoxH);
            foreach (var box in boxes)
            {
                var panel = Ui.OutlinedPanel(row.transform, "Stat", Theme.CardWhite, Theme.PanelBorder, 14, 2);
                Ui.Sized(panel.gameObject, flexWidth: 1, height: StatBoxH);
                var stack = Ui.VStack(panel.transform, "Stack", 2);
                Ui.Stretch((RectTransform)stack.transform);
                Ui.ThemedLabel(stack.transform, box.label, 17, Theme.LockGray);
                Ui.ThemedLabel(stack.transform, box.value, 36, box.color, Theme.TitleFont);
                Ui.ThemedLabel(stack.transform, box.note, 16, Theme.LockGray);
            }
        }

        /// <summary>双方向字:攻与护是**同一张牌的两个用法**,不是两张牌。
        /// 各自带「怎么触发」的小标题 —— 玩家在这一屏要回答的就是「该拖过去还是点自己」。</summary>
        private static void BuildDualBlocks(Transform parent, CharDef def, int cardLevel, float width)
        {
            var row = Ui.Row(parent, "Dual", 15);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, flexWidth: 1, height: 150);
            DualBlock(row.transform, Strings.T("charsheet.dual.attack"),
                def.AttackEffects, def, cardLevel, Theme.GlyphColor(Element.Fire));
            DualBlock(row.transform, Strings.T("charsheet.dual.support"),
                def.Effects, def, cardLevel, Theme.GlyphColor(Element.Earth));
        }

        private static void DualBlock(Transform parent, string title, IReadOnlyList<EffectDef> effects,
            CharDef def, int cardLevel, Color accent)
        {
            var panel = Ui.OutlinedPanel(parent, "Side", Theme.CardWhite, Theme.PanelBorder, 14, 2);
            Ui.Sized(panel.gameObject, flexWidth: 1, height: 150);
            var edge = Ui.Panel(panel.transform, "Edge");
            edge.AddComponent<Image>().color = accent;
            Ui.Anchor((RectTransform)edge.transform, Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(6, 0));

            var stack = Ui.VStack(panel.transform, "Stack", 4);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(19, 13, 11, 11);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, title, 17, accent);

            // 头一个有量级的效果放大字;其余归到下面那行小字里(与数值格同一条取法)
            int headline = 0;
            foreach (var effect in effects)
                if (effect.Value > 0) { headline = MetaRules.ScaleByCardLevel(effect.Value, cardLevel); break; }
            if (headline > 0)
                Ui.ThemedLabel(stack.transform, headline.ToString(), 36, accent, Theme.TitleFont);
            var sub = Ui.ThemedLabel(stack.transform,
                CharInfo.SideEffectsText(effects, def, cardLevel), 17, Theme.TextDim);
            sub.alignment = TextAnchor.UpperLeft;
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(sub.gameObject, flexWidth: 1, flexHeight: 1);
        }

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

        // ================= 右栏 =================

        private static void BuildFieldColumn(Transform parent, CharDef def, RecipeGraph graph,
            BattleContext battle, float width)
        {
            var col = Column(parent, "Field", width);
            SectionTitle(col, battle != null
                ? Strings.T("charsheet.section.field")
                : Strings.T("charsheet.section.wuxing"));

            if (battle != null) BuildFoeMatchups(col, def, battle);
            else BuildStaticWuxing(col, def);

            if (!def.IsLeaf)
            {
                SectionTitle(col, Strings.T("charsheet.section.recipe"));
                BuildRecipe(col, def, graph);
            }
        }

        /// <summary>这一击对场上每只敌人各是多少倍 —— 本屏独有的一件事。
        ///
        /// ⚠ 属性一律读 <see cref="EnemyState.ApparentElement"/>:伪装怪显示的是**假**属性、
        /// 生僻字受击两次前是 null(未现形)。读 Element 会让详情弹窗替玩家把底掀了,
        /// 而这条错误是静默的 —— 屏上只是多出一个「克制」标签,没有任何测试会红。</summary>
        private static void BuildFoeMatchups(Transform parent, CharDef def, BattleContext battle)
        {
            var scroll = Ui.ScrollList(parent, "Foes", 8, out var content);
            Ui.Sized(scroll, flexWidth: 1, flexHeight: 1);

            var attacker = def.Element ?? Element.Heart;   // 中性字视作心:全 1.0x,与引擎同口径
            int drawn = 0;
            foreach (var foe in battle.Foes)
            {
                if (!foe.Alive) continue;
                drawn++;
                var apparent = foe.ApparentElement;
                float multiplier = apparent is { } shown ? WuxingResolver.KeMultiplier(attacker, shown) : 1f;
                bool unknown = apparent == null;

                var row = Ui.Row(content, $"Foe_{drawn}", 13);
                var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.padding = new RectOffset(13, 13, 0, 0);
                var rowImage = row.AddComponent<Image>();
                rowImage.sprite = Theme.Rounded(12);
                rowImage.type = Image.Type.Sliced;
                rowImage.color = !unknown && multiplier > 1f ? Theme.WarnBg : Theme.PanelInset;
                Ui.Sized(row, flexWidth: 1, height: WxRowH);

                var dot = Ui.CardPanel(row.transform, "Dot", Theme.ElementColor(apparent), 10);
                Ui.Sized(dot.gameObject, 34, 34);
                Ui.ThemedLabel(dot.transform,
                    apparent is { } e ? CharInfo.ElementName(e) : Strings.T("char.element.unknown"),
                    19, Color.white, Theme.TitleFont);

                var name = Ui.ThemedLabel(row.transform, foe.Def.Id, 19, Theme.TextMain);
                name.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(name.gameObject, flexWidth: 1);

                var (mulColor, tag) = unknown
                    ? (Theme.LockGray, Strings.T("charsheet.wx.unknown"))
                    : multiplier > 1f ? (Theme.CinnabarDark, Strings.T("charsheet.wx.up"))
                    : multiplier < 1f ? (Theme.LockGray, Strings.T("charsheet.wx.down"))
                    : (Theme.TextDim, Strings.T("charsheet.wx.flat"));
                if (!unknown)
                    Ui.ThemedLabel(row.transform, $"×{multiplier:0.#}", 25, mulColor, Theme.TitleFont);
                Ui.ThemedLabel(row.transform, tag, 17, mulColor);
            }

            if (drawn == 0)
                Ui.ThemedLabel(content, Strings.T("charsheet.wx.none"), 19, Theme.LockGray);
            // 护盾/治疗从来不吃倍率(WuxingResolver.ResolveEffect 的无目标重载是恒等函数)
            if (def.AttackEffects.Count > 0 || HasSupport(def))
            {
                var note = Ui.ThemedLabel(content, Strings.T("charsheet.wx.note_support"), 17, Theme.LockGray);
                note.alignment = TextAnchor.UpperLeft;
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(note.gameObject, flexWidth: 1, height: 34);
            }
        }

        private static bool HasSupport(CharDef def)
        {
            foreach (var effect in def.Effects)
                if (effect.Kind == EffectKind.Shield || effect.Kind == EffectKind.HealSelf
                    || effect.Kind == EffectKind.HealAll || effect.Kind == EffectKind.HealOverTime)
                    return true;
            return false;
        }

        /// <summary>不在战斗里(开箱 / 商城):没有场面可对,退回卡组页那种「克谁 / 被谁克」。</summary>
        private static void BuildStaticWuxing(Transform parent, CharDef def)
        {
            if (def.Element is not { } element || element == Element.Heart)
            {
                Ui.ThemedLabel(parent, Strings.T("char.element.neutral"), 19, Theme.LockGray);
                return;
            }
            var row = Ui.Row(parent, "Wuxing", 13);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, flexWidth: 1, height: 84);
            if (WuxingResolver.Victim(element) is { } victim)
                WuxingBox(row.transform, victim, Theme.WarnBg, Theme.WarnText,
                    Strings.T("collection.side.ke", ("element", CharInfo.ElementName(victim))));
            if (WuxingResolver.Counter(element) is { } counter)
                WuxingBox(row.transform, counter, Theme.PanelInset, Theme.TextDim,
                    Strings.T("collection.side.bei", ("element", CharInfo.ElementName(counter))));
        }

        private static void WuxingBox(Transform parent, Element element, Color bg, Color fg, string text)
        {
            var box = Ui.CardPanel(parent, "Wx", bg, 14);
            Ui.Sized(box.gameObject, flexWidth: 1, height: 84);
            var row = Ui.Row(box.transform, "Row", 13);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            Ui.Stretch((RectTransform)row.transform);
            var dot = Ui.CardPanel(row.transform, "Dot", Theme.ElementColor(element), 10);
            Ui.Sized(dot.gameObject, 36, 36);
            Ui.ThemedLabel(dot.transform, CharInfo.ElementName(element), 21, Color.white, Theme.TitleFont);
            var label = Ui.ThemedLabel(row.transform, text, 19, fg);
            label.alignment = TextAnchor.MiddleLeft;
            Ui.Sized(label.gameObject, flexWidth: 1, height: 84);
        }

        private static void BuildRecipe(Transform parent, CharDef def, RecipeGraph graph)
        {
            var row = Ui.Row(parent, "Recipe", 11);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, flexWidth: 1, height: RecipeH);
            for (int i = 0; i < def.Recipe.Count; i++)
            {
                string part = def.Recipe[i];
                RecipePart(row.transform, part,
                    graph.TryGet(part, out var partDef) ? partDef.Element : null, false);
                Ui.ThemedLabel(row.transform,
                    i == def.Recipe.Count - 1 ? Strings.T("collection.side.recipe_to")
                                              : Strings.T("collection.side.recipe_plus"),
                    21, Theme.LockGray);
            }
            RecipePart(row.transform, def.Id, def.Element, true);
        }

        private static void RecipePart(Transform parent, string id, Element? element, bool isOutput)
        {
            var go = isOutput
                ? Ui.OutlinedPanel(parent, "Part", Theme.ElementSoft(element), Theme.ElementColor(element), 13, 3).gameObject
                : Ui.CardPanel(parent, "Part", Theme.ElementSoft(element), 13).gameObject;
            Ui.Sized(go, RecipeH, RecipeH);
            var glyph = Ui.ThemedLabel(go.transform, id, 31, Theme.ElementSoftFg(element), Theme.TitleFont);
            Ui.Stretch(glyph.rectTransform);
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
