using UnityEngine;
using UnityEngine.UI;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>新手引导弹层(2026-08-31,稿 Battle.dc.html 的 .scrim/.coach)。
    ///
    /// 三段恒定的故事模板:一句道理(旁白)→一个动作(指令)→一句结果(预告)。此前是屏底
    /// 一行「◆ 提示」——一行字既讲不清道理也预告不了结果,四步引导挤在同一行更是谁都看不清。
    ///
    /// 调用方(BattleView.DrawTutorialHint)每次 Refresh 都重新 Show 一份新的,旧的自己
    /// 负责销毁——与 <c>_modal</c> 那套「用之前先 Destroy 上一份」同一惯例,这里不重复接手
    /// 生命周期管理,Show 只管画。</summary>
    public static class CoachOverlay
    {
        /// <summary>稿上写死的遮罩透明度(<c>.scrim { background: rgba(22,27,34,.38) }</c>)。
        /// ⚠ 不是 <see cref="Theme.Scrim"/>(55%)——那个是给「操作被拒」「确认退出」这类
        /// 普通模态提示用的,盖住整屏没关系;这里点名的是屏幕中央具体的一张牌/一只怪,
        /// 压到 55% 玩家会照着「这样做」的指令去找,却找不到要点哪里。两者刻意不同,
        /// 不要图省事复用 <see cref="Theme.Scrim"/>,也不要把这个常量搬去 Theme 里给别处公用
        /// ——38% 只这里用。</summary>
        private const float ScrimAlpha = 0.38f;

        // 卡片配色取自稿 .coach 的精确色值。Theme 调色板里凑巧色值相近的几个(GoldSoft/
        // GoldDeep)语义上是给「墨锭条/满级牌脚」用的,借来正好对得上就直接用;剩下几个
        // 稿独有的(卡片底色/分隔线/进度点)项目没有对应语义色,就近私有声明,不塞进 Theme。
        private static readonly Color CardBg = new(0.984f, 0.969f, 0.925f);    // 稿 .coach #FBF7EC
        private static readonly Color RuleColor = new(0.894f, 0.859f, 0.773f); // 稿 .rule #E4DBC5
        private static readonly Color DotOff = new(0.878f, 0.835f, 0.722f);    // 稿 .dots i #E0D5B8
        private static readonly Color DotDone = new(0.659f, 0.580f, 0.408f);   // 稿 .dots i.done #A89468

        // 尺寸 = 稿 pt × 2.093(与 BattleView 骨架同一套换算,见 BattleView.cs:17)。
        // 卡片高度是固定值而不是随内容撑高:项目里所有弹窗(Modal/ModalShell/EnemyPreview)
        // 都是这个套路——按已知文案估一个够用的高度,不额外接一套「外框跟内容联动撑高」的
        // uGUI 布局(OutlinedPanel 的 face 靠显式 Anchor 内缩出边框,不是靠布局组撑出来的,
        // 硬接 ContentSizeFitter 会跟这条内缩逻辑打架)。12 条文案都在一两句以内,CardH
        // 留了余量;人工试玩如果发现哪一步文案被卡片切掉,调这一个常量即可。
        private const float CardW = 779f;      // 稿 .coach { width: 372px }
        private const float CardH = 480f;      // 按四步文案估的够用高度(见上,非稿直译值)
        private const float PadX = 42f;        // 稿 .coach { padding: 16px 20px 14px } 的左右
        private const float PadTop = 33f;      // 同上,顶
        private const float PadBottom = 29f;   // 同上,底
        private const float ContentGap = 21f;  // 段间距,近似稿 .rule 上下 margin / .then margin-top 的均值
        private const float RuleH = 3f;        // 稿 .rule { height: 1px }
        private const float DoitGap = 15f;     // 稿 .doit { gap: 7px }
        private const float FootGap = 19f;     // 稿 .foot { gap: 9px }
        private const float DotGap = 8f;       // 稿 .dots { gap: 4px }
        private const float DotSize = 13f;     // 稿 .dots i { width/height: 6px }
        private const float NextH = 71f;       // 稿 .next { height: 34px }
        private const float NextW = 146f;      // 稿 .next { padding: 0 20px } + 两字「下一步/开始」估宽
        private const float SealRightInset = 29f; // 稿 .seal { right: 14px }
        private const float SealTopOverlap = 23f; // 稿 .seal { top: -11px }——正值=向上探出卡片顶边

        private const int TaleFontSize = 38;   // 稿 .tale { font-size: 18px }
        private const int DoitKFontSize = 19;  // 稿 .doit .k { font-size: 9px }
        private const int DoitFontSize = 26;   // 稿 .doit { font-size: 12.5px }
        private const int ThenFontSize = 23;   // 稿 .then { font-size: 11px }
        private const int SealFontSize = 21;   // 稿 .seal { font-size: 10px }
        private const int SkipFontSize = 23;   // 稿 .skip { font-size: 11px }
        private const int NextFontSize = 27;   // 稿 .next { font-size: 13px }

        /// <summary>版面自上而下(稿 .coach):右上角金色印章「第 N 步/共 M 步」→ 衬线大字的
        /// <paramref name="tale"/> → 分隔线 → 「这样做」小标 + <paramref name="doIt"/> →
        /// 灰字 <paramref name="then"/> → 底部一行(进度圆点 + 跳过引导 + 朱砂下一步钮)。
        ///
        /// <paramref name="onNext"/> 只负责关掉这份弹层——真正的教程步骤推进由玩家实际做出
        /// 「拆/合/出/领奖」那个动作时各自调用 <see cref="Brushblade.Core.Tutorial.Notify"/>,
        /// 不是点这颗按钮。调用方(BattleView)据此决定下一次 Refresh 要不要因为 Step 变了
        /// 而重新弹出下一步。</summary>
        public static GameObject Show(Transform root, int stepNo, int stepTotal,
            string tale, string doIt, string then,
            System.Action onNext, System.Action onSkip)
        {
            var scrim = Ui.Panel(root, "CoachScrim");
            Ui.Stretch((RectTransform)scrim.transform);
            var scrimImage = scrim.AddComponent<Image>();
            scrimImage.color = new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, ScrimAlpha);
            // 不挂 Button:Image.raycastTarget 默认开着已经挡住了底下的点击——弹层显示期间
            // 不许穿透去点场上的牌/怪,遮罩压到 38% 只是为了「看得见」,不是为了「点得到」。

            var card = Ui.OutlinedPanel(scrim.transform, "Coach", CardBg, Theme.Gold, 16, 3f, out var face);
            var cardRect = (RectTransform)card.transform;
            Ui.Anchor(cardRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-CardW / 2f, -CardH / 2f), new Vector2(CardW / 2f, CardH / 2f));

            // 印章(稿 .seal):挂在 card 而不是 face 上,且晚于 face 添加——face 已经把
            // OutlinedPanel 的描边内缩挡住了,印章要探出卡片顶边,得是 face 的兄弟节点、
            // 画在它之后(后加入的兄弟排在上层)。card 自己没有布局组,RectTransform 可以
            // 随便摆,不受兄弟节点影响。
            string sealText = Strings.T("battle.coach.step", ("no", stepNo), ("total", stepTotal));
            var seal = Ui.Chip(card.transform, sealText, Theme.Gold, Theme.GoldText, SealFontSize, 21, 23);
            var sealRect = (RectTransform)seal.transform;
            sealRect.anchorMin = sealRect.anchorMax = new Vector2(1f, 1f);
            sealRect.pivot = new Vector2(1f, 1f);
            sealRect.sizeDelta = new Vector2(Ui.ChipWidth(sealText, SealFontSize, 21), Ui.ChipHeight(SealFontSize, 23));
            sealRect.anchoredPosition = new Vector2(-SealRightInset, SealTopOverlap);

            var content = Ui.VStack(face.transform, "Content", ContentGap);
            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            // 换行靠它给 Text 一个实际宽度算断行——与 BattleView 拆合台 pickedInfo 那处
            // effectLabel 换行同一手法(见 BattleView.cs 的 pickedInfoLayout 注释)。
            contentLayout.childForceExpandWidth = true;
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(PadX, PadBottom), new Vector2(-PadX, -PadTop));

            var taleLabel = Ui.ThemedLabel(content.transform, tale, TaleFontSize, Theme.TextMain,
                Theme.TitleFont, TextAnchor.UpperLeft);
            taleLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            taleLabel.verticalOverflow = VerticalWrapMode.Overflow;

            var rule = Ui.Panel(content.transform, "Rule");
            var ruleImage = rule.AddComponent<Image>();
            ruleImage.color = RuleColor;
            ruleImage.raycastTarget = false;
            Sized(rule, height: RuleH);

            var doitRow = Ui.Row(content.transform, "Doit", DoitGap);
            doitRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            Ui.Chip(doitRow.transform, Strings.T("battle.coach.doit_label"),
                Theme.GoldSoft, Theme.GoldDeep, DoitKFontSize, 13, 4);
            var doitLabel = Ui.ThemedLabel(doitRow.transform, doIt, DoitFontSize, Theme.TextMain,
                align: TextAnchor.UpperLeft);
            doitLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            doitLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Sized(doitLabel.gameObject, flexWidth: 1f); // 「这样做」chip 定宽,doIt 文本吃剩下的宽度才断得了行

            var thenLabel = Ui.ThemedLabel(content.transform, then, ThenFontSize, Theme.TextDim,
                align: TextAnchor.UpperLeft);
            thenLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            thenLabel.verticalOverflow = VerticalWrapMode.Overflow;

            var footRow = Ui.Row(content.transform, "Foot", FootGap);
            footRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            var dotsRow = Ui.Row(footRow.transform, "Dots", DotGap);
            for (int i = 0; i < stepTotal; i++)
            {
                var dot = Ui.Panel(dotsRow.transform, "Dot");
                var dotImage = dot.AddComponent<Image>();
                dotImage.sprite = Theme.Rounded((int)(DotSize / 2f));
                dotImage.type = Image.Type.Sliced;
                dotImage.color = i + 1 == stepNo ? Theme.Gold : i + 1 < stepNo ? DotDone : DotOff;
                Sized(dot, width: DotSize, height: DotSize);
            }

            var skipGo = Ui.Panel(footRow.transform, "Skip");
            var skipImage = skipGo.AddComponent<Image>();
            skipImage.color = new Color(0, 0, 0, 0); // 透明,只用来接收点击
            var skipButton = skipGo.AddComponent<Button>();
            skipButton.targetGraphic = skipImage;
            skipButton.onClick.AddListener(() => onSkip?.Invoke());
            string skipText = Strings.T("battle.coach.skip");
            var skipLabel = Ui.ThemedLabel(skipGo.transform, skipText, SkipFontSize, Theme.TextDim);
            Ui.Stretch(skipLabel.rectTransform);
            Sized(skipGo, width: Ui.ChipWidth(skipText, SkipFontSize, 8), height: NextH); // 与下一步钮等高,触控目标一致

            Sized(Ui.Panel(footRow.transform, "Spacer"), flexWidth: 1f); // 把「下一步」推到最右(稿 .next { margin-left: auto })

            // 最后一步(领奖)按「开始」——引导到此结束,该去真的玩了;其余几步仍是「下一步」
            // (还有下一段引导在等着,不该给「结束」的错觉)。
            string nextText = stepNo >= stepTotal
                ? Strings.T("battle.coach.done")
                : Strings.T("battle.coach.next");
            Ui.PillButton(footRow.transform, nextText, () => onNext?.Invoke(),
                Theme.Cinnabar, Color.white, NextFontSize, new Vector2(NextW, NextH));

            return scrim;
        }

        private static void Sized(GameObject go, float width = -1f, float height = -1f, float flexWidth = 0f)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.flexibleWidth = flexWidth;
        }
    }
}
