using Newtonsoft.Json.Linq;

namespace Brushblade.Data
{
    /// <summary>时间校准的纯逻辑(19.9):解析校时响应、计算设备时钟偏移。
    /// 网络请求在表现层;正式服务端就绪前用公共校时 API 顶替。</summary>
    public static class TimeSync
    {
        /// <summary>解析校时响应 JSON(兼容 worldtimeapi 的 unixtime 字段)。</summary>
        public static bool TryParseUnixTime(string json, out long unixSeconds)
        {
            unixSeconds = 0;
            if (string.IsNullOrEmpty(json))
                return false;
            try
            {
                var token = JObject.Parse(json)["unixtime"];
                if (token == null || token.Type != JTokenType.Integer)
                    return false;
                unixSeconds = token.Value<long>();
                return true;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        /// <summary>服务器时间 − 设备时间 = 偏移(设备快则为负)。</summary>
        public static long ComputeOffset(long serverUnixSeconds, long deviceUnixSeconds)
            => serverUnixSeconds - deviceUnixSeconds;
    }
}
