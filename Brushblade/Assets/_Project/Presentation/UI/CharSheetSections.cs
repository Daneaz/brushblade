using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>小节标题件:一段一个 <see cref="Section"/> 标题 + 一条横线。
    ///
    /// 这里原先是字卡详情的全部段落(召唤 / 怎么获得 / 等级 / 数值 / 攻击模式 / 特性 / 配方 / 生克,
    /// 2026-09-05 起卡组页与详情弹窗共用的那一份)。Plan E2(2026-10-11)字卡详情按 CardSheet 稿改版,
    /// 由 <see cref="CharPreview"/> + <see cref="CardSheetParts"/> 绘制,旧段落在 Task 5(Q20)删除;
    /// 剩下的 <see cref="Section"/> 只给卡组右栏的卡池概览用。</summary>
    public static class CharSheetSections
    {
        /// <summary>一个小节:标题 + 一条横线,下面是一个竖排容器(返回值)。</summary>
        public static Transform Section(Transform parent, string title)
        {
            var head = Ui.Row(parent, "SectionHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Sized(head, 0, 44, flexWidth: 1);
            Ui.ThemedLabel(head.transform, title, 19, Theme.LockGray);
            var rule = Ui.Panel(head.transform, "Rule");
            rule.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Sized(rule, 0, 2, flexWidth: 1);

            var stack = Ui.VStack(parent, "Section", 11);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            // ⚠ 只给弹性宽,**不要**设 preferredHeight —— LayoutElement 的 layoutPriority 压过
            // 布局组自己算出来的高,写个 0 会把整节压没(而且是静默的:节点还在,高度是 0)
            Ui.Sized(stack, flexWidth: 1);
            return stack.transform;
        }
    }
}
