using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字卡详情的「攻击模式」与「特性 · 技能」两段(稿 <c>docs/design/ui/scenes/CardDetail.dc.html</c>)。
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
                if (passive != null && passive.Shape != TargetShape.Single)
                    Add(modes, seen, new Mode(true, ShapeName(passive.Shape),
                        ShapeNote(passive.Shape, passive.ShapePercent, passive.Shots)));
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
                    case EffectKind.DamageSingle:
                        Add(modes, seen, new Mode(true, Strings.T("collection.mode.single_attack"),
                            DamageNote(e)));
                        break;
                    case EffectKind.DamageAll:
                        Add(modes, seen, new Mode(true, Strings.T("collection.mode.all_attack"),
                            DamageNote(e)));
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
                }
            }
        }

        private static void Add(List<Mode> modes, HashSet<string> seen, Mode mode)
        {
            if (seen.Add(mode.Name)) modes.Add(mode);
        }

        /// <summary>伤害那一条的形状限定:贯穿 / 横扫 / 溅射 / 连发 / 弹射,以及能不能越过前排。</summary>
        private static string DamageNote(EffectDef e)
        {
            // 只留形状限定(贯穿 · 溅 60% · 共 3 发)—— 溅多少、几发是选目标时要算的账。
            // 「可越过前排」那句去掉了(2026-09-04 改稿):偷袭已经在「特性 · 技能」里有一条,
            // 行尾再写一遍是同一件事说两遍
            if (e.Shape != TargetShape.Single)
                return ShapeName(e.Shape) + ShapeNote(e.Shape, e.ShapePercent, e.Shots);
            return "";
        }

        private static string ShapeName(TargetShape shape) => shape switch
        {
            TargetShape.Sweep => Strings.T("char.shape.sweep"),
            TargetShape.Cleave => Strings.T("char.shape.cleave"),
            TargetShape.Skewer => Strings.T("char.shape.skewer"),
            TargetShape.Volley => Strings.T("char.shape.volley"),
            TargetShape.Chain => Strings.T("char.shape.chain"),
            _ => Strings.T("char.shape.single"),
        };

        private static string ShapeNote(TargetShape shape, int percent, int shots)
        {
            if (percent <= 0) percent = 100;
            return shape switch
            {
                TargetShape.Volley => Strings.T("char.shape.suffix.volley", ("shots", shots)),
                TargetShape.Chain => Strings.T("char.shape.suffix.chain", ("shots", shots), ("percent", percent)),
                TargetShape.Sweep or TargetShape.Cleave or TargetShape.Skewer when percent != 100
                    => Strings.T("char.shape.suffix.splash", ("percent", percent)),
                _ => "",
            };
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
                int v = MetaRules.ScaleByCardLevel(e.Value, cardLevel);
                switch (e.Kind)
                {
                    // 伤害与护/治本身不是特性 —— 它们的量级在「数值」、去向在「攻击模式」
                    case EffectKind.DamageSingle:
                    case EffectKind.DamageAll:
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
                        if (e.TargetAll)
                            AddTrait(traits, "burn", "", Strings.T("collection.trait.detonate_all.name"),
                                Strings.T("collection.trait.detonate_all.desc"));
                        else
                            AddTrait(traits, "burn", "", Strings.T("collection.trait.detonate.name"),
                                Strings.T("collection.trait.detonate.desc"));
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
                            Strings.T("collection.trait.blind.desc", ("value", v), ("turns", e.Turns)));
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
                            Strings.T("collection.trait.crit.desc", ("value", v)));
                        break;
                    case EffectKind.Empower:
                        AddTrait(traits, "attack", "+" + v,
                            Strings.T("collection.trait.empower.name"),
                            Strings.T("collection.trait.empower.desc", ("value", v)));
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
                        AddWord(traits, Strings.T("collection.trait.cleanse.chip"),
                            Strings.T("collection.trait.cleanse.name"),
                            Strings.T("collection.trait.cleanse.desc"));
                        break;
                    // AP 是节奏不是资源,同样不吃卡等级
                    case EffectKind.ApBoost:
                        AddWord(traits, Strings.T("collection.trait.apboost.chip", ("value", e.Value)),
                            Strings.T("collection.trait.apboost.name"),
                            Strings.T("collection.trait.apboost.desc", ("value", e.Value)));
                        break;
                    case EffectKind.SpendMomentum:
                        AddWord(traits, Strings.T("collection.trait.spend_momentum.chip"),
                            Strings.T("collection.trait.spend_momentum.name"),
                            Strings.T("collection.trait.spend_momentum.desc", ("value", v)));
                        break;
                    case EffectKind.SpendWaterPower:
                        AddWord(traits, Strings.T("collection.trait.spend_water.chip"),
                            Strings.T("collection.trait.spend_water.name"),
                            Strings.T("collection.trait.spend_water.desc", ("value", v)));
                        break;
                    default:
                        // 兜底:新加的 Kind 忘了接线时,至少在屏上看得见
                        AddUnique(traits, new Trait(null, e.Kind.ToString(), "", e.Kind.ToString(), ""));
                        break;
                }

                // 伤害上的修饰(穿透 / 偷袭 / 分段 / 斩杀 / 条件翻倍):挂在这一击上,不是独立效果
                if (e.Kind == EffectKind.DamageSingle || e.Kind == EffectKind.DamageAll)
                    DamageModifiers(traits, e);
                if (e.SummonShield > 0)
                    AddTrait(traits, "shield", e.SummonShield.ToString(),
                            Strings.T("collection.trait.summon_shield.name"),
                            Strings.T("collection.trait.summon_shield.desc", ("value", e.SummonShield)));
            }
        }

        private static void DamageModifiers(List<Trait> traits, EffectDef e)
        {
            if (e.Pierce > 0)
                AddTrait(traits, "pierce", e.Pierce.ToString(),
                            Strings.T("collection.trait.pierce.name"),
                            Strings.T("collection.trait.pierce.desc", ("value", e.Pierce)));
            if (e.CanStrikeBackline)
                AddWord(traits, Strings.T("collection.trait.backline.chip"),
                            Strings.T("collection.trait.backline.name"),
                            Strings.T("collection.trait.backline.desc"));
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

        /// <summary>同一条特性只列一次(<see cref="Modes"/> 的 <c>seen</c> 是同一件事)。
        ///
        /// 双方向字(水/土,2026-09-02)的攻面与护面各带一份自己的效果表,两面**共有**的
        /// 特性会被 <see cref="Of"/> 的两次 <see cref="Scan"/> 各加一遍 —— 澡 的净化、
        /// 壁 的反弹在详情里因此印了两条一模一样的卡(2026-09-04 发现)。
        /// 特性段回答的是「这张字还带什么」,不区分哪一面带,重复只是噪声。
        ///
        /// 去重键 = 除说明外的全部字段:同类不同量(灼烧 3 层 / 灼烧 5 层)照常各列一条。</summary>
        private static void AddUnique(List<Trait> traits, Trait trait)
        {
            foreach (var t in traits)
                if (t.IconKey == trait.IconKey && t.Word == trait.Word
                    && t.Amount == trait.Amount && t.Name == trait.Name)
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
