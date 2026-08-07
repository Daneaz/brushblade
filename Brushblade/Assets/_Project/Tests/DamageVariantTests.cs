using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>伤害变体(2026-08-07,子项目 D):命中判定 / 致盲 / 闪避 / 沉默 / 反弹 / 多段。
    /// 规格见 docs/superpowers/specs/2026-08-07-伤害变体-design.md。</summary>
    public class DamageVariantTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 素:无被动的基准召唤(10 血 / 攻 0),用来当承伤靶
            new CharDef("素", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "木") }),
            // 闪:召唤物带 50% 闪避(柳)
            new CharDef("闪", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Dodge = 50 }) }),
            // 昏:单体致盲 100%,2 回合(熣 的极端版,用来做确定性断言)
            new CharDef("昏", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 100, turns: 2) }),
            // 雾:全体致盲 100%,1 回合(烟 的极端版)
            new CharDef("雾", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 100, turns: 1, targetAll: true) }),
            // 挡:护盾 20;御:免疫 1 次 —— 用来验「打空时这两样都不该被消耗」
            new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
            new CharDef("御", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Immunity, 1) }),
            // 凿:1 点伤害,用来精确磨血线 / 制造一次「受击存活」
            new CharDef("凿", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int seed = 1) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, Array.Empty<string>(), enemies, seed);

        private static EnemyDef Attacker(int attack = 8) => new("靶", Element.Heart, 200, attack);

        // ---- 命中判定的确定性两端 ----

        [Test]
        public void NoBlindNoDodge_AlwaysHits()
        {
            var engine = Engine(Array.Empty<string>(), new[] { Attacker() });
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(42), "无致盲无闪避:必中");
        }

        [Test]
        public void FullBlind_AlwaysMisses()
        {
            var engine = Engine(new[] { "昏" }, new[] { Attacker() });
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "致盲 100% → 必空");
        }

        [Test]
        public void Dodge_IsCarriedOntoTheSummon()
        {
            // 闪避字段确实从 EffectDef.Passive 落到了召唤物身上;闪避真的生效由
            // BlindPlusDodge_ClampsHitRateAtZero_NotNegative 那条做确定性断言——闪避 50% 本身
            // 不是确定性的,这里只做接线检查,不为了凑「必空」这个名字硬加断言
            var engine = Engine(new[] { "闪" }, new[] { Attacker() });
            engine.Cast("闪");
            Assert.That(engine.Summons[0].Passive.Dodge, Is.EqualTo(50));
        }

        [Test]
        public void BlindPlusDodge_ClampsHitRateAtZero_NotNegative()
        {
            // 致盲 100 + 闪避 50 = 命中率 −50。若只钳致盲不钳最终值,
            // _random.Next(100) 永远不小于负数,反而变成**必中** —— 正是这条要拦的
            var engine = Engine(new[] { "闪", "昏" }, new[] { Attacker() });
            engine.Cast("闪");
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(10), "召唤物一点没掉血");
        }

        [Test]
        public void BlindAll_HitsEveryEnemy()
        {
            var engine = Engine(new[] { "雾" }, new[] { Attacker(), Attacker() });
            engine.Cast("雾", 0);
            foreach (var enemy in engine.Enemies)
                Assert.That(enemy.Statuses.TotalMagnitude(StatusKind.Blind), Is.EqualTo(100));
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "两只都打空");
        }

        [Test]
        public void Blind_ExpiresAfterItsTurns()
        {
            // turns=2 与既有的 Freeze/Bleed 同口径(Freeze_SkipsEnemyTurnThenResumes:value=1 恰好
            // 挡 1 个敌方回合;Bleed_ExpiresAfterThreeTurns:3 回合恰好持续 3 次 EndTurn)——
            // TurnsLeft 的递减统一挪到 EndTurn 末尾(TickAllStatuses,在敌方攻击之后),所以
            // turns=N 要挡住第 1~N 个敌方回合,第 N+1 个才吃满。
            var engine = Engine(new[] { "昏" }, new[] { Attacker() });
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "第 1 个敌方回合被挡空");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "第 2 个敌方回合仍在 2 回合致盲覆盖内,继续被挡空");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(42), "致盲到期,第 3 个敌方回合吃满");
        }

        [Test]
        public void Miss_EmitsMissedEvent()
        {
            var engine = Engine(new[] { "昏" }, new[] { Attacker() });
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.Missed), Is.True,
                "打空要发事件 —— 没反馈玩家只会以为敌人这回合没动");
        }

        [Test]
        public void Miss_DoesNotConsumeImmunityOrShield()
        {
            // 打空 = 什么都没发生:免疫层数不掉、护盾不掉。免疫是稀缺资源,
            // 被一记本来就打不中的攻击吃掉是最亏的
            var engine = Engine(new[] { "御", "挡", "昏" }, new[] { Attacker(attack: 8) });
            engine.Cast("御", 0);
            engine.Cast("挡", 0);
            engine.Cast("昏", 0);
            int shieldBefore = engine.PlayerShield;
            Assert.That(shieldBefore, Is.GreaterThan(0), "护盾字得真生效了这条才有判别力");
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "免疫层数一点没掉");
            Assert.That(engine.PlayerShield, Is.EqualTo(shieldBefore), "护盾一点没掉");
        }

        [Test]
        public void Blind_DoesNotStopToppleShieldBreak()
        {
            // 刻意的裁定:致盲让「伤害」落空,但掀盾不经 DamagePlayerDirect,打不空。
            // 与免疫同口径(免疫也挡不住掀盾)
            var boss = new EnemyDef("覆", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 4, skill: BossSkill.Topple) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };
            var engine = Engine(new[] { "挡", "昏" }, new[] { boss }, config);
            engine.Cast("挡", 0);
            engine.EndTurn();                       // Boss 蓄力
            engine.Cast("昏", 0);                    // 致盲 100%
            engine.EndTurn();                       // 释放倾覆
            Assert.That(engine.PlayerShield, Is.EqualTo(0), "掀盾照常发生");
            Assert.That(engine.PlayerHp, Is.EqualTo(200), "但伤害那一记被打空");
        }

        [Test]
        public void Blind_DoesNotStopDevour()
        {
            // 吞噬直接置 0 血、不经 DamageSummon —— 「无视血量必杀」不受命中判定影响
            var boss = new EnemyDef("噬", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 300, 4, skill: BossSkill.Devour) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };
            var engine = Engine(new[] { "素", "昏" }, new[] { boss }, config);
            engine.Cast("素");
            engine.EndTurn();                       // Boss 蓄力
            engine.Cast("昏", 0);
            engine.EndTurn();                       // 释放吞噬
            Assert.That(engine.Summons[0].Alive, Is.False, "秒杀打不空");
        }

        [Test]
        public void Blind_NeedsNoExplicitTarget_WhenSoleEnemyAlive()
        {
            // NeedsTarget 白名单漏掉单体效果会让 targetIndex 停在 -1,ApplyEffects 里
            // _enemies[-1] 直接越界崩溃(子项目 A 的 Dispel 就栽在这——Step 7 的教训)。
            // 场上仅一个存活敌人时,Cast 不传目标下标也得能自动锁定,不抛异常且致盲挂上
            var engine = Engine(new[] { "昏" }, new[] { Attacker() });
            Assert.DoesNotThrow(() => engine.Cast("昏"));
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Blind), Is.EqualTo(100));
        }

        [Test]
        public void Dodge_SurvivesSaveRoundTrip()
        {
            // 闪避走 SummonPassive 的存档路径(子项目 C 已铺好 Clone + Capture/Restore),
            // 这条钉住新字段真的跟着走了 —— 漏进 Clone() 的话跨战斗就白闪了
            var engine = Engine(new[] { "闪" }, new[] { Attacker(attack: 0) });
            engine.Cast("闪");

            var meta = new MetaState { Endless = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 } };
            foreach (var summon in engine.Summons) meta.Endless.CarriedSummons.Add(summon.Capture());
            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            var revived = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Attacker(attack: 0) }, seed: 1,
                startingSummons: restored.Endless.CarriedSummons);
            Assert.That(revived.Summons[0].Passive.Dodge, Is.EqualTo(50));
        }

        // ---- 随机流不被污染 ----

        [Test]
        public void NoBlindNoDodge_DoesNotConsumeRandom()
        {
            // 命中率 100 时必须短路不摇。_random 的唯一既有消费方是回合掉字,多摇一次就会
            // 平移掉落序列,让所有依赖种子的既有测试全红。
            //
            // 判别力来自两台引擎「AttackHits 调用次数不同」,而不是「两台引擎完全一样」——
            // 后者哪怕命中判定无条件摇随机数,两边烧掉的随机数一样多,序列照样一致,零判别力。
            // 用 EnemyState.Speed 制造差异:Speed=100 每回合出手 1 次,Speed=200 每回合出手 2 次
            // (MaxActionsPerTurn=2 封顶)。同种子、同回合数、都无致盲无闪避,若 AttackHits
            // 偷摇随机数,出手次数更多的那台每回合多烧一次,掉落序列必然分叉。
            var config = new BattleConfig
            {
                DropTable = new[] { "木" }, PlayerMaxHp = 200,
                UnlockedChars = new[] { "素", "闪", "昏", "雾" }, DropsPerTurn = 1,
            };

            var slow = Engine(Array.Empty<string>(), new[] { Attacker(attack: 1) }, config, seed: 7);
            slow.Enemies[0].Speed = 100;
            for (int i = 0; i < 3; i++) slow.EndTurn();
            var dropsAtSpeed100 = new List<string>(slow.Library);

            var fast = Engine(Array.Empty<string>(), new[] { Attacker(attack: 1) }, config, seed: 7);
            fast.Enemies[0].Speed = 200;
            for (int i = 0; i < 3; i++) fast.EndTurn();

            Assert.That(fast.Library, Is.EqualTo(dropsAtSpeed100),
                "同种子、同回合数,只有敌人出手次数不同 —— 掉落序列分叉说明命中判定偷摇了随机数");
        }
    }
}
