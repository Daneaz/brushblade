using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>敌人详情弹窗(2026-07-22):怪牌 + 属性/能力/Boss 阶段。战斗页点怪与图鉴共用。</summary>
    public static class EnemyPreview
    {
        /// <summary>详情弹窗的一个形态页。Boss 的四阶段、小怪的单形态、将来精英怪的多形态
        /// 都归成这一种结构 —— 渲染按「形态数」驱动,不按「是不是 Boss」分叉(2026-07-30)。</summary>
        private readonly struct FormTab
        {
            public readonly string Label;    // tab 上的字
            public readonly int AssetPhase;  // 取形象用的形态下标
            public readonly string Detail;   // 该形态的数值 + 技能/能力说明
            public readonly Color TabColor;  // 选中态底色(该形态的五行色)
            /// <summary>该形态的识别 chip(与战斗中同一套命名,但不带实时状态——图鉴是静态资料)。</summary>
            public readonly IReadOnlyList<(string Text, Color Bg)> Chips;

            public FormTab(string label, int assetPhase, string detail, Color tabColor,
                IReadOnlyList<(string Text, Color Bg)> chips)
            {
                Label = label;
                AssetPhase = assetPhase;
                Detail = detail;
                TabColor = tabColor;
                Chips = chips;
            }
        }

        /// <summary>把敌人摊成形态列表。将来精英怪多形态只需在这里多生成几个,
        /// 渲染与交互都不用改。chip 的判断也归这里 —— 让 Select 只负责画。</summary>
        private static List<FormTab> FormsOf(EnemyDef def)
        {
            var forms = new List<FormTab>();
            if (def.Phases.Count > 0)
            {
                for (int i = 0; i < def.Phases.Count; i++)
                {
                    var phase = def.Phases[i];
                    var chips = new List<(string, Color)>();
                    if (phase.Skill != BossSkill.None)
                        chips.Add((EnemyInfo.BossSkillName(phase.Skill),
                            Theme.BossSkillChipColor(phase.Skill)));
                    if (phase.Defense > 0)
                        chips.Add((Strings.T("enemy.preview.defense_chip", ("defense", phase.Defense)), Theme.InkSoft));
                    forms.Add(new FormTab(phase.Char, i, EnemyInfo.PhaseDetail(def, i),
                        Theme.ElementColor(phase.Element), chips));
                }
            }
            else
            {
                var chips = new List<(string, Color)>();
                if (def.Ability != EnemyAbility.None)
                    chips.Add((EnemyInfo.AbilityName(def.Ability),
                        Theme.AbilityChipColor(def.Ability)));
                else if (def.Defense > 0)
                    chips.Add((Strings.T("enemy.preview.defense_chip", ("defense", def.Defense)), Theme.InkSoft)); // 墨渍:没能力,护甲就是它的特征
                forms.Add(new FormTab(EnemyInfo.FaceChar(def, 0), 0,
                    EnemyInfo.MinionDetail(def), Theme.ElementColor(def.Element), chips));
            }
            return forms;
        }

        /// <param name="bounty">&gt;0 时在窗内播报本次查阅领到的图鉴赏钱。</param>
        /// <param name="phase">初始选中的形态下标(战斗中点怪要落在敌人当前阶段,而非恒为 0;
        /// 图鉴调用不传,从第一形态看起)。越界会被钳制。</param>
        public static GameObject Show(Transform root, EnemyDef def, int bounty = 0, int phase = 0)
        {
            bool isBoss = def.Phases.Count > 0;
            var forms = FormsOf(def);
            phase = Mathf.Clamp(phase, 0, forms.Count - 1);
            var overlay = Ui.ModalShell(root, isBoss ? Strings.T("enemy.preview.modal_title_boss") : Strings.T("common.bestiary_title"),
                new Vector2(420, isBoss ? 400 : 340), dismissable: true, out var stack);

            // 标题行:Boss 附总血——四阶段是一条总血池,只看单阶段血量会误解
            int totalHp = 0;
            foreach (var p in def.Phases) totalHp += p.MaxHp;
            Ui.ThemedLabel(stack, isBoss ? Strings.T("enemy.preview.title_with_hp", ("enemyId", def.Id), ("totalHp", totalHp)) : def.Id,
                22, Theme.TextMain, Theme.TitleFont);

            // 先建 tab 行再建内容容器:VStack 按添加顺序排版,tab 必须在形象之上。
            // 按钮稍后填充(onClick 要捕获下面的 Select)
            var tabRow = forms.Count > 1 ? Ui.Row(stack, "Tabs", 6) : null;
            var content = Ui.VStack(stack, "Form", 8);
            var buttons = new List<Button>();

            void Select(int index)
            {
                Ui.Clear(content.transform); // 只重绘内容容器:重建整窗会闪,赏钱行也会丢
                var form = forms[index];
                Tile(content.transform, def, new Vector2(210, 230), false, form.AssetPhase, showFooter: false);
                if (form.Chips.Count > 0) // 无机制的怪(错字鬼/夯土妖)不画空行
                {
                    var chipRow = Ui.Row(content.transform, "Chips", 5);
                    foreach (var (text, bg) in form.Chips)
                        Ui.Chip(chipRow.transform, text, bg, Color.white, 12);
                }
                Ui.ThemedLabel(content.transform, form.Detail, 16, Theme.TextDim);
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i].targetGraphic is Image image)
                        image.color = i == index ? forms[i].TabColor : Theme.PaperDim;
                    var label = buttons[i].GetComponentInChildren<Text>();
                    if (label != null) label.color = i == index ? Color.white : Theme.TextMain;
                }
            }

            if (tabRow != null)
                for (int i = 0; i < forms.Count; i++)
                {
                    int index = i; // 闭包捕获:直接用 i 会让所有按钮都指向末位
                    buttons.Add(Ui.RoundButton(tabRow.transform, forms[i].Label, () => Select(index),
                        Theme.PaperDim, Theme.TextMain, 22, new Vector2(64, 64), 12));
                }

            Select(phase); // 默认形态(战斗中为敌人当前阶段,图鉴恒为 0);顺带把 tab 高亮刷成初始态

            if (isBoss)
                Ui.ThemedLabel(stack, EnemyInfo.ChargeRuleText(), 14, Theme.TextDim);
            if (bounty > 0)
                Ui.ThemedLabel(stack, Strings.T("enemy.preview.bounty_line", ("bounty", bounty)), 18, Theme.GoldBorder, Theme.TitleFont);
            Ui.PillButton(stack, Strings.T("common.ok"), () => Object.Destroy(overlay),
                Theme.LockedBg, Theme.TextMain, 18, new Vector2(150, 48));
            return overlay;
        }

        /// <summary>怪牌:战斗同款圆形字头像(五行实色 + 白字代表字)+ 名字 + 血攻;Boss 描金边。未解锁时打码。
        /// phaseIndex:取形象与代表字用的形态下标(Boss 的阶段;小怪恒为 0)。
        /// showFooter:名字 + 血攻两行是否画出。弹窗内传 false——标题行与 PhaseDetail 已给出
        /// 全部信息,Boss 的顶层 def.MaxHp/Attack 在弹窗里会跟阶段血攻互相矛盾(Finding 2)。</summary>
        public static GameObject Tile(Transform parent, EnemyDef def, Vector2 size,
            bool locked = false, int phaseIndex = 0, bool showFooter = true)
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
                string prefix = MobAssets.PrefixFor(def, phaseIndex);
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
                locked ? "?" : EnemyInfo.FaceChar(def, phaseIndex),
                // 回落颜色取该形态(phaseIndex)的五行,不是恒取首阶段——否则六只成语 Boss
                // 换字不换色,跟 tab 底色/"X系"文字互相矛盾(Finding 3)。小怪 Phases 为空,兼容取 def.Element。
                locked ? Theme.LockedBg : Theme.ElementColor(
                    def.Phases.Count > 0 ? def.Phases[phaseIndex].Element : def.Element),
                locked ? Theme.LockGray : Color.white, diameter);
            Ui.Anchor((RectTransform)portrait.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-diameter / 2f, -diameter - 8f), new Vector2(diameter / 2f, -8f));

            // 文字区压在形象之下:形象占到 0.66 高,名字必须从 0.28 以下起,否则叠字上
            if (showFooter)
            {
                var name = Ui.ThemedLabel(inner.transform, locked ? "" : def.Id,
                    Mathf.RoundToInt(size.y * 0.11f),
                    locked ? Theme.LockGray : Theme.TextMain, Theme.TitleFont);
                Ui.Anchor(name.rectTransform, new Vector2(0, 0.10f), new Vector2(1, 0.28f),
                    Vector2.zero, Vector2.zero);

                var stats = Ui.ThemedLabel(inner.transform,
                    locked ? Strings.T("common.unmet") : Strings.T("enemy.preview.tile_stats", ("maxHp", def.MaxHp), ("attack", def.Attack)),
                    Mathf.RoundToInt(size.y * 0.068f),
                    locked ? Theme.LockGray : Theme.TextDim);
                Ui.Anchor(stats.rectTransform, new Vector2(0, 0.01f), new Vector2(1, 0.10f),
                    Vector2.zero, Vector2.zero);
            }
            return go;
        }
    }
}
