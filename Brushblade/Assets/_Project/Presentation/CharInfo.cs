using System.Collections.Generic;
using System.Text;
using Brushblade.Core;
using Brushblade.Data;

namespace Brushblade.Presentation
{
    /// <summary>字卡简述:从定义机械生成(拼音/释义/属性/稀有度/AP/效果/配方)。</summary>
    public static class CharInfo
    {
        /// <summary>cardLevel:局外卡等级,效果数值按 MetaRules.ScaleByCardLevel 缩放后显示,
        /// 与战斗结算取同一函数(2026-07-20:此前恒显示基础值,升级看不出变化)。</summary>
        public static string Summary(CharDef def, RecipeGraph graph, int cardLevel = 1)
        {
            var text = new StringBuilder();
            text.Append('「').Append(def.Id).Append('」');
            if (def.Pinyin != null)
                text.Append(def.Pinyin).Append(' ');
            if (!string.IsNullOrEmpty(def.Gloss))
                text.Append(def.Gloss).Append('|');
            // 2026-08-21:不再印 AP —— ApCostFor 一律返回 1,写出来是零信息量。
            // 功能性的 AP 判断(能不能出、出不起时的报错)照旧读 def.ApCost,只是不进描述文案。
            text.Append(RarityName(def.Rarity)).Append('·')
                .Append(def.Element is { } element ? ElementName(element) + "系" : Strings.T("char.element.neutral"));

            if (!def.IsLeaf)
                text.Append('|').Append(Strings.T("char.summary.recipe")).Append(string.Join("+", def.Recipe));

            if (cardLevel > 1)
                text.Append("|Lv.").Append(cardLevel);

            text.Append('|').Append(EffectsText(def, cardLevel));

            return text.ToString();
        }

        /// <summary>详情弹窗用:Summary 的分行版。</summary>
        public static string Detail(CharDef def, RecipeGraph graph, int cardLevel = 1) =>
            Summary(def, graph, cardLevel).Replace("|", "\n");

        /// <summary>效果串(升级 preview 取前后两级各调一次)。
        ///
        /// 双方向字(水/土,2026-09-02):<see cref="CharDef.AttackEffects"/> 非空时,
        /// 卡面要把攻/护两面都印出来——玩家要在这张卡上选「攻」还是「护」,只显示
        /// <see cref="CharDef.Effects"/>(护面)会让另一面在游戏里彻底不可见。格式与
        /// tools/design/gen_char_doc.py 的 func_desc 同口径:`攻:… / 护:…`。</summary>
        public static string EffectsText(CharDef def, int cardLevel = 1)
        {
            string support = OneSideEffectsText(def.Effects, def, cardLevel);
            if (def.AttackEffects.Count == 0)
                return support;
            string attack = OneSideEffectsText(def.AttackEffects, def, cardLevel);
            return Strings.T("char.summary.dual_direction", ("attack", attack), ("support", support));
        }

        /// <summary>只渲染一面的效果串。字卡详情弹窗的双方向版把攻、护画成两块,
        /// 各自要一句自己的话 —— <see cref="EffectsText"/> 给的是「攻:… / 护:…」的合写。</summary>
        public static string SideEffectsText(IReadOnlyList<EffectDef> effects, CharDef def, int cardLevel) =>
            OneSideEffectsText(effects, def, cardLevel);

