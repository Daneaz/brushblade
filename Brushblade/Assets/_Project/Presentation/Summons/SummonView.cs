using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>召唤物立绘(草木围着字):墨枝(body)+ 按属性着色的枝叶(leaf)两层,
    /// 字形本身仍是外层按钮的那枚 Text —— 本组件垫在它下面,字永远在最上层、清晰可认
    /// (原《敌人形象关键词包》(已删,见 git 349c3cf5^)§0「字为骨」)。
    ///
    /// 动效与 <see cref="MobView"/> 同一条第 12 章戒律:不做骨骼/帧动画,只做程序 tween。
    /// 两层绕**地面**(pivot 在底边)各自摇曳、周期互质,看着是一丛在风里的活物;
    /// 受击整丛一抖、叶子乱颤;出手时枝叶往前一扬(Juice 的前冲/后坐负责整格位移)。</summary>
    public sealed class SummonView : MonoBehaviour
    {
        private const float BodyPeriod = 3.7f;
        private const float LeafPeriod = 2.6f;
        private const float HitDuration = 0.45f;
        private const float AttackDuration = 0.32f;

        private RectTransform _body, _leaf;
        private float _phase;
        private float _hitTimer;
        private float _attackTimer;

        /// <summary>装配。prefix 由 <see cref="SummonAssets.PrefixFor"/> 给;leafTint 是当前属性的
        /// 字形色(解封会重掷属性,所以颜色不烤进图里)。返回是否装上了(无资产则 false,不留空节点)。</summary>
        public bool Init(string prefix, Color leafTint)
        {
            var bodySprite = SummonAssets.Layer(prefix, "body");
            if (bodySprite == null) return false;
            _phase = Random.value * 10f; // 同排两只同种召唤物不齐步摇
            _body = AddLayer("Body", bodySprite, Color.white);
            _leaf = AddLayer("Leaf", SummonAssets.Layer(prefix, "leaf"), leafTint);
            return true;
        }

        private RectTransform AddLayer(string name, Sprite sprite, Color color)
        {
            if (sprite == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            // 铺满外层(外层是已按立绘尺寸定好的按钮);pivot 放到地面那一线,摇曳绕根部转
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.1f);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false; // 点击交给外层按钮
            return rect;
        }

        public void PlayHit() => _hitTimer = HitDuration;

        public void PlayAttack() => _attackTimer = AttackDuration;

        private void Update()
        {
            float t = Time.time + _phase;
            float hit = _hitTimer > 0f ? _hitTimer / HitDuration : 0f;
            float atk = _attackTimer > 0f ? 1f - _attackTimer / AttackDuration : 0f; // 0→1
            if (_hitTimer > 0f) _hitTimer -= Time.unscaledDeltaTime;
            if (_attackTimer > 0f) _attackTimer -= Time.unscaledDeltaTime;
            // 出手曲线:前 30% 往回收(蓄),之后猛地扬出再回落
            float lunge = _attackTimer > 0f
                ? (atk < 0.3f ? -atk / 0.3f * 0.5f : Mathf.Sin((atk - 0.3f) / 0.7f * Mathf.PI))
                : 0f;

            if (_body != null)
            {
                float sway = Mathf.Sin(t * Mathf.PI * 2f / BodyPeriod) * 1.6f;
                float shake = hit > 0f ? Mathf.Sin(hit * Mathf.PI * 7f) * 6f * hit : 0f;
                _body.localRotation = Quaternion.Euler(0f, 0f, sway + shake);
                _body.localScale = new Vector3(1f, 1f + 0.06f * lunge, 1f);
            }

            if (_leaf != null)
            {
                float sway = Mathf.Sin(t * Mathf.PI * 2f / LeafPeriod) * 3.2f;
                float shiver = hit > 0f ? Mathf.Sin(hit * Mathf.PI * 11f) * 10f * hit : 0f;
                float breathe = 1f + 0.03f * Mathf.Sin(t * Mathf.PI * 2f / (LeafPeriod * 1.7f));
                _leaf.localRotation = Quaternion.Euler(0f, 0f, sway + shiver);
                _leaf.localScale = Vector3.one * (breathe + 0.12f * lunge + 0.08f * hit);
            }
        }
    }
}
