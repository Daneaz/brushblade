using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>顶部行动条(2026-08-15,ATB 改造):最左是当前行动者(放大 + 高亮),
    /// 往右是 Forecast 出的未来 N 拍。冻结格置灰。
    ///
    /// ⚠ 每次 Refresh 都重新问引擎要预测,**不缓存** —— 怪死了、召唤物上场、减速生效
    /// 都会让上一次的预测失效(见 TurnScheduler.Forecast 的注释)。</summary>
    public sealed class TurnBar : MonoBehaviour
    {
        private const int Slots = 8;
        private readonly List<GameObject> _cells = new();
        private RectTransform _row;

        public void Build(Transform parent)
        {
            var go = Ui.Row(parent, "TurnBar");
            _row = (RectTransform)go.transform;
            // 夹在战况文案(0.900 起)与敌人区(压到 0.850)之间,横向同 Message 行留边
            // (拍板见 task-18-report:messageGo/topBar 都没有余量可让,敌人区本身有余量)。
            Ui.Anchor(_row, new Vector2(0.02f, 0.855f), new Vector2(0.98f, 0.900f), Vector2.zero, Vector2.zero);
        }

        /// <summary>2026-08-16 全分支终审 Important 4:「当前行动者」那一格改用
        /// <see cref="BattleEngine.LastActor"/>(刚行动过/正在行动的那个),不再用
        /// Forecast(8)[0]——Forecast 读的是**当前**计量器,玩家自己那一拍里第 0 格永远是
        /// 下一个要动的敌人,「我」根本不会出现在放大高亮位。spec §4.4 写的正是
        /// HighlightActor(Battle.LastActor)。第 0 格固定放大标"当前",其余 Slots-1 格
        /// 用 Forecast 出的序列标"接下来"。</summary>
        public void Refresh(BattleEngine battle)
        {
            foreach (var cell in _cells) if (cell != null) Object.Destroy(cell);
            _cells.Clear();
            if (battle == null || _row == null) return;

            var current = battle.LastActor;
            _cells.Add(Ui.Chip(_row, LabelFor(battle, current),
                ColorFor(battle, current), Color.white, 18));

            var forecast = battle.Forecast(Slots - 1);
            foreach (var actor in forecast)
                _cells.Add(Ui.Chip(_row, LabelFor(battle, actor),
                    ColorFor(battle, actor), Color.white, 14));
        }

        private static string LabelFor(BattleEngine battle, ActorRef actor) => actor.Kind switch
        {
            ActorKind.Player => "我",
            ActorKind.Summon => battle.Summons[actor.Index].Char,
            _ => battle.Enemies[actor.Index].Def.Id,
        };

        private static Color ColorFor(BattleEngine battle, ActorRef actor)
        {
            if (actor.Kind == ActorKind.Enemy
                && battle.Enemies[actor.Index].Statuses.Has(StatusKind.Freeze))
                return Theme.TextDim;   // 冻结:这一拍会被跳过
            return actor.Kind == ActorKind.Player ? Theme.Cinnabar : Theme.InkSoft;
        }
    }
}
