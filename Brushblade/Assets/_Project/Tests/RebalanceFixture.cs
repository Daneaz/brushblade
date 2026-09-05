using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;

namespace Brushblade.Core.Tests
{
    /// <summary>平衡重做 P0 那批测试的共享夹具(2026-09-05)。
    ///
    /// 取向沿用 HeftTests:测试字一律 Element.Heart 且不给配方(心对全属性生克 1.0×、
    /// 无配方不触发相生 —— 相生 ×3 虽已于 2026-09-02 取消,这条惯例仍保着"断言里看到的
    /// 数字就是被测机制本身"这个性质),PlayerAttack = 100 保住恒等性硬线,
    /// 一律走真实 Cast() / EndTurn()。</summary>
    internal static class RebalanceFixture
    {
        public const int BaseMaxHp = 500;

        /// <summary>图谱里总有一张 "甲"(单体 20)—— 秒杀 20 血靶子用它结束战斗。</summary>
        public static RecipeGraph Graph(params CharDef[] extra) =>
            new(new[]
                {
                    new CharDef("甲", Element.Heart,
                        effects: new[] { new EffectDef(EffectKind.DamageSingle, 20) }),
                }.Concat(extra).ToArray());

        public static CharDef Char(string id, params EffectDef[] effects) =>
            new(id, Element.Heart, effects: effects);

        /// <summary>杂兵靶子。hp 缺省给大值 = 打不死,便于在同一场里连续断言。</summary>
        public static EnemyDef Mob(int hp = 100000, int attack = 0, int armor = 0,
            EnemyAbility ability = EnemyAbility.None) =>
            new("怔", Element.Heart, hp, attack, ability, defense: armor);

        /// <summary>Boss 靶子。<c>EnemyState.IsBoss</c> 判的是 <c>Def.Phases.Count > 0</c>,
        /// 所以**必须给 phases**,只给个 id 叫 "钧" 是不够的 —— 那是本任务最容易漏的一处。</summary>
        public static EnemyDef Boss(int hp = 100000, int attack = 0, int armor = 0,
            EnemyAbility ability = EnemyAbility.None) =>
            new("钧", Element.Heart, hp, attack, ability, defense: armor,
                phases: new[]
                {
                    new BossPhaseDef("钧", Element.Heart, hp, attack, defense: armor),
                });

        public static BattleEngine Battle(RecipeGraph graph, IReadOnlyList<string> library,
            params EnemyDef[] enemies) =>
            new(graph, new BattleConfig { PlayerMaxHp = BaseMaxHp, PlayerAttack = 100 },
                library, Array.Empty<string>(), enemies, seed: 1);

        /// <summary>一场「一发就能打完」的爬塔,起始护盾可指定 ——
        /// RunEngine 的构造函数本来就有 startingNormalShield / startingPersistShield 两个参数,
        /// 不需要给引擎开新口子。</summary>
        public static RunEngine Run(int normalShield = 0, int persistShield = 0)
        {
            var config = new RunConfig
            {
                Encounters = new[] { new[] { Mob(hp: 20) } },
                RewardPool = new[] { "甲" },
            };
            return new RunEngine(Graph(), config,
                new BattleConfig { PlayerMaxHp = BaseMaxHp, PlayerAttack = 100 },
                new[] { "甲" }, Array.Empty<string>(), seed: 1,
                startingNormalShield: normalShield, startingPersistShield: persistShield);
        }
    }
}
