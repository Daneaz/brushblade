using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>拖牌进出出阵表(2026-09-03,卡组页)。把网格里的字拖到右栏松手 = 编入出阵;
    /// 把出阵格拖出右栏松手 = 卸下。本组件只管手势与字影,落点判定交给外层
    /// (与 <see cref="DragToAttack"/> 同一条分工:组件不认识出阵表)。
    ///
    /// ⚠ **竖向手势必须让给列表滚动。** 收集网格坐在 <see cref="ScrollRect"/> 里,而 uGUI 的
    /// 拖拽只发给**起拖的那个对象**往上找到的第一个 IDragHandler —— 牌上一挂这个组件,
    /// 竖着划就再也滚不动了(2026-09-03 刚修好「空白处拖不动」,这里不能把它拆回去)。
    /// 所以起拖那一刻按方向分流:竖向整条事件转发给祖先 ScrollRect,横向才算「拿起这张牌」。
    /// 判据用 <c>position − pressPosition</c>(按下到此刻的总位移)而不是单帧 delta:
    /// 单帧位移在阈值附近又小又抖,方向判不准。
    ///
    /// ⚠ 与 <see cref="DragToAttack"/> 同一条戒律:<paramref name="onDrop"/> 里几乎必然重绘,
    /// 会把本组件连同牌一起销毁,所以字影的收尾协程挂在**字影自己**身上
    /// (<see cref="GhostRelease"/>),不能挂在这边。</summary>
    public sealed class DragToDeck : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private string _glyph;
        private Color _color;
        private Action _onBeginDrag;
        private Action<Vector2> _onDrop;
        private ScrollRect _scroll;
        private bool _routedToScroll;   // 本次拖拽已判给列表滚动
        private RectTransform _ghost;

        /// <param name="onBeginDrag">真正起拖时回调一次(判给滚动时不回调)。用来点亮落点区。
        /// ⚠ 回调里不许触发重绘 —— 本组件一被销毁,OnEndDrag 就再也不来,字影会留在屏幕上。</param>
        /// <param name="onDrop">松手时回调,参数为松手处的屏幕坐标。</param>
        public static void Attach(GameObject target, string glyph, Color color,
            Action<Vector2> onDrop, Action onBeginDrag = null)
        {
            var drag = target.AddComponent<DragToDeck>();
            drag._glyph = glyph;
            drag._color = color;
            drag._onDrop = onDrop;
            drag._onBeginDrag = onBeginDrag;
            drag._scroll = target.GetComponentInParent<ScrollRect>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            var moved = eventData.position - eventData.pressPosition;
            if (_scroll != null && Mathf.Abs(moved.y) > Mathf.Abs(moved.x))
            {
                _routedToScroll = true;
                _scroll.OnBeginDrag(eventData);
                return;
            }

            var hold = GetComponent<HoldToPreview>();
            if (hold != null) hold.Cancel(); // 拖动中不弹长按详情

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling(); // 压在所有 UI 之上,跟手不被遮
            _ghost = (RectTransform)go.transform;
            _ghost.sizeDelta = new Vector2(72, 72);

            var label = go.AddComponent<Text>();
            // 必须用子集字体:四叠字是 PUA 码位,字形只存在于我们自己生成的子集里
            label.font = Theme.TitleFont;
            label.fontSize = 48;
            label.fontStyle = FontStyle.Bold;
            label.text = _glyph;
            label.color = new Color(_color.r, _color.g, _color.b, 0.9f);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false; // 别挡住底下落点区的射线

            _ghost.position = eventData.position;
            _onBeginDrag?.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_routedToScroll) { _scroll.OnDrag(eventData); return; }
            // ⚠ 守卫包住:起拖时若拿不到 Canvas,_ghost 保持 null,但 EventSystem 照样派发
            if (_ghost == null) return;
            _ghost.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_routedToScroll)
            {
                _routedToScroll = false;
                _scroll.OnEndDrag(eventData);
                return;
            }
            if (_ghost == null) return;
            var releasing = _ghost;
            _ghost = null;
            releasing.gameObject.AddComponent<GhostRelease>().Begin();
            _onDrop?.Invoke(eventData.position);
        }

        private void OnDisable() // 牌被重绘销毁时字影不能留在屏幕上
        {
            if (_ghost == null) return;
            Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }
}