        private static string OneSideEffectsText(IReadOnlyList<EffectDef> effects, CharDef def, int cardLevel)
        {
            if (effects.Count == 0)
                return Strings.T("char.summary.noeffect");

            var parts = new StringBuilder();
            for (int i = 0; i < effects.Count; i++)
            {
                // 分隔符是分号,不是逗号(2026-08-10 还债):效果内部本来就带逗号
                // ——Reflect 的「伤害,N回合」、HealOverTime 的「/回合,共N回合」、
                // 穿甲的「(穿甲:无视减伤,额外+15%)」——
                // 分隔符与内容同为 U+002C 时,多效果字会被读成比实际更多的段。
                // 分号的层级严格强于逗号(顿号反而更弱,当结构分隔符会把层级弄反),
                // 所以各分支内部照常写逗号即可,不必再逐个改文案。
                if (i > 0) parts.Append(';');
                var e = effects[i];
                int v = MetaRules.ScaleByCardLevel(e.Value, cardLevel);
                string shown = v.ToString();
                parts.Append(e.Kind switch
                {
                    EffectKind.DamageSingle => Strings.T("char.effect.damagesingle",
                            ("shape", ShapeLabel(e)), ("value", shown))
                        + DoubleVsText(e)
                        + PierceText(e) + BacklineText(e) + HitCountText(e) + ExecuteText(e)
                        + ShapeSuffix(e),
                    EffectKind.DamageAll => Strings.T("char.effect.damageall", ("value", shown))
                        + DoubleVsText(e)
                        + PierceText(e) + HitCountText(e) + ExecuteText(e),
                    EffectKind.BurnSingle => Strings.T("char.effect.burnsingle", ("value", shown)),
                    EffectKind.BurnAll => Strings.T("char.effect.burnall", ("value", shown)),
                    EffectKind.Shield => Strings.T("char.effect.shield", ("value", shown))
                        + (e.PersistOnce ? Strings.T("char.effect.shield.persistonce") : ""),
                    // 群体护盾(2026-09-05,崩):与 HealAll 同款「(含召唤物)」的措辞 ——
                    // 玩家要知道的是这一份盾不只给自己
                    EffectKind.ShieldAll => Strings.T("char.effect.shieldall", ("value", shown))
                        + (e.PersistOnce ? Strings.T("char.effect.shield.persistonce") : ""),
                    EffectKind.BurnPotency => Strings.T("char.effect.burnpotency", ("value", shown)),
                    EffectKind.HealSelf => Strings.T("char.effect.healself", ("value", shown)),
                    // 召唤物字形归位后(2026-08-15)绝大多数字召的就是自己,写成「梅:召1×「梅」」
                    // 纯属绕口;只有召别的字时才点名。数据侧的默认值仍是「木」,不同名照旧显示
                    EffectKind.Summon => (e.SummonChar == def.Id
                            ? Strings.T("char.effect.summon.self", ("count", e.SummonCount))
                            : Strings.T("char.effect.summon.other", ("count", e.SummonCount)) + "「" + e.SummonChar + "」") +
                        Strings.T("char.effect.summon.stats",
                            ("hp", shown), ("atk", MetaRules.ScaleByCardLevel(e.SummonAttack, cardLevel)))
                        + PassiveText(e.Passive) + SummonShieldText(e),
                    EffectKind.Bleed => Strings.T("char.effect.bleed", ("value", shown)),
                    EffectKind.HealAll => Strings.T("char.effect.healall", ("value", shown)),
                    EffectKind.HealOverTime => e.TargetAll
                        ? Strings.T("char.effect.healovertime.all", ("value", shown), ("turns", e.Turns))
                        : Strings.T("char.effect.healovertime.single", ("value", shown), ("turns", e.Turns)),
                    EffectKind.Freeze => Strings.T("char.effect.freeze", ("value", shown)),
                    EffectKind.Slow => Strings.T("char.effect.slow", ("value", shown)),
                    EffectKind.DefenseBuff => Strings.T("char.effect.defensebuff", ("value", shown)),
                    EffectKind.ArmorBreak => Strings.T("char.effect.armorbreak", ("value", shown)),
                    // 驱散条数不吃卡等级(与 BattleEngine 的 EffectKind.Dispel 分支同口径)——
                    // 用 e.Value 而不是 v:真正的约束是正数条数不能被 ScaleByCardLevel 缩放
                    // (Lv.10 系数 1.9,「驱散 2 条」会被算成 ceil(2×1.9)=4 条,与 Core 实际驱散数不符;
                    // −1 哨兵同样不缩放只是顺带受益,不是单独的理由)
                    EffectKind.Dispel => e.Value < 0
                        ? (e.TargetAll ? Strings.T("char.effect.dispel.all.full") : Strings.T("char.effect.dispel.single.full"))
                        : (e.TargetAll ? Strings.T("char.effect.dispel.all.count", ("count", e.Value)) : Strings.T("char.effect.dispel.single.count", ("count", e.Value))),
                    EffectKind.Cleanse => Strings.T("char.effect.cleanse"),
                    EffectKind.Immunity => Strings.T("char.effect.immunity", ("value", shown)),
                    EffectKind.Revive => Strings.T("char.effect.revive", ("value", shown)),
                    // 熣(DamageSingle + Blind)曾被读成三段,当时改成空格治标(与 ArmorBreak 的
                    // 「破甲 {shown} 回合」同款);根因已由上面的分号分隔符解决,这里保留空格写法不再动
                    EffectKind.Blind => e.TargetAll
                        ? Strings.T("char.effect.blind.all", ("value", shown), ("turns", e.Turns))
                        : Strings.T("char.effect.blind.single", ("value", shown), ("turns", e.Turns)),
                    EffectKind.Silence => Strings.T("char.effect.silence", ("turns", e.Turns)),
                    EffectKind.Reflect => Strings.T("char.effect.reflect", ("value", shown), ("turns", e.Turns)),
                    EffectKind.BurnNoDecay => Strings.T("char.effect.burnnodecay"),
                    EffectKind.BurnSettleNow => Strings.T("char.effect.burnsettlenow"),
                    EffectKind.Detonate => Strings.T("char.effect.detonate"),
                    // 不写「(基准 100)」:那是内部常量,玩家不该看见,而且为它多占 2 个字体码位。
                    // 跑图界面的角色栏已经在显示「攻击 N」,+50 对玩家是可解释的增量。
                    EffectKind.Empower => Strings.T("char.effect.empower", ("value", shown)),
                    EffectKind.Morale => Strings.T("char.effect.morale",
                        ("stacks", shown), ("per", 10), ("max", 5)),   // per 是百分数,文案里带 %
                    // ApBoost 不吃卡等级(与 BattleEngine 的 EffectKind.ApBoost 分支同口径:
                    // AP 是节奏/经济不是资源)——用 e.Value 而不是 v
                    EffectKind.ApBoost => Strings.T("char.effect.apboost", ("value", e.Value)),
                    // 倍率读常量而不是写死「×1.5」:E-b5 重平衡会改那个常量,写死了卡面就会骗人
                    EffectKind.CritBuff => Strings.T("char.effect.critbuff",
                        ("value", shown),
                        ("mult", (BattleConfig.CritMultiplierPercent / 100f).ToString("0.##"))),
                    // 与 PierceText 同一套措辞(「无视 N 点护甲」),差别只在存续:那条是本次,这条是本场。
                    // 锐 身上没有伤害效果,PierceText 不会出现,所以这里必须把口径自己说全。
                    EffectKind.PierceBuff => Strings.T("char.effect.piercebuff", ("value", shown)),
                    // 厚积薄发 / 涌泉相报(2026-09-02,水土双方向):清空全部厚/泉,按层数 × value 打全体。
                    // 与 tools/design/gen_char_doc.py 的 desc() 同口径。
                    EffectKind.SpendHeft => Strings.T("char.effect.spendheft", ("value", shown)),
                    EffectKind.SpendWellspring => Strings.T("char.effect.spendwellspring", ("value", shown)),
                    _ => e.Kind.ToString(),
                });
            }
            return parts.ToString();
        }

