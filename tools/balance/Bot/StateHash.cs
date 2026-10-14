using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>局面键(转置表 / 偏离判定):对 <see cref="BattleSnapshot"/> 整棵对象图做结构化 64 位哈希。
    /// 遍历口径与 Newtonsoft 序列化同形(公开可读属性 + 公开字段,按名字排序;列表带长度;字典按键排序;null 带标记),
    /// 但不分配字符串。剔除随机流状态与状态序号(<see cref="Ignored"/>):同一对局状态经不同路径到达时它们不同,
    /// 本体与根副本的随机流也本来就不同(spec §2.4)。
    /// 冲突防线:测试对一批真实局面断言「键相等 ⇔ 去掉同样字段后的 JSON 相等」。</summary>
    internal static class StateHash
    {
        internal static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(BattleSnapshot.RandomState), nameof(BattleSnapshot.TargetRandomState),
            nameof(BattleSnapshot.TraitRandomState), nameof(BattleSnapshot.StatusSerial),
            nameof(StatusEffect.SourceId),
        };

        private static readonly ConcurrentDictionary<Type, Func<object, object>[]> Accessors =
            new ConcurrentDictionary<Type, Func<object, object>[]>();

        public static ulong Of(BattleEngine b) => Of(b.Capture());

        public static ulong Of(BattleSnapshot s)
        {
            ulong h = 0xCBF29CE484222325UL;
            Walk(s, ref h);
            return h;
        }

        private static void Add(ref ulong h, ulong v)
        {
            unchecked
            {
                h = (h ^ v) * 0x100000001B3UL;
                h ^= h >> 29;
                h *= 0xBF58476D1CE4E5B9UL;
            }
        }

        private static void Walk(object o, ref ulong h)
        {
            switch (o)
            {
                case null: Add(ref h, 0x6E756C6CUL); return;
                case string str:
                    Add(ref h, 0x5354UL ^ ((ulong)str.Length << 16));
                    foreach (char ch in str) Add(ref h, ch);
                    return;
                case int i: Add(ref h, unchecked((ulong)(long)i)); return;
                case uint u: Add(ref h, u); return;
                case long l: Add(ref h, unchecked((ulong)l)); return;
                case ulong ul: Add(ref h, ul); return;
                case bool bo: Add(ref h, bo ? 0xB1UL : 0xB0UL); return;
                case Enum e: Add(ref h, unchecked((ulong)Convert.ToInt64(e))); return;
                case double d: Add(ref h, unchecked((ulong)BitConverter.DoubleToInt64Bits(d))); return;
                case float f: Add(ref h, unchecked((ulong)BitConverter.DoubleToInt64Bits(f))); return;
                case IDictionary dict:
                {
                    Add(ref h, 0xD1C7UL ^ ((ulong)dict.Count << 16));
                    var keys = new List<object>(dict.Count);
                    foreach (var k in dict.Keys) keys.Add(k);
                    keys.Sort((a, b) => string.CompareOrdinal(a?.ToString(), b?.ToString()));
                    foreach (var k in keys) { Walk(k, ref h); Walk(dict[k], ref h); }
                    return;
                }
                case IEnumerable seq:
                {
                    ulong n = 0;
                    foreach (var item in seq) { Walk(item, ref h); n++; }
                    Add(ref h, 0x5E0UL ^ (n << 16));
                    return;
                }
            }
            var type = o.GetType();
            if (type.IsPrimitive) { Add(ref h, unchecked((ulong)Convert.ToInt64(o))); return; }
            Add(ref h, 0x0B1EUL);
            foreach (var get in Accessors.GetOrAdd(type, Build)) Walk(get(o), ref h);
        }

        private static Func<object, object>[] Build(Type type)
        {
            var members = new List<MemberInfo>();
            members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0));
            members.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            return members
                .Where(m => !Ignored.Contains(m.Name))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m =>
                {
                    var p = Expression.Parameter(typeof(object));
                    var body = Expression.Convert(Expression.PropertyOrField(Expression.Convert(p, type), m.Name), typeof(object));
                    return Expression.Lambda<Func<object, object>>(body, p).Compile();
                })
                .ToArray();
        }
    }
}
