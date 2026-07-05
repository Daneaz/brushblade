using System;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>系统 UTC 时间源。
    /// TODO(19.9):接入可信时间(轻量服务端校时)后替换,防离线改表作弊;当前原型接受此风险。</summary>
    public sealed class RealTimeSource : ITimeSource
    {
        public long NowUnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
