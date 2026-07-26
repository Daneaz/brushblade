using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Brushblade.Presentation
{
    /// <summary>长按看详情(2026-07-21):按满阈值弹 preview,纯只读——松手不执行点击
    /// (2026-07-24:长按查看后不再补发点击,避免选中字卡长按看效果时误打出)。
    /// 短按由 Button.onClick 自己走,不经本组件。</summary>
    public sealed class HoldToPreview : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public const float Threshold = 0.45f;

        private Action _onHold;
        private float _downAt;
        private bool _held;
        private bool _pressed;

        /// <param name="onHold">按满阈值时调用(弹 preview)。</param>
        public static void Attach(GameObject target, Action onHold)
        {
            var hold = target.AddComponent<HoldToPreview>();
            hold._onHold = onHold;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _held = false;
            _downAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false; // 长按只看不打:抬起仅停止长按计时,不补发点击
        }

        /// <summary>放弃本次长按(拖字打人时调用:拖动中不该弹详情挡住视线)。</summary>
        public void Cancel() => _pressed = false;

        private void Update()
        {
            if (!_pressed || _held || Time.unscaledTime - _downAt < Threshold) return;
            _held = true;
            _onHold?.Invoke();
        }
    }
}
