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

        /// <summary>某一排该铺几个格位(2026-08-26)。表现层照这个数建格,敌人按 Column 落格 ——
        /// 「列」的几何在这里定一次,ExpandTargets 的 Skewer 才与玩家看到的对得上。
        ///
        /// 恒为 <see cref="RowCapacity"/>,**唯一例外**是两排都 ≤1 只:那时列没有对齐对象,
        /// 折叠成一格交给 MiddleCenter 摆正中(2026-08-23 实机反馈:单怪铺三格会被顶到最左)。
        ///
        /// ⚠ 这个例外原先只看本排(「本排只有一只就折叠」),前排 2 只 + 后排 1 只时后排被
        /// 居中到视觉第 2 位,而引擎认定它与前排第 1 位同列 —— 贯穿(枪)于是看起来打了
        /// 错位的一只。别再把判据缩回单排。</summary>
        public static int RowCells(int rowCount, int otherRowCount) =>
            rowCount == 1 && otherRowCount <= 1 ? 1 : RowCapacity;

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
            // 嘲讽(2026-08-25)压在最前:它要压过近战的前排拦截、远程的后排优先、
            // 以及 Focus.Player 的死盯玩家 —— 三条都是排位规则,而嘲讽是排位之上的强制。
            // 候选只有一个时不摇随机数,与下面那段同一条纪律(见方法头注释)。
            var taunters = TauntingSlots(summons);
            if (taunters != null)
                return taunters.Count == 1 ? taunters[0] : taunters[random.Next(taunters.Count)];

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

        /// <summary>全部**存活**的嘲讽召唤物槽位;一个都没有返回 null。
        /// 死了的不算 —— 否则全场攻击会打进一个空槽,玩家反而无敌。</summary>
        private static List<int> TauntingSlots(IReadOnlyList<SummonState> summons)
        {
            List<int> slots = null;
            for (int s = 0; s < summons.Count; s++)
            {
                var summon = summons[s];
                if (summon == null || !summon.Alive || summon.Passive == null || !summon.Passive.Taunt) continue;
                (slots ??= new List<int>()).Add(s);
            }
            return slots;
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
        public static int PickEnemyTargetForSummon(IReadOnlyList<EnemyState> enemies, bool ranged,
            TargetShape shape = TargetShape.Single,
            bool preferUnfrozen = false, bool preferUnslowed = false)
        {
            // 三条偏好都是**在原有排位规则之上的筛子**,不是替代:先按偏好缩小候选,
            // 缩不出来就退回全体,再走原来的「远程先后排 / 近战先前排」。
            // 顺序是 控场偏好 → 贯穿选列:前者关乎「这一下有没有用」(冻结/减速都是
            // 刷新而非叠加,打已中招的目标等于白费),后者只关乎「能不能多打一个」。
            var pool = enemies;
            if (preferUnfrozen)
            {
                var unfrozen = Subset(pool, i => !pool[i].Statuses.Has(StatusKind.Freeze));
                if (unfrozen != null) pool = unfrozen;
            }
            if (preferUnslowed)
            {
                // 「已减速」= 速度修正为负,与 DamageCondition.Controlled 的减速那一半同判据。
                // 不看是谁挂的:别人挂的减速同样让这一下失去意义。
                var unslowed = Subset(pool, i => pool[i].Statuses.TotalMagnitude(StatusKind.SpeedModifier) >= 0);
                if (unslowed != null) pool = unslowed;
            }
            if (shape == TargetShape.Skewer)
            {
                // 贯穿打的是整列。优先挑**前后排都有人**的那一列 —— 挑到空对位的列
                // 只会中一只,贯穿就白给了。挑不出来(没有任何列是满的)就不挑。
                var aligned = Subset(pool, i => HasBothRowsInColumn(pool, pool[i].Column));
                if (aligned != null) pool = aligned;
            }
            if (!ReferenceEquals(pool, enemies))
            {
                int picked = PickByRow(pool, ranged);
                // 候选池里的下标是子集自己的下标,要翻回原表
                if (picked >= 0) return IndexIn(enemies, pool[picked]);
            }
            return PickByRow(enemies, ranged);
        }

        /// <summary>按排位挑一个存活目标:远程先后排,近战先前排,都没有就退另一排。</summary>
        private static int PickByRow(IReadOnlyList<EnemyState> enemies, bool ranged)
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

        /// <summary>满足谓词的存活敌人子集;一个都没有返回 null(调用方据此退回全体)。</summary>
        private static List<EnemyState> Subset(IReadOnlyList<EnemyState> enemies,
            System.Func<int, bool> keep)
        {
            List<EnemyState> subset = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].Alive || !keep(i)) continue;
                (subset ??= new List<EnemyState>()).Add(enemies[i]);
            }
            return subset;
        }

        /// <summary>该列的前排与后排是否都还有活人 —— 贯穿要打满两只的前提。</summary>
        private static bool HasBothRowsInColumn(IReadOnlyList<EnemyState> enemies, int column)
        {
            bool front = false, back = false;
            foreach (var e in enemies)
            {
                if (!e.Alive || e.Column != column) continue;
                if (e.Row == EnemyRow.Front) front = true; else back = true;
            }
            return front && back;
        }

        /// <summary>实例在原表里的下标(子集筛选后翻回来用);找不到返回 −1。</summary>
        private static int IndexIn(IReadOnlyList<EnemyState> enemies, EnemyState target)
        {
            for (int i = 0; i < enemies.Count; i++)
                if (ReferenceEquals(enemies[i], target)) return i;
            return -1;
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
        /// 空位不递补:溅射打边格只溅一侧,整排只剩一只横扫就只中一只。
        /// 形状是几何,不是「保证打满 K 个」。</summary>
        public static IReadOnlyList<int> ExpandTargets(IReadOnlyList<EnemyState> enemies,
            int primaryIndex, TargetShape shape, int shots)
        {
            if (shape == TargetShape.Volley) return VolleyTargets(enemies, shots);
            if (shape == TargetShape.Chain) return ChainTargets(enemies, primaryIndex, shots);
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

        /// <summary>弹射的目标序列(2026-08-25):主目标打头,其余存活敌人按**离主目标的格子距离**
        /// 升序排,同距按下标 —— 全程确定性,**不摇随机数**(与本文件其余形状同一条硬要求)。
        /// 最多取 shots 个;目标不够就少跳,**不循环回头**(那是连发的语义)。
        /// shots ≤ 1 时只剩主目标,与单体等价 —— 漏配 shots 不会静默变成打全场。</summary>
        private static IReadOnlyList<int> ChainTargets(IReadOnlyList<EnemyState> enemies,
            int primaryIndex, int shots)
        {
            var result = new List<int> { primaryIndex };
            if (shots <= 1) return result;

            var primary = enemies[primaryIndex];
            var rest = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
                if (i != primaryIndex && enemies[i].Alive) rest.Add(i);
            // 距离 = 列差 + 排差(2×3 网格上的曼哈顿距离)。稳定排序:同距保持下标序。
            rest.Sort((x, y) =>
            {
                int dx = GridDistance(enemies[x], primary), dy = GridDistance(enemies[y], primary);
                return dx != dy ? dx.CompareTo(dy) : x.CompareTo(y);
            });
            for (int i = 0; i < rest.Count && result.Count < shots; i++) result.Add(rest[i]);
            return result;
        }

        private static int GridDistance(EnemyState a, EnemyState b) =>
            System.Math.Abs(a.Column - b.Column) + (a.Row == b.Row ? 0 : 1);

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
