using System;
using System.Collections;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>嵌套迭代器的展平驱动(Plan E4 Task 2)。不碰 UnityEngine,放 Core 只为能被单测覆盖;
    /// 唯一调用方是表现层的 Juice.SafeRun(演出协程的异常兜底)。
    ///
    /// 为什么要它:Unity 里协程 <c>yield return</c> 一个 IEnumerator 时,内层一抛异常,整条链
    /// 直接终止,外层末尾的回调永远不会被调用 —— 等这个回调解锁输入的战斗就永久卡死。
    /// 这里用一个栈自己驱动整条链,每次 MoveNext 都包 try/catch,出错时报告并保证 onDone 恰好调一次。
    ///
    /// 节拍与 Unity 嵌套协程一致:子程在父层吐出它的**同一步**里开跑,子程结束的**同一步**里父层接着跑;
    /// 只有「不展平」的值(null、WaitForSeconds 等 YieldInstruction、谓词排除的 CustomYieldInstruction)
    /// 才原样交出去,由外面(Unity)决定下一步什么时候来。</summary>
    public static class RoutineRunner
    {
        /// <param name="root">要驱动的根迭代器;null 视同空程,第一步就收尾。</param>
        /// <param name="isNested">吐出的值是否当子程展平;null = 凡 IEnumerator 都展平。
        /// 只对 IEnumerator 生效,非 IEnumerator 一律原样交出。</param>
        /// <param name="onError">某一步抛异常时调用(之后整条链放弃,父层不再推进)。</param>
        /// <param name="onDone">正常结束或出错放弃时调用,恰好一次;正常路径上就在最后一步(返回 false 的那次 MoveNext)里调。</param>
        public static IEnumerator Guard(IEnumerator root, Func<object, bool> isNested,
            Action<Exception> onError, Action onDone)
        {
            var stack = new Stack<IEnumerator>();
            if (root != null) stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool moved;
                object current = null;
                Exception error = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception ex)
                {
                    moved = false;
                    error = ex;
                }
                if (error != null)
                {
                    onError?.Invoke(error);
                    break;
                }
                if (!moved)
                {
                    stack.Pop();   // 子程结束:同一步回到父层
                    continue;
                }
                if (current is IEnumerator nested && (isNested == null || isNested(current)))
                {
                    stack.Push(nested);   // 子程:同一步开跑
                    continue;
                }
                yield return current;
            }
            onDone?.Invoke();
        }
    }
}
