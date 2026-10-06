namespace Brushblade.Core
{
    /// <summary>战斗钩子种类(spec v7 触发词表)。只在末尾追加。
    ///
    /// 各钩子的 <see cref="HookArgs.Amount"/> 口径(不用的保持 0):
    /// - PlayerHit:护甲折算(与格挡减伤)之后、护盾吸收之前的伤害;免疫挡下为 0。
    ///   <see cref="HookArgs.Absorbed"/> = 其中被护盾吃掉的部分(Amount − Absorbed = 实际掉血)。
    /// - SummonHit:过完生克与护甲之后、护盾吸收之前的伤害(taken);免疫挡下为 0;
    ///   吞噬 = 被吞时的剩余血量。Absorbed 同上(吞噬绕过护盾,恒 0)。
    /// - EnemyHit:实际掉血(不含护盾吸收与过量伤害)。
    /// - StatusApplied:施加**之后**该条状态的 Magnitude(总量,不是本次增量;累加型计数器
    ///   战意 / AP 加成、攒层型的厚 / 泉同口径)。
    /// - HpThresholdCrossed:阈值百分比(50)。
    /// - 其余:0。</summary>
    public enum HookKind
    {
        PlayerHit,          // 玩家被命中(含被护盾/免疫吃掉的);Source = 来源(铁画反噬 = IronBarb)
        SummonHit,          // 召唤物被命中
        EnemyHit,           // 敌人受到直接伤害(DamageEnemy);CastCharId = 出字中的那张字(非出字为 null)。
                            // 灼烧/流血结算、引爆、斩杀**不发**本钩子 —— 它们致死时只发 EnemyKilled(Source 区分)
        EnemyKilled,        // 敌人死亡(Other = 击杀者,Source = 致死来源,CastCharId 同 EnemyHit)
        SummonDied,         // 召唤物阵亡
        StatusApplied,      // 状态施加(Subject = 承受者,Other = 施加者);只在状态确实挂上/变化时发:
                            // 累加型计数器做增量时也发;厚/泉余数没攒够一层时不发
        TurnStarted,        // 单位那一拍开始(Subject = 行动者)
        TurnEnded,          // 单位那一拍结束;玩家 = 让出行动权。敌人/召唤物被灼烧、流血烧死的那一拍也发
        HpThresholdCrossed, // 生命从 ≥50% 跌到 <50%,每单位每场一次,复活不重置(R5)
        Composed,           // 合成出某字(CharId)
        Dismantled,         // 拆开某字(CharId)
    }

    /// <summary>一次钩子事件。不用的字段保持缺省:Status = 0、CharId = null、Amount = 0、
    /// Absorbed = 0、CastCharId = null。</summary>
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
        /// <summary>PlayerHit / SummonHit:Amount 中被护盾吸收的部分。</summary>
        public int Absorbed { get; }
        /// <summary>EnemyHit / EnemyKilled:正在出字的字 ID;非出字(敌人/召唤物那一拍、排空反应)为 null。</summary>
        public string CastCharId { get; }

        public HookArgs(HookKind kind, UnitRef subject, UnitRef other, int amount = 0,
            StatusKind status = default, string charId = null, EffectSource source = EffectSource.None, int depth = 0,
            int absorbed = 0, string castCharId = null)
        {
            Kind = kind;
            Subject = subject;
            Other = other;
            Amount = amount;
            Status = status;
            CharId = charId;
            Source = source;
            Depth = depth;
            Absorbed = absorbed;
            CastCharId = castCharId;
        }
    }

    /// <summary>钩子监听器。被动特性运行时(Plan D)实现它;测试用记录器实现它。</summary>
    public interface IBattleHookListener
    {
        void OnHook(BattleEngine battle, in HookArgs args);
    }
}
