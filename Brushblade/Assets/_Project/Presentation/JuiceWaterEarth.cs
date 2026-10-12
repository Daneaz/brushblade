using System;
using System.Collections;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>水土与嫁接的提示反馈(Plan E3 Task 5;WaterEarthCues / PartsAndGraft 稿 2026-10-11 拍板)。
    /// 套路同木系(JuiceWood 那份注释):载体先闪,名字从载体飘出,再跟数字。
    ///
    /// · 拦截(Q11):挡的那枚 chip(免疫 / 免疫灼烧 / 禁增益)闪 → 被挡的状态名从它上面飘出,
    ///   状态色、60% 透明、压一条横线当删除线(旧 Text 不认 &lt;s&gt;);浇熄转出的护盾数字跟在后面(Shield 那一支)。
    ///   不飘特性名:挡下那一刻状态本身就是理由。
    /// · 杜塞:Boss 放招(BossSkillCast 带 Source = SkillCut)时大招削弱 chip 闪、「杜塞」飘出,伤害照常。
    /// · 浮屠:回合初护盾回流(Shield 带 Source = ShieldCarry)时护盾回流 chip 闪、「浮屠」飘出,再飘护盾数字。
    /// · 碎玉:不出 chip,护盾被打空反伤时(Damage 带 Source = ShieldShatter)玩家护盾角标闪、「碎玉」从角标飘出,再飘反伤数字。
    /// · 嫁接:立绘闪、治疗数字(Graft.Amount = 回复量)、换上的本命 chip 闪,「嫁接」(面印 生)从格子飘出。不弹文字条(Q8)。
    ///
    /// 特性名是游戏数据,经 <see cref="EffectNameOf"/> 从图谱取(Q12);取不到就不飘名字。
    /// 特性名只做现状 Popup 样式;TriggerFx 稿的「纸底 + 面印」全套归 Plan E —— 嫁接的「生」印是 PartsAndGraft 稿明写的,单独做。</summary>
    public sealed partial class Juice
    {
        /// <summary>玩家底栏(状态 chip 与立绘护盾角标都在里面):拦截 / 浮屠的 chip 闪、碎玉的角标闪找这里。
        /// BattleView 传常驻的 _bottomRow。</summary>
        public Transform PlayerCell { get; set; }

        /// <summary>被挡状态名的透明度(稿:降到 60%)。</summary>
        private const float StruckAlpha = 0.6f;

        /// <summary>护盾数字跟在删除线后面的间隔、特性名与数字之间的间隔。</summary>
        private const float CueFollowGap = 0.18f;

        private static readonly Color EarthName = Theme.GlyphColor(Element.Earth);

        /// <summary>某个根节点下第一枚图标为 <paramref name="iconKey"/> 的 chip(同 <see cref="FlashIcon"/> 的找法);没有返回 null。</summary>
        private static RectTransform IconChipIn(Transform root, string iconKey)
        {
            if (root == null || iconKey == null) return null;
            var sprite = Icons.Get(iconKey);
            if (sprite == null) return null;
            foreach (var image in root.GetComponentsInChildren<Image>())
                if (image.sprite == sprite && image.transform.parent is RectTransform chip)
                    return chip;
            return null;
        }

        /// <summary>拦截(DebuffWarded):玩家 / 木灵身上的免疫 chip 闪,被挡的减益名删除线飘出;紧随的转盾 Shield 照常飘数字。</summary>
        private IEnumerator PlayWarded(BattleEvent e, Func<int, RectTransform> summonAnchor)
        {
            bool onSummon = e.TargetIndex >= 0;
            Transform root = onSummon ? SummonCellAt?.Invoke(e.TargetIndex) : PlayerCell;
            string icon = e.SecondIndex == (int)StatusKind.Burn ? "wardburn" : "ward";
            var kind = (StatusKind)e.Amount;
            // 减速与加速共用 SpeedModifier,按符号取名:这里挡的是减益,传负值
            string name = StatusText.Of(kind, -1, 0, isPlayer: !onSummon).Name;
            var chip = IconChipIn(root, icon);
            FlashIcon(root, icon);
            PopupStruck(name, UnitDetailChip.ColorFor(kind, -1),
                chip != null ? chip : onSummon ? summonAnchor?.Invoke(e.TargetIndex) : null);
            yield return Beat(CueFollowGap);   // 浇熄转出的护盾数字跟在后面
        }

        /// <summary>拦截(BuffBlocked):敌人身上的禁增益 chip 闪,被挡的增益名删除线飘出。</summary>
        private void PlayBuffBlocked(BattleEvent e, Func<int, RectTransform> enemyAnchor)
        {
            var root = EnemyCellAt?.Invoke(e.TargetIndex);
            var kind = (StatusKind)e.Amount;
            var chip = IconChipIn(root, "buffblock");
            FlashIcon(root, "buffblock");
            PopupStruck(StatusText.Of(kind, 1, 0).Name, UnitDetailChip.ColorFor(kind, 1),
                chip != null ? chip : enemyAnchor(e.TargetIndex));
        }

        /// <summary>杜塞:Boss 格的大招削弱 chip 闪,「杜塞」从 Boss 飘出。放招演出照常接在后面。</summary>
        private void PlaySkillCut(int enemyIndex, RectTransform enemy)
        {
            FlashIcon(EnemyCellAt?.Invoke(enemyIndex), "skillcut");
            EarthTraitName(EffectNameOf?.Invoke(EffectKind.SkillCut), enemy);
        }

        /// <summary>浮屠:护盾回流 chip 闪(数字 3 → 2 靠随后的重绘)、护盾角标闪,「浮屠」从 chip 飘出;停一拍再飘护盾数字。</summary>
        private IEnumerator PlayShieldCarry()
        {
            var chip = IconChipIn(PlayerCell, "shieldcarry");
            FlashIcon(PlayerCell, "shieldcarry");
            FlashIcon(PlayerCell, "shield");
            EarthTraitName(EffectNameOf?.Invoke(EffectKind.ShieldCarry), chip);
            yield return Beat(CueFollowGap);
        }

        /// <summary>碎玉:玩家护盾角标闪,「碎玉」从角标飘出;停一拍再在攻击者身上飘反伤数字(Damage 那一支照常)。</summary>
        private IEnumerator PlayShieldShatter()
        {
            var badge = IconChipIn(PlayerCell, "shield");
            FlashIcon(PlayerCell, "shield");
            EarthTraitName(EffectNameOf?.Invoke(EffectKind.ShieldShatter), badge);
            yield return Beat(CueFollowGap);
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
            PopupSealed(Strings.T("face.seal.wood"), Theme.GlyphColor(Element.Wood),
                Strings.T("battle.landing.graft"), WoodName, glyph);
            PlayClip(_healClip, 0.7f);
        }

        /// <summary>土系特性名飘字(小号、土字形色)。名取不到就不飘。</summary>
        private void EarthTraitName(string name, RectTransform anchor)
        {
            if (!string.IsNullOrEmpty(name)) Popup(name, EarthName, anchor, small: true);
        }

        /// <summary>被挡下的状态名(Q11):照常配色、整体 60% 透明,压一条同色横线当删除线,随飘字一起淡出。名取不到就不飘。</summary>
        private void PopupStruck(string text, Color color, RectTransform anchor)
        {
            if (string.IsNullOrEmpty(text)) return;
            var label = Popup(text, color, anchor, small: true);
            var group = label.gameObject.AddComponent<CanvasGroup>();   // 新建的飘字物件,不会已有
            group.alpha = StruckAlpha;
            group.blocksRaycasts = false;
            group.interactable = false;

            var line = new GameObject("Strike", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(label.transform, false);
            var rect = (RectTransform)line.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(label.preferredWidth + 4f, Mathf.Max(2f, label.fontSize * 0.1f));
            var image = line.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            StartCoroutine(FollowAlpha(label, new Graphic[] { image }));
        }

        /// <summary>带面印的名字飘字(PartsAndGraft 嫁接:「生」印 + 「嫁接」):印放在字左侧,随飘字一起淡出。</summary>
        private void PopupSealed(string seal, Color sealColor, string text, Color color, RectTransform anchor)
        {
            var label = Popup(text, color, anchor, small: true);
            var stamp = Ui.FaceSeal(label.transform, seal, sealColor, Ui.FaceSealXs);
            ((RectTransform)stamp.transform).anchoredPosition =
                new Vector2(-(label.preferredWidth / 2f + 4f + Ui.FaceSealXs / 2f), 0f);
            StartCoroutine(FollowAlpha(label, stamp.GetComponentsInChildren<Graphic>()));
        }

        /// <summary>飘字上的附件(删除线 / 面印)跟着字的 alpha 走:FloatAndFade 只淡 Text 本身。</summary>
        private static IEnumerator FollowAlpha(Text label, Graphic[] parts)
        {
            while (label != null)
            {
                float a = label.color.a;
                foreach (var part in parts)
                    if (part != null)
                    {
                        var c = part.color;
                        c.a = a;
                        part.color = c;
                    }
                yield return null;
            }
        }
    }
}
