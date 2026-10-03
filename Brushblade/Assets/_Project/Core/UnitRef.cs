using System;

namespace Brushblade.Core
{
    public enum UnitSide
    {
        None,
        Player,
        Summon,
        Enemy,
    }

    /// <summary>战斗内单位引用:玩家 / 某槽位的召唤物 / 某下标的敌人。default = None。</summary>
    public readonly struct UnitRef : IEquatable<UnitRef>
    {
        public UnitSide Side { get; }
        /// <summary>Summon = 槽位,Enemy = 下标,Player/None = −1。</summary>
        public int Index { get; }

        public UnitRef(UnitSide side, int index)
        {
            Side = side;
            Index = index;
        }

        public static UnitRef None => new UnitRef(UnitSide.None, -1);
        public static UnitRef Player => new UnitRef(UnitSide.Player, -1);
        public static UnitRef Summon(int slot) => new UnitRef(UnitSide.Summon, slot);
        public static UnitRef Enemy(int index) => new UnitRef(UnitSide.Enemy, index);

        /// <summary>只有 Summon/Enemy 比较 Index:None/Player 的 Index 无意义,
        /// 这样 default(UnitRef)(Index = 0)与 UnitRef.None(Index = −1)相等。</summary>
        public bool Equals(UnitRef other) =>
            Side == other.Side && (!HasIndex || Index == other.Index);
        public override bool Equals(object obj) => obj is UnitRef o && Equals(o);
        public override int GetHashCode() => HasIndex ? ((int)Side * 397) ^ Index : (int)Side * 397;
        private bool HasIndex => Side == UnitSide.Summon || Side == UnitSide.Enemy;
        public static bool operator ==(UnitRef a, UnitRef b) => a.Equals(b);
        public static bool operator !=(UnitRef a, UnitRef b) => !a.Equals(b);
        public override string ToString() => Side == UnitSide.Player || Side == UnitSide.None ? Side.ToString() : $"{Side}#{Index}";
    }
}
