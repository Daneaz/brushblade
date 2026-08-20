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
            new CharDef("垒", Element.Heart,   // 筑:纯护盾(锁定「免疫先于护盾消耗」用)
                effects: new[] { new EffectDef(EffectKind.Shield, 10) }),
            new CharDef("堵", Element.Heart,   // 塞:免疫 1 次
                effects: new[] { new EffectDef(EffectKind.Immunity, 1) }),
            new CharDef("绝", Element.Heart,   // 杜:免疫 2 次
                effects: new[] { new EffectDef(EffectKind.Immunity, 2) }),
            new CharDef("峙", Element.Heart,   // 岿:免疫 1 次 + 立即净化
                effects: new[] { new EffectDef(EffectKind.Immunity, 1),
                                 new EffectDef(EffectKind.Cleanse, 0) }),
            new CharDef("斩", Element.Heart,   // 铡:伤 20,HP<25% 且非 Boss → 直接击杀
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 20,
                    executeBelowPercent: 25, executeKills: true) }),
            new CharDef("割", Element.Heart,   // 镰:伤 9,HP<30% → ×2
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 9,
                    executeBelowPercent: 30) }),
            new CharDef("扫荡", Element.Heart, // 剿:全体伤 6,对 HP<30% 的目标 ×2
                effects: new[] { new EffectDef(EffectKind.DamageAll, 6,
                    executeBelowPercent: 30) }),
            new CharDef("凿", Element.Heart,   // 1 点伤害,用来把敌人精确磨到目标血线
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 1) }),
            new CharDef("苏", Element.Heart,   // 活:复活一名阵亡召唤物
                effects: new[] { new EffectDef(EffectKind.Revive, 1) }),
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

        // 原用 ToppleBoss(BossChargeEvery=1)驱动——但该配置下 Boss 每 2 回合重铸一次倾覆,
        // 两个周期重合会让 Seal 被下一次重铸永远续上、测不出"到期"这件事,故改为绕开会持续
        // 重铸的 Boss,直接挂 Seal 隔离验证衰减本身。
        // 2026-08-16 全分支终审 Important 1:此前 TickPlayerStatuses 曾短暂错放在 YieldTurn
        // (拍尾,先递减后结算),一度被误判成这里也要多续一轮而错改名为「两轮」——实际上
        // 这条测试是直接注入 Seal(不经由敌人攻击这个中间环节),不受那次错位影响,数值从未
        // 变过:TickPlayerStatuses 现在紧跟在 BeginPlayerTurn 的结算之后、StartTurn 之前,每次
        // EndTurn 恰好递减一次并被同一次 StartTurn 读到——第 1 轮递减到 1(仍非零,照常扣 AP),
        // 第 2 轮减到 0 移除,AP 回满,总共只罚满 1 个玩家回合。改回原名。
        [Test]
        public void Seal_ExpiresAfterExactlyOnePenalizedTurn()
        {
            var engine = Engine(Array.Empty<string>(), new[] { Dummy(attack: 0) });
            int fullAp = engine.Ap;
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Seal, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = 2, SourceId = "倾覆",
            });

            engine.EndTurn(); // 第 1 轮:BeginPlayerTurn 递减到 1(仍非零),StartTurn 照常扣 1 点 AP
            Assert.That(engine.Ap, Is.EqualTo(fullAp - 1));
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.True);

            engine.EndTurn(); // 第 2 轮:再递减一次(1→0)移除,StartTurn 读到时已不存在
            Assert.That(engine.Ap, Is.EqualTo(fullAp), "只罚满一个玩家回合就解除");
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

        // 2026-08-16 CTB 改造:原为「1 次 EndTurn 后玩家灼烧层数为 1」(灯花本回合刚挂上,
        // 还没被结算),现为 0——灯花出手挂灼烧(敌方段)与玩家灼烧结算(BeginPlayerTurn,
        // spec §4.3「玩家那一拍」第 1 步)现在落在同一次 EndTurn() 调用内:灯花挂上 1 层后,
        // 同一次调用尾部的 BeginPlayerTurn 紧接着就把这 1 层烧掉、减到 0 移除。旧模型里这
        // 两件事分属相邻两次 EndTurn,新模型合并成一次。
        [Test]
        public void Sear_AppliesBurnToPlayerOnAttack()
        {
            var engine = Engine(Array.Empty<string>(), new[] { Searer() });
            engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
        }

        // 2026-08-16 CTB 改造:原假设"当回合刚挂的灼烧要等下一回合才结算",与
        // Sear_AppliesBurnToPlayerOnAttack 同一条因果链——灯花挂灼烧(敌方段)与玩家灼烧
        // 结算(BeginPlayerTurn)现在落在同一次 EndTurn() 调用内,"当场烧"取代了"下一回合烧"。
        // 于是每一回合都是「普攻 30 + 当场结算的灼烧 20」= 50,而不是旧模型里"这回合只挨
        // 普攻、下回合才补上灼烧"那种交错节奏;稳态灼烧层数也从"净 0(挂 1 减 1)"变成
        // "回合末恒为 0"(当场就烧没了,不会跨回合存在)。
        [Test]
        public void PlayerBurn_TicksThenDecays_AndIgnoresWuxing()
        {
            // 玩家没有五行属性,灼烧结算不套任何倍率:1 层 × 系数 20 = 20 伤。
            // 灯花攻 30 → 挂灼烧那一记也打 30,同一次 EndTurn 内当场结算掉这 1 层。
            // 灯花攻与玩家血量在这一条里随灼烧系数一同 ×10,断言才是旧值的机械 ×10。
            var engine = Engine(Array.Empty<string>(), new[] { Searer(attack: 30) },
                config: new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500 });
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(450), "第 1 回合:普攻 30 + 当场结算的灼烧 20");
            engine.EndTurn();
            Assert.That(engine.PlayerHp, Is.EqualTo(400), "第 2 回合:同样是普攻 30 + 当场结算的灼烧 20");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0),
                "挂上的这层当场就被结算掉,不会跨回合留存");
        }

        // 2026-08-16 CTB 改造:原为「稳态 1 层」,现为「稳态 0 层」——与 Sear_AppliesBurnToPlayerOnAttack
        // 同一条因果链,灼烧当场就被结算(SettlePlayerBurn 每次只 -1 层,不是清零)。这条测试真正
        // 守的"不雪球"不变量并未失效:RefreshBurn 若退化回累加语义,挂上的层数就会 >1,
        // SettlePlayerBurn 那次 -1 之后仍会剩下 ≥1 层——所以"结算后归零"本身就是新的雪球探针,
        // 断言从 1 改成 0 后判别力不变。
        [Test]
        public void PlayerBurn_StaysAtOneStack_DoesNotSnowball()
        {
            // 子项目 C 的烓因为「每回合挂 3、只减 1」净 +2 而失控。灯花挂 1 减 1,当场结算归零。
            var engine = Engine(Array.Empty<string>(), new[] { Searer() }, startingHp: 200,
                config: new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 });
            for (int turn = 0; turn < 5; turn++) engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
        }

        // 2026-08-16 CTB 改造:同上一条因果链,稳态从 1 层改为 0 层。
        [Test]
        public void PlayerBurn_TwoSearers_StaysAtOneStack()
        {
            // 2026-08-06 I1:Sear 原先走 ApplyBurn 的累加语义,单只灯花净 0(挂 1 减 1),
            // 但 BuildFloor 是有放回抽取,同场可能出现多只灯花,N 只就净 +(N−1)/回合,
            // 雪球失控(实测 4 只第 6 回合单灼烧 38 伤/回合)。改走 RefreshBurn(取较大值)后,
            // 两只同时打,每回合仍只挂 1 层、当场结算归零——若退化回累加语义,这里会读到 ≥1。
            var engine = Engine(Array.Empty<string>(), new[] { Searer(), Searer() }, startingHp: 200,
                config: new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200 });
            for (int turn = 0; turn < 5; turn++) engine.EndTurn();
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Burn), Is.EqualTo(0));
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

        [Test]
        public void PlayerBurn_KillsPlayer_EvenWithHealOverTimeSameTurn()
        {
            // 2026-08-06 全分支终审 C2:旧代码把玩家灼烧的判负推迟到回合尾部,期间的 HoT
            // 循环会先把血救回去。归零即死是拍板口径,持续治疗不能救。
            var engine = Engine(Array.Empty<string>(), new[] { Dummy() }, startingHp: 2);
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = -1,
            });
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.HealOverTime, Polarity = StatusPolarity.Buff,
                Magnitude = 5, TurnsLeft = 3, SourceId = "滋#0",
            });

            engine.EndTurn(); // 灼烧 1 层 × 系数 2 = 2 伤,正好烧死;HoT 排在灼烧结算之后,不该救回

            Assert.That(engine.PlayerHp, Is.EqualTo(0));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Lost));
        }

        // 2026-08-16 CTB 改造:原名/原断言守的是"玩家灼烧应抢在同回合的召唤物清场之前烧死玩家"
        // ——这条前提建立在旧模型"灼烧结算点 = YieldTurn 那一刻"上,而这正是本任务(时序归属
        // 重排)要推翻的东西。新模型下玩家灼烧挪到了 BeginPlayerTurn(spec §4.3「玩家那一拍」
        // 第 1 步),是这一次 EndTurn() 调用里**最后**才会走到的一步——召唤物在敌方段之前
        // 出手清场,CheckWin() 判 Won 的时候,战斗循环当场停止(AdvanceOnce 见 Phase 已经不是
        // PlayerTurn 就直接返回 false),灼烧根本没有机会被结算。这不是"同一瞬间两件事抢判负/
        // 判胜优先级"的旧场景(那条口径不变,见 spec §4.3「同归于尽时玩家阵亡优先」)——而是
        // 在 CTB 的时间轴上,召唤物出手确确实实发生在玩家下一次轮到自己(灼烧结算点)之前,
        // 敌人先死是时间上的事实,不是判定顺序的巧合。断言已经改成"灼烧尚未结算(层数仍是 1、
        // PlayerHp 不变),战斗以 Won 收场"——原名 PlayerBurn_KillsPlayer_… 与断言的结果正相反
        // (全分支终审 Important 5 点名的两条名不副实测试之一),这里一并改准。
        [Test]
        public void PlayerBurn_NeverSettles_WhenSummonClearsLastEnemyFirst()
        {
            var engine = Engine(new[] { "素" }, new[] { Dummy(hp: 3, attack: 0) }, startingHp: 2);
            engine.Cast("素"); // 召唤攻 3 的木,回合末反击本该同回合秒掉这只 3 血靶
            engine.PlayerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = 1, TurnsLeft = -1,
            });

            engine.EndTurn(); // 召唤物先清场判 Won,玩家灼烧(挪到 BeginPlayerTurn)根本没轮到

            Assert.That(engine.PlayerHp, Is.EqualTo(2), "灼烧结算点在召唤物清场之后,这一拍没轮到");
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        // ---- 驱散 ----

        [Test]
        public void Dispel_MinusOne_WithoutExplicitTargetIndex_DoesNotThrow()
        {
            // 2026-08-06 全分支终审 C1:NeedsTarget 漏了 Dispel,UI 判定为"不需要选目标",
            // targetIndex 停在默认的 -1,ApplyEffects 里 _enemies[-1] 直接越界崩溃。
            // 场上只有一个敌人时,「单敌免选」应自动锁定它。
            var engine = Engine(new[] { "扫" }, new[] { Dummy() });
            GiveTwoBuffs(engine.Enemies[0]);
            Assert.DoesNotThrow(() => engine.Cast("扫"));
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(0),
                "自动锁定唯一存活敌人并清空增益");
        }

        /// <summary>给敌人挂两条可驱散的增益(与标点小妖加攻同形:AttackBuff、段内持久、可叠)。
        /// Magnitude 是**百分点**(2026-08-12 敌我单位统一后),取 50 与标点小妖实际发放的量一致。</summary>
        private static void GiveTwoBuffs(EnemyState enemy)
        {
            for (int i = 0; i < 2; i++)
                enemy.Statuses.Apply(new StatusEffect
                {
                    Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff,
                    Magnitude = 50, TurnsLeft = -1, SourceId = $"妖#{i}",
                });
        }

        [Test]
        public void Dispel_MinusOne_RemovesEveryBuff()
        {
            // 靶子必须有非 0 基础攻击:AttackBuff 是比值,攻 0 的敌人加多少百分比都还是 0,
            // 攻击力这两条断言就废了(只剩 TotalMagnitude 一条在守)。
            var engine = Engine(new[] { "扫" }, new[] { Dummy(attack: 8) });
            GiveTwoBuffs(engine.Enemies[0]);
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(16), "8 × (100 + 50 + 50) ÷ 100");
            engine.Cast("扫", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(0));
            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(8), "驱散后回到基础攻击");
        }

        [Test]
        public void Dispel_Counted_RemovesExactlyThatMany()
        {
            var engine = Engine(new[] { "剐" }, new[] { Dummy() });
            GiveTwoBuffs(engine.Enemies[0]);
            engine.Cast("剐", 0);
            Assert.That(engine.Enemies[0].Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(50),
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
                Assert.That(enemy.Statuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(50),
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
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 30, TurnsLeft = -1, SourceId = "铠",
            });
            engine.Cast("扫", 0);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DefenseBuff), Is.EqualTo(30));
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
                Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff,
                Magnitude = 30, TurnsLeft = -1, SourceId = "铠",
            });

            engine.Cast("涤", 0);

            Assert.That(engine.PlayerStatuses.Has(StatusKind.Seal), Is.False);
            Assert.That(engine.PlayerStatuses.Has(StatusKind.Burn), Is.False);
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DefenseBuff), Is.EqualTo(30),
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
            // 护盾必须原封不动地留到免疫用完之后 —— 用非零护盾锁住顺序:
            // 若实现误把护盾放在免疫之前吃,这条断言会因为护盾掉一截而变红。
            var engine = Engine(new[] { "垒", "堵" }, new[] { Dummy(attack: 8) });
            engine.Cast("垒");
            int shieldBefore = engine.PlayerShield;
            Assert.That(shieldBefore, Is.GreaterThan(0), "护盾字必须先立起非零护盾,测试才有区分力");
            engine.Cast("堵", 0);
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

        // ---- 斩杀 ----

        /// <summary>把首个敌人精确磨到 targetHp。`EnemyState.Hp` 的 setter 是 internal,
        /// 测试程序集改不到,所以用一张 1 点伤害的字磨——AP 开到够大,一回合内磨完,
        /// 期间敌人攻 0 不还手,不干扰血线。</summary>
        private static BattleEngine EngineWithEnemyAt(EnemyDef enemy, int targetHp, params string[] cards)
        {
            int hits = enemy.MaxHp - targetHp;
            var library = new List<string>(Enumerable.Repeat("凿", hits));
            library.AddRange(cards);
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = hits + 20 },
                library, Array.Empty<string>(), new[] { enemy }, seed: 1);
            for (int i = 0; i < hits; i++) engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(targetHp), "磨血辅助本身没磨准");
            return engine;
        }

        private static EnemyDef Target(int maxHp) => new("靶", Element.Heart, maxHp, 0);

        private static EnemyDef BossTarget(int maxHp) =>
            new("覆", Element.Heart, maxHp, 0,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, maxHp, 0) });

        [Test]
        public void Execute_KillsNonBossBelowThreshold()
        {
            // 上限 100、现血 24 = 24% < 25% → 直接击杀
            var engine = EngineWithEnemyAt(Target(100), 24, "斩");
            engine.Cast("斩", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(0));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Execute_DamageEvent_ReportsActualHpLost_NotZeroOrFaceValue()
        {
            // 处决把敌人直接置 0 血,但事件要报「实际抹掉的血量」(24),不是 0、
            // 也不是字面伤害值 20 —— 报 0 会让表现层每回合飘「-0」还带白闪震屏。
            var engine = EngineWithEnemyAt(Target(100), 24, "斩");
            engine.Cast("斩", 0);
            var damage = engine.LastEvents.Single(e => e.Kind == BattleEventKind.Damage);
            Assert.That(damage.Amount, Is.EqualTo(24));
        }

        [Test]
        public void Execute_JudgesHpBeforeTheHit_NotAfter()
        {
            // 现血 26 = 26% ≥ 25%:打之前不到线,只吃普通 20 伤剩 6。
            // 若改成打之后判定,20 伤打完剩 6 = 6% 就会触发处决,结果完全不同。
            var engine = EngineWithEnemyAt(Target(100), 26, "斩");
            engine.Cast("斩", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(6), "普通结算,没被处决");
        }

        [Test]
        public void Execute_DoesNotKillBoss()
        {
            // 上限 200、现血 40 = 20% < 25%,但 Boss 免疫处决 → 只吃普通 20 伤剩 20。
            // 血线特意选得让「处决」与「普通伤害」的结果不同,否则测不出区别。
            var engine = EngineWithEnemyAt(BossTarget(200), 40, "斩");
            engine.Cast("斩", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(20), "Boss 退化成普通 20 伤");
            Assert.That(engine.Enemies[0].Alive, Is.True);
        }

        [Test]
        public void ExecuteBonus_DoesNotDoubleAboveThreshold()
        {
            // 现血 31 = 31% ≥ 30% → 普通 9 伤
            var engine = EngineWithEnemyAt(Target(100), 31, "割");
            engine.Cast("割", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(22));
        }

        [Test]
        public void ExecuteBonus_DoublesDamageBelowThreshold()
        {
            // 现血 29 = 29% < 30% → 9 × 2 = 18
            var engine = EngineWithEnemyAt(Target(100), 29, "割");
            engine.Cast("割", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(11));
        }

        [Test]
        public void ExecuteBonus_AppliesToBossToo()
        {
            // 免疫的只是「直接击杀」,不是「残血加伤」。现血 20/100 = 20% < 30% → 18 伤
            var engine = EngineWithEnemyAt(BossTarget(100), 20, "割");
            engine.Cast("割", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(2));
        }

        [Test]
        public void ExecuteBonus_UsesTotalBossHpPool_NotSinglePhaseDefValue()
        {
            // 2026-08-06 M1:BelowExecuteThreshold 的阈值判定要用 EnemyState.MaxHp(Boss 全部
            // 阶段血量之和 = 总血池),不是 EnemyDef.MaxHp(构造时传入的单阶段数值,对分阶段
            // Boss 是错的)。故意让两者天差地别(Def.MaxHp 传 1,真实总血池是 60+40=100)来
            // 锁住这条——现有两条 Boss 斩杀测试都用单阶段 Boss,两者恒等,换成 Def.MaxHp 也不会红。
            var def = new EnemyDef("覆", Element.Heart, maxHp: 1, attack: 0,
                phases: new[]
                {
                    new BossPhaseDef("覆", Element.Heart, 60, 0),
                    new BossPhaseDef("阶", Element.Heart, 40, 0),
                });
            int hits = 100 - 29; // 磨到 29 = 29% < 30% → 该享受 ExecuteBonus ×2
            var library = new List<string>(Enumerable.Repeat("凿", hits)) { "割" };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = hits + 20 },
                library, Array.Empty<string>(), new[] { def }, seed: 1);
            for (int i = 0; i < hits; i++) engine.Cast("凿", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(29), "磨血辅助本身没磨准");

            engine.Cast("割", 0);

            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(11), "9 × 2 = 18 伤,29 − 18 = 11");
        }

        [Test]
        public void ExecuteBonus_TargetAll_JudgesEachEnemySeparately()
        {
            // 两只上限都 100:甲磨 80 下到 20(20% < 30% → 12 伤),
            // 乙磨 50 下到 50(50% ≥ 30% → 6 伤)。共需 130 张「凿」。
            var library = new List<string>(Enumerable.Repeat("凿", 130)) { "扫荡" };
            var engine = new BattleEngine(Graph(),
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = 200 },
                library, Array.Empty<string>(),
                new[] { Target(100), Target(100) }, seed: 1);
            for (int i = 0; i < 80; i++) engine.Cast("凿", 0);   // 甲 → 20
            for (int i = 0; i < 50; i++) engine.Cast("凿", 1);   // 乙 → 50
            engine.Cast("扫荡", 0);
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(8), "20 − 12");
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(44), "50 − 6");
        }

        // ---- 复活 ----

        [Test]
        public void Revive_RestoresDeadSummonToHalfHp()
        {
            var engine = Engine(new[] { "素", "苏" }, new[] { Dummy(attack: 40) });
            engine.Cast("素");
            engine.EndTurn();                         // 10 血召唤物挨 40,死透
            Assert.That(engine.Summons[0].Alive, Is.False);

            engine.Cast("苏", 0);
            Assert.That(engine.Summons[0].Alive, Is.True);
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(5), "半血,向上取整");
            Assert.That(engine.Summons[0].ActionMeter, Is.EqualTo(0), "重新攒节拍,不继承死前余额");
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0), "盾不跟着复活");
        }

        [Test]
        public void Revive_KeepsPassiveIdentity()
        {
            // Passive 是这只召唤物的身份,复活后必须还在
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("棘", Element.Wood,
                    effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 0,
                        summonChar: "木", passive: new SummonPassive { Thorns = 3 }) }),
                new CharDef("苏", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.Revive, 1) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                new[] { "棘", "苏" }, Array.Empty<string>(),
                new[] { Dummy(attack: 40) }, seed: 1);
            engine.Cast("棘");
            engine.EndTurn();
            engine.Cast("苏", 0);
            Assert.That(engine.Summons[0].Passive, Is.Not.Null);
            Assert.That(engine.Summons[0].Passive.Thorns, Is.EqualTo(3));
        }

        [Test]
        public void Revive_WithNoDeadSummon_IsANoOp()
        {
            // 与「无敌人时出 AOE」同口径:消耗 AP 但无效果,不抛异常
            var engine = Engine(new[] { "苏" }, new[] { Dummy() });
            Assert.That(engine.Cast("苏", 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.AliveSummonCount, Is.EqualTo(0));
        }

        [Test]
        public void Revive_RefusesWhenSummonCapIsFull()
        {
            // 死尸占着槽位,复活不新增条目但存活数 +1 —— 满员时必须停手,否则会变成 7 只。
            // 构造:召满 6 只 → 敌人打死最前一只(场上多一具尸体)→ 补召 1 只回到 6 只存活
            // → 此时出「苏」,尸体还在但已满员。
            var graph = new RecipeGraph(new[]
            {
                new CharDef("木", Element.Wood),
                new CharDef("丛", Element.Wood,   // 一次召 6 只,正好塞满上限
                    effects: new[] { new EffectDef(EffectKind.Summon, 4, summonCount: 6, summonChar: "木") }),
                new CharDef("苗", Element.Wood,   // 补位用的单召
                    effects: new[] { new EffectDef(EffectKind.Summon, 4, summonCount: 1, summonChar: "木") }),
                new CharDef("苏", Element.Heart,
                    effects: new[] { new EffectDef(EffectKind.Revive, 1) }),
            });
            var engine = new BattleEngine(graph,
                new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 200, ApPerTurn = 10 },
                new[] { "丛", "苗", "苏" }, Array.Empty<string>(),
                new[] { new EnemyDef("靶", Element.Heart, 500, 4) }, seed: 1);

            engine.Cast("丛");
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));
            engine.EndTurn();                      // 敌人攻 4 打死最前一只(4 血)
            Assert.That(engine.AliveSummonCount, Is.EqualTo(5));
            engine.Cast("苗");                      // 补回 6 只存活,场上留着那具尸体
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6));

            engine.Cast("苏", 0);
            Assert.That(engine.AliveSummonCount, Is.EqualTo(6),
                "满员时复活必须停手,不许变成 7 只");
        }
    }
}
