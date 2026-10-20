using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-金 Task 6:金系 39 格专属特性的数据落表 —— 读真实 chars.json。
    /// 先用一张表钉死「每条特性能加载、落在正确的 (槽, 面)、效果种类对得上特性表那一行」,
    /// 再按机制类别各取至少一条做集成测试(剁截下架后的单击反击、扫锋同排横扫、立威斩杀 + 铁则、割喉致命、双金合璧开局登记、
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
            ("剑", TraitSlot.Lv8, TraitFace.Feature, "扫锋", "BlockMod"),   // spec C:反戈在剑上改测扫锋(counterShape Row)
            ("锥", TraitSlot.Lv5, TraitFace.Feature, "锥立", "Augment,BlockMod"),
            ("锥", TraitSlot.Lv8, TraitFace.Attack, "锥心", "Reshape"),
            ("剿", TraitSlot.Lv5, TraitFace.Feature, "坚营", "Augment,BlockMod"),
            ("剿", TraitSlot.Lv8, TraitFace.Attack, "直捣巢穴", "Reshape"),
            ("剁", TraitSlot.Lv4, TraitFace.Attack, "剁骨", "Reshape"),
            ("剁", TraitSlot.Lv4, TraitFace.Feature, "精进", "Amplify"),   // spec C:铠面 剁骨 改名精进(同档数值)
            ("剁", TraitSlot.Lv5, TraitFace.Feature, "点金", "Attune"),   // spec C:剁截下架,Lv5 铠换池·点金
            ("剁", TraitSlot.Lv8, TraitFace.Attack, "大卸八块", "Reshape"),
            ("剁", TraitSlot.Lv8, TraitFace.Feature, "刀山", "Augment,BlockMod"),
            ("铡", TraitSlot.Lv4, TraitFace.Both, "铁则", "Morale"),
            ("铡", TraitSlot.Lv5, TraitFace.Feature, "立威", "BlockMod"),
            ("铡", TraitSlot.Lv8, TraitFace.Attack, "铡刀落", "Reshape"),
            ("铡", TraitSlot.Lv8, TraitFace.Feature, "铡关", "DamageCut,Retaliate"),
            ("鍂", TraitSlot.Lv4, TraitFace.Both, "双金", "Morale"),
            ("鍂", TraitSlot.Lv5, TraitFace.Attack, "金石", "Reshape"),
            ("鍂", TraitSlot.Lv6, TraitFace.Attack, "金鸣", "Amplify"),   // spec A D9:每层战意 +5%
            ("鍂", TraitSlot.Lv6, TraitFace.Feature, "金气", "MoraleShield"),
            ("鍂", TraitSlot.Lv8, TraitFace.Attack, "双锋", "Reshape"),
            ("鍂", TraitSlot.Lv8, TraitFace.Feature, "双金合璧", "Morale,BlockMod"),
            ("刲", TraitSlot.Lv4, TraitFace.Attack, "放血", "Bleed"),
            ("刲", TraitSlot.Lv4, TraitFace.Feature, "先声", "Amplify"),   // spec C:铠面 放血 改名先声
            ("刲", TraitSlot.Lv5, TraitFace.Attack, "刲刺", "Amplify"),
            ("刲", TraitSlot.Lv5, TraitFace.Feature, "匿锋", "Morale,BlockMod"),
            ("刲", TraitSlot.Lv6, TraitFace.Attack, "割取", "HealSelf"),
            ("刲", TraitSlot.Lv6, TraitFace.Feature, "刲刃", "BlockMod"),   // spec A D3
            ("刲", TraitSlot.Lv8, TraitFace.Attack, "割喉", "Doom"),
            ("刲", TraitSlot.Lv8, TraitFace.Feature, "刀光", "Augment,Amplify"),
            ("鑫", TraitSlot.Lv4, TraitFace.Both, "聚金", "MoraleOverflowShield"),
            ("鑫", TraitSlot.Lv5, TraitFace.Attack, "三才", "Reshape"),
            ("鑫", TraitSlot.Lv5, TraitFace.Feature, "金身", "Augment,DefenseBuff"),
            ("鑫", TraitSlot.Lv6, TraitFace.Attack, "金汇", "BlockGain"),   // spec A D1 / D7
            ("鑫", TraitSlot.Lv6, TraitFace.Feature, "富甲", "MoraleArmor"),
            ("鑫", TraitSlot.Lv8, TraitFace.Attack, "三金破", "Reshape,Amplify"),
            ("鑫", TraitSlot.Lv8, TraitFace.Feature, "金玉满堂", "Morale,Augment,Morale"),
            (Stack4, TraitSlot.Lv4, TraitFace.Both, "刚", "Amplify"),
            (Stack4, TraitSlot.Lv5, TraitFace.Attack, "四金", "Reshape"),
            (Stack4, TraitSlot.Lv5, TraitFace.Feature, "千锤", "Morale,BlockMod"),
            (Stack4, TraitSlot.Lv6, TraitFace.Attack, "破军", "Amplify"),
            (Stack4, TraitSlot.Lv6, TraitFace.Feature, "镇军", "DamageCut"),   // spec A D4
            (Stack4, TraitSlot.Lv8, TraitFace.Attack, "千钧", "ArmorBreak,Reshape"),
            (Stack4, TraitSlot.Lv8, TraitFace.Feature, "金刚", "Augment,Morale,BlockMod"),
        };

        [Test]
        public void EveryExclusiveTrait_LoadsAtItsSlotAndFace_WithTheTableEffects()
        {
            Assert.That(Table.Length, Is.EqualTo(46), "39 格,放血 / 剁骨拆成攻击面 + 铠面两行(Q20);spec A 新增 Lv6 专属 4 行(刲刃 / 金鸣 / 金汇 / 镇军);spec C 增 剑 扫锋 1 行");
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

        // ================= 剁截下架(spec C):剁 Lv5 铠换成池·点金,反击回到单击 =================

        [Test]
        public void DuoJie_Retired_Lv5FeatureIsPoolAttune_CounterIsSingleHit()
        {
            var traits = Graph.Get("剁").Traits;
            Assert.That(traits.Any(x => x.Name == "剁截"), Is.False, "剁截已下架");
            var diangJin = traits.Single(x => x.Slot == TraitSlot.Lv5 && x.Face == TraitFace.Feature);
            Assert.That(diangJin.Name, Is.EqualTo("点金"));
            Assert.That(diangJin.Effects.Select(e => e.Kind).ToArray(), Is.EqualTo(new[] { EffectKind.Attune }));

            // 反击不再是 3 击:Lv5 铠面只有 Lv3 强化后的普通格挡,一次格挡 = 一击反击
            var rich = Battle("剁", 5, Mob(attack: 100000), Mob());
            Assert.That(rich.Cast("剁", -1, attackMode: false), Is.EqualTo(BattleError.None));
            rich.EndTurn();
            Assert.That(CounterEvents(rich), Is.EqualTo(1), "只剩单击反击");
        }

        // ================= 立威斩杀(格挡反击)与铁则(击杀时 +1 战意,spec C) =================

        [Test]
        public void LiWei_ExecutesBelowThreshold_ButCounterKillNoLongerTriggersIronRule()
        {
            // 铡 Lv5·铠 立威(`counterExecuteBelow 20`);铁则改成「击杀时战意 +1」后,格挡反击的斩杀不再触发它(Q16)
            var control = Battle("铡", 5, Mob(hp: 1000, attack: 1), Mob());
            control.Cast("铡", -1, attackMode: false);
            control.EndTurn();
            Assert.That(control.Enemies[0].Alive, Is.True, "对照:满血不被斩杀");

            var b = Battle("铡", 5, Mob(hp: 1000, attack: 1), Mob());
            b.Enemies[0].Hp = 150;   // 15% < 20%
            b.Cast("铡", -1, attackMode: false);
            b.EndTurn();
            Assert.That(b.Enemies[0].Alive, Is.False, "格挡反击命中 20% 以下的敌人 → 直接斩杀");
            Assert.That(Morale(b) - Morale(control), Is.EqualTo(0), "Q16:立威斩杀不是本字击杀,铁则不触发");
        }

        [Test]
        public void TieZe_AttackKill_GivesOneMorale_NoKillNoGain()
        {
            int MoraleAfter(int victimHp)
            {
                var b = Battle("铡", 4, Mob(hp: victimHp), Mob());
                Assert.That(b.Cast("铡", 0, attackMode: true), Is.EqualTo(BattleError.None));
                return Morale(b);
            }
            Assert.That(MoraleAfter(1) - MoraleAfter(100000), Is.EqualTo(1), "铁则:击杀时战意 +1");
        }

        // ================= 扫锋(spec C 真实数据冒烟):剑 Lv8 铠面,反击横扫同排 =================

        [Test]
        public void SaoFeng_Jian_Lv8_CounterSweepsSameRowAtSeventyPercent()
        {
            // 剑 Lv8·铠 扫锋:`BlockMod` `counterShape Row`。前排三敌只有 #0 出手:攻击者吃反击,同排另两只吃反击 × 70%
            var b = Battle("剑", 8, Mob(attack: 1000), Mob(), Mob());
            Assert.That(b.Cast("剑", -1, attackMode: false), Is.EqualTo(BattleError.None));
            b.EndTurn();
            int Lost(int i) => b.Enemies[i].MaxHp - b.Enemies[i].Hp;
            Assert.That(Lost(0), Is.GreaterThan(0), "攻击者吃到反击");
            Assert.That(Lost(1), Is.EqualTo(Lost(0) * 70 / 100).Within(1), "同排 #1 = 反击 × 70%");
            Assert.That(Lost(2), Is.EqualTo(Lost(0) * 70 / 100).Within(1), "同排 #2 = 反击 × 70%");
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
            int carried = Morale(run.Battle);   // 战意跨战斗携带(2026-10-20),开局 +2 叠在携带值上
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(Morale(run.Battle), Is.EqualTo(Math.Min(BattleConfig.BaseMoraleCap, carried + 2)), "第 2 场开局战意 +2");
        }

        // ================= 双金合璧:格挡次数 = 出字后战意(Q21 / Ruling 11) =================

        /// <summary>鍂 Lv8·铠折叠后 = [Morale 1(spec A 新本体), Block(countPerMorale, spec C min 3), Morale 1(池·蓄势 Lv5), MoraleShield, Morale 2(登记)]。
        /// 蓄势排在 Block 之后,所以格挡次数必须等效果循环结束后再读战意(终审 I1)。
        /// spec A:铠面本体战意 3 → 1(Lv3 战意加成只落在攻击面),出字后战意 = 起手 + 2。
        /// spec C:格挡次数下限 2 → 3,次数 = max(出字后战意, 3)。</summary>
        [TestCase(0, 2)]
        [TestCase(1, 3)]
        [TestCase(2, 4)]
        public void ShuangJinHeBi_BlockCount_EqualsPostCastMorale(int startMorale, int expected)
        {
            var b = Battle("鍂", 8, Mob());
            if (startMorale > 0) SetMorale(b, startMorale);
            Assert.That(b.Cast("鍂", -1, attackMode: false), Is.EqualTo(BattleError.None));
            int count = b.PlayerStatuses.Find(StatusKind.Block).Magnitude;
            Assert.That(Morale(b), Is.EqualTo(expected), "出字后战意(spec A:本体 +1、蓄势 +1,钳到上限)");
            Assert.That(count, Is.EqualTo(Math.Max(Morale(b), 3)), "格挡次数 = 出字后战意,下限 3");
            Assert.That(count, Is.GreaterThanOrEqualTo(3), "min 3(spec C)");
        }

        [Test]
        public void ShuangJinHeBi_Lv5_PlainBlockCount_Unchanged()
        {
            // 对照:Lv5 没有双金合璧,铠面是 Lv3 替换后的普通 `Block 3`(spec A:Lv1 本体 Block 2、Lv3 强化为 3),与战意无关
            foreach (int start in new[] { 0, 2 })
            {
                var b = Battle("鍂", 5, Mob());
                if (start > 0) SetMorale(b, start);
                Assert.That(b.Cast("鍂", -1, attackMode: false), Is.EqualTo(BattleError.None));
                Assert.That(b.PlayerStatuses.Find(StatusKind.Block).Magnitude, Is.EqualTo(3), $"战意 {start} 起");
            }
        }

        // ================= 聚金:溢出转盾 =================

        [Test]
        public void JuJin_OverflowMorale_BecomesShield()
        {
            // 鑫 Lv4·两面 聚金:`MoraleOverflowShield 30`。spec A:铠面本体战意只剩 1 层,凑不满溢出,
            // 改用攻击面(Lv3 强化后 Morale 4)。战意 3 + 攻击面 4 层 → 溢出,每溢出 1 层给 30
            var b = Battle("鑫", 4, Mob());
            SetMorale(b, 3);
            Assert.That(b.Cast("鑫", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(Morale(b), Is.EqualTo(Config.MoraleCap));
            Assert.That(b.PlayerShield, Is.GreaterThan(0), "溢出的战意层数 × 每层护盾(30 × 卡等级系数)");

            var none = Battle("鑫", 4, Mob());
            Assert.That(none.Cast("鑫", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(none.PlayerShield, Is.EqualTo(0), "对照:从 0 起不溢出");
        }

        /// <summary>聚金钉值(final fix 4;spec A 改走攻击面):鑫 Lv4 攻击面(Lv3 强化)Morale 4,上限 5 —— 起手 3 溢出 2 层、起手 4 溢出 3 层;
        /// 每层 = 30 × Lv4 系数。两档护盾之比 2 : 3(实测 72 / 108)。</summary>
        [Test]
        public void JuJin_ShieldPerOverflowLayer_Exact()
        {
            int Shield(int start)
            {
                var b = Battle("鑫", 4, Mob());
                SetMorale(b, start);
                Assert.That(b.Cast("鑫", 0, attackMode: true), Is.EqualTo(BattleError.None));
                return b.PlayerShield;
            }
            int perLayer = MetaRules.ScaleByCardLevel(30, 4);
            int three = Shield(3), four = Shield(4);
            Assert.That(three, Is.EqualTo(2 * perLayer), "起手 3:溢出 3 + 4 − 5 = 2 层");
            Assert.That(four, Is.EqualTo(3 * perLayer), "起手 4:溢出 3 层");
            Assert.That(three * 3, Is.EqualTo(four * 2));
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
            // 2026-10-19 锋刃回鞘并入断金:满战意攻击面没点断金也 +100%、也清战意;断金 +300% → +(100 + 300)%,
            // 相对「只有锋刃回鞘」每击 ×5/2(原断言「×4、没点断金不清战意」按新口径改)
            Assert.That(brokenHits[0].Amount * 2, Is.EqualTo(fullHits[0].Amount * 5).Within(10), "断金 +300%:每击 ×5/2");
            Assert.That(Morale(full), Is.EqualTo(0), "锋刃回鞘结算后清空战意");
            Assert.That(Morale(broken), Is.EqualTo(0), "断金同样清空");
        }

        /// <summary>大卸八块钉值(final fix 4):每击 = 本体单击 × 35%。本体单击 = 攻击面 DamageSingle 按 Lv8 缩放
        /// (攻击 100 = 基准、心属性靶子、0 甲、无暴击);起手战意 0,第一击时还没有战意攻击加成。</summary>
        [Test]
        public void DaXieBaKuai_EachHitIs35PercentOfBody()
        {
            int body = MetaRules.ScaleEffectValue(EffectKind.DamageSingle,
                Graph.Get("剁").AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value, 8);
            var b = Battle("剁", 8, Mob());
            Assert.That(b.Cast("剁", 0, attackMode: true), Is.EqualTo(BattleError.None));
            var hits = Hits(b, 0);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].Amount, Is.EqualTo(body * 35 / 100).Within(1), $"本体单击 {body} × 35%");
        }

        /// <summary>割取钉值(final fix 4):刲 Lv6 攻击面击杀 → 回复被杀者最大生命 × 10%(Q19:不吃等级 / 攻击力)。</summary>
        [Test]
        public void GeQu_KillHealsTenPercentOfVictimMaxHp()
        {
            const int victimMaxHp = 1000;
            var b = Battle("刲", 6, Mob(hp: victimMaxHp), Mob());
            b.Enemies[0].Hp = 1;
            b.DamagePlayerForTest(b.PlayerHp / 2);
            int hpBefore = b.PlayerHp;
            Assert.That(b.Cast("刲", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(b.Enemies[0].Alive, Is.False);
            Assert.That(b.PlayerHp - hpBefore, Is.EqualTo(victimMaxHp * 10 / 100));
        }

        /// <summary>金汇冒烟(spec A D7,Lv6 攻):鑫 Lv6 攻击面击杀 → 已有格挡次数 +1;没击杀则不加。
        /// 先出铠面挂格挡,再用攻击面打。</summary>
        [Test]
        public void JinHui_KillAddsOneBlockCount_NoKillNoGain()
        {
            int BlockAfter(int victimHp)
            {
                var b = Battle("鑫", 6, Mob(hp: victimHp), Mob());
                Assert.That(b.Cast("鑫", -1, attackMode: false), Is.EqualTo(BattleError.None));
                int before = b.PlayerStatuses.Find(StatusKind.Block).Magnitude;
                Assert.That(b.Cast("鑫", 0, attackMode: true), Is.EqualTo(BattleError.None));
                return b.PlayerStatuses.Find(StatusKind.Block).Magnitude - before;
            }
            Assert.That(BlockAfter(1), Is.EqualTo(1), "击杀:格挡次数 +1");
            Assert.That(BlockAfter(100000), Is.EqualTo(0), "对照:没击杀不加");
        }
    }
}
