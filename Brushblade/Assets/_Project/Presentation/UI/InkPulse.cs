using System.Collections;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>墨锭数字的**翻牌**动效(2026-08-30 改版)。余额一变,顶栏那个数字就沿竖轴
    /// 翻过去亮出「+2」(进账翠玉 / 支出朱砂),停一下,再翻回来显示新余额。
    ///
    /// 上一版是往上飘一个「+2」的浮字。飘字有两个毛病:它是**另一个**东西,与那枚墨锭没有
    /// 视觉上的因果关系;而且顶栏贴着画布顶边,它没多少地方可飘。翻牌把增量和结果压在同一
    /// 枚数字上 —— 翻过去看见「挣了多少」,翻回来看见「现在有多少」,两件事一次说完,
    /// 也不再占用顶栏之外的任何空间。
    ///
    /// **为什么是「观察」而不是「事件」**:`MetaState.Ink` 是普通字段,买卡/开箱/看广告/
    /// 塔结算各处都直接 `+=`,没有任何一处会通知 UI。把它改成带事件的属性要动 Data 层
    /// 和全部写入点,而这些页签本来就是**每次交互全量重建**的 —— 重建时拿当前值与上次
    /// 见到的值一比,delta 自然就有了,一行都不用碰经济侧。
    ///
    /// 代价是「变化发生时没开着任何顶栏」的那些增减会攒到下次打开时一次性翻出来。
    /// 这反而是想要的:玩家回到主界面正好看见这一趟挣了多少。
    ///
    /// ⚠ 服务的是**玩家余额**这一条线:外层五个顶栏的 `MetaState.Ink`,以及局内右上的
    /// `RunEngine.AvailableInk`。2026-08-30 半额结算取消后这两个数字同源了 —— 层清算与
    /// 字摊收支都记在 run.EarnedInk 上、随赚随结进账户,每条离塔路径又都先 CommitEventInk,
    /// 所以切换视图时两边必然相等,共用这份静态 `_lastSeen` 不会互相误报。
    /// 别把**别的账本**接进来:结算弹窗上的「这趟挣了 N」、安全层的累计、商品价签都不是余额,
    /// 接进来就会翻出凭空的增减。</summary>
    public sealed class InkPulse : MonoBehaviour
    {
        private const float FlipHalf = 0.16f;   // 半圈翻转(1 → 0 或 0 → 1)的时长
        private const float HoldDelta = 0.85f;  // 「+2」那一面停留多久 —— 够读一眼,不拖节奏
        private const float DeltaScale = 1.15f; // 增量那一面稍微放大:一眼看出这是变化量不是余额

        /// <summary>上次见到的余额。`int.MinValue` = 本次进程还没见过 ——
        /// 首次显示不翻,否则每次冷启动都会当着玩家的面「+全部身家」。</summary>
        private static int _lastSeen = int.MinValue;

        /// <summary>顶栏每次重建都调一次。<paramref name="label"/> 就是那枚数字本身。</summary>
        public static void Observe(Text label, int ink)
        {
            int delta = _lastSeen == int.MinValue ? 0 : ink - _lastSeen;
            _lastSeen = ink;
            if (delta == 0 || label == null) return;

            // 挂在数字自己身上:它被重绘销毁时协程一起没,翻到一半的样子不会留在屏幕上 ——
            // 而重绘出来的新数字本来就是最终值,视觉上就是「翻转被打断,直接落到结果」。
            label.gameObject.AddComponent<InkPulse>().StartCoroutine(Flip(label, delta, ink));
        }

        private static IEnumerator Flip(Text label, int delta, int finalInk)
        {
            var rect = label.rectTransform;
            var restColor = label.color;
            // 两条各自写全 —— key 传三元表达式会被 StringsTableTests 当成孤儿(它只认字面量)
            string deltaText = delta > 0
                ? Strings.T("ui.ink_pulse.gain", ("delta", delta))
                : Strings.T("ui.ink_pulse.spend", ("delta", -delta));
            var deltaColor = delta > 0 ? Theme.Jade : Theme.Cinnabar;

            yield return HalfFlip(rect, 1f, 0f, 1f, 1f);       // 立起来:旧值转到看不见
            if (label == null) yield break;
            label.text = deltaText;                            // 背面写着变化量
            label.color = deltaColor;
            yield return HalfFlip(rect, 0f, 1f, 1f, DeltaScale);

            float held = 0f;
            while (held < HoldDelta && label != null)
            {
                held += Time.unscaledDeltaTime;                 // 弹窗/暂停时 timeScale 可能是 0
                yield return null;
            }
            if (label == null) yield break;

            yield return HalfFlip(rect, 1f, 0f, DeltaScale, DeltaScale);
            if (label == null) yield break;
            label.text = finalInk.ToString();                   // 翻回正面:新余额
            label.color = restColor;
            yield return HalfFlip(rect, 0f, 1f, DeltaScale, 1f);

            if (rect != null) rect.localScale = Vector3.one;    // 收干净:重绘不一定紧跟着来
            if (label != null) Destroy(label.GetComponent<InkPulse>());
        }

        /// <summary>半圈翻转:横向缩到 0(或从 0 展开),同时把整体尺寸从 fromScale 推向 toScale。
        /// 用 scaleX 而不是绕 Y 轴旋转 —— uGUI 的 Text 没有厚度,真旋转到 90° 附近会因为
        /// 背面剔除闪一下;缩放版本在任何 Canvas 模式下表现都一样。</summary>
        private static IEnumerator HalfFlip(RectTransform rect, float fromX, float toX,
            float fromScale, float toScale)
        {
            float t = 0f;
            while (t < FlipHalf && rect != null)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / FlipHalf);
                float ease = p * p * (3f - 2f * p);            // smoothstep:两端慢、中间快,像真在翻
                float scale = Mathf.Lerp(fromScale, toScale, ease);
                rect.localScale = new Vector3(Mathf.Lerp(fromX, toX, ease) * scale, scale, 1f);
                yield return null;
            }
            if (rect != null)
                rect.localScale = new Vector3(toX * toScale, toScale, 1f);
        }
    }
}
