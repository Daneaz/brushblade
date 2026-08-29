using System.Collections;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>账户墨锭的增减飘字(2026-08-29)。顶栏数字变了就在它上方浮一个
    /// 「+120」/「−80」,进账翠玉、支出朱砂。
    ///
    /// **为什么是「观察」而不是「事件」**:`MetaState.Ink` 是普通字段,买卡/开箱/看广告/
    /// 塔结算各处都直接 `+=`,没有任何一处会通知 UI。把它改成带事件的属性要动 Data 层
    /// 和全部写入点,而外层五个页签本来就是**每次交互全量重建**的 —— 重建时拿当前值与
    /// 上次见到的值一比,delta 自然就有了,一行都不用碰经济侧。
    ///
    /// 代价是「变化发生时没开着任何顶栏」的那些增减(后台奖励、塔内滚存)会攒到下次
    /// 打开时一次性飘出来。这反而是想要的:玩家回到主界面正好看见这一趟挣了多少。
    ///
    /// ⚠ 服务的是**玩家余额**这一条线:外层五个顶栏的 `MetaState.Ink`,以及局内右上的
    /// `RunEngine.AvailableInk`。2026-08-30 半额结算取消后这两个数字同源了 —— 层清算与
    /// 字摊收支都记在 run.EarnedInk 上、随赚随结进账户,每条离塔路径又都先 CommitEventInk,
    /// 所以切换视图时两边必然相等,共用这份静态 `_lastSeen` 不会互相误报。
    /// 别把**别的账本**接进来:结算弹窗上的「这趟挣了 N」、安全层的累计、商品价签都不是余额,
    /// 接进来就会飘出凭空的增减。</summary>
    public sealed class InkPulse : MonoBehaviour
    {
        private const float Duration = 0.9f;
        private const float Rise = 52f;      // 总上浮距离(px,1600×900 参考分辨率下)
        private const float PopIn = 0.12f;   // 冒头那一下的放大时长

        /// <summary>上次见到的账户墨锭。`int.MinValue` = 本次进程还没见过 ——
        /// 首次显示不飘,否则每次冷启动都会当着玩家的面「+全部身家」。</summary>
        private static int _lastSeen = int.MinValue;

        /// <summary>顶栏每次重建都调一次。anchor = 墨锭标签本身,飘字从它上方冒出来。</summary>
        public static void Observe(RectTransform anchor, int ink)
        {
            int delta = _lastSeen == int.MinValue ? 0 : ink - _lastSeen;
            _lastSeen = ink;
            if (delta == 0 || anchor == null) return;

            // 挂到 Canvas 而不是顶栏下:顶栏是 Ui.Clear 的清理对象,下一次重建会连飘字
            // 一起销毁 —— 而「扣墨锭」与「重建顶栏」恰恰总是同一次点击里发生的
            var canvas = anchor.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("InkPulse", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var label = go.AddComponent<Text>();
            label.font = Theme.TitleFont;
            label.fontSize = 28;
            // 两条各自写全 —— key 传三元表达式会被 StringsTableTests 当成孤儿(它只认字面量)
            label.text = delta > 0
                ? Strings.T("ui.ink_pulse.gain", ("delta", delta))
                : Strings.T("ui.ink_pulse.spend", ("delta", -delta));
            label.color = delta > 0 ? Theme.Jade : Theme.Cinnabar;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            go.AddComponent<InkPulse>().StartCoroutine(Float(anchor, (RectTransform)go.transform, label));
        }

        private static IEnumerator Float(RectTransform anchor, RectTransform rect, Text label)
        {
            // 等一帧:顶栏是 LayoutGroup 排的,建出来的这一帧 anchor.position 还是 (0,0)
            yield return null;
            if (rect == null) yield break;
            if (anchor != null) rect.position = anchor.position;
            rect.anchoredPosition += new Vector2(0, 22f);

            float t = 0f;
            while (t < Duration && rect != null)
            {
                t += Time.unscaledDeltaTime;   // 弹窗/暂停时 timeScale 可能是 0
                float p = Mathf.Clamp01(t / Duration);
                rect.anchoredPosition += new Vector2(0, Rise / Duration * Time.unscaledDeltaTime);
                // 冒头先放大到 1.15 再回落,尾段才开始褪 —— 一路线性淡出会看不清数字
                float scale = t < PopIn ? Mathf.Lerp(0.7f, 1.15f, t / PopIn)
                    : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - PopIn) / PopIn));
                rect.localScale = new Vector3(scale, scale, 1f);
                var c = label.color;
                c.a = p < 0.55f ? 1f : 1f - (p - 0.55f) / 0.45f;
                label.color = c;
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }
    }
}
