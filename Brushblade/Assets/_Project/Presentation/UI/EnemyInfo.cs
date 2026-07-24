using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>敌人简述:从定义机械生成(属性/血攻/能力/Boss 阶段)。战斗页与图鉴共用。</summary>
    public static class EnemyInfo
    {
        /// <summary>怪的代表字(圆形头像用):Boss 取当前阶段字,小怪取名字首字。战斗与图鉴共用。</summary>
        public static string FaceChar(EnemyDef def, int phaseIndex) =>
            def.Phases.Count > 0 ? def.Phases[phaseIndex].Char : def.Id.Substring(0, 1);

        public static string Detail(EnemyDef def)
        {
            var text = new StringBuilder();
            text.Append(def.Phases.Count > 0 ? "成语 Boss·四阶段" : "小怪").Append('\n');
            text.Append(CharInfo.ElementName(def.Element)).Append("系 · 血 ")
                .Append(def.MaxHp).Append(" · 攻 ").Append(def.Attack).Append('\n');
            if (def.Phases.Count == 0 && def.DamageTaken < 1f)
                text.Append("承伤 ×").Append(def.DamageTaken).Append("(被克制可破)\n");
            text.Append(AbilityText(def));

            if (def.Phases.Count > 0)
            {
                text.Append("\n阶段:");
                for (int i = 0; i < def.Phases.Count; i++)
                {
                    var phase = def.Phases[i];
                    if (i > 0) text.Append(" → ");
                    text.Append('【').Append(phase.Char).Append('】')
                        .Append(CharInfo.ElementName(phase.Element))
                        .Append(' ').Append(phase.MaxHp).Append('/').Append(phase.Attack);
                    if (phase.DamageTaken < 1f)
                        text.Append("(承伤×").Append(phase.DamageTaken).Append(')');
                }
            }
            return text.ToString();
        }

        public static string AbilityText(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Regrow => "缺笔:每回合自补全(攻 +2、回 3 血),第 3 次补全后攻翻倍并回满",
            EnemyAbility.Split => "叠字:首次受击存活则分裂成两个半血(场上不足 4 只时)",
            EnemyAbility.Buff => $"标点:有同伴时每回合给其他怪攻 +{def.Attack}(本场累计不回滚);"
                + "落单则亲自出手——优先击杀目标",
            EnemyAbility.Disguise => $"通假:伪装成{CharInfo.ElementName(def.DisguiseElement)}系,首次行动后现形",
            EnemyAbility.Obscure => "生僻:属性隐藏,受击两次后被「读懂」现形",
            EnemyAbility.Scorch => "自燃:每次受击存活,攻 +2(越磨越烫,宜速杀)",
            _ => "无特殊能力",
        };
    }
}
