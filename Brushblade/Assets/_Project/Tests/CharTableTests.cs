using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>实际出货字表(StreamingAssets/config/chars.json)的内容校验。
    /// 与 ConfigLoaderTests 分开:那个文件引 UnityEngine,被 dotnet 工装排除。</summary>
    public class CharTableTests
    {
        /// <summary>实际出货字表;同程序集的其他测试(StartingSetupTests)也用这一份。</summary>
        internal static RecipeGraph RealGraph()
        {
            // 从测试程序集所在目录往上找仓库根
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根(含 Brushblade/ 的目录)");
            var path = Path.Combine(dir.FullName,
                "Brushblade/Assets/StreamingAssets/config/chars.json");
            return ConfigLoader.LoadGraph(File.ReadAllText(path));
        }

        [Test]
        public void RealConfig_StackChainRecipesAreComponentFirst()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("森").Recipe, Is.EqualTo(new[] { "木", "林" }));
            Assert.That(graph.Get("燚").Recipe, Is.EqualTo(new[] { "火", "焱" }));
            Assert.That(graph.Get("㙓").Recipe, Is.EqualTo(new[] { "土", "垚" }));
        }

        [Test]
        public void RealConfig_FiveStackCharsAreTopRarity()
        {
            // 𣛧/𨰻 是增补平面字符,UGUI Text 不支持代理对显示,落地时换成 PUA 代理码位
            // (subset_fonts.py 的 STACKED,U+E625 = 四木、U+E626 = 四金)。
            var graph = RealGraph();
            foreach (var id in new[] { "燚", "㵘", "㙓", "\uE625", "\uE626" })
                Assert.That(graph.Get(id).Rarity, Is.EqualTo(CardRarity.Red), $"{id} 应为红档(最高)");
        }

        [Test]
        public void RealConfig_XiangShengCharsStoreBaseValue()
        {
            // 焚含木生火,配置表填基础值 7,引擎结算时 ×3 = 21
            var aoe = RealGraph().Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(aoe.Value, Is.EqualTo(7), "相生字必须填基础值,不是最终值");
        }

        [Test]
        public void RealConfig_P0UnlockedWordsAreLoadable()
        {
            var graph = RealGraph();
            foreach (var id in new[] { "锯", "淋", "润", "沐", "滋", "冰", "冻", "溺",
                                       "埋", "坑", "藤", "洼", "凝", "冷",
                                       "铠", "崊", "崟", "磐", "巍", "漜" })
                Assert.That(graph.Get(id), Is.Not.Null, $"{id} 应已收录");
        }

        [Test]
        public void RealConfig_KaiIsDamageReductionTwenty()
        {
            var effect = RealGraph().Get("铠").Effects
                .First(e => e.Kind == EffectKind.DamageReduction);
            Assert.That(effect.Value, Is.EqualTo(20));
        }

        [Test]
        public void RealConfig_RunIsHealOverTimeTargetAll()
        {
            // 润:群体持续治疗,turns/targetAll 必须从 JSON 真正传到 EffectDef
            // (ConfigLoader.ParseEffects 此前没接这两个字段——本测试就是防回归的)
            var effect = RealGraph().Get("润").Effects
                .First(e => e.Kind == EffectKind.HealOverTime);
            Assert.That(effect.Turns, Is.EqualTo(2));
            Assert.That(effect.TargetAll, Is.True);
        }

        [Test]
        public void RealConfig_MuIsHealOverTimeSingleTargetThreeTurns()
        {
            // 沐:单体持续,turns=3、targetAll 应为 false(不含召唤物)
            var effect = RealGraph().Get("沐").Effects
                .First(e => e.Kind == EffectKind.HealOverTime);
            Assert.That(effect.Turns, Is.EqualTo(3));
            Assert.That(effect.TargetAll, Is.False);
        }

        [Test]
        public void RealConfig_PierceChars_CarryIgnoreArmorFlag()
        {
            // ignoreArmor 若没从 JSON 传到 EffectDef,字照常能打但穿甲效果静默消失
            var graph = RealGraph();
            foreach (var id in new[] { "锥", "刺", "錰" })
            {
                var hit = graph.Get(id).Effects.First(e => e.Kind == EffectKind.DamageSingle);
                Assert.That(hit.IgnoreArmor, Is.True, $"「{id}」应带穿甲标记");
            }
        }

        [Test]
        public void RealConfig_SummonPassiveChars_CarryTheirPassive()
        {
            // passive 若没从 JSON 传到 EffectDef,这些字照常能召唤,但被动会静默消失
            var graph = RealGraph();
            var expected = new Dictionary<string, Action<SummonPassive>>
            {
                ["烓"] = p => { Assert.That(p.OnHitBurn, Is.EqualTo(3)); Assert.That(p.OnHitBurnAll, Is.True); },
                ["灶"] = p => { Assert.That(p.OnHitBurn, Is.EqualTo(2)); Assert.That(p.OnHitBurnAll, Is.False); },
                ["楸"] = p => Assert.That(p.OnHitBurn, Is.EqualTo(1)),
                ["荆"] = p => Assert.That(p.Thorns, Is.EqualTo(3)),
                ["桃"] = p => Assert.That(p.HealAlly, Is.EqualTo(3)),
                ["槐"] = p => Assert.That(p.OnHitCurse, Is.EqualTo(25)),
                ["桤"] = p => Assert.That(p.Speed, Is.EqualTo(150)),
            };
            foreach (var pair in expected)
            {
                var summon = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.Summon);
                Assert.That(summon.Passive, Is.Not.Null, $"「{pair.Key}」应带被动");
                pair.Value(summon.Passive);
            }
        }

        [Test]
        public void RealConfig_GuiGrantsSummonShield()
        {
            var graph = RealGraph();
            var summon = graph.Get("桂").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.SummonShield, Is.EqualTo(6));
            Assert.That(summon.SummonCount, Is.EqualTo(2));
        }

        [Test]
        public void RealConfig_JiaoIsPlainTankSummon()
        {
            // 蕉 靠高血低攻当天然肉盾,不该被顺手加上被动
            var graph = RealGraph();
            var summon = graph.Get("蕉").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.Value, Is.EqualTo(28));
            Assert.That(summon.SummonAttack, Is.EqualTo(3));
            Assert.That(summon.Passive, Is.Null);
        }

        [Test]
        public void RealConfig_ArmorBreakChars_CarryTwoTurns()
        {
            var graph = RealGraph();
            foreach (var id in new[] { "熔", "溃", "溶", "锤", "破", "碎" })
            {
                var brk = graph.Get(id).Effects.First(e => e.Kind == EffectKind.ArmorBreak);
                Assert.That(brk.Value, Is.EqualTo(2), $"「{id}」破甲应持续 2 回合");
            }
        }

        [Test]
        public void RealConfig_MaxHpEvents_ReachEventOption()
        {
            // 养气/淬骨/换气:maxHpPercent 与 maxHpChancePercent 得真从 JSON 传到 EventOption
            // ——ConfigLoader 漏接字段是静默失败(上限奇遇会变成什么都不做),故钉住
            var campaign = RealCampaign();
            var byId = campaign.Events.ToDictionary(e => e.Id);

            Assert.That(byId["养气"].Options[0].MaxHpPercent, Is.EqualTo(30));
            Assert.That(byId["养气"].Options[0].MaxHpChancePercent, Is.EqualTo(0)); // 必得

            Assert.That(byId["淬骨"].Options[0].MaxHpPercent, Is.EqualTo(30));
            Assert.That(byId["淬骨"].Options[0].MaxHpChancePercent, Is.EqualTo(80)); // 两成反噬

            Assert.That(byId["换气"].Options[0].MaxHpPercent, Is.EqualTo(30));
            Assert.That(byId["换气"].Options[0].ComponentCost, Is.EqualTo(1));
        }

        [Test]
        public void RealConfig_DengHuaCarriesSearAbility()
        {
            // ability 若没从 JSON 传到 EnemyDef,灯花照常能打但不会给玩家挂灼烧,
            // 净化与免疫这批字就全成了死牌
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(RepoRoot(),
                    "Brushblade/Assets/StreamingAssets/config/enemies.json")), RealGraph());
            var dengHua = campaign.Endless.Bands
                .SelectMany(b => b.EnemyPool)
                .FirstOrDefault(e => e.Id == "灯花");
            Assert.That(dengHua, Is.Not.Null, "灯花应出现在层段的敌人池里");
            Assert.That(dengHua.Ability, Is.EqualTo(EnemyAbility.Sear));
        }

        private static CampaignConfig RealCampaign()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            var path = Path.Combine(dir.FullName,
                "Brushblade/Assets/StreamingAssets/config/enemies.json");
            return ConfigLoader.LoadCampaign(File.ReadAllText(path), RealGraph());
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到仓库根");
            return dir.FullName;
        }
    }
}
