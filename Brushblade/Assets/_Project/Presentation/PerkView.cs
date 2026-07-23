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
        private GameObject _modal;

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

            // 牌可点/长按看详情(短按也开,牌本身无其它点击动作)
            var tile = PerkTile(cell.transform, def, level, TileSize);
            var tileButton = tile.AddComponent<Button>();
            tileButton.targetGraphic = tile.GetComponent<Image>();
            tileButton.onClick.AddListener(() => ShowDetail(def));
            HoldToPreview.Attach(tile, () => ShowDetail(def), null);

            if (level >= def.MaxLevel)
            {
                Ui.ThemedLabel(cell.transform, "满级", 15, Theme.UpgradeText);
                return;
            }

            bool gated = level == 0 && charLevel < def.UnlockLevel;
            int cost = def.InkCosts[level];
            string label = gated ? $"需角色 {def.UnlockLevel} 级"
                                  : (level == 0 ? $"解锁 · {cost}墨" : $"升级 · {cost}墨");
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

        private void ShowDetail(PerkDef def)
        {
            if (_modal != null) Destroy(_modal);
            int level = PerkRules.PerkLevel(_meta, def.Id);
            var overlay = Ui.ModalShell(transform, "技能", new Vector2(330, 260), dismissable: true, out var stack);
            PerkTile(stack, def, level, new Vector2(150, 150));
            Ui.ThemedLabel(stack, PerkInfo.Detail(def, level), 17, Theme.TextDim);
            Ui.PillButton(stack, "知道了", () => Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            _modal = overlay;
        }

        /// <summary>技能牌:圆角方牌 + 主题色淡染底 + 两字名 + 当前等级 + 效果短语。参考字库牌与怪牌。</summary>
        private static GameObject PerkTile(Transform parent, PerkDef def, int level, Vector2 size)
        {
            Color theme = PerkColor(def.Effect);

            var go = Ui.Panel(parent, $"Perk_{def.Id}");
            var frame = go.AddComponent<Image>();
            frame.sprite = Theme.Rounded(14);
            frame.type = Image.Type.Sliced;
            frame.color = Theme.Shadow;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;

            var inner = Ui.Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(12);
            face.type = Image.Type.Sliced;
            face.color = Color.Lerp(theme, Theme.CardWhite, 0.86f);
            Ui.Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));

            var name = Ui.ThemedLabel(inner.transform, def.Name, 40, theme, Theme.TitleFont);
            Ui.Anchor(name.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.9f), Vector2.zero, Vector2.zero);

            var lv = Ui.ThemedLabel(inner.transform, $"Lv{level}", 16, Theme.TextMain);
            Ui.Anchor(lv.rectTransform, new Vector2(0, 0.28f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);

            var effect = Ui.ThemedLabel(inner.transform, PerkInfo.ShortEffect(def), 14, Theme.TextDim);
            Ui.Anchor(effect.rectTransform, new Vector2(0, 0.05f), new Vector2(1, 0.26f), Vector2.zero, Vector2.zero);
            return go;
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
