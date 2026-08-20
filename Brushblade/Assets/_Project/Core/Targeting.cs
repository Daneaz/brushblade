using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>「打谁」的唯一裁定处(2026-08-20,spec §4)。纯函数:吃列表与 RNG,返回下标,
    /// 不持有也不修改任何引擎状态。引擎与表现层都只调它,不各自判断——排位规则一旦分散,
    /// 「玩家看到能点」与「引擎认为能打」就会失配。
    ///
    /// 单独成文件而不是塞进 BattleEngine:那个文件已经 2400 行,而这套规则本身值得独立测试。</summary>
    public static class Targeting
    {
        /// <summary>PickAllyTarget 的返回值:打玩家本人,而不是某个召唤物槽位。</summary>
        public const int PlayerTarget = -1;

        /// <summary>敌人选我方目标。返回召唤物槽位,或 <see cref="PlayerTarget"/>。
        ///
        /// 均匀随机的口径(spec §4.1):把**全部存活后排召唤物与玩家**放进同一个候选池抽一个,
        /// 不是先五五开决定「打后排还是打玩家」。后排站 2 只时玩家挨打概率是 1/3——
        /// 站位越厚玩家越安全。
        ///
        /// ⚠ 候选只有一个时**不摇随机数**——这一保证是两层叠的:第一层是
        /// <see cref="GameRandom.Next"/> 自己的性质(maxExclusive ≤ 1 直接 return 0,
        /// 不碰内部状态,见 GameRandomTests.NextZeroOrOne_DoesNotAdvanceState);
        /// `pool.Count == 1 ? pool[0] : …` 这句短路是第二层纵深防御,不是唯一防线,
        /// 两层任一在位都够。绝大多数既有战斗(没有后排召唤物)因此完全不消耗随机数,
        /// 随机流与改前逐位相同,上千条带种子的既有测试才不会整体位移。</summary>
        public static int PickAllyTarget(AttackRange range, AttackFocus focus,
            IReadOnlyList<SummonState> summons, int frontRow, GameRandom random)
        {
            if (range == AttackRange.Melee)
            {
                int blocker = FirstAliveSlot(summons, 0, frontRow);
                if (blocker >= 0) return blocker;   // 被前排拦下,后面的规则一概不看
            }

            if (focus == AttackFocus.Player) return PlayerTarget;

            var pool = new List<int>();
            for (int s = frontRow; s < summons.Count; s++)
                if (summons[s] != null && summons[s].Alive) pool.Add(s);
            pool.Add(PlayerTarget);
            return pool.Count == 1 ? pool[0] : pool[random.Next(pool.Count)];
        }

        /// <summary>「最前召唤物」(Boss 贯穿 / 吞噬):前排槽序最小的存活者;前排全空则取后排。
        /// 全空返回 −1。
        ///
        /// 槽位是 0..5 且前排恰是低位段,所以本函数与「从 0 扫到末尾取第一个存活」等价——
        /// 存在的意义是把这条口径写成显式契约,而不是依赖槽位编号的巧合。</summary>
        public static int FrontmostSummon(IReadOnlyList<SummonState> summons, int frontRow)
        {
            int front = FirstAliveSlot(summons, 0, frontRow);
            return front >= 0 ? front : FirstAliveSlot(summons, frontRow, summons.Count);
        }

        /// <summary>召唤物出手选敌。近战打敌方前排(全清则打全场序最靠前的存活者);
        /// 远程优先打后排(后排空了才按近战规则来)。无敌可打返回 −1。
        ///
        /// 排位不影响召唤物**自己**能不能出手:站后排的近战照常攻击(用户 2026-08-20 拍板)。</summary>
        public static int PickEnemyTargetForSummon(IReadOnlyList<EnemyState> enemies, bool ranged)
        {
            if (ranged)
            {
                int back = FirstAliveInRow(enemies, EnemyRow.Back);
                if (back >= 0) return back;
            }
            int front = FirstAliveInRow(enemies, EnemyRow.Front);
            if (front >= 0) return front;
            return FirstAliveInRow(enemies, EnemyRow.Back);
        }

        /// <summary>玩家的**单体直接伤害**能不能打这只敌人(spec §4.2)。
        /// ignoresRow = 该字标了偷袭(刺)。控制类、AOE 一律不调本函数——它们不受排位限制。
        ///
        /// 「前排从未有过」与「前排已被清空」同等对待:一场若全是后排怪,玩家直接全场可点。</summary>
        public static bool CanPlayerHit(IReadOnlyList<EnemyState> enemies, int enemyIndex, bool ignoresRow)
        {
            if (enemyIndex < 0 || enemyIndex >= enemies.Count || !enemies[enemyIndex].Alive) return false;
            if (ignoresRow || enemies[enemyIndex].Row == EnemyRow.Front) return true;
            return FirstAliveInRow(enemies, EnemyRow.Front) < 0;
        }

        private static int FirstAliveSlot(IReadOnlyList<SummonState> summons, int from, int toExclusive)
        {
            for (int s = from; s < toExclusive && s < summons.Count; s++)
                if (summons[s] != null && summons[s].Alive) return s;
            return -1;
        }

        private static int FirstAliveInRow(IReadOnlyList<EnemyState> enemies, EnemyRow row)
        {
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].Alive && enemies[i].Row == row) return i;
            return -1;
        }
    }
}
