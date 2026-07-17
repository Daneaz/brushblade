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

        /// <summary>敌人数值缩放(HP/攻击向上取整,承伤系数不缩放);无尽深度缩放复用(20.4)。</summary>
        public static EnemyDef Scale(EnemyDef enemy, float scale)
        {
            List<BossPhaseDef> phases = null;
            if (enemy.Phases.Count > 0)
            {
                phases = new List<BossPhaseDef>();
                foreach (var phase in enemy.Phases)
                    phases.Add(new BossPhaseDef(phase.Char, phase.Element,
                        (int)Math.Ceiling(phase.MaxHp * scale),
                        (int)Math.Ceiling(phase.Attack * scale),
                        phase.DamageTaken)); // 承伤系数不缩放
            }
            return new EnemyDef(enemy.Id, enemy.Element,
                (int)Math.Ceiling(enemy.MaxHp * scale),
                (int)Math.Ceiling(enemy.Attack * scale),
                enemy.Ability, phases, enemy.DisguiseElement);
        }
    }
}
