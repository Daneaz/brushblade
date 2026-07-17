using System;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>主界面(20.2 无尽外层):角色状态头 + 无尽书塔面板 + 宝箱栏(19.5)。</summary>
    public sealed class MapView : MonoBehaviour
    {
        private RecipeGraph _graph;
        private CampaignConfig _campaign;
        private MetaState _meta;
        private ITimeSource _time;
        private Action _onStartTower;
        private Action _save;
        private Action _onOpenCollection;
        private Action _onOpenShop;
        private string _message;

        // 计时中箱位的倒计时/加速价标签引用:Tick 只改文本不重建,避免按钮每秒被销毁点不中
        private readonly System.Collections.Generic.List<(int index, Text countdown, Text skipCost)> _countdowns = new();
        private GameObject _resultPanel; // 开箱结果面板;打开期间禁止整页重建

        public void Init(RecipeGraph graph, CampaignConfig campaign, MetaState meta, ITimeSource time,
            Action onStartTower, Action save, string message, Action onOpenCollection, Action onOpenShop)
        {
            _graph = graph;
            _onOpenShop = onOpenShop;
            _campaign = campaign;
            _meta = meta;
            _time = time;
            _onStartTower = onStartTower;
            _save = save;
            _onOpenCollection = onOpenCollection;
            _message = message ?? "";
            Rebuild();
            InvokeRepeating(nameof(Tick), 1f, 1f); // 倒计时刷新
        }

        private void Tick()
        {
            // 计时中:只更新倒计时与加速价文本;跃迁到就绪才整页重建(结果面板打开时押后到关闭)
            bool becameReady = false;
            foreach (var (index, countdown, skipCost) in _countdowns)
            {
                if (index >= _meta.Chests.Count) continue;
                var chest = _meta.Chests[index];
                if (!chest.Timing) continue;
                if (ChestRules.IsReady(chest, _time))
                {
                    becameReady = true;
                    continue;
                }
                long remaining = ChestRules.RemainingSeconds(chest, _time);
                if (countdown != null) countdown.text = Format(remaining);
                if (skipCost != null) skipCost.text = $"{ChestRules.InkCostToSkip(remaining)}墨";
            }
            if (becameReady && _resultPanel == null)
                Rebuild();
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            var root = (RectTransform)transform;
            Ui.Stretch(root);

            // 角色状态头
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var header = Ui.Row(transform, "Header", 22);
            Ui.Anchor((RectTransform)header.transform, new Vector2(0.02f, 0.9f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero);
            Ui.ThemedLabel(header.transform, $"正字者 Lv.{level}", 28, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(header.transform,
                $"经验 {_meta.CharacterXp}    HP 上限 {MetaRules.MaxHpFor(level)}", 20, Theme.TextDim);
            Ui.IngotLabel(header.transform, _meta.Ink.ToString(), 22);
            var collectionButton = Ui.RoundButton(header.transform, "收集/卡组", () => _onOpenCollection(),
                Theme.InkSoft, Color.white, 20, new Vector2(140, 50), 12);
            if (AnyCardUpgradable()) // 可升级红点导航
            {
                var dot = Ui.Panel(collectionButton.transform, "Dot");
                var dotImage = dot.AddComponent<Image>();
                dotImage.sprite = Theme.Circle;
                dotImage.color = Theme.Cinnabar;
                dotImage.raycastTarget = false;
                Ui.Anchor((RectTransform)dot.transform, Vector2.one, Vector2.one,
                    new Vector2(-16, -16), new Vector2(-4, -4));
            }
            Ui.RoundButton(header.transform, "商城", () => _onOpenShop(),
                Theme.ShopNav, Color.white, 20, new Vector2(100, 50), 12);

            // 消息横幅(绿胶囊,有内容才显示)
            if (!string.IsNullOrEmpty(_message))
            {
                var banner = Ui.Row(transform, "Banner");
                Ui.Anchor((RectTransform)banner.transform, new Vector2(0.25f, 0.845f), new Vector2(0.75f, 0.9f), Vector2.zero, Vector2.zero);
                var pill = Ui.CardPanel(banner.transform, "Pill", Theme.AdGreenBg, 24);
                var pillElement = pill.gameObject.AddComponent<LayoutElement>();
                pillElement.preferredWidth = 560;
                pillElement.preferredHeight = 40;
                var text = Ui.ThemedLabel(pill.transform, _message, 17, Theme.AdGreenText);
                Ui.Stretch(text.rectTransform);
            }

            // 无尽书塔面板(20.2):段位/最高层 + 层段进度 + 登塔/续爬
            var endless = _campaign.Endless;
            var tower = Ui.CardPanel(transform, "Tower");
            Ui.Anchor((RectTransform)tower.transform, new Vector2(0.2f, 0.27f), new Vector2(0.8f, 0.84f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(tower.transform, "Stack", 14);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, "无尽书塔", 32, Theme.TextMain, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform,
                $"段位「{EndlessRules.RankTitle(_meta.BestDepth)}」 · 最高第 {_meta.BestDepth} 层",
                20, Theme.TextDim);

            // 层段进度:已踏入亮色,未至灰色
            var bands = Ui.Row(stack.transform, "Bands", 10);
            foreach (var band in endless.Bands)
            {
                bool reached = band.FromDepth == 1 || _meta.BestDepth >= band.FromDepth;
                Ui.Chip(bands.transform, $"{band.Name} {band.FromDepth}层起",
                    reached ? Theme.InkSoft : Theme.LockedBg,
                    reached ? Color.white : Theme.LockGray, 15);
            }

            var snapshot = _meta.Endless;
            string label = snapshot == null
                ? "登 塔"
                : $"继续 · 「{endless.BandFor(snapshot.Depth).Name}」第 {snapshot.Depth} 层";
            Ui.PillButton(stack.transform, label, () => _onStartTower(),
                Theme.Cinnabar, Color.white, 24, new Vector2(300, 62));
            if (snapshot != null)
                Ui.ThemedLabel(stack.transform,
                    $"进行中:HP {snapshot.PlayerHp} · 滚存墨锭 {snapshot.EarnedInk}", 16, Theme.TextDim);
            else
                Ui.ThemedLabel(stack.transform, "每 5 层一位 Boss,战胜后可收官或深入", 16, Theme.TextDim);

            DrawChestBar();
        }

        // ---- 宝箱栏(19.5) ----

        private void DrawChestBar()
        {
            _countdowns.Clear(); // 旧标签随 Ui.Clear 已销毁,重建时重新登记
            var bar = Ui.Row(transform, "Chests", 18);
            Ui.Anchor((RectTransform)bar.transform, new Vector2(0, 0.02f), new Vector2(1, 0.24f), Vector2.zero, Vector2.zero);

            Ui.ThemedLabel(bar.transform, $"箱位\n{_meta.Chests.Count}/{ChestRules.SlotLimit}", 18, Theme.TextDim, Theme.TitleFont);

            for (int i = 0; i < ChestRules.SlotLimit; i++)
            {
                if (i >= _meta.Chests.Count) { DrawEmptySlot(bar.transform); continue; }
                int index = i;
                var chest = _meta.Chests[i];
                bool ready = chest.Timing && ChestRules.IsReady(chest, _time);

                var card = Ui.CardPanel(bar.transform, $"Chest{i}", Theme.CardWhite, 14);
                var cardElement = card.gameObject.AddComponent<LayoutElement>();
                cardElement.preferredWidth = 168;
                cardElement.preferredHeight = 150;
                var stack = Ui.VStack(card.transform, "Stack", 5);
                Ui.Stretch((RectTransform)stack.transform);

                // 箱型图标:档位色圆角块 + 档位首字(两套形状按档位色区分;19.5.1 六档)
                var iconRow = Ui.Row(stack.transform, "Icon", 0);
                var icon = Ui.CardPanel(iconRow.transform, "Body",
                    Theme.RarityColor((Brushblade.Core.CardRarity)(int)chest.Tier), 10);
                var iconElement = icon.gameObject.AddComponent<LayoutElement>();
                iconElement.preferredWidth = 52;
                iconElement.preferredHeight = 40;
                var iconGlyph = Ui.ThemedLabel(icon.transform, ChestRules.TierName(chest.Tier).Substring(0, 1),
                    22, Color.white, Theme.TitleFont);
                Ui.Stretch(iconGlyph.rectTransform);

                Ui.ThemedLabel(stack.transform, ChestRules.TierName(chest.Tier), 17, Theme.TextMain, Theme.TitleFont);

                if (!chest.Timing)
                {
                    bool anotherTiming = AnyChestTiming();
                    var start = Ui.RoundButton(stack.transform, "开始开启",
                        () => Do(() => ChestRules.TryStartOpening(_meta, index, _time)),
                        Theme.InkSoft, Color.white, 15, new Vector2(140, 36));
                    start.interactable = !anotherTiming;
                }
                else if (ready)
                {
                    Ui.RoundButton(stack.transform, "开箱!", () => OpenChest(index),
                        Theme.Gold, Theme.GoldText, 16, new Vector2(140, 36));
                }
                else
                {
                    long remaining = ChestRules.RemainingSeconds(chest, _time);
                    var countdown = Ui.ThemedLabel(stack.transform, Format(remaining), 15, Theme.TextDim);
                    var mini = Ui.Row(stack.transform, "Mini", 5);
                    if (!chest.AdUsed)
                    {
                        long cut = ChestRules.AdReductionSeconds[(int)chest.Tier - 1];
                        Ui.AdBadge(mini.transform, $"-{cut / 60}m", // 原型:直接生效,广告 SDK 后接
                            () => Do(() => ChestRules.TryApplyAdBoost(chest)), new Vector2(74, 34));
                    }
                    var skip = Ui.RoundButton(mini.transform, $"{ChestRules.InkCostToSkip(remaining)}墨",
                        () => Do(() => ChestRules.TrySkipWithInk(_meta, index, _time)),
                        Theme.Gold, Theme.GoldText, 14, new Vector2(70, 34));
                    _countdowns.Add((index, countdown, skip.GetComponentInChildren<Text>()));
                }
            }
        }

        private void DrawEmptySlot(Transform parent)
        {
            var slot = Ui.CardPanel(parent, "Empty", Theme.LockedBg, 14);
            var slotElement = slot.gameObject.AddComponent<LayoutElement>();
            slotElement.preferredWidth = 168;
            slotElement.preferredHeight = 150;
            var label = Ui.ThemedLabel(slot.transform, "空位", 16, Theme.LockGray);
            Ui.Stretch(label.rectTransform);
        }

        private bool AnyCardUpgradable()
        {
            foreach (var id in _meta.OwnedCards)
                if (MetaRules.CanUpgradeCard(_meta, id, _graph.Get(id).Rarity))
                    return true;
            return false;
        }

        private bool AnyChestTiming()
        {
            foreach (var chest in _meta.Chests)
                if (chest.Timing && !ChestRules.IsReady(chest, _time))
                    return true;
            return false;
        }

        private void OpenChest(int index)
        {
            string tierName = ChestRules.TierName(_meta.Chests[index].Tier);
            var ownedBefore = new System.Collections.Generic.HashSet<string>(_meta.OwnedCards);
            if (ChestRules.TryOpen(_meta, index, _time, new GameRandom(Environment.TickCount), out var rewards, _graph))
            {
                _save();
                _message = "";
                Rebuild();
                ShowChestResult(tierName, rewards, ownedBefore);
                return;
            }
            Rebuild();
        }

        // ---- 开箱结果面板:逐张翻卡,新卡标「新!」,重复卡显示升级进度 ----

        private void ShowChestResult(string tierName, ChestRewards rewards,
            System.Collections.Generic.HashSet<string> ownedBefore)
        {
            var scrim = Ui.Panel(transform, "ChestResult");
            _resultPanel = scrim;
            var scrimImage = scrim.AddComponent<Image>();
            scrimImage.color = Theme.Scrim; // raycastTarget 默认 true:挡住底下所有点击
            Ui.Stretch((RectTransform)scrim.transform);

            var card = Ui.CardPanel(scrim.transform, "Panel");
            Ui.Anchor((RectTransform)card.transform, new Vector2(0.16f, 0.16f), new Vector2(0.84f, 0.84f), Vector2.zero, Vector2.zero);
            var stack = Ui.VStack(card.transform, "Stack", 14);
            Ui.Stretch((RectTransform)stack.transform);

            Ui.ThemedLabel(stack.transform, $"「{tierName}」开启!", 28, Theme.TextMain, Theme.TitleFont);
            Ui.IngotLabel(stack.transform, $"+{rewards.Ink}", 22);

            // 字卡:每行最多 8 张(赤霄 16 张两行),先隐藏再逐张弹出
            var tiles = new System.Collections.Generic.List<GameObject>();
            var seen = new System.Collections.Generic.HashSet<string>(ownedBefore);
            Transform row = null;
            for (int i = 0; i < rewards.Cards.Count; i++)
            {
                if (i % 8 == 0) row = Ui.Row(stack.transform, $"CardRow{i / 8}", 10).transform;
                string cardId = rewards.Cards[i];
                var def = _graph.Get(cardId);
                bool isNew = seen.Add(cardId);

                var cell = Ui.VStack(row, $"Reward_{cardId}_{i}", 4);
                Ui.GlyphTile(cell.transform, def, "", false, null, new Vector2(76, 96));
                if (isNew)
                {
                    Ui.Chip(cell.transform, "新!", Theme.ExitPink, Color.white, 12);
                }
                else
                {
                    int level = MetaRules.CardLevel(_meta, cardId);
                    _meta.CardCopies.TryGetValue(cardId, out int copies);
                    string progress = level >= MetaRules.MaxCardLevel
                        ? "满级"
                        : $"升级 {copies}/{MetaRules.CopiesRequired(level, def.Rarity)}";
                    Ui.Chip(cell.transform, progress, Theme.AdGreenBg, Theme.UpgradeText, 12);
                }
                cell.SetActive(false);
                tiles.Add(cell);
            }

            Ui.PillButton(stack.transform, "收下", () =>
            {
                Destroy(_resultPanel);
                _resultPanel = null;
                Rebuild(); // 面板期间押后的就绪跃迁在此补上
            }, Theme.Cinnabar, Color.white, 20, new Vector2(180, 50));

            StartCoroutine(RevealTiles(tiles));
        }

        private System.Collections.IEnumerator RevealTiles(System.Collections.Generic.List<GameObject> tiles)
        {
            foreach (var tile in tiles)
            {
                if (tile == null) yield break; // 面板已被「收下」关闭
                tile.SetActive(true);
                var rect = (RectTransform)tile.transform;
                float t = 0;
                while (t < 0.12f)
                {
                    if (rect == null) yield break;
                    t += Time.unscaledDeltaTime;
                    rect.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, t / 0.12f);
                    yield return null;
                }
                if (rect != null) rect.localScale = Vector3.one;
                yield return new WaitForSecondsRealtime(0.08f);
            }
        }

        private void Do(Func<bool> action)
        {
            if (action())
                _save();
            Rebuild();
        }

        private static string Format(long seconds) =>
            seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }
}
