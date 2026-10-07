using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 3:灼附着族(附录 N5)与焚城结算(N6)。
    /// 干涸 HealBlock、上炎 BurnGrow、四火 BurnHold(G10)、炽焰 Weaken + minBurn、焚城 BurnBurst(R4)、焚身载体 BurnBacklash。
    ///
    /// 夹具口径同 FireBurnOpsTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、暴击率 0、卡 Lv1(火力 100)。
    /// 附着效果直接写在本体效果表里(TraitKey 缺省回落字 ID,与特性折叠后的写法走同一条分支)。</summary>
    public class FireRiderTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20 };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int level = 1) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static EnemyDef Mob(int hp = Hp, int attack = 0, EnemyAbility ability = EnemyAbility.None) =>
            new("怔", Element.Heart, hp, attack, ability);

        private static EffectDef Rider(EffectKind kind, int value = 0, int turns = 0, int minBurn = 0) =>
            new(kind, value, turns: turns, pick: EffectPick.BurnedByThisCast, riderOf: StatusKind.Burn, minBurn: minBurn);

        /// <summary>点火字「燃」:给主目标上 <paramref name="burn"/> 层灼,再挂附着。</summary>
        private static CharDef Igniter(int burn, params EffectDef[] riders) =>
            new("燃", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnSingle, burn) }.Concat(riders).ToArray());

        private static readonly CharDef Boom = new("煸", Element.Heart, effects: new[] { new EffectDef(EffectKind.Detonate, 0) });

        private static CharDef Killer(int damage, TargetArea shape = TargetArea.Single) =>
            new("斩", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, damage, shape: shape) });

        private static int Burn(BattleEngine b, int i) => b.Enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;

        private static StatusEffect Find(BattleEngine b, int i, StatusKind kind) => b.Enemies[i].Statuses.Find(kind);

        private static void SetBurn(BattleEngine b, int i, int stacks, int potency = 100) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = potency });

        /// <summary>每层灼一次结算的伤害(夹具下 = 每层基数)。用全额引爆 1 层量出来,不写死常量。</summary>
        private static int PerStack()
        {
            var b = Battle(new[] { Boom }, new[] { Mob() });
            SetBurn(b, 0, 1);
            int before = b.Enemies[0].Hp;
            Assert.That(b.Cast("煸", 0), Is.EqualTo(BattleError.None));
            return before - b.Enemies[0].Hp;
        }

        private static readonly StatusKind[] RiderKinds =
            { StatusKind.HealBlock, StatusKind.BurnGrow, StatusKind.BurnHold, StatusKind.BurnBurstMark, StatusKind.BurnBacklashMark };

        private static EffectDef[] AllRiders() => new[]
        {
            Rider(EffectKind.HealBlock), Rider(EffectKind.BurnGrow, 1, turns: 3), Rider(EffectKind.BurnHold),
            Rider(EffectKind.BurnBurst), Rider(EffectKind.BurnBacklash), Rider(EffectKind.Weaken, 30, minBurn: 5),
        };

        // ---------------- 通用附着:只给本次出字上过灼的目标,挂隐藏载体 ----------------

        [Test]
        public void Riders_LandOnlyOnTargetsBurnedByThisCast_WithHiddenCarrier()
        {
            var b = Battle(new[] { Igniter(2, AllRiders()) }, new[] { Mob(), Mob() });
            SetBurn(b, 1, 3);   // 别人点的灼:不算本字的灼
            Assert.That(b.Cast("燃", 0), Is.EqualTo(BattleError.None));
            foreach (var kind in RiderKinds)
            {
                var s = Find(b, 0, kind);
                Assert.That(s, Is.Not.Null, $"{kind} 挂在本字点过灼的目标上");
                Assert.That(s.SourceId, Is.EqualTo("燃"));
                Assert.That(s.TraitKey, Is.EqualTo("燃"), "缺省特性键回落字 ID,与载体配对");
                Assert.That(Find(b, 1, kind), Is.Null, $"{kind} 不挂在只带别人灼的目标上");
            }
            Assert.That(Find(b, 0, StatusKind.BurnGrow).TurnsLeft, Is.EqualTo(3), "上炎保留自己的回合数");
            Assert.That(Find(b, 0, StatusKind.HealBlock).TurnsLeft, Is.EqualTo(-1), "没写 turns 的附着随载体存续");
            var curse = Find(b, 0, StatusKind.Curse);
            Assert.That(curse.Magnitude, Is.EqualTo(30));
            Assert.That(curse.MinBurn, Is.EqualTo(5));
            Assert.That(curse.TurnsLeft, Is.EqualTo(-1));
            var carrier = Find(b, 0, StatusKind.TraitRider);
            Assert.That(carrier.Magnitude, Is.EqualTo((int)StatusKind.Burn));
            Assert.That(Find(b, 1, StatusKind.TraitRider), Is.Null);
        }

        // ---------------- 载体(灼)消失时附着一并移除 ----------------

        [Test]
        public void Riders_DroppedWhenBurnDetonated()
        {
            var b = Battle(new[] { Igniter(2, AllRiders()), Boom }, new[] { Mob() });
            b.Cast("燃", 0);
            b.Cast("煸", 0);
            foreach (var kind in RiderKinds)
                Assert.That(Find(b, 0, kind), Is.Null, $"{kind} 随灼一并移除");
            Assert.That(Find(b, 0, StatusKind.Curse), Is.Null, "附着减攻随灼一并移除");
            Assert.That(Find(b, 0, StatusKind.TraitRider), Is.Null);
        }

        [Test]
        public void Riders_DroppedWhenBurnSettlesToZero()
        {
            var b = Battle(new[] { Igniter(1, Rider(EffectKind.HealBlock), Rider(EffectKind.BurnBacklash), Rider(EffectKind.BurnBurst)) },
                new[] { Mob() });
            b.Cast("燃", 0);
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(0));
            Assert.That(Find(b, 0, StatusKind.HealBlock), Is.Null);
            Assert.That(Find(b, 0, StatusKind.BurnBacklashMark), Is.Null);
            Assert.That(Find(b, 0, StatusKind.BurnBurstMark), Is.Null);
            Assert.That(Find(b, 0, StatusKind.TraitRider), Is.Null);
        }

        // ---------------- 干涸:挡住敌人的每一条回血路径 ----------------

        /// <summary>涂改(Mend)给伤最重的同伴回血:带干涸的同伴不再被选中。返回 [挨奶者血量, 涂改出手的 EnemyMend 事件数]。</summary>
        private static (int hp, int mends) MendRun(bool healBlock)
        {
            var riders = healBlock ? new[] { Rider(EffectKind.HealBlock) } : Array.Empty<EffectDef>();
            var b = Battle(new[] { Igniter(3, riders) }, new[] { Mob(attack: 50, ability: EnemyAbility.Mend), Mob() });
            b.Enemies[1].Hp = Hp - 1000;
            b.Cast("燃", 1);
            b.EndTurn();
            return (b.Enemies[1].Hp, b.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyMend));
        }

        [Test]
        public void HealBlock_MendSkipsBlockedAlly()
        {
            var open = MendRun(healBlock: false);
            var blocked = MendRun(healBlock: true);
            Assert.That(open.mends, Is.EqualTo(1), "对照:没有干涸时涂改照常回血");
            Assert.That(blocked.mends, Is.EqualTo(0), "带干涸的同伴不被涂改选中");
            Assert.That(open.hp - blocked.hp, Is.EqualTo(50), "差的正好是涂改那一口(= 涂改攻击力)");
        }

        [Test]
        public void HealBlock_RegrowStillGrowsAttack_ButNoHeal()
        {
            int RegrowHp(bool healBlock, int progress, out EnemyState enemy)
            {
                var riders = healBlock ? new[] { Rider(EffectKind.HealBlock) } : Array.Empty<EffectDef>();
                var b = Battle(new[] { Igniter(3, riders) }, new[] { Mob(attack: 10, ability: EnemyAbility.Regrow) });
                b.Enemies[0].Hp = Hp - 5000;
                b.Enemies[0].RegrowProgress = progress;
                b.Cast("燃", 0);
                b.EndTurn();
                enemy = b.Enemies[0];
                return enemy.Hp;
            }

            int open = RegrowHp(false, 0, out var openEnemy);
            int blocked = RegrowHp(true, 0, out var blockedEnemy);
            Assert.That(open - blocked, Is.EqualTo(30), "补全的 +30 血被挡下");
            Assert.That(blockedEnemy.RegrowProgress, Is.EqualTo(1), "补全进度照常推进");
            Assert.That(blockedEnemy.BaseAttack, Is.EqualTo(openEnemy.BaseAttack), "攻击成长照常");

            int full = RegrowHp(true, 2, out var finalEnemy);
            Assert.That(finalEnemy.RegrowProgress, Is.EqualTo(3));
            Assert.That(full, Is.LessThan(finalEnemy.MaxHp), "第 3 次补全的回满血也被挡下");
            Assert.That(finalEnemy.BaseAttack, Is.EqualTo((10 + 20) * 2), "攻击 ×2 照常");
        }

        // ---------------- 上炎:每回合开始 +1 层(灼前),持续 3 回合 ----------------

        [Test]
        public void BurnGrow_AddsBeforeSettle_ForItsOwnTurns()
        {
            var b = Battle(new[] { Igniter(2, Rider(EffectKind.BurnGrow, 1, turns: 3)) }, new[] { Mob() });
            b.Cast("燃", 0);
            for (int turn = 1; turn <= 3; turn++)
            {
                b.EndTurn();
                Assert.That(Burn(b, 0), Is.EqualTo(2), $"第 {turn} 回合:+1 再结算 −1,层数不掉");
            }
            Assert.That(Find(b, 0, StatusKind.BurnGrow), Is.Null, "3 回合后到期");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(1), "到期后照常衰减");
        }

        // ---------------- 四火:第一次结算不减层;重新上灼刷新(G10) ----------------

        [Test]
        public void BurnHold_FirstDecayingSettleKeepsStacks_RefreshedOnReburn()
        {
            var b = Battle(new[] { Igniter(3, Rider(EffectKind.BurnHold)) }, new[] { Mob() });
            b.Cast("燃", 0);
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(3), "第一次结算不减层");
            Assert.That(Find(b, 0, StatusKind.BurnHold), Is.Null, "用掉即移除");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(2), "第二次照常减层");

            Assert.That(b.Cast("燃", 0), Is.EqualTo(BattleError.None));
            Assert.That(Find(b, 0, StatusKind.BurnHold), Is.Not.Null, "再上灼:附着刷新");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(5), "刷新后又是「第一次」");
        }

        [Test]
        public void BurnHold_KeepSettleDoesNotConsumeIt()
        {
            var keep = new CharDef("引", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnSettleNow, 0, keepStacks: true) });
            var b = Battle(new[] { Igniter(3, Rider(EffectKind.BurnHold)), keep }, new[] { Mob() });
            b.Cast("燃", 0);
            b.Cast("引", 0);
            Assert.That(Find(b, 0, StatusKind.BurnHold), Is.Not.Null, "不减层的结算不消耗四火");
            b.EndTurn();
            Assert.That(Burn(b, 0), Is.EqualTo(3));
        }

        // ---------------- 炽焰:灼 ≥5 层才生效的减攻 ----------------

        [Test]
        public void MinBurnWeaken_OnlyCountsAtThreshold_StrongestWins()
        {
            var b = Battle(new[] { Igniter(4, Rider(EffectKind.Weaken, 30, minBurn: 5)) }, new[] { Mob(attack: 100) });
            b.Cast("燃", 0);
            Assert.That(b.Enemies[0].Attack, Is.EqualTo(100), "4 层:门槛未到,不减攻");
            SetBurn(b, 0, 5);
            Assert.That(b.Enemies[0].Attack, Is.EqualTo(70), "5 层:−30%");

            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Curse, Polarity = StatusPolarity.Debuff, Magnitude = 15, TurnsLeft = 3, SourceId = "他" });
            Assert.That(b.Enemies[0].Attack, Is.EqualTo(70), "多来源取最强");
            SetBurn(b, 0, 4);
            Assert.That(b.Enemies[0].Attack, Is.EqualTo(85), "掉到门槛下:只剩无门槛的那条");
        }

        // ---------------- 焚城:死亡时对全体结算一次剩下的灼 ----------------

        [Test]
        public void BurnBurst_OnDeath_SettlesRemainingBurnOnEveryone_LayersUnchanged()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(4, Rider(EffectKind.BurnBurst)), Killer(1000) }, new[] { Mob(hp: 100), Mob(), Mob() });
            b.Cast("燃", 0);
            SetBurn(b, 2, 3);
            int hp1 = b.Enemies[1].Hp, hp2 = b.Enemies[2].Hp;
            Assert.That(b.Cast("斩", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(hp1 - b.Enemies[1].Hp, Is.EqualTo(4 * per), "死者剩 4 层:每名存活敌人各吃 4 层的灼烧结算");
            Assert.That(hp2 - b.Enemies[2].Hp, Is.EqualTo(4 * per));
            Assert.That(Burn(b, 1), Is.EqualTo(0), "不改层数:没灼的照旧没灼");
            Assert.That(Burn(b, 2), Is.EqualTo(3), "不改层数");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.BurnTick && e.Amount == 4 * per), Is.EqualTo(2));
        }

        [Test]
        public void BurnBurst_UsesCorpsesPotency()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(2, Rider(EffectKind.BurnBurst)), Killer(1000) }, new[] { Mob(hp: 100), Mob() });
            b.Cast("燃", 0);
            b.Enemies[0].Statuses.Find(StatusKind.Burn).Potency = 150;
            int hp1 = b.Enemies[1].Hp;
            b.Cast("斩", 0);
            Assert.That(hp1 - b.Enemies[1].Hp, Is.EqualTo(2 * per * 150 / 100), "火力取死者灼的火力");
        }

        [Test]
        public void BurnBurst_KillFromBurstDoesNotChain_R4()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(4, Rider(EffectKind.BurnBurst)), Killer(1000) },
                new[] { Mob(hp: 100), Mob(hp: 1), Mob() });
            b.Cast("燃", 0);
            b.Cast("燃", 1);   // 1 号也带焚城标记、4 层灼,但血只有 1
            int hp2 = b.Enemies[2].Hp;
            b.Cast("斩", 0);
            Assert.That(b.Enemies[1].Alive, Is.False, "被 0 号的焚城烧死");
            Assert.That(hp2 - b.Enemies[2].Hp, Is.EqualTo(4 * per), "1 号死于焚城(反应里):不再连锁焚城");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        [Test]
        public void BurnBurst_SimultaneousDeaths_ResolveInDeathOrder()
        {
            int per = PerStack();
            var b = Battle(new[] { Igniter(2, Rider(EffectKind.BurnBurst)), Igniter5(), Killer(500, TargetArea.All) },
                new[] { Mob(hp: 100), Mob(hp: 100), Mob() });
            b.Cast("燃", 0);
            b.Cast("灼", 1);
            int hp2 = b.Enemies[2].Hp;
            b.Cast("斩", -1);
            Assert.That(b.Enemies[0].Alive || b.Enemies[1].Alive, Is.False);
            var bursts = b.LastEvents.Where(e => e.Kind == BattleEventKind.BurnTick && e.TargetIndex == 2).Select(e => e.Amount).ToArray();
            Assert.That(bursts, Is.EqualTo(new[] { 2 * per, 5 * per }), "按死亡顺序(0 号先、1 号后)各结算一次");
            Assert.That(hp2 - b.Enemies[2].Hp, Is.EqualTo(500 + 7 * per));
        }

        private static CharDef Igniter5() =>
            new("灼", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnSingle, 5), Rider(EffectKind.BurnBurst) });

        [Test]
        public void BurnBurst_LastEnemyKilledByBurst_Wins()
        {
            var b = Battle(new[] { Igniter(4, Rider(EffectKind.BurnBurst)), Killer(1000) }, new[] { Mob(hp: 100), Mob(hp: 1) });
            b.Cast("燃", 0);
            b.Cast("斩", 0);
            Assert.That(b.Enemies.Any(e => e.Alive), Is.False);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
        }

        // ---------------- Task 2 遗留:满层翻倍也算本字上过灼 ----------------

        [Test]
        public void BurnScale_AtFullStacks_StillCountsAsBurnedByThisCast()
        {
            var def = new CharDef("焦", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnScale, 200), Rider(EffectKind.HealBlock) });
            var b = Battle(new[] { def }, new[] { Mob() });
            SetBurn(b, 0, CombatCaps.BurnStacks);
            b.Cast("焦", 0);
            Assert.That(Find(b, 0, StatusKind.HealBlock), Is.Not.Null, "满层翻倍没有增量,但同 BurnSingle 口径记进本字上灼名单");
        }

        // ---------------- 快照往返 ----------------

        [Test]
        public void StatusEffect_Clone_CopiesMinBurn()
        {
            var s = new StatusEffect { Kind = StatusKind.Curse, Magnitude = 30, MinBurn = 5 };
            Assert.That(s.Clone().MinBurn, Is.EqualTo(5));
        }

        [Test]
        public void Riders_SurviveSnapshotRoundTrip()
        {
            var defs = new[] { Igniter(2, AllRiders()) };
            var b = Battle(defs, new[] { Mob() });
            b.Cast("燃", 0);
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(defs), Config,
                new Dictionary<string, int> { ["燃"] = 1 }, new Dictionary<string, EnemyDef> { ["怔"] = Mob() });
            foreach (var kind in RiderKinds.Append(StatusKind.Curse).Append(StatusKind.TraitRider))
            {
                var a = Find(b, 0, kind);
                var r = Find(restored, 0, kind);
                Assert.That(r, Is.Not.Null, $"{kind} 读档后还在");
                Assert.That((r.Magnitude, r.TurnsLeft, r.SourceId, r.TraitKey, r.MinBurn),
                    Is.EqualTo((a.Magnitude, a.TurnsLeft, a.SourceId, a.TraitKey, a.MinBurn)), $"{kind} 逐字段一致");
            }
            Assert.That(Find(restored, 0, StatusKind.Curse).MinBurn, Is.EqualTo(5));
        }

        [Test]
        public void Riders_SurviveRealSaveFile()
        {
            var igniter = Igniter(2, AllRiders());
            var graph = RebalanceFixture.Graph(igniter);
            var enemy = RebalanceFixture.Mob(attack: 0);
            var runConfig = new RunConfig { Encounters = new[] { new[] { enemy }, new[] { enemy } }, RewardPool = new[] { "燃" } };
            var levels = new Dictionary<string, int> { ["燃"] = 1 };
            var run = new RunEngine(graph, runConfig, Config, new[] { "燃", "燃" }, Array.Empty<string>(), 3,
                startingInk: 50, perFloorNormalShield: 2, cardLevels: levels);
            Assert.That(run.Battle.Cast("燃", 0), Is.EqualTo(BattleError.None));
            var meta = new MetaState
            {
                EndlessV2 = new EndlessSaveState
                {
                    Depth = 3, Seed = 999,
                    InProgress = new InProgressRun { FromDepth = 1, FirstTowerSegment = true, Run = run.Capture() },
                },
            };
            var reloaded = SaveSerializer.FromJson(SaveSerializer.ToJson(meta));
            var restored = RunEngine.Restore(reloaded.EndlessV2.InProgress.Run, graph, runConfig, Config, levels,
                startingInk: 50, perFloorNormalShield: 2);
            var bag = restored.Battle.Enemies[0].Statuses;
            foreach (var kind in RiderKinds)
                Assert.That(bag.Has(kind), Is.True, $"{kind} 进存档 JSON 往返");
            Assert.That(bag.Find(StatusKind.BurnGrow).TurnsLeft, Is.EqualTo(3));
            Assert.That(bag.Find(StatusKind.Curse).MinBurn, Is.EqualTo(5), "MinBurn 进存档 JSON 往返");
        }

        // ---------------- 字表加载 ----------------

        private static RecipeGraph Load(string traitEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""燚"",""element"":""Fire"",""effects"":[{""kind"":""BurnSingle"",""value"":2}],
              ""traits"":[{""slot"":""Lv6"",""face"":""Feature"",""form"":""Passive"",""name"":""附"",""effects"":[" + traitEffects + "]}]}]}");

        private static EffectDef LoadOne(string effect) => Load(effect).Get("燚").Traits[0].Effects[0];

        [Test]
        public void Config_RiderFamily_Parses()
        {
            foreach (var kind in new[] { "HealBlock", "BurnHold", "BurnBurst", "BurnBacklash" })
            {
                var e = LoadOne(@"{""kind"":""" + kind + @""",""value"":0,""pick"":""BurnedByThisCast"",""riderOf"":""Burn""}");
                Assert.That(e.Kind.ToString(), Is.EqualTo(kind));
                Assert.That(e.RiderOf, Is.EqualTo(StatusKind.Burn));
                Assert.That(e.Pick, Is.EqualTo(EffectPick.BurnedByThisCast));
            }
            var grow = LoadOne(@"{""kind"":""BurnGrow"",""value"":1,""turns"":3,""pick"":""BurnedByThisCast"",""riderOf"":""Burn""}");
            Assert.That((grow.Value, grow.Turns), Is.EqualTo((1, 3)));
            var weaken = LoadOne(@"{""kind"":""Weaken"",""value"":30,""pick"":""BurnedByThisCast"",""riderOf"":""Burn"",""minBurn"":5}");
            Assert.That(weaken.MinBurn, Is.EqualTo(5));
        }

        [Test]
        public void Config_RiderFamily_RejectsBadShapes()
        {
            // 附着族不写 riderOf 会被引擎当成什么都不做 —— 拦下
            foreach (var kind in new[] { "HealBlock", "BurnGrow", "BurnHold", "BurnBurst", "BurnBacklash" })
                Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""" + kind + @""",""value"":1,""pick"":""BurnedByThisCast""}"),
                    $"{kind} 必须写 riderOf");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Weaken"",""value"":30,""turns"":2,""minBurn"":5}"),
                "minBurn 只给附着的减攻");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Blind"",""value"":15,""riderOf"":""Burn"",""minBurn"":5}"),
                "minBurn 只给减攻");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""Weaken"",""value"":30,""riderOf"":""Burn"",""minBurn"":-1}"));
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""BurnGrow"",""value"":0,""turns"":3,""riderOf"":""Burn""}"),
                "上炎至少 +1 层");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""HealBlock"",""value"":0,""riderOf"":""Burn"",""openingBattles"":1}"),
                "附着效果不能登记为开局效果(开局时没有本次出字的灼)");
        }
    }
}
