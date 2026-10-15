using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>字卡详情 / 升级预览的一行特性(Plan E2,附录 §2.7)。
    /// 面特性行(Lv5 / Lv6 / Lv8)一行一条;「两面」条(Lv3 / Lv4)按槽位合成一行,分面的两条都挂在 <see cref="Defs"/> 里
    /// (先攻面后五行面),Presentation 不许只取第一条(Review Focus 2)。</summary>
    public sealed class SheetTrait
    {
        public TraitSlot Slot { get; }
        /// <summary>Lv3 / Lv4 恒 Both(按槽位定,不按数据里的 Face);其余 = 那条特性的 Face。</summary>
        public TraitFace Face { get; }
        /// <summary>这一行的特性。Both 行可能是 [攻面那条, 五行面那条],也可能只有一条 Both。</summary>
        public IReadOnlyList<TraitDef> Defs { get; }
        /// <summary>卡等级 ≥ 槽位等级。</summary>
        public bool Unlocked { get; }
        /// <summary>Lv8 招牌。</summary>
        public bool IsSignature => Slot == TraitSlot.Lv8;
        /// <summary>去重后的名字(按 Defs 顺序):同名只出一个,不同名两个都在(Q3「名 A / 名 B」)。</summary>
        public IReadOnlyList<string> Names { get; }
        /// <summary>去重后的类别(按 Defs 顺序,每条取自己的 <see cref="TraitDef.Form"/>):同类别只出一个,不同两个都在
        /// (final review Minor 2:木系 11 张字的 Lv3 是「攻面主动 / 五行面被动」,不能都按 Defs[0] 印)。</summary>
        public IReadOnlyList<TraitForm> Forms { get; }

        public SheetTrait(TraitSlot slot, TraitFace face, IReadOnlyList<TraitDef> defs, bool unlocked)
        {
            Slot = slot;
            Face = face;
            Defs = defs ?? throw new ArgumentNullException(nameof(defs));
            Unlocked = unlocked;
            Names = defs.Select(t => t.Name).Distinct().ToList();
            Forms = defs.Select(t => t.Form).Distinct().ToList();
        }
    }

    /// <summary>面头落点的四种说法(稿:拖到敌人 / 拖到自己或木灵 / 拖到自己 / 空位召唤)。</summary>
    public enum FaceTarget
    {
        Enemy,
        Ally,
        SelfOnly,
        Summon,
    }

    /// <summary>字卡详情全平铺的一级(spec B §1.2):两面是同一张网格的两列,同级并排。
    /// 某面没有这一级 = null(Presentation 画「待补」占位;spec A 合并后不应出现,留作防御)。</summary>
    public sealed class SheetLevelRow
    {
        public TraitSlot Slot { get; }
        public SheetTrait Attack { get; }
        public SheetTrait Feature { get; }
        /// <summary>卡等级 ≥ 槽位等级(两面同级同解锁)。</summary>
        public bool Unlocked { get; }

        public SheetLevelRow(TraitSlot slot, SheetTrait attack, SheetTrait feature, bool unlocked)
        {
            Slot = slot;
            Attack = attack;
            Feature = feature;
            Unlocked = unlocked;
        }

        public SheetTrait Of(CardFace face) => face == CardFace.Attack ? Attack : Feature;
    }

    /// <summary>字卡详情 / 升级预览「显示哪几行」的规则(Plan E2)。纯函数,只读字表,不看场面。
    /// 字卡详情(spec B)已全平铺:每面逐级一行(Lv1 起,两面同级成对);「两面」条(<see cref="SharedTraits"/> 等)现只服务升级预览,
    /// 其归属:Lv3 / Lv4 进「两面」条(Q3 / Q4);Lv5 / Lv6 / Lv8 两面各一条,进各自那一面。</summary>
    public static class CardSheetRules
    {
        /// <summary>进升级预览「两面」条的槽位(字卡详情已不用;数据里 Lv3 恒分面、Lv4 有 22 张分面)。</summary>
        private static bool IsShared(TraitSlot slot) => slot == TraitSlot.Lv3 || slot == TraitSlot.Lv4;

        private static readonly TraitFace[] BothMark = { TraitFace.Both };

        /// <summary>等级条第 <paramref name="level"/> 格的解锁记号:Lv1 / 3 / 4 → [Both](按槽位定);
        /// 面特性所在的格 → 该级出现的面(先攻后五行,Lv5 / Lv6 / Lv8 都是两枚,攻面一枚、五行面一枚);没有特性的格 → 空。</summary>
        public static IReadOnlyList<TraitFace> LadderMarks(CharDef def, int level)
        {
            var atLevel = def.Traits.Where(t => t.UnlockLevel == level).ToList();
            if (atLevel.Count == 0) return Array.Empty<TraitFace>();
            if (level == (int)TraitSlot.Lv1 || IsShared((TraitSlot)level) || atLevel.Any(t => t.Face == TraitFace.Both))
                return BothMark;
            return atLevel.Select(t => t.Face).Distinct().OrderBy(FaceOrder).ToList();
        }

        /// <summary>这一面的特性行:槽位不是 Lv1 / 3 / 4、作用于这一面的特性,一条一行,按槽位升序,含未解锁。</summary>
        public static IReadOnlyList<SheetTrait> FaceTraits(CharDef def, CardFace face, int cardLevel) =>
            def.Traits
                .Where(t => t.Slot != TraitSlot.Lv1 && !IsShared(t.Slot) && t.AppliesTo(face))
                .OrderBy(t => (int)t.Slot).ThenBy(t => FaceOrder(t.Face))
                .Select(t => Row(t, cardLevel))
                .ToList();

        /// <summary>「两面」条:Lv3、Lv4 各一行,分面的两条合在同一行的 Defs 里(先攻后五行)。不含 Lv1。</summary>
        public static IReadOnlyList<SheetTrait> SharedTraits(CharDef def, int cardLevel) =>
            def.Traits
                .Where(t => IsShared(t.Slot))
                .GroupBy(t => t.Slot)
                .OrderBy(g => (int)g.Key)
                .Select(g => SharedRow(g.Key, g, cardLevel))
                .ToList();

        /// <summary>恰好在 <paramref name="level"/> 解锁的行(升级预览「新解锁」):Lv3 / Lv4 一行(两面合并),
        /// Lv5 / Lv6 / Lv8 两行(先攻后五行),其余空。行一律 Unlocked。Lv1 不算(没有效果)。</summary>
        public static IReadOnlyList<SheetTrait> UnlockedAt(CharDef def, int level) => RowsAt(def, level, level);

        /// <summary><paramref name="afterLevel"/> 之后第一个有特性的等级及其全部行(行按 afterLevel 判定,均未解锁);
        /// 没有了返回 (0, 空表)(升级预览「下一级解锁在 LvN」)。</summary>
        public static (int Level, IReadOnlyList<SheetTrait> Traits) NextUnlock(CharDef def, int afterLevel)
        {
            for (int lv = Math.Max(afterLevel + 1, 2); lv <= MetaRules.MaxCardLevel; lv++)
            {
                var rows = RowsAt(def, lv, afterLevel);
                if (rows.Count > 0) return (lv, rows);
            }
            return (0, Array.Empty<SheetTrait>());
        }

        /// <summary>字卡详情全平铺(spec B §1.2):每个有特性的槽一行(升序),两面各一格。Both 特性两面各挂一份;
        /// 格的 <see cref="SheetTrait.Face"/> 是所在面。加载器保证同槽同面至多一条(ConfigLoader「同一槽位每个作用面只能有一条」)。</summary>
        public static IReadOnlyList<SheetLevelRow> LevelRows(CharDef def, int cardLevel) =>
            def.Traits.Select(t => t.Slot).Distinct().OrderBy(s => (int)s)
                .Select(slot => new SheetLevelRow(slot,
                    LevelCell(def, slot, CardFace.Attack, cardLevel),
                    LevelCell(def, slot, CardFace.Feature, cardLevel),
                    cardLevel >= (int)slot))
                .ToList();

        /// <summary>Lv1 行的正文效果(spec B §1.4):该面折叠后本体(Lv1 + Lv3 替换,不含 Lv4+),去掉伤害(伤害在数值格)。
        /// 木系五行面里是带本命的召唤效果,Presentation 取它的被动印本命句。未拥有(&lt; 1)按 1 级。</summary>
        public static IReadOnlyList<EffectDef> Lv1Effects(CharDef def, CardFace face, int cardLevel) =>
            FaceStats.FoldedBody(def, face, Math.Max(1, cardLevel), TraitSlot.Lv4)
                .Where(e => e.Kind != EffectKind.DamageSingle)
                .ToList();

        private static SheetTrait LevelCell(CharDef def, TraitSlot slot, CardFace face, int cardLevel)
        {
            var trait = def.Traits.FirstOrDefault(t => t.Slot == slot && t.AppliesTo(face));
            if (trait == null) return null;
            var mark = face == CardFace.Attack ? TraitFace.Attack : TraitFace.Feature;
            return new SheetTrait(slot, mark, new[] { trait }, cardLevel >= trait.UnlockLevel);
        }

        /// <summary>面头落点:把 <see cref="CardFaceRules.Landing"/> 的 flags 折成四种说法。
        /// 召唤(EmptySlot)优先,其次敌人,其次友方(自己或木灵),只剩自己 = SelfOnly。</summary>
        public static FaceTarget TargetOf(CharDef def, CardFace face, int cardLevel)
        {
            var landing = CardFaceRules.Landing(def, face, cardLevel);
            if ((landing & FaceLanding.EmptySlot) != 0) return FaceTarget.Summon;
            if ((landing & FaceLanding.Enemy) != 0) return FaceTarget.Enemy;
            if ((landing & FaceLanding.Summons) != 0) return FaceTarget.Ally;
            return FaceTarget.SelfOnly;
        }

        /// <summary>面头范围,按这一级**折叠后**的效果取(剑 Lv8 横扫千军会改成 Row):
        /// 召唤面 → null;有伤害 → 第一条 DamageSingle 的 Shape;没有伤害 → 有全体类效果(*All、pick All、TargetAll、
        /// 改形成全体的治疗)算 All,否则 Single。</summary>
        public static TargetArea? AreaOf(CharDef def, CardFace face, int cardLevel)
        {
            if (TargetOf(def, face, cardLevel) == FaceTarget.Summon) return null;
            var effects = TraitRules.CastEffects(def, BattleEngine.FaceOf(def, face == CardFace.Attack), cardLevel);
            foreach (var e in effects)
                if (e.Kind == EffectKind.DamageSingle) return e.Shape;
            foreach (var e in effects)
                if (IsAllTargeted(e)) return TargetArea.All;
            return TargetArea.Single;
        }

        private static bool IsAllTargeted(EffectDef e) =>
            e.Kind == EffectKind.BurnAll || e.Kind == EffectKind.HealAll || e.Kind == EffectKind.ShieldAll
            || e.TargetAll || e.Shape == TargetArea.All
            || (EffectPickRules.Supports(e.Kind) && EffectPickRules.Effective(e) == EffectPick.All);

        // ---- 内部 ----

        private static int FaceOrder(TraitFace face) => face switch
        {
            TraitFace.Attack => 0,
            TraitFace.Feature => 1,
            _ => 2,
        };

        private static SheetTrait Row(TraitDef t, int cardLevel) =>
            new SheetTrait(t.Slot, t.Face, new[] { t }, cardLevel >= t.UnlockLevel);

        private static SheetTrait SharedRow(TraitSlot slot, IEnumerable<TraitDef> defs, int cardLevel) =>
            new SheetTrait(slot, TraitFace.Both, defs.OrderBy(t => t.Face == TraitFace.Both ? -1 : FaceOrder(t.Face)).ToList(),
                cardLevel >= (int)slot);

        /// <summary>槽位等于 <paramref name="level"/> 的行:两面槽合一行,面槽一条一行(先攻后五行)。Lv1 不成行。</summary>
        private static IReadOnlyList<SheetTrait> RowsAt(CharDef def, int level, int cardLevel)
        {
            if (level <= (int)TraitSlot.Lv1) return Array.Empty<SheetTrait>();
            var atLevel = def.Traits.Where(t => t.UnlockLevel == level).ToList();
            if (atLevel.Count == 0) return Array.Empty<SheetTrait>();
            var slot = (TraitSlot)level;
            if (IsShared(slot)) return new[] { SharedRow(slot, atLevel, cardLevel) };
            return atLevel.OrderBy(t => FaceOrder(t.Face)).Select(t => Row(t, cardLevel)).ToList();
        }
    }
}
