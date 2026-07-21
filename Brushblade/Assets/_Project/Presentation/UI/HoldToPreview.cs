using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Brushblade.Presentation
{
    /// <summary>长按看详情(2026-07-21):按满阈值弹 preview,松手仍照常执行点击。
    /// preview 是模态,弹出后遮罩会盖住本按钮 —— UGUI 要求按下与抬起命中同一对象才算
    /// click,所以此时 Button.onClick 不会自然触发,由本组件在抬起时补发。</summary>
    public sealed class HoldToPreview : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public const float Threshold = 0.45f;

        private Action _onHold;
        private Action _onTap;
        private float _downAt;
        private bool _held;
        private bool _pressed;

        /// <param name="onHold">按满阈值时调用(弹 preview)。</param>
        /// <param name="onTap">长按后抬起时补发的点击;短按由 Button.onClick 自己走,不经这里。</param>
        public static void Attach(GameObject target, Action onHold, Action onTap)
        {
            var hold = target.AddComponent<HoldToPreview>();
            hold._onHold = onHold;
            hold._onTap = onTap;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _held = false;
            _downAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            if (_held) _onTap?.Invoke(); // 短按不补发:那条路径 Button.onClick 正常触发
        }

        private void Update()
        {
            if (!_pressed || _held || Time.unscaledTime - _downAt < Threshold) return;
            _held = true;
            _onHold?.Invoke();
        }
    }
}
