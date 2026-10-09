using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>火系三枚状态 chip(StatusChipsFire 稿,2026-10-09 拍板)给表现层的只读查询:
    /// 埋雷血条预扣段(<see cref="BattleEngine.MineHpLoss"/>)、回敬是否生效与剩余次数
    /// (<see cref="BattleEngine.RetaliateArmed"/> / <see cref="BattleEngine.RetaliateChargesLeft"/>)、
    /// 炽焰详情里的特性名(<see cref="TraitRules.NameOf"/>)。
    ///
    /// 夹具口径同 FireHookTests:Element.Heart(生克 1.0×)、PlayerAttack = 100、卡 Lv1。</summary>
    public class FireChipQueryTests
    {
        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = RebalanceFixture.BaseMaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(CharDef[] defs, EnemyDef[] enemies) =>
            new(RebalanceFixture.Graph(defs), Config, defs.SelectMany(d => new[] { d.Id, d.Id, d.Id }).ToArray(), Array.Empty<string>(),
                enemies, seed: 1, cardLevels: defs.ToDictionary(d => d.Id, _ => 1));

        private static EnemyDef Mob(int hp = 1000, int attack = 10, int defense = 0) =>
            new("怔", Element.Heart, hp, attack, defense: defense);

        private static readonly CharDef Idle = new("闲", Element.Heart, effects: new[] { new EffectDef(EffectKind.BurnSingle, 1) });

        private static CharDef Guard(int cap, string id = "烈") =>
            new(id, Element.Heart, effects: new[]
            {
                new EffectDef(EffectKind.Retaliate, cap, perHit: new[] { new EffectDef(EffectKind.BurnSingle, 2) }),
            });

        private static void SetMine(BattleEngine b, int i, int magnitude, string source = "炸") =>
            b.Enemies[i].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Mine, Polarity = StatusPolarity.Debuff, Magnitude = magnitude, TurnsLeft = -1, SourceId = source });

        // ================= 埋雷:血条预扣段 =================

        [Test]
        public void MineHpLoss_NoMine_IsZero()
        {
            var b = Battle(new[] { Idle }, new[] { Mob() });
            Assert.That(b.MineHpLoss(0), Is.EqualTo(0));
        }

        [Test]
        public void MineHpLoss_IgnoresArmor_SumsSources()
        {
            var b = Battle(new[] { Idle }, new[] { Mob(defense: 50) });
            SetMine(b, 0, 50);
            Assert.That(b.MineHpLoss(0), Is.EqualTo(50), "埋雷无视护甲");
            SetMine(b, 0, 30, "爆");
            Assert.That(b.MineHpLoss(0), Is.EqualTo(80), "不同来源各炸一次,累加");
        }

        [Test]
        public void MineHpLoss_AppliesMark_ThenShield_CappedAtHp()
        {
            var b = Battle(new[] { Idle }, new[] { Mob(hp: 1000) });
            SetMine(b, 0, 100);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Vulnerable, Polarity = StatusPolarity.Debuff, Magnitude = 20, TurnsLeft = 2, SourceId = "标" });
            Assert.That(b.MineHpLoss(0), Is.EqualTo(120), "标记 +20%");
            b.Enemies[0].Shield = 30;
            Assert.That(b.MineHpLoss(0), Is.EqualTo(90), "护盾先吃,剩下才是掉的血");
            b.Enemies[0].Hp = 50;
            Assert.That(b.MineHpLoss(0), Is.EqualTo(50), "封顶到当前生命 = 整截斜纹");
        }

        [Test]
        public void MineHpLoss_MatchesActualBlast()
        {
            var b = Battle(new[] { Idle }, new[] { Mob(hp: 1000, attack: 0, defense: 40) });
            SetMine(b, 0, 77);
            b.Enemies[0].Statuses.Apply(new StatusEffect
                { Kind = StatusKind.Vulnerable, Polarity = StatusPolarity.Debuff, Magnitude = 15, TurnsLeft = 3, SourceId = "标" });
            b.Enemies[0].Shield = 10;
            int predicted = b.MineHpLoss(0);
            int before = b.Enemies[0].Hp;
            b.EndTurn();
            Assert.That(before - b.Enemies[0].Hp, Is.EqualTo(predicted), "预扣段与爆炸实际扣血同口径");
        }

        // ================= 回敬:是否生效 + 剩余次数 =================

        [Test]
        public void Retaliate_None_NotArmed()
        {
            var b = Battle(new[] { Guard(0) }, new[] { Mob() });
            Assert.That(b.RetaliateArmed, Is.False);
            Assert.That(b.RetaliateChargesLeft(), Is.Null);
        }

        [Test]
        public void Retaliate_Uncapped_ArmedWithoutNumber()
        {
            var b = Battle(new[] { Guard(0) }, new[] { Mob() });
            b.Cast("烈", -1);
            Assert.That(b.RetaliateArmed, Is.True);
            Assert.That(b.RetaliateChargesLeft(), Is.Null, "不限次数:chip 不带数字");
        }

        [Test]
        public void Retaliate_Capped_CountsDown_HidesWhenSpent()
        {
            var b = Battle(new[] { Guard(2) }, new[] { Mob() });
            b.Cast("烈", -1);
            var s = b.PlayerStatuses.Find(StatusKind.Retaliate);
            Assert.That(b.RetaliateChargesLeft(), Is.EqualTo(2));
            Assert.That(b.TryUseTrait(BattleEngine.RetaliateUseKey(s), perTurn: s.Magnitude, perBattle: 0), Is.True);
            Assert.That(b.RetaliateChargesLeft(), Is.EqualTo(1), "数字 = 剩余次数");
            Assert.That(b.RetaliateArmed, Is.True);
            b.TryUseTrait(BattleEngine.RetaliateUseKey(s), perTurn: s.Magnitude, perBattle: 0);
            Assert.That(b.RetaliateArmed, Is.False, "用完 = 不起作用,chip 隐藏(同格挡)");
        }

        [Test]
        public void Retaliate_UncappedSourceWins_NoNumber()
        {
            var b = Battle(new[] { Guard(1), Guard(0, "焱") }, new[] { Mob() });
            b.Cast("烈", -1);
            b.Cast("焱", -1);
            Assert.That(b.RetaliateArmed, Is.True);
            Assert.That(b.RetaliateChargesLeft(), Is.Null, "有一条不限次数就不带数字");
        }

        // ================= 炽焰:特性名 =================

        [Test]
        public void TraitNameOf_FindsByKey_NullWhenMissing()
        {
            var blaze = new TraitDef(TraitSlot.Lv6, TraitFace.Feature, TraitForm.Passive, null, "炽焰", Array.Empty<EffectDef>());
            var def = new CharDef("炎", Element.Fire, effects: new[] { new EffectDef(EffectKind.BurnSingle, 1) }, traits: new[] { blaze });
            Assert.That(TraitRules.NameOf(def, BattleEngine.TraitKey("炎", TraitSlot.Lv6, TraitFace.Feature)), Is.EqualTo("炽焰"));
            Assert.That(TraitRules.NameOf(def, BattleEngine.TraitKey("炎", TraitSlot.Lv5, TraitFace.Feature)), Is.Null);
            Assert.That(TraitRules.NameOf(def, null), Is.Null);
        }
    }
}
