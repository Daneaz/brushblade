using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>部件池里**还能再拆一层**的部件(烝 = 丞 + 灬)的呼吸标记。
    ///
    /// 曲线与字库牌「这张出得起」那圈呼吸是同一条(<see cref="CardFrameView.BreatheScale"/>),
    /// 刻意不另立一套:玩家在同一屏上读到的「这一格还有得做」应当是同一个动作。
    /// 部件卡是手搭的(<c>BattleView.PartTile</c>),身上没有 <see cref="CardFrameView"/>,
    /// 而那整套按稀有度 / 属性跑的元件与光效对部件既没有数据也没有意义,只借这一条呼吸。
    ///
    /// 只改 localScale,不动 LayoutElement —— 缩放不参与布局,一排部件不会跟着挤。</summary>
    public sealed class PartBreath : MonoBehaviour
    {
        private float _phase;

        private void Awake() => _phase = Random.value * 10f; // 同屏多张不齐步走,同 CardFrameView

        private void Update()
        {
            float scale = CardFrameView.BreatheScale(Time.time + _phase);
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
