using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-水 Task 2:冻结族 —— 附录 W1(冻结载体附着:怀山 FrostBite、寒彻 ThawStrike、冰水 ThawSlow;
    /// OnFreezeEnd 收拢自然到期与 ThawOn)与 W2(冷却 ChargeDelay,Q5)。
    ///
    /// 夹具口径同 FireRiderTests:Element.Heart 靶子(水打心 1.0×)、PlayerAttack = 100、无甲 10 万血、敌人攻击 0、卡 Lv1。
    /// 附着效果直接写在本体效果表里(TraitKey 缺省回落字 ID,与特性折叠后的写法走同一条分支)。</summary>
    public class WaterFreezeRiderTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int level = 1) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static EnemyDef Mob(int hp = Hp) => new("怔", Element.Heart, hp, 0);

        private static EnemyDef Boss(BossSkill skill = BossSkill.Deluge) =>
            new("钧", Element.Heart, Hp, 0, phases: new[] { new BossPhaseDef("钧", Element.Heart, Hp, 0, skill) });

        private static EffectDef Rider(EffectKind kind, int value) =>
            new(kind, value, pick: EffectPick.FrozenByThisCast, riderOf: StatusKind.Freeze);

        /// <summary>冻字「冰」:主目标冻 <paramref name="turns"/> 回合,再挂附着。</summary>
        private static CharDef Icer(int turns, params EffectDef[] riders) =>
            new("冰", Element.Water, effects: new[] { new EffectDef(EffectKind.Freeze, turns) }.Concat(riders).ToArray());

        private static readonly CharDef Thawer = new("融", Element.Heart, effects: new[] { new EffectDef(EffectKind.Thaw, 0) });

        private static CharDef Killer(int damage) =>
            new("斩", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, damage) });

        private static void Cast(BattleEngine b, string id, int target = 0) =>
            Assert.That(b.Cast(id, target), Is.EqualTo(BattleError.None), id);

        private static List<BattleEvent> RiderHits(IEnumerable<BattleEvent> events) =>
            events.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.FreezeRider).ToList();

        private static StatusEffect Find(BattleEngine b, int i, StatusKind kind) => b.Enemies[i].Statuses.Find(kind);

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<HookArgs> All = new List<HookArgs>();
            public void OnHook(BattleEngine battle, in HookArgs args) => All.Add(args);
        }

        // ---------------- 枚举只追加 ----------------

        [Test]
        public void NewEnumValues_AppendedAtEnd()
        {
            Assert.That((int)StatusKind.FrostBite, Is.EqualTo((int)StatusKind.MoraleShield + 1));
            Assert.That((int)StatusKind.ThawStrike, Is.EqualTo((int)StatusKind.MoraleShield + 2));
            Assert.That((int)StatusKind.ThawSlow, Is.EqualTo((int)StatusKind.MoraleShield + 3));
            Assert.That((int)EffectKind.FrostBite, Is.EqualTo((int)EffectKind.MoraleShield + 1));
            Assert.That((int)EffectKind.ThawStrike, Is.EqualTo((int)EffectKind.MoraleShield + 2));
            Assert.That((int)EffectKind.ThawSlow, Is.EqualTo((int)EffectKind.MoraleShield + 3));
            Assert.That((int)EffectKind.ChargeDelay, Is.EqualTo((int)EffectKind.MoraleShield + 4));
            Assert.That((int)EffectSource.FreezeRider, Is.EqualTo((int)EffectSource.ExecuteSplash + 1));
        }

        // ---------------- 附着只挂在本字冻上的目标 ----------------

        [Test]
        public void Riders_LandOnlyOnTargetsFrozenByThisCast_WithHiddenFreezeCarrier()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.FrostBite, 30), Rider(EffectKind.ThawStrike, 50), Rider(EffectKind.ThawSlow, 2)) },
                new[] { Mob(), Mob() });
            b.Enemies[1].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 2, TurnsLeft = 2 });   // 别人冻的
            Cast(b, "冰", 0);
            Assert.That(Find(b, 0, StatusKind.FrostBite).Magnitude, Is.EqualTo(30), "攻击力 100 = 基准,定死量 = 30");
            Assert.That(Find(b, 0, StatusKind.ThawStrike).Magnitude, Is.EqualTo(50));
            Assert.That(Find(b, 0, StatusKind.ThawSlow).Magnitude, Is.EqualTo(2));
            Assert.That(Find(b, 0, StatusKind.FrostBite).TurnsLeft, Is.EqualTo(-1), "随载体存续");
            Assert.That(Find(b, 0, StatusKind.TraitRider).Magnitude, Is.EqualTo((int)StatusKind.Freeze));
            Assert.That(Find(b, 1, StatusKind.FrostBite), Is.Null, "只带别人冻结的目标不挂");
            Assert.That(Find(b, 1, StatusKind.TraitRider), Is.Null);
        }

        [Test]
        public void Riders_DoNotLandOnBoss_IceStallIsNotFreeze()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.FrostBite, 30), Rider(EffectKind.ThawStrike, 50), Rider(EffectKind.ThawSlow, 2)) },
                new[] { Boss() });
            Cast(b, "冰", 0);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.IceStall), Is.True);
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.TraitRider), Is.False, "Q3:冰滞不收进「被本字冻结」");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostBite) || b.Enemies[0].Statuses.Has(StatusKind.ThawStrike)
                || b.Enemies[0].Statuses.Has(StatusKind.ThawSlow), Is.False);
            b.EndTurn();
            Assert.That(RiderHits(b.LastEvents), Is.Empty);
        }

        // ---------------- 怀山:冻结中每次行动开始(含被跳过那拍) ----------------

        [Test]
        public void FrostBite_HitsEveryFrozenBeat_IncludingTheSkippedOne()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.FrostBite, 30)) }, new[] { Mob() });
            Cast(b, "冰", 0);
            var perBeat = new List<int>();
            for (int t = 0; t < 3; t++)
            {
                b.EndTurn();
                perBeat.Add(RiderHits(b.LastEvents).Sum(e => e.Amount));
            }
            Assert.That(perBeat, Is.EqualTo(new[] { 30, 30, 0 }), "冻 2 回合 = 两拍各 30,解冻后不再结算");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(Hp - 60));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.FrostBite), Is.False, "冻结结束随载体移除");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.TraitRider), Is.False);
        }

        [Test]
        public void FrostBite_SnapshotsBodyPercentTimesLevelTimesAttack_AtCast()
        {
            // 真特性折叠:本体 DamageSingle 100 + Freeze 2,Lv4 被动「怀山」FrostBite bodyPercent 20 → 20,Lv4 吃等级 ×1.18 → 24(向上取整)
            var trait = new TraitDef(TraitSlot.Lv4, TraitFace.Attack, TraitForm.Passive, null, "怀山",
                new[] { new EffectDef(EffectKind.FrostBite, 0, pick: EffectPick.FrozenByThisCast, riderOf: StatusKind.Freeze, bodyPercent: 20) });
            var def = new CharDef("㵘", Element.Water,
                effects: new[] { new EffectDef(EffectKind.HealSelf, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.Freeze, 2) },
                traits: new[] { trait });
            var b = Battle(new[] { def }, new[] { Mob() }, level: 4);
            Assert.That(b.Cast("㵘", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var bite = Find(b, 0, StatusKind.FrostBite);
            Assert.That(bite, Is.Not.Null);
            Assert.That(bite.Magnitude, Is.EqualTo(MetaRules.ScaleByCardLevel(20, 4)));
            Assert.That(bite.TraitKey, Is.EqualTo(BattleEngine.TraitKey("㵘", TraitSlot.Lv4, TraitFace.Attack)));
            b.EndTurn();
            Assert.That(RiderHits(b.LastEvents).Single().Amount, Is.EqualTo(MetaRules.ScaleByCardLevel(20, 4)));
        }

        [Test]
        public void FrostBite_IsWaterElement_NoCrit_RaisedAtTriggerDepth()
        {
            var b = Battle(new[] { Icer(1, Rider(EffectKind.FrostBite, 30)) }, new[] { Mob() });
            var log = new HookLog();
            b.AddHookListener(log);
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 100, TurnsLeft = -1 });
            Cast(b, "冰", 0);
            uint before = b.Capture().RandomState;
            b.EndTurn();
            var hit = RiderHits(b.LastEvents).Single();
            Assert.That(hit.Crit, Is.False, "不暴击");
            Assert.That(hit.Attacker, Is.EqualTo(Element.Water));
            Assert.That(b.Capture().RandomState, Is.EqualTo(before), "附着伤害不摇号");
            var hook = log.All.Single(h => h.Kind == HookKind.EnemyHit && h.Source == EffectSource.FreezeRider);
            Assert.That(hook.Depth, Is.GreaterThan(0), "R4:整段抬一层 TriggerDepth");
        }

        // ---------------- 寒彻:自然到期 / 被解冻各一次,死亡不算 ----------------

        [Test]
        public void ThawStrike_FiresOnceOnNaturalExpiry()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.ThawStrike, 50)) }, new[] { Mob() });
            Cast(b, "冰", 0);
            var perBeat = new List<int>();
            for (int t = 0; t < 4; t++)
            {
                b.EndTurn();
                perBeat.Add(RiderHits(b.LastEvents).Count);
            }
            Assert.That(perBeat, Is.EqualTo(new[] { 0, 1, 0, 0 }), "冻结在第二拍到期时结算一次,之后不再");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(Hp - 50));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.ThawStrike), Is.False);
        }

        [Test]
        public void ThawStrike_FiresOnceWhenThawed_NotAgainLater()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.ThawStrike, 50)), Thawer }, new[] { Mob() });
            Cast(b, "冰", 0);
            Cast(b, "融", 0);
            Assert.That(RiderHits(b.LastEvents).Count, Is.EqualTo(1), "被解冻时结算一次");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(Hp - 50));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.TraitRider), Is.False, "解冻后附着随载体移除");
            for (int t = 0; t < 3; t++)
            {
                b.EndTurn();
                Assert.That(RiderHits(b.LastEvents), Is.Empty);
            }
        }

        [Test]
        public void ThawStrike_EnemyDeath_DoesNotFire()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.ThawStrike, 50)), Killer(500) }, new[] { Mob(hp: 300), Mob() });
            Cast(b, "冰", 0);
            Cast(b, "斩", 0);
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(RiderHits(b.LastEvents), Is.Empty, "敌人死亡不算冻结结束");
            for (int t = 0; t < 3; t++)
            {
                b.EndTurn();
                Assert.That(RiderHits(b.LastEvents), Is.Empty);
            }
        }

        // ---------------- 冰水:先霜抗后减速(Q19) ----------------

        [Test]
        public void ThawSlow_NaturalExpiry_FrostResistThenSlow()
        {
            var b = Battle(new[] { Icer(1, Rider(EffectKind.ThawSlow, 2)) }, new[] { Mob() });
            var log = new HookLog();
            b.AddHookListener(log);
            Cast(b, "冰", 0);
            log.All.Clear();
            b.EndTurn();
            var applied = log.All.Where(h => h.Kind == HookKind.StatusApplied && h.Subject.Side == UnitSide.Enemy)
                .Select(h => h.Status).ToList();
            Assert.That(applied, Is.EqualTo(new[] { StatusKind.FrostResist, StatusKind.SpeedModifier }), "先挂霜抗、再挂减速");
            var slow = Find(b, 0, StatusKind.SpeedModifier);
            Assert.That(slow.Magnitude, Is.LessThan(0));
            Assert.That(slow.TurnsLeft, Is.EqualTo(2));
            Assert.That(Find(b, 0, StatusKind.FrostResist).TurnsLeft, Is.EqualTo(1), "霜抗照旧等长");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.ThawSlow), Is.False);
        }

        [Test]
        public void ThawSlow_WhenThawed_SlowSurvivesTheThaw()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.ThawSlow, 2)), Thawer }, new[] { Mob() });
            var log = new HookLog();
            b.AddHookListener(log);
            Cast(b, "冰", 0);
            log.All.Clear();
            Cast(b, "融", 0);
            var applied = log.All.Where(h => h.Kind == HookKind.StatusApplied && h.Subject.Side == UnitSide.Enemy)
                .Select(h => h.Status).ToList();
            Assert.That(applied, Is.EqualTo(new[] { StatusKind.FrostResist, StatusKind.SpeedModifier }));
            Assert.That(Find(b, 0, StatusKind.SpeedModifier)?.TurnsLeft, Is.EqualTo(2), "冻结结束后的减速不被这次解冻清掉");
        }

        // ---------------- R4:附着伤害不触发死亡类被动 ----------------

        [Test]
        public void FrostBiteKill_DoesNotTriggerBurningCity()
        {
            var b = Battle(new[] { Icer(2, Rider(EffectKind.FrostBite, 30)) }, new[] { Mob(hp: 20), Mob() });
            Cast(b, "冰", 0);
            // 死者身上带焚城标记与灼:R4 下反应深度 > 0 打死的不入队焚城
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 5, TurnsLeft = -1, Potency = 100 });
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.BurnBurstMark, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = -1, SourceId = "燃", TraitKey = "燃" });
            var log = new HookLog();
            b.AddHookListener(log);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "怀山打死");
            var kill = log.All.Single(h => h.Kind == HookKind.EnemyKilled);
            Assert.That(kill.Source, Is.EqualTo(EffectSource.FreezeRider));
            Assert.That(kill.Depth, Is.GreaterThan(0));
            Assert.That(b.Enemies[1].Hp, Is.EqualTo(Hp), "焚城没有入队");
        }

        /// <summary>附着伤害 × 致命(D2-金 J5):怀山那一下把带致命的杂兵压到 30% 以下 → 被斩杀;
        /// 斩杀发生在附着整段抬起的 TriggerDepth 里,EnemyKilled 的 Depth &gt; 0(R4:不触发击杀时 / 斩杀时特性反应)。</summary>
        [Test]
        public void FrostBite_PushesDoomedMobBelowLine_Executes_AtTriggerDepth()
        {
            const int hp = 1000, nearLine = 305;   // 30.5%:怀山 30 压到 27.5%,本身打不死
            BattleEngine Make()
            {
                var e = Battle(new[] { Icer(2, Rider(EffectKind.FrostBite, 30)) }, new[] { Mob(hp: hp), Mob() });
                Cast(e, "冰", 0);
                e.Enemies[0].Hp = nearLine;
                return e;
            }

            var control = Make();
            control.EndTurn();
            Assert.That(control.Enemies[0].Alive, Is.True, "对照:没有致命时怀山打不死");
            Assert.That(control.Enemies[0].Hp * 100, Is.LessThan(hp * BattleEngine.DoomExecutePercent), "对照:已压到 30% 以下");

            var b = Make();
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Doom, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 5, SourceId = "刲" });
            var log = new HookLog();
            b.AddHookListener(log);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "致命斩杀");
            var kill = log.All.Single(h => h.Kind == HookKind.EnemyKilled);
            Assert.That(kill.Source, Is.EqualTo(EffectSource.Execute));
            Assert.That(kill.Depth, Is.GreaterThan(0), "R4:附着整段抬一层 TriggerDepth,斩杀不触发击杀时被动");
        }

        // ---------------- 冷却(W2,Q5) ----------------

        private static CharDef Cooler(int value = 1) =>
            new("冷", Element.Water, effects: new[] { new EffectDef(EffectKind.ChargeDelay, value) });

        [Test]
        public void ChargeDelay_NotCharging_CounterMinusOne()
        {
            var b = Battle(new[] { Cooler() }, new[] { Boss() });
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(0));
            Cast(b, "冷", 0);
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(-1));
            Assert.That(b.Enemies[0].IsCharging, Is.False);
        }

        [Test]
        public void ChargeDelay_WhileCharging_WithdrawsAndRechargesNextBeat()
        {
            var b = Battle(new[] { Cooler() }, new[] { Boss() });
            b.EndTurn();
            b.EndTurn();
            Assert.That(b.Enemies[0].IsCharging, Is.True, "BossChargeEvery 2:第二拍进入蓄力");
            Cast(b, "冷", 0);
            Assert.That(b.Enemies[0].IsCharging, Is.False, "撤回蓄力");
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(1), "计数置 BossChargeEvery − 1");
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossCharging), Is.True, "下一拍重新蓄力、重发预警");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.False, "原释放拍变成蓄力拍");
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True, "再下一拍释放");
        }

        [Test]
        public void ChargeDelay_OncePerBossPerBattle()
        {
            var b = Battle(new[] { Cooler() }, new[] { Boss() });
            Cast(b, "冷", 0);
            Cast(b, "冷", 0);
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(-1), "第二次不再推迟");
        }

        /// <summary>小怪无效果。次数阀按敌人下标记账,对小怪空转「没占次数」本身不可观测,
        /// 这里只断言小怪不被推迟、另一只 Boss 照常推迟。</summary>
        [Test]
        public void ChargeDelay_Mob_NoEffect_BossElsewhereStillDelayable()
        {
            var b = Battle(new[] { Cooler() }, new[] { Mob(), Boss() });
            Cast(b, "冷", 0);
            Assert.That(b.Enemies[0].ChargeCounter, Is.EqualTo(0), "小怪无效果");
            Cast(b, "冷", 1);
            Assert.That(b.Enemies[1].ChargeCounter, Is.EqualTo(-1), "另一只 Boss 照常推迟");
        }

        // ---------------- 加载期校验 ----------------

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""淼"",""element"":""Water"",""effects"":[{""kind"":""HealSelf"",""value"":10}],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10},{""kind"":""Freeze"",""value"":1}],
              ""traits"":[{""slot"":""Lv6"",""face"":""Attack"",""form"":""Passive"",""name"":""附"",""effects"":[" + traitEffects + "]}]}]}");

        private static EffectDef LoadOne(string effect) => Load(effect).Get("淼").Traits[0].Effects[0];

        [Test]
        public void Loader_ReadsFreezeRiders_AndChargeDelay()
        {
            var bite = LoadOne(@"{""kind"":""FrostBite"",""value"":0,""bodyPercent"":20,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}");
            Assert.That(bite.RiderOf, Is.EqualTo(StatusKind.Freeze));
            Assert.That(bite.BodyPercent, Is.EqualTo(20));
            Assert.That(LoadOne(@"{""kind"":""ThawStrike"",""value"":0,""bodyPercent"":50,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}").Kind,
                Is.EqualTo(EffectKind.ThawStrike));
            Assert.That(LoadOne(@"{""kind"":""ThawSlow"",""value"":2,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}").Value, Is.EqualTo(2));
            Assert.That(LoadOne(@"{""kind"":""ChargeDelay"",""value"":1}").Kind, Is.EqualTo(EffectKind.ChargeDelay));
        }

        [TestCase(@"{""kind"":""FrostBite"",""value"":20,""pick"":""FrozenByThisCast""}")]                         // 漏写 riderOf
        [TestCase(@"{""kind"":""ThawSlow"",""value"":2,""pick"":""FrozenByThisCast"",""riderOf"":""Burn""}")]       // 冻结附着挂到灼上
        [TestCase(@"{""kind"":""HealBlock"",""value"":0,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}")]    // 灼附着挂到冻结上
        [TestCase(@"{""kind"":""ThawStrike"",""value"":30,""riderOf"":""Freeze""}")]                                // 不写 pick FrozenByThisCast
        [TestCase(@"{""kind"":""ThawStrike"",""value"":0,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}")]   // 没有伤害量
        [TestCase(@"{""kind"":""ThawSlow"",""value"":0,""pick"":""FrozenByThisCast"",""riderOf"":""Freeze""}")]     // 0 回合
        [TestCase(@"{""kind"":""ChargeDelay"",""value"":0}")]                                                      // 0 拍
        public void Loader_RejectsBadFreezeRiders(string effect)
        {
            Assert.Throws<ConfigException>(() => Load(effect));
        }
    }
}
