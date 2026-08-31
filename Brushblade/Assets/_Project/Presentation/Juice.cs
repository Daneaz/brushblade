using System;
using System.Collections;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
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
        private AudioSource[] _voices;
        private int _voice;
        private AudioClip _hitClip;
        private AudioClip _thudClip;
        private AudioClip _shieldClip;
        private AudioClip _killClip;
        private AudioClip _healClip;

        /// <summary>同时发声的路数(2026-08-30)。此前只有一个 AudioSource,而 pitch 是它的**持续属性**、
        /// 不是 PlayOneShot 的快照 —— 原先「设 pitch → PlayOneShot → 立刻设回 1」的写法等于
        /// 让几乎所有音效都以 pitch 1 播出,「伤害越高音调越低」这条设计从来没真正落地过。
        /// 分成几路轮转、每路设好 pitch 就不再动它,调制才生效,并发的几记也不再互相串音。</summary>
        private const int VoiceCount = 6;

        public void Init(RectTransform shakeTarget)
        {
            _shakeTarget = shakeTarget;
            _shakeHome = shakeTarget.anchoredPosition;
            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++) _voices[i] = gameObject.AddComponent<AudioSource>();
            _hitClip = Synth(0.07f, 190f, noise: 0.7f);   // 命中:脆
            _thudClip = Synth(0.12f, 90f, noise: 0.4f);   // 重击/受击:闷
            _shieldClip = Synth(0.1f, 320f, noise: 0.1f); // 护盾:润
            _killClip = SynthSweep(0.18f, 260f, 90f, noise: 0.25f); // 击杀:下行收束
            _healClip = SynthSweep(0.16f, 380f, 620f, noise: 0.05f); // 治疗:上行,与击杀的下行相反
        }

        /// <summary>发一记音:取下一路音源,设好 pitch 再播,**播完不复位**(见 <see cref="VoiceCount"/>)。
        /// 每次叠一点随机微扰 —— 同一记打击音一模一样地重复几十遍,人耳会把它听成机械噪声而不是打击。</summary>
        private void PlayClip(AudioClip clip, float volume, float pitch = 1f)
        {
            if (_voices == null || clip == null) return;
            _voice = (_voice + 1) % _voices.Length;
            var source = _voices[_voice];
            source.pitch = pitch * UnityEngine.Random.Range(1f - PitchJitter, 1f + PitchJitter);
            source.PlayOneShot(clip, volume);
        }

        private const float PitchJitter = 0.06f;

        // ---- 顿帧(hit stop) ----

        /// <summary>命中瞬间把时间按住几十毫秒。打击感里投入产出比最高的一件事 ——
        /// 没有它,再大的震屏也只是画面在抖,冲击拿不到「重量」。
        ///
        /// (2026-08-30:本类的类注释与 BattleView 的注释从一开始就写着「命中顿帧」,
        /// 但全项目没有一处写过 Time.timeScale —— 这个功能此前只存在于注释里。)
        ///
        /// 记的是**结束时刻**而不是累加时长:全体攻击同帧五记命中只取最狠的那记停,
        /// 不会五次各停 60ms 叠成一次半秒的卡死。</summary>
        private float _hitStopUntil;
        private bool _hitStopRunning;

        // 克制加强档(2026-08-30 用户拍板):水墨的「静」保住,只把「重」补回来。
        // 三个数是这套打击感的主旋钮,想更爽/更收敛先动这里
        private const float HitStopLight = 0.03f;  // 小伤害:一记轻轻的迟滞
        private const float HitStopHeavy = 0.075f; // 重击封顶
        private const float HitStopBig = 0.10f;    // 暴击 / 相克 / 引爆:再重一档

        private void HitStop(float seconds)
        {
            if (seconds <= 0f) return;
            _hitStopUntil = Mathf.Max(_hitStopUntil, UnityEngine.Time.unscaledTime + seconds);
            if (_hitStopRunning) return;
            _hitStopRunning = true;
            StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            UnityEngine.Time.timeScale = 0f;
            while (UnityEngine.Time.unscaledTime < _hitStopUntil) yield return null;
            UnityEngine.Time.timeScale = 1f;
            _hitStopRunning = false;
        }

        /// <summary>⚠ 顿帧期间本组件被销毁/禁用(战斗结束、切场景)时必须把 timeScale 放回去 ——
        /// 协程随物件一起没了,timeScale 会永远停在 0,整个游戏冻死。这是引入顿帧唯一的真风险,
        /// 所以兜底写在这里而不是指望协程一定跑完。</summary>
        private void OnDisable()
        {
            if (!_hitStopRunning) return;
            UnityEngine.Time.timeScale = 1f;
            _hitStopRunning = false;
        }

        // ---- 结算快进(按住屏幕)----

        /// <summary>按住屏幕时结算节拍走得更快(2026-08-30)。群攻 + DoT + 一排召唤反击时一轮结算
        /// 能到好几秒,看过一遍之后就只是在等 —— 但节拍本身不能删,它是「看得清」的唯一保障。
        ///
        /// 做成**按住**而不是点一下切换:玩家随时能松手回到正常速度,不会误触之后整段糊过去。
        /// 结算期间外层是锁输入的,所以这个按住不跟任何点击抢。</summary>
        private const float FastForwardRate = 3f;
        private float _rate = 1f;

        private void Update()
        {
            _rate = Input.GetMouseButton(0) || Input.touchCount > 0 ? FastForwardRate : 1f;
        }

        /// <summary>结算节拍的等待。不用 WaitForSecondsRealtime —— 那个一旦 yield 出去时长就锁死了,
        /// 中途按住屏幕对**已经在等的这一拍**没用;逐帧累加才能立刻响应。</summary>
        /// <summary>给外层驱动协程用的同一条等待(BattleView 的行动者间停顿)——
        /// 否则按住屏幕时段内飞快、段间照旧慢等,整体反而更别扭。</summary>
        public IEnumerator Wait(float seconds) => Beat(seconds);

        private IEnumerator Beat(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += UnityEngine.Time.unscaledDeltaTime * _rate;
                yield return null;
            }
        }

        private const float FlyDuration = 0.24f; // 飞牌全程(与 FlyRoutine 一致)
        private const float StepGap = 0.42f;     // 一记结算与下一记之间的间隔(串行看得清的关键;DoT/召唤/敌方通用)
        private const float TailGap = 0.3f;      // 末次打击到「播完回调」的收尾停顿

        /// <summary>播放一次动作的全部结算表现,全程有序、播完回调。enemyAnchor(i) 返回敌人本体圆
        /// (可为 null);summonAnchor(k) 返回第 k 记召唤反击的发起召唤物(可为 null);onComplete 在
        /// 所有动效落幕后调用(战斗结束标语等它,2026-07-24)。
        /// 召唤反击逐个顺序播、伤害与「正!」分节拍(2026-07-24):此前同帧齐发相互重叠、只见一次。</summary>
        public void Play(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor = null, Action onComplete = null, Action<BattleEvent> onImpact = null,
            Func<int, SummonState> summonInfo = null, Func<int, Element?> enemyElement = null)
        {
            StartCoroutine(PlayRoutine(events, enemyAnchor, summonAnchor, onComplete, onImpact,
                summonInfo, enemyElement));
        }

        private IEnumerator PlayRoutine(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action onComplete, Action<BattleEvent> onImpact,
            Func<int, SummonState> summonInfo, Func<int, Element?> enemyElement)
        {
            // 读锚点世界坐标前先结算本帧布局:敌人格挂布局组,新建/重排后同帧读到的是未结算值,
            // DoT/召唤伤害会飘到屏幕中间而非怪物本体(2026-07-24)。
            Canvas.ForceUpdateCanvases();
            _flashing.RemoveWhere(t => t == null); // 上一轮重绘销毁的目标不留在集合里
            // 飘字排队表同理:锚点是跨帧持有的 RectTransform,重绘一过就全成了空引用。
            // 一整段结算之间必然隔着重绘,所以整表清掉即可,不用逐个判空
            _popupSlots.Clear();

            // 逐格驱动后(2026-08-15 ATB 改造),每批事件天生属于一个行动者,
            // 不再需要猜段边界 —— 原先靠 SummonAttack / EnemyTurnBegan 划界的三段切分已删除,
            // 段间停顿由 BattleView 的驱动协程控制。
            yield return ApplyBatch(events, enemyAnchor, summonAnchor, onImpact, summonInfo, enemyElement);

            yield return Beat(TailGap);
            onComplete?.Invoke();                                                       // 关卡胜利标语(外层)
        }

        /// <summary>结算一批事件的表现。受击致死者(紧随伤害的 EnemyDied)当帧内联:飘「正!」+ 立刻置灰,
        /// 与致死伤害同帧、分别显示。onImpact 在每记伤害触达时回调外层(触达才掉血)。
        /// 并行组(Damage 同帧齐出)组末停一拍;串行单位(DoT/敌攻/SummonHit)在下一记前才停一拍,好让致死伤害与正同帧。</summary>
        private IEnumerator ApplyBatch(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action<BattleEvent> onImpact,
            Func<int, SummonState> summonInfo = null, Func<int, Element?> enemyElement = null)
        {
            bool anyParallel = false;   // 全体攻击:多个 Damage 同帧齐出,组末只停一拍
            bool serialPending = false; // 上一记串行单位已出,下一记串行单位前先停一拍
            int lastDamageTarget = int.MinValue; // 上一记 Damage 的目标:跨过分裂/加攻/现形这些伴随事件,
                                                  // 不能只看 events[idx-1] 是否紧邻(2026-08-08 评审修复)
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
                        // 多段(2026-08-07,剁;2026-08-08 评审修复):同一目标连续两记伤害要拉开一拍,
                        // 否则一拍打完两段,玩家看不出是两段。用「上一记 Damage 的目标」而不是
                        // 「events[idx-1] 是否紧邻」判断 —— DamageEnemy 在两记伤害之间可能插
                        // EnemyRevealed/EnemyBuff/EnemySplit 这些伴随事件(焦痕/生僻字/叠字怪),
                        // 紧邻判据会被这些事件打断,漏掉本该拉开的一拍。跨目标的全体攻击(DamageAll)
                        // 各条 TargetIndex 不同,lastDamageTarget 逐个变化,仍并行不受影响
                        if (lastDamageTarget == e.TargetIndex)
                            yield return Beat(StepGap);
                        lastDamageTarget = e.TargetIndex;
                        // 暴击(2026-08-12,E-b2):飘「暴」+ 放大一档 + 更重的震屏。
                        // 数值上暴击只是 ×1.5,与相克 ×1.5 长得一模一样 —— 玩家能不能读出
                        // 「这记暴了」全靠这里的表达,不能靠数字大小。
                        //
                        // 相克(2026-08-30,e.Ke):同一条道理,而且更迫切 —— 相克此前**一点表达都没有**,
                        // 玩家读不出自己有没有打对属性,而这是本作的核心机制(还顺带无视全部护甲)。
                        // 两者可以同时发生:暴击是「打得狠」,相克是「打得对」,飘字合成「暴克」,
                        // 颜色则交给相克 —— 金比朱砂更跳,而「打对属性」是玩家更需要学会的那件事
                        var hitAnchor = enemyAnchor(e.TargetIndex);
                        // 放大档:暴击 1.35 起(罕见,该抢眼),相克 1.18 起(常见,大一点就够,
                        // 再大整场都是巨字、反而分不出哪记特别);两者都占时按暴击那档
                        float damageScale = e.Crit ? 1.35f : e.Ke ? 1.18f : 1f;
                        // 飘字颜色 = **攻击方**的五行色(2026-08-30):火系打出来是朱砂、水系是靖蓝……
                        // 玩家一眼读得出这记是拿什么属性打的。用 GlyphColor 而不是 ElementColor ——
                        // 飘字就是字,得过 WCAG 4.5:1(金 #B3A382 对宣纸底只有 2.48,大字门槛都够不到)。
                        // 火系色恰好与原来的朱砂几乎同色,所以火系那一路看起来跟改之前一样
                        //
                        // 护盾分账(2026-08-30):与 EnemyAttack 同口径,Amount − Absorbed 才是实际掉血。
                        // 敌人目前没有任何加盾来源,Absorbed 恒为 0,这条分支今天走不到,但要先写对——
                        // 等加盾辅助怪上线,飘字数字不能跟血条实际掉的量对不上
                        int enemyHpLoss = e.Amount - e.Absorbed;
                        if (e.Absorbed <= 0)
                            Popup(DamageText(e), Theme.GlyphColor(e.Attacker), hitAnchor,
                                sizeScale: Mathf.Clamp(damageScale + e.Amount / 50f,
                                    1f, e.Crit ? 2.4f : e.Ke ? 2.1f : 1.9f),
                                outline: e.Ke ? Theme.GoldBorder : null);
                        else if (enemyHpLoss <= 0)
                            Popup(Strings.T("juice.popup.shield_absorbed", ("absorbed", e.Absorbed)), Theme.SplitBlue, hitAnchor);
                        else
                            Popup(Strings.T("juice.popup.shield_and_hp_loss", ("absorbed", e.Absorbed), ("hpLoss", enemyHpLoss)),
                                Theme.GlyphColor(e.Attacker), hitAnchor, small: true);
                        if (e.Ke) Ring(hitAnchor, Theme.GoldBorder); // 相克专属:一圈金环炸开
                        if (!kills) HitReact(hitAnchor); // 致死不白闪,让位给置灰
                        HitFx(e.Amount, e.Crit, e.Ke, hitAnchor);
                        onImpact?.Invoke(e);
                        anyParallel = true;
                        break;
                    case BattleEventKind.BurnTick: // 火系 DoT:串行 + 火焰视觉
                        if (serialPending) yield return Beat(StepGap); // 与上一记 DoT 拉开
                        Popup($"-{e.Amount}", Theme.GlyphColor(e.Attacker), enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f),
                            outline: e.Ke ? Theme.GoldBorder : null);
                        FlameBurst(enemyAnchor(e.TargetIndex));
                        HitFx(e.Amount, ke: e.Ke, target: enemyAnchor(e.TargetIndex));
                        onImpact?.Invoke(e);
                        serialPending = true;
                        break;
                    // 引爆(2026-08-09,灱):把剩余灼烧层数一次性全打出来再清空,不是一记普通 DoT tick,
                    // 给一记比 BurnTick 更重的震屏,读出「抢杀爆发」的分量——不调 HitFx(它自带的震屏
                    // 上限 26,比这里这条更轻),改成内联 HitFx 的音效/闪光两件事(2026-08-10 复核补):
                    // 打击音 + amount≥40 全屏微闪照抄 HitFx,震屏单用下面这条更重的,避免叠两次。
                    case BattleEventKind.Detonate:
                        Popup(Strings.T("juice.popup.detonate", ("amount", e.Amount)),
                            Theme.GlyphColor(e.Attacker), enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f),
                            outline: e.Ke ? Theme.GoldBorder : null);
                        var blastAnchor = enemyAnchor(e.TargetIndex);
                        HitStop(HitStopBig); // 抢杀爆发:与暴击/相克同一档的顿帧
                        PlayClip(e.Amount >= 30 ? _thudClip : _hitClip, 0.9f,
                            Mathf.Clamp(1.3f - e.Amount / 80f, 0.6f, 1.3f));
                        StartCoroutine(Shake(Mathf.Clamp(10f + e.Amount * 0.4f, 10f, 30f),
                            AttackDir(blastAnchor)));
                        Ring(blastAnchor, Theme.GlyphColor(e.Attacker)); // 火色扩散环,读出「炸开」
                        if (e.Amount >= 40) ScreenFlash(0.12f, Color.white);
                        onImpact?.Invoke(e);
                        break;
                    case BattleEventKind.BleedTick: // 无属性 DoT:同样串行,但用朱砂色且不带火焰
                        if (serialPending) yield return Beat(StepGap);
                        Popup($"-{e.Amount}", Theme.Cinnabar, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        if (!kills) HitReact(enemyAnchor(e.TargetIndex));
                        HitFx(e.Amount);
                        onImpact?.Invoke(e);
                        serialPending = true;
                        break;
                    // 召唤物反击敌人:飞它**自己的字**从发起召唤物(SecondIndex)砸向受击敌人(TargetIndex),
                    // 与玩家出牌的飞字同款(2026-08-17,此前写死「木」);落地才继续播后续事件
                    // (伤害走 Damage,紧随其后)。原先由 PlayRoutine 的 strikes 循环
                    // 单独驱动,三段切分删除后(2026-08-16)搬进这里,否则召唤反击的飞字动画会随切分一起消失。
                    case BattleEventKind.SummonAttack:
                        var from = summonAnchor?.Invoke(e.SecondIndex);
                        var toRect = enemyAnchor(e.TargetIndex);
                        if (from != null && toRect != null)
                        {
                            var attacker = summonInfo?.Invoke(e.SecondIndex);
                            FlyGlyph(attacker?.Char ?? "木",
                                Theme.ElementColor(attacker?.Element ?? Element.Wood),
                                from.position, toRect.position);
                            yield return Beat(FlyDuration); // 等飞牌砸到才结算
                        }
                        break;
                    case BattleEventKind.EnemyDied: // 受击致死:与刚才那记伤害同帧,飘「正!」+ 立刻置灰(分别显示)
                        var dead = enemyAnchor(e.TargetIndex);
                        Popup(Strings.T("juice.popup.kill_mark"), Theme.Ink, dead);
                        GreyOut(dead);                       // 立刻置灰
                        Knockback(dead);                     // 一记后坐
                        InkBurst(dead, enemyElement?.Invoke(e.TargetIndex)); // 墨散:一团墨炸开又收(2026-08-30)
                        HitStop(HitStopBig);                 // 击杀值一记最重的顿帧
                        PlayClip(_killClip, 0.9f); // 下行收束音
                        ScreenFlash(0.16f, Color.white);     // 致命全屏微闪
                        break;
                    case BattleEventKind.SummonHit: // 敌人打召唤物:攻击者(TargetIndex)下扑 + 飘伤害在承伤召唤(SecondIndex)身上
                        if (serialPending) yield return Beat(StepGap);
                        var tank = summonAnchor?.Invoke(e.SecondIndex); // 承伤者(坦克死后前移到下一个)
                        Lunge(enemyAnchor(e.TargetIndex));
                        // 敌方那一记同样按**攻击者**的五行上色(2026-08-30):这两类事件的 TargetIndex
                        // 就是攻击者下标,属性顺着它查得到,所以 Core 侧刻意没给它们加 Attacker 字段。
                        // 查不到(伪装怪未现形)时回落中性色 —— 玩家本来就还不知道它是什么属性
                        Popup($"-{e.Amount}", Theme.GlyphColor(enemyElement?.Invoke(e.TargetIndex)), tank);
                        HitReact(tank);
                        PlayClip(_thudClip, 0.7f);
                        HitStop(HitStopLight);
                        // 方向取反:这一记是敌人从上面撞下来,震屏该往我方那侧走
                        StartCoroutine(Shake(7f, -AttackDir(enemyAnchor(e.TargetIndex))));
                        onImpact?.Invoke(e); // 触达才扣召唤血
                        serialPending = true;
                        break;
                    case BattleEventKind.EnemyAttack: // 敌人打我方:攻击者下扑 + 飘伤害 + 闷响 + 震屏 + 屏缘朱砂微闪
                        if (serialPending) yield return Beat(StepGap);
                        Lunge(enemyAnchor(e.TargetIndex));
                        // 飘字分账(2026-07-25):护盾吃掉多少、血实掉多少分开写,与两条同步
                        int hpLoss = e.Amount - e.Absorbed;
                        // 掉血那一路按攻击者属性上色;被盾吃掉的那一路仍是盾的语义色(SplitBlue)——
                        // 「盾挡下了多少」说的是盾,不是打过来的是什么属性
                        var foeColor = Theme.GlyphColor(enemyElement?.Invoke(e.TargetIndex));
                        if (e.Absorbed <= 0) Popup($"-{e.Amount}", foeColor, null);
                        else if (hpLoss <= 0) Popup(Strings.T("juice.popup.shield_absorbed", ("absorbed", e.Absorbed)), Theme.SplitBlue, null);
                        else Popup(Strings.T("juice.popup.shield_and_hp_loss", ("absorbed", e.Absorbed), ("hpLoss", hpLoss)), foeColor, null, small: true);
                        PlayClip(_thudClip, 0.8f);
                        HitStop(hpLoss > 0 ? HitStopHeavy : HitStopLight); // 盾全吃下就轻一档
                        StartCoroutine(Shake(10f, -AttackDir(enemyAnchor(e.TargetIndex))));
                        ScreenFlash(0.14f, foeColor); // 屏缘那一闪也跟着来袭属性走
                        onImpact?.Invoke(e); // 触达才扣玩家血
                        serialPending = true;
                        break;
                    case BattleEventKind.Burn:
                        Popup(Strings.T("juice.popup.burn_stack", ("amount", e.Amount)),
                            Theme.GlyphColor(Element.Fire), enemyAnchor(e.TargetIndex), small: true);
                        break;
                    // 召唤物被点燃 / 自身灼烧结算(2026-08-26,灯花「打谁烧谁」)。与上面敌人侧的
                    // Burn / BurnTick 同款演出,只是锚点换成 summonAnchor —— 这两个 Kind 的
                    // TargetIndex 是**召唤物槽位**,喂给 enemyAnchor 会锚到编号相同的那只怪身上。
                    case BattleEventKind.SummonBurn:
                        Popup(Strings.T("juice.popup.burn_stack", ("amount", e.Amount)),
                            Theme.GlyphColor(Element.Fire), summonAnchor?.Invoke(e.TargetIndex), small: true);
                        break;
                    case BattleEventKind.SummonBurnTick:
                        if (serialPending) yield return Beat(StepGap);
                        var burntSummon = summonAnchor?.Invoke(e.TargetIndex);
                        Popup($"-{e.Amount}", Theme.GlyphColor(Element.Fire), burntSummon,
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        FlameBurst(burntSummon);
                        HitFx(e.Amount);
                        onImpact?.Invoke(e);
                        serialPending = true;
                        break;
                    // 免疫完全挡下一记(2026-08-06):血条护盾条都不动,只给一个「免」的表达。
                    // 与护盾吸伤同款(2026-08-06 M4 改):攻击者下扑(Lunge)让这记攻击在画面上
                    // 真的发生过,飘字锚在屏幕中下(null,与 EnemyAttack/Heal/Shield 同口径)——
                    // 原先飘在敌人头上会读成「这只敌人免疫了」,而且没有 Lunge,整记攻击等于
                    // 在画面上凭空消失。
                    // 免疫挡下(2026-08-06;2026-08-28 免疫可以挂给召唤物了)。
                    // SecondIndex ≥0 = 被保护的召唤物槽位,飘字锚在它身上;玩家为 −1,
                    // 飘屏幕中下 —— 与 Missed / EnemyAttack 同口径。不锚的话「免」会飘在
                    // **攻击者**头上,玩家读成「敌人免疫了」,正好反过来。
                    case BattleEventKind.ImmunityBlocked:
                        Lunge(enemyAnchor(e.TargetIndex));
                        Popup(Strings.T("juice.popup.immune"), Theme.Jade, e.SecondIndex >= 0
                            ? summonAnchor?.Invoke(e.SecondIndex) : null);
                        break;
                    // 打空(2026-08-07,致盲/闪避):敌人照常下扑,但什么都没打到。
                    // 没有反馈的话玩家只会以为敌人这回合没动。SecondIndex ≥0 = 打空的召唤物,
                    // 飘字锚在那只召唤物身上;玩家为 −1,与 EnemyAttack/ImmunityBlocked 同口径飘屏幕中下
                    case BattleEventKind.Missed:
                        Lunge(enemyAnchor(e.TargetIndex));
                        Popup(Strings.T("juice.popup.miss"), Theme.InkSoft, e.SecondIndex >= 0
                            ? summonAnchor?.Invoke(e.SecondIndex) : null);
                        break;
                    // 治疗:刻意**不 yield、不置 serialPending** —— 群攻与回血是同一记里的两件事,
                    // 分开演就成了「先打完,血条才慢半拍地涨」(2026-07-29 实测)
                    case BattleEventKind.Heal:
                        if (e.Amount <= 0) break;
                        Popup($"+{e.Amount}", Theme.SplitBlue, e.SecondIndex >= 0
                            ? summonAnchor?.Invoke(e.SecondIndex) : null);
                        PlayClip(_healClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨血条
                        break;
                    // 缺笔妖补全:串行占一拍 —— 它是敌方回合里独立发生的事,
                    // 与那一记攻击挤在同帧就会被当成攻击的一部分
                    case BattleEventKind.Regrow:
                        if (serialPending) yield return Beat(StepGap);
                        Popup(e.SecondIndex >= 3
                                ? (e.Amount > 0 ? Strings.T("juice.popup.regrow_full_with_heal", ("amount", e.Amount)) : Strings.T("juice.popup.regrow_full"))
                                : (e.Amount > 0 ? Strings.T("juice.popup.regrow_partial_with_heal", ("index", e.SecondIndex), ("amount", e.Amount)) : Strings.T("juice.popup.regrow_partial", ("index", e.SecondIndex))),
                            Theme.Jade, enemyAnchor(e.TargetIndex), small: e.SecondIndex < 3);
                        PlayClip(_healClip, 0.6f);
                        onImpact?.Invoke(e); // 触达才回血
                        serialPending = true;
                        break;
                    case BattleEventKind.Shield:
                        Popup(Strings.T("juice.popup.shield_gain", ("amount", e.Amount)), Theme.SplitBlue, null);
                        PlayClip(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨护盾条
                        break;
                    case BattleEventKind.ShieldBroken:
                        Popup(Strings.T("juice.popup.shield_broken", ("amount", e.Amount)), Theme.SplitBlue, null);
                        PlayClip(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才把护盾条推到 0(倾覆专用,BattleView.OnImpact 处理)
                        break;
                    case BattleEventKind.EnemySplit:
                        Popup(Strings.T("juice.popup.enemy_split"), Theme.Jade, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.BossPhase:
                        Popup(Strings.T("juice.popup.boss_phase"), Theme.GoldBorder, enemyAnchor(e.TargetIndex));
                        PlayClip(_thudClip, 1f);
                        break;
                    case BattleEventKind.EnemyBuff:
                        // Amount 是百分点(2026-08-12 敌我 AttackBuff 单位统一),飘「攻+50%」
                        // 而不是「攻+50」—— 后者会被读成加了 50 点攻击。
                        Popup(Strings.T("juice.popup.enemy_buff", ("amount", e.Amount)), Theme.InkSoft, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    // 涂改给同伴回血(2026-08-29):飘在**被治疗的那只**头上,不是治疗者头上 ——
                    // 玩家要看见的是「谁被奶回去了」,才知道该先打谁
                    case BattleEventKind.EnemyMend:
                        Popup(Strings.T("juice.popup.enemy_mend", ("amount", e.Amount)),
                            Theme.Jade, enemyAnchor(e.TargetIndex), small: true);
                        PlayClip(_healClip, 0.5f);
                        break;
                    case BattleEventKind.EnemyRevealed:
                        Popup(Strings.T("juice.popup.enemy_revealed"), Theme.SplitBlue, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.ActorActed: // 段首标记,不播(2026-08-16)
                        break;
                }
            }
            if (anyParallel) // 全体伤害同帧齐出后,统一停一拍(看清飘字/掉血)再进下一阶段
                yield return Beat(StepGap);
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

        /// <summary>一记命中的顿帧 + 音效 + 震屏(伤害越高音调越低、震屏越大,封顶);大伤害叠全屏微闪。
        ///
        /// crit(2026-08-12,E-b2):暴击整体重一档 —— 闷响、更大的震屏、更低的全屏闪阈值。
        /// ke(2026-08-30):相克与暴击**同档**(数值上也都是 ×1.5),但走各自的表达 ——
        /// 暴击是「打得狠」,相克是「打得对」,两者可以同时发生。
        /// target(2026-08-30):受击者,只用来定震屏方向;传 null 退回原来的全随机抖。
        /// 三个参数都有缺省值,DoT 那几个调用点一个字都不用改。</summary>
        private void HitFx(int amount, bool crit = false, bool ke = false, RectTransform target = null)
        {
            bool heavy = crit || ke;
            // 顿帧:小伤害一记轻迟滞,重击封顶,暴击/相克再重一档。
            // 排在最前 —— 时间要在音效和震屏**开始之前**按住,才是「命中卡了一下」而不是「抖完才卡」
            HitStop(heavy ? HitStopBig
                : Mathf.Lerp(HitStopLight, HitStopHeavy, Mathf.Clamp01(amount / 60f)));
            PlayClip(amount >= 30 || heavy ? _thudClip : _hitClip, 0.9f,
                Mathf.Clamp(1.3f - amount / 80f, 0.6f, 1.3f));
            StartCoroutine(Shake(heavy
                ? Mathf.Clamp(10f + amount * 0.5f, 10f, 34f)
                : Mathf.Clamp(4f + amount * 0.35f, 4f, 26f), AttackDir(target)));
            // 全屏微闪的阈值只看暴击,不看相克(2026-08-30):相克太常见 —— 五分之一的属性组合
            // 都吃它,阈值一降就整场都在闪,白光反而不再意味着「这记不一般」。
            // 相克的分量交给金环 + 金字 + 顿帧,那几样是**指向性**的,不抢全屏
            if (amount >= (crit ? 20 : 40)) ScreenFlash(0.12f, Color.white); // 大伤害:一记全屏微闪
        }

        /// <summary>从玩家席指向 target 的单位向量,用作震屏方向(2026-08-30)。
        ///
        /// 为什么值得算:原先震屏是纯 <c>Random.insideUnitCircle</c>,每帧方向乱跳,
        /// 读起来是画面在「糊」而不是被「撞」了一下 —— 同样的振幅,给它一个方向,冲击力差一个量级。
        /// 起点取屏幕中下 0.32,与 <see cref="Popup"/> 玩家侧飘字的锚点同一个口径。
        /// 拿不到目标就回落竖直向上(我方在下、敌方在上,这是最常见的那一记)。</summary>
        private Vector2 AttackDir(RectTransform target)
        {
            if (target == null || _shakeTarget == null) return Vector2.up;
            var area = _shakeTarget.rect;
            Vector3 seat = _shakeTarget.TransformPoint(new Vector3(0f, area.yMin + area.height * 0.32f, 0f));
            Vector2 delta = target.position - seat;
            return delta.sqrMagnitude < 1f ? Vector2.up : delta.normalized;
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
            // ⚠ 取整棵子树,不是 target.GetComponent<Image>()(2026-08-30 修):
            // 分层字怪(MobView)的锚点物件身上**根本没有 Image** —— 各层 Image 都在子节点上,
            // 只看自己会让所有有形象的怪白闪静默失效,而现在场上绝大多数怪都有形象。
            // GreyRoutine 早就为同一个坑改成了 GetComponentsInChildren,当时只修了置灰这一半。
            var images = target.GetComponentsInChildren<Image>(true);
            var original = new Color[images.Length];
            for (int i = 0; i < images.Length; i++) original[i] = images[i].color;

            float t = 0f;
            const float duration = 0.16f;
            while (t < duration && target != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / duration;
                float s = 1f + 0.28f * Mathf.Sin((1f - k) * Mathf.PI); // 更狠冲击
                target.localScale = new Vector3(s, s, 1f);
                for (int i = 0; i < images.Length; i++)                 // 白闪 → 复原
                    if (images[i] != null) images[i].color = FlashOf(original[i], k);
                yield return null;
            }
            if (target != null) target.localScale = Vector3.one;
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null) images[i].color = original[i];
            _flashing.Remove(target);
        }

        /// <summary>白闪色:只推 RGB,alpha 保各层原值 —— 与 <see cref="GreyOf"/> 同一条戒律,
        /// 状态层(L4)的 alpha 编码着战斗状态,一并拉到 1 会让墨雾/火芯在受击瞬间突然全显。</summary>
        private static Color FlashOf(Color original, float k)
        {
            var flash = Color.Lerp(Color.white, original, k);
            flash.a = original.a;
            return flash;
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

        /// <summary>一记伤害的飘字内容。四种组合:平、暴、克、暴克 —— 单独一条方法是因为
        /// 这四种要在**同一个** Popup 调用里选,散在三元表达式里读不出来。</summary>
        private static string DamageText(BattleEvent e)
        {
            if (e.Crit && e.Ke) return Strings.T("juice.popup.ke_crit_damage", ("amount", e.Amount));
            if (e.Crit) return Strings.T("juice.popup.crit_damage", ("amount", e.Amount));
            if (e.Ke) return Strings.T("juice.popup.ke_damage", ("amount", e.Amount));
            return $"-{e.Amount}";
        }

        // ---- 扩散环:一记落点的冲击波(相克 / 引爆 / 飞字砸中)----

        private const float RingDuration = 0.26f;

        /// <summary>从落点炸开的一圈细环,放大 + 淡出。程序生成,无资产。
        ///
        /// 为什么值得有:此前一记命中的全部「落点」表达就是白闪 + 震屏,两者都是**目标身上**的变化,
        /// 画面上没有任何东西说明冲击是从这个点扩散出去的。一圈环把「这里发生了一记」钉在原地。</summary>
        private void Ring(RectTransform at, Color color)
        {
            if (at == null || _shakeTarget == null) return;
            var go = new GameObject("Ring", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(64f, 64f);
            rect.position = at.position;
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(16);
            image.type = Image.Type.Sliced;
            image.fillCenter = false; // 只要边:实心会把目标整个盖住
            image.raycastTarget = false;
            image.color = color;
            StartCoroutine(RingRoutine(rect, image, color));
        }

        private static IEnumerator RingRoutine(RectTransform rect, Image image, Color color)
        {
            float t = 0f;
            while (t < RingDuration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / RingDuration;
                float scale = Mathf.Lerp(0.35f, 2.3f, 1f - (1f - k) * (1f - k)); // ease-out:炸得快、收得慢
                rect.localScale = new Vector3(scale, scale, 1f);
                image.color = new Color(color.r, color.g, color.b, 0.85f * (1f - k));
                yield return null;
            }
            if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
        }

        // ---- 墨散:死亡的一团墨(2026-08-30)----

        // 墨色阶:浓墨 → 淡墨 → 一点朱砂(收尾那点血色),水墨的死法
        private static readonly Color[] InkPalette =
        {
            new Color(0.10f, 0.10f, 0.12f), new Color(0.24f, 0.22f, 0.26f),
            new Color(0.42f, 0.40f, 0.44f), new Color(0.62f, 0.20f, 0.18f),
        };

        /// <summary>怪物咽气时炸开一团墨:碎片向外抛、旋转、边飞边淡。
        ///
        /// 此前死亡的全部表现是「置灰 + 后坐 + 全屏闪」,置灰是个 0.2s 的渐变 —— 一只怪就这么
        /// 悄悄褪色了,分量还不如挨一记普通攻击。复用 <see cref="EmberRoutine"/> 那套即抛即毁的
        /// 程序碎片,不违反第 12 章「不做骨骼/帧动画」。</summary>
        /// <param name="element">死者的属性:墨色里掺它一成,一团墨也就带上了这只怪的底色。
        /// 掺而不是替换 —— 全用属性色就成了「彩色纸屑」,水墨那口气全散了。</param>
        private void InkBurst(RectTransform target, Element? element = null)
        {
            if (target == null || _shakeTarget == null) return;
            var tint = Theme.GlyphColor(element);
            for (int n = 0; n < 12; n++)
            {
                var go = new GameObject("InkSplat", typeof(RectTransform));
                go.transform.SetParent(_shakeTarget, false);
                var rect = (RectTransform)go.transform;
                float side = UnityEngine.Random.Range(7f, 19f);
                rect.sizeDelta = new Vector2(side, side * UnityEngine.Random.Range(0.6f, 1.5f));
                rect.position = target.position;
                var image = go.AddComponent<Image>();
                image.sprite = Theme.Rounded(8);
                image.type = Image.Type.Sliced;
                image.color = element.HasValue
                    ? Color.Lerp(InkPalette[UnityEngine.Random.Range(0, InkPalette.Length)], tint, 0.32f)
                    : InkPalette[UnityEngine.Random.Range(0, InkPalette.Length)];
                image.raycastTarget = false;
                // 均匀铺满一圈再加抖动:纯随机方向会结块,看着像溅在一侧而不是炸开
                float angle = (n / 12f + UnityEngine.Random.Range(-0.04f, 0.04f)) * Mathf.PI * 2f;
                StartCoroutine(InkSplatRoutine(rect, image,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(46f, 96f)));
            }
        }

        private static IEnumerator InkSplatRoutine(RectTransform rect, Image image, Vector2 throwTo)
        {
            Vector2 start = rect.anchoredPosition;
            Color from = image.color;
            float duration = UnityEngine.Random.Range(0.34f, 0.58f);
            float spin = UnityEngine.Random.Range(-220f, 220f);
            float t = 0f;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / duration;
                float eased = 1f - (1f - k) * (1f - k); // 抛出去就减速:墨有阻力,不是弹片
                rect.anchoredPosition = start + throwTo * eased + new Vector2(0f, -26f * k * k); // 尾段微微下坠
                rect.localRotation = Quaternion.Euler(0f, 0f, spin * eased);
                rect.localScale = Vector3.one * (1f - 0.4f * k);
                image.color = new Color(from.r, from.g, from.b, 1f - k * k);
                yield return null;
            }
            if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
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

        /// <summary>出字/合成/拆解那记飞牌的时长与曲线。ease-in = 蓄力后加速**砸**向目标,
        /// 配的是「打出去」这个动作。抽卡是「滑进来」,得用相反的一头,见
        /// <see cref="FlyGlyph"/> 的两个可选参数。</summary>
        private const float CastFlyDuration = 0.22f;

        /// <summary>幽灵字牌从 from 飞到 to(世界坐标),到达后销毁并回调。出字/合成/拆解的过渡表现。
        ///
        /// duration / easeOut 两个可选参数给抽卡动画用(BattleView.DealRoutine),缺省值就是
        /// 出字那一记的原样 —— 加参数而不是改常量:两种动作的手感刻意不同,
        /// 出字要「砸」(ease-in,越飞越快),抽卡要「滑」(ease-out,快进慢停)。</summary>
        public void FlyGlyph(string glyph, Color color, Vector3 from, Vector3 to, Action onArrive = null,
            float duration = CastFlyDuration, bool easeOut = false)
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

            StartCoroutine(FlyRoutine(rect, from, to, onArrive, duration, easeOut, color));
        }

        // 拖尾:每隔这么久落一片残影。0.03 ≈ 每两帧一片(60fps),0.22s 的一记出字大约留 7 片,
        // 够连成一条线又不至于铺满屏
        private const float TrailInterval = 0.03f;

        private IEnumerator FlyRoutine(RectTransform rect, Vector3 from, Vector3 to, Action onArrive,
            float duration, bool easeOut, Color color)
        {
            float t = 0f;
            float nextTrail = 0f;
            if (duration <= 0f) duration = CastFlyDuration; // 防 0 除:传 0 会让 k 变 NaN,牌卡在起点
            // 沿飞行方向的倾角:朝目标「低头」扎过去,而不是端端正正地平移。
            // 只给出字那一记(ease-in = 砸);抽卡是滑进来的,滑着还歪就成了打滑
            float lean = easeOut ? 0f : Mathf.Clamp(Vector3.Distance(from, to) * 0.02f, 6f, 16f);
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // ease-in(k²)= 蓄力后加速砸向目标,出字用;
                // ease-out(1−(1−k)²)= 起步就有速度、临到位收住,抽卡滑入用
                float eased = easeOut ? 1f - (1f - k) * (1f - k) : k * k;
                rect.position = Vector3.Lerp(from, to, eased);
                rect.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(k * Mathf.PI));
                rect.localRotation = Quaternion.Euler(0f, 0f, -lean * eased);
                if (lean > 0f && t >= nextTrail) // 拖尾只给"砸"的那一记,滑入不留
                {
                    nextTrail = t + TrailInterval;
                    Trail(rect, color);
                }
                yield return null;
            }
            if (rect != null)
                UnityEngine.Object.Destroy(rect.gameObject);
            onArrive?.Invoke();
        }

        /// <summary>飞字残影:在当前位置落一片同色底板,原地淡出。只画底板不画字 ——
        /// 一串半透明的字叠在一起会糊成一团黑,只留色块反而读得出是一条轨迹。</summary>
        private void Trail(RectTransform source, Color color)
        {
            if (_shakeTarget == null) return;
            var go = new GameObject("Trail", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = source.sizeDelta;
            rect.position = source.position;
            rect.localRotation = source.localRotation;
            rect.localScale = source.localScale;
            var image = go.AddComponent<Image>();
            image.sprite = Theme.Rounded(12);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            image.color = color;
            StartCoroutine(TrailRoutine(rect, image, color));
        }

        private const float TrailFade = 0.16f;

        private static IEnumerator TrailRoutine(RectTransform rect, Image image, Color color)
        {
            float t = 0f;
            while (t < TrailFade && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / TrailFade;
                image.color = new Color(color.r, color.g, color.b, 0.42f * (1f - k));
                rect.localScale *= 1f - 0.9f * UnityEngine.Time.unscaledDeltaTime; // 边淡边缩,尾巴收得干净
                yield return null;
            }
            if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
        }

        /// <summary>字牌到位弹跳(合成结果/拆出部件落位)。目标可能已被重绘销毁,全程判空。</summary>
        public void PopTile(RectTransform target)
        {
            if (target != null)
                StartCoroutine(PunchRoutine(target));
        }

        // ---- 新到手的牌:持续高亮(2026-08-30) ----

        /// <summary>给一张牌套一圈**呼吸的属性色光晕**,亮到 <paramref name="untilUnscaledTime"/> 为止。
        ///
        /// 为什么是「到某个时刻」而不是「持续 N 秒」:BattleView 是全量重绘的,拆完字随手点一下
        /// 界面,这张牌连同光晕就被 Ui.Clear 销毁了。所以真正记账的是 BattleView 里那张
        /// 「字 → 到期时刻」的表,每次重绘照着表把光晕重新套上 —— 传剩余时长的话,每次重绘都会
        /// 把倒计时重置,牌会一直亮下去。
        ///
        /// 光是 <see cref="Theme.Halo"/> 那张贴图:牌沿一线属性色,向外 10px 洇开到透明,
        /// **牌内全透明**。牌自己的底图挂在 tile 本身上,任何子物体都画在它之上 ——
        /// 实心色板会把牌面整个染成属性色、字形跟着糊掉,而一圈实边(上一版:Rounded +
        /// fillCenter=false)在 56 见方的部件牌上又粗得吃掉大半。发光是唯一不占版面的
        /// 强调方式:牌面、四角、描线一样都不动,只在牌之外加一圈会呼吸的光。
        ///
        /// 呼吸**只改 alpha,不改尺寸** —— 尺寸一动,一排牌就会跟着挤。</summary>
        public void Glow(RectTransform target, Color color, float untilUnscaledTime)
        {
            if (target == null || UnityEngine.Time.unscaledTime >= untilUnscaledTime) return;
            StartCoroutine(GlowRoutine(target, color, untilUnscaledTime));
        }

        // 牌的圆角:字库牌 9、部件牌 12,取中间值 —— 光是模糊的,差这 3px 看不出来,
        // 而多一张贴图就多一次运行时生成
        private const int GlowRadius = 10;
        private const float GlowPeriod = 0.75f; // 呼吸一个来回的时长
        private const float GlowDim = 0.26f;    // 呼吸谷:牌沿那一线的 alpha
        private const float GlowBright = 0.55f; // 呼吸峰

        private static IEnumerator GlowRoutine(RectTransform target, Color color, float until)
        {
            var go = new GameObject("Glow", typeof(RectTransform));
            go.transform.SetParent(target, false);
            go.transform.SetAsFirstSibling(); // 排在最底:字形、徽标都盖在光之上
            var halo = go.AddComponent<Image>();
            halo.sprite = Theme.Halo(GlowRadius);
            halo.type = Image.Type.Sliced;
            halo.fillCenter = false;    // 中心本就全透明,少画一个 quad
            halo.raycastTarget = false; // 不抢牌自己的点击
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            // 外扩量必须与贴图里的 HaloPad 一致 —— 对不上的话,渐变的峰值就不落在牌沿了
            rect.offsetMin = new Vector2(-Theme.HaloPad, -Theme.HaloPad);
            rect.offsetMax = new Vector2(Theme.HaloPad, Theme.HaloPad);

            while (go != null && UnityEngine.Time.unscaledTime < until)
            {
                // 呼吸只改 alpha —— 尺寸一动,一排牌就会跟着挤。
                // 末段随剩余时间整体收暗:光晕是「淡出」而不是「啪地消失」
                float breathe = 0.5f + 0.5f * Mathf.Sin(UnityEngine.Time.unscaledTime / GlowPeriod * Mathf.PI * 2f);
                float remain = Mathf.Clamp01((until - UnityEngine.Time.unscaledTime) / GlowPeriod);
                halo.color = new Color(color.r, color.g, color.b,
                    Mathf.Lerp(GlowDim, GlowBright, breathe) * remain);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ---- 震屏 ----

        /// <summary>震屏。direction 给了就沿那个轴来回撞(2026-08-30),default 回落成原来的全随机抖。
        ///
        /// 有方向的是「撞」,没方向的是「糊」:衰减正弦跑一个半来回,再叠一点小噪声 ——
        /// 纯正弦会读成机械滑动,纯随机又丢掉了这一记是从哪个方向来的。</summary>
        private IEnumerator Shake(float amplitude, Vector2 direction = default)
        {
            // 以固定 home 为基准(不读实时位置):多个 Shake 并发时不会把彼此的偏移当原点累积
            float t = 0f;
            const float duration = 0.22f;
            bool aimed = direction.sqrMagnitude > 0.0001f;
            while (t < duration)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float decay = 1f - t / duration;
                Vector2 offset = aimed
                    ? direction * (Mathf.Sin(t / duration * Mathf.PI * 3f) * amplitude * decay)
                      + UnityEngine.Random.insideUnitCircle * (amplitude * 0.18f * decay)
                    : UnityEngine.Random.insideUnitCircle * (amplitude * decay);
                _shakeTarget.anchoredPosition = _shakeHome + offset;
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

        // ---- 掉血残影(chip damage)----

        private const float ChipHold = 0.18f;   // 停在原处不动的时间:让「刚掉了这么多」看得见
        private const float ChipDrain = 0.34f;  // 之后收回去的时长

        /// <summary>在血条上留一截浅色的尾巴,盖住「刚刚掉掉的那一段」,停一拍再收回去。
        ///
        /// 血条本身是**瞬时**按到新值的(SetHpBar 直接改 anchor),掉 3 点和掉 30 点在画面上
        /// 都只是长度变了一下 —— 玩家读得到数字,却感觉不到分量。这截尾巴把「掉了多少」
        /// 在条上停成一个看得见的量。
        ///
        /// 只占 [to, from] 这一段、画在 Fill 之上:不覆盖剩余血量,也就不用动 Fill 与
        /// BarPulse 的层级关系(<see cref="BarGlowRoutine"/> 按 sibling index 定位)。</summary>
        public void ChipDamage(RectTransform fill, float fromFrac, float toFrac)
        {
            if (fill == null || fill.parent == null) return;
            fromFrac = Mathf.Clamp01(fromFrac);
            toFrac = Mathf.Clamp01(toFrac);
            if (fromFrac - toFrac < 0.002f) return; // 掉得太少,一条尾巴还没一个像素宽

            var go = new GameObject("Chip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(fill.parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = Theme.Rounded(10);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            StartCoroutine(ChipRoutine((RectTransform)go.transform, image, fromFrac, toFrac));
        }

        private static IEnumerator ChipRoutine(RectTransform rect, Image image, float from, float to)
        {
            var color = Theme.Paper;
            float t = 0f;
            while (t < ChipHold + ChipDrain && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                // 停住那一拍整条都在,之后右缘从 from 收到 to —— 收的是「血刚流干」的方向
                float right = t <= ChipHold ? from
                    : Mathf.Lerp(from, to, (t - ChipHold) / ChipDrain);
                Ui.Anchor(rect, new Vector2(to, 0f), new Vector2(right, 1f), Vector2.zero, Vector2.zero);
                image.color = new Color(color.r, color.g, color.b,
                    t <= ChipHold ? 0.85f : 0.85f * (1f - (t - ChipHold) / ChipDrain));
                yield return null;
            }
            if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
        }

        // ---- 伤害飘字 ----

        // 同一锚点上排队的层高与时窗。34 略高于常规飘字的行高(36 号字),叠两三条读得开;
        // 0.55s 略长于飘字自己的 0.7s 前半程 —— 上一条还没飘走,下一条就该往上让
        private const float PopupStackStep = 34f;
        private const float PopupStackWindow = 0.55f;
        // 排到第 4 层封顶(再高就飘出屏幕了)。封顶后**停在顶层**而不是绕回底部 ——
        // 底层是伤害数字的位置,绕回去压住的正是最该读清的那条;停在顶层压住的是更老的标记
        private const int PopupStackMax = 4;

        /// <summary>同一个锚点上短时间内连着飘的字,依次往上让一层(2026-08-30)。
        ///
        /// 「正!」是刻意与致死伤害**同帧**的(要让玩家同时看见「掉了多少」和「死了」),
        /// 破阶、现形、分裂、加攻这些也都紧跟着伤害 —— 原先全都锚在同一个点上、只靠
        /// ±24 的水平随机分开,同帧两条几乎必然叠在一起,叠上了就两条都读不出来。
        ///
        /// 记「上次是什么时候、排到第几层」而不是「当前有几条活着」:飘字自己会飘走、会被
        /// 重绘销毁,数活的就得维护一份注册表并处理销毁回调;而时窗一过自然归零,不用记账。</summary>
        private readonly Dictionary<RectTransform, (float time, int slot)> _popupSlots = new();
        private (float time, int slot) _playerPopupSlot; // 玩家侧锚点是 null,单独记一份

        private int NextSlot(RectTransform anchor)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (anchor == null)
            {
                _playerPopupSlot = Advance(_playerPopupSlot, now);
                return _playerPopupSlot.slot;
            }
            _popupSlots.TryGetValue(anchor, out var state);
            state = Advance(state, now);
            _popupSlots[anchor] = state;
            return state.slot;
        }

        private static (float time, int slot) Advance((float time, int slot) state, float now) =>
            (now, now - state.time > PopupStackWindow
                ? 0
                : Mathf.Min(state.slot + 1, PopupStackMax - 1));

        /// <param name="outline">描边色。相克专用(2026-08-30):飘字本身已经交给五行属性色了,
        /// 「这记打对了属性」得靠别的通道说 —— 一圈金边既不抢属性色,又是全场独一份的信号。</param>
        private void Popup(string text, Color color, RectTransform anchor, bool small = false,
            float sizeScale = 1f, Color? outline = null)
        {
            var go = new GameObject("Popup", typeof(RectTransform));
            go.transform.SetParent(_shakeTarget, false);
            var rect = (RectTransform)go.transform;
            int slot = NextSlot(anchor);
            if (anchor != null)
            {
                rect.position = anchor.position;
                // 第 0 条留一点水平抖动(同一只怪连挨几记时不至于呆板),排到第 1 条起横向收窄 ——
                // 叠起来的几条要读成一列,左右乱跳反而更难认
                float jitter = slot == 0 ? 24f : 8f;
                rect.anchoredPosition += new Vector2(UnityEngine.Random.Range(-jitter, jitter),
                    30f + slot * PopupStackStep);
            }
            else // 玩家侧:屏幕中下
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.32f);
                rect.anchoredPosition = new Vector2(
                    UnityEngine.Random.Range(-60f, 60f), slot * PopupStackStep);
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
            if (outline.HasValue)
            {
                var edge = go.AddComponent<Outline>();
                edge.effectColor = outline.Value;
                edge.effectDistance = new Vector2(2.5f, 2.5f);
                edge.useGraphicAlpha = true; // 跟着字一起淡出,否则字没了金边还悬在那儿
            }

            StartCoroutine(FloatAndFade(rect, label));
        }

        // 飘字三段的分界(占总时长的比例):弹出 → 悬停 → 上浮淡出
        private const float PopupPunch = 0.12f;
        private const float PopupHold = 0.34f;

        private static IEnumerator FloatAndFade(RectTransform rect, Text label)
        {
            float t = 0f;
            const float duration = 0.7f;
            Color from = label.color;
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = t / duration;

                // 弹出:0.4 → 1.15 → 1。匀速淡出的数字看着是「浮上来的」,弹一下才是「打出来的」
                float scale = k < PopupPunch
                    ? Mathf.Lerp(0.4f, 1.15f, k / PopupPunch)
                    : k < PopupHold
                        ? Mathf.Lerp(1.15f, 1f, (k - PopupPunch) / (PopupHold - PopupPunch))
                        : 1f;
                rect.localScale = new Vector3(scale, scale, 1f);

                // 上浮:弹出那一段几乎不动,让数字先站住;悬停之后才加速离场
                float rise = k < PopupPunch ? 10f : Mathf.Lerp(24f, 120f, Mathf.InverseLerp(PopupPunch, 1f, k));
                rect.anchoredPosition += new Vector2(0f, rise * UnityEngine.Time.unscaledDeltaTime);

                // 淡出同样推后:前半段全不透明,数字才读得清
                from.a = k < PopupHold ? 1f : 1f - (k - PopupHold) / (1f - PopupHold);
                label.color = from;
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
