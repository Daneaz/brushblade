using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>翻牌:牌背立起来 → 换面 → 正面展开(2026-09-03,开箱结果)。
    ///
    /// 上一版是「0.5 → 1 缩放弹入」:牌一出现就是正面,玩家没有「还没看见」的那一拍,
    /// 开箱最值钱的悬念被跳过了。现在牌先扣着,再一张张翻开。
    ///
    /// ⚠ 用 **scaleX 缩到 0 再展开**,不是绕 Y 轴真旋转 —— 与 <see cref="InkPulse"/> 同一条理由:
    /// uGUI 没有厚度,真旋转到 90° 附近会因背面剔除闪一下,而缩放版本在任何 Canvas 模式下都一样。
    ///
    /// 稀有度高的多停一拍:金档以上翻开后按住 <see cref="RareHold"/> 再翻下一张 ——
    /// 一箱十二张一个节拍翻完的话,那张橙卡与旁边的白卡在时间上毫无区别。</summary>
    public sealed class CardFlip : MonoBehaviour
    {
        public const float HalfFlip = 0.15f;   // 半圈(立起来 / 展开)
        public const float Gap = 0.06f;        // 两张之间的间隔
        public const float RareHold = 0.34f;   // 金档以上翻开后多停这么久

        /// <summary>翻开一张:<paramref name="back"/> 收起、<paramref name="front"/> 亮出来。
        /// 协程挂在调用方身上 —— 牌被重绘销毁时,这条协程自己会在 null 检查处退出。</summary>
        public static IEnumerator Flip(RectTransform card, GameObject back, GameObject front)
        {
            if (card == null) yield break;
            back.SetActive(true);
            front.SetActive(false);
            yield return Half(card, 1f, 0f);
            if (card == null) yield break;
            back.SetActive(false);
            front.SetActive(true);
            yield return Half(card, 0f, 1f);
            if (card != null) card.localScale = Vector3.one;
        }

        private static IEnumerator Half(RectTransform rect, float from, float to)
        {
            float t = 0f;
            while (t < HalfFlip && rect != null)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / HalfFlip);
                float ease = p * p * (3f - 2f * p);   // smoothstep:两端慢中间快,像真在翻
                rect.localScale = new Vector3(Mathf.Lerp(from, to, ease), 1f, 1f);
                yield return null;
            }
            if (rect != null) rect.localScale = new Vector3(to, 1f, 1f);
        }

        /// <summary>牌背:宣纸底 + 一枚居中的印记。七档共用一张背面 —— 背面若透出稀有度,
        /// 翻开前就已经把答案说了。</summary>
        public static GameObject Back(Transform parent, Vector2 size, string mark)
        {
            var back = Ui.OutlinedPanel(parent, "Back", Theme.InkSoft, Theme.Ink, 14, 3).gameObject;
            Ui.Stretch((RectTransform)back.transform);
            var label = Ui.ThemedLabel(back.transform, mark,
                Mathf.RoundToInt(size.y * 0.3f), Theme.Paper, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            return back;
        }
    }
}
