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
            // 焚含木生火,配置表填基础值 30,引擎结算时 ×3 = 90
            // (2026-08-15 火系「攻击 + DOT」批量改造:原 70(=210)已占满金档全体锚点 200,3 层全体是白送的)
            var aoe = RealGraph().Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(aoe.Value, Is.EqualTo(30), "相生字必须填基础值,不是最终值");
        }

        [Test]
        public void RealConfig_P0UnlockedWordsAreLoadable()
        {
            var graph = RealGraph();
            // 2026-08-14:溺 / 埋 / 坑 随用户裁定移出字表,从本列表删去。
            // 2026-08-14 第二批裁定移出 锯 / 磐 / 巍,从本列表删去。
            // 2026-08-14 第三批:润 / 滋 移出。
            foreach (var id in new[] { "淋", "沐", "冰", "冻",
                                       "藤", "洼", "凝", "冷",
                                       "铠", "崊", "崟", "漜" })
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
                // 铠 额外降到 5(金系不压土系),漜 同时去掉了 Slow 2。
                // 2026-08-14 第二批裁定移出 巍(2)/ 磐(4)。
                ["崟"] = 6, ["铠"] = 5, ["崊"] = 8, ["漜"] = 10,
            };
            foreach (var pair in expected)
            {
                var buff = graph.Get(pair.Key).Effects.First(e => e.Kind == EffectKind.DefenseBuff);
                Assert.That(buff.Value, Is.EqualTo(pair.Value), $"「{pair.Key}」护甲点数");
            }
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
                // 2026-08-15 金系批量挂战意(计 0.10):105→95 / 150→135 / 460→415。
                // 穿透点数不动 —— 它是防御轴的量,不参与战意计价。
                ["锥"] = (95, 10), ["刺"] = (135, 15), ["錰"] = (415, 30),
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
                // 2026-08-14:桃(HealAlly)/ 槐(OnHitCurse)随用户裁定移出字表。
                // 这两个被动字段与引擎实现都还在,只是当前无字使用 —— 新字接手即可复活。
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
        public void RealConfig_SummonCharIsTheCastingCharItself()
        {
            // 2026-08-15:召唤物在场上显示 summonChar,原先全表填「木」/「火」,
            // 一排召唤物长得一模一样,玩家分不出哪只是梅哪只是荆。
            // ConfigLoader 的默认值又恰好是「木」—— 新字漏填就静默回到那个样子,故钉死。
            var graph = RealGraph();
            foreach (var def in graph.All)
                foreach (var effect in def.Effects)
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
                // 2026-08-14 第二批裁定移出字表:熔 / 锤(均为 20 点)。
                ["碎"] = 10, ["溶"] = 15, ["破"] = 15, ["溃"] = 20,
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
            // 2026-08-14 第二批裁定移出字表:削(清一条)/ 刮(清全部)。
            // 「清一条」的载体现在只剩 淡(全体各清一条),见下。
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
            // 2026-08-14 第二批裁定移出字表:塞(免疫 1)/ 岿(免疫 1 + 净化)。
            // 免疫的载体现在只剩 杜 一张;「免疫 + 净化」的组合无字使用。
            Assert.That(graph.Get("浴").Effects.Any(e => e.Kind == EffectKind.Cleanse), Is.True);
            Assert.That(graph.Get("活").Effects.First(e => e.Kind == EffectKind.Revive).Value,
                Is.EqualTo(1));
        }

        [Test]
        public void RealConfig_ManualRecipeBeatsSupplementaryPlaneIds()
        {
            // 2026-08-14:塞 随第二批裁定移出字表(它的 MANUAL_RECIPES 条目保留待复活),
            // 本测试改由 湮 单独守住「手工配方优先于增补平面 IDS」这条不变量。
            var graph = RealGraph();
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

        /// <summary>铸(2026-08-14 第三批新增)接手 镜 移出后无载体的 Reflect。
        /// 绿档口径比旧 镜(蓝,50%/2 回合)低一档:反弹按价目表计 0.30,余下给单攻。</summary>
        [Test]
        public void RealConfig_ZhuCarriesReflect()
        {
            var zhu = RealGraph().Get("铸");
            Assert.That(zhu.Rarity, Is.EqualTo(CardRarity.Green));
            Assert.That(zhu.Element, Is.EqualTo(Element.Metal));
            Assert.That(zhu.Recipe, Is.EqualTo(new[] { "钅", "寿" }));
            var reflect = zhu.Effects.Single(e => e.Kind == EffectKind.Reflect);
            Assert.That(reflect.Value, Is.EqualTo(40));
            Assert.That(reflect.Turns, Is.EqualTo(2), "turns 被静默丢掉的话会是 0——挂上去当场到期");
            Assert.That(zhu.Effects.Single(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(65), "T9 攻击性下限:绿档 90 × 0.70");
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
            Assert.That(duo.Value, Is.EqualTo(100), "每段 100");
            // 2026-08-15 金系批量挂战意:多段补偿 220 再按战意计价 ×0.90 = 198 → 取整 200。
            Assert.That(duo.Value * duo.HitCount, Is.EqualTo(200),
                "紫档单段锚点 200 × 多段补偿 1.1 × 战意计价 0.9 ≈ 200");
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
        public void RealConfig_MuCarriesBurnAndNoDecay()
        {
            var effects = RealGraph().Get("炑").Effects;
            // 断全序列(而非只断前两条)——否则行尾静默多挂一个效果(如 Detonate)不会被发现
            // 2026-08-15 火系「攻击 + DOT」批量改造:2 → 3 层并补攻(基础 10,含木生火 ×3 = 30)。
            // BurnNoDecay 排在最后是 extract_values 的 VALUELESS_EFFECTS 统一追加所致,
            // 与结算无关(不灭是标记位,不参与顺序敏感的兑现链)。
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.DamageSingle, EffectKind.BurnNoDecay,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(effects[0].Value, Is.EqualTo(3));
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
        public void RealConfig_XiaoIsThreeStacksThenDetonate()
        {
            var effects = RealGraph().Get("灱").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.BurnSingle, EffectKind.Detonate,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            // 2026-08-15 火系「攻击 + DOT」批量改造:4 → 3 层。4 层的 DOT 当量 N(N+1)/2 × 20 = 200 已占满紫档锚点,
            // 再挂引爆就是白送;3 层 120 + 引爆 60 = 180 ≈ 200。
            Assert.That(effects[0].Value, Is.EqualTo(3));
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
