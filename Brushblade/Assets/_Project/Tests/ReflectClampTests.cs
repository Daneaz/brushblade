using System;
using NUnit.Framework;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>反伤总量钳位 60%(2026-09-05,平衡重做 P0 任务 6)。
    ///
    /// 兑现 BattleEngine.cs:3335 的既有警告:此前刻意不钳位,理由是「字表只有一个
    /// Reflect 字,多来源叠加现实不可达」。P2 让 壁(绿 30%)与 圭(金 50%)同时存在,
    /// 叠加 80% —— 那条前提失效了。
    ///
    /// 60 不是拍的:30 层一轮敌方总伤 936,936 × 60% = 562,约等于红档单攻锚点 600。
    /// 即「站着挨满一整轮」的反伤收益 ≈ 一张红档输出字,再高站桩流的输出就会超过
    /// 专职输出系(设计稿 §1.6)。</summary>
    public sealed class ReflectClampTests
    {
        [Test]
        public void Reflect_TwoSources_ClampedToSixtyPercent()
        {
            Assert.That(BounceFrom(30, 50), Is.EqualTo(60), "合 80% 被钳到 60%,不是 80");
        }

        [Test]
        public void Reflect_UnderCap_NotClamped()
        {
            Assert.That(BounceFrom(30), Is.EqualTo(30), "没到上限就原样反");
        }

        [Test]
        public void Reflect_Zero_BouncesNothing()
        {
            Assert.That(BounceFrom(), Is.EqualTo(0), "0 层不反 —— 恒等");
        }

        /// <summary>挂上给定百分比的反伤,让敌人打玩家一记,返回敌人掉了多少血。
        /// 敌人攻击 100、血 100000(打不死),玩家 MaxHp 500 且不出护盾 ——
        /// 反伤按「打过来的总伤害」算,不掺护盾吸收。</summary>
        private static int BounceFrom(params int[] reflectPercents)
        {
            var chars = new System.Collections.Generic.List<CharDef>();
            var library = new System.Collections.Generic.List<string>();
            for (int i = 0; i < reflectPercents.Length; i++)
            {
                if (reflectPercents[i] <= 0) continue;
                string id = $"映{i}";
                chars.Add(RebalanceFixture.Char(id,
                    new EffectDef(EffectKind.Reflect, reflectPercents[i], turns: 5)));
                library.Add(id);
            }
            var battle = RebalanceFixture.Battle(
                RebalanceFixture.Graph(chars.ToArray()), library,
                RebalanceFixture.Mob(attack: 100));
            foreach (var id in library) battle.Cast(id, 0);
            int before = battle.Enemies[0].Hp;
            battle.EndTurn();
            return before - battle.Enemies[0].Hp;
        }
    }
}
