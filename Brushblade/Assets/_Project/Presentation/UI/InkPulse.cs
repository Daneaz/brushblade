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
        private const float Duration = 1.6f;    // 2026-08-30:0.9 太短,数字还没读完就没了
        private const float Rise = 46f;         // 上行距离(px,1600×900 参考分辨率下)
        private const float StartBelow = 40f;   // 起点压在计数器下方多少 —— 见 Observe 里的说明
        private const float PopIn = 0.16f;      // 冒头那一下的放大时长
        private const float Hold = 0.7f;        // 前 70% 全不透明,尾段才褪
        private const float SizeScale = 1.7f;   // 相对顶栏字号的倍数:要一眼看见,又不至于糊住半屏

        /// <summary>上次见到的账户墨锭。`int.MinValue` = 本次进程还没见过 ——
        /// 首次显示不飘,否则每次冷启动都会当着玩家的面「+全部身家」。</summary>
        private static int _lastSeen = int.MinValue;

        /// <summary>顶栏每次重建都调一次。anchor = 墨锭标签本身,飘字从它下方往上汇入。</summary>
        public static void Observe(RectTransform anchor, int ink, int fontSize = 20)
        {
            int delta = _lastSeen == int.MinValue ? 0 : ink - _lastSeen;
            _lastSeen = ink;
            if (delta == 0 || anchor == null) return;

            // 挂到 Canvas 而不是顶栏下:顶栏是 Ui.Clear 的清理对象,下一次重建会连飘字
            // 一起销毁 —— 而「扣墨锭」与「重建顶栏」恰恰总是同一次点击里发生的
            var canvas = anchor.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 立刻把布局排完,当场取 anchor 的位置(2026-08-30)。原先是在协程里等一帧再读 ——
            // 那一帧里顶栏可能已经被下一次 Refresh 清掉,anchor 变成已销毁对象,`anchor != null`
            // 判 false 就跳过定位,飘字于是留在画布正中央。奇遇选完选项那一下正是连着两次重绘。
            Canvas.ForceUpdateCanvases();

            var go = new GameObject("InkPulse", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var label = go.AddComponent<Text>();
            label.font = Theme.TitleFont;
            label.fontSize = Mathf.RoundToInt(fontSize * SizeScale);
            // 两条各自写全 —— key 传三元表达式会被 StringsTableTests 当成孤儿(它只认字面量)
            label.text = delta > 0
                ? Strings.T("ui.ink_pulse.gain", ("delta", delta))
                : Strings.T("ui.ink_pulse.spend", ("delta", -delta));
            label.color = delta > 0 ? Theme.Jade : Theme.Cinnabar;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            // 宣纸色描边:翠玉/朱砂压在深色页签或水墨立绘上都还读得清
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(Theme.Paper.r, Theme.Paper.g, Theme.Paper.b, 0.92f);
            outline.effectDistance = new Vector2(2.2f, -2.2f);

            var rect = (RectTransform)go.transform;
            rect.position = anchor.position;
            // 起点压到计数器**下方**,再往上汇入(2026-08-30)。原先是从上方往上飘 ——
            // 而所有顶栏都贴着画布顶边,52px 的行程有一大半在屏幕外:主界面只看得见开头一截
            // (于是「不够久」),局内顶栏更靠边,整个飘字压根看不见(于是「奇遇没动效」)。
            // 现在的轨迹是「墨锭从下面飞进计数器」,全程可见,顺带把隐喻也说对了。
            var start = rect.anchoredPosition + new Vector2(0, -StartBelow);
            // 再钳一道:万一日后哪个顶栏挪了位置,也不会把飘字顶出可视区
            float maxY = ((RectTransform)rect.parent).rect.height / 2f - 8f;
            if (start.y + Rise > maxY) start.y = maxY - Rise;
            rect.anchoredPosition = start;

            go.AddComponent<InkPulse>().StartCoroutine(Float(rect, label, start));
        }

        private static IEnumerator Float(RectTransform rect, Text label, Vector2 start)
        {
            float t = 0f;
            while (t < Duration && rect != null)
            {
                t += Time.unscaledDeltaTime;   // 弹窗/暂停时 timeScale 可能是 0
                float p = Mathf.Clamp01(t / Duration);
                // 缓出:一冒头就窜上去,后半程几乎停住 —— 停住的那段正好是读数字的时间。
                // 按绝对位置算而不是每帧累加:累加会随帧率漂,低帧时行程被吃掉一截。
                float ease = 1f - (1f - p) * (1f - p) * (1f - p);
                rect.anchoredPosition = start + new Vector2(0, Rise * ease);
                // 冒头先放大到 1.3 再回落,尾段才褪 —— 一路线性淡出会看不清数字
                float scale = t < PopIn ? Mathf.Lerp(0.6f, 1.3f, t / PopIn)
                    : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((t - PopIn) / PopIn));
                rect.localScale = new Vector3(scale, scale, 1f);
                var c = label.color;
                c.a = p < Hold ? 1f : 1f - (p - Hold) / (1f - Hold);
                label.color = c;
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }
    }
}
