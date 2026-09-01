using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>单位详情弹窗的状态/能力/攻击模式取词表(2026-08-31,单位详情轮二 Task 2)。
    /// 纯查表,不碰战斗状态本身 —— 调用方(Task 3)从 StatusBag / EnemyDef / SummonPassive
    /// 读出 (kind, magnitude, turnsLeft) 或枚举值,这里只管把它翻成人话。
    ///
    /// ⚠ 字符串表账目(与最初「稿上 32 条」的落差,记在这里免得下一个人对不上账):
    /// - **新写** `status.&lt;kind&gt;.*`:StatusGlossary.dc.html 是这些的唯一出处,
    ///   覆盖 StatusKind 除 ObsoleteDamageReduction 外的全部真实值(20 个;
    ///   SpeedModifier 一个枚举拆成 slow/speed 两条文案,因为符号不同、图标也不同)。
    ///   ApBoost 在内 —— 见下面「AP 上限」那条,不是「不写」。
    /// - **复用既有** `enemy.ability.*` / `enemy.range.*`:这两族已经完整覆盖
    ///   EnemyAbility(含稿子没有的 mend/barb —— 2026-08-29 才加,比这份稿新)和
    ///   AttackRange,形状也正好是详情弹窗要的 name/desc。没有另建 `ability.*` 平行家族——
    ///   建第二套是同一内容两份、必然漂;这两族还被图鉴/EnemyPreview 用着,不能碰。
    ///   代价:详情里这两类的措辞不会与 glossary 逐字一致,但内容口径相同。
    /// - **复用既有 `char.shape.&lt;x&gt;`当 name**,新写 `char.shape.&lt;x&gt;.desc`:
    ///   TargetShape 六个值稿子只画了 Sweep/Skewer 两个,Cleave/Volley/Chain 从
    ///   TargetShape.cs 自己的注释外推(代码是它们的真相源,算不上发明)。Sweep/Cleave/
    ///   Skewer 三条 desc 统一用「非主目标」而不是「溅射」/「后续目标」这两个词 ——
    ///   「溅射」是 Cleave 的正式显示名,塞进别的形状会让人以为在说另一个形状;
    ///   「后续目标」暗示时序先后,而这三个形状命中的非主目标之间并没有先后关系,
    ///   三者共用同一条结算规则(BattleEngine.ApplyEffects:主目标全额、非主目标一次
    ///   ShapePercent;弹射是唯一逐跳累乘的例外,不受这句话约束)。Single(缺省值,只打
    ///   一个)不出条目,规则与 `Focus.Default` 相同,理由见 <see cref="OfShape"/> 的注释;
    ///   `char.shape.single.desc` 因此没有新写(`char.shape.single` 这个裸 name key
    ///   是既有的,CharInfo.cs 的卡面文案还在用,不受影响,没有删)。
    /// - **全新 `enemy.focus.*`**:AttackFocus 没有既有文案,只能新写;命名跟
    ///   `enemy.range.*` 同一个「enemy.」家族。只有 `Player`(死盯玩家)写了 key ——
    ///   `Default` 是均匀随机的默认行为,没有特性可说,不出条目(<see cref="OfFocus"/>
    ///   对它返回 <see cref="None"/>),这样也不需要给它现拟一个容易漂的中文名。
    /// - **不写**:护盾(稿明写「不是状态,是资源」,不占状态位,由 `.bars` 那段的血条/盾条
    ///   自己表达,不需要单独的状态说明文案)。
    /// - **AP 上限**(`StatusKind.ApBoost`)有文字无图标:稿子「刻意不出 chip」说的是
    ///   战场格子上的 chip 行(底栏 AP 格子多一格已是反馈),不是详情弹窗的状态列表——
    ///   详情弹窗的全部意义就是把身上的状态逐条列出并附一句说明,一个正在生效的增益
    ///   在这里看不到,玩家就没法弄清「AP 为什么变了」。<see cref="Of"/> 对它返回
    ///   `IconKey = null` 但 Name/Duration/Desc 完整,与缺笔/标点/通假三条同一处理。
    /// - **不写**:组六末三条(反伤 N / 召唤物被动闪避 N% / 疾)读的是
    ///   `SummonPassive.Thorns/.Dodge/.Speed` 三个裸 int 字段,不是 EnemyAbility 枚举,
    ///   装不进 `OfAbility(EnemyAbility)` 这个签名;`SummonInfo.cs` 的
    ///   `summon.passive.thorns/dodge/haste` 已经是同一内容的现成参数化文案,
    ///   Task 3 到时直接读那三个字段配现成 key,本类不重复开路。</summary>
    public static class StatusText
    {
        /// <summary>一条状态/能力/攻击模式的展示四元组。IconKey 为 null 表示这条没有图标
        /// (稿上「四条不出图标」的那几条,以及没有对应 icon 资产的 TargetShape 值);
        /// 四个字段全 null 表示这条压根不该出现在详情列表里(护盾式的「跳过」信号)。</summary>
        public readonly struct Info
        {
            public readonly string IconKey;
            public readonly string Name;
            public readonly string Duration;
            public readonly string Desc;

            public Info(string iconKey, string name, string duration, string desc)
            {
                IconKey = iconKey;
                Name = name;
                Duration = duration;
                Desc = desc;
            }
        }

        private static readonly Info None = new(null, null, null, null);

        /// <summary>状态类。Magnitude/TurnsLeft 的解读口径与 <see cref="StatusEffect"/> 上的
        /// 注释一致 —— 这里只是把同一份数字套进人话模板,不重新定义语义。
        ///
        /// 时长措辞统一成三种(稿子里明写的三个口径,别混用):
        /// 按回合递减 → status.duration.turns(「剩 N 回合」);
        /// 按层数/次数消耗 → status.duration.stacks 或 .charges;
        /// TurnsLeft = -1 的本场持久 → status.duration.persistent(固定文案,不带数字)。
        /// Seal 是回合口径里的特例(只对下一回合生效,不是「倒数 N 回合」),单独一条措辞。
        ///
        /// ⚠ <paramref name="isPlayer"/>(2026-09-01 review 修):AttackBuff 的 desc 敌我口径
        /// 不同 —— EnemyState.Attack 拿它当基础攻击的百分比乘,BattleEngine.EffectiveAttack /
        /// SummonState.EffectiveAttack 都是拿它当点数直接加(同一个 StatusKind,单位不同)。
        /// 一条 status.attack.desc 不可能同时对两种口径说对话,拆成 .desc / .desc.player 两条,
        /// 调用方按自己是谁传这个参数选(目前只有 PlayerInfo 传 true;召唤物身上出现 AttackBuff
        /// 时——如剡挂在召唤物身上——仍走敌人那条百分比措辞,是本轮审查明确限定的范围,
        /// 没有跟着改,见 final-fix-report.md D 条)。</summary>
        public static Info Of(StatusKind kind, int magnitude, int turnsLeft, bool isPlayer = false)
        {
            switch (kind)
            {
                case StatusKind.Burn:
                    return new Info("burn", Strings.T("status.burn.name"),
                        Strings.T("status.duration.stacks", ("value", magnitude)),
                        Strings.T("status.burn.desc", ("magnitude", magnitude)));
                case StatusKind.BurnNoDecay:
                    return new Info("burn_nodecay", Strings.T("status.burn_nodecay.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.burn_nodecay.desc"));
                case StatusKind.Bleed:
                    return new Info("bleed", Strings.T("status.bleed.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.bleed.desc", ("magnitude", magnitude)));
                case StatusKind.Freeze:
                    return new Info("freeze", Strings.T("status.freeze.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.freeze.desc"));
                case StatusKind.SpeedModifier:
                    // 减速/速度共用一个枚举,符号决定是哪一条(施加处:减速传负值,见
                    // BattleEngine 的 Magnitude = -50 那一支)——图标、名字、文案都跟着符号走。
                    return magnitude < 0
                        ? new Info("slow", Strings.T("status.slow.name"),
                            Strings.T("status.duration.turns", ("value", turnsLeft)),
                            Strings.T("status.slow.desc", ("magnitude", -magnitude)))
                        : new Info("speed", Strings.T("status.speed.name"),
                            Strings.T("status.duration.turns", ("value", turnsLeft)),
                            Strings.T("status.speed.desc", ("magnitude", magnitude)));
                case StatusKind.Blind:
                    return new Info("blind", Strings.T("status.blind.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.blind.desc", ("magnitude", magnitude)));
                case StatusKind.Silence:
                    return new Info("silence", Strings.T("status.silence.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.silence.desc"));
                case StatusKind.Curse:
                    return new Info("curse", Strings.T("status.curse.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.curse.desc", ("magnitude", magnitude)));
                case StatusKind.ArmorBreak:
                    return new Info("armorbreak", Strings.T("status.armorbreak.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.armorbreak.desc", ("magnitude", magnitude)));
                case StatusKind.Seal:
                    return new Info("seal", Strings.T("status.seal.name"),
                        Strings.T("status.duration.next_turn"),
                        Strings.T("status.seal.desc", ("magnitude", magnitude)));
                case StatusKind.DefenseBuff:
                    return new Info("defense", Strings.T("status.defense.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.defense.desc", ("magnitude", magnitude)));
                case StatusKind.Immunity:
                    return new Info("immunity", Strings.T("status.immunity.name"),
                        Strings.T("status.duration.charges", ("value", magnitude)),
                        Strings.T("status.immunity.desc", ("magnitude", magnitude)));
                case StatusKind.Reflect:
                    return new Info("reflect", Strings.T("status.reflect.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.reflect.desc", ("magnitude", magnitude)));
                case StatusKind.DodgeBuff:
                    return new Info("dodge", Strings.T("status.dodge.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.dodge.desc", ("magnitude", magnitude)));
                case StatusKind.HealOverTime:
                    return new Info("heal", Strings.T("status.heal.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        Strings.T("status.heal.desc", ("magnitude", magnitude)));
                case StatusKind.AttackBuff:
                    return new Info("attack", Strings.T("status.attack.name"),
                        Strings.T("status.duration.turns", ("value", turnsLeft)),
                        isPlayer
                            ? Strings.T("status.attack.desc.player", ("magnitude", magnitude))
                            : Strings.T("status.attack.desc", ("magnitude", magnitude)));
                case StatusKind.Morale:
                    return new Info("morale", Strings.T("status.morale.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.morale.desc", ("magnitude", magnitude)));
                case StatusKind.CritBuff:
                    return new Info("crit", Strings.T("status.crit.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.crit.desc", ("magnitude", magnitude)));
                case StatusKind.PierceBuff:
                    return new Info("pierce", Strings.T("status.pierce.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.pierce.desc", ("magnitude", magnitude)));
                case StatusKind.ApBoost:
                    // 稿明写「刻意不出 chip」说的是战场格子上的 chip 行(战斗屏,底栏 AP 格子
                    // 多一格已是反馈);但详情弹窗的全部意义就是「身上的状态逐条列出并附一句
                    // 说明」——玩家想弄清 AP 为什么变了,这里得查得到。IconKey 仍是 null
                    // (与缺笔/标点/通假三条同一处理:没有图标,不代表这一行不存在)。
                    return new Info(null, Strings.T("status.apboost.name"),
                        Strings.T("status.duration.persistent"),
                        Strings.T("status.apboost.desc", ("magnitude", magnitude)));
                default:
                    // ObsoleteDamageReduction 等占位值:按约定不得再被构造进真实 StatusBag,
                    // 这里兜底返回 None 而不是抛异常,免得万一读到旧存档脏数据时详情弹窗整屏崩掉。
                    return None;
            }
        }

        /// <summary>敌人天生能力。文案取自既有 `enemy.ability.*`(EnemyInfo.cs 的图鉴/战斗面板
        /// 同一套,单一真相源),本类不重新写一份。缺笔/标点/通假/涂改/反噬这五条在图标表里
        /// 都没有对应 icon(缺笔/标点/通假是稿子明写的「走文字 chip」;涂改/反噬是稿子还没收的
        /// 新能力),IconKey 统一给 null,由 Task 3 走文字 chip 分支渲染。</summary>
        public static Info OfAbility(EnemyAbility ability)
        {
            switch (ability)
            {
                case EnemyAbility.Scorch:
                    return new Info("scorch", Strings.T("enemy.ability.scorch.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.scorch.desc"));
                case EnemyAbility.Sear:
                    return new Info("sear", Strings.T("enemy.ability.sear.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.sear.desc"));
                case EnemyAbility.Split:
                    return new Info("split", Strings.T("enemy.ability.split.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.split.desc"));
                case EnemyAbility.Obscure:
                    // 唯一时长措辞不是「清不掉」而是「至现形」——受击两次后这条能力本身就终止了。
                    return new Info("obscure", Strings.T("enemy.ability.obscure.name"),
                        Strings.T("status.duration.until_revealed"), Strings.T("enemy.ability.obscure.desc"));
                case EnemyAbility.Regrow:
                    return new Info(null, Strings.T("enemy.ability.regrow.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.regrow.desc"));
                case EnemyAbility.Buff:
                    return new Info(null, Strings.T("enemy.ability.buff.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.buff.desc"));
                case EnemyAbility.Disguise:
                    return new Info(null, Strings.T("enemy.ability.disguise.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.disguise.desc"));
                case EnemyAbility.Mend:
                    return new Info(null, Strings.T("enemy.ability.mend.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.mend.desc"));
                case EnemyAbility.Barb:
                    return new Info(null, Strings.T("enemy.ability.barb.name"),
                        Strings.T("status.duration.ability"), Strings.T("enemy.ability.barb.desc"));
                default: // None
                    return None;
            }
        }

        /// <summary>攻击距离。文案取自既有 `enemy.range.*`(EnemyInfo.RangeName/RangeText 同一套)。
        /// 近战是默认站位,不出图标(与稿子「近战是默认,不出 chip」同一条规则),但详情列表里
        /// 仍然给出完整说明——图标省的是战斗画面里的小标签,不是详情弹窗的解释权。</summary>
        public static Info OfRange(AttackRange range) => range == AttackRange.Ranged
            ? new Info("ranged", Strings.T("enemy.range.ranged.name"),
                Strings.T("status.duration.persistent_trait"), Strings.T("enemy.range.ranged.desc"))
            : new Info(null, Strings.T("enemy.range.melee.name"),
                Strings.T("status.duration.persistent_trait"), Strings.T("enemy.range.melee.desc"));

        /// <summary>够得着玩家时打谁。`enemy.focus.*` 是本任务新写的(AttackFocus 全项目没有
        /// 既有文案),命名跟 `enemy.range.*` 同一个家族。Default 是均匀随机的默认行为,不是
        /// 一个值得说明的特性——与 <see cref="TargetShape.Single"/> 同一处理,不出条目
        /// (返回 <see cref="None"/>);只有 Player(死盯玩家)值得列一条。</summary>
        public static Info OfFocus(AttackFocus focus) => focus == AttackFocus.Player
            ? new Info("focus", Strings.T("enemy.focus.player.name"),
                Strings.T("status.duration.persistent_trait"), Strings.T("enemy.focus.player.desc"))
            : None;

        /// <summary>召唤物出手的目标形状。Name 复用既有 `char.shape.&lt;x&gt;`(CharInfo.cs 卡面
        /// 文案同一套简称),Desc 是本任务新写的详情说明。Sweep/Skewer 有稿子出处也有既有 icon;
        /// Cleave/Volley/Chain 稿子没画,从 TargetShape.cs 自己的注释外推,也没有对应 icon 资产,
        /// IconKey 一律给 null。
        ///
        /// Single(只打一个,缺省值)不出条目——与 <see cref="OfFocus"/> 的 Default 同一条规则:
        /// 只有偏离默认的才出条目,「打谁」的默认不出、「打几个」的默认也不出,下一个新增的
        /// TargetShape 值该往哪边走由这条规则直接判断,不必逐个碰运气。这也是为什么每一只
        /// 普通近战单体怪不会在详情里挂一条「单体」——真正该被看见的是那几个偏离默认的。</summary>
        public static Info OfShape(TargetShape shape) => shape switch
        {
            TargetShape.Sweep => new Info("sweep", Strings.T("char.shape.sweep"),
                Strings.T("status.duration.persistent_trait"), Strings.T("char.shape.sweep.desc")),
            TargetShape.Skewer => new Info("skewer", Strings.T("char.shape.skewer"),
                Strings.T("status.duration.persistent_trait"), Strings.T("char.shape.skewer.desc")),
            TargetShape.Cleave => new Info(null, Strings.T("char.shape.cleave"),
                Strings.T("status.duration.persistent_trait"), Strings.T("char.shape.cleave.desc")),
            TargetShape.Volley => new Info(null, Strings.T("char.shape.volley"),
                Strings.T("status.duration.persistent_trait"), Strings.T("char.shape.volley.desc")),
            TargetShape.Chain => new Info(null, Strings.T("char.shape.chain"),
                Strings.T("status.duration.persistent_trait"), Strings.T("char.shape.chain.desc")),
            _ => None, // Single
        };
    }
}
