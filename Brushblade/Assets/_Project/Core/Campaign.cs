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

        /// <summary>敌人数值缩放(HP/攻击按 <see cref="Scaled"/>,承伤系数不缩放);无尽深度缩放复用(20.4)。</summary>
        public static EnemyDef Scale(EnemyDef enemy, float scale)
        {
            List<BossPhaseDef> phases = null;
            if (enemy.Phases.Count > 0)
            {
                phases = new List<BossPhaseDef>();
                foreach (var phase in enemy.Phases)
                    phases.Add(new BossPhaseDef(phase.Char, phase.Element,
                        Scaled(phase.MaxHp, scale), Scaled(phase.Attack, scale),
                        phase.DamageTaken, phase.Skill)); // 承伤系数与技能都不缩放
            }
            return new EnemyDef(enemy.Id, enemy.Element,
                Scaled(enemy.MaxHp, scale), Scaled(enemy.Attack, scale),
                enemy.Ability, phases, enemy.DamageTaken); // 承伤系数不缩放
        }

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
