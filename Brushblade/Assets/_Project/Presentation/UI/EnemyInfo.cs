using System.Collections.Generic;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

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
            EnemyAbility.Mend => Strings.T("enemy.ability.mend.name"),
            EnemyAbility.Barb => Strings.T("enemy.ability.barb.name"),
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
            EnemyAbility.Mend => Strings.T("enemy.ability.mend.desc"),
            EnemyAbility.Barb => Strings.T("enemy.ability.barb.desc"),
            _ => "",
        };

        /// <summary>战斗中的能力 chip:带实时状态,机制已失效时两项都为空(调用方据此不画)。
        ///
        /// **2026-09-02 用户拍板:战场上的状态只用「图标 + 数字」,不用文字描述,全量说明只在详情里。**
        /// 于是有图标的六种(叠字/生僻/自燃/灼身/反噬/涂改)改成只出图标、文案留空;
        /// 剩下三种(缺笔/标点/通假)眼下没有对应图标,**暂时保留文字**,等美术补齐再一起换 ——
        /// 所以这里出的是「文案 + 图标 key」两项而不是一项。缺笔那条还要带 2/3 进度数字。
        ///
        /// 生效条件(分裂过就不画、现形/读懂后撤掉)只写在这一处 —— 若把图标拆成第二个方法,
        /// 那几条判据就得抄一遍,改一处漏一处不会有任何东西报错。
        ///
        /// 与 <see cref="AbilityName"/> 同一套命名 —— 玩家在详情学一次,战斗中看到 chip 就懂。
        /// chip 与 name 各用独立 key(不复用):中文眼下相同,但 chip 是小标签有长度上限、
        /// 详情面板没有 —— 英文版 chip 必须缩写、name 不必。</summary>
        public static (string Text, string IconKey) AbilityChip(EnemyState enemy) => enemy.Def.Ability switch
        {
            EnemyAbility.Regrow => (enemy.RegrowProgress >= 3
                ? Strings.T("enemy.ability.regrow.chip_full")
                : Strings.T("enemy.ability.regrow.chip_progress", ("progress", enemy.RegrowProgress)), null),
            EnemyAbility.Split => enemy.HasSplit ? ("", null) : ("", "split"), // 分裂过就没这威胁了
            EnemyAbility.Buff => (Strings.T("enemy.ability.buff.chip"), null),
            // 通假:chip 只说「这属性不可信」,不泄真属性;现形(真伪一致)后撤掉
            EnemyAbility.Disguise => enemy.ApparentElement == enemy.Element
                ? ("", null) : (Strings.T("enemy.ability.disguise.chip"), null),
            // 生僻:未读懂时 ApparentElement 为 null(属性显示「?」);被读懂后撤掉
            EnemyAbility.Obscure => enemy.ApparentElement != null ? ("", null) : ("", "obscure"),
            EnemyAbility.Scorch => ("", "scorch"),
            EnemyAbility.Sear => ("", "sear"),
            // 涂改的 chip 常驻:治疗是每回合都会发生的事,没有「用掉就没了」的状态
            EnemyAbility.Mend => ("", "heal"),
            EnemyAbility.Barb => ("", "thorns"),
            _ => ("", null),
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

        // ============ 结构化详情(2026-08-31,单位详情轮二 Task 3) ============

        /// <summary>敌人详情弹窗(稿 UnitFoe.dc.html)。**追加**方法,以上整段文本 API 一个不动——
        /// EnemyPreview 与图鉴还在用它们。数值口径与那些方法完全一致(同一份 Def/State 字段,
        /// 只是拆成条目而不是拼成一段字符串);Figures/Tags/Flavor 这几个稿上有、老文本从来
        /// 没有的字段,来源与外推理由见 task-3-report.md 的对照表。</summary>
        public static UnitDetail Sheet(EnemyState enemy)
        {
            var def = enemy.Def;
            bool isBoss = enemy.IsBoss;
            return new UnitDetail
            {
                PortraitPrefix = MobAssets.PrefixFor(def, enemy.PhaseIndex), // 没形象时它自己返回 null
                FaceChar = FaceChar(def, enemy.PhaseIndex),
                // 通假/生僻的「对玩家隐藏真属性」就是靠 ApparentElement 这个字段本身表达的
                // (真身/伪装/未读懂三种状态,见 EnemyState.ApparentElement 的类型注释)——
                // 这里直接用它而不是 enemy.Element,否则详情弹窗会把老文本刻意藏起来的真身泄出去。
                Element = enemy.ApparentElement,
                // ApparentElement == null 只可能是生僻字没读懂(受击不到两次),不可能是别的
                // 情形——EnemyInfo.Sheet 只服务敌人,这里恒不会遇到「执笔人没有五行」那一支。
                ElementUnknown = enemy.ApparentElement == null,
                Name = isBoss ? BossTitleText(def, enemy.PhaseIndex) : def.Id,
                Tags = BuildTags(enemy, isBoss),
                Flavor = null, // enemies.json 没有风味文案字段,19 只怪的这句话没法凭空写
                Hp = enemy.Hp,
                MaxHp = enemy.MaxHp,
                Shield = enemy.Shield,
                ActionMeter = enemy.ActionMeter,
                Figures = BuildFigures(enemy),
                Statuses = UnitDetailChip.BuildStatuses(enemy.Statuses),
                Abilities = BuildAbilities(enemy, isBoss),
                Wuxing = UnitDetailChip.WuxingOf(enemy.ApparentElement),
            };
        }

        /// <summary>成语 Boss 的详情标题:「排【山】倒海」,当前阶段字加框。
        /// 与 BattleView.BossTitle 同一条规则(那边是 private,战场小名牌用;这里独立一份给
        /// 详情大标题用)——两处都在展示「这只 Boss 现在是第几阶段」,没有别的既有惯例可循。</summary>
        private static string BossTitleText(EnemyDef def, int phaseIndex)
        {
            var title = new StringBuilder();
            for (int i = 0; i < def.Phases.Count; i++)
                title.Append(i == phaseIndex ? "【" + def.Phases[i].Char + "】" : def.Phases[i].Char);
            return title.ToString();
        }

        /// <summary>Tags:小怪一枚「前排 · 近战」;Boss 再加一枚「第 N/M 阶段」在前面
        /// (老文本 PhaseDetail 的 enemy.phase.header,旧文本里是阶段信息,新结构挪进 Tags)。
        /// 稿上焦痕那条标签还有第二枚金色「文山 ×3.0」(层段深度缩放倍率)——那个倍率在
        /// Endless.cs 里现算现丢,不进 EnemyDef/EnemyState,Sheet(EnemyState) 这个签名拿不到,
        /// 故没有第二个 Tag(报告里点名跳过,不是漏掉)。</summary>
        private static string[] BuildTags(EnemyState enemy, bool isBoss)
        {
            var position = PositionRangeTag(enemy);
            if (!isBoss) return new[] { position };
            string phaseTag = Strings.T("enemy.phase.tag",
                ("phaseNumber", enemy.PhaseIndex + 1), ("phaseCount", enemy.Def.Phases.Count));
            return new[] { phaseTag, position };
        }

        /// <summary>「前排 · 近战」标签。⚠ 站位读 <c>enemy.Row</c>,不是 <c>enemy.Def.Row</c>——
        /// Def.Row 只是「偏好」,实际站位在开场分配(BattleEngine.AssignSlots)时可能因为
        /// 偏好排满了被改判到另一排,之后 Targeting/前排拦截/Endless.cs 全部读的是
        /// EnemyState.Row。这个标签存在的全部意义就是告诉玩家「它现在站哪」,读错字段会让
        /// 一只实际在后排的怪显示成「前排」,判定与展示对不上(2026-09-01 review 抓到过一次,
        /// 当时读的是 def.Row,已改)。</summary>
        private static string PositionRangeTag(EnemyState enemy)
        {
            // 两支各写各的 Strings.T(字面量 key):StringsTableTests 扫的是紧跟在 T( 后面的
            // 字符串字面量,key 从三元表达式的结果变量传进去它认不出来,会被判孤儿
            // (StatusText.cs 的注释早就点过这个坑,这里再踩一次没必要)。
            string rowName = enemy.Row == EnemyRow.Front
                ? Strings.T("enemy.row.front.name")
                : Strings.T("enemy.row.back.name");
            return rowName + " · " + RangeName(enemy.Def.Range);
        }

        /// <summary>攻/甲/速/行动四格,口径见 task-3-report.md 的逐条对照表:
        /// 前三格是「基准值 + 实时状态偏离」的展开(老文本从不展示这层偏离,只印一个静态数);
        /// 第四格(行动)整个是新内容,老文本从未提过冻结/蓄力对行动条的影响。</summary>
        private static (string, string, string)[] BuildFigures(EnemyState enemy)
        {
            int armorBreak = enemy.Statuses.TotalMagnitude(StatusKind.ArmorBreak);
            int defenseValue = System.Math.Max(0, enemy.Defense - armorBreak);
            int speedMod = enemy.Statuses.TotalMagnitude(StatusKind.SpeedModifier);
            int speedValue = TurnScheduler.ClampSpeed(enemy.Speed + speedMod);

            string attackNote = UnitDetailChip.BaseNote(enemy.BaseAttack,
                UnitDetailChip.DeltaBuffPct(Strings.T("status.attack.name"),
                    enemy.Statuses.TotalMagnitude(StatusKind.AttackBuff)),
                UnitDetailChip.DeltaDebuffPct(Strings.T("status.curse.name"),
                    enemy.Statuses.TotalMagnitude(StatusKind.Curse)));
            // ⚠ 这一格只含破甲(敌人自己身上的减益),不含攻击方的穿透——不是签名拿不到,
            // 是拍板的设计分工(2026-09-01 review 追加裁定):UnitMe.dc.html 的穿透词条自己写着
            // 「实际减多少看那只怪的甲」,即穿透是执笔人那一屏的属性,这一屏只显示敌人自身的甲。
            // 两者混进同一个数会让「这只怪的甲」这个概念含糊掉。
            string defenseNote = UnitDetailChip.BaseNote(enemy.Defense,
                UnitDetailChip.DeltaDebuffPts(Strings.T("status.armorbreak.name"), armorBreak));
            string speedNote = UnitDetailChip.BaseNote(enemy.Speed,
                speedMod < 0
                    ? UnitDetailChip.DeltaDebuffPts(Strings.T("status.slow.name"), -speedMod)
                    : UnitDetailChip.DeltaBuffPts(Strings.T("status.speed.name"), speedMod));

            string actionValue = "";
            string actionNote;
            if (enemy.Statuses.Has(StatusKind.Freeze))
                actionNote = Strings.T("detail.figure.action_frozen");
            else if (enemy.IsCharging)
                actionNote = Strings.T("detail.figure.action_charging");
            else
            {
                actionValue = Strings.T("detail.chip.plain_pct", ("value", enemy.ActionMeter));
                actionNote = null;
            }

            return new[]
            {
                (Strings.T("char.stat.attack"), enemy.Attack.ToString(), attackNote),
                (Strings.T("char.stat.defense"), defenseValue.ToString(), defenseNote),
                (Strings.T("char.stat.speed"), speedValue.ToString(), speedNote),
                (Strings.T("char.stat.action"), actionValue, actionNote),
            };
        }

        /// <summary>「特性 · 技能」列。小怪与 Boss 分支和老文本的 PhaseDetail/MinionDetail
        /// 逐支对应(见 task-3-report.md):同一份 if/else 结构,只是每一支从「往 StringBuilder
        /// 里追加一段话」改成「往 List 里加一张卡」。</summary>
        private static List<AbilityEntry> BuildAbilities(EnemyState enemy, bool isBoss)
        {
            var def = enemy.Def;
            var list = new List<AbilityEntry>();

            var range = StatusText.OfRange(def.Range); // 老文本无条件追加 RangeText,这里同理
            list.Add(new AbilityEntry
            {
                IconKey = range.IconKey, ChipColor = UnitDetailChip.Positioning,
                Name = range.Name, Desc = range.Desc,
            });
            var focus = StatusText.OfFocus(def.Focus); // Default 返回 None,新信息,老文本从未提过
            if (focus.Name != null)
                list.Add(new AbilityEntry
                {
                    IconKey = focus.IconKey, ChipColor = UnitDetailChip.Positioning,
                    Name = focus.Name, Desc = focus.Desc,
                });

            if (isBoss)
            {
                // 四个阶段**全列**(2026-09-01 用户拍板)。此前这里只画当前阶段那一张技能卡,
                // 于是详情弹窗答不了 Boss 战里最要紧的那个问题:「下一段是什么属性、多少血、
                // 什么大招」——玩家要按它决定现在留哪张克制的字、要不要先攒盾。四字成语的
                // 四个阶段在 EnemyDef 里是完全公开的静态配置,没有任何理由藏着。
                //
                // 血/攻取 BossPhaseDef 的配置值(该阶段的**基准**),不是实时值:实时只有
                // 当前阶段有意义(Boss 的血是一条连续总池,阶段只是分段阈值),而这张列表
                // 的用处正是「未至的阶段长什么样」。当前阶段的实时数值在上面四格里。
                string section = Strings.T("enemy.phase.section", ("phaseCount", def.Phases.Count));
                for (int i = 0; i < def.Phases.Count; i++)
                {
                    var phase = def.Phases[i];
                    bool current = i == enemy.PhaseIndex;
                    // 两支各写各的 Strings.T(字面量 key):StringsTableTests 只认紧跟在 T( 后面的
                    // 字符串字面量,key 从三元表达式传进去会被判成孤儿(PositionRangeTag 那里
                    // 有同一条注释)。
                    string name = current
                        ? Strings.T("enemy.phase.card_name_current", ("phaseNumber", i + 1),
                            ("char", phase.Char), ("element", CharInfo.ElementName(phase.Element)))
                        : Strings.T("enemy.phase.card_name", ("phaseNumber", i + 1),
                            ("char", phase.Char), ("element", CharInfo.ElementName(phase.Element)));
                    // 护甲与技能是两套独立配置(老文本 PhaseDetail 的 Finding 1),不能假定
                    // 「有护甲 ⇔ 技能是坚壁」—— 所以是两个独立的 key 而不是拼一句。
                    string stats = phase.Defense > 0
                        ? Strings.T("enemy.phase.card_stats_armor", ("hp", phase.MaxHp),
                            ("attack", phase.Attack), ("defense", phase.Defense))
                        : Strings.T("enemy.phase.card_stats", ("hp", phase.MaxHp), ("attack", phase.Attack));
                    string skillLine = phase.Skill == BossSkill.None
                        ? Strings.T("enemy.phase.no_ultimate").TrimStart('\n')
                        : "【" + BossSkillName(phase.Skill) + "】" + BossSkillText(phase.Skill);
                    list.Add(new AbilityEntry
                    {
                        IconKey = null,
                        ChipColor = Theme.BossSkillChipColor(phase.Skill),
                        Name = name,
                        Desc = stats + "\n" + skillLine,
                        Section = section,
                    });
                }
            }
            else
            {
                // 老文本(MinionDetail)里能力/护甲/无机制三支互斥,这里保持同一互斥关系——
                // 注意 Boss 的 EnemyDef.Ability 老文本从未读过(它只看 phase.Skill),
                // 所以这一支只在非 Boss 分支里判断,与老文本的疏漏保持一致(口径不变)。
                if (def.Ability != EnemyAbility.None)
                {
                    var info = StatusText.OfAbility(def.Ability);
                    list.Add(new AbilityEntry
                    {
                        IconKey = info.IconKey, ChipColor = Theme.AbilityChipColor(def.Ability),
                        Name = info.Name, Desc = info.Desc,
                    });
                }
                else if (def.Defense > 0)
                    list.Add(DefenseEntry(def.Defense));
                else
                    list.Add(new AbilityEntry
                    {
                        IconKey = null, ChipColor = UnitDetailChip.Ability,
                        Name = Strings.T("enemy.minion.no_mechanic").TrimStart('\n'), Desc = null,
                    });
            }
            return list;
        }

        /// <summary>护甲卡:复用 status.defense.name 当标题(与「护甲增益」是同一个词,含义在
        /// 这里的语境下不会混淆——这条讲的是敌人天生带的甲,不是谁给它挂的增益),
        /// Desc 是老文本 DefenseText 的原句原参数,数值口径与老文本一字不差。</summary>
        private static AbilityEntry DefenseEntry(int defense) => new()
        {
            IconKey = "defense", ChipColor = UnitDetailChip.ColorFor(StatusKind.DefenseBuff, defense),
            Name = Strings.T("status.defense.name"), Desc = Strings.T("enemy.defense.desc", ("defense", defense)),
        };
    }
}
