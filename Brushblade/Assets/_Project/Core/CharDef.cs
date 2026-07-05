using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>字卡稀有度六档(19.3.1):出厂固定,由人工筛选指定。</summary>
    public enum CardRarity
    {
        White = 1,  // 白
        Green = 2,  // 绿
        Blue = 3,   // 蓝
        Purple = 4, // 紫
        Orange = 5, // 橙
        Red = 6,    // 红
    }

    /// <summary>可玩对象定义:部件与汉字共用(第 4 章 4.2)。部件 = 无配方的叶子。</summary>
    public sealed class CharDef
    {
        /// <summary>唯一标识,即字形("火"、"林"、"焚")。</summary>
        public string Id { get; }

        /// <summary>自身属性;中性部件为 null。</summary>
        public Element? Element { get; }

        /// <summary>配方原料(可为部件或更低阶的字);部件为空数组。</summary>
        public IReadOnlyList<string> Recipe { get; }

        /// <summary>出字消耗 AP:基础 1,高阶 2(第 3 章 3.3)。</summary>
        public int ApCost { get; }

        /// <summary>稀有度(19.3.1,缺省白)。</summary>
        public CardRarity Rarity { get; }

        /// <summary>出字效果;部件的"单独出战"弱效果也在此(第 4 章 4.2.1)。</summary>
        public IReadOnlyList<EffectDef> Effects { get; }

        public bool IsLeaf => Recipe.Count == 0;

        public CharDef(string id, Element? element, IReadOnlyList<string> recipe = null,
            int apCost = 1, IReadOnlyList<EffectDef> effects = null, CardRarity rarity = CardRarity.White)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Element = element;
            Recipe = recipe ?? Array.Empty<string>();
            ApCost = apCost;
            Effects = effects ?? Array.Empty<EffectDef>();
            Rarity = rarity;
        }
    }
}
