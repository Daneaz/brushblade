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
                new Vector2(420, 330), dismissable: true, out var stack);
            Tile(stack, def, new Vector2(210, 230));
            Ui.ThemedLabel(stack, EnemyInfo.Detail(def), 16, Theme.TextDim);
            if (bounty > 0)
                Ui.ThemedLabel(stack, $"◆ 首次查阅赏 {bounty} 墨锭", 18, Theme.GoldBorder, Theme.TitleFont);
            Ui.PillButton(stack, "知道了", () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            return overlay;
        }

        /// <summary>怪牌:战斗同款圆形字头像(五行实色 + 白字代表字)+ 名字 + 血攻;Boss 描金边。未解锁时打码。</summary>
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
            face.color = Theme.CardWhite;
            Ui.Anchor((RectTransform)inner.transform, Vector2.zero, Vector2.one,
                new Vector2(3f, 3f), new Vector2(-3f, -3f));

            // 有形象资产就用分层字怪(三层各自浮动),没有则回落到圆形字头像——
            // 资产分批上线,缺哪只哪只照常显示字牌,不会开天窗
            // 形象系数比圆头像大:底稿四周留了 10% 白,同样直径下视觉体积反而更小
            float diameter = size.y * 0.5f;
            GameObject portrait = null;
            if (!locked)
            {
                string prefix = MobAssets.PrefixFor(def, 0);
                if (MobAssets.Layer(prefix, "body") != null)
                {
                    diameter = size.y * 0.66f;
                    portrait = new GameObject($"Mob_{def.Id}", typeof(RectTransform));
                    portrait.transform.SetParent(inner.transform, false);
                    var mob = portrait.AddComponent<MobView>();
                    mob.Init(prefix, diameter);
                    // 图鉴展示机制特征:缺笔妖的残笔、通假字的面具、生僻字的墨雾、焦痕的火芯。
                    // 战斗里这一层由实际状态驱动(MobView.SetStateAmount),这里只是静态露出
                    mob.SetStateAmount(0.55f);
                }
            }
            portrait ??= Ui.CircleGlyph(inner.transform,
                locked ? "?" : EnemyInfo.FaceChar(def, 0),
                locked ? Theme.LockedBg : Theme.ElementColor(def.Element),
                locked ? Theme.LockGray : Color.white, diameter);
            Ui.Anchor((RectTransform)portrait.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-diameter / 2f, -diameter - 8f), new Vector2(diameter / 2f, -8f));

            // 文字区压在形象之下:形象占到 0.66 高,名字必须从 0.28 以下起,否则叠字上
            var name = Ui.ThemedLabel(inner.transform, locked ? "" : def.Id,
                Mathf.RoundToInt(size.y * 0.11f),
                locked ? Theme.LockGray : Theme.TextMain, Theme.TitleFont);
            Ui.Anchor(name.rectTransform, new Vector2(0, 0.10f), new Vector2(1, 0.28f),
                Vector2.zero, Vector2.zero);

            var stats = Ui.ThemedLabel(inner.transform,
                locked ? "未遇" : $"血{def.MaxHp} 攻{def.Attack}",
                Mathf.RoundToInt(size.y * 0.068f),
                locked ? Theme.LockGray : Theme.TextDim);
            Ui.Anchor(stats.rectTransform, new Vector2(0, 0.01f), new Vector2(1, 0.10f),
                Vector2.zero, Vector2.zero);
            return go;
        }
    }
}
