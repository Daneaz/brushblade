using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 6:金系 39 格专属特性的数据落表 —— 读真实 chars.json。
    /// 先用一张表钉死「每条特性能加载、落在正确的 (槽, 面)、效果种类对得上特性表那一行」,
    /// 再按机制类别各取至少一条做集成测试(剁截 3 击预算、立威斩杀 + 铁则、割喉致命、双金合璧开局登记、
    /// 聚金溢出、大卸八块 × 断金)。机制本身的单元级测试在 Metal* 系列;这里只证明「那一行写出来的配置在真字上接上了」。
    ///
    /// 夹具:敌人一律心属性(生克 1.0×)、0 甲;玩家攻击 100、暴击率 0。期望值尽量写成前后差,数值归 Plan F 定标。</summary>
    public class MetalExclusiveDataTests
    {
        private const string Stack4 = "";   // 𨰻 在 chars.json 里的 PUA 代理码位(export_chars.py:32)

        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(string id, int level, params EnemyDef[] enemies) =>
            new(Graph, Config, new[] { id, id, id }, Array.Empty<string>(), enemies, seed: 1,
                cardLevels: new Dictionary<string, int> { [id] = level });

        private static EnemyDef Mob(int hp = 100000, int attack = 0) => RebalanceFixture.Mob(hp: hp, attack: attack);

        private static int Morale(BattleEngine b) => b.PlayerStatuses.TotalMagnitude(StatusKind.Morale);

        private static void SetMorale(BattleEngine b, int stacks)
        {
            var m = b.PlayerStatuses.Find(StatusKind.Morale);
            if (m != null) { m.Magnitude = stacks; return; }
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.Morale, Polarity = StatusPolarity.Buff, Magnitude = stacks, TurnsLeft = -1 });
        }

        private static List<BattleEvent> Hits(BattleEngine b, int i) =>
            b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == i).ToList();

        private static int CounterEvents(BattleEngine b) =>
            b.LastEvents.Count(e => e.Kind == BattleEventKind.Damage && e.Source == EffectSource.BlockCounter);

        // ================= 表驱动:39 格 41 行 =================

        // (字, 槽, 面, 名, 效果种类 —— 不计顺序)。数值不在这里钉,归 Plan F。
        private static readonly (string Ch, TraitSlot Slot, TraitFace Face, string Name, string Kinds)[] Table =
        {
            ("利", TraitSlot.Lv8, TraitFace.Feature, "得利", "Morale,BlockMod"),
            ("锋", TraitSlot.Lv8, TraitFace.Attack, "锋锐", "Reshape"),
            ("剑", TraitSlot.Lv5, TraitFace.Feature, "剑意", "Morale,BlockMod"),
            ("剑", TraitSlot.Lv8, TraitFace.Attack, "横扫千军", "Morale,Reshape"),
            ("锥", TraitSlot.Lv5, TraitFace.Feature, "锥立", "Augment,BlockMod"),
            ("锥", TraitSlot.Lv8, TraitFace.Attack, "锥心", "Reshape"),
            ("剿", TraitSlot.Lv5, TraitFace.Feature, "坚营", "Augment,BlockMod"),
            ("剿", TraitSlot.Lv8, TraitFace.Attack, "直捣巢穴", "Reshape"),
            ("剁", TraitSlot.Lv4, TraitFace.Attack, "剁骨", "Reshape"),
            ("剁", TraitSlot.Lv4, TraitFace.Feature, "剁骨", "Amplify"),
            ("剁", TraitSlot.Lv5, TraitFace.Feature, "剁截", "BlockMod"),
            ("剁", TraitSlot.Lv8, TraitFace.Attack, "大卸八块", "Reshape"),
            ("剁", TraitSlot.Lv8, TraitFace.Feature, "刀山", "Augment,BlockMod"),
            ("铡", TraitSlot.Lv4, TraitFace.Both, "铁则", "Morale"),
            ("铡", TraitSlot.Lv5, TraitFace.Feature, "立威", "BlockMod"),
            ("铡", TraitSlot.Lv8, TraitFace.Attack, "铡刀落", "Reshape"),
            ("铡", TraitSlot.Lv8, TraitFace.Feature, "铡关", "DamageCut,Retaliate"),
            ("鍂", TraitSlot.Lv4, TraitFace.Both, "双金", "Morale"),
            ("鍂", TraitSlot.Lv5, TraitFace.Attack, "金石", "Reshape"),
            ("鍂", TraitSlot.Lv6, TraitFace.Feature, "金气", "MoraleShield"),
            ("鍂", TraitSlot.Lv8, TraitFace.Attack, "双锋", "Reshape"),
            ("鍂", TraitSlot.Lv8, TraitFace.Feature, "双金合璧", "Morale,BlockMod"),
            ("刲", TraitSlot.Lv4, TraitFace.Attack, "放血", "Bleed"),
            ("刲", TraitSlot.Lv4, TraitFace.Feature, "放血", "Amplify"),
            ("刲", TraitSlot.Lv5, TraitFace.Attack, "刲刺", "Amplify"),
            ("刲", TraitSlot.Lv5, TraitFace.Feature, "匿锋", "Morale,BlockMod"),
            ("刲", TraitSlot.Lv6, TraitFace.Attack, "割取", "HealSelf"),
            ("刲", TraitSlot.Lv8, TraitFace.Attack, "割喉", "Doom"),
            ("刲", TraitSlot.Lv8, TraitFace.Feature, "刀光", "Augment,Amplify"),
            ("鑫", TraitSlot.Lv4, TraitFace.Both, "聚金", "MoraleOverflowShield"),
            ("鑫", TraitSlot.Lv5, TraitFace.Attack, "三才", "Reshape"),
            ("鑫", TraitSlot.Lv5, TraitFace.Feature, "金身", "Augment,DefenseBuff"),
            ("鑫", TraitSlot.Lv6, TraitFace.Feature, "富甲", "MoraleArmor"),
            ("鑫", TraitSlot.Lv8, TraitFace.Attack, "三金破", "Reshape,Amplify"),
            ("鑫", TraitSlot.Lv8, TraitFace.Feature, "金玉满堂", "Morale,Augment,Morale"),
            (Stack4, TraitSlot.Lv4, TraitFace.Both, "刚", "Amplify"),
            (Stack4, TraitSlot.Lv5, TraitFace.Attack, "四金", "Reshape"),
            (Stack4, TraitSlot.Lv5, TraitFace.Feature, "千锤", "Morale,BlockMod"),
            (Stack4, TraitSlot.Lv6, TraitFace.Attack, "破军", "Amplify"),
            (Stack4, TraitSlot.Lv8, TraitFace.Attack, "千钧", "ArmorBreak,Reshape"),
            (Stack4, TraitSlot.Lv8, TraitFace.Feature, "金刚", "Augment,Morale,BlockMod"),
        };

        [Test]
        public void EveryExclusiveTrait_LoadsAtItsSlotAndFace_WithTheTableEffects()
        {
            Assert.That(Table.Length, Is.EqualTo(41), "39 格,放血 / 剁骨拆成攻击面 + 铠面两行(Q20)");
            foreach (var (ch, slot, face, name, kinds) in Table)
            {
                var def = Graph.Get(ch);
                var t = def.Traits.SingleOrDefault(x => x.Slot == slot && x.Face == face && x.Name == name);
                Assert.That(t, Is.Not.Null, $"{name}({slot}/{face})应在 {name} 的字上");
                var want = kinds.Split(',').OrderBy(k => k, StringComparer.Ordinal).ToArray();
                var got = t.Effects.Select(e => e.Kind.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToArray();
                Assert.That(got, Is.EqualTo(want), $"{name}({slot}/{face})效果种类");
            }
        }

        // ================= 剁截:反击 3 击,共用 60% 预算 =================

        [Test]
        public void DuoJie_ThreeCounterHits_ShareBudget()
        {
            // 剁 Lv5·铠 剁截:`BlockMod` `counterHits 3` `counter 40`
            var rich = Battle("剁", 5, Mob(attack: 100000), Mob());
            Assert.That(rich.Cast("剁", -1, attackMode: false), Is.EqualTo(BattleError.None));
            rich.EndTurn();
            Assert.That(CounterEvents(rich), Is.EqualTo(3), "预算充足:3 击");

            // 攻击 10 → 预算 ≤ 10 × 60% = 6:三击打不满,总量不得超预算(Q1:格挡减伤后为基数,只会更少)
            var poor = Battle("剁", 5, Mob(attack: 10), Mob());
            Assert.That(poor.Cast("剁", -1, attackMode: false), Is.EqualTo(BattleError.None));
            poor.EndTurn();
            int lost = poor.Enemies[0].MaxHp - poor.Enemies[0].Hp;
            Assert.That(lost, Is.LessThanOrEqualTo(6), "反伤预算 ≤ 敌人攻击的 60%");
            Assert.That(CounterEvents(poor), Is.LessThan(3), "预算撑不到 3 击");
        }

        // ================= 立威斩杀 + 铁则 =================

        [Test]
        public void LiWei_ExecutesBelowThreshold_AndIronRuleGivesMorale()
        {
            // 铡 Lv5·铠 立威(`counterExecuteBelow 20`)+ Lv4 铁则(斩杀时战意 +2)
            var control = Battle("铡", 5, Mob(hp: 1000, attack: 1), Mob());
            control.Cast("铡", -1, attackMode: false);
            control.EndTurn();
            Assert.That(control.Enemies[0].Alive, Is.True, "对照:满血不被斩杀");

            var b = Battle("铡", 5, Mob(hp: 1000, attack: 1), Mob());
            b.Enemies[0].Hp = 150;   // 15% < 20%
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "格挡反击命中 20% 以下的敌人 → 直接斩杀");
            Assert.That(Morale(b) - Morale(control), Is.EqualTo(2), "铁则:立威斩杀也算本字斩杀,战意 +2");
        }

        // ================= 割喉:致命 =================

        [Test]
        public void GeHou_Doom_ExecutesWhenHpFallsBelow30Percent()
        {
            // 刲 Lv8·攻 割喉:`Doom 2`。先量出这一下本体伤害 d
            var probe = Battle("刲", 8, Mob(), Mob());
            probe.Cast("刲", 0, attackMode: true);
            int d = probe.Enemies[0].MaxHp - probe.Enemies[0].Hp;
            Assert.That(d, Is.GreaterThan(0));
            var doom = probe.Enemies[0].Statuses.Find(StatusKind.Doom);
            Assert.That(doom, Is.Not.Null, "割喉给目标挂致命");
            Assert.That(doom.TurnsLeft, Is.EqualTo(2));
            Assert.That(probe.Enemies[0].Alive, Is.True, "满血不会被致命斩杀");

            // 起手 30% + d/2:本体这一下把它压到 30% 以下,单靠伤害死不了,致命补上最后一刀
            var b = Battle("刲", 8, Mob(), Mob());
            b.Enemies[0].Hp = b.Enemies[0].MaxHp * 30 / 100 + d / 2;
            b.Cast("刲", 0, attackMode: true);
            Assert.That(b.Enemies[0].Alive, Is.False, "致命:生命低于 30% 立即斩杀");
        }

        // ================= 双金合璧:开局登记 =================

        [Test]
        public void ShuangJinHeBi_RegistersOpeningMorale_NextBattleStartsWithTwo()
        {
            // 鍂 Lv8·铠:`Morale 2` `battles 1` —— 本场只登记,下一场开局战意 +2
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 3).Select(_ => new[] { Mob(hp: 50) }).ToArray(),
                RewardPool = new[] { "鍂" },
            };
            var run = new RunEngine(Graph, config, Config, new[] { "鍂", "鍂", "鍂", "鍂", "鍂" }, Array.Empty<string>(),
                seed: 1, cardLevels: new Dictionary<string, int> { ["鍂"] = 8 });
            Assert.That(run.Battle.Cast("鍂", 0), Is.EqualTo(BattleError.None));
            var pending = run.Battle.PendingOpenings.Single();
            Assert.That((pending.Kind, pending.Value, pending.BattlesLeft), Is.EqualTo((EffectKind.Morale, 2, 1)));

            Assert.That(run.Battle.Cast("鍂", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(Morale(run.Battle), Is.EqualTo(2), "第 2 场开局战意 +2");
        }

        // ================= 聚金:溢出转盾 =================

        [Test]
        public void JuJin_OverflowMorale_BecomesShield()
        {
            // 鑫 Lv4·两面 聚金:`MoraleOverflowShield 30`。战意 3 + 铠面本体 4 层 → 溢出,每溢出 1 层给 30
            var b = Battle("鑫", 4, Mob());
            SetMorale(b, 3);
            Assert.That(b.Cast("鑫", -1, attackMode: false), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(Config.MoraleCap));
            Assert.That(b.PlayerShield, Is.GreaterThan(0), "溢出的战意层数 × 每层护盾(30 × 卡等级系数)");

            var none = Battle("鑫", 4, Mob());
            Assert.That(none.Cast("鑫", -1, attackMode: false), Is.EqualTo(BattleError.None));
            Assert.That(none.PlayerShield, Is.EqualTo(0), "对照:从 0 起不溢出");
        }

        // ================= 大卸八块 × 断金 =================

        [Test]
        public void DaXieBaKuai_HitsGrowWithMorale_AndBreakGoldMultipliesEachHit()
        {
            // 剁 Lv8·攻:击数 = 战意层数 + 1(每击 35%);断金(金脉 perk,本张 +300%,结算后清空战意)与击数相乘
            BattleEngine Make(int release) => new(Graph, new BattleConfig
                {
                    PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20,
                    BossPhaseJitterPercent = 0, MoraleReleasePercent = release,
                }, new[] { "剁", "剁", "剁" }, Array.Empty<string>(), new[] { Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["剁"] = 8 });

            var low = Make(0);
            Assert.That(low.Cast("剁", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var lowHits = Hits(low, 0);

            var full = Make(0);
            SetMorale(full, Config.MoraleCap);
            Assert.That(full.Cast("剁", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var fullHits = Hits(full, 0);
            Assert.That(fullHits.Count - lowHits.Count, Is.EqualTo(Config.MoraleCap),
                "满战意比空战意多出战意层数那么多击");

            var broken = Make(300);
            SetMorale(broken, Config.MoraleCap);
            Assert.That(broken.Cast("剁", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var brokenHits = Hits(broken, 0);
            Assert.That(brokenHits.Count, Is.EqualTo(fullHits.Count), "断金不改击数");
            Assert.That(brokenHits[0].Amount, Is.EqualTo(fullHits[0].Amount * 4).Within(4), "断金 +300%:每击 ×4");
            Assert.That(Morale(full), Is.EqualTo(Config.MoraleCap).Or.GreaterThan(0), "对照:没点断金不清战意");
            Assert.That(Morale(broken), Is.LessThan(Morale(full)), "断金结算后战意被清空");
        }
    }
}
