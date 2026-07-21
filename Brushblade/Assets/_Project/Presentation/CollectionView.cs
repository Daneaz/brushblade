using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>收集与出阵卡组页(19.3):卡等级/重复卡/墨锭升级 + 出阵选择(5~15 字)。</summary>
    public sealed class CollectionView : MonoBehaviour
    {
        private const int CardsPerPage = 12; // 2 行 × 6

        private RecipeGraph _graph;
        private MetaState _meta;
        private Action _onBack;
        private Action _save;
        private GameObject _modal; // 当前告知弹窗(同屏仅一个)
        private int _page;
        private string _message = "点字卡看能力;「出阵」按钮编入卡组(只有出阵的字才会上场);集满重复卡后可升级";

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

            // 可升级导航:可升级卡稳定排前,头部计数
            var ordered = new System.Collections.Generic.List<string>(_meta.OwnedCards.Count);
            int upgradable = 0;
            foreach (var id in _meta.OwnedCards)
                if (MetaRules.CanUpgradeCard(_meta, id, _graph.Get(id).Rarity))
                {
                    ordered.Insert(upgradable, id);
                    upgradable++;
                }
                else
                {
                    ordered.Add(id);
                }

            var header = Ui.Row(transform, "Header", 20);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.88f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, "卡组", 34, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(header.transform,
                $"收集 {_meta.OwnedCards.Count} 张    出阵 {_meta.Deck.Count}/{MetaRules.DeckLimit}", 22, Theme.TextDim);
            if (upgradable > 0)
                Ui.Chip(header.transform, $"可升 {upgradable}", Theme.Cinnabar, Color.white, 15);
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
            int start = _page * CardsPerPage;
            int end = Mathf.Min(start + CardsPerPage, ordered.Count);
            for (int i = start; i < end; i++)
            {
                string cardId = ordered[i];
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
                bool pinned = _meta.Deck.Contains(cardId);   // 自选出阵(入档;补齐已废止,2026-07-19)
                var def = _graph.Get(cardId);

                // 主卡:GlyphTile;自选出阵:选中环 + 粉色『出阵』chip
                var badges = Ui.Row(cell.transform, "Badges", 6);
                Ui.Chip(badges.transform, $"Lv.{level}", Theme.Ink, Color.white, 13);
                if (pinned)
                    Ui.Chip(badges.transform, "出阵", Theme.ExitPink, Color.white, 13);
                Ui.GlyphTile(cell.transform, def, "", pinned, () => ShowDetail(cardId),
                    new Vector2(118, 112)); // 点字卡 = 看能力;出阵改走下方独立按钮(2026-07-20)
                Ui.RoundButton(cell.transform, pinned ? "卸下" : "出阵", () => ToggleDeck(cardId),
                    pinned ? Theme.LockedBg : Theme.ExitPink,
                    pinned ? Theme.TextMain : Color.white, 14, new Vector2(118, 32));

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
                        () => ShowUpgradePreview(cardId),
                        can ? Theme.Jade : Theme.AdGreenBg,
                        can ? Color.white : Theme.UpgradeText, 14, new Vector2(118, 36));
                    upgrade.interactable = can;
                }
            }
        }

        private void ToggleDeck(string cardId)
        {
            var deck = new System.Collections.Generic.List<string>(_meta.Deck);
            bool removing = deck.Contains(cardId);
            if (removing) deck.Remove(cardId);
            else deck.Add(cardId);

            if (MetaRules.TrySetDeck(_meta, deck, _graph))
            {
                _save();
                _message = $"「{cardId}」" + (removing
                    ? "已移出出阵列表(未出阵的字不上场)"
                    : "已加入出阵列表(下次登塔生效)");
                Rebuild();
                return;
            }

            Rebuild();
            ShowAlert("出阵受限", removing
                ? $"出阵不能少于 {MetaRules.DeckMinimum} 字。\n先把别的字加进来,再移出这一张。"
                : $"「{cardId}」加不进来。\n出阵列表:{MetaRules.DeckMinimum}~{MetaRules.DeckLimit} 字," +
                  $"每属性至多 {MetaRules.DeckPerElementLimit} 字。");
        }

        /// <summary>点字卡:只看不改状态(拼音/释义/属性/配方/当前等级效果)。</summary>
        private void ShowDetail(string cardId)
        {
            if (_modal != null) Destroy(_modal);
            _modal = CharPreview.Show(transform, _graph.Get(cardId), _graph, MetaRules.CardLevel(_meta, cardId));
        }

        /// <summary>升级前 preview:前后两级效果对比 + 消耗,确认才扣(2026-07-20)。</summary>
        private void ShowUpgradePreview(string cardId)
        {
            var def = _graph.Get(cardId);
            int level = MetaRules.CardLevel(_meta, cardId);
            _meta.CardCopies.TryGetValue(cardId, out int copies);
            int copiesNeeded = MetaRules.CopiesRequired(level, def.Rarity);
            int inkNeeded = MetaRules.InkRequired(level, def.Rarity);

            // 自建而非走 Ui.Modal:要在正文里放卡面(2026-07-22),Modal 只接纯文本
            if (_modal != null) Destroy(_modal);
            var overlay = Ui.ModalShell(transform, $"升级「{cardId}」",
                new Vector2(340, 275), dismissable: true, out var stack);
            _modal = overlay;

            Ui.GlyphTile(stack, def, $"{def.ApCost}AP", false, null, new Vector2(118, 142));
            Ui.ThemedLabel(stack, $"Lv.{level} → Lv.{level + 1}", 21, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack,
                $"{CharInfo.EffectsText(def, level)}\n↓\n{CharInfo.EffectsText(def, level + 1)}",
                17, Theme.TextDim);
            Ui.ThemedLabel(stack,
                $"消耗:重复卡 {copiesNeeded}(有 {copies}) · 墨锭 {inkNeeded}(有 {_meta.Ink})",
                16, Theme.TextDim);

            var buttons = Ui.Row(stack, "Buttons", 14);
            Ui.PillButton(buttons.transform, "确认升级", () =>
            {
                Destroy(overlay); // 先关弹窗:Upgrade 会 Rebuild 清根,顺序反了会留残影
                Upgrade(cardId);
            }, Theme.Jade, Color.white, 18, new Vector2(150, 52));
            Ui.PillButton(buttons.transform, "再想想", () => Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 52));
        }

        private void Upgrade(string cardId)
        {
            var def = _graph.Get(cardId);
            if (MetaRules.TryUpgradeCard(_meta, cardId, def.Rarity))
            {
                int newLevel = MetaRules.CardLevel(_meta, cardId);
                _message = $"「{cardId}」升至 Lv.{newLevel}!\n" + CharInfo.Summary(def, _graph, newLevel);
                _save();
                Rebuild();
                return;
            }

            int level = MetaRules.CardLevel(_meta, cardId);
            Rebuild();
            if (level >= MetaRules.MaxCardLevel)
            {
                ShowAlert("已满级", $"「{cardId}」已是 Lv.{MetaRules.MaxCardLevel},无法再升。");
                return;
            }
            _meta.CardCopies.TryGetValue(cardId, out int copies);
            int copiesNeeded = MetaRules.CopiesRequired(level, def.Rarity);
            int inkNeeded = MetaRules.InkRequired(level, def.Rarity);
            ShowAlert("升级条件不足",
                $"「{cardId}」升到 Lv.{level + 1} 需要:\n" +
                $"重复卡 {copies}/{copiesNeeded} · 墨锭 {_meta.Ink}/{inkNeeded}");
        }

        /// <summary>被拒提示统一弹窗(2026-07-19);须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }
    }
}
