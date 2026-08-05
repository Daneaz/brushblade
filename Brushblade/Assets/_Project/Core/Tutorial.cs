namespace Brushblade.Core
{
    /// <summary>引导节拍(11.2.2 分层教学,首局剧本:3.9 拆合链战例)。
    /// v0.7 出字即消耗(无回归);2026-07-19「只能合已收集的字」后,首局用手上的字
    /// 演示 拆→合→出 三个核心动作,一回合 3 AP 闭环(拆 0 + 合 1 + 出 1)。
    /// 2026-08-05:初始收集改为五系白/绿/蓝后【炎】不再在手,演示字换成
    /// <see cref="DemoChar"/>——金克木,一击斩掉首层的木系错字鬼。</summary>
    public enum TutorialStep
    {
        DismantleDemo, // 拆【剑】得 佥+刂
        RecomposeDemo, // 佥+刂 合回【剑】——拆与合互为表里
        CastDemo,      // 打出【剑】清场
        PickReward,    // 战后三选一
        Done,
    }

    public enum TutorialAction
    {
        Cast,
        Dismantle,
        Compose,
        EndTurn,
        PickReward,
    }

    /// <summary>新手引导步骤机:动作通知驱动线性推进;文案由表现层按 Step 映射。</summary>
    public sealed class Tutorial
    {
        /// <summary>首局演示字:必须在默认出阵里(StartingSetupTests 守这条),
        /// 且配方是「部件+部件」——拆开就能原地合回,不依赖别的字。</summary>
        public const string DemoChar = "剑";

        private static readonly (TutorialStep step, TutorialAction action, string charId)[] Script =
        {
            (TutorialStep.DismantleDemo, TutorialAction.Dismantle, DemoChar),
            (TutorialStep.RecomposeDemo, TutorialAction.Compose, DemoChar),
            (TutorialStep.CastDemo, TutorialAction.Cast, DemoChar),
            (TutorialStep.PickReward, TutorialAction.PickReward, null),
        };

        private int _index;

        public TutorialStep Step => _index < Script.Length ? Script[_index].step : TutorialStep.Done;
        public bool Done => Step == TutorialStep.Done;

        public void Notify(TutorialAction action, string charId = null)
        {
            if (Done) return;
            var current = Script[_index];
            if (current.action != action) return;
            if (current.charId != null && current.charId != charId) return;
            _index++;
        }
    }
}
