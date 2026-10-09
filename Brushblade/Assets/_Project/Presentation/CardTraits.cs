using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情的「攻击模式」与「特性 · 技能」两段(稿 <c>docs/design/drafts/scenes/CardDetail.dc.html</c>)。
    ///
    /// 此前这两段都压在 <see cref="CharInfo.EffectsText"/> 那一整串里 —— 一句话把打谁、叠几层、
    /// 带什么被动全说完,玩家要在分号之间自己找。现在拆成两张表:
    /// · <see cref="Modes"/> 回答**打谁 / 护谁**(单体攻击 / 全体攻击 / 自身护盾…;召唤物是近战 / 远程);
    /// · <see cref="Of"/> 回答**还带什么**,一条一张小卡:图标 chip + 名 + 一行说明,
    ///   与召唤物 / 敌人详情的 <c>.abil</c> 同款,玩家在三处读到的是同一种东西。
    ///
    /// ⚠ **每个 <see cref="EffectKind"/> 都要有分支**。漏一个的表现是这一条特性在详情里
    /// 凭空消失(不是报错、不是英文枚举名 —— 就是没有),而 Presentation 没有自动化测试。
    /// 兜底分支把枚举名原样印出来,好歹看得见。同理:<see cref="SummonPassive"/> 每加一个字段,
    /// 这里要跟着加一条,否则新被动在卡面上不存在。
    ///
    /// 数值一律过 <see cref="MetaRules.ScaleByCardLevel"/> —— 与战斗结算取同一个函数,
    /// 卡面印的就是这一级真正打出来的数。不吃等级的几项(驱散条数、AP、回合数)照原值,
    /// 与 <see cref="CharInfo"/> 的口径逐条对齐。</summary>
    public static class CardTraits
    {
        /// <summary>一条「攻击模式」。<see cref="Attack"/> = 朱砂(打人)/ 翠玉(护己)。</summary>
        public readonly struct Mode
        {
            public readonly bool Attack;
            public readonly string Name;
            public readonly string Note;   // 形状上的限定,可为空

            public Mode(bool attack, string name, string note = "")
            {
                Attack = attack;
                Name = name;
                Note = note;
            }
        }

        /// <summary>一条特性。<see cref="IconKey"/> 为空时退成纯文字 chip(<see cref="Word"/>)。</summary>
        public readonly struct Trait
        {
            public readonly string IconKey;
            public readonly string Word;
            public readonly string Amount;  // chip 上跟在图标后的数字,可为空
            public readonly string Name;
            public readonly string Desc;

            public Trait(string iconKey, string word, string amount, string name, string desc)
            {
                IconKey = iconKey;
                Word = word;
                Amount = amount;
                Name = name;
                Desc = desc;
            }
        }

        // ================= 攻击模式 =================

        /// <summary>这张字**打谁 / 护谁**。召唤字换成召唤物的近战 / 远程 ——
        /// 召唤字自己不打人,问「单体还是全体」没有意义,该问的是那几只够不够得着后排。</summary>
        public static List<Mode> Modes(CharDef def)
        {
            var modes = new List<Mode>();
            var seen = new HashSet<string>();

            var summon = FindSummon(def);
            if (summon != null)
            {
                var passive = summon.Passive;
                bool ranged = passive != null && passive.Ranged;
                // 近战 / 远程本身已经把「够不够得着后排」说完了,行尾不再补一句同义的小注。
                // 与 StatusText.OfRange / OfSummonRange 的 Desc 一并去掉,三处口径统一。
                Add(modes, seen, new Mode(true,
                    ranged ? Strings.T("collection.mode.ranged") : Strings.T("collection.mode.melee")));
                return modes;
            }

            ScanModes(def.AttackEffects, true, modes, seen);
            ScanModes(def.Effects, def.AttackEffects.Count > 0, modes, seen);
            return modes;
        }

        /// <param name="dualSupportSide">双方向字的护面:那一面即使有伤害也归「护」那一侧的颜色?
        /// 不 —— 传进来的是「本次扫的是不是攻面」,伤害仍按伤害算,只有护/治走翠玉。</param>
        private static void ScanModes(IReadOnlyList<EffectDef> effects, bool dualSupportSide,
            List<Mode> modes, HashSet<string> seen)
        {
            foreach (var e in effects)
            {
                switch (e.Kind)
                {
                    // 全体(spec v7 §11.6:DamageAll 并入 DamageSingle + All)仍报「全体攻击」
                    case EffectKind.DamageSingle:
                        Add(modes, seen, new Mode(true, e.Shape == TargetArea.All
                            ? Strings.T("collection.mode.all_attack")
                            : Strings.T("collection.mode.single_attack")));
                        break;
                    case EffectKind.Shield:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_shield"),
                            e.PersistOnce ? Strings.T("collection.mode.note.persist") : ""));
                        break;
                    case EffectKind.ShieldAll:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.all_shield"),
                            e.PersistOnce ? Strings.T("collection.mode.note.persist") : ""));
                        break;
                    case EffectKind.HealSelf:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_heal")));
                        break;
                    case EffectKind.HealAll:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.all_heal")));
                        break;
                    case EffectKind.HealOverTime:
                        Add(modes, seen, new Mode(false, e.TargetAll
                            ? Strings.T("collection.mode.all_hot")
                            : Strings.T("collection.mode.self_hot")));
                        break;
                    case EffectKind.Revive:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.revive")));
                        break;
                    // 纯增益面(2026-09-08):利 / 锋 拆成攻护两面之后,它们的护面**只有** buff
                    // ——而这个 switch 原先只认伤害 / 护盾 / 治疗 / 复活,于是那一面在
                    // 「打谁 / 护谁」这一段里彻底不出现,详情卡上看起来还是单面字
                    // (用户 2026-09-08 实测报的就是这个:两面已经拆了,卡面描述没跟上)。
                    //
                    // 这几条与 BattleEngine.NeedsAllyTarget 的名单同源 —— 那张名单说的正是
                    // 「挂上就真生效、且能指定给玩家或某只召唤物」的增益,拿它当判据不会分叉。
                    case EffectKind.Empower:
                    case EffectKind.CritBuff:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_buff")));
                        break;
                    case EffectKind.DefenseBuff:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_armor")));
                        break;
                    case EffectKind.Immunity:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_immunity")));
                        break;
                    case EffectKind.Block:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_block")));
                        break;
                    case EffectKind.Reflect:
                        Add(modes, seen, new Mode(false, Strings.T("collection.mode.self_reflect")));
                        break;
                    // 火·燃(D1 Task 12,spec v7 §2.1):燃面没有伤害,只有 灼 + 减攻 —— 不补这一支,
                    // 11 张火字的燃面在「打谁 / 护谁」里整面隐形(CardFaceCoverageTests 守)。
                    // 只认 Weaken(燃面专有),不认灼:攻击面也带灼,认灼会给攻击面多印一行。
                    case EffectKind.Weaken:
                        Add(modes, seen, new Mode(true, EffectPickRules.Effective(e) == EffectPick.All
                            ? Strings.T("collection.mode.all_debuff")
                            : Strings.T("collection.mode.single_debuff")));
                        break;
                }
            }
        }

        private static void Add(List<Mode> modes, HashSet<string> seen, Mode mode)
        {
            if (seen.Add(mode.Name)) modes.Add(mode);
        }

        private static string ShapeName(TargetArea shape) => shape switch
        {
            TargetArea.Row => Strings.T("char.shape.sweep"),
            TargetArea.Adjacent => Strings.T("char.shape.cleave"),
            TargetArea.Column => Strings.T("char.shape.skewer"),
            TargetArea.Scatter => Strings.T("char.shape.volley"),
            TargetArea.Chain => Strings.T("char.shape.chain"),
            TargetArea.All => Strings.T("char.shape.all"),
            _ => Strings.T("char.shape.single"),
        };

        private static string ShapeNote(TargetArea shape, int percent, int shots)
        {
            if (percent <= 0) percent = 100;
            return shape switch
            {
                TargetArea.Scatter => Strings.T("char.shape.suffix.volley", ("shots", shots)),
                TargetArea.Chain => Strings.T("char.shape.suffix.chain", ("shots", shots), ("percent", percent)),
                TargetArea.Row or TargetArea.Adjacent or TargetArea.Column when percent != 100
                    => Strings.T("char.shape.suffix.splash", ("percent", percent)),
                _ => "",
            };
        }

        /// <summary>目标形状(贯穿 / 横扫 / 溅射 / 连发 / 弹射)算特性技能,不算攻击模式
        /// (见设计稿 §0,2026-09-06 裁定)。单体不算形状,不建卡。名字与后缀复用
        /// <see cref="ShapeName"/> / <see cref="ShapeNote"/> ——两个渲染器保持不动,这里只是换了个挂载点。</summary>
        private static void AddShapeTrait(List<Trait> traits, TargetArea shape, int percent, int shots)
        {
            if (shape == TargetArea.Single) return;
            // 全体不建形状卡:它在「打谁」那一段已经报成「全体攻击」(ScanModes),
            // 与 DamageAll 时代的卡面一致,这里再挂一张就重复了
            if (shape == TargetArea.All) return;
            var name = ShapeName(shape);
            AddWord(traits, name, name, ShapeNote(shape, percent, shots));
        }

        // ================= 特性 · 技能 =================

        /// <summary>这张字除了「多大」「打谁」之外**还带什么**。召唤字列的是那几只的被动。</summary>
        public static List<Trait> Of(CharDef def, int cardLevel)
        {
            var traits = new List<Trait>();
            var summon = FindSummon(def);
            if (summon != null)
            {
                SummonTraits(traits, summon, cardLevel);
                return traits;
            }
            Scan(traits, def.AttackEffects, cardLevel);
            Scan(traits, def.Effects, cardLevel);
            return traits;
        }

        private static EffectDef FindSummon(CharDef def)
        {
            foreach (var e in def.Effects)
                if (e.Kind == EffectKind.Summon) return e;
            return null;
        }

        private static void Scan(List<Trait> traits, IReadOnlyList<EffectDef> effects, int cardLevel)
        {
            foreach (var e in effects)
            {
                // 带后缀的效果(开局登记 / 每击附带)先扫进自己的临时表、补完后缀再并入(终审 8):直接扫进 traits 的话,
                // 同名同量的 chip 会先被 AddUnique 去重掉,后缀无处可补(如「灼 2」+「灼 2 开局登记」同在一面)
                bool suffixed = e.OpeningBattles > 0 || (e.PerHit.Count > 0 && e.Kind != EffectKind.Retaliate);
                if (!suffixed)
                {
                    ScanOne(traits, e, cardLevel);
                    continue;
                }
                var own = new List<Trait>();
                ScanOne(own, e, cardLevel);
                foreach (var t in own) AddUnique(traits, t);
            }
        }

        private static void ScanOne(List<Trait> traits, EffectDef e, int cardLevel)
        {
            int v = MetaRules.ScaleEffectValue(e.Kind, e.Value, cardLevel);
            int before = traits.Count;   // 本条效果新增的 chip 从这里起(开局登记 / 每击附带的后缀要补到它们的说明上)
            switch (e.Kind)
            {
                // 伤害与护/治本身不是特性 —— 它们的量级在「数值」、去向在「攻击模式」
                case EffectKind.DamageSingle:
                case EffectKind.Shield:
                case EffectKind.ShieldAll:
                case EffectKind.HealSelf:
                case EffectKind.HealAll:
                case EffectKind.HealOverTime:
                case EffectKind.Revive:
                case EffectKind.Summon:
                    break;

                case EffectKind.BurnSingle:
                    AddTrait(traits, "burn", v.ToString(),
                        Strings.T("collection.trait.burn.name"),
                        Strings.T("collection.trait.burn.desc", ("value", v)));
                    break;
                case EffectKind.BurnAll:
                    AddTrait(traits, "burn", v.ToString(),
                        Strings.T("collection.trait.burn_all.name"),
                        Strings.T("collection.trait.burn_all.desc", ("value", v)));
                    break;
                case EffectKind.BurnPotency:
                    AddTrait(traits, "burn", "+" + v,
                        Strings.T("collection.trait.burn_potency.name"),
                        Strings.T("collection.trait.burn_potency.desc", ("value", v)));
                    break;
                case EffectKind.BurnNoDecay:
                    AddTrait(traits, "burn_nodecay", "",
                        Strings.T("collection.trait.burn_nodecay.name"),
                        Strings.T("collection.trait.burn_nodecay.desc"));
                    break;
                case EffectKind.BurnSettleNow:
                    AddTrait(traits, "burn", "",
                        Strings.T("collection.trait.burn_settle.name"),
                        Strings.T("collection.trait.burn_settle.desc"));
                    break;
                case EffectKind.Detonate:
                {
                    // 保留 / 部分引爆(D2-火 G5):惊爆 / 焚天 / 燥火攻心,说明末尾补一句
                    string tail = e.RetainPercent > 0
                        ? Strings.T("collection.trait.detonate.retain", ("percent", e.RetainPercent))
                        : e.PortionPercent < 100
                            ? Strings.T("collection.trait.detonate.portion", ("percent", e.PortionPercent))
                            : "";
                    if (e.TargetAll)
                        AddTrait(traits, "burn", "", Strings.T("collection.trait.detonate_all.name"),
                            Strings.T("collection.trait.detonate_all.desc") + tail);
                    else
                        AddTrait(traits, "burn", "", Strings.T("collection.trait.detonate.name"),
                            Strings.T("collection.trait.detonate.desc") + tail);
                    break;
                }
                case EffectKind.BurnScale:
                {
                    // 灼操作族(D2-火 Task 2):与其余灼操作共用 "burn" 图标;百分比离散,读 e.Value
                    string mult = (e.Value / 100f).ToString("0.##");
                    AddTrait(traits, "burn", "×" + mult,
                        Strings.T("collection.trait.burn_scale.name"),
                        Strings.T("collection.trait.burn_scale.desc", ("mult", mult), ("cap", CombatCaps.BurnStacks)));
                    break;
                }
                case EffectKind.BurnEqualize:
                    AddTrait(traits, "burn", "",
                        Strings.T("collection.trait.burn_equalize.name"),
                        Strings.T("collection.trait.burn_equalize.desc"));
                    break;
                case EffectKind.Bleed:
                    AddTrait(traits, "bleed", v.ToString(),
                        Strings.T("collection.trait.bleed.name"),
                        Strings.T("collection.trait.bleed.desc", ("value", v)));
                    break;
                case EffectKind.Freeze:
                    AddTrait(traits, "freeze", v.ToString(),
                        Strings.T("collection.trait.freeze.name"),
                        Strings.T("collection.trait.freeze.desc", ("value", v)));
                    break;
                case EffectKind.Slow:
                    AddTrait(traits, "slow", v.ToString(),
                        Strings.T("collection.trait.slow.name"),
                        Strings.T("collection.trait.slow.desc", ("value", v)));
                    break;
                case EffectKind.Blind:
                    AddTrait(traits, "blind", v + "%",
                        Strings.T("collection.trait.blind.name"),
                        e.RiderOf == StatusKind.Burn   // 烟熏(D1 Task 9):随灼存续,没有回合数
                            ? Strings.T("collection.trait.blind.desc.rider_burn", ("value", v))
                            : Strings.T("collection.trait.blind.desc", ("value", v), ("turns", e.Turns)));
                    break;
                case EffectKind.Weaken:
                    // 减攻(D1 Task 5):无图标,走纯文字 chip(与魅惑同款 AddWord);Value 吃等级,回合不吃
                    // 炽焰(D2-火 Task 3):附着在灼上的随灼存续,带门槛时印门槛
                    AddWord(traits, Strings.T("collection.trait.weaken.chip"),
                        Strings.T("collection.trait.weaken.name"),
                        e.RiderOf != StatusKind.Burn
                            ? Strings.T("collection.trait.weaken.desc", ("value", v), ("turns", System.Math.Max(1, e.Turns)))
                            : e.MinBurn > 0
                                ? Strings.T("collection.trait.weaken.desc.rider_burn.min", ("value", v), ("min", e.MinBurn))
                                : Strings.T("collection.trait.weaken.desc.rider_burn", ("value", v)));
                    break;
                // 灼附着族(D2-火 Task 3):与其余灼操作共用 "burn" 图标;层数 / 回合离散,读 e.Value / e.Turns
                case EffectKind.HealBlock:
                    AddTrait(traits, "burn", "", Strings.T("collection.trait.heal_block.name"),
                        Strings.T("collection.trait.heal_block.desc"));
                    break;
                case EffectKind.BurnGrow:
                    AddTrait(traits, "burn", "+" + e.Value, Strings.T("collection.trait.burn_grow.name"),
                        Strings.T("collection.trait.burn_grow.desc", ("value", e.Value), ("turns", System.Math.Max(1, e.Turns))));
                    break;
                case EffectKind.BurnHold:
                    AddTrait(traits, "burn", "", Strings.T("collection.trait.burn_hold.name"),
                        Strings.T("collection.trait.burn_hold.desc"));
                    break;
                case EffectKind.BurnBurst:
                    AddTrait(traits, "burn", "", Strings.T("collection.trait.burn_burst.name"),
                        Strings.T("collection.trait.burn_burst.desc"));
                    break;
                case EffectKind.BurnBacklash:
                    AddTrait(traits, "burn", "", Strings.T("collection.trait.burn_backlash.name"),
                        Strings.T("collection.trait.burn_backlash.desc"));
                    break;
                // 敌人出手前 / 受击挂点(D2-火 Task 4):埋雷共用 "burn" 图标(火的出手前爆炸);回敬是通用形态,走纯文字 chip
                case EffectKind.Mine:
                    AddTrait(traits, "burn", v.ToString(), Strings.T("collection.trait.mine.name"),
                        e.BodyPercent > 0
                            ? Strings.T("collection.trait.mine.desc.body", ("percent", e.BodyPercent))
                            : Strings.T("collection.trait.mine.desc", ("value", v)));
                    break;
                case EffectKind.Retaliate:
                    AddWord(traits, Strings.T("collection.trait.retaliate.chip"),
                        Strings.T("collection.trait.retaliate.name"),
                        e.Value > 0
                            ? Strings.T("collection.trait.retaliate.desc.cap", ("cap", e.Value))
                            : Strings.T("collection.trait.retaliate.desc"));
                    break;
                // 其余单点效果(D2-火 Task 5):没有对应图标,走纯文字 chip(同回敬)
                case EffectKind.ExtraStrike:
                    AddWord(traits, Strings.T("collection.trait.extra_strike.chip"),
                        Strings.T("collection.trait.extra_strike.name"),
                        Strings.T("collection.trait.extra_strike.desc", ("percent", e.Value)));
                    break;
                case EffectKind.Thaw:
                    AddWord(traits, Strings.T("collection.trait.thaw.chip"),
                        Strings.T("collection.trait.thaw.name"), Strings.T("collection.trait.thaw.desc"));
                    break;
                case EffectKind.SelfCost:
                    AddWord(traits, Strings.T("collection.trait.self_cost.chip"),
                        Strings.T("collection.trait.self_cost.name"),
                        Strings.T("collection.trait.self_cost.desc", ("value", e.Value)));
                    break;
                case EffectKind.Reveal:
                    AddWord(traits, Strings.T("collection.trait.reveal.chip"),
                        Strings.T("collection.trait.reveal.name"), Strings.T("collection.trait.reveal.desc"));
                    break;
                case EffectKind.Seed:
                    // 种(D1 Task 6):图标 seed,Value 吃等级、回合不吃
                    AddTrait(traits, "seed", v.ToString(),
                        Strings.T("collection.trait.seed.name"),
                        Strings.T("collection.trait.seed.desc", ("value", v), ("turns", System.Math.Max(1, e.Turns))));
                    break;
                case EffectKind.Vulnerable:
                    // 标记(D1 Task 6):Turns == 0 + 冰缚选择器 = 持续到解冻
                    AddTrait(traits, "mark", v + "%",
                        Strings.T("collection.trait.mark.name"),
                        e.Turns <= 0 && e.Pick == EffectPick.FrozenByThisCast
                            ? Strings.T("collection.trait.mark.desc.bind", ("value", v))
                            : Strings.T("collection.trait.mark.desc", ("value", v), ("turns", System.Math.Max(1, e.Turns))));
                    break;
                case EffectKind.Silence:
                    AddTrait(traits, "silence", "",
                        Strings.T("collection.trait.silence.name"),
                        Strings.T("collection.trait.silence.desc", ("turns", e.Turns)));
                    break;
                case EffectKind.ArmorBreak:
                    AddTrait(traits, "armorbreak", v.ToString(),
                        Strings.T("collection.trait.armorbreak.name"),
                        Strings.T("collection.trait.armorbreak.desc", ("value", v)));
                    break;
                case EffectKind.Immunity:
                    AddTrait(traits, "immunity", v.ToString(),
                        Strings.T("collection.trait.immunity.name"),
                        Strings.T("collection.trait.immunity.desc", ("value", v)));
                    break;
                case EffectKind.Block:
                    // 次数是离散量,不吃等级:读 e.Value 而不是缩放后的 v
                    AddTrait(traits, "block", e.Value.ToString(),
                        Strings.T("collection.trait.block.name"),
                        Strings.T("collection.trait.block.desc", ("value", e.Value)));
                    break;
                case EffectKind.Reflect:
                    AddTrait(traits, "reflect", v + "%",
                        Strings.T("collection.trait.reflect.name"),
                        Strings.T("collection.trait.reflect.desc", ("value", v), ("turns", e.Turns)));
                    break;
                case EffectKind.Morale:
                    AddTrait(traits, "morale", "+" + v,
                        Strings.T("collection.trait.morale.name"),
                        Strings.T("collection.trait.morale.desc", ("value", v)));
                    break;
                case EffectKind.CritBuff:
                    AddTrait(traits, "crit", "+" + v + "%",
                        Strings.T("collection.trait.crit.name"),
                        Strings.T("collection.trait.crit.desc", ("value", v), ("turns", e.Turns)));
                    break;
                case EffectKind.Empower:
                    AddTrait(traits, "attack", "+" + v,
                        Strings.T("collection.trait.empower.name"),
                        Strings.T("collection.trait.empower.desc", ("value", v), ("turns", e.Turns)));
                    break;
                case EffectKind.DefenseBuff:
                    AddTrait(traits, "defense", "+" + v,
                        Strings.T("collection.trait.defense.name"),
                        Strings.T("collection.trait.defense.desc", ("value", v)));
                    break;
                case EffectKind.PierceBuff:
                    AddTrait(traits, "pierce", v.ToString(),
                        Strings.T("collection.trait.piercebuff.name"),
                        Strings.T("collection.trait.piercebuff.desc", ("value", v)));
                    break;
                // 驱散条数不吃卡等级(与 BattleEngine / CharInfo 同口径):用 e.Value
                case EffectKind.Dispel:
                    if (e.Value < 0 && e.TargetAll)
                        AddWord(traits, Strings.T("collection.trait.dispel_all_full.chip"),
                            Strings.T("collection.trait.dispel_all_full.name"),
                            Strings.T("collection.trait.dispel_all_full.desc"));
                    else if (e.Value < 0)
                        AddWord(traits, Strings.T("collection.trait.dispel_full.chip"),
                            Strings.T("collection.trait.dispel_full.name"),
                            Strings.T("collection.trait.dispel_full.desc"));
                    else if (e.TargetAll)
                        AddWord(traits, Strings.T("collection.trait.dispel_all.chip", ("count", e.Value)),
                            Strings.T("collection.trait.dispel_all.name"),
                            Strings.T("collection.trait.dispel_all.desc", ("count", e.Value)));
                    else
                        AddWord(traits, Strings.T("collection.trait.dispel.chip", ("count", e.Value)),
                            Strings.T("collection.trait.dispel.name"),
                            Strings.T("collection.trait.dispel.desc", ("count", e.Value)));
                    break;
                case EffectKind.Cleanse:
                    // D1 Task 7:Value > 0 = 只清前 N 个(离散量,读 e.Value)
                    AddWord(traits, Strings.T("collection.trait.cleanse.chip"),
                        Strings.T("collection.trait.cleanse.name"),
                        e.Value > 0 ? Strings.T("collection.trait.cleanse.desc.count", ("count", e.Value))
                            : Strings.T("collection.trait.cleanse.desc"));
                    break;
                // ---- D1 Task 7:我方侧新效果。无图标(减伤 / 反击加倍 / 保命的图标在 Task 7b),走纯文字 chip ----
                case EffectKind.DamageCut:
                    AddWord(traits, Strings.T("collection.trait.damagecut.chip"),
                        Strings.T("collection.trait.damagecut.name"),
                        Strings.T("collection.trait.damagecut.desc", ("value", v)));
                    break;
                case EffectKind.CounterBoost:
                    AddWord(traits, Strings.T("collection.trait.counterboost.chip"),
                        Strings.T("collection.trait.counterboost.name"),
                        Strings.T("collection.trait.counterboost.desc", ("mult", CharInfo.BoostMult(v))));
                    break;
                case EffectKind.Endure:
                    AddWord(traits, Strings.T("collection.trait.endure.chip"),
                        Strings.T("collection.trait.endure.name"),
                        Strings.T("collection.trait.endure.desc"));
                    break;
                case EffectKind.SummonSapling:
                    AddWord(traits, Strings.T("collection.trait.summonsapling.chip"),
                        Strings.T("collection.trait.summonsapling.name"),
                        Strings.T("collection.trait.summonsapling.desc", ("count", e.SummonCount), ("value", v)));
                    break;
                case EffectKind.HealSummons:
                    AddWord(traits, Strings.T("collection.trait.healsummons.chip"),
                        Strings.T("collection.trait.healsummons.name"),
                        e.PercentOfMax
                            ? Strings.T("collection.trait.healsummons.desc.pct", ("value", v))
                            : Strings.T("collection.trait.healsummons.desc", ("value", v)));
                    break;
                case EffectKind.ShieldSummons:
                    AddWord(traits, Strings.T("collection.trait.shieldsummons.chip"),
                        Strings.T("collection.trait.shieldsummons.name"),
                        Strings.T("collection.trait.shieldsummons.desc", ("value", v)));
                    break;
                case EffectKind.SummonStrike:
                    AddWord(traits, Strings.T("collection.trait.summonstrike.chip"),
                        Strings.T("collection.trait.summonstrike.name"),
                        Strings.T("collection.trait.summonstrike.desc", ("value", v)));
                    break;
                case EffectKind.ShieldFromHeal:
                    AddWord(traits, Strings.T("collection.trait.shieldfromheal.chip"),
                        Strings.T("collection.trait.shieldfromheal.name"),
                        Strings.T("collection.trait.shieldfromheal.desc", ("value", v)));
                    break;
                case EffectKind.AddWellspring:
                    AddWord(traits, Strings.T("collection.trait.addwellspring.chip", ("value", v)),
                        Strings.T("collection.trait.addwellspring.name"),
                        Strings.T("collection.trait.addwellspring.desc", ("value", v)));
                    break;
                case EffectKind.AddHeft:
                    AddWord(traits, Strings.T("collection.trait.addheft.chip", ("value", v)),
                        Strings.T("collection.trait.addheft.name"),
                        Strings.T("collection.trait.addheft.desc", ("value", v)));
                    break;
                // 嘲讽(D2-0 Task 2):Value = 回合数,0 = 本场
                case EffectKind.Taunt:
                    AddWord(traits, Strings.T("collection.trait.taunt.chip"),
                        Strings.T("collection.trait.taunt.name"),
                        CharInfo.TauntText(e));
                    break;
                // 反震(D1 Task 9):无图标,纯文字 chip;百分比离散
                case EffectKind.ShieldRecoil:
                    AddWord(traits, Strings.T("collection.trait.shieldrecoil.chip"),
                        Strings.T("collection.trait.shieldrecoil.name"),
                        Strings.T("collection.trait.shieldrecoil.desc", ("value", v)));
                    break;
                // AP 是节奏不是资源,同样不吃卡等级
                case EffectKind.ApBoost:
                    AddWord(traits, Strings.T("collection.trait.apboost.chip", ("value", e.Value)),
                        Strings.T("collection.trait.apboost.name"),
                        Strings.T("collection.trait.apboost.desc", ("value", e.Value)));
                    break;
                case EffectKind.SpendHeft:
                    AddWord(traits, Strings.T("collection.trait.spend_heft.chip"),
                        Strings.T("collection.trait.spend_heft.name"),
                        Strings.T("collection.trait.spend_heft.desc", ("value", v)));
                    break;
                case EffectKind.SpendWellspring:
                    AddWord(traits, Strings.T("collection.trait.spend_wellspring.chip"),
                        Strings.T("collection.trait.spend_wellspring.name"),
                        Strings.T("collection.trait.spend_wellspring.desc", ("value", v)));
                    break;
                case EffectKind.Charm:
                    AddWord(traits, Strings.T("collection.trait.charm.chip"),
                        Strings.T("collection.trait.charm.name"),
                        Strings.T("collection.trait.charm.desc", ("turns", e.Turns)));
                    break;
                case EffectKind.Quench:
                    // 蓄热(2026-09-16):与 BurnPotency 共用 "burn" 图标 —— 两者都是抬高
                    // _burnPerStack 的效果,差别只在数值来源(固定 vs 夺目标层数)。
                    AddTrait(traits, "burn", "+" + v,
                        Strings.T("collection.trait.quench.name"),
                        Strings.T("collection.trait.quench.desc", ("value", v)));
                    break;
                case EffectKind.Haste:
                    // 加速/急速(2026-09-16,水):与 BattleView/SummonInfo 的正向速度 chip
                    // 共用 "speed" 图标。按**未缩放的基础值**分档(e.Value,与 CharInfo 同口径)
                    // ——v 已被 ScaleByCardLevel 抬高,用它判档会让满级的「加速」误判成「急速」。
                    AddTrait(traits, "speed", "+" + v + "%",
                        e.Value >= 100
                            ? Strings.T("collection.trait.rapid.name")
                            : Strings.T("collection.trait.haste.name"),
                        e.Value >= 100
                            ? Strings.T("collection.trait.rapid.desc", ("value", v), ("turns", e.Turns))
                            : Strings.T("collection.trait.haste.desc", ("value", v), ("turns", e.Turns)));
                    break;
                case EffectKind.Unseal:
                    // 解封(2026-09-16,水):没有对应图标(不进 tools/icons 三件套,
                    // 走纯文字 chip,与净化/魅惑同款 AddWord)。Value 不用。
                    AddWord(traits, Strings.T("collection.trait.unseal.chip"),
                        Strings.T("collection.trait.unseal.name"),
                        Strings.T("collection.trait.unseal.desc"));
                    break;
                case EffectKind.Amplify:
                case EffectKind.Reshape:
                case EffectKind.Augment:
                    // 修饰器(D1 Task 3):只出现在特性里、出字前折叠进本体,本身不是独立效果,
                    // 不出 chip;它改了什么由 CharInfo 的卡面文案印。
                    break;
                default:
                    // 兜底:新加的 Kind 忘了接线时,至少在屏上看得见
                    AddUnique(traits, new Trait(null, e.Kind.ToString(), "", e.Kind.ToString(), ""));
                    break;
            }

            // 每击附带(D2-火 N4b):子效果各出自己的 chip,说明末尾注明「每击后触发」。回敬的 perHit 是它自己的反制效果,
            // 已由回敬那一条说明,不展开
            if (e.PerHit.Count > 0 && e.Kind != EffectKind.Retaliate)
            {
                int sub = traits.Count;
                Scan(traits, e.PerHit, cardLevel);
                AppendDesc(traits, sub, Strings.T("collection.trait.suffix.perhit"));
            }
            // 开局登记(D2-火 N12):本场不生效,说明末尾注明
            if (e.OpeningBattles > 0)
                AppendDesc(traits, before, Strings.T("collection.trait.suffix.opening", ("battles", e.OpeningBattles)));

            // 伤害上的修饰(穿透 / 分段 / 斩杀 / 条件翻倍):挂在这一击上,不是独立效果
            if (e.Kind == EffectKind.DamageSingle)
                DamageModifiers(traits, e);
            // 形状特性(2026-09-16 起 HealSelf 也认):治疗弹射(海/澡)配 Chain 的那一支,
            // AddShapeTrait 内部对 Single 早退,既有 HealSelf 字(Shape 恒 Single)因此
            // 不受影响。
            if (e.Kind == EffectKind.DamageSingle || e.Kind == EffectKind.HealSelf)
                AddShapeTrait(traits, e.Shape, e.ShapePercent, e.Shots);
            if (e.SummonShield > 0)
                AddTrait(traits, "shield", e.SummonShield.ToString(),
                        Strings.T("collection.trait.summon_shield.name"),
                        Strings.T("collection.trait.summon_shield.desc", ("value", e.SummonShield)));
        }

        private static void DamageModifiers(List<Trait> traits, EffectDef e)
        {
            if (e.Pierce > 0)
                AddTrait(traits, "pierce", e.Pierce.ToString(),
                            Strings.T("collection.trait.pierce.name"),
                            Strings.T("collection.trait.pierce.desc", ("value", e.Pierce)));
            if (e.HitCount > 1)
                AddWord(traits, Strings.T("collection.trait.hitcount.chip", ("count", e.HitCount)),
                            Strings.T("collection.trait.hitcount.name"),
                            Strings.T("collection.trait.hitcount.desc", ("count", e.HitCount)));
            if (e.ExecuteBelowPercent > 0)
            {
                if (e.ExecuteKills)
                    AddWord(traits, Strings.T("collection.trait.execute_kill.chip"),
                        Strings.T("collection.trait.execute_kill.name"),
                        Strings.T("collection.trait.execute_kill.desc", ("percent", e.ExecuteBelowPercent)));
                else
                    AddWord(traits, Strings.T("collection.trait.execute_bonus.chip"),
                        Strings.T("collection.trait.execute_bonus.name"),
                        Strings.T("collection.trait.execute_bonus.desc", ("percent", e.ExecuteBelowPercent)));
            }
            switch (e.DoubleVs)
            {
                case DamageCondition.Burning:
                    AddWord(traits, Strings.T("collection.trait.doublevs_burning.chip"),
                        Strings.T("collection.trait.doublevs_burning.name"),
                        Strings.T("collection.trait.doublevs_burning.desc"));
                    break;
                case DamageCondition.Bleeding:
                    AddWord(traits, Strings.T("collection.trait.doublevs_bleeding.chip"),
                        Strings.T("collection.trait.doublevs_bleeding.name"),
                        Strings.T("collection.trait.doublevs_bleeding.desc"));
                    break;
                case DamageCondition.Controlled:
                    AddWord(traits, Strings.T("collection.trait.doublevs_controlled.chip"),
                        Strings.T("collection.trait.doublevs_controlled.name"),
                        Strings.T("collection.trait.doublevs_controlled.desc"));
                    break;
                case DamageCondition.ArmorBroken:
                    AddWord(traits, Strings.T("collection.trait.doublevs_armorbroken.chip"),
                        Strings.T("collection.trait.doublevs_armorbroken.name"),
                        Strings.T("collection.trait.doublevs_armorbroken.desc"));
                    break;
            }
        }

        /// <summary>召唤物被动。顺序与 <see cref="SummonPassive"/> 的字段声明一致,
        /// 与 <c>CharInfo</c> 里那张被动表同源 —— 那边一句话带过,这边一条一卡。
        /// **远程不在这里**:它是攻击模式,归 <see cref="Modes"/>。</summary>
        private static void SummonTraits(List<Trait> traits, EffectDef summon, int cardLevel)
        {
            var p = summon.Passive;
            if (p == null) return;
            if (p.Speed > 0) AddTrait(traits, "speed", p.Speed.ToString(),
                            Strings.T("collection.trait.summon_speed.name"),
                            Strings.T("collection.trait.summon_speed.desc", ("value", p.Speed)));
            if (p.Thorns > 0) AddTrait(traits, "thorns", p.Thorns + "%",
                            Strings.T("collection.trait.summon_thorns.name"),
                            Strings.T("collection.trait.summon_thorns.desc", ("value", p.Thorns)));
            if (p.HealAlly > 0) AddTrait(traits, "heal", p.HealAlly.ToString(),
                            Strings.T("collection.trait.summon_heal.name"),
                            Strings.T("collection.trait.summon_heal.desc", ("value", p.HealAlly)));
            if (p.Regen > 0) AddTrait(traits, "heal", p.Regen.ToString(),
                            Strings.T("collection.trait.summon_regen.name"),
                            Strings.T("collection.trait.summon_regen.desc", ("value", p.Regen)));
            // 光环攻(2026-09-05,𣛧,平衡重做 P0):此前只接了 CharInfo 一处,徽章列表漏接
            // 导致卡面详情能看到一句话文案、召唤被动那排小卡却凭空少一条(P2 Task 4c 查漏)。
            if (p.AuraAttack > 0) AddTrait(traits, "attack", "+" + p.AuraAttack,
                            Strings.T("collection.trait.summon_auraattack.name"),
                            Strings.T("collection.trait.summon_auraattack.desc", ("value", p.AuraAttack)));
            if (p.OnHitBurn > 0)
            {
                if (p.OnHitBurnAll)
                    AddTrait(traits, "burn", p.OnHitBurn.ToString(),
                        Strings.T("collection.trait.summon_onhitburn_all.name"),
                        Strings.T("collection.trait.summon_onhitburn_all.desc", ("value", p.OnHitBurn)));
                else
                    AddTrait(traits, "burn", p.OnHitBurn.ToString(),
                        Strings.T("collection.trait.summon_onhitburn.name"),
                        Strings.T("collection.trait.summon_onhitburn.desc", ("value", p.OnHitBurn)));
            }
            if (p.OnHitCurse > 0) AddTrait(traits, "curse", p.OnHitCurse.ToString(),
                            Strings.T("collection.trait.summon_curse.name"),
                            Strings.T("collection.trait.summon_curse.desc", ("value", p.OnHitCurse)));
            if (p.Dodge > 0) AddTrait(traits, "dodge", p.Dodge + "%",
                            Strings.T("collection.trait.summon_dodge.name"),
                            Strings.T("collection.trait.summon_dodge.desc", ("value", p.Dodge)));
            if (p.Taunt) AddWord(traits, Strings.T("collection.trait.summon_taunt.chip"),
                            Strings.T("collection.trait.summon_taunt.name"),
                            Strings.T("collection.trait.summon_taunt.desc"));
            if (p.OnHitFreezeChance > 0)
                AddTrait(traits, "freeze", p.OnHitFreezeChance + "%",
                    Strings.T("collection.trait.summon_onhitfreeze.name"),
                    Strings.T("collection.trait.summon_onhitfreeze.desc",
                        ("chance", p.OnHitFreezeChance), ("turns", Mathf.Max(1, p.OnHitFreezeTurns))));
            if (p.OnHitSlowPercent > 0)
                AddTrait(traits, "slow", p.OnHitSlowPercent.ToString(),
                    Strings.T("collection.trait.summon_onhitslow.name"),
                    Strings.T("collection.trait.summon_onhitslow.desc",
                        ("value", p.OnHitSlowPercent), ("turns", Mathf.Max(1, p.OnHitSlowTurns))));
            if (p.OnSummonFreeze > 0)
                AddTrait(traits, "freeze", p.OnSummonFreeze.ToString(),
                            Strings.T("collection.trait.summon_onsummonfreeze.name"),
                            Strings.T("collection.trait.summon_onsummonfreeze.desc", ("value", p.OnSummonFreeze)));
            // 本命新字段(D2-0 Task 6,spec §9 木)
            if (p.BackRowBonusPercent > 0)
                AddTrait(traits, "attack", "+" + p.BackRowBonusPercent + "%",
                    Strings.T("collection.trait.summon_backrowbonus.name"),
                    Strings.T("collection.trait.summon_backrowbonus.desc", ("value", p.BackRowBonusPercent)));
            if (p.PerAllyAttackPercent > 0)
                AddTrait(traits, "attack", "+" + p.PerAllyAttackPercent + "%",
                    Strings.T("collection.trait.summon_perallyattack.name"),
                    Strings.T("collection.trait.summon_perallyattack.desc", ("value", p.PerAllyAttackPercent)));
            if (p.Armor > 0)
                AddTrait(traits, "defense", p.Armor.ToString(),
                    Strings.T("collection.trait.summon_armor.name"),
                    Strings.T("collection.trait.summon_armor.desc", ("value", p.Armor)));
            if (p.HealAllyTimes > 1)
                AddTrait(traits, "heal", "x" + p.HealAllyTimes,
                    Strings.T("collection.trait.summon_healallytimes.name"),
                    Strings.T("collection.trait.summon_healallytimes.desc", ("value", p.HealAllyTimes)));
            if (p.SproutPercent > 0 && p.SproutMax > 0)
                AddTrait(traits, "seed", p.SproutPercent + "%",
                    Strings.T("collection.trait.summon_sprout.name"),
                    Strings.T("collection.trait.summon_sprout.desc", ("value", p.SproutPercent), ("max", p.SproutMax)));
            if (p.EntrySaplings > 0)
                AddTrait(traits, "seed", p.EntrySaplings.ToString(),
                    Strings.T("collection.trait.summon_entrysaplings.name"),
                    Strings.T("collection.trait.summon_entrysaplings.desc", ("value", p.EntrySaplings)));
            if (p.OnHitCharmChance > 0)
                AddWord(traits, Strings.T("collection.trait.summon_onhitcharm.chip"),
                    Strings.T("collection.trait.summon_onhitcharm.name"),
                    Strings.T("collection.trait.summon_onhitcharm.desc", ("chance", p.OnHitCharmChance)));
            AddShapeTrait(traits, p.Shape, p.ShapePercent, p.Shots);
        }

        // ---- 建条目:名与说明各一个 key,拼在一起的话翻译者拿不到完整句子 ----

        /// ⚠ 名与说明由调用方**逐个字面量**取好再传进来,不在这里拼 key ——
        /// StringsTableTests 的扫描只认写死的 key,拼出来的话全部文案会被判成孤儿、
        /// 而拼出的前缀会被判成缺失(2026-09-04 当场踩到)。
        private static void AddTrait(List<Trait> traits, string iconKey, string amount,
            string name, string desc) =>
            AddUnique(traits, new Trait(iconKey, null, amount, name, desc));

        private static void AddWord(List<Trait> traits, string chip, string name, string desc) =>
            AddUnique(traits, new Trait(null, chip, "", name, desc));

        /// <summary>给 <paramref name="from"/> 起新增的 chip 的说明末尾补一句后缀(开局登记 / 每击附带)。</summary>
        private static void AppendDesc(List<Trait> traits, int from, string suffix)
        {
            for (int i = from; i < traits.Count; i++)
            {
                var t = traits[i];
                traits[i] = new Trait(t.IconKey, t.Word, t.Amount, t.Name, t.Desc + suffix);
            }
        }

        /// <summary>同一条特性只列一次(<see cref="Modes"/> 的 <c>seen</c> 是同一件事)。
        ///
        /// 双方向字(水/土,2026-09-02)的攻面与护面各带一份自己的效果表,两面**共有**的
        /// 特性会被 <see cref="Of"/> 的两次 <see cref="Scan"/> 各加一遍 —— 澡 的净化、
        /// 壁 的反弹在详情里因此印了两条一模一样的卡(2026-09-04 发现)。
        /// 特性段回答的是「这张字还带什么」,不区分哪一面带,重复只是噪声。
        ///
        /// 去重键 = 全部字段:同类不同量(灼烧 3 层 / 灼烧 5 层)照常各列一条;说明也算 ——
        /// 补了后缀的(开局登记 / 每击附带)与同名同量的普通条目是两件事,不能互相吞掉(终审 8)。</summary>
        private static void AddUnique(List<Trait> traits, Trait trait)
        {
            foreach (var t in traits)
                if (t.IconKey == trait.IconKey && t.Word == trait.Word
                    && t.Amount == trait.Amount && t.Name == trait.Name && t.Desc == trait.Desc)
                    return;
            traits.Add(trait);
        }

        /// <summary>图标 chip 的底色。按「这一条是什么性质」分组,不是按属性 ——
        /// 灼烧类朱砂、冰缓类水蓝、控制类紫、增益类墨蓝、防护类赭金、召唤物类木绿。</summary>
        public static Color ChipColor(string iconKey) => iconKey switch
        {
            "burn" or "burn_nodecay" or "bleed" or "scorch" or "sear" => Theme.Cinnabar,
            "freeze" or "slow" or "heal" => Theme.GlyphColor(Element.Water),
            "blind" or "silence" or "curse" => Theme.GlyphColor(Element.Heart),
            "armorbreak" or "pierce" or "attack" or "morale" or "crit" => Theme.InkSoft,
            "shield" or "defense" or "immunity" or "reflect" => Theme.GlyphColor(Element.Earth),
            "thorns" or "dodge" or "speed" => Theme.GlyphColor(Element.Wood),
            _ => Theme.InkSoft,
        };
    }
}
