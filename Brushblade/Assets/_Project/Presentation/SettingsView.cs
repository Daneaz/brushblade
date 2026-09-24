using System;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>设置页(2026-09-24)。此前是个占位按钮,点了只说「尚未实现」。
    ///
    /// 三个开关,一屏放完 —— 刻意不做分组/分页:能调的就这几样,
    /// 为三行内容套一层导航是给自己找活。
    ///
    /// 改动**即时生效并即时落盘**,没有「确定/取消」:设置页没有会让人后悔的操作,
    /// 多一步确认只是多一步 —— 与商城那些不可逆操作不是一回事。</summary>
    public sealed class SettingsView : MonoBehaviour
    {
        private const float PanelW = 760f;
        private const float PanelH = 560f;

        private MetaState _meta;
        private Action _save;
        private Action _onBack;
        private Transform _list;

        public void Init(MetaState meta, Action save, Action onBack)
        {
            _meta = meta;
            _save = save;
            _onBack = onBack;
            Ui.Stretch((RectTransform)transform); // 视图根铺满安全区,与其余各页同口径
            Build();
        }

        private void Build()
        {
            var panel = Ui.OutlinedPanel(transform, "SettingsPanel",
                Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            // 居中定尺寸:锚点收成中心一点,offset 即 ±半宽半高
            Ui.Anchor((RectTransform)panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-PanelW / 2f, -PanelH / 2f), new Vector2(PanelW / 2f, PanelH / 2f));

            var stack = Ui.VStack(panel.transform, "Stack", 18);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 28, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true; // 开关行要占满宽,右侧状态钮才靠得到右边
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, Strings.T("settings.title"), 30,
                Theme.TextMain, Theme.TitleFont);
            _list = stack.transform;

            Rebuild();
        }

        /// <summary>整块重画 —— 开关的字要跟着状态变,逐个去找 Text 组件改反而更易漏。
        /// 三行内容,重画的代价可以忽略。</summary>
        private void Rebuild()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var child = _list.GetChild(i);
                if (child.name == "Row" || child.name == "Note" || child.name == "Back")
                    Destroy(child.gameObject);
            }

            var s = _meta.Settings;

            ToggleRow(Strings.T("settings.fast_battle"),
                Strings.T("settings.fast_battle.desc",
                    ("rate", SpeedRules.FastRate(GameSettings.Subscribed).ToString("0.#"))),
                s.FastBattle, () => s.FastBattle = !s.FastBattle);

            ToggleRow(Strings.T("settings.sfx"), null,
                s.SfxEnabled, () => s.SfxEnabled = !s.SfxEnabled);

            ToggleRow(Strings.T("settings.music"), null,
                s.MusicEnabled, () => s.MusicEnabled = !s.MusicEnabled);

            // 订阅那一档 ×3 还没实装(第 14 章 14.3.1),这里只写一句说明、不画成可买的钮 ——
            // 「屏上写着玩家点不到的功能」是 README 的硬规矩
            var note = Ui.ThemedLabel(_list, Strings.T("settings.subscriber_speed_note"), 17,
                Theme.TextDim, null, TextAnchor.MiddleCenter);
            note.gameObject.name = "Note";

            var back = Ui.PillButton(_list, Strings.T("settings.back"), () => _onBack?.Invoke(),
                Theme.InkSoft, Color.white, 26, new Vector2(240, 66));
            back.gameObject.name = "Back";
        }

        /// <summary>一行开关:左边标题(可带一行小字说明),右边开/关状态钮。</summary>
        private void ToggleRow(string title, string desc, bool on, Action toggle)
        {
            var row = Ui.Row(_list, "Row", 12);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(row, height: desc == null ? 62f : 78f, flexWidth: 1f);

            var texts = Ui.VStack(row.transform, "Texts", 3);
            texts.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(texts, flexWidth: 1f);
            Ui.ThemedLabel(texts.transform, title, 23, Theme.TextMain, null, TextAnchor.MiddleLeft);
            if (desc != null)
                Ui.ThemedLabel(texts.transform, desc, 17, Theme.TextDim, null, TextAnchor.MiddleLeft);

            // 开=翠玉,关=灰。只靠颜色分不够(色觉障碍 + 强光下),所以**文字也写明**开/关
            Ui.RoundButton(row.transform,
                Strings.T(on ? "settings.on" : "settings.off"),
                () => { toggle(); Commit(); Rebuild(); },
                on ? Theme.Jade : Theme.LockGray, Color.white, 22, new Vector2(132, 58), 14);
        }

        /// <summary>即时生效 + 即时落盘。不调 Bind 的话音乐开关不会当场响/停。</summary>
        private void Commit()
        {
            GameSettings.Bind(_meta.Settings);
            _save?.Invoke();
        }
    }
}
