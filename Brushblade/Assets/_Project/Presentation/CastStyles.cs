using System.Collections.Generic;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>我方出字的招式动效(2026-09-30,用户在「招式动效」demo 里拍板)。
    /// 此前所有攻击字一律是「字牌飞过去」;现在按招式分:剑是一道横扫刀光、剁是双刀连剁、
    /// 炎是火球、冰是冰刺、碎是落石……</summary>
    public enum CastStyle
    {
        Glyph,       // 字牌飞过去(非攻击字 / 兜底)
        Slash,       // 刀光·单斩:利 锋 剿
        HeavyChop,   // 重斩:铡
        Sweep,       // 刀光·横扫:剑 鑫
        DoubleChop,  // 双刀连剁:剁 刲 鍂(分段伤害)
        Thrust,      // 突刺:锥(贯穿一列)
        Fireball,    // 火球:炎 燥 蒸 热 灿
        FireWave,    // 火浪:爆 烈 焚 焱 燚
        Blast,       // 爆破:炸
        IceShard,    // 冰刺:冰 淼
        Frost,       // 寒雾:冷 冻
        WaterChain,  // 水弹连跳:海
        Wave,        // 浪潮:溃 淋 㵘
        InkSeal,     // 静默水印:湮 澡 沐
        Rock,        // 落石:碉 垒 堡 壁 碎 垚 圭 杜
        RockVolley,  // 连投:塔
        Quake,       // 地震:崩 㙓
        Petals,      // 花瓣:花
    }

    /// <summary>字 → 招式。**按效果推,少数招牌字单独点名** —— 新加的字自动落到合适的一类,
    /// 不会因为漏登记而退回飞字牌。点名表只放「按效果推不出来」的那几张(剑/剁 这类靠形状、
    /// 炸/海/塔 这类靠特殊机制的,推也推得出,但点名更不容易被将来的数值改动带偏)。</summary>
    public static class CastStyles
    {
        private static readonly Dictionary<string, CastStyle> ById = new()
        {
            { "剑", CastStyle.Sweep }, { "鑫", CastStyle.Sweep },
            { "剁", CastStyle.DoubleChop }, { "刲", CastStyle.DoubleChop }, { "鍂", CastStyle.DoubleChop },
            { "铡", CastStyle.HeavyChop },
            { "锥", CastStyle.Thrust },
            { "炸", CastStyle.Blast },
            { "海", CastStyle.WaterChain },
            { "塔", CastStyle.RockVolley },
            { "花", CastStyle.Petals },
        };

        /// <summary>这次出字用哪套招式。attackMode 与 BattleEngine 取效果列表的口径一致:
        /// 攻面有效果就看攻面,否则看本面。没有任何伤害的出字(护盾/治疗/召唤)返回 Glyph。</summary>
        public static CastStyle For(CharDef def, bool attackMode)
        {
            if (def == null) return CastStyle.Glyph;
            var effects = attackMode && def.AttackEffects.Count > 0 ? def.AttackEffects : def.Effects;
            bool single = false, all = false, freeze = false, slow = false, silence = false;
            int hitCount = 1;
            TargetArea shape = TargetArea.Single;
            foreach (var e in effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.DamageSingle:
                        // 全体(spec v7 §11.6:原 DamageAll)走原来 all 那一支,招式不变
                        if (e.Shape == TargetArea.All) { all = true; break; }
                        if (!single) { shape = e.Shape; hitCount = e.HitCount; }
                        single = true;
                        break;
                    case EffectKind.Freeze: freeze = true; break;
                    case EffectKind.Slow: slow = true; break;
                    case EffectKind.Silence: silence = true; break;
                }
            }
            if (!single && !all) return CastStyle.Glyph;
            if (ById.TryGetValue(def.Id, out var named)) return named;

            // 形状比属性更能说明「这一下长什么样」:两段就是两刀,横扫就是一排
            if (single && hitCount > 1) return CastStyle.DoubleChop;
            if (single && shape == TargetArea.Row) return CastStyle.Sweep;
            if (single && shape == TargetArea.Column) return CastStyle.Thrust;
            if (single && shape == TargetArea.Chain) return CastStyle.WaterChain;
            if (single && shape == TargetArea.Scatter) return CastStyle.RockVolley;

            switch (def.Element)
            {
                case Element.Metal: return CastStyle.Slash;
                case Element.Fire: return all ? CastStyle.FireWave : CastStyle.Fireball;
                case Element.Water:
                    if (all) return CastStyle.Wave;
                    if (freeze) return CastStyle.IceShard;
                    if (slow) return CastStyle.Frost;
                    if (silence) return CastStyle.InkSeal;
                    return CastStyle.IceShard;
                case Element.Earth: return all ? CastStyle.Quake : CastStyle.Rock;
                case Element.Wood: return CastStyle.Petals;
                default: return CastStyle.Glyph;
            }
        }
    }
}
