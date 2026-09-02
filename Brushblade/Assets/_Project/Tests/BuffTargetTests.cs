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
            // 攻 20 的召唤物:20 这个数便于逐位核对(+50 → 70,×1.5 → 30,减甲 8 → 12)
            new CharDef("卒", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 100, summonCount: 1, summonAttack: 20, summonChar: "木") }),
            // 战:攻击 +50 点(真实字表的 战 还带 DamageSingle)
            new CharDef("战", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.Empower, 50) }),
            // 锋:暴击 +100 个百分点 = **必暴**。刻意取 100 而不是真实字表的值 ——
            // RollCrit 在 ≥100 时短路不摇骰,断言因此不依赖种子
            new CharDef("锋", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.CritBuff, 100) }),
            // 锐:穿透 +8 点
            new CharDef("锐", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.PierceBuff, 8) }),
            // 铠:护甲 +8 点
            new CharDef("铠", Element.Metal,
                effects: new[] { new EffectDef(EffectKind.DefenseBuff, 8) }),
            // 壁:反弹 50%(真实字表的 壁 还带 Shield,盾会把伤害吃掉、看不出反弹,所以这里只留反弹)
            new CharDef("壁", Element.Earth,
                effects: new[] { new EffectDef(EffectKind.Reflect, 50, turns: 3) }),
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

        // ---- 第二批:攻击侧(战 Empower / 锋 CritBuff / 锐 PierceBuff) ----
        //
        // 观测手法一律是「敌人掉了多少血」:玩家这几张字本身不带伤害,所以敌人的血只可能
        // 是召唤物打掉的。敌人取心属性(对木系召唤物 1.0x),数值因此逐位可核。

        /// <summary>让召唤物打满一整拍,返回敌人掉的血。</summary>
        private static int SummonDamageInOneTurn(BattleEngine engine)
        {
            int before = engine.Enemies[0].Hp;
            engine.EndTurn();
            return before - engine.Enemies[0].Hp;
        }

        [Test]
        public void NeedsAllyTarget_TrueForAttackSideBuffs()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("战")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("锋")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("锐")), Is.True);
        }

        [Test]
        public void Empower_DefaultsToPlayer()
        {
            // 不传 allySlot 时口径与改前逐位相同
            var engine = Engine(new[] { "战" });
            engine.Cast("战");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.AttackBuff), Is.EqualTo(50));
        }

        [Test]
        public void Empower_OnSummon_RaisesThatSummonsDamage()
        {
            var plain = Engine(new[] { "卒" });
            plain.Cast("卒");
            int baseline = SummonDamageInOneTurn(plain);
            Assert.That(baseline, Is.EqualTo(20), "夹具基线:攻 20、无甲敌人、无生克");

            var engine = Engine(new[] { "卒", "战" });
            engine.Cast("卒");
            engine.Cast("战", allySlot: 0);
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(70), "20 + 50 点");
        }

        [Test]
        public void Empower_OnPlayer_LeavesSummonDamageAlone()
        {
            // 玩家身上的攻击增益**不该**漏到召唤物身上 —— 召唤物读自己的袋子
            var engine = Engine(new[] { "卒", "战" });
            engine.Cast("卒");
            engine.Cast("战");   // 给玩家
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(20));
        }

        [Test]
        public void CritBuff_OnSummon_MakesItCrit()
        {
            var engine = Engine(new[] { "卒", "锋" });
            engine.Cast("卒");
            engine.Cast("锋", allySlot: 0);
            // 20 × 150% = 30。暴击在护甲之前,无甲敌人这里看不出顺序,顺序另有玩家侧测试守
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(30));
        }

        [Test]
        public void CritBuff_OnPlayer_DoesNotMakeSummonCrit()
        {
            var engine = Engine(new[] { "卒", "锋" });
            engine.Cast("卒");
            engine.Cast("锋");   // 给玩家
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(20), "玩家必暴不等于召唤物必暴");
        }

        [Test]
        public void NoCritBuff_SummonDamageIsDeterministic()
        {
            // 恒等性硬线的可观测面:零层时 RollCrit 短路、一次随机都不摇,所以召唤物的伤害
            // 每拍都是同一个数。真正的保证来自既有那千余条依赖种子的测试仍全绿 ——
            // 这里只钉住「没有随机波动」这个能直接断言的部分。
            var engine = Engine(new[] { "卒" },
                new[] { new EnemyDef("靶", Element.Heart, 100000, 0) });
            engine.Cast("卒");
            for (int turn = 0; turn < 5; turn++)
                Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(20), $"第 {turn + 1} 拍");
        }

        [Test]
        public void PierceBuff_OnSummon_IgnoresEnemyDefense()
        {
            var armored = new[] { new EnemyDef("甲", Element.Heart, 3000, 0, defense: 8) };

            var plain = Engine(new[] { "卒" }, armored);
            plain.Cast("卒");
            Assert.That(SummonDamageInOneTurn(plain), Is.EqualTo(12), "夹具基线:20 − 8 点甲");

            var engine = Engine(new[] { "卒", "锐" }, armored);
            engine.Cast("卒");
            engine.Cast("锐", allySlot: 0);
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(20), "穿透 8 正好抵掉 8 点甲");
        }

        [Test]
        public void PierceBuff_OnPlayer_DoesNotHelpSummon()
        {
            var engine = Engine(new[] { "卒", "锐" },
                new[] { new EnemyDef("甲", Element.Heart, 3000, 0, defense: 8) });
            engine.Cast("卒");
            engine.Cast("锐");   // 给玩家
            Assert.That(SummonDamageInOneTurn(engine), Is.EqualTo(12),
                "玩家的穿透不该帮召唤物破甲");
        }

        // ---- 第三批:防御侧(铠 DefenseBuff / 壁 Reflect) ----

        [Test]
        public void NeedsAllyTarget_TrueForDefenseSideBuffs()
        {
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("铠")), Is.True);
            Assert.That(BattleEngine.NeedsAllyTarget(Graph().Get("壁")), Is.True);
        }

        [Test]
        public void DefenseBuff_DefaultsToPlayer()
        {
            var engine = Engine(new[] { "铠" });
            engine.Cast("铠");
            Assert.That(engine.PlayerStatuses.TotalMagnitude(StatusKind.DefenseBuff), Is.EqualTo(8));
        }

        [Test]
        public void DefenseBuff_OnSummon_CutsIncomingDamage()
        {
            // 30 攻的敌人打 100 血的召唤物:无甲掉 30,挂 8 点甲掉 22
            var puncher = new[] { new EnemyDef("拳", Element.Heart, 3000, 30) };

            var plain = Engine(new[] { "兵" }, puncher);
            plain.Cast("兵");
            int hp0 = plain.Summons[0].Hp;
            plain.EndTurn();
            Assert.That(hp0 - plain.Summons[0].Hp, Is.EqualTo(30), "夹具基线:无甲吃满 30");

            var engine = Engine(new[] { "兵", "铠" }, puncher);
            engine.Cast("兵");
            engine.Cast("铠", allySlot: 0);
            int hp1 = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(hp1 - engine.Summons[0].Hp, Is.EqualTo(22), "30 − 8 点甲");
        }

        [Test]
        public void DefenseBuff_OnPlayer_DoesNotProtectSummon()
        {
            var engine = Engine(new[] { "兵", "铠" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("铠");   // 给玩家
            int hp = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(hp - engine.Summons[0].Hp, Is.EqualTo(30), "玩家的甲不该替召唤物挡");
        }

        [Test]
        public void DefenseBuff_OnSummon_CannotPushDamageBelowZero()
        {
            // 甲厚过攻击力时下钳 0,不给召唤物回血
            var engine = Engine(new[] { "兵", "铠", "铠", "铠", "铠" },
                new[] { new EnemyDef("轻", Element.Heart, 3000, 5) });
            engine.Cast("兵");
            for (int n = 0; n < 4; n++) engine.Cast("铠", allySlot: 0);  // 同字按 SourceId 只刷新
            int hp = engine.Summons[0].Hp;
            engine.EndTurn();
            Assert.That(engine.Summons[0].Hp, Is.EqualTo(hp), "5 攻打不穿 8 甲,血量分毫不动");
        }

        [Test]
        public void Reflect_OnSummon_BouncesBackToAttacker()
        {
            // 30 攻打召唤物,50% 反弹 → 敌人吃 15。反弹不吃敌人护甲、不走生克
            var engine = Engine(new[] { "兵", "壁" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("壁", allySlot: 0);
            int enemyHp = engine.Enemies[0].Hp;

            engine.EndTurn();

            // 召唤物攻 3(兵),自己也会打敌人一下 —— 所以扣掉那 3
            Assert.That(enemyHp - engine.Enemies[0].Hp, Is.EqualTo(15 + 3),
                "反弹 15 + 召唤物自己那记 3");
        }

        [Test]
        public void Reflect_OnPlayerAndSummon_BothBounce()
        {
            // 玩家身上的反弹在召唤物顶前排时本来就会结算(2026-08-08,镜 × 召唤物)。
            // 召唤物自己也挂一份时**两份都反** —— 它们是两个不同来源
            var engine = Engine(new[] { "兵", "壁", "壁" },
                new[] { new EnemyDef("拳", Element.Heart, 3000, 30) });
            engine.Cast("兵");
            engine.Cast("壁");                // 给玩家
            engine.Cast("壁", allySlot: 0);   // 给召唤物
            int enemyHp = engine.Enemies[0].Hp;

            engine.EndTurn();

            Assert.That(enemyHp - engine.Enemies[0].Hp, Is.EqualTo(15 + 15 + 3),
                "玩家那份 15 + 召唤物那份 15 + 召唤物自己那记 3");
        }

        // ---- 真实字表:这几张必须是**纯友方字**,否则拖不到友方身上 ----

        private static string ConfigDir()
        {
            // ⚠ 锚点只能是 TestContext.CurrentContext.TestDirectory,见 EventLabelWidthTests 的注释
            var dir = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根目录");
            return System.IO.Path.Combine(dir.FullName, "Brushblade", "Assets", "StreamingAssets", "config");
        }

        private static RecipeGraph RealGraph() =>
            Brushblade.Data.ConfigLoader.LoadGraph(
                System.IO.File.ReadAllText(System.IO.Path.Combine(ConfigDir(), "chars.json")));

        /// <summary>2026-08-29 用户拍板:七张 buff 字**统一去掉对敌效果**,只留增益。
        ///
        /// 钉的是表现层那条判据的取数:BattleView.AttachDragToAttack 用
        /// <c>NeedsAllyTarget(def, attackMode: true) &amp;&amp; !NeedsTarget(def, attackMode: true)</c>
        /// 判「纯友方字」,只有它为真才**起拖就点亮友方落点**、让玩家直接拖到自己或某只召唤物
        /// 身上松手。带一发伤害的话这个判据为假,玩家就得先拖到敌人、松手后再点友方两段操作,
        /// 而直接拖到召唤物身上会落进 target &lt; 0 那一支 = 静默取消,什么也不发生。
        ///
        /// ⚠ attackMode 传 **true**:拖拽路径恒用攻击模式,而 EffectsOf 在 attackEffects
        /// 非空时**只用它、跳过 effects**。给这几张字配第二用法会让增益在拖拽下静默失效 ——
        /// 这条断言连那个陷阱一起守住了。
        ///
        /// 2026-09-02(水土双方向,Task 10):澡/浴 原本没配 attackEffects,拖拽下退回
        /// effects(纯友方),所以曾经也在这份名单里。本批给它们真正配上了攻击面
        /// (DamageSingle),它们不再是「纯友方字」——从名单里移出,与 铠/战/锋/锐/杜/壁
        /// 这七条真正的纯增益字分开。</summary>
        [Test]
        public void ShippedBuffChars_AreAllyOnly_SoTheyCanBeDraggedOntoAllies()
        {
            var graph = RealGraph();
            // 七条纯增益的载体 + 壁(护盾 + 反弹)。
            foreach (string id in new[] { "铠", "战", "锋", "锐", "杜", "壁" })
            {
                var def = graph.Get(id);
                Assert.That(BattleEngine.NeedsAllyTarget(def, attackMode: true), Is.True,
                    $"「{id}」要选友方目标");
                Assert.That(BattleEngine.NeedsTarget(def, attackMode: true), Is.False,
                    $"「{id}」不该还要选敌人 —— 带对敌效果就拖不到友方身上了");
            }
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
