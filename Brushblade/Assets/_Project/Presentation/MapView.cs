using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>章节地图(19.1 外层):角色状态头 + 章节/关卡格 + 宝箱栏(19.5)。</summary>
    public sealed class MapView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private CampaignConfig _campaign;
        private MetaState _meta;
        private ITimeSource _time;
        private Action<int, int> _onStartStage;
        private Action _save;
        private Action _onOpenCollection;
        private Action _onOpenShop;
        private string _message;

        public void Init(RecipeGraph graph, CampaignConfig campaign, MetaState meta, ITimeSource time,
            Action<int, int> onStartStage, Action save, string message, Action onOpenCollection, Action onOpenShop)
        {
            _graph = graph;
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

            // 角色状态头
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var header = Ui.Row(transform, "Header", 22);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.9f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, $"正字者 Lv.{level}", 28, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(header.transform,
                $"经验 {_meta.CharacterXp}    HP 上限 {MetaRules.MaxHpFor(level)}", 20, Theme.TextDim);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 22);
            Ui.RoundButton(header.transform, "收集/卡组", () => _onOpenCollection(),
                Theme.InkSoft, Color.white, 20, new Vector2(140, 50), 12);
            Ui.RoundButton(header.transform, "商城", () => _onOpenShop(),
                Theme.ShopNav, Color.white, 20, new Vector2(100, 50), 12);

            // 消息横幅(绿胶囊,有内容才显示)
            if (!string.IsNullOrEmpty(_message))
            {
                var banner = Ui.Row(transform, "Banner");
                Ui.Anchor((RectTransform)banner.transform, new Vector2(0.25f, 0.845f), new Vector2(0.75f, 0.9f), Vector2.zero, Vector2.zero);
                var pill = Ui.CardPanel(banner.transform, "Pill", Theme.AdGreenBg, 24);
                var pillElement = pill.gameObject.AddComponent<LayoutElement>();
                pillElement.preferredWidth = 560;
                pillElement.preferredHeight = 40;
                var text = Ui.ThemedLabel(pill.transform, _message, 17, Theme.AdGreenText);
                Ui.Stretch(text.rectTransform);
            }

            // 章节区
            for (int c = 0; c < _campaign.Chapters.Count; c++)
            {
                var chapter = _campaign.Chapters[c];
                float top = 0.84f - c * 0.19f;

                var titleGo = Ui.Panel(transform, $"ChapterTitle{c}");
                Ui.Anchor((RectTransform)titleGo.transform, new Vector2(0, top - 0.045f), new Vector2(1, top), Vector2.zero, Vector2.zero);
                var title = Ui.ThemedLabel(titleGo.transform,
                    $"第{Ui.ChineseNumber(c + 1)}章 · {chapter.Name}  (难度 ×{chapter.EnemyScale:0.#})",
                    21, Theme.TextMain, Theme.TitleFont);
                Ui.Stretch(title.rectTransform);

                var row = Ui.Row(transform, $"Chapter{c}", 14);
                Ui.Anchor((RectTransform)row.transform, new Vector2(0, top - 0.14f), new Vector2(1, top - 0.045f), Vector2.zero, Vector2.zero);

                for (int s = 0; s < chapter.Stages.Count; s++)
                {
                    int chapterIndex = c, stageIndex = s;
                    bool unlocked = MetaRules.IsStageUnlocked(_meta, _campaign, c, s);
                    bool cleared = c < _meta.ClearedStages.Count && s < _meta.ClearedStages[c];
                    bool boss = chapter.Stages[s].Boss;

                    string label = (boss ? "Boss" : $"关{s + 1}") + (cleared ? "\n✓" : unlocked ? "" : "\n锁");
                    var bg = cleared ? Theme.DoneGreen
                        : unlocked ? (boss ? Theme.Cinnabar : Theme.Gold)
                        : Theme.LockedBg;
                    var fg = cleared ? Color.white
                        : unlocked ? (boss ? Color.white : Theme.GoldText)
                        : Theme.LockGray;

                    var button = Ui.RoundButton(row.transform, label,
                        () => _onStartStage(chapterIndex, stageIndex), bg, fg, 19, new Vector2(96, 78), 14);
                    button.interactable = unlocked;
                }
            }

            DrawChestBar();
        }

        // ---- 宝箱栏(19.5) ----

        private void DrawChestBar()
        {
            var bar = Ui.Row(transform, "Chests", 18);
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0, 0.02f), new Vector2(1, 0.24f), Vector2.zero, Vector2.zero);

            Ui.ThemedLabel(bar.transform, $"箱位\n{_meta.Chests.Count}/{ChestRules.SlotLimit}", 18, Theme.TextDim, Theme.TitleFont);

            for (int i = 0; i < ChestRules.SlotLimit; i++)
            {
                if (i >= _meta.Chests.Count) { DrawEmptySlot(bar.transform); continue; }
                int index = i;
                var chest = _meta.Chests[i];
                bool ready = chest.Timing && ChestRules.IsReady(chest, _time);

                var card = Ui.CardPanel(bar.transform, $"Chest{i}", Theme.CardWhite, 14);
                var cardElement = card.gameObject.AddComponent<LayoutElement>();
                cardElement.preferredWidth = 168;
                cardElement.preferredHeight = 150;
                var stack = Ui.VStack(card.transform, "Stack", 5);
                Ui.Stretch((RectTransform)stack.transform);

                // 箱型图标:档位色圆角块 + 档位首字(两套形状按档位色区分;19.5.1 六档)
                var iconRow = Ui.Row(stack.transform, "Icon", 0);
                var icon = Ui.CardPanel(iconRow.transform, "Body",
                    Theme.RarityColor((Brushblade.Core.CardRarity)(int)chest.Tier), 10);
                var iconElement = icon.gameObject.AddComponent<LayoutElement>();
                iconElement.preferredWidth = 52;
                iconElement.preferredHeight = 40;
                var iconGlyph = Ui.ThemedLabel(icon.transform, ChestRules.TierName(chest.Tier).Substring(0, 1),
                    22, Color.white, Theme.TitleFont);
                Ui.Stretch(iconGlyph.rectTransform);

                Ui.ThemedLabel(stack.transform, ChestRules.TierName(chest.Tier), 17, Theme.TextMain, Theme.TitleFont);

                if (!chest.Timing)
                {
                    bool anotherTiming = AnyChestTiming();
                    var start = Ui.RoundButton(stack.transform, "开始开启",
                        () => Do(() => ChestRules.TryStartOpening(_meta, index, _time)),
                        Theme.InkSoft, Color.white, 15, new Vector2(140, 36));
                    start.interactable = !anotherTiming;
                }
                else if (ready)
                {
                    Ui.RoundButton(stack.transform, "开箱!", () => OpenChest(index),
                        Theme.Gold, Theme.GoldText, 16, new Vector2(140, 36));
                }
                else
                {
                    long remaining = ChestRules.RemainingSeconds(chest, _time);
                    Ui.ThemedLabel(stack.transform, Format(remaining), 15, Theme.TextDim);
                    var mini = Ui.Row(stack.transform, "Mini", 5);
                    if (!chest.AdUsed)
                    {
                        long cut = ChestRules.AdReductionSeconds[(int)chest.Tier - 1];
                        Ui.AdBadge(mini.transform, $"-{cut / 60}m", // 原型:直接生效,广告 SDK 后接
                            () => Do(() => ChestRules.TryApplyAdBoost(chest)), new Vector2(74, 34));
                    }
                    Ui.RoundButton(mini.transform, $"{ChestRules.InkCostToSkip(remaining)}墨",
                        () => Do(() => ChestRules.TrySkipWithInk(_meta, index, _time)),
                        Theme.Gold, Theme.GoldText, 14, new Vector2(70, 34));
                }
            }
        }

        private void DrawEmptySlot(Transform parent)
        {
            var slot = Ui.CardPanel(parent, "Empty", Theme.LockedBg, 14);
            var slotElement = slot.gameObject.AddComponent<LayoutElement>();
            slotElement.preferredWidth = 168;
            slotElement.preferredHeight = 150;
            var label = Ui.ThemedLabel(slot.transform, "空位", 16, Theme.LockGray);
            Ui.Stretch(label.rectTransform);
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
            if (ChestRules.TryOpen(_meta, index, _time, new GameRandom(Environment.TickCount), out var rewards, _graph))
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
    }
}
