namespace Brushblade.Core
{
    /// <summary>头行护甲格的形态(2026-10-21 拍板稿「削甲图标」):无减益 / 削甲 / 破甲。</summary>
    public enum ArmorCellKind { Normal, Shred, Sunder }

    /// <summary>头行护甲格该显示什么。Visible = 出不出格;Kind = 用哪枚图标;
    /// Value = 剩余护甲;ShowValue = 是否印数字(破甲杂兵为纯图标)。</summary>
    public readonly struct ArmorCell
    {
        public bool Visible { get; }
        public ArmorCellKind Kind { get; }
        public int Value { get; }
        public bool ShowValue { get; }

        public ArmorCell(bool visible, ArmorCellKind kind, int value, bool showValue)
        {
            Visible = visible; Kind = kind; Value = value; ShowValue = showValue;
        }
    }

    /// <summary>护甲格显示规则的唯一出处,Presentation 只读不拼。
    /// 「基准护甲」= 被破甲 / 削甲 / 封禁影响前的护甲(含增益);基准 ≤ 0 不出格。</summary>
    public static class ArmorCellRules
    {
        /// <summary>敌人:Value 读 <see cref="BattleEngine.DisplayedEnemyDefense"/>。</summary>
        public static ArmorCell ForEnemy(EnemyState enemy) =>
            ForUnit(enemy.Defense, BattleEngine.DisplayedEnemyDefense(enemy), enemy.Statuses, enemy.IsBoss);

        /// <summary>玩家 / 召唤物共用。<paramref name="baseDefense"/> 为未被减益前的护甲,
        /// <paramref name="shownDefense"/> 为减益后实际数。</summary>
        public static ArmorCell ForUnit(int baseDefense, int shownDefense, StatusBag statuses, bool isBoss)
        {
            if (baseDefense <= 0) return new ArmorCell(false, ArmorCellKind.Normal, 0, false);
            if (statuses.Has(StatusKind.Sunder))
                return new ArmorCell(true, ArmorCellKind.Sunder, shownDefense, isBoss);
            if (statuses.TotalMagnitude(StatusKind.ArmorBreak) > 0)
                return new ArmorCell(true, ArmorCellKind.Shred, shownDefense, true);
            return new ArmorCell(shownDefense > 0, ArmorCellKind.Normal, shownDefense, true);
        }
    }
}
