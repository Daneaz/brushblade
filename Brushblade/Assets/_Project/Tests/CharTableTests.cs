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

        /// <summary>五系叠字链的稀有度阶梯:部件白 / 纯 2 叠金 / 纯 3 叠橙 / 纯 4 叠红。
        ///
        /// ⚠ **这条是 ConfigLoaderTests.ShippedCharsJson_LoadsFiveElementLadders 的工装副本。**
        /// 那个文件因为引了 UnityEngine.Application(streamingAssetsPath)被
        /// tools/coretests/*.csproj **显式排除**,只有 Unity Test Runner 能跑 ——
        /// 于是 2026-08-25 字表重构把阶梯从「2叠紫/3叠金」上调成「2叠金/3叠橙」时,
        /// 工装全绿而编辑器里那条红着,直到用户手动跑 Test Runner 才发现。
        /// 本条读真实 chars.json 走 TestContext.TestDirectory,两边都能跑,把盲区堵上。
        /// 改阶梯时**两处一起改**。</summary>
        [Test]
        public void RealConfig_StackedLadders_FollowTheRarityStep()
        {
            var graph = RealGraph();
            var ladders = new[]
            {
                new[] { "金", "鍂", "鑫", "\ue626" },
                new[] { "木", "林", "森", "\ue625" },
                new[] { "水", "沝", "淼", "㵘" },
                new[] { "火", "炎", "焱", "燚" },
                new[] { "土", "圭", "垚", "㙓" },
            };
            var rarities = new[]
            {
                CardRarity.White, CardRarity.Gold, CardRarity.Orange, CardRarity.Red,
            };
            foreach (var ladder in ladders)
                for (int i = 0; i < ladder.Length; i++)
                    Assert.That(graph.Get(ladder[i]).Rarity, Is.EqualTo(rarities[i]), ladder[i]);
        }

        [Test]
        public void RealConfig_FormerXiangShengCharStoresFinalValue()
        {
            // 2026-08-25 字表重构:随「3 部件 → 橙档」升档,全体锚点 200 → 240(基础值 40,×3=120)。
            // 2026-09-02:相生 ×3 取消(等值改写),焚 的配置值直接就是实战值 120,不再是基础值 40。
            Assert.That(RealGraph().Get("焚").Rarity, Is.EqualTo(CardRarity.Orange));
            var aoe = RealGraph().Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(aoe.Value, Is.EqualTo(120), "相生取消后,配置值必须等于实战值");
        }

        [Test]
        public void RealConfig_P0UnlockedWordsAreLoadable()
        {
            var graph = RealGraph();
            // 2026-08-14:溺 / 埋 / 坑 随用户裁定移出字表,从本列表删去。
            // 2026-08-14 第二批裁定移出 锯 / 磐 / 巍,从本列表删去。
            // 2026-08-14 第三批:润 / 滋 移出。
            // 2026-08-25 字表重构:洼 / 凝 / 崊 / 崟 / 漜 移出字表,换成留表的同系字。
            foreach (var id in new[] { "淋", "沐", "冰", "冻",
                                       "藤", "淡", "浴", "冷",
                                       "铠", "垚", "圭", "塔" })
                Assert.That(graph.Get(id), Is.Not.Null, $"{id} 应已收录");
        }

        [Test]
        public void RealConfig_KaiIsDefenseFive()
        {
            var effect = RealGraph().Get("铠").Effects
                .First(e => e.Kind == EffectKind.DefenseBuff);
            // 2026-08-14 T9:12 → 5。金系不该在防御轴上压过同档土系(崟 = 6),
            // 差额换成了单攻 80 —— 铠 现在是「带一点甲的金系攻击字」,不是防御字。
            Assert.That(effect.Value, Is.EqualTo(5));
        }

        /// <summary>6 个护甲字的点数(spec §6.2 的折算表:旧减伤% × 0.6)。
        /// 逐字钉住而不是只钉 铠 —— 折算表是设计裁定,漏改一个字不会有别的测试红。</summary>
        [Test]
        public void RealConfig_DefenseChars_CarryTheirPoints()
        {
            var graph = RealGraph();
            var expected = new Dictionary<string, int>
            {
                // 2026-08-14 T9:点数 ×0.65,腾出的预算换成各自的单攻(总预算守恒)。
                // 2026-08-14 第二批裁定移出 巍(2)/ 磐(4)。
                // 2026-08-25 字表重构:崟 / 崊 / 漜 移出字表(三张护甲字与 铠 同质),
                // DefenseBuff 的载体自此只剩 铠 一张 —— 少一张就再没有横向对照,故一并断唯一性。
                ["铠"] = 5,
            };
            foreach (var pair in expected)
            {
                var buff = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.DefenseBuff);
                Assert.That(buff.Value, Is.EqualTo(pair.Value), $"「{pair.Key}」护甲点数");
            }
            Assert.That(graph.All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Any(e => e.Kind == EffectKind.DefenseBuff)), Is.EqualTo(expected.Count),
                "护甲字的全集就是上表;新增载体时把它加进来一起钉");
        }

        [Test]
        public void RealConfig_NoCharCarriesTargetAllHealOverTime()
        {
            // 2026-08-14 第三批:润 / 滋 移出字表,群体持续治疗(targetAll HoT)自此无载体。
            // 本测试原是防「ConfigLoader.ParseEffects 不接 turns/targetAll」回归的 ——
            // turns 那一半改由 沐 继续钉(见 RealConfig_MuIsHealOverTimeSingleTargetThreeTurns),
            // targetAll 那一半在字表里没有靶子了,先钉住空集:新字带 targetAll HoT 时本条会红。
            var graph = RealGraph();
            Assert.That(graph.All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.HealOverTime && e.TargetAll), Is.False,
                "群体持续治疗当前应无载体");
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
                // 2026-08-15 金系批量挂战意(计 0.10)。穿透点数不动 —— 它是防御轴的量,不参与战意计价。
                // 2026-08-25 字表重构:锥 转攻击型召唤、錰 移出字表,穿透伤害字只剩 刺;
                // 刺 随升蓝档 135 → 100(蓝档单攻锚点 130,穿透 15 与偷袭各占一份预算)。
                ["刺"] = (100, 15),
            };
            foreach (var pair in expected)
            {
                var hit = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.DamageSingle);
                Assert.That(hit.Value, Is.EqualTo(pair.Value.Damage), $"「{pair.Key}」基础值(含固化的 +15%)");
                Assert.That(hit.Pierce, Is.EqualTo(pair.Value.Pierce), $"「{pair.Key}」穿透点数");
            }
        }

        [Test]
        public void RealConfig_BacklineChars_CanStrikeBackline()
        {
            // 偷袭(无视敌方前排)在 2026-08-25 刺 改贯穿之后一度**零字使用** —— 引擎、管线、
            // 字符串表三处都还在,只是没有载体,漏配了不会有任何东西变红。
            // 2026-09-02 按字意重新装配四张:砸(重物下击,抛物线越过前排)、冷(寒气弥漫)、
            // 熣(火光晃眼,光照不被挡)、刲(割取、刺杀,潜入取要害)。数值一概不动。
            // ⚠ 两面都要扫(2026-09-02 双方向合流):砸/冷 是水/土系,双方向改造把它们的伤害
            // 搬进了 AttackEffects —— 偷袭本来就是攻击属性,搬过去反而是它该在的位置。
            // 只扫 .Effects 会让这条不变量对全部 28 张双方向字半盲(熣/刲 是火/金系没改,
            // 所以只扫单面时那两个照样绿,失效是**部分**的、更难发现)。
            var graph = RealGraph();
            var expected = new[] { "砸", "冷", "熣", "刲" };
            foreach (var id in expected)
            {
                var def = graph.Get(id);
                var hit = def.Effects.Concat(def.AttackEffects)
                    .First(e => e.Kind == EffectKind.DamageSingle);
                Assert.That(hit.CanStrikeBackline, Is.True, $"「{id}」应能直接点后排");
            }

            // 全集也钉住:偷袭是稀缺的战术位,新增载体时把它加进上表一起钉。
            var carriers = graph.All
                .Where(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>())
                    .Any(e => e.CanStrikeBackline))
                .Select(c => c.Id).ToList();
            Assert.That(carriers.Count, Is.EqualTo(expected.Length), "偷袭字的全集就是上表");
        }

        [Test]
        public void RealConfig_Ci_IsSkewerNotBackline()
        {
            // 刺 的「够到后排」走的是贯穿几何(先点前排、串到同列后排),不是偷袭 ——
            // 两条路径并存,别在装配偷袭时顺手把它也标上:那会让刺可以直接点后排,
            // 而它是教程演示字,首层单怪的一击清场口径建立在「主目标在前排」之上。
            var hit = RealGraph().Get("刺").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(hit.Shape, Is.EqualTo(TargetShape.Skewer));
            Assert.That(hit.CanStrikeBackline, Is.False, "刺 靠贯穿够后排,不是偷袭");
        }

        [Test]
        public void RealConfig_SummonPassiveChars_CarryTheirPassive()
        {
            // passive 若没从 JSON 传到 EffectDef,这些字照常能召唤,但被动会静默消失
            var graph = RealGraph();
            var expected = new Dictionary<string, Action<SummonPassive>>
            {
                // 2026-08-25:荆 改前排肉盾后让出 Ranged,楸 接手(远程挂灼烧,同 灶/烓 的旧定位)
                ["楸"] = p => { Assert.That(p.OnHitBurn, Is.EqualTo(1)); Assert.That(p.OnHitBurnAll, Is.False);
                                Assert.That(p.Ranged, Is.True, "远程唯一载体"); },
                ["桤"] = p => Assert.That(p.Speed, Is.EqualTo(150)),
                // 2026-08-25 字表重构:召唤定位由配方里的第二个五行部件决定,
                // 被动跟着定位走(spec §3)。烓 / 灶 移出后 OnHitBurnAll 无载体,
                // 桃(HealAlly)的位子由新增的 杖 接手。
                // 荆(2026-08-25 二次调整):纯反伤肉盾 —— 攻 0,输出全靠反伤。
                // Thorns 的单位此时已是「受到伤害的百分比」,50 = 反弹一半。
                ["荆"] = p => { Assert.That(p.Thorns, Is.EqualTo(50)); Assert.That(p.Ranged, Is.False, "改前排肉盾,不再远程");
                                Assert.That(p.Taunt, Is.True, "嘲讽是「挨打即输出」成立的前提"); },
                ["蕉"] = p => Assert.That(p.OnHitSlowPercent, Is.EqualTo(50)),
                ["杖"] = p => Assert.That(p.HealAlly, Is.EqualTo(10)),
                ["藤"] = p => { Assert.That(p.OnHitFreezeChance, Is.EqualTo(10)); Assert.That(p.OnSummonFreeze, Is.EqualTo(0)); },
                ["锥"] = p => { Assert.That(p.Shape, Is.EqualTo(TargetShape.Volley)); Assert.That(p.Shots, Is.EqualTo(2)); },
                ["剑"] = p => { Assert.That(p.Shape, Is.EqualTo(TargetShape.Sweep)); Assert.That(p.ShapePercent, Is.EqualTo(50)); },
                ["枪"] = p => { Assert.That(p.Shape, Is.EqualTo(TargetShape.Skewer)); Assert.That(p.ShapePercent, Is.EqualTo(70)); },
            };
            foreach (var pair in expected)
            {
                var summon = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.Summon);
                Assert.That(summon.Passive, Is.Not.Null, $"「{pair.Key}」应带被动");
                pair.Value(summon.Passive);
            }

            // 碉/堡(2026-08-25):与 荆 同型的纯反伤肉盾,攻 0、反弹 50%。
            // 嘲讽只给 堡(蓝)与 荆(紫) —— 白档的 碉 拿不到全套坦克包。
            // ⚠ 2026-09-02:曾因双方向重配把 Summon 搬进 AttackEffects,这里一度改读那一侧;
            // 同日用户拍板「召唤字不做双方向」后又搬回 Effects,故恢复成与上面同源的读法。
            var diaoSummon = graph.Get("碉").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(diaoSummon.Passive.Thorns, Is.EqualTo(50));
            Assert.That(diaoSummon.Passive.Taunt, Is.False, "白档不给嘲讽");
            var baoSummon = graph.Get("堡").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(baoSummon.Passive.Thorns, Is.EqualTo(50));
            Assert.That(baoSummon.Passive.Taunt, Is.True);

            // 荆 的攻击力必须是 0:它的定位就是「靠挨打反伤输出」,给它补基础攻
            // 等于把这条设计悄悄抹平(2026-08-25 用户拍板)。
            var jingSummon = graph.Get("荆").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(jingSummon.SummonAttack, Is.EqualTo(0), "荆 靠反伤输出,不该有基础攻");
            Assert.That(jingSummon.Value, Is.EqualTo(330), "血量翻倍换掉攻击力");
        }

        [Test]
        public void RealConfig_SummonCharIsTheCastingCharItself()
        {
            // 2026-08-15:召唤物在场上显示 summonChar,原先全表填「木」/「火」,
            // 一排召唤物长得一模一样,玩家分不出哪只是梅哪只是荆。
            // ConfigLoader 的默认值又恰好是「木」—— 新字漏填就静默回到那个样子,故钉死。
            // 2026-09-02 双方向重配(Task 11):碉/堡/塔 的 Summon 搬进了 AttackEffects,
            // 扫描范围跟着盖住两个列表,否则这三个字会被这条不变量悄悄漏掉。
            var graph = RealGraph();
            foreach (var def in graph.All)
                foreach (var effect in def.Effects.Concat(def.AttackEffects))
                    if (effect.Kind == EffectKind.Summon)
                        Assert.That(effect.SummonChar, Is.EqualTo(def.Id),
                            $"「{def.Id}」的召唤物应显示本字,而不是「{effect.SummonChar}」");
        }

        [Test]
        public void RealConfig_GuiGrantsSummonShield()
        {
            var graph = RealGraph();
            var summon = graph.Get("桂").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.SummonShield, Is.EqualTo(60));
            Assert.That(summon.SummonCount, Is.EqualTo(3), "2026-08-25 升橙档:2 只 → 3 只");
        }

        [Test]
        public void RealConfig_JiaoIsSlowSummon()
        {
            // 2026-08-25 用户拍板:蕉 改控制型(出手减速),不再是灼烧型。
            // ⚠ 这是「第二五行部件定型」的**例外** —— 艹+焦 的 焦 属火,按规则该是灼烧型。
            // 例外由用户指定,规则本身不变(其余木系召唤仍按部件定型)。
            var graph = RealGraph();
            var summon = graph.Get("蕉").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.SummonCount, Is.EqualTo(2));
            Assert.That(summon.Value, Is.EqualTo(110), "控制型系数 1.0,取紫档召唤锚点原值");
            Assert.That(summon.SummonAttack, Is.EqualTo(50));
            Assert.That(summon.Passive.OnHitSlowPercent, Is.EqualTo(50));
            Assert.That(summon.Passive.OnHitSlowTurns, Is.EqualTo(2));
            Assert.That(summon.Passive.OnHitBurn, Is.EqualTo(0), "改控制型后不该还挂着灼烧");
        }

        [Test]
        public void RealConfig_ArmorBreakChars_CarryTheirPoints()
        {
            // ⚠ 语义反转(2026-08-12,E-b4 T3):value 从**回合数**(全部 6 字 = 2)
            // 变成**削减的护甲点数**。档位依据是战例二「三张蓝档削光坚壁 Boss(60)」。
            var graph = RealGraph();
            // 2026-08-14 第二批裁定移出字表:熔 / 锤(均为 20 点)。
            // 2026-08-25 字表重构:溶 / 破 移出(与 碎 / 溃 同质);碎 升蓝 10 → 20、
            // 溃 降白 20 → 10 —— 两个字的点数正好对调,破甲轴仍是「白 10 / 蓝 20」两级。
            // 溃/碎(2026-09-02 双方向重配):破甲随攻击面一起搬进 AttackEffects,不再挂在
            // Effects 上 —— 点数本身不变(10 / 20),读取位置跟着改。
            var kui = graph.Get("溃").AttackEffects.First(e => e.Kind == EffectKind.ArmorBreak);
            Assert.That(kui.Value, Is.EqualTo(10), "「溃」破甲削减点数");
            var sui = graph.Get("碎").AttackEffects.First(e => e.Kind == EffectKind.ArmorBreak);
            Assert.That(sui.Value, Is.EqualTo(20), "「碎」破甲削减点数");
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

            // 2026-08-23 用户拍板:斩杀字的阈值统一 25%,差别只在直杀 / 双倍
            // 2026-08-25 字表重构:镰 移出字表(词组归零),斩杀只剩 铡(直杀)/ 剿(双倍)两张。
            // 2026-08-25 用户拍板:剿 由全体改**单体**斩杀并升蓝档
            Assert.That(graph.Get("剿").Rarity, Is.EqualTo(CardRarity.Blue));
            var jiao = graph.Get("剿").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(jiao.ExecuteBelowPercent, Is.EqualTo(25));
            Assert.That(jiao.ExecuteKills, Is.False, "残血加伤,不是处决");
            Assert.That(jiao.Value, Is.EqualTo(100), "蓝档单攻锚点 130 减去斩杀与战意的计价");
            Assert.That(graph.Get("剿").Effects.Any(e => e.Kind == EffectKind.DamageAll), Is.False,
                "改单体后不该还留着全体那条");

            // 铡 同时接了「对流血目标翻倍」——与 劈 的流血组成金系的铺/收一对
            Assert.That(zha.DoubleVs, Is.EqualTo(DamageCondition.Bleeding));
        }

        [Test]
        public void RealConfig_DispelChars_CarryTheirCounts()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("灭").Effects.First(e => e.Kind == EffectKind.Dispel).Value,
                Is.EqualTo(-1), "灭清全部");
            // 2026-08-14 第二批裁定移出字表:削(清一条)/ 刮(清全部)。
            // 「清一条」的载体现在只剩 淡(全体各清一条),见下。
            // 2026-09-02 双方向重配:淡 的驱散随攻击面搬进 AttackEffects,治疗面回归纯治疗。
            var dan = graph.Get("淡").AttackEffects.First(e => e.Kind == EffectKind.Dispel);
            Assert.That(dan.TargetAll, Is.True, "淡是全体各清一条");
            Assert.That(dan.Value, Is.EqualTo(1));
        }

        [Test]
        public void RealConfig_ImmunityAndCleanseAndRevive()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("杜").Effects.First(e => e.Kind == EffectKind.Immunity).Value,
                Is.EqualTo(2));
            // 2026-08-14 第二批裁定移出字表:塞(免疫 1)/ 岿(免疫 1 + 净化)。
            // 免疫的载体现在只剩 杜 一张;「免疫 + 净化」的组合无字使用。
            Assert.That(graph.Get("浴").Effects.Any(e => e.Kind == EffectKind.Cleanse), Is.True);
            // 2026-08-25 字表重构:活 移出字表(词组归零),复活机制移交 浴(「浴火重生」)——
            // 浴 于是同时是净化与复活的载体,蓝档一张纯功能牌。
            // 2026-09-02 双方向重配:浴 拆成治疗面(HealSelf + Cleanse)/攻击面(Revive +
            // DamageSingle)——复活随攻击面搬进 AttackEffects,净化仍留在治疗面。
            Assert.That(graph.Get("浴").AttackEffects.First(e => e.Kind == EffectKind.Revive).Value,
                Is.EqualTo(1));
            Assert.That(graph.All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>())
                    .Any(e => e.Kind == EffectKind.Revive)),
                Is.EqualTo(1), "复活当前只有 浴 一个载体");
        }

        [Test]
        public void RealConfig_ManualRecipeBeatsSupplementaryPlaneIds()
        {
            // 2026-08-14:塞 随第二批裁定移出字表(它的 MANUAL_RECIPES 条目保留待复活),
            // 本测试改由 湮 单独守住「手工配方优先于增补平面 IDS」这条不变量。
            // 2026-09-01 二级拆解:湮 的手工配方从 氷+土 引回 氷+垔(垔 = 覀+土,
            // 见 RealConfig_JingAndYanRouteThroughTheMiddleLayer),不变量本身未变。
            var graph = RealGraph();
            Assert.That(graph.Get("湮").Recipe, Is.EqualTo(new[] { "氵", "垔" }));
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
                Is.EqualTo(80), "2026-08-15:再挂 2 层 DOT(当量 60),80 + 60 + 致盲 60 = 紫档 200");
            Assert.That(graph.Get("熣").Effects.Any(e => e.Kind == EffectKind.BurnSingle), Is.True,
                "火系批量改造:攻击的同时挂 DOT");

            // 2026-08-14 第二批裁定移出 烟(全体致盲 30/1 回合)——Blind 的载体现在只剩 熣 一张,
            // targetAll 那一半的守卫改由 DamageVariantTests.NeedsTarget_BlindAll_False_BlindSingle_True
            // 用构造的 CharDef 顶着。
        }

        [Test]
        public void RealConfig_SilenceHasNoCarrier()
        {
            // 2026-08-14 第二批裁定移出 锁(Silence)。引擎实现与 StatusKind 都还在
            // (BattleEngine 的沉默分支有自己的单元测试),这里钉住空集:
            // 哪天有新字接手,本条会红,提醒把数值守卫加回来。Reflect 已由 铸 接手,见下。
            Assert.That(RealGraph().All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Silence), Is.False, "Silence 当前应无载体");
        }

        /// <summary>壁(2026-08-25 字表重构)接手 铸 移出后无载体的 Reflect。
        /// 换载体的理由是语义:「墙壁反弹」比「铸造」贴 —— 而且 Reflect 是防御向机制,
        /// 挂在土系防御字上比挂在金系攻击字上读得通。
        /// 2026-09-02 双方向重配(Task 11):护盾 40 → 49(绿档满值 70 × 0.7,带反弹附加特性),
        /// 攻击面(DamageSingle 49 + Reflect 30)另有 DualDirectionTests 覆盖,这里只钉护盾面。</summary>
        [Test]
        public void RealConfig_BiCarriesReflect()
        {
            var bi = RealGraph().Get("壁");
            Assert.That(bi.Rarity, Is.EqualTo(CardRarity.Green));
            Assert.That(bi.Element, Is.EqualTo(Element.Earth));
            Assert.That(bi.Recipe, Is.EqualTo(new[] { "辟", "土" }));
            var reflect = bi.Effects.Single(e => e.Kind == EffectKind.Reflect);
            Assert.That(reflect.Value, Is.EqualTo(30));
            Assert.That(reflect.Turns, Is.EqualTo(2), "turns 被静默丢掉的话会是 0——挂上去当场到期");
            Assert.That(bi.Effects.Single(e => e.Kind == EffectKind.Shield).Value, Is.EqualTo(49));
            Assert.That(RealGraph().All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Reflect)), Is.EqualTo(1), "反弹当前只有 壁 一个载体");
        }

        /// <summary>剁 是全表唯一的多段字,数值走 spec §4.4(b) 的**多段补偿规则**
        /// (2026-08-13 E-b4/E-b5 T8 落地):
        ///
        /// > 多段字的总基础值 = 同档单段字 × (1 + 0.1 × (段数 − 1))
        ///
        /// 点数护甲对多段有天然惩罚 —— 每段各扣一次 DEF。紫档单段锚点 200,
        /// 补偿后 剁 = 110 × 2 = 220 总(补偿前是 100 × 2 = 200)。
        /// 面对 DEF 30 的敌人:剁 打出 (110−30)×2 = 160,同档单段 220 伤打出 190,
        /// 补偿前只打出 140。**刻意不追求完全拉平** —— 多段在「两次过斩杀阈值」
        /// 「两次触发受击后效」上有独立收益,拉平会让它净赚。
        ///
        /// ⚠ **2026-08-13 用户裁定:以公式为准,不是 spec 正文的 240。**
        /// spec §4.4(b) 的正文与它自己上一行的公式矛盾(240 = 200×1.2,公式给 200×1.1=220)。
        /// 公式是有原理的那个:补偿存在是因为 N 段字比单段多吃 `(N−1)` 次 DEF,
        /// 系数**必须**正比于 `段数 − 1` —— 单段字代入才得 ×1.0(不需要补偿)。
        /// 240 相当于 `1 + 0.1 × 段数`,那会让单段字也白拿 +10%。</summary>
        [Test]
        public void RealConfig_DuoIsTwoSegments()
        {
            var graph = RealGraph();
            var duo = graph.Get("剁").Effects.First(e => e.Kind == EffectKind.DamageSingle);
            Assert.That(duo.HitCount, Is.EqualTo(2));
            Assert.That(duo.Value, Is.EqualTo(70), "2026-08-25 补流血后每段 100 → 70");
            // 2026-08-25 补流血(20/回合 ×3 = 60)后重新分配:140 伤 + 60 流血 = 200,
            // 战意仍计 0.10 —— 与多段补偿 1.1 大致对冲。
            Assert.That(duo.Value * duo.HitCount, Is.EqualTo(140), "两段合计 140");
            Assert.That(RealGraph().Get("剁").Effects.Any(e => e.Kind == EffectKind.Bleed), Is.True,
                "剁 是流血的紫档载体");
        }

        [Test]
        public void RealConfig_DodgeHasNoCarrier()
        {
            // 2026-08-25 字表重构:柳 移出字表(词组归零),Dodge 自此无载体。
            // 引擎实现与命中率算式都还在(有自己的单元测试),这里钉住空集:
            // 哪天有新字接手,本条会红,提醒把数值守卫加回来 —— 与 Silence 同口径。
            Assert.That(RealGraph().All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Summon && e.Passive != null && e.Passive.Dodge > 0),
                Is.False, "闪避当前应无载体");
        }

        [Test]
        public void RealConfig_JiaoCarriesBurnAndNoDecay()
        {
            // 2026-08-25 字表重构:炑 移出字表,不灭机制移交 焦(「烧焦的痕迹不褪」)。
            // 焦 是白档,故层数压到 1(当量 20)+ 不灭(计 40)= 白档锚点 60。
            var jiao = RealGraph().Get("焦");
            Assert.That(jiao.Rarity, Is.EqualTo(CardRarity.White));
            // 断全序列(而非只断第一条)——否则行尾静默多挂一个效果不会被发现。
            // BurnNoDecay 排在最后是 extract_values 的 VALUELESS_EFFECTS 统一追加所致,
            // 与结算无关(不灭是标记位,不参与顺序敏感的兑现链)。
            Assert.That(jiao.Effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.BurnNoDecay,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(jiao.Effects[0].Value, Is.EqualTo(1));
            Assert.That(RealGraph().All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.BurnNoDecay)), Is.EqualTo(1), "不灭当前只有 焦 一个载体");

            // 流血梯度:劈 白 10 / 剁 紫 20,收割者是 铡。
            // 2026-08-25 曾是三档(锋 蓝 15 居中);2026-08-29 用户拍板把 锋 连同其余六张
            // buff 字一起去掉对敌效果、回归纯 buff,中间那一档因此空出来 —— 是已知缺口,
            // 不是漏钉。要补就再找一张蓝档金系的字挂 Bleed 15。
            var bleeders = RealGraph().All
                .Where(c => (c.Effects ?? Array.Empty<EffectDef>()).Any(e => e.Kind == EffectKind.Bleed))
                .ToDictionary(c => c.Id,
                    c => c.Effects.First(e => e.Kind == EffectKind.Bleed).Value);
            Assert.That(bleeders, Is.EquivalentTo(new Dictionary<string, int>
            {
                ["劈"] = 10, ["剁"] = 20,
            }), "铺流血的梯度就是这两张;新增载体时把它加进来一起钉");
        }

        [Test]
        public void RealConfig_ZaoSettlesAfterRaisingPotency()
        {
            var effects = RealGraph().Get("燥").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.BurnPotency, EffectKind.BurnSettleNow,
            }), "顺序错了立即结算就吃不到自己抬的系数");
            Assert.That(effects[0].Value, Is.EqualTo(3));   // 层数,不吃 ×10;2026-08-15 火系「攻击 + DOT」批量改造 后 2 → 3
            Assert.That(effects[1].Value, Is.EqualTo(10));  // 灼烧系数,吃 ×10
        }

        [Test]
        public void RealConfig_ZhaCarriesDetonate()
        {
            // 2026-08-25 字表重构:灱 移出字表,引爆机制移交 炸(语义直接就是「引爆」)。
            // 炸 不自带灼烧层 —— 它是**收状态**的字,层数由 灼/热/烧/爆 铺;
            // 蓝档预算:单体 90 + 引爆(计 0.30 × 130 ≈ 40)= 130。
            // 2026-08-25 用户拍板:炸 改 AOE —— 与 爆(全体 50 + 全体灼烧 1)成对,爆铺、炸收
            var effects = RealGraph().Get("炸").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.DamageAll, EffectKind.Detonate,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(effects[0].Value, Is.EqualTo(50));
            // 2026-08-26:引爆必须是**全体**(详表:「引爆全部剩余灼烧」)。落成单体会让
            // 一张 AOE 字反过来要求玩家选目标 —— 交互与语义两头都错
            Assert.That(effects[1].TargetAll, Is.True, "炸 是全体引爆,不是只炸主目标");
            Assert.That(BattleEngine.NeedsTarget(RealGraph().Get("炸")), Is.False, "全体字不进选目标态");
            Assert.That(RealGraph().All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Detonate)), Is.EqualTo(1), "引爆当前只有 炸 一个载体");
        }

        [Test]
        public void RealConfig_NewBurnCharsAddNoLeafParts()
        {
            // 三个字的部件(火 木 喿 刀)全部已在表中,本批不该新增任何叶子——
            // 直接断配方本身,而不是断「部件能在表里查到」:build_chars 会自动把任何
            // 配方部件补成叶子条目写进 chars.json,查得到不代表它是本批之前就已存在的字。
            // 2026-08-25 字表重构:炑 / 灱 移出字表,不灭与引爆分别移交 焦 / 炸 ——
            // 两个接手的字本来就在表里,同样不新增叶子。
            var graph = RealGraph();
            Assert.That(graph.Get("焦").Recipe, Is.EqualTo(new[] { "隹", "灬" }));
            Assert.That(graph.Get("燥").Recipe, Is.EqualTo(new[] { "火", "喿" }));
            Assert.That(graph.Get("炸").Recipe, Is.EqualTo(new[] { "火", "乍" }));
        }

        [Test]
        public void RealConfig_NewBurnCharsHaveExpectedRarity()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("焦").Rarity, Is.EqualTo(CardRarity.White));
            Assert.That(graph.Get("燥").Rarity, Is.EqualTo(CardRarity.Purple));
            Assert.That(graph.Get("炸").Rarity, Is.EqualTo(CardRarity.Blue));
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

        /// <summary>二级拆解(2026-09-01):12 个部件有了配方,但仍然是部件。
        /// 两个谓词正交是这次改动的支点 —— 谁把 IsComponent 又推导回 IsLeaf,这条就红。</summary>
        [Test]
        public void RealConfig_ComponentsWithRecipes_AreStillComponents()
        {
            var graph = RealGraph();
            var expected = new (string Part, string[] Recipe)[]
            {
                ("秋", new[] { "禾", "火" }), ("崔", new[] { "山", "隹" }),
                ("岂", new[] { "山", "己" }), ("荅", new[] { "艹", "合" }),
                ("列", new[] { "歹", "刂" }), ("喿", new[] { "品", "木" }),
                ("烝", new[] { "丞", "灬" }), ("则", new[] { "贝", "刂" }),
                ("朵", new[] { "几", "木" }), ("切", new[] { "七", "刀" }),
                ("茾", new[] { "艹", "开" }), ("垔", new[] { "覀", "土" }),
            };
            foreach (var (part, recipe) in expected)
            {
                var def = graph.Get(part);
                Assert.That(def.Recipe, Is.EqualTo(recipe), $"{part} 的配方不对");
                Assert.That(def.IsComponent, Is.True, $"{part} 有了配方,但它仍然是部件");
                Assert.That(def.IsLeaf, Is.False, $"{part} 该能拆");
            }
        }

        /// <summary>新部件是终点,只做两级(2026-09-01 拍板)。</summary>
        [Test]
        public void RealConfig_NewComponentsAreTerminal()
        {
            var graph = RealGraph();
            foreach (var part in new[] { "己", "合", "歹", "品", "丞", "贝", "几", "七", "开", "覀" })
            {
                Assert.That(graph.TryGet(part, out var def), Is.True, $"{part} 不在字表里");
                Assert.That(def.IsComponent, Is.True, $"{part} 该是部件");
                Assert.That(def.IsLeaf, Is.True, $"{part} 是终点,不该有配方");
            }
        }

        /// <summary>荆 / 湮 的一级配方引回中间层(2026-09-01 用户复核后拍板,spec §六)。
        /// 代价是这两个字拆一次的产出从 2 个五行部件降到 1 个,已明确接受。</summary>
        [Test]
        public void RealConfig_JingAndYanRouteThroughTheMiddleLayer()
        {
            var graph = RealGraph();
            Assert.That(graph.Get("荆").Recipe, Is.EqualTo(new[] { "茾", "刂" }));
            Assert.That(graph.Get("湮").Recipe, Is.EqualTo(new[] { "氵", "垔" }));
        }

        /// <summary>74 个可出牌字一个都不是部件;部件一个都不是可出牌字。
        /// 库/池归属的 9 处判据全压在这条上。</summary>
        [Test]
        public void RealConfig_PlayableCharsAndComponentsDoNotOverlap()
        {
            var graph = RealGraph();
            int playable = 0, components = 0;
            foreach (var def in graph.All)
            {
                if (def.Effects.Count > 0)
                {
                    playable++;
                    Assert.That(def.IsComponent, Is.False, $"{def.Id} 有效果,不该是部件");
                }
                else
                {
                    components++;
                    Assert.That(def.IsComponent, Is.True, $"{def.Id} 没效果,该是部件");
                }
            }
            // 部件 57 → 69:12 条 COMPONENT_RECIPES 里 10 个原料是全新终点部件,另外 2 个
            // (茾、垔)本身也是全新条目——荆/湮 之前的一级配方绕开了它们(见
            // tools/pipeline/tests/test_export_chars.py::test_real_table_entry_count)。
            Assert.That(playable, Is.EqualTo(74));
            Assert.That(components, Is.EqualTo(69));
        }

        /// <summary>叠字前置不因部件有了配方而收紧(spec §一列出的三个回归之一)。
        /// 把 IsComponent 换回 IsLeaf,合 蒸 会开始要求玩家「拥有 烝」这张收集卡,
        /// 而 烝 根本不在收集图鉴里 —— 蒸 变得永远合不出来,且无声。</summary>
        [Test]
        public void RealConfig_ComponentWithRecipe_IsNotAPrerequisite()
        {
            var graph = RealGraph();
            var ownNothing = new List<string>();
            // 蒸 = 艹 + 烝,烝 现在有配方了,但它是部件,不该成为前置
            Assert.That(MetaRules.PrerequisitesMet("蒸", graph, ownNothing), Is.True);
            Assert.That(MetaRules.PrerequisitesMet("荆", graph, ownNothing), Is.True, "荆 = 茾 + 刂,茾 是部件");
            // 对照:森 = 木 + 林,林 是可出牌字 → 仍然是前置
            Assert.That(MetaRules.PrerequisitesMet("森", graph, ownNothing), Is.False);
            Assert.That(MetaRules.PrerequisitesMet("森", graph, new List<string> { "林" }), Is.True);
        }

        /// <summary>登塔起手部件池仍然收得到带配方的部件(spec §一列出的三个回归之一)。
        /// 把 IsComponent 换回 IsLeaf,烝 不再算可掉落部件,蒸 的原料就掉不出来了。</summary>
        [Test]
        public void RealConfig_ComponentWithRecipe_StillCountsAsDeckComponent()
        {
            var graph = RealGraph();
            var parts = new List<string>(MetaRules.DeckComponents(new List<string> { "蒸" }, graph));
            Assert.That(parts.Contains("烝"), Is.True, "烝 有了配方,但它仍是 蒸 的可掉落部件");
            Assert.That(parts.Contains("艹"), Is.True);
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
        // ---- 拆出来的中间字要合得回去(2026-09-03 用户报的 bug,真实字表)----

        [Test]
        public void RealConfig_ComposableSet_CoversWhatDismantlingProduces()
        {
            // 用户原话:「蕉 = 焦 + 艹,拆后获得 焦 和 艹,焦 可以进一步拆为 隹 + 灬,
            // 但 隹 + 灬 却无法再合成 焦。」根因是 焦 不在出阵列表里 —— 闭包补上这一层。
            // 钉在**真实字表**上:夹具图谱证明不了 蕉/焦/隹/灬 这四个字的配方还长这样。
            var set = ForgeEngine.ComposableSet(RealGraph(), new[] { "蕉" });

            Assert.That(set.Contains("焦"), Is.True, "拆 蕉 就能拿到 焦,那就该合得回去");
            Assert.That(set.Contains("艹"), Is.True);
            Assert.That(set.Contains("隹"), Is.True, "焦 再拆一层的产物 —— 闭包是递归的");
            Assert.That(set.Contains("灬"), Is.True);
        }

    }
}
