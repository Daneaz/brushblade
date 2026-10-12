using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>木系提示反馈(D2-木 Task 5,WoodCues 稿 2026-10-11 拍板)。稿的套路:**不新增动效**,本命触发 = 那枚本命
    /// chip 闪、特性名从它上面飘出,再跟数字或状态名;木灵阵亡时触发的(秋实 / 落木 / 藻荇)从阵亡那一格飘出;
    /// 进池 / 进库的让新进的那一件闪一下(BattleView 的 MarkFresh 光晕)。
    ///
    /// 特性名是游戏数据,不进字符串表 —— 由 BattleView 接到图谱(<see cref="NatureCueOf"/> / <see cref="EffectCueOf"/>),
    /// 取不到时不飘名字。飘字样式 = TriggerFx 特性名签(纸底 + 面印,JuiceTriggerFx,Plan E4)。
    ///
    /// 「chip 闪」分两种时机:当场就在格子上的(守护)直接闪;这一批事件里才挂上、要等重绘才出现的
    /// (披荆的流血、藻荇的减速、绿潮的嘲讽、光环挂上)经 <see cref="ChipCue"/> 交给 BattleView,重绘后再闪。</summary>
    public sealed partial class Juice
    {
        /// <summary>chip 闪的落点种类。</summary>
        public enum CueOn { Enemy, Summon, Player }

        /// <summary>本命特性(按本命字段在图谱里找):has → 字 + 特性;取不到 null。</summary>
        public Func<Func<SummonPassive, bool>, TraitCue?> NatureCueOf { get; set; }

        /// <summary>效果特性(按效果种类在图谱里找,绿潮 / 折桂 / 杜塞 …):kind → 字 + 特性;取不到 null。</summary>
        public Func<EffectKind, TraitCue?> EffectCueOf { get; set; }

        /// <summary>第 k 格木灵的整格(chip 在格里):守护 chip 当场闪、绿潮的小藻格淡出用。</summary>
        public Func<int, RectTransform> SummonCellAt { get; set; }

        /// <summary>第 i 只敌人的整格(chip 在格里):攀附那一下被缠 chip 当场闪。</summary>
        public Func<int, RectTransform> EnemyCellAt { get; set; }

        /// <summary>重绘后再闪的 chip:(落点种类, 下标, 图标)。BattleView 记下,重绘完逐条闪。</summary>
        public Action<CueOn, int, string> ChipCue { get; set; }

        /// <summary>绿潮:小藻依次缩小飞进藻灵的间隔(稿 0.12s)。</summary>
        private const float MergeStagger = 0.12f;
        private const float MergeFly = 0.3f;

        /// <summary>某个根节点下、图标为 <paramref name="iconKey"/> 的 chip 闪一下(TriggerFx 载体闪,<see cref="FlashCarrier"/>)。
        /// 按图标 Sprite 找;PNG 缺失走兜底字的 chip 找不到,安静跳过。返回首个命中的 chip(飘字锚点用),没有返回 null。</summary>
        public RectTransform FlashIcon(Transform root, string iconKey)
        {
            if (root == null) return null;
            var sprite = Icons.Get(iconKey);
            if (sprite == null) return null;
            RectTransform first = null;
            foreach (var image in root.GetComponentsInChildren<Image>())
                if (image.sprite == sprite && image.transform.parent is RectTransform chip)
                {
                    first ??= chip;
                    FlashCarrier(chip);
                }
            return first;
        }

        /// <summary>守护(W5):与玩家那一下**同时**飘伤害 —— 由 EnemyAttack 那一拍触达时调用。柘灵飘自己的那份(盾吃掉的分账同玩家侧),
        /// 守护 chip 闪,「守护」从它上面飘出;不画连线。</summary>
        private void PlayGuard(BattleEvent e, Func<int, RectTransform> summonAnchor, Color foe, Action<BattleEvent> onImpact)
        {
            var tank = summonAnchor?.Invoke(e.SecondIndex);
            int hpLoss = e.Amount - e.Absorbed;
            if (e.Absorbed <= 0) Popup($"-{e.Amount}", foe, tank);
            else if (hpLoss <= 0) Popup(Strings.T("juice.popup.shield_absorbed", ("absorbed", e.Absorbed)), Theme.SplitBlue, tank);
            else Popup(Strings.T("juice.popup.shield_and_hp_loss", ("absorbed", e.Absorbed), ("hpLoss", hpLoss)), foe, tank, small: true);
            HitReact(tank, 0.6f);
            SummonViewOf(tank)?.PlayHit();
            var chip = FlashIcon(SummonCellAt?.Invoke(e.SecondIndex), "guard");
            TraitCueAt(NatureCueOf?.Invoke(p => p.Guard > 0), chip != null ? chip : tank);   // 从 chip 飘,chip 不在退回立绘
            onImpact?.Invoke(e);   // 触达才扣柘灵血条
        }

        /// <summary>保命(扎根,StatusChips 稿):木灵的保命 chip 闪,「扎根」(面印 生)从 chip 飘出;血停在 1 由前一条
        /// 伤害事件的 OnImpact 完成,chip 消失交给随后的重绘。只会在敌人回合触发(先播后重绘,chip 还在);chip 不在退回立绘。</summary>
        private void PlayEndure(BattleEvent e, Func<int, RectTransform> summonAnchor)
        {
            var chip = FlashIcon(SummonCellAt?.Invoke(e.TargetIndex), "lastlife");
            TraitCueAt(EffectCueOf?.Invoke(EffectKind.Endure), chip != null ? chip : summonAnchor?.Invoke(e.TargetIndex));
        }

        /// <summary>攀附(W3,WoodCues 稿「它行动:被缠 chip 闪,挨藤灵一下」):敌人那一拍开头藤灵打它之前,
        /// 它身上的被缠 chip 闪、「攀附」从它上面飘出。藤灵扑过去那一下沿用藤灵自己的出手动作(SummonAttack)。</summary>
        private void PlayBindStrike(int enemyIndex, RectTransform enemy)
        {
            var chip = FlashIcon(EnemyCellAt?.Invoke(enemyIndex), "bind");
            TraitCueAt(EffectCueOf?.Invoke(EffectKind.Bind), chip != null ? chip : enemy);
        }

        /// <summary>绿潮(W10):这一组 SummonMerged 依次(间隔 0.12s)把小藻缩小飞进藻灵,每进一只藻灵血条长一截(onImpact);
        /// 全部进完「绿潮」从藻灵飘出,嘲讽 chip 等重绘后闪。只用位移 + 缩放。</summary>
        private IEnumerator PlayMerge(List<BattleEvent> group, Func<int, RectTransform> summonAnchor, Action<BattleEvent> onImpact)
        {
            if (group.Count == 0) yield break;
            int receiver = group[0].SecondIndex;
            var to = summonAnchor?.Invoke(receiver);
            foreach (var e in group)
            {
                var from = summonAnchor?.Invoke(e.TargetIndex);
                var cell = SummonCellAt?.Invoke(e.TargetIndex);
                if (from != null && to != null) StartCoroutine(MergeFlyRoutine(from, cell, to.position));
                yield return Beat(MergeStagger);
                StartCoroutine(MergeLand(e, to, onImpact));
            }
            yield return Beat(MergeFly);
            FlashPortrait(to);   // 载体是藻灵立绘(稿 .pt.flash):不缩放,加亮 + 赭金发光
            TraitCueAt(EffectCueOf?.Invoke(EffectKind.SummonMerge), to);
            ChipCue?.Invoke(CueOn.Summon, receiver, "taunt");
        }

        private IEnumerator MergeLand(BattleEvent e, RectTransform to, Action<BattleEvent> onImpact)
        {
            yield return Beat(MergeFly);
            onImpact?.Invoke(e);   // 藻灵血条往右长一截
            if (to != null) HitReact(to, 0.4f);
        }

        /// <summary>一只小藻:立绘缩小着飞向藻灵,所在格同时淡出(格子本身在布局组里,不挪它)。</summary>
        private IEnumerator MergeFlyRoutine(RectTransform glyph, RectTransform cell, Vector3 target)
        {
            var lift = LiftAbove(glyph);
            Vector3 start = glyph.position;
            CanvasGroup fade = null;
            if (cell != null && !cell.TryGetComponent(out fade)) fade = cell.gameObject.AddComponent<CanvasGroup>();
            yield return Tween(MergeFly, k =>
            {
                if (glyph != null)
                {
                    glyph.position = Vector3.Lerp(start, target, k * k);
                    float s = Mathf.Lerp(1f, 0.2f, k);
                    glyph.localScale = new Vector3(s, s, 1f);
                }
                if (fade != null) fade.alpha = 1f - k;
            });
            if (glyph != null) glyph.localScale = Vector3.zero;   // 落定:这一格等收尾重绘时清掉
            if (lift != null) { _lifted.Remove(lift); Destroy(lift); }
        }
    }
}
