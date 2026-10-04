using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>反应队列(Plan A R8):监听器只入队;ApplyEffects 不可重入;安全点排空;死循环保险。</summary>
    public class ReactionQueueTests
    {
        private static readonly CharDef Hit = new("击", Element.Heart,
            effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) });

        private static BattleEngine Battle(params string[] library) =>
            RebalanceFixture.Battle(RebalanceFixture.Graph(Hit), library, RebalanceFixture.Mob(attack: 1));

        /// <summary>收到 EnemyHit 时入队一条「再打 10」的反应。</summary>
        private sealed class EchoOnHit : IBattleHookListener
        {
            public int Seen;
            public void OnHook(BattleEngine b, in HookArgs a)
            {
                if (a.Kind != HookKind.EnemyHit || a.Depth > 0) return;
                Seen++;
                b.Enqueue(new BattleEngine.Reaction("回", Element.Heart,
                    new[] { new EffectDef(EffectKind.DamageSingle, 10) }, a.Subject.Index, a.Depth + 1));
            }
        }

        [Test]
        public void ListenerEnqueues_DrainedAtEndOfCast()
        {
            var b = Battle("击");
            var echo = new EchoOnHit();
            b.AddHookListener(echo);
            int hp = b.Enemies[0].Hp;
            b.Cast("击", 0);
            Assert.That(echo.Seen, Is.EqualTo(1), "深度 1 的回响不再触发回响");
            Assert.That(b.PendingReactionCount, Is.EqualTo(0), "出字结束时队列已排空");
            Assert.That(b.Enemies[0].Hp, Is.LessThan(hp - 100), "回响那一下也打出去了");
        }

        private sealed class SyncReentrant : IBattleHookListener
        {
            public Exception Caught;
            public void OnHook(BattleEngine b, in HookArgs a)
            {
                if (a.Kind != HookKind.EnemyHit) return;
                try { b.ApplyDetachedEffects("坏", Element.Heart, new[] { new EffectDef(EffectKind.DamageSingle, 1) }, 0); }
                catch (InvalidOperationException e) { Caught = e; }
            }
        }

        [Test]
        public void SynchronousResolveInsideCast_Throws()
        {
            var b = Battle("击");
            var bad = new SyncReentrant();
            b.AddHookListener(bad);
            b.Cast("击", 0);
            Assert.That(bad.Caught, Is.Not.Null, "出字途中同步结算必须响亮失败");
        }

        private sealed class Forever : IBattleHookListener
        {
            public void OnHook(BattleEngine b, in HookArgs a)
            {
                if (a.Kind != HookKind.EnemyHit) return;
                b.Enqueue(new BattleEngine.Reaction("环", Element.Heart,
                    new[] { new EffectDef(EffectKind.DamageSingle, 1) }, a.Subject.Index, a.Depth + 1));
            }
        }

        [Test]
        public void RunawayReactions_HitTheCap()
        {
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(Hit), new[] { "击" },
                RebalanceFixture.Mob(attack: 1, hp: 1_000_000));
            b.AddHookListener(new Forever());
            Assert.Throws<InvalidOperationException>(() => b.Cast("击", 0));
        }

        [Test]
        public void NoListeners_TraceIdentical_QueueUntouched()
        {
            var b = Battle("击");
            b.Cast("击", 0);
            Assert.That(b.PendingReactionCount, Is.EqualTo(0));
        }

        // ── 嵌套恢复(Plan C 交接项 2):出字中入队,排空后外层的砺刃 / 锋芒不被打乱 ──

        /// <summary>只在第一次深度 0 的 EnemyHit 时入队一条心属性反应(不吃砺刃、不暴击)。</summary>
        private sealed class OnceOnFirstHit : IBattleHookListener
        {
            private bool _done;
            public int PendingDuringCast = -1;
            public void OnHook(BattleEngine b, in HookArgs a)
            {
                if (a.Kind != HookKind.EnemyHit || a.Depth > 0) return;
                if (_done) { PendingDuringCast = b.PendingReactionCount; return; }
                _done = true;
                b.Enqueue(new BattleEngine.Reaction("回", Element.Heart,
                    new[] { new EffectDef(EffectKind.DamageSingle, 10) }, a.Subject.Index, a.Depth + 1));
            }
        }

        [Test]
        public void ReactionDuringMetalCast_OuterCritBonusAndMoraleIntact()
        {
            var blade = new CharDef("锋", Element.Metal, effects: new[]
            {
                new EffectDef(EffectKind.DamageSingle, 10),
                new EffectDef(EffectKind.DamageSingle, 10),
            });
            var b = new BattleEngine(new RecipeGraph(new[] { blade }),
                new BattleConfig
                {
                    PlayerMaxHp = 500, PlayerAttack = 100,
                    PlayerCritChance = 0, MetalCritChance = 100, MoraleOnCrit = 1,
                },
                new[] { "锋" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob() }, seed: 1);
            var l = new OnceOnFirstHit();
            b.AddHookListener(l);

            Assert.That(b.Cast("锋", 0), Is.EqualTo(BattleError.None));

            Assert.That(l.PendingDuringCast, Is.EqualTo(1), "第二记出手时反应还在队里,没被同步结算");
            var hits = b.LastEvents.Where(e => e.Kind == BattleEventKind.Damage).ToList();
            Assert.That(hits.Count, Is.EqualTo(3), "两记本体 + 一记反应");
            Assert.That(hits[0].Crit && hits[1].Crit, Is.True, "砺刃在外层两记上都生效");
            Assert.That(hits[2].Crit, Is.False, "反应排在最后,心属性不吃砺刃");
            Assert.That(b.PlayerStatuses.TotalMagnitude(StatusKind.Morale), Is.EqualTo(1), "锋芒只兑现一次");
        }

        // ── DOT 致死补 TurnEnded ──

        private sealed class Recorder : IBattleHookListener
        {
            public readonly List<HookArgs> Log = new();
            public void OnHook(BattleEngine battle, in HookArgs args) => Log.Add(args);
        }

        [Test]
        public void EnemyBurnedToDeath_StillRaisesTurnEnded()
        {
            var burner = RebalanceFixture.Char("燃", new EffectDef(EffectKind.BurnSingle, 5));
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(burner), new[] { "燃" }, RebalanceFixture.Mob(hp: 30));
            var r = new Recorder();
            b.AddHookListener(r);
            b.Cast("燃", 0);
            b.EndTurn();   // 敌人那一拍开头结算灼烧:5 层 × 20 = 100 ≥ 30
            Assert.That(b.Enemies[0].Alive, Is.False, "前提:烧死了");
            Assert.That(r.Log.Count(a => a.Kind == HookKind.TurnStarted && a.Subject == UnitRef.Enemy(0)), Is.EqualTo(1));
            Assert.That(r.Log.Count(a => a.Kind == HookKind.TurnEnded && a.Subject == UnitRef.Enemy(0)), Is.EqualTo(1));
        }

        // ── HookArgs.Absorbed ──

        [Test]
        public void PlayerHit_CarriesShieldAbsorbed()
        {
            var b = new BattleEngine(RebalanceFixture.Graph(), new BattleConfig { PlayerMaxHp = 500, PlayerAttack = 100 },
                new[] { "甲" }, Array.Empty<string>(), new[] { RebalanceFixture.Mob(attack: 80) }, seed: 1,
                startingNormalShield: 50);
            Assert.That(b.ShieldNormal, Is.EqualTo(50), "前提:开场敌人还没出手");
            var r = new Recorder();
            b.AddHookListener(r);
            b.EndTurn();
            var hit = r.Log.First(a => a.Kind == HookKind.PlayerHit);
            Assert.That(hit.Amount, Is.EqualTo(80));
            Assert.That(hit.Absorbed, Is.EqualTo(50));
        }

        // ── CastCharId:出字期间的 EnemyHit / EnemyKilled 带上正在出的字 ──

        [Test]
        public void EnemyHitAndKilled_CarryCastCharId()
        {
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(), new[] { "甲" }, RebalanceFixture.Mob(hp: 20));
            var r = new Recorder();
            b.AddHookListener(r);
            b.Cast("甲", 0);
            Assert.That(r.Log.Single(a => a.Kind == HookKind.EnemyHit).CastCharId, Is.EqualTo("甲"));
            Assert.That(r.Log.Single(a => a.Kind == HookKind.EnemyKilled).CastCharId, Is.EqualTo("甲"));
        }

        [Test]
        public void EnemyKilledByBurn_HasNoCastCharId()
        {
            var burner = RebalanceFixture.Char("燃", new EffectDef(EffectKind.BurnSingle, 5));
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(burner), new[] { "燃" }, RebalanceFixture.Mob(hp: 30));
            var r = new Recorder();
            b.AddHookListener(r);
            b.Cast("燃", 0);
            b.EndTurn();
            Assert.That(r.Log.Single(a => a.Kind == HookKind.EnemyKilled).CastCharId, Is.Null);
        }

        // ── 钩子口径:AddPlayerCounter 增量补发 / GainStacks 只在涨层时发 ──

        [Test]
        public void MoraleIncrement_RaisesStatusAppliedWithTotal()
        {
            var war = RebalanceFixture.Char("战", new EffectDef(EffectKind.Morale, 1));
            var b = RebalanceFixture.Battle(RebalanceFixture.Graph(war), new[] { "战", "战" }, RebalanceFixture.Mob());
            var r = new Recorder();
            b.AddHookListener(r);
            b.Cast("战");
            b.Cast("战");
            var morale = r.Log.Where(a => a.Kind == HookKind.StatusApplied && a.Status == StatusKind.Morale)
                .Select(a => a.Amount).ToList();
            Assert.That(morale, Is.EqualTo(new[] { 1, 2 }), "第二次是增量,也要发,Amount = 总量");
        }

        [Test]
        public void HeftRemainderOnly_RaisesNothing()
        {
            var b = Battle("击");
            var r = new Recorder();
            b.AddHookListener(r);
            b.GainHeftForTest(BattleEngine.ResourceThresholdValue / 2);   // 余数没攒够一层
            Assert.That(r.Log.Any(a => a.Kind == HookKind.StatusApplied), Is.False);
            b.GainHeftForTest(BattleEngine.ResourceThresholdValue / 2);   // 攒满一层
            Assert.That(r.Log.Single(a => a.Kind == HookKind.StatusApplied).Amount, Is.EqualTo(1));
        }
    }
}
