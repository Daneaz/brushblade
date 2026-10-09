using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>特性解锁规则(spec v7 §1):等级达到槽位值即解锁;被同一作用面上更高槽位 Replaces 的不再生效
    /// (Lv1 例外,见 Unlocked);结果按槽位升序。</summary>
    public static class TraitRules
    {
        /// <summary>已解锁的特性,按槽位升序。被同一作用面上更高槽位 Replaces 的不再返回 ——
        /// **Lv1 例外**(D1 Task 4):被 Lv3 替换的 Lv1 仍返回(UI 要显示关键词名);
        /// 它的效果不参与出字,见 <see cref="Superseded"/>。</summary>
        public static IReadOnlyList<TraitDef> Unlocked(CharDef def, int cardLevel)
        {
            var unlocked = def.Traits.Where(t => t.UnlockLevel <= cardLevel).ToList();
            var replaced = new HashSet<(TraitSlot, TraitFace)>(
                unlocked.Where(t => t.Replaces.HasValue && t.Replaces.Value != TraitSlot.Lv1)
                    .Select(t => (t.Replaces.Value, t.Face)));
            return unlocked.Where(t => !replaced.Contains((t.Slot, t.Face))).OrderBy(t => (int)t.Slot).ToList();
        }

        /// <summary>被同一作用面上更高槽位 Replaces 的特性(含 Lv1):效果不参与出字。
        /// Lv1 被 Lv3 替换时,Lv3 的效果以「按 Kind 替换本体」的方式生效(见 <see cref="Fold"/>)。</summary>
        private static HashSet<(TraitSlot, TraitFace)> Superseded(IReadOnlyList<TraitDef> unlocked) =>
            new(unlocked.Where(t => t.Replaces.HasValue).Select(t => (t.Replaces.Value, t.Face)));

        /// <summary>按特性键(<c>BattleEngine.TraitKey</c> 产出的「字/槽/面」)找该字的特性名;找不到或 key 为 null 返回 null。
        /// 表现层用它把状态上的 TraitKey 翻成名字(炽焰详情「炽焰:灼满 5 层时……」)。</summary>
        public static string NameOf(CharDef def, string traitKey)
        {
            if (def == null || traitKey == null) return null;
            foreach (var t in def.Traits)
                if (BattleEngine.TraitKey(def.Id, t.Slot, t.Face) == traitKey) return t.Name;
            return null;
        }

        public static IReadOnlyList<TraitDef> ActiveTraits(CharDef def, CardFace face, int cardLevel)
        {
            var unlocked = Unlocked(def, cardLevel);
            var superseded = Superseded(unlocked);
            return unlocked.Where(t => t.Form == TraitForm.Active && t.AppliesTo(face)
                && !superseded.Contains((t.Slot, t.Face))).ToList();
        }

        /// <summary>附着类效果(D1 Task 9):挂在载体上(RiderOf)或以护盾为载体(ShieldRecoil)。
        /// 被动特性里的这类效果也在出字时结算(其余被动效果不在出字时执行)。</summary>
        public static bool IsAttached(EffectDef e) => e.RiderOf.HasValue || e.Kind == EffectKind.ShieldRecoil;

        /// <summary>该字这一面已解锁、Trigger 为 <paramref name="trigger"/> 的特性(D1 Task 9,出字内触发)。
        /// 被替换的槽位不算;按槽位升序。</summary>
        public static IReadOnlyList<TraitDef> Triggered(CharDef def, CardFace face, int cardLevel, TraitTrigger trigger)
        {
            if (def.Traits.Count == 0) return Array.Empty<TraitDef>();
            var unlocked = Unlocked(def, cardLevel);
            var superseded = Superseded(unlocked);
            return unlocked.Where(t => t.Trigger == trigger && t.AppliesTo(face)
                && !superseded.Contains((t.Slot, t.Face))).ToList();
        }

        /// <summary>修饰器:出字前折叠进本体,不进结算循环(D1 Task 3 / 4)。</summary>
        public static bool IsModifier(EffectKind kind) =>
            kind == EffectKind.Amplify || kind == EffectKind.Reshape || kind == EffectKind.Augment
            || kind == EffectKind.BlockMod;   // D2-金 E12

        /// <summary>该字这一面本次出字实际结算的效果表(D1 Task 4,取代 BattleEngine.CastEffectsOf 里的拼装)。
        /// 本体取法与 <c>BattleEngine.EffectsOf</c> 一致(攻击面空 / 效果空时走兜底一击)。</summary>
        public static List<EffectDef> CastEffects(CharDef def, CardFace face, int cardLevel) =>
            Fold(BattleEngine.EffectsOf(def, face == CardFace.Attack), def, face, cardLevel);

        // ---- Augment 的「回合」字段:表驱动,值 = 回合是否放在 Value 上(否则在 Turns 上)----
        // 新增带回合的 kind(Weaken、种……)时在这里加一行。
        private static readonly Dictionary<EffectKind, bool> TurnsInValue = new()
        {
            [EffectKind.Freeze] = true,
            [EffectKind.Slow] = true,
            [EffectKind.DefenseBuff] = false,
            [EffectKind.ArmorBreak] = false,
            [EffectKind.HealOverTime] = false,
            [EffectKind.Weaken] = false,
            [EffectKind.Seed] = false,
            [EffectKind.Vulnerable] = false,
            [EffectKind.Bleed] = false,   // D2-金 E8:流血读 turns(缺省 3)
        };

        public static bool HasTurns(EffectKind kind) => TurnsInValue.ContainsKey(kind);

        /// <summary>该效果的回合数(按 kind 自动取 Value 或 Turns);表外的 kind 返回 0。</summary>
        public static int TurnsOf(EffectDef e) =>
            !TurnsInValue.TryGetValue(e.Kind, out bool inValue) ? 0 : inValue ? e.Value : e.Turns;

        /// <summary>回合数 + <paramref name="add"/> 的副本;表外的 kind 原样返回。</summary>
        public static EffectDef WithTurns(EffectDef e, int add) =>
            !TurnsInValue.TryGetValue(e.Kind, out bool inValue) ? e
            : inValue ? e.With(value: e.Value + add) : e.With(turns: e.Turns + add);

        /// <summary>本次出字实际结算的效果表(D1 Task 3 / 4),按顺序:
        /// ① 本体复制一份;
        /// ② 已解锁、面匹配、出字时机、<c>Replaces == Lv1</c> 的特性:每条效果替换本体里**第一条同 Kind** 的效果(同位置),
        ///    没有同 Kind 就追加到末尾;被 Replaces 的 Lv1 特性自己的效果不执行;
        /// ③ 其余已解锁、面匹配、出字时机的特性(主动与被动)效果按槽位追加(R3)——被动的 Lv6 余震 / 冰缚 /
        ///    护持 / 扎根等就靠这一步生效;非出字时机(暴击 / 击杀)的被动走反应队列,不在这里;
        /// ④ 修饰器(本体里、特性里、出字时机的被动特性里)按出现顺序逐条折叠:Amplify / Reshape / Augment,
        ///    修饰器本身不进结果。
        ///
        /// 口径(终审 Minor 6):
        /// - **Lv1 恒为两面**(TraitFace.Both),特性行只放名字、效果为空 —— Lv1 的效果写在本体里。
        ///   所以 Lv3 的「替换 Lv1」实际作用于**那一面的本体**(按 Kind),不是去掉 Lv1 特性行自己的效果;
        ///   Lv3 可以只写一面(木只有攻面),另一面本体不动。
        /// - 被替换判定按 (槽位, 面) 精确匹配(<see cref="Superseded"/>):单面 Lv3 记下的是 (Lv1, 那一面),
        ///   与 (Lv1, Both) 不相等 —— 因为 Lv1 效果为空,这一点不影响结果;若将来给 Lv1 行写效果,要先改这里。
        /// - ②③ 是**同一趟**按槽位升序遍历:替换与追加按槽位交错发生(Lv3 先替换、Lv5 再追加……)。
        ///   替换只找「本体里或此前追加的」同 Kind 条目,所以槽位更低的追加项也可能被更高槽位的替换命中;
        ///   现行数据里只有 Lv3 做替换、它是最低的非 Lv1 槽,交错不产生差别。
        ///
        /// 纯函数:不改 <paramref name="body"/> 与特性里的任何 EffectDef(它们是字表共享对象),
        /// 被修饰的效果换成 <see cref="EffectDef.With"/> 产出的副本。本体条目在没有修饰器、没有 Lv3 替换时
        /// 逐项是同一对象;追加的特性效果(非 Lv1 / Lv3 槽,D2-火 G11)是打了 TraitKey 的副本(<see cref="ForCast"/>),
        /// 数值字段不变。没有已解锁特性时结果与本体逐项同一对象 —— 恒等。</summary>
        public static List<EffectDef> Fold(IReadOnlyList<EffectDef> body, CharDef def, CardFace face, int cardLevel)
        {
            var effects = new List<EffectDef>(body.Count);
            var modifiers = new List<EffectDef>();
            foreach (var e in body)
                (IsModifier(e.Kind) ? modifiers : effects).Add(e);
            var unlocked = Unlocked(def, cardLevel);
            var superseded = Superseded(unlocked);
            var touched = new HashSet<int>();   // 已被替换 / 刚追加的位置:同一条 Lv3 里的两条同 Kind 不互相覆盖
            foreach (var t in unlocked)
            {
                if (!t.AppliesTo(face) || t.Trigger != TraitTrigger.Cast) continue;
                if (superseded.Contains((t.Slot, t.Face))) continue;
                bool replacesLv1 = t.Replaces == TraitSlot.Lv1;
                foreach (var raw in t.Effects)
                {
                    if (IsModifier(raw.Kind))
                    {
                        // Reshape 带来的每击附带同样是特性来源(G11):本体的 DamageSingle 不打键,键只能打在附带上
                        bool bodySlot = t.Slot == TraitSlot.Lv1 || t.Slot == TraitSlot.Lv3;   // 同 ForCast:Lv1 / Lv3 是本体,不打
                        modifiers.Add(bodySlot ? raw : TagPerHit(raw, BattleEngine.TraitKey(def.Id, t.Slot, t.Face)));
                        continue;
                    }
                    // 附着类(D1 Task 9,附录 M9)与非替换特性:追加,打上特性键(ForCast;载体配对 / G11 分来源)
                    if (IsAttached(raw) || !replacesLv1)
                    {
                        effects.Add(ForCast(raw, t, def, body));
                        continue;
                    }
                    // Lv3 替换本体(Replaces == Lv1):按 Kind 换掉本体第一条同 Kind 的效果,不打来源键(仍是本体)
                    var e = ResolveBodyPercent(raw, body, def);
                    int at = -1;
                    for (int i = 0; i < effects.Count; i++)
                        if (effects[i].Kind == e.Kind && !touched.Contains(i)) { at = i; break; }
                    if (e.Kind == EffectKind.Summon && e.Value == 0)
                    {
                        // 本命强化(D2-0 Task 6,E8):只覆盖本体 Summon 被动里的非缺省字段;血 / 攻 / 只数不动。
                        // 找不到本体 Summon 就空转,不追加一条 0 血的召唤。
                        if (at >= 0)
                        {
                            effects[at] = effects[at].With(passive: MergePassive(effects[at].Passive, e.Passive));
                            touched.Add(at);
                        }
                        continue;
                    }
                    if (at >= 0) effects[at] = e;
                    else { effects.Add(e); at = effects.Count - 1; }
                    touched.Add(at);
                }
            }
            ApplyModifiers(effects, modifiers);
            return effects;
        }

        /// <summary>一条特性效果进出字 / 反应前的解析(D2-火 E4 / E5):① bodyPercent → 具体 Value;
        /// ② 打特性来源键 TraitKey —— 附着类一律打(载体配对);其余只给非 Lv1 / Lv3 槽位打(G11:Weaken / Blind / Vulnerable
        /// 按 SourceId + TraitKey 分来源,不与本体同源合并)。Lv1 / Lv3 是本体的一部分(替换 / 强化本体),不打。
        /// 主动、被动一视同仁:走到这里的都是出字时机(或暴击 / 击杀反应)的特性(D1 终审 Critical)。</summary>
        internal static EffectDef ForCast(EffectDef e, TraitDef t, CharDef def, IReadOnlyList<EffectDef> body)
        {
            e = ResolveBodyPercent(e, body, def);
            bool tag = IsAttached(e) || (t.Slot != TraitSlot.Lv1 && t.Slot != TraitSlot.Lv3);
            if (!tag) return e;
            string key = BattleEngine.TraitKey(def.Id, t.Slot, t.Face);
            return TagPerHit(e, key).With(traitKey: key);
        }

        /// <summary>每击附带的子效果随宿主打特性来源键(终审 5,G11):否则每击挂的减攻 / 致盲 / 标记会与本体同源合并。
        /// 没有每击附带时原样返回。</summary>
        private static EffectDef TagPerHit(EffectDef e, string key) =>
            e.PerHit.Count == 0 ? e : e.With(perHit: e.PerHit.Select(c => c.With(traitKey: key)).ToList());

        /// <summary>bodyPercent(E5):Value = 本体伤害基数 × N%(向下取整),基数见 <see cref="BodyDamageOf"/>。未启用原样返回。</summary>
        private static EffectDef ResolveBodyPercent(EffectDef e, IReadOnlyList<EffectDef> body, CharDef def)
        {
            if (e.BodyPercent <= 0) return e;
            return e.With(value: BodyDamageOf(body, def) * e.BodyPercent / 100);
        }

        /// <summary>「本体」伤害基数(未缩放):本面本体首条 DamageSingle 的 Value;本面没有伤害(燃 / 铠 / 生面)时
        /// 取攻击面本体首条 DamageSingle(D2-火 Task 4 Ruling 1,炸·埋雷在燃面);都没有为 0。
        /// bodyPercent(Fold / 反应入队)与追加一击(ExtraStrike)共用这一份口径。</summary>
        internal static int BodyDamageOf(IReadOnlyList<EffectDef> body, CharDef def)
        {
            foreach (var b in body)
                if (b.Kind == EffectKind.DamageSingle) return b.Value;
            return BattleEngine.AttackBaseOf(def);
        }

        private static void ApplyModifiers(List<EffectDef> effects, IEnumerable<EffectDef> modifiers)
        {
            foreach (var m in modifiers)
            {
                switch (m.Kind)
                {
                    case EffectKind.Amplify: ApplyAmplify(effects, m); break;
                    case EffectKind.Reshape: ApplyReshape(effects, m); break;
                    case EffectKind.Augment: ApplyAugment(effects, m); break;
                    case EffectKind.BlockMod: ApplyBlockMod(effects, m); break;
                }
            }
        }

        /// <summary>部件印记(D2-0 Task 7,E10):把来源特性的效果并进部件这次出手的效果表 ——
        /// 非修饰器追加到末尾,修饰器(火山的 Reshape 全体等)随后按出现顺序折叠到整张表上(与 <see cref="Fold"/> ④ 同口径)。
        /// 纯函数:返回新表,不改 <paramref name="cast"/>。</summary>
        public static List<EffectDef> FoldExtra(List<EffectDef> cast, IReadOnlyList<EffectDef> extra)
        {
            var effects = new List<EffectDef>(cast);
            var modifiers = new List<EffectDef>();
            foreach (var e in extra)
                (IsModifier(e.Kind) ? modifiers : effects).Add(e);
            ApplyModifiers(effects, modifiers);
            return effects;
        }

        /// <summary>字形特性(spec §9「(拆字) / (成字)」,D2-0 Task 7):已解锁、Trigger 为 <paramref name="trigger"/>
        /// (OnCompose / OnDismantle)的特性,不分面;被替换的槽位不算;按槽位升序。没有特性数据时直接返回空表。</summary>
        public static IReadOnlyList<TraitDef> Glyph(CharDef def, int cardLevel, TraitTrigger trigger)
        {
            if (def.Traits.Count == 0) return Array.Empty<TraitDef>();
            var unlocked = Unlocked(def, cardLevel);
            var superseded = Superseded(unlocked);
            return unlocked.Where(t => t.Trigger == trigger && !superseded.Contains((t.Slot, t.Face))).ToList();
        }

        /// <summary>本命强化的被动合并(E8):<paramref name="over"/> 里非缺省的字段(数值 ≠ 0、布尔 true、
        /// Shape ≠ Single)覆盖 <paramref name="baseline"/>,其余沿用。纯函数,返回新对象。</summary>
        internal static SummonPassive MergePassive(SummonPassive baseline, SummonPassive over)
        {
            var m = baseline?.Clone() ?? new SummonPassive();
            if (over == null) return m;
            if (over.Speed != 0) m.Speed = over.Speed;
            if (over.Thorns != 0) m.Thorns = over.Thorns;
            if (over.HealAlly != 0) m.HealAlly = over.HealAlly;
            if (over.Regen != 0) m.Regen = over.Regen;
            if (over.AuraAttack != 0) m.AuraAttack = over.AuraAttack;
            if (over.OnHitBurn != 0) m.OnHitBurn = over.OnHitBurn;
            if (over.OnHitBurnAll) m.OnHitBurnAll = true;
            if (over.OnHitCurse != 0) m.OnHitCurse = over.OnHitCurse;
            if (over.Dodge != 0) m.Dodge = over.Dodge;
            if (over.OnSummonFreeze != 0) m.OnSummonFreeze = over.OnSummonFreeze;
            if (over.OnHitFreezeChance != 0) m.OnHitFreezeChance = over.OnHitFreezeChance;
            if (over.OnHitFreezeTurns != 0) m.OnHitFreezeTurns = over.OnHitFreezeTurns;
            if (over.OnHitSlowPercent != 0) m.OnHitSlowPercent = over.OnHitSlowPercent;
            if (over.OnHitSlowTurns != 0) m.OnHitSlowTurns = over.OnHitSlowTurns;
            if (over.Taunt) m.Taunt = true;
            if (over.Ranged) m.Ranged = true;
            if (over.Shape != TargetArea.Single) m.Shape = over.Shape;
            if (over.ShapePercent != 0) m.ShapePercent = over.ShapePercent;
            if (over.Shots != 0) m.Shots = over.Shots;
            if (over.BackRowBonusPercent != 0) m.BackRowBonusPercent = over.BackRowBonusPercent;
            if (over.PerAllyAttackPercent != 0) m.PerAllyAttackPercent = over.PerAllyAttackPercent;
            if (over.Armor != 0) m.Armor = over.Armor;
            if (over.HealAllyTimes != 0) m.HealAllyTimes = over.HealAllyTimes;
            if (over.SproutPercent != 0) m.SproutPercent = over.SproutPercent;
            if (over.SproutMax != 0) m.SproutMax = over.SproutMax;
            if (over.EntrySaplings != 0) m.EntrySaplings = over.EntrySaplings;
            if (over.OnHitCharmChance != 0) m.OnHitCharmChance = over.OnHitCharmChance;
            return m;
        }

        /// <summary>Augment:本面**第一条** Kind == AugmentKind 的效果,对应字段 + Value。找不到 / 该 kind 没有这个字段 → 空转。</summary>
        private static void ApplyAugment(List<EffectDef> effects, EffectDef aug)
        {
            int at = effects.FindIndex(e => e.Kind == aug.AugmentKind);
            if (at < 0) return;
            var target = effects[at];
            switch (aug.AugmentField)
            {
                case AugmentField.Count:
                    if (target.Kind == EffectKind.Block) effects[at] = target.With(value: target.Value + aug.Value);
                    break;
                case AugmentField.Turns:
                    effects[at] = WithTurns(target, aug.Value);
                    break;
                case AugmentField.Shots:
                    if (target.Kind == EffectKind.DamageSingle || target.Kind == EffectKind.HealSelf)
                        effects[at] = target.With(shots: target.Shots + aug.Value);
                    break;
            }
        }

        /// <summary>Amplify:本面每条 scope 匹配的效果挂一项 (百分点, 条件)。不在这里求值 ——
        /// 目标相关的条件要按每一击的目标判定(BattleEngine.AmpPercent)。
        /// 开局效果(OpeningBattles &gt; 0)不挂(Ruling 5,spec §5.2 第 5 律「跨场只存不长」):登记的是定值,不吃本场的加成。</summary>
        private static void ApplyAmplify(List<EffectDef> effects, EffectDef amp)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (!InScope(amp.Scope, e.Kind) || e.OpeningBattles > 0) continue;
                var terms = new List<(int Percent, DamageCondition If, ScaleBasis Per, int Cap)>(e.AmpTerms)
                    { (amp.Value, amp.OnlyIf, amp.ScaleBy, amp.ScaleCap) };
                effects[i] = e.With(ampTerms: terms);
            }
        }

        /// <summary>Reshape:只改**第一条** DamageSingle;Reshape 上非缺省的字段覆盖原值。没有就空转。</summary>
        private static void ApplyReshape(List<EffectDef> effects, EffectDef r)
        {
            int at = effects.FindIndex(e => e.Kind == EffectKind.DamageSingle);
            if (at < 0)
            {
                // 重选目标(D2-火 E3,烈风「燃改为横扫」):本面没有伤害时,把落在主目标上的敌方效果换成 Reshape 的选择器
                if (r.Pick == EffectPick.Primary) return;
                for (int i = 0; i < effects.Count; i++)
                    if (EffectPickRules.Supports(effects[i].Kind) && EffectPickRules.Effective(effects[i]) == EffectPick.Primary)
                        effects[i] = effects[i].With(pick: r.Pick);
                return;
            }
            effects[at] = effects[at].With(
                shape: r.Shape != TargetArea.Single ? r.Shape : (TargetArea?)null,
                // 改成全体时百分比一并重置:没写 shapePercent 就是全额 100,不沿用原效果(如横扫 50)的溅射比例
                shapePercent: r.ShapePercent != 100 || r.Shape == TargetArea.All ? r.ShapePercent : (int?)null,
                shots: r.Shots != 0 ? r.Shots : (int?)null,
                hitCount: r.HitCount != 1 ? r.HitCount : (int?)null,
                hitPercent: r.HitPercent != 100 ? r.HitPercent : (int?)null,
                forceCrit: r.ForceCrit ? true : (bool?)null,
                armorIgnorePercent: r.ArmorIgnorePercent > 0 ? r.ArmorIgnorePercent : (int?)null,
                shieldStrikePercent: r.ShieldStrikePercent > 0 ? r.ShieldStrikePercent : (int?)null,
                armorStrikePercent: r.ArmorStrikePercent > 0 ? r.ArmorStrikePercent : (int?)null,
                // 每击附带 / 散射每发百分比(D2-火 N4b):炎刃、四炎、火花四溅都写在 Reshape 上
                perHit: r.PerHit.Count > 0 ? r.PerHit : null,
                perHitFrom: r.PerHitFrom != 1 ? r.PerHitFrom : (int?)null,
                shotPercent: r.ShotPercent != 100 ? r.ShotPercent : (int?)null,
                // 斩杀(D2-金 E7,铡刀落)与击数按战意(E10,大卸八块)
                executeBelowPercent: r.ExecuteBelowPercent > 0 ? r.ExecuteBelowPercent : (int?)null,
                executeKills: r.ExecuteBelowPercent > 0 ? r.ExecuteKills : (bool?)null,
                scaleBy: r.ScaleBy != ScaleBasis.None ? r.ScaleBy : (ScaleBasis?)null);
        }

        /// <summary>BlockMod(D2-金 E12):只改本面**第一条** Block;非缺省字段覆盖原值(多条按出现顺序,后者覆盖)。没有就空转。</summary>
        private static void ApplyBlockMod(List<EffectDef> effects, EffectDef m)
        {
            int at = effects.FindIndex(e => e.Kind == EffectKind.Block);
            if (at < 0) return;
            effects[at] = effects[at].With(
                counterPercent: m.CounterPercent > 0 ? m.CounterPercent : (int?)null,
                scaleBy: m.ScaleBy != ScaleBasis.None ? m.ScaleBy : (ScaleBasis?)null,
                scaleMin: m.ScaleMin > 0 ? m.ScaleMin : (int?)null);
        }

        public static bool InScope(AmpScope scope, EffectKind kind)
        {
            bool damage = kind == EffectKind.DamageSingle;
            bool heal = kind == EffectKind.HealSelf || kind == EffectKind.HealAll || kind == EffectKind.HealOverTime;
            bool shield = kind == EffectKind.Shield || kind == EffectKind.ShieldAll;
            bool counter = kind == EffectKind.Block;
            bool seed = kind == EffectKind.Seed;
            bool burn = kind == EffectKind.BurnSingle || kind == EffectKind.BurnAll;   // 作用于火力(D2-火 G3)
            return scope switch
            {
                AmpScope.Damage => damage,
                AmpScope.Heal => heal,
                AmpScope.Shield => shield,
                AmpScope.Counter => counter,
                AmpScope.Seed => seed,
                AmpScope.Burn => burn,
                AmpScope.All => damage || heal || shield || counter || seed || burn,
                _ => false,
            };
        }
    }
}
