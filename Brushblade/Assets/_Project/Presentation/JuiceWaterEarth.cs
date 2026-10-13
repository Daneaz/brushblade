using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>水土与嫁接的提示反馈(Plan E3 Task 5;WaterEarthCues / PartsAndGraft 稿 2026-10-11 拍板)。
    /// 套路同木系(JuiceWood 那份注释):载体先闪,名字从载体飘出,再跟数字。
    ///
    /// · 拦截(Q11):挡的那枚 chip(免疫 / 免疫灼烧 / 禁增益)闪 → 被挡的状态名签从它上面飘出(TriggerFx 状态名签,
    ///   战场 chip 色底、60% 透明、白删除线,Plan E4);浇熄转出的护盾数字跟在后面(Shield 那一支)。
    ///   不飘特性名:挡下那一刻状态本身就是理由。
    /// · 杜塞:Boss 放招(BossSkillCast 带 Source = SkillCut)时大招削弱 chip 闪、「杜塞」飘出,伤害照常。
    /// · 碎玉:不出 chip,护盾被打空反伤时(Damage 带 Source = ShieldShatter)玩家护盾角标闪、「碎玉」从角标飘出,再飘反伤数字。
    /// · 嫁接:立绘闪、治疗数字(Graft.Amount = 回复量)、换上的本命 chip 闪,「嫁接」(面印 生)从格子飘出。不弹文字条(Q8)。
    ///
    /// 特性名是游戏数据,经 <see cref="EffectCueOf"/> 从图谱取(Q12);取不到就不飘名字。
    /// 名签一律 TriggerFx 纸底 + 面印(JuiceTriggerFx,Plan E4);嫁接不是特性(落点关键词),印与名按 PartsAndGraft 稿单独给(生 + 嫁接)。</summary>
    public sealed partial class Juice
    {
        /// <summary>玩家底栏(状态 chip 与立绘护盾角标都在里面):拦截 / 浮屠的 chip 闪、碎玉的角标闪找这里。
        /// BattleView 传常驻的 _bottomRow。</summary>
        public Transform PlayerCell { get; set; }

        /// <summary>护盾数字跟在删除线后面的间隔、特性名与数字之间的间隔。</summary>
        private const float CueFollowGap = 0.18f;

        /// <summary>拦截(DebuffWarded):玩家 / 木灵身上的免疫 chip 闪,被挡的减益名删除线飘出;紧随的转盾 Shield 照常飘数字。
        /// 只在下一条真是同目标、有量的转盾 Shield 时停一拍(濯身 / 杜绝没有转盾,连挡多段不累积停顿)。</summary>
        private IEnumerator PlayWarded(IReadOnlyList<BattleEvent> events, int idx, Func<int, RectTransform> summonAnchor)
        {
            var e = events[idx];
            bool onSummon = e.TargetIndex >= 0;
            Transform root = onSummon ? SummonCellAt?.Invoke(e.TargetIndex) : PlayerCell;
            string icon = e.SecondIndex == (int)StatusKind.Burn ? "wardburn" : "ward";
            var kind = (StatusKind)e.Amount;
            // 减速与加速共用 SpeedModifier,按符号取名 / 取色:这里挡的是减益,传负值
            var chip = FlashIcon(root, icon);
            StatusLabel(kind, -1, struck: true,
                chip != null ? chip : onSummon ? summonAnchor?.Invoke(e.TargetIndex) : null, isPlayer: !onSummon);
            if (idx + 1 < events.Count && events[idx + 1] is { Kind: BattleEventKind.Shield, Amount: > 0 } next
                && next.TargetIndex == e.TargetIndex)
                yield return Beat(CueFollowGap);   // 浇熄转出的护盾数字跟在后面
        }

        /// <summary>拦截(BuffBlocked):敌人身上的禁增益 chip 闪,被挡的增益名删除线飘出。</summary>
        private void PlayBuffBlocked(BattleEvent e, Func<int, RectTransform> enemyAnchor)
        {
            var root = EnemyCellAt?.Invoke(e.TargetIndex);
            var kind = (StatusKind)e.Amount;
            var chip = FlashIcon(root, "buffblock");
            StatusLabel(kind, 1, struck: true, chip != null ? chip : enemyAnchor(e.TargetIndex));
        }

        /// <summary>杜塞:Boss 格的大招削弱 chip 闪,「杜塞」从 Boss 飘出。放招演出照常接在后面。</summary>
        private void PlaySkillCut(int enemyIndex, RectTransform enemy)
        {
            var chip = FlashIcon(EnemyCellAt?.Invoke(enemyIndex), "skillcut");
            TraitCueAt(EffectCueOf?.Invoke(EffectKind.SkillCut), chip != null ? chip : enemy);
        }

        /// <summary>碎玉 / 反震(TriggerFx C):玩家护盾角标闪,特性名从角标飘出;停一拍再在攻击者身上飘反伤数字(Damage 那一支照常)。</summary>
        private IEnumerator PlayShieldBadgeTrait(EffectKind kind)
        {
            var badge = FlashIcon(PlayerCell, "shield");
            TraitCueAt(EffectCueOf?.Invoke(kind), badge);
            yield return Beat(CueFollowGap);
        }

        /// <summary>敌人格上的某枚 chip 闪,特性名从它飘出(埋雷 / 上炎);chip 不在退回立绘。</summary>
        private void EnemyChipTrait(int enemyIndex, string icon, EffectKind kind, RectTransform enemy)
        {
            var chip = FlashIcon(EnemyCellAt?.Invoke(enemyIndex), icon);
            TraitCueAt(EffectCueOf?.Invoke(kind), chip != null ? chip : enemy);
        }

        /// <summary>嫁接(PartsAndGraft):立绘闪、治疗数字、换上的本命 chip 闪,「嫁接」(面印 生)从格子飘出 —— 同时发生。
        /// 出字路径先重绘再播(BattleView.ExecuteCast),新本命 chip 此刻已在格上;没有图标的本命不出 chip,不闪。</summary>
        private void PlayGraft(BattleEvent e, Func<int, RectTransform> summonAnchor, Func<int, SummonState> summonInfo)
        {
            int slot = e.SecondIndex;
            var glyph = summonAnchor?.Invoke(slot);
            HitReact(glyph, 0.5f);
            if (e.Amount > 0)
            {
                Popup($"+{e.Amount}", Theme.Jade, glyph);
                HealBloom(AnchorPoint(glyph));
            }
            FlashIcon(SummonCellAt?.Invoke(slot), SummonInfo.NatureChipIcon(summonInfo?.Invoke(slot)?.Passive));
            // 嫁接是落点关键词不是特性:印与名按稿单独给(生 + 嫁接,木字形色),样式同特性名签
            SealTag(parent => Ui.FaceSeal(parent, Strings.T("face.seal.wood"), Theme.GlyphColor(Element.Wood), Ui.FaceSealS),
                Strings.T("battle.landing.graft"), Theme.GlyphColor(Element.Wood), glyph);
            PlayClip(_healClip, 0.7f);
        }

        /// <summary>拆合播放的上下文(Plan E3 Task 6):BattleView 在播拆 / 合的事件前设好 —— 哪张字、拆合台的结果格
        /// (拆:新进池的部件;合:合出的字牌)。只有 <see cref="BattleEventKind.GlyphTrait"/> 读它们,而那只产自拆合。</summary>
        public CharDef ForgeChar { get; set; }
        public RectTransform ForgeResult { get; set; }

        /// <summary>特性名飘出后到效果落地之间的停顿:稿「特性名先飘,紧接着效果在战场上落地」。</summary>
        private const float GlyphCueLead = 0.35f;

        /// <summary>字形特性(PartsAndGraft):特性名签从结果格飘出,面印位是墨蓝「拆」/「合」(Ui.TraitSeal 按 Trigger 分派,Q13 只做飘字)。
        /// 名取自 def.Traits[Amount](游戏数据);取不到不飘、不停。印记类(双焰 / 火山)同样飘,之后没有战场效果。</summary>
        private IEnumerator PlayGlyphTrait(BattleEvent e)
        {
            var def = ForgeChar;
            if (def == null || e.Amount < 0 || e.Amount >= def.Traits.Count) yield break;
            if (TraitCueAt(new TraitCue(def, def.Traits[e.Amount]), ForgeResult) == null) yield break;   // 名取不到不飘、不停
            yield return Beat(GlyphCueLead);
        }
    }
}
