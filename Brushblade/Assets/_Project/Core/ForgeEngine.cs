using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>拆合引擎的当前状态:字库 + 部件池(不可变,操作返回新状态)。</summary>
    public sealed class ForgeState
    {
        public IReadOnlyList<string> Library { get; }
        public IReadOnlyList<string> Pool { get; }

        public ForgeState(IReadOnlyList<string> library, IReadOnlyList<string> pool)
        {
            Library = library;
            Pool = pool;
        }
    }

    public enum ForgeError
    {
        None,
        NotInLibrary,      // 字不在字库
        NotDismantlable,   // 独体字/部件不可拆
        PoolWouldOverflow, // 拆解产物会超出部件池容量
        MissingIngredients,// 池中原料不足,无法锻造
        LibraryFull,       // 字库已满
        UnknownChar,       // 图谱中无此字
    }

    public readonly struct ForgeResult
    {
        public bool Success { get; }
        public ForgeError Error { get; }
        public ForgeState State { get; }

        public ForgeResult(bool success, ForgeError error, ForgeState state)
        {
            Success = success;
            Error = error;
            State = state;
        }

        public static ForgeResult Ok(ForgeState state) => new(true, ForgeError.None, state);
        public static ForgeResult Fail(ForgeError error, ForgeState state) => new(false, error, state);
    }

    /// <summary>合字建议(第 4 章 4.7 提示引擎)。</summary>
    public readonly struct NearMiss
    {
        public string CharId { get; }
        public string MissingIngredient { get; }

        public NearMiss(string charId, string missingIngredient)
        {
            CharId = charId;
            MissingIngredient = missingIngredient;
        }
    }

    public readonly struct SuggestResult
    {
        /// <summary>池中原料已完全满足配方的字。</summary>
        public IReadOnlyList<string> Composable { get; }

        /// <summary>还差一个原料即可合成的字。</summary>
        public IReadOnlyList<NearMiss> NearMisses { get; }

        public SuggestResult(IReadOnlyList<string> composable, IReadOnlyList<NearMiss> nearMisses)
        {
            Composable = composable;
            NearMisses = nearMisses;
        }
    }

    /// <summary>拆合引擎:拆(4.4.1)/合(4.4.2)/提示(4.7)。纯函数,无副作用。</summary>
    public static class ForgeEngine
    {
        /// <summary>拆:字库中的字 → 配方全部原料入池(无损返还)。</summary>
        public static ForgeResult TryDismantle(string charId, RecipeGraph graph, ForgeState state, int poolCapacity)
        {
            if (!graph.TryGet(charId, out var def))
                return ForgeResult.Fail(ForgeError.UnknownChar, state);
            if (def.IsLeaf)
                return ForgeResult.Fail(ForgeError.NotDismantlable, state);
            if (!state.Library.Contains(charId))
                return ForgeResult.Fail(ForgeError.NotInLibrary, state);
            if (state.Pool.Count + def.Recipe.Count > poolCapacity)
                return ForgeResult.Fail(ForgeError.PoolWouldOverflow, state);

            var library = new List<string>(state.Library);
            library.Remove(charId);
            var pool = new List<string>(state.Pool);
            pool.AddRange(def.Recipe);
            return ForgeResult.Ok(new ForgeState(library, pool));
        }

        /// <summary>合:消耗配方全部原料 → 字入字库。原料优先取部件池,池中没有则消耗字库中的
        /// 低阶字(4.2.3「原料可以是更低阶的汉字」,3.9 战例:合林 → 合焚)。</summary>
        public static ForgeResult TryCompose(string charId, RecipeGraph graph, ForgeState state, int libraryCapacity)
        {
            if (!graph.TryGet(charId, out var def))
                return ForgeResult.Fail(ForgeError.UnknownChar, state);

            var pool = new List<string>(state.Pool);
            var library = new List<string>(state.Library);
            foreach (var ingredient in def.Recipe)
            {
                if (pool.Remove(ingredient)) continue;
                if (library.Remove(ingredient)) continue;
                return ForgeResult.Fail(ForgeError.MissingIngredients, state);
            }

            // 容量在消耗原料之后判定:用字库中的字升阶不占新位
            if (library.Count >= libraryCapacity)
                return ForgeResult.Fail(ForgeError.LibraryFull, state);

            library.Add(charId);
            return ForgeResult.Ok(new ForgeState(library, pool));
        }

        /// <summary>提示:可合成的字 + 差一个原料的字。原料 = 部件池 + 字库低阶字(3.9 战例语义)。</summary>
        public static SuggestResult Suggest(RecipeGraph graph, IReadOnlyList<string> pool, IReadOnlyList<string> library)
        {
            var composable = new List<string>();
            var nearMisses = new List<NearMiss>();

            var available = new List<string>(pool.Count + library.Count);
            available.AddRange(pool);
            available.AddRange(library);

            foreach (var def in graph.All)
            {
                if (def.IsLeaf)
                    continue;

                var remaining = new List<string>(available);
                var missing = new List<string>();
                foreach (var ingredient in def.Recipe)
                {
                    if (!remaining.Remove(ingredient))
                        missing.Add(ingredient);
                }

                if (missing.Count == 0)
                    composable.Add(def.Id);
                else if (missing.Count == 1)
                    nearMisses.Add(new NearMiss(def.Id, missing[0]));
            }

            return new SuggestResult(composable, nearMisses);
        }
    }
}
