using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>打击感(13.3):有序时间线播放战斗事件——飞牌、伤害飘字(随伤害缩放)、受击白闪+缩放冲击、
    /// 命中顿帧、震屏、全屏微闪、击杀后坐、程序合成音效;播完回调供结算标语等待。
    /// 消费 BattleEngine.LastEvents,不反向驱动逻辑。</summary>
    public sealed class Juice : MonoBehaviour
    {
        private RectTransform _shakeTarget;
        private Vector2 _shakeHome; // 震屏基准位:并发震屏都以此复位,避免读实时位置累积漂移
        private AudioSource _audio;
        private AudioClip _hitClip;
        private AudioClip _thudClip;
        private AudioClip _shieldClip;
        private AudioClip _killClip;

        public void Init(RectTransform shakeTarget)
        {
            _shakeTarget = shakeTarget;
            _shakeHome = shakeTarget.anchoredPosition;
            _audio = gameObject.AddComponent<AudioSource>();
            _hitClip = Synth(0.07f, 190f, noise: 0.7f);   // 命中:脆
            _thudClip = Synth(0.12f, 90f, noise: 0.4f);   // 重击/受击:闷
            _shieldClip = Synth(0.1f, 320f, noise: 0.1f); // 护盾:润
            _killClip = SynthSweep(0.18f, 260f, 90f, noise: 0.25f); // 击杀:下行收束
        }

        private const float FlyDuration = 0.22f; // 飞牌全程(与 FlyRoutine 一致)
        private const float StrikeGap = 0.16f;   // 召唤反击一记与下一记之间的间隔
        private const float DeathBeat = 0.18f;   // 伤害飘字到「正!」之间的节拍(先痛后毙)
        private const float TailGap = 0.24f;     // 末次打击到「播完回调」的收尾停顿
        private const float HitStop = 0.05f;     // 命中顿帧:冲击瞬间极短定格再继续
        private const float EnemyHitGap = 0.14f; // 敌人逐个出手之间的间隔(错开震屏/音效,分开感受每记)

        /// <summary>播放一次动作的全部结算表现,全程有序、播完回调。enemyAnchor(i) 返回敌人本体圆
        /// (可为 null);summonAnchor(k) 返回第 k 记召唤反击的发起召唤物(可为 null);onComplete 在
        /// 所有动效落幕后调用(战斗结束标语等它,2026-07-24)。
        /// 召唤反击逐个顺序播、伤害与「正!」分节拍(2026-07-24):此前同帧齐发相互重叠、只见一次。</summary>
        public void Play(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor = null, Action onComplete = null)
        {
            StartCoroutine(PlayRoutine(events, enemyAnchor, summonAnchor, onComplete));
        }

        private IEnumerator PlayRoutine(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action onComplete)
        {
            // 读锚点世界坐标前先结算本帧布局:敌人格挂布局组,新建/重排后同帧读到的是未结算值,
            // DoT/召唤伤害会飘到屏幕中间而非怪物本体(2026-07-24)。
            Canvas.ForceUpdateCanvases();

            // 拆出召唤反击段:灼烧等在前(preRest)、召唤反击逐记(strikes)、敌人行动在后(postRest)。
            // 每记召唤 = SummonAttack + 紧随的伤害/击杀事件(BattleEngine 出招即紧接 DamageEnemy)。
            int i = 0;
            var preRest = new List<BattleEvent>();
            while (i < events.Count && events[i].Kind != BattleEventKind.SummonAttack)
                preRest.Add(events[i++]);
            var strikes = new List<(int target, List<BattleEvent> effects)>();
            while (i < events.Count && events[i].Kind == BattleEventKind.SummonAttack)
            {
                int target = events[i++].TargetIndex;
                var effects = new List<BattleEvent>();
                while (i < events.Count && (events[i].Kind == BattleEventKind.Damage
                    || events[i].Kind == BattleEventKind.EnemyDied))
                    effects.Add(events[i++]);
                strikes.Add((target, effects));
            }
            var postRest = new List<BattleEvent>();
            while (i < events.Count)
                postRest.Add(events[i++]);

            // 死亡结算收拢到伤害之后成排播(顺序节拍:①DoT ②召唤 ③死亡置灰+正 ④敌人反击 ⑤胜利标语)。
            var deaths = new List<int>();
            yield return ApplyBatch(preRest, enemyAnchor, summonAnchor, deaths);      // ① DoT 先结算
            for (int k = 0; k < strikes.Count; k++)                                    // ② 召唤物逐个行动+结算
            {
                var from = summonAnchor?.Invoke(k);
                var toRect = enemyAnchor(strikes[k].target);
                if (from != null && toRect != null)
                {
                    FlyGlyph("木", Theme.ElementColor(Element.Wood), from.position, toRect.position);
                    yield return new WaitForSecondsRealtime(FlyDuration); // 等飞牌砸到才结算
                }
                yield return ApplyBatch(strikes[k].effects, enemyAnchor, summonAnchor, deaths);
                yield return new WaitForSecondsRealtime(StrikeGap);
            }
            if (deaths.Count > 0)
                yield return new WaitForSecondsRealtime(DeathBeat); // 与伤害拉开一拍,兼让末次受击白闪先复原再置灰
            foreach (int target in deaths)                                             // ③④ 怪物死亡:置灰 + 正字,成排逐个
            {
                var t = enemyAnchor(target);
                Popup("正!", Theme.Ink, t);
                GreyOut(t);                            // 本体此刻才置灰(此前保持着色挨打)
                Knockback(t);                          // 一记后坐
                _audio.PlayOneShot(_killClip, 0.9f);   // 下行收束音
                ScreenFlash(0.16f, Color.white);       // 致命全屏微闪
                yield return new WaitForSecondsRealtime(DeathBeat);
            }
            yield return ApplyBatch(postRest, enemyAnchor, summonAnchor, deaths);       // 敌人反击(带打击动效)

            yield return new WaitForSecondsRealtime(TailGap);
            onComplete?.Invoke();                                                       // ⑤ 关卡胜利标语(外层)
        }

        /// <summary>结算一批事件的表现;怪物死亡不在此播,收集进 deaths 交死亡节拍统一结算。</summary>
        private IEnumerator ApplyBatch(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, List<int> deaths)
        {
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case BattleEventKind.SummonCapReached:
                        Popup("前排已满", Theme.InkSoft, null);
                        break;
                    case BattleEventKind.Damage:
                    case BattleEventKind.BurnTick:
                        // 伤害数字随伤害量放大(轻重分明);受击白闪 + 更狠缩放冲击
                        Popup($"-{e.Amount}", e.Kind == BattleEventKind.Damage
                            ? Theme.Cinnabar : Theme.ShopNav, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        HitReact(enemyAnchor(e.TargetIndex));
                        HitFx(e.Amount);
                        yield return new WaitForSecondsRealtime(HitStop); // 命中顿帧
                        break;
                    case BattleEventKind.EnemyDied:
                        deaths.Add(e.TargetIndex); // 攒到死亡节拍统一置灰+正,不与伤害同帧
                        break;
                    case BattleEventKind.SummonHit: // 敌人打召唤物:飘伤害 + 召唤物受击反应(TargetIndex=-1,顶前排=首个召唤)
                        var tank = summonAnchor?.Invoke(0);
                        Popup($"-{e.Amount}", Theme.Cinnabar, tank);
                        HitReact(tank);
                        _audio.PlayOneShot(_thudClip, 0.7f);
                        StartCoroutine(Shake(7f));
                        yield return new WaitForSecondsRealtime(EnemyHitGap);
                        break;
                    case BattleEventKind.EnemyAttack: // 敌人打我方:飘伤害 + 闷响 + 震屏 + 屏缘朱砂微闪
                        Popup($"-{e.Amount}", Theme.Cinnabar, null);
                        _audio.PlayOneShot(_thudClip, 0.8f);
                        StartCoroutine(Shake(10f));
                        ScreenFlash(0.14f, Theme.Cinnabar);
                        yield return new WaitForSecondsRealtime(EnemyHitGap); // 多敌人攻击错开,不同帧齐震齐响
                        break;
                    case BattleEventKind.Burn:
                        Popup($"灼+{e.Amount}", Theme.ShopNav, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    case BattleEventKind.Shield:
                        Popup($"盾+{e.Amount}", Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        break;
                    case BattleEventKind.EnemySplit:
                        Popup("分裂!", Theme.Jade, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.BossPhase:
                        Popup("破阶!", Theme.GoldBorder, enemyAnchor(e.TargetIndex));
                        _audio.PlayOneShot(_thudClip, 1f);
                        break;
                    case BattleEventKind.EnemyBuff:
                        Popup($"攻+{e.Amount}", Theme.InkSoft, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    case BattleEventKind.EnemyRevealed:
                        Popup("现形!", Theme.SplitBlue, enemyAnchor(e.TargetIndex));
                        break;
                }
            }
        }

        /// <summary>一记命中的音效 + 震屏(伤害越高音调越低、震屏越大,封顶);大伤害叠全屏微闪。</summary>
        private void HitFx(int amount)
        {
            _audio.pitch = Mathf.Clamp(1.3f - amount / 80f, 0.6f, 1.3f);
            _audio.PlayOneShot(amount >= 30 ? _thudClip : _hitClip, 0.9f);
            _audio.pitch = 1f;
            StartCoroutine(Shake(Mathf.Clamp(4f + amount * 0.35f, 4f, 26f)));
            if (amount >= 40) ScreenFlash(0.12f, Color.white); // 大伤害:一记全屏微闪
        }

        /// <summary>受击反应:更狠的缩放冲击 + 头像白闪一下(比 Punch 更强,专供敌人挨打)。</summary>
        private void HitReact(RectTransform target)
        {
            if (target != null) StartCoroutine(HitReactRoutine(target));
        }

        private IEnumerator HitReactRoutine(RectTransform target)
        {
            var image = target.GetComponent<Image>();
            Color original = image != null ? image.color : Color.white;
            float t = 0f;
            const float duration = 0.16f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / duration;
                float s = 1f + 0.28f * Mathf.Sin((1f - k) * Mathf.PI); // 更狠冲击
                target.localScale = new Vector3(s, s, 1f);
                if (image != null) image.color = Color.Lerp(Color.white, original, k); // 白闪 → 复原
                yield return null;
            }
            if (target != null) target.localScale = Vector3.one;
            if (image != null) image.color = original;
        }

        /// <summary>击杀一记后坐:头像被打退一下再归位(下一次重绘会把它画成已正)。</summary>
        private void Knockback(RectTransform target)
        {
            if (target != null) StartCoroutine(KnockbackRoutine(target));
        }

        private static IEnumerator KnockbackRoutine(RectTransform target)
        {
            Vector2 origin = target.anchoredPosition;
            float t = 0f;
            const float duration = 0.18f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float decay = 1f - t / duration;
                target.anchoredPosition = origin + new Vector2(0, 20f) * decay; // 向上弹退
                yield return null;
            }
            if (target != null) target.anchoredPosition = origin;
        }

        /// <summary>全屏微闪:大伤害/致命一记白光、我方受击朱砂屏缘,快速淡出。</summary>
        private void ScreenFlash(float alpha, Color color)
        {
            var go = new GameObject("Flash", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, alpha);
            image.raycastTarget = false;
            StartCoroutine(FlashRoutine(rect, image, alpha));
        }

        private static IEnumerator FlashRoutine(RectTransform rect, Image image, float alpha)
        {
            float t = 0f;
            const float duration = 0.12f;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                var c = image.color;
                c.a = alpha * (1f - t / duration);
                image.color = c;
                yield return null;
            }
            if (rect != null)
                UnityEngine.Object.Destroy(rect.gameObject);
        }

        /// <summary>怪物本体置灰:死亡节拍里把头像圆从当前色渐变到锁定灰(此前保持着色挨打)。</summary>
        private void GreyOut(RectTransform target)
        {
            if (target != null) StartCoroutine(GreyRoutine(target));
        }

        private static IEnumerator GreyRoutine(RectTransform target)
        {
            var image = target.GetComponent<Image>();
            if (image == null) yield break;
            Color from = image.color;
            Color to = Theme.LockedBg;
            float t = 0f;
            const float duration = 0.2f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                image.color = Color.Lerp(from, to, t / duration);
                yield return null;
            }
            if (target != null) image.color = to;
        }

        // ---- 过渡动效:飞牌 / 字牌弹跳(整屏重绘后补播,纯浮层不碰逻辑) ----

        /// <summary>幽灵字牌从 from 飞到 to(世界坐标),到达后销毁并回调。出字/合成/拆解的过渡表现。</summary>
        public void FlyGlyph(string glyph, Color color, Vector3 from, Vector3 to, Action onArrive = null)
        {
            var go = new GameObject("FlyGlyph", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(56, 56);
            rect.position = from;

            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;

            var label = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(go.transform, false);
            label.font = Theme.TitleFont;
            label.fontSize = 26;
            label.fontStyle = FontStyle.Bold;
            label.text = glyph;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            Ui.Stretch(label.rectTransform);

            StartCoroutine(FlyRoutine(rect, from, to, onArrive));
        }

        private static IEnumerator FlyRoutine(RectTransform rect, Vector3 from, Vector3 to, Action onArrive)
        {
            float t = 0f;
            const float duration = 0.22f;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = k * k; // ease-in:蓄力后加速砸向目标
                rect.position = Vector3.Lerp(from, to, eased);
                rect.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (rect != null)
                UnityEngine.Object.Destroy(rect.gameObject);
            onArrive?.Invoke();
        }

        /// <summary>字牌到位弹跳(合成结果/拆出部件落位)。目标可能已被重绘销毁,全程判空。</summary>
        public void PopTile(RectTransform target)
        {
            if (target != null)
                StartCoroutine(PunchRoutine(target));
        }

        // ---- 震屏 ----

        private IEnumerator Shake(float amplitude)
        {
            // 以固定 home 为基准(不读实时位置):多个 Shake 并发时不会把彼此的偏移当原点累积
            float t = 0f;
            const float duration = 0.22f;
            while (t < duration)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float decay = 1f - t / duration;
                _shakeTarget.anchoredPosition = _shakeHome + UnityEngine.Random.insideUnitCircle * (amplitude * decay);
                yield return null;
            }
            _shakeTarget.anchoredPosition = _shakeHome;
        }

        // ---- 字牌落位弹跳(PopTile 用;敌人受击改走更强的 HitReact) ----

        private IEnumerator PunchRoutine(RectTransform target)
        {
            float t = 0f;
            const float duration = 0.16f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float s = 1f + 0.18f * Mathf.Sin((1f - t / duration) * Mathf.PI);
                target.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (target != null)
                target.localScale = Vector3.one;
        }

        // ---- 伤害飘字 ----

        private void Popup(string text, Color color, RectTransform anchor, bool small = false, float sizeScale = 1f)
        {
            var go = new GameObject("Popup", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            if (anchor != null)
            {
                rect.position = anchor.position;
                rect.anchoredPosition += new Vector2(UnityEngine.Random.Range(-24f, 24f), 30f);
            }
            else // 玩家侧:屏幕中下
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.32f);
                rect.anchoredPosition = new Vector2(UnityEngine.Random.Range(-60f, 60f), 0);
            }

            var label = go.AddComponent<Text>();
            label.font = Ui.Font;
            label.fontSize = Mathf.RoundToInt((small ? 26 : 36) * sizeScale); // 伤害越高字越大
            label.fontStyle = FontStyle.Bold;
            label.text = text;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            StartCoroutine(FloatAndFade(rect, label));
        }

        private static IEnumerator FloatAndFade(RectTransform rect, Text label)
        {
            float t = 0f;
            const float duration = 0.7f;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                rect.anchoredPosition += new Vector2(0, 70f * UnityEngine.Time.unscaledDeltaTime);
                var c = label.color;
                c.a = 1f - t / duration;
                label.color = c;
                yield return null;
            }
            if (rect != null)
                UnityEngine.Object.Destroy(rect.gameObject);
        }

        // ---- 程序合成音效(无资产依赖):噪声打击 + 低频正弦体 ----

        private static AudioClip Synth(float duration, float baseFreq, float noise)
        {
            const int rate = 44100;
            int samples = (int)(rate * duration);
            var data = new float[samples];
            var random = new System.Random(12345);
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Exp(-t * 40f);
                float tone = Mathf.Sin(2f * Mathf.PI * baseFreq * t);
                float hiss = (float)(random.NextDouble() * 2 - 1);
                data[i] = (tone * (1f - noise) + hiss * noise) * envelope * 0.8f;
            }
            var clip = AudioClip.Create($"synth_{baseFreq}", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>频率下扫的合成音(击杀收束):相位累积以支持变频。</summary>
        private static AudioClip SynthSweep(float duration, float startFreq, float endFreq, float noise)
        {
            const int rate = 44100;
            int samples = (int)(rate * duration);
            var data = new float[samples];
            var random = new System.Random(12345);
            double phase = 0;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / rate;
                float freq = Mathf.Lerp(startFreq, endFreq, t / duration);
                phase += 2 * Math.PI * freq / rate;
                float envelope = Mathf.Exp(-t * 18f);
                float tone = (float)Math.Sin(phase);
                float hiss = (float)(random.NextDouble() * 2 - 1);
                data[i] = (tone * (1f - noise) + hiss * noise) * envelope * 0.8f;
            }
            var clip = AudioClip.Create("synth_sweep", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
