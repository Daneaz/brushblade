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
            // 眩/昡:单体致盲各 60%,2 回合,数值相同但字 ID 不同(熣 的部分致盲版,用来测
            // 「闪避需要跟非满值致盲组合才有判别力」以及「不同来源致盲可叠加」两件事)
            new CharDef("眩", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 60, turns: 2) }),
            new CharDef("昡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Blind, 60, turns: 2) }),
            // 挡:护盾 20;御:免疫 1 次 —— 用来验「打空时这两样都不该被消耗」
            new CharDef("挡", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Shield, 20) }),
            new CharDef("御", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Immunity, 1) }),
            // 映:反弹 50%,2 回合(镜)
            new CharDef("映", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Reflect, 50, turns: 2) }),
            // 凿:1 点伤害,用来精确磨血线 / 制造一次「受击存活」
            new CharDef("凿", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) }),
            // 禁:单体沉默 2 回合(锁)
            new CharDef("禁", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Silence, 0, turns: 2) }),
            // 冻:单体冻结 2 回合(评审 Important 1 的冻结组合测试专用)
            new CharDef("冻", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Freeze, 2) }),
            // 棘:攻 0 + 反伤 3(荆,借自子项目 C 的 SummonPassiveTests),用来在
            // Reflect_DoesNotDuplicateDeathWhenBossDiesToThornsBeforePierceLands 里让
            // Boss 在 Pierce 打召唤物那一步先被反伤打死
            new CharDef("棘", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 30, summonCount: 1, summonAttack: 0, summonChar: "木",
                    passive: new SummonPassive { Thorns = 3 }) }),
            // 斫:10 伤 ×2 段(剁)
            new CharDef("斫", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 2) }),
            // 斩:20 伤,HP<25% 且非 Boss 直接击杀(用来验多段与斩杀的交互)
            new CharDef("斩", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 2,
                    executeBelowPercent: 25, executeKills: true) }),
            // 刈:10 伤 ×2 段,HP<25% → 该段伤害 ×2(evaluator Important 1:executeKills:false 的
            // 「残血加伤」那一半此前零覆盖,只覆盖了 executeKills:true 的直接击杀那一半)
            new CharDef("刈", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 2,
                    executeBelowPercent: 25, executeKills: false) }),
            // 凿(1 点伤害)在 Task 1 已加进 Graph(),这里直接用
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
            // BlindPlusDodge_SummonTakesNothing / Dodge_NeededForDeterministicMiss_WithPartialBlind
            // 那两条做断言——闪避 50% 本身不是确定性的,这里只做接线检查,不为了凑
            // 「必空」这个名字硬加断言
            var engine = Engine(new[] { "闪" }, new[] { Attacker() });
            engine.Cast("闪");
            Assert.That(engine.Summons[0].Passive.Dodge, Is.EqualTo(50));
        }

        [Test]
        public void BlindPlusDodge_SummonTakesNothing()
        {
            // 名字订正(2026-08-08,评审 Important 2):原名 _ClampsHitRateAtZero_NotNegative
            // 暗示钳位在拦一个「反而变成必中」的错误行为,但按现有比较式
            // `_random.Next(100) < hitRate`,负的 hitRate 本就恒为 false = 必空,和钳到 0
            // 逐位相同(评审已用变异验证:去掉钳位这条测试照样绿)。这条测试实际断言的是
            // 「致盲 + 闪避同时命中率归零时召唤物毫发无损」,与钳位是否存在无关,改成描述
            // 断言本身的名字。真正验证「闪避确实进了算式」的是下面
            // Dodge_NeededForDeterministicMiss_WithPartialBlind。
            var engine = Engine(new[] { "闪", "昏" }, new[] { Attacker() });
            engine.Cast("闪");
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(10), "召唤物一点没掉血");

            // 修复 3(Minor,2026-08-08):Missed 事件的 SecondIndex 契约——变异把 summonIndex
            // 改成默认 -1,737 全绿存活。真实后果:Juice.cs 靠 SecondIndex >= 0 决定「空」字
            // 飘在被闪的召唤物身上还是屏幕中下,传 -1 会让飘字跑到玩家位置,读成「玩家躲开了」。
            var missed = engine.LastEvents.First(e => e.Kind == BattleEventKind.Missed);
            Assert.That(missed.SecondIndex, Is.EqualTo(0),
                "打空的是召唤物,SecondIndex 该指向被闪的那只,不是玩家(-1)");
        }

        [Test]
        public void Dodge_NeededForDeterministicMiss_WithPartialBlind()
        {
            // 评审 Important 1:BlindPlusDodge_SummonTakesNothing 用的是致盲 100% ——光靠
            // 致盲命中率就已经是 0,50 点闪避完全被掩盖,对「闪避是不是真的进了算式」没有
            // 判别力(变异证据:把 DamageSummon 里 summon.Passive?.Dodge ?? 0 换成 0,那条
            // 测试仍然全绿)。
            // 这里用致盲 60%(眩)——单独作用命中率是 40(会摇,不确定);只有加上闪避 50
            // 才让命中率归零、变成确定性必空,判别力精确落在闪避这一项上。
            var engine = Engine(new[] { "闪", "眩" }, new[] { Attacker() });
            engine.Cast("闪");
            engine.Cast("眩", 0);
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(10),
                "致盲 60% + 闪避 50% = 命中率 0,召唤物一点没掉血");
        }

        [Test]
        public void Blind_MultipleSourcesStack_ClampedHitRateStaysZero()
        {
            // spec §九 第 4 条(评审 Minor 2):致盲多来源合计钳到 100,不出现负命中率。
            // 眩/昡 数值相同(各 60%)但字 ID 不同——ApplyBlind 用字 ID 做 SourceId 去重,
            // 同字才刷新,不同字要能叠加。顺带给 AttackHits 的钳位提供一个真实的多来源场景
            // (100 - 120 = -20,钳到 0)。
            var engine = Engine(new[] { "眩", "昡" }, new[] { Attacker() });
            engine.Cast("眩", 0);
            engine.Cast("昡", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Blind), Is.EqualTo(120),
                "两个不同来源各 60%,合计 120,不同字不刷新只叠加");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50),
                "命中率钳到 0,必空,不会因为原始值是负数就摇出异常结果");
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

            // 修复 3(Minor,2026-08-08):顺带确认打玩家那条路径(不经召唤物)的 Missed
            // 事件 SecondIndex 是 -1,与打召唤物那条(BlindPlusDodge_SummonTakesNothing)
            // 的 0 形成对照。
            var missed = engine.LastEvents.First(e => e.Kind == BattleEventKind.Missed);
            Assert.That(missed.SecondIndex, Is.EqualTo(-1),
                "打玩家那条路径的 SecondIndex 该是 -1,不是召唤物下标");
        }

        [Test]
        public void Blind_DoesNotStopToppleShieldBreak()
        {
            // 刻意的裁定:致盲让「伤害」落空,但掀盾不经 DamagePlayerDirect,打不空。
            // 与免疫同口径(免疫也挡不住掀盾)。
            // 攻击力订正(2026-08-08,评审 Minor 1):原来是 4,倾覆伤害 ReducedDamage(4×2)=8
            // 全被 20 点护盾吃掉,命中与否 PlayerHp 都是 200——这条对「伤害是否被打空」根本
            // 没有判别力(变异证据:删掉 DamagePlayerDirect 里打空的 return,这条仍然绿,
            // 变红的是别的 4 条)。攻击力提到 20 后,倾覆伤害 40 > 护盾 20,命中会掉血到
            // 200-(40-20)=180,断言 200 才真的钉住「打空」。
            var boss = new EnemyDef("覆", Element.Heart, 300, 20,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 20, skill: BossSkill.Topple) });
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
        public void Blind_StopsSearFromBurningPlayer()
        {
            // 评审 Important 3:灯花(Sear)每次攻击给玩家挂 1 层灼烧,是攻击的附带效果——
            // 打空 = 攻击根本没落到身上,附带效果不该触发。原实现里 Sear 分支在攻击循环里、
            // DamagePlayerDirect 之外,打空 return 之后灼烧照挂,与「打空 = 什么都没发生」冲突
            var sear = new EnemyDef("灯", Element.Heart, 200, 8, EnemyAbility.Sear);
            var engine = Engine(new[] { "昏" }, new[] { sear });
            engine.Cast("昏", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "致盲 100% → 攻击打空");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "打空连带灼烧也不该挂——攻击根本没落到身上");
        }

        // 2026-08-16 CTB 改造:原断言"免疫挡下伤害→PlayerHp 不变"不再成立——免疫确实挡住了
        // 灯花那记普攻的物理伤害(8 点),但攻击同时新挂的 1 层灼烧会在同一次 EndTurn() 调用
        // 尾部的 BeginPlayerTurn 被当场结算(20 点)。免疫管不到灼烧这件事本身没变(设计从
        // 一开始就是如此),只是"灼烧当场结算"这个时序改动让它与这条测试的观察点撞进了
        // 同一次调用。断言从"HP 不变"改成"只掉 20(灼烧那份)",既证明普攻确实被免疫挡下,
        // 也证明灼烧确实触发了;末尾的层数断言从"> 0"改成"== 0",因为灼烧当场结算完就会
        // 归零移除——HP 少的那 20 点才是它触发过的证据,不能再用剩余层数来看。
        [Test]
        public void ImmunityBlocked_StillTriggersSear()
        {
            // 评审 Important 3 的另一半裁定:免疫挡下的是「伤害」,不是「攻击是否发生」——
            // 攻击确实命中了,只是被免疫完全吸收;灯花在攻击发生时就触发,不受免疫影响。
            // 防止有人把 hit 的口径写反,连免疫分支也一起 gate 掉
            var sear = new EnemyDef("灯", Element.Heart, 200, 8, EnemyAbility.Sear);
            var engine = Engine(new[] { "御" }, new[] { sear });
            engine.Cast("御", 0);
            int hpBefore = engine.PlayerHp;
            engine.EndTurn();
            Assert.That(hpBefore - engine.PlayerHp, Is.EqualTo(20), "普攻 8 点被免疫挡下,只剩当场结算的灼烧 20 点");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(0),
                "免疫层数被消耗——确认真的走了免疫分支,不是刚好没受击");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "灼烧已当场结算完毕——HP 那 20 点损失就是它触发过的证据");
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
        public void NeedsTarget_BlindAll_False_BlindSingle_True()
        {
            // 修复 2(Minor,2026-08-08):NeedsTarget 里 Blind 的 `&& !effect.TargetAll` 排除
            // 零判别力(变异证据:去掉这半句,737 全绿存活)。真实后果:出「烟」(全体致盲)时
            // UI 会进入选目标模式,逼玩家点一个毫无意义的目标才肯出牌。
            // 用实际出货字表断言:NeedsTarget 是公开静态方法,不用为测试放宽可见性。
            // 2026-08-14:烟(唯一的全体致盲字)随第二批裁定移出字表。这条守卫的判别力
            // 不能跟着没,故 targetAll 那一半改用构造的 CharDef —— 真实字表里没有载体了,
            // 但 NeedsTarget 的分支还在,新字一旦带 targetAll Blind 就要走对路径。
            var graph = CharTableTests.RealGraph();
            var blindAll = new CharDef("测", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Blind, 30, turns: 1, targetAll: true) });
            Assert.That(BattleEngine.NeedsTarget(blindAll), Is.False, "全体致盲不需要选目标");
            Assert.That(BattleEngine.NeedsTarget(graph.Get("熣")), Is.True, "单体致盲需要选目标");
        }

        [Test]
        public void Dodge_SurvivesSaveRoundTrip()
        {
            // 闪避走 SummonPassive 的存档路径(子项目 C 已铺好 Clone + Capture/Restore),
            // 这条钉住新字段真的跟着走了 —— 漏进 Clone() 的话跨战斗就白闪了
            var engine = Engine(new[] { "闪" }, new[] { Attacker(attack: 0) });
            engine.Cast("闪");

            var meta = new MetaState { EndlessV2 = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 } };
            foreach (var summon in engine.Summons) meta.EndlessV2.CarriedSummons.Add(summon.Capture());
            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));

            var revived = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { Attacker(attack: 0) }, seed: 1,
                startingSummons: restored.EndlessV2.CarriedSummons);
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
            // (2026-08-16 CTB 改造:MaxActionsPerTurn 已删除,出手频次改由调度器按速度比例
            // 决定,不再封顶——这里仍是 1:2 的比例,判别力不受影响)。同种子、同回合数、
            // 都无致盲无闪避,若 AttackHits 偷摇随机数,出手次数更多的那台每回合多烧一次,
            // 掉落序列必然分叉。
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

        // ---- 沉默 ----

        [Test]
        public void Silence_StopsScorchSelfBuff()
        {
            // 简报原版用 EndTurn 让敌人打玩家,但焦痕的触发点是「受击存活即自燃」
            // (DamageEnemy 后段),不是回合结算——那条从没打过这只敌人,加不加沉默都一样,
            // 零判别力(2026-08-07 控制器裁定)。这里改成真的用「凿」打它一记(1 点伤害,
            // 打不死 200 血的靶),沉默之下不该自燃。
            var engine = Engine(new[] { "禁", "凿" }, new[] { new EnemyDef("焦", Element.Heart, 200, 4, EnemyAbility.Scorch) });
            engine.Cast("禁", 0);
            int attackBefore = engine.Enemies[0].Attack;
            engine.Cast("凿", 0);   // 受击存活,焦痕本该自燃 —— 但沉默压住了
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(attackBefore), "沉默中不自燃");
        }

        [Test]
        public void NoSilence_ScorchGainsAttackWhenHit()
        {
            // 对照组(控制器要求):不沉默时受击存活确实自燃 +2(ScorchGain),
            // 与上面一条一起把判别力钉死——两条都在断言同一处代码的两种取值。
            var engine = Engine(new[] { "凿" }, new[] { new EnemyDef("焦", Element.Heart, 200, 4, EnemyAbility.Scorch) });
            int attackBefore = engine.Enemies[0].Attack;
            engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(attackBefore + 2), "无沉默:受击存活自燃 +2");
        }

        [Test]
        public void Silence_StopsRegrow()
        {
            var engine = Engine(new[] { "禁" }, new[] { new EnemyDef("缺", Element.Heart, 200, 0, EnemyAbility.Regrow) });
            engine.Cast("禁", 0);
            int hpBefore = engine.Enemies[0].Hp;
            int attackBefore = engine.Enemies[0].Attack;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBefore), "沉默中不补全");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(attackBefore));
        }

        [Test]
        public void Silence_StopsSear()
        {
            // 评审 Important 3:原版只断言 Burn == 0,对「沉默压的是能力不是行动」没有判别力——
            // 若沉默被误实现成「跳过整个行动」,灯花照样不挂灼烧(因为它压根没出手),这条测试
            // 照样绿。补上 PlayerHp 断言:灯花该照常出手打人,只是灼烧这个附带效果哑火。
            var engine = Engine(new[] { "禁" }, new[] { new EnemyDef("灯", Element.Heart, 200, 3, EnemyAbility.Sear) });
            engine.Cast("禁", 0);
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "沉默中不挂灼烧");
            Assert.That(engine.PlayerHp, Is.EqualTo(47), "照常出手,只是不挂灼烧");
        }

        [Test]
        public void Silence_StopsSplit()
        {
            var engine = Engine(new[] { "禁", "凿" }, new[] { new EnemyDef("叠", Element.Heart, 200, 0, EnemyAbility.Split) });
            engine.Cast("禁", 0);
            engine.Cast("凿", 0);   // 受击存活,本该分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(1), "沉默中不分裂");
        }

        [Test]
        public void Silence_SilencedBuffMinionAttacksInstead()
        {
            // 标点小妖有同伴时用加攻代替出手。被沉默后加攻哑火,它就该**改为亲自攻击**,
            // 而不是站着什么都不做 —— 沉默压的是「能力」,不是「行动」
            var engine = Engine(new[] { "禁" }, new[]
            {
                new EnemyDef("标", Element.Heart, 200, 3, EnemyAbility.Buff),
                new EnemyDef("伴", Element.Heart, 200, 0),
            });
            engine.Cast("禁", 0);
            int companionAttack = engine.Enemies[1].Attack;
            engine.EndTurn();
            Assert.That(engine.Enemies[1].Attack, Is.EqualTo(companionAttack), "同伴没被加攻");
            Assert.That(engine.PlayerHp, Is.EqualTo(47), "标点小妖改为亲自出手,打了 3");
        }

        [Test]
        public void Silence_CancelsBossChargeAndResetsCounter()
        {
            var boss = new EnemyDef("覆", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 4, skill: BossSkill.Topple) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };
            var engine = Engine(new[] { "禁" }, new[] { boss }, config);
            engine.EndTurn();                       // Boss 进入蓄力
            Assert.That(engine.Enemies[0].IsCharging, Is.True);

            engine.Cast("禁", 0);
            int hpBefore = engine.PlayerHp;
            engine.EndTurn();                       // 沉默 → 蓄力取消,不放大招,交回普攻

            Assert.That(engine.Enemies[0].IsCharging, Is.False, "蓄力被取消");
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "计数清零,解锁后从头攒");
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False, "倾覆没放出来");
            // 评审 Important 3:上面三条只看了蓄力状态和倾覆的副作用(Seal),没看「沉默压的是
            // 能力不是行动」这件事本身——若把 ResolveBossTurn 顶部的沉默短路从「交回普攻」
            // 误改成「本回合什么都不干」，三条断言照样绿。补上普攻伤害断言堵死这条路。
            Assert.That(engine.PlayerHp, Is.EqualTo(hpBefore - 4), "取消蓄力后交回普攻,照常打 4");
        }

        [Test]
        public void Silence_CancelsBossChargeImmediately()
        {
            // 评审 Important 1(控制器裁定,2026-08-08,真洞):原实现的取消逻辑挂在
            // ResolveBossTurn 里,只有敌人真的行动(actionCount>0)时才会跑到。这里不等
            // EndTurn,直接在 Cast 之后当场断言——取消必须发生在「挂上沉默的那一刻」,
            // 不能等到敌人下次行动才生效。
            var boss = new EnemyDef("覆", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 4, skill: BossSkill.Topple) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };
            var engine = Engine(new[] { "禁" }, new[] { boss }, config);
            engine.EndTurn();                       // Boss 进入蓄力
            Assert.That(engine.Enemies[0].IsCharging, Is.True);

            engine.Cast("禁", 0);                    // 不 EndTurn,当场就该打断蓄力
            Assert.That(engine.Enemies[0].IsCharging, Is.False, "蓄力当场被取消,不用等下次行动");
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "计数当场清零");
        }

        [Test]
        public void Silence_CancelsBossChargeEvenThroughFreeze()
        {
            // 评审 Important 1 的复现场景:Boss 蓄力中 → 沉默 + 冻结同时压上 → 冻结期间
            // actionCount 恒为 0,若取消逻辑挂在「行动时判」(ResolveBossTurn)就永远等不到
            // 触发的机会 —— 沉默 2 回合早早过期,冻结 2 回合解开后 IsCharging 仍是 true,
            // 一解冻就立刻放出倾覆(倾覆的 Seal 会挂上)。取消挂在 Cast 那一刻就不受冻结影响。
            var boss = new EnemyDef("覆", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 4, skill: BossSkill.Topple) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 1 };
            var engine = Engine(new[] { "禁", "冻" }, new[] { boss }, config);
            engine.EndTurn();                       // Boss 进入蓄力
            Assert.That(engine.Enemies[0].IsCharging, Is.True);

            engine.Cast("禁", 0);                    // 沉默 2 回合
            engine.Cast("冻", 0);                    // 冻结 2 回合,盖住沉默的整个窗口
            engine.EndTurn();                       // 冻结中不行动(第 1 个被挡的敌方回合)
            engine.EndTurn();                       // 冻结中不行动(第 2 个被挡的敌方回合,沉默也在这期间到期)
            engine.EndTurn();                       // 冻结解开,恢复行动 —— 不该立刻放技能

            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False, "倾覆没有被放出来");
        }

        [Test]
        public void Silence_ResetsChargeCounterWhenNotYetCharging()
        {
            // 修复 4(Minor,2026-08-08):ResolveBossTurn 沉默短路里的 ChargeCounter = 0——
            // 变异删掉这一行,737 全绿存活。真实行为差:Boss 计数为 1(还没进蓄力)时被沉默,
            // 现实现在敌方回合把计数清 0(语义:沉默期间不攒力);删掉则保留 1,解锁后
            // 早一回合放大招。BossChargeEvery=2:第一个敌方回合只攒到 1,还没进蓄力。
            var boss = new EnemyDef("覆", Element.Heart, 300, 4,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 300, 4, skill: BossSkill.Topple) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, BossChargeEvery = 2 };
            var engine = Engine(new[] { "禁" }, new[] { boss }, config);
            engine.EndTurn(); // 普攻,ChargeCounter → 1(未达到蓄力阈值 2),不进入蓄力
            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(1));
            Assert.That(engine.Enemies[0].IsCharging, Is.False);

            engine.Cast("禁", 0);   // 沉默 2 回合
            engine.EndTurn();      // 沉默期间的敌方回合:ResolveBossTurn 顶部短路应清零计数

            Assert.That(engine.Enemies[0].ChargeCounter, Is.EqualTo(0), "沉默期间不攒力,计数清零");
        }

        [Test]
        public void Silence_DoesNotAffectDisguiseOrObscure()
        {
            // 通假/生僻是信息隐藏,不是主动机制 —— 锁一下就看穿了不符合「锁」的语义。
            // 评审 Minor 1:原版名字撒谎,只测了 Disguise,Obscure 一行没碰
            // (变异证据:给 Obscure 的现形条件误加 !IsSilenced,711 全绿)——补上后半段。
            var disguised = Engine(new[] { "禁" }, new[] { new EnemyDef("通", Element.Wood, 200, 3, EnemyAbility.Disguise) });
            disguised.Cast("禁", 0);
            disguised.EndTurn();
            Assert.That(disguised.Enemies[0].ApparentElement, Is.EqualTo(disguised.Enemies[0].Element),
                "通假:首次行动后照常现形");

            // 库里要放两张「凿」——出字即消耗(3.8.1),一张打完就从库里没了,只放一张
            // 第二次 Cast 会静默 NotCastable,凿不出第二下
            var obscured = Engine(new[] { "禁", "凿", "凿" }, new[] { new EnemyDef("僻", Element.Wood, 200, 0, EnemyAbility.Obscure) });
            obscured.Cast("禁", 0);
            obscured.Cast("凿", 0);
            obscured.Cast("凿", 0);   // 受击两次
            Assert.That(obscured.Enemies[0].ApparentElement, Is.EqualTo(obscured.Enemies[0].Element),
                "生僻:受击两次后照常现形");
        }

        [Test]
        public void Silence_NeedsNoExplicitTarget_WhenSoleEnemyAlive()
        {
            // 评审 Important 2:NeedsTarget 白名单漏掉 Silence 不会崩溃,而是更阴的静默吞掉——
            // targetIndex 停在 -1,ApplyEffects 的 case Silence 里 `targetIndex >= 0` 兜底直接
            // 跳过不挂状态,AP 扣了、字消耗了,沉默却没挂上。仿 Task 1 的
            // Blind_NeedsNoExplicitTarget_WhenSoleEnemyAlive,不传目标下标也得能自动锁定。
            var engine = Engine(new[] { "禁" }, new[] { Attacker() });
            Assert.DoesNotThrow(() => engine.Cast("禁"));
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Silence), Is.True,
                "沉默真的挂上了,不是静默吞掉");
        }

        // ---- 反弹 ----

        [Test]
        public void Reflect_SendsBackHalfTheDamage()
        {
            var engine = Engine(new[] { "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(42), "照常挨 8");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore - 4), "反弹 8 的 50% = 4");
        }

        [Test]
        public void Reflect_UsesTotalDamage_NotHpActuallyLost()
        {
            // 护盾吸掉的部分也照样照回去 —— 「镜」是把东西原样反射,不管你挡没挡住。
            // 与召唤物 荆 的反伤同口径(被打死的那一击也照样扎)
            var engine = Engine(new[] { "挡", "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("挡", 0);
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50), "8 点全被 20 点护盾吃掉");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore - 4), "仍按总伤害 8 反弹 4");
        }

        [Test]
        public void Reflect_DoesNotFireWhenImmunityBlocks()
        {
            // 免疫是完全挡下,压根没吃到那记伤害 —— 没吃到就没得反
            var engine = Engine(new[] { "御", "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("御", 0);
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(50));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore), "免疫挡下 → 不反弹");
        }

        [Test]
        public void Reflect_DoesNotFireWhenAttackMisses()
        {
            var engine = Engine(new[] { "昏", "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("昏", 0);
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore), "打空 → 不反弹");
        }

        [Test]
        public void Reflect_KillingLastEnemy_WinsTheBattle()
        {
            // 反弹与召唤物反伤同型:会在敌方回合里杀敌。子项目 C 为反伤补的
            // 「敌方段后判胜」在这里直接受益 —— 这条钉住它确实覆盖了反弹
            var engine = Engine(new[] { "映" }, new[] { new EnemyDef("靶", Element.Heart, 4, 8) });
            engine.Cast("映", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Reflect_NeedsNoExplicitTarget()
        {
            // 反弹是给玩家自己上的增益,不需要选目标 —— 仿前两个任务在这处抓到的洞
            // (Dispel 漏白名单直接崩溃、Silence 白名单零判别力)。这里刻意放两个存活敌人:
            // 若 Reflect 被错误加进 NeedsTarget 白名单,targetIndex=-1 且场上不止一个存活敌人时
            // 「单敌免选」兜底找不到唯一目标,Cast 会静默返回 InvalidTarget、不抛异常但也不挂状态
            // ——DoesNotThrow 抓不住这种静默吞掉,得靠后面的状态断言。只有一个敌人时「单敌免选」
            // 会兜底补上目标,这条测试就失去判别力了。
            var engine = Engine(new[] { "映" }, new[] { Attacker(attack: 8), Attacker(attack: 8) });
            Assert.DoesNotThrow(() => engine.Cast("映"));
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Reflect), Is.EqualTo(50),
                "不需要选目标,状态照样挂上");
        }

        // 2026-08-16 全分支终审 Important 1 修复后:玩家侧状态递减(TickPlayerStatuses)从
        // YieldTurn(拍尾,先递减后结算)挪回 BeginPlayerTurn 尾部(结算之后、StartTurn 之前)。
        // Reflect 挂上 TurnsLeft=2 后,第 1 次 EndTurn 的敌人攻击先触发反弹,随后本次 EndTurn
        // 到达下一次 BeginPlayerTurn 时才把 TurnsLeft 减到 1(仍非零、照常反弹);第 2 次 EndTurn
        // 的敌人攻击时 Reflect 仍在(TurnsLeft=1),照常反弹,随后才减到 0 移除——覆盖满整整
        // 2 个敌方回合,恢复成本条测试原本要守的语义(曾短暂被误改成只覆盖 1 个,已修正)。
        [Test]
        public void Reflect_ExpiresAfterTwoEnemyTurns()
        {
            // 评审 Important 1(2026-08-08):turns=2 之前零覆盖——把 ApplyEffects 里挂状态那句的
            // TurnsLeft 写成 -1(永不过期),720 条测试一条不红,一张蓝字就此变成整场永久反伤。
            var engine = Engine(new[] { "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("映", 0);

            // 评审 Minor 1:顺带钉住 Polarity——反弹是给自己上的增益,不是减益。误标成
            // Debuff 会让净化(Cleanse,清玩家自身全部减益)把自己刚上的反弹清掉,
            // 敌方驱散反而碰不到它。
            Assert.That(engine.PlayerStatuses.Find(StatusKind.Reflect).Polarity, Is.EqualTo(StatusPolarity.Buff),
                "反弹是增益,净化/驱散得认对边");

            int enemyHp = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHp - 4), "第 1 个敌方回合仍在覆盖内,照常反弹");

            enemyHp = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHp - 4), "第 2 个敌方回合仍在覆盖内,照常反弹");

            enemyHp = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHp), "反弹已到期,第 3 个敌方回合不再反弹");
        }

        [Test]
        public void Reflect_IgnoresWuxing_BounceIsFlatRegardlessOfDefenderElement()
        {
            // 评审 Important 2(2026-08-08):「反弹不走生克」是 spec §3.2 明文条款,之前零覆盖——
            // 既有 6 条反弹测试全用「心」属性敌人,心不在相克环里,DamageEnemy 传哪个 attacker
            // 元素,KeMultiplier(attacker, Heart) 都恒 1.0,对 attacker 元素传错没有判别力
            // (把反弹结算的 Element.Heart 改成 Element.Metal,720 条测试一条不红)。
            // 换成金属性敌人才有判别力:与 SummonPassiveTests.Thorns_ReflectsFlatDamage_IgnoringWuxing
            // 同型——传对(心)得平值 4;误传木(比如手滑传成攻击方/施法字自身元素)会被金
            // 反克成 floor(4×0.5)=2;误传火会被克成 floor(4×1.5)=6,三者互不相同。
            var engine = Engine(new[] { "映" }, new[] { new EnemyDef("锈", Element.Metal, 200, 8) });
            engine.Cast("映", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(196), "反弹不走生克,8 的 50% = 4 平值打进金属性敌人");
        }

        [Test]
        public void Reflect_RoundingToZero_DoesNotTriggerAttackSideEffects()
        {
            // 评审 Important 4(2026-08-08):`bounced > 0` 守卫之前零覆盖,它挡的不是事件噪音而是
            // 真实副作用——敌人攻 1、反弹 50% 时 bounced=0,少了这条守卫,DamageEnemy 照样会跑
            // enemy.HitsTaken += 1,连带推进焦痕(Scorch)「受击存活即自燃加攻」的判定:
            // 玩家带反弹站桩,每挨一记 1 点小伤就白送敌人一次加攻。
            var engine = Engine(new[] { "映" }, new[] { new EnemyDef("焦", Element.Heart, 200, 1, EnemyAbility.Scorch) });
            engine.Cast("映", 0);
            int attackBefore = engine.Enemies[0].Attack;
            int hpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBefore), "反弹算出 0,没有额外伤害落地");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(attackBefore), "0 伤反弹不该被误判成受击自燃");
        }

        [Test]
        public void Reflect_DoesNotDuplicateDeathWhenBossDiesToThornsBeforePierceLands()
        {
            // 评审 Important 3(2026-08-08):`_enemies[enemyIndex].Alive` 守卫之前零覆盖,而它挡的
            // 是真实可达路径——BossSkill.Pierce 先 DamageSummon(可能触发召唤物 荆 的反伤打死
            // Boss)再 DamagePlayerDirect,此时 enemyIndex 上已是一具尸体。少了守卫,反弹会对
            // 死尸再补一刀,走进 DamageEnemy 触发第二次 ResolveDefeat,发出重复的 EnemyDied 事件
            // (表现层会把死亡动效播两遍)。
            // 3 血的 Boss:召唤物 棘(反伤 3)在 Pierce 打它那一步正好把它扎死;紧接着 Pierce
            // 还会无条件打玩家一下,玩家带着反弹——若漏了 Alive 守卫就会对死 Boss 补刀。
            var boss = new EnemyDef("觥", Element.Heart, 3, 4,
                phases: new[] { new BossPhaseDef("觥", Element.Heart, 3, 4, skill: BossSkill.Pierce) });
            var config = new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50, BossChargeEvery = 1 };
            var engine = Engine(new[] { "棘", "映" }, new[] { boss }, config);
            engine.Cast("棘");                        // 召唤反伤 3(荆)
            engine.Cast("映", 0);                     // 反弹 50%
            engine.EndTurn();                        // Boss 蓄力
            engine.EndTurn();                        // 释放贯穿:先打召唤物(反伤打死 3 血 Boss),
                                                       // 再打玩家(反弹若漏 Alive 守卫会补刀死尸)
            Assert.That(engine.Enemies[0].Alive, Is.False, "Boss 被反伤打死");
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyDied), Is.EqualTo(1),
                "只该有一条阵亡事件,不能因为反弹对死尸补刀而发出第二条");
        }

        [Test]
        public void Reflect_StacksWithSummonThorns_BothBounceBack()
        {
            // 修复 1(用户裁定,2026-08-08):反弹只结算在 DamagePlayerDirect 末尾,但敌人普攻
            // 若场上有存活召唤物顶前排,走的是 DamageSummon,那里原先没有任何反弹代码——
            // 「柳(闪避召唤)+ 镜」这类组合与全部召唤字互斥。用户裁定:DamageSummon 里也要
            // 结算玩家的反弹(挡在前排的伤害同样算「打到了我方」)。
            // 棘(荆,反伤 3)+ 映(反弹 50%)同场:敌人打召唤物应同时挨反伤与反弹。
            var engine = Engine(new[] { "棘", "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("棘");
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            // taken = 8(心对心恒 1.0x);荆反伤平值 3;反弹 = taken(8) × 50% = 4;合计 7
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore - 7), "反伤 3 + 反弹 4 = 7");

            // 顺带钉住 EnemyDied 不重复:低血敌人构造「荆先打死」的场景 —— 荆的反伤(3)
            // 打死 3 血的敌人后,_enemies[enemyIndex].Alive 守卫必须挡住反弹再对死尸补刀。
            var dupEngine = Engine(new[] { "棘", "映" }, new[] { new EnemyDef("觥", Element.Heart, 3, 8) });
            dupEngine.Cast("棘");
            dupEngine.Cast("映", 0);
            dupEngine.EndTurn();
            Assert.That(dupEngine.Enemies[0].Alive, Is.False, "敌人被荆的反伤打死");
            Assert.That(dupEngine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyDied), Is.EqualTo(1),
                "只该有一条阵亡事件,反弹不能对死尸补刀发出第二条");
        }

        [Test]
        public void Reflect_OnSummonHit_UsesPostWuxingTakenAsBasis()
        {
            // 修复 1 测试 2:反弹基数必须是 taken(过完生克的值),不是原始 damage——召唤物
            // 承伤本来就走五行(DamageSummon 里 WuxingResolver.ResolveEffect(damage, ...,
            // attacker, summon.Element)),所以「总伤害」在召唤物这一侧本来就是 taken。
            // 用非心属性的敌人(金)让 taken 与原始 damage 拉开:召唤物是木,金克木 ×1.5 ——
            // damage=8,taken=floor(8×1.5)=12,反弹 50% = 6;若误用原始 damage,
            // 会算成 floor(8×50%)=4,两者不同,才有判别力。
            var engine = Engine(new[] { "素", "映" }, new[] { new EnemyDef("锈", Element.Metal, 200, 8) });
            engine.Cast("素");
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore - 6),
                "taken = floor(8×1.5) = 12(金克木),反弹 50% = 6");
        }

        [Test]
        public void Reflect_DoesNotFireWhenSummonDodgesTheAttack()
        {
            // 修复 1 测试 3:打空(命中判定在 DamageSummon 最开头就 return false)天然走不到
            // 反弹那段,这里钉住这个组合场景。闪(50% 闪避)+ 昏(100% 致盲)确保命中率归零,必空。
            var engine = Engine(new[] { "闪", "昏", "映" }, new[] { Attacker(attack: 8) });
            engine.Cast("闪");
            engine.Cast("昏", 0);
            engine.Cast("映", 0);
            int enemyHpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(enemyHpBefore), "打空,敌人一点没掉血(反弹没触发)");
        }

        [Test]
        public void Reflect_OnSummonHit_RoundingToZero_DoesNotTriggerAttackSideEffects()
        {
            // 修复 1 变异验证:bounced > 0 守卫如果没有,DamageEnemy 会把 0 伤反弹也算成
            // 一次命中,推进 enemy.HitsTaken,连带触发焦痕「受击存活即自燃」——与玩家侧
            // Reflect_RoundingToZero_DoesNotTriggerAttackSideEffects 同型。
            // taken = floor(1×1.0) = 1(心对心恒 1.0x),反弹 50% = 0(整数除法向下取整)。
            var engine = Engine(new[] { "素", "映" }, new[] { new EnemyDef("焦", Element.Heart, 200, 1, EnemyAbility.Scorch) });
            engine.Cast("素");
            engine.Cast("映", 0);
            int attackBefore = engine.Enemies[0].Attack;
            int hpBefore = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpBefore), "反弹算出 0,没有额外伤害落地");
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(attackBefore), "0 伤反弹不该被误判成受击自燃");
        }

        // ---- 多段 ----

        [Test]
        public void MultiHit_DealsEachSegmentSeparately()
        {
            var engine = Engine(new[] { "斫" }, new[] { new EnemyDef("靶", Element.Heart, 200, 0) });
            engine.Cast("斫", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(180), "10 × 2 段");
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(2),
                "两段各发一条伤害事件——一拍打完玩家看不出是两段");
        }

        [Test]
        public void MultiHit_StopsWhenTargetDies()
        {
            // 8 血靶:第一段 10 伤就打死了,第二段该停手,不对尸体再发一条伤害事件
            var engine = Engine(new[] { "斫" }, new[] { new EnemyDef("靶", Element.Heart, 8, 0) });
            engine.Cast("斫", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(0));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(1),
                "第一段就打死了,第二段停手");
        }

        [Test]
        public void MultiHit_SecondSegmentCanTriggerExecute()
        {
            // 上限 100、磨到 30:第一段 10 伤 → 20(20% < 25%),第二段判血命中阈值 → 直接击杀。
            // 「打之前判血」是每段各自判,所以这是真会发生的涌现
            var library = new List<string>(Enumerable.Repeat("凿", 70)) { "斩" };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = 200 },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100, 0) }, seed: 1);
            for (int i = 0; i < 70; i++) engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(30), "磨血辅助本身没磨准");

            engine.Cast("斩", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(0), "第二段处决");
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void MultiHit_SecondSegmentGetsExecuteBonus()
        {
            // 评审 Important 1:「打之前判血」的斩杀阈值有两半——executeKills: true(直接击杀)
            // 那半已被 MultiHit_SecondSegmentCanTriggerExecute 钉住,executeKills: false(残血
            // 加伤 ×2)那半此前零覆盖(变异证据:把伤害值提到循环外只算一次,730 条一条不红)。
            // 上限 100、磨到 34:第一段 34% ≥ 25%,普通 10 伤 → 24;第二段重判 24% < 25%,
            // 该段伤害 ×2 → 20 伤 → 剩 4。断言两条 Damage 事件各自的 Amount(10、20),
            // 不只看总血量——否则「第一段 20、第二段 10」这类错序也能蒙混过关。
            var library = new List<string>(Enumerable.Repeat("凿", 66)) { "刈" };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = 200 },
                library, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 100, 0) }, seed: 1);
            for (int i = 0; i < 66; i++) engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(34), "磨血辅助本身没磨准");

            engine.Cast("刈", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(4), "10 + 20");
            var damages = engine.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(damages.Count, Is.EqualTo(2));
            Assert.That(damages[0].Amount, Is.EqualTo(10), "第一段:34% 未进阈值,普通伤害");
            Assert.That(damages[1].Amount, Is.EqualTo(20), "第二段:24% 进阈值,该段伤害 ×2");
        }

        /// <summary>多段字的破甲专用图:裂 = 破甲 3 点、斫 = 10 伤 ×2 段。</summary>
        private static BattleEngine MultiHitArmorEngine(int enemyDefense)
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("裂", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.ArmorBreak, 3) }),
                new CharDef("斫", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 2) }),
            });
            return new BattleEngine(graph,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                new[] { "裂", "斫" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 200, 0, defense: enemyDefense) }, seed: 1);
        }

        /// <summary>⚠ 语义仍成立但含义变了(2026-08-12,E-b4 T3,spec §4.4 末尾):
        /// 旧口径是「每段各享一次承伤 +25%」,新口径是「每段各按同一个有效护甲结算」。
        /// 破甲是目标身上的持续状态,两段之间不会变化,所以「每段独立」在破甲这条上是
        /// **平凡成立**的 —— 真正要守的是「每段各扣一次护甲」,见下面那条。</summary>
        [Test]
        public void MultiHit_EachSegmentGoesThroughArmorBreakSeparately()
        {
            var engine = MultiHitArmorEngine(enemyDefense: 4);
            engine.Cast("裂", 0);   // 破甲 3 → 有效护甲 4 − 3 = 1
            engine.Cast("斫", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(182), "(10 − 1) × 2 段 = 18");
        }


        [Test]
        public void HitCountDefaultsToOne_ExistingDamageUnchanged()
        {
            var engine = Engine(new[] { "凿" }, new[] { new EnemyDef("靶", Element.Heart, 200, 0) });
            engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(199));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.Damage), Is.EqualTo(1));
        }

        [Test]
        public void HitCountZeroOrNegative_TreatedAsOne_NotADud()
        {
            // 构造函数守卫:hitCount ≤ 0 视为 1,不让配置写错把字变成哑弹
            // (变异证据:把 `HitCount = hitCount <= 0 ? 1 : hitCount` 改成直接赋值,
            // 729 条测试一条不红——补这条堵死)
            //
            // 名字撒谎修复(评审 Minor 1,2026-08-08):名字承诺 "ZeroOrNegative",原版只构造
            // 了 hitCount: 0,负数分支没人守(变异证据:把守卫改成 `hitCount == 0 ? 1 : hitCount`
            // ——只挡 0、放过负数——730 条全绿存活)。补上 hitCount: -1 的字,同样只该打 1 段。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("哑", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: 0) }),
                new CharDef("闷", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.DamageSingle, 10, hitCount: -1) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                new[] { "哑", "闷" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 200, 0) }, seed: 1);
            engine.Cast("哑", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(190), "hitCount: 0 当 1 打,不是哑弹");
            engine.Cast("闷", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(180), "hitCount: -1 同样当 1 打,不是哑弹");
        }
    }
}
