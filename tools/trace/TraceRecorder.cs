using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Brushblade.Core;

namespace Brushblade.Trace
{
    /// <summary>黄金轨迹的写出端(spec §10.2)。只负责「把状态摊成 token」,
    /// 「哪一列是量级」那份策略住在 <see cref="TraceCompare"/> —— 轨迹文件里不带
    /// 任何分类标记,是纯数据。这样 T1 若发现某个字段的量级归类判错了,
    /// 改比对器即可,不用重跑 baseline。
    ///
    /// 确定性约束(整个工装唯一的价值):
    /// - 行尾恒为 \n、编码恒为无 BOM UTF-8、数字恒走 InvariantCulture;
    /// - 不写时间戳、不写路径、不写机器名;
    /// - 列表 token 一律按产生顺序写(List 顺序),不经任何 Dictionary/HashSet 中转。</summary>
    public sealed class TraceRecorder : IDisposable
    {
        private readonly StreamWriter _writer;
        private int _lines;

        public TraceRecorder(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        }

        public int EventCount { get; private set; }
        public int LineCount => _lines;

        public void Comment(string text) => Write("# " + text);

        /// <summary>爬塔开始。</summary>
        public void Run(int seed, string profile, int startDepth) =>
            Write($"R {seed} {profile} {startDepth}");

        /// <summary>层段开始(fromDepth 起到本段 Boss 层)。</summary>
        public void Segment(int seed, int fromDepth, uint runRandomState) =>
            Write($"S {seed} {fromDepth} {runRandomState}");

        /// <summary>一场战斗开战:敌人编成 + 初始血量。</summary>
        public void BattleStart(int seed, int depth, int battleIndex, IReadOnlyList<EnemyState> enemies,
            uint battleRandomState)
        {
            var ids = new List<string>();
            var hps = new List<string>();
            foreach (var enemy in enemies)
            {
                ids.Add(enemy.Def.Id);
                hps.Add(Num(enemy.MaxHp));
            }
            Write($"B {seed} {depth} {battleIndex} {Join(ids)} {Join(hps)} {battleRandomState}");
        }

        /// <summary>回合开始时的全景:随机流位置 + 玩家血/盾 + 字库/部件池 + 敌人血量。
        /// 字库快照顺带把「回合掉字」的掉落序列也观测进来了 —— 掉的字就是这一行比上一行多出来的那个。</summary>
        public void Turn(int seed, int depth, int battleIndex, int turn, uint randomState,
            int playerHp, int shield, IReadOnlyList<string> library, IReadOnlyList<string> pool,
            IReadOnlyList<EnemyState> enemies)
        {
            var hps = new List<string>();
            foreach (var enemy in enemies) hps.Add(Num(enemy.Hp));
            Write($"T {seed} {depth} {battleIndex} {turn} {randomState} {Num(playerHp)} {Num(shield)} " +
                  $"{Join(library)} {Join(pool)} {Join(hps)}");
        }

        /// <summary>满库掉字的决议(Phase == DropChoice):掉的是什么字、机器人怎么处置的。</summary>
        public void Drop(int seed, int depth, int battleIndex, int turn, string dropped, string action) =>
            Write($"D {seed} {depth} {battleIndex} {turn} {dropped} {action}");

        public void Event(int seed, int depth, int battleIndex, int turn, BattleEvent e)
        {
            EventCount++;
            Write($"E {seed} {depth} {battleIndex} {turn} {e.Kind} {e.TargetIndex} {e.SecondIndex} " +
                  $"{(e.Crit ? 1 : 0)} {Num(e.Amount)} {Num(e.Absorbed)}");
        }

        /// <summary>战斗结束:胜负 + 回合数 + 收尾时的随机流位置与血量。</summary>
        public void BattleEnd(int seed, int depth, int battleIndex, string result, int turns,
            uint randomState, int playerHp) =>
            Write($"W {seed} {depth} {battleIndex} {result} {turns} {randomState} {Num(playerHp)}");

        /// <summary>战利品:5 选 N 的候选序列 + 实际取走的那些(掉落序列的另一半)。</summary>
        public void Reward(int seed, int depth, int battleIndex, IReadOnlyList<string> options,
            IReadOnlyList<string> picked) =>
            Write($"K {seed} {depth} {battleIndex} {Join(options)} {Join(picked)}");

        /// <summary>奇遇:撞到哪个奇遇、机器人选了第几项。</summary>
        public void Adventure(int seed, int depth, string eventId, int optionIndex) =>
            Write($"V {seed} {depth} {eventId} {optionIndex}");

        /// <summary>爬塔终局:卒于第几层、为什么停。</summary>
        public void RunEnd(int seed, int deathDepth, string reason) =>
            Write($"Z {seed} {deathDepth} {reason}");

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>列表 token:空列表写 <c>-</c>(空 token 会把列位置错开)。
        /// 分隔符 <c>|</c> 与汉字 id 不会冲突。</summary>
        private static string Join(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "-";
            return string.Join("|", items);
        }

        private void Write(string line)
        {
            _writer.WriteLine(line);
            _lines++;
        }

        public void Dispose() => _writer.Dispose();
    }
}
