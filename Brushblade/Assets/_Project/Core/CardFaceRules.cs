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
            bool summons = false, wideDamage = false;
            foreach (var e in effects)
            {
                if (e.Kind == EffectKind.Summon || e.Kind == EffectKind.SummonSapling) summons = true;
                if (e.Kind == EffectKind.DamageSingle
                    && (e.Shape == TargetArea.All || e.Shape == TargetArea.Scatter)) wideDamage = true;
            }

            bool needsEnemy = BattleEngine.NeedsTarget(def, attackMode, cardLevel);
            bool needsAlly = BattleEngine.NeedsAllyTarget(def, attackMode);

            var landing = FaceLanding.None;
            if (needsEnemy || wideDamage) landing |= FaceLanding.Enemy;
            if (needsAlly) landing |= FaceLanding.Self | FaceLanding.Summons;
            else if (!needsEnemy && !summons && !wideDamage) landing |= FaceLanding.Self;
            if (summons) landing |= FaceLanding.EmptySlot;
            if (summons && HasGraftFace(def, actual, effects)) landing |= FaceLanding.Graft;
            return landing;
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
