using System.Text;
using Brushblade.Core;
using Brushblade.Data;

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
            BossSkill.Deluge => Strings.T("enemy.skill.deluge.name"),
            BossSkill.Impale => Strings.T("enemy.skill.impale.name"),
            BossSkill.Topple => Strings.T("enemy.skill.topple.name"),
            BossSkill.Devour => Strings.T("enemy.skill.devour.name"),
            BossSkill.Bulwark => Strings.T("enemy.skill.bulwark.name"),
            _ => "",
        };

        /// <summary>技能说明。护甲(Defense)是与技能解耦的独立配置,不在这里插值——
        /// 由 <see cref="PhaseDetail"/> 在技能说明之后独立追加(见 Finding 1)。
        /// 不吃 phase 参数(Finding 3):五个分支全部只依赖 skill 本身,护甲行已由 PhaseDetail 独立追加。</summary>
        public static string BossSkillText(BossSkill skill) => skill switch
        {
            BossSkill.Deluge => Strings.T("enemy.skill.deluge.desc"),
            BossSkill.Impale => Strings.T("enemy.skill.impale.desc"),
            BossSkill.Topple => Strings.T("enemy.skill.topple.desc"),
            BossSkill.Devour => Strings.T("enemy.skill.devour.desc"),
            // 「以守为攻」是旧措辞:坚壁可能落在 Defense == 0 的位次(见 spec 5.1),
            // 那时没有任何防御机制支撑这句话。改成只陈述事实,不暗示存在减伤(Finding 4)。
            BossSkill.Bulwark => Strings.T("enemy.skill.bulwark.desc"),
            _ => "",
        };

        /// <summary>蓄力节拍:阈值=2 时是「打 1 回合、蓄 1 回合、第 3 回合放大招」三回合一轮
        /// (见 <see cref="ChargeEvery"/> 的注释与 <c>BattleEngine.ResolveBossTurn</c>)。
        /// 第二句是定稿文案,不要改(Finding 1)。</summary>
        public static string ChargeRuleText() =>
            Strings.T("enemy.charge.rule", ("restTurns", ChargeEvery - 1), ("releaseTurn", ChargeEvery + 1));

        // ============ 小怪能力 ============

        public static string AbilityName(EnemyAbility ability) => ability switch
        {
            EnemyAbility.Regrow => Strings.T("enemy.ability.regrow.name"),
            EnemyAbility.Split => Strings.T("enemy.ability.split.name"),
            EnemyAbility.Buff => Strings.T("enemy.ability.buff.name"),
            EnemyAbility.Disguise => Strings.T("enemy.ability.disguise.name"),
            EnemyAbility.Obscure => Strings.T("enemy.ability.obscure.name"),
            EnemyAbility.Scorch => Strings.T("enemy.ability.scorch.name"),
            EnemyAbility.Sear => Strings.T("enemy.ability.sear.name"),
            _ => "",
        };

        /// <summary>能力说明(不含能力名前缀,名字走 <see cref="AbilityName"/>)。
        /// 六条统一为「机制 + 战术提示」。</summary>
        public static string AbilityText(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Regrow => Strings.T("enemy.ability.regrow.desc"),
            EnemyAbility.Split => Strings.T("enemy.ability.split.desc"),
            EnemyAbility.Buff => Strings.T("enemy.ability.buff.desc"),
            EnemyAbility.Disguise => Strings.T("enemy.ability.disguise.desc"),
            EnemyAbility.Obscure => Strings.T("enemy.ability.obscure.desc"),
            EnemyAbility.Scorch => Strings.T("enemy.ability.scorch.desc"),
            EnemyAbility.Sear => Strings.T("enemy.ability.sear.desc"),
            _ => "",
        };

        /// <summary>战斗中的能力 chip 文案:带实时状态,机制已失效时返回空串(调用方据此不画)。
        /// 与 <see cref="AbilityName"/> 同一套命名 —— 玩家在详情学一次,战斗中看到 chip 就懂。
        /// chip 与 name 各用独立 key(不复用):中文眼下相同,但 chip 是小标签有长度上限、
        /// 详情面板没有 —— 英文版 chip 必须缩写、name 不必。</summary>
        public static string AbilityChipText(EnemyState enemy) => enemy.Def.Ability switch
        {
            EnemyAbility.Regrow => enemy.RegrowProgress >= 3
                ? Strings.T("enemy.ability.regrow.chip_full")
                : Strings.T("enemy.ability.regrow.chip_progress", ("progress", enemy.RegrowProgress)),
            EnemyAbility.Split => enemy.HasSplit ? "" : Strings.T("enemy.ability.split.chip"), // 分裂过就没这威胁了
            EnemyAbility.Buff => Strings.T("enemy.ability.buff.chip"),
            // 通假:chip 只说「这属性不可信」,不泄真属性;现形(真伪一致)后撤掉
            EnemyAbility.Disguise => enemy.ApparentElement == enemy.Element ? "" : Strings.T("enemy.ability.disguise.chip"),
            // 生僻:未读懂时 ApparentElement 为 null(属性显示「?」);被读懂后撤掉
            EnemyAbility.Obscure => enemy.ApparentElement != null ? "" : Strings.T("enemy.ability.obscure.chip"),
            EnemyAbility.Scorch => Strings.T("enemy.ability.scorch.chip"),
            EnemyAbility.Sear => Strings.T("enemy.ability.sear.chip"),
            _ => "",
        };

        /// <summary>护甲特性行(2026-08-12,E-b4 T3:口径从承伤系数换成点数)。与 Boss 坚壁走
        /// 同一条规则,措辞刻意一致 —— 玩家学一次就能套用到所有带甲敌人。
        ///
        /// 不再提「用克制它的属性打减免完全失效」:那条补丁随乘法层一起没了,而且**没必要** ——
        /// 减法对乘法透明,克制的加成原封不动落到血条,与无甲时完全相同。
        /// 反过来要告诉玩家的是护甲怎么削:破甲(本场)与穿透(本次)。</summary>
        public static string DefenseText(int defense) =>
            Strings.T("enemy.defense.desc", ("defense", defense));

        // ============ 形态详情 ============

        /// <summary>Boss 单阶段:第 N/M 阶段 + 属性血攻 + 技能名与说明。</summary>
        public static string PhaseDetail(EnemyDef def, int phaseIndex)
        {
            var phase = def.Phases[phaseIndex];
            var text = new StringBuilder();
            text.Append(Strings.T("enemy.phase.header", ("phaseNumber", phaseIndex + 1), ("phaseCount", def.Phases.Count)))
                .Append(CharInfo.ElementName(phase.Element)).Append("系\n");
            // 攻击类型配在 EnemyDef 上、不分阶段(Boss 换阶段换的是属性/血/攻/技能,不换站位与射程)
            text.Append(Strings.T("enemy.phase.stats", ("hp", phase.MaxHp), ("attack", phase.Attack)))
                .Append(RangeName(def.Range)).Append('\n');
            text.Append('\n').Append(RangeText(def.Range)).Append('\n');
            if (phase.Skill == BossSkill.None)
                text.Append(Strings.T("enemy.phase.no_ultimate"));
            else
                text.Append('\n').Append('【').Append(BossSkillName(phase.Skill)).Append("】\n")
                    .Append(BossSkillText(phase.Skill));
            // 护甲与技能是两套独立配置(Finding 1):独立判断、独立追加,
            // 不能假定「有护甲 ⇔ 技能是坚壁」——两者在配置里完全解耦。
            if (phase.Defense > 0)
                text.Append('\n').Append(DefenseText(phase.Defense));
            return text.ToString();
        }

        /// <summary>详情弹窗属性行的展示文本(Finding 2):通假字的真身/伪装每局重摇,
        /// 配置里的 <c>Element</c> 对它不作数(见 <c>EnemyDef.cs</c> 注释),直接打印
        /// 既非真身也非伪装,是纯错误信息;生僻字的 <c>Element</c> 就是真属性,直接打印
        /// 是泄底,与「?」显示和「生僻」chip 自相矛盾。两者都改为不含具体五行名的短标记,
        /// 详细机制交给下方的【通假】/【生僻】卡片,这里不重复展开。</summary>
        private static string ElementDisplayForDetail(EnemyDef def) => def.Ability switch
        {
            EnemyAbility.Disguise => Strings.T("enemy.element_display.disguise"),
            EnemyAbility.Obscure => Strings.T("enemy.element_display.obscure"),
            _ => CharInfo.ElementName(def.Element) + "系",
        };

        /// <summary>攻击类型的短标(2026-08-21):跟在血攻后面,一眼看出前排挡不挡得住它。</summary>
        public static string RangeName(AttackRange range) => range == AttackRange.Ranged
            ? Strings.T("enemy.range.ranged.name") : Strings.T("enemy.range.melee.name");

        /// <summary>攻击类型的说明行。这是玩家排兵布阵时最需要的一条:决定「把召唤物摆在前排
        /// 到底挡不挡得住这只怪」。近战会被前排拦下,远程根本不看前排。</summary>
        public static string RangeText(AttackRange range) => range == AttackRange.Ranged
            ? Strings.T("enemy.range.ranged.desc")
            : Strings.T("enemy.range.melee.desc");

        /// <summary>小怪单形态:属性血攻 + 攻击类型 + 能力 / 护甲 / 无机制(后三者互斥)。</summary>
        public static string MinionDetail(EnemyDef def)
        {
            var text = new StringBuilder();
            text.Append(ElementDisplayForDetail(def))
                .Append(Strings.T("enemy.minion.stats", ("hp", def.MaxHp), ("attack", def.Attack)))
                .Append(RangeName(def.Range)).Append('\n');
            text.Append('\n').Append(RangeText(def.Range)).Append('\n');
            if (def.Ability != EnemyAbility.None)
                text.Append('\n').Append('【').Append(AbilityName(def.Ability)).Append("】\n")
                    .Append(AbilityText(def));
            else if (def.Defense > 0)
                text.Append('\n').Append(DefenseText(def.Defense));
            else
                text.Append(Strings.T("enemy.minion.no_mechanic"));
            return text.ToString();
        }
    }
}
