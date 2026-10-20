using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>局内配方集与生疏(spec 2026-10-19 §1 / §3)。</summary>
    public class RunRecipeTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("丁", null),
            new CharDef("炎", Element.Fire, new[] { "火", "火" }, rarity: CardRarity.Gold,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 100) }),
            new CharDef("焱", Element.Fire, new[] { "炎", "火" }, rarity: CardRarity.Orange),
            new CharDef("燚", Element.Fire, new[] { "焱", "火" }, rarity: CardRarity.Red),
            new CharDef("林", Element.Wood, new[] { "木", "木" }, rarity: CardRarity.Gold),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }, rarity: CardRarity.Orange),
            // 「七成炎」:与 炎 同形、基础值直接写成 70 —— 生疏 炎 的伤害应与它的 Lv1 完全一致
            new CharDef("七成炎", Element.Fire, new[] { "火", "火" }, rarity: CardRarity.Gold,
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 70) }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" },
                effects: new[] { new EffectDef(EffectKind.DamageSingle, 6, shape: TargetArea.All) }),
        });

        private static EnemyDef Dummy() => new("讹影", Element.Heart, 100000, 1);

        private static BattleEngine Battle(BattleConfig config, IReadOnlyList<string> library,
            IReadOnlyList<string> pool = null, IReadOnlyDictionary<string, int> levels = null) =>
            new(Graph(), config, library, pool ?? Array.Empty<string>(), new[] { Dummy() }, seed: 3,
                cardLevels: levels);

        private static int DamageOf(string charId, BattleConfig config, IReadOnlyDictionary<string, int> levels = null)
        {
            var battle = Battle(config, new[] { charId }, levels: levels);
            int before = battle.Enemies[0].Hp;
            Assert.That(battle.Cast(charId, 0), Is.EqualTo(BattleError.None));
            return before - battle.Enemies[0].Hp;
        }

        // ---- §1 可合成集 = 收集闭包 ∪ 配方集(只放开目标字本身) ----

        [Test]
        public void ComposableChars_IncludesRunRecipeTarget()
        {
            var battle = Battle(new BattleConfig { UnlockedChars = new[] { "灯" }, RunRecipes = new[] { "炎" } },
                Array.Empty<string>(), pool: new[] { "火", "火" });
            Assert.That(battle.ComposableChars.Contains("炎"), Is.True);
            Assert.That(battle.Compose("炎"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Library.Contains("炎"), Is.True);
        }

        [Test]
        public void ComposableChars_WithoutRecipe_StillGated()
        {
            var battle = Battle(new BattleConfig { UnlockedChars = new[] { "灯" } },
                Array.Empty<string>(), pool: new[] { "火", "火" });
            Assert.That(battle.ComposableChars.Contains("炎"), Is.False);
            Assert.That(battle.Compose("炎"), Is.EqualTo(BattleError.ForgeFailed));
        }

        [Test]
        public void RunRecipe_DoesNotUnlockDownstreamStackChain() // Review Focus 1:炎 不连带 焱 / 燚
        {
            var battle = Battle(new BattleConfig { UnlockedChars = new[] { "灯" }, RunRecipes = new[] { "炎" } },
                new[] { "炎" }, pool: new[] { "火", "火" });
            Assert.That(battle.ComposableChars.Contains("焱"), Is.False);
            Assert.That(battle.ComposableChars.Contains("燚"), Is.False);
            Assert.That(battle.Compose("焱"), Is.EqualTo(BattleError.ForgeFailed));
        }

        [Test]
        public void RunRecipe_DoesNotUnlockIngredientChars() // 焚 进配方集,上游的 林 不跟着可合
        {
            var battle = Battle(new BattleConfig { UnlockedChars = new[] { "灯" }, RunRecipes = new[] { "焚" } },
                Array.Empty<string>(), pool: new[] { "木", "木" });
            Assert.That(battle.ComposableChars.Contains("焚"), Is.True);
            Assert.That(battle.ComposableChars.Contains("林"), Is.False);
        }

        // ---- §3 生疏:效果 ×70%,卡等级按 Lv1 ----

        [Test]
        public void Unfamiliar_DamageIsSeventyPercentOfLevelOne_IgnoringCardLevel()
        {
            var levels = new Dictionary<string, int> { ["炎"] = 10 };
            int unfamiliar = DamageOf("炎", new BattleConfig { UnfamiliarChars = new[] { "炎" } }, levels);
            int reference = DamageOf("七成炎", new BattleConfig());          // 基础 70、Lv1
            int familiarLv10 = DamageOf("炎", new BattleConfig(), levels);
            Assert.That(unfamiliar, Is.EqualTo(reference), "生疏 = Lv1 × 70%,不吃局外 Lv10");
            Assert.That(unfamiliar, Is.LessThan(familiarLv10));
        }

        [Test]
        public void Unfamiliar_EstimateMatchesActualCast() // Review Focus 3:预估同口径
        {
            var levels = new Dictionary<string, int> { ["炎"] = 10 };
            var battle = Battle(new BattleConfig { UnfamiliarChars = new[] { "炎" } }, new[] { "炎" }, levels: levels);
            var estimate = battle.EstimateCast("炎", 0, false);
            int before = battle.Enemies[0].Hp;
            battle.Cast("炎", 0);
            Assert.That(estimate.Enemies.Single(h => h.Index == 0).Damage, Is.EqualTo(before - battle.Enemies[0].Hp));
        }

        [Test]
        public void Unfamiliar_CurveValues()
        {
            Assert.That(MetaRules.UnfamiliarPercent, Is.EqualTo(70));
            Assert.That(MetaRules.CardLevelPercent(MetaRules.UnfamiliarCardLevel), Is.EqualTo(70));
            Assert.That(MetaRules.ScaleByCardLevel(100, MetaRules.UnfamiliarCardLevel), Is.EqualTo(70));
            Assert.That(MetaRules.ScaleByCardLevel(45, MetaRules.UnfamiliarCardLevel), Is.EqualTo(32), "31.5 向上取整,与等级曲线同写法");
            // 离散量(层数 / 回合 / 次数)不吃等级曲线,生疏也不动它们(裁定,同 spec v7 §1)
            Assert.That(MetaRules.ScaleEffectValue(EffectKind.Freeze, 2, MetaRules.UnfamiliarCardLevel), Is.EqualTo(2));
        }

        [Test]
        public void Unfamiliar_KeepsLevelOneTraits()
        {
            var trait = new TraitDef(TraitSlot.Lv1, TraitFace.Both, TraitForm.Active, null, "测",
                new[] { new EffectDef(EffectKind.DamageSingle, 1) });
            var def = new CharDef("测", Element.Fire, new[] { "火", "火" }, traits: new[] { trait });
            Assert.That(TraitRules.Unlocked(def, MetaRules.UnfamiliarCardLevel).Count, Is.EqualTo(1));
        }

        [Test]
        public void IsUnfamiliar_ReadsConfig()
        {
            var battle = Battle(new BattleConfig { UnfamiliarChars = new[] { "炎" } }, new[] { "炎", "灯" });
            Assert.That(battle.IsUnfamiliar("炎"), Is.True);
            Assert.That(battle.IsUnfamiliar("灯"), Is.False);
        }

        // ---- RunEngine:携带、存档、奇遇入配方集 ----

        private static RunConfig TwoFloors(params EventDef[] events) => new()
        {
            Encounters = new[] { new[] { new EnemyDef("枯", Element.Wood, 4, 2) }, new[] { new EnemyDef("枯", Element.Wood, 4, 2) } },
            RewardPool = new[] { "灯" },
            EventPool = events,
            EventChancePercent = events.Length > 0 ? 100 : 0,
        };

        private static RunEngine Run(RunConfig config, IReadOnlyList<string> recipes = null,
            IReadOnlyList<string> unfamiliar = null, IReadOnlyCollection<string> unlocked = null) =>
            new(Graph(), config, new BattleConfig { UnlockedChars = unlocked ?? new[] { "灯" } },
                new[] { "灯", "灯" }, Array.Empty<string>(), seed: 7,
                runRecipes: recipes, unfamiliarChars: unfamiliar);

        private static void WinFloor(RunEngine run)
        {
            Assert.That(run.Battle.Cast("灯", -1), Is.EqualTo(BattleError.None));
            Assert.That(run.Battle.Phase, Is.EqualTo(BattlePhase.Won));
            run.AdvanceAfterBattle();
        }

        [Test]
        public void RunEngine_PassesRecipesAndUnfamiliarIntoBattle()
        {
            var run = Run(TwoFloors(), recipes: new[] { "炎" }, unfamiliar: new[] { "炎" });
            Assert.That(run.RunRecipes, Is.EqualTo(new[] { "炎" }));
            Assert.That(run.UnfamiliarChars, Is.EqualTo(new[] { "炎" }));
            Assert.That(run.Battle.ComposableChars.Contains("炎"), Is.True);
            Assert.That(run.Battle.IsUnfamiliar("炎"), Is.True);
        }

        [Test]
        public void RunEngine_CollectedCharIsNeverUnfamiliar() // Review Focus 2:已收集优先
        {
            var run = Run(TwoFloors(), recipes: new[] { "炎" }, unfamiliar: new[] { "炎" }, unlocked: new[] { "灯", "炎" });
            Assert.That(run.Battle.IsUnfamiliar("炎"), Is.False);
        }

        [Test]
        public void RunEngine_SnapshotRoundTrip_KeepsRecipesAndUnfamiliar()
        {
            var run = Run(TwoFloors(), recipes: new[] { "炎", "林" }, unfamiliar: new[] { "林" });
            var snapshot = run.Capture();
            Assert.That(snapshot.RunRecipes, Is.EqualTo(new[] { "炎", "林" }));
            Assert.That(snapshot.UnfamiliarChars, Is.EqualTo(new[] { "林" }));
            var restored = RunEngine.Restore(snapshot, Graph(), TwoFloors(),
                new BattleConfig { UnlockedChars = new[] { "灯" } }, null);
            Assert.That(restored.RunRecipes, Is.EqualTo(new[] { "炎", "林" }));
            Assert.That(restored.UnfamiliarChars, Is.EqualTo(new[] { "林" }));
            Assert.That(restored.Battle.ComposableChars.Contains("林"), Is.True, "续爬的战斗也要用配方集");
            Assert.That(restored.Battle.IsUnfamiliar("林"), Is.True);
        }

        [Test]
        public void EndlessSaveState_HasRecipeFields()
        {
            var state = new EndlessSaveState();
            Assert.That(state.RunRecipes, Is.Empty);
            Assert.That(state.UnfamiliarChars, Is.Empty);
        }

        private static EventDef Gift(string charId) => new()
        {
            Id = "测字先生",
            Text = "t",
            Options = new[] { new EventOption { Label = "求字", GainChar = charId } },
        };

        [Test]
        public void EventGift_UncollectedChar_IsAccepted_AndEntersRecipes_Unfamiliar()   // 用户 10-20:奇遇送的未收集字也生疏
        {
            var run = Run(TwoFloors(Gift("炎")));
            WinFloor(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Event));
            Assert.That(run.ChooseEventOption(0), Is.True, "配方集兜底后,送未收集字不再拒收");
            Assert.That(run.CarriedLibrary.Contains("炎"), Is.True);
            Assert.That(run.RunRecipes.Contains("炎"), Is.True);
            Assert.That(run.UnfamiliarChars.Contains("炎"), Is.True);
            run.SkipReward();
            Assert.That(run.Battle.ComposableChars.Contains("炎"), Is.True, "拆了能合回来");
            Assert.That(run.Battle.IsUnfamiliar("炎"), Is.True);
        }

        [Test]
        public void EventGift_AfterWheel_StaysUnfamiliar_NoDuplicate() // 用户 10-20:奇遇送的未收集字也生疏,不再撤销
        {
            var run = Run(TwoFloors(Gift("炎")), recipes: new[] { "炎" }, unfamiliar: new[] { "炎" });
            WinFloor(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.UnfamiliarChars.Count(r => r == "炎"), Is.EqualTo(1));
            Assert.That(run.RunRecipes.Count(r => r == "炎"), Is.EqualTo(1), "不重复记入");
        }

        [Test]
        public void EventGift_CollectedChar_NotRecordedAsRecipe()
        {
            var run = Run(TwoFloors(Gift("灯")));
            WinFloor(run);
            Assert.That(run.ChooseEventOption(0), Is.True);
            Assert.That(run.RunRecipes, Is.Empty);
            Assert.That(run.UnfamiliarChars, Is.Empty, "已收集的字永不生疏");
        }
    }
}
