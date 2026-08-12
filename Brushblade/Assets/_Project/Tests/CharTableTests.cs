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
            // 焚含木生火,配置表填基础值 70,引擎结算时 ×3 = 210
            var aoe = RealGraph().Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(aoe.Value, Is.EqualTo(70), "相生字必须填基础值,不是最终值");
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
        public void RealConfig_KaiIsDefenseTwelve()
        {
            var effect = RealGraph().Get("铠").Effects
                .First(e => e.Kind == EffectKind.DefenseBuff);
            Assert.That(effect.Value, Is.EqualTo(12));
        }

        /// <summary>6 个护甲字的点数(spec §6.2 的折算表:旧减伤% × 0.6)。
        /// 逐字钉住而不是只钉 铠 —— 折算表是设计裁定,漏改一个字不会有别的测试红。</summary>
        [Test]
        public void RealConfig_DefenseChars_CarryTheirPoints()
        {
            var graph = RealGraph();
            var expected = new Dictionary<string, int>
            {
                ["巍"] = 3, ["磐"] = 6, ["崟"] = 9, ["铠"] = 12, ["崊"] = 12, ["漜"] = 15,
            };
            foreach (var pair in expected)
            {
                var buff = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.DefenseBuff);
                Assert.That(buff.Value, Is.EqualTo(pair.Value), $"「{pair.Key}」护甲点数");
            }
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
        public void RealConfig_PierceChars_CarryPiercePoints()
        {
            // pierce 若没从 JSON 传到 EffectDef,字照常能打但穿透效果静默消失
            // (2026-08-12,E-b4 T3:旧的 ignoreArmor 布尔标记换成点数)。
            // 基础值同时钉住:旧「穿甲无条件 +15%」已固化进基础值(400→460 / 130→150 / 90→105),
            // 那是**精确等价变换**,漏做的话对无甲目标的收益会静默缩水 15%。
            var graph = RealGraph();
            var expected = new Dictionary<string, (int Damage, int Pierce)>
            {
                ["锥"] = (105, 10), ["刺"] = (150, 15), ["錰"] = (460, 30),
            };
            foreach (var pair in expected)
            {
                var hit = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.DamageSingle);
                Assert.That(hit.Value, Is.EqualTo(pair.Value.Damage), $"「{pair.Key}」基础值(含固化的 +15%)");
                Assert.That(hit.Pierce, Is.EqualTo(pair.Value.Pierce), $"「{pair.Key}」穿透点数");
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
                ["荆"] = p => Assert.That(p.Thorns, Is.EqualTo(30)),
                ["桃"] = p => Assert.That(p.HealAlly, Is.EqualTo(30)),
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
            Assert.That(summon.SummonShield, Is.EqualTo(60));
            Assert.That(summon.SummonCount, Is.EqualTo(2));
        }

        [Test]
        public void RealConfig_JiaoIsPlainTankSummon()
        {
            // 蕉 靠高血低攻当天然肉盾,不该被顺手加上被动
            var graph = RealGraph();
            var summon = graph.Get("蕉").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.Value, Is.EqualTo(280));
            Assert.That(summon.SummonAttack, Is.EqualTo(30));
            Assert.That(summon.Passive, Is.Null);
        }

        [Test]
        public void RealConfig_ArmorBreakChars_CarryTheirPoints()
        {
            // ⚠ 语义反转(2026-08-12,E-b4 T3):value 从**回合数**(全部 6 字 = 2)
            // 变成**削减的护甲点数**。档位依据是战例二「三张蓝档削光坚壁 Boss(60)」。
            var graph = RealGraph();
            var expected = new Dictionary<string, int>
            {
                ["碎"] = 10, ["溶"] = 15, ["破"] = 15, ["熔"] = 20, ["溃"] = 20, ["锤"] = 20,
            };
            foreach (var pair in expected)
            {
                var brk = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.ArmorBreak);
                Assert.That(brk.Value, Is.EqualTo(pair.Value), $"「{pair.Key}」破甲削减点数");
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

        [Test]
        public void RealConfig_ExecuteChars_CarryTheirThresholds()
        {
            var graph = RealGraph();
            var zha = graph.Get("铡").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(zha.ExecuteBelowPercent, Is.EqualTo(25));
            Assert.That(zha.ExecuteKills, Is.True);

            var lian = graph.Get("镰").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(lian.ExecuteBelowPercent, Is.EqualTo(30));
            Assert.That(lian.ExecuteKills, Is.False, "残血加伤,不是处决");

            var jiao = graph.Get("剿").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(jiao.ExecuteBelowPercent, Is.EqualTo(30));
        }

        [Test]
        public void RealConfig_DispelChars_CarryTheirCounts()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("灭").Effects.First(e => e.Kind == EffectKind.Dispel).Value,
                Is.EqualTo(-1), "灭清全部");
            Assert.That(graph.Get("削").Effects.First(e => e.Kind == EffectKind.Dispel).Value,
                Is.EqualTo(1), "削只清一条");
            Assert.That(graph.Get("刮").Effects.First(e => e.Kind == EffectKind.Dispel).Value,
                Is.EqualTo(-1), "刮清全部");
            var dan = graph.Get("淡").Effects.First(e => e.Kind == EffectKind.Dispel);
            Assert.That(dan.TargetAll, Is.True, "淡是全体各清一条");
            Assert.That(dan.Value, Is.EqualTo(1));
        }

        [Test]
        public void RealConfig_ImmunityAndCleanseAndRevive()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("杜").Effects.First(e => e.Kind == EffectKind.Immunity).Value,
                Is.EqualTo(2));
            Assert.That(graph.Get("塞").Effects.First(e => e.Kind == EffectKind.Immunity).Value,
                Is.EqualTo(1));
            // 岿 = 免疫 1 次 + 立即净化,两半都要在
            var kui = graph.Get("岿").Effects;
            Assert.That(kui.Any(e => e.Kind == EffectKind.Immunity), Is.True);
            Assert.That(kui.Any(e => e.Kind == EffectKind.Cleanse), Is.True);
            Assert.That(graph.Get("浴").Effects.Any(e => e.Kind == EffectKind.Cleanse), Is.True);
            Assert.That(graph.Get("活").Effects.First(e => e.Kind == EffectKind.Revive).Value,
                Is.EqualTo(1));
        }

        [Test]
        public void RealConfig_SaiUsesManualRecipe_NotSupplementaryPlanePart()
        {
            // 塞 的 IDS 部件 𡨄 在增补平面,UGUI Text 显示不出代理对 ——
            // 走 IDS 会让塞退化成不可拆的叶子(只能靠掉落获得)
            var graph = RealGraph();
            Assert.That(graph.Get("塞").Recipe, Is.EqualTo(new[] { "宀", "土" }));
            Assert.That(graph.Get("湮").Recipe, Is.EqualTo(new[] { "氵", "土" }));
        }

        [Test]
        public void RealConfig_BlindCharsCarryTheirPercentAndTurns()
        {
            var graph = RealGraph();
            var sui = graph.Get("熣").Effects.First(e => e.Kind == EffectKind.Blind);
            Assert.That(sui.Value, Is.EqualTo(50));
            Assert.That(sui.Turns, Is.EqualTo(2), "turns 被静默丢掉的话会是 0——挂上去当场到期");
            Assert.That(sui.TargetAll, Is.False);
            Assert.That(graph.Get("熣").Effects.First(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(160), "160 是带致盲后的平衡值,原值 210");

            var yan = graph.Get("烟").Effects.First(e => e.Kind == EffectKind.Blind);
            Assert.That(yan.Value, Is.EqualTo(30));
            Assert.That(yan.Turns, Is.EqualTo(1));
            Assert.That(yan.TargetAll, Is.True, "烟 是全体致盲");
        }

        [Test]
        public void RealConfig_SilenceAndReflectCarryTurns()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("锁").Effects.First(e => e.Kind == EffectKind.Silence).Turns,
                Is.EqualTo(1));
            var jing = graph.Get("镜").Effects.First(e => e.Kind == EffectKind.Reflect);
            Assert.That(jing.Value, Is.EqualTo(50));
            Assert.That(jing.Turns, Is.EqualTo(2));
        }

        [Test]
        public void RealConfig_DuoIsTwoSegments()
        {
            var graph = RealGraph();
            var duo = graph.Get("剁").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(duo.Value, Is.EqualTo(100));
            Assert.That(duo.HitCount, Is.EqualTo(2));
        }

        [Test]
        public void RealConfig_LiuCarriesDodge()
        {
            var graph = RealGraph();
            var summon = graph.Get("柳").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.Value, Is.EqualTo(80));
            Assert.That(summon.SummonAttack, Is.EqualTo(30));
            Assert.That(summon.Passive, Is.Not.Null);
            Assert.That(summon.Passive.Dodge, Is.EqualTo(50));
        }

        [Test]
        public void RealConfig_SuoUsesManualRecipe_NotSupplementaryPlanePart()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("锁").Recipe, Is.EqualTo(new[] { "钅", "贝" }));
        }

        [Test]
        public void RealConfig_MuCarriesBurnAndNoDecay()
        {
            var effects = RealGraph().Get("炑").Effects;
            // 断全序列(而非只断前两条)——否则行尾静默多挂一个效果(如 Detonate)不会被发现
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.BurnNoDecay,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(effects[0].Value, Is.EqualTo(2));
        }

        [Test]
        public void RealConfig_ZaoSettlesAfterRaisingPotency()
        {
            var effects = RealGraph().Get("燥").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.BurnPotency, EffectKind.BurnSettleNow,
            }), "顺序错了立即结算就吃不到自己抬的系数");
            Assert.That(effects[0].Value, Is.EqualTo(2));   // 层数,不吃 ×10
            Assert.That(effects[1].Value, Is.EqualTo(10));  // 灼烧系数,吃 ×10
        }

        [Test]
        public void RealConfig_XiaoIsFourStacksThenDetonate()
        {
            var effects = RealGraph().Get("灱").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.Detonate,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(effects[0].Value, Is.EqualTo(4), "4 层给引爆 20 伤的地板");
        }

        [Test]
        public void RealConfig_NewBurnCharsAddNoLeafParts()
        {
            // 三个字的部件(火 木 喿 刀)全部已在表中,本批不该新增任何叶子——
            // 直接断配方本身,而不是断「部件能在表里查到」:build_chars 会自动把任何
            // 配方部件补成叶子条目写进 chars.json,查得到不代表它是本批之前就已存在的字。
            var graph = RealGraph();
            Assert.That(graph.Get("炑").Recipe, Is.EqualTo(new[] { "火", "木" }));
            Assert.That(graph.Get("燥").Recipe, Is.EqualTo(new[] { "火", "喿" }));
            Assert.That(graph.Get("灱").Recipe, Is.EqualTo(new[] { "火", "刀" }));
        }

        [Test]
        public void RealConfig_NewBurnCharsHaveExpectedRarity()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("炑").Rarity, Is.EqualTo(CardRarity.Purple));
            Assert.That(graph.Get("燥").Rarity, Is.EqualTo(CardRarity.Purple));
            Assert.That(graph.Get("灱").Rarity, Is.EqualTo(CardRarity.Purple));
        }

        [Test]
        public void RealConfig_GouIsNotInTheTable()
        {
            // 钩 是模型缺口(敌人无排位概念),已移出字表。
            // ⚠ 这条只守生成物这一层——钩 抽不出来是因为详表「效果配置」列是纯中文描述、
            // 没有可解析 token,不是因为管线看了 ⚠/✅ 标记本身。真要挡住「有人把 ⚠ 改成 ✅」
            // 那种手滑,得看 tools/pipeline/tests/test_export_chars.py 的
            // test_gou_row_is_not_marked_implemented——那条直接读详表的标记列。
            var graph = RealGraph();
            Assert.That(() => graph.Get("钩"), Throws.Exception,
                "钩 不该出现在字表里");
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
