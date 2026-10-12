using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>跨场卷轴 / 气泡一行的效果文案(Plan E4 Task 6,CarryOver 稿 R7)。
    ///
    /// 只给数据里实际出现的 5 个 Kind 写紧凑句(附录 §D 一.5);别的 Kind 回落 <see cref="CharInfo.DetachedEffectsText"/>
    /// 的整句 —— 不直接复用 CharInfo 的卡面句,是因为那句(如战意「每层攻击 +X%,上限 5 层」)放不进卷轴约 415 宽的效果栏。
    /// 数值走 <see cref="MetaRules.ScaleEffectValue"/>,与 CharInfo 卡面 shown、开局结算同一口径(按来源字等级缩放)。
    ///
    /// 给两串:Plain 用来量宽 / 截断(测量只吃纯文本,§C 口径),Rich 把数字染成 text-main(稿 .ef .n),
    /// 只在最后赋 Text.text 时用;截断过就用截断后的 Plain,不再上色。</summary>
    internal static class CarryOverText
    {
        public static (string Plain, string Rich) Effect(OpeningEffect o, int cardLevel)
        {
            int value = MetaRules.ScaleEffectValue(o.Kind, o.Value, cardLevel);
            string v = value.ToString();
            string t = Mathf.Max(1, o.Turns).ToString();
            string vr = Num(v);
            string tr = Num(t);
            switch (o.Kind)
            {
                case EffectKind.BurnAll:
                    return (Strings.T("battle.carryover.effect.burnall", ("value", v)),
                        Strings.T("battle.carryover.effect.burnall", ("value", vr)));
                case EffectKind.Morale:
                    return (Strings.T("battle.carryover.effect.morale", ("value", v)),
                        Strings.T("battle.carryover.effect.morale", ("value", vr)));
                case EffectKind.HealOverTime:
                    return (Strings.T("battle.carryover.effect.healovertime", ("turns", t)),
                        Strings.T("battle.carryover.effect.healovertime", ("turns", tr)));
                case EffectKind.Shield:
                    return (Strings.T("battle.carryover.effect.shield", ("value", v)),
                        Strings.T("battle.carryover.effect.shield", ("value", vr)));
                case EffectKind.DefenseBuff:
                    return (Strings.T("battle.carryover.effect.defensebuff", ("value", v)),
                        Strings.T("battle.carryover.effect.defensebuff", ("value", vr)));
                default:
                    string whole = CharInfo.DetachedEffectsText(new[] { o }, cardLevel);
                    return (whole, whole);
            }
        }

        private static string Num(string n) => $"<color=#{ColorUtility.ToHtmlStringRGB(Theme.TextMain)}>{n}</color>";
    }
}
