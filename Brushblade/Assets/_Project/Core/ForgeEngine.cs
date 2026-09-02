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
        /// <summary>拆:配方原料按性质归位——部件(IsComponent)回部件池,可出牌字回字库
        /// (2026-07-22 拍板:如森=林+木,林回库、木回池;原「全进池」废止)。
        /// 源可以是字库里的字,也可以是部件池里带配方的部件(2026-09-01 二级拆解)。
        /// 任一去处放不下则整体失败,不动状态(先验后扣)。</summary>
        public static ForgeResult TryDismantle(string charId, RecipeGraph graph, ForgeState state,
            int poolCapacity, int libraryCapacity)
        {
            if (!graph.TryGet(charId, out var def))
                return ForgeResult.Fail(ForgeError.UnknownChar, state);
            if (def.IsLeaf)
                return ForgeResult.Fail(ForgeError.NotDismantlable, state);

            // 来源:字库里的字,或**部件池里带配方的部件**(2026-09-01 二级拆解:烝 = 丞 + 灬)。
            // 两者都不在就是 NotInLibrary —— 错误名沿用旧的,含义扩成「手上没有这个东西」。
            bool fromLibrary = state.Library.Contains(charId);
            if (!fromLibrary && !state.Pool.Contains(charId))
                return ForgeResult.Fail(ForgeError.NotInLibrary, state);

            // 归位按原料**自身的性质**,与拆的来源无关:部件回池、可出牌字回字库
            // (2026-07-22 拍板,如 森 = 林 + 木,林 回库、木 回池)。
            var toPool = new List<string>();
            var toLibrary = new List<string>();
            foreach (var ingredient in def.Recipe)
                (graph.TryGet(ingredient, out var idef) && !idef.IsComponent ? toLibrary : toPool).Add(ingredient);

            // 容量先验后扣,任一去处放不下则整体失败、不动状态。
            // 源先移除腾出 1 位:拆字库的字腾字库位,拆池里的部件腾池位。
            int poolAfter = state.Pool.Count + toPool.Count - (fromLibrary ? 0 : 1);
            if (poolAfter > poolCapacity)
                return ForgeResult.Fail(ForgeError.PoolWouldOverflow, state);
            int libraryAfter = state.Library.Count + toLibrary.Count - (fromLibrary ? 1 : 0);
            if (libraryAfter > libraryCapacity)
                return ForgeResult.Fail(ForgeError.LibraryFull, state);

            var library = new List<string>(state.Library);
            var pool = new List<string>(state.Pool);
            if (fromLibrary) library.Remove(charId);
            else pool.Remove(charId);
            library.AddRange(toLibrary);
            pool.AddRange(toPool);
            return ForgeResult.Ok(new ForgeState(library, pool));
        }

        /// <summary>可合成集(2026-09-03):出阵列表 ∪ 这些字**配方原料的递归闭包**。
        /// unlockedChars 为 null(不限)时原样返回 null。
        ///
        /// 为什么要闭包:出阵列表管的是「哪些字算你的牌」,而拆解会把它们变成中间产物 ——
        /// 蕉 拆出 焦,焦 再拆出 隹+灬,可 焦 本身从来不在出阵列表里,于是
        /// 隹+灬 合不回 焦,拆解成了一条不可逆的单行道(用户 2026-09-03 报的 bug)。
        /// 闭包正好等于「凡是你能拆出来的,就能拆回去」,不会凭空多出与你卡组无关的字。
        ///
        /// 调用方(BattleEngine)算一次就够,<see cref="TryCompose"/> 与 <see cref="Suggest"/>
        /// 本身仍只认「允许集」这一个概念,不在里面做图遍历。</summary>
        public static IReadOnlyCollection<string> ComposableSet(RecipeGraph graph,
            IReadOnlyCollection<string> unlockedChars)
        {
            if (unlockedChars == null) return null;
            var set = new HashSet<string>(unlockedChars);
            var pending = new Stack<string>(unlockedChars);
            while (pending.Count > 0)
            {
                if (!graph.TryGet(pending.Pop(), out var def)) continue;
                foreach (var ingredient in def.Recipe)
                    if (set.Add(ingredient)) pending.Push(ingredient);
            }
            return set;
        }

        /// <summary>合:消耗配方全部原料 → 产物按**自身性质**归位(部件回池、可出牌字回字库,
        /// 与 <see cref="TryDismantle"/> 的归位规则同一条)。原料优先取部件池,池中没有则消耗
        /// 字库中的低阶字(4.2.3「原料可以是更低阶的汉字」,3.9 战例:合林 → 合焚)。
        /// unlockedChars 非空时只能合其中的字(2026-07-20 拍板:注入出阵列表,没编入就合不出来);
        /// null = 不限。真实注入的是 <see cref="ComposableSet"/> 算出的闭包,不是裸出阵列表。
        ///
        /// poolCapacity 只在**产物是部件**(烝 = 丞 + 灬 这类带配方的部件)时才用得上,
        /// 故给了 int.MaxValue 缺省供不涉及这一支的既有调用与测试沿用;真实调用方
        /// (BattleEngine.Compose)恒传真值。</summary>
        public static ForgeResult TryCompose(string charId, RecipeGraph graph, ForgeState state, int libraryCapacity,
            IReadOnlyCollection<string> unlockedChars = null, int poolCapacity = int.MaxValue)
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
            if (def.IsComponent)
            {
                if (pool.Count >= poolCapacity)
                    return ForgeResult.Fail(ForgeError.PoolWouldOverflow, state);
                pool.Add(charId);
            }
            else
            {
                if (library.Count >= libraryCapacity)
                    return ForgeResult.Fail(ForgeError.LibraryFull, state);
                library.Add(charId);
            }
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
                    // 与 TryCompose 同口径(2026-08-15):同系部件可替代。
                    // Suggest 只有一份合并列表,故 pool/library 两个参数传同一个 ——
                    // RemoveIngredient 里第二次 Remove 必然落空,行为等价于"只在这一份里找"。
                    if (!RemoveIngredient(remaining, remaining, ingredient))
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
