using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>可玩对象定义:部件与汉字共用(第 4 章 4.2)。部件 = 无配方的叶子。</summary>
    public sealed class CharDef
    {
        /// <summary>唯一标识,即字形("火"、"林"、"焚")。</summary>
        public string Id { get; }

        /// <summary>自身属性;中性部件为 null。</summary>
        public Element? Element { get; }

        /// <summary>配方原料(可为部件或更低阶的字);部件为空数组。</summary>
        public IReadOnlyList<string> Recipe { get; }

        public bool IsLeaf => Recipe.Count == 0;

        public CharDef(string id, Element? element, IReadOnlyList<string> recipe = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Element = element;
            Recipe = recipe ?? Array.Empty<string>();
        }
    }
}
