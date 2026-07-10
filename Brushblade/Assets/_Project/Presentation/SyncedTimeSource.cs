using System;
using System.Collections;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace Brushblade.Presentation
{
    /// <summary>可信时间源(19.9):设备 UTC + 校时偏移。未同步前退化为设备时间。
    /// 正式轻服务端就绪前用公共校时 API;届时只换 URL。</summary>
    public sealed class SyncedTimeSource : ITimeSource
    {
        public bool Synced { get; private set; }
        private long _offsetSeconds;

        public long NowUnixSeconds =>
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _offsetSeconds;

        public void ApplySync(long serverUnixSeconds)
        {
            _offsetSeconds = TimeSync.ComputeOffset(
                serverUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Synced = true;
        }
    }

    /// <summary>启动校时:请求校时端点,成功即写入偏移;失败重试 3 次后放弃(容忍离线)。</summary>
    public sealed class TimeSyncFetcher : MonoBehaviour
    {
        private const string Endpoint = "https://worldtimeapi.org/api/timezone/Etc/UTC";

        public void Begin(SyncedTimeSource target) => StartCoroutine(Fetch(target));

        private static IEnumerator Fetch(SyncedTimeSource target)
        {
            for (int attempt = 0; attempt < 3 && !target.Synced; attempt++)
            {
                using var request = UnityWebRequest.Get(Endpoint);
                request.timeout = 8;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success &&
                    TimeSync.TryParseUnixTime(request.downloadHandler.text, out var unix))
                {
                    target.ApplySync(unix);
                    Debug.Log($"[TimeSync] 校时成功,偏移已应用(第 {attempt + 1} 次尝试)");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(2f * (attempt + 1));
            }
            if (!target.Synced)
                Debug.LogWarning("[TimeSync] 校时失败,本次会话使用设备时间(离线容忍)");
        }
    }
}
