using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火系 DOT 三分化(2026-08-09,子项目 E-a):不灭 / 立即结算 / 引爆。
    /// 规格见 docs/superpowers/specs/2026-08-09-火系DOT三分化-design.md。</summary>
    public class BurnVariantTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 燃:效果同真实字表(纯灼烧 4 层);属性刻意用 心(真实是 火),
            // 隔离施加时的生克——灼烧只在结算时吃克制,施加这一步不该被生克污染测试
            new CharDef("燃", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 4) }),
            // 炽:灼烧系数 +1(与真实字表的 炽 同配置)
            new CharDef("炽", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 1) }),
            // 燋:2 层 + 不灭(炑 的等价配置)
            new CharDef("燋", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 2),
                                 new EffectDef(EffectKind.BurnNoDecay, 0) }),
            // 噤:沉默目标(自制变异验证辅助字,非规格产物)——用来在灯花闭嘴之后,
            // 单独观察玩家自身灼烧是否还会正常衰减,不被灯花每回合的刷新掩盖
            new CharDef("噤", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Silence, 0, turns: 3) }),
            // 驱:驱散目标全部增益(自制变异验证辅助字,非规格产物)——用来钉住
            // 不灭的 Polarity 必须是 Debuff,否则会被这张字清掉
            new CharDef("驱", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Dispel, -1) }),
            // 熇:2 层 + 系数 +1 + 立即结算(燥 的等价配置,效果顺序即结算顺序)
            new CharDef("熇", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 2),
                                 new EffectDef(EffectKind.BurnPotency, 1),
                                 new EffectDef(EffectKind.BurnSettleNow, 0) }),
            // 熯:只有立即结算,不带灼烧也不带系数(用来单独验结算逻辑)
            new CharDef("熯", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSettleNow, 0) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            BattleConfig config = null, int seed = 1) =>
            new(Graph(), config ?? new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 },
                library, Array.Empty<string>(), enemies, seed);

        private static EnemyDef Dummy(int hp = 300, int attack = 0) =>
            new("靶", Element.Heart, hp, attack);

        // ---- 灼烧结算的基线(重构守卫)----

        [Test]
        public void Burn_TicksThenDecaysOneStack()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8), "4 层 × 系数 2 = 8");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "结算后减一层");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8 - 6), "3 层 × 2 = 6");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
        }

        [Test]
        public void Burn_TicksOnlyTheBurningEnemy_WithCorrectTargetIndex()
        {
            // 评审 Important 3:原名叫「每个敌人一条」,但场上只有 1 个敌人,变异把
            // TargetIndex 写死成 0 都测不出来。这里放两个敌人,只烧 1 号,断言事件
            // 数量与 TargetIndex 都对——写死会让 2 号烧掉的血全飘到 1 号头上。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(), Dummy() });
            engine.Cast("燃", 1);
            engine.EndTurn();

            var ticks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick).ToList();
            Assert.That(ticks.Count, Is.EqualTo(1), "只有 1 号敌人带灼烧");
            Assert.That(ticks[0].TargetIndex, Is.EqualTo(1), "事件要带对目标下标,不能写死成 0");
        }

        [Test]
        public void Burn_TicksBothBurningEnemies_EachWithOwnTargetIndex()
        {
            var engine = Engine(new[] { "燃", "燃" }, new[] { Dummy(), Dummy() });
            engine.Cast("燃", 0);
            engine.Cast("燃", 1);
            engine.EndTurn();

            var ticks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick)
                .OrderBy(e => e.TargetIndex).ToList();
            Assert.That(ticks.Count, Is.EqualTo(2));
            Assert.That(ticks[0].TargetIndex, Is.EqualTo(0));
            Assert.That(ticks[1].TargetIndex, Is.EqualTo(1));
        }

        [Test]
        public void Burn_RespectsKeMultiplier_NotShengMultiplier()
        {
            // 火克金 ×1.5:4 层 × 2 × 1.5 = 12。用金属性靶子才测得出克制,
            // 心属性对全属性都是 1.0x(子项目 D 的教训:同属性对同属性也是 1.0,同样测不出来)
            var engine = Engine(new[] { "燃" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12), "4 × 2 × 1.5(火克金)");
        }

        [Test]
        public void Burn_UsesCurrentBurnPerStack_AndFloors_NotRounds()
        {
            // 评审 Minor 2:炽 在 fixture 里定义了但基线一条都没 Cast 过它,于是
            // _burnPerStack 写死成 2、Math.Floor 换成 Math.Round 都测不出来。炽 把系数从
            // 基础 2 抬到 3;3 层 × 3 × 1.5(金)= 13.5 —— floor 给 13,Math.Round 的
            // 银行家舍入会把 13.5 舍到偶数 14,两者在这里可分辨。
            var engine = Engine(new[] { "燃", "炽" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燃", 0); // 4 层
            int before = engine.Enemies[0].Hp;

            engine.EndTurn(); // 系数仍是基础 2:4 × 2 × 1.5 = 12,层数减到 3
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12));

            engine.Cast("炽"); // 系数 2 → 3(全局字段,不需要选目标)
            engine.EndTurn(); // 3 × 3 × 1.5 = 13.5 → floor 13
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 12 - 13),
                "floor(13.5) = 13;若用 Math.Round 会是 14");
        }

        [Test]
        public void Burn_LastStackRemovesTheStatus()
        {
            var engine = Engine(new[] { "燃" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            for (int i = 0; i < 4; i++) engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.Burn), Is.False,
                "烧完 4 层后状态条目被移除");
        }

        [Test]
        public void Burn_KillingTheLastEnemy_EmitsEnemyDied_AndWinsTheBattle()
        {
            // 评审 Important 1:Alive/Phase 两条断言都不经过 ResolveDefeat——它只发
            // EnemyDied 事件、不碰 Phase(Phase 由外层 CheckWin() 单独决定)。补事件断言
            // 才堵住「漏调 ResolveDefeat」这个洞:死亡动画/掉落飘字全靠这条事件驱动。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(hp: 6) });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyDied
                && e.TargetIndex == 0), Is.EqualTo(1),
                "灼烧致死要发 EnemyDied —— 那是 ResolveDefeat 分支唯一的可观测产物");
        }

        [Test]
        public void Burn_DoesNotTickAnAlreadyDeadEnemy()
        {
            // 评审 Minor 1:守卫「!enemy.Alive return」原来零覆盖。0 号被烧死后,
            // 灼烧状态条目还挂着(层数从 4 减到 3,>0 不会被移除)——若没有这道守卫,
            // 下一回合会对着尸体再结算一次,多发一条 BurnTick、多杀一次。
            var engine = Engine(new[] { "燃" }, new[] { Dummy(hp: 6), Dummy() });
            engine.Cast("燃", 0);
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Alive, Is.False);

            engine.EndTurn();
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BurnTick
                && e.TargetIndex == 0), Is.False, "死敌人不该再吃灼烧结算");
        }

        [Test]
        public void Burn_CrossingPhaseThreshold_EmitsBossPhase()
        {
            // 评审 Important 2:CheckBossPhase 分支原来零覆盖,而 Task 4(引爆)恰恰最容易
            // 一击连跨多阶。两阶 Boss,总血 13(3+10);灼烧一击 8 点打到 5,
            // ≤ 下一阶预算 10 就该换阶。BossPhaseJitterPercent 归零去掉随机浮动,阈值才可推算。
            var boss = new EnemyDef("靶", Element.Heart, 0, 0, phases: new[]
            {
                new BossPhaseDef("靶一阶", Element.Heart, 3, 0),
                new BossPhaseDef("靶二阶", Element.Heart, 10, 0),
            });
            var config = new BattleConfig
            {
                DropTable = new[] { "木" }, PlayerMaxHp = 200, BossPhaseJitterPercent = 0,
            };
            var engine = Engine(new[] { "燃" }, new[] { boss }, config);

            engine.Cast("燃", 0); // 4 层,心属性无生克:tick = 4 × 2 = 8
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(13), "总血 = 3 + 10");

            engine.EndTurn(); // 13 − 8 = 5,≤ 10(下一阶预算)→ 换阶
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(5));
            Assert.That(engine.Enemies[0].Alive, Is.True);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase), Is.True,
                "灼烧把 Boss 血量打过阶段阈值时要换阶,否则 Boss 停在旧阶段属性上继续挨打");
        }

        // ---- 不灭(炑)----

        [Test]
        public void NoDecay_StacksDoNotDrop()
        {
            var engine = Engine(new[] { "燋" }, new[] { Dummy() });
            engine.Cast("燋", 0);
            int before = engine.Enemies[0].Hp;

            // 评审 Important 2:不灭必须是段内持久(TurnsLeft < 0),不能是有限回合数——
            // 若被改成 TurnsLeft = 2 之类的有限值,恰好能撑过下面这两次 EndTurn 才过期,
            // 后面的层数/HP 断言全部撞车看不出来,得直接钉这个字段
            Assert.That(engine.Enemies[0].Statuses.Find(StatusKind.BurnNoDecay).TurnsLeft,
                Is.LessThan(0), "段内持久,不吃回合递减");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "不灭:结算后层数不掉");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 4), "2 层 × 2");

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8), "第二回合照样 2 层 × 2");
        }

        [Test]
        public void NoDecay_DoesNotChangeTickFormula()
        {
            // 不灭只挡减层,不碰伤害算式。用金属性靶子确认克制仍然生效
            var engine = Engine(new[] { "燋" }, new[] { new EnemyDef("锈", Element.Metal, 300, 0) });
            engine.Cast("燋", 0);
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 6), "2 × 2 × 1.5(火克金)");
        }

        [Test]
        public void NoDecay_SameCharDoesNotStack()
        {
            var engine = Engine(new[] { "燋", "燋" }, new[] { Dummy() });
            engine.Cast("燋", 0);
            engine.Cast("燋", 0);
            Assert.That(engine.Enemies[0].Statuses.All
                .Count(s => s.Kind == StatusKind.BurnNoDecay), Is.EqualTo(1),
                "同字重放只刷新,不挂两条");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(4),
                "但灼烧层数照常累加");
        }

        [Test]
        public void NoDecay_SurvivesSaveRoundTrip()
        {
            // 规格 §8:不灭挂在 EnemyState.Statuses 上,走既有的 StatusBag 存档路径。
            // 这条钉住新 StatusKind 真的跟着快照走了——漏了的话断点续爬回来层数就开始掉了
            var engine = Engine(new[] { "燋" }, new[] { Dummy() });
            engine.Cast("燋", 0);

            // 签名(BattleEngine.cs:232):Restore(snapshot, graph, config, cardLevels, enemyDefs)
            var restored = BattleEngine.Restore(
                engine.Capture(),
                Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 },
                new Dictionary<string, int>(),
                new Dictionary<string, EnemyDef> { ["靶"] = Dummy() });

            Assert.That(restored.Enemies[0].Statuses.Has(StatusKind.BurnNoDecay), Is.True);
            int before = restored.Enemies[0].Hp;
            restored.EndTurn();
            Assert.That(restored.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "读档回来仍然不衰减");
            Assert.That(restored.Enemies[0].Hp, Is.EqualTo(before - 4));
        }

        [Test]
        public void NoDecay_DoesNotAffectPlayerOwnBurn()
        {
            // 炑 是玩家出的牌,没有理由让自己身上的灼烧也不衰减。
            // 灯花(Sear)每次攻击给玩家挂 1 层,玩家灼烧走 _playerStatuses,是另一段结算。
            //
            // 期望值推演(EndTurn 顺序:敌人灼烧 → 玩家灼烧 → …… → 敌方行动):
            // 第 1 个 EndTurn:敌人灼烧段结算 0 号(不灭,层数不掉);玩家灼烧段此时玩家
            //   还没有灼烧(Cast 只烧了敌人),跳过;到敌方行动段,灯花攻击命中,
            //   RefreshBurn(_playerStatuses, 1) 把玩家灼烧从 0 刷新到 max(0,1)=1。
            //   → 断言 1:玩家灼烧 = 1。
            // 第 2 个 EndTurn:玩家灼烧段先结算——层数 1 × 系数 2 = 2 点伤害,然后 1 层
            //   自减到 0 被移除(这段不受敌人的 BurnNoDecay 影响,炑 挂在敌人身上,
            //   与 _playerStatuses 无关);紧接着到敌方行动段,灯花又攻击一次,
            //   RefreshBurn(_playerStatuses, 1) 把玩家灼烧从 0 刷新回 1。
            //   → 断言 2:玩家灼烧仍是 1(先掉到 0 又被灯花补上,不是「因为敌人不灭而堆积」)。
            var engine = Engine(new[] { "燋" },
                new[] { new EnemyDef("灯", Element.Heart, 300, 0, EnemyAbility.Sear) });
            engine.Cast("燋", 0);
            engine.EndTurn();                       // 灯花出手 → 玩家挂 1 层
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));
            engine.EndTurn();                       // 玩家灼烧结算 → 该减到 0(灯花又补 1 层)
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1),
                "本条只钉灯花刷新的稳态 = 1;『衰减这一步真的跑了』由 " +
                "NoDecay_PlayerBurnActuallyReachesZero_WhenSearIsSilenced 守,别删那条");
        }

        [Test]
        public void NoDecay_PlayerBurnActuallyReachesZero_WhenSearIsSilenced()
        {
            // 变异验证补丁:上面那条 NoDecay_DoesNotAffectPlayerOwnBurn 有个死角——
            // RefreshBurn 是 Math.Max(current, 1) 语义,灯花每回合都把玩家灼烧刷新回 1,
            // 于是「误把不灭也套用到玩家灼烧那一段」这个变异,最终层数照样停在 1,
            // 两个版本的断言 100% 撞车,测试杀不掉这处变异(已用真实变异跑过一遍验证)。
            // 这里让灯花闭嘴,不再有人刷新玩家灼烧,才能看清「层数会不会正常掉到 0」
            // 这件独立于灯花的事——如果不灭错误地也挡住了玩家灼烧的衰减,这里会停在 1。
            var engine = Engine(new[] { "燋", "噤" },
                new[] { new EnemyDef("灯", Element.Heart, 300, 0, EnemyAbility.Sear) });
            engine.Cast("燋", 0);                    // 敌人挂 2 层灼烧 + 不灭
            engine.EndTurn();                        // 灯花出手 → 玩家挂 1 层
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(1));

            engine.Cast("噤", 0);                     // 沉默灯花,下回合它不再刷新玩家灼烧
            engine.EndTurn();                        // 玩家灼烧独立衰减:1 − 1 = 0,被移除
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "灯花闭嘴后玩家灼烧正常掉到 0——敌人身上挂着的不灭不该管到这里");
        }

        [Test]
        public void NoDecay_SurvivesDispel_BecauseItsPolarityIsDebuff()
        {
            // 变异验证补丁:不灭挂的是 Polarity.Debuff(对敌人不利),而驱散(Dispel)
            // 只清 Polarity.Buff——两者刻意错位,不灭才不会被玩家自己的驱散连带清掉。
            // 若不灭的 Polarity 被错改成 Buff,这条会红(已用真实变异验证过)。
            var engine = Engine(new[] { "燋", "驱" }, new[] { Dummy() });
            engine.Cast("燋", 0);
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.BurnNoDecay), Is.True);

            engine.Cast("驱", 0);
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.BurnNoDecay), Is.True,
                "驱散清的是增益,不灭是减益,不该被清掉");

            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "驱散之后不灭仍生效,层数照样不掉");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 4));
        }

        [Test]
        public void NoDecay_IsolatedPerEnemy_DoesNotLeakToOtherEnemies()
        {
            // 评审 Important 1:此前所有不灭测试都是单敌人场景(NoDecay_SameCharDoesNotStack
            // 那条是两张牌打同一只怪,还是单敌人),没有一条能区分「查自己的 StatusBag」
            // 和「查全场任意一只」。3 怪混战很常见——这个洞会让「单体延长」变成
            // 「全体永久 DOT」,数值崩盘级,而当时的回归测试一声不吭
            var engine = Engine(new[] { "燋", "燃" }, new[] { Dummy(), Dummy() });
            engine.Cast("燋", 0); // 0 号:2 层 + 不灭
            engine.Cast("燃", 1); // 1 号:4 层,无不灭

            engine.EndTurn();
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "0 号带不灭,层数不掉");
            Assert.That(engine.Enemies[1].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "1 号没不灭,照常掉 1 层——不能被 0 号的不灭连带保护");
        }

        [Test]
        public void NoDecay_ClearedOnBossPhaseChange()
        {
            // 评审 Important 3(控制器裁定):Boss 换阶「新字新体」,不灭是挂在旧躯壳那份
            // 灼烧上的属性,躯壳换了没道理留着——否则一张 炑 就能买断整场 Boss 战,
            // 规格 §4.2 标成爆发链根的不灭就失去了「只延长一次」的边界
            var boss = new EnemyDef("靶", Element.Heart, 0, 0, phases: new[]
            {
                new BossPhaseDef("靶一阶", Element.Heart, 3, 0),
                new BossPhaseDef("靶二阶", Element.Heart, 10, 0),
            });
            var config = new BattleConfig
            {
                DropTable = new[] { "木" }, PlayerMaxHp = 200, BossPhaseJitterPercent = 0,
            };
            var engine = Engine(new[] { "燋", "燃" }, new[] { boss }, config);

            engine.Cast("燋", 0); // 2 层 + 不灭;心属性无生克:tick = 2 × 2 = 4
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(13), "总血 = 3 + 10");

            engine.EndTurn(); // 13 − 4 = 9,≤ 10(下一阶预算)→ 换阶,旧灼烧 + 不灭一起清零
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BossPhase), Is.True,
                "先确认真的换阶了,不然下面的断言没有意义");
            Assert.That(engine.Enemies[0].Statuses.Has(StatusKind.BurnNoDecay), Is.False,
                "新字新体:不灭跟着旧灼烧一起清掉,不能带进下一阶");

            // 新一阶重新挂一次纯灼烧(不带不灭),验证衰减恢复正常——不是被旧不灭悄悄续上
            engine.Cast("燃", 0); // 4 层
            engine.EndTurn(); // tick = 4 × 2 = 8(心属性无生克),层数正常 −1
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "新一阶的灼烧照常衰减一层,没有被旧不灭续上");
        }

        // ---- 立即结算(燥)----

        [Test]
        public void SettleNow_BurnsImmediatelyWithinThePlayerTurn()
        {
            var engine = Engine(new[] { "燃", "熯" }, new[] { Dummy() });
            engine.Cast("燃", 0);                    // 4 层
            int before = engine.Enemies[0].Hp;
            engine.Cast("熯", 0);                    // 立即结算一次
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 8), "4 层 × 2,当场掉血");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(3),
                "立即结算照常减一层");
        }

        [Test]
        public void SettleNow_EatsPotencyFromTheSameCard()
        {
            // 熇 的三个效果按数组顺序结算:先上 2 层,再把系数抬到 3,最后立即兑现
            var engine = Engine(new[] { "熇" }, new[] { Dummy() });
            int before = engine.Enemies[0].Hp;
            engine.Cast("熇", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 6),
                "2 层 × 系数 3(同一张牌抬的系数,立即结算就吃到了)");
        }

        [Test]
        public void SettleNow_EmitsBurnTickEvent()
        {
            var engine = Engine(new[] { "燃", "熯" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            engine.Cast("熯", 0);
            Assert.That(engine.LastEvents.Count(e => e.Kind == BattleEventKind.BurnTick),
                Is.EqualTo(1), "复用既有 BurnTick 事件,不新建事件种类");
        }

        [Test]
        public void SettleNow_OnCleanTarget_DoesNothing()
        {
            var engine = Engine(new[] { "熯" }, new[] { Dummy() });
            int before = engine.Enemies[0].Hp;
            engine.Cast("熯", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "没有灼烧就没得结算");
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.BurnTick), Is.False);
        }

        [Test]
        public void SettleNow_WithNoDecay_IsFreeAndKeepsStacks()
        {
            // 规格 §3.1 点名的连锁:不灭之下立即结算不掉层 = 免费兑现
            var engine = Engine(new[] { "燋", "熯" }, new[] { Dummy() });
            engine.Cast("燋", 0);                    // 2 层 + 不灭
            int before = engine.Enemies[0].Hp;
            engine.Cast("熯", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before - 4), "2 层 × 2");
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(2),
                "不灭之下立即结算也不掉层");
        }

        [Test]
        public void SettleNow_WithoutExplicitTarget_DoesNothingSafely()
        {
            // 熯 只有 BurnSettleNow,不在 NeedsTarget 白名单里(它不像 BurnSingle/Dispel
            // 那样在 Cast 里享受「单敌免选/强制选目标」),所以不传目标时 targetIndex 落在
            // 默认值 -1。case 里的 `if (targetIndex >= 0)` 守卫必须挡住这一步——
            // 去掉它会在这里让 SettleBurnOn(-1) 越界崩溃(2026-08-06 C1 同款教训)。
            var engine = Engine(new[] { "燃", "熯" }, new[] { Dummy() });
            engine.Cast("燃", 0);
            int before = engine.Enemies[0].Hp;
            var error = engine.Cast("熯"); // 不传目标,targetIndex 停在默认 -1
            Assert.That(error, Is.EqualTo(BattleError.None), "不该报错,只是不结算");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before), "没解析到目标,不结算,不崩溃");
        }

        [Test]
        public void SettleNow_TargetsTheCorrectEnemy_NotHardcodedToZero()
        {
            // 立即结算复用 SettleBurnOn,但这里独立验证「立即结算」这条调用路径本身传的
            // targetIndex 没被写死——多个敌人时只结算被点中的那个,不能把 1 号身上算出的
            // 伤害/事件飘到 0 号头上。此前所有 SettleNow_* 测试都只摆一个敌人在下标 0,
            // 写死成 0 的变异在那些用例里全都撞车看不出来
            var engine = Engine(new[] { "燃", "熯" }, new[] { Dummy(), Dummy() });
            engine.Cast("燃", 1);                     // 只给 1 号挂灼烧
            int before0 = engine.Enemies[0].Hp;
            int before1 = engine.Enemies[1].Hp;
            engine.Cast("熯", 1);                     // 立即结算 1 号
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(before0), "0 号没有灼烧,不该被扣血");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(before1 - 8), "1 号 4 层 × 2 当场掉血");
            var ticks = engine.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick).ToList();
            Assert.That(ticks.Count, Is.EqualTo(1));
            Assert.That(ticks[0].TargetIndex, Is.EqualTo(1), "事件要带对目标下标,不能写死成 0");
        }

        [Test]
        public void SettleNow_CanKillAndWin()
        {
            var engine = Engine(new[] { "燃", "熯" }, new[] { Dummy(hp: 8) });
            engine.Cast("燃", 0);
            engine.Cast("熯", 0);
            Assert.That(engine.Enemies[0].Alive, Is.False);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won),
                "玩家回合内也能靠灼烧结算杀敌并判胜");
        }
    }
}
