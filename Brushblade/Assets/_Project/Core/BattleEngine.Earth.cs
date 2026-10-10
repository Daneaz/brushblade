using System;

namespace Brushblade.Core
{
    /// <summary>土系专属机制(Plan D2-土)。Task 2 = 护盾操作族:G1 ShieldSpend(塔倾 / 地裂)、G8 ShieldToMorale(土生金)、
    /// G9 ShieldCarry(浮屠)。ApplyEffects 的 switch 与 BeginPlayerTurn 的清盾段各只留一行调用,实现放这里。
    ///
    /// 恒等:G1 / G8 只在新 EffectKind 出现时才走到;G9 挂在每回合必经的清盾段,没挂 ShieldCarry 时一次 Has 判断即返回。
    /// Task 3 = 受击挂点族:G2 HitCap(铜墙铁壁)、G3 HurtShield(堡垒)、G4 ShieldShatter(碎玉),挂在 DamagePlayerDirect /
    /// DamageSummon 里,同样没挂时一次 Has 判断即返回。
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

        // ---- Task 3:受击挂点族(G2 / G3 / G4) ----

        /// <summary>铜墙铁壁(G2)挂载:TurnsLeft 1(同 DamageCut,玩家下回合开始的 tick 到期),同源刷新。</summary>
        private void ApplyHitCap(EffectDef effect, string sourceId)
        {
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = StatusKind.HitCap, Polarity = StatusPolarity.Buff,
                Magnitude = Math.Clamp(effect.Value, 0, 100), TurnsLeft = 1, SourceId = sourceId, TraitKey = effect.TraitKey,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>铜墙铁壁(G2,Q1):格挡 + DamageCut 之后,把单次伤害压到 maxHp × N%(多条取最强);
        /// 削减并入 §5.2.4 的 60% 非护甲减伤预算 —— 结果不低于 preCut × (100 − 60)%(格挡 + DamageCut 已用的部分也算在内)。
        /// preCut = 非护甲减伤之前的伤害(护甲之后)。玩家传玩家最大生命、木灵传木灵 MaxHp。没挂时一次判断即返回。</summary>
        private int CapHitDamage(int damage, int preCut, int maxHp)
        {
            if (!_playerStatuses.Has(StatusKind.HitCap)) return damage;
            int cap = maxHp * _playerStatuses.MaxMagnitude(StatusKind.HitCap) / 100;
            int floor = preCut * (100 - CombatCaps.NonArmorReductionPercent) / 100;
            return Math.Max(Math.Min(damage, cap), floor);
        }

        /// <summary>堡垒(G3)挂载:量 = 卡等级 + L3 之后的 value 过攻击力缩放(同 Shield 本体),出字时定死进 Magnitude;
        /// TurnsLeft = 回合(按玩家回合递减),同源刷新。</summary>
        private void ApplyHurtShield(EffectDef effect, int value, string sourceId)
        {
            int amount = ScaleByBaseAttack(WuxingResolver.ResolveEffect(value));
            if (amount <= 0) return;
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = StatusKind.HurtShield, Polarity = StatusPolarity.Buff,
                Magnitude = amount, TurnsLeft = Math.Max(1, effect.Turns), SourceId = sourceId, TraitKey = effect.TraitKey,
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>堡垒(G3,Q7):DamagePlayerDirect 命中结算之后 —— 只认敌人挥击(allowReflect)且 damage &gt; 0、本击未判负;
        /// 每次都给、不限次数;多条取最强;进普通桶并按请求量攒厚(同 Shield);在触发深度里结算(R4)。
        /// 只落在玩家身上(木灵受击不触发)。没挂时一次判断即返回。</summary>
        private void TriggerHurtShield(int damage, bool allowReflect)
        {
            if (!_playerStatuses.Has(StatusKind.HurtShield)) return;
            if (!allowReflect || damage <= 0 || Phase == BattlePhase.Lost) return;
            int amount = _playerStatuses.MaxMagnitude(StatusKind.HurtShield);
            if (amount <= 0) return;
            EnterTrigger();
            try
            {
                int granted = AddPlayerShield(amount, persist: false);
                GainHeft(amount);
                _events.Add(new BattleEvent(BattleEventKind.Shield, Targeting.PlayerTarget, granted));
            }
            finally { ExitTrigger(); }
        }

        /// <summary>碎玉(G4)挂载:同类只留最强一条(同反震),TurnsLeft -1,随两桶归零由 DropShieldRecoilIfEmpty 移除。</summary>
        private void ApplyShieldShatter(int percent, string sourceId)
        {
            var existing = _playerStatuses.Find(StatusKind.ShieldShatter);
            if (existing != null && existing.Magnitude >= percent) return;
            _playerStatuses.Remove(StatusKind.ShieldShatter);
            _playerStatuses.Apply(new StatusEffect
            {
                Kind = StatusKind.ShieldShatter, Polarity = StatusPolarity.Buff,
                Magnitude = Math.Clamp(percent, 0, 100), TurnsLeft = -1, SourceId = sourceId,
            });
        }

        /// <summary>碎玉(G4,Q6):DamagePlayerDirect 里护盾吸收 &gt; 0 之后、冰晶之前 —— 敌人挥击把两桶打到 0 时,
        /// 同步对攻击者打「本击吸收量 × N%」,钳在 60% 反伤预算剩余量(镜 → 格挡 → 反震之后,调用方算好传入)。
        /// 心属性、无视护甲、不算挥击(allowBarb: false)。载体移除交给紧随的 DropShieldRecoilIfEmpty
        /// (预算耗尽打不出也照样移除)。没挂时一次判断即返回。</summary>
        private void ShatterShieldBack(int enemyIndex, int absorbed, bool allowReflect, int budget)
        {
            if (!_playerStatuses.Has(StatusKind.ShieldShatter)) return;
            if (!allowReflect || _shieldNormal + _shieldPersist > 0 || !_enemies[enemyIndex].Alive) return;
            int dealt = Math.Min(absorbed * _playerStatuses.MaxMagnitude(StatusKind.ShieldShatter) / 100, budget);
            if (dealt > 0)
                DamageEnemy(enemyIndex, dealt, Element.Heart,
                    bypassDefense: true, allowBarb: false,
                    source: EffectSource.ShieldShatter, attackerRef: UnitRef.Player);
        }
    }
}
