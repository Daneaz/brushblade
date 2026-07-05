using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>章节地图(19.1 外层):角色状态头 + 章节/关卡格 + 宝箱栏(19.5)。</summary>
    public sealed class MapView : MonoBehaviour
    {
        private CampaignConfig _campaign;
        private MetaState _meta;
        private ITimeSource _time;
        private Action<int, int> _onStartStage;
        private Action _save;
        private Action _onOpenCollection;
        private Action _onOpenShop;
        private string _message;

        public void Init(CampaignConfig campaign, MetaState meta, ITimeSource time,
            Action<int, int> onStartStage, Action save, string message, Action onOpenCollection, Action onOpenShop)
        {
            _onOpenShop = onOpenShop;
            _campaign = campaign;
            _meta = meta;
            _time = time;
            _onStartStage = onStartStage;
            _save = save;
            _onOpenCollection = onOpenCollection;
            _message = message ?? "";
            Rebuild();
            InvokeRepeating(nameof(Tick), 1f, 1f); // 倒计时刷新
        }

        private void Tick()
        {
            foreach (var chest in _meta.Chests)
                if (chest.Timing && !ChestRules.IsReady(chest, _time))
                {
                    Rebuild();
                    return;
                }
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            var root = (RectTransform)transform;
            Ui.Stretch(root);

            // 角色状态头 + 消息
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var header = Ui.Panel(transform, "Header");
            Ui.Anchor((RectTransform)header.transform, new Vector2(0, 0.9f), Vector2.one, Vector2.zero, Vector2.zero);
            var headerRow = Ui.Row(header.transform, "HeaderRow", 24);
            Ui.Stretch((RectTransform)headerRow.transform);
            Ui.Label(headerRow.transform,
                $"正字者 Lv.{level}    经验 {_meta.CharacterXp}    HP 上限 {MetaRules.MaxHpFor(level)}    墨锭 {_meta.Ink}" +
                (string.IsNullOrEmpty(_message) ? "" : $"\n{_message}"), 26);
            Ui.TextButton(headerRow.transform, "收集/卡组", () => _onOpenCollection(),
                new Color(0.28f, 0.3f, 0.42f), 22, new Vector2(140, 56));
            Ui.TextButton(headerRow.transform, "商城", () => _onOpenShop(),
                new Color(0.42f, 0.3f, 0.28f), 22, new Vector2(100, 56));

            // 章节区
            for (int c = 0; c < _campaign.Chapters.Count; c++)
            {
                var chapter = _campaign.Chapters[c];
                float top = 0.88f - c * 0.2f;

                var titleGo = Ui.Panel(transform, $"ChapterTitle{c}");
                Ui.Anchor((RectTransform)titleGo.transform, new Vector2(0, top - 0.045f), new Vector2(1, top), Vector2.zero, Vector2.zero);
                Ui.Label(titleGo.transform, $"第{ChineseNumber(c + 1)}章 · {chapter.Name}(难度 ×{chapter.EnemyScale:0.#})", 24);

                var row = Ui.Row(transform, $"Chapter{c}", 12);
                Ui.Anchor((RectTransform)row.transform, new Vector2(0, top - 0.15f), new Vector2(1, top - 0.045f), Vector2.zero, Vector2.zero);

                for (int s = 0; s < chapter.Stages.Count; s++)
                {
                    int chapterIndex = c, stageIndex = s;
                    bool unlocked = MetaRules.IsStageUnlocked(_meta, _campaign, c, s);
                    bool cleared = c < _meta.ClearedStages.Count && s < _meta.ClearedStages[c];
                    bool boss = chapter.Stages[s].Boss;

                    string label = (boss ? "Boss" : $"关{s + 1}") + (cleared ? "\n✓" : unlocked ? "" : "\n锁");
                    var color = cleared ? new Color(0.2f, 0.4f, 0.25f)
                        : unlocked ? (boss ? new Color(0.55f, 0.25f, 0.2f) : new Color(0.5f, 0.4f, 0.15f))
                        : new Color(0.2f, 0.2f, 0.22f);

                    var button = Ui.TextButton(row.transform, label,
                        () => _onStartStage(chapterIndex, stageIndex), color, 22, new Vector2(100, 76));
                    button.interactable = unlocked;
                }
            }

            DrawChestBar();
        }

        // ---- 宝箱栏(19.5) ----

        private void DrawChestBar()
        {
            var bar = Ui.Row(transform, "Chests", 16);
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0, 0.02f), new Vector2(1, 0.26f), Vector2.zero, Vector2.zero);

            Ui.Label(bar.transform, $"宝箱\n{_meta.Chests.Count}/{ChestRules.SlotLimit}", 20);

            for (int i = 0; i < _meta.Chests.Count; i++)
            {
                int index = i;
                var chest = _meta.Chests[i];
                var cell = Ui.Panel(bar.transform, $"Chest{i}");
                var layout = cell.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 4;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                string name = ChestRules.TierName(chest.Tier);
                if (!chest.Timing)
                {
                    bool anotherTiming = AnyChestTiming();
                    var button = Ui.TextButton(cell.transform, $"{name}\n开始开启",
                        () => Do(() => ChestRules.TryStartOpening(_meta, index, _time)),
                        new Color(0.3f, 0.3f, 0.38f), 20, new Vector2(150, 76));
                    button.interactable = !anotherTiming;
                }
                else if (ChestRules.IsReady(chest, _time))
                {
                    Ui.TextButton(cell.transform, $"{name}\n开箱!", () => OpenChest(index),
                        new Color(0.55f, 0.42f, 0.12f), 20, new Vector2(150, 76));
                }
                else
                {
                    long remaining = ChestRules.RemainingSeconds(chest, _time);
                    Ui.TextButton(cell.transform, $"{name}\n{Format(remaining)}", null,
                        new Color(0.25f, 0.28f, 0.34f), 20, new Vector2(150, 56));
                    var mini = Ui.Row(cell.transform, "Mini", 4);
                    if (!chest.AdUsed)
                    {
                        long cut = ChestRules.AdReductionSeconds[(int)chest.Tier - 1];
                        Ui.TextButton(mini.transform, $"广告-{cut / 60}m", // 原型:直接生效,广告 SDK 后接
                            () => Do(() => ChestRules.TryApplyAdBoost(chest)),
                            new Color(0.2f, 0.38f, 0.3f), 18, new Vector2(96, 40));
                    }
                    Ui.TextButton(mini.transform, $"墨锭{ChestRules.InkCostToSkip(remaining)}",
                        () => Do(() => ChestRules.TrySkipWithInk(_meta, index, _time)),
                        new Color(0.4f, 0.32f, 0.16f), 18, new Vector2(84, 40));
                }
            }
        }

        private bool AnyChestTiming()
        {
            foreach (var chest in _meta.Chests)
                if (chest.Timing && !ChestRules.IsReady(chest, _time))
                    return true;
            return false;
        }

        private void OpenChest(int index)
        {
            if (ChestRules.TryOpen(_meta, index, _time, new GameRandom(Environment.TickCount), out var rewards))
            {
                _message = $"开箱:墨锭 +{rewards.Ink},字卡 {string.Join(" ", rewards.Cards)}";
                _save();
            }
            Rebuild();
        }

        private void Do(Func<bool> action)
        {
            if (action())
                _save();
            Rebuild();
        }

        private static string Format(long seconds) =>
            seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";

        private static string ChineseNumber(int n) => n switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => n.ToString(),
        };
    }
}