        /// <summary>斩杀后缀(2026-08-23)。此前**卡面一个字都不印** —— 引擎侧 2026-08-06 就实现了
        /// (`1514207 feat(core)`,范围只写了 core),而 CharInfo 从没跟进,玩家看铡的卡面只见
        /// 「单体145伤」,不知道它有 25% 直接抹杀。措辞与 tools/design/gen_char_doc.py 同口径。
        ///
        /// **直杀与双倍是互斥的两条分支**(BattleEngine 的 TryExecuteKill / ExecuteBonus):
        /// executeKills 的字命中阈值直接归零血量、那记伤害根本不结算,没命中则毫无加成;
        /// 其余斩杀字是基础值 ×2。Boss 只免疫前者,后者照常吃 —— 所以打 Boss 时铡反不如镰。</summary>
        private static string ExecuteText(EffectDef e) =>
            e.ExecuteBelowPercent <= 0 ? "" :
            e.ExecuteKills
                ? Strings.T("char.effect.execute.kill", ("percent", e.ExecuteBelowPercent))
                : Strings.T("char.effect.execute.double", ("percent", e.ExecuteBelowPercent));

        /// <summary>多段后缀(2026-08-23)。每段完全独立:各自过生克、各自减一次护甲,
        /// 也各自过斩杀的「打之前判血」——「第一段把敌人打进阈值、第二段触发处决」是真会
        /// 发生的涌现(EffectDef.HitCount 的注释)。全表只有「剁」用它。</summary>
        private static string HitCountText(EffectDef e) =>
            e.HitCount > 1 ? Strings.T("char.effect.hitcount", ("count", e.HitCount)) : "";

