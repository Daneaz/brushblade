using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>牌外那圈会呼吸的光(稿 CardStates 的 <c>.newwrap</c>:2.6s 一个来回,不闪不跳)。
    ///
    /// 与 <see cref="Juice.Glow"/> 是两件事:那条是战斗里「这张牌刚到手」的**限时**高亮,
    /// 记账在 BattleView 的到期表里;这条是卡组页「这张字还没看过」的**常驻**标记,
    /// 亮到玩家点开它为止 —— 没有到期时刻可传,所以不能复用那条协程。
    ///
    /// 呼吸只改 alpha,不改尺寸 —— 尺寸一动,一排牌就会跟着挤。</summary>
    public sealed class CardHalo : MonoBehaviour
    {
        private const float Period = 2.6f;  // 稿上的呼吸周期
        private const float Dim = 0.45f;
        private const float Bright = 1f;

        private Image _halo;
        private Color _base;
        private float _phase;

        public void Init(Image halo, Color color)
        {
            _halo = halo;
            _base = color;
            _phase = Random.value * Period; // 同屏多张不齐步走
        }

        private void Update()
        {
            if (_halo == null) return;
            // unscaledTime:弹窗/暂停时 timeScale 可能是 0,而这圈光是界面标记不是战斗表现
            float t = (Time.unscaledTime + _phase) / Period * Mathf.PI * 2f;
            float breathe = 0.5f + 0.5f * Mathf.Sin(t);
            _halo.color = new Color(_base.r, _base.g, _base.b, Mathf.Lerp(Dim, Bright, breathe));
        }
    }
}
