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
        private float _exitArmedUntil;  // 退出二次确认窗口(unscaled 时间戳)
        private string _message = "点击字库中的字开始行动";

        private string _title;          // 关卡标题(顶栏,可选)
        // Battle.PlayerMaxHp 在 Core 引擎里不存在(仅 BattleConfig 有);禁止改 Core,由 GameRoot 经 Init 透传
        private int _playerMaxHp = 50;

        // 容器
        private Transform _enemyRow;
        private Transform _topLeft, _topRight, _bottomRow;
        private Transform _statusRow;    // 语义:结束回合行
        private Transform _libraryRow;
        private Transform _poolRow;
        private Transform _suggestRow;
        private Transform _hintColumn;   // 差字面板(屏幕左侧竖排,五行三级目录)
        private Transform _actionRow;
        private Text _messageLabel;
        private string _hintBucket;      // 差字目录一级选中的五行桶(金木水火土心/中性;null = 收起)
        private string _hintCharFocus;   // 三级目录二级选中的字(null = 未选)

        private Tutorial _tutorial;      // 新手引导(11.2);null = 不引导

        public void Init(RecipeGraph graph, RunEngine run, System.Action<bool> onRunEnded,
            Tutorial tutorial = null, string title = null, int playerMaxHp = 50)
        {
            _graph = graph;
            _run = run;
            _onRunEnded = onRunEnded;
            _tutorial = tutorial;
            _title = title ?? "";
            _playerMaxHp = playerMaxHp;
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

            // 顶栏:标题 | 墨锭 · 回合 · 退出
            var topBar = Ui.Panel(transform, "TopBar");
            Ui.Anchor((RectTransform)topBar.transform, new Vector2(0.02f, 0.94f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            _topLeft = Ui.Row(topBar.transform, "Left", 10).transform;
            Ui.Anchor((RectTransform)_topLeft, new Vector2(0, 0), new Vector2(0.4f, 1), Vector2.zero, Vector2.zero);
            _topRight = Ui.Row(topBar.transform, "Right", 14).transform;
            Ui.Anchor((RectTransform)_topRight, new Vector2(0.4f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);

            var messageGo = Ui.Panel(transform, "Message");
            Ui.Anchor((RectTransform)messageGo.transform, new Vector2(0.02f, 0.885f), new Vector2(0.98f, 0.94f), Vector2.zero, Vector2.zero);
            _messageLabel = Ui.ThemedLabel(messageGo.transform, "", 19, Theme.TextDim);
            Ui.Stretch(_messageLabel.rectTransform);

            _enemyRow = MakeSection("Enemies", 0.62f, 0.885f);

            // 拆合台白卡:第一行内容(配方/拆字),第二行动作
            var workbenchCard = Ui.CardPanel(transform, "Workbench", Theme.CardWhite, 20);
            Ui.Anchor((RectTransform)workbenchCard.transform, new Vector2(0.14f, 0.37f), new Vector2(0.86f, 0.61f), Vector2.zero, Vector2.zero);
            var workbenchStack = Ui.VStack(workbenchCard.transform, "Stack", 6);
            Ui.Stretch((RectTransform)workbenchStack.transform);
            Ui.ThemedLabel(workbenchStack.transform, "拆 合 台", 13, Theme.TextDim, Theme.TitleFont);
            _suggestRow = Ui.Row(workbenchStack.transform, "Content", 10).transform;
            _actionRow = Ui.Row(workbenchStack.transform, "Actions", 8).transform;

            // 差字面板:屏幕最左侧,上下居中,五行三级目录
            var hintGo = Ui.VStack(transform, "HintPanel", 4);
            Ui.Anchor((RectTransform)hintGo.transform, new Vector2(0.002f, 0.16f), new Vector2(0.135f, 0.84f), Vector2.zero, Vector2.zero);
            hintGo.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _hintColumn = hintGo.transform;

            _statusRow = MakeSection("EndTurn", 0.3f, 0.37f);
            _libraryRow = MakeSection("Library", 0.16f, 0.3f);
            _poolRow = MakeSection("Pool", 0.065f, 0.16f);
            _bottomRow = MakeSection("PlayerStats", 0f, 0.065f);
        }

        private Transform MakeSection(string name, float yMin, float yMax)
        {
            var go = Ui.Row(transform, name);
            Ui.Anchor((RectTransform)go.transform, new Vector2(0, yMin), new Vector2(1, yMax), Vector2.zero, Vector2.zero);
            return go.transform;
        }

        // ---- 渲染 ----

        // 字牌位置登记(charId→当前 RectTransform):过渡动效的起终点;每次重绘重新登记
        private readonly System.Collections.Generic.Dictionary<string, RectTransform> _tileRects = new();

        private bool TryGetTilePos(string charId, out Vector3 pos)
        {
            if (_tileRects.TryGetValue(charId, out var rect) && rect != null)
            {
                pos = rect.position;
                return true;
            }
            pos = default;
            return false;
        }

        private void Refresh()
        {
            _tileRects.Clear();
            Ui.Clear(_topLeft);
            Ui.Clear(_topRight);
            Ui.Clear(_enemyRow);
            Ui.Clear(_suggestRow);
            Ui.Clear(_actionRow);
            Ui.Clear(_hintColumn);
            Ui.Clear(_statusRow);
            Ui.Clear(_libraryRow);
            Ui.Clear(_poolRow);
            Ui.Clear(_bottomRow);

            switch (_run.Phase)
            {
                case RunPhase.InBattle when Battle.Phase == BattlePhase.PlayerTurn:
                    DrawEnemies();
                    DrawTopBar();
                    DrawPlayerStats();
                    DrawLibrary();
                    DrawPool();
                    DrawSuggest();
                    DrawActions();
                    DrawEndTurn();
                    break;
                case RunPhase.InBattle: // 本场已分胜负,等待结算
                    DrawEnemies();
                    DrawTopBar();
                    DrawPlayerStats();
                    DrawBattleSettle();
                    break;
                case RunPhase.Reward:
                    DrawTopBar();
                    DrawPlayerStats();
                    DrawReward();
                    break;
                case RunPhase.Event:
                    DrawEvent();
                    break;
                default:
                    DrawRunEnd();
                    break;
            }
            DrawTutorialHint();
            _messageLabel.text = _message;
        }

        /// <summary>引导横幅:一步一句话(11.2.5),金色置于结束回合行(屏幕中部显眼)。</summary>
        private void DrawTutorialHint()
        {
            if (_tutorial == null || _tutorial.Done) return;
            var hint = Ui.Label(_statusRow, "◆ " + TutorialText(_tutorial.Step), 26);
            hint.color = Theme.GoldBorder;
            hint.transform.SetAsFirstSibling();
        }

        private static string TutorialText(TutorialStep step) => step switch
        {
            TutorialStep.CastLamp => "点字库的【灯】,出字就是攻击!",
            TutorialStep.EndTurn => "点【结束回合】(AP 耗尽会自动结束)——小心字怪反击",
            TutorialStep.PickReward => "清掉字怪!战后三选一,挑个字入库",
            TutorialStep.DismantleLamp => "选中【灯】点【拆】——拆出部件『火』『丁』",
            TutorialStep.ComposeForest => "两个『木』能拼字:点提示里的【合 林】",
            TutorialStep.ComposeBurn => "『林』+『火』——点【合 焚】,拼出大杀器!",
            TutorialStep.CastBurn => "打出【焚】,一击清场!",
            _ => "",
        };

        private void DrawTopBar()
        {
            Ui.ThemedLabel(_topLeft, string.IsNullOrEmpty(_title) ? $"战斗 {_run.BattleIndex + 1}" : $"{_title} · 战斗 {_run.BattleIndex + 1}",
                20, Theme.TextMain, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.IngotLabel(_topRight, _run.AvailableInk.ToString(), 18);
            Ui.ThemedLabel(_topRight, $"回合 {Battle.Turn}", 18, Theme.TextDim);
            bool exitArmed = Time.unscaledTime < _exitArmedUntil;
            Ui.PillButton(_topRight, exitArmed ? "确认退出?" : "退出关卡", () =>
            {
                if (Time.unscaledTime < _exitArmedUntil) { _onRunEnded(false); return; }
                _exitArmedUntil = Time.unscaledTime + 2.5f;
                _message = "再点一次「确认退出?」放弃本关(进度不推进,奇遇墨锭保留)";
                Refresh();
            }, exitArmed ? Theme.Cinnabar : Theme.ExitPink, Color.white, 15, new Vector2(110, 38));
        }

        private void DrawPlayerStats()
        {
            var hpStack = Ui.VStack(_bottomRow, "Hp", 3);
            Ui.ThemedLabel(hpStack.transform, $"HP {Battle.PlayerHp}/{_playerMaxHp}", 14, Theme.TextDim);
            Ui.Bar(hpStack.transform, Battle.PlayerHp / (float)_playerMaxHp, Theme.Cinnabar, new Vector2(260, 13));
            if (Battle.PlayerShield > 0)
            {
                Ui.Bar(hpStack.transform, Mathf.Clamp01(Battle.PlayerShield / 30f), Theme.Jade, new Vector2(260, 7));
                Ui.ThemedLabel(hpStack.transform, $"护盾 {Battle.PlayerShield}", 12, Theme.Jade);
            }
            var apStack = Ui.VStack(_bottomRow, "Ap", 4);
            Ui.ThemedLabel(apStack.transform, "AP", 12, Theme.TextDim);
            var pips = Ui.Row(apStack.transform, "Pips", 12);
            for (int i = 0; i < 3; i++)
            {
                var pip = Ui.Panel(pips.transform, $"Pip{i}");
                var image = pip.AddComponent<Image>();
                image.sprite = Theme.Rounded(10);
                image.type = Image.Type.Sliced;
                image.color = i < Battle.Ap ? Theme.Gold : Theme.PaperDim;
                pip.transform.localRotation = Quaternion.Euler(0, 0, 45);
                var element = pip.AddComponent<LayoutElement>();
                element.preferredWidth = 18;
                element.preferredHeight = 18;
            }
        }

        private void DrawEnemies()
        {
            _enemyRects.Clear();
            for (int i = 0; i < Battle.Enemies.Count; i++)
            {
                var enemy = Battle.Enemies[i];
                int index = i;

                var cell = Ui.Panel(_enemyRow, $"Enemy{i}");
                var cellElement = cell.AddComponent<LayoutElement>();
                cellElement.preferredWidth = 168;
                cellElement.preferredHeight = 208;

                var circle = Ui.Panel(cell.transform, "Portrait");
                var circleImage = circle.AddComponent<Image>();
                circleImage.sprite = Theme.Circle;
                circleImage.color = enemy.Alive
                    ? Theme.ElementColor(enemy.ApparentElement)
                    : Theme.LockedBg;
                Ui.Anchor((RectTransform)circle.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-52, -104), new Vector2(52, 0));
                if (_targeting && enemy.Alive)
                {
                    var outline = circle.AddComponent<Outline>();
                    outline.effectColor = Theme.Ink;
                    outline.effectDistance = new Vector2(3, 3);
                }
                var glyph = Ui.ThemedLabel(circle.transform,
                    enemy.IsBoss ? enemy.Def.Phases[enemy.PhaseIndex].Char : enemy.Def.Id.Substring(0, 1),
                    44, Color.white, Theme.TitleFont);
                Ui.Stretch(glyph.rectTransform);

                var info = Ui.VStack(cell.transform, "Info", 3);
                Ui.Anchor((RectTransform)info.transform, new Vector2(0, 0), new Vector2(1, 1),
                    Vector2.zero, new Vector2(0, -106));
                Ui.ThemedLabel(info.transform, BossTitle(enemy), 17, Theme.TextMain, Theme.TitleFont);
                var chips = Ui.Row(info.transform, "Chips", 5);
                Ui.Chip(chips.transform, enemy.ApparentElement is { } apparent ? ElementName(apparent) : "?",
                    Theme.ElementColor(enemy.ApparentElement), Color.white, 12);
                Ui.Chip(chips.transform, $"攻 {enemy.Attack}", Theme.PaperDim, Theme.TextMain, 12);
                if (enemy.DamageTaken < 1f) Ui.Chip(chips.transform, "坚壁", Theme.InkSoft, Color.white, 12);
                if (enemy.Burn > 0) Ui.Chip(chips.transform, $"灼烧 {enemy.Burn}", Theme.Cinnabar, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Regrow && enemy.Alive)
                    Ui.Chip(chips.transform, enemy.RegrowProgress >= 3 ? "已补全!" : $"补全 {enemy.RegrowProgress}/3",
                        Theme.Jade, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Split && enemy.Alive && !enemy.HasSplit)
                    Ui.Chip(chips.transform, "受击分裂", Theme.InkSoft, Color.white, 12);
                if (enemy.Def.Ability == EnemyAbility.Buff && enemy.Alive)
                    Ui.Chip(chips.transform, "增益辅助", Theme.InkSoft, Color.white, 12);

                if (enemy.Alive)
                {
                    Ui.Bar(info.transform, enemy.Hp / (float)enemy.MaxHp, Theme.Cinnabar, new Vector2(140, 9));
                    Ui.ThemedLabel(info.transform, $"{enemy.Hp} / {enemy.MaxHp}", 12, Theme.TextDim);
                }
                else
                {
                    Ui.ThemedLabel(info.transform, "已正", 14, Theme.LockGray);
                }

                var button = cell.AddComponent<Button>();
                button.targetGraphic = circleImage;
                button.onClick.AddListener(() => OnEnemyClicked(index));
                button.interactable = enemy.Alive;
                _enemyRects.Add((RectTransform)circle.transform);
            }
        }

        private void DrawLibrary()
        {
            Ui.ThemedLabel(_libraryRow, $"字库 {Battle.Library.Count}/{Battle.LibraryCapacity}", 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.LibraryExpanded)
                Ui.AdBadge(_libraryRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandLibrary();
                    _message = "字库上限 +2(本关有效)";
                    Refresh();
                }, new Vector2(64, 38));
            if (Battle.Library.Count == 0)
                Ui.ThemedLabel(_libraryRow, "(空)", 16, Theme.TextDim);
            foreach (var id in Battle.Library)
            {
                string charId = id;
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting;
                var tile = Ui.GlyphTile(_libraryRow, def, $"{def.ApCost} AP", selected,
                    () => OnLibraryCharClicked(charId), new Vector2(82, 104));
                _tileRects[charId] = (RectTransform)tile.transform;
            }
        }

        private void DrawPool()
        {
            Ui.ThemedLabel(_poolRow, $"部件池 {Battle.Pool.Count}/{Battle.PoolCapacity}", 16, Theme.TextDim, Theme.TitleFont);
            if (!_run.PoolExpanded)
                Ui.AdBadge(_poolRow, "+2", () => // 原型:点击即生效,SDK 后接
                {
                    _run.TryExpandPool();
                    _message = "部件池上限 +2(本关有效)";
                    Refresh();
                }, new Vector2(64, 38));
            foreach (var id in Battle.Pool)
            {
                string charId = id;
                var def = _graph.Get(charId);
                bool selected = _selectedChar == charId && !_targeting;
                var tile = Ui.RoundButton(_poolRow, charId, () => OnPoolCharClicked(charId),
                    selected ? Theme.ElementColor(def.Element) : Theme.ElementSoft(def.Element),
                    selected ? Color.white : Theme.ElementSoftFg(def.Element),
                    22, new Vector2(56, 56), 12);
                _tileRects[charId] = (RectTransform)tile.transform; // 同名部件取最后一个,动效近似即可
            }
        }

        private void DrawSuggest()
        {
            var suggest = ForgeEngine.Suggest(_graph, Battle.Pool, Battle.Library);
            DrawNearMissHints(suggest.NearMisses); // 左侧差字面板:选中与否都显示
            if (_selectedChar != null || _targeting) return; // 选中态:拆合台交给拆字+动作两行
            if (suggest.Composable.Count == 0)
                Ui.ThemedLabel(_suggestRow, "凑齐部件即可合字", 15, Theme.TextDim);
            foreach (var id in suggest.Composable)
            {
                string charId = id;
                var def = _graph.Get(charId);
                var combo = Ui.Row(_suggestRow, $"Combo_{charId}", 4);
                foreach (var part in def.Recipe)
                {
                    var partDef = _graph.Get(part);
                    Ui.RoundButton(combo.transform, part, null,
                        Theme.ElementColor(partDef.Element), Color.white, 15, new Vector2(34, 34), 8);
                }
                Ui.ThemedLabel(combo.transform, "=", 14, Theme.TextDim);
                Ui.RoundButton(combo.transform, charId, () => OnCompose(charId),
                    Theme.Ink, Color.white, 17, new Vector2(40, 40), 8);
            }
        }

        private static readonly string[] HintBucketOrder = { "金", "木", "水", "火", "土", "心", "中性" };

        /// <summary>差字面板(屏幕左侧竖排):统一五行三级目录——属性→字→差什么,玩家主动查。</summary>
        private void DrawNearMissHints(System.Collections.Generic.IReadOnlyList<NearMiss> nearMisses)
        {
            if (nearMisses.Count == 0) return;

            // 分桶:目标字的五行属性(心/中性单列,避免丢字)
            var buckets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NearMiss>>();
            foreach (var miss in nearMisses)
            {
                var element = _graph.Get(miss.CharId).Element;
                string key = element is { } e ? ElementName(e) : "中性";
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new System.Collections.Generic.List<NearMiss>();
                list.Add(miss);
            }

            // 一级:属性胶囊竖排(带可合数),点选/再点收起
            Ui.ThemedLabel(_hintColumn, "配字表", 16, Theme.TextDim, Theme.TitleFont);
            foreach (var key in HintBucketOrder)
            {
                if (!buckets.TryGetValue(key, out var list)) continue;
                bool selected = _hintBucket == key;
                var element = ElementByName(key);
                Ui.RoundButton(_hintColumn, $"{key} {list.Count}", () =>
                {
                    _hintBucket = selected ? null : key;
                    _hintCharFocus = null;
                    Refresh();
                }, selected ? Theme.ElementColor(element) : Theme.ElementSoft(element),
                    selected ? Color.white : Theme.ElementSoftFg(element), 14, new Vector2(96, 30), 8);
            }

            if (_hintBucket == null || !buckets.TryGetValue(_hintBucket, out var bucketChars)) return;

            // 二级:该系可合的字(窄栏每行 4,最多四行,超出计数)
            const int perRow = 4, maxShown = 16;
            Transform row = null;
            NearMiss? focused = null;
            for (int i = 0; i < bucketChars.Count && i < maxShown; i++)
            {
                if (i % perRow == 0) row = Ui.Row(_hintColumn, $"HintChars{i / perRow}", 4).transform;
                var miss = bucketChars[i];
                bool focus = _hintCharFocus == miss.CharId;
                if (focus) focused = miss;
                var def = _graph.Get(miss.CharId);
                Ui.RoundButton(row, miss.CharId, () =>
                {
                    _hintCharFocus = focus ? null : miss.CharId;
                    Refresh();
                }, focus ? Theme.Ink : Theme.CardWhite,
                    focus ? Color.white : Theme.ElementColor(def.Element), 15, new Vector2(34, 30), 8);
            }
            if (bucketChars.Count > maxShown)
                Ui.ThemedLabel(_hintColumn, $"…共 {bucketChars.Count} 字", 13, Theme.TextDim);

            // 三级:差什么
            if (focused is { } target)
            {
                var def = _graph.Get(target.CharId);
                Ui.ThemedLabel(_hintColumn,
                    $"「{target.CharId}」= {string.Join("+", def.Recipe)},差「{target.MissingIngredient}」",
                    14, Theme.TextMain);
            }
        }

        private static Element? ElementByName(string name) => name switch
        {
            "金" => Element.Metal,
            "木" => Element.Wood,
            "水" => Element.Water,
            "火" => Element.Fire,
            "土" => Element.Earth,
            "心" => Element.Heart,
            _ => null,
        };

        private void DrawActions()
        {
            if (_selectedChar == null) return;
            var def = _graph.Get(_selectedChar);

            // 第一行(拆字):选中字 → 部件拆解
            Ui.RoundButton(_suggestRow, def.Id, null, Theme.Ink, Color.white, 22, new Vector2(52, 52), 12);
            if (!def.IsLeaf)
            {
                Ui.ThemedLabel(_suggestRow, "→", 16, Theme.TextDim);
                foreach (var part in def.Recipe)
                    Ui.RoundButton(_suggestRow, part, null,
                        Theme.ElementColor(_graph.Get(part).Element), Color.white, 16, new Vector2(38, 38), 8);
            }
            else
            {
                Ui.ThemedLabel(_suggestRow, "(独体字,不可拆)", 14, Theme.TextDim);
            }

            // 第二行(动作)
            if (_targeting)
            {
                Ui.ThemedLabel(_actionRow, $"「{_selectedChar}」点击目标敌人", 16, Theme.TextMain);
                Ui.RoundButton(_actionRow, "取消", CancelSelection, Theme.LockedBg, Theme.TextMain, 15, new Vector2(84, 40));
                return;
            }
            bool inLibrary = System.Linq.Enumerable.Contains(Battle.Library, _selectedChar);
            string castLabel = def.Effects.Count > 0 ? (inLibrary ? "出字" : "直出") : "兜底一击";
            Ui.RoundButton(_actionRow, castLabel, () => OnCastPressed(def), Theme.Cinnabar, Color.white, 16, new Vector2(90, 44));
            if (inLibrary && !def.IsLeaf)
                Ui.RoundButton(_actionRow, "拆", () => OnDismantle(def.Id), Theme.SplitBlue, Color.white, 16, new Vector2(60, 44));
            Ui.RoundButton(_actionRow, "丢弃", () => OnDiscard(def.Id), Theme.ExitPink, Color.white, 16, new Vector2(72, 44));
            Ui.RoundButton(_actionRow, "取消", CancelSelection, Theme.LockedBg, Theme.TextMain, 16, new Vector2(72, 44));
        }

        private void DrawEndTurn()
        {
            Ui.PillButton(_statusRow, "结束回合", OnEndTurn, Theme.Cinnabar, Color.white, 21, new Vector2(190, 52));
        }

        private void DrawBattleSettle()
        {
            bool won = Battle.Phase == BattlePhase.Won;
            Ui.ThemedLabel(_actionRow, won ? "本场胜利!" : "败北……", 36, Theme.TextMain, Theme.TitleFont);
            Ui.PillButton(_actionRow, "结算", () =>
            {
                _run.AdvanceAfterBattle();
                _message = _run.Phase == RunPhase.Reward ? "战利品:三选一(可跳过)" : "";
                Refresh();
            }, Theme.Jade, Color.white, 26, new Vector2(150, 70));
        }

        private void DrawReward()
        {
            Ui.ThemedLabel(_actionRow, "选一个字入库:", 22, Theme.TextMain, Theme.TitleFont);
            for (int i = 0; i < _run.RewardOptions.Count; i++)
            {
                int index = i;
                var id = _run.RewardOptions[i];
                var def = _graph.Get(id);
                Ui.GlyphTile(_actionRow, def, $"{def.ApCost} AP", false, () =>
                {
                    _run.PickReward(index);
                    _tutorial?.Notify(TutorialAction.PickReward);
                    _message = $"「{id}」入库,下一战!";
                    CancelSelection();
                });
            }
            Ui.RoundButton(_actionRow, "跳过", () =>
            {
                _run.SkipReward();
                _tutorial?.Notify(TutorialAction.PickReward); // 跳过也算完成"三选一"节拍,引导不卡死
                _message = "轻装上阵,下一战!";
                CancelSelection();
            }, Theme.LockedBg, Theme.TextMain, 18, new Vector2(80, 44));
        }

        private void DrawEvent() // 奇遇(9.6):短情境 + 选择;字摊类消费显示预算
        {
            var evt = _run.CurrentEvent;
            Ui.ThemedLabel(_enemyRow, $"奇遇 · {evt.Id}", 30, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(_statusRow, $"{evt.Text}    (墨锭 {_run.AvailableInk})", 18, Theme.TextDim);
            for (int i = 0; i < evt.Options.Count; i++)
            {
                int index = i;
                var option = evt.Options[i];
                bool affordable = option.InkCost <= _run.AvailableInk;
                var button = Ui.RoundButton(_actionRow, option.Label, () =>
                {
                    if (_run.ChooseEventOption(index))
                        _message = $"{evt.Id}:{option.Label}";
                    else
                        _message = "墨锭不足,换个选择";
                    CancelSelection();
                }, affordable ? Theme.InkSoft : Theme.LockedBg,
                    affordable ? Color.white : Theme.TextDim, 22, new Vector2(260, 72));
                button.interactable = affordable;
            }
        }

        private void DrawRunEnd()
        {
            bool won = _run.Phase == RunPhase.RunWon;
            Ui.ThemedLabel(_actionRow, won ? "关卡通过——字正!" : "败北", 40, Theme.TextMain, Theme.TitleFont);
            Ui.PillButton(_actionRow, "返回地图", () => _onRunEnded(won),
                Theme.Jade, Color.white, 26, new Vector2(170, 70));
            _message = won ? "通关结算:经验与墨锭入账。" : "死亡即结算,回地图重整旗鼓。";
        }

        // ---- 交互 ----

        private void OnLibraryCharClicked(string charId)
        {
            if (_selectedChar == charId && !_targeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中字 = 直接出字
                return;
            }
            _selectedChar = charId;
            _targeting = false;
            _message = CharInfo.Summary(_graph.Get(charId), _graph) + "|再点即出";
            Refresh();
        }

        private void OnPoolCharClicked(string charId)
        {
            if (_selectedChar == charId && !_targeting)
            {
                OnCastPressed(_graph.Get(charId)); // 再点一次选中部件 = 直出
                return;
            }
            _selectedChar = charId;
            _targeting = false;
            _message = CharInfo.Summary(_graph.Get(charId), _graph) + "|直出:部件不入库直接打出|再点即出";
            Refresh();
        }

        private void OnCastPressed(CharDef def)
        {
            if (BattleEngine.NeedsTarget(def) && AliveEnemyCount() > 1)
            {
                _targeting = true;
                _message = $"「{def.Id}」:点击目标敌人";
                Refresh();
                return;
            }
            ExecuteCast(def.Id, -1); // 单敌免选:引擎自动锁定唯一存活目标
        }

        private int AliveEnemyCount()
        {
            int count = 0;
            foreach (var enemy in Battle.Enemies)
                if (enemy.Alive) count++;
            return count;
        }

        private void OnEnemyClicked(int index)
        {
            if (_targeting && _selectedChar != null)
                ExecuteCast(_selectedChar, index);
        }

        private void ExecuteCast(string charId, int target)
        {
            bool hasFrom = TryGetTilePos(charId, out var fromPos); // 起点须在重绘销毁字牌前捕获
            var error = Battle.Cast(charId, target);
            if (error == BattleError.None)
                _tutorial?.Notify(TutorialAction.Cast, charId);
            _message = error == BattleError.None ? $"出「{charId}」!" : Describe(error);
            AppendBossPhaseMessage();
            CancelSelection();
            if (error == BattleError.None)
            {
                // 飞牌到首个受击敌人,到达才播结算表现;事件快照防连点串场
                var events = new System.Collections.Generic.List<BattleEvent>(Battle.LastEvents);
                var toRect = CastTargetRect(events);
                if (hasFrom && toRect != null)
                    _juice.FlyGlyph(charId, Theme.ElementColor(_graph.Get(charId).Element), fromPos, toRect.position,
                        () => _juice.Play(events, i => i >= 0 && i < _enemyRects.Count ? _enemyRects[i] : null));
                else
                    PlayJuice(); // 无伤害目标(纯护盾等)或起点缺失:即时表现
                MaybeAutoEndTurn();
            }
        }

        /// <summary>出字动效终点:第一个受击/受灼敌人格;没有则 null。</summary>
        private RectTransform CastTargetRect(System.Collections.Generic.IReadOnlyList<BattleEvent> events)
        {
            foreach (var e in events)
                if ((e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.Burn)
                    && e.TargetIndex >= 0 && e.TargetIndex < _enemyRects.Count)
                    return _enemyRects[e.TargetIndex];
            return null;
        }

        private float _autoEndDueAt; // AP 耗尽后自动结束回合的时点;每次动作重置,给连续丢弃留手

        /// <summary>AP 耗尽自动结束回合:留短缓冲看清结算;免 AP 丢弃会顺延缓冲。</summary>
        private void MaybeAutoEndTurn()
        {
            if (_run.Phase != RunPhase.InBattle || Battle.Phase != BattlePhase.PlayerTurn || Battle.Ap != 0)
                return;
            _autoEndDueAt = Time.unscaledTime + 0.45f;
            StartCoroutine(AutoEndTurn());
        }

        private System.Collections.IEnumerator AutoEndTurn()
        {
            while (Time.unscaledTime < _autoEndDueAt)
                yield return null;
            if (_run.Phase != RunPhase.InBattle || Battle.Phase != BattlePhase.PlayerTurn || Battle.Ap != 0)
                yield break; // 期间局面已变(胜负已分/新回合)则作罢
            OnEndTurn();
            _messageLabel.text = "AP 耗尽,自动结束回合 · " + _message;
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
            if (error == BattleError.None)
                MaybeAutoEndTurn(); // 0 AP 时的丢弃:顺延自动结束缓冲,可连续丢
        }

        private void OnDismantle(string charId)
        {
            bool hasFrom = TryGetTilePos(charId, out var fromPos);
            var recipe = _graph.Get(charId).Recipe;
            var error = Battle.Dismantle(charId);
            if (error == BattleError.None)
                _tutorial?.Notify(TutorialAction.Dismantle, charId);
            _message = error == BattleError.None ? $"拆「{charId}」" : Describe(error);
            CancelSelection();
            if (error == BattleError.None)
            {
                if (hasFrom) // 部件从原字牌位置散落到池中新位(重绘后已登记)
                    foreach (var part in recipe)
                    {
                        if (!_tileRects.TryGetValue(part, out var partTile) || partTile == null) continue;
                        var target = partTile;
                        _juice.FlyGlyph(part, Theme.ElementColor(_graph.Get(part).Element), fromPos, target.position,
                            () => _juice.PopTile(target));
                    }
                MaybeAutoEndTurn();
            }
        }

        private void OnCompose(string charId)
        {
            // 起点须在重绘销毁部件牌前捕获
            var partsFrom = new System.Collections.Generic.List<(string glyph, Vector3 pos)>();
            foreach (var part in _graph.Get(charId).Recipe)
                if (TryGetTilePos(part, out var pos))
                    partsFrom.Add((part, pos));
            var error = Battle.Compose(charId);
            if (error == BattleError.None)
                _tutorial?.Notify(TutorialAction.Compose, charId);
            _message = error == BattleError.None ? $"合出「{charId}」!" : Describe(error);
            CancelSelection();
            if (error == BattleError.None)
            {
                if (_tileRects.TryGetValue(charId, out var resultTile) && resultTile != null && partsFrom.Count > 0)
                {
                    int arrived = 0; // 闭包共享:全部到齐才弹跳
                    foreach (var (glyph, pos) in partsFrom)
                        _juice.FlyGlyph(glyph, Theme.ElementColor(_graph.Get(glyph).Element), pos, resultTile.position,
                            () => { if (++arrived == partsFrom.Count) _juice.PopTile(resultTile); });
                }
                MaybeAutoEndTurn();
            }
        }

        private void OnEndTurn()
        {
            Battle.EndTurn();
            _tutorial?.Notify(TutorialAction.EndTurn);
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
