using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>收集与出阵卡组页(19.3):卡等级/重复卡/墨锭升级 + 出阵选择(≤4)。</summary>
    public sealed class CollectionView : MonoBehaviour
    {
        private const int CardsPerPage = 12; // 2 行 × 6

        private RecipeGraph _graph;
        private MetaState _meta;
        private Action _onBack;
        private Action _save;
        private int _page;
        private string _message = "点击卡片加入/移出出阵卡组;集满重复卡后可升级";

        public void Init(RecipeGraph graph, MetaState meta, Action save, Action onBack)
        {
            _graph = graph;
            _meta = meta;
            _save = save;
            _onBack = onBack;
            Rebuild();
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            int pageCount = Mathf.Max(1, (_meta.OwnedCards.Count + CardsPerPage - 1) / CardsPerPage);
            _page = Mathf.Clamp(_page, 0, pageCount - 1);

            var header = Ui.Row(transform, "Header", 20);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.88f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, "卡组", 34, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(header.transform,
                $"收集 {_meta.OwnedCards.Count} 张    出阵 {CurrentDeck().Count}/{MetaRules.DeckLimit}", 22, Theme.TextDim);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 22);
            if (pageCount > 1)
            {
                var prev = Ui.RoundButton(header.transform, "◀", () => { _page--; Rebuild(); },
                    Theme.InkSoft, Color.white, 20, new Vector2(48, 48));
                prev.interactable = _page > 0;
                Ui.ThemedLabel(header.transform, $"{_page + 1}/{pageCount}", 20, Theme.TextDim);
                var next = Ui.RoundButton(header.transform, "▶", () => { _page++; Rebuild(); },
                    Theme.InkSoft, Color.white, 20, new Vector2(48, 48));
                next.interactable = _page < pageCount - 1;
            }
            Ui.PillButton(header.transform, "返回地图", () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(130, 48));

            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0, 0.8f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
            var messageLabel = Ui.ThemedLabel(messageGo.transform, _message, 19, Theme.TextDim);
            Ui.Stretch(messageLabel.rectTransform);

            // 卡格(每页 12 张:2 行 × 6):出阵粉环 + Lv 角标 + 升级脚注
            var deck = CurrentDeck();
            int start = _page * CardsPerPage;
            int end = Mathf.Min(start + CardsPerPage, _meta.OwnedCards.Count);
            for (int i = start; i < end; i++)
            {
                string cardId = _meta.OwnedCards[i];
                int slot = i - start;
                int row = slot / 6, col = slot % 6;
                float y = 0.78f - row * 0.38f;

                var cell = Ui.Panel(transform, $"Card_{cardId}");
                Ui.Anchor((RectTransform)cell.transform,
                    new Vector2(0.02f + col * 0.16f, y - 0.34f), new Vector2(0.02f + col * 0.16f + 0.15f, y),
                    Vector2.zero, Vector2.zero);
                var layout = cell.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 5;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                int level = MetaRules.CardLevel(_meta, cardId);
                _meta.CardCopies.TryGetValue(cardId, out int copies);
                bool inDeck = deck.Contains(cardId);
                var def = _graph.Get(cardId);

                // 主卡:GlyphTile;出阵者用粉色描环表达(selected 环颜色改为粉——用 chip 叠加)
                var badges = Ui.Row(cell.transform, "Badges", 6);
                Ui.Chip(badges.transform, $"Lv.{level}", Theme.Ink, Color.white, 13);
                if (inDeck)
                    Ui.Chip(badges.transform, "出阵", Theme.ExitPink, Color.white, 13);
                Ui.GlyphTile(cell.transform, def, "", inDeck, () => ToggleDeck(cardId),
                    new Vector2(118, 128));

                if (level >= MetaRules.MaxCardLevel)
                {
                    Ui.ThemedLabel(cell.transform, "满级", 15, Theme.UpgradeText);
                }
                else
                {
                    int copiesNeeded = MetaRules.CopiesRequired(level, def.Rarity);
                    int inkNeeded = MetaRules.InkRequired(level, def.Rarity);
                    bool can = copies >= copiesNeeded && _meta.Ink >= inkNeeded;
                    var upgrade = Ui.RoundButton(cell.transform,
                        $"升级 {copies}/{copiesNeeded} · {inkNeeded}墨",
                        () => Upgrade(cardId),
                        can ? Theme.Jade : Theme.AdGreenBg,
                        can ? Color.white : Theme.UpgradeText, 14, new Vector2(118, 36));
                    upgrade.interactable = can;
                }
            }
        }

        private System.Collections.Generic.List<string> CurrentDeck()
        {
            // 显示用:卡组未满时按 StartingLibrary 的自动补齐口径展示实际出阵
            return new System.Collections.Generic.List<string>(MetaRules.StartingLibrary(_meta));
        }

        private void ToggleDeck(string cardId)
        {
            string summary = CharInfo.Summary(_graph.Get(cardId), _graph);
            var deck = new System.Collections.Generic.List<string>(_meta.Deck);
            if (deck.Contains(cardId))
            {
                deck.Remove(cardId);
                _message = $"{summary}\n已移出出阵卡组";
            }
            else if (deck.Count >= MetaRules.DeckLimit)
            {
                _message = $"{summary}\n出阵卡组已满({MetaRules.DeckLimit} 张),先移出一张";
                Rebuild();
                return;
            }
            else
            {
                deck.Add(cardId);
                _message = $"{summary}\n已加入出阵卡组";
            }

            if (MetaRules.TrySetDeck(_meta, deck))
                _save();
            Rebuild();
        }

        private void Upgrade(string cardId)
        {
            if (MetaRules.TryUpgradeCard(_meta, cardId, _graph.Get(cardId).Rarity))
            {
                _message = $"「{cardId}」升至 Lv.{MetaRules.CardLevel(_meta, cardId)}!";
                _save();
            }
            else
            {
                _message = "重复卡或墨锭不足";
            }
            Rebuild();
        }
    }
}
