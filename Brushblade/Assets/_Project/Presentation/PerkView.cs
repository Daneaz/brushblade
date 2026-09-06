using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>技能页。临时可编译版(T1,技能树重构)——40 节点按 <see cref="PerkTree"/>
    /// 分三段,竖排一列滚动列表(2×2 牌阵/树状网格是 T9 的事)。锁着的节点置灰;
    /// 点行看详情,点按钮解锁扣墨锭。</summary>
    public sealed class PerkView : MonoBehaviour
    {
        private const float RowHeight = 62f;

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
            Ui.ThemedLabel(header.transform, Strings.T("perk.view.title"), 28, Theme.TextMain, Theme.TitleFont);
            Ui.InkCounter(header.transform, _meta.Ink, 20);

            var scroll = Ui.ScrollList(stack.transform, "List", 8, out var content);
            Ui.Sized(scroll, flexWidth: 1, flexHeight: 1);

            int charLevel = MetaRules.CharacterLevel(_meta.CharacterXp);
            PerkTree? currentTree = null;
            foreach (var def in PerkRules.Nodes)
            {
                if (def.Tree != currentTree)
                {
                    currentTree = def.Tree;
                    var sectionLabel = Ui.ThemedLabel(content, currentTree.ToString(), 18, Theme.TextDim);
                    Ui.Sized(sectionLabel.gameObject, flexWidth: 1, height: 26);
                }
                BuildPerkRow(content, def, charLevel);
            }

            Ui.PillButton(stack.transform, Strings.T("common.back_to_map"), () => _onBack(), Theme.ExitPink, Color.white, 20, new Vector2(180, 50));
        }

        private void BuildPerkRow(Transform parent, PerkNodeDef def, int charLevel)
        {
            bool owned = PerkRules.IsUnlocked(_meta, def.Id);
            bool can = PerkRules.CanUnlock(_meta, def.Id);

            var row = Ui.Row(parent, $"Perk_{def.Id}", 10);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.padding = new RectOffset(15, 15, 0, 0);
            var frame = row.AddComponent<Image>();
            frame.sprite = Theme.Rounded(14);
            frame.type = Image.Type.Sliced;
            frame.color = owned ? Theme.AdGreenBg : Theme.PanelInset;
            Ui.Sized(row, flexWidth: 1, height: RowHeight);

            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.onClick.AddListener(() => Ui.Alert(transform, def.Id, PerkInfo.Detail(def)));

            var textCol = Ui.VStack(row.transform, "Text", 2);
            Ui.Sized(textCol, flexWidth: 1);
            var nameLabel = Ui.ThemedLabel(textCol.transform, def.Id, 18, owned ? Theme.TextMain : Theme.LockGray);
            nameLabel.alignment = TextAnchor.MiddleLeft;
            var subLabel = Ui.ThemedLabel(textCol.transform, PerkInfo.ShortEffect(def), 14, Theme.TextDim);
            subLabel.alignment = TextAnchor.MiddleLeft;

            bool levelGated = !owned && charLevel < def.UnlockLevel;
            string actionLabel = owned ? Strings.T("common.maxed")
                : levelGated ? Strings.T("perk.view.locked_requirement", ("level", def.UnlockLevel))
                : Strings.T("perk.view.unlock_button", ("cost", def.InkCost));
            var actionButton = Ui.PillButton(row.transform, actionLabel, () =>
            {
                if (PerkRules.TryUnlock(_meta, def.Id))
                {
                    _save();
                    Build(); // 成功后刷新
                }
            }, can ? Theme.Cinnabar : Theme.InkSoft, Color.white, 15, new Vector2(140, 40));
            actionButton.interactable = can;
        }
    }
}
