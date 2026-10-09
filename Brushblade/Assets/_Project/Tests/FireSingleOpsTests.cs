using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-火 Task 5:其余单点效果 —— 附录 N9 ExtraStrike(星火 / 烈焚)、N10 Thaw(水火相激)/ SelfCost(玉石俱焚,G9)、
    /// N11 Reveal(光耀);另补 Task 4 Ruling 1:本面没有 DamageSingle 时 bodyPercent 改按攻击面本体解析(Fold 与入队两处)。
    ///
    /// 夹具口径同 FireHookTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、无甲 10 万血靶子、暴击率 0(另注明的除外)。</summary>
    public class FireSingleOpsTests
    {
        private const int Hp = 100000;

        private static BattleConfig Config(int crit = 0) => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, PlayerCritChance = crit };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int level = 1, int crit = 0, int? hp = null) =>
            new(RebalanceFixture.Graph(defs), Config(crit), defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(),
                Array.Empty<string>(), enemies, seed: 1, startingHp: hp, cardLevels: defs.ToDictionary(d => d.Id, _ => level));

        private static EnemyDef Mob(int hp = Hp, EnemyAbility ability = EnemyAbility.None) => new("怔", Element.Heart, hp, 0, ability);

        private static EnemyDef[] Mobs(int n) => Enumerable.Range(0, n).Select(_ => Mob()).ToArray();

        private static TraitDef Trait(TraitSlot slot, TraitFace face, params EffectDef[] effects) =>
            new(slot, face, TraitForm.Active, null, "试" + (int)slot, effects);

        private static void SetBurn(BattleEngine b, int i, int stacks) =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = stacks, TurnsLeft = -1, Potency = 100 });

        private static int Lost(BattleEngine b, int i) => Hp - b.Enemies[i].Hp;

        private sealed class Recorder : IBattleHookListener
        {
            public readonly List<HookArgs> Log = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => Log.Add(args);
            public int Count(HookKind k) => Log.Count(a => a.Kind == k);
        }

        // ================= Task 4 Ruling 1:bodyPercent 在本面没有伤害时读攻击面本体 =================

        [Test]
        public void BodyPercent_FaceWithoutDamage_Fold_ResolvesFromAttackBody()
        {
            // 炸·埋雷的形状:燃面本体只有灼,攻击面本体 DamageSingle 40;埋雷写 bodyPercent 200
            var def = new CharDef("炸", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.BurnSingle, 2) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Feature, new EffectDef(EffectKind.Mine, 0, bodyPercent: 200)) });
            var mine = TraitRules.CastEffects(def, CardFace.Feature, 5).Single(e => e.Kind == EffectKind.Mine);
            Assert.That(mine.Value, Is.EqualTo(80), "燃面没有 DamageSingle → 取攻击面本体 40 × 200%");

            var b = Battle(new[] { def }, new[] { Mob() }, level: 5);
            Assert.That(b.Cast("炸", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.Mine)?.Magnitude, Is.EqualTo(MetaRules.ScaleByCardLevel(80, 5)));
        }

        [Test]
        public void BodyPercent_FaceWithoutDamage_Reaction_ResolvesFromAttackBody()
        {
            // 燃面本体 = 引爆(打死敌人 0),击杀时特性写在燃面:全体伤害 bodyPercent 50 → 攻击面本体 40 × 50%
            var def = new CharDef("爆", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.Detonate, 0) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 40) },
                traits: new[] { new TraitDef(TraitSlot.Lv8, TraitFace.Feature, TraitForm.Passive, null, "连",
                    new[] { new EffectDef(EffectKind.DamageSingle, 0, shape: TargetArea.All, bodyPercent: 50) },
                    TraitTrigger.OnKill) });
            var b = Battle(new[] { def }, new[] { Mob(hp: 10), Mob() }, level: 8);
            SetBurn(b, 0, 5);
            Assert.That(b.Cast("爆", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(Lost(b, 1), Is.EqualTo(MetaRules.ScaleByCardLevel(20, 8)), "反应的全体伤害 = 攻击面本体 40 × 50%(吃等级)");
        }

        // ================= N9 ExtraStrike =================

        private static CharDef Striker(EffectDef body, EffectDef extra, params EffectDef[] moreBody) => new("焱", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { body }.Concat(moreBody).ToArray(),
            traits: extra == null ? null : new[] { Trait(TraitSlot.Lv5, TraitFace.Attack, extra) });

        [Test]
        public void ExtraStrike_MostBurn_HitsHighestBurn_ForBodyPercent()
        {
            // 烈焚:对灼层最高的敌人追加一次本体 50%
            var def = Striker(new EffectDef(EffectKind.DamageSingle, 100), new EffectDef(EffectKind.ExtraStrike, 50, pick: EffectPick.MostBurn));
            var b = Battle(new[] { def }, Mobs(3), level: 5);
            SetBurn(b, 1, 1);
            SetBurn(b, 2, 3);
            Assert.That(b.Cast("焱", 0, attackMode: true), Is.EqualTo(BattleError.None));
            int body = MetaRules.ScaleByCardLevel(100, 5);
            Assert.That(Lost(b, 0), Is.EqualTo(body), "主目标只吃本体");
            Assert.That(Lost(b, 1), Is.EqualTo(0));
            Assert.That(Lost(b, 2), Is.EqualTo(body * 50 / 100), "灼层最高者吃本体(吃等级)× 50%");
        }

        [Test]
        public void ExtraStrike_PerBurningHit_OneRandomStrikePerPreCastBurningHitTarget()
        {
            // 星火:全体本体,出字前带灼的命中目标两名 → 追加两发 30%,每发随机目标(_traitRandom)
            var extra = new EffectDef(EffectKind.ExtraStrike, 30, pick: EffectPick.Random, perBurningHit: true);
            var body = new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.All);
            var withTrait = Battle(new[] { Striker(body, extra) }, Mobs(3), level: 5);
            var plain = Battle(new[] { Striker(body, null) }, Mobs(3), level: 5);
            foreach (var b in new[] { withTrait, plain }) { SetBurn(b, 0, 2); SetBurn(b, 1, 1); }
            withTrait.Cast("焱", -1, attackMode: true);
            plain.Cast("焱", -1, attackMode: true);
            int strike = MetaRules.ScaleByCardLevel(10, 5) * 30 / 100;
            int total = Enumerable.Range(0, 3).Sum(i => Lost(withTrait, i));
            int plainTotal = Enumerable.Range(0, 3).Sum(i => Lost(plain, i));
            Assert.That(total - plainTotal, Is.EqualTo(2 * strike), "两名出字前带灼的命中目标 → 两发");
            Assert.That(withTrait.Capture().TraitRandomState, Is.Not.EqualTo(plain.Capture().TraitRandomState), "随机目标走特性随机流");
            Assert.That(withTrait.Capture().RandomState, Is.EqualTo(plain.Capture().RandomState), "暴击率 0:不摇战斗随机流");
        }

        [Test]
        public void ExtraStrike_NoPreCastBurningHit_NoRollAnywhere_Identical()
        {
            // 本体自己上的灼不算(出字前快照);没有可追加的就一个随机数都不摇 —— 暴击率 50 时 _random 也与无特性逐位相同
            var extra = new EffectDef(EffectKind.ExtraStrike, 30, pick: EffectPick.Random, perBurningHit: true);
            var body = new EffectDef(EffectKind.DamageSingle, 10, shape: TargetArea.All);
            var burnAll = new EffectDef(EffectKind.BurnAll, 2);
            var withTrait = Battle(new[] { Striker(body, extra, burnAll) }, Mobs(3), level: 5, crit: 50);
            var plain = Battle(new[] { Striker(body, null, burnAll) }, Mobs(3), level: 5, crit: 50);
            withTrait.Cast("焱", -1, attackMode: true);
            plain.Cast("焱", -1, attackMode: true);
            for (int i = 0; i < 3; i++) Assert.That(Lost(withTrait, i), Is.EqualTo(Lost(plain, i)));
            var (s1, s2) = (withTrait.Capture(), plain.Capture());
            Assert.That((s1.RandomState, s1.TraitRandomState), Is.EqualTo((s2.RandomState, s2.TraitRandomState)));
        }

        [Test]
        public void ExtraStrike_TakesElementL3_AndKe_SameAsBodyHit()
        {
            // 终审 6:追加一击的基数吃五行 L3(与本体同一条 ApplyElementPercent),落点过生克(火克金 ×1.5)。
            // 主目标只吃本体一击,烈焚(MostBurn)那一发落在另一名敌人身上:两者之比 = 50%
            var def = new CharDef("焚", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.Shield, 10) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Attack, new EffectDef(EffectKind.ExtraStrike, 50, pick: EffectPick.MostBurn)) });
            var l3 = new int[Enum.GetValues(typeof(Element)).Length];
            l3[(int)Element.Fire] = 20;
            var config = Config();
            config.ElementEffectPercent = l3;
            var metal = new[] { new EnemyDef("怔", Element.Metal, Hp, 0), new EnemyDef("怔", Element.Metal, Hp, 0) };
            var b = new BattleEngine(RebalanceFixture.Graph(def), config, new[] { "焚", "焚", "焚" }, Array.Empty<string>(),
                metal, seed: 1, cardLevels: new Dictionary<string, int> { ["焚"] = 5 });
            SetBurn(b, 1, 3);
            Assert.That(b.Cast("焚", 0, attackMode: true), Is.EqualTo(BattleError.None));
            int bodyHit = Lost(b, 0);
            int plainBody = MetaRules.ScaleByCardLevel(100, 5);
            Assert.That(bodyHit, Is.EqualTo(plainBody * 120 / 100 * 3 / 2).Within(1), "前提:本体一击 = L3 +20% × 火克金 1.5");
            Assert.That(Lost(b, 1), Is.EqualTo(bodyHit / 2).Within(1), "追加一击 = 同口径的本体 × 50%(L3 与生克都吃)");
        }

        [Test]
        public void ExtraStrike_TargetDeadOrOnlyIfFails_NoCritRoll()
        {
            // 终审 6:这一发作罢时不摇暴击(_random 与无特性逐位相同);暴击率 50 让「摇了」可见
            var plainBody = new EffectDef(EffectKind.DamageSingle, 100);
            CharDef With(EffectDef extra) => Striker(plainBody, extra);
            // ① 目标已死:本体打死主目标,追加一击 pick Primary 落空
            var dead = Battle(new[] { With(new EffectDef(EffectKind.ExtraStrike, 50)) }, new[] { Mob(hp: 1), Mob() }, level: 5, crit: 50);
            var deadPlain = Battle(new[] { With(null) }, new[] { Mob(hp: 1), Mob() }, level: 5, crit: 50);
            dead.Cast("焱", 0, attackMode: true);
            deadPlain.Cast("焱", 0, attackMode: true);
            Assert.That(dead.Enemies[0].Alive, Is.False, "前提:本体打死了主目标");
            Assert.That(dead.Capture().RandomState, Is.EqualTo(deadPlain.Capture().RandomState), "目标已死:不摇暴击");
            // ② 条件不满足:if Burning,目标没有灼
            var gated = Battle(new[] { With(new EffectDef(EffectKind.ExtraStrike, 50, onlyIf: DamageCondition.Burning)) }, Mobs(1), level: 5, crit: 50);
            var gatedPlain = Battle(new[] { With(null) }, Mobs(1), level: 5, crit: 50);
            gated.Cast("焱", 0, attackMode: true);
            gatedPlain.Cast("焱", 0, attackMode: true);
            Assert.That(Lost(gated, 0), Is.EqualTo(Lost(gatedPlain, 0)), "条件不满足:没有追加");
            Assert.That(gated.Capture().RandomState, Is.EqualTo(gatedPlain.Capture().RandomState), "条件不满足:不摇暴击");
        }

        // ================= N10 Thaw =================

        private static CharDef Thawer(EffectPick pick = EffectPick.Primary) =>
            new("融", Element.Heart, effects: new[] { new EffectDef(EffectKind.Thaw, 0, pick: pick) });

        [Test]
        public void Thaw_Freeze_RemovedWithEqualFrostResist()
        {
            var b = Battle(new[] { Thawer() }, new[] { Mob() });
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 2, TurnsLeft = 1 });
            Assert.That(BattleEngine.EffectNeedsTarget(new EffectDef(EffectKind.Thaw, 0)), Is.True);
            Assert.That(b.Cast("融", 0), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.FrostResist)?.TurnsLeft, Is.EqualTo(2), "R1:冻结结束 → 等长(冻结时长)霜抗");
        }

        [Test]
        public void Thaw_SlowAndIceStall_Removed_HasteKept()
        {
            var b = Battle(new[] { Thawer(EffectPick.All) }, Mobs(2));
            var bag0 = b.Enemies[0].Statuses;
            bag0.Apply(new StatusEffect { Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff, Magnitude = -50, TurnsLeft = 2, SourceId = "淋" });
            bag0.Apply(new StatusEffect { Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Buff, Magnitude = 30, TurnsLeft = 2, SourceId = "疾" });
            b.Enemies[1].Statuses.Apply(new StatusEffect { Kind = StatusKind.IceStall, Polarity = StatusPolarity.Debuff, Magnitude = 2, TurnsLeft = -1 });
            Assert.That(b.Cast("融", -1), Is.EqualTo(BattleError.None));
            var speeds = bag0.All.Where(s => s.Kind == StatusKind.SpeedModifier).Select(s => s.Magnitude).ToList();
            Assert.That(speeds, Is.EqualTo(new[] { 30 }), "只解负的减速,正的加速留着");
            Assert.That(bag0.Has(StatusKind.FrostResist), Is.False, "减速不发霜抗");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.IceStall), Is.False);
            Assert.That(b.Enemies[1].Statuses.Find(StatusKind.FrostResist)?.TurnsLeft, Is.EqualTo(3), "冰滞照自然结束给 N+1");
        }

        [Test]
        public void WaterFire_AmplifyIfControlled_DamageFirst_ThenThaw()
        {
            // 水火相激:目标被冻结或减速时伤害 ×2(Amplify 100,出字前快照),伤害之后解除冻结
            var def = new CharDef("蒸", Element.Heart,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) },
                traits: new[] { Trait(TraitSlot.Lv5, TraitFace.Both,
                    new EffectDef(EffectKind.Amplify, 100, onlyIf: DamageCondition.Controlled),
                    new EffectDef(EffectKind.Thaw, 0, onlyIf: DamageCondition.Controlled)) });
            var b = Battle(new[] { def }, Mobs(2), level: 5);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Freeze, Polarity = StatusPolarity.Debuff, Magnitude = 1, TurnsLeft = 1 });
            int body = MetaRules.ScaleByCardLevel(100, 5);
            Assert.That(b.Cast("蒸", 0), Is.EqualTo(BattleError.None));
            Assert.That(Lost(b, 0), Is.EqualTo(body * 2));
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Freeze), Is.False);
            Assert.That(b.Enemies[0].Statuses.Find(StatusKind.FrostResist)?.TurnsLeft, Is.EqualTo(1));

            Assert.That(b.Cast("蒸", 1), Is.EqualTo(BattleError.None));
            Assert.That(Lost(b, 1), Is.EqualTo(body), "未受控:不加成");
            Assert.That(b.Enemies[1].Statuses.Has(StatusKind.FrostResist), Is.False);
        }

        // ================= N10b SelfCost(G9) =================

        private static CharDef Reckless() => new("焚", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.HealSelf, 10) },
            traits: new[] { Trait(TraitSlot.Lv8, TraitFace.Both, new EffectDef(EffectKind.SelfCost, 20)) });

        [Test]
        public void SelfCost_AtCastStart_LosesCurrentPercent_CrossesThreshold_NotAHit()
        {
            var b = Battle(new[] { Reckless() }, new[] { Mob() }, level: 8, hp: 275);   // 55%
            var r = new Recorder();
            b.AddHookListener(r);
            Assert.That(b.Cast("焚", -1), Is.EqualTo(BattleError.None));
            // 开头扣:275 − ⌊275 × 20%⌋ = 220,再回 15(10 吃 Lv8)= 235(若排在末尾会是 290 − 58 = 232)
            Assert.That(b.PlayerHp, Is.EqualTo(235), "自损在出字开头、按当前生命 20%,不吃卡等级");
            Assert.That(r.Count(HookKind.HpThresholdCrossed), Is.EqualTo(1), "失去生命也会跌破 50%(阈值不是受击)");
            Assert.That(r.Count(HookKind.PlayerHit), Is.EqualTo(0), "R4:自损不算受击");
        }

        [Test]
        public void SelfCost_NeverLethal()
        {
            var b = Battle(new[] { Reckless() }, new[] { Mob() }, level: 8, hp: 1);
            Assert.That(b.Cast("焚", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerHp, Is.EqualTo(1 + MetaRules.ScaleByCardLevel(10, 8)), "1 血不扣(至少留 1 点),随后回血");
            Assert.That(b.Phase, Is.Not.EqualTo(BattlePhase.Lost));

            var low = Battle(new[] { new CharDef("焚", Element.Heart, effects: new[] { new EffectDef(EffectKind.SelfCost, 50) }) },
                new[] { Mob() }, hp: 3);
            Assert.That(low.Cast("焚", -1), Is.EqualTo(BattleError.None));
            Assert.That(low.PlayerHp, Is.EqualTo(2), "⌊3 × 50%⌋ = 1;不会扣到 0");
        }

        // ================= N11 Reveal =================

        [Test]
        public void Reveal_DisguiseAndObscure_PlainIsNoOp()
        {
            var def = new CharDef("灿", Element.Heart, effects: new[] { new EffectDef(EffectKind.Reveal, 0, pick: EffectPick.All) });
            var b = Battle(new[] { def }, new[]
            {
                new EnemyDef("伪", Element.Wood, Hp, 0, EnemyAbility.Disguise),
                new EnemyDef("僻", Element.Wood, Hp, 0, EnemyAbility.Obscure),
                new EnemyDef("常", Element.Wood, Hp, 0),
            });
            Assert.That(b.Enemies[0].ApparentElement, Is.Not.EqualTo(b.Enemies[0].Element), "夹具:通假字开局伪装");
            Assert.That(b.Enemies[1].ApparentElement, Is.Null, "夹具:生僻字开局未读懂");
            Assert.That(b.Cast("灿", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].ApparentElement, Is.EqualTo(b.Enemies[0].Element));
            Assert.That(b.Enemies[1].ApparentElement, Is.EqualTo(b.Enemies[1].Element));
            Assert.That(b.Enemies[2].ApparentElement, Is.EqualTo(Element.Wood));
            var revealed = b.LastEvents.Where(e => e.Kind == BattleEventKind.EnemyRevealed).Select(e => e.TargetIndex).ToList();
            Assert.That(revealed, Is.EqualTo(new[] { 0, 1 }), "普通敌人空转,不发事件");

            // 已揭示过再揭示:空转
            Assert.That(b.Cast("灿", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyRevealed), Is.EqualTo(0));
        }

        // ================= 字表加载 =================

        private static EffectDef LoadOne(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""焱"",""element"":""Fire"",""effects"":[{""kind"":""DamageSingle"",""value"":40}],
              ""traits"":[{""slot"":""Lv5"",""face"":""Feature"",""form"":""Active"",""name"":""星"",""effects"":[" + effect + "]}]}]}")
            .Get("焱").Traits[0].Effects[0];

        [Test]
        public void Config_SingleOps_Parse()
        {
            var x = LoadOne(@"{""kind"":""ExtraStrike"",""value"":30,""pick"":""Random"",""perBurningHit"":true}");
            Assert.That((x.Kind, x.Value, x.Pick, x.PerBurningHit), Is.EqualTo((EffectKind.ExtraStrike, 30, EffectPick.Random, true)));
            var t = LoadOne(@"{""kind"":""Thaw"",""onlyIf"":""Controlled""}");
            Assert.That((t.Kind, t.OnlyIf), Is.EqualTo((EffectKind.Thaw, DamageCondition.Controlled)));
            Assert.That(LoadOne(@"{""kind"":""SelfCost"",""value"":20}").Value, Is.EqualTo(20));
            Assert.That(LoadOne(@"{""kind"":""Reveal""}").Kind, Is.EqualTo(EffectKind.Reveal));
        }

        [Test]
        public void Config_SingleOps_RejectBadShapes()
        {
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""BurnSingle"",""value"":2,""perBurningHit"":true}"), "perBurningHit 只给 ExtraStrike");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""ExtraStrike"",""value"":0}"), "追加一击须 > 0%");
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""SelfCost"",""value"":0}"));
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""SelfCost"",""value"":100}"), "自损须 1–99%");
            // 终审 6:自损在出字开头结算、不经条件门 —— 写 onlyIf 会静默无效;写进每击附带会每击扣一次血
            Assert.Throws<ConfigException>(() => LoadOne(@"{""kind"":""SelfCost"",""value"":20,""onlyIf"":""Burning""}"), "自损不能带条件门");
            Assert.Throws<ConfigException>(() => LoadOne(
                @"{""kind"":""DamageSingle"",""value"":10,""perHit"":[{""kind"":""SelfCost"",""value"":20}]}"), "自损不能写进每击附带");
        }
    }
}
