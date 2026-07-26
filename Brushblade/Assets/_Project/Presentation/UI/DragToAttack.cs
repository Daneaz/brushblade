using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>拖字打人(2026-07-26):把字牌拖到敌人身上松手 = 出字攻击该敌人。
    /// 拖动中显示跟随指针的字影;松手把屏幕坐标交给外层判定落在哪个敌人上
    /// (本组件不认识敌人,只管手势与反馈)。
    /// 与"点选 → 点敌人"并存:uGUI 一旦进入拖拽就清掉 eligibleForClick,不会重复触发点击。</summary>
    public sealed class DragToAttack : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private string _glyph;
        private Color _color;
        private Func<bool> _canDrag;
        private Action<Vector2> _onDrop;
        private RectTransform _ghost;

        /// <param name="canDrag">此刻能否拖(结算动画中/非玩家回合返回 false)。</param>
        /// <param name="onDrop">松手时回调,参数为松手处的屏幕坐标。</param>
        public static void Attach(GameObject target, string glyph, Color color,
            Func<bool> canDrag, Action<Vector2> onDrop)
        {
            var drag = target.AddComponent<DragToAttack>();
            drag._glyph = glyph;
            drag._color = color;
            drag._canDrag = canDrag;
            drag._onDrop = onDrop;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_canDrag != null && !_canDrag()) return;
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
            // 必须用子集字体:四叠字是 PUA 码位(U+E625/E626),字形由部件 2×2 拼合而来,
            // 只存在于我们自己生成的子集里 —— 走系统字体会拖出一个空白(与字牌、飞牌同源)
            label.font = Theme.TitleFont;
            label.fontSize = 44;
            label.fontStyle = FontStyle.Bold;
            label.text = _glyph;
            label.color = new Color(_color.r, _color.g, _color.b, 0.85f);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false; // 别挡住底下敌人格的射线

            _ghost.position = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_ghost != null) _ghost.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost == null) return; // 起拖时被拒(结算中):什么也不做
            Destroy(_ghost.gameObject);
            _ghost = null;
            _onDrop?.Invoke(eventData.position);
        }

        private void OnDisable() // 字牌被重绘销毁时字影不能留在屏幕上
        {
            if (_ghost == null) return;
            Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }
}
