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

        /// <summary>每排的列数(2026-08-22)。BattleEngine.EnemyRowCap 转引这个数
        /// (敌方每排上限);写在这里是为了让形状裁定不必反向依赖引擎。
        /// 召唤物侧的 FrontRowSize 是另一回事——那是召唤物前排的槽位数,与敌方每排列数
        /// 没有耦合,两者恰好同为 3 纯属巧合,不要以为改一个另一个也得跟着改(2026-08-22 评审)。</summary>
        public const int RowCapacity = 3;

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

        /// <summary>「最前召唤物」(Boss 洞穿 / 吞噬):前排槽序最小的存活者;前排全空则取后排。
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

        /// <summary>把「主目标 + 形状」展开成实际要结算的敌人下标表(2026-08-22,spec §4)。
        ///
        /// **首项恒为主目标**(Volley 除外——它没有主目标),调用方靠这一条区分
        /// 「吃斩杀/多段/穿透的那一发」与「只吃 ShapePercent 的溅射」。
        ///
        /// **表内可含重复下标**:Volley 循环补足时同一只怪会出现多次,调用方按
        /// 「每项一次结算」处理即可,不去重。形状类返回的表恒不重复。
        ///
        /// **不摇随机数**。这是硬要求,不是巧合:上千条带种子的既有测试靠随机流不位移
        /// 才不会整体变红(与本文件 PickAllyTarget 那条「候选只有一个时不摇」同一套纪律)。
        ///
        /// 空位不递补:顺劈打边格只溅一侧,整排只剩一只横扫就只中一只。
        /// 形状是几何,不是「保证打满 K 个」。</summary>
        public static IReadOnlyList<int> ExpandTargets(IReadOnlyList<EnemyState> enemies,
            int primaryIndex, TargetShape shape, int shots)
        {
            if (shape == TargetShape.Volley) return VolleyTargets(enemies, shots);
            if (primaryIndex < 0 || primaryIndex >= enemies.Count) return System.Array.Empty<int>();

            var result = new List<int> { primaryIndex };
            if (shape == TargetShape.Single) return result;

            var primary = enemies[primaryIndex];
            for (int i = 0; i < enemies.Count; i++)
            {
                if (i == primaryIndex || !enemies[i].Alive) continue;
                bool hit = shape switch
                {
                    TargetShape.Sweep => enemies[i].Row == primary.Row,
                    TargetShape.Cleave => enemies[i].Row == primary.Row
                        && System.Math.Abs(enemies[i].Column - primary.Column) == 1,
                    TargetShape.Skewer => enemies[i].Column == primary.Column,
                    _ => false,
                };
                if (hit) result.Add(i);
            }
            return result;
        }

        /// <summary>连发的目标序列:后排优先、各排按列序排出候选,再从头循环取满 shots 发。
        /// 候选为空或 shots ≤ 0 返回空表。</summary>
        private static IReadOnlyList<int> VolleyTargets(IReadOnlyList<EnemyState> enemies, int shots)
        {
            if (shots <= 0) return System.Array.Empty<int>();
            var pool = new List<int>();
            CollectRowByColumn(enemies, EnemyRow.Back, pool);
            CollectRowByColumn(enemies, EnemyRow.Front, pool);
            if (pool.Count == 0) return System.Array.Empty<int>();

            var result = new List<int>(shots);
            for (int n = 0; n < shots; n++) result.Add(pool[n % pool.Count]);
            return result;
        }

        /// <summary>把某一排的存活者按**列序**追加进 pool(不是按列表下标序)——
        /// 「后排优先」这条口径要的是阵型上的先后,而 _enemies 的下标序是生成顺序。</summary>
        private static void CollectRowByColumn(IReadOnlyList<EnemyState> enemies, EnemyRow row,
            List<int> pool)
        {
            for (int col = 0; col < RowCapacity; col++)
                for (int i = 0; i < enemies.Count; i++)
                    if (enemies[i].Alive && enemies[i].Row == row && enemies[i].Column == col)
                        pool.Add(i);
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
