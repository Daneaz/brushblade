using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 3:受击挂点族 —— G2 HitCap(铜墙铁壁)、G3 HurtShield(堡垒)、G4 ShieldShatter(碎玉)。
    /// 口径见 plan「已定口径」Q1 / Q6 / Q7 / Q21 与附录 G2–G4;字表数据归 Task 6,这里一律手搓 CharDef。</summary>
    public class EarthHitHooksTests
    {
        private const int MaxHp = 1000;

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies, int playerAttack = 100,
            int startingHp = MaxHp, int bossChargeEvery = 2)
        {
            var all = new[] { new CharDef("木", Element.Wood) }.Concat(defs).ToArray();
            return new BattleEngine(RebalanceFixture.Graph(all),
                new BattleConfig
                {
                    PlayerMaxHp = MaxHp, PlayerAttack = playerAttack, ApPerTurn = 200, LibraryCapacity = 200,
                    BossChargeEvery = bossChargeEvery,
                },
                defs.SelectMany(d => Enumerable.Repeat(d.Id, 10)).ToArray(), Array.Empty<string>(),
                enemies, seed: 3, startingHp: startingHp);
        }

        private static EnemyDef Mob(int attack) => RebalanceFixture.Mob(attack: attack);

        private static List<int> PlayerHits(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.EnemyAttack).Select(e => e.Amount).ToList();

        private static int SourceDamage(BattleEngine b, EffectSource src) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == src).Sum(e => e.Amount);

        private static List<int> PlayerShieldEvents(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == Targeting.PlayerTarget).Select(e => e.Amount).ToList();

        // ---------------- G2 HitCap ----------------

        private static CharDef Wall(int n = 10) => RebalanceFixture.Char("壁", new EffectDef(EffectKind.HitCap, n));

        [Test]
        public void HitCap_CapsPlayerHit_AtMaxHpPercent()
        {
            var b = Battle(new[] { Wall() }, new[] { Mob(200) });
            Assert.That(b.Cast("壁", -1), Is.EqualTo(BattleError.None));
            Assert.That(b.PlayerStatuses.Find(StatusKind.HitCap).TurnsLeft, Is.EqualTo(1));
            b.EndTurn();
            // 200 → 上限 1000 × 10% = 100;预算下限 200 × 40% = 80,不触底
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 100 }));
            Assert.That(b.PlayerHp, Is.EqualTo(MaxHp - 100));
        }

        [Test]
        public void HitCap_UnderCap_NoChange()
        {
            var b = Battle(new[] { Wall() }, new[] { Mob(70) });
            b.Cast("壁", -1);
            b.EndTurn();
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 70 }));
        }

        [Test]
        public void HitCap_ReductionMergedIntoSixtyPercentBudget()
        {
            // 300 → 上限 100 会削 66%,超出 60% 非护甲减伤预算 → 钳到 300 × 40% = 120
            var b = Battle(new[] { Wall() }, new[] { Mob(300) });
            b.Cast("壁", -1);
            b.EndTurn();
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 120 }));
        }

        [Test]
        public void HitCap_WithDamageCut_SharesBudget()
        {
            var cut = RebalanceFixture.Char("杜", new EffectDef(EffectKind.DamageCut, 20));
            // 150:减伤 20% → 120,再封顶 100(合计削 33%,预算内)
            var b = Battle(new[] { Wall(), cut }, new[] { Mob(150) });
            b.Cast("壁", -1); b.Cast("杜", -1);
            b.EndTurn();
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 100 }));
            // 300:减伤 20% → 240,封顶 100 会让合计削 66% → 钳到 120
            var b2 = Battle(new[] { Wall(), cut }, new[] { Mob(300) });
            b2.Cast("壁", -1); b2.Cast("杜", -1);
            b2.EndTurn();
            Assert.That(PlayerHits(b2), Is.EqualTo(new[] { 120 }));
        }

        /// <summary>Review Focus 1:格挡(40%)+ 杜门不出式减伤(60%)+ 铜墙铁壁同时在 —— 合计仍钳 60%,不出负数。</summary>
        [Test]
        public void HitCap_BlockPlusDamageCutPlusHitCap_StillSixtyPercent()
        {
            var cut = RebalanceFixture.Char("杜", new EffectDef(EffectKind.DamageCut, 60));
            var parry = RebalanceFixture.Char("挡", new EffectDef(EffectKind.Block, 1));
            var b = Battle(new[] { Wall(1), cut, parry }, new[] { Mob(500) });
            b.Cast("壁", -1); b.Cast("杜", -1); b.Cast("挡", -1);
            b.EndTurn();
            // 封顶 1000 × 1% = 10,但格挡 + 减伤已用满 60% → 500 × 40% = 200,铜墙铁壁不再削
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 200 }));
        }

        [Test]
        public void HitCap_ExpiresAtNextPlayerTurn()
        {
            var b = Battle(new[] { Wall() }, new[] { Mob(200) });
            b.Cast("壁", -1);
            b.EndTurn();
            Assert.That(b.PlayerStatuses.Has(StatusKind.HitCap), Is.False, "TurnsLeft 1:下回合开始到期");
            b.EndTurn();
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 200 }));
        }

        [Test]
        public void HitCap_MultipleSources_TakeStrongest()
        {
            var a = RebalanceFixture.Char("壁", new EffectDef(EffectKind.HitCap, 10));
            var c = RebalanceFixture.Char("墉", new EffectDef(EffectKind.HitCap, 15));
            var b = Battle(new[] { a, c }, new[] { Mob(200) });
            b.Cast("壁", -1); b.Cast("墉", -1);
            b.EndTurn();
            // 「取最强」= 上限百分比最大(150)—— 不是取最紧的那条
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 150 }));
        }

        [Test]
        public void HitCap_Summon_UsesSummonMaxHp()
        {
            var grove = RebalanceFixture.Char("林",
                new EffectDef(EffectKind.Summon, 500, summonCount: 1, summonAttack: 0, summonChar: "木"));
            var b = Battle(new[] { Wall(), grove }, new[] { Mob(100) });
            b.Cast("林", -1); b.Cast("壁", -1);
            b.EndTurn();
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonHit).Select(e => e.Amount).ToList();
            // 木灵 MaxHp 500 × 10% = 50(按玩家 1000 算会是 100、不削);预算下限 40
            Assert.That(hits, Is.EqualTo(new[] { 50 }));
            Assert.That(b.Summons[0].Hp, Is.EqualTo(450));
        }

        [Test]
        public void HitCap_Summon_BudgetFloor()
        {
            var grove = RebalanceFixture.Char("林",
                new EffectDef(EffectKind.Summon, 500, summonCount: 1, summonAttack: 0, summonChar: "木"));
            var b = Battle(new[] { Wall(), grove }, new[] { Mob(200) });
            b.Cast("林", -1); b.Cast("壁", -1);
            b.EndTurn();
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.SummonHit).Select(e => e.Amount).ToList();
            Assert.That(hits, Is.EqualTo(new[] { 80 }), "上限 50 会削 75% → 钳到 200 × 40%");
        }

        // ---------------- G3 HurtShield ----------------

        private static CharDef Fort(int n = 50, int turns = 2, string id = "堡") =>
            RebalanceFixture.Char(id, new EffectDef(EffectKind.HurtShield, n, turns: turns));

        [Test]
        public void HurtShield_EveryHit_GrantsShield_AndHeft()
        {
            var b = Battle(new[] { Fort() }, new[] { Mob(10), Mob(10) });
            b.Cast("堡", -1);
            Assert.That(b.PlayerStatuses.Find(StatusKind.HurtShield).Magnitude, Is.EqualTo(50));
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 50, 50 }), "每次挥击命中都给,不限次数");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Heft), Is.EqualTo(1), "两份 50 攒满 1 层厚");
        }

        [Test]
        public void HurtShield_AmountFixedAtCast_ScalesWithAttack()
        {
            var b = Battle(new[] { Fort() }, new[] { Mob(10) }, playerAttack: 200);
            b.Cast("堡", -1);
            Assert.That(b.PlayerStatuses.Find(StatusKind.HurtShield).Magnitude, Is.EqualTo(100));
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 100 }));
        }

        [Test]
        public void HurtShield_GoesIntoNormalBucket()
        {
            // 两只怪:第二击被第一击给的盾吸收 —— 吸收量 > 0 证明盾在普通桶里接着用
            var b = Battle(new[] { Fort() }, new[] { Mob(10), Mob(10) });
            b.Cast("堡", -1);
            b.EndTurn();
            var absorbed = b.LastEvents.Where(e => e.Kind == BattleEventKind.EnemyAttack).Select(e => e.Absorbed).ToList();
            Assert.That(absorbed, Is.EqualTo(new[] { 0, 10 }));
            Assert.That(b.ShieldPersist, Is.EqualTo(0), "进普通桶,不进留存桶");
        }

        [Test]
        public void HurtShield_NotOnLethalHit()
        {
            var b = Battle(new[] { Fort() }, new[] { Mob(100) }, startingHp: 50);
            b.Cast("堡", -1);
            b.EndTurn();
            Assert.That(b.Phase, Is.EqualTo(BattlePhase.Lost));
            Assert.That(PlayerShieldEvents(b), Is.Empty, "判负之后不触发");
        }

        [Test]
        public void HurtShield_NotOnZeroDamage()
        {
            var b = Battle(new[] { Fort() }, new[] { Mob(0) });
            b.Cast("堡", -1);
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.Empty);
        }

        [Test]
        public void HurtShield_MultipleSources_TakeStrongest()
        {
            var b = Battle(new[] { Fort(30, id: "堡"), Fort(70, id: "垒") }, new[] { Mob(10) });
            b.Cast("堡", -1); b.Cast("垒", -1);
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { 70 }), "取最强,不相加");
        }

        [Test]
        public void HurtShield_SummonHit_DoesNotTrigger()
        {
            var grove = RebalanceFixture.Char("林",
                new EffectDef(EffectKind.Summon, 500, summonCount: 1, summonAttack: 0, summonChar: "木"));
            var b = Battle(new[] { Fort(), grove }, new[] { Mob(30) });
            b.Cast("林", -1); b.Cast("堡", -1);
            b.EndTurn();
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.SummonHit), Is.True, "木灵确实顶在前面挨了打");
            Assert.That(PlayerShieldEvents(b), Is.Empty, "只认玩家受击");
        }

        [Test]
        public void HurtShield_LastsTurns()
        {
            var b = Battle(new[] { Fort(turns: 2) }, new[] { Mob(10) });
            b.Cast("堡", -1);
            var perTurn = new List<int>();
            for (int i = 0; i < 3; i++) { b.EndTurn(); perTurn.Add(PlayerShieldEvents(b).Count); }
            Assert.That(perTurn, Is.EqualTo(new[] { 1, 1, 0 }));
        }

        // ---------------- G4 ShieldShatter ----------------

        private static CharDef Jade(int shield, int shatter = 50, bool persist = false, int recoil = 0)
        {
            var effects = new List<EffectDef> { new EffectDef(EffectKind.Shield, shield, persistOnce: persist) };
            if (recoil > 0) effects.Add(new EffectDef(EffectKind.ShieldRecoil, recoil));
            effects.Add(new EffectDef(EffectKind.ShieldShatter, shatter));
            return RebalanceFixture.Char("碎", effects.ToArray());
        }

        [Test]
        public void ShieldShatter_BreakingHit_ReflectsAbsorbedPercent_ThenRemoved()
        {
            var b = Battle(new[] { Jade(100) }, new[] { Mob(300) });
            b.Cast("碎", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.True);
            int enemyHp = b.Enemies[0].Hp;
            b.EndTurn();
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(50), "吸收 100 × 50%");
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(enemyHp - 50), "心属性、无视护甲");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False, "触发后移除");
        }

        [Test]
        public void ShieldShatter_NotBroken_NoReflect_StaysUntilBroken()
        {
            // 留存桶撑过回合初清盾;第一回合 30 没打穿,第二回合 300 打穿剩下的 70
            var b = Battle(new[] { Jade(100, persist: true) }, new[] { Mob(30) });
            b.Cast("碎", -1);
            b.EndTurn();
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.True);

            var b2 = Battle(new[] { Jade(100, persist: true) }, new[] { Mob(30), Mob(300) });
            b2.Cast("碎", -1);
            b2.EndTurn();
            Assert.That(SourceDamage(b2, EffectSource.ShieldShatter), Is.EqualTo(35), "打破那一击只吸收了 70 → 35");
        }

        [Test]
        public void ShieldShatter_NotMountedWithoutShieldGranted()
        {
            var bare = RebalanceFixture.Char("碎", new EffectDef(EffectKind.ShieldShatter, 50));
            var b = Battle(new[] { bare }, new[] { Mob(10) });
            b.Cast("碎", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False, "挂载同反震:本次出字没给玩家加盾就不挂");
        }

        [Test]
        public void ShieldShatter_ToppleClear_OnlyRemoves()
        {
            // 倾覆:先挥击(100,没打穿 500 的盾)再掀盾 —— 掀盾不是挥击,只移除载体不反伤
            var boss = new EnemyDef("钧", Element.Heart, 100000, 50,
                phases: new[] { new BossPhaseDef("钧", Element.Heart, 100000, 50, BossSkill.Topple) });
            var b = Battle(new[] { Jade(500) }, new[] { boss }, bossChargeEvery: 1);
            b.EndTurn();   // Boss 蓄力
            b.Cast("碎", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.True);
            b.EndTurn();   // 倾覆
            Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.ShieldBroken), Is.True, "确实掀了盾");
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(0));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False);
        }

        [Test]
        public void ShieldShatter_ShieldSpendClear_OnlyRemoves()
        {
            var spend = RebalanceFixture.Char("裂", new EffectDef(EffectKind.ShieldSpend, 100));
            var b = Battle(new[] { Jade(100), spend }, new[] { Mob(0) });
            b.Cast("碎", -1); b.Cast("裂", -1);
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False);
            Assert.That(b.LastEvents.Any(e => e.Source == EffectSource.ShieldShatter), Is.False);
        }

        [Test]
        public void ShieldShatter_MultipleSources_KeepStrongest()
        {
            var weak = RebalanceFixture.Char("玉", new EffectDef(EffectKind.Shield, 10), new EffectDef(EffectKind.ShieldShatter, 20));
            var b = Battle(new[] { Jade(100, shatter: 50), weak }, new[] { Mob(300) });
            b.Cast("碎", -1); b.Cast("玉", -1);
            Assert.That(b.PlayerStatuses.All.Count(s => s.Kind == StatusKind.ShieldShatter), Is.EqualTo(1));
            b.EndTurn();
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(55), "吸收 110 × 50%");
        }

        // Review Focus 1:镜 + 格挡 + 反震 + 碎玉同时在 —— 合计钳 60%,顺序 镜 → 格挡 → 反震 → 碎玉(Q6)。
        // 场景:挥击 100,格挡削 40% → 60(预算 36);镜 30% → 18;格挡反击 = 攻击面 50 × 30% = 15;剩 3。
        private static CharDef ReflectChar() => RebalanceFixture.Char("镜", new EffectDef(EffectKind.Reflect, 30, turns: 2));
        private static CharDef ParryChar() => new("挡", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.Block, 1) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 50) });

        [Test]
        public void RetaliationBudget_MirrorBlockShatter_ShatterTakesRemainder()
        {
            var b = Battle(new[] { Jade(60), ReflectChar(), ParryChar() }, new[] { Mob(100) });
            b.Cast("碎", -1); b.Cast("镜", -1); b.Cast("挡", -1);
            b.EndTurn();
            Assert.That(PlayerHits(b), Is.EqualTo(new[] { 60 }));
            Assert.That(SourceDamage(b, EffectSource.Reflect), Is.EqualTo(18));
            Assert.That(SourceDamage(b, EffectSource.BlockCounter), Is.EqualTo(15));
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(3), "碎玉 60 × 50% = 30,只拿剩下的 3");
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False, "预算打不满也照样移除");
        }

        [Test]
        public void RetaliationBudget_MirrorBlockRecoilShatter_AllFourCapped()
        {
            var b = Battle(new[] { Jade(60, recoil: 30), ReflectChar(), ParryChar() }, new[] { Mob(100) });
            b.Cast("碎", -1); b.Cast("镜", -1); b.Cast("挡", -1);
            b.EndTurn();
            Assert.That(SourceDamage(b, EffectSource.Reflect), Is.EqualTo(18));
            Assert.That(SourceDamage(b, EffectSource.BlockCounter), Is.EqualTo(15));
            Assert.That(SourceDamage(b, EffectSource.ShieldRecoil), Is.EqualTo(3), "反震排在碎玉前,拿走剩下的 3");
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(0), "预算已空,不出负数");
            int total = new[] { EffectSource.Reflect, EffectSource.BlockCounter, EffectSource.ShieldRecoil, EffectSource.ShieldShatter }
                .Sum(s => SourceDamage(b, s));
            Assert.That(total, Is.EqualTo(60 * CombatCaps.ReflectPercent / 100));
            Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False);
        }

        [Test]
        public void RetaliationBudget_RecoilThenShatter_NoMirror()
        {
            // 无镜无格挡:挥击 100,预算 60;反震 吸收 100 × 30% = 30;碎玉 100 × 50% = 50 → 只拿 30
            var b = Battle(new[] { Jade(100, recoil: 30) }, new[] { Mob(100) });
            b.Cast("碎", -1);
            b.EndTurn();
            Assert.That(SourceDamage(b, EffectSource.ShieldRecoil), Is.EqualTo(30));
            Assert.That(SourceDamage(b, EffectSource.ShieldShatter), Is.EqualTo(30));
        }

        // ---------------- 分类 / Loader ----------------

        [Test]
        public void Enums_AppendedAtTail()
        {
            Assert.That((int)EffectKind.HitCap, Is.EqualTo((int)EffectKind.ShieldCarry + 1));
            Assert.That((int)EffectKind.HurtShield, Is.EqualTo((int)EffectKind.ShieldCarry + 2));
            Assert.That((int)EffectKind.ShieldShatter, Is.EqualTo((int)EffectKind.ShieldCarry + 3));
            Assert.That((int)StatusKind.HitCap, Is.EqualTo((int)StatusKind.ShieldCarry + 1));
            Assert.That((int)StatusKind.HurtShield, Is.EqualTo((int)StatusKind.ShieldCarry + 2));
            Assert.That((int)StatusKind.ShieldShatter, Is.EqualTo((int)StatusKind.ShieldCarry + 3));
            Assert.That((int)EffectSource.ShieldShatter, Is.EqualTo((int)EffectSource.FreezeRider + 1));
        }

        [Test]
        public void Classification()
        {
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.HitCap), Is.False);
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.ShieldShatter), Is.False);
            Assert.That(MetaRules.ScalesWithCardLevel(EffectKind.HurtShield), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.HitCap), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.HurtShield), Is.True);
            Assert.That(StatusRules.IsBattleScoped(StatusKind.ShieldShatter), Is.True);
        }

        private static RecipeGraph Load(string effect) => ConfigLoader.LoadGraph(
            @"{""chars"":[{""id"":""碎"",""element"":""Earth"",""effects"":[" + effect + "]}]}");

        [TestCase(@"{""kind"":""HitCap"",""value"":10}")]
        [TestCase(@"{""kind"":""HurtShield"",""value"":44,""turns"":2}")]
        [TestCase(@"{""kind"":""ShieldShatter"",""value"":50}")]
        public void Loader_Accepts(string effect) =>
            Assert.That(Load(effect).Get("碎").Effects.Count, Is.EqualTo(1));

        [TestCase(@"{""kind"":""HitCap"",""value"":0}", "HitCap")]
        [TestCase(@"{""kind"":""HitCap"",""value"":101}", "HitCap")]
        [TestCase(@"{""kind"":""ShieldShatter"",""value"":0}", "ShieldShatter")]
        [TestCase(@"{""kind"":""HurtShield"",""value"":44}", "HurtShield")]
        [TestCase(@"{""kind"":""HurtShield"",""value"":0,""turns"":2}", "HurtShield")]
        public void Loader_Rejects(string effect, string needle)
        {
            var ex = Assert.Throws<ConfigException>(() => Load(effect));
            Assert.That(ex.Message, Does.Contain(needle));
        }
    }
}
