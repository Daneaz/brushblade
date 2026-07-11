namespace Brushblade.Core
{
    /// <summary>引导节拍(11.2.2 分层教学,首局剧本:3.9 焚连招)。</summary>
    public enum TutorialStep
    {
        CastLamp,       // 出【灯】= 攻击
        EndTurn,        // 结束回合,看敌人反击
        PickReward,     // 战后三选一
        DismantleLamp,  // 拆【灯】得 火+丁
        ComposeForest,  // 木+木 合【林】
        ComposeBurn,    // 林+火 合【焚】
        CastBurn,       // 打出【焚】清场
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
            (TutorialStep.CastLamp, TutorialAction.Cast, "灯"),
            (TutorialStep.EndTurn, TutorialAction.EndTurn, null),
            (TutorialStep.PickReward, TutorialAction.PickReward, null),
            (TutorialStep.DismantleLamp, TutorialAction.Dismantle, "灯"),
            (TutorialStep.ComposeForest, TutorialAction.Compose, "林"),
            (TutorialStep.ComposeBurn, TutorialAction.Compose, "焚"),
            (TutorialStep.CastBurn, TutorialAction.Cast, "焚"),
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
