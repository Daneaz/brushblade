namespace Brushblade.Core
{
    /// <summary>怪物图鉴(2026-07-22 拍板):击败即解锁,赏钱不自动进账——
    /// 需玩家主动点进图鉴查阅该怪详情才发,主页红点提示有未查阅的条目。
    /// 赏钱直接入账户(一次性、全额,与层段首破里程碑同口径),不受当次爬塔成败影响。</summary>
    public static class BestiaryRules
    {
        public const int MinionBounty = 20;
        public const int BossBounty = 50;

        /// <summary>记击败;首次解锁返回 true(重复击败不重复记)。</summary>
        public static bool RecordDefeat(MetaState meta, string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId) || meta.DefeatedEnemies.Contains(enemyId))
                return false;
            meta.DefeatedEnemies.Add(enemyId);
            return true;
        }

        public static bool IsUnlocked(MetaState meta, string enemyId) =>
            meta.DefeatedEnemies.Contains(enemyId);

        /// <summary>有已解锁但未查阅的条目(主页红点)。</summary>
        public static bool HasUnclaimed(MetaState meta)
        {
            foreach (var id in meta.DefeatedEnemies)
                if (!meta.ClaimedBestiary.Contains(id))
                    return true;
            return false;
        }

        public static bool IsClaimed(MetaState meta, string enemyId) =>
            meta.ClaimedBestiary.Contains(enemyId);

        /// <summary>查阅领赏:未解锁或已领返回 0,否则入账并返回赏钱数额。</summary>
        public static int TryClaim(MetaState meta, EnemyDef def)
        {
            if (def == null || !IsUnlocked(meta, def.Id) || IsClaimed(meta, def.Id))
                return 0;
            meta.ClaimedBestiary.Add(def.Id);
            int bounty = def.Phases.Count > 0 ? BossBounty : MinionBounty;
            meta.Ink += bounty;
            return bounty;
        }
    }
}