        /// <summary>偷袭后缀(2026-08-23)。与召唤物被动的「远程」是同一件事的两侧:
        /// 那条挂在召唤物身上,这条作用于玩家出的这一记单体伤害。
        ///
        /// 载体:砸 / 冷 / 熣 / 刲(2026-09-02 按字意装配)。2026-08-25 刺 改贯穿之后
        /// 到此之前**一张字都没标**,这条后缀有半个月是渲染不出来的死路径 ——
        /// 全集由 CharTableTests.RealConfig_BacklineChars_CanStrikeBackline 钉住。</summary>
        private static string BacklineText(EffectDef e) =>
            e.CanStrikeBackline ? Strings.T("char.effect.backline") : "";

        /// <summary>召唤物被动(2026-08-23)。此前**卡面完全不印**,导致柳(血80/攻30)与
        /// 松(血120/攻30)除血量外毫无区别 —— 而柳实际带 50% 闪避,玩家没法判断该留哪张。
        /// 顺序与 SummonPassive 的字段声明一致,措辞与 gen_char_doc.py 的 PASSIVE 表同口径。</summary>
        private static string PassiveText(SummonPassive p)
        {
            if (p == null) return "";
            var parts = new List<string>();
            if (p.Speed > 0) parts.Add(Strings.T("char.passive.speed", ("value", p.Speed)));
            if (p.Thorns > 0) parts.Add(Strings.T("char.passive.thorns", ("value", p.Thorns)));
            if (p.HealAlly > 0) parts.Add(Strings.T("char.passive.healally", ("value", p.HealAlly)));
            if (p.OnHitBurn > 0)
                parts.Add(p.OnHitBurnAll
                    ? Strings.T("char.passive.onhitburn.all", ("value", p.OnHitBurn))
                    : Strings.T("char.passive.onhitburn", ("value", p.OnHitBurn)));
            if (p.OnHitCurse > 0) parts.Add(Strings.T("char.passive.onhitcurse", ("value", p.OnHitCurse)));
            if (p.Dodge > 0) parts.Add(Strings.T("char.passive.dodge", ("value", p.Dodge)));
            if (p.Ranged) parts.Add(Strings.T("char.passive.ranged"));
            // 出手形状(2026-08-29,剑横扫 / 枪贯穿 / 锥连发):与效果侧共用 ShapeLabel /
            // ShapeSuffix,措辞因此天然一致 —— 此前这三只召唤物的形状卡面上一个字都没有,
            // 剑(血48攻64,横扫整排)与一只同数值的单体召唤物在卡面上完全无法区分。
            // 与 Ranged 正交:那条管越不越得过前排,这条管一次打几个(SummonPassive.Shape 的注释)
            if (p.Shape != TargetShape.Single)
                parts.Add(ShapeLabel(p.Shape) + ShapeSuffix(p.Shape, p.ShapePercent, p.Shots));
            if (p.Taunt) parts.Add(Strings.T("char.passive.taunt"));
            // 入场冻结(2026-08-25,藤):写在最后 —— 它不是这只召唤物的持续能力,
            // 而是召唤那一瞬间的一次性效果,读感上收尾比夹在中间清楚
            // 出手冻结 / 出手减速(2026-08-25,藤 / 蕉):这两项**吃卡等级**,
            // 所以走 p 里已缩放好的值 —— 卡面显示的就是这一张卡当前等级的实际数字
            if (p.OnHitFreezeChance > 0)
                parts.Add(Strings.T("char.passive.onhitfreeze",
                    ("chance", p.OnHitFreezeChance), ("turns", System.Math.Max(1, p.OnHitFreezeTurns))));
            if (p.OnHitSlowPercent > 0)
                parts.Add(Strings.T("char.passive.onhitslow",
                    ("value", p.OnHitSlowPercent), ("turns", System.Math.Max(1, p.OnHitSlowTurns))));
            if (p.OnSummonFreeze > 0)
                parts.Add(Strings.T("char.passive.onsummonfreeze", ("value", p.OnSummonFreeze)));
            return parts.Count == 0 ? "" : Strings.T("char.passive.wrap", ("list", string.Join("/", parts)));
        }

