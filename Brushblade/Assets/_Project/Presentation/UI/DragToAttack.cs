using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>拖字打人(2026-07-26):把字牌拖到敌人身上松手 = 出字攻击该敌人。
    /// 拖动中显示跟随指针的字影;松手把屏幕坐标交给外层判定落在哪个敌人上
    /// (本组件不认识敌人,只管手势与反馈)。
    /// 与"点选 → 点敌人"并存:uGUI 一旦进入拖拽就清掉 eligibleForClick,不会重复触发点击。</summary>
    public sealed class DragToAttack : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private string _glyph;
        private Color _color;
        private Func<bool> _canDrag;
        private Action _onBeginDrag;
        private Action<Vector2> _onDrop;
        private Action<Vector2> _onDragMove;
        private RectTransform _ghost;

        /// <param name="canDrag">此刻能否拖(结算动画中/非玩家回合返回 false)。</param>
        /// <param name="onDrop">松手时回调,参数为松手处的屏幕坐标。</param>
        /// <param name="onBeginDrag">真正起拖时回调一次(被 canDrag 拒掉则不回调)。
        /// 2026-08-21 加的:拖召唤字时外层要在这一刻点亮 6 个槽位 —— 松手才点亮就晚了,
        /// 玩家在整个拖拽过程中都不知道能往哪儿放。
        ///
        /// ⚠ 回调里**不许触发会销毁本组件的重绘**:uGUI 的拖拽事件只发给起拖的那个对象,
        /// 它一旦被销毁,OnEndDrag 就再也不会来 —— 字影留在屏幕上、外层状态卡死。
        /// 外层为此专门有一个只重画召唤两排、不动字牌的路径。</param>
        /// <param name="onDragMove">2026-08-22 加的:拖拽过程中**每帧**回调一次,参数为当前
        /// 指针的屏幕坐标(用于「悬停到敌人上方时预览会打到哪几格」)。与 onBeginDrag 同一条
        /// ⚠:不许触发会重绘/销毁本组件的路径 —— 这里只能就地改已存在物件的颜色。</param>
        public static void Attach(GameObject target, string glyph, Color color,
            Func<bool> canDrag, Action<Vector2> onDrop, Action onBeginDrag = null,
            Action<Vector2> onDragMove = null)
        {
            var drag = target.AddComponent<DragToAttack>();
            drag._glyph = glyph;
            drag._color = color;
            drag._canDrag = canDrag;
            drag._onDrop = onDrop;
            drag._onBeginDrag = onBeginDrag;
            drag._onDragMove = onDragMove;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_canDrag != null && !_canDrag()) return;
            var hold = GetComponent<HoldToPreview>();
            if (hold != null) hold.Cancel(); // 拖动中不弹长按详情

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling(); // 压在所有 UI 之上,跟手不被遮
            _ghost = (RectTransform)go.transform;
            _ghost.sizeDelta = new Vector2(72, 72);

            var label = go.AddComponent<Text>();
            // 必须用子集字体:四叠字是 PUA 码位(U+E625/E626),字形由部件 2×2 拼合而来,
            // 只存在于我们自己生成的子集里 —— 走系统字体会拖出一个空白(与字牌、飞牌同源)
            label.font = Theme.TitleFont;
            label.fontSize = 44;
            label.fontStyle = FontStyle.Bold;
            label.text = _glyph;
            label.color = new Color(_color.r, _color.g, _color.b, 0.85f);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false; // 别挡住底下敌人格的射线

            _ghost.position = eventData.position;
            _ghost.localScale = Vector3.one * GhostBirthScale;
            _lastPointer = eventData.position;
            StartCoroutine(GhostBirthRoutine());
            _onBeginDrag?.Invoke(); // 字影建好之后再通知:回调里可能重画别的区,先把手感立起来
        }

        // 起拖的一记「抓起来」(2026-08-30):字影从略小弹到略大再回 1。
        // 此前它是啪地出现在指尖的一个静态字,拿起来这个动作在画面上完全没有发生过
        private const float GhostBirthScale = 0.7f;
        private const float GhostPeakScale = 1.16f;
        private const float GhostBirthTime = 0.12f;
        // 跟手的倾斜:按指针移动速度左右倒,封顶 14°。拖得越快歪得越多,像真的在拽着一张牌走
        private const float GhostLeanMax = 14f;
        private const float GhostLeanPerPixel = 0.6f;

        private Vector2 _lastPointer;
        private float _lean;

        private System.Collections.IEnumerator GhostBirthRoutine()
        {
            float t = 0f;
            while (t < GhostBirthTime && _ghost != null)
            {
                t += Time.unscaledDeltaTime;
                float k = t / GhostBirthTime;
                // 一记 overshoot:小 → 过头 → 落回 1
                float scale = k < 0.6f
                    ? Mathf.Lerp(GhostBirthScale, GhostPeakScale, k / 0.6f)
                    : Mathf.Lerp(GhostPeakScale, 1f, (k - 0.6f) / 0.4f);
                _ghost.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            if (_ghost != null) _ghost.localScale = Vector3.one;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // ⚠ 守卫必须包住这两行:起拖被 _canDrag() 拒掉时 _ghost 保持 null,但 uGUI 的
            // EventSystem 不看这个内部状态——它在调 OnBeginDrag 之前就已经把指针标成
            // dragging,之后 OnDrag/OnEndDrag 照样派发。被拒的拖拽必须是彻底的 no-op
            // (2026-08-22 评审 Finding 1:onDragMove 单独出了守卫,预览高亮会被写上,
            // 而 OnEndDrag 因 _ghost == null 提前返回、永不调 onDrop 去清它)。
            if (_ghost == null) return;
            // 倾斜跟着横向速度走,再往 0 收 —— 不收的话停手时字影会一直歪着
            float dx = eventData.position.x - _lastPointer.x;
            _lastPointer = eventData.position;
            _lean = Mathf.Clamp(Mathf.Lerp(_lean, -dx * GhostLeanPerPixel, 0.35f),
                -GhostLeanMax, GhostLeanMax);
            _ghost.localRotation = Quaternion.Euler(0f, 0f, _lean);
            _ghost.position = eventData.position;
            _onDragMove?.Invoke(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost == null) return; // 起拖时被拒(结算中):什么也不做
            // 松手不再是啪地消失:字影原地缩一下淡出,手上「放开了」这个动作才有落点。
            // ⚠ 协程挂在**字影自己**身上而不是本组件上 —— onDrop 通常会触发重绘、把本组件连同
            // 字牌一起销毁,挂在这边的协程会当场断掉,字影就永远留在屏幕上了
            var releasing = _ghost;
            _ghost = null;
            var fader = releasing.gameObject.AddComponent<GhostRelease>();
            fader.Begin();
            _onDrop?.Invoke(eventData.position);
        }

        private void OnDisable() // 字牌被重绘销毁时字影不能留在屏幕上
        {
            if (_ghost == null) return;
            Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }

    /// <summary>松手后字影的收尾:原地缩一点、淡出、自毁(2026-08-30)。
    ///
    /// 单独一个组件挂在字影身上,是因为 <see cref="DragToAttack"/> 的 onDrop 几乎必然触发重绘,
    /// 把那个组件连同字牌一起销毁 —— 收尾协程只要挂在它那边就会当场断掉,字影永远留在屏幕上。
    /// 挂在字影自己身上,它的生命周期就只跟自己有关。</summary>
    public sealed class GhostRelease : MonoBehaviour
    {
        private const float Duration = 0.14f;

        public void Begin() => StartCoroutine(Fade());

        private System.Collections.IEnumerator Fade()
        {
            var rect = (RectTransform)transform;
            var label = GetComponent<Text>();
            Color from = label != null ? label.color : Color.white;
            float t = 0f;
            while (t < Duration)
            {
                t += Time.unscaledDeltaTime;
                float k = t / Duration;
                float scale = 1f - 0.25f * k;
                rect.localScale = new Vector3(scale, scale, 1f);
                if (label != null) label.color = new Color(from.r, from.g, from.b, from.a * (1f - k));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
