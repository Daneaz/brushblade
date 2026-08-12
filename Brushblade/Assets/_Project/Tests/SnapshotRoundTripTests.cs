using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>战斗内断点存档往返(2026-07-27):存档 → 读档 → 接着打,必须与「不中断打完」
    /// 逐字节一致。漏存任何一个可变字段都会在这里现形 —— 前提是那条路径被下面的用例走到,
    /// 所以每加一种敌人能力/效果,都要在这里补一条。</summary>
    public class SnapshotRoundTripTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire, effects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("土", Element.Earth, effects: new[] { new EffectDef(EffectKind.Shield, 3) },
                attackEffects: new[] { new EffectDef(EffectKind.DamageSingle, 4) }),
            new CharDef("水", Element.Water, effects: new[] { new EffectDef(EffectKind.HealSelf, 3) }),
            new CharDef("林", Element.Wood, new[] { "木", "木" },
                effects: new[] { new EffectDef(EffectKind.Summon, 6, summonCount: 2, summonAttack: 2, summonChar: "木") }),
            new CharDef("炎", Element.Fire, new[] { "火", "火" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6), new EffectDef(EffectKind.BurnSingle, 2) }),
            new CharDef("炽", Element.Fire, new[] { "火", "只" },
                effects: new[] { new EffectDef(EffectKind.BurnPotency, 1) }),
            new CharDef("只", null),
            new CharDef("堡", Element.Earth, new[] { "呆", "土" },
                effects: new[] { new EffectDef(EffectKind.Shield, 10, persistOnce: true) }),
            new CharDef("呆", null),
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DefenseBuff, 12) }),
            new CharDef("锯", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.Bleed, 3) }),
        });

        private static BattleConfig Config() => new()
        {
            DropTable = new[] { "木", "火", "土" },
            PlayerMaxHp = 60,
        };

        /// <summary>状态摘要:比对用。覆盖所有会影响后续对局的可变量。</summary>
        private static string Digest(BattleEngine b)
        {
            var sb = new StringBuilder();
            sb.Append($"hp{b.PlayerHp}|ap{b.Ap}|turn{b.Turn}|ph{b.Phase}")
              .Append($"|sn{b.ShieldNormal}|sp{b.ShieldPersist}")
              .Append($"|def{string.Join(",", b.PlayerStatuses.All.Where(s => s.Kind == StatusKind.DefenseBuff).OrderBy(s => s.SourceId).Select(s => $"{s.SourceId}{s.Magnitude}"))}")
              .Append($"|lib{string.Join(",", b.Library)}|pool{string.Join(",", b.Pool)}");
            foreach (var e in b.Enemies)
                sb.Append($"|E({e.Def.Id},{e.Hp}/{e.MaxHp},{e.Element},{e.ApparentElement},burn{e.Statuses.TotalMagnitude(StatusKind.Burn)}," +
                          $"atk{e.Attack},def{e.Defense},ab{e.Statuses.TotalMagnitude(StatusKind.ArmorBreak)},ph{e.PhaseIndex},rg{e.RegrowProgress}," +
                          $"sp{e.HasSplit},ht{e.HitsTaken})");
            foreach (var s in b.Summons)
                sb.Append($"|S({s.Char},{s.Element},{s.Hp}/{s.MaxHp},atk{s.Attack})");
            return sb.ToString();
        }

        private static string Digest(RunEngine r)
        {
            var sb = new StringBuilder();
            sb.Append($"ph{r.Phase}|bi{r.BattleIndex}|cbi{r.ClearedBattleIndex}")
              .Append($"|ink{r.EarnedInk}|avail{r.AvailableInk}")
              .Append($"|cl{string.Join(",", r.CarriedLibrary)}|cp{string.Join(",", r.CarriedPool)}")
              .Append($"|cns{r.CarriedNormalShield}|cps{r.CarriedPersistShield}")
              .Append($"|cdr{string.Join(",", r.CarriedStatuses.OrderBy(s => s.SourceId).Select(s => $"{s.SourceId}{s.Magnitude}"))}")
              .Append($"|cs{string.Join(",", r.CarriedSummons.Select(s => $"{s.Char}{s.Element}{s.Hp}/{s.MaxHp}atk{s.Attack}"))}")
              .Append($"|cpk{r.CharPicksLeft}")
              .Append($"|ro{string.Join(",", r.RewardOptions)}|co{string.Join(",", r.ComponentOptions)}")
              .Append($"|ev{r.CurrentEvent?.Id}|lx{r.LibraryExpanded}|px{r.PoolExpanded}|rv{r.Revived}")
              .Append($"|def{string.Join(",", r.DefeatedEnemyIds)}")
              .Append($"||{Digest(r.Battle)}");
            return sb.ToString();
        }

        private static IReadOnlyDictionary<string, EnemyDef> Defs(params EnemyDef[] defs)
        {
            var map = new Dictionary<string, EnemyDef>();
            foreach (var d in defs) map[d.Id] = d;
            return map;
        }

        private static BattleEngine Battle(EnemyDef[] enemies, string[] library, string[] pool = null, int seed = 42) =>
            new(Graph(), Config(), library, pool ?? Array.Empty<string>(), enemies, seed);

        /// <summary>存档→读档,返回复原出来的引擎。</summary>
        private static BattleEngine Reload(BattleEngine origin, params EnemyDef[] defs) =>
            BattleEngine.Restore(origin.Capture(), Graph(), Config(), null, Defs(defs));

        // ---- 战斗层 ----

        [Test]
        public void Battle_FreshState_RoundTrips()
        {
            var def = new EnemyDef("枯", Element.Wood, 40, 3);
            var origin = Battle(new[] { def }, new[] { "炎" });
            Assert.That(Digest(Reload(origin, def)), Is.EqualTo(Digest(origin)));
        }

        [Test]
        public void Battle_AfterActions_ContinuesIdentically()
        {
            var def = new EnemyDef("枯", Element.Wood, 60, 3);
            var a = Battle(new[] { def }, new[] { "炎", "林" }, new[] { "土" });
            a.Cast("炎", 0);
            a.Cast("土");
            var b = Reload(a, def);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)), "读档瞬间就该一致");

            foreach (var engine in new[] { a, b }) // 同样的后续动作
            {
                engine.Cast("林");
                engine.EndTurn();
                engine.EndTurn();
            }
            Assert.That(Digest(b), Is.EqualTo(Digest(a)), "接着打也要一致");
        }

        [Test]
        public void Battle_RandomStream_DoesNotFork() // 掉字靠 RNG(2026-08-04:池→库):存档后两边必须摇出同样的字
        {
            var def = new EnemyDef("枯", Element.Wood, 200, 1);
            var config = new BattleConfig
            {
                DropTable = new[] { "木", "火", "土" }, PlayerMaxHp = 60,
                LibraryCapacity = 20, DropsPerTurn = 1, UnlockedChars = new[] { "木", "火", "土" },
            };
            var a = new BattleEngine(Graph(), config, new[] { "炎" }, Array.Empty<string>(), new[] { def }, seed: 42);
            a.EndTurn();
            var b = BattleEngine.Restore(a.Capture(), Graph(), config, null, Defs(def));
            for (int i = 0; i < 6; i++) { a.EndTurn(); b.EndTurn(); }
            Assert.That(b.Library, Is.EqualTo(a.Library));
        }

        [Test]
        public void Battle_BurnPotency_Survives() // 炽抬高的灼烧系数不在任何显式属性上,最容易漏
        {
            var def = new EnemyDef("枯", Element.Wood, 200, 0);
            var a = Battle(new[] { def }, new[] { "炽", "炎" });
            a.Cast("炽");        // 每层灼烧 2 → 3
            a.Cast("炎", 0);     // 挂 2 层
            var b = Reload(a, def);
            a.EndTurn(); b.EndTurn();
            Assert.That(b.Enemies[0].Hp, Is.EqualTo(a.Enemies[0].Hp));
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_Shields_BothBucketsSurvive()
        {
            var def = new EnemyDef("枯", Element.Wood, 200, 5);
            var a = Battle(new[] { def }, new[] { "堡" }, new[] { "土" });
            a.Cast("堡");  // 豁免桶
            a.Cast("土");  // 普通桶
            var b = Reload(a, def);
            Assert.That(b.ShieldNormal, Is.EqualTo(a.ShieldNormal));
            Assert.That(b.ShieldPersist, Is.EqualTo(a.ShieldPersist));
            a.EndTurn(); b.EndTurn();
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_DefenseBuff_Survives() // 护甲增益来源(状态容器)存档往返
        {
            var def = new EnemyDef("枯", Element.Wood, 200, 5);
            var a = Battle(new[] { def }, new[] { "铠" });
            a.Cast("铠");
            var b = Reload(a, def);
            Assert.That(b.PlayerStatuses.Find(StatusKind.DefenseBuff).Magnitude, Is.EqualTo(12));
            Assert.That(b.EffectivePlayerDefense, Is.EqualTo(a.EffectivePlayerDefense));
            a.EndTurn(); b.EndTurn();
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_Summons_Survive()
        {
            var def = new EnemyDef("枯", Element.Wood, 200, 4);
            var a = Battle(new[] { def }, new[] { "林" });
            a.Cast("林");
            a.EndTurn();  // 召唤挨一下,血量不满
            var b = Reload(a, def);
            Assert.That(b.Summons.Count, Is.EqualTo(a.Summons.Count));
            a.EndTurn(); b.EndTurn();
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_ScorchStacking_Survives() // 焦痕加过的攻不能回退
        {
            var def = new EnemyDef("焦痕", Element.Fire, 200, 4, EnemyAbility.Scorch);
            var a = Battle(new[] { def }, new[] { "炎", "火" });
            a.Cast("炎", 0);
            a.Cast("火", 0);
            Assert.That(a.Enemies[0].Attack, Is.GreaterThan(4)); // 确实加过攻
            var b = Reload(a, def);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_RegrowProgress_Survives()
        {
            var def = new EnemyDef("缺笔妖", Element.Metal, 200, 3, EnemyAbility.Regrow);
            var a = Battle(new[] { def }, new[] { "炎" });
            a.EndTurn(); a.EndTurn();
            Assert.That(a.Enemies[0].RegrowProgress, Is.EqualTo(2));
            var b = Reload(a, def);
            a.EndTurn(); b.EndTurn(); // 第 3 次补全:攻×2、血回满
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_SplitClones_Survive() // 分裂出的克隆共用 Def,数量多于遭遇定义
        {
            var def = new EnemyDef("叠字怪", Element.Wood, 40, 3, EnemyAbility.Split);
            var a = Battle(new[] { def }, new[] { "火" }, new[] { "火" });
            a.Cast("火", 0); // 受击存活 → 分裂
            Assert.That(a.Enemies.Count, Is.EqualTo(2));
            var b = Reload(a, def);
            Assert.That(b.Enemies.Count, Is.EqualTo(2));
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_DisguisedElement_Survives() // 真身/伪装是进场摇的,重建会摇出别的
        {
            var def = new EnemyDef("通假字", Element.Wood, 200, 3, EnemyAbility.Disguise);
            var a = Battle(new[] { def }, new[] { "炎" });
            var b = Reload(a, def);
            Assert.That(b.Enemies[0].Element, Is.EqualTo(a.Enemies[0].Element));
            Assert.That(b.Enemies[0].ApparentElement, Is.EqualTo(a.Enemies[0].ApparentElement));
        }

        [Test]
        public void Battle_ObscureHitsTaken_Survives() // 受击计数决定第几击被读懂
        {
            var def = new EnemyDef("生僻字", Element.Earth, 200, 2, EnemyAbility.Obscure);
            var a = Battle(new[] { def }, new[] { "火" }, new[] { "火" });
            a.Cast("火", 0);
            Assert.That(a.Enemies[0].ApparentElement, Is.Null);
            var b = Reload(a, def);
            a.Cast("火", 0); b.Cast("火", 0); // 第二击应当同时现形
            Assert.That(b.Enemies[0].ApparentElement, Is.EqualTo(a.Enemies[0].ApparentElement));
            Assert.That(b.Enemies[0].ApparentElement, Is.Not.Null);
        }

        [Test]
        public void Battle_BossPhaseBounds_Survive() // 阈值带种子浮动,重算会得到另一套
        {
            var boss = new EnemyDef("排山倒海", Element.Water, 12, 6, phases: new[]
            {
                new BossPhaseDef("排", Element.Metal, 12, 6),
                new BossPhaseDef("山", Element.Earth, 15, 4, defense: 60),
                new BossPhaseDef("倒", Element.Wood, 12, 8),
            });
            var a = Battle(new[] { boss }, new[] { "炎" }, seed: 5);
            var b = Reload(a, boss);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
            a.Cast("炎", 0); b.Cast("炎", 0);
            a.EndTurn(); b.EndTurn();
            Assert.That(b.Enemies[0].PhaseIndex, Is.EqualTo(a.Enemies[0].PhaseIndex));
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Battle_DeadEnemies_Survive() // 尸体留在列表里,下标不能塌陷
        {
            var weak = new EnemyDef("枯", Element.Wood, 4, 1);
            var tough = new EnemyDef("锈", Element.Metal, 200, 1);
            var a = Battle(new[] { weak, tough }, new[] { "火" }, new[] { "火" });
            a.Cast("火", 0); // 打死 0 号
            Assert.That(a.Enemies[0].Alive, Is.False);
            var b = Reload(a, weak, tough);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void DropChoice_SurvivesRoundTrip() // 阶段与待决议字丢了 = 续爬后掉落凭空消失
        {
            var graph = Graph();
            var enemyDef = new EnemyDef("枯", Element.Wood, 10, 2);
            var engine = new BattleEngine(graph,
                new BattleConfig { LibraryCapacity = 2, DropsPerTurn = 1, UnlockedChars = new[] { "林" } },
                new[] { "炎", "炎" }, Array.Empty<string>(), new[] { enemyDef }, 42);
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.DropChoice)); // 前置:确实卡在决议

            var restored = BattleEngine.Restore(engine.Capture(), graph,
                new BattleConfig { LibraryCapacity = 2, DropsPerTurn = 1, UnlockedChars = new[] { "林" } },
                null, new Dictionary<string, EnemyDef> { ["枯"] = enemyDef });

            Assert.That(restored.Phase, Is.EqualTo(BattlePhase.DropChoice));
            Assert.That(restored.PendingDrop, Is.EqualTo("林"));
        }

        [Test]
        public void EnemyStatuses_SurviveRoundTrip_AndAreNotShared()
        {
            var graph = Graph();
            var enemyDef = new EnemyDef("桩", Element.Metal, 500, 0);
            var engine = new BattleEngine(graph, Config(), new[] { "锯" },
                Array.Empty<string>(), new[] { enemyDef, enemyDef }, 42);
            engine.Cast("锯", 0); // 只给 0 号上流血

            var snapshot = engine.Capture();
            var restored = BattleEngine.Restore(snapshot, graph, Config(), null,
                new Dictionary<string, EnemyDef> { ["桩"] = enemyDef });

            // 归属正确:状态没有串到别的敌人身上(与是否深拷贝无关,单独成立)
            Assert.That(restored.Enemies[0].Statuses.Has(StatusKind.Bleed), Is.True);
            Assert.That(restored.Enemies[1].Statuses.Has(StatusKind.Bleed), Is.False); // 没被别名共享

            // 深拷贝护栏(review 2026-08-04):归属正确不等于没共享引用。下面两段任一处
            // 退化成浅拷贝(Clone() 换成直接赋值)都会让断言失败。

            // Capture() 的深拷贝:快照拍下之后,原 engine 继续推进不该污染已拍下的快照
            engine.EndTurn(); // 原 engine 里的流血回合数递减
            var snapshotBleed = snapshot.Enemies[0].Statuses.Find(s => s.Kind == StatusKind.Bleed);
            Assert.That(snapshotBleed.TurnsLeft, Is.EqualTo(3),
                "Capture() 必须深拷贝:原 engine 继续推进不该改到已拍下的快照");

            // Restore() 的深拷贝:同一份快照复原两次,两份实例不该共享同一条状态对象
            var restoredAgain = BattleEngine.Restore(snapshot, graph, Config(), null,
                new Dictionary<string, EnemyDef> { ["桩"] = enemyDef });
            restored.Enemies[0].Statuses.Find(StatusKind.Bleed).Magnitude = 99;
            Assert.That(restoredAgain.Enemies[0].Statuses.Find(StatusKind.Bleed).Magnitude, Is.EqualTo(3),
                "Restore() 必须深拷贝:两份复原实例不该共享同一条状态对象");
        }

        // ---- run 层 ----

        private static RunConfig TwoBattles(params EnemyDef[] defs) => new()
        {
            Encounters = new[] { new[] { defs[0] }, new[] { defs.Length > 1 ? defs[1] : defs[0] } },
            RewardPool = new[] { "炎", "林", "堡" },
        };

        private static RunEngine Run(RunConfig config, int seed = 3) =>
            new(Graph(), config, Config(), new[] { "炎", "炎" }, new[] { "火" }, seed,
                startingInk: 50, perFloorNormalShield: 2);

        private static RunEngine Reload(RunEngine origin, RunConfig config) =>
            RunEngine.Restore(origin.Capture(), Graph(), config, Config(), null,
                startingInk: 50, perFloorNormalShield: 2);

        [Test]
        public void Run_MidBattle_RoundTrips()
        {
            var config = TwoBattles(new EnemyDef("枯", Element.Wood, 60, 3));
            var a = Run(config);
            a.Battle.Cast("炎", 0);
            var b = Reload(a, config);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));

            foreach (var r in new[] { a, b }) { r.Battle.EndTurn(); r.Battle.Cast("炎", 0); }
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Run_RewardPhase_RoundTrips() // 停在战利品页挂起:候选与额度都要原样回来
        {
            var config = TwoBattles(new EnemyDef("枯", Element.Wood, 4, 1));
            var a = Run(config);
            a.Battle.Cast("炎", 0);
            a.AdvanceAfterBattle();
            Assert.That(a.Phase, Is.EqualTo(RunPhase.Reward));

            var b = Reload(a, config);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
            Assert.That(b.RewardOptions, Is.EqualTo(a.RewardOptions));

            a.SkipReward(); b.SkipReward();
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Run_ClearedIndexAndDefeated_Survive()
        {
            var config = TwoBattles(new EnemyDef("枯", Element.Wood, 4, 1));
            var a = Run(config);
            a.Battle.Cast("炎", 0);
            a.AdvanceAfterBattle();
            a.SkipReward();

            var b = Reload(a, config);
            Assert.That(b.ClearedBattleIndex, Is.EqualTo(a.ClearedBattleIndex));
            Assert.That(b.BattleIndex, Is.EqualTo(a.BattleIndex));
            Assert.That(b.DefeatedEnemyIds, Is.EqualTo(a.DefeatedEnemyIds));
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void Run_Expansions_Survive()
        {
            var config = TwoBattles(new EnemyDef("枯", Element.Wood, 60, 1));
            var a = Run(config);
            a.TryExpandLibrary();
            a.TryExpandPool();
            var b = Reload(a, config);
            Assert.That(b.LibraryExpanded, Is.True);
            Assert.That(b.PoolExpanded, Is.True);
            Assert.That(b.Battle.LibraryCapacity, Is.EqualTo(a.Battle.LibraryCapacity));
            Assert.That(b.Battle.PoolCapacity, Is.EqualTo(a.Battle.PoolCapacity));
        }

        [Test]
        public void InProgress_SurvivesRealSaveFile() // 整份存档走 SaveSerializer 往返
        {
            var config = TwoBattles(new EnemyDef("枯", Element.Wood, 60, 3));
            var origin = Run(config);
            origin.Battle.Cast("炎", 0);

            var meta = new MetaState
            {
                Endless = new EndlessSaveState
                {
                    Depth = 3,
                    Seed = 999,
                    InProgress = new InProgressRun
                    {
                        FromDepth = 1,
                        FirstTowerSegment = true,
                        CommittedEventInk = 17,
                        Run = origin.Capture(),
                    },
                },
            };

            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var resume = reloaded.Endless.InProgress;
            Assert.That(resume, Is.Not.Null);
            Assert.That(resume.FromDepth, Is.EqualTo(1));          // 段起点不能丢:靠它重建本段
            Assert.That(resume.FirstTowerSegment, Is.True);
            Assert.That(resume.CommittedEventInk, Is.EqualTo(17)); // 丢了会把字摊净额重复入账

            var restored = RunEngine.Restore(resume.Run, Graph(), config, Config(), null,
                startingInk: 50, perFloorNormalShield: 2);
            Assert.That(Digest(restored), Is.EqualTo(Digest(origin)), "读档瞬间就该一致");

            foreach (var r in new[] { origin, restored }) { r.Battle.EndTurn(); r.Battle.Cast("炎", 0); }
            Assert.That(Digest(restored), Is.EqualTo(Digest(origin)), "接着打也要一致");
        }

        [Test]
        public void CarriedSummons_RoundTrip_AcrossFloorBreak() // 层间携带的召唤物必须原样回来
        {
            var def = new EnemyDef("枯", Element.Wood, 8, 3);
            var config = TwoBattles(def);
            var a = new RunEngine(Graph(), config, Config(), new[] { "林", "炎" }, new[] { "火" }, 3,
                startingInk: 50, perFloorNormalShield: 2);
            a.Battle.Cast("林");        // 召 2 只 6 血木偶
            a.Battle.EndTurn();         // 召唤物回合末反击,随后敌方整次攻击落在首只召唤物身上:残血原样带走的前提
            a.Battle.Cast("炎", 0);     // 收尾:火对木中立无加成,但敌方已被召唤物打残,基础伤害足以终结
            Assert.That(a.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            a.AdvanceAfterBattle();     // 进战利品页,携带态已含 2 只召唤物
            Assert.That(a.CarriedSummons.Count, Is.EqualTo(2));

            var b = Reload(a, config);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
            Assert.That(b.CarriedSummons.Count, Is.EqualTo(2));
            Assert.That(b.CarriedSummons[0].Char, Is.EqualTo("木"));
            Assert.That(b.CarriedSummons[0].Attack, Is.EqualTo(2));
            Assert.That(b.CarriedSummons[0].Hp, Is.GreaterThan(0), "首只召唤物应挨过打但活着");
            Assert.That(b.CarriedSummons[0].Hp, Is.LessThan(b.CarriedSummons[0].MaxHp), "残血原样带走,不回满");
            Assert.That(b.CarriedSummons[1].Hp, Is.EqualTo(b.CarriedSummons[1].MaxHp), "整次攻击只由首只承受,不溢出");

            foreach (var r in new[] { a, b }) r.SkipReward(); // 读档接着打:召唤物照样上场
            Assert.That(b.Battle.AliveSummonCount, Is.EqualTo(2));
            Assert.That(b.Battle.Summons[0].Hp, Is.EqualTo(3), "残血原样入场,不回满");
            Assert.That(b.Battle.Summons[0].MaxHp, Is.EqualTo(6), "MaxHp 与 Hp 脱钩");
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void CarriedDefenseBuffs_RoundTrip_AcrossFloorBreak() // 护甲增益跨战斗结转:段内持久
        {
            var def = new EnemyDef("枯", Element.Wood, 4, 1);
            var config = TwoBattles(def);
            var a = new RunEngine(Graph(), config, Config(), new[] { "铠", "炎" }, new[] { "火" }, 3,
                startingInk: 50, perFloorNormalShield: 2);
            a.Battle.Cast("铠");
            a.Battle.Cast("炎", 0); // 一发清场
            Assert.That(a.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            a.AdvanceAfterBattle();     // 打完第一场:携带态已含护甲增益来源
            Assert.That(a.CarriedStatuses.First(s => s.SourceId == "铠").Magnitude, Is.EqualTo(12));

            var b = Reload(a, config);
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
            Assert.That(b.CarriedStatuses.First(s => s.SourceId == "铠").Magnitude, Is.EqualTo(12));

            foreach (var r in new[] { a, b }) r.SkipReward(); // 进入第二场
            Assert.That(a.Battle.EffectivePlayerDefense, Is.EqualTo(12), "跨战斗仍在生效");
            Assert.That(b.Battle.EffectivePlayerDefense, Is.EqualTo(12));
            Assert.That(Digest(b), Is.EqualTo(Digest(a)));
        }

        [Test]
        public void CarriedSummons_SurviveRealSaveFile() // 跨段:整份存档走 SaveSerializer 往返
        {
            var meta = new MetaState
            {
                Endless = new EndlessSaveState
                {
                    Depth = 6,
                    Seed = 999,
                    CarriedSummons = new List<SummonSnapshot>
                    {
                        new() { Char = "木", Element = Element.Wood, Hp = 3, MaxHp = 6, Attack = 2 },
                    },
                },
            };

            var reloaded = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var carried = reloaded.Endless.CarriedSummons;
            Assert.That(carried.Count, Is.EqualTo(1));
            Assert.That(carried[0].Char, Is.EqualTo("木"));
            Assert.That(carried[0].Element, Is.EqualTo(Element.Wood));
            Assert.That(carried[0].Hp, Is.EqualTo(3));      // 残血跨段延续
            Assert.That(carried[0].MaxHp, Is.EqualTo(6));
            Assert.That(carried[0].Attack, Is.EqualTo(2));
        }

        [Test]
        public void LegacySave_WithoutCarriedSummons_LoadsEmpty() // 旧档没这字段:读出空场,不崩
        {
            var meta = Data.SaveSerializer.FromJson("{\"Endless\":{\"Depth\":3,\"Seed\":999,\"NormalShield\":5}}");
            Assert.That(meta.Endless, Is.Not.Null);
            Assert.That(meta.Endless.Depth, Is.EqualTo(3));
            Assert.That(meta.Endless.NormalShield, Is.EqualTo(5));
            Assert.That(meta.Endless.CarriedSummons, Is.Not.Null);
            Assert.That(meta.Endless.CarriedSummons, Is.Empty);
        }
    }
}
