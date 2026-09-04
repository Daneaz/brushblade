using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>每日商城页(19.6):卡位 ×4 + 宝箱位 + 墨锭广告位 + 每日广告刷新。</summary>
    public sealed class ShopView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private MetaState _meta;
        private IReadOnlyList<string> _cardPool;  // 卡位池:部件 + 已拥有的字
        private IReadOnlyList<string> _chestPool; // 宝箱池:全部可收集字(未拥有的字只出宝箱)
        private ITimeSource _time;
        private Action _save;
        private Action _onBack;
        private GameObject _modal; // 当前告知弹窗(同屏仅一个)
        private string _message = Strings.T("shop.hint.default");

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

            // 顶栏:标题 | 墨锭 + 返回
            var header = Ui.Row(transform, "Header", 24);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.88f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, Strings.T("shop.header.title"), 34, Theme.TextMain, Theme.TitleFont);
            Ui.InkCounter(header.transform, _meta.Ink, 24);
            Ui.PillButton(header.transform, Strings.T("common.back_to_map"), () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(130, 48));

            // 消息行
            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0, 0.8f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
            var messageLabel = Ui.ThemedLabel(messageGo.transform, _message, 20, Theme.TextDim);
            Ui.Stretch(messageLabel.rectTransform);

            // 货架卡位 ×4:字牌 + 价格按钮
            var cardRow = Ui.Row(transform, "Cards", 20);
            Ui.Anchor((RectTransform)cardRow.transform, new Vector2(0, 0.44f), new Vector2(1, 0.78f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < _meta.Shop.CardSlots.Count; i++)
            {
                int index = i;
                string card = _meta.Shop.CardSlots[i];
                bool sold = _meta.Shop.CardSold[i];
                var def = _graph.Get(card);
                int price = ShopRules.CardPriceFor(def.Rarity);
                bool affordable = !sold && _meta.Ink >= price;

                var cell = Ui.VStack(cardRow.transform, $"Slot{i}", 8);
                // 2026-08-21:牌面底部那条「已售」随费用带一起撤销 —— 紧挨着的购买钮
                // (下一行)本来就写着「已售」且整个置灰,牌上再印一遍是同一信息说两次。
                Ui.GlyphTile(cell.transform, def, false,
                    () => ShowPreview(def), new Vector2(144, 180)); // 点卡看详情(2026-07-21)
                var buy = Ui.RoundButton(cell.transform, sold ? Strings.T("shop.slot.sold") : price.ToString(),
                    () => Do(() => ShopRules.TryBuyCard(_meta, index, def.Rarity), Strings.T("shop.card.buy_success", ("card", card)),
                        Strings.T("shop.card.buy_fail_title"), Strings.T("shop.card.buy_fail_body", ("card", card), ("price", price), ("ink", _meta.Ink))),
                    sold ? Theme.LockedBg : Theme.Ink, sold ? Theme.LockGray : Color.white,
                    18, new Vector2(144, 42));
                buy.interactable = affordable;
            }

            // 特殊行:宝箱位 + 看广告领墨锭 + 看广告刷新
            var bottomRow = Ui.Row(transform, "Bottom", 24);
            Ui.Anchor((RectTransform)bottomRow.transform, new Vector2(0, 0.06f), new Vector2(1, 0.4f), Vector2.zero, Vector2.zero);

            int chestPrice = ShopRules.ChestPrice[(int)_meta.Shop.ChestSlot - 1];
            string chestName = ChestRules.TierName(_meta.Shop.ChestSlot);
            var chestCell = Ui.VStack(bottomRow.transform, "Chest", 8);
            var chestCard = Ui.CardPanel(chestCell.transform, "ChestCard");
            var chestCardElement = chestCard.gameObject.AddComponent<LayoutElement>();
            chestCardElement.preferredWidth = 170;
            chestCardElement.preferredHeight = 100;
            var chestLabel = Ui.ThemedLabel(chestCard.transform, chestName, 24,
                Theme.ChestColor(_meta.Shop.ChestSlot), Theme.TitleFont);
            Ui.Stretch(chestLabel.rectTransform);
            var chestBuy = Ui.RoundButton(chestCell.transform, _meta.Shop.ChestSold ? Strings.T("shop.slot.sold") : chestPrice.ToString(),
                () => Do(() => ShopRules.TryBuyChest(_meta, _chestPool, _time), Strings.T("shop.chest.buy_success", ("chestName", chestName)),
                    Strings.T("shop.chest.buy_fail_title"),
                    _meta.Chests.Count >= ChestRules.SlotLimit
                        ? Strings.T("shop.chest.slot_full_body", ("count", ChestRules.SlotLimit), ("limit", ChestRules.SlotLimit))
                        : Strings.T("shop.chest.buy_fail_body", ("chestName", chestName), ("price", chestPrice), ("ink", _meta.Ink))),
                _meta.Shop.ChestSold ? Theme.LockedBg : Theme.Ink,
                _meta.Shop.ChestSold ? Theme.LockGray : Color.white, 18, new Vector2(170, 42));
            chestBuy.interactable = !_meta.Shop.ChestSold && _meta.Ink >= chestPrice
                && _meta.Chests.Count < ChestRules.SlotLimit;

            var inkAd = Ui.AdBadge(bottomRow.transform,
                _meta.Shop.InkAdClaimed ? Strings.T("shop.ink_ad.claimed_label") : Strings.T("shop.ink_ad.claim_label", ("amount", ShopRules.InkAdAmount)),
                () => Do(() => ShopRules.TryClaimInkAd(_meta), Strings.T("shop.ink_ad.claim_success"), // 原型:点击即生效,SDK 后接
                    Strings.T("shop.ink_ad.already_claimed_title"), Strings.T("shop.ink_ad.already_claimed_body")),
                new Vector2(170, 64));
            inkAd.interactable = !_meta.Shop.InkAdClaimed;

            var refresh = Ui.AdBadge(bottomRow.transform,
                _meta.Shop.AdRefreshUsed ? Strings.T("shop.refresh.done_label") : Strings.T("shop.refresh.action_label"),
                () => Do(() => ShopRules.TryAdRefresh(_meta, _cardPool,
                    new GameRandom(Environment.TickCount)), Strings.T("shop.refresh.success"),
                    Strings.T("shop.refresh.done_label"), Strings.T("shop.refresh.already_done_body")),
                new Vector2(190, 64));
            refresh.interactable = !_meta.Shop.AdRefreshUsed;
        }

        /// <summary>执行一笔交易:成功走消息条,失败弹窗给具体原因(2026-07-19 提示统一弹窗)。</summary>
        private void Do(Func<bool> action, string successMessage, string failTitle = null, string failBody = null)
        {
            if (action())
            {
                _message = successMessage;
                _save();
                Rebuild();
                return;
            }
            Rebuild();
            ShowAlert(failTitle ?? Strings.T("shop.generic_fail_title"), failBody ?? Strings.T("shop.generic_fail_body"));
        }

        /// <summary>点货架字卡:看详情(商城卡未拥有,按 1 级基础值展示)。</summary>
        private void ShowPreview(Brushblade.Core.CharDef def)
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
