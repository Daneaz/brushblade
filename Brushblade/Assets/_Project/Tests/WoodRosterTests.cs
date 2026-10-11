using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 4:木灵编队与敌人侧。
    /// M7 攀附 Bind(Q13)、M8 绿潮 SummonMerge(Q18)、M9 四木成林 SummonAct(Q24)、M10 折桂 GrantChar(Q16 + E49 退次)、
    /// M11 水草 HealMirror(Q17)。
    ///
    /// 夹具:Element.Heart 敌人(对木 1.0×)、PlayerAttack = 100、玩家暴击率 0、卡等级 1、无掉字。木灵用起手携带直接摆位。</summary>
    public class WoodRosterTests
    {
        private static BattleConfig Config(int library = 300, IReadOnlyCollection<string> owned = null) => new()
        {
            PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = library,
            BossPhaseJitterPercent = 0, DropsPerTurn = 0, UnlockedChars = owned,
        };

        private static SummonSnapshot W(int slot, int hp = 1000, int maxHp = 1000, int attack = 0, SummonPassive passive = null,
            string source = "林", string ch = "木", int sproutParent = -1, StatusEffect[] statuses = null) => new()
        {
            Slot = slot, Char = ch, SourceChar = source, Element = Element.Wood, Hp = hp, MaxHp = maxHp,
            Attack = attack, Speed = 100, Passive = passive, SproutParentSlot = sproutParent,
            Statuses = (statuses ?? Array.Empty<StatusEffect>()).ToList(),
        };

        private static BattleEngine Battle(CharDef[] defs, IReadOnlyList<SummonSnapshot> summons, EnemyDef[] enemies,
            BattleConfig config = null, int copies = 10, int startingHp = 3000) =>
            new(RebalanceFixture.Graph(defs), config ?? Config(), defs.SelectMany(d => Enumerable.Repeat(d.Id, copies)).ToArray(),
                Array.Empty<string>(), enemies, seed: 11, startingHp: startingHp, startingSummons: summons);

        private static CharDef Card(string id, params EffectDef[] effects) => RebalanceFixture.Char(id, effects);

        private static CharDef WoodCard(string id, EffectDef[] feature, EffectDef[] attack = null, TraitDef[] traits = null,
            CardRarity rarity = CardRarity.White) =>
            new(id, Element.Wood, effects: feature, rarity: rarity,
                attackEffects: attack ?? new[] { new EffectDef(EffectKind.DamageSingle, 10) }, traits: traits);

        private static EffectDef Body(int hp = 500, int attack = 40, string ch = "藤", SummonPassive passive = null) =>
            new(EffectKind.Summon, hp, summonAttack: attack, summonChar: ch, passive: passive);

        private static EnemyDef Foe(int hp = 100000, int attack = 0, AttackRange range = AttackRange.Melee) =>
            new("怔", Element.Heart, hp, attack, range: range);

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<HookArgs> All = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => All.Add(args);
        }

        private static StatusEffect BindOn(int slot, int percent = 100) => new()
        {
            Kind = StatusKind.Bind, Polarity = StatusPolarity.Debuff, Magnitude = percent, TurnsLeft = -1,
            SourceId = "藤", TargetSlot = slot,
        };

        private static IEnumerable<StatusEffect> Binds(BattleEngine b, int enemy) =>
            b.Enemies[enemy].Statuses.All.Where(s => s.Kind == StatusKind.Bind);

        private static int SummonAttacksFrom(BattleEngine b, int slot) =>
            b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == slot);

        // ================= M7 攀附 Bind =================

        private static CharDef Vine(int percent = 100, int attack = 40, SummonPassive passive = null) =>
            WoodCard("藤", new[] { Body(attack: attack, passive: passive), new EffectDef(EffectKind.Bind, percent, pick: EffectPick.HighestHp) });

        [Test]
        public void M7_Bind_HangsOnHighestHpEnemy_TargetsFirstSummoned_NoRoll()
        {
            var b = Battle(new[] { Vine() }, new[] { W(0) }, new[] { Foe(3000), Foe(9000), Foe(5000) });
            uint trait = b.Capture().TraitRandomState, target = b.Capture().TargetRandomState;
            Assert.That(b.Cast("藤"), Is.EqualTo(BattleError.None));
            Assert.That(Binds(b, 0).Any() || Binds(b, 2).Any(), Is.False, "只挂生命最高的那只");
            var bind = Binds(b, 1).Single();
            Assert.That((bind.Magnitude, bind.TurnsLeft, bind.TargetSlot, bind.Polarity),
                Is.EqualTo((100, -1, 1, StatusPolarity.Debuff)), "藤灵 = 本次召出的第一只(落在空槽 1)");
            Assert.That((b.Capture().TraitRandomState, b.Capture().TargetRandomState), Is.EqualTo((trait, target)), "HighestHp 不摇号");
        }

        [Test]
        public void M7_Bind_StrikesAtEachEnemyActionStart_IncludingFrozenBeat()
        {
            var b = Battle(new[] { Vine(200, attack: 30) }, null, new[] { Foe() });
            b.Cast("藤");
            int slot = Binds(b, 0).Single().TargetSlot;
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            b.EndTurn();
            var strikes = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == slot).ToList();
            Assert.That(strikes.Count, Is.EqualTo(2), "自己那一拍出手一次 + 敌人(被冻结跳过的)那一拍开始时攀附一次");
            Assert.That(strikes.Any(e => e.Amount == 60), Is.True, "攀附打击 = 有效攻击 × 200%");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False, "这一拍确实被冻结跳过(冻结到期)");

            b.EndTurn();
            Assert.That(SummonAttacksFrom(b, slot), Is.EqualTo(2), "解冻后的普通一拍同样结算");
            Assert.That(Binds(b, 0).Single().TurnsLeft, Is.EqualTo(-1), "攀附不随敌人行动递减");
        }

        [Test]
        public void M7_Bind_OnBoss_Works()
        {
            var boss = new EnemyDef("噬", Element.Heart, 100000, 0,
                phases: new[] { new BossPhaseDef("噬", Element.Heart, 100000, 0, skill: BossSkill.Devour) });
            var b = Battle(new[] { Vine(100, attack: 30) }, null, new[] { boss });
            b.Cast("藤");
            Assert.That(Binds(b, 0).Count(), Is.EqualTo(1), "Boss 也可以被缠");
            int hp = b.Enemies[0].Hp;
            b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.LessThan(hp - 30), "藤灵自己一拍 + 攀附一拍");
        }

        [Test]
        public void M7_Bind_VineDeath_ClearsBind()
        {
            var b = Battle(new[] { Vine() }, null, new[] { Foe(), Foe(200000) });
            b.Cast("藤");
            int slot = Binds(b, 1).Single().TargetSlot;
            b.KillSummonForTest(slot);
            Assert.That(Binds(b, 1).Any(), Is.False, "藤灵阵亡 → 解除");
        }

        [Test]
        public void M7_Bind_VineReplaced_ClearsBind_NewSummonDoesNotInherit()
        {
            var plain = WoodCard("林", new[] { Body(ch: "木") });
            var b = Battle(new[] { Vine(), plain }, null, new[] { Foe() },
                new BattleConfig { PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, LibraryCapacity = 300, DropsPerTurn = 0,
                    UnlockedSummonSlots = 0b1 });
            b.Cast("藤");
            Assert.That(Binds(b, 0).Single().TargetSlot, Is.EqualTo(0));
            Assert.That(b.Cast("林", replaceSummon: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Summons[0].SourceChar, Is.EqualTo("林"), "夹具前提:顶替");
            Assert.That(Binds(b, 0).Any(), Is.False, "藤灵被顶替 → 解除(顶替不走阵亡)");
            b.EndTurn();
            Assert.That(SummonAttacksFrom(b, 0), Is.EqualTo(1), "新木灵只有自己那一拍");
        }

        [Test]
        public void M7_Bind_Graft_VineIsGraftee()
        {
            var b = Battle(new[] { Vine() }, new[] { W(3, attack: 20) }, new[] { Foe() });
            Assert.That(b.Cast("藤", allySlot: 3), Is.EqualTo(BattleError.None));
            Assert.That(Binds(b, 0).Single().TargetSlot, Is.EqualTo(3), "嫁接时藤灵 = 被嫁接者(E42)");
        }

        [Test]
        public void M7_Bind_NoSummonThisCast_NoBind()
        {
            var bindOnly = WoodCard("缠", new[] { new EffectDef(EffectKind.Bind, 100, pick: EffectPick.HighestHp) });
            var b = Battle(new[] { bindOnly }, new[] { W(0, attack: 20) }, new[] { Foe() });
            b.Cast("缠");
            Assert.That(Binds(b, 0).Any(), Is.False, "本次没召出藤灵:空转");
        }

        [Test]
        public void M7_Bind_StrikeCanKill_BeatEnds()
        {
            var b = Battle(new[] { Vine(100, attack: 500) }, null, new[] { Foe(100000, attack: 100), Foe(400, attack: 100) });
            b.Cast("藤");
            // 挂在生命最高的 0 号上;把 1 号也缠上,攀附打死它时它这一拍不再出手
            b.Enemies[1].Statuses.Apply(BindOn(Binds(b, 0).Single().TargetSlot, 100));
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(b.Enemies[1].Alive, Is.False);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.EnemyAttack && e.TargetIndex == 1), Is.False,
                "被攀附打死的那一拍不出手");
        }

        // ================= M8 绿潮 SummonMerge =================

        private static CharDef Tide() => WoodCard("藻", new[]
        {
            Body(hp: 400, attack: 30, ch: "藻"),
            new EffectDef(EffectKind.SummonMerge, 0),
            new EffectDef(EffectKind.Taunt, 2, pick: EffectPick.SummonedThisCast),
        });

        [Test]
        public void M8_Merge_AddsCurrentStats_RemovesSaplingsWithoutDeath_TauntOnReceiver()
        {
            var log = new HookLog();
            var b = Battle(new[] { Tide() }, new[]
            {
                W(1, hp: 50, maxHp: 100, attack: 10, source: "藻", ch: "苗", sproutParent: 4),
                W(2, hp: 80, maxHp: 80, attack: 12, source: "藻", ch: "苗"),
                W(3, hp: 60, maxHp: 60, attack: 9, source: "林", ch: "苗"),    // 别家幼苗:不并
                W(4, hp: 300, maxHp: 400, attack: 30, source: "藻", ch: "藻", passive: new SummonPassive { DeathPart = true }),   // 藻灵本体:不并
            }, new[] { Foe() }, Config());
            b.AddHookListener(log);
            Assert.That(b.Cast("藻"), Is.EqualTo(BattleError.None));
            var r = b.Summons[0];
            Assert.That((r.Char, r.Hp, r.MaxHp, r.Attack), Is.EqualTo(("藻", 400 + 50 + 80, 400 + 100 + 80, 30 + 10 + 12)),
                "生命 / 上限 / 攻击按当前值相加");
            Assert.That(b.Summons[1], Is.Null);
            Assert.That(b.Summons[2], Is.Null);
            Assert.That(b.Summons[3]?.Hp, Is.EqualTo(60), "别家幼苗留场");
            Assert.That(b.Summons[4]?.Hp, Is.EqualTo(300), "藻灵本体留场");
            Assert.That(log.All.Any(h => h.Kind == HookKind.SummonDied), Is.False, "并入不触发阵亡");
            Assert.That(b.Pool.Contains("木"), Is.False, "不触发阵亡挂点");
            Assert.That(r.Statuses.Has(StatusKind.Taunt), Is.True, "嘲讽落到藻灵");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.SummonMerged), Is.EqualTo(2));
        }

        [Test]
        public void M8_Merge_ClearsSlotAurasAndBindsOfRemovedSaplings()
        {
            var b = Battle(new[] { Tide() }, new[]
            {
                W(1, hp: 50, source: "藻", ch: "苗"),
                W(2, hp: 50, source: "林", ch: "苗"),
            }, new[] { Foe() }, Config());
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.SummonEmpower, Polarity = StatusPolarity.Buff, Magnitude = 30, TurnsLeft = 3, SourceId = "擎", TargetSlot = 1 });
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.SummonEmpower, Polarity = StatusPolarity.Buff, Magnitude = 30, TurnsLeft = 3, SourceId = "擎", TargetSlot = 2 });
            b.Enemies[0].Statuses.Apply(BindOn(1));
            b.Enemies[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Bind, Polarity = StatusPolarity.Debuff, Magnitude = 100, TurnsLeft = -1, SourceId = "藤2", TargetSlot = 2,
            });
            b.Cast("藻");
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.SummonEmpower && s.TargetSlot == 1), Is.False);
            Assert.That(b.PlayerStatuses.All.Any(s => s.Kind == StatusKind.SummonEmpower && s.TargetSlot == 2), Is.True);
            Assert.That(Binds(b, 0).Select(s => s.TargetSlot).ToArray(), Is.EqualTo(new[] { 2 }), "只清被并入的槽");
        }

        [Test]
        public void M8_Merge_NoSaplings_NoChange()
        {
            var b = Battle(new[] { Tide() }, null, new[] { Foe() });
            b.Cast("藻");
            var r = b.Summons[0];
            Assert.That((r.Hp, r.MaxHp, r.Attack), Is.EqualTo((400, 400, 30)));
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonMerged), Is.False);
        }

        [Test]
        public void M8_Merge_IntoGraftee()
        {
            var b = Battle(new[] { Tide() }, new[]
            {
                W(0, hp: 200, maxHp: 500, attack: 20, source: "林"),
                W(1, hp: 70, maxHp: 70, attack: 7, source: "藻", ch: "苗"),
            }, new[] { Foe() });
            Assert.That(b.Cast("藻", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That((b.Summons[0].Hp, b.Summons[0].MaxHp, b.Summons[0].Attack), Is.EqualTo((570, 570, 27)),
                "嫁接回满后并入(E42 嫁接算本次召出)");
            Assert.That(b.Summons[1], Is.Null);
        }

        [Test]
        public void M8_MergedStats_SurviveSaveRoundTrip()
        {
            var tide = Tide();
            var b = Battle(new[] { tide }, new[] { W(1, hp: 50, maxHp: 100, attack: 10, source: "藻", ch: "苗") }, new[] { Foe() });
            b.Cast("藻");
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(tide), Config(), null,
                new Dictionary<string, EnemyDef> { ["怔"] = Foe() });
            Assert.That((restored.Summons[0].Hp, restored.Summons[0].MaxHp, restored.Summons[0].Attack), Is.EqualTo((450, 500, 40)));
        }

        // ================= M9 四木成林 SummonAct =================

        private static CharDef Grove() => WoodCard("𣛧", new[] { Body(ch: "木") },
            new[] { new EffectDef(EffectKind.SummonAct, 0) });

        [Test]
        public void M9_SummonAct_EachSummonStrikesOnce_NoMeterNoTickNoHealNoSproutNoBurn()
        {
            var buff = new StatusEffect { Kind = StatusKind.AttackBuff, Polarity = StatusPolarity.Buff, Magnitude = 5, TurnsLeft = 2 };
            var burn = new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = 3, TurnsLeft = -1 };
            var b = Battle(new[] { Grove() }, new[]
            {
                W(0, hp: 500, attack: 30, statuses: new[] { buff }, passive: new SummonPassive { HealAlly = 50 }),
                W(2, hp: 500, attack: 20, statuses: new[] { burn }, passive: new SummonPassive { SproutPercent = 50, SproutMax = 2 }),
            }, new[] { Foe() }, startingHp: 3000);
            var meters = (b.Summons[0].ActionMeter, b.Summons[2].ActionMeter);
            Assert.That(b.Cast("𣛧", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(SummonAttacksFrom(b, 0), Is.EqualTo(1));
            Assert.That(SummonAttacksFrom(b, 2), Is.EqualTo(1));
            var dmg = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).ToList();
            Assert.That(dmg.Select(e => e.Amount).ToArray(), Is.EqualTo(new[] { 35, 20 }), "按槽位升序,有效攻击(含 AttackBuff)");
            Assert.That((b.Summons[0].ActionMeter, b.Summons[2].ActionMeter), Is.EqualTo(meters), "不推进行动条");
            Assert.That(b.Summons[0].Statuses.Find(StatusKind.AttackBuff).TurnsLeft, Is.EqualTo(2), "不递减状态");
            Assert.That(b.Summons[2].Statuses.Find(StatusKind.Burn).Magnitude, Is.EqualTo(3), "不结算灼烧");
            Assert.That(b.PlayerHp, Is.EqualTo(3000), "不触发光环治疗");
            Assert.That(b.Summons.Count(s => s != null), Is.EqualTo(2), "不触发丛生");
        }

        [Test]
        public void M9_SummonAct_NoSummons_NoRoll()
        {
            var b = Battle(new[] { Grove() }, null, new[] { Foe(), Foe() });
            var before = b.Capture();
            b.Cast("𣛧", 0, attackMode: true);
            var after = b.Capture();
            Assert.That(after.TargetRandomState, Is.EqualTo(before.TargetRandomState));
        }

        // ================= M10 折桂 GrantChar =================

        private static CharDef Laurel(int perBattle = 1) => WoodCard("桂", new[] { Body(ch: "桂") },
            new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) },
            new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Passive, null, "折桂",
                new[] { new EffectDef(EffectKind.GrantChar, 1) }, trigger: TraitTrigger.OnKill, maxPerBattle: perBattle) });

        private static readonly CharDef GoldA = new("金甲", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, isComponent: false,
            rarity: CardRarity.Gold);
        private static readonly CharDef GoldB = new("金乙", Element.Metal, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, isComponent: false,
            rarity: CardRarity.Gold);
        private static readonly CharDef Red = new("赤", Element.Fire, recipe: new[] { "甲" },
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, isComponent: false, rarity: CardRarity.Red);

        private static BattleEngine LaurelBattle(BattleConfig config, int copies = 3, int perBattle = 1, params EnemyDef[] enemies)
        {
            var laurel = Laurel(perBattle);
            return new BattleEngine(RebalanceFixture.Graph(laurel, GoldA, GoldB, Red), config,
                Enumerable.Repeat("桂", copies).ToArray(), Array.Empty<string>(),
                enemies.Length > 0 ? enemies : new[] { Foe(10), Foe(10), Foe() }, seed: 5,
                cardLevels: new Dictionary<string, int> { ["桂"] = 5 });
        }

        private static List<string> Granted(BattleEngine b) => b.Library.Where(id => id != "桂").ToList();

        [Test]
        public void M10_GrantChar_OnKill_AddsOneOwnedTopTierChar_OncePerBattle()
        {
            var b = LaurelBattle(Config(owned: new[] { "桂", "金甲", "金乙" }));
            uint trait = b.Capture().TraitRandomState;
            Assert.That(b.Cast("桂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies.Count(e => !e.Alive), Is.EqualTo(2), "夹具前提:一次出字两次击杀");
            var got = Granted(b);
            Assert.That(got.Count, Is.EqualTo(1), "每场 1 次");
            Assert.That(got[0] == "金甲" || got[0] == "金乙", Is.True, "候选 = 已拥有里档位最高的一档(不含未拥有的赤)");
            Assert.That(b.Capture().TraitRandomState, Is.Not.EqualTo(trait), "用 _traitRandom 抽");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
            // 主 RNG 不动(Task 4 review Minor 2):与「同字表、桂没有折桂」的对照档同种子出同一张,主 RandomState 逐位相同
            var plain = WoodCard("桂", new[] { Body(ch: "桂") }, new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) });
            var control = new BattleEngine(RebalanceFixture.Graph(plain, GoldA, GoldB, Red), Config(owned: new[] { "桂", "金甲", "金乙" }),
                Enumerable.Repeat("桂", 3).ToArray(), Array.Empty<string>(), new[] { Foe(10), Foe(10), Foe() }, seed: 5,
                cardLevels: new Dictionary<string, int> { ["桂"] = 5 });
            control.Cast("桂", 0, attackMode: true);
            Assert.That(b.Capture().RandomState, Is.EqualTo(control.Capture().RandomState), "折桂不消耗主 RNG");
        }

        [Test]
        public void M10_GrantChar_NoOwnershipRecord_FallsBackToWholeTableTopTier()
        {
            var b = LaurelBattle(Config());
            b.Cast("桂", 0, attackMode: true);
            Assert.That(Granted(b), Is.EqualTo(new[] { "赤" }), "全字表非部件里的最高档");
        }

        [Test]
        public void M10_GrantChar_Fallback_SkipsLeafChars_LikeRewardPool()
        {
            // 没有拥有记录时退回全字表:没配方的字(IsLeaf,奖励池拿不到)不进候选,哪怕它档位最高
            var laurel = Laurel();
            var leafTop = new CharDef("赭", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
                isComponent: false, rarity: CardRarity.Red);
            var crafted = new CharDef("赫", Element.Fire, recipe: new[] { "甲" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 10) }, isComponent: false, rarity: CardRarity.Orange);
            var b = new BattleEngine(RebalanceFixture.Graph(laurel, leafTop, crafted), Config(),
                Enumerable.Repeat("桂", 3).ToArray(), Array.Empty<string>(), new[] { Foe(10), Foe() }, seed: 5,
                cardLevels: new Dictionary<string, int> { ["桂"] = 5 });
            b.Cast("桂", 0, attackMode: true);
            Assert.That(Granted(b), Is.EqualTo(new[] { "赫" }), "只在有配方的字里取最高档");
        }

        [Test]
        public void M10_GrantChar_LibraryFull_NotAdded_UseRefunded()
        {
            // 字库上限 2、起手 3 张:出一张后剩 2 = 满库 → 不入、退次;丢一张腾位后,下一次击杀照常入
            var b = LaurelBattle(Config(library: 2, owned: new[] { "金甲" }), copies: 3, perBattle: 1,
                Foe(10), Foe(100000), Foe(10), Foe(100000));
            Assert.That(b.Library.Count, Is.EqualTo(3), "夹具前提:起手 3 张");
            b.Cast("桂", 0, attackMode: true);
            Assert.That(b.Enemies.Count(e => !e.Alive), Is.EqualTo(2));
            Assert.That(Granted(b), Is.Empty, "满库不入");
            Assert.That(b.Capture().TraitUsesThisBattle.TryGetValue("场次:桂/5/Attack", out int used) ? used : 0, Is.EqualTo(0),
                "不计次:入队时记下的那一次退回");
        }

        [Test]
        public void M10_GrantChar_LibraryFull_ThenRoom_SecondKillStillGrants()
        {
            // 第一张:满库不入并退次;第二张出字前字库有空位 → 击杀照常入,证明次数确实退回
            var b = LaurelBattle(Config(library: 2, owned: new[] { "金甲" }), copies: 3, perBattle: 1,
                Foe(10), Foe(100000), Foe(100000), Foe(100000));
            b.Cast("桂", 0, attackMode: true);
            Assert.That(Granted(b), Is.Empty);
            Assert.That(b.Library.Count, Is.EqualTo(2));
            b.Discard("桂");
            b.Enemies[1].Hp = 5;   // 让第二张也能击杀
            Assert.That(b.Cast("桂", 1, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[1].Alive, Is.False);
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲" }), "次数已退回,第二次击杀照常入库");
        }

        [Test]
        public void M10_GrantChar_UsesSurviveSaveRoundTrip()
        {
            var laurel = Laurel();
            var cfg = Config(owned: new[] { "金甲" });
            var b = LaurelBattle(cfg, copies: 3, perBattle: 1, Foe(10), Foe(100000), Foe(100000));
            b.Cast("桂", 0, attackMode: true);
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲" }));
            var restored = BattleEngine.Restore(b.Capture(), RebalanceFixture.Graph(laurel, GoldA, GoldB, Red), cfg,
                new Dictionary<string, int> { ["桂"] = 5 }, new Dictionary<string, EnemyDef> { ["怔"] = Foe() });
            restored.Enemies[1].Hp = 5;
            restored.Cast("桂", 1, attackMode: true);
            Assert.That(restored.Enemies[1].Alive, Is.False);
            Assert.That(Granted(restored), Is.EqualTo(new[] { "金甲" }), "读档后次数仍在:不再入");
        }

        // ---- 终审 Important 1:折桂打死最后一名敌人时也入库(字库跨场;伤害类反应照旧随胜负丢弃)----

        [Test]
        public void M10_GrantChar_FinalBlow_SingleEnemy_StillGrants()
        {
            var b = LaurelBattle(Config(owned: new[] { "金甲" }), copies: 3, perBattle: 1, Foe(10));
            Assert.That(b.Cast("桂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "夹具前提:这一击清场");
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲" }), "单敌战的最后一击照常入库");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
            Assert.That(b.Capture().TraitUsesThisBattle["场次:桂/5/Attack"], Is.EqualTo(1), "照常计次");
        }

        [Test]
        public void M10_GrantChar_FinalBlow_Boss_StillGrants()
        {
            var boss = new EnemyDef("噬", Element.Heart, 10, 0,
                phases: new[] { new BossPhaseDef("甲", Element.Heart, 10, 0) });
            var b = LaurelBattle(Config(owned: new[] { "金甲" }), copies: 3, perBattle: 1, boss);
            Assert.That(b.Enemies[0].IsBoss, Is.True, "夹具前提:Boss");
            b.Cast("桂", 0, attackMode: true);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲" }), "Boss 战的最后一击照常入库");
        }

        [Test]
        public void M10_GrantChar_FinalBlow_MultiEnemy_ClosingKillStillGrants()
        {
            // 每场 2 次:第一张先杀 0 号(1 张),第二张收尾杀 1 号(清场)仍入第 2 张
            var b = LaurelBattle(Config(owned: new[] { "金甲" }), copies: 3, perBattle: 2, Foe(10), Foe(100000));
            b.Cast("桂", 0, attackMode: true);
            Assert.That(Granted(b).Count, Is.EqualTo(1), "夹具前提:非最后一击已入一张");
            b.Enemies[1].Hp = 5;
            b.Cast("桂", 1, attackMode: true);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲", "金甲" }), "多敌战收尾一击照常入库");
        }

        [Test]
        public void M10_GrantChar_FinalBlow_LibraryFull_NotAdded_UseRefunded()
        {
            // 字库上限 2、起手 3 张:出一张剩 2 = 满库 → 清场一击也不入、退次(满库口径与非终局一致)
            var b = LaurelBattle(Config(library: 2, owned: new[] { "金甲" }), copies: 3, perBattle: 1, Foe(10));
            b.Cast("桂", 0, attackMode: true);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(Granted(b), Is.Empty, "满库不入");
            Assert.That(b.Capture().TraitUsesThisBattle.TryGetValue("场次:桂/5/Attack", out int used) ? used : 0, Is.EqualTo(0),
                "退次");
        }

        [Test]
        public void M10_FinalBlow_DamageReactionsStillDropped()
        {
            // 同一击挂一条伤害类击杀时(OverkillSplash 之类不必要,直接用 DamageSingle):胜负已分时伤害反应照旧丢弃,
            // 只兑现 GrantChar。玩家不掉血、不出 Damage 事件之外的额外伤害事件,且队列清空(Capture 不抛)。
            var laurel = WoodCard("桂", new[] { Body(ch: "桂") },
                new[] { new EffectDef(EffectKind.DamageSingle, 100, shape: TargetArea.All) },
                new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Passive, null, "折桂",
                    new[] { new EffectDef(EffectKind.GrantChar, 1), new EffectDef(EffectKind.HealSelf, 50) },
                    trigger: TraitTrigger.OnKill, maxPerBattle: 1) });
            var b = new BattleEngine(RebalanceFixture.Graph(laurel, GoldA), Config(owned: new[] { "金甲" }),
                Enumerable.Repeat("桂", 3).ToArray(), Array.Empty<string>(), new[] { Foe(10) }, seed: 5,
                startingHp: 1000, cardLevels: new Dictionary<string, int> { ["桂"] = 5 });
            b.Cast("桂", 0, attackMode: true);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(Granted(b), Is.EqualTo(new[] { "金甲" }), "GrantChar 兑现");
            Assert.That(b.PlayerHp, Is.EqualTo(1000), "同一条反应里的非 GrantChar 效果照旧丢弃");
            Assert.DoesNotThrow(() => b.Capture(), "队列已清空");
        }

        // ================= M11 水草 HealMirror =================

        private static SummonSnapshot Algae(int slot, int hp, int mirror = 100) =>
            W(slot, hp: hp, maxHp: 5000, source: "藻", ch: "藻", passive: new SummonPassive { HealMirror = mirror });

        [Test]
        public void M11_HealMirror_MirrorsCastHeal_ExcludingHealToMirrors()
        {
            var rain = Card("雨", new EffectDef(EffectKind.HealAll, 100));
            var b = Battle(new[] { rain }, new[] { Algae(0, 1000), W(1, hp: 500, maxHp: 1000), Algae(2, 1000, mirror: 50) },
                new[] { Foe() }, startingHp: 3000);
            b.Cast("雨");
            // 本次治疗:玩家 100 + 1 号 100(两只藻灵各回的 100 不算)= 200
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1000 + 100 + 200));
            Assert.That(b.Summons[2].Hp, Is.EqualTo(1000 + 100 + 100), "× 50%");
            Assert.That(b.Summons[1].Hp, Is.EqualTo(600), "非藻灵不镜像");
            Assert.That(b.PlayerHp, Is.EqualTo(3100));
        }

        [Test]
        public void M11_HealMirror_OnlyActualHeal_OverflowIgnored()
        {
            var spring = Card("泉", new EffectDef(EffectKind.HealSelf, 500));
            var b = Battle(new[] { spring }, new[] { Algae(0, 1000) }, new[] { Foe() }, startingHp: 5800);
            b.Cast("泉");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1200), "玩家只回了 200");
        }

        [Test]
        public void M11_HealMirror_SeedsAndHealAllyAuraDoNotCount()
        {
            var seed = Card("种", new EffectDef(EffectKind.Seed, 300, turns: 3));
            var b = Battle(new[] { seed }, new[]
            {
                Algae(0, 4000),
                W(1, hp: 500, maxHp: 1000, passive: new SummonPassive { HealAlly = 100 }),
            }, new[] { Foe() }, startingHp: 1000);
            b.Cast("种", 0);
            Assert.That(b.Summons[0].Hp, Is.EqualTo(4000), "挂种本身不治疗");
            b.EndTurn();
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Heal), Is.GreaterThan(0), "夹具前提:种 / 桂香治疗发生了");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(4000 + 100), "只有桂香光环给它本身的 100;种 / 桂香不被镜像");
        }

        [Test]
        public void M11_HealMirror_ReactionHealDoesNotCount()
        {
            var striker = new CharDef("击", Element.Heart, effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Both, TraitForm.Passive, null, "愈",
                    new[] { new EffectDef(EffectKind.HealSelf, 200) }, trigger: TraitTrigger.OnKill) });
            var b = new BattleEngine(RebalanceFixture.Graph(striker), Config(), new[] { "击", "击" }, Array.Empty<string>(),
                new[] { Foe(10), Foe() }, seed: 3, startingHp: 3000, cardLevels: new Dictionary<string, int> { ["击"] = 5 },
                startingSummons: new[] { Algae(0, 1000) });
            b.Cast("击", 0);
            Assert.That(b.PlayerHp, Is.GreaterThan(3000), "夹具前提:击杀时反应治疗了玩家");
            Assert.That(b.Summons[0].Hp, Is.EqualTo(1000), "反应里的治疗不算");
        }

        // ================= ConfigLoader =================

        private static RecipeGraph Load(string feature, string traits = "") => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""藤"",""element"":""Wood"",""effects"":[" + feature + @"],
              ""attackEffects"":[{""kind"":""DamageSingle"",""value"":10}],""traits"":[" + traits + @"]}]}");

        private const string SummonJson = @"{""kind"":""Summon"",""value"":300,""count"":1,""attack"":20,""summonChar"":""藤""}";

        [Test]
        public void Loader_Bind_RequiresEnemyPickAndPositiveValue()
        {
            Assert.That(Load(SummonJson + @",{""kind"":""Bind"",""value"":100,""pick"":""HighestHp""}").Get("藤").Effects[1].Pick,
                Is.EqualTo(EffectPick.HighestHp));
            Assert.Throws<ConfigException>(() => Load(SummonJson + @",{""kind"":""Bind"",""value"":100}"), "必须写 pick");
            Assert.Throws<ConfigException>(() => Load(SummonJson + @",{""kind"":""Bind"",""value"":100,""pick"":""SummonedThisCast""}"),
                "只收敌方选择器");
            Assert.Throws<ConfigException>(() => Load(SummonJson + @",{""kind"":""Bind"",""value"":0,""pick"":""HighestHp""}"), "量须 > 0");
        }

        [Test]
        public void Loader_GrantChar_OnlyInOnKillTraits()
        {
            const string kill = @"{""slot"":""Lv5"",""face"":""Attack"",""form"":""Passive"",""trigger"":""OnKill"",""name"":""折桂"",""maxPerBattle"":1,
                ""effects"":[{""kind"":""GrantChar"",""value"":1}]}";
            Assert.That(Load(SummonJson, kill).Get("藤").Traits[0].Effects[0].Kind, Is.EqualTo(EffectKind.GrantChar));
            Assert.Throws<ConfigException>(() => Load(SummonJson + @",{""kind"":""GrantChar"",""value"":1}"), "本体不能写");
            Assert.Throws<ConfigException>(() => Load(SummonJson,
                @"{""slot"":""Lv5"",""face"":""Attack"",""name"":""折桂"",""effects"":[{""kind"":""GrantChar"",""value"":1}]}"), "出字时不能写");
            Assert.Throws<ConfigException>(() => Load(SummonJson,
                @"{""slot"":""Lv5"",""face"":""Attack"",""form"":""Passive"",""trigger"":""OnKill"",""name"":""折桂"",
                ""effects"":[{""kind"":""GrantChar"",""value"":0}]}"), "张数须 > 0");
        }
    }
}
