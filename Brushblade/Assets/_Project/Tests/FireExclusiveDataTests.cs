using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 7:火系 44 条专属特性的数据落表 —— 读真实 chars.json,每类机制至少一条集成测试。
    /// 单元级的机制测试在 FireSharedExtTests / FireBurnOpsTests / FireRiderTests / FireHookTests / FireSingleOpsTests;
    /// 这里只证明「特性表那一行写出来的配置,在真实字上真的接上了」。
    ///
    /// 夹具:敌人一律心属性(生克 1.0×)、0 甲;玩家攻击 100、暴击率 0(配置缺省)。期望值尽量写成前后差,
    /// 不写死受卡等级 / 本体数值影响的绝对伤害(数值归 Plan F 定标)。</summary>
    public class FireExclusiveDataTests
    {
        private const int Hp = 100000;

        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(string id, int level, params EnemyDef[] enemies) =>
            new(Graph, Config, new[] { id, id, id }, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: new Dictionary<string, int> { [id] = level });

        private static EnemyDef Mob(int hp = Hp, int attack = 0) => new("怔", Element.Heart, hp, attack);

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        private static void SetBurn(BattleEngine b, int i, int stacks) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = 100 });

        private static List<BattleEvent> On(BattleEngine b, int i, BattleEventKind kind) =>
            b.LastEvents.Where(e => e.Kind == kind && e.TargetIndex == i).ToList();

        private static string Key(string id, TraitSlot slot, TraitFace face) => BattleEngine.TraitKey(id, slot, face);

        // ================= 灼操作族 =================

        [Test]
        public void Zao_ScorchedEarth_DoublesBurn_TraitWeakenIsOwnSource()
        {
            // 燥 Lv8·燃 焦土:`BurnScale 200` + `Weaken 30` `turns 2`。Lv3 燃面上灼 3(续火看出字前,不触发)→ 翻倍 6
            var b = Battle("燥", 8, Mob());
            Assert.That(b.Cast("燥", 0), Is.EqualTo(BattleError.None));
            Assert.That(Burn(b, 0), Is.EqualTo(6), "3 层 × 200%");
            var curses = b.Enemies[0].Statuses.All.Where(s => s.Kind == StatusKind.Curse).ToList();
            var scorched = curses.SingleOrDefault(c => c.TraitKey == Key("燥", TraitSlot.Lv8, TraitFace.Feature));
            Assert.That(scorched, Is.Not.Null, "焦土的减攻独立来源(G11),不并进本体那条");
            Assert.That(scorched.TurnsLeft, Is.EqualTo(2));
            Assert.That(curses.Any(c => c.TraitKey == null && c.TurnsLeft == 3), Is.True, "本体减攻(Lv3 3 回合)仍在");
        }

        [Test]
        public void Lie_BurningCamps_RaisesEveryoneToHighest()
        {
            // 烈 Lv8·攻 火烧连营:本体全体 +灼 3(Lv3)后拉平到全场最高
            var b = Battle("烈", 8, Mob(), Mob(), Mob());
            SetBurn(b, 0, 5);
            Assert.That(b.Cast("烈", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2) }, Is.EqualTo(new[] { 8, 8, 8 }));
        }

        [Test]
        public void Zha_StartlingBlast_DetonatesAll_KeepsHalf()
        {
            // 炸 Lv8·攻 惊爆:`Detonate` `pick All` `retain 50`。本体全体 +灼 2(Lv3)→ 8 / 2 层,引爆后保留 ⌊N/2⌋
            var b = Battle("炸", 8, Mob(), Mob());
            SetBurn(b, 0, 6);
            Assert.That(b.Cast("炸", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(new[] { Burn(b, 0), Burn(b, 1) }, Is.EqualTo(new[] { 4, 1 }));
            Assert.That(On(b, 0, BattleEventKind.BurnTick).Count, Is.GreaterThanOrEqualTo(1), "引爆照全额兑现");
        }

        [Test]
        public void Yi_SkyBurn_RetainsExactlyOneThird_AtMultiplesOfThree()
        {
            // 燚 Lv8·攻 焚天:「之后每名敌人保留 1/3 层」。保留按 ⌊N × R%⌋ 算,R 写 33 时 3 / 6 / 9 层会少 1 层;
            // 取真实数据那条 Detonate,放进试字里对 3 / 6 / 9 层各引爆一次(真字的本体会先叠灼,凑不出这三档)
            var skyBurn = Graph.Get("燚").Traits
                .Single(t => t.Slot == TraitSlot.Lv8 && t.Face == TraitFace.Attack && t.Name == "焚天")
                .Effects.Single(e => e.Kind == EffectKind.Detonate);
            var probe = new CharDef("试", Element.Heart, effects: new[] { skyBurn });
            var b = new BattleEngine(RebalanceFixture.Graph(probe), Config, new[] { "试", "试", "试" },
                Array.Empty<string>(), new[] { Mob(), Mob(), Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["试"] = 1 });
            SetBurn(b, 0, 3);
            SetBurn(b, 1, 6);
            SetBurn(b, 2, 9);
            Assert.That(b.Cast("试", -1), Is.EqualTo(BattleError.None));
            Assert.That(new[] { Burn(b, 0), Burn(b, 1), Burn(b, 2) }, Is.EqualTo(new[] { 1, 2, 3 }), "⌊N/3⌋");
        }

        [Test]
        public void Yan_FlameBlade_TwoHits_SettleAfterEach()
        {
            // 炎 Lv8·攻 炎刃:`Reshape` `hits 2` `hitSettle` —— 每击后目标的灼结算一次(不减层)
            var b = Battle("炎", 8, Mob());
            SetBurn(b, 0, 3);
            Assert.That(b.Cast("炎", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var order = b.LastEvents.Where(e => e.TargetIndex == 0
                    && (e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.BurnTick))
                .Select(e => e.Kind).ToArray();
            Assert.That(order, Is.EqualTo(new[]
                { BattleEventKind.Damage, BattleEventKind.BurnTick, BattleEventKind.Damage, BattleEventKind.BurnTick }));
            Assert.That(Burn(b, 0), Is.EqualTo(3 + 4), "keep 结算不减层;本体再 +灼 4(Lv3)");
        }

        [Test]
        public void Yi_FourFlames_EveryEnemyTakesFourHitsAndFourBurn()
        {
            // 燚 Lv5·攻 四炎(V2):全体每人 4 击、每击附灼 1;本体全体 +灼 5(Lv3)
            var b = Battle("燚", 5, Mob(), Mob(), Mob());
            Assert.That(b.Cast("燚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(On(b, i, BattleEventKind.Damage).Count, Is.EqualTo(4), $"敌人 {i} 吃 4 击");
                Assert.That(Burn(b, i), Is.EqualTo(4 + 5), $"敌人 {i} 每击 +1 共 4 层 + 本体 5 层");
            }
        }

        [Test]
        public void Zao_HeartFire_PortionDetonatesHalf()
        {
            // 燥 Lv8·攻 燥火攻心:`Detonate` `portion 50` —— 只引爆 ⌊N/2⌋ 层。出字时目标 4 层 + 本体 3 层(Lv3)= 7 → 引爆 3、剩 4
            var b = Battle("燥", 8, Mob());
            SetBurn(b, 0, 4);
            Assert.That(b.Cast("燥", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(Burn(b, 0), Is.EqualTo(4));
        }

        // ================= 灼附着族 =================

        [Test]
        public void Zao_Drought_HealBlockRidesOnBothFaces()
        {
            // 燥 Lv4·两面 干涸:`HealBlock` `pick BurnedByThisCast` `rider Burn`
            foreach (bool attack in new[] { true, false })
            {
                var b = Battle("燥", 4, Mob());
                Assert.That(b.Cast("燥", 0, attackMode: attack), Is.EqualTo(BattleError.None));
                var block = b.Enemies[0].Statuses.Find(StatusKind.HealBlock);
                Assert.That(block, Is.Not.Null, attack ? "攻击面" : "燃面");
                Assert.That(block.TraitKey, Is.EqualTo(Key("燥", TraitSlot.Lv4, TraitFace.Both)));
            }
        }

        [Test]
        public void Fen_BurningCity_DeathSettlesRemainingBurnOnOthers()
        {
            // 焚 Lv5·燃 焚城:被本字点灼的敌人死亡时,对其余敌人结算一次它剩下的灼(不改它们的层数)
            var b = Battle("焚", 5, Mob(hp: 1), Mob());
            Assert.That(b.Cast("焚", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.BurnBurstMark), Is.True, "焚城载体挂上");
            Assert.That(b.Cast("焚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(On(b, 1, BattleEventKind.BurnTick).Count, Is.EqualTo(1), "死者的残灼对另一名敌人结算一次");
            Assert.That(Burn(b, 1), Is.EqualTo(4), "焚城不改层数:只有攻击面本体的 4 层");

            // 对照:没有焚城载体时同一下不会烧到旁人
            var plain = Battle("焚", 5, Mob(hp: 1), Mob());
            Assert.That(plain.Cast("焚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(On(plain, 1, BattleEventKind.BurnTick).Count, Is.EqualTo(0));
        }

        // ================= 反应击杀链(R4)=================

        private static BattleEngine Duo(string a, int la, string c, int lc, params EnemyDef[] enemies) =>
            new(Graph, Config, new[] { a, a, a, c, c, c }, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: new Dictionary<string, int> { [a] = la, [c] = lc });

        [Test]
        public void Bao_ChainBlast_AtMostTwicePerCast()
        {
            // 爆 Lv8·攻 连爆:`DamageSingle 0` `All` `bodyPercent 100` `limit 2`(击杀时)。本体全体一击秒掉 3 只 → 只连爆 2 次
            var b = Battle("爆", 8, Mob(hp: 1), Mob(hp: 1), Mob(hp: 1), Mob());
            Assert.That(b.Cast("爆", -1, attackMode: true), Is.EqualTo(BattleError.None));
            var hits = On(b, 3, BattleEventKind.Damage);
            Assert.That(hits.Count, Is.EqualTo(1 + 2), "本体一击 + 连爆 2 次(limit 2)");
            Assert.That(hits[1].Amount, Is.EqualTo(hits[0].Amount).Within(1), "连爆 = 本体 100%");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void Bao_ChainBlast_ReactionKill_DoesNotChainAgain_NorTriggerBurningCity()
        {
            // 同一夹具先量出敌人 1(带灼,吃爆燃)挨的本体一击 d;正式那场给它 d + 1 血:本体打不死、连爆打死
            var probe = Duo("焚", 5, "爆", 8, Mob(hp: 1), Mob(), Mob());
            Assert.That(probe.Cast("焚", 1), Is.EqualTo(BattleError.None));
            Assert.That(probe.Cast("爆", -1, attackMode: true), Is.EqualTo(BattleError.None));
            var probeHits = On(probe, 1, BattleEventKind.Damage);
            Assert.That(probeHits.Count, Is.EqualTo(2), "前提:本体一击 + 连爆 1 次");
            int d = probeHits[0].Amount;

            // 敌人 1 先吃焚 Lv5·燃 的焚城载体与灼;再用爆出字:敌人 0 被本体打死 → 连爆 1 次 → 打死敌人 1
            var b = Duo("焚", 5, "爆", 8, Mob(hp: 1), Mob(hp: d + 1), Mob());
            Assert.That(b.Cast("焚", 1), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.BurnBurstMark), Is.True, "前提:焚城载体挂上");
            Assert.That(b.Cast("爆", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Alive, Is.False, "前提:连爆打死了敌人 1");
            Assert.That(On(b, 2, BattleEventKind.Damage).Count, Is.EqualTo(1 + 1),
                "反应里的击杀不再入队连爆(R4,虽然 limit 2 还剩 1 次)");
            Assert.That(On(b, 2, BattleEventKind.BurnTick).Count, Is.EqualTo(0), "反应里的击杀不触发焚城(R4)");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void Fen_FierceBurn_InCastExtraStrikeKill_TriggersBurningCityOnce()
        {
            // 口径(终审 Important 2):烈焚 / 星火的追加一击是出字本身(TriggerDepth 0),它打死带焚城载体的敌人 → 焚城入队 1 次;
            // 对照上一条:连爆(反应)打死的不入队
            var probe = Duo("焚", 5, "焚", 5, Mob(), Mob());
            Assert.That(probe.Cast("焚", 0), Is.EqualTo(BattleError.None));
            Assert.That(probe.Cast("焚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            var probeHits = On(probe, 0, BattleEventKind.Damage);
            Assert.That(probeHits.Count, Is.EqualTo(2), "前提:本体一击 + 烈焚追加一击(MostBurn = 敌人 0)");
            int body = probeHits[0].Amount;

            var b = Duo("焚", 5, "焚", 5, Mob(hp: body + 1), Mob());
            Assert.That(b.Cast("焚", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Cast("焚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:追加一击打死了敌人 0");
            Assert.That(On(b, 1, BattleEventKind.BurnTick).Count, Is.EqualTo(1), "焚城入队并兑现恰好 1 次");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        // ================= 敌人出手前 / 受击挂点 =================

        [Test]
        public void Zha_Mine_ExplodesBeforeAttack_ForAttackBodyTimesTwo()
        {
            // 炸 Lv5·燃 埋雷:`Mine` `bodyPercent 200`(燃面没有伤害,按攻击面本体解析,Task 4 Ruling 1)
            var b = Battle("炸", 5, Mob(attack: 10));
            Assert.That(b.Cast("炸", 0), Is.EqualTo(BattleError.None));
            var mine = b.Enemies[0].Statuses.Find(StatusKind.Mine);
            Assert.That(mine, Is.Not.Null);
            int body = Graph.Get("炸").AttackEffects.First(e => e.Kind == EffectKind.DamageSingle).Value;
            Assert.That(mine.Magnitude, Is.GreaterThanOrEqualTo(body * 2), "本体 ×2(再吃卡等级)");
            b.EndTurn();
            var blast = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.Mine).ToList();
            Assert.That(blast.Count, Is.EqualTo(1), "出手前爆炸一次");
            Assert.That(blast[0].Amount, Is.EqualTo(mine.Magnitude));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Mine), Is.False, "一次性");
        }

        [Test]
        public void Yi_Backlash_SettlesBurnBeforeAttack()
        {
            // 燚 Lv6·攻 焚身:带本字灼的敌人出手前先受一次灼烧结算(正常减层)。
            // Lv4 四火让它自己回合那次结算不减层,所以一整个敌方段:两次 BurnTick、层数只掉 1
            var b = Battle("燚", 6, Mob(attack: 1));
            Assert.That(b.Cast("燚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.BurnBacklashMark), Is.True);
            int before = Burn(b, 0);
            b.EndTurn();
            Assert.That(On(b, 0, BattleEventKind.BurnTick).Count, Is.EqualTo(2), "回合开始一次 + 出手前一次");
            Assert.That(Burn(b, 0), Is.EqualTo(before - 1));
        }

        [Test]
        public void Lie_FlameGuard_HitPlayerBurnsAttacker()
        {
            // 烈 Lv8·燃 烈焰护身:`BurnAll 2` + `Retaliate` `onHit` `BurnSingle 2`
            var b = Battle("烈", 8, Mob(attack: 10));
            Assert.That(b.Cast("烈", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Has(StatusKind.Retaliate), Is.True);
            int before = Burn(b, 0);
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(before - 1 + 2), "回合开始灼 −1,打到玩家后 +2");
        }

        // ================= 单点效果 =================

        [Test]
        public void Yan_Blazing_RegistersOpening_NextBattleStartsBurning()
        {
            // 炎 Lv8·燃 炎炎:`BurnScale 200` + `BurnAll 2` `battles 1` —— 本场只翻倍,下一场开局全体 +灼 2
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 3).Select(_ => new[] { Mob(hp: 50) }).ToArray(),
                RewardPool = new[] { "炎" },
            };
            var run = new RunEngine(Graph, config, Config, new[] { "炎", "炎", "炎", "炎", "炎" }, Array.Empty<string>(),
                seed: 1, cardLevels: new Dictionary<string, int> { ["炎"] = 8 });
            Assert.That(run.Battle.Cast("炎", 0), Is.EqualTo(BattleError.None));
            Assert.That(Burn(run.Battle, 0), Is.EqualTo(8), "本场:Lv3 灼 4 × 200%,开局那条不执行");
            var pending = run.Battle.PendingOpenings.Single();
            Assert.That((pending.Kind, pending.Value, pending.BattlesLeft), Is.EqualTo((EffectKind.BurnAll, 2, 1)));

            Win(run);
            Assert.That(Burn(run.Battle, 0), Is.EqualTo(2), "第 2 场开局全体 +灼 2(跨场只存不长)");
            Win(run);
            Assert.That(Burn(run.Battle, 0), Is.EqualTo(0), "battles 1:第 3 场不再有");
        }

        private static void Win(RunEngine run)
        {
            Assert.That(run.Battle.Cast("炎", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
        }

        [Test]
        public void Yan3_Spark_ExtraStrikePerPreCastBurningHit()
        {
            // 焱 Lv5·攻 星火:`ExtraStrike 30` `pick Random` `perBurningHit` —— 出字前带灼的命中目标每名追加一发本体 30%
            var b = Battle("焱", 5, Mob());
            SetBurn(b, 0, 2);
            Assert.That(b.Cast("焱", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var hits = On(b, 0, BattleEventKind.Damage);
            Assert.That(hits.Count, Is.EqualTo(2), "本体一击 + 追加一发");
            Assert.That(hits[1].Amount, Is.EqualTo(hits[0].Amount * 30 / 100).Within(1), "追加 = 本体 30%");

            var cold = Battle("焱", 5, Mob());
            Assert.That(cold.Cast("焱", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(On(cold, 0, BattleEventKind.Damage).Count, Is.EqualTo(1), "本字自己上的灼不算(出字前)");
        }

        [Test]
        public void Zheng_WaterFire_ThawsFrozenTarget_DoubleDamage()
        {
            // 蒸 Lv5·攻 水火相激:`Amplify 100` `if Controlled` + `Thaw`(不带 if,Task 5 Ruling 1)
            var plain = Battle("蒸", 5, Mob());
            Assert.That(plain.Cast("蒸", 0, attackMode: true), Is.EqualTo(BattleError.None));
            int baseDealt = Hp - plain.Enemies[0].Hp;

            var b = Battle("蒸", 5, Mob());
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            Assert.That(b.Cast("蒸", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(Hp - b.Enemies[0].Hp, Is.EqualTo(baseDealt * 2).Within(1), "受控时伤害 ×2");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "解冻");
        }

        [Test]
        public void Fen_JadeAndStone_SelfCostAtCastStart_AllBurnPlusThree()
        {
            // 焚 Lv8·攻 玉石俱焚:`SelfCost 20` + `Amplify 150` + `BurnAll 3`
            var b = Battle("焚", 8, Mob(), Mob());
            int hp = b.PlayerHp;
            Assert.That(b.Cast("焚", -1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerHp, Is.EqualTo(hp - hp * 20 / 100), "失去当前生命 20%");
            Assert.That(new[] { Burn(b, 0), Burn(b, 1) }, Is.EqualTo(new[] { 4 + 3, 4 + 3 }), "本体 4(Lv3)+ 3");
        }

        [Test]
        public void Can_Radiance_RevealsDisguise_MarksVulnerable()
        {
            // 灿 Lv6·攻 光耀:`Reveal` + `Vulnerable 15` `turns 1`
            var b = Battle("灿", 6, new EnemyDef("伪", Element.Wood, Hp, 0, EnemyAbility.Disguise));
            Assert.That(b.Enemies[0].ApparentElement, Is.Not.EqualTo(b.Enemies[0].Element), "夹具:通假字开局伪装");
            Assert.That(b.Cast("灿", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].ApparentElement, Is.EqualTo(b.Enemies[0].Element), "揭示");
            var mark = b.Enemies[0].Statuses.All.SingleOrDefault(s => s.Kind == StatusKind.Vulnerable);
            Assert.That(mark, Is.Not.Null);
            Assert.That(mark.TraitKey, Is.EqualTo(Key("灿", TraitSlot.Lv6, TraitFace.Attack)));
            Assert.That(mark.Magnitude, Is.GreaterThanOrEqualTo(15));
        }
    }
}
