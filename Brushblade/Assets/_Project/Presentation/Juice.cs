using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>打击感最小集(13.3):震屏 + 受击弹跳 + 伤害飘字 + 程序合成音效。
    /// 消费 BattleEngine.LastEvents,不反向驱动逻辑。</summary>
    public sealed class Juice : MonoBehaviour
    {
        private RectTransform _shakeTarget;
        private AudioSource _audio;
        private AudioClip _hitClip;
        private AudioClip _thudClip;
        private AudioClip _shieldClip;

        public void Init(RectTransform shakeTarget)
        {
            _shakeTarget = shakeTarget;
            _audio = gameObject.AddComponent<AudioSource>();
            _hitClip = Synth(0.07f, 190f, noise: 0.7f);   // 命中:脆
            _thudClip = Synth(0.12f, 90f, noise: 0.4f);   // 重击/受击:闷
            _shieldClip = Synth(0.1f, 320f, noise: 0.1f); // 护盾:润
        }

        /// <summary>播放一次动作的全部结算表现。enemyAnchor(i) 返回敌人格的 RectTransform(可为 null)。</summary>
        public void Play(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor)
        {
            int maxHit = 0;
            bool playerHit = false;

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case BattleEventKind.Damage:
                    case BattleEventKind.BurnTick:
                        maxHit = Mathf.Max(maxHit, e.Amount);
                        Popup($"-{e.Amount}", e.Kind == BattleEventKind.Damage
                            ? Theme.Cinnabar : Theme.ShopNav, enemyAnchor(e.TargetIndex));
                        Punch(enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.Burn:
                        Popup($"灼+{e.Amount}", Theme.ShopNav, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    case BattleEventKind.Shield:
                        Popup($"盾+{e.Amount}", Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        break;
                    case BattleEventKind.EnemyDied:
                        Popup("正!", Theme.Ink, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.EnemyAttack:
                        playerHit = true;
                        Popup($"-{e.Amount}", Theme.Cinnabar, null);
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

            if (maxHit > 0)
            {
                // 伤害越高音调越低、震屏越大(封顶)
                _audio.pitch = Mathf.Clamp(1.3f - maxHit / 80f, 0.6f, 1.3f);
                _audio.PlayOneShot(maxHit >= 30 ? _thudClip : _hitClip, 0.9f);
                _audio.pitch = 1f;
                StartCoroutine(Shake(Mathf.Clamp(4f + maxHit * 0.35f, 4f, 26f)));
            }
            if (playerHit)
            {
                _audio.PlayOneShot(_thudClip, 0.8f);
                StartCoroutine(Shake(10f));
            }
        }

        // ---- 震屏 ----

        private IEnumerator Shake(float amplitude)
        {
            var origin = _shakeTarget.anchoredPosition;
            float t = 0f;
            const float duration = 0.22f;
            while (t < duration)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float decay = 1f - t / duration;
                _shakeTarget.anchoredPosition = origin + UnityEngine.Random.insideUnitCircle * (amplitude * decay);
                yield return null;
            }
            _shakeTarget.anchoredPosition = origin;
        }

        // ---- 受击弹跳(以缩放冲击代替命中停顿,回合制 UI 更合适) ----

        private void Punch(RectTransform target)
        {
            if (target != null)
                StartCoroutine(PunchRoutine(target));
        }

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

        private void Popup(string text, Color color, RectTransform anchor, bool small = false)
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
            label.fontSize = small ? 26 : 36;
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
    }
}
