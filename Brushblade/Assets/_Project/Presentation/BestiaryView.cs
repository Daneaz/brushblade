using System;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>怪物图鉴(2026-07-22):击败即解锁,点开条目查阅时才发赏钱(小怪 20 / Boss 50)。</summary>
    public sealed class BestiaryView : MonoBehaviour
    {
        // 2 行 × 4(2026-07-28 由 2×5 放宽):字怪形象比原先的圆形字头像吃空间,
        // 挤在 5 列里辨认不出谁是谁 —— 少一列换更大的格子
        private const int Columns = 4;
        private const int PerPage = Columns * 2;

        private MetaState _meta;
        private List<EnemyDef> _all;
        private Action _onBack;
        private Action _save;
        private GameObject _modal;
        private int _page;
        private string _message = "击败过的怪会进图鉴;点开未查阅的条目可领赏钱";

        public void Init(CampaignConfig campaign, MetaState meta, Action save, Action onBack)
        {
            _meta = meta;
            _save = save;
            _onBack = onBack;
            _all = CollectEnemies(campaign);
            Rebuild();
        }

        /// <summary>图鉴全集 = 各层段的杂兵池 + Boss 池 + 成语 Boss(按 id 去重,保持配置顺序)。</summary>
        private static List<EnemyDef> CollectEnemies(CampaignConfig campaign)
        {
            var all = new List<EnemyDef>();
            var seen = new HashSet<string>();
            void Add(EnemyDef def)
            {
                if (def != null && seen.Add(def.Id)) all.Add(def);
            }

            if (campaign.Endless?.Bands != null)
                foreach (var band in campaign.Endless.Bands)
                {
                    foreach (var enemy in band.EnemyPool) Add(enemy);
                    foreach (var boss in band.BossPool) Add(boss);
                    foreach (var idiom in band.IdiomBossPool)
                        Add(EndlessGenerator.BuildIdiomBoss(idiom));
                }
            return all;
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            int pageCount = Mathf.Max(1, (_all.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pageCount - 1);

            int unlocked = 0, unclaimed = 0;
            foreach (var def in _all)
            {
                if (!BestiaryRules.IsUnlocked(_meta, def.Id)) continue;
                unlocked++;
                if (!BestiaryRules.IsClaimed(_meta, def.Id)) unclaimed++;
            }

            var header = Ui.Row(transform, "Header", 20);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.88f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, "怪物图鉴", 34, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(header.transform, $"已录 {unlocked}/{_all.Count}", 22, Theme.TextDim);
            if (unclaimed > 0)
                Ui.Chip(header.transform, $"可领 {unclaimed}", Theme.Cinnabar, Color.white, 15);
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

            int start = _page * PerPage;
            int end = Mathf.Min(start + PerPage, _all.Count);
            for (int i = start; i < end; i++)
            {
                var def = _all[i];
                int slot = i - start;
                int row = slot / Columns, col = slot % Columns;
                float y = 0.75f - row * 0.36f;
                const float cellWidth = 0.205f, cellStride = 0.2225f, cellLeft = 0.07f;

                var cell = Ui.Panel(transform, $"Cell_{def.Id}");
                Ui.Anchor((RectTransform)cell.transform,
                    new Vector2(cellLeft + col * cellStride, y - 0.32f),
                    new Vector2(cellLeft + col * cellStride + cellWidth, y),
                    Vector2.zero, Vector2.zero);
                var layout = cell.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 6;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                bool isUnlocked = BestiaryRules.IsUnlocked(_meta, def.Id);
                bool claimable = isUnlocked && !BestiaryRules.IsClaimed(_meta, def.Id);

                var tile = EnemyPreview.Tile(cell.transform, def, new Vector2(190, 210), !isUnlocked);
                var button = tile.AddComponent<Button>();
                button.targetGraphic = tile.GetComponent<Image>();
                button.onClick.AddListener(() => OnEntryClicked(def));
                button.interactable = isUnlocked;

                if (claimable)
                    Ui.Chip(cell.transform,
                        $"可领 {(def.Phases.Count > 0 ? BestiaryRules.BossBounty : BestiaryRules.MinionBounty)}",
                        Theme.Cinnabar, Color.white, 13);
                else
                    Ui.ThemedLabel(cell.transform, isUnlocked ? "已查阅" : "未遇", 14, Theme.TextDim);
            }
        }

        private void OnEntryClicked(EnemyDef def)
        {
            int bounty = BestiaryRules.TryClaim(_meta, def); // 首次查阅即领赏
            if (bounty > 0)
            {
                _message = $"「{def.Id}」录入图鉴,赏 {bounty} 墨锭";
                _save();
            }
            Rebuild(); // 先重建再弹窗:Rebuild 会清空根节点
            if (_modal != null) Destroy(_modal);
            _modal = EnemyPreview.Show(transform, def, bounty);
        }
    }
}
