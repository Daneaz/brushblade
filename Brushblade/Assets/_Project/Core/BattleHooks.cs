namespace Brushblade.Core
{
    /// <summary>战斗钩子种类(spec v6 §5 触发词表)。</summary>
    public enum HookKind
    {
        PlayerHit,          // 玩家被命中(含被护盾/免疫吃掉的)
        SummonHit,          // 召唤物被命中
        EnemyHit,           // 敌人受到直接伤害(DamageEnemy;不含灼烧/流血结算)
        EnemyKilled,        // 敌人死亡(Other = 击杀者,Source = 致死来源)
        SummonDied,         // 召唤物阵亡
        StatusApplied,      // 状态施加(Subject = 承受者,Other = 施加者)
        TurnStarted,        // 单位那一拍开始(Subject = 行动者)
        TurnEnded,          // 单位那一拍结束;玩家 = 让出行动权
        HpThresholdCrossed, // 生命从 ≥50% 跌到 <50%,每单位每场一次,复活不重置(R5)
        Composed,           // 合成出某字(CharId)
        Dismantled,         // 拆开某字(CharId)
    }

    /// <summary>一次钩子事件。不用的字段保持缺省:Status = 0、CharId = null、Amount = 0。</summary>
    public readonly struct HookArgs
    {
        public HookKind Kind { get; }
        public UnitRef Subject { get; }
        public UnitRef Other { get; }
        public int Amount { get; }
        public StatusKind Status { get; }
        public string CharId { get; }
        public EffectSource Source { get; }
        /// <summary>触发深度(R4):0 = 正常行动;>0 = 由特性触发的结算内部发生的。</summary>
        public int Depth { get; }

        public HookArgs(HookKind kind, UnitRef subject, UnitRef other, int amount = 0,
            StatusKind status = default, string charId = null, EffectSource source = EffectSource.None, int depth = 0)
        {
            Kind = kind;
            Subject = subject;
            Other = other;
            Amount = amount;
            Status = status;
            CharId = charId;
            Source = source;
            Depth = depth;
        }
    }

    /// <summary>钩子监听器。被动特性运行时(Plan D)实现它;测试用记录器实现它。</summary>
    public interface IBattleHookListener
    {
        void OnHook(BattleEngine battle, in HookArgs args);
    }
}
