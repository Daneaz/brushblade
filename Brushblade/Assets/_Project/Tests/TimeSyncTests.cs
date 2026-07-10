using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>可信时间纯逻辑(19.9):响应解析与偏移计算。</summary>
    public class TimeSyncTests
    {
        [Test]
        public void ParsesWorldTimeApiResponse()
        {
            const string json = @"{""abbreviation"":""UTC"",""unixtime"":1767225600,""utc_offset"":""+00:00""}";
            Assert.That(TimeSync.TryParseUnixTime(json, out var unix), Is.True);
            Assert.That(unix, Is.EqualTo(1767225600L));
        }

        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase(null)]
        [TestCase(@"{""unixtime"":""abc""}")]
        public void GarbageResponse_Rejected(string json)
        {
            Assert.That(TimeSync.TryParseUnixTime(json, out _), Is.False);
        }

        [Test]
        public void Offset_PositiveWhenDeviceBehind()
        {
            Assert.That(TimeSync.ComputeOffset(serverUnixSeconds: 1000, deviceUnixSeconds: 900), Is.EqualTo(100));
        }

        [Test]
        public void Offset_NegativeWhenDeviceAhead() // 玩家把表调快:偏移为负,校正回真实时间
        {
            Assert.That(TimeSync.ComputeOffset(serverUnixSeconds: 1000, deviceUnixSeconds: 90000), Is.EqualTo(-89000));
        }
    }
}
