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
        public int InkCost { get; set; }                      // 墨锭消费(字摊类;余额不足不可选)
        public string GainChar { get; set; }                  // 获得字入关内字库
        public IReadOnlyList<string> GainComponents { get; set; } = Array.Empty<string>(); // 部件入池
    }

    /// <summary>奇遇事件(9.6:短情境 + 2~4 选择,run 内非战斗节点)。</summary>
    public sealed class EventDef
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public IReadOnlyList<EventOption> Options { get; set; }
    }
}
