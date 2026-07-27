using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>分层字怪(《敌人形象关键词包》§2/§4):三层各跑各的周期、相位错开,
    /// 所以它看着像一个活物而不是一坨在缩放——这正是选分层方案的理由。
    /// 第 12 章戒律:不做骨骼/帧动画,动效全靠程序 tween。</summary>
    public sealed class MobView : MonoBehaviour
    {
        // 各层的呼吸/漂浮周期(秒)。刻意取互质的小数:三层永不同步,合起来才「活」
        private const float BodyPeriod = 3.1f;
        private const float FacePeriod = 2.3f;
        private const float WispPeriod = 4.7f;
        private const float BlinkPeriod = 4.1f;
        private const float HitDuration = 0.5f;

        private RectTransform _body, _face, _wisp, _state;
        private Image _faceImage, _stateImage;
        private float _phase;      // 每只怪随机起相:同屏两只同种怪不会齐步走
        private float _hitTimer;   // >0 = 受击动效进行中

        /// <summary>装配一只怪。prefix 由 MobAssets.PrefixFor 给;返回是否装上了(无资产则 false)。</summary>
        public bool Init(string prefix, float size)
        {
            var bodySprite = MobAssets.Layer(prefix, "body");
            if (bodySprite == null) return false;

            _phase = Random.value * 10f;
            var self = (RectTransform)transform;
            self.sizeDelta = new Vector2(size, size);

            _body = AddLayer("Body", bodySprite, size);
            _face = AddLayer("Face", MobAssets.Layer(prefix, "face"), size, out _faceImage);
            _wisp = AddLayer("Wisp", MobAssets.Layer(prefix, "wisp"), size);
            _state = AddLayer("State", MobAssets.Layer(prefix, "state"), size, out _stateImage);
            SetStateAmount(0f); // 状态层默认不显示,由战斗状态点亮
            return true;
        }

        private RectTransform AddLayer(string name, Sprite sprite, float size) =>
            AddLayer(name, sprite, size, out _);

        private RectTransform AddLayer(string name, Sprite sprite, float size, out Image image)
        {
            image = null;
            if (sprite == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);
            image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false; // 点击交给外层的按钮,别被层挡住
            image.preserveAspect = true;
            return rect;
        }

        /// <summary>状态层强度 0~1(L4 绑战斗字段:缺笔妖补全进度、焦痕火芯亮度……)。</summary>
        public void SetStateAmount(float amount)
        {
            if (_stateImage == null) return;
            var color = _stateImage.color;
            color.a = Mathf.Clamp01(amount);
            _stateImage.color = color;
        }

        /// <summary>受击:主体抖、墨丝甩尾、眼睛瞪大——三层不同步才有层次。</summary>
        public void PlayHit() => _hitTimer = HitDuration;

        private void Update()
        {
            float t = Time.time + _phase;
            float hit = _hitTimer > 0f ? _hitTimer / HitDuration : 0f;
            if (_hitTimer > 0f) _hitTimer -= Time.deltaTime;

            // 主体:呼吸缩放 + 受击横向抖动(阻尼衰减的正弦)
            if (_body != null)
            {
                float breathe = 1f + 0.045f * Mathf.Sin(t * Mathf.PI * 2f / BodyPeriod);
                float shake = hit > 0f ? Mathf.Sin(hit * Mathf.PI * 6f) * 11f * hit : 0f;
                _body.localScale = Vector3.one * (breathe + hit * 0.1f);
                _body.anchoredPosition = new Vector2(shake, 0f);
            }

            // 面孔:独立漂移 + 眨眼 + 受击瞪大
            if (_face != null)
            {
                float drift = Mathf.Sin(t * Mathf.PI * 2f / FacePeriod);
                _face.anchoredPosition = new Vector2(drift * 2.2f, drift * -2.6f);
                // 眨眼:周期内绝大部分时间睁着,末尾极短一段闭合
                float blinkCycle = Mathf.Repeat(t, BlinkPeriod) / BlinkPeriod;
                float blink = blinkCycle > 0.94f ? Mathf.Abs(Mathf.Sin((blinkCycle - 0.94f) / 0.06f * Mathf.PI)) : 0f;
                _face.localScale = new Vector3(1f + hit * 0.35f, (1f - blink * 0.92f) * (1f + hit * 0.35f), 1f);
            }

            // 墨丝:慢漂 + 受击甩尾(旋转 + 外抛)
            if (_wisp != null)
            {
                float floatT = t * Mathf.PI * 2f / WispPeriod;
                _wisp.anchoredPosition = new Vector2(Mathf.Sin(floatT) * 5f + hit * 15f,
                    Mathf.Cos(floatT * 0.7f) * 6f + hit * 19f);
                _wisp.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(floatT) * 4f + hit * 34f);
            }
        }
    }
}
