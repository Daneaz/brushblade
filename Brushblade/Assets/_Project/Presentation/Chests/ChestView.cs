using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>宝箱三态(<c>Chests.dc.html</c> 的「三态」块):同一只箱三种画法,
    /// **只改叠加层、不换底图** —— 一只箱一张素材,七档三态省下 21 张图。
    ///
    /// 「已就绪」是主界面唯一的动效:四个箱位同屏,光晕与起伏要压得过另外三只,
    /// 谁该点一眼就见。三段同用稿上的 1.6s 周期。
    /// 第 12 章戒律:不做骨骼/帧动画,动效全靠程序 tween(同 <see cref="MobView"/>)。</summary>
    public sealed class ChestView : MonoBehaviour
    {
        /// <summary>箱位上的三种画法。</summary>
        public enum State
        {
            Idle,    // 未开始:原色满不透明,唯一「还能动手」的一只
            Timing,  // 计时中:压到 45% + 沙漏角标
            Ready,   // 已就绪:金光晕 + 七道光芒 + 盖缝透光,箱身一起一落
        }

        private const float Period = 1.6f;   // 稿上三段动效同一个周期
        private const float TimingAlpha = 0.45f;
        private const float BobAmount = 2.5f / 120f; // 稿上 120 画布里起落 2.5px,按素材边长等比

        private RectTransform _bob;
        private Image _halo, _seam;
        private float _bobPixels;

        /// <summary>装配一只箱;没有立绘返回 false —— 调用方回落到色块 + 首字。</summary>
        public bool Init(ChestTier tier, State state, float size)
        {
            var body = ChestAssets.Layer(tier, "body");
            if (body == null) return false;

            var self = (RectTransform)transform;
            self.sizeDelta = new Vector2(size, size);
            _bobPixels = size * BobAmount;

            // 层序(下 → 上):光晕 → 箱身 + 盖缝 → 沙漏角标。
            // 光晕在箱身之下,才是「从箱后透出来」而不是糊在箱面上。
            if (state == State.Ready)
                _halo = AddLayer(transform, "Halo", ChestAssets.Effect("fx_ready"));

            // 箱身与盖缝一起起伏,所以套一层 bob 容器;光晕不跟着动(它是场,不是物)
            var bobGo = new GameObject("Bob", typeof(RectTransform));
            bobGo.transform.SetParent(transform, false);
            _bob = (RectTransform)bobGo.transform;
            Ui.Stretch(_bob);

            var bodyImage = AddLayer(_bob, "Body", body);
            if (state == State.Timing)
                bodyImage.color = new Color(1f, 1f, 1f, TimingAlpha);

            if (state == State.Ready)
                _seam = AddLayer(_bob, "Seam", ChestAssets.Layer(tier, "seam"));
            if (state == State.Timing)
                AddLayer(transform, "Glass", ChestAssets.Effect("fx_timing"));

            enabled = state == State.Ready; // 另外两态是静态画面,不必每帧跑
            return true;
        }

        private static Image AddLayer(Transform parent, string name, Sprite sprite)
        {
            if (sprite == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            // 各层一律铺满 ChestView 的框:素材是 1:1 方图,画幅本身已对齐,
            // 靠 sizeDelta 摆会吃到 RectTransform 的锚点默认值(代码建的与编辑器建的不一样)
            Ui.Stretch((RectTransform)go.transform);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false; // 点击交给格内的按钮,别被层挡住
            image.preserveAspect = true;
            return image;
        }

        private void Update()
        {
            // 0→1→0 的三角波;三段同相,合起来是「一次呼吸」而不是三件事各动各的
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / Period);
            if (_halo != null) SetAlpha(_halo, Mathf.Lerp(0.45f, 1f, wave));
            if (_seam != null) SetAlpha(_seam, Mathf.Lerp(0.55f, 1f, wave));
            if (_bob != null) _bob.anchoredPosition = new Vector2(0f, wave * _bobPixels);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            var color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }
}
