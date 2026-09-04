using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情的**段落**,一段一个方法,只有这一份。
    ///
    /// 2026-09-05 用户拍板:「字卡详情在卡组详情、开宝箱详情和战斗详情不一致,都采用卡组
    /// 字卡详情的排版」。此前卡组页(<see cref="CollectionView"/> 的右侧栏)与详情弹窗
    /// (<see cref="CharPreview"/>)各写了一份 —— 配方、生克、数值格三段**各有两个实现**,
    /// 一边改了另一边不动,于是同一张字在两处读出来不一样。段落搬到这里之后,
    /// 两处调的是同一个方法,分歧从「靠人记得同步」变成「编译期只有一条路」。
    ///
    /// 排版基线取卡组页那一套(用户指定):一段一个 <see cref="Section"/> 标题 + 一条横线。
    /// **身份段不在这里** —— 弹窗的头(<c>CharPreview.BuildHeader</c>)已经把牌 / 名 / 拼音 /
    /// 稀有度 / 属性 / 释义摊开了,而且比卡组页那版还多 AP 与来源两枚 chip,再画一遍是重复。
    ///
    /// 每个方法都收 <c>width</c>:换行高度要按**实际栏宽**算(卡组页的 485 与弹窗两栏的
    /// 约 750 差得远),写死一个值会让另一处的长文案要么截断要么留一片空白。
    /// 收 <c>meta</c> 的那两段(等级 / 怎么获得)在战斗里没有意义(局内不能升级、也不谈获取),
    /// 由调用方决定画不画,这里不做 null 分支 —— 让「战斗里该不该有这一段」留在调用点上,
    /// 比藏在段落内部好读。</summary>
    public static class CharSheetSections
    {
        private static readonly Vector2 SummonTile = new(52f, 65f);
        private const float StatBoxH = 78f;   // 去掉格内小注后矮了 26(见 Stats 的注释)

        /// <summary>一个小节:标题 + 一条横线,下面是一个竖排容器(返回值)。</summary>
        public static Transform Section(Transform parent, string title)
        {
            var head = Ui.Row(parent, "SectionHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            var rule = Ui.Panel(head.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, 0, 2, flexWidth: 1);

            var stack = Ui.VStack(parent, "Section", 11);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            // ⚠ 只给弹性宽,**不要**设 preferredHeight —— LayoutElement 的 layoutPriority 压过
            // 布局组自己算出来的高,写个 0 会把整节压没(而且是静默的:节点还在,高度是 0)
            Ui.Sized(stack, flexWidth: 1);
            return stack.transform;
        }

        /// <summary>召唤:**召几只就画几只**,不写「×2」也不写说明 —— 数出来的比读出来的快,
        /// 而它们的血与攻就在紧接着的「数值(召唤物)」里,再写一遍是同一件事说两遍。</summary>
        public static void Summon(Transform parent, CharDef def)
        {
            int count = CollectionStats.SummonCount(def);
            var section = Section(parent, Strings.T("collection.side.section.summon"));
            var box = Ui.CardPanel(section, "Summons", Theme.AdGreenBg, 14);
            Ui.Sized(box.gameObject, 0, 92, flexWidth: 1);
            var row = Ui.Row(box.transform, "Row", 15);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Stretch((RectTransform)row.transform);
            for (int i = 0; i < count; i++)
                Ui.MiniGlyphTile(row.transform, def, SummonTile);
        }

        /// <summary>未拥有:这张字**怎么才能拿到**。写的是真规则 —— 没收集过的字只出宝箱,
        /// 商城字摊按 ShopView 的池子只卖部件和你已有的字。</summary>
        public static void HowToGet(Transform parent, CharDef def, float width)
        {
            var section = Section(parent, Strings.T("collection.side.section.get"));
            GetRow(section, Theme.RarityColor(def.Rarity), Theme.TextDim, width,
                Strings.T("collection.side.get.chest", ("hint", ChestHint(def.Rarity))));
            GetRow(section, Theme.LockedBg, Theme.LockGray, width, Strings.T("collection.side.get.shop"));
        }

        private static void GetRow(Transform parent, Color iconColor, Color fg, float width, string text)
        {
            var row = Ui.Row(parent, "GetRow", 17);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            float textWidth = width - 54 - 17;
            float height = Mathf.Max(54, Ui.WrappedTextHeight(text, 19, textWidth));
            Ui.Sized(row, 0, height, flexWidth: 1);

            var icon = Ui.CardPanel(row.transform, "Icon", iconColor, 12);
            Ui.Sized(icon.gameObject, 54, 54);

            var label = Ui.ThemedLabel(row.transform, text, 19, fg);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Sized(label.gameObject, textWidth, height);
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

        /// <summary>等级 + 升级进度 + 两格成本。**要 meta**:份数与墨锭是养成外层的账,
        /// 战斗里读不到也没意义(局内不能升级),那一处靠头上的等级角标交代。</summary>
        public static void Level(Transform parent, CharDef def, MetaState meta)
        {
            int level = MetaRules.CardLevel(meta, def.Id);
            bool maxed = level >= MetaRules.MaxCardLevel;
            meta.CardCopies.TryGetValue(def.Id, out int copies);
            var section = Section(parent, Strings.T("collection.side.section.level"));

            var row = Ui.Row(section, "LevelRow", 15);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(row.transform, $"Lv.{level}", 40, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(row.transform, $"/ {MetaRules.MaxCardLevel}", 21, Theme.LockGray);

            var pips = Ui.Row(row.transform, "Pips", 4);
            pips.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(pips, 0, 11, flexWidth: 1);
            bool canUpgrade = MetaRules.CanUpgradeCard(meta, def.Id, def.Rarity);
            for (int i = 1; i <= MetaRules.MaxCardLevel; i++)
            {
                var pip = Ui.Panel(pips.transform, $"Pip{i}");
                var image = pip.AddComponent<Image>();
                image.sprite = Theme.Rounded(5);
                image.type = Image.Type.Sliced;
                image.color = i <= level ? Theme.InkSoft
                    : (i == level + 1 && canUpgrade ? Theme.Jade : Theme.PaperDim);
                Ui.Sized(pip, 0, 11, flexWidth: 1);
            }

            if (maxed) return;
            int needed = MetaRules.CopiesRequired(level, def.Rarity);
            int ink = MetaRules.InkRequired(level, def.Rarity);
            var costs = Ui.Row(section, "Costs", 13);
            costs.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(costs, 0, 75, flexWidth: 1);
            CostBox(costs.transform, Strings.T("collection.side.cost.copies"),
                $"{copies} / {needed}", copies >= needed);
            CostBox(costs.transform, Strings.T("collection.side.cost.ink"),
                $"{meta.Ink} / {ink}", meta.Ink >= ink);
        }

        private static void CostBox(Transform parent, string key, string value, bool ok)
        {
            var box = Ui.CardPanel(parent, "Cost", ok ? Theme.AdGreenBg : Theme.PanelInset, 14);
            Ui.Sized(box.gameObject, 0, 75, flexWidth: 1);
            var stack = Ui.VStack(box.transform, "Stack", 4);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, key, 18, Theme.LockGray);
            Ui.ThemedLabel(stack.transform, value, 25, ok ? Theme.UpgradeText : Theme.CinnabarDark);
        }

        /// <summary>数值格。**格子里不再有下面那行小注**(「单体,每次」之类)——
        /// 那句话由 <see cref="Modes"/> 整段来说,留在格子里是同一件事说两遍。
        /// 格子因此矮了 26 单位(104 → 78)。</summary>
        public static void Stats(Transform parent, CharDef def, int level, bool owned, bool summon)
        {
            var stats = CollectionStats.Of(def, level);
            if (stats.Count == 0) return;
            string title = summon
                ? Strings.T("collection.side.section.stats_summon", ("level", level))
                : (owned ? Strings.T("collection.side.section.stats", ("level", level))
                         : Strings.T("collection.side.section.stats_base"));
            var section = Section(parent, title);

            var row = Ui.Row(section, "Stats", 13);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, StatBoxH, flexWidth: 1);
            foreach (var stat in stats)
            {
                var box = Ui.OutlinedPanel(row.transform, "Stat", Theme.CardWhite, Theme.PanelBorder, 14, 2);
                Ui.Sized(box.gameObject, 0, StatBoxH, flexWidth: 1);
                var stack = Ui.VStack(box.transform, "Stack", 2);
                Ui.Stretch((RectTransform)stack.transform);
                Ui.ThemedLabel(stack.transform, stat.Label, 18, Theme.LockGray);
                Ui.ThemedLabel(stack.transform, stat.Value.ToString(), 38, stat.Color, Theme.TitleFont);
            }
        }

        /// <summary>攻击模式:这一记**打谁 / 护谁**。一格一条,前面那枚色点就是方向 ——
        /// 朱砂是攻、翠玉是护。召唤字换成召唤物的近战 / 远程。</summary>
        public static void Modes(Transform parent, CharDef def)
        {
            var modes = CardTraits.Modes(def);
            if (modes.Count == 0) return;
            var section = Section(parent, Strings.T("collection.side.section.mode"));
            foreach (var mode in modes)
            {
                var row = Ui.Row(section, "Mode", 15);
                var layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.padding = new RectOffset(17, 17, 0, 0);
                var image = row.AddComponent<Image>();
                image.sprite = Theme.Rounded(12);
                image.type = Image.Type.Sliced;
                image.color = mode.Attack ? Theme.WarnBg : Theme.AdGreenBg;
                Ui.Sized(row, 0, 54, flexWidth: 1);

                var dot = Ui.CardPanel(row.transform, "Dir",
                    mode.Attack ? Theme.GlyphColor(Element.Fire) : Theme.Jade, 8);
                Ui.Sized(dot.gameObject, 31, 31);
                Ui.ThemedLabel(dot.transform,
                    mode.Attack ? Strings.T("collection.mode.dir_attack") : Strings.T("collection.mode.dir_support"),
                    18, Color.white, Theme.TitleFont);

                var name = Ui.ThemedLabel(row.transform, mode.Name, 21, Theme.TextMain);
                name.alignment = TextAnchor.MiddleLeft;
                Ui.Sized(name.gameObject, flexWidth: 1);
                if (!string.IsNullOrEmpty(mode.Note))
                    Ui.ThemedLabel(row.transform, mode.Note, 18, Theme.LockGray);
            }
        }

        /// <summary>特性 · 技能:一条一张小卡,头是「图标 chip + 名」,下面一行说明 ——
        /// 与召唤物 / 敌人详情的那块同款,玩家在三处读到的是同一种东西。
        /// 没有图标的退成纯文字 chip,宽度对得齐。</summary>
        public static void Traits(Transform parent, CharDef def, int level, bool summon, float width)
        {
            var traits = CardTraits.Of(def, level);
            var section = Section(parent, summon
                ? Strings.T("collection.side.section.traits_summon")
                : Strings.T("collection.side.section.traits"));
            if (traits.Count == 0)
            {
                var empty = Ui.ThemedLabel(section, Strings.T("collection.side.no_traits"), 19, Theme.LockGray);
                empty.alignment = TextAnchor.UpperLeft;
                Ui.Sized(empty.gameObject, 0, 34, flexWidth: 1);
                return;
            }

            foreach (var trait in traits)
            {
                float descHeight = string.IsNullOrEmpty(trait.Desc)
                    ? 0f : Ui.WrappedTextHeight(trait.Desc, 19, width - 34);
                var card = Ui.OutlinedPanel(section, "Trait", Theme.CardWhite, Theme.PanelBorder, 14, 2);
                Ui.Sized(card.gameObject, 0, 40 + descHeight + 24, flexWidth: 1);

                var stack = Ui.VStack(card.transform, "Stack", 5);
                var layout = stack.GetComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.padding = new RectOffset(17, 17, 12, 12);
                Ui.Stretch((RectTransform)stack.transform);

                var head = Ui.Row(stack.transform, "Head", 10);
                head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.Sized(head, 0, 33, flexWidth: 1);
                if (trait.IconKey != null)
                    Ui.Chip(head.transform, trait.Amount, CardTraits.ChipColor(trait.IconKey),
                        Color.white, 18, iconKey: trait.IconKey);
                else
                    Ui.Chip(head.transform, trait.Word, Theme.LockedBg, Theme.TextDim, 18);
                Ui.ThemedLabel(head.transform, trait.Name, 22, Theme.TextMain, Theme.TitleFont);

                if (descHeight <= 0f) continue;
                var desc = Ui.ThemedLabel(stack.transform, trait.Desc, 19, Theme.TextDim);
                desc.alignment = TextAnchor.UpperLeft;
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Sized(desc.gameObject, 0, descHeight, flexWidth: 1);
            }
        }

        public static void Recipe(Transform parent, CharDef def, RecipeGraph graph)
        {
            var section = Section(parent, Strings.T("collection.side.section.recipe"));
            var row = Ui.Row(section, "Recipe", 13);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, 0, 88, flexWidth: 1);

            for (int i = 0; i < def.Recipe.Count; i++)
            {
                string part = def.Recipe[i];
                Element? element = graph.TryGet(part, out var partDef) ? partDef.Element : null;
                RecipePart(row.transform, part, element, false);
                Ui.ThemedLabel(row.transform,
                    i == def.Recipe.Count - 1 ? Strings.T("collection.side.recipe_to")
                                              : Strings.T("collection.side.recipe_plus"),
                    24, Theme.LockGray);
            }
            RecipePart(row.transform, def.Id, def.Element, true);
        }

        private static void RecipePart(Transform parent, string id, Element? element, bool isOutput)
        {
            var go = isOutput
                ? Ui.OutlinedPanel(parent, "Part", Theme.ElementSoft(element), Theme.ElementColor(element), 16, 3).gameObject
                : Ui.CardPanel(parent, "Part", Theme.ElementSoft(element), 16).gameObject;
            Ui.Sized(go, 88, 88);
            var stack = Ui.VStack(go.transform, "Stack", 2);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, id, 40, Theme.ElementSoftFg(element), Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                element is { } e ? CharInfo.ElementName(e) : Strings.T("char.element.neutral"),
                16, Theme.ElementSoftFg(element));
        }

        public static void Wuxing(Transform parent, Element element)
        {
            var section = Section(parent, Strings.T("collection.side.section.wuxing"));
            var row = Ui.Row(section, "Wuxing", 13);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Ui.Sized(row, 0, 84, flexWidth: 1);

            var victim = WuxingResolver.Victim(element);
            var counter = WuxingResolver.Counter(element);
            if (victim is { } v)
                WuxingBox(row.transform, v, Theme.WarnBg, Theme.WarnText,
                    Strings.T("collection.side.ke", ("element", CharInfo.ElementName(v))));
            if (counter is { } c)
                WuxingBox(row.transform, c, Theme.PanelInset, Theme.TextDim,
                    Strings.T("collection.side.bei", ("element", CharInfo.ElementName(c))));
        }

        private static void WuxingBox(Transform parent, Element element, Color bg, Color fg, string text)
        {
            var box = Ui.CardPanel(parent, "Wx", bg, 14);
            Ui.Sized(box.gameObject, 0, 84, flexWidth: 1);
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
            Ui.Sized(label.gameObject, 0, 84, flexWidth: 1);
        }
    }
}
