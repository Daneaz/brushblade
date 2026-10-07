using System;

namespace Brushblade.Core
{
    /// <summary>火系专属机制(Plan D2-火):灼操作族(Task 2,附录 N1 / N2 / N4)。ApplyEffects 的 switch 只留一行分派,
    /// 实现放这里,免得那个 switch 继续膨胀。引爆的 retain / portion(N3)改在 <c>Detonate</c> 原处;每击附带(N4b)
    /// 在 DamageSingle 的多段循环里递归调用 ResolveEffect(BattleEngine.cs)。
    ///
    /// 恒等:这些分支只在新 EffectKind / 新字段非缺省时才走到;一律不摇随机数。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>BurnScale(N1,焦土 / 炎炎 / 灿然):目标灼层数 × percent%(向下取整),钳到上限;只升不降,0 层空转。</summary>
        private void ScaleBurnOn(int enemyIndex, int percent, int potency)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;
            int stacks = enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
            if (stacks <= 0) return;
            RaiseBurnTo(enemyIndex, (int)Math.Min(CombatCaps.BurnStacks, (long)stacks * percent / 100), potency);
        }

        /// <summary>BurnEqualize(N2,火烧连营):存活敌人的最高层数 M,每名存活敌人补到 M(0 层也补);全场 0 层空转。
        /// 按调用时的表长遍历(与全体伤害同纪律)。</summary>
        private void EqualizeBurn(int potency)
        {
            int count = _enemies.Count;
            int max = 0;
            for (int i = 0; i < count; i++)
                if (_enemies[i].Alive) max = Math.Max(max, _enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0);
            if (max <= 0) return;
            for (int i = 0; i < count; i++)
                if (_enemies[i].Alive) RaiseBurnTo(i, max, potency);
        }

        /// <summary>把一名敌人的灼补到 <paramref name="stacks"/> 层(不高于现有层数时什么都不做)。火力取
        /// max(原火力, 100, 本字火力)(spec v7 §4,同 ApplyBurn)。发 Burn 事件报实际增量;记进本次出字的上灼名单
        /// (BurnedByThisCast:翻倍 / 拉平也算本字上了灼)。</summary>
        private void RaiseBurnTo(int enemyIndex, int stacks, int potency)
        {
            var enemy = _enemies[enemyIndex];
            var existing = enemy.Statuses.Find(StatusKind.Burn);
            int before = existing?.Magnitude ?? 0;
            if (stacks <= before) return;
            ApplyStatus(enemy.Statuses, new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = stacks, TurnsLeft = -1, Potency = Math.Max(Math.Max(existing?.Potency ?? 0, 100), potency),
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
            int gain = (enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0) - before;
            if (gain > 0) _events.Add(new BattleEvent(BattleEventKind.Burn, enemyIndex, gain));
            if (!_cast.BurnedTargets.Contains(enemyIndex)) _cast.BurnedTargets.Add(enemyIndex);
        }

        // ---- N4 计数缩放(G2):Amplify 读出字前快照(R3,条件类);HealSelf 读结算那一刻(产出量) ----

        /// <summary>出字前每名敌人的灼层数(死者记 0)。只读状态、不摇号。</summary>
        private int[] CapturePreCastBurnStacks()
        {
            var stacks = new int[_enemies.Count];
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive) stacks[i] = _enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
            return stacks;
        }

        /// <summary>出字前这名敌人的灼层数。出字之外(快照为 null)读现值;不选敌(−1)/ 出字途中才出现的敌人 = 0。</summary>
        private int PreCastBurnStacksOf(int enemyIndex)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemies.Count) return 0;
            var snap = _cast.PreCastBurnStacks;
            if (snap == null) return _enemies[enemyIndex].Alive ? _enemies[enemyIndex].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0 : 0;
            return enemyIndex < snap.Length ? snap[enemyIndex] : 0;
        }

        /// <summary>出字前带灼的存活敌人数。</summary>
        private int PreCastBurningEnemies()
        {
            var snap = _cast.PreCastBurnStacks ?? CapturePreCastBurnStacks();
            int n = 0;
            foreach (int s in snap) if (s > 0) n++;
            return n;
        }

        /// <summary>一条带计数缩放的 Amplify 加成项:百分点 × 计数(出字前),cap &gt; 0 时钳到 cap。</summary>
        private int ScaledAmpPercent(int percent, ScaleBasis per, int cap, int enemyIndex)
        {
            long count = per == ScaleBasis.BurnStack ? PreCastBurnStacksOf(enemyIndex) : PreCastBurningEnemies();
            long sum = percent * count;
            if (cap > 0) sum = Math.Min(sum, cap);
            return (int)sum;
        }

        /// <summary>结算那一刻的计数(温润:本字先上的灼也算,G2)。BurnStack = 存活敌人灼层数之和;BurningEnemy = 带灼的存活敌人数。</summary>
        private int CurrentBurnCount(ScaleBasis per)
        {
            int n = 0;
            foreach (var e in _enemies)
            {
                if (!e.Alive) continue;
                int stacks = e.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
                n += per == ScaleBasis.BurnStack ? stacks : stacks > 0 ? 1 : 0;
            }
            return n;
        }
    }
}
