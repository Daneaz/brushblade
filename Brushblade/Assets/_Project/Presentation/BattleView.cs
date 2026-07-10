using System.Text;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>连战界面:战斗 → 结算 → 三选一奖励 → 下一战。每次操作后整体重绘(原型期够用)。
    /// 战斗内交互:点字库字 → 出字/拆;点部件 → 直出;可合成列表一键合;单体效果进入选目标模式。</summary>
    public sealed class BattleView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private RunEngine _run;
        private System.Action<bool> _onRunEnded;
        private Juice _juice;
        private readonly System.Collections.Generic.List<RectTransform> _enemyRects = new();

        private BattleEngine Battle => _run.Battle;

        // 交互状态
        private string _selectedChar;   // 当前选中的字/部件
        private bool _targeting;        // 等待点击敌人
        private string _message = "点击字库中的字开始行动";

        // 容器
        private Transform _enemyRow;
        private Transform _statusRow;
        private Transform _libraryRow;
        private Transform _poolRow;
        private Transform _suggestRow;
        private Transform _hintColumn;   // 差一提示(分组行,点击展开)
        private Transform _actionRow;
        private Text _messageLabel;
        private string _expandedHint;    // 当前展开的差一类别(缺的部件 id;null = 全收起)

        public void Init(RecipeGraph graph, RunEngine run, System.Action<bool> onRunEnded)
        {
            _graph = graph;
            _run = run;
            _onRunEnded = onRunEnded;
            BuildSkeleton();
            _juice = gameObject.AddComponent<Juice>();
            _juice.Init((RectTransform)transform);
            Refresh();
        }

        /// <summary>动作结算后播放打击感(需在 Refresh 重建敌人格之后调用)。</summary>
        private void PlayJuice()
        {
            _juice.Play(Battle.LastEvents,
                i => i >= 0 && i < _enemyRects.Count ? _enemyRects[i] : null);
        }

        private void BuildSkeleton()
        {
            var root = (RectTransform)transform;
            Ui.Stretch(root);

            _enemyRow = MakeSection("Enemies", 0.68f, 0.94f);
            _statusRow = MakeSection("Status", 0.61f, 0.68f);
            _suggestRow = MakeSection("Suggest", 0.53f, 0.61f);

            // 差一提示列:纵向,最多 5 行
            var hintGo = Ui.Panel(transform, "Hints");
            Ui.Anchor((RectTransform)hintGo.transform, new Vector2(0.02f, 0.36f), new Vector2(0.98f, 0.53f), Vector2.zero, Vector2.zero);
            var hintLayout = hintGo.AddComponent<VerticalLayoutGroup>();
            hintLayout.spacing = 2;
            hintLayout.childAlignment = TextAnchor.UpperLeft;
            hintLayout.childForceExpandWidth = false;
            hintLayout.childForceExpandHeight = false;
            _hintColumn = hintGo.transform;

            _actionRow = MakeSection("Actions", 0.27f, 0.36f);
            _libraryRow = MakeSection("Library", 0.14f, 0.27f);
            _poolRow = MakeSection("Pool", 0.02f, 0.14f);

            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0, 0.94f), Vector2.one, Vector2.zero, Vector2.zero);
            _messageLabel = Ui.Label(messageGo.transform, "", 24);
            Ui.Stretch(_messageLabel.rectTransform);
        }

        private Transform MakeSection(string name, float yMin, float yMax)
        {
            var go = Ui.Row(transform, name);
            Ui.Anchor((RectTransform)go.transform, new Vector2(0, yMin), new Vector2(1, yMax), Vector2.zero, Vector2.zero);
            return go.transform;
        }

        // ---- 渲染 ----

        private void Refresh()
        {
            Ui.Clear(_enemyRow);
            Ui.Clear(_statusRow);
            Ui.Clear(_libraryRow);
            Ui.Clear(_poolRow);
            Ui.Clear(_suggestRow);
            Ui.Clear(_hintColumn);
            Ui.Clear(_actionRow);

            switch (_run.Phase)
            {
                case RunPhase.InBattle when Battle.Phase == BattlePhase.PlayerTurn:
                    DrawEnemies();
                    DrawStatus();
                    DrawLibrary();
                    DrawPool();
                    DrawSuggest();
                    DrawActions();
                    break;
                case RunPhase.InBattle: // 本场已分胜负,等待结算
                    DrawEnemies();
                    DrawStatus();
                    DrawBattleSettle();
                    break;
                case RunPhase.Reward:
                    DrawStatus();
                    DrawReward();
                    break;
                case RunPhase.Event:
                    DrawEvent();
                    break;
                default:
                    DrawRunEnd();
                    break;
            }
            _messageLabel.text = _message;
        }

        private void DrawEnemies()
        {
            _enemyRects.Clear();
            for (int i = 0; i < Battle.Enemies.Count; i++)
            {
                var enemy = Battle.Enemies[i];
                var text = new StringBuilder();
                text.Append(BossTitle(enemy)).Append('\n')
                    .Append(enemy.ApparentElement is { } apparent ? ElementName(apparent) : "?")
                    .Append(" 攻").Append(enemy.Attack)
                    .Append(enemy.DamageTaken < 1f ? " 坚壁" : "").Append('\n')
                    .Append(enemy.Alive ? $"HP {enemy.Hp}/{enemy.MaxHp}" : "已正")
                    .Append(enemy.Burn > 0 ? $"\n灼烧 {enemy.Burn}" : "")
                    .Append(enemy.Def.Ability == EnemyAbility.Regrow && enemy.Alive
                        ? (enemy.RegrowProgress >= 3 ? "\n已补全!" : $"\n补全 {enemy.RegrowProgress}/3") : "")
                    .Append(enemy.Def.Ability == EnemyAbility.Split && enemy.Alive && !enemy.HasSplit
                        ? "\n受击分裂" : "")
                    .Append(enemy.Def.Ability == EnemyAbility.Buff && enemy.Alive ? "\n增益辅助" : "");

                int index = i;
                var color = enemy.Alive
                    ? (_targeting ? new Color(0.6f, 0.25f, 0.2f) : new Color(0.35f, 0.2f, 0.2f))
                    : new Color(0.15f, 0.15f, 0.15f);
                var button = Ui.TextButton(_enemyRow, text.ToString(), () => OnEnemyClicked(index),
                    color, 24, new Vector2(180, 150));
                button.interactable = enemy.Alive;
                _enemyRects.Add((RectTransform)button.transform);
            }
        }

        private void DrawStatus()
        {
            Ui.Label(_statusRow,
                $"战斗 {_run.BattleIndex + 1}    回合 {Battle.Turn}    AP {Battle.Ap}/3    HP {Battle.PlayerHp}/50" +
                (Battle.PlayerShield > 0 ? $"    护盾 {Battle.PlayerShield}" : ""), 26);
        }

        private void DrawLibrary()
        {
            Ui.Label(_libraryRow, $"字库 {Battle.Library.Count}/{Battle.LibraryCapacity}", 22);
            if (!_run.LibraryExpanded)
                Ui.TextButton(_libraryRow, "广告+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandLibrary();
                    _message = "字库上限 +2(本关有效)";
                    Refresh();
                }, new Color(0.2f, 0.38f, 0.3f), 18, new Vector2(80, 44));
            if (Battle.Library.Count == 0)
                Ui.Label(_libraryRow, "(空)", 22);
            foreach (var id in Battle.Library)
            {
                string charId = id;
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting;
                Ui.TextButton(_libraryRow, $"{charId}\n{def.ApCost}AP", () => OnLibraryCharClicked(charId),
                    selected ? new Color(0.5f, 0.45f, 0.15f) : new Color(0.22f, 0.22f, 0.28f),
                    26, new Vector2(96, 88));
            }
        }

        private void DrawPool()
        {
            Ui.Label(_poolRow, $"部件池 {Battle.Pool.Count}/{Battle.PoolCapacity}", 22);
            if (!_run.PoolExpanded)
                Ui.TextButton(_poolRow, "广告+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandPool();
                    _message = "部件池上限 +2(本关有效)";
                    Refresh();
                }, new Color(0.2f, 0.38f, 0.3f), 18, new Vector2(80, 44));
            foreach (var id in Battle.Pool)
            {
                string charId = id;
                bool selected = _selectedChar == charId && !_targeting;
                Ui.TextButton(_poolRow, charId, () => OnPoolCharClicked(charId),
                    selected ? new Color(0.5f, 0.45f, 0.15f) : new Color(0.2f, 0.28f, 0.2f),
                    26, new Vector2(72, 64));
            }
        }

        private void DrawSuggest()
        {
            var suggest = ForgeEngine.Suggest(_graph, Battle.Pool, Battle.Library);
            Ui.Label(_suggestRow, "可合成", 22);
            if (suggest.Composable.Count == 0)
                Ui.Label(_suggestRow, "(无)", 22);
            foreach (var id in suggest.Composable)
            {
                string charId = id;
                Ui.TextButton(_suggestRow, $"合 {charId}", () => OnCompose(charId),
                    new Color(0.2f, 0.32f, 0.42f), 24, new Vector2(110, 56));
            }

            DrawNearMissHints(suggest.NearMisses);
        }

        /// <summary>差一提示:按缺的部件分组,最多 5 行;点击展开该类别(手风琴)。</summary>
        private void DrawNearMissHints(System.Collections.Generic.IReadOnlyList<NearMiss> nearMisses)
        {
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            foreach (var miss in nearMisses)
            {
                if (!groups.TryGetValue(miss.MissingIngredient, out var chars))
                    groups[miss.MissingIngredient] = chars = new System.Collections.Generic.List<string>();
                chars.Add(miss.CharId);
            }
            if (groups.Count == 0) return;

            // 可合数量降序,取前 5 类
            var ordered = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<string>>>(groups);
            ordered.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            int shown = 0;
            foreach (var group in ordered)
            {
                if (shown >= 5)
                {
                    Ui.Label(_hintColumn, $"…还有 {ordered.Count - 5} 类", 16, TextAnchor.MiddleLeft);
                    break;
                }
                shown += 1;
                string ingredient = group.Key;
                bool expanded = _expandedHint == ingredient;
                var chars = group.Value.Count <= 10
                    ? string.Join("·", group.Value)
                    : string.Join("·", group.Value.GetRange(0, 10)) + $"…等{group.Value.Count}字";
                string label = expanded
                    ? $"差「{ingredient}」→ {chars}"
                    : $"差「{ingredient}」可合 {group.Value.Count} 字 ▸";
                Ui.TextButton(_hintColumn, label, () =>
                {
                    _expandedHint = expanded ? null : ingredient; // 手风琴:再点收起
                    Refresh();
                }, expanded ? new Color(0.22f, 0.28f, 0.34f) : new Color(0.16f, 0.18f, 0.22f),
                    18, new Vector2(expanded ? 560 : 240, 26));
            }
        }

        private void DrawActions()
        {
            if (_targeting)
            {
                Ui.TextButton(_actionRow, "取消", CancelSelection, new Color(0.3f, 0.3f, 0.3f));
            }
            else if (_selectedChar != null)
            {
                var def = _graph.Get(_selectedChar);
                bool inLibrary = System.Linq.Enumerable.Contains(Battle.Library, _selectedChar);
                string castLabel = def.Effects.Count > 0 ? (inLibrary ? "出字" : "直出") : "兜底一击";
                Ui.TextButton(_actionRow, castLabel, () => OnCastPressed(def),
                    new Color(0.55f, 0.3f, 0.15f));
                if (inLibrary && !def.IsLeaf)
                    Ui.TextButton(_actionRow, "拆", () => OnDismantle(def.Id), new Color(0.3f, 0.35f, 0.5f));
                Ui.TextButton(_actionRow, "丢弃", () => OnDiscard(def.Id), new Color(0.42f, 0.24f, 0.24f));
                Ui.TextButton(_actionRow, "取消", CancelSelection, new Color(0.3f, 0.3f, 0.3f));
            }
            Ui.TextButton(_actionRow, "结束回合", OnEndTurn, new Color(0.45f, 0.2f, 0.35f), 26, new Vector2(150, 64));
        }

        private void DrawBattleSettle()
        {
            bool won = Battle.Phase == BattlePhase.Won;
            Ui.Label(_actionRow, won ? "本场胜利!" : "败北……", 36);
            Ui.TextButton(_actionRow, "结算", () =>
            {
                _run.AdvanceAfterBattle();
                _message = _run.Phase == RunPhase.Reward ? "战利品:三选一(可跳过)" : "";
                Refresh();
            }, new Color(0.2f, 0.4f, 0.25f), 26, new Vector2(150, 70));
        }

        private void DrawReward()
        {
            Ui.Label(_actionRow, "选一个字入库:", 26);
            for (int i = 0; i < _run.RewardOptions.Count; i++)
            {
                int index = i;
                var id = _run.RewardOptions[i];
                var def = _graph.Get(id);
                Ui.TextButton(_actionRow, $"{id}\n{def.ApCost}AP", () =>
                {
                    _run.PickReward(index);
                    _message = $"「{id}」入库,下一战!";
                    CancelSelection();
                }, Ui.RarityColor(def.Rarity), 26, new Vector2(110, 88));
            }
            Ui.TextButton(_actionRow, "跳过", () =>
            {
                _run.SkipReward();
                _message = "轻装上阵,下一战!";
                CancelSelection();
            }, new Color(0.3f, 0.3f, 0.3f));
        }

        private void DrawEvent() // 奇遇(9.6):短情境 + 选择
        {
            var evt = _run.CurrentEvent;
            Ui.Label(_enemyRow, $"奇遇 · {evt.Id}", 34);
            Ui.Label(_statusRow, evt.Text, 24);
            for (int i = 0; i < evt.Options.Count; i++)
            {
                int index = i;
                var option = evt.Options[i];
                Ui.TextButton(_actionRow, option.Label, () =>
                {
                    _run.ChooseEventOption(index);
                    _message = $"{evt.Id}:{option.Label}";
                    CancelSelection();
                }, new Color(0.3f, 0.32f, 0.44f), 22, new Vector2(240, 72));
            }
        }

        private void DrawRunEnd()
        {
            bool won = _run.Phase == RunPhase.RunWon;
            Ui.Label(_actionRow, won ? "关卡通过——字正!" : "败北", 40);
            Ui.TextButton(_actionRow, "返回地图", () => _onRunEnded(won),
                new Color(0.2f, 0.4f, 0.25f), 26, new Vector2(170, 70));
            _message = won ? "通关结算:经验与墨锭入账。" : "死亡即结算,回地图重整旗鼓。";
        }

        // ---- 交互 ----

        private void OnLibraryCharClicked(string charId)
        {
            _selectedChar = charId;
            _targeting = false;
            var def = _graph.Get(charId);
            _message = def.Effects.Count > 0
                ? $"「{charId}」:出字({def.ApCost} AP)或拆(1 AP)"
                : $"「{charId}」是材料字:兜底一击({def.ApCost} AP)/ 拆 / 用于合成";
            Refresh();
        }

        private void OnPoolCharClicked(string charId)
        {
            var def = _graph.Get(charId);
            _selectedChar = charId;
            _targeting = false;
            _message = def.Effects.Count > 0
                ? $"部件「{charId}」可直出(1 AP)"
                : $"部件「{charId}」可兜底一击(1 AP,弱伤害)或等待合成";
            Refresh();
        }

        private void OnCastPressed(CharDef def)
        {
            if (BattleEngine.NeedsTarget(def))
            {
                _targeting = true;
                _message = $"「{def.Id}」:点击目标敌人";
                Refresh();
                return;
            }
            ExecuteCast(def.Id, -1);
        }

        private void OnEnemyClicked(int index)
        {
            if (_targeting && _selectedChar != null)
                ExecuteCast(_selectedChar, index);
        }

        private void ExecuteCast(string charId, int target)
        {
            var error = Battle.Cast(charId, target);
            _message = error == BattleError.None ? $"出「{charId}」!" : Describe(error);
            AppendBossPhaseMessage();
            CancelSelection();
            if (error == BattleError.None)
                PlayJuice();
        }

        private void AppendBossPhaseMessage()
        {
            foreach (var e in Battle.LastEvents)
                if (e.Kind == BattleEventKind.BossPhase)
                {
                    var enemy = Battle.Enemies[e.TargetIndex];
                    _message += $"  破阶!「{enemy.Def.Phases[e.Amount].Char}」现身——{ElementName(enemy.Element)}系";
                }
        }

        private void OnDiscard(string charId)
        {
            var error = Battle.Discard(charId);
            _message = error == BattleError.None ? $"丢弃「{charId}」(免 AP)" : Describe(error);
            CancelSelection();
        }

        private void OnDismantle(string charId)
        {
            var error = Battle.Dismantle(charId);
            _message = error == BattleError.None ? $"拆「{charId}」" : Describe(error);
            CancelSelection();
        }

        private void OnCompose(string charId)
        {
            var error = Battle.Compose(charId);
            _message = error == BattleError.None ? $"合出「{charId}」!" : Describe(error);
            CancelSelection();
        }

        private void OnEndTurn()
        {
            Battle.EndTurn();
            _message = Battle.Phase == BattlePhase.PlayerTurn ? $"回合 {Battle.Turn}:+3 AP,部件掉落" : "";
            CancelSelection();
            PlayJuice();
        }

        private void CancelSelection()
        {
            _selectedChar = null;
            _targeting = false;
            Refresh();
        }

        /// <summary>成语 Boss 显示当前阶段字:排【山】倒海;普通怪显示名字。</summary>
        private static string BossTitle(EnemyState enemy)
        {
            if (!enemy.IsBoss)
                return enemy.Def.Id;
            var title = new StringBuilder();
            for (int i = 0; i < enemy.Def.Phases.Count; i++)
                title.Append(i == enemy.PhaseIndex ? $"【{enemy.Def.Phases[i].Char}】" : enemy.Def.Phases[i].Char);
            return title.ToString();
        }

        private string Describe(BattleError error) => error switch
        {
            BattleError.NotEnoughAp => "AP 不足",
            BattleError.NotCastable => "此字当前不可出",
            BattleError.InvalidTarget => "目标无效",
            BattleError.BattleOver => "战斗已结束",
            BattleError.ForgeFailed => Battle.LastForgeError switch
            {
                ForgeError.PoolWouldOverflow => "部件池放不下,拆解取消",
                ForgeError.MissingIngredients => "原料不足",
                ForgeError.LibraryFull => "字库已满",
                ForgeError.NotDismantlable => "独体字不可拆",
                _ => "操作被拒",
            },
            _ => "",
        };

        private static string ElementName(Element element) => element switch
        {
            Element.Wood => "木",
            Element.Fire => "火",
            Element.Earth => "土",
            Element.Metal => "金",
            Element.Water => "水",
            Element.Heart => "心",
            _ => "?",
        };
    }
}
