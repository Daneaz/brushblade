using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 1 第一步:改热路径之前先钉住「没有新状态时逐位恒等」。
    /// 要守的改动:
    /// <list type="bullet">
    /// <item>木灵掉血唯一入口 LoseSummonHp 改返回值,三个调用点(挨打 / 灼烧 / 守护)之后追加 EndureSaved —— 只在保命触发时发;</item>
    /// <item>预估试算加的 <c>_estimate</c> 分支(RollCrit / DrainReactions / DamageEnemy 生克取元素),本体恒 false;</item>
    /// <item>ApplyEffects 的效果表抽成 EffectsForCast(限次特性筛法 + 印记并入)。</item>
    /// </list>
    /// 指纹:出字 / 拆字返回值与事件(种类 / 下标 / 量 / 关联下标 / 吸收 / 来源 / 暴击)、每拍事件、玩家血盾、
    /// 木灵血盾状态、敌人血与状态、库 / 池、三条 RandomState。期望值在改动前(base f7d6cc5b)实跑得到。</summary>
    public class E4CoreIdentityTests
    {
        private static void AppendEvents(StringBuilder sb, BattleEngine b)
        {
            foreach (var e in b.LastEvents.Where(x => x.Kind != BattleEventKind.StatusApplied))   // 上状态事件不进恒等指纹(spec 2026-10-18 §1)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Source}:{e.Crit}:{e.Ke};");
        }

        private static void AppendState(StringBuilder sb, BattleEngine b)
        {
            sb.Append($"|P{b.PlayerHp}/S{b.ShieldNormal}+0/A{b.Ap}[");
            foreach (var s in b.PlayerStatuses.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft} ");
            sb.Append(']');
            for (int i = 0; i < b.Summons.Count; i++)
            {
                var s = b.Summons[i];
                if (s == null) continue;
                sb.Append($",W{i}:{s.Hp}/{s.MaxHp}/{s.Shield}[");
                foreach (var st in s.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft} ");
                sb.Append(']');
            }
            foreach (var en in b.Enemies)
            {
                sb.Append($",E{en.Hp}/{en.Shield}[");
                foreach (var st in en.Statuses.All) sb.Append($"{st.Kind}:{st.Magnitude}:{st.TurnsLeft} ");
                sb.Append(']');
            }
            sb.Append("|L").Append(string.Join("", b.Library)).Append("|Pool").Append(string.Join("", b.Pool));
            var snap = b.Capture();
            sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
        }

        private static string Hash(string raw)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16") + " " + raw.Length;
        }

        private static void Cast(StringBuilder sb, BattleEngine b, string id, int target, bool attack = false)
        {
            sb.Append($"#{id}{target}{(attack ? "A" : "")}={b.Cast(id, target, attackMode: attack)};");
            AppendEvents(sb, b);
            AppendState(sb, b);
        }

        private static void End(StringBuilder sb, BattleEngine b)
        {
            if (b.Phase != BattlePhase.PlayerTurn) return;
            b.EndTurn();
            sb.Append("|T");
            AppendEvents(sb, b);
            AppendState(sb, b);
        }

        // ---------------- 木灵掉血两路(挨打 / 灼烧;守护一路已随 2026-10-17 守护改磐固删除),无保命 ----------------

        private static readonly CharDef Hit = RebalanceFixture.Char("击", new EffectDef(EffectKind.DamageSingle, 30));

        private static SummonSnapshot Spirit(int slot, int hp, int maxHp, SummonPassive passive = null, int burn = 0) =>
            new SummonSnapshot
            {
                Slot = slot, Char = "木", SourceChar = "林", Element = Element.Wood, Hp = hp, MaxHp = maxHp,
                Attack = 10, Speed = 100, Passive = passive,
                Statuses = burn > 0
                    ? new List<StatusEffect>
                        { new StatusEffect { Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff, Magnitude = burn, TurnsLeft = -1 } }
                    : new List<StatusEffect>(),
            };

        [Test]
        public void SummonHpLoss_ThreePaths_NoEndure_Identical()
        {
            var b = new BattleEngine(RebalanceFixture.Graph(Hit),
                new BattleConfig { PlayerMaxHp = 3000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60, BossPhaseJitterPercent = 0 },
                Enumerable.Repeat("击", 30).ToArray(), Array.Empty<string>(),
                new[]
                {
                    RebalanceFixture.Mob(attack: 160),
                    new EnemyDef("远", Element.Fire, 100000, 120, range: AttackRange.Ranged, focus: AttackFocus.Player),
                    new EnemyDef("壮", Element.Metal, 100000, 200),
                },
                seed: 53, startingHp: 2000,
                startingSummons: new[]
                {
                    Spirit(0, 330, 330),
                    Spirit(3, 400, 400),
                    Spirit(4, 90, 300, burn: 3),
                    Spirit(5, 70, 200, burn: 5),
                });
            var sb = new StringBuilder();
            AppendState(sb, b);
            for (int t = 0; t < 6 && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                Cast(sb, b, "击", t % 3);
                End(sb, b);
            }
            Assert.That(Hash(sb.ToString()), Is.EqualTo(SummonHpLossExpected), sb.ToString());
        }

        // 2026-10-18 战场改 3×2(敌每排 3 列、召唤槽 6、前排 3 格)后重录,原值 "0d3b452a6d57732a 4967"
        private const string SummonHpLossExpected = "5cc4a1d91a95a61a 4967";   // 2026-10-17:3 号木灵去掉守护(守护改磐固)后重录

        // ---------------- 出字热路径:暴击 / 反应 / 限次特性 / 印记 ----------------

        private static TraitDef OnKillSplash() => new(TraitSlot.Lv1, TraitFace.Both, TraitForm.Passive, null, "迎刃",
            new[] { new EffectDef(EffectKind.DamageSingle, 15, shape: TargetArea.All) }, TraitTrigger.OnKill);

        private static TraitDef Shut() => new(TraitSlot.Lv1, TraitFace.Both, TraitForm.Active, null, "闭门",
            new[] { new EffectDef(EffectKind.DamageCut, 60) }, maxPerBattle: 1);

        private static TraitDef Glyph(string partChar, int partCount, params EffectDef[] effects) =>
            new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "字形", effects, TraitTrigger.OnDismantle, partChar, partCount);

        private static CharDef Part(string id, params EffectDef[] effects) =>
            new(id, Element.Heart, effects: effects, isComponent: true);

        internal static readonly CharDef Blade = new("刃", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.Shield, 20) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 60) },
            traits: new[] { OnKillSplash() });
        internal static readonly CharDef Door = new("杜", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 40) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 20) },
            traits: new[] { Shut() });
        internal static readonly CharDef Fire = Part("火", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BurnSingle, 1));
        internal static readonly CharDef Mountain = Part("山", new EffectDef(EffectKind.DamageSingle, 10));
        internal static readonly CharDef Yan = new("炎", Element.Fire, recipe: new[] { "火", "火" },
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: new[] { Glyph("火", 2, new EffectDef(EffectKind.BurnSingle, 2)) });
        internal static readonly CharDef Can = new("灿", Element.Fire, recipe: new[] { "火", "山" },
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: new[] { Glyph("山", 1, new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All), new EffectDef(EffectKind.BurnAll, 1)) });

        [Test]
        public void CastHotPath_CritReactionLimitMark_Identical()
        {
            var defs = new[] { Hit, Blade, Door, Fire, Mountain, Yan, Can };
            var library = new[] { Blade, Door, Hit }.SelectMany(d => Enumerable.Repeat(d.Id, 8))
                .Concat(new[] { Yan, Can }.SelectMany(d => Enumerable.Repeat(d.Id, 3))).ToArray();
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig
                {
                    PlayerMaxHp = 5000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60, PoolCapacity = 20,
                    PlayerCritChance = 50, MoraleOnCrit = 1, BossPhaseJitterPercent = 0,
                },
                library, Array.Empty<string>(),
                new[]
                {
                    RebalanceFixture.Mob(hp: 150, attack: 30),
                    new EnemyDef("木妖", Element.Wood, 400, 20),
                    new EnemyDef("火妖", Element.Fire, 100000, 25, defense: 30),
                    RebalanceFixture.Mob(hp: 90, attack: 10),
                },
                seed: 71, cardLevels: new Dictionary<string, int> { ["炎"] = 4, ["灿"] = 4 });
            var sb = new StringBuilder();
            AppendState(sb, b);
            for (int t = 0; t < 5 && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                if (t == 0)
                {
                    sb.Append($"-炎={b.Dismantle("炎")};");
                    AppendState(sb, b);
                    Cast(sb, b, "火", 2);
                    Cast(sb, b, "火", 1);
                }
                if (t == 1)
                {
                    sb.Append($"-灿={b.Dismantle("灿")};");
                    AppendState(sb, b);
                    Cast(sb, b, "山", 2);
                }
                Cast(sb, b, "刃", t % 4, attack: true);
                Cast(sb, b, "杜", -1);
                Cast(sb, b, "刃", (t + 1) % 4, attack: true);
                End(sb, b);
            }
            Assert.That(Hash(sb.ToString()), Is.EqualTo(CastHotPathExpected), sb.ToString());
        }

        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "fecc792f7c174146 7006")。
        private const string CastHotPathExpected = "3236e9fbe28965b2 7011";
    }
}
