using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>手势翻页(2026-08-18):在整页范围内左右滑动 = 上/下一页。
    /// 挂在**页面根节点**上,与 ◀▶ 按钮并存 —— 按钮仍是明示入口,手势只是更顺手的那条。
    ///
    /// 挂根节点而不是内容容器是刻意的:uGUI 起拖时会沿层级向上找第一个
    /// <see cref="IDragHandler"/>,而卡片上的 Button/Tile 都只实现点击类接口,
    /// 于是从卡片上起手的滑动会一路冒泡到这里;同时 uGUI 一旦进入拖拽就清掉
    /// eligibleForClick,不会顺带触发那张卡的点击(与 <see cref="DragToAttack"/> 同一套依据)。
    /// 空白处也要能起手,所以 Attach 会补一张全透明的 raycast 底图。</summary>
    public sealed class SwipePager : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // 触发阈值取屏宽的比例:横屏 1600 宽约 96px,竖握窄屏也不会变得难触发。
        // 下限 50px 是防止极窄视口下手指一抖就翻页。
        private const float ThresholdRatio = 0.06f;
        private const float MinThreshold = 50f;

        private Action _onPrev;
        private Action _onNext;
        private Func<bool> _canSwipe;
        private Vector2 _start;
        private bool _tracking;

        /// <param name="canSwipe">此刻能否翻页(弹窗打开时返回 false)。null = 一直可翻。</param>
        public static void Attach(GameObject target, Action onPrev, Action onNext, Func<bool> canSwipe = null)
        {
            // 空白处也要接得到射线:补一张全透明底图。页面自己的内容都盖在它之上,
            // 不影响任何既有点击(alpha 0 也不影响观感)。
            var catcher = target.GetComponent<Image>();
            if (catcher == null) catcher = target.AddComponent<Image>();
            catcher.color = Color.clear;
            catcher.raycastTarget = true;

            var pager = target.GetComponent<SwipePager>();
            if (pager == null) pager = target.AddComponent<SwipePager>();
            pager._onPrev = onPrev;
            pager._onNext = onNext;
            pager._canSwipe = canSwipe;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _tracking = _canSwipe == null || _canSwipe();
            _start = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // 空实现是必需的:uGUI 只把拖拽事件发给同时实现了 IDragHandler 的对象,
            // 少了它 OnBeginDrag/OnEndDrag 一次都不会来。
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_tracking) return;
            _tracking = false;

            var delta = eventData.position - _start;
            // 纵向为主的滑动不翻页:留给将来的纵向滚动,也避免斜着划一下就跳页
            if (Mathf.Abs(delta.x) <= Mathf.Abs(delta.y)) return;
            if (Mathf.Abs(delta.x) < Mathf.Max(MinThreshold, Screen.width * ThresholdRatio)) return;

            // 向左划 = 内容往左走 = 下一页(与移动端通行方向一致)
            if (delta.x < 0) _onNext?.Invoke();
            else _onPrev?.Invoke();
        }
    }
}
