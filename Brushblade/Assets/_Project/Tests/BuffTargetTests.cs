using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>增益改单体、可加给召唤物(2026-08-28 用户拍板)。与护盾/单体治疗
    /// (spec §8.1、ShieldTargetTests)共用同一套 NeedsAllyTarget / CanHealSlot / allySlot
    /// 流程,不写第二份。
    ///
    /// **本文件目前只覆盖第一批两条**:净化(澡/浴)与免疫(杜)。它们是唯一「挂上就真生效」
    /// 的两条 —— StatusBag 本来就是通用容器,而免疫只需在 DamageSummon 加一支拦截。
    /// 攻击/暴击/穿透(战/锋/锐)与护甲/反弹(铠/壁)要先在召唤物侧建结算链路,分批做;
    /// 在那之前它们**刻意不进 NeedsAllyTarget** —— 让玩家把铠加给召唤物、状态挂上去却
    /// 没人读,比不让加更糟。
    ///
    /// 玩家专属的四条不在此列,别顺手加进来:战意(Morale,连续出字的节奏奖励,召唤物不由
    /// 玩家逐张出字驱动)、利(ApBoost,AP 是玩家资源)、燥(BurnPotency,召唤物不施加灼烧)、
    /// 淋(HealAll,群体治疗本来就覆盖全场)。</summary>
    public class BuffTargetTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 兵:普通召唤物,当收 buff 方
            new CharDef("兵", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            // 浴:纯净化(真实字表的 浴 还带 Revive,那一支与选目标无关)
            new CharDef("浴", Element.Water,
                effects: new[] { new EffectDef(EffectKind.Cleanse, 0) }),
            // 杜:免疫 2 次(真实字表的 杜 还带 DamageSingle,那张要先选敌人再选友方)
            new CharDef("杜", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Immunity, 2) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies = null, int seed = 1) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 500, ApPerTurn = 9 },
                library, Array.Empty<string>(),
                enemies ?? new[] { new EnemyDef("靶", Element.Heart, 3000, 0) }, seed);

        /// <summary>往状态袋里塞一条减益,供净化去清。走 Apply 而不是直接改字段 ——
        /// 与真实施加路径同一个入口。</summary>
        private static void AddDebuff(StatusBag bag, StatusKind kind, int magnitude) =>
            bag.Apply(new StatusEffect
            {
                Kind = kind, Polarity = StatusPolarity.Debuff,
                Magnitude = magnitude, TurnsLeft = -1, SourceId = "测试",
            });

        // ---- 接线 ----

        [Test]
        public void NeedsAllyTarget_TrueForCleanseAndImmunity()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("浴")), Is.True, "净化要选给谁净");
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("杜")), Is.True, "免疫要选给谁挂");
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("兵")), Is.False, "召唤字不选友方");
        }

        // ---- 净化(Cleanse) ----

        [Test]
        public void Cleanse_DefaultsToPlayer()
        {
            // 不传 allySlot 时口径与改前逐位相同 —— 既有测试靠这条不变
            var engine = Engine(new[] { "浴" });
            AddDebuff(engine.PlayerStatuses, StatusKind.Curse, 30);

            Assert.That(engine.Cast("浴"), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerStatuses.Find(StatusKind.Curse), Is.Null);
        }

        [Test]
        public void Cleanse_OnSummon_ClearsThatSummonOnly()
        {
            // 净化点在召唤物身上 → 清它的减益,**玩家自己的一条不动**。
            // 这条是「改单体」的核心:改前无论点谁,清的都是玩家。
            var engine = Engine(new[] { "兵", "浴" });
            engine.Cast("兵");
            AddDebuff(engine.Summons[0].Statuses, StatusKind.Burn, 3);
            AddDebuff(engine.PlayerStatuses, StatusKind.Curse, 30);

            Assert.That(engine.Cast("浴", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0].Statuses.Find(StatusKind.Burn), Is.Null, "召唤物的灼烧该被清掉");
            Assert.That(engine.PlayerStatuses.Find(StatusKind.Curse), Is.Not.Null,
                "点的是召唤物,玩家身上那条不该跟着清");
        }

        [Test]
        public void Cleanse_OnSummon_KeepsItsBuffs()
        {
            // 净化只清减益。召唤物的护盾是字段不是状态,所以这里用状态袋里的增益来断
            var engine = Engine(new[] { "兵", "浴" });
            engine.Cast("兵");
            engine.Summons[0].Statuses.Apply(new StatusEffect
            {
                Kind = StatusKind.Immunity, Polarity = StatusPolarity.Buff,
                Magnitude = 1, TurnsLeft = -1, SourceId = "测试",
            });
            AddDebuff(engine.Summons[0].Statuses, StatusKind.Burn, 3);

            engine.Cast("浴", allySlot: 0);

            Assert.That(engine.Summons[0].Statuses.Find(StatusKind.Burn), Is.Null);
            Assert.That(engine.Summons[0].Statuses.Find(StatusKind.Immunity), Is.Not.Null, "增益不该被净化掉");
        }

        // ---- 免疫(Immunity) ----

        [Test]
        public void Immunity_DefaultsToPlayer()
        {
            var engine = Engine(new[] { "杜" });
            Assert.That(engine.Cast("杜"), Is.EqualTo(BattleError.None));

            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(2));
        }

        [Test]
        public void Immunity_OnSummon_LandsOnThatSummon()
        {
            var engine = Engine(new[] { "兵", "杜" });
            engine.Cast("兵");

            Assert.That(engine.Cast("杜", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(2));
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(0),
                "挂给召唤物就不该同时挂在玩家身上");
        }

        [Test]
        public void Immunity_OnSummon_BlocksTheHitEntirely()
        {
            // 端到端:免疫挂在召唤物身上,它挨打时整记被挡下(不是减免)。
            // 30 攻的敌人 + 100 血的召唤物,挡下则血量分毫不动
            var engine = Engine(new[] { "兵", "杜" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("杜", allySlot: 0);
            int hpBefore = engine.Summons[0].Hp;

            engine.EndTurn();

            Assert.That(engine.Summons[0].Hp, Is.EqualTo(hpBefore), "免疫该把整记挡下");
            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1),
                "挡一记扣一层");
        }

        [Test]
        public void Immunity_OnSummon_DoesNotSpendPlayersOwnLayers()
        {
            // 两边各有免疫时互不挪用:打召唤物只扣召唤物的
            var engine = Engine(new[] { "兵", "杜", "杜" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("杜");                  // 给玩家
            engine.Cast("杜", allySlot: 0);     // 给召唤物

            engine.EndTurn();

            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(2),
                "挨打的是召唤物,玩家的层数一层都不该少");
            Assert.That(engine.Summons[0].Statuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(1));
        }

        [Test]
        public void Immunity_BlockEmitsEvent()
        {
            // 表现层靠 ImmunityBlocked 飘「免」字。TargetIndex 是攻击者敌人下标(与玩家侧同口径),
            // SecondIndex 给出被保护的槽位 —— 玩家侧那条是 −1,飘字才知道该飘在谁头上
            var engine = Engine(new[] { "兵", "杜" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("杜", allySlot: 0);

            engine.EndTurn();

            var blocked = engine.LastEvents.Where(e => e.Kind == BattleEventKind.ImmunityBlocked).ToList();
            Assert.That(blocked.Count, Is.EqualTo(1));
            Assert.That(blocked[0].SecondIndex, Is.EqualTo(0), "被保护的是槽 0 的召唤物");
        }

        // ---- 与既有校验共用同一条口径 ----

        [Test]
        public void Buff_RejectsCorpseSlot()
        {
            var engine = Engine(new[] { "兵", "杜" });
            engine.Cast("兵");
            engine.Summons[0].Hp = 0;

            Assert.That(engine.Cast("杜", allySlot: 0), Is.EqualTo(BattleError.InvalidTarget));
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(0),
                "拒出就不该扣字扣 AP,更不该挂到玩家身上");
        }

        [Test]
        public void Buff_AutoLocksToPlayerWhenNoSummonAlive()
        {
            var engine = Engine(new[] { "杜" });
            Assert.That(engine.Cast("杜", allySlot: 0), Is.EqualTo(BattleError.None));
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.Immunity), Is.EqualTo(2));
        }
    }
}
