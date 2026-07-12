using System;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>每日商城页(19.6):卡位 ×4 + 宝箱位 + 墨锭广告位 + 每日广告刷新。</summary>
    public sealed class ShopView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private MetaState _meta;
        private IReadOnlyList<string> _unlockedPool;
        private ITimeSource _time;
        private Action _save;
        private Action _onBack;
        private string _message = "每日 0 点刷新;看广告可再刷一次货架";

        public void Init(RecipeGraph graph, MetaState meta, IReadOnlyList<string> unlockedPool, ITimeSource time,
            Action save, Action onBack)
        {
            _graph = graph;
            _meta = meta;
            _unlockedPool = unlockedPool;
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
            Ui.ThemedLabel(header.transform, "每日商城", 34, Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 24);
            Ui.PillButton(header.transform, "返回地图", () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(130, 48));

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
                Ui.GlyphTile(cell.transform, def, sold ? "已售" : "", false, null, new Vector2(130, 150));
                var buy = Ui.RoundButton(cell.transform, sold ? "已售" : price.ToString(),
                    () => Do(() => ShopRules.TryBuyCard(_meta, index, def.Rarity), $"购入「{card}」!"),
                    sold ? Theme.LockedBg : Theme.Ink, sold ? Theme.LockGray : Color.white,
                    18, new Vector2(130, 42));
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
                Theme.RarityColor((Brushblade.Core.CardRarity)(int)_meta.Shop.ChestSlot), Theme.TitleFont);
            Ui.Stretch(chestLabel.rectTransform);
            var chestBuy = Ui.RoundButton(chestCell.transform, _meta.Shop.ChestSold ? "已售" : chestPrice.ToString(),
                () => Do(() => ShopRules.TryBuyChest(_meta, _unlockedPool, _time), $"{chestName}入箱位!"),
                _meta.Shop.ChestSold ? Theme.LockedBg : Theme.Ink,
                _meta.Shop.ChestSold ? Theme.LockGray : Color.white, 18, new Vector2(170, 42));
            chestBuy.interactable = !_meta.Shop.ChestSold && _meta.Ink >= chestPrice
                && _meta.Chests.Count < ChestRules.SlotLimit;

            var inkAd = Ui.AdBadge(bottomRow.transform,
                _meta.Shop.InkAdClaimed ? "墨锭已领" : $"看广告领 {ShopRules.InkAdAmount}",
                () => Do(() => ShopRules.TryClaimInkAd(_meta), "墨锭到账!"), // 原型:点击即生效,SDK 后接
                new Vector2(170, 64));
            inkAd.interactable = !_meta.Shop.InkAdClaimed;

            var refresh = Ui.AdBadge(bottomRow.transform,
                _meta.Shop.AdRefreshUsed ? "今日已刷新" : "看广告刷新货架",
                () => Do(() => ShopRules.TryAdRefresh(_meta, _unlockedPool,
                    new GameRandom(Environment.TickCount)), "货架焕然一新!"),
                new Vector2(190, 64));
            refresh.interactable = !_meta.Shop.AdRefreshUsed;
        }

        private void Do(Func<bool> action, string successMessage)
        {
            if (action())
            {
                _message = successMessage;
                _save();
            }
            else
            {
                _message = "无法完成:墨锭不足或箱位已满";
            }
            Rebuild();
        }
    }
}