        /// <summary>出字瞬间给**全场存活召唤物**各加盾(桂 = 60)。不并进 PassiveText ——
        /// 它作用于出字时已在场的其他召唤物,不是这只召唤物自带的被动。</summary>
        private static string SummonShieldText(EffectDef e) =>
            e.SummonShield > 0 ? Strings.T("char.effect.summonshield", ("value", e.SummonShield)) : "";

        /// <summary>穿透后缀(2026-08-12,E-b4 T3)。口径从「穿甲:无视减伤,额外 +15%」换成
        /// 点数 —— 旧的 +15% 已固化进这三个字的基础值,卡面上的伤害数字自己涨了,
        /// 后缀只剩下真正与防御有关的那一半。破甲与穿透的一句话区分见 spec 第七节:
        /// 破甲削的是目标的甲(削掉就一直是削掉的、队友蹭得到),穿透只是这一击的视角。</summary>
        /// <summary>条件加成后缀(2026-08-25):目标带某状态时这一记翻倍。
        /// 四个条件各一条字符串表 key —— 拼「对 + 状态名 + 目标翻倍」会让翻译者
        /// 拿不到完整句子,与本文件其余文案同口径。</summary>
        private static string DoubleVsText(EffectDef e) => e.DoubleVs switch
        {
            DamageCondition.Burning => Strings.T("char.effect.doublevs.burning"),
            DamageCondition.Bleeding => Strings.T("char.effect.doublevs.bleeding"),
            DamageCondition.Controlled => Strings.T("char.effect.doublevs.controlled"),
            DamageCondition.ArmorBroken => Strings.T("char.effect.doublevs.armorbroken"),
            _ => "",
        };

        private static string PierceText(EffectDef e) =>
            e.Pierce > 0 ? Strings.T("char.effect.piercetext", ("pierce", e.Pierce)) : "";

