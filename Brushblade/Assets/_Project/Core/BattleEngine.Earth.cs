using System;

namespace Brushblade.Core
{
    /// <summary>土系专属机制(Plan D2-土)。Task 2 = 护盾操作族:G1 ShieldSpend(塔倾 / 地裂)、G8 ShieldToMorale(土生金)、
    /// G9 ShieldCarry(浮屠)。ApplyEffects 的 switch 与 BeginPlayerTurn 的清盾段各只留一行调用,实现放这里。
    ///
    /// 恒等:G1 / G8 只在新 EffectKind 出现时才走到;G9 挂在每回合必经的清盾段,没挂 ShieldCarry 时一次 Has 判断即返回。
    /// 一律不摇随机数。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>扣盾(G1,Q5 / Q10):两桶合计 × percent% 向下取整,先扣普通桶、不够再扣留存桶。
        /// 不算受击 —— 不触发冰晶 / 反震(碎玉同理),只判空移除载体;扣到了才发 ShieldBroken(−1, 扣除量)。返回扣除量。</summary>
        private int SpendPlayerShield(int percent)
        {
            int spend = (_shieldNormal + _shieldPersist) * Math.Clamp(percent, 0, 100) / 100;
            if (spend <= 0) return 0;
            int fromNormal = Math.Min(_shieldNormal, spend);
            _shieldNormal -= fromNormal;
            _shieldPersist -= spend - fromNormal;
            DropShieldRecoilIfEmpty();
            _events.Add(new BattleEvent(BattleEventKind.ShieldBroken, -1, spend));
            return spend;
        }

        /// <summary>土生金(G8,Q18):按 G1 扣盾,战意 + ⌈扣除量 / ResourceThresholdValue⌉(扣除量 0 → +0)。
        /// 走 AddPlayerCounter 钳 MoraleCap,不经 ResolveMorale —— 不计聚金的溢出。</summary>
        private void ResolveShieldToMorale(int percent)
        {
            int spent = SpendPlayerShield(percent);
            if (spent <= 0) return;
            int stacks = (spent + ResourceThresholdValue - 1) / ResourceThresholdValue;
            AddPlayerCounter(StatusKind.Morale, stacks, MoraleCapOrDefault);
        }

        /// <summary>浮屠(G9)挂载:Magnitude = 加回百分比,TurnsLeft = 回合(按玩家回合递减,之后 N 个回合开始各一次)。</summary>
        private void ApplyShieldCarry(EffectDef effect, string sourceId)
        {
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = StatusKind.ShieldCarry, Polarity = StatusPolarity.Buff,
                Magnitude = effect.Value, TurnsLeft = Math.Max(1, effect.Turns), SourceId = sourceId, TraitKey = effect.TraitKey,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>浮屠(G9,Q4):BeginPlayerTurn 清普通桶之后、判空移除载体之前,把被清量 × N% 作普通桶加回(不攒厚)。
        /// 多条取最强。留存桶本来不清,不参与。没挂时一次判断即返回。</summary>
        private void CarryShieldOnTurnClear(int clearedNormal)
        {
            if (!_playerStatuses.Has(StatusKind.ShieldCarry)) return;
            int percent = 0;
            foreach (var s in _playerStatuses.All)
                if (s.Kind == StatusKind.ShieldCarry && s.Magnitude > percent) percent = s.Magnitude;
            int kept = clearedNormal * Math.Min(percent, 100) / 100;
            if (kept <= 0) return;
            int granted = AddPlayerShield(kept, persist: false);
            if (granted > 0) _events.Add(new BattleEvent(BattleEventKind.Shield, Targeting.PlayerTarget, granted));
        }
    }
}
