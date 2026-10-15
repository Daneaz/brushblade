using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan D2-土 Task 6:土系 35 格专属特性的数据落表 —— 读真实 chars.json。
    /// 先用一张表钉死「每条特性落在正确的 (槽, 面, 形态),效果按书写顺序、关键字段逐条对得上特性表那一行」,
    /// 再按机制类别各取一条做集成测试(地裂 5 击上限、塔倾减半、积土成山三次、七级浮屠计层、厚德载物本场、堡垒逐击、碎玉进预算、粉身碎骨平分、
    /// 杜塞 Boss 半伤、杜门不出每场一次、圭臬战后不携带、对峙开局护甲 5 场、山岳门槛)。
    /// 机制本身的单元级测试在 Earth* 系列;这里只证明「那一行写出来的配置在真字上接上了」。
    ///
    /// 夹具:敌人一律心属性(土打心 1.0×)、0 甲;玩家攻击 100、最大生命 5000(护盾上限 5000)、暴击率 0、基础护甲 0。
    /// 数值是 (F) 起值,改表必须同步这里。</summary>
    public class EarthExclusiveDataTests
    {
        /// <summary>山岳用:每只敌人那一拍结束时记一次 <see cref="BattleEngine.RetaliateChargesLeft"/>。</summary>
        private sealed class ChargesProbe : IBattleHookListener
        {
            public readonly List<int?> Seen = new List<int?>();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.TurnEnded && args.Subject.Side == UnitSide.Enemy) Seen.Add(battle.RetaliateChargesLeft());
            }
        }

        private const int MaxHp = 5000;
        private const int Hp = 100000;

        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        private static BattleConfig Config => new BattleConfig
            { PlayerMaxHp = MaxHp, PlayerAttack = 100, ApPerTurn = 20, BossPhaseJitterPercent = 0 };

        private static BattleEngine Battle(string id, int level, int shield, params EnemyDef[] enemies) =>
            new(Graph, Config, Enumerable.Repeat(id, 6).ToArray(), Array.Empty<string>(), enemies, seed: 1,
                cardLevels: new Dictionary<string, int> { [id] = level },
                startingNormalShield: shield);

        private static BattleEngine Battle(string id, int level, params EnemyDef[] enemies) => Battle(id, level, 0, enemies);

        private static EnemyDef Mob(int hp = Hp, int attack = 0) => new("怔", Element.Heart, hp, attack);

        private static void Cast(BattleEngine b, string id, int target, bool attack) =>
            Assert.That(b.Cast(id, target, attackMode: attack), Is.EqualTo(BattleError.None), id);

        private static List<BattleEvent> Damage(BattleEngine b, EffectSource src) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Damage && e.Source == src).ToList();

        private static List<int> PlayerShieldEvents(BattleEngine b) => b.LastEvents
            .Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == Targeting.PlayerTarget).Select(e => e.Amount).ToList();

        /// <summary>按书写顺序把一条效果的种类、数值与非缺省字段压成一行;特性表那一行的每个 token 都在这里留痕。</summary>
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
            if (e.Kind == EffectKind.Augment) p.Add($"of {e.AugmentKind}.{e.AugmentField}");
            if (e.OpeningBattles != 0) p.Add($"battles {e.OpeningBattles}");
            if (e.ScaleBy != ScaleBasis.None) p.Add($"per {e.ScaleBy}");
            if (e.ScaleDivisor != 0) p.Add($"div {e.ScaleDivisor}");
            if (e.ScaleCap != 0) p.Add($"cap {e.ScaleCap}");
            if (e.ShieldStrikePercent != 0) p.Add($"shieldStrike {e.ShieldStrikePercent}");
            if (e.ArmorStrikePercent != 0) p.Add($"armorStrike {e.ArmorStrikePercent}");
            if (e.ArmorShieldPercent != 0) p.Add($"armorShield {e.ArmorShieldPercent}");
            if (e.ArmorStrikeEach) p.Add("armorStrikeEach");
            if (e.OfShield) p.Add("ofShield");
            if (e.PerShieldTenth) p.Add("perShieldTenth");
            if (e.OfPlayerShield) p.Add("ofPlayerShield");
            if (e.MinArmor != 0) p.Add($"minArmor {e.MinArmor}");
            if (e.WardCount != 0) p.Add($"wardCount {e.WardCount}");
            if (e.PerHit.Count > 0) p.Add("perHit[" + string.Join(";", e.PerHit.Select(Sig)) + "]");
            return string.Join(" ", p);
        }

        // ================= 表驱动:35 格 37 行 =================

        private const TraitSlot L4 = TraitSlot.Lv4, L5 = TraitSlot.Lv5, L6 = TraitSlot.Lv6, L8 = TraitSlot.Lv8;
        private const TraitFace Atk = TraitFace.Attack, Ftr = TraitFace.Feature, Both = TraitFace.Both;

        // (字, 槽, 面, 名, 效果签名 ——「|」分隔,按 chars.json 落表顺序 = 书写顺序,Ruling 13)。
        // 塔倾 / 地裂的 ShieldSpend 写在伤害修饰之后:先打、后扣盾
        private static readonly (string Ch, TraitSlot Slot, TraitFace Face, string Name, string Sigs)[] Table =
        {
            ("碉", L8, Ftr, "碉楼", "ShieldSummons 50 ofShield"),
            ("垒", L8, Ftr, "积土成山", "Amplify 30 scope Shield|TurnPulse 0 turns 3 perHit[Shield 16]"),
            ("壁", L8, Ftr, "铜墙铁壁", "HitCap 10"),
            ("堡", L5, Atk, "据守反击", "Reshape 0 shieldStrike 30"),
            ("堡", L8, Ftr, "堡垒", "HurtShield 44 turns 2"),
            ("崩", L5, Ftr, "落石", "Weaken 20 turns 1 pick All"),
            ("崩", L8, Atk, "地裂", "Reshape 0 per Shield div 100 cap 4|ShieldSpend 100"),
            ("碎", L5, Ftr, "碎玉", "ShieldShatter 50"),
            ("碎", L8, Atk, "粉身碎骨", "OverkillSplash 100"),
            ("塔", L4, Atk, "塔基", "Reshape 0 armorStrike 300"),
            ("塔", L4, Ftr, "塔基", "Reshape 0 armorShield 300"),
            ("塔", L5, Atk, "塔倾", "Reshape 0 shieldStrike 50|ShieldSpend 50"),
            ("塔", L8, Atk, "镇塔", "Reshape 0 armorStrike 500|Weaken 30 turns 2"),
            ("塔", L8, Ftr, "七级浮屠", "DefenseBuff 8 turns 4 cap 7 perShieldTenth"),
            ("杜", L4, Both, "杜绝", "DebuffWard 0 turns 1 wardCount 1"),
            ("杜", L5, Atk, "杜塞", "Silence 0 turns 2 if NotBoss|SkillCut 50 if IsBoss"),
            ("杜", L6, Atk, "杜渐", "BuffBlock 0 turns 2"),   // spec A D1 改名自「杜绝」、D2 改成 2 回合无法获得增益
            ("杜", L6, Ftr, "闭门", "Shield 63 battles 1"),
            ("杜", L8, Atk, "堵截", "Freeze 1"),
            ("杜", L8, Ftr, "杜门不出", "DamageCut 60|ApCost 1"),
            ("圭", L4, Both, "叠土", "Shield 126"),
            ("圭", L5, Atk, "重土", "TurnPulse 0 turns 1 perHit[Shield 63]"),
            ("圭", L6, Atk, "圭棱", "Amplify 20 if ArmorBroken scope Damage"),   // spec A D1 改名自「圭角」
            ("圭", L6, Ftr, "圭臬", "Augment 0 of DefenseBuff.Battle"),
            ("圭", L8, Atk, "圭角", "Reshape 0 shieldStrike 40|ArmorBreak 32 turns 2"),
            ("圭", L8, Ftr, "土生金", "ShieldToMorale 30"),
            ("垚", L4, Both, "三土", "Shield 75"),
            ("垚", L5, Atk, "移山", "Reshape 0 shape Column shapePct 70 shieldStrike 30"),
            ("垚", L5, Ftr, "叠嶂", "DefenseBuff 38 turns 4"),
            ("垚", L6, Atk, "垚峙", "Reshape 0 armorStrike 200"),   // spec A:伤害 + 护甲 × 2
            ("垚", L6, Ftr, "山岳", "Retaliate 1 turns 4 minArmor 50 perHit[ArmorBreak 38 turns 2]"),
            ("垚", L8, Atk, "三山", "Reshape 0 shape Scatter shots 3 armorStrike 300 armorStrikeEach"),
            ("垚", L8, Ftr, "对峙", "Taunt 1 pick Self|DefenseBuff 38 turns 4 battles 5"),
            ("㙓", L4, Atk, "深厚", "Amplify 30 scope All"),
            ("㙓", L4, Ftr, "深厚", "Augment 4 of DefenseBuff.Turns"),
            ("㙓", L5, Atk, "山压", "Reshape 0 shieldStrike 50"),
            ("㙓", L5, Ftr, "厚土", "Amplify 50 scope Shield|AddHeft 2"),
            ("㙓", L6, Atk, "厚壤", "Augment 1 of ArmorBreak.Turns|ArmorBreak 20 turns 3"),   // spec A D1 改名自「厚土」
            ("㙓", L6, Ftr, "载物", "ShieldSummons 50 ofShield"),
            ("㙓", L8, Atk, "地动", "Reshape 0 shape All shieldStrike 30|ArmorBreak 45 turns 2 pick All"),
            ("㙓", L8, Ftr, "厚德载物", "Amplify 100 scope Shield|TurnPulse 0 turns -1 perHit[ShieldSummons 10 ofPlayerShield]"),
        };

        [Test]
        public void EveryExclusiveTrait_LoadsAtItsSlotAndFace_WithTheTableEffects()
        {
            Assert.That(Table.Length, Is.EqualTo(41), "35 格,塔基 / 深厚拆成两行(V4);spec A 增 Lv6 攻面专属 4 行");
            Assert.That(Table.Select(r => (r.Ch, r.Slot, r.Name)).Distinct().Count(), Is.EqualTo(39));
            foreach (var (ch, slot, face, name, sigs) in Table)
            {
                var t = Graph.Get(ch).Traits.SingleOrDefault(x => x.Slot == slot && x.Face == face && x.Name == name);
                Assert.That(t, Is.Not.Null, $"{ch} {name}({slot}/{face})");
                (TraitForm, TraitTrigger) form = name switch
                {
                    "叠土" => (TraitForm.Passive, TraitTrigger.OnDismantle),
                    "三土" => (TraitForm.Passive, TraitTrigger.OnCompose),
                    "粉身碎骨" => (TraitForm.Passive, TraitTrigger.OnKill),
                    _ => (slot == L4 || slot == L6 ? TraitForm.Passive : TraitForm.Active, TraitTrigger.Cast),
                };
                Assert.That((t.Form, t.Trigger), Is.EqualTo(form), $"{ch} {name} 形态");
                Assert.That(t.Replaces.HasValue, Is.False, $"{ch} {name} 不替换");
                Assert.That(t.MaxPerBattle, Is.EqualTo(name == "杜门不出" ? 1 : 0), $"{ch} {name} 每场次数(E35)");
                Assert.That(string.Join("|", t.Effects.Select(Sig)), Is.EqualTo(sigs), $"{ch} {name}({slot}/{face})效果");
            }
        }

        // ================= 折叠:同面多条 Reshape 的 strike 相加(E27)、改形修饰落到本体上 =================

        private static EffectDef Folded(string ch, CardFace face, int level, EffectKind kind) =>
            TraitRules.CastEffects(Graph.Get(ch), face, level).First(e => e.Kind == kind);

        [Test]
        public void Fold_StackedStrikesAdd_AndShieldModifiersLand()
        {
            // 塔 Lv8 攻:塔基 armorStrike 300 + 镇塔 500 = 800(E27,改前会被覆盖成 500)
            Assert.That(Folded("塔", CardFace.Attack, 8, EffectKind.DamageSingle).ArmorStrikePercent, Is.EqualTo(800));
            // 㙓 Lv8 攻:山压 50 + 地动 30 = 80,全体
            var dong = Folded("㙓", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((dong.ShieldStrikePercent, dong.Shape), Is.EqualTo((80, TargetArea.All)));
            // 垚 Lv8 攻:三山散射 3 发、逐发镇压 300 + spec A Lv6 垚峙 200 = 500;移山的 shieldStrike 30 留着(Q13)
            var shan = Folded("垚", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((shan.Shape, shan.Shots, shan.ArmorStrikePercent, shan.ArmorStrikeEach, shan.ShieldStrikePercent),
                Is.EqualTo((TargetArea.Scatter, 3, 500, true, 30)));
            // 崩 Lv8 攻:击数按出字前护盾,每 100 点 +1、最多 +4
            var lie = Folded("崩", CardFace.Attack, 8, EffectKind.DamageSingle);
            Assert.That((lie.ScaleBy, lie.ScaleDivisor, lie.ScaleCap, lie.Shape), Is.EqualTo((ScaleBasis.Shield, 100, 4, TargetArea.All)));
            // 塔 Lv4 固:本体护盾 + 护甲 × 300%(E30)
            Assert.That(Folded("塔", CardFace.Feature, 4, EffectKind.Shield).ArmorShieldPercent, Is.EqualTo(300));
            // 垒 Lv8 / 㙓 Lv8 固:回合脉冲落进出字效果(积土成山 3 回合 / 厚德载物本场);Lv7 没有
            Assert.That(Folded("垒", CardFace.Feature, 8, EffectKind.TurnPulse).Turns, Is.EqualTo(3));
            Assert.That(Folded("㙓", CardFace.Feature, 8, EffectKind.TurnPulse).Turns, Is.EqualTo(-1));
            Assert.That(TraitRules.CastEffects(Graph.Get("㙓"), CardFace.Feature, 7).Any(e => e.Kind == EffectKind.TurnPulse), Is.False,
                "对照:Lv7 没有厚德载物");
            // 塔 Lv8 固:七级浮屠的按护盾计层护甲跟在本体护甲之后
            Assert.That(TraitRules.CastEffects(Graph.Get("塔"), CardFace.Feature, 8).Count(e => e.Kind == EffectKind.DefenseBuff && e.PerShieldTenth),
                Is.EqualTo(1));
            // 圭 Lv6 固:第一条护甲本场持续(E34);㙓 Lv4 固:护甲 4 + 4 = 8 回合
            Assert.That(Folded("圭", CardFace.Feature, 6, EffectKind.DefenseBuff).Turns, Is.EqualTo(-1));
            Assert.That(Folded("㙓", CardFace.Feature, 4, EffectKind.DefenseBuff).Turns, Is.EqualTo(8));
        }

        // ================= 地裂:出字前护盾每 100 点 +1 击、总击数 ≤ 5,打完清空护盾(Q5) =================

        [TestCase(0, 1)]
        [TestCase(99, 1)]
        [TestCase(250, 3)]
        [TestCase(400, 5)]
        [TestCase(2000, 5)]
        public void DiLie_ExtraHitsFromPreCastShield_CappedAtFiveTotal(int shield, int hits)
        {
            var b = Battle("崩", 8, shield, Mob(), Mob());
            Assert.That(b.PlayerShield, Is.EqualTo(shield), "前提:开局护盾如夹具所设");
            Cast(b, "崩", 0, attack: true);
            for (int i = 0; i < 2; i++)
                Assert.That(Damage(b, EffectSource.None).Count(e => e.TargetIndex == i), Is.EqualTo(hits),
                    $"护盾 {shield}:#{i} 吃 {hits} 击(全体每一击都打到每个人)");
            Assert.That(b.PlayerShield, Is.EqualTo(0), "伤害之后清空护盾");
        }

        // ================= 塔倾:伤害 + 当前护盾 50%(扣盾前),之后护盾减半(Q10) =================

        [Test]
        public void TaQing_StrikeReadsFullShield_ThenHalves()
        {
            int Dealt(int shield, out BattleEngine b)
            {
                b = Battle("塔", 5, shield, Mob());
                Cast(b, "塔", 0, attack: true);
                return Hp - b.Enemies[0].Hp;
            }
            int bare = Dealt(0, out _);
            int full = Dealt(400, out var b);
            Assert.That(full - bare, Is.EqualTo(200), "400 × 50%,读的是扣盾前的护盾");
            Assert.That(b.PlayerShield, Is.EqualTo(200), "400 → 200");
            Assert.That(bare, Is.EqualTo(165), "钉值:spec A 本体 133(紫档单体)按 Lv5");
        }

        // ================= 积土成山:之后 3 个玩家回合开始各得护盾 16(按来源字等级缩放) =================

        [Test]
        public void JiTu_PulseShieldThreeTurnStarts_ScaledByLevel()
        {
            var b = Battle("垒", 8, Mob());
            Cast(b, "垒", -1, attack: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse)?.TurnsLeft, Is.EqualTo(3));
            for (int t = 1; t <= 3; t++)
            {
                b.EndTurn();
                Assert.That(PlayerShieldEvents(b), Is.EqualTo(new[] { MetaRules.ScaleEffectValue(EffectKind.Shield, 16, 8) }), $"第 {t} 个回合开始");
            }
            b.EndTurn();
            Assert.That(PlayerShieldEvents(b), Is.Empty, "三次之后到期");
        }

        // ================= 七级浮屠:出字结算完后,护盾每 10% 最大生命护甲 +8,最多 7 层,4 回合 =================

        [Test]
        public void QiJi_LayersFromShieldAfterCast()
        {
            int perLayer = MetaRules.ScaleEffectValue(EffectKind.DefenseBuff, 8, 8);
            List<int> Armors(BattleEngine x) =>
                x.PlayerStatuses.All.Where(s => s.Kind == StatusKind.DefenseBuff).Select(s => s.Magnitude).ToList();
            var bare = Battle("塔", 8, Mob());
            Cast(bare, "塔", -1, attack: false);
            Assert.That(bare.PlayerShield, Is.EqualTo(189), "钉值:塔 Lv8 固面护盾 spec A 133 按 Lv8(出字时护甲 0,塔基不加)");
            var body = Armors(bare);
            Assert.That(body.Count, Is.EqualTo(1), "189 < 500:0 层不挂,只剩本体(破甲·强化)那条护甲");

            var b = Battle("塔", 8, 2000, Mob());
            Cast(b, "塔", -1, attack: false);
            Assert.That(b.PlayerShield, Is.EqualTo(2189));
            Assert.That(Armors(b), Is.EqualTo(new[] { body[0], perLayer * 4 }), "2189 × 10 / 5000 = 4 层,排在本体护甲之后");
            Assert.That(b.PlayerStatuses.All.Last(s => s.Kind == StatusKind.DefenseBuff).TurnsLeft, Is.EqualTo(4));

            var full = Battle("塔", 8, MaxHp, Mob());
            Cast(full, "塔", -1, attack: false);
            Assert.That(Armors(full), Is.EqualTo(new[] { body[0], perLayer * 7 }), "护盾顶满(10 层)钳 7");
        }

        // ================= 厚德载物:本场每个玩家回合开始,存活木灵各得玩家当前护盾 10% =================

        [Test]
        public void HouDe_PulseLastsBattle_SpiritsGetTenthOfPlayerShield()
        {
            var b = new BattleEngine(Graph, Config, Enumerable.Repeat("㙓", 6).ToArray(), Array.Empty<string>(), new[] { Mob() }, seed: 1,
                cardLevels: new Dictionary<string, int> { ["㙓"] = 8 },
                startingSummons: new[]
                {
                    // MaxHp 给足 3000:木灵盾跨回合累加(2026-10-13 取消 U1),300 的上限会在出字(载物)时就钳满
                    new SummonSnapshot { Slot = 0, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = 3000, MaxHp = 3000,
                        Attack = 10, Speed = 100, Statuses = new List<StatusEffect>() },
                });
            Cast(b, "㙓", -1, attack: false);
            int playerShield = b.PlayerShield;
            Assert.That(playerShield, Is.GreaterThan(0), "前置:出字给了玩家护盾");
            var pulse = b.PlayerStatuses.Find(StatusKind.TurnPulse);
            Assert.That(pulse?.TurnsLeft, Is.EqualTo(-1), "本场持续");
            Assert.That((pulse.OnHit[0].Kind, pulse.OnHit[0].Value, pulse.OnHit[0].OfPlayerShield),
                Is.EqualTo((EffectKind.ShieldSummons, 10, true)));
            for (int t = 1; t <= 4; t++)
            {
                b.EndTurn();
                var spirit = b.LastEvents.Where(e => e.Kind == BattleEventKind.Shield && e.TargetIndex == 0).Select(e => e.Amount).ToList();
                Assert.That(b.PlayerShield, Is.EqualTo(playerShield), $"第 {t} 个回合开始:玩家护盾跨回合保留");
                Assert.That(spirit, Is.EqualTo(new[] { playerShield / 10 }), $"第 {t} 个回合开始:木灵得玩家当前护盾 10%");
            }
            Assert.That(b.PlayerStatuses.Find(StatusKind.TurnPulse)?.TurnsLeft, Is.EqualTo(-1));
        }

        // ================= 堡垒:之后 2 回合,每次受击后获得护盾(定死于出字时,Q7) =================

        [Test]
        public void BaoLei_EveryHitGrantsFixedShield_TwoRounds()
        {
            var b = Battle("堡", 8, Mob(attack: 30), Mob(attack: 30));
            Cast(b, "堡", -1, attack: false);
            int x = b.PlayerStatuses.Find(StatusKind.HurtShield).Magnitude;
            Assert.That(x, Is.EqualTo(63), "钉值:44 按 Lv8 × 攻击 100%");
            var perRound = new List<int[]>();
            for (int round = 0; round < 3; round++)
            {
                b.EndTurn();
                Assert.That(b.LastEvents.Count(e => e.Kind == BattleEventKind.EnemyAttack), Is.EqualTo(2), "两只都打中");
                perRound.Add(PlayerShieldEvents(b).ToArray());
            }
            Assert.That(perRound, Is.EqualTo(new[] { new[] { x, x }, new[] { x, x }, Array.Empty<int>() }), "每击都给,2 回合");
        }

        // ================= 碎玉:护盾被打破时反伤吸收量 × 50%,进 60% 反伤预算(镜在前,Q6) =================

        [Test]
        public void SuiYu_ShatterReflect_SharesSixtyPercentBudgetWithMirror()
        {
            BattleEngine Hit(bool mirror)
            {
                var b = Battle("碎", 5, Mob(attack: 300));
                Cast(b, "碎", -1, attack: false);
                Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.True, "出字给了盾 → 挂碎玉");
                if (mirror)
                    b.PlayerStatuses.Apply(new StatusEffect
                        { Kind = StatusKind.Reflect, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = 2, SourceId = "镜" });
                b.EndTurn();
                Assert.That(b.PlayerStatuses.Has(StatusKind.ShieldShatter), Is.False, "打破后移除");
                return b;
            }
            var alone = Hit(mirror: false);
            var hit = alone.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            int shatter = Damage(alone, EffectSource.ShieldShatter).Sum(e => e.Amount);
            Assert.That(shatter, Is.EqualTo(hit.Absorbed * 50 / 100), "单独:吸收量 × 50%(没顶到预算)");

            var both = Hit(mirror: true);
            var hit2 = both.LastEvents.Single(e => e.Kind == BattleEventKind.EnemyAttack);
            int reflect = Damage(both, EffectSource.Reflect).Sum(e => e.Amount);
            int shatter2 = Damage(both, EffectSource.ShieldShatter).Sum(e => e.Amount);
            Assert.That(reflect, Is.EqualTo(hit2.Amount * 40 / 100), "镜 40%");
            Assert.That(reflect + shatter2, Is.EqualTo(hit2.Amount * CombatCaps.ReflectPercent / 100), "合计钳 60%");
            Assert.That(shatter2, Is.LessThan(shatter), "碎玉只拿预算余量");
            // 挥击 300 过护甲 25 → 240;盾 129(spec A 碎 本体 90 按 Lv5)被打破;单独 64;有镜:镜 96 + 碎玉 48 = 144 = 240 × 60%
            Assert.That((hit.Amount, hit.Absorbed, shatter, reflect, shatter2), Is.EqualTo((240, 129, 64, 96, 48)), "钉值");
        }

        // ================= 粉身碎骨:致死一击的溢出平分给其余存活敌人(Q9) =================

        [Test]
        public void FenShenSuiGu_OverkillSplitsEvenly_EarthNoCrit()
        {
            var b = Battle("碎", 8, Mob(hp: 10), Mob(), Mob());
            Cast(b, "碎", 0, attack: true);
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(0));
            int blow = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage && e.TargetIndex == 0 && e.Source == EffectSource.None)
                .Sum(e => e.Amount - e.Absorbed);
            var splash = Damage(b, EffectSource.OverkillSplash);
            Assert.That(splash.Select(e => e.TargetIndex).OrderBy(i => i).ToList(), Is.EqualTo(new[] { 1, 2 }), "其余两只各一份");
            Assert.That(splash.Select(e => e.Amount).ToList(), Is.EqualTo(new[] { (blow - 10) / 2, (blow - 10) / 2 }));
            Assert.That(splash.All(e => !e.Crit), Is.True, "不暴击");
            Assert.That(blow, Is.EqualTo(128), "钉值:spec A 本体 90 按 Lv8;每份 (128 − 10) / 2 = 59");
        }

        // ================= 杜塞:杂兵封禁 2 回合;Boss 下一次大招伤害 −50%(Q3) =================

        private const int BossAttack = 40;

        private static EnemyDef DelugeBoss() => new("试炼", Element.Heart, Hp, BossAttack,
            phases: new[] { new BossPhaseDef("甲", Element.Heart, Hp, BossAttack, skill: BossSkill.Deluge) });

        [Test]
        public void DuSai_BossNextSkillHalved_MobSilenced()
        {
            int SkillDamage(bool cut)
            {
                var b = Battle("杜", 5, DelugeBoss(), Mob());
                b.EndTurn();   // 普攻
                b.EndTurn();   // 蓄力
                Assert.That(b.Enemies[0].IsCharging, Is.True, "前提:下一拍放大招");
                if (cut)
                {
                    Cast(b, "杜", 0, attack: true);
                    Assert.That(b.Enemies[0].Statuses.Find(StatusKind.SkillCut)?.Magnitude, Is.EqualTo(50));
                    Assert.That(b.Enemies[0].Statuses.Has(StatusKind.Silence), Is.False, "Boss 不吃封禁");
                }
                int hp = b.PlayerHp;
                b.EndTurn();
                Assert.That(b.LastEvents.Any(e => e.Kind == BattleEventKind.BossSkillCast), Is.True);
                Assert.That(b.Enemies[0].Statuses.Has(StatusKind.SkillCut), Is.False, "用掉即移除");
                return hp - b.PlayerHp;
            }
            Assert.That(SkillDamage(cut: false), Is.EqualTo(BossAttack * 2), "淹没:攻击 × 2");
            Assert.That(SkillDamage(cut: true), Is.EqualTo(BossAttack * 2 * 50 / 100));

            var m = Battle("杜", 5, DelugeBoss(), Mob());
            Cast(m, "杜", 1, attack: true);
            Assert.That(m.Enemies[1].Statuses.Find(StatusKind.Silence)?.TurnsLeft, Is.EqualTo(2), "杂兵封禁 2 回合");
            Assert.That(m.Enemies[1].Statuses.Has(StatusKind.SkillCut), Is.False);
        }

        // ================= 杜门不出:本回合减伤 60%、下回合 AP −1;整条特性每场 1 次(E35) =================

        [Test]
        public void DuMenBuChu_OncePerBattle_DamageCutAndApCost()
        {
            var b = Battle("杜", 8, Mob());
            Cast(b, "杜", -1, attack: false);
            Assert.That(b.PlayerStatuses.Find(StatusKind.DamageCut)?.Magnitude, Is.EqualTo(60));
            var cost = b.PlayerStatuses.Find(StatusKind.Seal);
            Assert.That((cost?.Polarity, cost?.Magnitude, cost?.TurnsLeft),
                Is.EqualTo(((StatusPolarity?)StatusPolarity.Buff, (int?)1, (int?)2)), "AP 代价不算减益");
            Cast(b, "杜", -1, attack: false);   // 同回合第二次:特性不再进 Fold(同源 Seal 原地刷新,TotalMagnitude 区分不了 —— 由下面第三次出字与 AP 恢复证明)
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(Config.ApPerTurn - 1), "下回合 AP −1");
            Assert.That(b.PlayerStatuses.Has(StatusKind.DamageCut), Is.False, "减伤只管本回合");
            Cast(b, "杜", -1, attack: false);
            Assert.That(b.PlayerStatuses.Has(StatusKind.DamageCut), Is.False, "第三次也只剩本体");
            b.EndTurn();
            Assert.That(b.Ap, Is.EqualTo(Config.ApPerTurn), "没有再扣 AP");
        }

        // ================= 圭臬:本字护甲持续到战斗结束,战后不携带(E34) =================

        [Test]
        public void GuiNie_ArmorLastsTheBattle_NotCarriedToNext()
        {
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 2).Select(_ => new[] { Mob(hp: 50) }).ToArray(),
                RewardPool = new[] { "圭" },
            };
            var run = new RunEngine(Graph, config, Config, Enumerable.Repeat("圭", 6).ToArray(), Array.Empty<string>(),
                seed: 1, cardLevels: new Dictionary<string, int> { ["圭"] = 6 });
            Assert.That(run.Battle.Cast("圭", -1), Is.EqualTo(BattleError.None));
            var armor = run.Battle.PlayerStatuses.All.Where(s => s.Kind == StatusKind.DefenseBuff).ToList();
            Assert.That(armor.Select(s => s.TurnsLeft).ToList(), Is.EqualTo(new[] { -1 }), "唯一那条护甲(Lv3 本体)改成本场");
            for (int t = 0; t < 5; t++) run.Battle.EndTurn();
            Assert.That(run.Battle.PlayerStatuses.Has(StatusKind.DefenseBuff), Is.True, "过了本来的 4 回合还在");

            Assert.That(run.Battle.Cast("圭", 0, attackMode: true), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
            while (run.Phase == RunPhase.Reward) run.SkipReward();
            Assert.That(run.CarriedStatuses.Any(s => s.Kind == StatusKind.DefenseBuff), Is.False, "战后不进携带");
            Assert.That(run.Battle.PlayerStatuses.Has(StatusKind.DefenseBuff), Is.False, "下一场开局没有");
        }

        // ================= 对峙:嘲讽本回合;之后 5 场开局护甲 +38(按卡等级,4 回合) =================

        [Test]
        public void DuiZhi_TauntNow_OpeningArmorForFiveBattles()
        {
            var config = new RunConfig
            {
                Encounters = Enumerable.Range(0, 7).Select(_ => new[] { Mob(hp: 50) }).ToArray(),
                RewardPool = new[] { "垚" },
            };
            var run = new RunEngine(Graph, config, Config, Enumerable.Repeat("垚", 12).ToArray(), Array.Empty<string>(),
                seed: 1, cardLevels: new Dictionary<string, int> { ["垚"] = 8 });
            Assert.That(run.Battle.Cast("垚", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.PlayerStatuses.Find(StatusKind.Taunt)?.TurnsLeft, Is.EqualTo(1), "嘲讽给玩家、本回合");
            var pending = run.Battle.PendingOpenings.Single();
            Assert.That((pending.Kind, pending.Value, pending.Turns, pending.BattlesLeft),
                Is.EqualTo((EffectKind.DefenseBuff, 38, 4, 5)));

            var openingArmor = new List<int>();
            for (int battle = 2; battle <= 7; battle++)
            {
                Assert.That(run.Battle.Cast("垚", 0, attackMode: true), Is.EqualTo(BattleError.None));
                Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
                run.AdvanceAfterBattle();
                while (run.Phase == RunPhase.Reward) run.SkipReward();
                var armor = run.Battle.PlayerStatuses.All.Where(s => s.Kind == StatusKind.DefenseBuff).ToList();
                // 开局效果在开场调度之前落地(spec v6 §5.1),第一个 BeginPlayerTurn 已递减一次:登记 4 回合 → 玩家第一回合剩 3
                Assert.That(armor.All(s => s.TurnsLeft == 3), Is.True, $"第 {battle} 场开局护甲(4 回合,第一回合剩 3)");
                openingArmor.Add(armor.Sum(s => s.Magnitude));
            }
            int x = openingArmor[0];
            Assert.That(x, Is.EqualTo(54), "钉值:38 按 Lv8 缩放");
            Assert.That(openingArmor, Is.EqualTo(new[] { x, x, x, x, x, 0 }), "第 2–6 场有,第 7 场没有");
        }

        // ================= 山岳:护甲 ≥ 50 时,每回合第一次受击使攻击者破甲(门槛不满足不扣次数,E32) =================

        [Test]
        public void ShanYue_ArmorGate_FirstHitEachRoundBreaksAttacker()
        {
            // 每只敌人那一拍收尾时读一次回敬剩余次数(_traitUsesThisTurn 在玩家回合开始才清,EndTurn 返回后已重置,只能在敌方段里读)
            List<bool> Broken(bool lowArmor, out BattleEngine b, out List<int?> charges)
            {
                b = Battle("垚", 6, Mob(attack: 10), Mob(attack: 10));
                Cast(b, "垚", -1, attack: false);
                Assert.That(b.EffectivePlayerDefense, Is.GreaterThanOrEqualTo(50), "前提:出字的护甲已过门槛");
                if (lowArmor)
                    b.PlayerStatuses.Apply(new StatusEffect
                        { Kind = StatusKind.ArmorBreak, Polarity = StatusPolarity.Debuff, Magnitude = 1000, TurnsLeft = 9, SourceId = "试" });
                var probe = new ChargesProbe();
                b.AddHookListener(probe);
                b.EndTurn();
                charges = probe.Seen;
                var e = b;
                return Enumerable.Range(0, 2).Select(i => e.Enemies[i].Statuses.Has(StatusKind.ArmorBreak)).ToList();
            }
            Assert.That(Broken(lowArmor: true, out var low, out var lowCharges), Is.EqualTo(new[] { false, false }), "护甲 0 < 50:不回敬");
            Assert.That(lowCharges, Is.EqualTo(new int?[] { 1, 1 }), "门槛不满足不扣次数:两只敌人打完仍剩 1 次");
            Assert.That(Broken(lowArmor: false, out var high, out var highCharges), Is.EqualTo(new[] { true, false }), "只有第一个打中的被破甲");
            Assert.That(highCharges, Is.EqualTo(new int?[] { null, null }), "对照:过门槛时第一击就用掉本回合唯一一次(用尽返回 null)");
            Assert.That(high.Enemies[0].Statuses.Find(StatusKind.ArmorBreak)?.Magnitude, Is.EqualTo(50), "钉值:破甲 38 按 Lv6");
        }

        // ================= spec A:Lv6 新专属冒烟(真实 graph + cardLevels 6)=================

        [Test]
        public void YaoZhi_Lv6_AttackAddsTwiceTheArmorToDamage()
        {
            // 垚 Lv6·攻 垚峙:`Reshape` `armorStrike 200` —— 攻击伤害 + 玩家护甲 × 2(spec A)
            int Dealt(int armor)
            {
                var b = Battle("垚", 6, 0, Mob());
                if (armor > 0)
                    b.PlayerStatuses.Apply(new StatusEffect
                        { Kind = StatusKind.DefenseBuff, Polarity = StatusPolarity.Buff, Magnitude = armor, TurnsLeft = 3 });
                Cast(b, "垚", 0, attack: true);
                return Hp - b.Enemies[0].Hp;
            }
            int bare = Dealt(0), armored = Dealt(50);
            Assert.That(armored - bare, Is.EqualTo(100).Within(2), "护甲 50 × 200% = +100");
        }
    }
}
