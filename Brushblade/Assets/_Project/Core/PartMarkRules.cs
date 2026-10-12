using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>一条拆字印记的只读视图(Plan E3,部件池「印」角标与部件详情用)。
    /// <see cref="Trait"/> 是来源字上挂印记的那条字形特性。</summary>
    public readonly struct PartMarkView
    {
        public readonly string PartChar;
        public readonly int Remaining;
        public readonly string SourceCharId;
        public readonly TraitDef Trait;

        public PartMarkView(string partChar, int remaining, string sourceCharId, TraitDef trait)
        {
            PartChar = partChar;
            Remaining = remaining;
            SourceCharId = sourceCharId;
            Trait = trait;
        }
    }

    /// <summary>部件池「印」角标规则(PartsAndGraft 稿:角标个数 = 剩余次数)。纯函数,不碰引擎。</summary>
    public static class PartMarkRules
    {
        /// <summary>池里第 i 张是否画「印」:同字部件按出现顺序,前 charges(partId) 张画;charges 大于池中同字张数时截断。
        /// 每个字只查一次 <paramref name="charges"/>(引擎读口每次遍历印记表)。</summary>
        public static bool[] Badges(IReadOnlyList<string> pool, Func<string, int> charges)
        {
            var badges = new bool[pool.Count];
            var left = new Dictionary<string, int>();
            for (int i = 0; i < pool.Count; i++)
            {
                string id = pool[i];
                if (!left.TryGetValue(id, out int n)) n = charges(id);
                badges[i] = n > 0;
                left[id] = n - 1;
            }
            return badges;
        }
    }
}
