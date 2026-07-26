using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>带种子的 RNG(架构硬规则:Core 内随机一律走此类,禁用 UnityEngine.Random)。
    /// 用 xorshift32 而非 System.Random:内部状态就是一个 uint,能存进档、也能从档里接着摇 ——
    /// 断点续爬要求挂起再进的随机流与不中断时完全一致(2026-07-27)。</summary>
    public sealed class GameRandom
    {
        private uint _state;

        public GameRandom(int seed) => _state = Seed(Scramble(unchecked((uint)seed)));

        private GameRandom(uint state) => _state = Seed(state); // 已在流中的状态,不再打散

        /// <summary>从存档里的状态恢复,接着原来的流摇下去。</summary>
        public static GameRandom FromState(uint state) => new(state);

        /// <summary>当前内部状态:存进档,读档时交给 <see cref="FromState"/>。</summary>
        public uint State => _state;

        /// <summary>xorshift 的 0 是吸收态(此后永远吐 0),换成一个非零常数。</summary>
        private static uint Seed(uint value) => value == 0 ? 0x9E3779B9u : value;

        /// <summary>种子打散(SplitMix32 finalizer):xorshift 拿 1、2、3 这类低熵种子起步时,
        /// 头几个输出彼此高度相关(同一 Boss 连开 20 个种子会摇出同一结果),先混一道再用。</summary>
        private static uint Scramble(uint x)
        {
            unchecked
            {
                x += 0x9E3779B9u;
                x = (x ^ (x >> 16)) * 0x85EBCA6Bu;
                x = (x ^ (x >> 13)) * 0xC2B2AE35u;
                return x ^ (x >> 16);
            }
        }

        private uint NextBits()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>[0, maxExclusive) 上的均匀整数;maxExclusive ≤ 1 时恒为 0。</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            uint bound = (uint)maxExclusive;
            // 拒绝采样:直接取模会让前 (2^32 mod bound) 个取值多占一份概率
            uint limit = uint.MaxValue - uint.MaxValue % bound;
            uint value;
            do { value = NextBits(); } while (value >= limit);
            return (int)(value % bound);
        }

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
    }
}