        /// <summary>目标形状前缀(2026-08-22,spec §7)。Single 沿用原「单体」——87 张既有
        /// DamageSingle 卡面因此逐字节不变。
        ///
        /// 拆出**只吃 TargetShape 的重载**(2026-08-29):召唤物被动也有形状(剑横扫/枪贯穿/
        /// 锥连发),此前 PassiveText 自己不印、这两个函数又只认 EffectDef,三只召唤物的形状
        /// 在卡面上一个字都没有。共用同一张表 = 加新形状时两侧不可能再各自漏。</summary>
        private static string ShapeLabel(EffectDef e) => ShapeLabel(e.Shape);

        private static string ShapeLabel(TargetShape shape) => shape switch
        {
            TargetShape.Sweep => Strings.T("char.shape.sweep"),
            TargetShape.Cleave => Strings.T("char.shape.cleave"),
            TargetShape.Skewer => Strings.T("char.shape.skewer"),
            TargetShape.Volley => Strings.T("char.shape.volley"),
            TargetShape.Chain => Strings.T("char.shape.chain"),
            _ => Strings.T("char.shape.single"),
        };

        /// <summary>目标形状后缀,与 PierceText 同一种「有则挂、无则空」写法。Volley 没有
        /// 「非主目标」的概念——每发都按主目标满额结算(BattleEngine.cs:1291、1519 对 Volley
        /// 都直接跳过 ShapePercent),卡面只报发数;其余形状报 ShapePercent,等于 100(未配置)
        /// 时不写,省字数。
        ///
        /// Chain 单列一支(2026-08-29):它的 ShapePercent 是**逐跳累乘**的衰减率
        /// (第 k 跳 = ShapePercent^k,BattleEngine.ChainPercent),与其余形状「非主目标一次性
        /// 折算」不是一回事,共用「(溅 N%)」会把玩家骗了;跳数也得报,否则 3 跳与 5 跳同面。</summary>
        private static string ShapeSuffix(EffectDef e) => ShapeSuffix(e.Shape, e.ShapePercent, e.Shots);

        /// <summary>≤0 的 percent 兜回 100:EffectDef 在构造里已兜过,SummonPassive 没有
        /// (引擎侧 BattleEngine.cs:1514 每次现兜),漏配时会写出「(溅 0%)」。</summary>
        private static string ShapeSuffix(TargetShape shape, int percent, int shots)
        {
            if (percent <= 0) percent = 100;
            return shape switch
            {
                TargetShape.Volley => Strings.T("char.shape.suffix.volley", ("shots", shots)),
                TargetShape.Chain => Strings.T("char.shape.suffix.chain",
                    ("shots", shots), ("percent", percent)),
                TargetShape.Sweep or TargetShape.Cleave or TargetShape.Skewer when percent != 100
                    => Strings.T("char.shape.suffix.splash", ("percent", percent)),
                _ => "",
            };
        }

        public static string ElementName(Element element) => element switch
        {
            Element.Wood => Strings.T("char.element.wood"),
            Element.Fire => Strings.T("char.element.fire"),
            Element.Earth => Strings.T("char.element.earth"),
            Element.Metal => Strings.T("char.element.metal"),
            Element.Water => Strings.T("char.element.water"),
            Element.Heart => Strings.T("char.element.heart"),
            _ => Strings.T("char.element.unknown"),
        };

        /// <summary>稀有度显示名(与 <see cref="Theme.RarityColor"/> 同一套):
        /// 枚举名 = 皮肤色 = 强度序,视觉层级 白→绿→蓝→紫→金→橙→红。</summary>
        public static string RarityName(CardRarity rarity) => rarity switch
        {
            CardRarity.White => Strings.T("char.rarity.white"),
            CardRarity.Green => Strings.T("char.rarity.green"),
            CardRarity.Blue => Strings.T("char.rarity.blue"),
            CardRarity.Purple => Strings.T("char.rarity.purple"),
            CardRarity.Gold => Strings.T("char.rarity.gold"),
            CardRarity.Orange => Strings.T("char.rarity.orange"),
            CardRarity.Red => Strings.T("char.rarity.red"),
            _ => Strings.T("char.rarity.unknown"),
        };
    }
}
