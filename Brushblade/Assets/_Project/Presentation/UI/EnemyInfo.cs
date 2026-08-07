using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>敌人文案的唯一来源:属性/血攻/小怪能力/Boss 技能。战斗页与图鉴共用。
    /// 文案写精确数值(沿用既有惯例),故平衡改动时需同步这里。</summary>
    public static class EnemyInfo
    {
        /// <summary>蓄力计数阈值,对应 <c>BattleConfig.BossChargeEvery</c>(默认 2)——注意这是阈值,
        /// 不是周期:实际节拍是「(阈值−1)回合普攻 + 1 回合蓄力(不出手) + 1 回合释放」一轮,
        /// 阈值=2 时就是 3 个敌方回合一轮(见 <see cref="ChargeRuleText"/>,Finding 1)。
        /// 本类拿不到 BattleConfig 实例,故在此写常量;改配置需同步这里。</summary>
        private const int ChargeEvery = 2;

        /// <summary>怪的代表字(圆形头像用):Boss 取当前阶段字,小怪取名字首字。战斗与图鉴共用。</summary>
        public static string FaceChar(EnemyDef def, int phaseIndex) =>
            def.Phases.Count > 0 ? def.Phases[phaseIndex].Char : def.Id.Substring(0, 1);

        // ============ Boss 技能 ============

        public static string BossSkillName(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => "淹没",
            BossSkill.Pierce => "贯穿",
            BossSkill.Topple => "倾覆",
            BossSkill.Devour => "吞噬",
            BossSkill.Bulwark => "坚壁",
            _ => "",
        };

        /// <summary>技能说明。承伤减免(DamageTaken)是与技能解耦的独立配置,不在这里插值——
        /// 由 <see cref="PhaseDetail"/> 在技能说明之后独立追加(见 Finding 1)。
        /// 不吃 phase 参数(Finding 3):五个分支全部只依赖 skill 本身,承伤行已由 PhaseDetail 独立追加。</summary>
        public static string BossSkillText(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => "对你造 攻×2,同时对每只召唤物造 攻×1(走五行)",
            BossSkill.Pierce => "穿透前排:最前一只召唤物造 攻×1,你造 攻×2",
            BossSkill.Topple => "对你造 攻×2,清空你全部护盾,下回合 AP −1",
            BossSkill.Devour => "吞掉最前一只召唤物(无视其血量);场上无召唤物时改为对你造 攻×1",
            // 「以守为攻」是旧措辞:坚壁可能落在 DamageTaken == 1.0 的位次(见 spec 5.1),
            // 那时没有任何减伤机制支撑这句话。改成只陈述事实,不暗示存在减伤(Finding 4)。
            BossSkill.Bulwark => "该阶段不放大招",
            _ => "",
        };

        /// <summary>蓄力节拍:阈值=2 时是「打 1 回合、蓄 1 回合、第 3 回合放大招」三回合一轮
        /// (见 <see cref="ChargeEvery"/> 的注释与 <c>BattleEngine.ResolveBossTurn</c>)。
        /// 第二句是定稿文案,不要改(Finding 1)。</summary>
        public static string ChargeRuleText() =>
            $"蓄力:Boss 打 {ChargeEvery - 1} 回合、蓄力 1 回合(不出手),第 {ChargeEvery + 1} 回合放当前预告的大招,如此循环。\n"
            + "大招无视召唤物,直接打到你身上(护盾仍能挡)。";

        // ============ 小怪能力 ============

        public static string AbilityName(EnemyAbility ability) => ability switch
        {
            EnemyAbility.Regrow => "缺笔",
            EnemyAbility.Split => "叠字",
            EnemyAbility.Buff => "标点",
            EnemyAbility.Disguise => "通假",
            EnemyAbility.Obscure => "生僻",
            EnemyAbility.Scorch => "自燃",
            EnemyAbility.Sear => "灼身",
            _ => "",
        };

        /// <summary>能力说明(不含能力名前缀,名字走 <see cref="AbilityName"/>)。
        /// 六条统一为「机制 + 战术提示」。</summary>
        public static string AbilityText(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Regrow => "每回合自补全:攻 +2、回 3 血;第 3 次补全后攻翻倍并回满 —— 拖不得",
            EnemyAbility.Split => "首次受击存活即分裂成两个半血(场上不足 4 只时) —— 一击打死免分裂",
            EnemyAbility.Buff => $"有同伴时每回合给其他怪攻 +{def.Attack}(整场累计不回滚);"
                + "落单则亲自出手 —— 优先清掉",
            EnemyAbility.Disguise => "显示的属性是假的,首次行动后才露真身 —— 别急着按显示的属性配克制",
            EnemyAbility.Obscure => "属性隐藏,受击两次后被「读懂」现形",
            EnemyAbility.Scorch => "每次受击存活,攻 +2 —— 越磨越烫,宜速杀",
            EnemyAbility.Sear => "每次攻击给你挂 1 层灼烧 —— 拖得越久烧得越久,备一张净化",
            _ => "",
        };

        /// <summary>战斗中的能力 chip 文案:带实时状态,机制已失效时返回空串(调用方据此不画)。
        /// 与 <see cref="AbilityName"/> 同一套命名 —— 玩家在详情学一次,战斗中看到 chip 就懂。</summary>
        public static string AbilityChipText(EnemyState enemy) => enemy.Def.Ability switch
        {
            EnemyAbility.Regrow => enemy.RegrowProgress >= 3
                ? "缺笔 已补全!" : $"缺笔 {enemy.RegrowProgress}/3",
            EnemyAbility.Split => enemy.HasSplit ? "" : "叠字", // 分裂过就没这威胁了
            EnemyAbility.Buff => "标点",
            // 通假:chip 只说「这属性不可信」,不泄真属性;现形(真伪一致)后撤掉
            EnemyAbility.Disguise => enemy.ApparentElement == enemy.Element ? "" : "通假",
            // 生僻:未读懂时 ApparentElement 为 null(属性显示「?」);被读懂后撤掉
            EnemyAbility.Obscure => enemy.ApparentElement != null ? "" : "生僻",
            EnemyAbility.Scorch => "自燃",
            EnemyAbility.Sear => "灼身",
            _ => "",
        };

        /// <summary>减伤特性行。与 Boss 坚壁走同一条规则,措辞刻意一致——
        /// 玩家学一次就能套用到所有减伤敌人。</summary>
        public static string DamageTakenText(float damageTaken) =>
            $"承伤 ×{damageTaken:0.##}:伤害打折 —— 但用克制它的属性打,减免完全失效";

        // ============ 形态详情 ============

        /// <summary>Boss 单阶段:第 N/M 阶段 + 属性血攻 + 技能名与说明。</summary>
        public static string PhaseDetail(EnemyDef def, int phaseIndex)
        {
            var phase = def.Phases[phaseIndex];
            var text = new StringBuilder();
            text.Append("第 ").Append(phaseIndex + 1).Append('/').Append(def.Phases.Count)
                .Append(" 阶段 · ").Append(CharInfo.ElementName(phase.Element)).Append("系\n");
            text.Append("血 ").Append(phase.MaxHp).Append(" · 攻 ").Append(phase.Attack).Append('\n');
            if (phase.Skill == BossSkill.None)
                text.Append("\n本阶段无大招,只有普攻");
            else
                text.Append('\n').Append('【').Append(BossSkillName(phase.Skill)).Append("】\n")
                    .Append(BossSkillText(phase.Skill));
            // 承伤减免与技能是两套独立配置(Finding 1):独立判断、独立追加,
            // 不能假定「有减伤 ⇔ 技能是坚壁」——两者在配置里完全解耦。
            if (phase.DamageTaken < 1f)
                text.Append('\n').Append(DamageTakenText(phase.DamageTaken));
            return text.ToString();
        }

        /// <summary>详情弹窗属性行的展示文本(Finding 2):通假字的真身/伪装每局重摇,
        /// 配置里的 <c>Element</c> 对它不作数(见 <c>EnemyDef.cs</c> 注释),直接打印
        /// 既非真身也非伪装,是纯错误信息;生僻字的 <c>Element</c> 就是真属性,直接打印
        /// 是泄底,与「?」显示和「生僻」chip 自相矛盾。两者都改为不含具体五行名的短标记,
        /// 详细机制交给下方的【通假】/【生僻】卡片,这里不重复展开。</summary>
        private static string ElementDisplayForDetail(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Disguise => "属性现摇",
            EnemyAbility.Obscure => "属性隐藏",
            _ => CharInfo.ElementName(def.Element) + "系",
        };

        /// <summary>小怪单形态:属性血攻 + 能力 / 减伤 / 无机制(三者互斥)。</summary>
        public static string MinionDetail(EnemyDef def)
        {
            var text = new StringBuilder();
            text.Append(ElementDisplayForDetail(def)).Append(" · 血 ")
                .Append(def.MaxHp).Append(" · 攻 ").Append(def.Attack).Append('\n');
            if (def.Ability != EnemyAbility.None)
                text.Append('\n').Append('【').Append(AbilityName(def.Ability)).Append("】\n")
                    .Append(AbilityText(def));
            else if (def.DamageTaken < 1f)
                text.Append('\n').Append(DamageTakenText(def.DamageTaken));
            else
                text.Append("\n无特殊机制 · 纯数值对拼");
            return text.ToString();
        }
    }
}
