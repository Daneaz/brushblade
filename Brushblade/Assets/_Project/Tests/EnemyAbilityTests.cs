using System;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>字怪特殊能力(8.3):缺笔妖自补全、叠字怪受击分裂。</summary>
    public class EnemyAbilityTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("火", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 40) }),
            new CharDef("烧", Element.Fire,
                effects: new[] { new EffectDef(EffectKind.DamageAll, 50) }),
        });

        private static EnemyDef Regrower(int hp = 300) =>
            new("缺笔妖", Element.Metal, hp, 20, EnemyAbility.Regrow);

        private static EnemyDef Plain(string id, int hp, int attack) =>
            new(id, Element.Wood, hp, attack, EnemyAbility.None);

        private static EnemyDef Splitter(int hp = 160) =>
            new("叠字怪", Element.Wood, hp, 50, EnemyAbility.Split);

        // 玩家血量显式给 MaxHpFor(1):本夹具的怪攻已随量级 ×10,再吃 BattleConfig 的
        // 缺省 50(旧量级)会让玩家在第二个 EndTurn 就阵亡,补全进度推不到第 3 级。
        private static BattleEngine Engine(params EnemyDef[] enemies) =>
            new(Graph(), new BattleConfig { PlayerMaxHp = MetaRules.MaxHpFor(1) },
                new[] { "烧" }, new[] { "火", "火", "火" }, enemies, seed: 1);

        // ---- 缺笔妖:自补全 ----

        [Test]
        public void Regrow_GainsAttackAndHeals_EachEnemyTurn()
        {
            var engine = Engine(Regrower(hp: 300));
            engine.Cast("火", 0);  // 破点血:金被火克 ×1.5 → 60;300−60=240
            int hpAfterHit = engine.Enemies[0].Hp;
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Attack, Is.EqualTo(20 + 20));        // 攻 +20
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(hpAfterHit + 30));    // 回 30 血
            Assert.That(engine.Enemies[0].RegrowProgress, Is.EqualTo(1));
        }

        [Test]
        public void Regrow_HealCapsAtMaxHp()
        {
            var engine = Engine(Regrower(hp: 300)); // 未受伤
            engine.EndTurn();
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(300));
        }

        [Test]
        public void Regrow_ThirdTurn_Completes_DoubleAttackFullHeal()
        {
            var engine = Engine(Regrower(hp: 300));
            engine.Cast("火", 0); // 掉 60 血
            engine.EndTurn();     // 进度 1,攻 40
            engine.EndTurn();     // 进度 2,攻 60
            engine.EndTurn();     // 进度 3:补全完成

            var enemy = engine.Enemies[0];
            Assert.That(enemy.RegrowProgress, Is.EqualTo(3));
            Assert.That(enemy.Attack, Is.EqualTo((20 + 20 * 3) * 2)); // (基础20+20×3)×2 = 160
            Assert.That(enemy.Hp, Is.EqualTo(300));                   // 血回满

            engine.EndTurn(); // 完成后不再成长
            Assert.That(enemy.Attack, Is.EqualTo(160));
        }

        /// <summary>补全必须发事件。它原先是静默结算的:模型瞬时回血,表现层只在末次重绘看到结果,
        /// 玩家看到的就是「召唤物砸上去不掉血」「还没打就满血」(2026-07-29 实测)。</summary>
        [Test]
        public void Regrow_EmitsEvent_WithHealAmountAndProgress()
        {
            var engine = Engine(Regrower(hp: 300));
            engine.Cast("火", 0); // 先破点血,回血才有量
            engine.EndTurn();

            var regrow = FirstOfKind(engine, BattleEventKind.Regrow);
            Assert.That(regrow.HasValue, "补全没发事件,表现层无从按节拍演");
            Assert.That(regrow.Value.TargetIndex, Is.EqualTo(0));
            Assert.That(regrow.Value.Amount, Is.EqualTo(30));     // 实际回血量
            Assert.That(regrow.Value.SecondIndex, Is.EqualTo(1)); // 补全进度
        }

        /// <summary>回血被上限吃掉时,事件金额必须是**实际**回了多少(0),不是名义的 30 ——
        /// 表现层拿它推血条,报名义值会把条推过头。</summary>
        [Test]
        public void Regrow_EventAmount_IsActualHeal_NotNominal()
        {
            var engine = Engine(Regrower(hp: 300)); // 满血,回不动
            engine.EndTurn();

            var regrow = FirstOfKind(engine, BattleEventKind.Regrow);
            Assert.That(regrow.HasValue);
            Assert.That(regrow.Value.Amount, Is.EqualTo(0));
        }

        /// <summary>第 3 次补全把血拉满:事件金额要覆盖这一整段,否则血条只涨 30 点就停住。</summary>
        [Test]
        public void Regrow_FinalStage_EventAmount_CoversFullHeal()
        {
            var engine = Engine(Regrower(hp: 300));
            engine.Cast("火", 0);
            engine.EndTurn();
            engine.EndTurn();
            int before = engine.Enemies[0].Hp;
            engine.EndTurn(); // 第 3 次:血回满

            var regrow = FirstOfKind(engine, BattleEventKind.Regrow);
            Assert.That(regrow.HasValue);
            Assert.That(regrow.Value.SecondIndex, Is.EqualTo(3));
            Assert.That(regrow.Value.Amount, Is.EqualTo(300 - before));
        }

        // 2026-08-16 CTB 改造:原断言"补全必须在最后一记敌方攻击之后结算"依据的是 2026-07-30
        // 的试玩裁定,已被 spec §3.1「本次推翻的既有裁定」明确推翻——CTB 下已无"回合末尾"
        // 这个位置,缺笔妖补全挪到了它自己那一拍、攻击之前(spec §4.3「每个敌人那一拍」
        // 第 3 步:先补全,第 5 步才出手)。原测试想防的"打到一半血突然回了"的手感问题,
        // 现在反过来由"补全排在它自己出手之前"来满足——它这一下已经是用补全后的新攻击力/
        // 新血量打的,强度上升一档(spec §3.1 已注明这是有意的代价,平衡期再调)。
        /// <summary>补全必须排在**它自己出手之前**(spec §4.3,推翻 2026-07-30 试玩裁定)。</summary>
        [Test]
        public void Regrow_SettlesBeforeItsOwnAttack_NotAfterAllAttacks()
        {
            var engine = Engine(Regrower(hp: 300), Plain("木妖", hp: 200, attack: 10));
            engine.Cast("火", 0);
            engine.EndTurn();

            int regrowAt = IndexOfKind(engine, BattleEventKind.Regrow);
            Assert.That(regrowAt, Is.GreaterThanOrEqualTo(0), "补全没发事件");
            int firstAttackAt = IndexOfKind(engine, BattleEventKind.EnemyAttack);
            Assert.That(firstAttackAt, Is.GreaterThanOrEqualTo(0), "这一回合应当有敌方攻击");
            Assert.That(regrowAt, Is.LessThan(firstAttackAt),
                "补全排在它自己出手之前,这一下已经是用补全后的新攻击力打的");
        }

        /// <summary>本回合更早的时候已经被打死的,不许再补全 —— 死了还回血就成了打不死的怪。
        /// 补全挪到全部攻击之后独立结算,这条守卫才有意义:灼烧在敌方回合开头就把它带走,
        /// 而补全那一趟在回合收尾才跑,中间隔着别的怪的整轮行动。</summary>
        [Test]
        public void Regrow_DoesNotSettle_WhenKilledEarlierInTheTurn()
        {
            var engine = Engine(Regrower(hp: 30), Plain("木妖", hp: 200, attack: 10));
            engine.Cast("烧", 0);  // 挂灼烧,敌方回合开头结算
            engine.EndTurn();

            Assert.That(engine.Enemies[0].Alive, Is.False, "前置条件:灼烧该在回合开头带走它");
            Assert.That(IndexOfKind(engine, BattleEventKind.Regrow), Is.EqualTo(-1),
                "死了还补全就成了打不死的怪");
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(0));
            Assert.That(engine.Enemies[0].RegrowProgress, Is.EqualTo(0), "进度也不该推进");
        }

        private static int IndexOfKind(BattleEngine engine, BattleEventKind kind)
        {
            for (int i = 0; i < engine.LastEvents.Count; i++)
                if (engine.LastEvents[i].Kind == kind) return i;
            return -1;
        }

        private static int LastIndexOfKind(BattleEngine engine, BattleEventKind kind)
        {
            for (int i = engine.LastEvents.Count - 1; i >= 0; i--)
                if (engine.LastEvents[i].Kind == kind) return i;
            return -1;
        }

        private static BattleEvent? FirstOfKind(BattleEngine engine, BattleEventKind kind)
        {
            foreach (var e in engine.LastEvents)
                if (e.Kind == kind) return e;
            return null;
        }

        // ---- 叠字怪:受击分裂 ----

        [Test]
        public void Split_FirstDamageSurvived_SpawnsCloneHalfHp()
        {
            var engine = Engine(Splitter(hp: 160));
            engine.Cast("火", 0); // 火 vs 木 1.0 → 40 伤 → 120 血,分裂

            Assert.That(engine.Enemies.Count, Is.EqualTo(2));
            Assert.That(engine.Enemies[0].Hp, Is.EqualTo(60));  // ceil(120/2)
            Assert.That(engine.Enemies[1].Hp, Is.EqualTo(60));
            Assert.That(engine.Enemies[1].Def.Id, Is.EqualTo("叠字怪"));
            Assert.That(engine.Enemies.All(e => e.HasSplit), Is.True);
            Assert.That(engine.LastEvents.Any(e => e.Kind == BattleEventKind.EnemySplit), Is.True);
        }

        [Test]
        public void Split_OnlyOnce()
        {
            var engine = Engine(Splitter(hp: 160));
            engine.Cast("火", 0);            // 分裂 → 两只 60 血
            engine.EndTurn();
            engine.Cast("火", 0);            // 再打不再分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(2));
        }

        [Test]
        public void Split_NotWhenKilled()
        {
            var engine = Engine(Splitter(hp: 40));
            engine.Cast("火", 0); // 40 伤致死
            Assert.That(engine.Enemies.Count, Is.EqualTo(1));
            Assert.That(engine.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void Split_CappedAtSixEnemies()
        {
            var engine = Engine(Splitter(), Splitter(), Splitter(), Splitter());
            engine.Cast("烧"); // AOE 打全场:分裂到 6 只后守卫阻挡
            Assert.That(engine.Enemies.Count, Is.EqualTo(6));
        }

        [Test]
        public void Split_PreexistingAtCap_NoFurtherSplit()
        {
            // 预先就在上限 6,AOE 命中后仍不再分裂(恢复原场景覆盖)
            var engine = Engine(Splitter(), Splitter(), Splitter(), Splitter(), Splitter(), Splitter());
            engine.Cast("烧"); // AOE 打 6 只叠字怪,全部活着但无人分裂
            Assert.That(engine.Enemies.Count, Is.EqualTo(6));
        }

        // ---- 配置解析 ----

        [Test]
        public void LoadCampaign_ParsesAbility_DefaultsNone()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            var campaign = ConfigLoader.LoadCampaign(@"{
                ""enemies"": [
                    { ""id"": ""叠字怪"", ""element"": ""Wood"", ""maxHp"": 16, ""attack"": 5, ""ability"": ""Split"" },
                    { ""id"": ""错字鬼"", ""element"": ""Wood"", ""maxHp"": 10, ""attack"": 3 }
                ],
                ""dropTable"": [],
                ""chapters"": [ { ""name"": ""蒙学"",
                    ""stages"": [ { ""encounters"": [ [ ""叠字怪"", ""错字鬼"" ] ] } ], ""rewardPool"": [] } ]
            }", graph);
            var encounter = campaign.Chapters[0].Stages[0].Encounters[0];
            Assert.That(encounter[0].Ability, Is.EqualTo(EnemyAbility.Split));
            Assert.That(encounter[1].Ability, Is.EqualTo(EnemyAbility.None));
        }

        [Test]
        public void LoadCampaign_UnknownAbility_Throws()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""灯"" } ] }");
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadCampaign(@"{
                ""enemies"": [ { ""id"": ""謎"", ""element"": ""Wood"", ""maxHp"": 1, ""attack"": 1, ""ability"": ""Fly"" } ],
                ""dropTable"": [], ""chapters"": [ { ""name"": ""x"",
                    ""stages"": [ { ""encounters"": [] } ], ""rewardPool"": [] } ]
            }", graph));
        }
    }
}
