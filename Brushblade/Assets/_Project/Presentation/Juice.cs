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
        private const float TailGap = 0.3f;      // 末次打击到「播完回调」的收尾停顿

        /// <summary>播放一次动作的全部结算表现,全程有序、播完回调。enemyAnchor(i) 返回敌人本体圆
        /// (可为 null);summonAnchor(k) 返回第 k 记召唤反击的发起召唤物(可为 null);onComplete 在
        /// 所有动效落幕后调用(战斗结束标语等它,2026-07-24)。
        /// 召唤反击逐个顺序播、伤害与「正!」分节拍(2026-07-24):此前同帧齐发相互重叠、只见一次。</summary>
        public void Play(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor = null, Action onComplete = null, Action<BattleEvent> onImpact = null,
            Func<int, SummonState> summonInfo = null)
        {
            StartCoroutine(PlayRoutine(events, enemyAnchor, summonAnchor, onComplete, onImpact, summonInfo));
        }

        private IEnumerator PlayRoutine(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action onComplete, Action<BattleEvent> onImpact,
            Func<int, SummonState> summonInfo)
        {
            // 读锚点世界坐标前先结算本帧布局:敌人格挂布局组,新建/重排后同帧读到的是未结算值,
            // DoT/召唤伤害会飘到屏幕中间而非怪物本体(2026-07-24)。
            Canvas.ForceUpdateCanvases();
            _flashing.RemoveWhere(t => t == null); // 上一轮重绘销毁的目标不留在集合里

            // 逐格驱动后(2026-08-15 ATB 改造),每批事件天生属于一个行动者,
            // 不再需要猜段边界 —— 原先靠 SummonAttack / EnemyTurnBegan 划界的三段切分已删除,
            // 段间停顿由 BattleView 的驱动协程控制。
            yield return ApplyBatch(events, enemyAnchor, summonAnchor, onImpact, summonInfo);

            yield return new WaitForSecondsRealtime(TailGap);
            onComplete?.Invoke();                                                       // 关卡胜利标语(外层)
        }

        /// <summary>结算一批事件的表现。受击致死者(紧随伤害的 EnemyDied)当帧内联:飘「正!」+ 立刻置灰,
        /// 与致死伤害同帧、分别显示。onImpact 在每记伤害触达时回调外层(触达才掉血)。
        /// 并行组(Damage 同帧齐出)组末停一拍;串行单位(DoT/敌攻/SummonHit)在下一记前才停一拍,好让致死伤害与正同帧。</summary>
        private IEnumerator ApplyBatch(IReadOnlyList<BattleEvent> events, Func<int, RectTransform> enemyAnchor,
            Func<int, RectTransform> summonAnchor, Action<BattleEvent> onImpact,
            Func<int, SummonState> summonInfo = null)
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
                            yield return new WaitForSecondsRealtime(StepGap);
                        lastDamageTarget = e.TargetIndex;
                        // 暴击(2026-08-12,E-b2):飘「暴」+ 放大一档 + 更重的震屏。
                        // 数值上暴击只是 ×1.5,与相克 ×1.5 长得一模一样 —— 玩家能不能读出
                        // 「这记暴了」全靠这里的表达,不能靠数字大小
                        Popup(e.Crit ? Strings.T("juice.popup.crit_damage", ("amount", e.Amount)) : $"-{e.Amount}", Theme.Cinnabar,
                            enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp((e.Crit ? 1.35f : 1f) + e.Amount / 50f,
                                1f, e.Crit ? 2.4f : 1.9f));
                        if (!kills) HitReact(enemyAnchor(e.TargetIndex)); // 致死不白闪,让位给置灰
                        HitFx(e.Amount, e.Crit);
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
                    // 引爆(2026-08-09,灱):把剩余灼烧层数一次性全打出来再清空,不是一记普通 DoT tick,
                    // 给一记比 BurnTick 更重的震屏,读出「抢杀爆发」的分量——不调 HitFx(它自带的震屏
                    // 上限 26,比这里这条更轻),改成内联 HitFx 的音效/闪光两件事(2026-08-10 复核补):
                    // 打击音 + amount≥40 全屏微闪照抄 HitFx,震屏单用下面这条更重的,避免叠两次。
                    case BattleEventKind.Detonate:
                        Popup(Strings.T("juice.popup.detonate", ("amount", e.Amount)), Theme.Cinnabar, enemyAnchor(e.TargetIndex),
                            sizeScale: Mathf.Clamp(1f + e.Amount / 50f, 1f, 1.9f));
                        _audio.pitch = Mathf.Clamp(1.3f - e.Amount / 80f, 0.6f, 1.3f);
                        _audio.PlayOneShot(e.Amount >= 30 ? _thudClip : _hitClip, 0.9f);
                        _audio.pitch = 1f;
                        StartCoroutine(Shake(Mathf.Clamp(10f + e.Amount * 0.4f, 10f, 30f)));
                        if (e.Amount >= 40) ScreenFlash(0.12f, Color.white);
                        onImpact?.Invoke(e);
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
                            yield return new WaitForSecondsRealtime(FlyDuration); // 等飞牌砸到才结算
                        }
                        break;
                    case BattleEventKind.EnemyDied: // 受击致死:与刚才那记伤害同帧,飘「正!」+ 立刻置灰(分别显示)
                        var dead = enemyAnchor(e.TargetIndex);
                        Popup(Strings.T("juice.popup.kill_mark"), Theme.Ink, dead);
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
                        else if (hpLoss <= 0) Popup(Strings.T("juice.popup.shield_absorbed", ("absorbed", e.Absorbed)), Theme.SplitBlue, null);
                        else Popup(Strings.T("juice.popup.shield_and_hp_loss", ("absorbed", e.Absorbed), ("hpLoss", hpLoss)), Theme.Cinnabar, null, small: true);
                        _audio.PlayOneShot(_thudClip, 0.8f);
                        StartCoroutine(Shake(10f));
                        ScreenFlash(0.14f, Theme.Cinnabar);
                        onImpact?.Invoke(e); // 触达才扣玩家血
                        serialPending = true;
                        break;
                    case BattleEventKind.Burn:
                        Popup(Strings.T("juice.popup.burn_stack", ("amount", e.Amount)), Theme.ShopNav, enemyAnchor(e.TargetIndex), small: true);
                        break;
                    // 召唤物被点燃 / 自身灼烧结算(2026-08-26,灯花「打谁烧谁」)。与上面敌人侧的
                    // Burn / BurnTick 同款演出,只是锚点换成 summonAnchor —— 这两个 Kind 的
                    // TargetIndex 是**召唤物槽位**,喂给 enemyAnchor 会锚到编号相同的那只怪身上。
                    case BattleEventKind.SummonBurn:
                        Popup(Strings.T("juice.popup.burn_stack", ("amount", e.Amount)), Theme.ShopNav,
                            summonAnchor?.Invoke(e.TargetIndex), small: true);
                        break;
                    case BattleEventKind.SummonBurnTick:
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        var burntSummon = summonAnchor?.Invoke(e.TargetIndex);
                        Popup($"-{e.Amount}", Theme.ShopNav, burntSummon,
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
                        _audio.PlayOneShot(_healClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨血条
                        break;
                    // 缺笔妖补全:串行占一拍 —— 它是敌方回合里独立发生的事,
                    // 与那一记攻击挤在同帧就会被当成攻击的一部分
                    case BattleEventKind.Regrow:
                        if (serialPending) yield return new WaitForSecondsRealtime(StepGap);
                        Popup(e.SecondIndex >= 3
                                ? (e.Amount > 0 ? Strings.T("juice.popup.regrow_full_with_heal", ("amount", e.Amount)) : Strings.T("juice.popup.regrow_full"))
                                : (e.Amount > 0 ? Strings.T("juice.popup.regrow_partial_with_heal", ("index", e.SecondIndex), ("amount", e.Amount)) : Strings.T("juice.popup.regrow_partial", ("index", e.SecondIndex))),
                            Theme.Jade, enemyAnchor(e.TargetIndex), small: e.SecondIndex < 3);
                        _audio.PlayOneShot(_healClip, 0.6f);
                        onImpact?.Invoke(e); // 触达才回血
                        serialPending = true;
                        break;
                    case BattleEventKind.Shield:
                        Popup(Strings.T("juice.popup.shield_gain", ("amount", e.Amount)), Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才涨护盾条
                        break;
                    case BattleEventKind.ShieldBroken:
                        Popup(Strings.T("juice.popup.shield_broken", ("amount", e.Amount)), Theme.SplitBlue, null);
                        _audio.PlayOneShot(_shieldClip, 0.7f);
                        onImpact?.Invoke(e); // 触达才把护盾条推到 0(倾覆专用,BattleView.OnImpact 处理)
                        break;
                    case BattleEventKind.EnemySplit:
                        Popup(Strings.T("juice.popup.enemy_split"), Theme.Jade, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.BossPhase:
                        Popup(Strings.T("juice.popup.boss_phase"), Theme.GoldBorder, enemyAnchor(e.TargetIndex));
                        _audio.PlayOneShot(_thudClip, 1f);
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
                        _audio.PlayOneShot(_healClip, 0.5f);
                        break;
                    case BattleEventKind.EnemyRevealed:
                        Popup(Strings.T("juice.popup.enemy_revealed"), Theme.SplitBlue, enemyAnchor(e.TargetIndex));
                        break;
                    case BattleEventKind.ActorActed: // 段首标记,不播(2026-08-16)
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

        /// <summary>一记命中的音效 + 震屏(伤害越高音调越低、震屏越大,封顶);大伤害叠全屏微闪。
        /// crit(2026-08-12,E-b2):暴击整体重一档 —— 闷响、更大的震屏、更低的全屏闪阈值。
        /// 默认 false 让 DoT 那几个调用点一个字都不用改。</summary>
        private void HitFx(int amount, bool crit = false)
        {
            _audio.pitch = Mathf.Clamp(1.3f - amount / 80f, 0.6f, 1.3f);
            _audio.PlayOneShot(amount >= 30 || crit ? _thudClip : _hitClip, 0.9f);
            _audio.pitch = 1f;
            StartCoroutine(Shake(crit
                ? Mathf.Clamp(10f + amount * 0.5f, 10f, 34f)
                : Mathf.Clamp(4f + amount * 0.35f, 4f, 26f)));
            if (amount >= (crit ? 20 : 40)) ScreenFlash(0.12f, Color.white); // 大伤害:一记全屏微闪
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

            StartCoroutine(FlyRoutine(rect, from, to, onArrive, duration, easeOut));
        }

        private static IEnumerator FlyRoutine(RectTransform rect, Vector3 from, Vector3 to, Action onArrive,
            float duration, bool easeOut)
        {
            float t = 0f;
            if (duration <= 0f) duration = CastFlyDuration; // 防 0 除:传 0 会让 k 变 NaN,牌卡在起点
            while (t < duration && rect != null)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // ease-in(k²)= 蓄力后加速砸向目标,出字用;
                // ease-out(1−(1−k)²)= 起步就有速度、临到位收住,抽卡滑入用
                float eased = easeOut ? 1f - (1f - k) * (1f - k) : k * k;
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
