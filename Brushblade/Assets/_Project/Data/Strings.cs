using System.Collections.Generic;
using Newtonsoft.Json;

namespace Brushblade.Data
{
    /// <summary>玩家可见 UI 文案表(architecture.md §5 的 i18n)。
    /// key → 文本,扁平一层;表内容由上层注入(Data 禁引 UnityEngine,路径不在这里)。
    ///
    /// **边界**(architecture.md:89):进表的是按钮/提示/系统消息;字卡的字形/拼音/释义、
    /// 敌人名(它同时是 enemies.json 的主键与美术 slug 的查表键)、Core/Data 的配置异常
    /// 消息都**不进表** —— 判据是「这句话错了,是玩家看到错字,还是开发者看到报错」。</summary>
    public static class Strings
    {
        private static Dictionary<string, string> _table = new Dictionary<string, string>();

        /// <summary>注入表内容。重复调用**整个替换**而不是合并 —— 换语言即换文件。
        /// JSON 坏了直接抛,与 ConfigLoader 同口径的 fail fast。</summary>
        public static void Load(string json)
        {
            _table = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                     ?? new Dictionary<string, string>();
        }

        public static IReadOnlyCollection<string> Keys => _table.Keys;

        public static int Count => _table.Count;

        /// <summary>取文案。**缺 key 返回 `?key?` 而不抛异常** —— UI 不该因为漏了一句话
        /// 整屏白掉(与 BattleView 那条「白屏的代价 vs 画错一只怪的代价不对称」同型)。
        /// 真正的防线是 StringsTableTests 的对账:漏 key 在工装里毫秒级变红,活不到运行期。</summary>
        public static string T(string key)
            => _table.TryGetValue(key, out var text) ? text : "?" + key + "?";

        /// <summary>取文案并填命名占位符:`T("effect.morale", ("stacks", 3))` 把 `{stacks}` 换成 3。
        ///
        /// 用命名而不是 `{0}`/`{1}`:中文「战意+3层(每层攻击+10)」译成英文语序会变,
        /// 位置占位符届时会**静默插错值**,而读表的人也看不出 `{0}` 是什么。
        ///
        /// 漏传的占位符原样留在文本里(不抛)——同样交给对账测试拦。</summary>
        public static string T(string key, params (string name, object value)[] args)
        {
            var text = T(key);
            if (args == null) return text;
            foreach (var (name, value) in args)
                text = text.Replace("{" + name + "}", value?.ToString() ?? "");
            return text;
        }
    }
}
