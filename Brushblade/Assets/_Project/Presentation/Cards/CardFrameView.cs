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
        // ⚠️ 稀有度显示皮肤错位映射(2026-08-04,接入金卡素材,与 Theme.RarityColor 等同一套映射):
        // 枚举 Orange 现在显示"金"皮肤、枚举 Red 现在显示"橙"皮肤、新增枚举 Gold(强度最高)显示"红"皮肤。
        // 下面周期常量按**当前显示皮肤**重新命名/追加,不是按枚举名——刻意错位,不是 bug。
        private const float SweepPeriod = 6.1f;      // 蓝:釉面反光扫过(挂在枚举 Blue,皮肤未变)
        private const float BreathePeriod = 4.3f;    // 紫:边缘辉光呼吸(挂在枚举 Purple,皮肤未变)
        private const float FlowPeriod = 3.1f;       // 金:金边流光(挂在枚举 Orange,现显示"金"皮肤)
        private const float FlowPeriodBright = 2.85f;// 橙:流光加强版(挂在枚举 Red,现显示"橙"皮肤)—— 周期比
                                                       // 金档快、比红档星芒慢,呼应「金<橙<红」的视觉层级递增
        private const float TwinklePeriod = 2.7f;    // 红:星芒明灭(挂在枚举 Gold,现显示"红"皮肤)
        private const float PlayablePeriod = 2.9f;   // 通用:可出手呼吸

        // 六系签名动效周期(§4.1)。金/土 刻意最慢:金是「瞬时、间隔长」,土是「几乎不动」
        private static float PeriodOf(Element? element) => element switch
        {
            Element.Fire => 3.7f,
            Element.Water => 4.3f,
            Element.Wood => 5.9f,
            Element.Metal => 5.1f,
            Element.Earth => 5.3f,
            Element.Heart => 4.9f,
            _ => 5f,
        };

        /// <summary>元件个数与透明度上限(§4.2 的「音量」)。白/绿只给一件、极淡 —— 暗示级。
        /// 音量按 白→绿→蓝→紫→金→橙→红 的视觉层级递增。</summary>
        private static (int count, float alpha) VolumeOf(CardRarity rarity) => rarity switch
        {
            CardRarity.White => (1, 0.20f),
            CardRarity.Green => (1, 0.28f),
            CardRarity.Blue => (2, 0.40f),
            CardRarity.Purple => (3, 0.52f),
            CardRarity.Gold => (3, 0.58f),    // 金:介于紫(0.52)与橙(0.64)之间
            CardRarity.Orange => (3, 0.64f),
            CardRarity.Red => (4, 0.78f),
            _ => (1, 0.2f),
        };

        /// <summary>选中某张牌时,其余牌的属性动效降到这个比例(§4.5:把注意力让给正在操作的那张)。</summary>
        private const float UnfocusedAttention = 0.32f;
        private static CardFrameView _focused; // destroy 后 Unity 的 == null 会认出来,不必手动清

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
        private float _alphaScale = 1f;
        private Color _frameBase;
        /// <summary>出手状态三态。**Untracked 是关键的一档**:呼吸曾因「全屏都在闪」被砍,
        /// 病根不在呼吸本身,而在只有战斗调 SetPlayable、别处所有牌都默认「可出手」——
        /// 卡组同屏 12 张全在胀缩。分出「没人告诉过我」这一档,呼吸就只发生在真正需要
        /// 表达可否出手的地方(战斗字库),其余界面一律安静。</summary>
        private enum Playability { Untracked, Playable, Blocked }

        private Playability _play = Playability.Untracked;
        private Playability _frameApplied = Playability.Untracked;
        private RectTransform _self;

        /// <summary>低于这个 alpha 变化量就不写回 —— UI 的 color 每写一次就标脏一次 Canvas。</summary>
        private const float AlphaEpsilon = 0.004f;

        /// <summary>材质光效里掺多少属性色。掺多了稀有度就认不出了,三成是能看出系别的下限。</summary>
        private const float GlowTintStrength = 0.3f;

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
            if (glow != null)
            {
                // 材质光效也带上系别:往白里掺三成属性色再乘上去。
                // 不能直接乘属性色 —— Image.color 是**相乘**,紫檀的紫辉光乘火红只会变成一团暗泥
                // (与元件改白底同一条道理);掺白后亮度基本不掉,只偏色相,
                // 稀有度的形状与行为原样保留(§1:两套色系各占一个通道,不打架)
                glow.color = Color.Lerp(Color.white, Theme.ElementColor(element), GlowTintStrength);
            }
            _phase = Random.value * 10f;
            if (selected) _focused = this;

            var sprite = CardFrames.Element(element);
            var (count, alpha) = VolumeOf(rarity);
            _alphaCeil = alpha;
            _alphaScale = MoteAlphaScale(element);
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
                // 锋光斜着划才像「斩」;角度恒定,在此设一次,不进每帧
                if (element == Element.Metal) rect.localRotation = Quaternion.Euler(0f, 0f, -11f);
                var image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.color = MoteColor(element);
                _motes[i] = rect;
                _moteImages[i] = image;
            }
        }

        /// <summary>锋光专用冷钢色 `#5B6B7A`。**「亮银」在白牌面上是做不到的** —— UI 走 alpha 混合,
        /// 比纸更亮的颜色叠上去等于没叠(银灰 `#8A97A3` 对牌面只有 2.84:1,实测)。
        /// 金的辨识只能来自比纸**暗**的冷钢,「一线高光」改由元件自身的镂空刀芯衬出来。
        /// 顺带解决金土难分:原来的橄榄灰 `#6B6449` 与土只差 ΔE 25.4,换冷钢后拉到 50.0。</summary>
        private static readonly Color SteelEdge = new(0.357f, 0.420f, 0.478f);

        /// <summary>元件染色(白底元件 × 这个色 = 最终色)。用**鲜色板** ElementColor 而非
        /// 字色板 GlyphColor —— 后者是为过 WCAG 4.5:1 特意加深去彩的,染出来属性色被洗掉一半;
        /// 元件是图不是字,不背对比度这条约束,它要的恰恰是「一眼认出是哪一系」。
        /// 金是例外,走 <see cref="SteelEdge"/>:鲜色板的 `#B3A382` 对浅牌面只有 2.48:1,
        /// 那道锋光会淡到等于没做 —— 这正是当初逼出加深字色板的同一个字色。</summary>
        private static Color MoteColor(Element? element) =>
            element == Element.Metal ? SteelEdge : Theme.ElementColor(element);

        /// <summary>属性层的额外音量。金/土 实测「几乎看不见」,各补一档,其余系不动:
        /// 金只在两成周期里出场、且是一条细线;土刻意不动、原本体量又是六系最小。
        /// 补的是**透明度**不是幅度 —— 土的性格是静,让它动起来就不是土了。</summary>
        private static float MoteAlphaScale(Element? element) => element switch
        {
            Element.Metal => 1.5f,
            Element.Earth => 1.35f,
            _ => 1f,
        };

        /// <summary>元件相对牌宽的尺寸:锋光要长、尘石要小,其余居中。</summary>
        private static float MoteScaleOf(Element? element) => element switch
        {
            Element.Metal => 0.54f,
            Element.Earth => 0.36f,
            Element.Wood => 0.28f,
            _ => 0.26f,
        };

        /// <summary>AP 够不够出这张(§4.4)。够:极轻微呼吸,提示「这张能打」;
        /// 不够:去饱和压暗 + 属性动效停。不调这个方法的界面两样都不做。</summary>
        public void SetPlayable(bool playable) =>
            _play = playable ? Playability.Playable : Playability.Blocked;

        private void Update()
        {
            float t = Time.time + _phase;
            float attention = _focused == null || _focused == this ? 1f : UnfocusedAttention;
            float gate = _play == Playability.Blocked ? 0f : attention;

            DriveFrame(t);
            DriveGlow(t, gate);
            DriveMotes(t, gate);
        }

        /// <summary>通用层(§4.4):可出手呼吸 + AP 不足去饱和压暗。
        /// 呼吸只作用在**明确报过可出手**的牌上(见 <see cref="Playability"/>):
        /// 曾经因为「全屏都在闪」砍过一次,病根是别处的牌也默认在呼吸,不是呼吸本身。</summary>
        private void DriveFrame(float t)
        {
            if (_play == Playability.Playable)
            {
                float breathe = 1f + 0.015f * Mathf.Sin(t * Mathf.PI * 2f / PlayablePeriod);
                _self.localScale = new Vector3(breathe, breathe, 1f);
            }
            // 框色只在出手状态翻转时写一次 —— 每帧无条件赋 color 会把整块 Canvas 每帧标脏
            if (_frame == null || _play == _frameApplied) return;
            _frameApplied = _play;
            if (_play != Playability.Playable) _self.localScale = Vector3.one;
            // 去饱和不能动 alpha:框素材自带牌面底色,压 alpha 会把牌变透明
            _frame.color = _play == Playability.Blocked
                ? Color.Lerp(_frameBase, Theme.LockedBg, 0.62f)
                : _frameBase;
        }

        /// <summary>材质光效层(§4.2):四档各跑各的。位移幅度都压在牌内 —— 不裁剪,就不会溢到邻牌上。</summary>
        private void DriveGlow(float t, float gate)
        {
            if (_glow == null) return;
            float alpha = 1f, x = 0f;
            switch (_rarity)
            {
                // 釉面反光原本是横向扫过的,实测溢到邻牌上了:光带斜跨整张牌,横向可见范围
                // 占 88% 牌宽,再扫 ±0.21 牌宽,两侧各有约 15% 牌宽画在牌外 —— 而这一层没有裁剪。
                // 要恢复「扫过」得给它单加一层 RectMask2D;先改成原地明灭,同样读得出釉光。
                case CardRarity.Blue:
                    alpha = 0.30f + 0.34f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / SweepPeriod));
                    break;
                case CardRarity.Purple: // 边缘辉光呼吸
                    alpha = 0.50f + 0.30f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / BreathePeriod));
                    break;
                case CardRarity.Gold: // 金边流光:光条沿顶栏来回,幅度小到不出框
                {
                    float u = Mathf.Repeat(t / FlowPeriod, 1f);
                    x = Mathf.Sin(u * Mathf.PI * 2f) * 0.068f * _size.x; // 光条只占 69% 牌宽,这个幅度不出牌
                    alpha = 0.62f + 0.28f * Mathf.Abs(Mathf.Cos(u * Mathf.PI * 2f));
                    break;
                }
                // 橙沿用流光形态,但比金档更快更亮:幅度 0.078 > 0.068,基线 alpha 0.68 > 0.62,
                // 让「金 < 橙 < 红」的视觉层级递增。
                case CardRarity.Orange:
                {
                    float u = Mathf.Repeat(t / FlowPeriodBright, 1f);
                    x = Mathf.Sin(u * Mathf.PI * 2f) * 0.078f * _size.x;
                    alpha = 0.68f + 0.30f * Mathf.Abs(Mathf.Cos(u * Mathf.PI * 2f));
                    break;
                }
                case CardRarity.Red: // 星芒明灭(最高档)
                    alpha = 0.72f + 0.24f * Mathf.Sin(t * Mathf.PI * 2f / TwinklePeriod);
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
                        const float window = 0.20f;
                        if (u > window) { SetMote(i, Vector2.zero, 0f); continue; }
                        float v = u / window;
                        pos = new Vector2(Mathf.Lerp(-0.20f, 0.20f, v) * _size.x, -0.40f * _size.y + stagger);
                        alpha = Mathf.Sin(v * Mathf.PI);
                        break;
                    }

                    // 土刻意不动,但「不动」不等于「看不见」—— 它是同屏 8 张里的视觉锚点。
                    // 体量给到六系最大,再补一点沉降的起伏(±1.2% 牌高):有重量,不飘
                    case Element.Earth:
                        // 错层走横向:体量放大后,纵向错层会把上面那颗石头顶进字带(实测)
                        pos = new Vector2(side * (0.30f + i / 2 * 0.05f) * _size.x,
                            (-0.355f + 0.012f * Mathf.Sin(u * Mathf.PI * 2f)) * _size.y);
                        alpha = 0.86f + 0.14f * Mathf.Sin(u * Mathf.PI * 2f);
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
                float target = Mathf.Clamp01(alpha * _alphaCeil * _alphaScale * gate);
                if (Mathf.Abs(target - color.a) < AlphaEpsilon) return;
                color.a = target;
                _moteImages[i].color = color;
            }
        }
    }
}
