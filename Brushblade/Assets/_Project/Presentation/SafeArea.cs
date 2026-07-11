using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>把子内容压进 Screen.safeArea(刘海/挖孔/圆角),黑底背景仍全屏铺满。
    /// 横屏 only(2026-07-11 拍板):左右横屏切换时安全区会变,故每帧比对。</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _applied;

        private void Awake() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _applied)
                Apply();
        }

        private void Apply()
        {
            _applied = Screen.safeArea;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(_applied.xMin / Screen.width, _applied.yMin / Screen.height);
            rect.anchorMax = new Vector2(_applied.xMax / Screen.width, _applied.yMax / Screen.height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
