using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>敌人详情弹窗(2026-07-22):怪牌 + 属性/能力/Boss 阶段。战斗页点怪与图鉴共用。</summary>
    public static class EnemyPreview
    {
        /// <param name="bounty">>0 时在窗内播报本次查阅领到的图鉴赏钱。</param>
        public static GameObject Show(Transform root, EnemyDef def, int bounty = 0)
        {
            var overlay = Ui.ModalShell(root, def.Phases.Count > 0 ? "Boss 图鉴" : "怪物图鉴",
                new Vector2(340, 250), dismissable: true, out var stack);
            Tile(stack, def, new Vector2(150, 150));
            Ui.ThemedLabel(stack, EnemyInfo.Detail(def), 16, Theme.TextDim);
            if (bounty > 0)
                Ui.ThemedLabel(stack, $"◆ 首次查阅赏 {bounty} 墨锭", 18, Theme.GoldBorder, Theme.TitleFont);
            Ui.PillButton(stack, "知道了", () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            return overlay;
        }

        /// <summary>怪牌:五行色底 + 名字 + 血攻;Boss 描金边。未解锁时打码。</summary>
        public static GameObject Tile(Transform parent, EnemyDef def, Vector2 size, bool locked = false)
        {
            var go = Ui.Panel(parent, $"Enemy_{def.Id}");
            var frame = go.AddComponent<Image>();
            frame.sprite = Theme.Rounded(14);
            frame.type = Image.Type.Sliced;
            frame.color = def.Phases.Count > 0 ? Theme.Gold : Theme.Shadow;
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;

            var inner = Ui.Panel(go.transform, "Face");
            var face = inner.AddComponent<Image>();
            face.sprite = Theme.Rounded(12);
            face.type = Image.Type.Sliced;
            face.color = locked ? Theme.LockedBg : Theme.ElementSoft(def.Element);
            Ui.Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));

            var name = Ui.ThemedLabel(inner.transform, locked ? "?" : def.Id,
                Mathf.RoundToInt(size.y * 0.2f),
                locked ? Theme.LockGray : Theme.ElementSoftFg(def.Element), Theme.TitleFont);
            Ui.Anchor(name.rectTransform, new Vector2(0, 0.34f), new Vector2(1, 0.92f),
                Vector2.zero, Vector2.zero);

            var stats = Ui.ThemedLabel(inner.transform,
                locked ? "未遇" : $"血{def.MaxHp} 攻{def.Attack}", 13,
                locked ? Theme.LockGray : Theme.TextDim);
            Ui.Anchor(stats.rectTransform, new Vector2(0, 0.06f), new Vector2(1, 0.32f),
                Vector2.zero, Vector2.zero);
            return go;
        }
    }
}
