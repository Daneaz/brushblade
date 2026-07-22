using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>技能页(第 A 章):列出各技能等级/上限/下一级墨锭价,墨锭买断解锁与升级。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        private MetaState _meta;
        private System.Action _save;
        private System.Action _onBack;

        public void Init(MetaState meta, System.Action save, System.Action onBack)
        {
            _meta = meta;
            _save = save;
            _onBack = onBack;
            Build();
        }

        private void Build()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            var card = Ui.CardPanel(transform, "Panel");
            Ui.Anchor((RectTransform)card.transform,
                new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 10);
            Ui.Stretch((RectTransform)stack.transform);

            var header = Ui.Row(stack.transform, "Header", 20);
            Ui.ThemedLabel(header.transform, "技能", 28, Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 20);

            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            foreach (var def in PerkRules.All)
                BuildPerkRow(stack.transform, def, charLevel);

            Ui.PillButton(stack.transform, "返回地图", () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(180, 50));
        }

        private void BuildPerkRow(Transform parent, PerkDef def, int charLevel)
        {
            int level = PerkRules.PerkLevel(_meta, def.Id);
            var row = Ui.VStack(parent, def.Id, 4);

            string status = level >= def.MaxLevel
                ? $"{def.Name}  Lv{level}/{def.MaxLevel}  已满"
                : $"{def.Name}  Lv{level}/{def.MaxLevel}  ·  下一级 +{def.PerLevelValue}  ·  {def.InkCosts[level]} 墨锭";
            Ui.ThemedLabel(row.transform, status, 16, Theme.TextMain);

            if (level >= def.MaxLevel) return;

            bool gated = level == 0 && charLevel < def.UnlockLevel;
            string label = gated ? $"需角色 {def.UnlockLevel} 级"
                                  : (level == 0 ? "解锁" : "升级");
            var button = Ui.PillButton(row.transform, label, () =>
            {
                if (PerkRules.TryUpgradePerk(_meta, def.Id))
                {
                    _save();
                    Build(); // 成功后刷新
                }
            }, gated ? Theme.InkSoft : Theme.Cinnabar, Color.white, 15, new Vector2(160, 40));
            button.interactable = !gated;
        }
    }
}
