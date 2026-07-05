using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>配方图谱(DAG):全部字与部件的定义集合(第 4 章 4.2.3)。</summary>
    public sealed class RecipeGraph
    {
        private readonly Dictionary<string, CharDef> _defs = new();

        public RecipeGraph(IEnumerable<CharDef> defs)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            foreach (var def in defs)
                _defs.Add(def.Id, def);
        }

        public IReadOnlyCollection<CharDef> All => _defs.Values;

        public CharDef Get(string id) => _defs[id];

        public bool TryGet(string id, out CharDef def) => _defs.TryGetValue(id, out def);

        /// <summary>配方原料的属性集合(去重,用于相生判定;中性原料忽略)。</summary>
        public IReadOnlyCollection<Element> RecipeElements(string id)
        {
            var elements = new HashSet<Element>();
            foreach (var ingredientId in Get(id).Recipe)
            {
                if (TryGet(ingredientId, out var ingredient) && ingredient.Element is { } element)
                    elements.Add(element);
            }
            return elements;
        }
    }
}
