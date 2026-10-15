using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-木 Task 6:木系 46 格专属特性的数据落表 —— 读真实 chars.json。
    /// 先用一张表钉死「每条特性落在正确的 (槽, 面, 形态),效果按书写顺序、关键字段(含本命字段)逐条对得上特性表那一行」,
    /// 再按机制类别各取一条做集成测试(附录 T6 列的 15 类:守护转移、林荫封顶 20、森严 ≥3、良弓进预算、荆刺进预算、
    /// 攀附每次行动、绿潮并入不触发阵亡、落木全体、秋实入池、折桂每场 1 次、林立 2×60%、擎天不给新召出的、万木回春 30%、
    /// 双木拆字、E38 生面精进放大召唤)。机制本身的单元级测试在 Wood* 系列;这里只证明「那一行写出来的配置在真字上接上了」。
    ///
    /// 夹具:敌人一律心属性(木打心 1.0×)、0 甲;玩家攻击 100、最大生命 6000、暴击率 0、基础护甲 0;六个木灵槽全开。
    /// 数值是 (F) 起值,改表必须同步这里。</summary>
    public class WoodExclusiveDataTests
    {
        private const string Ten = "";   // 𣛧 的 PUA 代理码位(同 TraitDataCoverageTests)
        private const int Hp = 1000000;

        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static BattleConfig Config(IReadOnlyCollection<string> unlocked = null) => new()
        {
            PlayerMaxHp = 6000, PlayerAttack = 100, ApPerTurn = 200, BossPhaseJitterPercent = 0,
            UnlockedSummonSlots = BattleEngine.AllSummonSlotsMask, UnlockedChars = unlocked,
        };

        private static BattleEngine Battle(string id, int level, EnemyDef[] enemies, int copies = 10,
            IReadOnlyList<SummonSnapshot> summons = null, BattleConfig config = null, RecipeGraph graph = null,
            IReadOnlyList<string> pool = null) =>
            new(graph ?? Graph, config ?? Config(), Enumerable.Repeat(id, copies).ToArray(), pool ?? Array.Empty<string>(),
                enemies, seed: 1, cardLevels: new Dictionary<string, int> { [id] = level }, startingSummons: summons);

        /// <summary>只打玩家的远程(Focus.Player)。</summary>
        private static EnemyDef Shooter(int attack, int hp = Hp) =>
            new("盯", Element.Heart, hp, attack, range: AttackRange.Ranged, focus: AttackFocus.Player);

        private static EnemyDef Melee(int attack, int hp = Hp) => new("怔", Element.Heart, hp, attack);

        private static EnemyDef Dummy(int hp = Hp) => new("靶", Element.Heart, hp, 0);

        private static void Cast(BattleEngine b, string id, int target = -1, bool attack = false) =>
            Assert.That(b.Cast(id, target, attackMode: attack), Is.EqualTo(BattleError.None), id);

        private static int Dealt(BattleEngine b, EffectSource source, int enemy = -1) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == source && (enemy < 0 || e.TargetIndex == enemy))
            .Sum(e => e.Amount);

        private static List<SummonState> Alive(BattleEngine b) => b.Summons.Where(s => s != null && s.Alive).ToList();

        private sealed class HookLog : IBattleHookListener
        {
            public readonly List<HookArgs> All = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => All.Add(args);
        }

        /// <summary>按书写顺序把一条效果的种类、数值与非缺省字段(含木系新 token 与本命字段)压成一行。</summary>
        private static string Sig(EffectDef e)
        {
            var p = new List<string> { $"{e.Kind} {e.Value}" };
            if (e.Turns != 0) p.Add($"turns {e.Turns}");
            if (e.Pick != EffectPick.Primary) p.Add($"pick {e.Pick}");
            if (e.OnlyIf != DamageCondition.None) p.Add($"if {e.OnlyIf}");
            if (e.Kind == EffectKind.Amplify) p.Add($"scope {e.Scope}");
            if (e.Shape != TargetArea.Single) p.Add($"shape {e.Shape}");
            if (e.ShapePercent != 100) p.Add($"shapePct {e.ShapePercent}");
            if (e.Shots != 0) p.Add($"shots {e.Shots}");
            if (e.ShotPercent != 100) p.Add($"shotPct {e.ShotPercent}");
            if (e.HitCount != 1) p.Add($"hits {e.HitCount}");
            if (e.HitPercent != 100) p.Add($"hitPct {e.HitPercent}");
            if (e.ArmorIgnorePercent != 0) p.Add($"armorIgnore {e.ArmorIgnorePercent}");
            if (e.Kind == EffectKind.Augment) p.Add($"of {e.AugmentKind}.{e.AugmentField}");
            if (e.ScaleBy != ScaleBasis.None) p.Add($"per {e.ScaleBy}");
            if (e.Kind == EffectKind.SummonSapling || e.Kind == EffectKind.Summon) p.Add($"count {e.SummonCount}");
            if (e.PercentOfMax) p.Add("pct");
            if (e.SummonAtkStrikePercent != 0) p.Add($"summonAtkStrike {e.SummonAtkStrikePercent}");
            if (e.HostAtkStrikePercent != 0) p.Add($"hostAtkStrike {e.HostAtkStrikePercent}");
            if (e.HostArmorStrikePercent != 0) p.Add($"hostArmorStrike {e.HostArmorStrikePercent}");
            if (e.SummonPercent != 100) p.Add($"summonPercent {e.SummonPercent}");
            if (e.FromBody) p.Add("fromBody");
            if (e.Sprout) p.Add("sprout");
            if (e.OwnSummons) p.Add("own");
            if (e.ByArmor) p.Add("byArmor");
            if (e.ReviveHpPercent != 0) p.Add($"pct {e.ReviveHpPercent}");
            if (e.Passive != null) p.Add("passive{" + PassiveSig(e.Passive) + "}");
            if (e.PerHit.Count > 0) p.Add("perHit[" + string.Join(";", e.PerHit.Select(Sig)) + "]");
            return string.Join(" ", p);
        }

        /// <summary>本命里所有非缺省字段,按名排序。</summary>
        private static string PassiveSig(SummonPassive passive)
        {
            var list = new List<string>();
            foreach (var prop in typeof(SummonPassive).GetProperties())
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                var v = prop.GetValue(passive);
                if (v == null || v is int i && i == 0 || v is bool f && !f || v is TargetArea a && a == TargetArea.Single) continue;
                list.Add(prop.Name + "=" + v);
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list);
        }

        // ================= 表驱动:46 格 51 行 =================

        private const TraitSlot L4 = TraitSlot.Lv4, L5 = TraitSlot.Lv5, L6 = TraitSlot.Lv6, L8 = TraitSlot.Lv8;
        private const TraitFace Atk = TraitFace.Attack, Ftr = TraitFace.Feature, Both = TraitFace.Both;

        // (字, 槽, 面, 名, 效果签名 ——「|」分隔,按 chars.json 落表顺序 = 书写顺序)。
        // 本命追加(E37)= `Summon 0` + 本命字段;Fold 时并进本面第一条本体召唤(见下方 Fold 测试)
        private static readonly (string Ch, TraitSlot Slot, TraitFace Face, string Name, string Sigs)[] Table =
        {
            ("花", L8, Atk, "移花接木", "Charm 100 turns 1"),   // spec A D15:花攻面不再有魅惑,魅惑挪到 Lv8 招牌
            ("藤", L8, Ftr, "攀附", "Bind 100 pick HighestHp"),
            ("箭", L5, Ftr, "木箭", "SummonRanged 0"),
            ("箭", L8, Atk, "万箭齐发", "Reshape 0 shape Scatter shots 5 shotPct 40|SummonStrike 50"),
            ("楸", L4, Atk, "先声", "Amplify 26 if FirstCastThisTurn scope All"),   // spec C §2:攻面 秋实 改名先声
            ("楸", L4, Ftr, "秋实", "Summon 0 count 1 passive{DeathPart=True}"),
            ("楸", L5, Atk, "落叶", "BurnSingle 1 pick All"),
            ("楸", L8, Atk, "秋风扫叶", "Reshape 0 shape Row shapePct 60 hitPct 60|BurnSingle 1 pick HitTargets"),
            ("楸", L8, Ftr, "一叶知秋", "TurnPulse 0 turns 3 perHit[Vulnerable 30 turns 1 pick Random if OwnSummonAlive]"),
            ("荆", L4, Atk, "精进", "Amplify 18 scope All"),   // spec C §2:攻面 披荆 改名精进
            ("荆", L4, Ftr, "披荆", "Summon 0 count 1 passive{ThornsBleed=35}"),
            ("荆", L5, Atk, "荆刺", "ThornMark 30 turns 2"),
            ("荆", L8, Atk, "荆鞭", "Reshape 0 hits 3 hitPct 40 perHit[Bleed 35 turns 3]"),   // 流血缺省 3 回合(金 E8,加载时补)
            ("荆", L8, Ftr, "荆天棘地", "SummonThorns 30 turns 3"),
            ("林", L4, Both, "双木", "SummonSapling 20 count 2 fromBody"),
            ("林", L5, Atk, "众木", "Amplify 10 scope Damage per SummonCount"),
            ("林", L5, Ftr, "林风", "Summon 0 count 1 passive{Speed=150}"),   // spec A D22:底速移出本命,Lv5 起再出现
            ("林", L6, Atk, "林间", "Amplify 20 scope Seed per SummonCount"),
            ("林", L6, Ftr, "林荫", "Summon 0 count 1 passive{GroveCut=5,GroveCutCap=20}"),
            ("林", L8, Atk, "林海", "SummonStrike 70"),
            ("林", L8, Ftr, "林立", "Augment 1 of Summon.Count|Reshape 0 summonPercent 60|SummonEmpower 20 turns 3 pick AllSummons"),
            ("柘", L4, Atk, "精进", "Amplify 21 scope All"),   // spec C §2:坚心拆两行,攻面精进
            ("柘", L4, Ftr, "坚心", "Summon 0 count 1 passive{SelfCut=15}"),   // 生面本命:本字木灵受到伤害 −15%
            ("柘", L5, Atk, "柘弓", "SummonStrike 200 own byArmor"),
            ("柘", L6, Atk, "柘刺", "ShieldSummons 59"),   // spec A D5
            ("柘", L6, Ftr, "守护", "Summon 0 count 1 passive{Guard=30}"),
            ("柘", L8, Atk, "硬弓", "Reshape 0 armorIgnore 100 hostArmorStrike 400"),
            ("柘", L8, Ftr, "良弓", "Summon 0 count 1 passive{ReturnFire=200}"),
            ("桂", L4, Atk, "精进", "Amplify 25 scope All"),   // spec C §2
            ("桂", L4, Ftr, "馥郁", "Summon 0 count 1 passive{HealAllyPercent=30}"),
            ("桂", L5, Atk, "折桂", "GrantChar 1"),
            ("桂", L5, Ftr, "桂花雨", "HealAll 75"),
            ("桂", L6, Atk, "桂馥", "Amplify 50 scope Seed"),
            ("桂", L6, Ftr, "蟾宫", "Summon 0 count 1 passive{CritAura=10}"),
            ("桂", L8, Atk, "月桂", "CritBuff 30 turns 1 pick Self"),
            ("桂", L8, Ftr, "桂冠", "SummonEmpower 50 turns 3 pick StrongestSummon"),
            ("藻", L4, Atk, "精进", "Amplify 25 scope All"),   // spec C §2
            ("藻", L4, Ftr, "水草", "Summon 0 count 1 passive{HealMirror=100}"),
            ("藻", L5, Atk, "缠足", "Slow 1 pick All"),
            ("藻", L5, Ftr, "浮藻", "Summon 0 count 1 passive{Speed=150}"),   // spec A D22:丛藻删除
            ("藻", L6, Atk, "藻缠", "Slow 2"),   // spec A review 裁定:改为纯减速 2
            ("藻", L6, Ftr, "藻荇", "Summon 0 count 1 passive{SproutDeathSlow=1}"),
            ("藻", L8, Atk, "藻缚", "Freeze 1|Slow 2 pick Adjacent"),
            ("藻", L8, Ftr, "绿潮", "SummonMerge 0|Taunt 2 pick SummonedThisCast"),
            ("森", L4, Both, "三木", "HealSummons 100 pct"),
            ("森", L5, Atk, "森罗", "Reshape 0 summonAtkStrike 20"),
            ("森", L5, Ftr, "森风", "Summon 0 count 1 passive{Speed=150}"),   // spec A D22:丛林删除
            ("森", L6, Atk, "森生", "SummonSapling 20 count 1 fromBody"),   // spec A D1 改名自「森然」;击杀时
            ("森", L6, Ftr, "森严", "Summon 0 count 1 passive{ThicketCut=25,ThicketMin=3}"),
            ("森", L8, Atk, "林深", "Reshape 0 shape All shapePct 50|Amplify 10 scope Damage per SummonCount"),
            ("森", L8, Ftr, "万木回春", "Revive 0 pct 30"),
            (Ten, L4, Atk, "先声", "Amplify 45 if FirstCastThisTurn scope All"),   // spec C §2:攻面 参天之力 改名先声
            (Ten, L4, Ftr, "参天之力", "Summon 0 count 1 passive{AttackPercent=20}"),
            (Ten, L5, Atk, "四木成林", "SummonAct 0"),
            (Ten, L5, Ftr, "木疾", "Summon 0 count 1 passive{Speed=150}"),   // spec A D22:独木生林删除
            (Ten, L6, Atk, "落叶", "SummonStrike 30"),   // 与楸 Lv5「落叶」重名,分属两字(D1)
            (Ten, L6, Ftr, "落木", "Summon 0 count 1 passive{DeathBlast=300}"),
            (Ten, L8, Atk, "撼天", "Reshape 0 hostAtkStrike 200"),
            (Ten, L8, Ftr, "擎天", "SummonEmpower 30 turns 3 pick OtherSummons"),
        };

        [Test]
        public void EveryExclusiveTrait_LoadsAtItsSlotAndFace_WithTheTableEffects()
        {
            Assert.That(Table.Length, Is.EqualTo(59), "46 格,秋实 / 披荆 / 馥郁 / 水草 / 参天之力拆成两行(V4);spec A 增 Lv5 底速被动 / Lv6 攻面专属共 7 行;spec C 柘 坚心拆两行 +1");
            Assert.That(Table.Select(r => (r.Ch, r.Slot, r.Name)).Distinct().Count(), Is.EqualTo(59), "spec C 起拆行 / 改名后每一行名字互异(秋实 / 披荆 / 馥郁 / 水草 / 参天之力的攻面改名,柘 坚心拆行)");
            var errors = new List<string>();   // 汇总全部不符,免得第一条失败遮住后面
            foreach (var (ch, slot, face, name, sigs) in Table)
            {
                var t = Graph.Get(ch).Traits.SingleOrDefault(x => x.Slot == slot && x.Face == face && x.Name == name);
                if (t == null) { errors.Add($"缺 {ch} {name}({slot}/{face})"); continue; }
                (TraitForm, TraitTrigger) form = name switch
                {
                    "双木" => (TraitForm.Passive, TraitTrigger.OnDismantle),
                    "三木" => (TraitForm.Passive, TraitTrigger.OnCompose),
                    "折桂" => (TraitForm.Passive, TraitTrigger.OnKill),
                    "森生" => (TraitForm.Passive, TraitTrigger.OnKill),
                    "林风" or "森风" or "浮藻" or "木疾" => (TraitForm.Passive, TraitTrigger.Cast),   // spec A D22:Lv5 五行面被动
                    _ => (slot == L4 || slot == L6 ? TraitForm.Passive : TraitForm.Active, TraitTrigger.Cast),
                };
                if ((t.Form, t.Trigger) != form) errors.Add($"{ch} {name} 形态 {t.Form}/{t.Trigger} 应为 {form}");
                if (t.Replaces.HasValue) errors.Add($"{ch} {name} 不应替换");
                if (t.MaxPerBattle != (name == "折桂" ? 1 : 0)) errors.Add($"{ch} {name} 每场次数(E49) {t.MaxPerBattle}");
                var got = string.Join("|", t.Effects.Select(Sig));
                if (got != sigs) errors.Add($"{ch} {name}({slot}/{face})效果:期望 [{sigs}] 实际 [{got}]");
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // ================= 折叠:本命追加并进本体召唤(E37)、改形 / 加伤修饰落到本体上 =================

        private static List<EffectDef> Folded(string ch, CardFace face, int level) => TraitRules.CastEffects(Graph.Get(ch), face, level);

        private static EffectDef FoldedOf(string ch, CardFace face, int level, EffectKind kind) =>
            Folded(ch, face, level).First(e => e.Kind == kind);

        [Test]
        public void Fold_NatureFieldsMergeIntoBodySummon_AndModifiersLand()
        {
            // 本命追加:Lv8 生面恰一条召唤,本命 = Lv1 本命 + Lv3 强化 + 全部 Lv4/6/8 专属本命
            (string ch, string passive)[] natures =
            {
                ("楸", "DeathPart=True,OnHitBurn=2"),
                ("荆", "Thorns=80,ThornsBleed=35"),
                ("林", "GroveCut=5,GroveCutCap=20,PerAllyAttackPercent=25,Speed=150"),
                ("柘", "Armor=60,Guard=30,ReturnFire=200,SelfCut=15,Taunt=True"),   // spec C:坚心 SelfCut 15 并入本命
                ("桂", "CritAura=10,HealAlly=30,HealAllyPercent=30,HealAllyTimes=2"),
                ("藻", "HealMirror=100,Speed=150,SproutDeathSlow=1,SproutMax=3,SproutPercent=30"),
                ("森", "EntrySaplings=2,Speed=150,ThicketCut=25,ThicketMin=3"),
                (Ten, "AttackPercent=20,DeathBlast=300,Shape=Row,ShapePercent=50,Speed=150"),
            };
            foreach (var (ch, passive) in natures)
            {
                var summons = Folded(ch, CardFace.Feature, 8).Where(e => e.Kind == EffectKind.Summon).ToList();
                Assert.That(summons.Count, Is.EqualTo(1), ch + " 本命追加不多出一条 0 血召唤");
                Assert.That(summons[0].Value, Is.GreaterThan(0), ch);
                Assert.That(PassiveSig(summons[0].Passive), Is.EqualTo(passive), ch + " Lv8 本命");
            }
            Assert.That(PassiveSig(FoldedOf("楸", CardFace.Feature, 3, EffectKind.Summon).Passive), Is.EqualTo("OnHitBurn=2"),
                "对照:Lv3 没有秋实");
            // 攻面的本命追加没有本体召唤可并 → 空转(V4 攻面另写精进)
            Assert.That(Folded("楸", CardFace.Attack, 8).Any(e => e.Kind == EffectKind.Summon), Is.False);

            // 林立:召唤 2 只、各 60%(E48);花 Lv8:spec A D15 起移花接木本身就是 `Charm 100` `turns 1`(E45 的 Augment 写法作废)
            var lin = FoldedOf("林", CardFace.Feature, 8, EffectKind.Summon);
            Assert.That((lin.SummonCount, lin.SummonPercent), Is.EqualTo((2, 60)));
            var charm = FoldedOf("花", CardFace.Attack, 8, EffectKind.Charm);
            Assert.That((charm.Value, charm.Turns), Is.EqualTo((100, 1)));
            // 硬弓 / 撼天 / 森罗加伤落到本体伤害;林深全体 50%;万箭齐发散射 5 发 40%;秋风扫叶横扫各 60%
            var ying = FoldedOf("柘", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((ying.HostArmorStrikePercent, ying.ArmorIgnorePercent), Is.EqualTo((400, 100)));
            Assert.That(FoldedOf(Ten, CardFace.Attack, 8, EffectKind.DamageSingle).HostAtkStrikePercent, Is.EqualTo(200));
            var shen = FoldedOf("森", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((shen.SummonAtkStrikePercent, shen.Shape, shen.ShapePercent), Is.EqualTo((20, TargetArea.All, 50)));
            var jian = FoldedOf("箭", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((jian.Shape, jian.Shots, jian.ShotPercent), Is.EqualTo((TargetArea.Scatter, 5, 40)));
            var qiu = FoldedOf("楸", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((qiu.Shape, qiu.ShapePercent, qiu.HitPercent), Is.EqualTo((TargetArea.Row, 60, 60)));
            // spec A D15:花攻击面本体不再有魅惑,Lv8 之前攻面都没有 Charm(Q27 的 Lv5 寄生 +1 回合随之无物可加)
            Assert.That(Folded("花", CardFace.Attack, 5).Any(e => e.Kind == EffectKind.Charm), Is.False);
            Assert.That(Folded("花", CardFace.Attack, 4).Any(e => e.Kind == EffectKind.Charm), Is.False);
        }

        // ================= 数据任务补的规则缺口 =================

        private static RecipeGraph LoadOne(string attackEffects) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""桂"",""element"":""Wood"",
              ""effects"":[{""kind"":""Summon"",""value"":100,""count"":1,""attack"":10,""summonChar"":""桂""}],
              ""attackEffects"":[" + attackEffects + "]}]}");

        /// <summary>月桂「本回合我方暴击率 +30%」(Q23 只给玩家):攻面不选友方,写 `pick Self` 直接落玩家。
        /// CritBuff 缺省仍是选友方的单体增益(锋 / 锐),只有 Self 写法免选。</summary>
        [Test]
        public void CritBuff_PickSelf_OnAttackFace_LandsOnPlayer_WithoutAllyTarget()
        {
            var g = LoadOne(@"{""kind"":""DamageSingle"",""value"":10},{""kind"":""CritBuff"",""value"":30,""turns"":1,""pick"":""Self""}");
            var crit = g.Get("桂").AttackEffects[1];
            Assert.That(BattleEngine.EffectNeedsAllyTarget(crit), Is.False, "Self 写法不选友方");
            Assert.That(BattleEngine.EffectNeedsAllyTarget(new EffectDef(EffectKind.CritBuff, 30)), Is.True, "缺省仍选友方");

            var b = new BattleEngine(g, Config(), Enumerable.Repeat("桂", 6).ToArray(), Array.Empty<string>(),
                new[] { Dummy() }, seed: 1);
            Cast(b, "桂");   // 先召一只木灵:证明不落到木灵上
            // 显式指向 0 号木灵(Task 6 review Minor 1):攻面不再校验 allySlot,Self 写法必须无视它、落玩家(critSlot 分支)
            Assert.That(b.Cast("桂", 0, attackMode: true, allySlot: 0), Is.EqualTo(BattleError.None));
            var s = b.PlayerStatuses.Find(StatusKind.CritBuff);
            Assert.That((s?.Magnitude, s?.TurnsLeft), Is.EqualTo(((int?)30, (int?)1)), "挂玩家、1 回合");
            Assert.That(b.Summons[0].Statuses.Has(StatusKind.CritBuff), Is.False, "木灵不吃");
        }

        [Test]
        public void CritBuff_PickSelf_Allowed_OtherAllyPicksStillRejected()
        {
            Assert.That(EffectPickRules.Allows(EffectKind.CritBuff, EffectPick.Self), Is.True);
            Assert.That(EffectPickRules.Allows(EffectKind.CritBuff, EffectPick.AllSummons), Is.False);
            Assert.Throws<ConfigException>(() => LoadOne(
                @"{""kind"":""DamageSingle"",""value"":10},{""kind"":""CritBuff"",""value"":30,""turns"":1,""pick"":""AllSummons""}"));
            Assert.Throws<ConfigException>(() => LoadOne(
                @"{""kind"":""DamageSingle"",""value"":10},{""kind"":""Empower"",""value"":30,""pick"":""Self""}"), "只放行 CritBuff");
        }

        /// <summary>Q27:花没有种,池·寄生「种 +1 回合」在花上读作「魅惑 +1 回合」。
        /// `Augment of Seed field Turns` 本面找不到种时退到魅惑(魅惑的回合在 Turns 上);有种的字照旧加在种上。</summary>
        [Test]
        public void Parasite_SeedTurnsAugment_FallsBackToCharm_WhenFaceHasNoSeed()
        {
            var aug = new EffectDef(EffectKind.Augment, 1, augmentKind: EffectKind.Seed, augmentField: AugmentField.Turns);
            var parasite = new[] { new TraitDef(TraitSlot.Lv5, TraitFace.Attack, TraitForm.Active, null, "寄生", new[] { aug }) };
            var hua = new CharDef("花", Element.Wood, effects: new[] { new EffectDef(EffectKind.HealSelf, 1) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Charm, 0, turns: 2) },
                traits: parasite);
            Assert.That(TraitRules.CastEffects(hua, CardFace.Attack, 5).Single(e => e.Kind == EffectKind.Charm).Turns,
                Is.EqualTo(3), "魅惑 2 + 1");
            Assert.That(TraitRules.CastEffects(hua, CardFace.Attack, 4).Single(e => e.Kind == EffectKind.Charm).Turns,
                Is.EqualTo(2), "对照:Lv4 未解锁");

            var teng = new CharDef("藤", Element.Wood, effects: new[] { new EffectDef(EffectKind.HealSelf, 1) },
                attackEffects: new[]
                {
                    new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.Charm, 0, turns: 2),
                    new EffectDef(EffectKind.Seed, 10, turns: 3),
                },
                traits: parasite);
            var folded = TraitRules.CastEffects(teng, CardFace.Attack, 5);
            Assert.That((folded.Single(e => e.Kind == EffectKind.Seed).Turns, folded.Single(e => e.Kind == EffectKind.Charm).Turns),
                Is.EqualTo((4, 2)), "有种:加在种上,魅惑不动");
        }

        // ================= 1 守护:柘灵在场,替玩家承担 30%(减伤之后、护盾之前;不算挥击) =================

        [Test]
        public void Guard_ZheLing_TakesThirtyPercentOfPlayersHit()
        {
            // 柘灵天生嘲讽,敌人只会打它;给玩家挂嘲讽(压过一切)让这一击落到玩家身上
            var b = Battle("柘", 6, new[] { Shooter(1000) });
            Cast(b, "柘");
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Taunt, Polarity = StatusPolarity.Buff, TurnsLeft = 5, SourceId = "试" });
            var zhe = Alive(b).Single();
            int zheHp = zhe.Hp, hp = b.PlayerHp;
            b.EndTurn();
            var guard = b.LastEvents.Single(e => e.Kind == BattleEventKind.GuardTransfer);
            var hit = b.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            Assert.That(guard.Amount, Is.EqualTo(300), "1000 × 30%");
            Assert.That((hit.Amount, hp - b.PlayerHp), Is.EqualTo((700, 700)), "玩家只承受 70%");
            Assert.That(zheHp - zhe.Hp, Is.EqualTo(300), "转移不过柘灵护甲");
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.False, "不算挥击");

            var c = Battle("柘", 5, new[] { Shooter(1000) });   // 对照:Lv5 没有守护
            Cast(c, "柘");
            c.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Taunt, Polarity = StatusPolarity.Buff, TurnsLeft = 5, SourceId = "试" });
            int hp5 = c.PlayerHp;
            c.EndTurn();
            Assert.That(hp5 - c.PlayerHp, Is.EqualTo(1000));
        }

        // ================= 2 林荫:每只木灵 5%,封顶 20%(玩家与木灵都吃) =================

        [Test]
        public void GroveCut_FivePercentPerSummon_CappedAtTwenty()
        {
            var b = Battle("林", 6, new[] { Shooter(1000) });
            // spec A:Lv5 幼苗(独木生林 / 丛林 / 新芽)已删除,一次生面只召 1 只林灵 —— 靠多出几次生面凑木灵数
            Cast(b, "林");
            Assert.That(Alive(b).Count, Is.EqualTo(1), "1 只林灵");
            Assert.That(b.WoodDamageCutPercent(forSummon: false), Is.EqualTo(5));
            Cast(b, "林");
            Assert.That(Alive(b).Count, Is.EqualTo(2));
            Assert.That(b.WoodDamageCutPercent(forSummon: false), Is.EqualTo(10));
            for (int i = 0; i < 4; i++) Cast(b, "林");
            Assert.That(Alive(b).Count, Is.EqualTo(6));
            Assert.That(b.WoodDamageCutPercent(forSummon: false), Is.EqualTo(20), "6 × 5% = 30 → 封顶 20");
            Assert.That(b.WoodDamageCutPercent(forSummon: true), Is.EqualTo(20));
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(hp - b.PlayerHp, Is.EqualTo(800), "1000 × 80%");
        }

        // ================= 3 森严:森灵在场且木灵 ≥ 3 只,木灵受伤 −25%(只给木灵) =================

        [Test]
        public void ThicketCut_ThreeOrMoreSummons_SummonsOnly()
        {
            var b = Battle("森", 6, new[] { Shooter(1000) });
            Cast(b, "森");
            Assert.That(Alive(b).Count, Is.EqualTo(3), "spec A:森灵 + 入场幼苗 2(本命 EntrySaplings;丛林 / 森然 的幼苗已删除)");
            Assert.That(b.WoodDamageCutPercent(forSummon: true), Is.EqualTo(25));
            Assert.That(b.WoodDamageCutPercent(forSummon: false), Is.EqualTo(0), "玩家不吃森严");
            int hp = b.PlayerHp;
            b.EndTurn();
            Assert.That(hp - b.PlayerHp, Is.EqualTo(1000));

            // 只剩森灵 + 1 只幼苗(2 只)→ 不满足门槛
            var c = Battle("森", 6, new[] { Dummy() });
            Cast(c, "森");
            foreach (var s in Alive(c).Where(s => s.Char == "苗").Skip(1)) s.Hp = 0;
            Assert.That(Alive(c).Count, Is.EqualTo(2));
            Assert.That(c.WoodDamageCutPercent(forSummon: true), Is.EqualTo(0));
        }

        // ================= 坚心(spec C 真实数据冒烟):柘 Lv4 生面召出的木灵受到伤害 × 85% =================

        [Test]
        public void JianXin_Lv4_SummonTakesFifteenPercentLess()
        {
            int Hit(bool keepSelfCut)
            {
                var b = Battle("柘", 4, new[] { Melee(1000) });
                Cast(b, "柘");
                var zhe = Alive(b).Single();
                Assert.That(zhe.Passive.SelfCut, Is.EqualTo(15), "坚心:本命 SelfCut 15");
                if (!keepSelfCut) zhe.Passive.SelfCut = 0;
                b.EndTurn();
                return b.LastEvents.First(e => e.Kind == BattleEventKind.SummonHit).Amount;
            }
            int plain = Hit(false), cut = Hit(true);
            Assert.That(plain, Is.GreaterThan(0));
            Assert.That(cut, Is.EqualTo(plain * 85 / 100).Within(1), "同一击:有坚心比没有少 15%");
        }

        // ================= 4 良弓:柘灵挨打射还 护甲 × 200%,进 60% 反伤预算(最后一档) =================

        [TestCase(1000, false)]
        [TestCase(150, true)]
        public void ReturnFire_ArmorTimesTwo_CappedByReflectBudget(int attack, bool capped)
        {
            var b = Battle("柘", 8, new[] { Melee(attack) });
            Cast(b, "柘");
            var zhe = Alive(b).Single();
            int armor = b.SummonDefense(zhe);
            b.EndTurn();
            var hit = b.LastEvents.First(e => e.Kind == BattleEventKind.SummonHit);
            int fire = Dealt(b, EffectSource.ReturnFire);
            // 钉值:柘灵护甲 86(柘 Lv8 坚木 60 按卡等级);spec C 坚心 SelfCut 15%:受击再 × 85%。
            // 1000 过护甲 537 → 456,良弓 172 ≤ 预算 273;150 → 80 → 68,预算 40
            Assert.That((armor, hit.Amount, fire), Is.EqualTo(capped ? (86, 68, 40) : (86, 456, 172)), "钉值");
            Assert.That(fire, Is.EqualTo(capped ? hit.Amount * 60 / 100 : armor * 2),
                capped ? "预算 = 这一击 × 60%" : "护甲 × 200%");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.ReturnFire),
                Is.EqualTo(1), "每次受击一箭");
        }

        // ================= 5 荆刺:标记的敌人打我方时吃 30% 反伤,与镜共用 60% 预算 =================

        [TestCase(0, 300)]
        [TestCase(20, 500)]
        [TestCase(50, 600)]
        public void ThornMark_ThirtyPercentReflect_SharesBudgetWithMirror(int mirror, int expected)
        {
            var b = Battle("荆", 5, new[] { Shooter(1000) });
            Cast(b, "荆", 0, attack: true);
            var mark = b.Enemies[0].Statuses.Find(StatusKind.ThornMark);
            Assert.That((mark?.Magnitude, mark?.TurnsLeft), Is.EqualTo(((int?)30, (int?)2)));
            if (mirror > 0)
                b.PlayerStatuses.Apply(new StatusEffect
                    { Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff, Magnitude = mirror, TurnsLeft = 3, SourceId = "镜" });
            b.EndTurn();
            Assert.That(Dealt(b, EffectSource.Reflect, 0), Is.EqualTo(expected));
        }

        // ================= 6 攀附:缠住生命最高的敌人,它每次行动开头挨藤灵一下 =================

        [Test]
        public void Bind_HighestHpEnemy_StruckAtEachOfItsActions()
        {
            var b = Battle("藤", 8, new[] { Dummy(500000), Dummy(), Dummy(800000) });
            var before = b.Capture();
            Cast(b, "藤");
            var after = b.Capture();
            Assert.That((after.TraitRandomState, after.TargetRandomState), Is.EqualTo((before.TraitRandomState, before.TargetRandomState)),
                "HighestHp 不摇号(Q13)");
            var vine = Alive(b).Single();
            int slot = Array.IndexOf(b.Summons.ToArray(), vine);
            Assert.That(b.Enemies[1].Statuses.Find(StatusKind.Bind)?.TargetSlot, Is.EqualTo(slot), "缠住生命最高的那只");
            Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Bind) || b.Enemies[2].Statuses.Has(StatusKind.Bind), Is.False);
            b.EndTurn();
            // 按 ActorActed 切段:被缠敌人自己那一拍开头挨藤灵一下;藤灵自己那一拍另算
            var strikesInBoundBeat = new List<BattleEvent>();
            int boundBeats = 0;
            bool inBound = false;
            foreach (var e in b.LastEvents)
            {
                if (e.Kind == BattleEventKind.ActorActed)
                {
                    inBound = e.Amount == (int)ActorKind.Enemy && e.TargetIndex == 1;
                    if (inBound) boundBeats++;
                    continue;
                }
                if (inBound && e.Kind == BattleEventKind.SummonAttack && e.SecondIndex == slot) strikesInBoundBeat.Add(e);
            }
            Assert.That(boundBeats, Is.GreaterThanOrEqualTo(1), "前提:被缠的敌人行动过");
            Assert.That(strikesInBoundBeat.Count, Is.EqualTo(boundBeats), "它每次行动开头挨一下");
            Assert.That(strikesInBoundBeat.All(e => e.TargetIndex == 1 && e.Amount == vine.EffectiveAttack), Is.True,
                "打击 = 藤灵有效攻击 × 100%");
            Assert.That(vine.EffectiveAttack, Is.EqualTo(36), "钉值:spec A 藤 Lv8 生面攻击(绿档 22,含通·精进 +12%,E38)");
        }

        // ================= 7 绿潮:小藻并入藻灵(属性相加),不触发阵亡;藻灵嘲讽 2 =================

        [Test]
        public void GreenTide_SproutsMergeIntoAlgaeLing_NoDeath_Taunt()
        {
            // spec A:丛藻(Lv5 一次分出 2 只小藻)已删除,小藻只剩本命「丛生」(藻灵每拍分裂 1 只,SproutMax 2)这一个来源。
            // 先出一只藻灵、过两拍让它分出小藻(Lv3 本命强化 SproutMax 3,两拍内已满 3 只),再出第二只藻灵 —— 绿潮把场上全部小藻并入这只新召出的藻灵
            var log = new HookLog();
            var b = Battle("藻", 8, new[] { Dummy() });
            b.AddHookListener(log);
            Cast(b, "藻");
            var first = Alive(b).Single();
            b.EndTurn();
            b.EndTurn();
            var sprouts = Alive(b).Where(s => s.Char == "苗").ToList();
            Assert.That(sprouts.Count, Is.EqualTo(3), "前提:丛生分出 3 只小藻(SproutMax 3)");
            int sproutAttack = sprouts.Sum(s => s.Attack), sproutHp = sprouts.Sum(s => s.Hp);
            int firstAttack = first.Attack;
            Cast(b, "藻");
            var merged = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonMerged).ToList();
            Assert.That(merged.Count, Is.EqualTo(3), "3 只小藻当场并入");
            var alive = Alive(b);
            Assert.That(alive.Count, Is.EqualTo(2), "两只藻灵;小藻槽位置空");
            Assert.That(alive.All(s => s.Char != "苗"), Is.True);
            int born = b.LastEvents.First(e => e.Kind == BattleEventKind.Summon).Amount;
            var zao = alive.Single(s => s.MaxHp > first.MaxHp);
            Assert.That(zao.MaxHp, Is.EqualTo(born + merged.Sum(e => e.Amount)), "血量相加");
            Assert.That(merged.Sum(e => e.Amount), Is.EqualTo(sproutHp), "并入的是小藻当前生命");
            Assert.That(zao.Hp, Is.EqualTo(zao.MaxHp), "当前生命也相加(Q18;三只都满血 → 合计满血)");
            Assert.That(zao.Attack, Is.EqualTo(firstAttack + sproutAttack), "攻击相加(Q18):藻灵 + 小藻各 30%");
            Assert.That(zao.Statuses.Find(StatusKind.Taunt)?.TurnsLeft, Is.EqualTo(2), "嘲讽 2");
            Assert.That(log.All.Any(h => h.Kind == HookKind.SummonDied), Is.False, "并入不算阵亡");
        }

        // ================= 8 落木:巨木灵阵亡 → 全体敌人吃 有效攻击 × 300%(不暴击) =================

        [Test]
        public void DeathBlast_GiantDies_AllEnemiesTakeThreeTimesAttack()
        {
            var b = Battle(Ten, 6, new[] { Melee(10000000), Dummy(), Dummy() });
            Cast(b, Ten);
            var giant = Alive(b).Single(s => s.Char != "苗");
            int atk = giant.EffectiveAttack;
            foreach (var s in Alive(b).Where(s => s.Char == "苗")) s.Hp = 0;   // 只留巨木灵挨打
            b.EndTurn();
            Assert.That(giant.Alive, Is.False, "前提:巨木灵阵亡");
            var blasts = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.DeathBlast).ToList();
            Assert.That(blasts.Select(e => e.TargetIndex).OrderBy(i => i).ToList(), Is.EqualTo(new[] { 0, 1, 2 }), "全体");
            Assert.That(blasts.All(e => e.Amount == atk * 3 && !e.Crit), Is.True, $"每只 {atk} × 300%");
            Assert.That(atk, Is.EqualTo(230), "钉值:spec A 巨木灵 Lv6 有效攻击(含参天之力 +20%)→ 每只 690");
        }

        // ================= 9 秋实:楸灵阵亡,部件池 +木 =================

        [Test]
        public void DeathPart_QiuLingDies_PoolGainsWood()
        {
            var b = Battle("楸", 4, new[] { Melee(10000000) });
            Cast(b, "楸");
            Assert.That(b.Pool, Is.Empty);
            b.EndTurn();
            Assert.That(Alive(b), Is.Empty, "前提:楸灵阵亡");
            Assert.That(b.Pool, Is.EqualTo(new[] { "木" }));
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.PartGranted), Is.EqualTo(1));

            var c = Battle("楸", 3, new[] { Melee(10000000) });   // 对照:Lv3 没有秋实
            Cast(c, "楸");
            c.EndTurn();
            Assert.That(c.Pool, Is.Empty);
        }

        // ================= 10 折桂:击杀时字库 +1 张已拥有字里的最高档,每场 1 次 =================

        [Test]
        public void ZheGui_KillGrantsTopTierChar_OncePerBattle()
        {
            var b = Battle("桂", 5, new[] { Dummy(1), Dummy(1), Dummy() }, copies: 3,
                config: Config(unlocked: new[] { "花", "藤", "森" }));
            int lib = b.Library.Count;
            Cast(b, "桂", 0, attack: true);
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:击杀");
            var granted = b.LastEvents.Where(e => e.Kind == BattleEventKind.CharGranted).ToList();
            Assert.That(granted.Count, Is.EqualTo(1));
            Assert.That(b.Library[granted[0].Amount], Is.EqualTo("森"), "候选 = 已拥有(白 花 / 绿 藤 / 橙 森)里的最高档");
            Assert.That(b.Library.Count, Is.EqualTo(lib - 1 + 1), "出掉 1 张、入 1 张");
            Cast(b, "桂", 1, attack: true);
            Assert.That(b.Enemies[1].Alive, Is.False);
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.CharGranted), Is.False, "每场 1 次");
        }

        [Test]
        public void ZheGui_FinalBlowOnSingleEnemy_StillGrants()
        {
            // 终审探针 P2:单敌战的最后一击(当场判胜)也入库 —— 字库跨场
            var b = Battle("桂", 5, new[] { Dummy(10) }, copies: 4, config: Config(unlocked: new[] { "花", "藤", "森" }));
            int lib = b.Library.Count;
            Cast(b, "桂", 0, attack: true);
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Won), "前提:清场");
            Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.CharGranted), Is.EqualTo(1));
            Assert.That(b.Library.Count, Is.EqualTo(lib), "出掉 1 张、入 1 张(净 +1 相对修复前)");
            Assert.That(b.Library.Contains("森"), Is.True);
        }

        // ================= 11 林立:本体 1 只 100% → 2 只各 60%;全部木灵 3 回合攻击 +20% =================

        [Test]
        public void LinLi_TwoLinLingsAtSixtyPercent_PlusAttackAura()
        {
            var b = Battle("林", 8, new[] { Dummy() });
            Cast(b, "林");
            var lings = Alive(b).Where(s => s.Char != "苗").ToList();
            Assert.That(lings.Count, Is.EqualTo(2), "2 只林灵");
            Assert.That(lings[0].MaxHp == lings[1].MaxHp && lings[0].Attack == lings[1].Attack, Is.True);
            var aura = b.PlayerStatuses.All.Single(s => s.Kind == StatusKind.SummonEmpower);
            Assert.That((aura.Magnitude, aura.TurnsLeft, aura.TargetSlot), Is.EqualTo((20, 3, -1)));
            // 钉值(spec A):林 Lv8 本体血 / 攻(293 / 68 按 Lv8)各 × 60% → 250 / 58;
            // 有效攻击 = 58 × 光环 120% + 成林 58 × 25% × 其他 1 只 = 83(新芽幼苗已删除,场上只有 2 只林灵)
            Assert.That((lings[0].MaxHp, lings[0].Attack, lings[0].EffectiveAttack, Alive(b).Count), Is.EqualTo((250, 58, 83, 2)),
                "钉值(场上只有这 2 只林灵)");
        }

        // ================= 12 擎天:出字时已在场的木灵各挂攻击 +30%,本次召出的不吃 =================

        [Test]
        public void QingTian_EmpowersOnlyPreExistingSummons()
        {
            var b = Battle(Ten, 8, new[] { Dummy() });
            Cast(b, Ten);
            var first = Enumerable.Range(0, 6).Where(i => b.Summons[i] != null && b.Summons[i].Alive).ToList();
            Assert.That(first.Count, Is.EqualTo(1), "spec A:只有 1 只巨木灵(独木生林已删除)");
            Assert.That(b.PlayerStatuses.Has(StatusKind.SummonEmpower), Is.False, "第一次出字场上没有别的木灵");
            Cast(b, Ten);
            var auras = b.PlayerStatuses.All.Where(s => s.Kind == StatusKind.SummonEmpower).ToList();
            Assert.That(auras.Select(s => s.TargetSlot).OrderBy(i => i).ToList(), Is.EqualTo(first), "只挂第一次的那只");
            Assert.That(auras.All(s => s.Magnitude == 30 && s.TurnsLeft == 3), Is.True);
            var second = Enumerable.Range(0, 6).Where(i => b.Summons[i] != null && b.Summons[i].Alive).Except(first).ToList();
            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(second.All(i => b.SummonAurasAt(i).Count == 0), Is.True, "新召出的不吃");
        }

        // ================= 13 万木回春:复活本场阵亡的木灵,30% 生命(本体召唤先落位) =================

        [Test]
        public void WanMuHuiChun_RevivesRemainingCorpsesAtThirtyPercent()
        {
            SummonSnapshot Corpse(int slot) => new()
            {
                Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 1000, MaxHp = 1000, Attack = 10,
                Speed = 100, SproutParentSlot = -1, Statuses = new List<StatusEffect>(),
            };
            BattleEngine Run(int level)
            {
                var b = Battle("森", level, new[] { Dummy() },
                    summons: Enumerable.Range(0, BattleEngine.MaxSummonSlots).Select(Corpse).ToList());
                Assert.That(b.Summons.Count(s => s != null), Is.EqualTo(BattleEngine.MaxSummonSlots), "前提:8 格全占");
                foreach (var s in b.Summons) s.Hp = 0;
                Cast(b, "森");
                return b;
            }
            // 8 具尸体:spec A 森灵 + 入场幼苗 2 先占 3 格(Q28 书写顺序),万木回春救剩下 5 具
            var revived = Alive(Run(8)).Where(s => s.Char == "木").ToList();
            Assert.That(revived.Count, Is.EqualTo(5));
            Assert.That(revived.All(s => s.Hp == 300), Is.True, "⌈1000 × 30%⌉");
            Assert.That(Alive(Run(7)).Any(s => s.Char == "木"), Is.False, "对照:Lv7 没有万木回春");
        }

        // ================= 14 双木:拆林时召唤 2 只幼苗(属性 = 林生面本体 × 20%) =================

        [Test]
        public void ShuangMu_DismantleLin_TwoSaplingsFromBody()
        {
            var b = Battle("林", 4, new[] { Dummy() }, copies: 2);
            Assert.That(b.Dismantle("林"), Is.EqualTo(BattleError.None));
            var saplings = Alive(b);
            Assert.That(saplings.Count, Is.EqualTo(2));
            Assert.That(saplings.All(s => s.Char == "苗" && s.SourceChar == "林"), Is.True);
            Assert.That(saplings.Select(s => (s.MaxHp, s.Attack)).Distinct().Single(), Is.EqualTo((69, 16)),
                "钉值:spec A 林 Lv4 生面本体(293 / 68 按 Lv4)× 20%");

            var c = Battle("林", 3, new[] { Dummy() }, copies: 2);   // 对照:Lv3 没有双木
            Assert.That(c.Dismantle("林"), Is.EqualTo(BattleError.None));
            Assert.That(Alive(c), Is.Empty);
        }

        // ================= 15 E38:生面 Lv4 精进放大召唤血 / 攻 =================

        [Test]
        public void RefineOnFeatureFace_AmplifiesSummonHpAndAttack()
        {
            var teng = Graph.Get("藤");
            var bare = new CharDef(teng.Id, teng.Element, teng.Recipe, teng.Effects, teng.Rarity, teng.Pinyin, teng.Gloss,
                teng.AttackEffects, teng.IsComponent, teng.Traits.Where(t => t.Slot != TraitSlot.Lv4).ToList());
            SummonState Summoned(RecipeGraph g)
            {
                var b = Battle("藤", 4, new[] { Dummy() }, graph: g);
                Cast(b, "藤");
                return Alive(b).Single();
            }
            var with = Summoned(Graph);
            var without = Summoned(RebalanceFixture.Graph(bare));
            Assert.That((without.MaxHp, without.Attack), Is.EqualTo((110, 26)), "钉值:spec A 藤 Lv4 生面本体(93 / 22,绿档)");
            Assert.That((with.MaxHp, with.Attack), Is.EqualTo((124, 30)), "× 112%(通·精进 12,E38 作用于召唤血与攻;110 / 26 取整后)");
        }

        // ================= spec A:Lv6 新专属冒烟(真实 graph + cardLevels 6)=================

        [Test]
        public void LuoYe_Lv6_AttackFace_EachSummonStrikesAtThirtyPercent()
        {
            // 𣛧 Lv6·攻 落叶:`SummonStrike 30` —— 攻击面出字时每只木灵各追击 30%(与楸 Lv5「落叶」重名,分属两字)。
            // Lv5 的四木成林(SummonAct)已让木灵出手 1 次(100%),Lv6 在它之上再多 1 次 30% 的追击
            (List<int> Amounts, int GiantAttack) Strikes(int level)
            {
                var b = Battle(Ten, level, new[] { Dummy() });
                Cast(b, Ten);
                var giant = Alive(b).Single();
                Cast(b, Ten, 0, attack: true);
                return (b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonAttack).Select(e => e.Amount).ToList(), giant.EffectiveAttack);
            }
            var (lv5, _) = Strikes(5);
            var (lv6, atk) = Strikes(6);
            Assert.That(lv6.Count, Is.EqualTo(lv5.Count + 1), "Lv6 比 Lv5 多 1 次木灵追击");
            Assert.That(lv6.Any(a => Math.Abs(a - atk * 30 / 100) <= 2), Is.True,
                $"其中一击 ≈ 有效攻击 {atk} × 30%;实际 {string.Join(",", lv6)}");
        }

        [Test]
        public void SenSheng_Lv6_AttackKill_SummonsOneSapling()
        {
            // 森 Lv6·攻 森生(D1 改名自「森然」):击杀时 `SummonSapling 20` `fromBody` —— 出 1 只小木灵
            int Saplings(int level, int victimHp)
            {
                var b = Battle("森", level, new[] { Dummy(victimHp), Dummy() });
                Cast(b, "森", 0, attack: true);
                return Alive(b).Count(s => s.Char == "苗");
            }
            Assert.That(Saplings(6, 1), Is.EqualTo(1), "击杀:出 1 只小木灵");
            Assert.That(Saplings(6, Hp), Is.EqualTo(0), "对照:没击杀不出");
            Assert.That(Saplings(5, 1), Is.EqualTo(0), "对照:Lv5 没有森生");
        }
    }
}
