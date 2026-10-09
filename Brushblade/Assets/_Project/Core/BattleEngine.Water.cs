using System;

namespace Brushblade.Core
{
    /// <summary>水系专属机制(Plan D2-水)。Task 1 = 共用小扩展的敌方侧:E15 选择器(Column / AdjacentOne / HighestHp /
    /// SlowedByThisCast)、E16 名单类推迟、E17 冻结的条件回合与冰滞后退、E18 减速只续不挂、E25 仅在减速中的种。
    /// ApplyEffects 的分支只留一行调用,实现放这里。
    ///
    /// 恒等:这些分支只在新字段 / 新选择器出现时才改变结果;一律不摇随机数。trace 必经的 Slow 分支只多一次名单记录。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>名单类效果(E16):选择器读「本次出字冻结 / 减速过的敌人」,要推迟到出字效果循环末尾才收得全。</summary>
        private static bool IsRosterPick(EffectDef effect) =>
            effect.Pick == EffectPick.FrozenByThisCast || effect.Pick == EffectPick.SlowedByThisCast;

        /// <summary>选择器 SlowedByThisCast 的名单(去重,按施加顺序)。</summary>
        private void RecordSlowed(int enemyIndex)
        {
            if (!_cast.SlowedTargets.Contains(enemyIndex)) _cast.SlowedTargets.Add(enemyIndex);
        }

        /// <summary>AdjacentOne(E15,坚冰):主目标同排左右中第一个存活者,先左后右(同侧按下标);Freeze 再滤掉冻结中 / 霜抗 / 冰滞的
        /// (R1:那些挂不上)。没有返回 −1。不摇号。</summary>
        private int AdjacentOneOf(EffectDef effect, int primary)
        {
            if (primary < 0 || primary >= _enemies.Count) return -1;
            var p = _enemies[primary];
            int left = -1, right = -1;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (i == primary || !e.Alive || !e.SharesRow(p)) continue;
                if (effect.Kind == EffectKind.Freeze && (e.Statuses.Has(StatusKind.Freeze)
                        || e.Statuses.Has(StatusKind.FrostResist) || e.Statuses.Has(StatusKind.IceStall)))
                    continue;
                if (left < 0 && e.ColumnEnd == p.Column) left = i;
                else if (right < 0 && p.ColumnEnd == e.Column) right = i;
            }
            return left >= 0 ? left : right;
        }

        /// <summary>HighestHp(E15,浩瀚):当前生命最高的存活敌人,同值取下标小;没有存活敌人 −1。</summary>
        private int HighestHpEnemy()
        {
            int best = -1;
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive && (best < 0 || _enemies[i].Hp > _enemies[best].Hp)) best = i;
            return best;
        }

        /// <summary>冻结的条件回合加成(E17b,冰冻三尺):目标出字前满足 BonusIf 时 + BonusTurns;缺省 0。</summary>
        private int FreezeBonusTurns(EffectDef effect, int enemyIndex) =>
            effect.BonusTurns > 0 && PreCastConditionMet(effect.BonusIf, enemyIndex) ? effect.BonusTurns : 0;

        /// <summary>只续不挂的减速(E18,倾盆):目标已有的减速(负 SpeedModifier,R1 合并后通常只有一条)TurnsLeft + turns;
        /// 没有减速返回 false(空转)。不经 ApplyStatus:状态没有新挂,只是续时。</summary>
        private bool ExtendSlow(int enemyIndex, int turns)
        {
            bool extended = false;
            foreach (var s in _enemies[enemyIndex].Statuses.All)
            {
                if (s.Kind != StatusKind.SpeedModifier || s.Magnitude >= 0 || s.TurnsLeft < 0) continue;
                s.TurnsLeft += turns;
                extended = true;
            }
            return extended;
        }

        /// <summary>种的回合数(E25):写了 turns 用 turns;仅在减速中且缺 turns 时 = 施加时目标的减速剩余回合;其余 0(调用方兜 1)。</summary>
        private int SeedTurnsOf(EffectDef effect, int enemyIndex)
        {
            if (effect.Turns > 0 || !effect.WhileSlowed) return effect.Turns;
            int left = 0;
            foreach (var s in _enemies[enemyIndex].Statuses.All)
                if (s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0) left = Math.Max(left, s.TurnsLeft);
            return left;
        }
    }
}
