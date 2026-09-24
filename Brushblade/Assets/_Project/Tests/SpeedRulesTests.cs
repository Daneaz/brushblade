using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗演出速度(2026-09-24 用户拍板:免费 ×2 / 订阅 ×3)。
    ///
    /// ⚠️ 这是**行为变更**:长按加速 2026-08-30 就有了,倍率写死 3f。本次把原本白给的
    /// 3 倍速改成 2 倍免费、3 倍进订阅权益。订阅模块尚未实装,所以 subscribed 目前恒为 false,
    /// ×3 这一档是「先把形状定下来」—— 这里照样断言它,免得订阅落地时才发现口径漂了。</summary>
    public class SpeedRulesTests
    {
        private static SettingsState Off() => new() { FastBattle = false };
        private static SettingsState Fixed() => new() { FastBattle = true };

        [Test]
        public void Idle_RunsAtNormalSpeed()
        {
            Assert.That(SpeedRules.RateFor(Off(), holding: false, subscribed: false),
                Is.EqualTo(SpeedRules.NormalRate));
        }

        [Test]
        public void Holding_GivesTheFreeRate()
        {
            Assert.That(SpeedRules.RateFor(Off(), holding: true, subscribed: false),
                Is.EqualTo(2f), "免费档长按 ×2");
        }

        [Test]
        public void Subscribed_GivesTheHigherRate()
        {
            Assert.That(SpeedRules.RateFor(Off(), holding: true, subscribed: true),
                Is.EqualTo(3f), "订阅档 ×3(权益未实装,形状先钉住)");
        }

        [Test]
        public void FixedFastBattle_NeedsNoHolding()
        {
            // 设置里固定开了之后,手不按也是加速 —— 这就是「设置内可以长期固定」那一条
            Assert.That(SpeedRules.RateFor(Fixed(), holding: false, subscribed: false),
                Is.EqualTo(2f));
        }

        [Test]
        public void FixedAndHolding_DoNotStack()
        {
            // 加速是「看过一遍想快点」,不是数值。叠成 ×4 只会糊成一片,
            // 而且松手时会从 ×4 掉到 ×2,观感是「越按越怪」
            Assert.That(SpeedRules.RateFor(Fixed(), holding: true, subscribed: false),
                Is.EqualTo(2f));
            Assert.That(SpeedRules.RateFor(Fixed(), holding: true, subscribed: true),
                Is.EqualTo(3f));
        }

        [Test]
        public void NullSettings_FallsBackToHoldingOnly()
        {
            // GameSettings.Bind 之前就可能有人来问(Juice 比 GameRoot 先跑起来),
            // 这时不该炸,也不该当成「固定加速开着」
            Assert.That(SpeedRules.RateFor(null, holding: false, subscribed: false),
                Is.EqualTo(SpeedRules.NormalRate));
            Assert.That(SpeedRules.RateFor(null, holding: true, subscribed: false),
                Is.EqualTo(2f));
        }

        [Test]
        public void DefaultSettings_AreReadyToPlay()
        {
            // 旧存档没有 Settings 这一项,反序列化后留的就是这份缺省值 ——
            // 音效音乐必须是开的,加速必须是关的(第一次玩要先看清结算)
            var s = new SettingsState();
            Assert.That(s.SfxEnabled, Is.True);
            Assert.That(s.MusicEnabled, Is.True);
            Assert.That(s.FastBattle, Is.False);
        }

        [Test]
        public void NewMetaState_CarriesSettings()
        {
            // Settings 不能是 null:各读取点都直接 .SfxEnabled,判 null 会散到到处都是
            Assert.That(new MetaState().Settings, Is.Not.Null);
        }

        [Test]
        public void Settings_SurviveASaveRoundTrip()
        {
            // 走 SaveSerializer 这个真实入口(CLAUDE.md:测试禁止直接引 Newtonsoft)。
            // 设置存不住的话,玩家每次重进都要重新关一遍音乐 —— 而这不会有任何测试报错,
            // 除非这里断一下
            var meta = new MetaState();
            meta.Settings.FastBattle = true;
            meta.Settings.MusicEnabled = false;
            meta.Settings.SfxEnabled = false;

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            Assert.That(restored.Settings.FastBattle, Is.True);
            Assert.That(restored.Settings.MusicEnabled, Is.False);
            Assert.That(restored.Settings.SfxEnabled, Is.False);
        }

        [Test]
        public void OldSave_WithoutSettings_GetsTheDefaults()
        {
            // 旧存档整个没有 Settings 这一项。反序列化后必须是可用的缺省值,不能是 null
            var restored = Data.SaveSerializer.FromJson("{\"Ink\":42}");
            Assert.That(restored.Settings, Is.Not.Null);
            Assert.That(restored.Settings.SfxEnabled, Is.True);
            Assert.That(restored.Settings.MusicEnabled, Is.True);
            Assert.That(restored.Settings.FastBattle, Is.False);
            Assert.That(restored.Ink, Is.EqualTo(42), "其余字段照常读出来");
        }
    }
}
