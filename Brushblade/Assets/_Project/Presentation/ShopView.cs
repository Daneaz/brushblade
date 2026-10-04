using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using Brushblade.Platform;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>每日商城页(19.6)。2026-09-20 按设计系统的 ShopScreen 重设计稿重写:
    /// 左「今日字摊」(2026-09-30 起 2 行 × 6 列:3 个字卡广告位 + 4–9 个墨锭摊位)
    /// + 右栏「今日补给」(宝箱位 / 免费补给),顶栏与收集 / 图鉴 / 技能逐数同款。
    ///
    /// 旧版(按屏幕比例锚点摆的三行)被换掉的三件事:
    ///   ① 不走外层页骨架 —— 顶栏高度、标题字号、返回钮尺寸各是一套,也没有安全区内缩;
    ///   ② 货架上看不出「这张字我几级、还差几张」,而买字卡的真实动机就是凑升级材料;
    ///   ③ 常驻消息行占掉一整行,只为显示「购入成功」——改成顶部居中的墨色 toast。
    ///
    /// 数值规则一条没动(卡价按稀有度、箱价按档、两个广告位每日各一次),全部仍在 ShopRules。</summary>
    public sealed class ShopView : MonoBehaviour
    {
        // 与 CollectionView / BestiaryView 同一套骨架常量(稿面 pt × 2.093)
        private const float TopH = 80f;        // 顶栏 38pt
        private const float SideW = 527f;      // 右栏 252pt
        private const float MainGap = 19f;     // 主区与右栏之间 9pt
        private const float SideHeadH = 54f;   // 右栏头 26pt
        private const float SidePad = 21f;     // 右栏内边距 10pt
        private const float Gap = 13f;         // 行距 6pt

        // 摊位字牌:宽按左侧实际可用宽度 6 列等分,夹在 [96, 144],高 = 宽 × 1.25(Rebuild 里算)。
        // 画布按高适配,左侧宽度随屏幕比例与安全区补边变:16:9 非刘海屏只有约 808,
        // 定宽 144 × 6 会溢出(2026-09-30 并入广告位、铺满 6 列时发现)
        private const float CardMaxW = 144f, CardMinW = 96f, CardAspect = 1.25f;
        private Vector2 CardSize = new(CardMaxW, CardMaxW * CardAspect);
        private float _columnW = CardMaxW; // 货架一列的实宽(未夹取):宽屏上比字牌宽,预告行可借用
        private const float SlotGap = 21f;
        // 一格竖排 = 字牌 180 + 牌脚 16.9(牌高 × 12/128)+ 预告 ≈27.5(19 号 Noto Sans SC 行高 1.448)
        // + 钮 46 + 3 段间距 = 270.4 + 3 × CellGap。2026-10-04 实算:原先间距 8、行高 280 时
        // 需要 ≈294.4,超出的那截被竖排布局按比例压扁(字牌连带变矮)。行高又不能单独加 ——
        // 两行 + 订阅条在 900 高下只剩 600(行 0 顶 67 → 订阅条顶 667),行高 ≤ 287。
        // 所以间距收到 4、行高 284(留 ≈1.6 余量):行 1 底 648,离订阅条 19。
        // 以上是牌宽 144 的账;牌变窄时省下的高让给预告行折行(FitCaption)。
        private const float CellGap = 4f;
        private const float SlotRowH = 284f;
        private const float BuyH = 46f;        // 价格钮高(宝箱格同款)
        private const float SubBarH = 96f;     // 订阅条

        private RecipeGraph _graph;
        private MetaState _meta;
        private IReadOnlyList<string> _cardPool;  // 卡位池:已拥有的非部件字
        private IReadOnlyList<string> _chestPool; // 宝箱池:全部可收集字(未拥有的字只出宝箱)
        private ITimeSource _time;
        private Action _save;
        private Action _onBack;
        private GameObject _modal; // 当前弹窗(同屏仅一个)
        private string _toast;     // 成交反馈:画一次就清,不再是常驻消息行
        private Text _countdown;   // 顶栏副标题:刷新倒计时,Tick 每秒改字

        public void Init(RecipeGraph graph, MetaState meta, IReadOnlyList<string> cardPool,
            IReadOnlyList<string> chestPool, ITimeSource time, Action save, Action onBack)
        {
            _graph = graph;
            _meta = meta;
            _cardPool = cardPool;
            _chestPool = chestPool;
            _time = time;
            _save = save;
            _onBack = onBack;
            Rebuild();
            InvokeRepeating(nameof(Tick), 1f, 1f); // 倒计时刷新(与 MapView 箱位倒计时同一种写法)
        }

        /// <summary>每秒:同日内只改倒计时那行字;跨 UTC 日则重摆货架并整页重建。
        /// 跨日但有弹窗(字卡详情 / 告知)开着时押后到关闭 —— Rebuild 会 Ui.Clear 掉挂在根上的弹窗;
        /// 押后期间倒计时**不改字**(停在 0:00),免得货架还是昨天的、倒计时却跳回 23:xx。</summary>
        private void Tick()
        {
            if (_time.NowUnixSeconds / 86400 != _meta.Shop.DayStamp)
            {
                if (_modal != null) return;
                ShopRules.EnsureShelf(_meta, _cardPool, _time, new GameRandom(Environment.TickCount),
                    id => _graph.Get(id).Rarity, _chestPool); // 与 GameRoot.ShowShop 同一组参数
                ShopRules.MarkVisited(_meta, _time); // 人就在商城里,新一天的红点不该再亮
                _save();
                Rebuild();
                return;
            }
            if (_countdown != null)
                _countdown.text = Strings.T("shop.header.refresh_in", ("time", RefreshCountdown()));
        }

        private void Rebuild()
        {
            Ui.Clear(transform);
            Ui.Stretch((RectTransform)transform);

            // 安全区内缩与其余外层页同一条(SafeArea 只有这一份,别另抄)
            var (padSide, padBottom) = SafeArea.MissingInset();
            // 宽取**安全区**宽:SafeAreaFitter 已在父级让掉了 Screen.safeArea 之外那段,这里只再扣
            // MissingInset 补的差额。2026-10-04 前拿整屏宽算,刘海机(基准机 932×430pt:整屏 1951、
            // 安全区 ≈1705)上左栏多算 246 —— 字牌宽被 144 上限夹住没露馅,但预告行按列距排字要真宽。
            float safeW = Screen.height > 0 ? 900f * Screen.safeArea.width / Screen.height : 1600f;
            float shelfW = safeW - 2f * padSide - SideW - MainGap;
            _columnW = (shelfW - (ShelfColumns - 1) * SlotGap) / ShelfColumns;
            float cardW = Mathf.Clamp(_columnW, CardMinW, CardMaxW);
            CardSize = new Vector2(cardW, cardW * CardAspect);
            var content = Ui.Panel(transform, "Content");
            Ui.Anchor((RectTransform)content.transform, Vector2.zero, Vector2.one,
                new Vector2(padSide, padBottom), new Vector2(-padSide, 0));
            var frame = content.transform;

            BuildTopBar(frame);

            var main = Ui.Panel(frame, "Main");
            Ui.Anchor((RectTransform)main.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0, -(TopH + Gap)));

            BuildShelf(main.transform);
            BuildSide(main.transform);

            if (!string.IsNullOrEmpty(_toast)) StartCoroutine(ToastRoutine(ShowToast(frame, _toast)));
            _toast = null; // 一次性:下次 Rebuild 不再复现
        }

        // ---- 顶栏 ----

        private void BuildTopBar(Transform parent)
        {
            var top = Ui.Row(parent, "Top", 21);
            top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)top.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -TopH), Vector2.zero);

            Ui.ThemedLabel(top.transform, Strings.T("shop.header.title"), 40, Theme.TextMain, Theme.TitleFont);
            // 副标题是刷新倒计时(UTC 0 点重掷),不是那句常驻说明 —— 玩家真正要的是「还有多久换货」
            _countdown = Ui.ThemedLabel(top.transform,
                Strings.T("shop.header.refresh_in", ("time", RefreshCountdown())), 23, Theme.TextDim);

            var spring = Ui.Panel(top.transform, "Spring");
            spring.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.InkCounter(top.transform, _meta.Ink, 25);
            Ui.PillButton(top.transform, Strings.T("common.back_to_map"), () => _onBack(),
                Theme.ExitPink, Color.white, 25, new Vector2(130, 63));
        }

        /// <summary>距下次刷新(UTC 次日 0 点)还有多久,格式 h:mm。</summary>
        private string RefreshCountdown()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(_time.NowUnixSeconds).UtcDateTime;
            var next = now.Date.AddDays(1);
            var left = next - now;
            return $"{(int)left.TotalHours}:{left.Minutes:00}";
        }

        // ---- 左:今日字摊 ----
        //
        // 2026-09-30 二版(用户要求):三个字卡广告位并入字摊,只是买法换成看广告;货架铺满整个左侧。
        // 版面 = 2 行 × 6 列 = 3 个广告位 + 9 个墨锭摊位(随角色等级 4→9 格),没开的摊位排在最后。
        // 每一列是**弹性等分**的 —— 画布按高适配,宽屏手机左侧会更宽,定宽排会在右边空出一截。

        private const int ShelfColumns = 6;
        private const int ShelfRows = 2;

        private void BuildShelf(Transform parent)
        {
            var shelf = Ui.Panel(parent, "Shelf");
            Ui.Anchor((RectTransform)shelf.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-(SideW + MainGap), 0));

            var head = Ui.Row(shelf.transform, "ShelfHead", 13);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("shop.shelf.title"), 19, Theme.LockGray);

            int cell = 0;
            for (int r = 0; r < ShelfRows; r++)
            {
                var row = Ui.Row(shelf.transform, $"Row{r}", SlotGap);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
                float top = SideHeadH + Gap + r * (SlotRowH + Gap);
                Ui.Anchor((RectTransform)row.transform, new Vector2(0, 1), Vector2.one,
                    new Vector2(0, -(top + SlotRowH)), new Vector2(0, -top));
                for (int c = 0; c < ShelfColumns; c++, cell++)
                {
                    var column = Ui.VStack(row.transform, $"Col{cell}", 0);
                    column.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                    column.AddComponent<LayoutElement>().flexibleWidth = 1;

                    if (cell < ShopRules.AdOfferCount) { BuildAdSlot(column.transform, cell); continue; }
                    int slot = cell - ShopRules.AdOfferCount;
                    if (slot < _meta.Shop.CardSlots.Count) BuildSlot(column.transform, slot);
                    else LockedSlot(column.transform, slot, next: slot == _meta.Shop.CardSlots.Count);
                }
            }

            BuildSubscriptionBar(shelf.transform);
        }

        /// <summary>摊位牌面(墨锭摊位与广告位共用):字牌 + 角标 + 牌脚进度 + 买下后的进度预告。
        /// 返回这一格的竖排容器,调用方再往里放各自的按钮。</summary>
        private Transform CardFace(Transform parent, string name, string card, int bundle, bool done, string doneSeal,
            string discountText = null)
        {
            var def = _graph.Get(card);
            bool owned = _meta.OwnedCards.Contains(card);

            var cell = Ui.VStack(parent, name, CellGap);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(cell, width: CardSize.x, flexWidth: 0);

            var tile = Ui.GlyphTile(cell.transform, def, false, () => ShowPreview(def), CardSize,
                locked: !owned);
            int level = MetaRules.CardLevel(_meta, card);
            bool maxed = level >= MetaRules.MaxCardLevel;
            _meta.CardCopies.TryGetValue(card, out int copies);
            int needed = maxed ? 0 : MetaRules.CopiesRequired(level, def.Rarity);
            // 一份几张 → 右下角数量角标「10张」(2026-09-30)。数量属于这份货,不写在钮上
            // (「看广告 ×5」读起来像要看 5 次广告);走 CardBadges 与等级/可升同一套尺寸
            CardBadges.Apply(tile.gameObject, CardSize, new CardBadges.Spec
            {
                QuantityText = Strings.T("shop.slot.bundle_count", ("count", bundle)),
                DiscountText = done ? null : discountText, // 已售/已领不挂价签
                Rarity = def.Rarity,
                Level = level,
                Maxed = maxed,
                CanUpgrade = MetaRules.CanUpgradeCard(_meta, card, def.Rarity),
                IsNew = false, // 货架上的牌不是「新到手」,那枚角旗只属于卡组页
                Locked = !owned,
            });
            CardBadges.Foot(cell.transform, CardSize, owned, copies, needed, maxed,
                MetaRules.CanUpgradeCard(_meta, card, def.Rarity));
            // 已售 / 已领:牌面盖一枚朱砂印,看得出哪格今天已经拿走了
            if (done) SoldSeal(tile.gameObject, doneSeal);

            // 进度预告:拿下这一份之后会怎样。没拥有的字(紫档广告位)第一张是解锁,不写进度
            string forecast;
            Color forecastColor;
            if (!owned)
            {
                forecast = Strings.T("shop.slot.new_card_note");
                forecastColor = Theme.UpgradeText;
            }
            else if (maxed)
            {
                forecast = Strings.T("shop.slot.maxed_note");
                forecastColor = Theme.TextDim;
            }
            else if (copies + bundle >= needed)
            {
                forecast = Strings.T("shop.slot.unlocks_upgrade", ("level", level + 1));
                forecastColor = Theme.UpgradeText;
            }
            else
            {
                forecast = Strings.T("shop.slot.copies_after", ("copies", copies + bundle), ("needed", needed));
                forecastColor = Theme.TextDim;
            }
            FitCaption(Ui.ThemedLabel(cell.transform, forecast, 19, forecastColor));
            return cell.transform;
        }

        // 说明小字的字号阶梯:caption 19 → badge 18 → pinyin 17(system/ 字号 token 9 / 8.5 / 8pt × 2.093),
        // 不另发明字号
        private static readonly int[] CaptionSizes = { 19, 18, 17 };
        private const float CaptionGap = 10f; // 单行预告探进列间空隙时,与邻格预告之间至少留的白

        /// <summary>进度预告放进这一格(2026-10-04 按 1600 宽验算)。Unity Text 不折行,原先 19 号单行
        /// 一律居中溢出,两档宽都有字压到邻格:
        ///   · 16:9 无刘海(内容区 1354 → 列宽 = 牌宽 117.2):「买下后 19/20 张」135、「新字,拿到即解锁」139、
        ///     「买下即可升 Lv.10」148、「已满级,买了只进重复卡」196 全超牌宽;
        ///   · 基准机(安全区 1705 → 列 175.7、牌夹到 144):「已满级…」196 > 列距 196.7 − 10。
        /// 解法按格里剩多少高分两路,字号从 19 起逐档试:
        ///   ① 按牌宽折行,高度放得下就用 —— 16:9 牌 117×146,格里余 65.8 高,19 号两行(55)够,
        ///     上面四条都折成两行、不出本格;
        ///   ② 折行放不下(基准机牌 144×180,只余 29.1 = 一行 27.5)→ 单行,允许居中探进列间空隙,
        ///     宽 ≤ 列距 − CaptionGap(基准机 186.7):「买下后 499/500 张」157 仍 19 号,
        ///     「已满级…」降到 18 号(185.4)。
        /// 牌宽到下限 96 时(内容区 < 1255,两档都到不了)走 ①:余 94.8 高(三行 82.5),最长那条 19 号折三行,
        /// 万一实测折成四行则下一档 18 号折两行。</summary>
        private void FitCaption(Text label)
        {
            // 格里除预告外的定高:字牌 + 牌脚(牌高 × 12/128,CardBadges.FootHRatio)+ 钮 + 3 段间距
            float roomH = SlotRowH - (CardSize.y * (1f + 12f / 128f) + BuyH + 3 * CellGap);
            float pitchW = _columnW + SlotGap - CaptionGap;
            foreach (int size in CaptionSizes)
            {
                label.fontSize = size;
                label.horizontalOverflow = HorizontalWrapMode.Wrap; // ① 竖排布局给它牌宽,按牌宽折
                var wrapped = label.GetGenerationSettings(new Vector2(CardSize.x, 0));
                if (label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, wrapped)
                    / label.pixelsPerUnit <= roomH) return;
                label.horizontalOverflow = HorizontalWrapMode.Overflow; // ② 单行,居中探出牌宽
                if (label.preferredWidth <= pitchW && label.preferredHeight <= roomH) return;
            }
            // 阶梯走完仍放不下:停在最小档单行。1354 / 1705 两档实算都到不了这里(见上)
        }

        /// <summary>墨锭摊位:牌面 → 价格钮(写明「×张数 · 价格」)。</summary>
        private void BuildSlot(Transform parent, int index)
        {
            string card = _meta.Shop.CardSlots[index];
            bool sold = _meta.Shop.CardSold[index];
            var def = _graph.Get(card);
            int price = ShopRules.CardPrice(_meta, index, def.Rarity);   // 当日折后价(2026-10-01)
            int bundle = ShopRules.BundleSizeFor(def.Rarity);   // 一份几张(2026-09-30 按稀有度打包)
            int discount = ShopRules.DiscountPercent(ShopRules.IsPremium(def.Rarity),
                index < _meta.Shop.CardDiscountRoll.Count ? _meta.Shop.CardDiscountRoll[index] : 0);

            var cell = CardFace(parent, $"Slot{index}", card, bundle, sold, Strings.T("shop.slot.sold"),
                DiscountTag(discount));
            BuyButton(cell, CardSize.x, sold, price,
                sold ? Strings.T("shop.slot.sold_today") : Ui.InkText(price),
                () => Do(() => ShopRules.TryBuyCard(_meta, index, def.Rarity),
                    Strings.T("shop.card.buy_success", ("card", card), ("count", bundle)),
                    Strings.T("shop.card.buy_fail_title"),
                    Strings.T("shop.card.buy_fail_body", ("card", card), ("price", Ui.InkText(price)), ("ink", Ui.InkText(_meta.Ink)))),
                original: ShopRules.BundlePriceFor(def.Rarity));
        }

        private static readonly AdPlacement[] CardAdPlacements =
            { AdPlacement.ShopCardGreen, AdPlacement.ShopCardBlue, AdPlacement.ShopCardPurple };

        /// <summary>字卡广告位(2026-09-30 并入字摊):牌面与墨锭摊位同一套,只是钮换成看广告。
        /// 绿 ×10 / 蓝 ×5 只出已拥有的字;紫 ×1 可以是没拥有的字 —— 那种钮上写「看广告解锁」。</summary>
        private void BuildAdSlot(Transform parent, int tier)
        {
            var shop = _meta.Shop;
            string card = tier < shop.AdOffers.Count ? shop.AdOffers[tier] : "";
            bool claimed = tier < shop.AdOfferClaimed.Count && shop.AdOfferClaimed[tier];
            int count = ShopRules.AdOfferCards[tier];

            if (card == "")
            {
                // 这一档今天没得出(例如还一张绿字都没有):留一个空格说清楚,不让版面缺一块
                var empty = EmptyWell(parent, $"AdSlot{tier}", Theme.PanelBorder);
                var none = Ui.ThemedLabel(empty, Strings.T("shop.card_ad.none_label"), 19, Theme.LockGray);
                // 「今日暂无可领」19 号宽 114:基准机卡面 140(牌 144 − 描边 2×2)放得下;16:9 卡面 113.2
                // 会顶到描边 → 按字号阶梯缩到离卡面内沿各留 4(16:9 取 17 号,宽 102 ≤ 105.2)。
                // 牌宽到下限 96 时 17 号也放不下(102 > 84),改按卡面宽折两行,卡高 120 够
                float fitW = CardSize.x - 2 * 2f - 2 * 4f;
                foreach (int size in CaptionSizes)
                {
                    none.fontSize = size;
                    if (none.preferredWidth <= fitW) break;
                }
                if (none.preferredWidth > fitW)
                {
                    none.horizontalOverflow = HorizontalWrapMode.Wrap;
                    Ui.Anchor(none.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
                }
                return;
            }

            bool isNew = !_meta.OwnedCards.Contains(card);
            var cell = CardFace(parent, $"AdSlot{tier}", card, count, claimed, Strings.T("shop.slot.claimed"));
            // 钮只写买法,不写数量(数量在牌面角标上)—— 否则「看广告 ×5」像是要看 5 次
            string label = claimed ? Strings.T("shop.card_ad.claimed_label")
                : isNew ? Strings.T("shop.card_ad.claim_new_label")
                : Strings.T("shop.card_ad.claim_label");
            var badge = Ui.AdBadge(cell, label,
                () => AdGate.Watch(CardAdPlacements[tier],
                    () => Do(() => ShopRules.TryClaimCardAd(_meta, tier),
                        Strings.T("shop.card_ad.claim_success", ("card", card), ("count", count)),
                        Strings.T("shop.card_ad.already_title"), Strings.T("shop.card_ad.already_body"))),
                new Vector2(CardSize.x, BuyH));
            badge.interactable = !claimed;
        }

        /// <summary>还没解锁的摊位(2026-09-30 二版):与字牌同尺寸的描边卡,宋体大字「Lv.N」+「解锁」。
        /// **下一格**描金边、底下写「还差 N 级」—— 那是玩家眼下在追的目标;更远的几格淡一档,
        /// 只告诉你「后面还有」,不和眼前那格抢视线。</summary>
        private void LockedSlot(Transform parent, int index, bool next)
        {
            int unlockLevel = ShopRules.UnlockLevelForSlot(index);
            var cell = Ui.VStack(parent, $"Locked{index}", CellGap);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Ui.Sized(cell, width: CardSize.x, flexWidth: 0);

            var face = EmptyWell(cell.transform, "Well", next ? Theme.Gold : Theme.PanelBorder);
            var stack = Ui.VStack(face, "Stack", 2);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.slot.locked_title", ("level", unlockLevel)),
                34, next ? Theme.GoldDeep : Theme.LockGray, Theme.TitleFont);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.slot.locked_sub"), 19,
                next ? Theme.GoldDeep : Theme.LockGray);

            if (next)
            {
                int gap = unlockLevel - MetaRules.CharacterLevel(_meta.CharacterXp);
                Ui.ThemedLabel(cell.transform, Strings.T("shop.slot.locked_gap", ("levels", gap)), 19, Theme.GoldDeep);
            }
        }

        /// <summary>字牌大小的空描边卡(锁位 / 广告位今日无货共用),返回内层填充面。</summary>
        private Transform EmptyWell(Transform parent, string name, Color border)
        {
            var outer = Ui.OutlinedPanel(parent, name, Theme.PanelInset, border, 14, 2, out var face);
            var element = outer.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = CardSize.x;
            element.preferredHeight = CardSize.y;
            return face.transform;
        }

        /// <summary>折扣价签文案「5折」(2026-10-01 每日随机打折;稿 商城折扣与捆数标记 · 方案 A)。</summary>
        private static string DiscountTag(int discountPercent) =>
            Strings.T("shop.discount_tag", ("zhe", discountPercent / 10));

        /// <summary>价格钮三态(稿):买得起 = 墨色底白字;墨锭不足 = 凹槽底 + 朱砂字的价格;
        /// 已售 = 锁灰底 + 「今日已购」。
        /// 墨锭不足时不再写「差 N 墨」(2026-10-02 用户拍板):照常显示价格、标红即可,与宝箱位同款。
        /// <paramref name="original"/> 高于 price 时,未售的两态都在折后价后面跟一个划线原价。</summary>
        private void BuyButton(Transform parent, float width, bool sold, int price, string label, Action onClick,
            int original = 0)
        {
            bool poor = !sold && _meta.Ink < price;
            var button = Ui.RoundButton(parent, label,
                onClick,
                sold ? Theme.LockedBg : poor ? Theme.PanelInset : Theme.Ink,
                sold ? Theme.LockGray : poor ? Theme.CinnabarDark : Color.white,
                19, new Vector2(width, BuyH), 14);
            button.interactable = !sold && !poor;
            if (!sold && original > price) StrikePrice(button, original, poor ? Theme.CinnabarDark : Color.white);
        }

        /// <summary>把钮上那行「折后价」改成「折后价 + 划线原价」并排居中。
        /// uGUI 的 Text 富文本没有删除线,线是原价标签里一条横贯的细 Image —— 标签由布局组
        /// 按自身 preferredWidth 定宽,线跟着标签宽走,不用估字宽。</summary>
        private static void StrikePrice(Button button, int original, Color fg)
        {
            var price = button.GetComponentInChildren<Text>();
            int font = price.fontSize;
            var row = Ui.Row(button.transform, "Price", 8);
            Ui.Stretch((RectTransform)row.transform);
            price.transform.SetParent(row.transform, false);
            var dim = new Color(fg.r, fg.g, fg.b, 0.6f);
            var orig = Ui.ThemedLabel(row.transform, Ui.InkText(original), Mathf.RoundToInt(font * 0.7f), dim);
            var line = Ui.Panel(orig.transform, "Strike");
            var image = line.AddComponent<Image>();
            image.color = dim;
            image.raycastTarget = false;
            Ui.Anchor((RectTransform)line.transform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-1, -1), new Vector2(1, 1));
        }

        /// <summary>已售印:朱砂斜标改成正放的一枚方印 —— uGUI 旋转会连带旋转裁剪矩形
        /// (与 CardBadges 那枚角旗同一个坑,见该处说明)。</summary>
        private static void SoldSeal(GameObject tile, string text)
        {
            var seal = Ui.Chip(tile.transform, text, Theme.Cinnabar, Color.white, 21);
            var element = seal.GetComponent<LayoutElement>();
            Ui.Anchor((RectTransform)seal.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-element.preferredWidth / 2f, -element.preferredHeight / 2f),
                new Vector2(element.preferredWidth / 2f, element.preferredHeight / 2f));
        }

        /// <summary>月订阅条(第 14 章 14.3)。**只占版面,点了说去向** —— 订阅的四条权益
        /// (广告位免看直领 / 开箱计时位 1→2 / 每日墨锭礼包 / 开箱时长 −25%)在 Core 里一条都还没有,
        /// 画成可买的按钮就是「屏上写着玩家点不到的功能」(README 的品牌硬规矩)。
        /// 与顶栏设置钮同一种处理:占位 + 说明弹窗,接上时只换回调、版面不动。
        ///
        /// ⚠️ 2026-09-22:Platform 层已有 <see cref="Brushblade.Platform.IBillingService"/>,
        /// 但**这里刻意不接** —— 计费通了也不能卖,因为四条权益还不存在,
        /// 卖出去就是收了钱不给货。解锁条件是**先把权益做进 Core**(箱位上限与开箱时长
        /// 要能按订阅态变,广告位要能免看直领),再把这个回调换成
        /// `Monetization.Billing.Purchase(...)`。顺序反了就是事故。</summary>
        private void BuildSubscriptionBar(Transform parent)
        {
            var bar = Ui.CardPanel(parent, "Subscription", Theme.GoldSoft, 15);
            Ui.Anchor((RectTransform)bar.transform, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, SubBarH));
            var button = bar.gameObject.AddComponent<Button>();
            button.targetGraphic = bar;
            button.onClick.AddListener(() => ShowAlert(Strings.T("shop.subscription.soon_title"),
                Strings.T("shop.subscription.soon_body")));

            var stack = Ui.VStack(bar.transform, "Stack", 6);
            stack.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            stack.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(21, 21, 10, 10);
            Ui.Stretch((RectTransform)stack.transform);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.title"), 23,
                Theme.GoldDeep, Theme.TitleFont, TextAnchor.MiddleLeft);
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.perks"), 19,
                Theme.TextDim, null, TextAnchor.MiddleLeft);
            // 「不卖数值、不卖卡、不卖抽数」——第 14 章的红线,写在条上,不藏在条款里
            Ui.ThemedLabel(stack.transform, Strings.T("shop.subscription.promise"), 19,
                Theme.TextDim, null, TextAnchor.MiddleLeft);
        }

        // ---- 右栏:今日箱位 + 免费补给 ----

        private void BuildSide(Transform parent)
        {
            var side = Ui.OutlinedPanel(parent, "Side", Theme.PanelPaper, Theme.PanelBorder, 21, 2);
            Ui.Anchor((RectTransform)side.transform, new Vector2(1, 0), Vector2.one,
                new Vector2(-SideW, 0), Vector2.zero);

            var head = Ui.Row(side.transform, "Head", 12);
            var headLayout = head.GetComponent<HorizontalLayoutGroup>();
            headLayout.childAlignment = TextAnchor.MiddleLeft;
            headLayout.padding = new RectOffset(17, 17, 0, 0);
            Ui.Anchor((RectTransform)head.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH), Vector2.zero);
            Ui.ThemedLabel(head.transform, Strings.T("shop.side.title"), 19, Theme.LockGray);

            var separator = Ui.Panel(side.transform, "HeadRule");
            separator.AddComponent<Image>().color = Theme.PanelBorder;
            Ui.Anchor((RectTransform)separator.transform, new Vector2(0, 1), Vector2.one,
                new Vector2(0, -SideHeadH - 2), new Vector2(0, -SideHeadH));

            var body = Ui.VStack(side.transform, "Body", 15);
            body.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Ui.Anchor((RectTransform)body.transform, Vector2.zero, Vector2.one,
                new Vector2(SidePad, SidePad), new Vector2(-SidePad, -SideHeadH - 2));

            BuildChestOffer(body.transform);
            BuildSupplies(body.transform);
        }

        private void BuildChestOffer(Transform parent)
        {
            var tier = _meta.Shop.ChestSlot;
            int tierIndex = (int)tier - 1;
            int price = ShopRules.ChestPrice(_meta);   // 当日折后价(2026-10-01)
            int discount = ShopRules.DiscountPercent(ShopRules.IsPremium(tier), Math.Max(0, _meta.Shop.ChestDiscountRoll));
            int original = ShopRules.ChestBasePrice[tierIndex];
            string chestName = ChestRules.TierName(tier);
            bool sold = _meta.Shop.ChestSold;
            bool slotsFull = _meta.Chests.Count >= ChestRules.SlotLimit;

            var card = Ui.OutlinedPanel(parent, "ChestOffer", Theme.CardWhite, Theme.PanelBorder, 17, 2);
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 258f;

            var stack = Ui.VStack(card.transform, "Stack", 8);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(13, 13, 13, 13);
            Ui.Stretch((RectTransform)stack.transform);

            ChestArt.Draw(stack.transform, tier, ChestView.State.Idle, 84f);
            Ui.ThemedLabel(stack.transform, chestName, 23, Theme.TextMain, Theme.TitleFont);

            // 两枚事实 chip:种数与张数 · 开启时长。都是「买之前该知道的事实」,不是促销话术。
            // 「箱位 N/4」那枚 2026-09-30 按用户要求撤掉 —— 满位时下面的按钮本来就写着「箱位已满」
            var facts = Ui.Row(stack.transform, "Facts", 7);
            facts.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Chip(facts.transform, Strings.T("shop.chest.cards_chip", ("count", ChestRules.KindCount[tierIndex]), ("cards", ChestRules.ExpectedCards(tier))),
                Theme.PanelInset, Theme.TextDim, 18);
            // 不满 1 小时按分钟写(2026-09-30 用户报:素纸匣 5 分钟曾显示成「0.0833333 小时」)
            long seconds = ChestRules.DurationSeconds[tierIndex];
            Ui.Chip(facts.transform, seconds < 3600
                    ? Strings.T("shop.chest.duration_chip_min", ("minutes", seconds / 60))
                    : Strings.T("shop.chest.duration_chip", ("hours", seconds / 3600f)),
                Theme.PanelInset, Theme.TextDim, 18);

            // 箱位满时不可买:按钮直接写清楚为什么,别让玩家点了才弹窗
            string label = sold ? Strings.T("shop.slot.sold_today")
                : slotsFull ? Strings.T("shop.chest.slots_full_label")
                : Ui.InkText(price);
            var buy = Ui.RoundButton(stack.transform, label,
                () => Do(() => ShopRules.TryBuyChest(_meta, _chestPool, _time),
                    Strings.T("shop.chest.buy_success", ("chestName", chestName)),
                    Strings.T("shop.chest.buy_fail_title"),
                    slotsFull
                        ? Strings.T("shop.chest.slot_full_body", ("count", ChestRules.SlotLimit), ("limit", ChestRules.SlotLimit))
                        : Strings.T("shop.chest.buy_fail_body", ("chestName", chestName), ("price", Ui.InkText(price)), ("ink", Ui.InkText(_meta.Ink)))),
                sold || slotsFull ? Theme.LockedBg : _meta.Ink < price ? Theme.PanelInset : Theme.Ink,
                sold || slotsFull ? Theme.LockGray : _meta.Ink < price ? Theme.CinnabarDark : Color.white,
                19, new Vector2(0, BuyH), 14);
            buy.GetComponent<LayoutElement>().flexibleWidth = 1;
            buy.interactable = !sold && !slotsFull && _meta.Ink >= price;
            if (!sold && !slotsFull && original > price)
                StrikePrice(buy, original, _meta.Ink < price ? Theme.CinnabarDark : Color.white);
            // 旗子最后建:兄弟序靠后才画在箱卡内容之上
            if (!sold) ChestDiscountFlag(card.transform, DiscountTag(discount));
        }

        /// <summary>宝箱位的折扣:箱卡右上角一面朱砂垂旗(稿 方案 A),顶边贴齐卡沿往下垂。
        /// 卡里是竖排布局组,旗子 ignoreLayout,只靠锚点定位。</summary>
        private static void ChestDiscountFlag(Transform card, string text)
        {
            const int font = 21;
            const float h = 38f, inset = 21f;
            float w = Ui.ChipWidth(text, font);
            var flag = Ui.Panel(card, "DiscountFlag");
            flag.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = flag.AddComponent<Image>();
            image.sprite = Theme.Rounded(6);
            image.type = Image.Type.Sliced;
            image.color = Theme.Cinnabar;
            image.raycastTarget = false;
            var label = Ui.ThemedLabel(flag.transform, text, font, Color.white, Theme.TitleFont);
            Ui.Stretch(label.rectTransform);
            Ui.Anchor((RectTransform)flag.transform, Vector2.one, Vector2.one,
                new Vector2(-inset - w, -h), new Vector2(-inset, 0));
        }

        /// <summary>免费补给:两条奖励式广告(领墨锭 / 刷新字摊)。用过的转灰写「已用完」——
        /// 第 14 章的口径是奖励式广告,没有强制广告,所以这一栏只可能是「多给」,不会是「解锁」。</summary>
        private void BuildSupplies(Transform parent)
        {
            Ui.ThemedLabel(parent, Strings.T("shop.supply.title"), 19, Theme.LockGray, null, TextAnchor.MiddleLeft);

            var inkAd = Ui.AdBadge(parent,
                _meta.Shop.InkAdClaimed
                    ? Strings.T("shop.supply.used_label")
                    : Strings.T("shop.ink_ad.claim_label", ("amount", Ui.InkText(_meta.Shop.InkAdAmount))),
                () => AdGate.Watch(AdPlacement.ShopInk,
                    () => Do(() => ShopRules.TryClaimInkAd(_meta), Strings.T("shop.ink_ad.claim_success"),
                        Strings.T("shop.ink_ad.already_claimed_title"), Strings.T("shop.ink_ad.already_claimed_body"))),
                new Vector2(0, 64));
            inkAd.GetComponent<LayoutElement>().flexibleWidth = 1;
            inkAd.interactable = !_meta.Shop.InkAdClaimed;

            var refresh = Ui.AdBadge(parent,
                _meta.Shop.AdRefreshUsed
                    ? Strings.T("shop.supply.used_label")
                    : Strings.T("shop.refresh.action_label"),
                () => AdGate.Watch(AdPlacement.ShopRefresh,
                    () => Do(() => ShopRules.TryAdRefresh(_meta, _cardPool, new GameRandom(Environment.TickCount),
                            id => _graph.Get(id).Rarity, _chestPool),
                        Strings.T("shop.refresh.success"),
                        Strings.T("shop.refresh.done_label"), Strings.T("shop.refresh.already_done_body"))),
                new Vector2(0, 64));
            refresh.GetComponent<LayoutElement>().flexibleWidth = 1;
            refresh.interactable = !_meta.Shop.AdRefreshUsed;

        }

        // ---- 反馈 ----

        /// <summary>成交反馈:顶部居中的墨色 toast,停 <see cref="ToastHold"/> 秒后淡出销毁(ToastRoutine)。
        /// 取代旧版那条常驻消息行 —— 那行占掉整整一行版面,只为显示一句「购入成功」。</summary>
        private static GameObject ShowToast(Transform parent, string text)
        {
            var toast = Ui.Chip(parent, text, Theme.Ink, Color.white, 21, padX: 29, padY: 17);
            var element = toast.GetComponent<LayoutElement>();
            Ui.Anchor((RectTransform)toast.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-element.preferredWidth / 2f, -(TopH + element.preferredHeight)),
                new Vector2(element.preferredWidth / 2f, -TopH));
            return toast;
        }

        // 停留 / 淡出时长取战斗胜利横幅那一组(BattleView.VictoryBannerRoutine:Boss 1.8 + 淡出 0.3)——
        // toast 是一整句带字名的话,取两档里长的那档
        private const float ToastHold = 1.8f, ToastFade = 0.3f;

        /// <summary>toast 停留后淡出并销毁。下一笔交易的 Rebuild 会先 Ui.Clear 掉它 ——
        /// 每帧先判假 null 再碰 CanvasGroup,否则就是 BattleView 那条注释说的 MissingReferenceException。</summary>
        private System.Collections.IEnumerator ToastRoutine(GameObject toast)
        {
            var group = toast.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; // 只是提示,不拦点击
            for (float t = 0; t < ToastHold; t += Time.unscaledDeltaTime)
            {
                if (toast == null) yield break;
                yield return null;
            }
            for (float t = 0; t < ToastFade; t += Time.unscaledDeltaTime)
            {
                if (toast == null) yield break;
                group.alpha = 1f - t / ToastFade;
                yield return null;
            }
            if (toast != null) Destroy(toast);
        }

        /// <summary>执行一笔交易:成功出 toast,失败弹窗给具体原因(2026-07-19 提示统一弹窗)。</summary>
        private void Do(Func<bool> action, string successMessage, string failTitle = null, string failBody = null)
        {
            if (action())
            {
                _toast = successMessage;
                _save();
                Rebuild();
                return;
            }
            Rebuild();
            ShowAlert(failTitle ?? Strings.T("shop.generic_fail_title"), failBody ?? Strings.T("shop.generic_fail_body"));
        }

        /// <summary>点货架字卡:看详情(商城卡未拥有,按 1 级基础值展示)。</summary>
        private void ShowPreview(CharDef def)
        {
            if (_modal != null) Destroy(_modal);
            // 传 meta:详情里那段「等级 + 升级成本」按养成外层的账画(与卡组页同一份)
            _modal = CharPreview.Show(transform, def, _graph, MetaRules.CardLevel(_meta, def.Id),
                meta: _meta);
        }

        /// <summary>被拒提示统一弹窗;须在 Rebuild 之后调用——Rebuild 会清空根节点。</summary>
        private void ShowAlert(string title, string body)
        {
            if (_modal != null) Destroy(_modal);
            _modal = Ui.Alert(transform, title, body);
        }
    }
}
