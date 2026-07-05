using Brushblade.Core;
using Newtonsoft.Json;

namespace Brushblade.Data
{
    /// <summary>存档序列化(纯字符串进出;文件 IO 与防篡改校验由上层负责,19.9)。</summary>
    public static class SaveSerializer
    {
        public static string ToJson(MetaState meta) => JsonConvert.SerializeObject(meta);

        /// <summary>解析存档;null/空/损坏返回全新状态(容错优先,不让玩家卡死)。</summary>
        public static MetaState FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new MetaState();
            try
            {
                return JsonConvert.DeserializeObject<MetaState>(json) ?? new MetaState();
            }
            catch (JsonException)
            {
                return new MetaState();
            }
        }
    }
}
