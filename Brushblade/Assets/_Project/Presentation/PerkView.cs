using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能页(第 A 章):技能以牌呈现(2×2),按效果配主题色;墨锭买断解锁与升级。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        private static readonly Vector2 TileSize = new(156, 150);

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
                new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.92f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 14);
            Ui.Stretch((RectTransform)stack.transform);

            var header = Ui.Row(stack.transform, "Header", 20);
            Ui.ThemedLabel(header.transform, "技能", 28, Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 20);

            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            var all = PerkRules.All;
            for (int i = 0; i < all.Count; i += 2)
            {
                var row = Ui.Row(stack.transform, $"Row{i}", 16);
                BuildPerkCell(row.transform, all[i], charLevel);
                if (i + 1 < all.Count) BuildPerkCell(row.transform, all[i + 1], charLevel);
            }

            Ui.PillButton(stack.transform, "返回地图", () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(180, 50));
        }

        private void BuildPerkCell(Transform parent, PerkDef def, int charLevel)
        {
            int level = PerkRules.PerkLevel(_meta, def.Id);
            var cell = Ui.VStack(parent, def.Id, 6);

            PerkTile(cell.transform, def, level);

            if (level >= def.MaxLevel) return; // 满级不再出升级按钮(牌面已显「已满」)

            bool gated = level == 0 && charLevel < def.UnlockLevel;
            string label = gated ? $"需角色 {def.UnlockLevel} 级"
                                  : (level == 0 ? "解锁" : "升级");
            var button = Ui.PillButton(cell.transform, label, () =>
            {
                if (PerkRules.TryUpgradePerk(_meta, def.Id))
                {
                    _save();
                    Build(); // 成功后刷新
                }
            }, gated ? Theme.InkSoft : Theme.Cinnabar, Color.white, 15, new Vector2(TileSize.x, 40));
            button.interactable = !gated;
        }

        /// <summary>技能牌:圆角方牌 + 主题色淡染底 + 主题色两字名 + 等级/下一级。参考字库牌与怪牌。</summary>
        private void PerkTile(Transform parent, PerkDef def, int level)
        {
            Color theme = PerkColor(def.Effect);

            var go = Ui.Panel(parent, $"Perk_{def.Id}");
            var frame = go.AddComponent<Image>();
            frame.sprite = Theme.Rounded(14);
            frame.type = Image.Type.Sliced;
            frame.color = Theme.Shadow;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = TileSize.x;
            element.preferredHeight = TileSize.y;

            var inner = Ui.Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(12);
            face.type = Image.Type.Sliced;
            face.color = Color.Lerp(theme, Theme.CardWhite, 0.86f);
            Ui.Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));

            var name = Ui.ThemedLabel(inner.transform, def.Name, 40, theme, Theme.TitleFont);
            Ui.Anchor(name.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.9f), Vector2.zero, Vector2.zero);

            var lv = Ui.ThemedLabel(inner.transform, $"Lv{level}/{def.MaxLevel}", 16, Theme.TextMain);
            Ui.Anchor(lv.rectTransform, new Vector2(0, 0.28f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);

            string next = level >= def.MaxLevel ? "已满"
                : $"+{def.PerLevelValue} · {def.InkCosts[level]}墨";
            var nextLabel = Ui.ThemedLabel(inner.transform, next, 13, Theme.TextDim);
            Ui.Anchor(nextLabel.rectTransform, new Vector2(0, 0.05f), new Vector2(1, 0.26f), Vector2.zero, Vector2.zero);
        }

        private static Color PerkColor(PerkEffect effect) => effect switch
        {
            PerkEffect.MaxHp => Theme.Cinnabar,   // 养元:朱(生命)
            PerkEffect.Shield => Theme.Gold,      // 金汤:金(护盾)
            PerkEffect.Library => Theme.SplitBlue, // 博闻:墨蓝(字库)
            PerkEffect.Ap => Theme.Jade,          // 一气:青(AP)
            _ => Theme.InkSoft,
        };
    }
}
