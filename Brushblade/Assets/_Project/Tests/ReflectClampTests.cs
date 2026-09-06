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

        // ---- 荆棘纳入同一份 60%(2026-09-06,用户裁定) ----
        //
        // 上面几条测的是玩家直接挨打那条管道(DamagePlayerDirect,只有 Reflect,没有荆棘)。
        // 下面这组测打召唤物那条管道(DamageSummon):那里荆棘(Thorns)与反弹(Reflect,
        // 玩家/召唤物两边来源都算)是两次独立弹射,此前只钳了 Reflect,漏了 Thorns ——
        // 「玩家壁 + 召唤物壁(钳到 60)+ 荆棘 50%」这条管道仍能反弹 > 100%。
        // 分配规则「荆棘先扣满,反弹拿剩余」:见 BattleEngine.DamageSummon 里的注释。

        [Test]
        public void Thorns_Alone_ClampedToSixtyPercent()
        {
            Assert.That(SummonBounce(thorns: 80), Is.EqualTo(60),
                "荆棘单独 80% 也要钳到 60%,不是原样反 80");
        }

        [Test]
        public void Thorns_AndReflect_SplitSixtyBudget_ThornsFirst()
        {
            // 荆棘 50 + 玩家反弹 30 + 召唤物反弹 50,合计原始 130。
            // 荆棘先扣满(50 ≤ 60,原样拿到),反弹只剩 60-50=10 的预算,
            // 而反弹原始合计 80 远超 10,所以反弹只反出 10。
            // 若实现错成「各自独立钳到 60」(荆棘不钳 + 反弹钳 60 各自结算),会得到 50+60=110,
            // 与本条断言的 60 判然不同,能分辨这两种结果。
            int bounce = SummonBounce(thorns: 50,
                playerReflectPercents: new[] { 30 }, summonReflectPercents: new[] { 50 });
            Assert.That(bounce, Is.EqualTo(60), "荆棘拿满 50、反弹只拿到 10,合计恰好 60");
        }

        [Test]
        public void Thorns_AndReflect_UnderCap_BothBounceInFull()
        {
            // 荆棘 20 + 反弹 30,合计 50 没到 60 —— 钳位不该误伤这个正常情形,两边原样反。
            int bounce = SummonBounce(thorns: 20, playerReflectPercents: new[] { 30 });
            Assert.That(bounce, Is.EqualTo(50), "没到顶时各自原样反,合计 50");
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

        /// <summary>召一只攻 0、10 万血(打不死)的召唤物顶前排替玩家挨敌人一记(攻 100、心属性,
        /// 无生克无护甲 —— taken 恒等于 100,好算账),按需给它挂荆棘与反弹,返回敌人掉了多少血。
        ///
        /// playerReflectPercents 走玩家的状态袋(默认 allySlot,与 BounceFrom 同一条效果);
        /// summonReflectPercents 用 allySlot: 0 显式扣在召唤物自己的状态袋上 —— 两条袋子在
        /// DamageSummon 里都会被读到、都各出各的份额(见 BattleEngine 那段「两份反弹都算」的注释)。
        /// 两组各自成字、id 不同 —— StatusBag.Apply 同 SourceId 是覆盖刷新不是叠加,同 id 测不出叠加。</summary>
        private static int SummonBounce(int thorns,
            int[] playerReflectPercents = null, int[] summonReflectPercents = null)
        {
            playerReflectPercents ??= Array.Empty<int>();
            summonReflectPercents ??= Array.Empty<int>();

            var chars = new System.Collections.Generic.List<CharDef>
            {
                RebalanceFixture.Char("本"), // 召唤物目标字:纯挂件,不进 library,只给召唤物提供属性
                RebalanceFixture.Char("巫",
                    new EffectDef(EffectKind.Summon, 100000, summonCount: 1, summonAttack: 0,
                        summonChar: "本", passive: new SummonPassive { Thorns = thorns })),
            };
            var library = new System.Collections.Generic.List<string> { "巫" };

            for (int i = 0; i < playerReflectPercents.Length; i++)
            {
                string id = $"甲映{i}";
                chars.Add(RebalanceFixture.Char(id,
                    new EffectDef(EffectKind.Reflect, playerReflectPercents[i], turns: 5)));
                library.Add(id);
            }
            for (int i = 0; i < summonReflectPercents.Length; i++)
            {
                string id = $"乙映{i}";
                chars.Add(RebalanceFixture.Char(id,
                    new EffectDef(EffectKind.Reflect, summonReflectPercents[i], turns: 5)));
                library.Add(id);
            }

            var battle = RebalanceFixture.Battle(
                RebalanceFixture.Graph(chars.ToArray()), library,
                RebalanceFixture.Mob(attack: 100));

            battle.Cast("巫");
            for (int i = 0; i < playerReflectPercents.Length; i++) battle.Cast($"甲映{i}");
            for (int i = 0; i < summonReflectPercents.Length; i++) battle.Cast($"乙映{i}", allySlot: 0);

            int before = battle.Enemies[0].Hp;
            battle.EndTurn();
            return before - battle.Enemies[0].Hp;
        }
    }
}
