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
        NotUnlocked,       // 此字不在可合成集(2026-07-20:只能合出阵列表里的字)
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
        /// <summary>拆:配方原料按性质归位——部件(叶子)回部件池,可合成字回字库
        /// (2026-07-22 拍板:如森=林+木,林回库、木回池;原「全进池」废止)。
        /// 任一去处放不下则整体失败,不动状态(先验后扣)。</summary>
        public static ForgeResult TryDismantle(string charId, RecipeGraph graph, ForgeState state,
            int poolCapacity, int libraryCapacity)
        {
            if (!graph.TryGet(charId, out var def))
                return ForgeResult.Fail(ForgeError.UnknownChar, state);
            if (def.IsLeaf)
                return ForgeResult.Fail(ForgeError.NotDismantlable, state);
            if (!state.Library.Contains(charId))
                return ForgeResult.Fail(ForgeError.NotInLibrary, state);

            var toPool = new List<string>();
            var toLibrary = new List<string>();
            foreach (var ingredient in def.Recipe)
                (graph.TryGet(ingredient, out var idef) && !idef.IsLeaf ? toLibrary : toPool).Add(ingredient);

            if (state.Pool.Count + toPool.Count > poolCapacity)
                return ForgeResult.Fail(ForgeError.PoolWouldOverflow, state);
            // 父字先移除腾出 1 位,故字库容量按 −1 判定
            if (state.Library.Count - 1 + toLibrary.Count > libraryCapacity)
                return ForgeResult.Fail(ForgeError.LibraryFull, state);

            var library = new List<string>(state.Library);
            library.Remove(charId);
            library.AddRange(toLibrary);
            var pool = new List<string>(state.Pool);
            pool.AddRange(toPool);
            return ForgeResult.Ok(new ForgeState(library, pool));
        }

        /// <summary>合:消耗配方全部原料 → 字入字库。原料优先取部件池,池中没有则消耗字库中的
        /// 低阶字(4.2.3「原料可以是更低阶的汉字」,3.9 战例:合林 → 合焚)。
        /// unlockedChars 非空时只能合其中的字(2026-07-20 拍板:注入出阵列表,没编入就合不出来);
        /// null = 不限。</summary>
        public static ForgeResult TryCompose(string charId, RecipeGraph graph, ForgeState state, int libraryCapacity,
            IReadOnlyCollection<string> unlockedChars = null)
        {
            if (!graph.TryGet(charId, out var def))
                return ForgeResult.Fail(ForgeError.UnknownChar, state);
            if (unlockedChars != null && !unlockedChars.Contains(charId))
                return ForgeResult.Fail(ForgeError.NotUnlocked, state);

            var pool = new List<string>(state.Pool);
            var library = new List<string>(state.Library);
            foreach (var ingredient in def.Recipe)
            {
                if (!RemoveIngredient(pool, library, ingredient))
                    return ForgeResult.Fail(ForgeError.MissingIngredients, state);
            }

            // 容量在消耗原料之后判定:用字库中的字升阶不占新位
            if (library.Count >= libraryCapacity)
                return ForgeResult.Fail(ForgeError.LibraryFull, state);

            library.Add(charId);
            return ForgeResult.Ok(new ForgeState(library, pool));
        }

        /// <summary>取用一份原料(2026-08-15,部件五系通用 spec §1.3)。四级优先:
        /// 池精确 → 库精确 → 池等价 → 库等价。
        ///
        /// **精确两级排在等价两级之前**是刻意的,不是顺手写的:这样在等价匹配用不上的场合,
        /// 取用顺序与旧实现逐字节相同 —— 既有 982 条测试因此一条都不用改。
        /// 等价只在原本会 MissingIngredients 的分支上多给一条路。
        ///
        /// 组内按 <see cref="ComponentKin"/> 的声明顺序取,保证同一手牌同一结果(可重放)。</summary>
        private static bool RemoveIngredient(List<string> pool, List<string> library, string ingredient)
        {
            if (pool.Remove(ingredient)) return true;
            if (library.Remove(ingredient)) return true;
            if (!ComponentKin.TryGetGroup(ingredient, out var group)) return false;
            foreach (var kin in group)
            {
                if (kin == ingredient) continue; // 精确那两级已经试过
                if (pool.Remove(kin)) return true;
                if (library.Remove(kin)) return true;
            }
            return false;
        }

        /// <summary>提示:可合成的字 + 差一个原料的字。原料 = 部件池 + 字库低阶字(3.9 战例语义)。
        /// unlockedChars 非空时只提示其中的字——合不出来的不该出现在拆合台(2026-07-19)。</summary>
        public static SuggestResult Suggest(RecipeGraph graph, IReadOnlyList<string> pool, IReadOnlyList<string> library,
            IReadOnlyCollection<string> unlockedChars = null)
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
                if (unlockedChars != null && !unlockedChars.Contains(def.Id))
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
