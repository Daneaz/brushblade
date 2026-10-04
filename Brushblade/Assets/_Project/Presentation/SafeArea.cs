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

    /// <summary>各界面稿上 .safe 内缩的换算 —— 与 <see cref="SafeAreaFitter"/> 配合、在它之内再补一层。
    ///
    /// 稿的 .safe 左右各留 59pt、下留 21pt(Home Indicator)。真机上 SafeAreaFitter 已经把
    /// Screen.safeArea 那一段让出来了,但编辑器与无刘海机上 safeArea = 整屏、什么都没让,
    /// 不补的话内容会直接贴着屏幕边。<see cref="MissingInset"/> 按差额补齐——设备已经给了
    /// 多少,就少补多少,两边最终都落到稿的数值上。
    ///
    /// ⚠ 只有这一份:MapView / BattleView 都调用它,别各抄一份再各自维护
    /// (这个项目在「两张表各改各的」上栽过不止一次)。</summary>
    public static class SafeArea
    {
        public const float SideInset = 123f;  // 稿上 .safe 左右各 59pt
        public const float BottomInset = 44f; // 稿上 .safe 下 21pt(Home Indicator)

        /// <summary>左右取两侧的较小值:横屏左右旋转时刘海会换边,取 min 才不会随旋转跳动。</summary>
        public static (float side, float bottom) MissingInset()
        {
            float scale = Screen.height / 900f; // CanvasScaler referenceResolution 1600×900,match = 1(按高)
            if (scale <= 0f) return (SideInset, BottomInset);
            var safe = Screen.safeArea;
            float given = Mathf.Min(safe.xMin, Screen.width - safe.xMax) / scale;
            return (Mathf.Max(0f, SideInset - given), Mathf.Max(0f, BottomInset - safe.yMin / scale));
        }

        /// <summary>稿上 .safe 框的实际宽(逻辑单位)= 安全区宽 − 两侧 <see cref="MissingInset"/>。
        /// 挂在 SafeAreaFitter 之内的视图、以及铺满它的浮层,横向能用的就是这么宽:
        /// 基准机 932×430pt ≈ 1705(刘海那段由 SafeAreaFitter 让出、MissingInset 补 0);
        /// 16:9 无刘海机 = 1600 − 123×2 = 1354。按 Screen 算而不读 RectTransform.rect ——
        /// 建界面那一帧 CanvasScaler 可能还没把画布尺寸定下来。
        ///
        /// 2026-10-04 加,供共用浮层(<see cref="CharPreview"/>)按「min(稿宽, 可用宽)」夹宽。
        /// ⚠ 同一笔账另有四份私有实现,口径相同、尚未改调本函数:
        /// <c>BattleView.FrameWidth</c>、<c>CollectionView.ContentWidth</c>、
        /// <c>MapView.RootWidth</c>(− 2×side 在调用处)、<c>ShopView.Init</c> 里的 <c>safeW</c>。</summary>
        public static float FrameWidth()
        {
            float scale = Screen.height / 900f; // CanvasScaler 1600×900,match = 1(按高)
            if (scale <= 0f) return 1600f - 2f * SideInset;
            return Screen.safeArea.width / scale - 2f * MissingInset().side;
        }
    }
}
