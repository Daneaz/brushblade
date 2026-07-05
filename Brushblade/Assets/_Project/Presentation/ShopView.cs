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

            // 头部
            var header = Ui.Row(transform, "Header", 24);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0, 0.86f), Vector2.one, Vector2.zero, Vector2.zero);
            Ui.Label(header.transform, $"每日商城    墨锭 {_meta.Ink}", 28);
            Ui.TextButton(header.transform, "返回地图", () => _onBack(), new Color(0.3f, 0.3f, 0.3f), 22, new Vector2(130, 56));

            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0, 0.78f), new Vector2(1, 0.86f), Vector2.zero, Vector2.zero);
            var messageLabel = Ui.Label(messageGo.transform, _message, 22);
            Ui.Stretch(messageLabel.rectTransform);

            // 卡位 ×4
            var cardRow = Ui.Row(transform, "Cards", 16);
            Ui.Anchor((RectTransform)cardRow.transform, new Vector2(0, 0.5f), new Vector2(1, 0.76f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < _meta.Shop.CardSlots.Count; i++)
            {
                int index = i;
                string card = _meta.Shop.CardSlots[i];
                bool sold = _meta.Shop.CardSold[i];
                var rarity = _graph.Get(card).Rarity;
                int price = ShopRules.CardPriceFor(rarity);
                var button = Ui.TextButton(cardRow.transform,
                    sold ? $"{card}\n已售" : $"{card}\n{price} 墨锭",
                    () => Do(() => ShopRules.TryBuyCard(_meta, index, rarity), $"购入「{card}」!"),
                    sold ? new Color(0.18f, 0.18f, 0.2f) : Ui.RarityColor(rarity),
                    24, new Vector2(140, 110));
                button.interactable = !sold && _meta.Ink >= price;
            }

            // 宝箱位 + 墨锭广告位 + 刷新
            var bottomRow = Ui.Row(transform, "Bottom", 24);
            Ui.Anchor((RectTransform)bottomRow.transform, new Vector2(0, 0.2f), new Vector2(1, 0.46f), Vector2.zero, Vector2.zero);

            int chestPrice = ShopRules.ChestPrice[(int)_meta.Shop.ChestSlot - 1];
            string chestName = ChestRules.TierName(_meta.Shop.ChestSlot);
            var chestButton = Ui.TextButton(bottomRow.transform,
                _meta.Shop.ChestSold ? $"{chestName}\n已售" : $"{chestName}\n{chestPrice} 墨锭",
                () => Do(() => ShopRules.TryBuyChest(_meta, _unlockedPool, _time), $"{chestName}入箱位!"),
                _meta.Shop.ChestSold ? new Color(0.18f, 0.18f, 0.2f) : new Color(0.4f, 0.32f, 0.16f),
                24, new Vector2(170, 110));
            chestButton.interactable = !_meta.Shop.ChestSold && _meta.Ink >= chestPrice
                && _meta.Chests.Count < ChestRules.SlotLimit;

            var inkAdButton = Ui.TextButton(bottomRow.transform,
                _meta.Shop.InkAdClaimed ? "墨锭已领" : $"看广告领\n{ShopRules.InkAdAmount} 墨锭",
                () => Do(() => ShopRules.TryClaimInkAd(_meta), "墨锭到账!"), // 原型:点击即生效,SDK 后接
                _meta.Shop.InkAdClaimed ? new Color(0.18f, 0.18f, 0.2f) : new Color(0.2f, 0.38f, 0.3f),
                22, new Vector2(150, 110));
            inkAdButton.interactable = !_meta.Shop.InkAdClaimed;

            var refreshButton = Ui.TextButton(bottomRow.transform,
                _meta.Shop.AdRefreshUsed ? "今日已刷新" : "看广告\n刷新货架",
                () => Do(() => ShopRules.TryAdRefresh(_meta, _unlockedPool,
                    new GameRandom(Environment.TickCount)), "货架焕然一新!"),
                _meta.Shop.AdRefreshUsed ? new Color(0.18f, 0.18f, 0.2f) : new Color(0.3f, 0.28f, 0.42f),
                22, new Vector2(150, 110));
            refreshButton.interactable = !_meta.Shop.AdRefreshUsed;
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
