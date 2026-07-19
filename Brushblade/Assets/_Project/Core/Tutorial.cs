namespace Brushblade.Core
{
    /// <summary>引导节拍(11.2.2 分层教学,首局剧本:3.9 拆合链战例)。
    /// v0.7 出字即消耗(无回归);2026-07-19「只能合已收集的字」后,首局用手上的【炎】
    /// 演示 拆→合→出 三个核心动作,一回合 3 AP 闭环(升阶字未收集,合不出来)。</summary>
    public enum TutorialStep
    {
        DismantleFlame, // 拆【炎】得 火+火
        RecomposeFlame, // 火+火 合回【炎】——拆与合互为表里
        CastFlame,      // 打出【炎】清场
        PickReward,     // 战后三选一
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
        private static readonly (TutorialStep step, TutorialAction action, string charId)[] Script =
        {
            (TutorialStep.DismantleFlame, TutorialAction.Dismantle, "炎"),
            (TutorialStep.RecomposeFlame, TutorialAction.Compose, "炎"),
            (TutorialStep.CastFlame, TutorialAction.Cast, "炎"),
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
