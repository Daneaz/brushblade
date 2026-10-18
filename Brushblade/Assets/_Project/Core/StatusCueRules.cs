namespace Brushblade.Core
{
    /// <summary>「上状态」事件(<see cref="BattleEventKind.StatusApplied"/>)的播报范围(spec 2026-10-18 §1)。
    /// 不播:隐藏载体(Presentation 的 StatusText.Of 无名、没有 chip,审计 §5)与 ApBoost(没有 chip,反馈是 AP 格数)。
    /// 对账见 StatusAppliedEventTests.IsAnnounced_ExactlyVisibleAndNotApBoost。</summary>
    public static class StatusCueRules
    {
        public static bool IsAnnounced(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.ApBoost:
                case StatusKind.ObsoleteDamageReduction:
                case StatusKind.TraitRider:
                case StatusKind.ShieldRecoil:
                case StatusKind.BurnGrow:
                case StatusKind.BurnHold:
                case StatusKind.BurnBurstMark:
                case StatusKind.BurnBacklashMark:
                case StatusKind.ApRefund:
                case StatusKind.MoraleArmor:
                case StatusKind.MoraleShield:
                case StatusKind.FrostBite:
                case StatusKind.ThawStrike:
                case StatusKind.ThawSlow:
                case StatusKind.ShieldFrost:
                case StatusKind.ShieldShatter:
                    return false;
                default:
                    return true;
            }
        }
    }
}
