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
        // 局内血量上限增减(2026-08-04):按**当前**有效上限的百分比复利叠加,正数同步等量回血。
        // 深层怪物 scale 无上限而 Meta.MaxHpFor 硬顶 100,靠这个在关内把上限顶上去。
        public int MaxHpPercent { get; set; }
        // >0 = MaxHpPercent 按此概率生效,**掷空则反向扣同样百分比**(不同于 InkChancePercent 的「不中即无」)
        public int MaxHpChancePercent { get; set; }
    }

    /// <summary>奇遇事件(9.6:短情境 + 2~4 选择,run 内非战斗节点)。</summary>
    public sealed class EventDef
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public IReadOnlyList<EventOption> Options { get; set; }
    }
}
