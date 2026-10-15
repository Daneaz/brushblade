using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>一条状态(稿 UnitFoe.dc.html 的 .st)。IconKey 可能为 null(缺笔/标点/通假/
    /// AP 上限四条走文字 chip)——按 StatusText 的契约,条目本身恒存在,调用方按「有没有
    /// 图标」分支画即可。</summary>
    public sealed class StatusEntry
    {
        public string IconKey;    // null = 走文字 chip
        public string ChipText;   // chip 上的数字或短字(「3」/「−25%」/「缺笔 2/3」)
        public Color ChipColor;
        // 描边态(封字·代价,StatusChipsWaterEarth 拍板「详情同样描边」):非 null 时 chip 前景改用 ChipFg、外圈画 ChipBorder。
        // 缺省 null = 实底白字,与从前相同
        public Color? ChipFg;
        public Color? ChipBorder;
        public string Name;       // 「灼烧 3 层」
        public string Duration;   // 「层数即时长」/「剩 2 回合」/「本场持久」
        public string Desc;
        // 挂着但眼下不生效(炽焰的减攻在灼不满门槛时,StatusChipsFire 稿):照列,名字与说明压成 text-faint
        public bool Dimmed;
    }

    /// <summary>一条特性或技能(稿的 .abil)。</summary>
    public sealed class AbilityEntry
    {
        public string IconKey;
        public Color ChipColor;
        public string Name;
        public string Desc;
        // 可空;非 null 时 UnitSheet 会在这条前面画一个分组小标题(连续同取值的条目共享一个
        // 标题,不逐条重画)。2026-09-01 review 新增——执笔人的「养成技能 · 局外」四条永久
        // 加成此前与护盾这类随时会消失的实时资源混排在同一列表、同款卡片、无任何视觉区分,
        // 丢了「这两类性质不同」这层信息。
        public string Section;
        // 召唤物详情右栏(SummonInfoNature 稿,D2-木 Task 5):Lead = 战场上那枚本命 chip 对应的一条 ——
        // 卡片左缘一道朱砂、名字后尾标「战场上那枚」、名字与说明分两行;Inline = 名字与说明排在同一行(其余本命与射程)
        public bool Lead;
        public bool Inline;
    }

    /// <summary>一张详情的全部内容。三类单位(敌人/召唤物/执笔人)共用,字段为 null 表达
    /// 「这类单位没有这项」——Task 4 的 UnitSheet 只认这一份结构,不认识 EnemyState/SummonState。</summary>
    public sealed class UnitDetail
    {
        public string PortraitPrefix;   // MobAssets 前缀;null = 用 FaceChar 画圆形字头像
        public string FaceChar;
        // Element 为 null 有两种含义,读的时候要分清是哪一种:
        //   1) 执笔人(玩家没有五行属性,PlayerInfo.Sheet 从不产生非 null 值);
        //   2) 生僻字未读懂真身(EnemyInfo.Sheet 读 enemy.ApparentElement,受击两次前是 null)。
        // 这两种情形 Wuxing 也同时为 null(WuxingOf(null) 恒返回 null),不能靠
        // 「Wuxing 是否为 null」反过来分辨是哪一种——两种情形下 Wuxing 都是 null,没有区分度。
        // 2026-09-01 review 修:此前这里写的结论是「真正能分辨的信号是这份 UnitDetail 从哪个
        // Info.Sheet 产生,不该指望从 UnitDetail 字段里反推」——这条已经不再成立。
        // ElementUnknown 就是专门为这件事补的显式信号:EnemyInfo.Sheet 按
        // enemy.ApparentElement == null 据实填 true,PlayerInfo.Sheet 恒填 false,
        // 调用方(UnitSheet)只读这一个字段就能分辨,不需要再去猜哪个 Info.Sheet 产生的。
        public Element? Element;
        public bool ElementUnknown; // true = 生僻字未读懂(画「?」徽章);
                                     // Element == null 且此项为 false = 执笔人没有五行(不画徽章)
        public string Name;
        public string[] Tags;           // 「前排 · 近战」/「文山 ×3.0」
        public string Flavor;           // 一句风味描述;null = 不画
        public int Hp, MaxHp, Shield, ActionMeter;
        public (string label, string value, string note)[] Figures;  // 攻/甲/速/行动
        public List<StatusEntry> Statuses;
        public List<AbilityEntry> Abilities;
        public string AbilitiesTitle;   // 右栏标题;null = 缺省「特性 · 技能」(召唤物详情写「本命」,SummonInfoNature 稿)
        public (Element beats, Element beatenBy)? Wuxing;  // 生克行;执笔人为 null
    }

    /// <summary>EnemyInfo/SummonInfo/PlayerInfo 共用的取词与配色(2026-08-31,单位详情轮二 Task 3)。
    /// 独立成一个内部类而不是三处各写一份:StatusEntry 的 chip 底色/文案只该有一份口径,
    /// 三个 Info 类分别对着敌人/召唤物/玩家的 StatusBag 抄三遍只会越改越漂。
    ///
    /// 颜色分组抄自 <c>docs/design/drafts/scenes/StatusGlossary.dc.html</c> 的六个 --gc 分组
    /// (唯一权威;那份稿本身就是给这 20 个 StatusKind 定颜色用的):
    ///   #C53637 持续伤害与增长的威胁(= Theme.Cinnabar,数值核对过,逐位相同)
    ///   #19507F 控制类 debuff(Theme 里没有现成的,新增一个字面量)
    ///   #2E7D46 我方增益 · 守(Theme 里没有精确匹配,新增一个字面量;DoneGreen 是另一支近似色,
    ///            数值对不上,不能借)
    ///   #C9A94A 我方增益 · 攻(= Theme.RarityColor(CardRarity.Gold),核对过)
    ///   #8F7332 攻击模式与站位(= Theme.ElementColor(Element.Earth);稿原为 #997C3C,2026-09-18 两边一起为白字对比度调深)
    ///   #3D4E69 能力与被动(= Theme.InkSoft,核对过;与 Theme.AbilityChipColor 的默认分支同色)</summary>
    internal static class UnitDetailChip
    {
        private static readonly Color Control = new(0.098f, 0.314f, 0.498f); // #19507F
        private static readonly Color Guard = new(0.180f, 0.490f, 0.275f);   // #2E7D46

        /// <summary>攻击模式与站位(远程/横扫/穿刺/锁人……)这一类词条的底色,OfRange/OfShape/
        /// OfFocus 的调用方直接读这个常量,不必逐个 switch。</summary>
        public static readonly Color Positioning = Theme.ElementColor(Element.Earth); // #8F7332

        /// <summary>能力/被动类词条(召唤物反伤/闪避/疾,以及找不到更合适分组时的兜底)。</summary>
        public static readonly Color Ability = Theme.InkSoft; // #3D4E69

        /// <summary>词条图标 chip 的底色(spec B §1.5):与 <see cref="ColorFor"/> 同四组,不另写一份色值。</summary>
        internal static Color ToneColor(ChipTone tone) => tone switch
        {
            ChipTone.Threat => Theme.Cinnabar,
            ChipTone.Control => Control,
            ChipTone.Guard => Guard,
            _ => Theme.RarityColor(CardRarity.Gold),
        };

        /// <summary>状态 chip 底色。只有 SpeedModifier 需要看符号(减速走控制蓝,速度走守御绿),
        /// 其余 19 个 StatusKind 各自固定在一个分组里,与符号无关。</summary>
        public static Color ColorFor(StatusKind kind, int magnitude) => kind switch
        {
            // 标记(D1 Task 6,StatusChips 稿 k-dot):朱砂,与灼烧同属「持续伤害与威胁」
            StatusKind.Burn or StatusKind.BurnNoDecay or StatusKind.Bleed or StatusKind.Vulnerable
                or StatusKind.Mine   // 埋雷(StatusChipsFire 稿 k-dot 朱砂)
                or StatusKind.Bind or StatusKind.ThornMark   // 被缠 / 荆刺(StatusChipsWood 稿 k-dot 朱砂:效果是挨打)
                or StatusKind.Doom => Theme.Cinnabar,   // 致命(traits StatusChips 稿 k-dot 朱砂)
            StatusKind.Freeze or StatusKind.Blind or StatusKind.Silence or StatusKind.Curse
                or StatusKind.ArmorBreak or StatusKind.Seal or StatusKind.FrostResist or StatusKind.IceStall
                or StatusKind.Taunt   // 嘲讽(D2-0 Task 2,稿 k-ctrl 墨蓝)
                or StatusKind.HealBlock   // 干涸(StatusChipsFire 稿 k-ctrl 墨蓝,与减攻同组)
                or StatusKind.BuffBlock or StatusKind.SkillCut => Control,   // 禁增益 / 大招削弱(StatusChipsWaterEarth 稿 k-ctrl 墨蓝)
            StatusKind.SpeedModifier => magnitude < 0 ? Control : Guard,
            StatusKind.DefenseBuff or StatusKind.Immunity or StatusKind.Reflect or StatusKind.Block
                or StatusKind.DodgeBuff or StatusKind.HealOverTime
                or StatusKind.Seed => Guard,   // 种(D1 Task 6,稿 k-heal 翠玉)
            // 本回合减伤 / 保命(D1 Task 7):我方守御类(StatusChips 10-04 稿翠玉)
            StatusKind.DamageCut or StatusKind.Endure
                or StatusKind.HitCap   // 铜墙铁壁(D2-土 G2):与减伤同组(Q20)
                or StatusKind.HurtShield or StatusKind.HurtHeal   // 堡垒 / 栉风沐雨(StatusChipsWaterEarth 稿 k-heal 翠玉)
                or StatusKind.DebuffWard or StatusKind.TurnPulse   // 免疫减益 / 每回合效果(同稿 k-heal)
                or StatusKind.SummonThorns   // 荆棘光环(StatusChipsWood 稿 k-heal 翠玉:临时的荆棘)
                or StatusKind.Retaliate => Guard,   // 回敬(StatusChipsFire 稿 k-heal 翠玉)
            StatusKind.AttackBuff or StatusKind.Morale or StatusKind.CritBuff
                or StatusKind.CounterBoost    // 反击加倍(StatusChips 10-04 稿赭金:加的是伤害)
                or StatusKind.SummonEmpower   // 攻击光环(StatusChipsWood 稿 k-gold 赭金)
                or StatusKind.PierceBuff
                or StatusKind.Kindle   // 燃意(spec C)
                => Theme.RarityColor(CardRarity.Gold),
            StatusKind.SummonRanged => Positioning,   // 远程光环:射程一类(攻击模式与站位)
            // AP 上限稿上没有归组(它在文字 chip 那份「两处待拍板」清单里,不在六色分组表里)——
            // 与 morale/attack/pierce 同属「我方增益」大类,按气质就近归到金,而不是新开一支色。
            StatusKind.ApBoost => Theme.RarityColor(CardRarity.Gold),
            _ => Theme.InkSoft, // Obsolete 等占位值:StatusText.Of 已对它们返回 None,调用方会
                                 // 在 Name == null 时跳过整条,这里的颜色实际上不会被用到。
        };

        /// <summary>状态 chip 上的短文案。符号/单位的取舍抄自三张权威稿里能直接看到的例子
        /// (灼烧「3」、冻结「」、减速「−50」、诅咒「−25%」、护甲「+18」、免疫「1」、反弹「30%」、
        /// 战意「3」、穿透「+24」、封字「−1AP」);稿子没画到的其余 StatusKind 按同一套符号规则
        /// 外推(正值增益点数记「+」、负值减益点数记「−」、纯计数不记符号、AP 类挂「AP」后缀)——
        /// 这批外推的口径在 task-3-report.md 里逐条列了,不在这里重复注释。</summary>
        public static string TextFor(StatusKind kind, int magnitude) => kind switch
        {
            // ⚠ 2026-09-01 review 修:三个 kind 与 StatusGlossary.dc.html 的 chip 列不符
            // (那份稿子对 20 个 StatusKind 的 chip 文案是全覆盖的,核对时漏了三处):
            // ArmorBreak 稿上是「20」不带符号(不是「−20」)、AttackBuff 稿上是「+50」不带 %
            // (不是「+50%」)、CritBuff 稿上是「20%」不带 +(不是「+20%」)。Seal 特例保留
            // UnitMe.dc.html 的「−1AP」而不是 glossary 的「−1」——具体屏稿优先于分类图鉴,
            // 这条不改。
            StatusKind.Burn or StatusKind.Bleed or StatusKind.Immunity or StatusKind.Block
                or StatusKind.Morale or StatusKind.HealOverTime or StatusKind.ArmorBreak =>
                Strings.T("detail.chip.plain", ("value", magnitude)),
            StatusKind.BurnNoDecay or StatusKind.Freeze or StatusKind.Silence or StatusKind.FrostResist
                or StatusKind.IceStall or StatusKind.Seed or StatusKind.Vulnerable
                or StatusKind.Taunt   // 种 / 标记 / 嘲讽无数字(稿)
                // 干涸 / 埋雷 / 回敬(StatusChipsFire 稿):正式图标已进管线,chip 不带字。埋雷的伤害写在说明里;
                // 回敬的剩余次数只在战场 chip 上(要读 Core 的本回合计数,StatusBag 里没有)
                or StatusKind.HealBlock or StatusKind.Mine or StatusKind.Retaliate
                or StatusKind.Bind or StatusKind.ThornMark or StatusKind.SummonRanged   // 被缠 / 荆刺 / 远程光环:无数字(StatusChipsWood 稿)
                or StatusKind.Doom   // 致命(traits StatusChips 稿):无数字,剩余回合在时长行
                // 反击加倍 / 保命(StatusChips 10-04 稿)、受击回复 / 受击得盾(E3 Q9:有了图标,数值在说明里):只出图标
                or StatusKind.CounterBoost or StatusKind.Endure or StatusKind.HurtShield or StatusKind.HurtHeal => "",
            // 减伤 / 受伤上限有了正式图标(bulwark / hitcap,2026-10-11 拍板):详情 chip 印百分比(SummonInfoNature 稿「减伤 15%」);
            // 战场上不带数字。
            // 攻击 / 荆棘光环(StatusChipsWood / SummonInfoNature 稿):详情 chip 印百分比
            StatusKind.DamageCut or StatusKind.HitCap or StatusKind.SummonEmpower or StatusKind.SummonThorns
                or StatusKind.Kindle =>
                Strings.T("detail.chip.plain_pct", ("value", magnitude)),
            StatusKind.SpeedModifier => magnitude < 0
                ? Strings.T("detail.chip.negative", ("value", -magnitude))
                : Strings.T("detail.chip.positive", ("value", magnitude)),
            StatusKind.Blind or StatusKind.Curse =>
                Strings.T("detail.chip.negative_pct", ("value", magnitude)),
            StatusKind.Seal =>
                Strings.T("detail.chip.negative_ap", ("value", magnitude)),
            StatusKind.DefenseBuff or StatusKind.PierceBuff or StatusKind.AttackBuff =>
                Strings.T("detail.chip.positive", ("value", magnitude)),
            StatusKind.Reflect or StatusKind.CritBuff =>
                Strings.T("detail.chip.plain_pct", ("value", magnitude)),
            StatusKind.DodgeBuff =>
                Strings.T("detail.chip.positive_pct", ("value", magnitude)),
            StatusKind.ApBoost =>
                Strings.T("detail.chip.positive_ap", ("value", magnitude)),
            _ => "",
        };

        /// <summary>「身上的状态」列表的共用构建:敌人/召唤物/执笔人的 StatusBag 结构完全一样,
        /// 三处各写一遍循环只会越改越漂。Name == null(StatusText.Of 对占位值的兜底)按契约跳过。
        ///
        /// <paramref name="isPlayer"/> 透传给 <see cref="StatusText.Of"/>(2026-09-01 review 修):
        /// AttackBuff 的说明文案敌我口径不同(百分比 vs 点数),只有 PlayerInfo 传 true。
        /// <paramref name="graph"/>(可空)只用来把带门槛的减攻翻出特性名(「炽焰:灼满 5 层时……」),
        /// 取不到时退回状态名。
        /// <paramref name="cardLevelOf"/>(可空,D2-水 Task 5):回敬 / 回合脉冲的载荷按来源字等级缩放后印,只有执笔人那一份传。
        /// <paramref name="slotLabelOf"/>(可空,D2-木 Task 5):木灵槽位 → 「前排第 1 位」,被缠点名藤灵用。</summary>
        public static List<StatusEntry> BuildStatuses(StatusBag statuses, bool isPlayer = false, RecipeGraph graph = null,
            Func<string, int> cardLevelOf = null, Func<int, string> slotLabelOf = null) =>
            BuildStatuses(statuses.All, statuses, isPlayer, graph, cardLevelOf, slotLabelOf);

        /// <summary>同上,但条目可以只是袋子的一部分(召唤物详情:玩家袋里落在这只木灵身上的光环,D2-木 Task 5)。
        /// <paramref name="statuses"/> 是条目所在的袋子(炽焰门槛要读同袋的灼)。</summary>
        public static List<StatusEntry> BuildStatuses(IEnumerable<StatusEffect> effects, StatusBag statuses, bool isPlayer = false,
            RecipeGraph graph = null, Func<string, int> cardLevelOf = null, Func<int, string> slotLabelOf = null)
        {
            var list = new List<StatusEntry>();
            foreach (var effect in effects)
            {
                // 整条状态取词:淋漓 / 免疫减益 / 回敬 / 回合脉冲的说明要读三元组以外的字段
                var info = StatusText.Of(effect, isPlayer, cardLevelOf, slotLabelOf);
                if (info.Name == null) continue;
                var entry = new StatusEntry
                {
                    IconKey = info.IconKey,
                    ChipText = TextFor(effect.Kind, effect.Magnitude),
                    ChipColor = ColorFor(effect.Kind, effect.Magnitude),
                    Name = info.Name,
                    Duration = info.Duration,
                    Desc = info.Desc,
                };
                // 封字·代价态(杜门不出,Buff 极性):墨蓝描边纸底,同战场那枚(StatusChipsWaterEarth 拍板「详情同样描边」)
                if (effect.Kind == StatusKind.Seal && effect.Polarity == StatusPolarity.Buff)
                {
                    entry.ChipColor = Theme.PanelPaper;
                    entry.ChipFg = Theme.InkSoft;
                    entry.ChipBorder = Theme.InkSoft;
                }
                // 炽焰(StatusChipsFire 稿):门槛没到的减攻不算进攻击力、战场上不出 chip,详情里照列、文字压暗,
                // 说明换成「炽焰:灼满 5 层时攻击 −30%(现在 4 层)」。判据与 EnemyState.ActiveCurse 同口径。
                if (effect.Kind == StatusKind.Curse && effect.MinBurn > 0)
                {
                    int burn = statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
                    if (burn < effect.MinBurn)
                    {
                        string trait = graph != null && effect.SourceId != null && graph.TryGet(effect.SourceId, out var src)
                            ? TraitRules.NameOf(src, effect.TraitKey) : null;
                        entry.Dimmed = true;
                        entry.Desc = Strings.T("status.curse.gated", ("trait", trait ?? info.Name),
                            ("min", effect.MinBurn), ("magnitude", effect.Magnitude), ("burn", burn));
                    }
                }
                // 移花接木(W13,WoodCues 稿):魅惑在「改为攻击自己阵营」后接「伤害 ×2(移花接木)」,倍数取 Magnitude(100 + Augment);
                // 没有时原句不变。特性名是游戏数据,从来源字的特性表里找(找不到就不带括号)
                if (effect.Kind == StatusKind.Charm && effect.Magnitude > 100)
                {
                    string mult = (effect.Magnitude / 100f).ToString("0.##");
                    string trait = CharInfo.TraitNameOf(graph, effect.SourceId,
                        e => e.Kind == EffectKind.Augment && e.AugmentKind == EffectKind.Charm);
                    entry.Desc = trait != null
                        ? Strings.T("status.charm.desc.mult", ("mult", mult), ("trait", trait))
                        : Strings.T("status.charm.desc.mult.plain", ("mult", mult));
                }
                // 木灵光环(SummonInfoNature 稿):时长行写来源 ——「林立光环 · 剩 2 回合」;特性名取不到时退回来源字
                if (effect.Kind == StatusKind.SummonEmpower || effect.Kind == StatusKind.SummonThorns
                    || effect.Kind == StatusKind.SummonRanged)
                {
                    var kind = effect.Kind == StatusKind.SummonEmpower ? EffectKind.SummonEmpower
                        : effect.Kind == StatusKind.SummonThorns ? EffectKind.SummonThorns : EffectKind.SummonRanged;
                    string source = CharInfo.TraitNameOf(graph, effect.SourceId, e => e.Kind == kind) ?? effect.SourceId;
                    if (source != null)
                        entry.Duration = Strings.T("status.summonaura.source", ("source", source), ("duration", info.Duration));
                }
                list.Add(entry);
            }
            return list;
        }

        /// <summary>本命在场光环(D2-木 M2:林荫 / 森严 / 蟾宫)不是状态条目 —— 它们实时读场上存活木灵的本命,
        /// 袋子里没有一条可列。详情里合成一条(SummonInfoNature 稿「减伤 15% · 林荫 · 林灵在场时」、StatusChipsWood 稿
        /// 「暴击 +10% · 蟾宫」),数值读 Core 读口(与结算同源),来源取特性名(游戏数据,取不到退回「本命」)。
        /// <paramref name="forSummon"/>:木灵侧(林荫与森严取最强)/ 玩家侧(只有林荫)。</summary>
        public static void AddWoodNatureEntries(List<StatusEntry> list, BattleEngine battle, RecipeGraph graph, bool forSummon)
        {
            int cut = battle.WoodDamageCutPercent(forSummon);
            int alive = battle.AliveSummonCount;
            int crit = 0;
            var cutLines = new List<string>();
            var cutNames = new List<string>();
            string critName = null;
            foreach (var s in battle.Summons)
            {
                var p = s != null && s.Alive ? s.Passive : null;
                if (p == null) continue;
                // 森严只在木灵数够门槛时生效(同 Core WoodCutPercent),没生效的不列
                bool grove = p.GroveCut > 0, thicket = forSummon && p.ThicketCut > 0 && alive >= p.ThicketMin;
                if (cut > 0 && (grove || thicket))
                    CharInfo.WoodNatureLines(p, (key, line) =>
                    {
                        if (!(key == CharInfo.NatureKey.GroveCut && grove) && !(key == CharInfo.NatureKey.ThicketCut && thicket)) return;
                        if (!cutLines.Contains(line)) cutLines.Add(line);
                        string name = CharInfo.NatureTraitName(graph, key == CharInfo.NatureKey.GroveCut
                            ? x => x.GroveCut > 0 : x => x.ThicketCut > 0);
                        if (name != null && !cutNames.Contains(name)) cutNames.Add(name);
                    });
                if (p.CritAura > crit) crit = p.CritAura;
            }
            if (crit > 0) critName = CharInfo.NatureTraitName(graph, x => x.CritAura > 0);
            if (cut > 0)
                list.Add(new StatusEntry
                {
                    IconKey = "bulwark",
                    ChipText = Strings.T("detail.chip.plain_pct", ("value", cut)),
                    ChipColor = ColorFor(StatusKind.DamageCut, cut),
                    Name = Strings.T("status.woodcut.name", ("value", cut)),
                    Duration = NatureDuration(cutNames.Count > 0 ? string.Join(" / ", cutNames) : null),
                    Desc = Strings.T("status.woodcut.desc", ("list", string.Join(";", cutLines)), ("count", alive),
                        ("cap", CombatCaps.NonArmorReductionPercent)),
                });
            if (crit > 0)
                list.Add(new StatusEntry
                {
                    IconKey = "crit",
                    ChipText = Strings.T("detail.chip.plain_pct", ("value", crit)),
                    ChipColor = ColorFor(StatusKind.CritBuff, crit),
                    Name = Strings.T("status.critaura.name", ("value", crit)),
                    Duration = NatureDuration(critName),
                    Desc = Strings.T("char.passive.crit_aura", ("value", crit)),
                });
        }

        private static string NatureDuration(string trait) => trait != null
            ? Strings.T("status.nature.duration", ("trait", trait))
            : Strings.T("status.nature.duration.plain");

        /// <summary>生克提示:「克 X ×1.5 · 被 Y 克」还原成 (beats, beatenBy) 元组。
        /// 五行环(木火土金水)里每个属性必然同时有一个「我克」与一个「克我」;心不在环内,
        /// 两头都摇不到,返回 null——与 SummonInfo.WuxingText 的空串同一条件,只是换了个类型。</summary>
        public static (Element beats, Element beatenBy)? WuxingOf(Element? self)
        {
            if (self == null) return null;
            Element? beats = null, beatenBy = null;
            foreach (Element other in Enum.GetValues(typeof(Element)))
            {
                if (other == self.Value) continue;
                if (WuxingResolver.KeMultiplier(self.Value, other) > 1f) beats = other;
                if (WuxingResolver.KeMultiplier(other, self.Value) > 1f) beatenBy = other;
            }
            return beats == null ? null : (beats.Value, beatenBy!.Value);
        }

        /// <summary>「基准 X」+ 若干偏离说明,拼成 Figures 的 note。deltas 为空时返回 null——
        /// 对应稿上「没有偏离就不画 note」的规则(焦痕的甲 0、速 100 时都不带小字)。</summary>
        public static string BaseNote(int baseValue, params string[] deltas)
        {
            var real = new List<string>();
            foreach (var d in deltas) if (!string.IsNullOrEmpty(d)) real.Add(d);
            if (real.Count == 0) return null;
            var text = Strings.T("detail.figure.base", ("value", baseValue));
            foreach (var d in real) text += " · " + d;
            return text;
        }

        public static string DeltaBuffPct(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_buff_pct", ("name", name), ("value", value));
        public static string DeltaDebuffPct(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_debuff_pct", ("name", name), ("value", value));
        public static string DeltaBuffPts(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_buff_pts", ("name", name), ("value", value));
        public static string DeltaDebuffPts(string name, int value) => value == 0 ? null
            : Strings.T("detail.figure.delta_debuff_pts", ("name", name), ("value", value));
    }
}
