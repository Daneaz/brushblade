using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>带种子的 RNG(架构硬规则:Core 内随机一律走此类,禁用 UnityEngine.Random)。</summary>
    public sealed class GameRandom
    {
        private readonly Random _random;

        public GameRandom(int seed) => _random = new Random(seed);

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
    }
}
