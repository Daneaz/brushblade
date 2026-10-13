using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E3 Task 1 第一步:改热路径之前先钉住「没有新状态时逐位恒等」。
    /// 要守的改动:ApplyStatus 拦截段加 DebuffWarded / BuffBlocked 事件、CastBossSkill 给杜塞打 Source 标签、
    /// 浮屠 Shield 打 Source 标签、CastCore 的 needsTarget 改调 CastEffectsNow、Dismantle / Compose 进门清事件 + 发 GlyphTrait。
    ///
    /// 场景:标点小妖 / 焦痕 / 灯花 / 倾覆 Boss 四种 ApplyStatus 来源;炎(双焰印记)/ 灿(火山印记)Lv4 拆字后从池里出火 / 山
    /// (CastCore 带印记与不带印记两支);林(无特性)拆了再合。拆合之后**不读** LastEvents(那正是本次要改的行为),
    /// 只记返回值、AP、库 / 池;成功出字与 EndTurn 后读事件(种类 / 下标 / 量 / 吸收 / 来源)。
    /// 另记钩子序列(StatusApplied / Composed / Dismantled)、敌我状态袋、血量护盾、三条随机流。
    /// 期望值是改动前(HEAD e1334ea5)跑出来的。</summary>
    public class E3CoreIdentityTests
    {
        private static readonly CharDef Striker = new("击", Element.Metal,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) });
        private static readonly CharDef Sprout = new("苗", Element.Wood,
            effects: new[] { new EffectDef(EffectKind.Summon, 1000, summonCount: 1, summonAttack: 0) });
        private static readonly CharDef Guard = new("护", Element.Earth,
            effects: new[] { new EffectDef(EffectKind.Shield, 60) });

        private static CharDef Part(string id, params EffectDef[] effects) =>
            new(id, Element.Heart, effects: effects, isComponent: true);

        private static readonly CharDef Fire = Part("火", new EffectDef(EffectKind.DamageSingle, 10), new EffectDef(EffectKind.BurnSingle, 1));
        private static readonly CharDef Mountain = Part("山", new EffectDef(EffectKind.DamageSingle, 10));
        private static readonly CharDef Wood = Part("木", new EffectDef(EffectKind.Shield, 5));

        private static TraitDef Glyph(string partChar, int partCount, params EffectDef[] effects) =>
            new(TraitSlot.Lv4, TraitFace.Both, TraitForm.Passive, null, "字形", effects, TraitTrigger.OnDismantle, partChar, partCount);

        private static CharDef Twin(string id, string a, string b, TraitDef trait) => new(id, Element.Heart,
            recipe: new[] { a, b },
            effects: new[] { new EffectDef(EffectKind.Shield, 10) },
            attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 10) },
            traits: trait == null ? null : new[] { trait });

        private static readonly CharDef Yan = Twin("炎", "火", "火", Glyph("火", 2, new EffectDef(EffectKind.BurnSingle, 2)));
        private static readonly CharDef Can = Twin("灿", "火", "山", Glyph("山", 1,
            new EffectDef(EffectKind.Reshape, 0, shape: TargetArea.All), new EffectDef(EffectKind.BurnAll, 1)));
        private static readonly CharDef Lin = Twin("林", "木", "木", null);

        private sealed class HookLog : IBattleHookListener
        {
            public readonly StringBuilder Sb = new StringBuilder();
            public void OnHook(BattleEngine battle, in HookArgs args)
            {
                if (args.Kind == HookKind.StatusApplied)
                    Sb.Append($"A{args.Subject.Side}{args.Subject.Index}<{args.Other.Side}{args.Other.Index}:{args.Status}:{args.Amount};");
                else if (args.Kind == HookKind.Composed || args.Kind == HookKind.Dismantled)
                    Sb.Append($"{args.Kind}:{args.CharId};");
            }
        }

        private static EnemyDef Toppler() =>
            new("覆", Element.Heart, 200000, 30, EnemyAbility.None,
                phases: new[] { new BossPhaseDef("覆", Element.Heart, 200000, 30, skill: BossSkill.Topple) });

        private static BattleEngine Battle(HookLog log)
        {
            var defs = new[] { Striker, Sprout, Guard, Fire, Mountain, Wood, Yan, Can, Lin };
            var library = new[] { Striker, Sprout, Guard }.SelectMany(d => Enumerable.Repeat(d.Id, 12))
                .Concat(new[] { Yan, Can, Lin }.SelectMany(d => Enumerable.Repeat(d.Id, 4))).ToArray();
            var b = new BattleEngine(RebalanceFixture.Graph(defs),
                new BattleConfig { PlayerMaxHp = 50000, PlayerAttack = 100, ApPerTurn = 60, LibraryCapacity = 60, PoolCapacity = 20 },
                library, Array.Empty<string>(),
                new[]
                {
                    RebalanceFixture.Mob(attack: 20, ability: EnemyAbility.Buff),
                    RebalanceFixture.Mob(attack: 15, ability: EnemyAbility.Scorch),
                    RebalanceFixture.Mob(attack: 10, ability: EnemyAbility.Sear),
                    Toppler(),
                },
                seed: 31,
                cardLevels: new Dictionary<string, int> { ["炎"] = 4, ["灿"] = 4, ["林"] = 4 });
            b.PlayerStatuses.Apply(new StatusEffect
                { Kind = StatusKind.CritBuff, Polarity = StatusPolarity.Buff, Magnitude = 40, TurnsLeft = -1 });
            b.AddHookListener(log);
            return b;
        }

        private static void Events(StringBuilder sb, BattleEngine b)
        {
            foreach (var e in b.LastEvents)
                sb.Append($"{e.Kind}:{e.TargetIndex}:{e.Amount}:{e.SecondIndex}:{e.Absorbed}:{e.Crit}:{e.Source};");
        }

        private static void Forge(StringBuilder sb, BattleEngine b) =>
            sb.Append($"<{b.Ap}|{string.Join("", b.Library)}|{string.Join("", b.Pool)}>");

        private static void Cast(StringBuilder sb, BattleEngine b, string id, int target)
        {
            var r = b.Cast(id, target);
            sb.Append($"#{id}{target}={r}");
            Forge(sb, b);
            // 失败的出字不清事件(LastEvents 残留上一次动作的),而拆合清事件正是本次要改的行为 —— 只在成功时读
            if (r == BattleError.None) Events(sb, b);
        }

        private static void Dismantle(StringBuilder sb, BattleEngine b, string id)
        {
            sb.Append($"-{id}={b.Dismantle(id)}");
            Forge(sb, b);
        }

        private static void Compose(StringBuilder sb, BattleEngine b, string id)
        {
            sb.Append($"+{id}={b.Compose(id)}");
            Forge(sb, b);
        }

        private static void Bag(StringBuilder sb, StatusBag bag)
        {
            sb.Append('[');
            foreach (var s in bag.All) sb.Append($"{s.Kind}:{s.Magnitude}:{s.TurnsLeft}:{s.Polarity} ");
            sb.Append(']');
        }

        [Test]
        public void ApplyStatus_CastCore_Forge_NoNewState_Identical()
        {
            var log = new HookLog();
            var b = Battle(log);
            var sb = new StringBuilder();
            for (int t = 0; t < 10 && b.Phase == BattlePhase.PlayerTurn; t++)
            {
                if (t == 0) Cast(sb, b, "苗", -1);
                Cast(sb, b, "击", 1);                   // 焦痕受击 → 自燃加攻
                if (t % 2 == 0)
                {
                    Dismantle(sb, b, "炎");             // 本场第一次:挂双焰印记(火 ×2)
                    Cast(sb, b, "火", 0);               // 带印记
                    Cast(sb, b, "火", 2);               // 带印记(或用完)
                }
                else
                {
                    Dismantle(sb, b, "灿");             // 本场第一次:挂火山印记(山 ×1)
                    Cast(sb, b, "山", -1);              // 带印记 → 全体、不选目标;用完后多敌 → InvalidTarget
                    Cast(sb, b, "山", 1);
                    Cast(sb, b, "火", -1);              // 无印记:多敌未选目标 → InvalidTarget
                    Cast(sb, b, "火", 3);
                }
                Dismantle(sb, b, "林");
                Compose(sb, b, "林");                   // 无特性拆合
                Compose(sb, b, "炎");                   // 池里没有两个火时 ForgeFailed
                if (t % 3 == 1) Cast(sb, b, "护", -1);
                sb.Append("|E");
                b.EndTurn();
                Events(sb, b);
                sb.Append("|H").Append(log.Sb);
                log.Sb.Clear();
                sb.Append($"|P{b.PlayerHp}/{b.ShieldNormal}/0");
                Bag(sb, b.PlayerStatuses);
                foreach (var s in b.Summons)
                    if (s != null) { sb.Append($",S{s.Hp}/{s.Shield}"); Bag(sb, s.Statuses); }
                for (int i = 0; i < b.Enemies.Count; i++)
                {
                    var en = b.Enemies[i];
                    sb.Append($",E{en.Hp}/{en.ActionMeter}");
                    Bag(sb, en.Statuses);
                }
                var snap = b.Capture();
                sb.Append($"|R{snap.RandomState}/{snap.TargetRandomState}/{snap.TraitRandomState}\n");
            }
            string raw = sb.ToString();
            // 防空转:每条要守的路径都真走到了
            Assert.That(raw.Contains("AEnemy1<Enemy1:AttackBuff"), Is.True, "焦痕自燃");
            Assert.That(raw.Contains("APlayer-1<Enemy2:Burn"), Is.True, "灯花给玩家上灼");
            Assert.That(raw.Contains("ASummon0<Enemy2:Burn"), Is.True, "灯花给木灵上灼");
            Assert.That(raw.Contains(":Seal:"), Is.True, "倾覆封字");
            Assert.That(raw.Contains("BossSkillCast"), Is.True, "Boss 放招");
            Assert.That(raw.Contains("-炎=None"), Is.True, "拆炎");
            Assert.That(raw.Contains("-灿=None"), Is.True, "拆灿");
            Assert.That(raw.Contains("#山-1=None"), Is.True, "带印记的山不选目标");
            Assert.That(raw.Contains("#山-1=InvalidTarget") || raw.Contains("#火-1=InvalidTarget"), Is.True, "无印记多敌要选目标");
            Assert.That(raw.Contains("+林=None"), Is.True, "合林");
            Assert.That(raw.Contains("Composed:林;") && raw.Contains("Dismantled:炎;"), Is.True, "拆合钩子");
            ulong h = 14695981039346656037UL;
            foreach (char c in raw) { h ^= c; h *= 1099511628211UL; }
            string fp = h.ToString("x16") + " " + raw.Length;
            Assert.That(fp, Is.EqualTo(Expected), fp + "\n" + raw);
        }

        // 2026-10-13(护盾回合保留 Task 4):删回合清盾(U1),护盾跨回合保留,指纹重取(原值 "9f05f8a305403059 20753")。
        private const string Expected = "fc9f5d75dc38a392 20757";
    }
}
