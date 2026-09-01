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
        /// ——38% 只这里用。RGB 沿用 <see cref="Theme.Scrim"/> 的底色(它与稿的
        /// rgba(22,27,34,...) 精确对应,<see cref="Theme.TextMain"/> 是同一个值)——
        /// 与 Theme.Scrim 的差别就只在 alpha,别自己另配一个「看着差不多」的近似色。</summary>
        private const float ScrimAlpha = 0.38f;

        // 卡片配色取自稿 .coach 的精确色值。Theme 调色板里凑巧色值相近的几个(GoldSoft/
        // GoldDeep)语义上是给「墨锭条/满级牌脚」用的,借来正好对得上就直接用;剩下几个
        // 稿独有的(卡片底色/分隔线/进度点)项目没有对应语义色,就近私有声明,不塞进 Theme。
        private static readonly Color CardBg = new(0.984f, 0.969f, 0.925f);    // 稿 .coach #FBF7EC
        private static readonly Color RuleColor = new(0.894f, 0.859f, 0.773f); // 稿 .rule #E4DBC5
        private static readonly Color DotOff = new(0.878f, 0.835f, 0.722f);    // 稿 .dots i #E0D5B8
        private static readonly Color DotDone = new(0.659f, 0.580f, 0.408f);   // 稿 .dots i.done #A89468

        // 尺寸 = 稿 pt × 2.093(与 BattleView 骨架同一套换算,见 BattleView.cs:17)。
        // 卡片高度是**内容驱动**的(稿 .coach 没写死 height):宽度固定,高度由 Show() 末尾
        // 对 content 强制跑一次布局、量出实际高度后反写到 card 身上——这不是最初的实现,
        // 最初图省事仿照项目里其它弹窗(Modal/ModalShell/EnemyPreview)按文案估了个固定值,
        // 但那几个弹窗的文案都是运营态基本不变的短句,这里是新手引导的正文,字数会随四步
        // 内容浮动,固定高度撑爆时是**溢出而不是裁剪**(OutlinedPanel/CardPanel 都不带
        // Mask/RectMask2D)——文字和按钮会画到金边卡片外面糊在遮罩上,新玩家看到的第一屏
        // UI 就裂开,划不来靠「估得准」去赌。
        private const float CardW = 779f;         // 稿 .coach { width: 372px }
        private const float BorderThickness = 3f; // 描边厚度,card 高度要把它加回去(见 Show() 末尾)
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
            scrimImage.color = new Color(Theme.Scrim.r, Theme.Scrim.g, Theme.Scrim.b, ScrimAlpha);
            // 不挂 Button:Image.raycastTarget 默认开着已经挡住了底下的点击——弹层显示期间
            // 不许穿透去点场上的牌/怪,遮罩压到 38% 只是为了「看得见」,不是为了「点得到」。

            var card = Ui.OutlinedPanel(scrim.transform, "Coach", CardBg, Theme.Gold, 16, BorderThickness, out var face);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            // 高度先随便填 0——只要宽度先定下来,face/content 的宽度就已经能正确解出
            // (uGUI 的锚点百分比是即时算的,不需要等一次布局),content 里的文字才能按
            // 正确的可用宽度换行。真正的高度在 Show() 末尾量出 content 实际内容后再回填。
            cardRect.sizeDelta = new Vector2(CardW, 0f);

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
            // 锚顶(不是四边拉伸)、宽度固定(内缩 PadX)、高度交给 ContentSizeFitter 按
            // 子物体撑出来——卡片高度就是靠量这个撑出来的高度反推的(见下面 Show() 末尾)。
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(-PadX * 2f, 0f);
            contentRect.anchoredPosition = new Vector2(0f, -PadTop);
            // ⚠ uGUI 经典坑:ContentSizeFitter 与 LayoutGroup 挂在同一物体、且该物体又被
            // 父级 LayoutGroup 沿同一根轴控制时,两边会互相递归打架。这里安全的前提是
            // content 的父级 face 没有 LayoutGroup(纯 Anchor 定位的 Image)、card 的父级
            // scrim 也没有——如果以后有人把这张卡片(或 content 的父层级)塞进某个
            // LayoutGroup 里,这条 PreferredSize 就要重新检查是否还成立。
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var taleLabel = Ui.ThemedLabel(content.transform, tale, TaleFontSize, Theme.TextMain,
                Theme.TitleFont, TextAnchor.UpperLeft);
            taleLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            taleLabel.verticalOverflow = VerticalWrapMode.Overflow;

            var rule = Ui.Panel(content.transform, "Rule");
            var ruleImage = rule.AddComponent<Image>();
            ruleImage.color = RuleColor;
            ruleImage.raycastTarget = false;
            Ui.Sized(rule, height: RuleH);

            var doitRow = Ui.Row(content.transform, "Doit", DoitGap);
            // 稿 .doit { align-items: center }——doIt 换行成两行时(如 pick_reward 那句),
            // 「这样做」chip 应该跟文本块垂直居中,不是贴在顶上。
            doitRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Chip(doitRow.transform, Strings.T("battle.coach.doit_label"),
                Theme.GoldSoft, Theme.GoldDeep, DoitKFontSize, 13, 4);
            var doitLabel = Ui.ThemedLabel(doitRow.transform, doIt, DoitFontSize, Theme.TextMain,
                align: TextAnchor.UpperLeft);
            doitLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            doitLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Ui.Sized(doitLabel.gameObject, flexWidth: 1f); // 「这样做」chip 定宽,doIt 文本吃剩下的宽度才断得了行

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
                Ui.Sized(dot, width: DotSize, height: DotSize);
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
            Ui.Sized(skipGo, width: Ui.ChipWidth(skipText, SkipFontSize, 8), height: NextH); // 与下一步钮等高,触控目标一致

            Ui.Sized(Ui.Panel(footRow.transform, "Spacer"), flexWidth: 1f); // 把「下一步」推到最右(稿 .next { margin-left: auto })

            // 最后一步(领奖)按「开始」——引导到此结束,该去真的玩了;其余几步仍是「下一步」
            // (还有下一段引导在等着,不该给「结束」的错觉)。
            string nextText = stepNo >= stepTotal
                ? Strings.T("battle.coach.done")
                : Strings.T("battle.coach.next");
            Ui.PillButton(footRow.transform, nextText, () => onNext?.Invoke(),
                Theme.Cinnabar, Color.white, NextFontSize, new Vector2(NextW, NextH));

            // 卡片高度 = content 实际内容高度 + 上下 padding + 两侧描边厚度。content 挂了
            // ContentSizeFitter 但它的 sizeDelta 要等一次布局才会更新,这里强制立即跑一遍
            // (递归重建 content 全部子物体,包括 doitRow/footRow 那些嵌套的 Row)才能在
            // 本帧同步拿到准确值——不强制的话就得等到下一帧,card 这一帧会先显示成 0 高。
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            float cardHeight = contentRect.rect.height + PadTop + PadBottom + BorderThickness * 2f;
            cardRect.sizeDelta = new Vector2(CardW, cardHeight);

            return scrim;
        }

    }
}
