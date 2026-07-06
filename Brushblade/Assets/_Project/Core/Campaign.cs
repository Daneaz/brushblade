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

    /// <summary>章节:逐章加难(EnemyScale,F2)+ 字池分章投放(RewardPool,F3)。</summary>
    public sealed class ChapterDef
    {
        public string Name { get; set; }
        public float EnemyScale { get; set; } = 1f;
        public IReadOnlyList<StageDef> Stages { get; set; }
        public IReadOnlyList<string> RewardPool { get; set; }
    }

    /// <summary>整个战役内容:章节列表 + 全局掉落表(F1 调平载体)。</summary>
    public sealed class CampaignConfig
    {
        public IReadOnlyList<ChapterDef> Chapters { get; set; }
        public IReadOnlyList<string> DropTable { get; set; }

        /// <summary>把某章某关装配成 RunEngine 可用的 RunConfig(敌人数值按章缩放,向上取整)。</summary>
        public RunConfig BuildRunConfig(int chapterIndex, int stageIndex)
        {
            var chapter = Chapters[chapterIndex];
            var stage = chapter.Stages[stageIndex];

            var encounters = new List<IReadOnlyList<EnemyDef>>();
            foreach (var encounter in stage.Encounters)
            {
                var group = new List<EnemyDef>();
                foreach (var enemy in encounter)
                    group.Add(chapter.EnemyScale == 1f ? enemy : Scale(enemy, chapter.EnemyScale));
                encounters.Add(group);
            }

            return new RunConfig { Encounters = encounters, RewardPool = chapter.RewardPool };
        }

        private static EnemyDef Scale(EnemyDef enemy, float scale)
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
                enemy.Ability, phases);
        }
    }
}
