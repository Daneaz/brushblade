using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>奇遇选项(9.6.1:各有后果;首批后果类型对齐已实现系统)。</summary>
    public sealed class EventOption
    {
        public string Label { get; set; }
        public int HpDelta { get; set; }                      // 正=治疗(不超上限),负=损伤(至少留 1)
        public int Ink { get; set; }                          // 墨锭收入(run 结束入账)
        public int InkCost { get; set; }                      // 墨锭消费(对赌类;余额不足不可选)
        public int ComponentCost { get; set; }                // 部件抵价(以物易物;池内不足不可选,2026-07-19)
        public string GainChar { get; set; }                  // 获得字入关内字库
        public IReadOnlyList<string> GainComponents { get; set; } = Array.Empty<string>(); // 部件入池
        public int RandomComponents { get; set; }             // 随机部件个数(五行均匀掷,2026-07-19)
        public IReadOnlyList<string> GainCharChoices { get; set; } = Array.Empty<string>(); // 任选一字入库(字摊)
        public int InkChancePercent { get; set; }             // >0 = Ink 按此概率发放(赌注;成本照付)
    }

    /// <summary>奇遇事件(9.6:短情境 + 2~4 选择,run 内非战斗节点)。</summary>
    public sealed class EventDef
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public IReadOnlyList<EventOption> Options { get; set; }
    }
}
