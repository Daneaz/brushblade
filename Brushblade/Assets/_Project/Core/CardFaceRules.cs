using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>两面字一面拖出去能落的位置(Plan E1 口径 F4)。可组合。</summary>
    [Flags]
    public enum FaceLanding
    {
        None = 0,
        Enemy = 1,
        Self = 2,
        Summons = 4,
        EmptySlot = 8,
        Graft = 16,
    }

    /// <summary>两面字的静态规则(spec v7 §2.1/§2.2)。纯查询,不看场面、不改结算,对仿真恒等。</summary>
    public static class CardFaceRules
    {
        /// <summary>两面字:攻击面与五行面都有本体效果。单面字不能翻。</summary>
        public static bool HasTwoFaces(CharDef def) =>
            def.AttackEffects.Count > 0 && def.Effects.Count > 0;

        /// <summary>这一面拖出去能落在哪。纯函数,不看场面(场上有没有木灵、哪格空由表现层再求交)。
        /// 两面字的攻击面一律 Enemy;其余按 F4 由引擎既有查询推出。</summary>
        public static FaceLanding Landing(CharDef def, CardFace face, int cardLevel)
        {
            bool attackMode = face == CardFace.Attack;
            // 只有一面的字,两个 face 实际落在同一面(与 BattleEngine.FaceOf 同口径)
            var actual = BattleEngine.FaceOf(def, attackMode);
            if (actual == CardFace.Attack && HasTwoFaces(def)) return FaceLanding.Enemy;

            var effects = TraitRules.CastEffects(def, actual, cardLevel);
            bool summons = false, hostile = false;
            foreach (var e in effects)
            {
                if (e.Kind == EffectKind.Summon || e.Kind == EffectKind.SummonSapling) summons = true;
                if (IsHostileTargeted(e)) hostile = true;
            }

            bool needsEnemy = BattleEngine.NeedsTarget(def, attackMode, cardLevel);
            bool needsAlly = BattleEngine.NeedsAllyTarget(def, attackMode);

            var landing = FaceLanding.None;
            // D2-水 E26(溃围):需要友方目标的面,pick All 的敌方附带不另加 Enemy 落点(那条效果不选敌)
            if (needsEnemy || (hostile && !needsAlly)) landing |= FaceLanding.Enemy;
            if (needsAlly) landing |= FaceLanding.Self | FaceLanding.Summons;
            else if (!needsEnemy && !summons && !hostile) landing |= FaceLanding.Self;
            if (summons) landing |= FaceLanding.EmptySlot;
            if (summons && HasGraftFace(def, actual, effects)) landing |= FaceLanding.Graft;
            return landing;
        }

        /// <summary>作用于敌人的效果 Kind。**新增敌对 Kind 要登记到这里**,否则落点会漏 Enemy。
        /// 注:Execute 是 DamageSingle 上的字段,不是独立 Kind。</summary>
        internal static readonly HashSet<EffectKind> HostileKinds = new()
        {
            EffectKind.DamageSingle, EffectKind.BurnSingle, EffectKind.BurnAll, EffectKind.Bleed,
            EffectKind.Freeze, EffectKind.Slow, EffectKind.ArmorBreak, EffectKind.Dispel, EffectKind.Blind,
            EffectKind.Silence, EffectKind.BurnNoDecay, EffectKind.BurnSettleNow, EffectKind.Detonate,
            EffectKind.Charm, EffectKind.Quench, EffectKind.Weaken, EffectKind.Seed, EffectKind.Vulnerable,
            EffectKind.SpendHeft, EffectKind.SpendWellspring, EffectKind.SummonStrike,
            EffectKind.BurnScale, EffectKind.BurnEqualize,   // D2-火 Task 2:动的是敌人的灼
            EffectKind.HealBlock, EffectKind.BurnGrow, EffectKind.BurnHold,   // D2-火 Task 3:挂在敌人的灼上
            EffectKind.BurnBurst, EffectKind.BurnBacklash,
            EffectKind.Mine,   // D2-火 Task 4:埋在敌人身上(受击回敬 Retaliate 挂在玩家身上,不敌对)
            EffectKind.Doom,   // D2-金 Task 3:致命挂在敌人身上
            EffectKind.ExtraStrike, EffectKind.Thaw, EffectKind.Reveal,   // D2-火 Task 5:落在敌人身上(自损 SelfCost 作用于玩家,不敌对)
            EffectKind.FrostBite, EffectKind.ThawStrike, EffectKind.ThawSlow,   // D2-水 Task 2:挂在敌人的冻结上
            EffectKind.ChargeDelay,   // D2-水 Task 2:推迟 Boss 蓄力
            EffectKind.BuffBlock,     // D2-水 Task 3:挂在敌人身上(DebuffWard 挂我方,不敌对)
        };

        /// <summary>敌对且取目标为 Primary 或全体(落任一敌人即成立)。pick Random / HitTargets / MostBurn /
        /// FrozenByThisCast 的敌对效果是附带效果,不单独构成 Enemy 落点。</summary>
        public static bool IsHostileTargeted(EffectDef e)
        {
            if (!HostileKinds.Contains(e.Kind)) return false;
            var pick = EffectPickRules.Effective(e);
            return pick == EffectPick.Primary || pick == EffectPick.All;
        }

        /// <summary>木的五行面里有本体召唤 —— 与 BattleEngine.HasSummonFace(私有,依赖实例卡等级)同判据:
        /// 非攻击面、木系、有 EffectKind.Summon(幼苗不算)。改那边时同步这里。</summary>
        private static bool HasGraftFace(CharDef def, CardFace face, List<EffectDef> effects)
        {
            if (face == CardFace.Attack || def.Element != Element.Wood) return false;
            foreach (var e in effects)
                if (e.Kind == EffectKind.Summon) return true;
            return false;
        }
    }
}
