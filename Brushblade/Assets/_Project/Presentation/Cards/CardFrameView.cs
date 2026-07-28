using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>字牌动效(《字牌形象关键词包》§4)。动效是**二维**的:
    /// 属性决定「动什么」(火冒烟 / 水洇渗 / 木抽条 / 金闪锋 / 土沉降 / 心重影),
    /// 稀有度决定「动多少」(元件个数与透明度上限,以及材质光效层怎么跑)。
    /// 第 12 章戒律:不做帧动画,全靠程序 tween;所有周期取互质小数,同屏 8 张不齐步走。</summary>
    public sealed class CardFrameView : MonoBehaviour
    {
        // 材质光效层周期(§4.2):蓝釉光最慢,红星芒最快 —— 稀有度越高越「活」
        private const float SweepPeriod = 6.1f;    // 蓝:釉面反光扫过
        private const float BreathePeriod = 4.3f;  // 紫:边缘辉光呼吸
        private const float FlowPeriod = 3.1f;     // 橙:金边流光
        private const float TwinklePeriod = 2.7f;  // 红:星芒明灭
        private const float PlayablePeriod = 2.9f; // 通用:可出手呼吸

        // 六系签名动效周期(§4.1)。金/土 刻意最慢:金是「瞬时、间隔长」,土是「几乎不动」
        private static float PeriodOf(Element? element) => element switch
        {
            Element.Fire => 3.7f,
            Element.Water => 4.3f,
            Element.Wood => 5.9f,
            Element.Metal => 6.7f,
            Element.Earth => 5.3f,
            Element.Heart => 4.9f,
            _ => 5f,
        };

        /// <summary>元件个数与透明度上限(§4.2 的「音量」)。白/绿只给一件、极淡 —— 暗示级。</summary>
        private static (int count, float alpha) VolumeOf(CardRarity rarity) => rarity switch
        {
            CardRarity.White => (1, 0.16f),
            CardRarity.Green => (1, 0.24f),
            CardRarity.Blue => (2, 0.36f),
            CardRarity.Purple => (3, 0.48f),
            CardRarity.Orange => (3, 0.60f),
            CardRarity.Red => (4, 0.74f),
            _ => (1, 0.2f),
        };

        /// <summary>选中某张牌时,其余牌的属性动效降到这个比例(§4.5:把注意力让给正在操作的那张)。</summary>
        private const float UnfocusedAttention = 0.32f;
        private static CardFrameView _focused; // destroy 后 Unity 的 == null 会认出来,不必手动清

        private RectTransform _self;
        private Image _frame;
        private Image _glow;
        private RectTransform _glowRect;
        private RectTransform[] _motes;
        private Image[] _moteImages;

        private CardRarity _rarity;
        private Element? _element;
        private Vector2 _size;
        private float _phase;       // 每张牌随机起相
        private float _alphaCeil;
        private Color _frameBase;
        private bool _playable = true;  // false = AP 不足:去饱和 + 属性动效停(§4.4)
        private bool _frameApplied = true;

        /// <summary>低于这个 alpha 变化量就不写回 —— UI 的 color 每写一次就标脏一次 Canvas。</summary>
        private const float AlphaEpsilon = 0.004f;

        public void Init(CardRarity rarity, Element? element, Vector2 size,
            Transform moteParent, Image frame, Image glow, bool selected)
        {
            _self = (RectTransform)transform;
            _rarity = rarity;
            _element = element;
            _size = size;
            _frame = frame;
            _frameBase = frame != null ? frame.color : Color.white;
            _glow = glow;
            _glowRect = glow != null ? (RectTransform)glow.transform : null;
            _phase = Random.value * 10f;
            if (selected) _focused = this;

            var sprite = CardFrames.Element(element);
            var (count, alpha) = VolumeOf(rarity);
            _alphaCeil = alpha;
            if (sprite == null || moteParent == null) return;

            _motes = new RectTransform[count];
            _moteImages = new Image[count];
            float side = size.x * MoteScaleOf(element);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Mote{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(moteParent, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(side, side);
                // 木要从根部往上长,缩放得绕底边转:pivot 落到底,anchoredPosition 随之指的是根部
                if (element == Element.Wood) rect.pivot = new Vector2(0.5f, 0f);
                var image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.color = Theme.GlyphColor(element); // 墨色元件运行时染属性色
                _motes[i] = rect;
                _moteImages[i] = image;
            }
        }

        /// <summary>元件相对牌宽的尺寸:锋光要长、尘石要小,其余居中。</summary>
        private static float MoteScaleOf(Element? element) => element switch
        {
            Element.Metal => 0.46f,
            Element.Earth => 0.24f,
            Element.Wood => 0.28f,
            _ => 0.26f,
        };

        /// <summary>AP 够不够出这张(§4.4)。不够:去饱和压暗 + 属性动效停,明确表达「用不了」。</summary>
        public void SetPlayable(bool playable) => _playable = playable;

        private void Update()
        {
            float t = Time.time + _phase;
            float attention = _focused == null || _focused == this ? 1f : UnfocusedAttention;
            float gate = _playable ? attention : 0f;

            DriveFrame(t);
            DriveGlow(t, gate);
            DriveMotes(t, gate);
        }

        /// <summary>通用层(§4.4):可出手时极轻微呼吸;AP 不足去饱和压暗。</summary>
        private void DriveFrame(float t)
        {
            if (_playable)
            {
                float breathe = 1f + 0.015f * Mathf.Sin(t * Mathf.PI * 2f / PlayablePeriod);
                _self.localScale = new Vector3(breathe, breathe, 1f);
            }
            else
            {
                _self.localScale = Vector3.one;
            }
            // 框色只在可出手状态翻转时写一次 —— 每帧无条件赋 color 会把整块 Canvas 每帧标脏
            if (_frame == null || _playable == _frameApplied) return;
            _frameApplied = _playable;
            // 去饱和不能动 alpha:框素材自带牌面底色,压 alpha 会把牌变透明
            _frame.color = _playable ? _frameBase : Color.Lerp(_frameBase, Theme.LockedBg, 0.62f);
        }

        /// <summary>材质光效层(§4.2):四档各跑各的。位移幅度都压在牌内 —— 不裁剪,就不会溢到邻牌上。</summary>
        private void DriveGlow(float t, float gate)
        {
            if (_glow == null) return;
            float alpha = 1f, x = 0f;
            switch (_rarity)
            {
                case CardRarity.Blue: // 釉面反光:斜光带横向扫过,两端各留余量不出牌
                {
                    float u = Mathf.Repeat(t / SweepPeriod, 1f);
                    x = Mathf.Lerp(-0.21f, 0.21f, u) * _size.x;
                    alpha = Mathf.Sin(u * Mathf.PI); // 进出各淡一次,不是硬切
                    break;
                }
                case CardRarity.Purple: // 边缘辉光呼吸
                    alpha = 0.42f + 0.48f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / BreathePeriod));
                    break;
                case CardRarity.Orange: // 金边流光:光条沿顶栏来回,幅度小到不出框
                {
                    float u = Mathf.Repeat(t / FlowPeriod, 1f);
                    x = Mathf.Sin(u * Mathf.PI * 2f) * 0.068f * _size.x;
                    alpha = 0.5f + 0.5f * Mathf.Abs(Mathf.Cos(u * Mathf.PI * 2f));
                    break;
                }
                case CardRarity.Red: // 星芒明灭
                    alpha = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2f / TwinklePeriod);
                    break;
            }
            _glowRect.anchoredPosition = new Vector2(x, 0f);
            var color = _glow.color;
            // 材质光效只随「选中降噪」减弱一半:它是稀有度的身份标识,不该被完全按掉
            float target = alpha * Mathf.Lerp(0.5f, 1f, gate);
            if (Mathf.Abs(target - color.a) < AlphaEpsilon) return;
            color.a = target;
            _glow.color = color;
        }

        /// <summary>属性层(§4.1):六系各一套形态语言。
        /// 元件一律走**两侧留白与底栏**——字形半宽约 0.21 牌宽,元件贴在 ±0.33 处,
        /// 既在「边缘」又有完整的纵向行程,不会压到字上(§4.5:中心恒定干净)。</summary>
        private void DriveMotes(float t, float gate)
        {
            if (_motes == null) return;
            float period = PeriodOf(_element);
            for (int i = 0; i < _motes.Length; i++)
            {
                // 每件错开一个不整齐的相位 + 左右交替 + 纵向错层:同一张牌上的两缕烟也不该同步
                float u = Mathf.Repeat(t / period + i * 0.37f, 1f);
                float side = i % 2 == 0 ? -1f : 1f;
                float stagger = i / 2 * 0.11f * _size.y;
                Vector2 pos;
                float scale = 1f, alpha;

                switch (_element)
                {
                    case Element.Fire: // 一缕墨烟自下升起,上升中飘散
                        pos = new Vector2(side * 0.33f * _size.x + Mathf.Sin(u * 5.2f) * 0.025f * _size.x,
                            Mathf.Lerp(-0.34f, 0.10f, u) * _size.y - stagger * 0.55f);
                        scale = Mathf.Lerp(0.72f, 1.18f, u);
                        alpha = Mathf.Sin(u * Mathf.PI);
                        break;

                    case Element.Water: // 水痕渗润:洇开又收回,不移动,只呼吸
                        pos = new Vector2(side * 0.33f * _size.x, -0.26f * _size.y - stagger * 0.82f);
                        scale = 0.78f + 0.42f * Mathf.Sin(u * Mathf.PI);
                        alpha = Mathf.Sin(u * Mathf.PI);
                        break;

                    case Element.Wood: // 一角抽条:长出一小段停住,再淡出重来
                    {
                        float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.45f));
                        pos = new Vector2(side * (0.30f + i / 2 * 0.06f) * _size.x, -0.44f * _size.y);
                        alpha = u < 0.7f ? Mathf.Clamp01(u / 0.12f) : Mathf.Clamp01((1f - u) / 0.3f);
                        _motes[i].localScale = new Vector3(1f, grow, 1f); // 纵向揭示 = 生长
                        SetMote(i, pos, alpha, skipScale: true);
                        continue;
                    }

                    case Element.Metal: // 瞬时:一道锋光快速掠过底栏,间隔长、持续短
                    {
                        const float window = 0.13f;
                        if (u > window) { SetMote(i, Vector2.zero, 0f); continue; }
                        float v = u / window;
                        pos = new Vector2(Mathf.Lerp(-0.22f, 0.22f, v) * _size.x, -0.42f * _size.y + stagger);
                        alpha = Mathf.Sin(v * Mathf.PI);
                        break;
                    }

                    case Element.Earth: // 几乎不动:定在角上,只有明暗里的重量感
                        pos = new Vector2(side * 0.34f * _size.x, -0.38f * _size.y + stagger);
                        alpha = 0.62f + 0.38f * Mathf.Sin(u * Mathf.PI * 2f);
                        break;

                    // 心是全书唯一贴着字跑的系,也是唯一的例外:§4.1 的签名动作就是「字形双影错位」,
                    // 挪到角上就不成立了。代价用透明度买回来 —— 封顶再打六折,只做一层恍惚的虚影,
                    // 且字在最上层渲染,可读性不受影响(心系本就不参与生克,「不对劲」是它的设计)
                    case Element.Heart:
                    {
                        float drift = Mathf.Sin(u * Mathf.PI * 2f) * 0.045f * _size.x;
                        pos = new Vector2(side * (0.31f * _size.x + drift), 0.02f * _size.y - stagger);
                        alpha = (0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f + (side > 0f ? 0f : Mathf.PI))) * 0.6f;
                        break;
                    }

                    default:
                        SetMote(i, Vector2.zero, 0f);
                        continue;
                }

                _motes[i].localScale = new Vector3(scale, scale, 1f);
                SetMote(i, pos, alpha, skipScale: true);
            }

            void SetMote(int i, Vector2 pos, float alpha, bool skipScale = false)
            {
                if (!skipScale) _motes[i].localScale = Vector3.one;
                _motes[i].anchoredPosition = pos;
                var color = _moteImages[i].color;
                float target = alpha * _alphaCeil * gate;
                if (Mathf.Abs(target - color.a) < AlphaEpsilon) return;
                color.a = target;
                _moteImages[i].color = color;
            }
        }
    }
}
