using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>章节地图(19.1 外层):角色状态头 + 章节/关卡格。点击已解锁关卡进入短 run。</summary>
    public sealed class MapView : MonoBehaviour
    {
        private CampaignConfig _campaign;
        private MetaState _meta;
        private Action<int, int> _onStartStage;

        public void Init(CampaignConfig campaign, MetaState meta, Action<int, int> onStartStage)
        {
            _campaign = campaign;
            _meta = meta;
            _onStartStage = onStartStage;
            Build();
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            Ui.Stretch(root);

            // 角色状态头
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var header = Ui.Panel(transform, "Header");
            Ui.Anchor((RectTransform)header.transform, new Vector2(0, 0.88f), Vector2.one, Vector2.zero, Vector2.zero);
            var headerLabel = Ui.Label(header.transform,
                $"正字者 Lv.{level}    经验 {_meta.CharacterXp}    HP 上限 {MetaRules.MaxHpFor(level)}    墨锭 {_meta.Ink}", 28);
            Ui.Stretch(headerLabel.rectTransform);

            // 章节区
            for (int c = 0; c < _campaign.Chapters.Count; c++)
            {
                var chapter = _campaign.Chapters[c];
                float top = 0.82f - c * 0.24f;

                var titleGo = Ui.Panel(transform, $"ChapterTitle{c}");
                Ui.Anchor((RectTransform)titleGo.transform, new Vector2(0, top - 0.05f), new Vector2(1, top), Vector2.zero, Vector2.zero);
                Ui.Label(titleGo.transform, $"第{ChineseNumber(c + 1)}章 · {chapter.Name}(难度 ×{chapter.EnemyScale:0.#})", 26);

                var row = Ui.Row(transform, $"Chapter{c}", 12);
                Ui.Anchor((RectTransform)row.transform, new Vector2(0, top - 0.17f), new Vector2(1, top - 0.05f), Vector2.zero, Vector2.zero);

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
                        () => _onStartStage(chapterIndex, stageIndex), color, 24, new Vector2(110, 84));
                    button.interactable = unlocked;
                }
            }
        }

        private static string ChineseNumber(int n) => n switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => n.ToString(),
        };
    }
}
