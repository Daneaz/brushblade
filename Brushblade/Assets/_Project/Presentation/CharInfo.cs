using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <param name="reshapeFace">治疗改形(D2-水 E20):这些效果所在那一面没有伤害、有 HealSelf ——
        /// Reshape 的 shape All 改的是治疗,不是伤害。只有 <see cref="TraitEffectsText"/> 拿得到面的上下文。</param>
        /// <param name="blockBasePercent">BlockMod 印「原为 N%」的原值:该特性解锁时本面折叠后的格挡反击 %
        /// (<see cref="TraitRules.FaceBlockCounterPercent(CharDef, TraitDef, int)"/>);拿不到面上下文时按缺省 30% 印。</param>
        /// <param name="ampAllItems">五行面上「本字效果」实际放大的项(已拼好,如「反击」);null = 攻击面 / 无面上下文,照印「本字效果」。</param>
        private static string OneSideEffectsText(IReadOnlyList<EffectDef> effects, CharDef def, int cardLevel,
            ReshapeFace reshapeFace = ReshapeFace.Damage, int blockBasePercent = BattleConfig.BlockCounterPercent,
            string ampAllItems = null)
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
                var e = effects[i];
                // 空转的改形(治疗面本体已是全体)整句不印,连分号一起省
                if (e.Kind == EffectKind.Reshape && reshapeFace == ReshapeFace.Noop && e.Pick == EffectPick.Primary
                    && e.Shape == TargetArea.All && e.SummonPercent == 100 && e.ArmorShieldPercent <= 0) continue;
                if (parts.Length > 0) parts.Append(';');
                int v = MetaRules.ScaleEffectValue(e.Kind, e.Value, cardLevel);
                // 本体百分比(D2-火 E5,连爆 / 埋雷):数值在出字时才按本面本体解析,没有具体数可印;
                // 只有 DamageSingle / Mine 能写(ConfigLoader 拦其余),这两个分支各走自己的整句 key,
                // 不把「本体×N%」塞进「{value}伤」的数字槽(会印成「全体本体×100%伤」)
                string shown = v.ToString();
                int segStart = parts.Length;   // 开局登记要给这一段整体加前缀
                parts.Append(e.Kind switch
                {
                    // 全体(spec v7 §11.6:DamageAll 并入 DamageSingle + All):沿用原「全体N伤」那句,
                    // 卡面逐字不变。必须排在下面那条之前 —— 落进它会印成「{ShapeLabel}N伤」。
                    EffectKind.DamageSingle when e.Shape == TargetArea.All
                        => (e.BodyPercent > 0
                            ? Strings.T("char.effect.damageall.body", ("percent", e.BodyPercent))
                            : Strings.T("char.effect.damageall", ("value", shown)))
                        + DoubleVsText(e)
                        + PierceText(e) + HitCountText(e) + ExecuteText(e) + TrueDamageText(e)
                        + ShapeSuffix(e) + MarkerText(e),
                    EffectKind.DamageSingle => (e.BodyPercent > 0
                            ? Strings.T("char.effect.damagesingle.body", ("shape", ShapeLabel(e)), ("percent", e.BodyPercent))
                            : Strings.T("char.effect.damagesingle", ("shape", ShapeLabel(e)), ("value", shown)))
                        + DoubleVsText(e)
                        + PierceText(e) + HitCountText(e) + ExecuteText(e)
                        + TrueDamageText(e) + ArmorStrikeText(e) + ShapeSuffix(e) + MarkerText(e),
                    EffectKind.BurnSingle => Strings.T("char.effect.burnsingle", ("value", shown)),
                    EffectKind.BurnAll => Strings.T("char.effect.burnall", ("value", shown)),
                    EffectKind.Shield => Strings.T("char.effect.shield", ("value", shown))
                        + ArmorShieldText(e),
                    // 群体护盾(2026-09-05,崩):与 HealAll 同款「(含召唤物)」的措辞 ——
                    // 玩家要知道的是这一份盾不只给自己
                    EffectKind.ShieldAll => Strings.T("char.effect.shieldall", ("value", shown)),
                    EffectKind.BurnPotency => Strings.T("char.effect.burnpotency", ("value", shown)),
                    // 治疗弹射(2026-09-16,水,海/澡):Shape 只在配 Chain 时才有意义,
                    // 缺省 Single 时这两截都要吐空串——不然全体既有治疗字都会平白多出「单体」
                    // 前缀(与 DamageSingle 那份「单体也印」的既有口径不同,这里不能照抄)。
                    // 割取(D2-金 E13):回复量 = 被杀者最大生命 × Value%,百分比不吃等级(读 e.Value)
                    EffectKind.HealSelf when e.OfVictimMaxHp => Strings.T("char.effect.healself.victim", ("value", e.Value)),
                    EffectKind.HealSelf => (e.Shape == TargetArea.Single ? "" : ShapeLabel(e))
                        + Strings.T("char.effect.healself", ("value", shown))
                        + ShapeSuffix(e),
                    // 召唤物字形归位后(2026-08-15)绝大多数字召的就是自己,写成「梅:召1×「梅」」
                    // 纯属绕口;只有召别的字时才点名。数据侧的默认值仍是「木」,不同名照旧显示
                    // 本命追加 / 本命强化(D2-木 E37、D2-0 E8):`Summon 0` 不是一条 0 血召唤,是把本命字段并进本面的本体召唤
                    EffectKind.Summon when e.Value == 0 => Strings.T("char.effect.summon.nature", ("list", PassiveText(e.Passive, bare: true))),
                    EffectKind.Summon => (e.SummonChar == def?.Id
                            ? Strings.T("char.effect.summon.self", ("count", e.SummonCount))
                            : Strings.T("char.effect.summon.other", ("count", e.SummonCount)) + "「" + e.SummonChar + "」") +
                        Strings.T("char.effect.summon.stats",
                            ("hp", shown), ("atk", MetaRules.ScaleByCardLevel(e.SummonAttack, cardLevel)))
                        + SummonPercentText(e) + PassiveText(e.Passive) + SummonShieldText(e) + SummonDefenseText(e),
                    // 流血回合(D2-金 E8):读 turns,缺省 BleedDefaultTurns;不吃等级
                    EffectKind.Bleed => Strings.T("char.effect.bleed", ("value", shown),
                        ("turns", e.Turns > 0 ? e.Turns : BattleEngine.BleedDefaultTurns)),
                    EffectKind.HealAll => Strings.T("char.effect.healall", ("value", shown)),
                    EffectKind.HealOverTime => e.TargetAll
                        ? Strings.T("char.effect.healovertime.all", ("value", shown), ("turns", e.Turns))
                        : Strings.T("char.effect.healovertime.single", ("value", shown), ("turns", e.Turns)),
                    EffectKind.Freeze => Strings.T("char.effect.freeze", ("value", shown)),
                    // 倾盆(D2-水 E18):只续已减速目标的回合
                    EffectKind.Slow when e.Extend => Strings.T("char.effect.slow.extend", ("value", shown)),
                    EffectKind.Slow => Strings.T("char.effect.slow", ("value", shown)),
                    // 减攻(D1 Task 5):Value 是百分点、吃卡等级(shown);回合数读 e.Turns,不吃等级
                    // 炽焰(D2-火 Task 3):附着在灼上的减攻随灼存续,MinBurn > 0 时印门槛
                    EffectKind.Weaken when e.RiderOf == StatusKind.Burn => e.MinBurn > 0
                        ? Strings.T("char.effect.weaken.rider_burn.min", ("value", shown), ("min", e.MinBurn))
                        : Strings.T("char.effect.weaken.rider_burn", ("value", shown)),
                    EffectKind.Weaken => Strings.T("char.effect.weaken", ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    // 种 / 标记(D1 Task 6):Value 吃卡等级(shown),回合不吃。标记 Turns == 0 + 冰缚选择器 = 跟随冻结回合
                    // 淋漓(D2-水 E25):仅在目标减速中触发;缺 turns 时回合数跟随目标的减速
                    EffectKind.Seed when e.WhileSlowed => e.Turns > 0
                        ? Strings.T("char.effect.seed", ("value", shown), ("turns", e.Turns)) + Strings.T("char.effect.seed.whileslowed.suffix")
                        : Strings.T("char.effect.seed.whileslowed", ("value", shown)),
                    EffectKind.Seed => Strings.T("char.effect.seed", ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    EffectKind.Vulnerable => e.Turns <= 0 && e.Pick == EffectPick.FrozenByThisCast
                        ? Strings.T("char.effect.vulnerable.bind", ("value", shown))
                        : Strings.T("char.effect.vulnerable", ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    // 护甲/破甲 2026-09-08 起限时,回合数要印在卡面上 —— 玩家看不到时限
                    // 就会当成本场持久去规划出牌顺序(这两条以前确实是持久的)
                    // 七级浮屠(spec 2026-10-13 §2,perShieldTenth):层数按出字结算后的玩家护盾计,cap 写在 ScaleCap(0 = 不设上限)。
                    // 不印「减伤 N%」:那是单层护甲的折算,叠层后会骗人
                    EffectKind.DefenseBuff when e.PerShieldTenth => e.ScaleCap > 0
                        ? Strings.T("char.effect.defensebuff.pershieldtenth",
                            ("value", shown), ("cap", e.ScaleCap), ("turns", Math.Max(1, e.Turns)))
                        : Strings.T("char.effect.defensebuff.pershieldtenth.nocap",
                            ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    EffectKind.DefenseBuff => Strings.T("char.effect.defensebuff",
                        ("value", shown), ("percent", StatusText.DefenseToReductionPercent(v)),
                        ("turns", Math.Max(1, e.Turns))),
                    EffectKind.Sunder => Strings.T("char.effect.sunder", ("turns", Math.Max(1, e.Turns))),
                    EffectKind.ArmorBreak => Strings.T("char.effect.armorbreak",
                        ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    // 驱散条数不吃卡等级(与 BattleEngine 的 EffectKind.Dispel 分支同口径)——
                    // 用 e.Value 而不是 v:真正的约束是正数条数不能被 ScaleByCardLevel 缩放
                    // (Lv.10 系数 1.9,「驱散 2 条」会被算成 ceil(2×1.9)=4 条,与 Core 实际驱散数不符;
                    // −1 哨兵同样不缩放只是顺带受益,不是单独的理由)
                    EffectKind.Dispel => e.Value < 0
                        ? (e.TargetAll ? Strings.T("char.effect.dispel.all.full") : Strings.T("char.effect.dispel.single.full"))
                        : (e.TargetAll ? Strings.T("char.effect.dispel.all.count", ("count", e.Value)) : Strings.T("char.effect.dispel.single.count", ("count", e.Value))),
                    // 净化(D1 Task 7 起可计数、可 Pick.Self):条数是离散量,读 e.Value
                    // 水大无际(D2-水 E15):玩家 + 全部木灵各清
                    EffectKind.Cleanse when e.Pick == EffectPick.AllAllies => e.Value > 0
                        ? Strings.T("char.effect.cleanse.allies.count", ("count", e.Value))
                        : Strings.T("char.effect.cleanse.allies"),
                    EffectKind.Cleanse => e.Pick == EffectPick.Self
                        ? (e.Value > 0 ? Strings.T("char.effect.cleanse.self.count", ("count", e.Value))
                            : Strings.T("char.effect.cleanse.self"))
                        : (e.Value > 0 ? Strings.T("char.effect.cleanse.count", ("count", e.Value))
                            : Strings.T("char.effect.cleanse")),
                    EffectKind.Immunity => Strings.T("char.effect.immunity", ("value", shown)),
                    // 格挡(spec v7 §3.1):次数是离散量,不吃等级 —— 读 e.Value,不读缩放后的 shown。
                    // D2-金:BlockMod 折叠进来的字段(按战意的次数 / 格挡附带)也印在这一条上 ——
                    // 按战意时引擎忽略 e.Value(BlockCountOf),不能再印「格挡N次」。
                    // 反击 % 不印(2026-10-15 用户):数值格「反击」已列,机制由「格挡」词条解释
                    EffectKind.Block => (e.ScaleBy == ScaleBasis.Morale
                            ? Strings.T("char.effect.block.morale", ("min", e.ScaleMin))
                            : Strings.T("char.effect.block", ("value", e.Value)))
                        + BlockRidersText(e, cardLevel),
                    // 万木回春(D2-木 E43):Value 0 = 全部尸体;ReviveHpPercent > 0 = 按最大生命百分比复活(缺省半血)
                    EffectKind.Revive => e.Value == 0
                        ? (e.ReviveHpPercent > 0
                            ? Strings.T("char.effect.revive.all.pct", ("percent", e.ReviveHpPercent))
                            : Strings.T("char.effect.revive.all"))
                        : e.ReviveHpPercent > 0
                            ? Strings.T("char.effect.revive.pct", ("value", shown), ("percent", e.ReviveHpPercent))
                            : Strings.T("char.effect.revive", ("value", shown)),
                    // 熣(DamageSingle + Blind)曾被读成三段,当时改成空格治标(与 ArmorBreak 的
                    // 「破甲 {shown} 回合」同款);根因已由上面的分号分隔符解决,这里保留空格写法不再动
                    // 烟熏(D1 Task 9):附着在灼上的致盲没有回合数,随灼存续
                    EffectKind.Blind when e.RiderOf == StatusKind.Burn =>
                        Strings.T("char.effect.blind.rider_burn", ("value", shown)),
                    EffectKind.Blind => e.TargetAll
                        ? Strings.T("char.effect.blind.all", ("value", shown), ("turns", e.Turns))
                        : Strings.T("char.effect.blind.single", ("value", shown), ("turns", e.Turns)),
                    EffectKind.Silence => Strings.T("char.effect.silence", ("turns", e.Turns)),
                    EffectKind.Reflect => Strings.T("char.effect.reflect", ("value", shown), ("turns", e.Turns)),
                    EffectKind.BurnNoDecay => Strings.T("char.effect.burnnodecay"),
                    EffectKind.BurnSettleNow => e.KeepStacks
                        ? Strings.T("char.effect.burnsettlenow.keep")
                        : Strings.T("char.effect.burnsettlenow"),
                    // 保留 / 部分引爆(D2-火 G5):惊爆 / 焚天保留 RetainPercent% 层;燥火攻心只引爆 PortionPercent% 层
                    EffectKind.Detonate => e.RetainPercent > 0
                        ? Strings.T("char.effect.detonate.retain", ("percent", e.RetainPercent))
                        : e.PortionPercent < 100
                            ? Strings.T("char.effect.detonate.portion", ("percent", e.PortionPercent))
                            : Strings.T("char.effect.detonate"),
                    // 灼操作族(D2-火 Task 2):百分比 / 层数是离散量,读 e.Value
                    // 上限(CombatCaps.BurnStacks)不印:「灼」词条已写「最多 10 层」
                    EffectKind.BurnScale => Strings.T("char.effect.burnscale",
                        ("mult", (e.Value / 100f).ToString("0.##"))),
                    EffectKind.BurnEqualize => Strings.T("char.effect.burnequalize"),
                    // 灼附着族(D2-火 Task 3):都挂在本字的灼上,灼消失时一并消失。上炎的层数 / 回合是离散量(e.Value / e.Turns)
                    EffectKind.HealBlock => Strings.T("char.effect.healblock"),
                    EffectKind.BurnGrow => Strings.T("char.effect.burngrow", ("value", e.Value), ("turns", Math.Max(1, e.Turns))),
                    EffectKind.BurnHold => Strings.T("char.effect.burnhold"),
                    EffectKind.BurnBurst => Strings.T("char.effect.burnburst"),
                    EffectKind.BurnBacklash => Strings.T("char.effect.burnbacklash"),
                    // 致命(D2-金 J5,割喉):Value = 回合数,不吃等级。斩杀线(常量 30%)与 Boss 分支由「致命」词条解释
                    EffectKind.Doom => Strings.T("char.effect.doom", ("turns", e.Value)),
                    // 战意族(D2-金 Task 4):量吃卡等级(shown)。富甲含木灵(Q15),金气只给玩家
                    EffectKind.MoraleOverflowShield => Strings.T("char.effect.moraleoverflowshield", ("value", shown)),
                    EffectKind.MoraleArmor => Strings.T("char.effect.morale_armor", ("value", shown)),
                    EffectKind.MoraleShield => Strings.T("char.effect.morale_shield", ("value", shown)),
                    // 敌人出手前 / 受击挂点(D2-火 Task 4):埋雷的伤害吃等级与攻击力(出字时定死);回敬的上限是离散次数
                    EffectKind.Mine => e.BodyPercent > 0
                        ? Strings.T("char.effect.mine.body", ("percent", e.BodyPercent))
                        : Strings.T("char.effect.mine", ("value", shown)),
                    EffectKind.Retaliate => RetaliateText(e, def, cardLevel),
                    // 其余单点效果(D2-火 Task 5):追加一击的百分比 / 自损的百分比离散(读 e.Value);解冻 / 揭示不用 Value
                    EffectKind.ExtraStrike => e.PerBurningHit
                        ? Strings.T("char.effect.extrastrike.perburning", ("percent", e.Value))
                        : Strings.T("char.effect.extrastrike", ("percent", e.Value)),
                    EffectKind.Thaw => Strings.T("char.effect.thaw"),
                    EffectKind.SelfCost => Strings.T("char.effect.selfcost", ("value", e.Value)),
                    EffectKind.Reveal => Strings.T("char.effect.reveal"),
                    // 冻结附着族(D2-水 W1):只挂在本字冻上的敌人身上(Boss 的冰滞不算)。伤害吃等级与攻击力(出字时定死),
                    // 写 bodyPercent 时印本体百分比;冰水的回合离散(读 e.Value)
                    EffectKind.FrostBite => e.BodyPercent > 0
                        ? Strings.T("char.effect.frostbite.body", ("percent", e.BodyPercent))
                        : Strings.T("char.effect.frostbite", ("value", shown)),
                    EffectKind.ThawStrike => e.BodyPercent > 0
                        ? Strings.T("char.effect.thawstrike.body", ("percent", e.BodyPercent))
                        : Strings.T("char.effect.thawstrike", ("value", shown)),
                    EffectKind.ThawSlow => Strings.T("char.effect.thawslow", ("turns", e.Value)),
                    // 冷却(D2-水 W2):拍数离散,每 Boss 每场 1 次
                    EffectKind.ChargeDelay => Strings.T("char.effect.chargedelay", ("value", e.Value)),
                    // 拦截族(D2-水 W3 / W4):回合离散(e.Turns);免疫减益的转盾量吃等级(v)
                    EffectKind.BuffBlock => Strings.T("char.effect.buffblock", ("turns", Math.Max(1, e.Turns))),
                    EffectKind.DebuffWard => DebuffWardText(e, v),
                    // 我方受击 / 回合挂点(D2-水 W5 / W6 / W7):百分比 / 冻结回合 / 回合都离散;回合脉冲的载荷逐条印
                    EffectKind.HurtHeal => Strings.T("char.effect.hurtheal", ("turns", Math.Max(1, e.Turns)), ("value", e.Value)),
                    EffectKind.ShieldFrost => Strings.T("char.effect.shieldfrost", ("value", e.Value)),
                    // turns −1 = 本场持续(厚德载物,spec 2026-10-13 §2):不能落进 Max(1, …) 印成「之后 1 回合」
                    EffectKind.TurnPulse => e.Turns < 0
                        ? Strings.T("char.effect.turnpulse.battle",
                            ("list", string.Join("/", e.PerHit.Select(p => OneSideEffectsText(new[] { p }, def, cardLevel)))))
                        : Strings.T("char.effect.turnpulse", ("turns", Math.Max(1, e.Turns)),
                            ("list", string.Join("/", e.PerHit.Select(p => OneSideEffectsText(new[] { p }, def, cardLevel))))),
                    // 不写「(基准 100)」:那是内部常量,玩家不该看见,而且为它多占 2 个字体码位。
                    // 跑图界面的角色栏已经在显示「攻击 N」,+50 对玩家是可解释的增量。
                    EffectKind.Empower => Strings.T("char.effect.empower", ("value", shown), ("turns", e.Turns)),
                    // 补满(D2-金 E9):战意直接设为上限
                    EffectKind.Morale when e.Fill => Strings.T("char.effect.morale.fill"),
                    // 不印「每层攻击 +10%,上限 5 层」(2026-10-15 用户):「战意」词条已有介绍
                    EffectKind.Morale => Strings.T("char.effect.morale", ("stacks", shown)),
                    // ApBoost 不吃卡等级(与 BattleEngine 的 EffectKind.ApBoost 分支同口径:
                    // AP 是节奏/经济不是资源)——用 e.Value 而不是 v
                    EffectKind.ApBoost => Strings.T("char.effect.apboost", ("value", e.Value)),
                    // 暴击倍率不印:「暴击」词条已写「伤害 ×1.5」(改 CritMultiplierPercent 时同步改 term.crit.desc)
                    EffectKind.CritBuff => Strings.T("char.effect.critbuff",
                        ("value", shown), ("turns", e.Turns)),
                    // 与 PierceText 同一套措辞(「无视 N 点护甲」),差别只在存续:那条是本次,这条是本场。
                    // 锐 身上没有伤害效果,PierceText 不会出现,所以这里必须把口径自己说全。
                    EffectKind.PierceBuff => Strings.T("char.effect.piercebuff", ("value", shown)),
                    // 厚积薄发 / 涌泉相报(2026-09-02,水土双方向):清空全部厚/泉,按层数 × value 打全体。
                    // 与 tools/design/gen_char_doc.py 的 desc() 同口径。
                    EffectKind.SpendHeft => Strings.T("char.effect.spendheft", ("value", shown)),
                    EffectKind.SpendWellspring => Strings.T("char.effect.spendwellspring", ("value", shown)),
                    // 魅惑(2026-09-05,花):持续 Turns 回合,Value 不用(与 Silence 同口径,
                    // 都是靠 Turns 而不是 shown 报时长)。
                    EffectKind.Charm => Strings.T("char.effect.charm", ("turns", e.Turns)),
                    // 蓄热(2026-09-16):清空目标灼烧层数,每层转成本场永久的灼烧威力。
                    // 与 Detonate(引爆)的分界:引爆兑现伤害,蓄热只夺层数、不打伤害。
                    EffectKind.Quench => Strings.T("char.effect.quench", ("value", shown)),
                    // 加速/急速(2026-09-16,水):同一条效果两档,按**未缩放的基础值**分档
                    // (Value 50=加速、100=急速)——v/shown 已被 ScaleByCardLevel 抬高,用它判档
                    // 会让满级的「加速」跨过 100 误判成「急速」,这里必须用 e.Value 原始值。
                    EffectKind.Haste => e.Value >= 100
                        ? Strings.T("char.effect.rapid", ("value", shown), ("turns", e.Turns))
                        : Strings.T("char.effect.haste", ("value", shown), ("turns", e.Turns)),
                    // 解封(2026-09-16,水):6 类纯随机重掷,永久,Value 不用(与 Cleanse 同口径)。
                    EffectKind.Unseal => Strings.T("char.effect.unseal"),
                    // 修饰器(D1 Task 3):不是独立效果,印「改了本字什么」。百分点不吃卡等级(shown == e.Value)。
                    EffectKind.Amplify => AmplifyText(e, ampAllItems) + OnlyIfText(e.OnlyIf),
                    EffectKind.Reshape => ReshapeText(e, reshapeFace == ReshapeFace.Heal),
                    // Augment(D1 Task 4):「目标 字段 +N」,不吃卡等级(shown == e.Value)
                    EffectKind.Augment => AugmentText(e),
                    // 格挡修饰器(D2-金 E12):按战意的次数 / 反击百分比覆盖 / 格挡附带都在 BlockModText 里印
                    EffectKind.BlockMod => BlockModText(e, cardLevel, blockBasePercent),
                    // ---- D1 Task 7:我方侧。百分比 / 层数是离散量(shown == e.Value);群疗 / 群盾吃等级 ----
                    EffectKind.DamageCut => Strings.T("char.effect.damagecut", ("value", shown)),
                    // 倍数不印:「反击加倍」词条已写「×2,本回合有效」(数据里 value 都是 100)
                    EffectKind.CounterBoost => Strings.T("char.effect.counterboost"),
                    EffectKind.Endure => e.Pick == EffectPick.SummonedThisCast
                        ? Strings.T("char.effect.endure.summoned") : Strings.T("char.effect.endure"),
                    // 双木(E41 fromBody)/ 丛藻(E47 sprout)
                    EffectKind.SummonSapling => Strings.T("char.effect.summonsapling",
                            ("count", e.SummonCount), ("value", shown))
                        + (e.FromBody ? Strings.T("char.effect.summonsapling.frombody") : "")
                        + (e.Sprout ? Strings.T("char.effect.summonsapling.sprout") : ""),
                    // 沐恩(D2-水 E24):按本次治疗量的百分比,不吃等级(读 e.Value)
                    EffectKind.HealSummons when e.OfHeal => Strings.T("char.effect.healsummons.ofheal", ("value", e.Value)),
                    EffectKind.HealSummons => e.PercentOfMax
                        ? Strings.T("char.effect.healsummons.pct", ("value", shown))
                        : Strings.T("char.effect.healsummons", ("value", shown)),
                    // 碉楼 / 载物(D2-土 E28):按本次护盾量的百分比,不吃等级(读 e.Value)
                    // 厚德载物(spec 2026-10-13 §2,ofPlayerShield):按兑现时的玩家当前护盾,不吃等级(读 e.Value)
                    EffectKind.ShieldSummons when e.OfPlayerShield => Strings.T("char.effect.shieldsummons.ofplayershield", ("value", e.Value)),
                    EffectKind.ShieldSummons when e.OfShield => Strings.T("char.effect.shieldsummons.ofshield", ("value", e.Value)),
                    EffectKind.ShieldSummons => Strings.T("char.effect.shieldsummons", ("value", shown)),
                    // 柘弓(E46):只让本字最强的一只出手;byArmor = 按护甲(CL 要求 byArmor 必配 own)
                    EffectKind.SummonStrike => e.OwnSummons
                        ? (e.ByArmor
                            ? Strings.T("char.effect.summonstrike.own.armor", ("value", shown))
                            : Strings.T("char.effect.summonstrike.own", ("value", shown)))
                        : Strings.T("char.effect.summonstrike", ("value", shown)),
                    EffectKind.ShieldFromHeal => Strings.T("char.effect.shieldfromheal", ("value", shown)),
                    EffectKind.AddWellspring when e.Fill => Strings.T("char.effect.addwellspring.fill"),   // D2-水 E21 泽及四方
                    EffectKind.AddWellspring => Strings.T("char.effect.addwellspring", ("value", shown)),
                    EffectKind.AddHeft => Strings.T("char.effect.addheft", ("value", shown)),
                    // 反震(D1 Task 9):百分比离散(shown == e.Value)
                    EffectKind.ShieldRecoil => Strings.T("char.effect.shieldrecoil", ("value", shown)),
                    // 嘲讽(D2-0 Task 2):Value = 回合数,0 = 本场;按落点分主语
                    EffectKind.Taunt => TauntText(e),
                    // ---- D2-土 Task 5:土专属新机制(措辞贴特性原文)。百分比 / AP 离散,读 e.Value;堡垒的护盾量吃等级(shown) ----
                    // 扣盾(G1 塔倾 / 地裂):100 = 清空
                    EffectKind.ShieldSpend => e.Value >= 100
                        ? Strings.T("char.effect.shieldspend.all")
                        : Strings.T("char.effect.shieldspend", ("value", e.Value)),
                    // 土生金(G8):每 ResourceThresholdValue 点护盾折 1 层战意(向上取整)
                    EffectKind.ShieldToMorale => Strings.T("char.effect.shieldtomorale",
                        ("value", e.Value), ("per", BattleEngine.ResourceThresholdValue)),
                    // 铜墙铁壁(G2):单次伤害上限,本回合
                    EffectKind.HitCap => Strings.T("char.effect.hitcap", ("value", e.Value)),
                    // 堡垒(G3):每次受击后加护盾(出字时定死,同 Shield)
                    EffectKind.HurtShield => Strings.T("char.effect.hurtshield", ("turns", Math.Max(1, e.Turns)), ("value", shown)),
                    // 碎玉(G4)
                    EffectKind.ShieldShatter => Strings.T("char.effect.shieldshatter", ("value", e.Value)),
                    // 粉身碎骨(G5):触发前缀「击杀时:」由 TriggerText 印
                    EffectKind.OverkillSplash => e.Value >= 100
                        ? Strings.T("char.effect.overkillsplash.all")
                        : Strings.T("char.effect.overkillsplash", ("value", e.Value)),
                    // 杜塞(G6):条件后缀「(目标是 Boss 时)」由 OnlyIfText 补
                    EffectKind.SkillCut => Strings.T("char.effect.skillcut", ("value", e.Value)),
                    // 杜门不出(G7):代价,不算减益
                    EffectKind.ApCost => Strings.T("char.effect.apcost", ("value", e.Value)),
                    // ---- D2-木 Task 5:木专属新机制(措辞贴特性原文)。百分比 / 回合离散,读 e.Value ----
                    // 木灵攻击光环(M1 林立 / 擎天 / 桂冠):落点按 pick —— 全部(含之后召出的)/ 出字时在场的其他木灵 / 攻击最高的一只
                    EffectKind.SummonEmpower => Strings.T("char.effect.summonempower", ("value", e.Value), ("turns", Math.Max(1, e.Turns)))
                        + (e.Pick == EffectPick.AllSummons ? Strings.T("char.effect.summonempower.all") : PickText(e.Pick)),
                    EffectKind.SummonThorns => Strings.T("char.effect.summonthorns", ("value", e.Value), ("turns", Math.Max(1, e.Turns))),
                    EffectKind.SummonRanged => Strings.T("char.effect.summonranged"),
                    // 攀附(M7):目标选择器后缀(生命最高的敌人)由下面的通用 PickText 补
                    EffectKind.Bind => Strings.T("char.effect.bind", ("value", e.Value)),
                    // 荆刺(M6):按该敌人的行动次数递减
                    EffectKind.ThornMark => Strings.T("char.effect.thornmark", ("value", e.Value), ("turns", Math.Max(1, e.Turns))),
                    EffectKind.SummonMerge => Strings.T("char.effect.summonmerge"),
                    EffectKind.SummonAct => Strings.T("char.effect.summonact"),
                    // 折桂(M10):触发前缀「击杀时:」由 TriggerText 印,每场次数由 battleLimit 印
                    EffectKind.GrantChar => Strings.T("char.effect.grantchar", ("value", e.Value)),
                    // spec A D8:沐霖 / 冰肌,Value 是百分比(不随卡等级缩放)
                    EffectKind.Lifesteal => Strings.T("char.effect.lifesteal", ("value", e.Value)),
                    EffectKind.OverhealShield => Strings.T("char.effect.overhealshield", ("value", e.Value)),
                    EffectKind.BlockGain => Strings.T("char.effect.blockgain", ("value", e.Value)),
                    EffectKind.ReviveShield => Strings.T("char.effect.reviveshield", ("value", e.Value)),
                    // spec C §5:燃意 —— 攻击 % 吃卡等级(shown),回合不吃
                    EffectKind.Kindle => e.Pick == EffectPick.AllAllies
                        ? Strings.T("char.effect.kindle.allies", ("value", shown), ("turns", Math.Max(1, e.Turns)))
                        : Strings.T("char.effect.kindle", ("value", shown), ("turns", Math.Max(1, e.Turns))),
                    // spec C §4:换属性,元素取本字所属系
                    EffectKind.Attune => Strings.T("char.effect.attune", ("element", ElementName(def?.Element ?? Element.Heart))),
                    EffectKind.SpendHeal => Strings.T("char.effect.spendheal", ("value", e.Value)),
                    EffectKind.SpendShield => Strings.T("char.effect.spendshield", ("value", e.Value)),
                    _ => e.Kind.ToString(),
                });
                // 敌方侧效果的目标选择器与条件门后缀(D1 Task 5);Amplify 的条件门已在它自己的分支里印
                // 冻结附着(D2-水 W1)的句子本身已点名「被本字冻结的敌人 / 本字的冻结」,不再补选择器后缀(会印成两遍)
                if (EffectPickRules.Supports(e.Kind) && !NamesOwnTarget(e.Kind))
                    parts.Append(PickText(e.Pick) + OnlyIfText(e.OnlyIf));
                else if (EffectPickRules.Supports(e.Kind))
                    parts.Append(OnlyIfText(e.OnlyIf));
                // 计数缩放(D2-火 N4):Amplify 的百分点 / HealSelf 的回复量 × 计数
                parts.Append(ScaleText(e));
                if (e.MinSpent > 0) parts.Append(Strings.T("char.effect.minspent", ("min", e.MinSpent)));   // spec C §6 释放门槛
                // 每击附带(D2-火 N4b):子效果逐条印,斜杠分隔(分号已是外层分隔符)
                if (e.PerHit.Count > 0 && e.Kind != EffectKind.Retaliate && e.Kind != EffectKind.TurnPulse)   // 回敬 / 回合脉冲的 perHit 段已印在它自己的文案里
                {
                    string list = string.Join("/", e.PerHit.Select(p => OneSideEffectsText(new[] { p }, def, cardLevel)));
                    parts.Append(e.PerHitFrom > 1
                        ? Strings.T("char.effect.perhit.from", ("from", e.PerHitFrom), ("list", list))
                        : Strings.T("char.effect.perhit", ("list", list)));
                }
                // 开局登记(D2-火 N12):这条本场不执行,之后 N 场开局对全场结算 —— 整段前缀「跨场:下 N 场开局:」,
                // 不能让玩家把后面的效果读成本场就生效。5 场是橙档的「跨段」(spec §5.1,词条 C-Q2),单用一条文案
                if (e.OpeningBattles == BandBattles)
                    parts.Insert(segStart, Strings.T("char.effect.opening.band", ("battles", e.OpeningBattles)));
                else if (e.OpeningBattles > 0)
                    parts.Insert(segStart, Strings.T("char.effect.opening", ("battles", e.OpeningBattles)));
            }
            return parts.ToString();
        }

        /// <summary>跨段 = 持续之后 5 场(spec §3.3 / §5.1;词条「跨段」的说明写的也是 5)。</summary>
        private const int BandBattles = 5;

        /// <summary>一条特性的整句效果文案(D2-火 N13):效果逐条印,带 <see cref="TraitDef.MaxPerCast"/>(limit)时补「每次出字最多触发 N 次」。
        /// 特性详情页(Plan E)的入口;卡面主句仍走 <see cref="EffectsText"/>。</summary>
        /// <param name="face">两面通用(Both)的特性印在哪一面;五行面上「本字效果」改印具体项(2026-10-15 用户)。null = 按特性自己的面。</param>
        public static string TraitEffectsText(TraitDef trait, CharDef def, int cardLevel, CardFace? face = null) =>
            TriggerText(trait.Trigger)
            + OneSideEffectsText(trait.Effects, def, cardLevel, ReshapeFaceOf(trait, def, cardLevel),
                def != null && trait.Effects.Any(e => e.Kind == EffectKind.BlockMod)
                    ? TraitRules.FaceBlockCounterPercent(def, trait, cardLevel) : BattleConfig.BlockCounterPercent,
                FeatureAmpItemsText(trait, def, cardLevel, face))
            + (trait.MaxPerCast > 0 ? Strings.T("char.trait.limit", ("count", trait.MaxPerCast)) : "")
            + (trait.MaxPerBattle > 0 ? Strings.T("char.trait.battlelimit", ("count", trait.MaxPerBattle)) : "");   // D2-土 E35

        /// <summary>五行面上的 Amplify All 印成具体项(「反击」「治疗」「木灵生命与攻击」…,<see cref="CardSheetRules.FeatureAmpItems"/>);
        /// 攻击面、无 Amplify All、取不到项时 null(照印「本字效果」)。</summary>
        private static string FeatureAmpItemsText(TraitDef trait, CharDef def, int cardLevel, CardFace? face)
        {
            bool feature = trait.Face == TraitFace.Feature || (trait.Face == TraitFace.Both && face == CardFace.Feature);
            if (def == null || !feature || !trait.Effects.Any(e => e.Kind == EffectKind.Amplify && e.Scope == AmpScope.All))
                return null;
            var items = CardSheetRules.FeatureAmpItems(def, cardLevel);
            return items.Count == 0 ? null
                : string.Join(Strings.T("char.effect.amplify.item_sep"), items.Select(AmpItemText));
        }

        private static string AmpItemText(AmpItem item) => item switch
        {
            AmpItem.Damage => Strings.T("char.amp_item.damage"),
            AmpItem.Heal => Strings.T("char.amp_item.heal"),
            AmpItem.Shield => Strings.T("char.amp_item.shield"),
            AmpItem.Counter => Strings.T("char.amp_item.counter"),
            AmpItem.Seed => Strings.T("char.amp_item.seed"),
            AmpItem.Burn => Strings.T("char.amp_item.burn"),
            AmpItem.Summon => Strings.T("char.amp_item.summon"),
            AmpItem.Kindle => Strings.T("char.amp_item.kindle"),
            _ => item.ToString(),
        };

        private enum ReshapeFace { Damage, Heal, Noop }

        /// <summary>这条特性所在那一面「没有伤害、有治疗」(D2-水 E20,海纳百川 / 细雨 / 泽被…):Reshape shape All 改的是第一条 HealSelf。
        /// 判据与 <c>TraitRules.ApplyReshape</c> 同口径,读的是该面折叠前的本体效果(折叠后已含本条 Reshape,HoT 会被改成全体而误判)。两面通用(Both)的特性按润面判:
        /// 双方向字的攻面恒有伤害,改的是伤害,印成「伤害改为」;单方向字只有一面,与润面同一份效果。</summary>
        private static ReshapeFace ReshapeFaceOf(TraitDef trait, CharDef def, int cardLevel)
        {
            if (def == null || !trait.Effects.Any(e => e.Kind == EffectKind.Reshape)) return ReshapeFace.Damage;
            if (trait.Face == TraitFace.Both && def.AttackEffects.Count > 0) return ReshapeFace.Damage;
            // 读折叠**前**的面本体:折叠已含本条 Reshape 时,HoT 早被改成全体,判据会误判成空转
            var cast = trait.Face == TraitFace.Attack ? def.AttackEffects : def.Effects;
            if (TraitRules.HealReshapeIndex(cast) >= 0) return ReshapeFace.Heal;
            // 无伤害、已是 HealAll(㵘):改形空转,这句不印
            return !cast.Any(e => e.Kind == EffectKind.DamageSingle) && cast.Any(e => e.Kind == EffectKind.HealAll)
                ? ReshapeFace.Noop : ReshapeFace.Damage;
        }

        /// <summary>冻结附着族的句子自带主语(「被本字冻结的敌人…」「本字的冻结结束时…」),选择器后缀不再印。</summary>
        private static bool NamesOwnTarget(EffectKind kind) =>
            kind == EffectKind.FrostBite || kind == EffectKind.ThawStrike || kind == EffectKind.ThawSlow;

        /// <summary>挂在状态上的效果载荷(受击回敬 / 回合脉冲的 <see cref="StatusEffect.OnHit"/>)的人话,斜杠分隔(同卡面 perHit)。
        /// 载荷存的是**未缩放**值,结算时按来源字等级缩放 —— <paramref name="cardLevel"/> 传来源字的等级,印的就是实际结算量。
        /// 载荷白名单(ConfigLoader)不含召唤,用不到字定义,传 null。</summary>
        public static string DetachedEffectsText(IReadOnlyList<OpeningEffect> payload, int cardLevel) =>
            payload == null ? "" :
            string.Join("/", payload.Select(o => OneSideEffectsText(new[] { o.ToEffect() }, null, cardLevel)));

        /// <summary>特性的结算时机前缀(D2-金 J3 起):出字时(Cast)不印;被动反应各一条完整 key。</summary>
        public static string TriggerText(TraitTrigger trigger) => trigger switch
        {
            TraitTrigger.OnCrit => Strings.T("char.trait.trigger.crit"),
            TraitTrigger.OnKill => Strings.T("char.trait.trigger.kill"),
            TraitTrigger.OnExecute => Strings.T("char.trait.trigger.execute"),
            TraitTrigger.OnCompose => Strings.T("char.trait.trigger.compose"),
            TraitTrigger.OnDismantle => Strings.T("char.trait.trigger.dismantle"),
            _ => "",
        };

        /// <summary>受击回敬(D2-火 Task 4):回敬的效果逐条印(斜杠分隔,同每击附带);Value &gt; 0 时印每回合上限。</summary>
        private static string RetaliateText(EffectDef e, CharDef def, int cardLevel)
        {
            string list = string.Join("/", e.PerHit.Select(p => OneSideEffectsText(new[] { p }, def, cardLevel)));
            // 山岳(D2-土 E32):护甲门槛,句首补「护甲 ≥ N 时,」
            string gate = e.MinArmor > 0 ? Strings.T("char.effect.retaliate.minarmor", ("armor", e.MinArmor)) : "";
            // 潜流(D2-水 E23):多回合时句首改成「之后 N 回合内」;带上限时补「每回合至多 N 次」(上限按回合计)
            if (e.Turns > 1)
                return gate + Strings.T("char.effect.retaliate.turns", ("turns", e.Turns), ("list", list))
                    + (e.Value > 0 ? Strings.T("char.effect.retaliate.turns.cap", ("cap", e.Value)) : "");
            return gate + (e.Value > 0
                ? Strings.T("char.effect.retaliate.cap", ("list", list), ("cap", e.Value))
                : Strings.T("char.effect.retaliate", ("list", list)));
        }

        /// <summary>免疫减益(D2-水 W4):濯身「N 回合内免疫减益」/ 浇熄「N 回合内免疫灼烧,每挡 1 层转为护盾 X」;
        /// WardCount &gt; 0 时补「(前 N 次)」。只拦一种时状态名取 StatusText(与详情弹窗同一份文案)。</summary>
        private static string DebuffWardText(EffectDef e, int shield)
        {
            int turns = Math.Max(1, e.Turns);
            string text = e.WardOf is StatusKind of
                ? Strings.T("char.effect.debuffward.of", ("turns", turns), ("status", StatusText.Of(of, 0, 0).Name ?? ""))
                : Strings.T("char.effect.debuffward", ("turns", turns));
            if (e.WardCount > 0) text += Strings.T("char.effect.debuffward.count", ("count", e.WardCount));
            // 转盾:灼按层计,其余减益按条计;全量免疫(WardOf null)两种都会挡,两种计法都印
            if (shield > 0)
                text += e.WardOf == StatusKind.Burn ? Strings.T("char.effect.debuffward.shield", ("value", shield))
                    : e.WardOf is null ? Strings.T("char.effect.debuffward.shield.mixed", ("value", shield))
                    : Strings.T("char.effect.debuffward.shield.entry", ("value", shield));
            return text;
        }

        /// <summary>反击增强的倍率(D1 Task 7):Value 100 → 「2」,50 → 「1.5」。</summary>
        /// <summary>嘲讽效果文案(D2-0 Task 2):主语按 pick(自己 / 召出的木灵 / 全部木灵),Value = 回合数,0 = 本场。</summary>
        internal static string TauntText(EffectDef e)
        {
            string who = e.Pick == EffectPick.Self ? Strings.T("char.effect.taunt.self")
                : e.Pick == EffectPick.AllSummons ? Strings.T("char.effect.taunt.all")
                : Strings.T("char.effect.taunt.summoned");
            return e.Value > 0 ? Strings.T("char.effect.taunt.turns", ("who", who), ("turns", e.Value))
                : Strings.T("char.effect.taunt.battle", ("who", who));
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
            (e.ExecuteKills
                ? Strings.T("char.effect.execute.kill", ("percent", e.ExecuteBelowPercent))
                    // 斩杀溅射(D2-金 J4,铡刀落)
                    + (e.ExecuteSplashPercent > 0 ? Strings.T("char.effect.execute.splash", ("percent", e.ExecuteSplashPercent)) : "")
                : Strings.T("char.effect.execute.double", ("percent", e.ExecuteBelowPercent)))
            + OnlyIfText(e.ExecuteIf);   // 湮灭无踪(D2-水 E19):斩杀条件门

        /// <summary>多段后缀(2026-08-23)。每段完全独立:各自过生克、各自减一次护甲,
        /// 也各自过斩杀的「打之前判血」——「第一段把敌人打进阈值、第二段触发处决」是真会
        /// 发生的涌现(EffectDef.HitCount 的注释)。全表只有「剁」用它。</summary>
        private static string HitCountText(EffectDef e) =>
            e.HitCount > 1 ? Strings.T("char.effect.hitcount", ("count", e.HitCount)) : "";

        /// <summary>召唤物被动(2026-08-23)。此前**卡面完全不印**,导致柳(血80/攻30)与
        /// 松(血120/攻30)除血量外毫无区别 —— 而柳实际带 50% 闪避,玩家没法判断该留哪张。
        /// 顺序与 SummonPassive 的字段声明一致,措辞与 gen_char_doc.py 的 PASSIVE 表同口径。</summary>
        /// <param name="bare">true = 只要逗号前的清单本身(本命追加的整句「本命:…」自己带抬头),不套 char.passive.wrap。</param>
        private static string PassiveText(SummonPassive p, bool bare = false)
        {
            if (p == null) return "";
            var parts = new List<string>();
            if (p.Speed > 0) parts.Add(Strings.T("char.passive.speed", ("value", p.Speed)));
            if (p.Thorns > 0) parts.Add(Strings.T("char.passive.thorns", ("value", p.Thorns)));
            if (p.HealAlly > 0) parts.Add(Strings.T("char.passive.healally", ("value", p.HealAlly)));
            // 自愈(2026-09-05,藻):与上面的 HealAlly 是一对,别合并成一句 —— 那个外溢给
            // 全队,这个只回持有者自己,文案上也要分开说清楚。措辞刻意避开「愈」字:那个
            // 字形不在现有字符串表的字符集里,用「自身回血」复用既有字形,免掉一次字体子集重跑。
            if (p.Regen > 0) parts.Add(Strings.T("char.passive.regen", ("value", p.Regen)));
            // 攻击光环(2026-09-05,平衡重做 P0 任务 4):持续加成,含自己 —— 与 HealAlly/
            // Regen 那组「每回合」的被动放在一起,读感上都是「持续生效的场面效果」。
            if (p.AuraAttack > 0) parts.Add(Strings.T("char.passive.auraattack", ("value", p.AuraAttack)));
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
            if (p.Shape != TargetArea.Single)
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
            // 本命新字段(D2-0 Task 6,spec §9 木)
            if (p.BackRowBonusPercent > 0) parts.Add(Strings.T("char.passive.backrowbonus", ("value", p.BackRowBonusPercent)));
            if (p.PerAllyAttackPercent > 0) parts.Add(Strings.T("char.passive.perallyattack", ("value", p.PerAllyAttackPercent)));
            if (p.Armor > 0) parts.Add(Strings.T("char.passive.armor", ("value", p.Armor)));
            if (p.HealAllyTimes > 1) parts.Add(Strings.T("char.passive.healallytimes", ("value", p.HealAllyTimes)));
            if (p.SproutPercent > 0 && p.SproutMax > 0)
                parts.Add(Strings.T("char.passive.sprout", ("value", p.SproutPercent), ("max", p.SproutMax)));
            if (p.EntrySaplings > 0) parts.Add(Strings.T("char.passive.entrysaplings", ("value", p.EntrySaplings)));
            if (p.OnHitCharmChance > 0) parts.Add(Strings.T("char.passive.onhitcharm", ("chance", p.OnHitCharmChance)));
            // 本命新字段(D2-木,SummonInfoNature 稿的说明句;召唤物详情右栏同读这些 key)
            parts.AddRange(WoodNatureLines(p));
            if (bare) return string.Join("/", parts);
            return parts.Count == 0 ? "" : Strings.T("char.passive.wrap", ("list", string.Join("/", parts)));
        }

        /// <summary>木系五行面 Lv1 本命行的正文(spec B §1.4):本命被动的裸句,斜杠分隔。</summary>
        internal static string NatureText(SummonPassive p) => PassiveText(p, bare: true);

        /// <summary>D2-木 的 14 个本命字段,各一句(SummonInfoNature 稿「13 个新字段的说明句」,林荫的上限并在同一句)。
        /// 卡面 <see cref="PassiveText"/> 与召唤物详情右栏(<c>SummonInfo</c>)共用 —— 两处说的是同一件事,只有一份文案。
        /// 顺序与 <see cref="SummonPassive"/> 的字段声明一致。CardFaceCoverageTests 把 PassiveText 与本方法的方法体
        /// 合在一起扫(本方法只由 PassiveText 与召唤物详情调用),新字段写在这里同样算「已渲染」。</summary>
        internal static List<string> WoodNatureLines(SummonPassive p) => WoodNatureLines(p, null);

        /// <param name="onto">非 null 时逐条回调 (字段键, 句子),供召唤物详情按 chip 优先级重排;键见 <see cref="NatureKey"/>。</param>
        internal static List<string> WoodNatureLines(SummonPassive p, Action<NatureKey, string> onto)
        {
            var lines = new List<string>();
            void Add(NatureKey key, string text) { lines.Add(text); onto?.Invoke(key, text); }
            if (p == null) return lines;
            if (p.AttackPercent > 0) Add(NatureKey.AttackPercent, Strings.T("char.passive.attackpercent", ("value", p.AttackPercent)));
            if (p.HealAllyPercent > 0) Add(NatureKey.HealAllyPercent, Strings.T("char.passive.healallypercent", ("value", p.HealAllyPercent)));
            if (p.HealMirror > 0)
                Add(NatureKey.HealMirror, p.HealMirror == 100
                    ? Strings.T("char.passive.healmirror")
                    : Strings.T("char.passive.healmirror.pct", ("value", p.HealMirror)));
            if (p.ThornsBleed > 0) Add(NatureKey.ThornsBleed, Strings.T("char.passive.thornsbleed", ("value", p.ThornsBleed)));
            if (p.CritAura > 0) Add(NatureKey.CritAura, Strings.T("char.passive.crit_aura", ("value", p.CritAura)));
            if (p.GroveCut > 0)
                Add(NatureKey.GroveCut, p.GroveCutCap > 0
                    ? Strings.T("char.passive.grovecut.cap", ("value", p.GroveCut), ("cap", p.GroveCutCap))
                    : Strings.T("char.passive.grovecut", ("value", p.GroveCut)));
            if (p.ThicketCut > 0)
                Add(NatureKey.ThicketCut, Strings.T("char.passive.thicketcut", ("value", p.ThicketCut), ("min", p.ThicketMin)));
            if (p.SelfCut > 0) Add(NatureKey.SelfCut, Strings.T("char.passive.selfcut", ("value", p.SelfCut)));
            if (p.Harden > 0)
                Add(NatureKey.Harden, Strings.T("char.passive.harden", ("value", p.Harden), ("cap", p.HardenMax)));
            if (p.ReturnFire > 0) Add(NatureKey.ReturnFire, Strings.T("char.passive.returnfire", ("value", p.ReturnFire)));
            if (p.DeathBlast > 0)
                Add(NatureKey.DeathBlast, Strings.T("char.passive.deathblast", ("mult", (p.DeathBlast / 100f).ToString("0.##"))));
            if (p.SproutDeathSlow > 0) Add(NatureKey.SproutDeathSlow, Strings.T("char.passive.sproutdeathslow", ("value", p.SproutDeathSlow)));
            if (p.DeathPart) Add(NatureKey.DeathPart, Strings.T("char.passive.deathpart"));
            return lines;
        }

        /// <summary>D2-木 本命字段的标识(召唤物详情按战场 chip 优先级排序用)。</summary>
        internal enum NatureKey
        {
            AttackPercent, HealAllyPercent, HealMirror, ThornsBleed, CritAura, GroveCut, ThicketCut,
            Harden, ReturnFire, DeathBlast, SproutDeathSlow, DeathPart, SelfCut,
        }

        /// <summary>特性名(游戏数据,不进字符串表):<paramref name="charId"/> 的特性表里第一条含满足 <paramref name="match"/>
        /// 的效果的特性名。光环 / 魅惑这类状态只记来源字、不记是哪条特性挂的,详情与飘字靠它把「林立」「移花接木」翻出来。
        /// 取不到(graph 为空、字不在图谱、没有这条特性)返回 null,调用方退回不带名的文案。
        /// <paramref name="charId"/> 为 null = 整张图谱里找第一条(绿潮 / 折桂这类每个效果只由一条特性给的)。</summary>
        internal static string TraitNameOf(RecipeGraph graph, string charId, Func<EffectDef, bool> match) =>
            TraitCueOf(graph, charId, match)?.Trait.Name;

        /// <summary>同 <see cref="TraitNameOf"/>,但连字带特性一起返回(Plan E4 TriggerFx:名签要字形色与面印)。</summary>
        internal static TraitCue? TraitCueOf(RecipeGraph graph, string charId, Func<EffectDef, bool> match)
        {
            if (graph == null) return null;
            if (charId == null)
            {
                foreach (var any in graph.All)
                    foreach (var t in any.Traits)
                        if (t.Effects.Any(match)) return new TraitCue(any, t);
                return null;
            }
            if (!graph.TryGet(charId, out var def)) return null;
            foreach (var t in def.Traits)
                if (t.Effects.Any(match)) return new TraitCue(def, t);
            return null;
        }

        /// <summary>本命特性名(D2-木):整张图谱里第一条「`Summon 0` 本命追加、本命满足 <paramref name="has"/>」的特性名
        /// (磐固、林荫、蟾宫…)。本命会随嫁接换到别的字的木灵身上,所以不按木灵的来源字查,按字段查 —— 每个木系本命字段
        /// 只由一条特性给。取不到返回 null。</summary>
        internal static string NatureTraitName(RecipeGraph graph, Func<SummonPassive, bool> has) =>
            NatureTraitCue(graph, has)?.Trait.Name;

        /// <summary>同 <see cref="NatureTraitName"/>,连字带特性一起返回(Plan E4 TriggerFx)。</summary>
        internal static TraitCue? NatureTraitCue(RecipeGraph graph, Func<SummonPassive, bool> has)
        {
            if (graph == null) return null;
            foreach (var def in graph.All)
                foreach (var t in def.Traits)
                    foreach (var e in t.Effects)
                        if (e.Kind == EffectKind.Summon && e.Value == 0 && e.Passive != null && has(e.Passive))
                            return new TraitCue(def, t);
            return null;
        }

        /// <summary>召唤改形(D2-木 E48,林立 `summonPercent`):召出的木灵血 / 攻 × N%。缺省 100 不印。</summary>
        private static string SummonPercentText(EffectDef e) =>
            e.SummonPercent != 100 ? Strings.T("char.effect.summon.percent", ("percent", e.SummonPercent)) : "";

        /// <summary>出字瞬间给**全场存活召唤物**各加盾(桂 = 60)。不并进 PassiveText ——
        /// 它作用于出字时已在场的其他召唤物,不是这只召唤物自带的被动。</summary>
        private static string SummonShieldText(EffectDef e) =>
            e.SummonShield > 0 ? Strings.T("char.effect.summonshield", ("value", e.SummonShield)) : "";

        /// <summary>召唤物**入场自带**的护甲(2026-09-08,塔 = 7)。与 SummonShieldText 分开:
        /// 那条发给全场已在场的召唤物,这条只是新召出这几只自己的属性。
        /// 也不并进 PassiveText —— 它不走 SummonPassive,而是往召唤物状态袋里挂 DefenseBuff。</summary>
        private static string SummonDefenseText(EffectDef e) =>
            e.SummonDefense > 0 ? Strings.T("char.effect.summondefense", ("value", e.SummonDefense),
                ("percent", StatusText.DefenseToReductionPercent(e.SummonDefense))) : "";

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

        /// <summary>Amplify 的作用范围 + 百分点(D1 Task 3)。每个范围一条完整句子的 key。</summary>
        private static string AmplifyText(EffectDef e, string ampAllItems) => e.Scope switch
        {
            AmpScope.All when ampAllItems != null => Strings.T("char.effect.amplify.items", ("items", ampAllItems), ("value", e.Value)),
            AmpScope.Heal => Strings.T("char.effect.amplify.heal", ("value", e.Value)),
            AmpScope.Shield => Strings.T("char.effect.amplify.shield", ("value", e.Value)),
            AmpScope.Seed => Strings.T("char.effect.amplify.seed", ("value", e.Value)),
            AmpScope.Counter => Strings.T("char.effect.amplify.counter", ("value", e.Value)),
            AmpScope.All => Strings.T("char.effect.amplify.all", ("value", e.Value)),
            AmpScope.Burn => Strings.T("char.effect.amplify.burn", ("value", e.Value)),
            AmpScope.Spend => Strings.T("char.effect.amplify.spend", ("value", e.Value)),
            _ => Strings.T("char.effect.amplify.damage", ("value", e.Value)),
        };

        /// <summary>目标选择器后缀(D1 Task 5,e.Pick)。Primary 不印;旧 TargetAll 标志有自己的「全体」文案,不走这里。</summary>
        private static string PickText(EffectPick pick) => pick switch
        {
            EffectPick.All => Strings.T("char.effect.pick.all"),
            EffectPick.Random => Strings.T("char.effect.pick.random"),
            EffectPick.HitTargets => Strings.T("char.effect.pick.hittargets"),
            EffectPick.MostBurn => Strings.T("char.effect.pick.mostburn"),
            EffectPick.FrozenByThisCast => Strings.T("char.effect.pick.frozen"),
            EffectPick.Row => Strings.T("char.effect.pick.row"),
            EffectPick.Adjacent => Strings.T("char.effect.pick.adjacent"),
            EffectPick.BurnedByThisCast => Strings.T("char.effect.pick.burnedbythiscast"),
            // D2-水 Task 1(附录 E15)
            EffectPick.Column => Strings.T("char.effect.pick.column"),
            EffectPick.AdjacentOne => Strings.T("char.effect.pick.adjacentone"),
            EffectPick.HighestHp => Strings.T("char.effect.pick.highesthp"),
            EffectPick.SlowedByThisCast => Strings.T("char.effect.pick.slowed"),
            // D2-木 M1(只给 SummonEmpower)
            EffectPick.OtherSummons => Strings.T("char.effect.pick.othersummons"),
            EffectPick.StrongestSummon => Strings.T("char.effect.pick.strongestsummon"),
            _ => "",
        };

        /// <summary>条件门后缀(D1 Task 3,e.OnlyIf)。与 DoubleVsText 一样每个条件一条 key,
        /// 不拼「对 + 状态名」—— 翻译者要拿到完整句子。</summary>
        private static string OnlyIfText(DamageCondition condition) => condition switch
        {
            DamageCondition.Burning => Strings.T("char.effect.onlyif.burning"),
            DamageCondition.Bleeding => Strings.T("char.effect.onlyif.bleeding"),
            DamageCondition.Controlled => Strings.T("char.effect.onlyif.controlled"),
            DamageCondition.ArmorBroken => Strings.T("char.effect.onlyif.armorbroken"),
            DamageCondition.Slowed => Strings.T("char.effect.onlyif.slowed"),
            DamageCondition.Frozen => Strings.T("char.effect.onlyif.frozen"),
            DamageCondition.TargetHpAbove70 => Strings.T("char.effect.onlyif.targethpabove70"),
            DamageCondition.TargetHpBelow30 => Strings.T("char.effect.onlyif.targethpbelow30"),
            DamageCondition.PlayerHpBelow50 => Strings.T("char.effect.onlyif.playerhpbelow50"),
            DamageCondition.PlayerHasArmor => Strings.T("char.effect.onlyif.playerhasarmor"),
            DamageCondition.FirstCastThisTurn => Strings.T("char.effect.onlyif.firstcastthisturn"),
            DamageCondition.Countering => Strings.T("char.effect.onlyif.countering"),
            DamageCondition.PlayerHpAbove70 => Strings.T("char.effect.onlyif.playerhpabove70"),
            DamageCondition.HasSummon => Strings.T("char.effect.onlyif.hassummon"),
            DamageCondition.MoraleFull => Strings.T("char.effect.onlyif.moralefull"),
            DamageCondition.IsBoss => Strings.T("char.effect.onlyif.isboss"),     // D2-水 E14
            DamageCondition.NotBoss => Strings.T("char.effect.onlyif.notboss"),
            DamageCondition.OwnSummonAlive => Strings.T("char.effect.onlyif.ownsummonalive"),   // D2-木 E50 一叶知秋
            _ => "",
        };

        /// <summary>Augment(D1 Task 4):「{目标}{字段}+{N}」。目标名与字段名各走一条完整 key,
        /// 不拼接动态 key(字符串表检查只认字面量)。</summary>
        private static string AugmentText(EffectDef e)
        {
            // 坚冰(D2-水 E17a):冰滞后退的百分比,单独一句
            if (e.AugmentField == AugmentField.StallPush)
                return Strings.T("char.effect.augment.stallpush", ("value", e.Value));
            // 圭臬(D2-土 E34):护甲持续到战斗结束,Value 不用
            if (e.AugmentField == AugmentField.Battle)
                return Strings.T("char.effect.augment.battle");
            string target = e.AugmentKind switch
            {
                EffectKind.Block => Strings.T("char.effect.augment.target.block"),
                EffectKind.Freeze => Strings.T("char.effect.augment.target.freeze"),
                EffectKind.Slow => Strings.T("char.effect.augment.target.slow"),
                EffectKind.DefenseBuff => Strings.T("char.effect.augment.target.defensebuff"),
                EffectKind.ArmorBreak => Strings.T("char.effect.augment.target.armorbreak"),
                EffectKind.Sunder => Strings.T("char.effect.augment.target.sunder"),
                EffectKind.HealOverTime => Strings.T("char.effect.augment.target.healovertime"),
                EffectKind.HealSelf => Strings.T("char.effect.augment.target.healself"),
                _ => Strings.T("char.effect.augment.target.damage"),
            };
            string field = e.AugmentField switch
            {
                AugmentField.Turns => Strings.T("char.effect.augment.field.turns"),
                AugmentField.Shots => Strings.T("char.effect.augment.field.shots"),
                _ => Strings.T("char.effect.augment.field.count"),
            };
            // 带条件的 Augment(D2-水 E17b,冰冻三尺):条件接在后面
            return Strings.T("char.effect.augment", ("target", target), ("field", field), ("value", e.Value)) + OnlyIfText(e.OnlyIf);
        }

        /// <summary>Reshape(D1 Task 3):「伤害改为 + 形状 + 改动的修饰」。只印 Reshape 上非缺省的字段,
        /// 与引擎 TraitRules.Fold 的覆盖口径一致。</summary>
        private static string ReshapeText(EffectDef e, bool healReshape = false) =>
            e.Pick != EffectPick.Primary
                // 重选目标(D2-火 E3,烈风):本面没有伤害时,落在主目标上的效果改落到选择器上
                ? Strings.T("char.effect.reshape.retarget") + PickText(e.Pick)
                // 召唤改形(D2-木 E48,林立):本面召出的木灵血 / 攻 × N%(只数由同面的 Augment 印)
                : e.SummonPercent != 100
                    ? Strings.T("char.effect.reshape.summonpercent", ("percent", e.SummonPercent))
                // 治疗改形(D2-水 E20):本面没有伤害、shape All → 第一条治疗改为全体友方,每人各治 shapePercent%
                // 护盾面(D2-土 E30):盾量 + 护甲 × N%
                : e.ArmorShieldPercent > 0
                    ? ArmorShieldText(e)
                : healReshape && e.Shape == TargetArea.All
                    ? (e.ShapePercent < 100
                        ? Strings.T("char.effect.reshape.heal.percent", ("percent", e.ShapePercent))
                        : Strings.T("char.effect.reshape.heal"))
                : Strings.T("char.effect.reshape", ("shape", e.Shape == TargetArea.Single ? "" : ShapeLabel(e)))
                    + ShapeSuffix(e) + HitCountText(e) + ExecuteText(e) + ArmorStrikeText(e) + MarkerText(e);

        /// <summary>BlockMod(D2-金 E12):改的是本面的格挡。按战意 →「格挡次数 = 战意(至少 N)」,否则「格挡」;
        /// 反击百分比覆盖带上原值(该特性解锁时本面折叠后 Block 的反击 %,不含本条;没写取 <see cref="BattleConfig.BlockCounterPercent"/>),玩家看得出是「改写」;
        /// D10 只抬不降:改后不高于原值时这句不印。再接格挡附带。</summary>
        private static string BlockModText(EffectDef e, int cardLevel, int basePercent)
        {
            int raised = TraitRules.BlockModCounterPercent(basePercent, e);
            return (e.ScaleBy == ScaleBasis.Morale
                    ? Strings.T("char.effect.block.morale", ("min", e.ScaleMin))
                    : Strings.T("char.effect.blockmod"))
                + (raised > basePercent
                    ? Strings.T("char.effect.blockmod.counter", ("percent", raised), ("base", basePercent))
                    : "")
                + BlockRidersText(e, cardLevel);
        }

        /// <summary>格挡附带(D2-金 J1):BlockMod 与折叠后的 Block 共用。流血量出字时 = 基础 × 卡等级 × 攻击力(定死),
        /// 卡面印等级缩放后的值并注明随攻击力;回合固定 <see cref="BattleEngine.BleedDefaultTurns"/>。</summary>
        private static string BlockRidersText(EffectDef e, int cardLevel) =>
            (e.CounterHits > 1 ? Strings.T("char.effect.blockmod.hits", ("hits", e.CounterHits)) : "")
            + (e.CounterColumn ? Strings.T("char.effect.blockmod.column", ("percent", BattleEngine.CounterColumnPercent)) : "")
            + (e.CounterRow ? Strings.T("char.effect.blockmod.row", ("percent", BattleEngine.CounterRowPercent)) : "")
            + (e.CounterExecuteBelow > 0 ? Strings.T("char.effect.blockmod.execute", ("percent", e.CounterExecuteBelow)) : "")
            + (e.BlockBleed > 0 ? Strings.T("char.effect.blockmod.bleed",
                ("value", MetaRules.ScaleByCardLevel(e.BlockBleed, cardLevel)), ("turns", BattleEngine.BleedDefaultTurns)) : "")
            + (e.BlockMorale > 0 ? Strings.T("char.effect.blockmod.morale", ("value", e.BlockMorale)) : "")
            + (e.KillRefundAp > 0 ? Strings.T("char.effect.blockmod.refund", ("value", e.KillRefundAp)) : "");

        /// <summary>计数缩放后缀(D2-火 N4):「(每 1 层灼烧)」/「(每名带灼烧的敌人)」+ 上限。不缩放时空串。</summary>
        private static string ScaleText(EffectDef e) =>
            e.ScaleBy switch
            {
                ScaleBasis.BurnStack => Strings.T("char.effect.per.burnstack"),
                ScaleBasis.BurningEnemy => Strings.T("char.effect.per.burningenemy"),
                // D2-金 E10:伤害的击数 + 战意 / 战意 × 多命中的敌人数。格挡次数 = 战意由 Block / BlockMod 分支自己印在句首
                ScaleBasis.Morale => e.Kind == EffectKind.Block || e.Kind == EffectKind.BlockMod
                    ? ""
                    : e.Kind == EffectKind.Amplify
                        ? Strings.T("char.effect.per.morale")
                        : Strings.T("char.effect.per.morale.hits"),
                ScaleBasis.ExtraHitTarget => Strings.T("char.effect.per.extrahittarget"),
                // D2-水 E22:洪峰(出字前泉层)/ 濯身(本次清掉的减益条数)
                ScaleBasis.Wellspring => Strings.T("char.effect.per.wellspring"),
                ScaleBasis.Cleansed => Strings.T("char.effect.per.cleansed"),
                ScaleBasis.Spent => Strings.T("char.effect.per.spent"),
                // D2-木 E39(众木 / 林深):出字前的存活木灵数(含幼苗)
                ScaleBasis.SummonCount => Strings.T("char.effect.per.summoncount"),
                // D2-土 E36(地裂):出字前护盾每 ScaleDivisor 点 +1 击,上限写在同一句里(单位是击,不是百分点)
                ScaleBasis.Shield => e.ScaleCap > 0   // cap 0 = 不设上限,不印「最多 +0 击」
                    ? Strings.T("char.effect.per.shield.hits", ("divisor", e.ScaleDivisor), ("cap", e.ScaleCap))
                    : Strings.T("char.effect.per.shield.hits.nocap", ("divisor", e.ScaleDivisor)),
                _ => "",
            } + (e.ScaleBy != ScaleBasis.None && e.ScaleBy != ScaleBasis.Shield && e.ScaleCap > 0
                ? Strings.T("char.effect.per.cap", ("cap", e.ScaleCap)) : "");

        /// <summary>伤害标记后缀(D1 Task 3):每段百分比 / 必暴 / 无视 N% 护甲 / 按护盾加伤。缺省全空。
        /// D2-火 N4b:散射每发百分比。</summary>
        private static string MarkerText(EffectDef e) =>
            (e.HitPercent != 100 ? Strings.T("char.effect.hitpercent", ("percent", e.HitPercent)) : "")
            + (e.ShotPercent != 100 ? Strings.T("char.effect.shotpercent", ("percent", e.ShotPercent)) : "")
            + (e.ForceCrit ? Strings.T("char.effect.forcecrit") : "")
            + (e.ArmorIgnorePercent > 0 ? Strings.T("char.effect.armorignore", ("percent", e.ArmorIgnorePercent)) : "")
            + (e.ShieldStrikePercent > 0 ? Strings.T("char.effect.shieldstrike", ("percent", e.ShieldStrikePercent)) : "")
            // D2-木 E40:按木灵攻击 / 本字木灵攻击 / 本字木灵护甲加伤(森罗 / 撼天 / 硬弓),加在每个主目标的第一段
            + (e.SummonAtkStrikePercent > 0 ? Strings.T("char.effect.summonatkstrike", ("percent", e.SummonAtkStrikePercent)) : "")
            + (e.HostAtkStrikePercent > 0 ? Strings.T("char.effect.hostatkstrike", ("percent", e.HostAtkStrikePercent)) : "")
            + (e.HostArmorStrikePercent > 0 ? Strings.T("char.effect.hostarmorstrike", ("percent", e.HostArmorStrikePercent)) : "");

        private static string PierceText(EffectDef e) =>
            e.Pierce > 0 ? Strings.T("char.effect.piercetext", ("pierce", e.Pierce)) : "";

        /// <summary>碾后缀(2026-09-16,土):与 <see cref="PierceText"/> 是同族的伤害修饰,
        /// 但两者**是两档**——穿透削一部分甲值再算 DR,碾直接跳过整条 DR
        /// (<see cref="EffectDef.TrueDamage"/> 的注释)。单体/AOE 两种伤害都能挂,
        /// 与只作用于单体的 <see cref="ArmorStrikeText"/> 不同。</summary>
        private static string TrueDamageText(EffectDef e) =>
            e.TrueDamage ? Strings.T("char.effect.truedamage") : "";

        /// <summary>镇压后缀(2026-09-16,土):与碾/穿透同构的伤害修饰,只挂在
        /// <see cref="EffectKind.DamageSingle"/> 上 —— spec §5 的镇压字(塔/壁/圭)全是单体。
        /// 数值读的是 <see cref="EffectDef.ArmorStrikePercent"/> 本身(百分比),不吃卡等级 ——
        /// 与 <see cref="EffectDef.Pierce"/>/<see cref="EffectDef.TrueDamage"/> 同款:
        /// 这一档由玩家局内的护甲现算,不是随等级成长的基础值。</summary>
        private static string ArmorStrikeText(EffectDef e) =>
            e.ArmorStrikePercent > 0
                ? Strings.T("char.effect.armorstrike", ("percent", e.ArmorStrikePercent))
                    + (e.ArmorStrikeEach ? Strings.T("char.effect.armorstrike.each") : "")   // D2-土 E31 三山
                : "";

        /// <summary>按护甲加盾后缀(D2-土 E30,塔基固面):百分比不吃卡等级,护甲取结算那一刻。</summary>
        private static string ArmorShieldText(EffectDef e) =>
            e.ArmorShieldPercent > 0 ? Strings.T("char.effect.armorshield", ("percent", e.ArmorShieldPercent)) : "";

        /// <summary>目标形状前缀(2026-08-22,spec §7)。Single 沿用原「单体」——87 张既有
        /// DamageSingle 卡面因此逐字节不变。
        ///
        /// 拆出**只吃 TargetArea 的重载**(2026-08-29):召唤物被动也有形状(剑横扫/枪贯穿/
        /// 锥连发),此前 PassiveText 自己不印、这两个函数又只认 EffectDef,三只召唤物的形状
        /// 在卡面上一个字都没有。共用同一张表 = 加新形状时两侧不可能再各自漏。</summary>
        private static string ShapeLabel(EffectDef e) => ShapeLabel(e.Shape);

        internal static string ShapeLabel(TargetArea shape) => shape switch
        {
            TargetArea.Row => Strings.T("char.shape.sweep"),
            TargetArea.Adjacent => Strings.T("char.shape.cleave"),
            TargetArea.Column => Strings.T("char.shape.skewer"),
            TargetArea.Scatter => Strings.T("char.shape.volley"),
            TargetArea.Chain => Strings.T("char.shape.chain"),
            TargetArea.All => Strings.T("char.shape.all"),
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
        private static string ShapeSuffix(TargetArea shape, int percent, int shots)
        {
            if (percent <= 0) percent = 100;
            return shape switch
            {
                TargetArea.Scatter => Strings.T("char.shape.suffix.volley", ("shots", shots)),
                TargetArea.Chain => Strings.T("char.shape.suffix.chain",
                    ("shots", shots), ("percent", percent)),
                TargetArea.Row or TargetArea.Adjacent or TargetArea.Column when percent != 100
                    => Strings.T("char.shape.suffix.splash", ("percent", percent)),
                // 全体每个目标都按主目标满额结算,没有溅射比例可报;
                // 例外是带百分比的全体(D1 Task 3,怒涛「全体各 60%」):每个目标都打折
                TargetArea.All when percent < 100 => Strings.T("char.shape.suffix.all", ("percent", percent)),
                TargetArea.All => "",
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
