using System.Collections;
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

        // ---- 成语 Boss 骨架(2026-09-30,「讹熔」立绘)----
        // 四个字各一层叠成一团(_body 是它们的容器,呼吸/受击照旧驱动容器);眼睛是
        // 眼晕/虹膜/眼眶三层(_face 是容器)。阶段决定:当前字染本属性色并提到最上层,
        // 其余染墨色,已破的字被「正」走(隐去),剩下的向团心收拢 —— Boss 越打越小。
        // 字的中心与 build_boss_art.LAYOUT 同一套 512 画布坐标(切阶段的墨环从这里炸开)。
        private static readonly Vector2[] CharCenters =
            { new(208, 214), new(308, 228), new(214, 320), new(312, 318) };
        private const float ShrinkPerPhase = 0.07f;   // 每正走一个字,剩下的收拢 7%
        private EnemyDef _bossDef;
        private float _size;
        private int _bossPhase;
        private Image[] _chars;
        private Image _iris;
        private Coroutine _phaseRoutine;

        /// <summary>装配一只怪。prefix 由 MobAssets.PrefixFor 给;返回是否装上了(无资产则 false)。
        /// def/phaseIndex 只有成语 Boss 用得到:它的立绘按阶段决定哪个字亮、哪些已被正走。</summary>
        public bool Init(string prefix, float size, EnemyDef def = null, int phaseIndex = 0)
        {
            var bodySprite = MobAssets.Layer(prefix, "body");
            if (bodySprite == null) return false;

            _phase = Random.value * 10f;
            _size = size;
            var self = (RectTransform)transform;
            self.sizeDelta = new Vector2(size, size);

            if (def != null && def.Phases.Count > 0 && MobAssets.IsBossRig(prefix))
            {
                InitBossRig(prefix, size, def, phaseIndex);
                return true;
            }

            _body = AddLayer("Body", bodySprite, size);
            _face = AddLayer("Face", MobAssets.Layer(prefix, "face"), size, out _faceImage);
            _wisp = AddLayer("Wisp", MobAssets.Layer(prefix, "wisp"), size);
            _state = AddLayer("State", MobAssets.Layer(prefix, "state"), size, out _stateImage);
            SetStateAmount(0f); // 状态层默认不显示,由战斗状态点亮
            return true;
        }

        private void InitBossRig(string prefix, float size, EnemyDef def, int phaseIndex)
        {
            _bossDef = def;
            // 意象配饰垫在最底下:枪尖/浪沫是这团字身后的景,不该压在字上
            _wisp = AddLayer("Wisp", MobAssets.Layer(prefix, "wisp"), size);
            _body = Container("Mass", size);
            _chars = new Image[4];
            for (int i = 0; i < 4; i++)
                AddLayer($"Char{i}", MobAssets.Layer(prefix, "c" + i), size, out _chars[i], _body);
            _face = Container("Eyes", size);
            AddLayer("Halo", MobAssets.Layer(prefix, "halo"), size, out _, _face);
            AddLayer("Iris", MobAssets.Layer(prefix, "iris"), size, out _iris, _face);
            AddLayer("EyeRim", MobAssets.Layer(prefix, "eyes"), size, out _, _face);
            ApplyBossPhase(phaseIndex);
        }

        private RectTransform Container(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }

        private Element PhaseElement(int index) =>
            _bossDef.Phases[Mathf.Clamp(index, 0, _bossDef.Phases.Count - 1)].Element;

        private static float ShrinkFor(int phaseIndex) => 1f - ShrinkPerPhase * phaseIndex;

        /// <summary>直接摆到某一阶段的终态(无动效):装配时、以及动效被下一次切阶段打断时用。</summary>
        private void ApplyBossPhase(int phaseIndex)
        {
            _bossPhase = Mathf.Clamp(phaseIndex, 0, _chars.Length - 1);
            float shrink = ShrinkFor(_bossPhase);
            for (int i = 0; i < _chars.Length; i++)
            {
                var image = _chars[i];
                if (image == null) continue;
                bool freed = i < _bossPhase;
                image.gameObject.SetActive(!freed);
                if (freed) continue;
                var rect = (RectTransform)image.transform;
                rect.localScale = Vector3.one * shrink;
                rect.anchoredPosition = Vector2.zero;
                image.color = i == _bossPhase ? Theme.GlyphColor(PhaseElement(i)) : InkTint;
            }
            if (_chars[_bossPhase] != null) _chars[_bossPhase].transform.SetAsLastSibling();   // 当前字压在最上层
            if (_iris != null) _iris.color = Theme.ElementColor(PhaseElement(_bossPhase));
        }

        private static readonly Color InkTint = new(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.9f);

        /// <summary>切阶段动效(BossPhase 事件):上一个字挣出团块、变回干净墨字飞走(被「正」出来),
        /// 剩下的字收拢,新阶段的字亮起本属性色、从它身上炸开一圈同色墨环,眼睛跟着换色。
        /// 不是成语 Boss 骨架时什么都不做。</summary>
        public void PlayBossPhase(int newPhaseIndex)
        {
            if (_chars == null) return;
            if (_phaseRoutine != null)
            {
                StopCoroutine(_phaseRoutine);
                ApplyBossPhase(_bossPhase);   // 上一段还没演完就又破阶:先落到它的终态
            }
            _phaseRoutine = StartCoroutine(BossPhaseRoutine(_bossPhase, newPhaseIndex));
        }

        private IEnumerator BossPhaseRoutine(int from, int to)
        {
            to = Mathf.Clamp(to, 0, _chars.Length - 1);
            if (to <= from) { ApplyBossPhase(to); yield break; }
            _hitTimer = HitDuration;   // 整团震一下

            // 1. 被正走的字:先挣一下(放大、褪成干净墨色),再飞离团块淡出
            var freed = _chars[from];
            var freedRect = (RectTransform)freed.transform;
            freed.transform.SetAsLastSibling();
            Color startColor = freed.color;
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                float k = t / 0.18f;
                freedRect.localScale = Vector3.one * (ShrinkFor(from) * (1f + 0.15f * k));
                freed.color = Color.Lerp(startColor, Theme.Ink, k);
                yield return null;
            }
            Vector2 flyFrom = CharOffset(from);
            Vector2 flyTo = new(0f, _size * 0.55f);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                float k = t / 0.6f;
                float ease = 1f - (1f - k) * (1f - k);
                freedRect.anchoredPosition = Vector2.Lerp(Vector2.zero, flyTo - flyFrom, ease);
                freedRect.localScale = Vector3.one * Mathf.Lerp(ShrinkFor(from) * 1.15f, 0.55f, ease);
                var c = Theme.Ink;
                c.a = 1f - k;
                freed.color = c;
                yield return null;
            }
            freed.gameObject.SetActive(false);

            // 2. 新阶段:剩下的字收拢、当前字换色,墨环从它身上炸开
            _bossPhase = to;
            var next = _chars[to];
            if (next != null) next.transform.SetAsLastSibling();
            Color irisFrom = _iris != null ? _iris.color : Color.white;
            Color irisTo = Theme.ElementColor(PhaseElement(to));
            Color glyphTo = Theme.GlyphColor(PhaseElement(to));
            StartCoroutine(RingRoutine(CharOffset(to), irisTo));
            float shrinkFrom = ShrinkFor(from), shrinkTo = ShrinkFor(to);
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                float k = t / 0.45f;
                for (int i = to; i < _chars.Length; i++)
                    if (_chars[i] != null) _chars[i].transform.localScale = Vector3.one * Mathf.Lerp(shrinkFrom, shrinkTo, k);
                if (next != null) next.color = Color.Lerp(InkTint, glyphTo, k);
                if (_iris != null) _iris.color = Color.Lerp(irisFrom, irisTo, k);
                yield return null;
            }
            ApplyBossPhase(to);
            _phaseRoutine = null;
        }

        /// <summary>第 i 个字在当前尺寸下相对立绘中心的偏移(Unity 局部坐标,y 向上)。</summary>
        private Vector2 CharOffset(int i) =>
            new Vector2(CharCenters[i].x - 256f, 256f - CharCenters[i].y) * (_size / 512f) * ShrinkFor(_bossPhase);

        private IEnumerator RingRoutine(Vector2 at, Color color)
        {
            var sprite = MobAssets.Layer("fx_boss", "ring");
            if (sprite == null) yield break;
            var rect = AddLayer("Ring", sprite, _size * 0.5f, out var image);
            rect.anchoredPosition = at;
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                float k = t / 0.7f;
                rect.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.6f, 1f - (1f - k) * (1f - k));
                var c = color;
                c.a = 0.9f * (1f - k);
                image.color = c;
                yield return null;
            }
            Destroy(rect.gameObject);
        }

        private RectTransform AddLayer(string name, Sprite sprite, float size) =>
            AddLayer(name, sprite, size, out _);

        private RectTransform AddLayer(string name, Sprite sprite, float size, out Image image,
            Transform parent = null)
        {
            image = null;
            if (sprite == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent != null ? parent : transform, false);
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

        /// <summary>整只染色(死亡置灰用)。alpha 保持各层原值 —— 状态层的 alpha 编码着战斗状态。</summary>
        public void ApplyTint(Color tint)
        {
            foreach (var image in GetComponentsInChildren<Image>(true))
            {
                var color = tint;
                color.a = image.color.a;
                image.color = color;
            }
        }

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
