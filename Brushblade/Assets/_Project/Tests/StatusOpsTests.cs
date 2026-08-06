using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>状态操作族(2026-08-06,子项目 A):驱散/净化/免疫/斩杀/复活,
    /// 以及配套的玩家侧减益(封字 / 玩家灼烧)。
    /// 规格见 docs/superpowers/specs/2026-08-06-状态操作族-design.md。</summary>
    public class StatusOpsTests
    {
        // 出字库:每个字只带一种待测机制,敌人一律用「心」属性避开生克干扰。
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("素", Element.Wood,   // 无被动的基准召唤(10 血 / 攻 3)
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            new CharDef("扫", Element.Heart,   // 灭:纯驱散全部(单体)
                effects: new[] { new EffectDef(EffectKind.Dispel, -1) }),
            new CharDef("剐", Element.Heart,   // 削:伤 9 + 驱散 1 条
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 9),
                                 new EffectDef(EffectKind.Dispel, 1) }),
            new CharDef("荡", Element.Heart,   // 淡:全体伤 + 全体各驱散 1 条
                effects: new[] { new EffectDef(EffectKind.DamageAll, 5),
                                 new EffectDef(EffectKind.Dispel, 1, targetAll: true) }),
            new CharDef("涤", Element.Heart,   // 浴:纯净化
                effects: new[] { new EffectDef(EffectKind.Cleanse, 0) }),
            new CharDef("堵", Element.Heart,   // 塞:免疫 1 次
                effects: new[] { new EffectDef(EffectKind.Immunity, 1) }),
            new CharDef("绝", Element.Heart,   // 杜:免疫 2 次
                effects: new[] { new EffectDef(EffectKind.Immunity, 2) }),
            new CharDef("峙", Element.Heart,   // 岿:免疫 1 次 + 立即净化
                effects: new[] { new EffectDef(EffectKind.Immunity, 1),
                                 new EffectDef(EffectKind.Cleanse, 0) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int? startingHp = null) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, Array.Empty<string>(), enemies, seed: 1, startingHp: startingHp);

        private static EnemyDef Dummy(int hp = 200, int attack = 0) => new("靶", Element.Heart, hp, attack);

        /// <summary>带倾覆大招的 Boss。BossChargeEvery = 1 → 第一个敌方回合蓄力,第二个释放。</summary>
        private static EnemyDef ToppleBoss(int attack = 4) =>
            new("覆", Element.Heart, 300, attack,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, attack, skill: BossSkill.Topple) });

        private static BattleConfig BossConfig() =>
            new() { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };

        // ---- 封字:倾覆的 AP 惩罚 ----

        [Test]
        public void Topple_AppliesSealStatus_NotABareField()
        {
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            engine.EndTurn();  // 蓄力
            engine.EndTurn();  // 释放倾覆
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.True, "倾覆应挂上封字");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Seal), Is.EqualTo(1));
        }

        [Test]
        public void Seal_SurvivesTheSameEndTurnAndCutsNextTurnAp()
        {
            // TurnsLeft 必须填 2:倾覆在敌方段挂上,而同一个 EndTurn 的「状态回合递减」排在
            // StartTurn 之前。填 1 会被当场减到 0 移除,StartTurn 读到 0 —— 效果凭空消失。
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            int fullAp = engine.Ap;
            engine.EndTurn();  // 蓄力
            Assert.That(engine.Ap, Is.EqualTo(fullAp), "蓄力回合不该扣 AP");
            engine.EndTurn();  // 释放倾覆 → 本次 StartTurn 就该少 1 点
            Assert.That(engine.Ap, Is.EqualTo(fullAp - 1));
        }

        [Test]
        public void Seal_ExpiresAfterExactlyOnePenalizedTurn()
        {
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            int fullAp = engine.Ap;
            engine.EndTurn();  // 蓄力
            engine.EndTurn();  // 释放 → AP 少 1
            Assert.That(engine.Ap, Is.EqualTo(fullAp - 1));
            engine.EndTurn();  // 下一轮蓄力 → 封字到期
            Assert.That(engine.Ap, Is.EqualTo(fullAp), "只该罚一个回合");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False);
        }

        [Test]
        public void Seal_SurvivesSaveRoundTrip()
        {
            // 既有 bug:_apPenaltyNextTurn 从来没进过 BattleSnapshot,倾覆后存档续爬白丢惩罚。
            // 状态化后它跟着 PlayerStatuses 一起存。
            var engine = Engine(Array.Empty<string>(), new[] { ToppleBoss() }, BossConfig());
            engine.EndTurn();
            engine.EndTurn();  // 封字已挂
            var snapshot = engine.Capture();
            Assert.That(snapshot.PlayerStatuses.Any(s => s.Kind == StatusKind.Seal), Is.True,
                "封字必须进快照");
        }

        // ---- 玩家灼烧与 Sear ----

        private static EnemyDef Searer(int attack = 3) =>
            new("灯花", Element.Fire, 200, attack, EnemyAbility.Sear);

        [Test]
        public void Sear_AppliesBurnToPlayerOnAttack()
        {
            var engine = Engine(Array.Empty<string>(), new[] { Searer() });
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
        }

        [Test]
        public void PlayerBurn_TicksThenDecays_AndIgnoresWuxing()
        {
            // 玩家没有五行属性,灼烧结算不套任何倍率:1 层 × 系数 2 = 2 伤。
            // 灯花攻 3 → 挂灼烧那一记也打 3。第一回合:挨 3(灼烧还没结算)。
            // 第二回合:先结算 1 层灼烧掉 2、层数降到 0,再挨 3、再挂 1 层。
            var engine = Engine(Array.Empty<string>(), new[] { Searer() });
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(47), "第 1 回合只挨普攻 3");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(42), "第 2 回合:灼烧 2 + 普攻 3");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1),
                "结算 −1 层、攻击 +1 层,净 0");
        }

        [Test]
        public void PlayerBurn_StaysAtOneStack_DoesNotSnowball()
        {
            // 子项目 C 的烓因为「每回合挂 3、只减 1」净 +2 而失控。灯花挂 1 减 1,恒定 1 层。
            var engine = Engine(Array.Empty<string>(), new[] { Searer() }, startingHp: 200,
                config: new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 });
            for (int turn = 0; turn < 5; turn++) engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
        }

        [Test]
        public void PlayerBurn_KillsWithoutSkippingStatusTick()
        {
            // 玩家被灼烧烧到 0 血时不能在灼烧段就早退——本回合的状态回合递减必须照跑,
            // 否则广告复活满血续战后,所有状态都会多续一回合(既有约束,
            // BattleEngine 的「状态回合递减」那段注释守着这条)。
            // 敌人用攻 0 的靶,确保这 2 点伤害只可能来自灼烧。
            var engine = Engine(Array.Empty<string>(), new[] { Dummy() }, startingHp: 2);
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = -1,
            });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 2, SourceId = "倾覆",
            });

            engine.EndTurn();   // 灼烧 1 层 × 系数 2 = 2 伤,正好烧死

            Assert.That(engine.PlayerHp, Is.EqualTo(0));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(engine.PlayerStatuses.Find(StatusKind.Seal)?.TurnsLeft, Is.EqualTo(1),
                "本回合的状态递减不能因为玩家阵亡就整个跳过");
        }

        // ---- 驱散 ----

        /// <summary>给敌人挂两条可驱散的增益(与标点小妖加攻同形:AttackBuff、段内持久、可叠)。</summary>
        private static void GiveTwoBuffs(EnemyState enemy)
        {
            for (int i = 0; i < 2; i++)
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = 3, TurnsLeft = -1, SourceId = $"妖#{i}",
                });
        }

        [Test]
        public void Dispel_MinusOne_RemovesEveryBuff()
        {
            var engine = Engine(new[] { "扫" }, new[] { Dummy() });
            GiveTwoBuffs(engine.Enemies[0]);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(6), "基础 0 + 两条各 3");
            engine.Cast("扫", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(0));
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(0));
        }

        [Test]
        public void Dispel_Counted_RemovesExactlyThatMany()
        {
            var engine = Engine(new[] { "剐" }, new[] { Dummy() });
            GiveTwoBuffs(engine.Enemies[0]);
            engine.Cast("剐", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(3),
                "只清一条,剩一条");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(191), "伤害照打");
        }

        [Test]
        public void Dispel_TargetAll_HitsEveryLivingEnemy()
        {
            var engine = Engine(new[] { "荡" }, new[] { Dummy(), Dummy() });
            GiveTwoBuffs(engine.Enemies[0]);
            GiveTwoBuffs(engine.Enemies[1]);
            engine.Cast("荡", 0);
            foreach (var enemy in engine.Enemies)
                Assert.That(enemy.Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(3),
                    "每只各清一条");
        }

        [Test]
        public void Dispel_LeavesDebuffsAlone()
        {
            // 驱散只打增益。敌人身上的灼烧是减益,不该被自己的驱散字误清
            var engine = Engine(new[] { "扫" }, new[] { Dummy() });
            engine.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 4, TurnsLeft = -1,
            });
            GiveTwoBuffs(engine.Enemies[0]);
            engine.Cast("扫", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(4));
        }

        [Test]
        public void Dispel_DoesNotTouchPlayerBuffs()
        {
            // 玩家自己的减伤/持续治疗也是 Buff 极性,但它们挂在 _playerStatuses 上,
            // 不在驱散的作用域里
            var engine = Engine(new[] { "扫" }, new[] { Dummy() });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DamageReduction, Polarity = StatusPolarity.Buff,
                Magnitude = 30, TurnsLeft = -1, SourceId = "铠",
            });
            engine.Cast("扫", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DamageReduction), Is.EqualTo(30));
        }

        // ---- 净化 ----

        [Test]
        public void Cleanse_RemovesPlayerDebuffs_KeepsBuffs()
        {
            var engine = Engine(new[] { "涤" }, new[] { Dummy() });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 2, SourceId = "倾覆",
            });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 3, TurnsLeft = -1,
            });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.DamageReduction, Polarity = StatusPolarity.Buff,
                Magnitude = 30, TurnsLeft = -1, SourceId = "铠",
            });

            engine.Cast("涤", 0);

            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False);
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Burn), Is.False);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DamageReduction), Is.EqualTo(30),
                "增益不该被净化误伤");
        }

        // ---- 免疫 ----

        [Test]
        public void Immunity_BlocksOneHitEntirely_NotPartially()
        {
            var engine = Engine(new[] { "堵" }, new[] { Dummy(attack: 20) });
            engine.Cast("堵", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "整记 20 伤被完全挡下,不是减免");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Immunity), Is.False, "一次性,用完即消");
        }

        [Test]
        public void Immunity_ConsumedBeforeShield()
        {
            // 免疫是稀缺的一次性资源,让它去挡小伤而把护盾留着更亏。
            // 护盾必须原封不动地留到免疫用完之后。
            var engine = Engine(new[] { "堵" }, new[] { Dummy(attack: 8) });
            engine.Cast("堵", 0);
            int shieldBefore = engine.PlayerShield;
            engine.EndTurn();
            Assert.That(engine.PlayerShield, Is.EqualTo(shieldBefore), "护盾一点没掉");
            Assert.That(engine.PlayerHp, Is.EqualTo(50));
        }

        [Test]
        public void Immunity_TwoChargesBlockTwoHits()
        {
            var engine = Engine(new[] { "绝" }, new[] { Dummy(attack: 9) });
            engine.Cast("绝", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "第 1 记挡下");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1));
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "第 2 记也挡下");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(41), "第 3 记吃满");
        }

        [Test]
        public void Immunity_SameCharRefreshes_DifferentCharsStack()
        {
            var engine = Engine(new[] { "堵", "堵", "绝" }, new[] { Dummy(attack: 5) });
            engine.Cast("堵", 0);
            engine.Cast("堵", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "同字只刷新,不叠成 2");
            engine.Cast("绝", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(3),
                "不同字是不同来源,可叠:1 + 2");
        }

        [Test]
        public void KuiGrantsImmunityAndCleansesAtOnce()
        {
            var engine = Engine(new[] { "峙" }, new[] { Dummy(attack: 20) });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 3, TurnsLeft = -1,
            });
            engine.Cast("峙", 0);
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Burn), Is.False, "净化那一半");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "免疫那一半");
        }

        [Test]
        public void Immunity_DoesNotProtectSummons()
        {
            // 免疫是玩家的资源。召唤物替玩家承伤走 DamageSummon,不经 DamagePlayerDirect
            var engine = Engine(new[] { "堵", "素" }, new[] { Dummy(attack: 4) });
            engine.Cast("素");
            engine.Cast("堵", 0);
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(6), "召唤物照常挨打");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "免疫没被召唤物那记消耗掉");
        }
    }
}
