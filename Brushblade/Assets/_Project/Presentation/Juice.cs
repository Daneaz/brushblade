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
        private AudioClip _healClip;

        public void Init(RectTransform shakeTarget)
        {
            _shakeTarget = shakeTarget;
            _shakeHome = shakeTarget.anchoredPosition;
            _audio = gameObject.AddComponent<AudioSource>();
            _hitClip = Synth(0.07f, 190f, noise: 0.7f);   // 命中:脆
            _thudClip = Synth(0.12f, 90f, noise: 0.4f);   // 重击/受击:闷
            _shieldClip = Synth(0.1f, 320f, noise: 0.1f); // 护盾:润
            _killClip = SynthSweep(0.18f, 260f, 90f, noise: 0.25f); // 击杀:下行收束
            _healClip = SynthSweep(0.16f, 380f, 620f, noise: 0.05f); // 治疗:上行,与击杀的下行相反
        }

        private const float FlyDuration = 0.24f; // 飞牌全程(与 FlyRoutine 一致)
        private const float StepGap = 0.42f;     // 一记结算与下一记之间的间隔(串行看得清的关键;DoT/召唤/敌方通用)
        private const float PhaseGap = 0.4f;     // 阶段之间的停顿(DoT → 召唤 → 敌方)
        private const float TailGap = 0.3f;      // 末次打击到「播完回调」的收尾停顿

        /// <summary>播放一次动作的全部结算表现,全程有序、播完回调。enemyAnchor(i) 返回敌人本体圆
        /// (可为 null);summonAnchor(k) 返回第 k 记召唤反击的发起召唤物(可为 null);onComplete 在
        /// 所有动效落幕后调用(战斗结束标语等它,2026-07-24)。
        /// 召唤反击逐个顺序播、伤害与「正!」分节拍(2026-07-24):此前同帧齐发相互重叠、只见一次。</summary>
        public void Play(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor = null, Action onComplete = null, Action<BattleEvent> onImpact = null)
        {
            StartCoroutine(PlayRoutine(events, enemyAnchor, summonAnchor, onComplete, onImpact));
        }

        private IEnumerator PlayRoutine(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action onComplete, Action<BattleEvent> onImpact)
        {
            // 读锚点世界坐标前先结算本帧布局:敌人格挂布局组,新建/重排后同帧读到的是未结算值,
            // DoT/召唤伤害会飘到屏幕中间而非怪物本体(2026-07-24)。
            Canvas.ForceUpdateCanvases();
            _flashing.RemoveWhere(t => t == null); // 上一轮重绘销毁的目标不留在集合里

            // 拆出召唤反击段:灼烧等在前(preRest)、召唤反击逐记(strikes)、敌人行动在后(postRest)。
            // 边界只认两个显式信号:SummonAttack 开一记、EnemyTurnBegan 开敌方段。
            // 一记召唤带的伴随事件不做种类白名单 —— 受击加攻/分裂/换阶都得跟着它那一记走,
            // 早先按 Damage/EnemyDied 白名单收,遇上焦痕的加攻就断段,后续召唤全被冲进敌方段齐发。
            int i = 0;
            var preRest = new List<BattleEvent>();
            while (i < events.Count && events[i].Kind != BattleEventKind.SummonAttack
                   && events[i].Kind != BattleEventKind.EnemyTurnBegan)
                preRest.Add(events[i++]);
            var strikes = new List<(int target, int source, List<BattleEvent> effects)>();
            while (i < events.Count && events[i].Kind == BattleEventKind.SummonAttack)
            {
                int target = events[i].TargetIndex;   // 受击敌人(飞牌终点)
                int source = events[i].SecondIndex;   // 发起召唤物(飞牌起点)
                i++;
                var effects = new List<BattleEvent>();
                while (i < events.Count && events[i].Kind != BattleEventKind.SummonAttack
                       && events[i].Kind != BattleEventKind.EnemyTurnBegan)
                    effects.Add(events[i++]);
                strikes.Add((target, source, effects));
            }
            if (i < events.Count && events[i].Kind == BattleEventKind.EnemyTurnBegan)
                i++; // 分隔符本身不播
            var postRest = new List<BattleEvent>();
            while (i < events.Count)
                postRest.Add(events[i++]);

            // 三阶段串行、阶段间留停顿(①DoT ②召唤 ③敌人反击 → ④胜利标语)。每记间隔在 ApplyBatch 内(StepGap),
            // 此处只管阶段边界(PhaseGap)。受击致死者由 ApplyBatch 内联即时置灰+正(与致死伤害同帧,不再攒到统一节拍)。
            yield return ApplyBatch(preRest, enemyAnchor, summonAnchor, onImpact);      // ① DoT / 全体伤害
            if (preRest.Count > 0 && (strikes.Count > 0 || postRest.Count > 0))
                yield return new WaitForSecondsRealtime(PhaseGap);
            for (int k = 0; k < strikes.Count; k++)                                    // ② 召唤物逐个行动+结算
            {
                var from = summonAnchor?.Invoke(strikes[k].source);
                var toRect = enemyAnchor(strikes[k].target);
                if (from != null && toRect != null)
                {
                    FlyGlyph("木", Theme.ElementColor(Element.Wood), from.position, toRect.position);
                    yield return new WaitForSecondsRealtime(FlyDuration); // 等飞牌砸到才结算
                }
                yield return ApplyBatch(strikes[k].effects, enemyAnchor, summonAnchor, onImpact); // 内含 StepGap
            }
            if (strikes.Count > 0 && postRest.Count > 0)
                yield return new WaitForSecondsRealtime(PhaseGap);
            yield return ApplyBatch(postRest, enemyAnchor, summonAnchor, onImpact);     // ③ 敌人逐记反击
            if (postRest.Count > 0)
                yield return new WaitForSecondsRealtime(PhaseGap); // 敌方行动收尾多停一拍:看清末记掉血(如召唤物被打死 HP 归 0)再回合

            yield return new WaitForSecondsRealtime(TailGap);
            onComplete?.Invoke();                                                       // ④ 关卡胜利标语(外层)
        }

        /// <summary>结算一批事件的表现。受击致死者(紧随伤害的 EnemyDied)当帧内联:飘「正!」+ 立刻置灰,
        /// 与致死伤害同帧、分别显示。onImpact 在每记伤害触达时回调外层(触达才掉血)。
        /// 并行组(Damage 同帧齐出)组末停一拍;串行单位(DoT/敌攻/SummonHit)在下一记前才停一拍,好让致死伤害与正同帧。</summary>
        private IEnumerator ApplyBatch(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action<BattleEvent> onImpact)
        {
            bool anyParallel = false;   // 全体攻击:多个 Damage 同帧齐出,组末只停一拍
            bool serialPending = false; // 上一记串行单位已出,下一记串行单位前先停一拍
            for (int idx = 0; idx < events.Count; idx++)
            {
                var e = events[idx];
                // 这记伤害是否当场打死目标:是则跳过白闪,交给死亡置灰(免抢同一 Image)
                bool kills = (e.Kind == BattleEventKind.Damage || e.Kind == BattleEventKind.BurnTick
                        || e.Kind == BattleEventKind.BleedTick)
                    && KillsTarget(events, idx, e.TargetIndex);
                switch (e.Kind)
                {
                    case BattleEventKind.Damage: // 直接伤害:全体攻击并行 —— 本记不 yield,组末统一停一拍
                        Popup($"-{e.Amount}", Theme.Cinnabar, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        if (!kills) HitReact(enemyAnchor(e.TargetIndex)); // 致死不白闪,让位给置灰
                        HitFx(e.Amount);
                        onImpact?.Invoke(e);
                        anyParallel = true;
                        break;
                    case BattleEventKind.BurnTick: // 火系 DoT:串行 + 火焰视觉
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap); // 与上一记 DoT 拉开
                        Popup($"-{e.Amount}", Theme.ShopNav, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        FlameBurst(enemyAnchor(e.TargetIndex));
                        HitFx(e.Amount);
                        onImpact?.Invoke(e);
                        serialPending = true;
                        break;
                    case BattleEventKind.BleedTick: // 无属性 DoT:同样串行,但用朱砂色且不带火焰
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        Popup($"-{e.Amount}", Theme.Cinnabar, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        if (!kills) HitReact(enemyAnchor(e.TargetIndex));
                        HitFx(e.Amount);
                        onImpact?.Invoke(e);
                        serialPending = true;
                        break;
                    case BattleEventKind.EnemyDied: // 受击致死:与刚才那记伤害同帧,飘「正!」+ 立刻置灰(分别显示)
                        var dead = enemyAnchor(e.TargetIndex);
                        Popup("正!", Theme.Ink, dead);
                        GreyOut(dead);                       // 立刻置灰
                        Knockback(dead);                     // 一记后坐
                        _audio.PlayOneShot(_killClip, 0.9f); // 下行收束音
                        ScreenFlash(0.16f, Color.white);     // 致命全屏微闪
                        break;
                    case BattleEventKind.SummonHit: // 敌人打召唤物:攻击者(TargetIndex)下扑 + 飘伤害在承伤召唤(SecondIndex)身上
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        var tank = summonAnchor?.Invoke(e.SecondIndex); // 承伤者(坦克死后前移到下一个)
                        Lunge(enemyAnchor(e.TargetIndex));
                        Popup($"-{e.Amount}", Theme.Cinnabar, tank);
                        HitReact(tank);
                        _audio.PlayOneShot(_thudClip, 0.7f);
                        StartCoroutine(Shake(7f));
                        onImpact?.Invoke(e); // 触达才扣召唤血
                        serialPending = true;
                        break;
                    case BattleEventKind.EnemyAttack: // 敌人打我方:攻击者下扑 + 飘伤害 + 闷响 + 震屏 + 屏缘朱砂微闪
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        Lunge(enemyAnchor(e.TargetIndex));
                        // 飘字分账(2026-07-25):护盾吃掉多少、血实掉多少分开写,与两条同步
                        int hpLoss = e.Amount - e.Absorbed;
                        if (e.Absorbed <= 0) Popup($"-{e.Amount}", Theme.Cinnabar, null);
                        else if (hpLoss <= 0) Popup($"盾-{e.Absorbed}", Theme.SplitBlue, null);
                        else Popup($"盾-{e.Absorbed} 血-{hpLoss}", Theme.Cinnabar, null, small: true);
                        _audio.PlayOneShot(_thudClip, 0.8f);
                        StartCoroutine(Shake(10f));
                        ScreenFlash(0.14f, Theme.Cinnabar);
                        onImpact?.Invoke(e); // 触达才扣玩家血
                        serialPending = true;
                        break;
                    case BattleEventKind.Burn:
                        Popup($"灼+{e.Amount}", Theme.ShopNav, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    // 免疫完全挡下一记(2026-08-06):血条护盾条都不动,只给一个「免」的表达。
                    // 与护盾吸伤同款(2026-08-06 M4 改):攻击者下扑(Lunge)让这记攻击在画面上
                    // 真的发生过,飘字锚在屏幕中下(null,与 EnemyAttack/Heal/Shield 同口径)——
                    // 原先飘在敌人头上会读成「这只敌人免疫了」,而且没有 Lunge,整记攻击等于
                    // 在画面上凭空消失。
                    case BattleEventKind.ImmunityBlocked:
                        Lunge(enemyAnchor(e.TargetIndex));
                        Popup("免", Theme.Jade, null);
                        break;
                    // 治疗:刻意**不 yield、不置 serialPending** —— 群攻与回血是同一记里的两件事,
                    // 分开演就成了「先打完,血条才慢半拍地涨」(2026-07-29 实测)
                    case BattleEventKind.Heal:
                        if (e.Amount <= 0) break;
                        Popup($"+{e.Amount}", Theme.SplitBlue, null);
                        _audio.PlayOneShot(_healClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨血条
                        break;
                    // 缺笔妖补全:串行占一拍 —— 它是敌方回合里独立发生的事,
                    // 与那一记攻击挤在同帧就会被当成攻击的一部分
                    case BattleEventKind.Regrow:
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        Popup(e.SecondIndex >= 3
                                ? (e.Amount > 0 ? $"补全! +{e.Amount}" : "补全!")
                                : (e.Amount > 0 ? $"补全 {e.SecondIndex}/3 +{e.Amount}" : $"补全 {e.SecondIndex}/3"),
                            Theme.Jade, enemyAnchor(e.TargetIndex), small: e.SecondIndex < 3);
                        _audio.PlayOneShot(_healClip, 0.6f);
                        onImpact?.Invoke(e); // 触达才回血
                        serialPending = true;
                        break;
                    case BattleEventKind.Shield:
                        Popup($"盾+{e.Amount}", Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨护盾条
                        break;
                    case BattleEventKind.ShieldBroken:
                        Popup($"盾-{e.Amount}", Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才把护盾条推到 0(倾覆专用,BattleView.OnImpact 处理)
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
            if (anyParallel) // 全体伤害同帧齐出后,统一停一拍(看清飘字/掉血)再进下一阶段
                yield return new WaitForSecondsRealtime(StepGap);
        }

        /// <summary>这记伤害是否打死了目标:向后扫到下一记伤害为止,期间出现本目标的 EnemyDied 即算。
        /// 不只看紧邻的下一条 —— 中间插了别的事件也不该误判成没打死(否则白闪与置灰抢同一 Image)。</summary>
        private static bool KillsTarget(IReadOnlyList<BattleEvent> events, int from, int target)
        {
            for (int j = from + 1; j < events.Count; j++)
            {
                var kind = events[j].Kind;
                if (kind == BattleEventKind.Damage || kind == BattleEventKind.BurnTick
                    || kind == BattleEventKind.BleedTick) return false; // 下一记伤害开始了
                if (kind == BattleEventKind.EnemyDied && events[j].TargetIndex == target) return true;
            }
            return false;
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

        // 正在白闪的目标:同一目标同帧挨多记时,后来者会把「原色」读成白闪中的颜色,
        // 复原时就把目标刷成白的并卡在那里(直到下次重绘)。一次只许闪一个。
        private readonly HashSet<RectTransform> _flashing = new();

        /// <summary>受击反应:更狠的缩放冲击 + 头像白闪一下(比 Punch 更强,专供敌人挨打)。</summary>
        private void HitReact(RectTransform target)
        {
            if (target == null || !_flashing.Add(target)) return;
            StartCoroutine(HitReactRoutine(target));
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
            _flashing.Remove(target);
        }

        /// <summary>敌人攻击下扑:攻击者头像向下(我方召唤/玩家所在)猛冲一记再收回,增强"撞过来"的打击感。</summary>
        private void Lunge(RectTransform attacker)
        {
            if (attacker != null) StartCoroutine(LungeRoutine(attacker));
        }

        private static IEnumerator LungeRoutine(RectTransform attacker)
        {
            Vector2 home = attacker.anchoredPosition;
            float t = 0f;
            const float duration = 0.18f;
            const float reach = 34f;
            while (t < duration && attacker != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float off = Mathf.Sin(t / duration * Mathf.PI) * reach; // 冲出去再收回(峰值在中点)
                attacker.anchoredPosition = home + new Vector2(0f, -off); // 我方在下,向下撞
                yield return null;
            }
            if (attacker != null) attacker.anchoredPosition = home;
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
            // 取整棵子树:圆形字头像的 Image 在自己身上,分层字怪(MobView)的却在各层子节点上——
            // 只看自己会让形象怪死了不变灰(静默失效)
            var images = target.GetComponentsInChildren<Image>(true);
            if (images.Length == 0) yield break;
            var from = new Color[images.Length];
            for (int i = 0; i < images.Length; i++) from[i] = images[i].color;

            float t = 0f;
            const float duration = 0.2f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                for (int i = 0; i < images.Length; i++)
                    if (images[i] != null) images[i].color = GreyOf(from[i], t / duration);
                yield return null;
            }
            if (target == null) yield break;
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null) images[i].color = GreyOf(from[i], 1f);
        }

        /// <summary>置灰色:只推 RGB,alpha 保持各层原值——状态层(L4)的 alpha 编码着战斗状态,
        /// 一并拉到 1 会让墨雾/火芯在死亡瞬间突然全显。</summary>
        private static Color GreyOf(Color from, float amount)
        {
            var grey = Color.Lerp(from, Theme.LockedBg, Mathf.Clamp01(amount));
            grey.a = from.a;
            return grey;
        }

        // 火焰色阶(黄 → 橙 → 红):火系 DoT 火苗
        private static readonly Color[] FlamePalette =
        {
            new Color(1f, 0.85f, 0.3f), new Color(1f, 0.6f, 0.12f), new Color(0.95f, 0.35f, 0.1f),
        };

        /// <summary>火系 DoT 火焰:怪物本体窜起几簇火苗,上升摇曳收缩淡出(程序生成,无资产)。</summary>
        private void FlameBurst(RectTransform target)
        {
            if (target == null) return;
            for (int n = 0; n < 5; n++)
            {
                var go = new GameObject("Ember", typeof(RectTransform));
                go.transform.SetParent(_shakeTarget, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(UnityEngine.Random.Range(9f, 16f), UnityEngine.Random.Range(14f, 26f));
                rect.position = target.position;
                rect.anchoredPosition += new Vector2(UnityEngine.Random.Range(-24f, 24f), UnityEngine.Random.Range(-18f, 10f));
                var image = go.AddComponent<Image>();
                image.sprite = Theme.Rounded(8);
                image.type = Image.Type.Sliced;
                image.color = FlamePalette[UnityEngine.Random.Range(0, FlamePalette.Length)];
                image.raycastTarget = false;
                StartCoroutine(EmberRoutine(rect, image));
            }
        }

        private static IEnumerator EmberRoutine(RectTransform rect, Image image)
        {
            Vector2 start = rect.anchoredPosition;
            Color from = image.color;
            float duration = UnityEngine.Random.Range(0.4f, 0.62f);
            float sway = UnityEngine.Random.Range(3f, 7f);
            float t = 0f;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / duration;
                rect.anchoredPosition = start + new Vector2(Mathf.Sin(k * 9f) * sway, 62f * k); // 上升 + 左右摇曳
                rect.localScale = Vector3.one * (1f - 0.55f * k);                                // 越升越小
                var c = from;
                c.a = 1f - k * k; // 尾段快速熄灭
                image.color = c;
                yield return null;
            }
            if (rect != null)
                UnityEngine.Object.Destroy(rect.gameObject);
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

        // ---- 条上涨势(治疗 / 筑盾 / 补全)----

        private const float BarPulseDuration = 0.55f;

        /// <summary>血条 / 盾条上的一记「涨」:条身被同色辉光洗一遍并微微上顶,
        /// 再从条上浮起几枚属性元件。治疗、筑盾、补全都是「涨」,共用一套动作语言,
        /// 靠颜色与元件区分是哪一系 —— 元件直接复用字牌那批素材
        /// (《字牌形象关键词包》§4.3 本就是这么许诺的:一个元件同时服务字牌与战斗特效)。</summary>
        public void BarPulse(RectTransform fill, Color color, Element? element = null)
        {
            if (fill == null || fill.parent == null) return;
            var bar = (RectTransform)fill.parent;
            StartCoroutine(BarGlowRoutine(bar, color));
            var sprite = element.HasValue ? CardFrames.Element(element) : null;
            if (sprite == null) return;
            // 土是**沉降**不是上浮(§4.1):尘石只抖一下就落定,别跟水一样往上飘,
            // 何况盾条在血条下方,飘高了会糊到血条上
            float rise = element == Element.Earth ? 0.2f : 0.75f;
            for (int i = 0; i < 3; i++)
                StartCoroutine(BarMoteRoutine(bar, sprite, color, i, rise));
        }

        private static IEnumerator BarGlowRoutine(RectTransform bar, Color color)
        {
            var go = new GameObject("BarPulse", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bar, false);
            go.transform.SetSiblingIndex(1); // 压在 Fill 之上、血值文本之下 —— 别糊住数字
            var image = go.GetComponent<Image>();
            image.sprite = Theme.Rounded(10);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            Ui.Stretch((RectTransform)go.transform);

            float t = 0f;
            while (t < BarPulseDuration && go != null && bar != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float u = t / BarPulseDuration;
                var c = color;
                c.a = Mathf.Sin(u * Mathf.PI) * 0.5f; // 进出各淡一次,不是硬切
                image.color = c;
                bar.localScale = new Vector3(1f, 1f + 0.16f * Mathf.Sin(u * Mathf.PI), 1f);
                yield return null;
            }
            if (bar != null) bar.localScale = Vector3.one;
            if (go != null) Destroy(go);
        }

        private static IEnumerator BarMoteRoutine(RectTransform bar, Sprite sprite, Color color, int index, float rise)
        {
            var go = new GameObject("BarMote", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bar, false);
            var rect = (RectTransform)go.transform;
            float side = Mathf.Max(20f, bar.rect.height * 2.4f);
            rect.sizeDelta = new Vector2(side, side);
            rect.anchorMin = rect.anchorMax = new Vector2(0.2f + index * 0.3f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // 三枚错开起跑,免得像一排整齐的图标同时弹出
            float delay = index * 0.06f;
            float t = -delay;
            while (t < BarPulseDuration && go != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / BarPulseDuration);
                var c = color;
                c.a = Mathf.Sin(u * Mathf.PI) * 0.8f;
                image.color = c;
                rect.anchoredPosition = new Vector2(0f, u * side * rise); // 自条上浮起(土只抖一下)
                float s = Mathf.Lerp(0.6f, 1.1f, u);
                rect.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (go != null) Destroy(go);
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
