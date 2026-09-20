using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>每日商城页(19.6)。2026-09-20 按设计系统的 ShopScreen 重设计稿重写:
    /// 左「今日字摊」四摊位 + 右栏「今日箱位 / 免费补给」,顶栏与收集 / 图鉴 / 技能逐数同款。
    ///
    /// 旧版(按屏幕比例锚点摆的三行)被换掉的三件事:
    ///   ① 不走外层页骨架 —— 顶栏高度、标题字号、返回钮尺寸各是一套,也没有安全区内缩;
    ///   ② 货架上看不出「这张字我几级、还差几张」,而买字卡的真实动机就是凑升级材料;
    ///   ③ 常驻消息行占掉一整行,只为显示「购入成功」——改成顶部居中的墨色 toast。
    ///
    /// 数值规则一条没动(卡价按稀有度、箱价按档、两个广告位每日各一次),全部仍在 ShopRules。</summary>
    public sealed class ShopView : MonoBehaviour
    {
        // 与 CollectionView / BestiaryView 同一套骨架常量(稿面 pt × 2.093)
        private const float TopH = 80f;        // 顶栏 38pt
        private const float SideW = 527f;      // 右栏 252pt
        private const float MainGap = 19f;     // 主区与右栏之间 9pt
        private const float SideHeadH = 54f;   // 右栏头 26pt
        private const float SidePad = 21f;     // 右栏内边距 10pt
        private const float Gap = 13f;         // 行距 6pt

        private static readonly Vector2 CardSize = new(144f, 180f); // 摊位字牌(与旧版同尺寸)
        private const float SlotGap = 21f;
        private const float BuyH = 46f;        // 价格钮高(宝箱格同款)
        private const float SubBarH = 96f;     // 订阅条

        private RecipeGraph _graph;
        private MetaState _meta;
        private IReadOnlyList<string> _cardPool;  // 卡位池:部件 + 已拥有的字
        private IReadOnlyList<string> _chestPool; // 宝箱池:全部可收集字(未拥有的字只出宝箱)
        private ITimeSource _time;
        private Action _save;
        private Action _onBack;
        private GameObject _modal; // 当前弹窗(同屏仅一个)
        private string _toast;     // 成交反馈:画一次就清,不再是常驻消息行

        public void Init(RecipeGraph graph, MetaState meta, IReadOnlyList<string> cardPool,
            IReadOnlyList<string> chestPool, ITimeSource time, Action save, Action onBack)
        {
            _graph = graph;
            _meta = meta;
            _cardPool = cardPool;
            _chestPool = chestPool;
            _time = time;
            _save = save;
            _onBack = onBack;
            Rebuild();
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            // 安全区内缩与其余外层页同一条(SafeArea 只有这一份,别另抄)
            var (padSide, padBottom) = SafeArea.MissingInset();
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frame = content.transform;

            BuildTopBar(frame);

            var main = Ui.Panel(frame, "Main");
            Ui.Anchor((RectTransform)main.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0, -(TopH + Gap)));

            BuildShelf(main.transform);
            BuildSide(main.transform);

            if (!string.IsNullOrEmpty(_toast)) ShowToast(frame, _toast);
            _toast = null; // 一次性:下次 Rebuild 不再复现
        }

        // ---- 顶栏 ----

        private void BuildTopBar(Transform parent)
        {
            var top = Ui.Row(parent, "Top", 21);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)top.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -TopH), Vector2.zero);

            Ui.ThemedLabel(top.transform, Strings.T("shop.header.title"), 40, Theme.TextMain, Theme.TitleFont);
            // 副标题是刷新倒计时(UTC 0 点重掷),不是那句常驻说明 —— 玩家真正要的是「还有多久换货」
            Ui.ThemedLabel(top.transform,
                Strings.T("shop.header.refresh_in", ("time", RefreshCountdown())), 23, Theme.TextDim);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63));
        }

        /// <summary>距下次刷新(UTC 次日 0 点)还有多久,格式 h:mm。</summary>
        private string RefreshCountdown()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(_time.NowUnixSeconds).UtcDateTime;
            var next = now.Date.AddDays(1);
            var left = next - now;
            return $"{(int)left.TotalHours}:{left.Minutes:00}";
        }

        // ---- 左:今日字摊 ----

        private void BuildShelf(Transform parent)
        {
            var shelf = Ui.Panel(parent, "Shelf");
            Ui.Anchor((RectTransform)shelf.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-(SideW + MainGap), 0));

            var head = Ui.Row(shelf.transform, "ShelfHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("shop.shelf.title"), 19, Theme.LockGray);

            var row = Ui.Row(shelf.transform, "Slots", SlotGap);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Anchor((RectTransform)row.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -(SideHeadH + Gap + CardSize.y + BuyH + 62f)), new Vector2(0, -(SideHeadH + Gap)));

            for (int i = 0; i < _meta.Shop.CardSlots.Count; i++) BuildSlot(row.transform, i);

            BuildSubscriptionBar(shelf.transform);
        }

        /// <summary>一个摊位:字牌(角标 + 牌脚进度)→ 买下后的进度预告 → 价格钮。</summary>
        private void BuildSlot(Transform parent, int index)
        {
            string card = _meta.Shop.CardSlots[index];
            bool sold = _meta.Shop.CardSold[index];
            var def = _graph.Get(card);
            int price = ShopRules.CardPriceFor(def.Rarity);
            bool owned = _meta.OwnedCards.Contains(card);
            bool component = def.IsComponent;

            var cell = Ui.VStack(parent, $"Slot{index}", 8);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(cell, width: CardSize.x, flexWidth: 0);

            var tile = Ui.GlyphTile(cell.transform, def, false, () => ShowPreview(def), CardSize,
                locked: !owned && !component);
            int level = MetaRules.CardLevel(_meta, card);
            bool maxed = level >= MetaRules.MaxCardLevel;
            _meta.CardCopies.TryGetValue(card, out int copies);
            int needed = maxed ? 0 : MetaRules.CopiesRequired(level, def.Rarity);
            if (!component)
            {
                CardBadges.Apply(tile.gameObject, CardSize, new CardBadges.Spec
                {
                    Rarity = def.Rarity,
                    Level = level,
                    Maxed = maxed,
                    CanUpgrade = MetaRules.CanUpgradeCard(_meta, card, def.Rarity),
                    IsNew = false, // 货架上的牌不是「新到手」,那枚角旗只属于卡组页
                    Locked = !owned,
                });
                CardBadges.Foot(cell.transform, CardSize, owned, copies, needed, maxed,
                    MetaRules.CanUpgradeCard(_meta, card, def.Rarity));
            }
            // 已售:牌面盖一枚朱砂印。与旧版不同 —— 旧版只把钮置灰,牌面看不出哪张已经买走
            if (sold) SoldSeal(tile.gameObject);

            // 进度预告:买下这张之后会怎样。部件没有等级,写它的定位
            string forecast;
            Color forecastColor;
            if (component)
            {
                forecast = Strings.T("shop.slot.component_note");
                forecastColor = Theme.TextDim;
            }
            else if (maxed)
            {
                forecast = Strings.T("shop.slot.maxed_note");
                forecastColor = Theme.TextDim;
            }
            else if (copies + 1 >= needed)
            {
                forecast = Strings.T("shop.slot.unlocks_upgrade", ("level", level + 1));
                forecastColor = Theme.UpgradeText;
            }
            else
            {
                forecast = Strings.T("shop.slot.copies_after", ("copies", copies + 1), ("needed", needed));
                forecastColor = Theme.TextDim;
            }
            Ui.ThemedLabel(cell.transform, forecast, 19, forecastColor);

            BuyButton(cell.transform, CardSize.x, sold, price,
                sold ? Strings.T("shop.slot.sold_today") : price.ToString(),
                () => Do(() => ShopRules.TryBuyCard(_meta, index, def.Rarity),
                    Strings.T("shop.card.buy_success", ("card", card)),
                    Strings.T("shop.card.buy_fail_title"),
                    Strings.T("shop.card.buy_fail_body", ("card", card), ("price", price), ("ink", _meta.Ink))));
        }

        /// <summary>价格钮三态(稿):买得起 = 墨色底白字;墨锭不足 = 凹槽底 + 「差 N」;
        /// 已售 = 锁灰底 + 「今日已购」。差多少写出来,玩家才知道要不要去看那条领墨锭的广告。</summary>
        private void BuyButton(Transform parent, float width, bool sold, int price, string label, Action onClick)
        {
            bool poor = !sold && _meta.Ink < price;
            var button = Ui.RoundButton(parent, poor ? Strings.T("shop.slot.short_by", ("amount", price - _meta.Ink)) : label,
                onClick,
                sold ? Theme.LockedBg : poor ? Theme.PanelInset : Theme.Ink,
                sold ? Theme.LockGray : poor ? Theme.CinnabarDark : Color.white,
                19, new Vector2(width, BuyH), 14);
            button.interactable = !sold && !poor;
        }

        /// <summary>已售印:朱砂斜标改成正放的一枚方印 —— uGUI 旋转会连带旋转裁剪矩形
        /// (与 CardBadges 那枚角旗同一个坑,见该处说明)。</summary>
        private static void SoldSeal(GameObject tile)
        {
            var seal = Ui.Chip(tile.transform, Strings.T("shop.slot.sold"), Theme.Cinnabar, Color.white, 21);
            var element = seal.GetComponent<LayoutElement>();
            Ui.Anchor((RectTransform)seal.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-element.preferredWidth / 2f, -element.preferredHeight / 2f),
                new Vector2(element.preferredWidth / 2f, element.preferredHeight / 2f));
        }

        /// <summary>月订阅条(第 14 章 14.3)。**只占版面,点了说去向** —— 订阅的四条权益
        /// (广告位免看直领 / 箱位 4→5 / 每日墨锭礼包 / 开箱时长 −25%)在 Core 里一条都还没有,
        /// 画成可买的按钮就是「屏上写着玩家点不到的功能」(README 的品牌硬规矩)。
        /// 与顶栏设置钮同一种处理:占位 + 说明弹窗,接上时只换回调、版面不动。</summary>
        private void BuildSubscriptionBar(Transform parent)
        {
            var bar = Ui.CardPanel(parent, "Subscription", Theme.GoldSoft, 15);
            Ui.Anchor((RectTransform)bar.transform, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, SubBarH));
            var button = bar.gameObject.AddComponent<Button>();
            button.targetGraphic = bar;
            button.onClick.AddListener(() => ShowAlert(Strings.T("shop.subscription.soon_title"),
                Strings.T("shop.subscription.soon_body")));

            var stack = Ui.VStack(bar.transform, "Stack", 6);
            stack.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(21, 21, 10, 10);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.title"), 23,
                Theme.GoldDeep, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.perks"), 19,
                Theme.TextDim, null, TextAnchor.MiddleLeft);
            // 「不卖数值、不卖卡、不卖抽数」——第 14 章的红线,写在条上,不藏在条款里
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.promise"), 19,
                Theme.TextDim, null, TextAnchor.MiddleLeft);
        }

        // ---- 右栏:今日箱位 + 免费补给 ----

        private void BuildSide(Transform parent)
        {
            var side = Ui.OutlinedPanel(parent, "Side", Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            Ui.Anchor((RectTransform)side.transform, new Vector2(1, 0), Vector2.one,
                new Vector2(-SideW, 0), Vector2.zero);

            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("shop.side.title"), 19, Theme.LockGray);

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            var body = Ui.VStack(side.transform, "Body", 15);
            body.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SidePad), new Vector2(-SidePad, -SideHeadH - 2));

            BuildChestOffer(body.transform);
            BuildSupplies(body.transform);
        }

        private void BuildChestOffer(Transform parent)
        {
            var tier = _meta.Shop.ChestSlot;
            int tierIndex = (int)tier - 1;
            int price = ShopRules.ChestPrice[tierIndex];
            string chestName = ChestRules.TierName(tier);
            bool sold = _meta.Shop.ChestSold;
            bool slotsFull = _meta.Chests.Count >= ChestRules.SlotLimit;

            var card = Ui.OutlinedPanel(parent, "ChestOffer", Theme.CardWhite, Theme.PanelBorder, 17, 2);
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 258f;

            var stack = Ui.VStack(card.transform, "Stack", 8);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(13, 13, 13, 13);
            Ui.Stretch((RectTransform)stack.transform);

            ChestArt.Draw(stack.transform, tier, ChestView.State.Idle, 84f);
            Ui.ThemedLabel(stack.transform, chestName, 23, Theme.ChestColor(tier), Theme.TitleFont);

            // 三枚事实 chip:张数 · 开启时长 · 当前箱位。都是「买之前该知道的事实」,不是促销话术
            var facts = Ui.Row(stack.transform, "Facts", 7);
            facts.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Chip(facts.transform, Strings.T("shop.chest.cards_chip", ("count", ChestRules.CardCount[tierIndex])),
                Theme.PanelInset, Theme.TextDim, 18);
            Ui.Chip(facts.transform, Strings.T("shop.chest.duration_chip",
                    ("hours", ChestRules.DurationSeconds[tierIndex] / 3600f)),
                Theme.PanelInset, Theme.TextDim, 18);
            Ui.Chip(facts.transform, Strings.T("shop.chest.slots_chip",
                    ("count", _meta.Chests.Count), ("limit", ChestRules.SlotLimit)),
                slotsFull ? Theme.WarnBg : Theme.PanelInset,
                slotsFull ? Theme.WarnText : Theme.TextDim, 18);

            // 箱位满时不可买:按钮直接写清楚为什么,别让玩家点了才弹窗
            string label = sold ? Strings.T("shop.slot.sold_today")
                : slotsFull ? Strings.T("shop.chest.slots_full_label")
                : price.ToString();
            var buy = Ui.RoundButton(stack.transform, label,
                () => Do(() => ShopRules.TryBuyChest(_meta, _chestPool, _time),
                    Strings.T("shop.chest.buy_success", ("chestName", chestName)),
                    Strings.T("shop.chest.buy_fail_title"),
                    slotsFull
                        ? Strings.T("shop.chest.slot_full_body", ("count", ChestRules.SlotLimit), ("limit", ChestRules.SlotLimit))
                        : Strings.T("shop.chest.buy_fail_body", ("chestName", chestName), ("price", price), ("ink", _meta.Ink))),
                sold || slotsFull ? Theme.LockedBg : _meta.Ink < price ? Theme.PanelInset : Theme.Ink,
                sold || slotsFull ? Theme.LockGray : _meta.Ink < price ? Theme.CinnabarDark : Color.white,
                19, new Vector2(0, BuyH), 14);
            buy.GetComponent<LayoutElement>().flexibleWidth = 1;
            buy.interactable = !sold && !slotsFull && _meta.Ink >= price;
        }

        /// <summary>免费补给:两条奖励式广告(领墨锭 / 刷新字摊)。用过的转灰写「已用完」——
        /// 第 14 章的口径是奖励式广告,没有强制广告,所以这一栏只可能是「多给」,不会是「解锁」。</summary>
        private void BuildSupplies(Transform parent)
        {
            Ui.ThemedLabel(parent, Strings.T("shop.supply.title"), 19, Theme.LockGray, null, TextAnchor.MiddleLeft);

            var inkAd = Ui.AdBadge(parent,
                _meta.Shop.InkAdClaimed
                    ? Strings.T("shop.supply.used_label")
                    : Strings.T("shop.ink_ad.claim_label", ("amount", ShopRules.InkAdAmount)),
                () => Do(() => ShopRules.TryClaimInkAd(_meta), Strings.T("shop.ink_ad.claim_success"),
                    Strings.T("shop.ink_ad.already_claimed_title"), Strings.T("shop.ink_ad.already_claimed_body")),
                new Vector2(0, 64));
            inkAd.GetComponent<LayoutElement>().flexibleWidth = 1;
            inkAd.interactable = !_meta.Shop.InkAdClaimed;

            var refresh = Ui.AdBadge(parent,
                _meta.Shop.AdRefreshUsed
                    ? Strings.T("shop.supply.used_label")
                    : Strings.T("shop.refresh.action_label"),
                () => Do(() => ShopRules.TryAdRefresh(_meta, _cardPool, new GameRandom(Environment.TickCount)),
                    Strings.T("shop.refresh.success"),
                    Strings.T("shop.refresh.done_label"), Strings.T("shop.refresh.already_done_body")),
                new Vector2(0, 64));
            refresh.GetComponent<LayoutElement>().flexibleWidth = 1;
            refresh.interactable = !_meta.Shop.AdRefreshUsed;
        }

        // ---- 反馈 ----

        /// <summary>成交反馈:顶部居中的墨色 toast,画一次就随下次 Rebuild 消失。
        /// 取代旧版那条常驻消息行 —— 那行占掉整整一行版面,只为显示一句「购入成功」。</summary>
        private static void ShowToast(Transform parent, string text)
        {
            var toast = Ui.Chip(parent, text, Theme.Ink, Color.white, 21, padX: 29, padY: 17);
            var element = toast.GetComponent<LayoutElement>();
            Ui.Anchor((RectTransform)toast.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-element.preferredWidth / 2f, -(TopH + element.preferredHeight)),
                new Vector2(element.preferredWidth / 2f, -TopH));
        }

        /// <summary>执行一笔交易:成功出 toast,失败弹窗给具体原因(2026-07-19 提示统一弹窗)。</summary>
        private void Do(Func<bool> action, string successMessage, string failTitle = null, string failBody = null)
        {
            if (action())
            {
                _toast = successMessage;
                _save();
                Rebuild();
                return;
            }
            Rebuild();
            ShowAlert(failTitle ?? Strings.T("shop.generic_fail_title"), failBody ?? Strings.T("shop.generic_fail_body"));
        }

        /// <summary>点货架字卡:看详情(商城卡未拥有,按 1 级基础值展示)。</summary>
        private void ShowPreview(CharDef def)
        {
            if (_modal != null) Destroy(_modal);
            // 传 meta:详情里那段「等级 + 升级成本」按养成外层的账画(与卡组页同一份)
            _modal = CharPreview.Show(transform, def, _graph, MetaRules.CardLevel(_meta, def.Id),
                meta: _meta);
        }

        /// <summary>被拒提示统一弹窗;须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }
    }
}
