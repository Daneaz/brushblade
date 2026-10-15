using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>格挡反击动效(drafts/counterfx,2026-10-15 拍板)。三拍,以敌人扑击命中为 0(1× 值,一律走 Beat / Dt,按住加速 ÷ 倍率):
    ///
    /// ① 挡(0–0.30):格挡 chip 的 block 图标放大成铜绿盾印压在受击者中心 + 朝敌半弧;受击者后坐 0.45、不白闪、
    ///    伤害数 0.78 倍挪到头顶偏右、顿帧 0.018 + 金属声(受击那一半在 EnemyAttack / SummonHit 分支里)。
    /// ② 扣(0.06 起):受击者自己那枚格挡 chip 闪(铜绿外晕)+ 次数跳格 0.16;扣到 0 → 跳完 chip 缩没 0.18。
    ///    反击加倍:0.12 再闪反击加倍 chip(赭金外晕)。
    /// ③ 反(0.16 起):金字形色一划从受击者射向攻击者 0.12 easeInQuad;0.28 命中:攻击者后坐 0.6 + 外扩环 + 顿帧 0.03 +「反击 N」。
    ///    加倍换赭金粗线、粗环、gold-deep 数字 +「×2」角签,顿帧 0.075 + 小震屏。多段每段 +0.08,≥3 段合并「反击 ×n · N」。
    ///
    /// 驱动事件是 Core 的 <see cref="BattleEventKind.BlockUsed"/>(紧跟受击、在反击伤害之前);反击被预算削到 0 / 攻击者已死时
    /// 后面没有 BlockCounter 伤害,只演 ①②。⚠ 这里的「反击」与 Juice 的 Countered(五行被克)无关,不复用它的褪色 / 内收环。</summary>
    public sealed partial class Juice
    {
        /// <summary>玩家立绘块(底栏「我」):盾印压在这里、被挡的伤害数落在它头顶偏右。BattleView 每次重绘后更新。</summary>
        public Func<RectTransform> PlayerPortraitAt { get; set; }

        /// <summary>玩家身上反击加倍(CounterBoost)的百分比,多来源取最强;0 = 没挂。木灵的格挡同样吃它。</summary>
        public Func<int> CounterBoostPct { get; set; }

        // ---- 时序(稿 CounterSpec §2,1× 值)----
        private const float SealDuration = 0.30f;
        private const float ArcDuration = 0.18f;
        private const float ChipCueDelay = 0.06f;     // ② 起点
        private const float RiposteDelay = 0.06f;     // 加倍 chip 在 ② 之后再 0.06(= 0.12)
        private const float ChipRollDuration = 0.16f;
        private const float ChipGoneDuration = 0.18f;
        private const float CounterLaunch = 0.16f;    // ③ 反击线发射
        private const float CounterFlight = 0.12f;    // 飞行;末端即命中(0.28)
        private const float CounterFade = 0.12f;
        private const float CounterMulti = 0.08f;     // 多段反击每段间隔
        private const float CounterTail = 0.10f;      // 下一步起点 = max(StepGap, 末次反击命中 + 0.10)
        private const float CounterRingDuration = 0.18f;
        private const float CounterPopLife = 1.0f;
        private const int CounterMergeMin = 3;

        // ---- 尺寸(稿 pt × 2.093)----
        private const float SealSize = 46f;           // 22pt
        private const float SealRim = 3f;             // 1.5pt 纸色描边
        private const float SealIcon = 29f;           // 14pt
        private const float SealRise = 8.4f;          // 4pt
        private const float ArcSize = 71f;            // 34pt
        private const float StreakThick = 6.3f;       // 3pt
        private const float StreakThickBoost = 10.5f; // 5pt
        private const float CounterRingSize = 63f;    // 30pt
        private const float CounterAnchorUp = 29f;    // 锚立绘中心上 14pt
        private const float CounterStackStep = 25f;   // 纵向错开 12pt
        private const float CounterRise = 38f;        // 升 18pt
        private const float CounterTopMargin = 31.4f; // 升完的中心离画顶 ≥ 15pt
        private const int CounterKeySize = 19;        // 「反击」9pt
        private const int CounterNumSize = 25;        // 数 12pt
        private const int CounterNumSizeBoost = 29;   // 加倍 14pt
        private const float BadgeHeight = 25f;        // 角签 12pt
        private const int BadgeFontSize = 18;         // 8.5pt
        private const float BadgeIcon = 17f;          // 8pt
        private const float BlockedPopupDx = 29f;     // 被挡伤害数:头顶偏右 14pt
        private const float BlockedPopupDy = 12.6f;   // 头顶上 6pt
        private const float BlockedDamageScale = 0.78f;
        private const float ChipRollDrop = 10.5f;     // 新数字自下 5pt 滑入

        // ---- 强度 ----
        private const float BlockRecoil = 0.45f;      // 受击者后坐
        private const float CounterRecoil = 0.6f;     // 攻击者后坐
        private const float BlockClipPitch = 1.25f;   // 金属声
        private const float BlockShake = 0.45f;       // 震屏 ×0.45(只玩家;木灵不震屏)

        private static readonly Color BlockGlow = new(Theme.Jade.r, Theme.Jade.g, Theme.Jade.b, 0.7f);
        private static readonly Color RiposteGlow = new(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.8f);
        private static Color MetalGlyph => Theme.GlyphColor(Element.Metal);

        /// <summary>演出时钟:每帧累加 <see cref="Dt"/>(吃按住加速、招式顿帧期间停)。反击各拍按它对齐 ——
        /// 中间夹着镜 / 荆棘那几条照常演的事件时,命中时刻仍落在格挡后 0.28 s。</summary>
        private float _pacedClock;

        /// <summary>扣到 0 正在缩没的 chip → 当前缩放倍率。载体闪(<see cref="CarrierFlashRoutine"/>)按它乘,两条协程不抢 localScale。</summary>
        private readonly Dictionary<RectTransform, float> _chipShrink = new();

        /// <summary>一次格挡的 ③ 拍上下文:后面跟了几记反击(多段 / 贯穿都算),已经演到第几记。</summary>
        private sealed class CounterRun
        {
            public RectTransform Attacker;   // 合并飘字的锚点(被格挡的那只敌人)
            public int BoostPct;
            public int Total;
            public int Shown;
            public int Sum;
            public float Start;              // 格挡那一刻的 _pacedClock
            public readonly Dictionary<RectTransform, int> Stacks = new();

            public float HitDue(int k) => Start + CounterLaunch + CounterFlight + k * CounterMulti;
        }

        /// <summary>这一下受击(EnemyAttack / SummonHit)是不是被格挡了:紧随其后(跳过保命)的是同一攻击者的 BlockUsed。</summary>
        private static bool BlockedNext(IReadOnlyList<BattleEvent> events, int idx)
        {
            int attacker = events[idx].TargetIndex;
            for (int j = idx + 1; j < events.Count; j++)
            {
                if (events[j].Kind == BattleEventKind.EndureSaved) continue;
                return events[j].Kind == BattleEventKind.BlockUsed && events[j].TargetIndex == attacker;
            }
            return false;
        }

        /// <summary>这次格挡之后的反击伤害(Source = BlockCounter)的落点,按事件顺序;到下一次受击 / 格挡 / 行动者为止。</summary>
        private static List<int> CounterTargetsAfter(IReadOnlyList<BattleEvent> events, int idx)
        {
            var targets = new List<int>();
            for (int j = idx + 1; j < events.Count; j++)
            {
                var kind = events[j].Kind;
                if (kind == BattleEventKind.EnemyAttack || kind == BattleEventKind.SummonHit
                    || kind == BattleEventKind.BlockUsed || kind == BattleEventKind.ActorActed) break;
                if (kind == BattleEventKind.Damage && events[j].Source == EffectSource.BlockCounter)
                    targets.Add(events[j].TargetIndex);
            }
            return targets;
        }

        /// <summary>BlockUsed:① 盾印 + 半弧,② chip 闪 / 跳格 / 缩没(+ 加倍 chip),③ 反击线按时刻发射。返回 ③ 的上下文;没有反击为 null。</summary>
        private CounterRun PlayBlockUsed(IReadOnlyList<BattleEvent> events, int idx, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor)
        {
            var e = events[idx];
            bool onSummon = e.SecondIndex >= 0;
            var holder = onSummon ? summonAnchor?.Invoke(e.SecondIndex) : PlayerPortraitAt?.Invoke();
            var attacker = enemyAnchor(e.TargetIndex);
            int boost = CounterBoostPct?.Invoke() ?? 0;

            BlockSeal(holder, attacker);
            var cell = onSummon ? SummonCellAt?.Invoke(e.SecondIndex) : PlayerCell;
            StartCoroutine(BlockChipCue(cell, e.Amount, boost > 0));

            var targets = CounterTargetsAfter(events, idx);
            if (targets.Count == 0) return null;   // 反击被削到 0 / 攻击者已死:只演 ①②
            var anchors = new List<RectTransform>(targets.Count);
            foreach (int t in targets) anchors.Add(enemyAnchor(t));
            StartCoroutine(CounterStreaks(holder, anchors, boost > 0));
            return new CounterRun { Attacker = attacker, BoostPct = boost, Total = targets.Count, Start = _pacedClock };
        }

        /// <summary>③ 命中:攻击者后坐 + 外扩环 + 顿帧 +「反击 N」(≥3 记合并到最后一记,锚被格挡的攻击者)。</summary>
        private void PlayCounterHit(BattleEvent e, RectTransform target, CounterRun run, bool kills)
        {
            int k = run.Shown++;
            run.Sum += e.Amount;
            bool boost = run.BoostPct > 0;
            CounterRing(target, boost);
            if (!kills) HitReact(target, CounterRecoil, whiten: false, paced: true);   // 致死让位给置灰
            HitStop((boost ? HitStopHeavy : HitStopLight) / _rate);
            PlayClip(_hitClip, 0.6f, BlockClipPitch);
            if (boost && k == 0) StartCoroutine(Shake(6f, AttackDir(target)));
            if (run.Total < CounterMergeMin)
            {
                int stack = target != null && run.Stacks.TryGetValue(target, out var s) ? s : 0;
                if (target != null) run.Stacks[target] = stack + 1;
                CounterNumber(target, Strings.T("juice.popup.source.block_counter",
                    ("amount", CounterAmount(e.Amount, boost))), run.BoostPct, stack);
            }
            else if (k == run.Total - 1)
                CounterNumber(run.Attacker, Strings.T("juice.popup.source.block_counter_merged",
                    ("n", run.Total), ("amount", CounterAmount(run.Sum, boost))), run.BoostPct, 0);
        }

        private static string CounterAmount(int amount, bool boost) =>
            $"<size={(boost ? CounterNumSizeBoost : CounterNumSize)}>{amount}</size>";

        /// <summary>① 受击的另一半:顿帧 0.018 + 金属声 + 后坐 0.45(不白闪);玩家侧再震屏 ×0.45。</summary>
        private void BlockedHitFx(RectTransform holder, RectTransform attacker, bool shake)
        {
            HitStop(HitStopCountered / _rate);
            PlayClip(_hitClip, 0.7f, BlockClipPitch);
            HitReact(holder, BlockRecoil, whiten: false, paced: true);
            if (shake) StartCoroutine(Shake(10f * BlockShake, -AttackDir(attacker)));
        }

        /// <summary>被格挡的伤害数:0.78 倍,落在受击者头顶偏右(让开盾印)。拿不到立绘回落原来的落点。</summary>
        private void BlockedPopup(string text, Color color, RectTransform holder, bool small = false, float scale = 1f,
            Color? outline = null)
        {
            Vector2? nudge = holder != null
                ? new Vector2(BlockedPopupDx, holder.rect.height * (1f - holder.pivot.y) + BlockedPopupDy)
                : null;
            Popup(text, color, holder, small, scale * BlockedDamageScale, outline, nudge);
        }

        // ---- ① 盾印 + 半弧 ----

        private void BlockSeal(RectTransform holder, RectTransform attacker)
        {
            if (holder == null || _shakeTarget == null) return;
            var go = new GameObject("BlockSeal", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(SealSize + SealRim * 2f, SealSize + SealRim * 2f);
            rect.position = holder.position;
            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var rim = Disc(rect, "Rim", new Color(Theme.Paper.r, Theme.Paper.g, Theme.Paper.b, 0.95f), SealSize + SealRim * 2f);
            rim.raycastTarget = false;
            Disc(rect, "Face", Theme.Jade, SealSize);
            var sprite = Icons.Get("block");
            Graphic icon;
            if (sprite != null)
            {
                var image = Ui.Panel(rect, "Icon").AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = Color.white;
                icon = image;
            }
            else icon = Ui.ThemedLabel(rect, Icons.Fallback("block"), Mathf.RoundToInt(SealIcon * 0.8f), Color.white);
            icon.raycastTarget = false;
            icon.rectTransform.sizeDelta = new Vector2(SealIcon, SealIcon);
            StartCoroutine(SealRoutine(rect, group));

            if (attacker == null) return;
            // 朝敌半弧:开口背向攻击者。Radial360 从顶起顺时针填一半 = 右半弧(中心朝 0°),再转到攻击者方向
            var arcGo = new GameObject("BlockArc", typeof(RectTransform), typeof(Image));
            arcGo.transform.SetParent(_shakeTarget, false);
            var arc = (RectTransform)arcGo.transform;
            arc.sizeDelta = new Vector2(ArcSize, ArcSize);
            arc.position = holder.position;
            Vector2 dir = attacker.position - holder.position;
            arc.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            var arcImage = arcGo.GetComponent<Image>();
            arcImage.sprite = CircleSprite(ArcSize, 2f * 2.093f);
            arcImage.type = Image.Type.Filled;
            arcImage.fillMethod = Image.FillMethod.Radial360;
            arcImage.fillOrigin = (int)Image.Origin360.Top;
            arcImage.fillClockwise = true;
            arcImage.fillAmount = 0.5f;
            arcImage.color = Theme.Jade;
            arcImage.raycastTarget = false;
            StartCoroutine(ArcRoutine(arc, arcImage));
        }

        /// <summary>稿 @keyframes seal(0.30):0% .6 透明 → 23% 1.15 不透明 → 40% 1 → 60% 仍不透明 → 100% 淡出、1.05、上浮 4pt。</summary>
        private IEnumerator SealRoutine(RectTransform rect, CanvasGroup group)
        {
            Vector2 home = rect.anchoredPosition;
            float t = 0f;
            while (t < SealDuration && rect != null)
            {
                t += Dt;
                float k = Mathf.Clamp01(t / SealDuration);
                float s, a, y = 0f;
                if (k < 0.23f) { float u = k / 0.23f; s = Mathf.Lerp(0.6f, 1.15f, EaseOutBack(u)); a = u; }
                else if (k < 0.4f) { s = Mathf.Lerp(1.15f, 1f, (k - 0.23f) / 0.17f); a = 1f; }
                else if (k < 0.6f) { s = 1f; a = 1f; }
                else { float u = (k - 0.6f) / 0.4f; s = Mathf.Lerp(1f, 1.05f, u); a = 1f - u * u; y = SealRise * u; }
                rect.localScale = new Vector3(s, s, 1f);
                rect.anchoredPosition = home + new Vector2(0f, y);
                group.alpha = a;
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        private IEnumerator ArcRoutine(RectTransform rect, Image image)
        {
            var color = image.color;
            float t = 0f;
            while (t < ArcDuration && rect != null)
            {
                t += Dt;
                float k = Mathf.Clamp01(t / ArcDuration);
                float s = Mathf.Lerp(0.7f, 1.35f, EaseOutCubic(k));
                rect.localScale = new Vector3(s, s, 1f);
                image.color = new Color(color.r, color.g, color.b, 1f - k);
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        // ---- ② chip 闪 + 跳格 / 缩没 ----

        private IEnumerator BlockChipCue(Transform cell, int remaining, bool boost)
        {
            yield return Beat(ChipCueDelay);
            var chip = FlashIcon(cell, "block", BlockGlow);
            if (chip != null) StartCoroutine(ChipRoll(chip, remaining));
            if (!boost) yield break;
            yield return Beat(RiposteDelay);
            // CounterBoost 挂在玩家身上:木灵挡时玩家那枚(若在)也亮 —— 看得出加倍从哪来
            FlashIcon(cell, "riposte", RiposteGlow);
            if (cell != PlayerCell) FlashIcon(PlayerCell, "riposte", RiposteGlow);
        }

        /// <summary>次数跳格:新数字自下 5pt 滑入淡入(0.16 easeOutCubic);扣到 0 → 跳完 chip 缩没(0.18 easeIn)。
        /// 只动 chip 自己的数字与缩放;真正移除等这一段演完后的重绘。</summary>
        private IEnumerator ChipRoll(RectTransform chip, int remaining)
        {
            var label = chip.GetComponentInChildren<Text>();
            if (label != null)
            {
                var lr = label.rectTransform;
                Vector2 home = lr.anchoredPosition;
                var color = label.color;
                label.text = remaining.ToString();
                float t = 0f;
                while (t < ChipRollDuration && lr != null)
                {
                    t += Dt;
                    float e = EaseOutCubic(Mathf.Clamp01(t / ChipRollDuration));
                    lr.anchoredPosition = home + new Vector2(0f, -ChipRollDrop * (1f - e));
                    label.color = new Color(color.r, color.g, color.b, color.a * e);
                    yield return null;
                }
                if (lr != null) lr.anchoredPosition = home;
                if (label != null) label.color = color;
            }
            else yield return Beat(ChipRollDuration);
            if (remaining > 0) yield break;

            float g = 0f;
            while (g < ChipGoneDuration && chip != null)
            {
                g += Dt;
                float k = Mathf.Clamp01(g / ChipGoneDuration);
                float mul = 1f - k * k;
                _chipShrink[chip] = mul;
                if (!_flashing.Contains(chip)) chip.localScale = new Vector3(mul, mul, 1f);
                yield return null;
            }
            if (chip != null)
            {
                _chipShrink[chip] = 0f;
                if (!_flashing.Contains(chip)) chip.localScale = new Vector3(0f, 0f, 1f);
            }
        }

        // ---- ③ 反击线 / 命中环 / 反击数 ----

        private IEnumerator CounterStreaks(RectTransform from, List<RectTransform> targets, bool boost)
        {
            yield return Beat(CounterLaunch);
            for (int k = 0; k < targets.Count; k++)
            {
                if (k > 0) yield return Beat(CounterMulti);
                Streak(from, targets[k], boost);
            }
        }

        /// <summary>一划:尾虚头实(渐变),从受击者中心到攻击者中心;0.12 easeInQuad 伸出(末端即命中),再 0.12 淡。加倍:赭金 5pt + 晕。</summary>
        private void Streak(RectTransform from, RectTransform to, bool boost)
        {
            if (from == null || to == null || _shakeTarget == null) return;
            Vector2 a = _shakeTarget.InverseTransformPoint(from.position);
            Vector2 b = _shakeTarget.InverseTransformPoint(to.position);
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1f) return;
            var go = new GameObject("CounterStreak", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            float thick = boost ? StreakThickBoost : StreakThick;
            rect.sizeDelta = new Vector2(d.magnitude, thick);
            rect.localPosition = a;
            rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var color = boost ? Theme.Gold : MetalGlyph;
            if (boost)   // 5pt 赭金晕:同一条渐变铺宽、压低透明度垫在下面
            {
                var glow = Ui.Panel(rect, "Glow").AddComponent<Image>();
                glow.sprite = StreakSprite();
                glow.color = new Color(color.r, color.g, color.b, 0.35f);
                glow.raycastTarget = false;
                Ui.Anchor(glow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -thick), new Vector2(0f, thick));
            }
            var line = Ui.Panel(rect, "Line").AddComponent<Image>();
            line.sprite = StreakSprite();
            line.color = color;
            line.raycastTarget = false;
            Ui.Stretch(line.rectTransform);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            StartCoroutine(StreakRoutine(rect, group));
        }

        private IEnumerator StreakRoutine(RectTransform rect, CanvasGroup group)
        {
            float t = 0f;
            rect.localScale = new Vector3(0f, 1f, 1f);
            while (t < CounterFlight + CounterFade && rect != null)
            {
                t += Dt;
                if (t < CounterFlight)
                {
                    float k = t / CounterFlight;
                    rect.localScale = new Vector3(k * k, 1f, 1f);   // easeInQuad:越飞越快
                }
                else
                {
                    rect.localScale = Vector3.one;
                    group.alpha = 1f - Mathf.Clamp01((t - CounterFlight) / CounterFade);
                }
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>命中外扩环:金字形色 2pt(加倍赭金 2.5pt),0.4 → 1.5,0.18 easeOutCubic 淡出。</summary>
        private void CounterRing(RectTransform at, bool boost)
        {
            if (at == null || _shakeTarget == null) return;
            var go = new GameObject("CounterRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(CounterRingSize, CounterRingSize);
            rect.position = at.position;
            var image = go.GetComponent<Image>();
            image.sprite = CircleSprite(CounterRingSize, (boost ? 2.5f : 2f) * 2.093f);
            image.color = boost ? Theme.Gold : MetalGlyph;
            image.raycastTarget = false;
            StartCoroutine(CounterRingRoutine(rect, image));
        }

        private IEnumerator CounterRingRoutine(RectTransform rect, Image image)
        {
            var color = image.color;
            float t = 0f;
            while (t < CounterRingDuration && rect != null)
            {
                t += Dt;
                float k = Mathf.Clamp01(t / CounterRingDuration);
                float s = Mathf.Lerp(0.4f, 1.5f, EaseOutCubic(k));
                rect.localScale = new Vector3(s, s, 1f);
                image.color = new Color(color.r, color.g, color.b, 1f - k);
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>「反击 N」:小字「反击」9pt + 数 12pt 粗(加倍 gold-deep 14pt +「×2」角签),金字形色、纸色描边;
        /// 锚立绘中心上 14pt,按 <paramref name="stack"/> 纵向错开 12pt,升 18pt、1.0 s(曲线同名签 lb)。
        /// 出画:中心夹在安全区内减半宽,升完的中心离画顶 ≥ 15pt。</summary>
        private void CounterNumber(RectTransform target, string text, int boostPct, int stack)
        {
            if (target == null || _shakeTarget == null) return;
            bool boost = boostPct > 0;
            var go = new GameObject("CounterPop", typeof(RectTransform), typeof(CanvasGroup), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            var row = go.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var label = Ui.ThemedLabel(rect, text, CounterKeySize, boost ? Theme.GoldDeep : MetalGlyph, Ui.Font);
            label.fontStyle = FontStyle.Bold;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            var edge = label.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(Theme.Paper.r, Theme.Paper.g, Theme.Paper.b, 0.9f);
            edge.effectDistance = new Vector2(1f, 1f);
            edge.useGraphicAlpha = true;

            if (boost) BoostBadge(rect, boostPct);

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            rect.position = target.position;
            // 夹取在父节点(震屏层)的本地坐标里做:localPosition 与 _shakeTarget.rect 同一参照(父 pivot),anchoredPosition 不是
            Vector2 p = (Vector2)rect.localPosition + new Vector2(0f, CounterAnchorUp + stack * CounterStackStep);
            var area = _shakeTarget.rect;
            float half = rect.rect.width / 2f;
            float side = SafeArea.MissingInset().side;
            p.x = Mathf.Clamp(p.x, area.xMin + side + half, Mathf.Max(area.xMin + side + half, area.xMax - side - half));
            p.y = Mathf.Min(p.y, area.yMax - CounterTopMargin - CounterRise);
            rect.localPosition = p;
            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            StartCoroutine(CounterPopRoutine(rect, group));
        }

        /// <summary>「×2」角签(加倍 Magnitude 100;别的量写「+N%」):赭金底 + riposte 图标 + gold-text 字。</summary>
        private static void BoostBadge(RectTransform parent, int pct)
        {
            var go = Ui.Panel(parent, "Boost");
            var bg = go.AddComponent<Image>();
            bg.sprite = Theme.Rounded(6);
            bg.type = Image.Type.Sliced;
            bg.color = Theme.Gold;
            bg.raycastTarget = false;
            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 0, 0);
            row.spacing = 2f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var el = go.AddComponent<LayoutElement>();
            el.minHeight = el.preferredHeight = BadgeHeight;

            var sprite = Icons.Get("riposte");
            if (sprite != null)
            {
                var icon = Ui.Panel(go.transform, "Icon").AddComponent<Image>();
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.color = Theme.GoldText;
                icon.raycastTarget = false;
                var iel = icon.gameObject.AddComponent<LayoutElement>();
                iel.preferredWidth = iel.preferredHeight = BadgeIcon;
            }
            string text = pct == 100
                ? Strings.T("juice.popup.source.block_counter_boost")
                : Strings.T("juice.popup.source.block_counter_boost_pct", ("pct", pct));
            var label = Ui.ThemedLabel(go.transform, text, BadgeFontSize, Theme.GoldText, Ui.Font);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
        }

        /// <summary>稿 @keyframes lb(1.0 s,--rise 18pt):0% 透明 · .7 · 低 0.2 高 → 14% 不透明 · 1.08 → 24% 1 → 78% 升满 → 100% 再升 6pt 淡出。</summary>
        private IEnumerator CounterPopRoutine(RectTransform rect, CanvasGroup group)
        {
            Vector2 home = rect.anchoredPosition;
            float h = rect.rect.height;
            float t = 0f;
            while (t < CounterPopLife && rect != null)
            {
                t += Dt;
                float k = Mathf.Clamp01(t / CounterPopLife);
                float y, s, a;
                if (k < 0.14f) { float u = k / 0.14f; y = Mathf.Lerp(-0.2f * h, 0f, u); s = Mathf.Lerp(0.7f, 1.08f, u); a = u; }
                else if (k < 0.78f)
                {
                    y = Mathf.Lerp(0f, CounterRise, (k - 0.14f) / 0.64f);
                    s = k < 0.24f ? Mathf.Lerp(1.08f, 1f, (k - 0.14f) / 0.1f) : 1f;
                    a = 1f;
                }
                else { float u = (k - 0.78f) / 0.22f; y = CounterRise + 12.6f * u; s = 1f; a = 1f - u; }
                rect.anchoredPosition = home + new Vector2(0f, y);
                rect.localScale = new Vector3(s, s, 1f);
                group.alpha = a;
                yield return null;
            }
            if (rect != null) Destroy(rect.gameObject);
        }

        // ---- 程序贴图 ----

        private static float EaseOutCubic(float k) => 1f - (1f - k) * (1f - k) * (1f - k);

        private static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = k - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }

        private static Image Disc(RectTransform parent, string name, Color color, float size)
        {
            var image = Ui.Panel(parent, name).AddComponent<Image>();
            image.sprite = CircleSprite(size, size / 2f);
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        private const int CircleTex = 128;
        private static readonly Dictionary<int, Sprite> _circles = new();

        /// <summary>正圆描环(thickness ≥ 半径 = 实心圆)。按「画出来的尺寸 / 环宽」折成贴图像素,同比例共用一张。
        /// 不用 Theme.RoundedRing:那是圆角方(四边各有一段直边),画盾印 / 半弧 / 命中环会发方。</summary>
        private static Sprite CircleSprite(float drawSize, float thickness)
        {
            int px = Mathf.Clamp(Mathf.RoundToInt(thickness / drawSize * CircleTex), 1, CircleTex / 2);
            if (_circles.TryGetValue(px, out var cached)) return cached;
            var tex = new Texture2D(CircleTex, CircleTex, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float c = CircleTex / 2f, outer = c - 1f, inner = outer - px;
            for (int y = 0; y < CircleTex; y++)
                for (int x = 0; x < CircleTex; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(outer - r + 0.5f) * (px >= CircleTex / 2 ? 1f : Mathf.Clamp01(r - inner + 0.5f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, CircleTex, CircleTex), new Vector2(0.5f, 0.5f), 100f);
            _circles[px] = sprite;
            return sprite;
        }

        private static Sprite _streak;

        /// <summary>反击线的渐变(稿 linear-gradient 0 → .55 @35% → 1):尾虚头实,用 Image.color 染色。</summary>
        private static Sprite StreakSprite()
        {
            if (_streak != null) return _streak;
            const int w = 64, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float a = u < 0.35f ? Mathf.Lerp(0f, 0.55f, u / 0.35f) : Mathf.Lerp(0.55f, 1f, (u - 0.35f) / 0.65f);
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _streak = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _streak;
        }
    }
}
