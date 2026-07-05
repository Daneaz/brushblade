using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>收集与出阵卡组页(19.3):卡等级/重复卡/墨锭升级 + 出阵选择(≤4)。</summary>
    public sealed class CollectionView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private MetaState _meta;
        private Action _onBack;
        private Action _save;
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

            // 头部:墨锭 / 卡组状态 / 返回
            var header = Ui.Row(transform, "Header", 24);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0, 0.88f), Vector2.one, Vector2.zero, Vector2.zero);
            Ui.Label(header.transform,
                $"收集 {_meta.OwnedCards.Count} 张    出阵 {CurrentDeck().Count}/{MetaRules.DeckLimit}    墨锭 {_meta.Ink}", 26);
            Ui.TextButton(header.transform, "返回地图", () => _onBack(), new Color(0.3f, 0.3f, 0.3f), 22, new Vector2(130, 56));

            // 消息行
            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0, 0.8f), new Vector2(1, 0.88f), Vector2.zero, Vector2.zero);
            var messageLabel = Ui.Label(messageGo.transform, _message, 22);
            Ui.Stretch(messageLabel.rectTransform);

            // 卡格(网格:每行 6 张)
            var deck = CurrentDeck();
            for (int i = 0; i < _meta.OwnedCards.Count; i++)
            {
                string cardId = _meta.OwnedCards[i];
                int row = i / 6, col = i % 6;
                float y = 0.76f - row * 0.24f;

                var cell = Ui.Panel(transform, $"Card_{cardId}");
                Ui.Anchor((RectTransform)cell.transform,
                    new Vector2(0.02f + col * 0.16f, y - 0.2f), new Vector2(0.02f + col * 0.16f + 0.15f, y),
                    Vector2.zero, Vector2.zero);
                var layout = cell.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 4;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                int level = MetaRules.CardLevel(_meta, cardId);
                _meta.CardCopies.TryGetValue(cardId, out int copies);
                bool inDeck = deck.Contains(cardId);
                var rarity = _graph.Get(cardId).Rarity;

                // 主卡:稀有度底色,点击切换出阵(出阵者加亮边字样)
                Ui.TextButton(cell.transform, $"{cardId}\nLv.{level}" + (inDeck ? "\n[出阵]" : ""),
                    () => ToggleDeck(cardId),
                    inDeck ? Color.Lerp(Ui.RarityColor(rarity), Color.yellow, 0.25f) : Ui.RarityColor(rarity),
                    24, new Vector2(120, 92));

                // 升级按钮
                if (level >= MetaRules.MaxCardLevel)
                {
                    Ui.Label(cell.transform, "满级", 18);
                }
                else
                {
                    int copiesNeeded = MetaRules.CopiesRequired(level, rarity);
                    int inkNeeded = MetaRules.InkRequired(level, rarity);
                    var upgrade = Ui.TextButton(cell.transform,
                        $"升级 {copies}/{copiesNeeded}\n墨锭{inkNeeded}",
                        () => Upgrade(cardId),
                        copies >= copiesNeeded && _meta.Ink >= inkNeeded
                            ? new Color(0.2f, 0.42f, 0.3f) : new Color(0.22f, 0.26f, 0.24f),
                        18, new Vector2(120, 52));
                    upgrade.interactable = copies >= copiesNeeded && _meta.Ink >= inkNeeded;
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
            var deck = new System.Collections.Generic.List<string>(_meta.Deck);
            if (deck.Contains(cardId))
            {
                deck.Remove(cardId);
                _message = $"「{cardId}」移出出阵卡组";
            }
            else if (deck.Count >= MetaRules.DeckLimit)
            {
                _message = $"出阵卡组已满({MetaRules.DeckLimit} 张),先移出一张";
                Rebuild();
                return;
            }
            else
            {
                deck.Add(cardId);
                _message = $"「{cardId}」加入出阵卡组";
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
