using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;
using UnityEngine;

namespace Brushblade.Core.Tests
{
    /// <summary>配置加载:JSON → RecipeGraph,非法数据 fail fast(architecture §3)。</summary>
    public class ConfigLoaderTests
    {
        [Test]
        public void LoadGraph_ParsesCharWithAllFields()
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""火"", ""element"": ""Fire"",
                      ""effects"": [ { ""kind"": ""DamageSingle"", ""value"": 4 } ] },
                    { ""id"": ""林"", ""element"": ""Wood"", ""recipe"": [ ""木"", ""木"" ] },
                    { ""id"": ""木"", ""element"": ""Wood"" }
                ]
            }");
            var fire = graph.Get("火");
            Assert.That(fire.Element, Is.EqualTo(Element.Fire));
            Assert.That(fire.IsLeaf, Is.True);
            Assert.That(fire.ApCost, Is.EqualTo(1)); // 缺省 1
            Assert.That(fire.Effects.Single().Kind, Is.EqualTo(EffectKind.DamageSingle));
            Assert.That(fire.Effects.Single().Value, Is.EqualTo(4));
            Assert.That(graph.Get("林").Recipe, Is.EqualTo(new[] { "木", "木" }));
        }

        [Test]
        public void LoadGraph_MissingElement_IsNeutral()
        {
            var graph = ConfigLoader.LoadGraph(@"{ ""chars"": [ { ""id"": ""丁"" } ] }");
            Assert.That(graph.Get("丁").Element, Is.Null);
        }

        [Test]
        public void LoadGraph_UnknownElement_Throws()
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""謎"", ""element"": ""Void"" } ] }"));
            Assert.That(ex.Message, Does.Contain("謎"));
        }

        [Test]
        public void LoadGraph_ParsesEffectFlags() // 灼/堡的条件标志
        {
            var graph = ConfigLoader.LoadGraph(@"{
                ""chars"": [
                    { ""id"": ""灼"", ""element"": ""Fire"",
                      ""effects"": [ { ""kind"": ""DamageSingle"", ""value"": 8, ""doubleVsBurning"": true } ] },
                    { ""id"": ""堡"", ""element"": ""Earth"",
                      ""effects"": [ { ""kind"": ""Shield"", ""value"": 10, ""persistOnce"": true } ] }
                ]
            }");
            Assert.That(graph.Get("灼").Effects.Single().DoubleVsBurning, Is.True);
            Assert.That(graph.Get("灼").Effects.Single().PersistOnce, Is.False);
            Assert.That(graph.Get("堡").Effects.Single().PersistOnce, Is.True);
        }

        [Test]
        public void LoadGraph_UnknownEffectKind_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""火"", ""effects"": [ { ""kind"": ""Explode"", ""value"": 1 } ] } ] }"));
        }

        [Test]
        public void LoadGraph_RecipeReferencesUndefinedChar_Throws() // fail fast 二次校验
        {
            var ex = Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""林"", ""recipe"": [ ""木"", ""木"" ] } ] }"));
            Assert.That(ex.Message, Does.Contain("木"));
        }

        [Test]
        public void LoadGraph_DuplicateId_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph(
                @"{ ""chars"": [ { ""id"": ""火"" }, { ""id"": ""火"" } ] }"));
        }

        [Test]
        public void LoadGraph_MalformedJson_Throws()
        {
            Assert.Throws<ConfigException>(() => ConfigLoader.LoadGraph("not json"));
        }

        // ---- 战役配置实船守卫(嵌入式解析测试见 CampaignTests,纯 C# 可在工装跑) ----

        [Test]
        public void ShippedCampaignJson_LoadsAgainstShippedChars() // 实船双表交叉守卫
        {
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json")));
            var campaign = ConfigLoader.LoadCampaign(File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/enemies.json")), graph);
            Assert.That(campaign.Chapters.Count, Is.EqualTo(3));          // 首发 3 章(17 章 v0.5)
            foreach (var chapter in campaign.Chapters)
            {
                Assert.That(chapter.Stages.Count, Is.EqualTo(5));         // 每章 5 关
                Assert.That(chapter.Stages[4].Boss, Is.True);             // 章末 Boss 关
                Assert.That(chapter.RewardPool, Is.Not.Empty);            // 字池分章投放(F3)
            }
            Assert.That(campaign.DropTable, Is.Not.Empty);
        }

        // ---- 实际配置表:StreamingAssets/config/chars.json 必须永远可加载 ----

        /// <summary>绿档的水与土是一对:各自本系的防御面 + 一记单攻(第 10 章数值表)。
        /// 钉的是**形状**不是数字 —— 沝 曾经是全表唯一只有防御面的绿档,拖上去打不动人
        /// (2026-07-30 补齐)。数值可调,少了攻击面就是回归。</summary>
        [Test]
        public void ShippedCharsJson_GreenWaterAndEarth_BothDefendAndStrike()
        {
            var json = File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json"));
            var graph = ConfigLoader.LoadGraph(json);

            AssertHasKinds(graph, "沝", EffectKind.HealSelf, EffectKind.DamageSingle);
            AssertHasKinds(graph, "圭", EffectKind.Shield, EffectKind.DamageSingle);
        }

        private static void AssertHasKinds(RecipeGraph graph, string id, params EffectKind[] kinds)
        {
            Assert.That(graph.TryGet(id, out var def), Is.True, id);
            foreach (var kind in kinds)
            {
                bool found = false;
                foreach (var effect in def.Effects)
                    if (effect.Kind == kind) found = true;
                Assert.That(found, Is.True, $"「{id}」缺 {kind}");
            }
        }

        [Test]
        public void ShippedCharsJson_LoadsFiveElementLadders() // 首发字库:5 系 × 2/3/4 叠
        {
            var json = File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json"));
            var graph = ConfigLoader.LoadGraph(json);

            // 每系升阶链存在:部件 → 2叠 → 3叠 → 4叠(四金/四木为 PUA 显示代理)
            var ladders = new[]
            {
                new[] { "金", "鍂", "鑫", "" },
                new[] { "木", "林", "森", "" },
                new[] { "水", "沝", "淼", "㵘" },
                new[] { "火", "炎", "焱", "燚" },
                new[] { "土", "圭", "垚", "㙓" },
            };
            // 稀有度阶梯(详表 1.2 迁移,2026-08-03):部件白 / 2叠紫 / 3叠橙 / 4叠金。
            // AP 与稀有度解耦后此迁移不影响手感,但让四叠字坐实「压箱底」的定位。
            var rarities = new[] { CardRarity.White, CardRarity.Purple, CardRarity.Orange, CardRarity.Gold };
            foreach (var ladder in ladders)
                for (int i = 0; i < ladder.Length; i++)
                {
                    Assert.That(graph.TryGet(ladder[i], out var def), Is.True, ladder[i]);
                    Assert.That(def.Rarity, Is.EqualTo(rarities[i]), ladder[i]);
                    if (i == 0) continue;
                    // 链式配方「部件在前、低阶字在后」(详表 1.5,2026-08-03 拍板):
                    // 读作「往低阶字上再加一个部件」。10 个字的顺序已随之修正。
                    Assert.That(def.Recipe, Is.EqualTo(new[] { ladder[0], ladder[i - 1] }));
                }

            // 出字 AP 一律 1,与稀有度解耦(2026-08-03 拍板;3/4 叠不再是 2 AP 的高阶字)
            Assert.That(graph.Get("焱").ApCost, Is.EqualTo(1));
            Assert.That(graph.Get("燚").ApCost, Is.EqualTo(1));
        }

        /// <summary>首局教程剧本(拆炎→合炎→出炎)必须能打通首层敌人,否则新手卡死。
        /// 「只能合已收集的字」后首局只有 2 叠可用,这里守住实船数值(步骤机测试见 TutorialTests)。</summary>
        [Test]
        public void ShippedConfig_FirstTowerTutorial_CanClearFloorOne()
        {
            var configDir = Path.Combine(Application.streamingAssetsPath, "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var segment = EndlessGenerator.BuildFirstTowerSegment(campaign.Endless, seed: 7);
            var floorOne = segment.Encounters[0];
            Assert.That(floorOne.Count, Is.EqualTo(1)); // 首层单敌

            var battle = new BattleEngine(graph,
                new BattleConfig
                {
                    DropTable = campaign.DropTable,
                    UnlockedChars = new[] { "鍂", "林", "沝", "炎", "圭" }, // 初始出阵列表
                },
                new[] { "炎" }, new[] { "火", "火" }, floorOne, seed: 7);

            Assert.That(battle.Compose("焱"), Is.EqualTo(BattleError.ForgeFailed)); // 不在出阵,合不出
            Assert.That(battle.Dismantle("炎"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Compose("炎"), Is.EqualTo(BattleError.None));
            Assert.That(battle.Cast("炎"), Is.EqualTo(BattleError.None));
            if (battle.Phase == BattlePhase.PlayerTurn)
                battle.EndTurn(); // 直伤不足则灼烧补刀
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
        }

        /// <summary>实船 enemies.json 的字表接线:三只固定 Boss 与程序生成成语 Boss
        /// 都应从 bossSkills 拿到技能(spec 5.1)。</summary>
        [Test]
        public void ShippedConfig_BossesGetSkillsFromCharTable()
        {
            var configDir = Path.Combine(Application.streamingAssetsPath, "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            var paiShan = campaign.Endless.Bands[0].BossPool[0];
            Assert.That(paiShan.Id, Is.EqualTo("排山倒海"));
            Assert.That(paiShan.Phases[0].Skill, Is.EqualTo(BossSkill.Topple));  // 排
            Assert.That(paiShan.Phases[1].Skill, Is.EqualTo(BossSkill.Bulwark)); // 山
            Assert.That(paiShan.Phases[2].Skill, Is.EqualTo(BossSkill.Topple));  // 倒
            Assert.That(paiShan.Phases[3].Skill, Is.EqualTo(BossSkill.Deluge));  // 海

            // 墨海层段(最后一个 band)的成语 Boss 也要拿到技能
            var moHai = campaign.Endless.Bands[campaign.Endless.Bands.Count - 1];
            var daoShan = moHai.IdiomBossPool[0];
            Assert.That(daoShan.Chars, Is.EqualTo("刀山火海"));
            Assert.That(daoShan.Skills[0], Is.EqualTo(BossSkill.Pierce));  // 刀
            Assert.That(daoShan.Skills[1], Is.EqualTo(BossSkill.Bulwark)); // 山
            Assert.That(daoShan.Skills[2], Is.EqualTo(BossSkill.Devour));  // 火
            Assert.That(daoShan.Skills[3], Is.EqualTo(BossSkill.Deluge));  // 海
        }
    }
}
