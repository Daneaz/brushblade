using System;
using System.Collections.Generic;
using System.Globalization;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>角色页(spec 2026-10-02 §6.2;稿 Character.dc.html):左档案、右上里程碑轨道、右下统计四页签。
    /// 与稿的唯一偏差:顶栏按全站惯例「标题在左、墨锭 + 返回在右」(PerkView.BuildTopBar)。</summary>
    public sealed class CharacterView : MonoBehaviour
    {
        private enum Tab { Chest, Tower, Battle, Econ }

        private RecipeGraph _graph;
        private CampaignConfig _campaign;
        private MetaState _meta;
        private IReadOnlyList<string> _cardPool;
        private Action _save, _onBack;
        private Tab _tab = Tab.Chest;

        private const float TopBarH = 80f;     // 与 PerkView 同
        private const float ProfW = 419f;      // 稿 200pt
        private const float MsH = 327f;        // 稿 156pt
        private const float Gap = 21f;         // 稿 10pt
        private const int PanelPad = 21;       // 稿 10pt
        private const int WindowSize = 10;     // 轨道格数

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void Init(RecipeGraph graph, CampaignConfig campaign, MetaState meta,
            IReadOnlyList<string> cardPool, Action save, Action onBack)
        {
            _graph = graph; _campaign = campaign; _meta = meta; _cardPool = cardPool;
            _save = save; _onBack = onBack;
            Rebuild();
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);
            var (padSide, padBottom) = SafeArea.MissingInset();
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));

            BuildTopBar(content.transform);

            var body = Ui.Row(content.transform, "Body", Gap);
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(0, 17), new Vector2(0, -TopBarH));
            var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.childForceExpandWidth = false;

            BuildProfile(body.transform);

            var right = Ui.VStack(body.transform, "Right", Gap);
            right.AddComponent<LayoutElement>().flexibleWidth = 1;
            var rightLayout = right.GetComponent<VerticalLayoutGroup>();
            rightLayout.childForceExpandWidth = true;
            rightLayout.childForceExpandHeight = false;
            BuildMilestones(right.transform);
            BuildStats(right.transform);
        }

        // ================= 顶栏 =================

        /// <summary>骨架照抄 PerkView.BuildTopBar(去掉渐隐与手势提示):标题 40 / 副标 23 /
        /// 墨锭 25 / 返回 25 + (130, 63) —— 右侧那对在每一页的位置都必须一样。</summary>
        private void BuildTopBar(Transform parent)
        {
            var bar = Ui.Panel(parent, "TopBar");
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -TopBarH), Vector2.zero);

            var top = Ui.Row(bar.transform, "Top", 21);
            Ui.Stretch((RectTransform)top.transform);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            Ui.ThemedLabel(top.transform, Strings.T("character.title"), 40, Theme.TextMain, Theme.TitleFont);
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            Ui.ThemedLabel(top.transform,
                Strings.T("character.subtitle", ("level", level),
                    ("rank", EndlessRules.RankTitle(_meta.BestDepth)), ("best", _meta.BestDepth)),
                23, Theme.TextDim);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;

            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63));
        }

        // ================= 左:执笔人档案 =================

        private void BuildProfile(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Prof", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().preferredWidth = ProfW;
            var stack = PaddedStack(panel.transform, "Stack", 19, new RectOffset(25, 25, 25, 25));

            int level = MetaRules.LevelProgress(_meta.CharacterXp, out int into, out int need);

            // 头像 + 名 + 等级/段位(与 MapView 同一组 key)
            var avatar = Ui.Row(stack, "Avatar", 21);
            avatar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.CircleGlyph(avatar.transform, Strings.T("map.hero.face"), Theme.Ink, Theme.Paper, 100);
            var names = Ui.VStack(avatar.transform, "Names", 4);
            names.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(names.transform, Strings.T("map.hero.name"), 36, Theme.TextMain, Theme.TitleFont,
                TextAnchor.MiddleLeft);
            Ui.ThemedLabel(names.transform,
                Strings.T("map.hero.level_line", ("level", level), ("rank", EndlessRules.RankTitle(_meta.BestDepth))),
                21, Theme.TextDim, null, TextAnchor.MiddleLeft);

            // 经验:等级与进度同源于 MetaRules.LevelProgress
            var xp = Ui.VStack(stack, "Xp", 6);
            xp.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            var xpRow = Ui.Row(xp.transform, "XpRow", 8);
            xpRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            FlexLabel(xpRow.transform, Strings.T("character.xp_line", ("next", level + 1)), 19, Theme.LockGray);
            Ui.ThemedLabel(xpRow.transform, Strings.T("map.hero.xp_value", ("into", into), ("need", need)),
                19, Theme.LockGray, null, TextAnchor.MiddleRight);
            Ui.Bar(xp.transform, need > 0 ? (float)into / need : 0f, Theme.Gold, new Vector2(370, 10));

            var ttl = TitleRow(stack, Strings.T("character.attrs_title"));
            Ui.ThemedLabel(ttl, Strings.T("character.attrs_next"), 19, Theme.LockGray);
            Rule(ttl);

            // 读的是战斗真正吃到的那份配置(与 MapView 同源),增量按等级曲线算下一级差多少
            var stats = MetaRules.BuildBattleConfig(_meta, _campaign.DropTable);
            int next = level + 1;
            var attrs = Ui.VStack(stack, "Attrs", 6);
            attrs.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            AttrRow(attrs.transform, Strings.T("map.hero.stat.hp"), stats.PlayerMaxHp.ToString(), Theme.Cinnabar,
                MetaRules.MaxHpFor(next) - MetaRules.MaxHpFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.attack"), stats.PlayerAttack.ToString(), Theme.TextMain,
                MetaRules.AttackFor(next) - MetaRules.AttackFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.defense"), stats.PlayerDefense.ToString(), Theme.TextMain,
                MetaRules.DefenseFor(next) - MetaRules.DefenseFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.dodge"),
                Strings.T("map.hero.stat_percent", ("value", stats.PlayerDodge)), Theme.TextMain,
                MetaRules.DodgeFor(next) - MetaRules.DodgeFor(level), "%");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.speed"), stats.PlayerSpeed.ToString(), Theme.TextMain,
                MetaRules.SpeedFor(next) - MetaRules.SpeedFor(level), "");
            AttrRow(attrs.transform, Strings.T("map.hero.stat.ap"), stats.ApPerTurn.ToString(), Theme.TextMain,
                0, ""); // AP 不随等级成长,恒写「—」

            Spring(stack); // 封顶说明钉在面板底(稿 margin-top:auto)
            var note = Ui.ThemedLabel(stack, Strings.T("character.cap_note"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            int owed = LevelRewardRules.OwedChests(_meta);
            if (owed > 0)
                Ui.Chip(stack, Strings.T("character.owed", ("count", owed)), Theme.WarnBg, Theme.WarnText, 19);
        }

        /// <summary>一行属性:名靠左、值靠右、下一级增量在最右一小列;增量为 0 写「—」。</summary>
        private static void AttrRow(Transform parent, string name, string value, Color valueColor,
            int delta, string unit)
        {
            var bg = Ui.CardPanel(parent, "Attr", Theme.PanelInset, 12);
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;   // 稿 21pt
            var row = Ui.Row(bg.transform, "Row", 12);
            Ui.Stretch((RectTransform)row.transform);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            FlexLabel(row.transform, name, 21, Theme.TextDim);
            Ui.ThemedLabel(row.transform, value, 27, valueColor, Theme.TitleFont, TextAnchor.MiddleRight);
            var d = Ui.ThemedLabel(row.transform,
                delta > 0 ? Strings.T("levelup.stat_delta", ("value", $"{delta}{unit}"))
                    : Strings.T("character.attr_capped"),
                18, delta > 0 ? Theme.UpgradeText : Theme.LockGray, null, TextAnchor.MiddleRight);
            d.gameObject.AddComponent<LayoutElement>().preferredWidth = 54;     // 稿 26pt
        }

        // ================= 右上:等级里程碑轨道 =================

        private void BuildMilestones(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Ms", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().preferredHeight = MsH;
            var stack = PaddedStack(panel.transform, "Stack", 17,
                new RectOffset(PanelPad, PanelPad, PanelPad, 19));

            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var window = Window(_meta);
            int claimed = 0;
            foreach (int lv in window)
                if (_meta.ClaimedMilestones.Contains(lv)) claimed++;

            var ttl = TitleRow(stack, Strings.T("character.ms_title"));
            Ui.ThemedLabel(ttl, Strings.T("character.ms_progress", ("claimed", claimed), ("total", window.Count)),
                19, Theme.LockGray);
            Rule(ttl);
            var after = MilestoneRules.ForLevel(MilestoneRules.TableEnd + MilestoneRules.AfterStep).Value;
            Ui.ThemedLabel(ttl, Strings.T("character.ms_after", ("ink", after.Ink.ToString("N0", Inv)),
                    ("rarity", CharInfo.RarityName(after.Rarity))),
                19, Theme.LockGray);

            var track = Ui.Row(stack, "Track", 13);
            track.AddComponent<LayoutElement>().flexibleHeight = 1;
            var trackLayout = track.GetComponent<HorizontalLayoutGroup>();
            trackLayout.childForceExpandWidth = true;
            trackLayout.childForceExpandHeight = true;

            // 轨道底线:从第一格的钉连到最后一格,已达成的那一段染金(稿 .rail)
            int lastReached = -1;
            for (int i = 0; i < window.Count; i++)
                if (window[i] <= level) lastReached = i;
            var rail = Ui.Bar(track.transform,
                window.Count > 1 && lastReached > 0 ? (float)lastReached / (window.Count - 1) : 0f,
                Theme.Gold, new Vector2(0, 6));
            rail.GetComponent<LayoutElement>().ignoreLayout = true;
            float half = window.Count > 0 ? 0.5f / window.Count : 0f;
            Ui.Anchor((RectTransform)rail.transform, new Vector2(half, 1f), new Vector2(1f - half, 1f),
                new Vector2(0, -21 - 3), new Vector2(0, -21 + 3));

            foreach (int lv in window)
                MilestoneNode(track.transform, lv, level);
        }

        /// <summary>一格里程碑:顶上一颗钉 + 下面一张卡,三态 已领 / 可领 / 未达。</summary>
        private void MilestoneNode(Transform parent, int lv, int level)
        {
            var def = MilestoneRules.ForLevel(lv).Value;
            bool done = _meta.ClaimedMilestones.Contains(lv);
            bool ready = !done && MilestoneRules.IsClaimable(_meta, lv);

            var node = Ui.VStack(parent, "Node", 8);
            node.AddComponent<LayoutElement>().flexibleWidth = 1;
            var nodeLayout = node.GetComponent<VerticalLayoutGroup>();
            nodeLayout.childAlignment = TextAnchor.UpperCenter;
            nodeLayout.childForceExpandWidth = false;

            // 钉:已领 = 金底白勾;可领 = 金底;未达 = 宣纸底描边
            var pin = Ui.CircleGlyph(node.transform, "", done || ready ? Theme.Gold : Theme.PanelBorder,
                Color.white, 42);
            if (!done && !ready)
            {
                var inner = Ui.Panel(pin.transform, "Inner").AddComponent<Image>();
                inner.sprite = Theme.Circle;
                inner.color = Theme.PanelPaper;
                Ui.Anchor(inner.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            }
            else if (done && Icons.Get("check") is { } check)
            {
                var icon = Ui.Panel(pin.transform, "Check").AddComponent<Image>();
                icon.sprite = check;
                icon.color = Color.white;
                icon.preserveAspect = true;
                Ui.Anchor(icon.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 10), new Vector2(-10, -10));
            }

            var card = Ui.OutlinedPanel(node.transform, "Card",
                done ? Theme.PanelInset : ready ? Theme.GoldSoft : Theme.CardWhite,
                ready ? Theme.Gold : Theme.PanelBorder, 17, ready ? 3f : 2f);
            var cardElement = card.gameObject.AddComponent<LayoutElement>();
            cardElement.flexibleWidth = 1;
            cardElement.flexibleHeight = 1;
            var cell = PaddedStack(card.transform, "Cell", 6, new RectOffset(6, 6, 10, 10));
            var cellLayout = cell.GetComponent<VerticalLayoutGroup>();
            cellLayout.childAlignment = TextAnchor.UpperCenter;
            cellLayout.childForceExpandWidth = false;

            Ui.ThemedLabel(cell, Strings.T("character.ms_level", ("level", lv)), 27,
                ready ? Theme.TextMain : Theme.TextDim, Theme.TitleFont);
            Ui.IngotLabel(cell, def.Ink.ToString("N0", Inv), 20);
            var pick = Ui.Row(cell, "Pick", 5);
            Dot(pick.transform, Theme.RarityColor(def.Rarity), 13);
            var pickLabel = Ui.ThemedLabel(pick.transform,
                Strings.T("character.ms_pick", ("rarity", CharInfo.RarityName(def.Rarity))), 17, Theme.TextDim);
            pickLabel.raycastTarget = false;

            Spring(cell); // 状态行 / 领取钮钉在卡底
            if (ready)
            {
                int claimLevel = lv;
                var claim = Ui.PillButton(cell, Strings.T("character.ms_claim"),
                    () => MilestonePickSheet.Show(transform, _graph, _meta, claimLevel, _cardPool, _save, Rebuild),
                    Theme.Gold, Theme.GoldText, 21, new Vector2(100, 44));
                claim.GetComponent<LayoutElement>().flexibleWidth = 1;   // 稿 width:100%
            }
            else if (done)
                Ui.ThemedLabel(cell, Strings.T("character.ms_claimed"), 18, Theme.LockGray);
            else
                Ui.ThemedLabel(cell, Strings.T("character.ms_gap", ("gap", lv - level)), 18, Theme.LockGray);
        }

        /// <summary>轨道上显示哪 10 档(纯计算)。Lv.50 及以内:表内十档。之后:从最早一个未领的那档起
        /// 往后 10 档;都领完取最后 10 档;不足 10 档向前补。候选含当前等级之后的下一档。</summary>
        private static List<int> Window(MetaState meta)
        {
            int level = MetaRules.CharacterLevel(meta.CharacterXp);
            var levels = new List<int>();
            if (level <= MilestoneRules.TableEnd)
            {
                foreach (var m in MilestoneRules.MilestonesUpTo(MilestoneRules.TableEnd))
                    levels.Add(m.Level);
                return levels;
            }
            // 多取一个步长:恰好露出当前等级之后的下一档(写「差 N 级」)
            foreach (var m in MilestoneRules.MilestonesUpTo(level + MilestoneRules.AfterStep))
                levels.Add(m.Level);

            int start = levels.Count;
            for (int i = 0; i < levels.Count; i++)
                if (!meta.ClaimedMilestones.Contains(levels[i])) { start = i; break; }
            if (start == levels.Count) start = levels.Count - WindowSize;   // 都领完:最后 10 档
            start = Math.Max(0, Math.Min(start, levels.Count - WindowSize)); // 不足 10 档:向前补
            return levels.GetRange(start, Math.Min(WindowSize, levels.Count - start));
        }

        // ================= 右下:统计四页签 =================

        private void BuildStats(Transform parent)
        {
            var panel = Ui.OutlinedPanel(parent, "Stats", Theme.PanelPaper, Theme.PanelBorder, 21);
            panel.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var stack = PaddedStack(panel.transform, "Stack", 17,
                new RectOffset(PanelPad, PanelPad, 19, PanelPad));

            var tabs = Ui.Row(stack, "Tabs", 13);
            tabs.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            TabButton(tabs.transform, Tab.Chest, Strings.T("character.tab.chest"));
            TabButton(tabs.transform, Tab.Tower, Strings.T("character.tab.tower"));
            TabButton(tabs.transform, Tab.Battle, Strings.T("character.tab.battle"));
            TabButton(tabs.transform, Tab.Econ, Strings.T("character.tab.econ"));

            var pane = Ui.Row(stack, "Pane", 29);
            pane.AddComponent<LayoutElement>().flexibleHeight = 1;
            var paneLayout = pane.GetComponent<HorizontalLayoutGroup>();
            paneLayout.childAlignment = TextAnchor.UpperLeft;
            paneLayout.childForceExpandHeight = true;

            switch (_tab)
            {
                case Tab.Chest: BuildChestPane(pane.transform); break;
                case Tab.Tower: BuildTowerPane(pane.transform); break;
                case Tab.Battle: BuildBattlePane(pane.transform); break;
                case Tab.Econ: BuildEconPane(pane.transform); break;
            }
        }

        /// <summary>页签:选中深灰蓝底白字;未选宣纸底 + 描边(浅卡压浅底必须带描边)。</summary>
        private void TabButton(Transform parent, Tab tab, string label)
        {
            bool on = _tab == tab;
            const int FontSize = 23;   // 稿 11pt
            var outer = Ui.OutlinedPanel(parent, "Tab", on ? Theme.InkSoft : Theme.PanelPaper,
                on ? Theme.InkSoft : Theme.PanelBorder, 25, 2f, out var face);
            Ui.Sized(outer.gameObject, label.Length * FontSize + 58, 50);   // 稿 高 24pt、左右各 14pt
            var text = Ui.ThemedLabel(outer.transform, label, FontSize, on ? Color.white : Theme.TextDim,
                Theme.TitleFont);
            Ui.Stretch(text.rectTransform);
            var button = outer.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() =>
            {
                if (_tab == tab) return;
                _tab = tab;
                Rebuild();
            });
        }

        private void BuildChestPane(Transform pane)
        {
            var stats = _meta.Stats;

            // ① 开箱:七档箱各开了几只
            int opened = 0;
            foreach (var kv in stats.ChestsOpened) opened += kv.Value;
            var chests = Column(pane, 268, Strings.T("character.chest.opened", ("count", opened.ToString("N0", Inv))));
            for (var tier = ChestTier.Paper; tier <= ChestTier.Crimson; tier++)
            {
                stats.ChestsOpened.TryGetValue(tier, out int n);
                ListRow(chests, Theme.ChestColor(tier), ChestRules.TierName(tier), n);
            }
            VSep(pane);

            // ② 开出字卡:按稀有度的张数
            int cards = 0;
            foreach (var kv in stats.CardsFromChests) cards += kv.Value;
            var rarities = Column(pane, 234, Strings.T("character.chest.cards", ("count", cards.ToString("N0", Inv))));
            for (var r = CardRarity.White; r <= CardRarity.Red; r++)
            {
                stats.CardsFromChests.TryGetValue(r, out int n);
                ListRow(rarities, Theme.RarityColor(r),
                    Strings.T("character.chest.rarity_row", ("rarity", CharInfo.RarityName(r))), n);
            }
            VSep(pane);

            // ③ 保底进度:读真实保底计数(与 ChestRules 同源);规则表是红→橙→金,展示倒过来金→橙→红
            var pity = Ui.VStack(pane, "Pity", 15);
            pity.AddComponent<LayoutElement>().flexibleWidth = 1;
            var pityLayout = pity.GetComponent<VerticalLayoutGroup>();
            pityLayout.childAlignment = TextAnchor.UpperLeft;
            pityLayout.childForceExpandWidth = true;
            Ui.ThemedLabel(pity.transform, Strings.T("character.pity.title"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            for (int i = ChestRules.PityRules.Length - 1; i >= 0; i--)
            {
                var rule = ChestRules.PityRules[i];
                int now = rule.Rarity switch
                {
                    CardRarity.Red => _meta.RedPity,
                    CardRarity.Orange => _meta.OrangePity,
                    _ => _meta.GoldPity,
                };
                PityBlock(pity.transform, rule.Rarity, rule.MinTier, now, rule.Threshold);
            }
        }

        private static void PityBlock(Transform parent, CardRarity rarity, ChestTier minTier, int now, int max)
        {
            string rarityName = CharInfo.RarityName(rarity);
            string tierName = ChestRules.TierName(minTier);
            var block = Ui.VStack(parent, "PityRow", 6);
            block.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            var row = Ui.Row(block.transform, "Head", 12);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Dot(row.transform, Theme.RarityColor(rarity), 17);
            FlexLabel(row.transform,
                Strings.T("character.pity.row", ("rarity", rarityName), ("tierName", tierName)), 21, Theme.TextMain);
            Ui.ThemedLabel(row.transform, Strings.T("character.pity.value", ("now", now), ("max", max)),
                27, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleRight);
            Ui.Bar(block.transform, max > 0 ? (float)now / max : 0f, Theme.RarityColor(rarity), new Vector2(0, 10));
            Ui.ThemedLabel(block.transform,
                Strings.T("character.pity.hint", ("left", Math.Max(0, max - now)), ("tierName", tierName),
                    ("rarity", rarityName)),
                18, Theme.LockGray, null, TextAnchor.MiddleLeft);
        }

        private void BuildTowerPane(Transform pane)
        {
            var s = _meta.Stats;
            var tiles = TileRow(pane);
            StatTile(tiles, Strings.T("character.tower.climbs"), s.Climbs);
            StatTile(tiles, Strings.T("character.tower.floors"), s.FloorsCleared);
            StatTile(tiles, Strings.T("character.tower.best"), _meta.BestDepth);
            StatTile(tiles, Strings.T("character.tower.bosses"), s.BossesDefeated);
            StatTile(tiles, Strings.T("character.tower.deaths"), s.Deaths);
        }

        private void BuildBattlePane(Transform pane)
        {
            const int PerRow = 5;
            var s = _meta.Stats;

            // 左:出手最多前 10(5 × 2)
            var left = Ui.VStack(pane, "Top", 10);
            var leftLayout = left.GetComponent<VerticalLayoutGroup>();
            leftLayout.childAlignment = TextAnchor.UpperLeft;
            Ui.ThemedLabel(left.transform, Strings.T("character.battle.top"), 19, Theme.LockGray, null,
                TextAnchor.MiddleLeft);
            var top = StatsRules.TopPlays(_meta, _graph, PerRow * 2);
            if (top.Count == 0)
                Ui.ThemedLabel(left.transform, Strings.T("map.hero.top_empty"), 19, Theme.LockGray, null,
                    TextAnchor.MiddleLeft);
            Transform row = null;
            for (int i = 0; i < top.Count; i++)
            {
                if (i % PerRow == 0)
                {
                    var rowGo = Ui.Row(left.transform, "TopRow", 21);
                    rowGo.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                    row = rowGo.transform;
                }
                var cell = Ui.Row(row, "Cell", 10);
                cell.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Ui.MiniGlyphTile(cell.transform, _graph.Get(top[i].Id), new Vector2(64, 80));
                var count = Ui.ThemedLabel(cell.transform, top[i].Plays.ToString("N0", Inv), 21, Theme.TextMain,
                    null, TextAnchor.MiddleLeft);
                count.gameObject.AddComponent<LayoutElement>().preferredWidth = 50;
            }
            VSep(pane);

            // 右:合成 / 拆解 并排,最高单次伤害通栏
            var right = Ui.VStack(pane, "Tiles", 17);
            right.AddComponent<LayoutElement>().flexibleWidth = 1;
            var rightLayout = right.GetComponent<VerticalLayoutGroup>();
            rightLayout.childAlignment = TextAnchor.UpperLeft;
            rightLayout.childForceExpandWidth = true;
            var pair = TileRowIn(right.transform);
            StatTile(pair, Strings.T("character.battle.composes"), s.Composes);
            StatTile(pair, Strings.T("character.battle.dismantles"), s.Dismantles);
            StatTile(right.transform, Strings.T("character.battle.maxhit"), s.MaxHit);
        }

        private void BuildEconPane(Transform pane)
        {
            var s = _meta.Stats;
            var tiles = TileRow(pane);
            StatTile(tiles, Strings.T("character.econ.earned"), s.InkEarned);
            StatTile(tiles, Strings.T("character.econ.spent"), s.InkSpent);
            StatTile(tiles, Strings.T("character.econ.ads"), s.AdRewards);
        }

        /// <summary>大数字卡:标签 20 号 TextDim、数值 46 号 TitleFont(千分位)。</summary>
        private static void StatTile(Transform parent, string label, int value)
        {
            var tile = Ui.OutlinedPanel(parent, "Tile", Theme.CardWhite, Theme.PanelBorder, 17);
            var element = tile.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.preferredHeight = 130;
            var stack = PaddedStack(tile.transform, "Stack", 8, new RectOffset(21, 21, 19, 19));
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(stack, label, 20, Theme.TextDim, null, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(stack, value.ToString("N0", Inv), 46, Theme.TextMain, Theme.TitleFont,
                TextAnchor.MiddleLeft);
        }

        /// <summary>整页一排等宽大数字卡(登塔 / 经济):贴顶,不被页高拉长。</summary>
        private static Transform TileRow(Transform pane)
        {
            var holder = Ui.VStack(pane, "TilesHolder", 0);
            holder.AddComponent<LayoutElement>().flexibleWidth = 1;
            var layout = holder.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            return TileRowIn(holder.transform);
        }

        private static Transform TileRowIn(Transform parent)
        {
            var row = Ui.Row(parent, "Tiles", 17);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            return row.transform;
        }

        // ================= 小件 =================

        /// <summary>撑满父节点、带内边距的竖排容器(面板里的内容都挂在这上面)。</summary>
        private static Transform PaddedStack(Transform parent, string name, float spacing, RectOffset padding)
        {
            var stack = Ui.VStack(parent, name, spacing);
            Ui.Stretch((RectTransform)stack.transform);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.padding = padding;
            return stack.transform;
        }

        /// <summary>面板标题行:宋体小标题打头,后面由调用方接副标与分隔线(稿 .ttl)。</summary>
        private static Transform TitleRow(Transform parent, string title)
        {
            var row = Ui.Row(parent, "Ttl", 13);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.ThemedLabel(row.transform, title, 25, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            return row.transform;
        }

        /// <summary>竖排列表一列:表头 + 若干行(宝箱页签 ① ②)。</summary>
        private static Transform Column(Transform pane, float width, string header)
        {
            var col = Ui.VStack(pane, "Col", 4);
            col.AddComponent<LayoutElement>().preferredWidth = width;
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            Ui.ThemedLabel(col.transform, header, 19, Theme.LockGray, null, TextAnchor.MiddleLeft);
            return col.transform;
        }

        /// <summary>一行「色点 + 名 + 数」;数为 0 整行压灰(稿 .li.zero)。</summary>
        private static void ListRow(Transform parent, Color dot, string name, int value)
        {
            var color = value == 0 ? Theme.LockGray : Theme.TextMain;
            var row = Ui.Row(parent, "Li", 12);
            row.AddComponent<LayoutElement>().preferredHeight = 33;   // 稿 16pt
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Dot(row.transform, value == 0 ? Theme.LockGray : dot, 17);
            FlexLabel(row.transform, name, 21, color);
            Ui.ThemedLabel(row.transform, value.ToString("N0", Inv), 21, color, null, TextAnchor.MiddleRight);
        }

        private static void Dot(Transform parent, Color color, float size)
        {
            var dot = Ui.Panel(parent, "Dot").AddComponent<Image>();
            dot.sprite = Theme.Rounded(6);
            dot.type = Image.Type.Sliced;
            dot.color = color;
            dot.raycastTarget = false;
            Ui.Sized(dot.gameObject, size, size);
        }

        private static void FlexLabel(Transform parent, string text, int size, Color color) =>
            Ui.ThemedLabel(parent, text, size, color, null, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        private static void Rule(Transform parent)
        {
            var rule = Ui.Panel(parent, "Rule").AddComponent<Image>();
            rule.color = Theme.PanelBorder;
            rule.raycastTarget = false;
            Ui.Sized(rule.gameObject, height: 2, flexWidth: 1);
        }

        private static void VSep(Transform parent)
        {
            var sep = Ui.Panel(parent, "VSep").AddComponent<Image>();
            sep.color = Theme.PanelBorder;
            sep.raycastTarget = false;
            Ui.Sized(sep.gameObject, width: 2);
        }

        private static void Spring(Transform parent) =>
            Ui.Sized(Ui.Panel(parent, "Spring"), flexHeight: 1);
    }
}
