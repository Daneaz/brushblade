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
    /// 与 ConfigLoaderTests 分开:那个文件引 UnityEngine,被 dotnet 工装排除。
    ///
    /// 2026-09-05 字表调整(移出 17 字)后,下列机制在全表**无载体**,规格 §1.3 已裁定
    /// 「休眠」而非找字硬凑;引擎/管线代码原样保留,只是暂时没有字用。原先钉这些机制的
    /// 测试**改钉空集**(而不是整条删掉不留守卫)——新字挂上时这几条会红,提醒把数值/
    /// 唯一性守卫加回来;原文可从 git 历史找回(2026-09-05 终审 fix-wave 之前那个提交):
    /// - pierce(一次性穿透 EffectDef.Pierce,原唯一载体 刺;PierceBuff 是本场持久 buff,
    ///   另一条通道,随 锐 一并移出后同样休眠——见 PierceBuffCharTests 的类文档)——
    ///   原 RealConfig_PierceChars_CarryPiercePoints 改钉空集,见 RealConfig_PierceHasNoCarrier
    /// - Blind(致盲,原唯一载体 熣)—— 原 RealConfig_BlindCharsCarryTheirPercentAndTurns
    ///   改钉空集,见 RealConfig_BlindHasNoCarrier(同一裁定波及
    ///   DamageVariantTests.NeedsTarget_BlindAll_False_BlindSingle_True,见该文件)
    /// - BurnNoDecay(不灭灼烧,原唯一载体 焦)—— 原 RealConfig_JiaoCarriesBurnAndNoDecay 里
    ///   针对 焦 的那部分(其余 Bleed 梯度断言已迁到 RealConfig_BleedChars_CarryTheirGradient)
    /// - 召唤被动 OnHitSlow(原唯一载体 蕉)—— 原 RealConfig_JiaoIsSlowSummon 改钉空集,
    ///   见 RealConfig_SummonOnHitSlowHasNoCarrier
    /// - 字卡攻击形状 Skewer(原唯一载体 刺,不含召唤被动的 Skewer——枪 仍在)——
    ///   原 RealConfig_Ci_IsSkewerNotBackline 改钉空集,见 RealConfig_CardSideSkewerHasNoCarrier
    /// - Dispel「清一条 / 清全部」与 Cleanse(净化)—— 2026-09-07 字表重做 P2 把两者
    ///   **并入封禁**(design §1.3.1「净化+驱散并入封禁」,用户裁定「不保留净化」,玩家从此
    ///   没有解控手段):原来的载体 灭/湮 现在挂的是 Silence(封禁),不再挂 Dispel;
    ///   浴 随本批一并移出,原来靠它守的 Cleanse 也没了替身。见
    ///   RealConfig_DispelAndCleanseHaveNoCarrier。
    /// 反过来,以下两个机制这一批**从「无载体」变成「有载体」**,原「应无载体」的测试已
    /// 反转为断言真实载体(方法名保留,内容已不是「无载体」了——见各自方法体的说明):
    /// - DefenseBuff(点数护甲):垒(绿4)/ 杜(金9)/ 垚(橙11)/ 㙓(红13),
    ///   见 RealConfig_DefenseBuffChars_CarryTheirPoints
    /// - Silence(封禁,2026-09-05 语义从「主动机制哑火」扩到「护甲/被动/大招全禁,对 Boss
    ///   降级为护甲减半」):灭/湮/海/淋/沐/澡 六字,见 RealConfig_SilenceChars_CarryTheirTurns
    /// 上述机制引擎侧仍有单元测试覆盖(BattleEngine/StatusOps 等),这里守的只是「真实字表
    /// 里还有没有字用它」这一层。</summary>
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
                new[] { "水", "冰", "淼", "㵘" },
                new[] { "火", "炎", "焱", "燚" },
                new[] { "土", "圭", "垚", "㙓" },
            };
            // 2026-09-05:沝 移出字表,水系 2叠环换成 冰(冫+水,冫 是水组同系部件)。
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
            // 2026-09-02:相生 ×3 取消(等值改写),焚 的配置值直接就是实战值,不再是基础值。
            // 2026-09-07 字表重做 P2:焚 按新公式重新标定,120 → 108(带 doubleVs=Burning
            // 对灼烧特性,预算里扣掉了这条价目,见 RealConfig_ArmorBreakChars_CarryTheirPoints
            // 一带同批改动的口径)。
            Assert.That(RealGraph().Get("焚").Rarity, Is.EqualTo(CardRarity.Orange));
            var aoe = RealGraph().Get("焚").Effects.First(e => e.Kind == EffectKind.DamageAll);
            Assert.That(aoe.Value, Is.EqualTo(108), "相生取消后,配置值必须等于实战值");
        }

        [Test]
        public void RealConfig_P0UnlockedWordsAreLoadable()
        {
            var graph = RealGraph();
            // 2026-08-14:溺 / 埋 / 坑 随用户裁定移出字表,从本列表删去。
            // 2026-08-14 第二批裁定移出 锯 / 磐 / 巍,从本列表删去。
            // 2026-08-14 第三批:润 / 滋 移出。
            // 2026-08-25 字表重构:洼 / 凝 / 崊 / 崟 / 漜 移出字表,换成留表的同系字。
            // 2026-09-05 字表调整:淡 / 铠 随 17 字一并移出字表,从本列表删去(同 2026-08-14
            // 的处理口径,不找字顶替)。2026-09-07 字表重做 P2:浴 随 桤/葬/锐 一并移出,
            // 同一口径删去,不找字顶替。
            foreach (var id in new[] { "淋", "沐", "冰", "冻",
                                       "藤", "冷",
                                       "垚", "圭", "塔" })
                Assert.That(graph.Get(id), Is.Not.Null, $"{id} 应已收录");
        }

        // 2026-09-05:RealConfig_KaiIsDefenseFive(铠 的单值校验)随 铠 移出一并作废,
        // 没有空集可钉。RealConfig_DefenseChars_CarryTheirPoints 原带的「护甲字全集」
        // 唯一性断言改钉空集,见下。

        [Test]
        public void RealConfig_DefenseBuffChars_CarryTheirPoints()
        {
            // ⚠ 2026-09-07 改名(原 RealConfig_DefenseBuffHasNoCarrier):旧名字断言的是
            // 「无载体」,但 2026-09-05 铠(DefenseBuff 原唯一载体)移出后点数护甲机制休眠
            // 了一批,2026-09-07 字表重做 P2 又给了它**四个**新载体——这是 DefenseBuff 的
            // 首次真正落地,不是「铠 复活」。旧名字沿用下去会变成一句假话,与
            // RealConfig_SilenceChars_CarryTheirTurns 一起按「方法名必须反映断言」的原则
            // 改名(2026-09-07 二次审阅拍板)。逐字典 + Count 唯一性断言按 D 类口径补回来。
            var carriers = RealGraph().All
                .Where(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Any(e => e.Kind == EffectKind.DefenseBuff))
                .ToDictionary(c => c.Id,
                    c => c.Effects.First(e => e.Kind == EffectKind.DefenseBuff).Value);
            Assert.That(carriers, Is.EquivalentTo(new Dictionary<string, int>
            {
                ["垒"] = 4, ["杜"] = 9, ["垚"] = 11, ["㙓"] = 13,
            }), "DefenseBuff 的全集就是土系这四张护甲梯队字;新增载体时把它加进来一起钉");
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
        public void RealConfig_PierceHasNoCarrier()
        {
            // 2026-09-05:刺(EffectDef.Pierce 一次性穿透,全表唯一载体)随字表调整移出,
            // 穿透机制休眠。锐 的 PierceBuff(本场持久叠加 +20/张)是另一条通道,不受影响
            // ——见第 10 章 §10.2。钉住空集:哪天有新字接手 Pierce,本条会红。
            Assert.That(RealGraph().All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Pierce > 0), Is.False, "一次性穿透(Pierce)当前应无载体");
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
            // 2026-09-05:砸(土)/熣(火)随字表调整移出,偷袭字只剩 冷/刲 两张。
            // 2026-09-07 字表重做 P2:冷 改单纯「减速 1 回合」,不再带偷袭(design §6 水系
            // 白档只留控制链定位);偷袭改挂 灿(火/金档,灼烧2 + 偷袭),偷袭字变成 灿/刲。
            var graph = RealGraph();
            var expected = new[] { "灿", "刲" };
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
        public void RealConfig_CardSideSkewerHasNoCarrier()
        {
            // 2026-09-05:刺(字卡攻击面 Skewer,全表唯一载体)随字表调整移出,该形状在
            // 字卡侧休眠 —— 枪 的召唤被动 Skewer 不受影响,仍由
            // RealConfig_SummonPassiveChars_CarryTheirPassive 钉着;这里钉的是出字直接效果
            // (Effects / AttackEffects)的 Shape 字段。钉住空集:哪天有新字接手,本条会红。
            Assert.That(RealGraph().All.SelectMany(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>()))
                .Any(e => e.Shape == TargetShape.Skewer), Is.False, "字卡攻击面 Skewer 当前应无载体");
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
                // 2026-09-07:桤(这里原来用来钉 Speed 150 的样本字)随字表重做 P2 移出
                // (它的部件 岂/己 也随之孤儿化并删除)。Speed 字段本身不是孤儿 ——
                // 林/森/藻/塔/𣛧(木系「迅捷」梯队)仍在挂它,只是这条字典没有另找一个
                // 顶替样本,不找字顶替是「机制休眠」的口径,这里不适用(机制并未休眠,
                // 只是这个字典本条被删,不重新挑样本)。
                // 2026-08-25 字表重构:召唤定位由配方里的第二个五行部件决定,
                // 被动跟着定位走(spec §3)。烓 / 灶 移出后 OnHitBurnAll 无载体,
                // 桃(HealAlly)的位子由新增的 杖 接手。
                // 荆(2026-08-25 二次调整):纯反伤肉盾 —— 攻 0,输出全靠反伤。
                // Thorns 的单位此时已是「受到伤害的百分比」,50 = 反弹一半。
                ["荆"] = p => { Assert.That(p.Thorns, Is.EqualTo(50)); Assert.That(p.Ranged, Is.False, "改前排肉盾,不再远程");
                                Assert.That(p.Taunt, Is.True, "嘲讽是「挨打即输出」成立的前提"); },
                // 2026-09-05:蕉(OnHitSlow)/ 杖(HealAlly)随字表调整移出,两条断言删去 ——
                // 复活线索见类文档顶部的「机制休眠」清单。
                ["藤"] = p => { Assert.That(p.OnHitFreezeChance, Is.EqualTo(10)); Assert.That(p.OnSummonFreeze, Is.EqualTo(0)); },
                // 2026-09-07 字表重做 P2:锥/剑 都不再是召唤字 —— 锥 改蓝档单体攻击 + 破甲
                // (design §6:「破甲链·蓝」),剑 改蓝档单体攻击 + 横扫(design §6:「横扫链·低」),
                // 两条 SummonPassive.Shape 样本(Volley/Sweep)随之删去。全表现在唯一还在挂
                // SummonPassive.Shape 的只剩 枪(Skewer)—— Volley/Sweep 作为**召唤被动**形状
                // 暂时无载体(横扫本身作为**字卡攻击**形状仍在,剑 自己就是新样本,
                // 见 RealConfig_ArmorBreakChars_CarryTheirPoints 一带的攻击面断言口径)。
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
            Assert.That(jingSummon.SummonCount, Is.EqualTo(1), "2026-09-04:多只召唤收归金档及以上");
            // 2026-09-07 字表重做 P2:召唤血/攻按总量守恒重新摊到 1 只(spec §6 落地值),
            // 660 → 430。
            Assert.That(jingSummon.Value, Is.EqualTo(430), "血量随全表召唤重新标定");
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

        /// <summary>「一次召多只」是金档及以上的专属卖点(2026-09-04 用户拍板)。
        ///
        /// 紫档以下召 1 只 —— 开局只开 2 个召唤槽(<see cref="MetaRules.SummonSlotsFor"/>),
        /// 一张紫卡召 2 只就把全场占满,第二张召唤字只能顶掉自己人。只数因此是**档位资源**,
        /// 不是随手给的数值;总量补偿走单只的血/攻,不走只数。
        ///
        /// 新字漏看这条不会有任何报错 —— 只会在游戏里悄悄变成「紫卡也能铺场」。</summary>
        [Test]
        public void RealConfig_MultiSummon_IsGoldAndAbove()
        {
            var graph = RealGraph();
            foreach (var def in graph.All)
                foreach (var effect in def.Effects.Concat(def.AttackEffects))
                    if (effect.Kind == EffectKind.Summon && effect.SummonCount > 1)
                        Assert.That((int)def.Rarity, Is.GreaterThanOrEqualTo((int)CardRarity.Gold),
                            $"「{def.Id}」({def.Rarity})召 {effect.SummonCount} 只——多只召唤只给金档及以上");
        }

        [Test]
        public void RealConfig_GuiCarriesThornsNotSummonShield()
        {
            // ⚠ 2026-09-07 二次审阅改名(原 RealConfig_GuiGrantsSummonShield):旧名字断言
            // 「有 SummonShield」,但字表重做 P2 把 桂 的特性配额收窄成「光环盾 / 荆棘」两条
            // (spec §6)——一次性 SummonShield 60 不在新表里了,旧名字继续叫
            // 「GrantsSummonShield」就是一句假话,按「方法名必须反映断言」的原则改名。
            // 只数也从 2026-08-25 的 3 只收回到全系统一的 1 只(2026-09-04/09-05 两次收紧
            // 「除 𣛧 外一律 1 只」的口径,见 rebalance_2026_09_05.py 的召唤只数注释)。
            // ⚠ 用户正在另行裁定「光环盾」能否用 SummonShield 顶替——若改判会另行通知,
            // 不阻塞本任务。
            var graph = RealGraph();
            var summon = graph.Get("桂").Effects.First(e => e.Kind == EffectKind.Summon);
            Assert.That(summon.SummonShield, Is.EqualTo(0), "一次性 SummonShield 已不在新表里");
            Assert.That(summon.SummonCount, Is.EqualTo(1), "只数收归全系统一的 1 只");
            Assert.That(summon.Passive.Thorns, Is.EqualTo(50), "荆棘是 桂 现在的第二条特性");
        }

        [Test]
        public void RealConfig_SummonOnHitSlowHasNoCarrier()
        {
            // 2026-09-05:蕉(召唤被动 OnHitSlow,全表唯一载体)随字表调整移出,该被动休眠。
            // 与 RealConfig_DodgeHasNoCarrier 同口径钉住空集:哪天有新字接手,本条会红。
            Assert.That(RealGraph().All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Summon && e.Passive != null && e.Passive.OnHitSlowPercent > 0),
                Is.False, "召唤被动 OnHitSlow 当前应无载体");
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
            // Effects 上 —— 读取位置跟着改。
            // 2026-09-05 用户拍板把 溃 从白档升到蓝档,破甲**跟着档位走**,10 → 20 ——
            // 「白 10 / 蓝 20」这条轴没变,变的是溃站在哪一档。于是两张字现在同为 20,
            // 而这条测试守的本来就是「每张字带着与自己档位相符的点数」。
            var kui = graph.Get("溃").AttackEffects.First(e => e.Kind == EffectKind.ArmorBreak);
            Assert.That(kui.Value, Is.EqualTo(20), "「溃」破甲削减点数(蓝档)");
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
            // 2026-09-07 字表重做 P2 重新标定:蓝档单攻锚点 130 × K[蓝]0.90 ×
            // (1 − 残血加伤 0.15) = 130 × 0.865 ≈ 112(spec §1.4/§2 公式,
            // tools/design/rebalance_2026_09_05.py 的 PRICE['残血加伤']=0.15)。
            Assert.That(jiao.Value, Is.EqualTo(112), "蓝档单攻锚点减去残血加伤的计价");
            Assert.That(graph.Get("剿").Effects.Any(e => e.Kind == EffectKind.DamageAll), Is.False,
                "改单体后不该还留着全体那条");

            // 铡 同时接了「对流血目标翻倍」——与 劈 的流血组成金系的铺/收一对
            Assert.That(zha.DoubleVs, Is.EqualTo(DamageCondition.Bleeding));
        }

        [Test]
        public void RealConfig_DispelAndCleanseHaveNoCarrier()
        {
            // ⚠ 方法名与断言方向都已改:原 RealConfig_DispelChars_CarryTheirCounts 钉的是
            // 灭/湮 两张 Dispel(Value=-1 清全部)。2026-09-07 字表重做 P2 把「净化 + 驱散
            // 并入封禁」(design §1.3.1,用户裁定「不保留净化」),灭/湮 现在挂的是
            // Silence(封禁),Dispel/Cleanse 两个机制全表都没有载体了 —— 与
            // RealConfig_BlindHasNoCarrier 等「机制休眠」测试同口径,钉住空集:
            // 哪天有新字接手,这两条会红,提醒把数值/唯一性守卫加回来。
            var graph = RealGraph();
            Assert.That(graph.All.SelectMany(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>()))
                .Any(e => e.Kind == EffectKind.Dispel), Is.False, "Dispel 当前应无载体(并入封禁)");
            Assert.That(graph.All.SelectMany(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>()))
                .Any(e => e.Kind == EffectKind.Cleanse), Is.False, "Cleanse 当前应无载体(不保留净化)");
        }

        [Test]
        public void RealConfig_ImmunityAndRevive()
        {
            // ⚠ 方法名去掉了 Cleanse:2026-09-07 字表重做 P2 把 浴(净化 + 复活的原载体)
            // 移出了字表,净化本身也随「净化并入封禁」的裁定不再保留(见
            // RealConfig_DispelAndCleanseHaveNoCarrier)——这条测试现在只钉 免疫 与 复活。
            var graph = RealGraph();
            Assert.That(graph.Get("杜").Effects.First(e => e.Kind == EffectKind.Immunity).Value,
                Is.EqualTo(2));
            // 2026-08-14 第二批裁定移出字表:塞(免疫 1)/ 岿(免疫 1 + 净化)。
            // 免疫的载体现在只剩 杜 一张。
            // 2026-09-07:复活机制从 浴(已移出)移交 沐(design §6:水/金档,「持续治疗 /
            // 封禁 / 复活1」),沐 的复活挂在攻击面。
            Assert.That(graph.Get("沐").Effects.First(e => e.Kind == EffectKind.Revive).Value,
                Is.EqualTo(1));
            Assert.That(graph.All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>())
                    .Any(e => e.Kind == EffectKind.Revive)),
                Is.EqualTo(1), "复活当前只有 沐 一个载体");
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
        public void RealConfig_BlindHasNoCarrier()
        {
            // 2026-09-05:熣(Blind,全表唯一载体)随字表调整移出,致盲机制休眠。targetAll
            // 那一半的守卫仍由 DamageVariantTests.NeedsTarget_BlindAll_False_BlindSingle_True
            // 用构造的 CharDef 顶着。与 Silence 同口径钉住空集:哪天有新字接手,本条会红。
            Assert.That(RealGraph().All.SelectMany(c => c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Blind), Is.False, "Blind 当前应无载体");
        }

        [Test]
        public void RealConfig_SilenceChars_CarryTheirTurns()
        {
            // ⚠ 2026-09-07 二次审阅改名(原 RealConfig_SilenceHasNoCarrier):2026-08-14
            // 第二批裁定移出 锁 之后 Silence 一度无载体。2026-09-05「封禁」上线(语义从
            // 「主动机制哑火」扩到「护甲/被动/大招全禁,对 Boss 降级为护甲减半」,见
            // BattleEngine.SuppressArmorOf),2026-09-07 字表重做 P2 把「净化 + 驱散并入
            // 封禁」也落了地(灭/湮 从 Dispel/Cleanse 改挂 Silence)—— 六张字挂着它,
            // 不再是无载体。旧名字继续叫「HasNoCarrier」是一句假话,与
            // RealConfig_DefenseBuffChars_CarryTheirPoints 一起按「方法名必须反映断言」的
            // 原则改名,断言按 D 类口径反转为「有这六个载体」。
            var carriers = RealGraph().All
                .SelectMany(c => (c.Effects ?? Array.Empty<EffectDef>())
                    .Concat(c.AttackEffects ?? Array.Empty<EffectDef>())
                    .Where(e => e.Kind == EffectKind.Silence).Select(e => (c.Id, e.Turns)))
                .ToDictionary(x => x.Id, x => x.Turns);
            Assert.That(carriers, Is.EquivalentTo(new Dictionary<string, int>
            {
                ["灭"] = 1, ["湮"] = 1, ["海"] = 1, ["淋"] = 2, ["沐"] = 1, ["澡"] = 1,
            }), "封禁(Silence)的全集就是这六张字,连带各自的持续回合数");
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
            // 2026-09-07 字表重做 P2:护盾/攻击都按 spec §1.4 公式重新标定 ——
            // 护盾 = 绿档护盾锚点 70 × SHIELD_F(0.65) × (1 − 反伤30 的预算 0.22) ≈ 35;
            // 攻击 = 绿档单攻锚点 90 × (1 − 0.22) ≈ 70(见 tools/design/rebalance_2026_09_05.py,
            // PRICE['反伤30']=0.22、K[绿]=1.00)。不再是旧版「满值砍半」的说法。
            Assert.That(bi.Effects.Single(e => e.Kind == EffectKind.Shield).Value, Is.EqualTo(35));
            Assert.That(bi.AttackEffects.Single(e => e.Kind == EffectKind.DamageSingle).Value,
                Is.EqualTo(70), "同一条预算算式算出来的攻击面,不是另外「不动」");
            // 2026-09-07 字表重做 P2:圭(金档,反伤50)也挂了 Reflect,反弹的载体从 1 → 2。
            Assert.That(RealGraph().All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Reflect)), Is.EqualTo(2), "反弹当前是 壁/圭 两个载体");
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
            // 2026-09-07 字表重做 P2:紫档单攻锚点 200 ×(1 − (分2段0.20 + 流血0.25) × K[紫]0.80)
            // = 200 ×(1 − 0.36) = 128 总量,两段平摊 → 每段 64(design 表 §6 的落地值,
            // 与 tools/design/rebalance_2026_09_05.py 的 PRICE/K 算式对得上)。
            Assert.That(duo.Value, Is.EqualTo(64), "2026-09-07 每段 70 → 64");
            Assert.That(duo.Value * duo.HitCount, Is.EqualTo(128), "两段合计 128");
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

        // 2026-09-05:本方法原名 RealConfig_JiaoCarriesBurnAndNoDecay,前半段钉 焦 的
        // BurnNoDecay —— 焦 随字表调整移出,该机制休眠(复活线索见类文档顶部的
        // 「机制休眠」清单),前半段断言已删。流血梯度断言与 焦 无关,保留并改名于此。
        [Test]
        public void RealConfig_BleedChars_CarryTheirGradient()
        {
            // 流血梯度:原为 劈 白 10 / 剁 紫 20,收割者是 铡。
            // 2026-08-25 曾是三档(锋 蓝 15 居中);2026-08-29 用户拍板把 锋 连同其余六张
            // buff 字一起去掉对敌效果、回归纯 buff,中间那一档因此空出来 —— 是已知缺口,
            // 不是漏钉。
            // 2026-09-05:劈 随字表调整移出,流血梯度只剩 剁 一张 —— 白档那一级暂空
            // (同属已知缺口,不是漏钉;要补就再找一张白档金系的字挂 Bleed 10)。
            var bleeders = RealGraph().All
                .Where(c => (c.Effects ?? Array.Empty<EffectDef>()).Any(e => e.Kind == EffectKind.Bleed))
                .ToDictionary(c => c.Id,
                    c => c.Effects.First(e => e.Kind == EffectKind.Bleed).Value);
            Assert.That(bleeders, Is.EquivalentTo(new Dictionary<string, int>
            {
                ["剁"] = 20,
            }), "铺流血的梯度就是这一张;新增载体时把它加进来一起钉");
        }

        /// <summary>2026-09-07 字表重做 P2:燥 的引爆手法从「抬灼烧系数(BurnPotency)后
        /// 立即结算(BurnSettleNow)」换成了「铺一层灼烧后自己引爆(Detonate)」——与 炸/爆/
        /// 燚 那组「铺 → 收」的引爆梯队用的是同一个机制,不再是它自己独有的
        /// BurnPotency+BurnSettleNow 组合(那条组合现在全表无载体)。方法名沿用旧名,
        /// 顺序不变量本身没变:必须先铺灼烧、再引爆,顺序反了引爆就吃不到刚铺的这层。</summary>
        [Test]
        public void RealConfig_ZaoSettlesAfterRaisingPotency()
        {
            var effects = RealGraph().Get("燥").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.DamageSingle, EffectKind.BurnSingle, EffectKind.Detonate,
            }), "顺序错了引爆就吃不到自己刚铺的这层灼烧");
            Assert.That(effects[0].Value, Is.EqualTo(120));  // 单攻
            Assert.That(effects[1].Value, Is.EqualTo(2));    // 灼烧层数,不吃 ×10
        }

        [Test]
        public void RealConfig_ZhaCarriesDetonate()
        {
            // 2026-08-25 字表重构:灱 移出字表,引爆机制移交 炸(语义直接就是「引爆」)。
            // 炸 不自带灼烧层 —— 它是**收状态**的字,层数由 灼/热/烧/爆 铺。
            // 2026-08-25 用户拍板:炸 改 AOE —— 与 爆(全体灼烧 2,铺)成对,爆铺、炸收。
            // 2026-09-07 字表重做 P2:蓝档全体锚点 70 ×(1 − 全体引爆 0.40 × K[蓝]0.90)
            // = 70 × 0.64 = 45(design 表 §6 落地值,旧值 50 是上一批的算式)。
            var effects = RealGraph().Get("炸").Effects;
            Assert.That(effects.Select(e => e.Kind), Is.EqualTo(new[]
            {
                EffectKind.DamageAll, EffectKind.Detonate,
            }), "多一条效果就是超模——数组顺序即结算顺序");
            Assert.That(effects[0].Value, Is.EqualTo(45));
            // 2026-08-26:引爆必须是**全体**(详表:「引爆全部剩余灼烧」)。落成单体会让
            // 一张 AOE 字反过来要求玩家选目标 —— 交互与语义两头都错
            Assert.That(effects[1].TargetAll, Is.True, "炸 是全体引爆,不是只炸主目标");
            Assert.That(BattleEngine.NeedsTarget(RealGraph().Get("炸")), Is.False, "全体字不进选目标态");
            // 2026-09-07 字表重做 P2:引爆梯队扩到三张 —— 炸(蓝,收)、燥(紫,铺+自收)、
            // 燚(红,收),三张字直接印证了「铺灼烧的字与收灼烧的字配对」这条设计。
            Assert.That(RealGraph().All.Count(c => (c.Effects ?? Array.Empty<EffectDef>())
                .Any(e => e.Kind == EffectKind.Detonate)), Is.EqualTo(3), "引爆当前是 炸/燥/燚 三个载体");
        }

        [Test]
        public void RealConfig_NewBurnCharsAddNoLeafParts()
        {
            // 三个字的部件(火 木 喿 刀)全部已在表中,本批不该新增任何叶子——
            // 直接断配方本身,而不是断「部件能在表里查到」:build_chars 会自动把任何
            // 配方部件补成叶子条目写进 chars.json,查得到不代表它是本批之前就已存在的字。
            // 2026-08-25 字表重构:炑 / 灱 移出字表,不灭与引爆分别移交 焦 / 炸 ——
            // 两个接手的字本来就在表里,同样不新增叶子。
            // 2026-09-05:焦 随字表调整移出,对应断言删去(焦 已不在字表里,断它的配方
            // 无意义)——燥/炸 两张不受影响,仍是本批(2026-08-25)新增的火系字。
            var graph = RealGraph();
            Assert.That(graph.Get("燥").Recipe, Is.EqualTo(new[] { "火", "喿" }));
            Assert.That(graph.Get("炸").Recipe, Is.EqualTo(new[] { "火", "乍" }));
        }

        [Test]
        public void RealConfig_NewBurnCharsHaveExpectedRarity()
        {
            // 2026-09-05:焦 随字表调整移出,对应断言删去 —— 燥/炸 不受影响。
            var graph = RealGraph();
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
            // 2026-09-05:崔(服务已移出的 熣)/ 切(服务已移出的 沏)随字表调整从
            // COMPONENT_RECIPES 里一并删去,两个部件在 chars.json 里彻底消失(不是
            // 改了配方,是条目本身不在了),故从下表移去。
            // 2026-09-07 字表重做 P2:岂(服务已移出的 桤)随字表调整从 COMPONENT_RECIPES
            // 一并删去,其部件 己 也随之孤儿化并整个消失(见类文档顶部的 A 类说明),
            // 从下表移去。
            var graph = RealGraph();
            var expected = new (string Part, string[] Recipe)[]
            {
                ("秋", new[] { "禾", "火" }),
                ("荅", new[] { "艹", "合" }),
                ("列", new[] { "歹", "刂" }), ("喿", new[] { "品", "木" }),
                ("烝", new[] { "丞", "灬" }), ("则", new[] { "贝", "刂" }),
                ("朵", new[] { "几", "木" }),
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
            // 2026-09-05:七(原只服务 切→沏 这条链)随 切 一并从 chars.json 消失,从下表移去。
            // 2026-09-07 字表重做 P2:己(原只服务 岂→桤 这条链)随 岂 一并从 chars.json
            // 消失,从下表移去(见类文档顶部的 A 类说明)。
            var graph = RealGraph();
            foreach (var part in new[] { "合", "歹", "品", "丞", "贝", "几", "开", "覀" })
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

        /// <summary>60 个可出牌字一个都不是部件;部件一个都不是可出牌字。
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
            // 2026-09-05 字表调整:可出牌字 74 → 60(移出 17、新增 藻/箭/葬,74−17+3=60);
            // 部件 69 → 58 —— 移出的 17 张里,不少字的专属部件(如 崔/切/七/戈/刀 及若干
            // 只服务它们配方的中间字)随之从 chars.json 整个消失,净减 11 个。
            // 2026-09-07 字表重做 P2:再删 桤/浴/葬/锐 四字、新增 花,可出牌字 60 → 57
            // (60 − 4 + 1 = 57,与 spec 通篇说的「57 字」对上)。删的四字级联孤儿化了
            // 5 个部件(桤→岂→己、锐→兑、葬→死、浴→谷,浴/葬各自专属部件链只有一层),
            // 花 的配方(艹+化)带来 1 个新部件 化,部件 58 → 54(58 − 5 + 1 = 54)。
            Assert.That(playable, Is.EqualTo(57));
            Assert.That(components, Is.EqualTo(54));
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
        public void RealConfig_ComponentWithRecipe_StillCountsAsPoolComponent()
        {
            var graph = RealGraph();
            var parts = new List<string>(MetaRules.PoolComponents(new List<string> { "蒸" }, graph));
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
            // 但 隹 + 灬 却无法再合成 焦。」根因是 焦 不在已解锁卡池里 —— 闭包补上这一层。
            // 2026-09-05:蕉/焦 双双随字表调整移出,原样本失效,换到同形状的真实样本:
            // 淼 = 水 + 冰(叠字链中间环),冰 本身可再拆为 冫 + 水 —— 冫 只能靠递归穿过
            // 冰(一张可出牌字,不是裸部件)才能拿到,正是原 bug 要防的那类回归。
            // 钉在**真实字表**上:夹具图谱证明不了 淼/冰/冫/水 这几个字的配方还长这样。
            var set = ForgeEngine.ComposableSet(RealGraph(), new[] { "淼" });

            Assert.That(set.Contains("冰"), Is.True, "拆 淼 就能拿到 冰,那就该合得回去");
            Assert.That(set.Contains("水"), Is.True);
            Assert.That(set.Contains("冫"), Is.True, "冰 再拆一层的产物 —— 闭包是递归的");
        }

    }
}
