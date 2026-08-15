using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>一个关卡(章内一格):进入即一次短 run(2~4 战,19.1)。</summary>
    public sealed class StageDef
    {
        public IReadOnlyList<IReadOnlyList<EnemyDef>> Encounters { get; set; }
        public bool Boss { get; set; }
    }

    /// <summary>章节:逐章加难(EnemyScale,F2)+ 字池分章投放(RewardPool,F3)+ Boss 池(8.5.3)。</summary>
    public sealed class ChapterDef
    {
        public string Name { get; set; }
        public float EnemyScale { get; set; } = 1f;
        public IReadOnlyList<StageDef> Stages { get; set; }
        public IReadOnlyList<string> RewardPool { get; set; }

        /// <summary>Boss 候选池:遭遇中的占位符(BossPlaceholder)在装配时从此池随机抽取。</summary>
        public IReadOnlyList<EnemyDef> BossPool { get; set; } = System.Array.Empty<EnemyDef>();
    }

    /// <summary>整个战役内容:章节列表 + 全局掉落表(F1 调平载体)。</summary>
    public sealed class CampaignConfig
    {
        /// <summary>遭遇中的 Boss 占位(配置里写 "$Boss"),装配时从章 BossPool 抽取。</summary>
        public static readonly EnemyDef BossPlaceholder = new("$Boss", Element.Heart, 1, 0);

        public IReadOnlyList<ChapterDef> Chapters { get; set; }
        public IReadOnlyList<string> DropTable { get; set; }

        /// <summary>奇遇事件池与触发概率(9.6,全战役共用)。</summary>
        public IReadOnlyList<EventDef> Events { get; set; } = System.Array.Empty<EventDef>();

        /// <summary>无尽模式配置(20.3);旧配置无 endless 段时为 null。</summary>
        public EndlessConfig Endless { get; set; }
        public int EventChancePercent { get; set; }

        /// <summary>把某章某关装配成 RunEngine 可用的 RunConfig(敌人数值按章缩放,向上取整;
        /// Boss 占位符从章 BossPool 抽取,random 为 null 时取首个)。</summary>
        public RunConfig BuildRunConfig(int chapterIndex, int stageIndex, GameRandom random = null)
        {
            var chapter = Chapters[chapterIndex];
            var stage = chapter.Stages[stageIndex];

            EnemyDef resolvedBoss = null; // 同一关内多处占位符解析为同一 Boss
            var encounters = new List<IReadOnlyList<EnemyDef>>();
            foreach (var encounter in stage.Encounters)
            {
                var group = new List<EnemyDef>();
                foreach (var enemy in encounter)
                {
                    var actual = enemy;
                    if (ReferenceEquals(enemy, BossPlaceholder))
                    {
                        resolvedBoss ??= chapter.BossPool[random?.Next(chapter.BossPool.Count) ?? 0];
                        actual = resolvedBoss;
                    }
                    group.Add(chapter.EnemyScale == 1f ? actual : Scale(actual, chapter.EnemyScale));
                }
                encounters.Add(group);
            }

            return new RunConfig
            {
                Encounters = encounters,
                RewardPool = chapter.RewardPool,
                EventPool = Events,
                EventChancePercent = EventChancePercent,
            };
        }

        /// <summary>敌人数值缩放(HP/攻击按 <see cref="Scaled"/>);无尽深度缩放复用(20.4)。
        ///
        /// **护甲按 scale 的一半增长**(2026-08-12,E-b4 裁定 11,见 <see cref="ScaledDefense"/>);
        /// 技能不缩放。</summary>
        public static EnemyDef Scale(EnemyDef enemy, float scale)
        {
            List<BossPhaseDef> phases = null;
            if (enemy.Phases.Count > 0)
            {
                phases = new List<BossPhaseDef>();
                foreach (var phase in enemy.Phases)
                    phases.Add(new BossPhaseDef(phase.Char, phase.Element,
                        Scaled(phase.MaxHp, scale), Scaled(phase.Attack, scale),
                        phase.Skill, ScaledDefense(phase.Defense, scale)));
            }
            return new EnemyDef(enemy.Id, enemy.Element,
                Scaled(enemy.MaxHp, scale), Scaled(enemy.Attack, scale),
                enemy.Ability, phases, ScaledDefense(enemy.Defense, scale));
        }

        /// <summary>护甲点数的深度缩放:**半速**(2026-08-12,E-b4 裁定 11)。
        ///
        /// 不缩放不行 —— 100 层的坚壁 Boss 血量 ×11 而护甲还是 60,占玩家单击的比例趋近 0,
        /// 护甲形同虚设。同速也不行 —— 点数减法对小数值是**开关**不是削减,同速会让深层的
        /// 低伤字全部归零,玩家在深层只剩几张高伤字可用,字库多样性被护甲单方面掐死。
        /// 于是取一半:<c>defScale = 1 + (scale − 1) / 2</c>。
        ///
        /// 判据(spec §6.3.2,守卫测试 LowestTierChar_StillDentsArmoredMobAtDepth20):
        /// 深度 20(scale 2.9 → defScale 1.95)时,字表最低伤害档的字打墨渍仍要有非零输出。
        /// 同速缩放会让它归零 —— 那条测试对这个变异有判别力,不是装饰性断言。
        ///
        /// 这里用 <c>Ceiling</c> 而不是 <see cref="Scaled"/> 的 <c>Round</c>:护甲不是血量量纲,
        /// 没有「量级 ×10 要与缩放交换」的约束,而向上取整保证 defScale &gt; 1 时护甲至少涨 1 点
        /// (DEF 1 的怪不会因为 Round 在深度 20 还是 1 点)。
        ///
        /// ⚠ **必须先在 double 下夹掉 float 噪声再取整**(2026-08-15 修)。原式全程 float:
        /// <c>(int)Math.Ceiling(defense * (1f + (scale - 1f) / 2f))</c> —— 与 <see cref="Scaled"/>
        /// 注释里写的是同一个坑,但那边靠 <c>Round</c> 天然免疫,这边的 <c>Ceiling</c> 会把
        /// 1e-7 量级的噪声整个放大一级。深度 20 的 <c>20 × 1.95</c> 精确值是 39,而
        /// <c>0.1f</c> 二进制不可表示:.NET 8 算出 38.99999976(Ceiling → 39),
        /// Unity Mono 的中间精度不同则可能算出 39.00000001(Ceiling → **40**)。
        /// 后果是同一份配置在工装与编辑器里给出不同的护甲值 ——
        /// <c>Scale_HalvesDefenseGrowth</c> 与 <c>LowestTierChar_StillDentsArmoredMobAtDepth20</c>
        /// 曾因此只在 Test Runner 里红。
        ///
        /// 现在**显式提升到 double 再夹到 4 位小数**,结果不再依赖中间精度:深度 20 的
        /// 精确值 39 对应的 double 是 39.00000095(scale 自带的 float 表示误差),
        /// 夹到 4 位得回 39。⚠ 夹取位数不能再宽:<c>Round(…, 6)</c> 留下的 39.000001
        /// 照样被 Ceiling 读成 40 —— 噪声量级是 1e-6 不是 1e-7。而真实的小数部分来自
        /// defScale 的 0.05 倍数(DEF 3 × 1.05 = 3.15 → 4),比 1e-4 大三个量级,不会被误伤。</summary>
        private static int ScaledDefense(int defense, float scale) =>
            (int)Math.Ceiling(Math.Round(defense * (1.0 + ((double)scale - 1.0) / 2.0), 4));

        /// <summary>一个血量量纲的数乘 scale 后落回整数。
        ///
        /// ⚠ **不许换回 <c>Ceiling</c>**(2026-08-12,E-b4/T1)。旧口径是
        /// <c>(int)Math.Ceiling(value × scale)</c>,而 <c>ceil(10a·s) ≠ 10·ceil(a·s)</c> ——
        /// 向上取整给低基础值的怪凭空补一份「取整红利」,且基数越小红利占比越大。
        /// 全表量级 ×10 之后这份红利必须消失,否则同一只怪在新旧量级下的相对强度对不上
        /// (实测 19 个敌人基础值 × 30 层里 63% 的组合不满足交换律)。
        ///
        /// 现在的口径:**缩放本身是精确的,取整只负责夹掉 float 的表示误差**。
        /// 敌人基础值全是 10 的倍数、scale 全是 0.1 的整数倍(无尽 <c>1 + 0.1×(depth−1)</c>、
        /// 章节 1.0 / 1.5 / 2.0),故 <c>value × scale</c> 数学上恒为整数;但 <c>0.1f</c>
        /// 二进制不可表示 —— depth 20 时 <c>140 × 2.9f</c> 实际算出 406.00001,
        /// <c>Ceiling</c> 会把它读成 407。<c>Round</c> 把这类 1e-4 量级的噪声夹回真值,
        /// 不承担任何取整语义。</summary>
        private static int Scaled(int value, float scale) =>
            (int)Math.Round(value * (double)scale, MidpointRounding.AwayFromZero);
    }
}
