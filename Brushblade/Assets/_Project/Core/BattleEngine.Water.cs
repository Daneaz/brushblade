using System;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>水系专属机制(Plan D2-水)。Task 1 = 共用小扩展的敌方侧:E15 选择器(Column / AdjacentOne / HighestHp /
    /// SlowedByThisCast)、E16 名单类推迟、E17 冻结的条件回合与冰滞后退、E18 减速只续不挂、E25 仅在减速中的种。
    /// ApplyEffects 的分支只留一行调用,实现放这里。
    ///
    /// 恒等:这些分支只在新字段 / 新选择器出现时才改变结果;一律不摇随机数。trace 必经的 Slow 分支只多一次名单记录。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>名单类效果(E16):选择器读「本次出字冻结 / 减速过的敌人」,要推迟到出字效果循环末尾才收得全。</summary>
        private static bool IsRosterPick(EffectDef effect) =>
            effect.Pick == EffectPick.FrozenByThisCast || effect.Pick == EffectPick.SlowedByThisCast;

        /// <summary>选择器 SlowedByThisCast 的名单(去重,按施加顺序)。</summary>
        private void RecordSlowed(int enemyIndex)
        {
            if (!_cast.SlowedTargets.Contains(enemyIndex)) _cast.SlowedTargets.Add(enemyIndex);
        }

        /// <summary>AdjacentOne(E15,坚冰):主目标同排左右中第一个存活者,先左后右(同侧按下标);Freeze 再滤掉冻结中 / 霜抗 / 冰滞的
        /// (R1:那些挂不上)。没有返回 −1。不摇号。</summary>
        private int AdjacentOneOf(EffectDef effect, int primary)
        {
            if (primary < 0 || primary >= _enemies.Count) return -1;
            var p = _enemies[primary];
            int left = -1, right = -1;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (i == primary || !e.Alive || !e.SharesRow(p)) continue;
                if (effect.Kind == EffectKind.Freeze && (e.Statuses.Has(StatusKind.Freeze)
                        || e.Statuses.Has(StatusKind.FrostResist) || e.Statuses.Has(StatusKind.IceStall)))
                    continue;
                if (left < 0 && e.ColumnEnd == p.Column) left = i;
                else if (right < 0 && p.ColumnEnd == e.Column) right = i;
            }
            return left >= 0 ? left : right;
        }

        /// <summary>HighestHp(E15,浩瀚):当前生命最高的存活敌人,同值取下标小;没有存活敌人 −1。</summary>
        private int HighestHpEnemy()
        {
            int best = -1;
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive && (best < 0 || _enemies[i].Hp > _enemies[best].Hp)) best = i;
            return best;
        }

        /// <summary>冻结的条件回合加成(E17b,冰冻三尺):目标出字前满足 BonusIf 时 + BonusTurns;缺省 0。</summary>
        private int FreezeBonusTurns(EffectDef effect, int enemyIndex) =>
            effect.BonusTurns > 0 && PreCastConditionMet(effect.BonusIf, enemyIndex) ? effect.BonusTurns : 0;

        /// <summary>只续不挂的减速(E18,倾盆):目标已有的减速(负 SpeedModifier,R1 合并后通常只有一条)TurnsLeft + turns;
        /// 没有减速返回 false(空转)。不经 ApplyStatus:状态没有新挂,只是续时。</summary>
        private bool ExtendSlow(int enemyIndex, int turns)
        {
            bool extended = false;
            foreach (var s in _enemies[enemyIndex].Statuses.All)
            {
                if (s.Kind != StatusKind.SpeedModifier || s.Magnitude >= 0 || s.TurnsLeft < 0) continue;
                s.TurnsLeft += turns;
                extended = true;
            }
            return extended;
        }

        // ---- 我方侧(E20 / E22 / E15 AllAllies)----

        /// <summary>净化一个状态袋:count &gt; 0 只清前 count 条,0 = 全清;返回实际清掉的条数(濯身计数,E22b)。</summary>
        private static int CleanseBag(StatusBag bag, int count) =>
            count > 0 ? bag.RemoveFirst(StatusPolarity.Debuff, count) : bag.RemoveAll(StatusPolarity.Debuff);

        /// <summary>出字前的泉层数(E22a);出字之外读现值。</summary>
        private int PreCastWellspringStacks() =>
            _cast.PreCastWellspring ?? _playerStatuses.TotalMagnitude(StatusKind.Wellspring);

        /// <summary>治疗改形为全体(E20,Q8):玩家 + 全部存活木灵各治「放大值 × percent%」(各自溢流,复用 HealPlayerAndSummons);
        /// 泉只攒一份名义值(基数 × percent%,与治疗弹射每跳的口径一致);名义治疗量(沐恩)也只记一份。percent == 100 不做乘除。</summary>
        private void HealEveryAlly(int healBase, int amplified, int percent)
        {
            int share = percent == 100 ? amplified : amplified * percent / 100;
            GainWellspring(percent == 100 ? healBase : healBase * percent / 100);
            HealPlayerAndSummons(share);
            _cast.HealNominal += share;
        }

        /// <summary>种的回合数(E25):写了 turns 用 turns;仅在减速中且缺 turns 时 = 施加时目标的减速剩余回合;其余 0(调用方兜 1)。</summary>
        private int SeedTurnsOf(EffectDef effect, int enemyIndex)
        {
            if (effect.Turns > 0 || !effect.WhileSlowed) return effect.Turns;
            int left = 0;
            foreach (var s in _enemies[enemyIndex].Statuses.All)
                if (s.Kind == StatusKind.SpeedModifier && s.Magnitude < 0) left = Math.Max(left, s.TurnsLeft);
            return left;
        }

        // ---- Task 2:冻结族(附录 W1 冻结载体附着、W2 冷却)----
        // 附着走 D2-火 的通用件(ApplyRider / DropRiders),载体 = Freeze、名单 = _cast.FrozenTargets(Boss 的冰滞不算,Q3)。
        // 冻结的两个结束点(ActEnemyTurn 冻结分支 tick 后自然到期、ThawOn 解冻)都在霜抗挂上之后调 OnFreezeEnd;
        // 敌人死亡不是「冻结结束」,附着随尸体留在袋子里、不再结算。全部在没有附着时一次判断即返回,不摇随机数(trace 必经)。

        /// <summary>冻结结束(自然到期 / 被解冻,霜抗已按现状挂上):先结算寒彻(每条一次伤害)、再挂冰水的减速(Q19:霜抗在前、减速在后,
        /// 两者并存),最后 DropRiders(Freeze) 把挂在冻结上的附着(含怀山)一并移除。没有附着载体时一次判断即返回(恒等)。</summary>
        private void OnFreezeEnd(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            var bag = enemy.Statuses;
            if (!bag.Has(StatusKind.TraitRider)) return;
            FreezeRiderStrikes(enemyIndex, StatusKind.ThawStrike);
            if (enemy.Alive)
                foreach (var thawSlow in bag.All.Where(s => s.Kind == StatusKind.ThawSlow).ToList())
                {
                    if (thawSlow.Magnitude <= 0) continue;
                    ApplyStatus(bag, new StatusEffect
                    {
                        Kind = StatusKind.SpeedModifier, Polarity = StatusPolarity.Debuff,
                        Magnitude = -50, TurnsLeft = thawSlow.Magnitude, SourceId = thawSlow.SourceId,
                    }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
                }
            DropRiders(bag, StatusKind.Freeze);
        }

        /// <summary>怀山:敌人行动开始(种之后、冻结跳过之前;含被冻结跳过的那拍)仍处于冻结就结算一次。没有 FrostBite 时一次判断即返回。</summary>
        private void SettleFrostBite(int enemyIndex)
        {
            var bag = _enemies[enemyIndex].Statuses;
            if (!bag.Has(StatusKind.FrostBite) || !bag.Has(StatusKind.Freeze)) return;
            FreezeRiderStrikes(enemyIndex, StatusKind.FrostBite);
        }

        /// <summary>冻结附着的伤害(Q18):每条 <paramref name="kind"/> 各打一次 Magnitude(出字时按本体 × N% × 卡等级 × 攻击力定死)。
        /// 水属性、过生克与护甲、吃标记(DamageEnemy 全套)、不暴击、不算挥击(allowBarb: false)、source = FreezeRider。
        /// 整段抬一层 TriggerDepth(R4):这里打死的不触发死亡 / 受击类特性被动。致命(AfterEnemyHpLoss)在 DamageEnemy 里照走。</summary>
        private void FreezeRiderStrikes(int enemyIndex, StatusKind kind)
        {
            var enemy = _enemies[enemyIndex];
            var strikes = enemy.Statuses.All.Where(s => s.Kind == kind && s.Magnitude > 0).ToList();
            if (strikes.Count == 0) return;
            EnterTrigger();
            try
            {
                foreach (var s in strikes)
                {
                    if (!enemy.Alive) break;
                    DamageEnemy(enemyIndex, s.Magnitude, Element.Water, allowBarb: false,
                        source: EffectSource.FreezeRider, attackerRef: UnitRef.Player);
                }
            }
            finally { ExitTrigger(); }
        }

        /// <summary>冷却(W2,Q5):只对 Boss。未蓄力 → ChargeCounter −beats(可为负);蓄力中 → 撤回蓄力、ChargeCounter = BossChargeEvery − beats
        /// (下一拍 ResolveBossTurn 重新蓄力并重发 BossCharging,再下一拍释放)。坚壁 / 无技能阶段照推计数。
        /// 每 Boss 每场 1 次(R1b,次数阀按敌人下标计);小怪空转、不占次数。</summary>
        private void DelayBossCharge(int enemyIndex, int beats)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.IsBoss || !enemy.Alive || beats <= 0) return;
            if (!TryUseTrait("冷却:" + enemyIndex, perTurn: 0, perBattle: 1)) return;
            if (enemy.IsCharging)
            {
                enemy.IsCharging = false;
                enemy.ChargeCounter = _config.BossChargeEvery - beats;
            }
            else enemy.ChargeCounter -= beats;
        }

        // ── 拦截族(D2-水 Task 3,附录 W3 / W4)。ApplyStatus 开头的拦截段只在袋里带 BuffBlock / DebuffWard 时走到这里 ──

        /// <summary>洗尽铅华(W3):挂 BuffBlock(Debuff 极性,自身不被它拦、也不被驱散)。回合按该敌人行动递减(ActEnemyTurn
        /// 每拍末尾 / 冻结跳过那拍都 TickTurns),「2 回合」= 2 次行动。同源刷新(bag.Apply 覆盖)。</summary>
        private void ApplyBuffBlock(int enemyIndex, EffectDef effect, string sourceId) =>
            ApplyStatus(_enemies[enemyIndex].Statuses, new StatusEffect
            {
                Kind = StatusKind.BuffBlock, Polarity = StatusPolarity.Debuff,
                TurnsLeft = Math.Max(1, effect.Turns), SourceId = sourceId, TraitKey = effect.TraitKey,
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);

        /// <summary>免疫减益(W4):落在我方(玩家 / 木灵)身上的这条减益,若袋里有能拦它的 DebuffWard(WardOf 为 null 或等于其 Kind,
        /// 按袋序取第一条)就拦下并返回 true —— 调用方随即返回 false、不写袋子、不发 StatusApplied。
        /// 层数:灼按「钳位后的新总层数 − 现有层数」计(RefreshBurn / ApplyBurn 传进来的 Magnitude 都是总层数);增量 ≤ 0 时
        /// 这次施加本来就不涨层,不算拦截、不耗次数、不给盾(照常写袋子,与改动前逐位一致)。其余减益一次 = 1 条。
        /// 次数:WardCount &gt; 0 时 −1,减到 0 移除这条。转盾:Magnitude × 层 / 条,给被保护的单位(玩家普通桶 / 木灵),发 Shield 事件。</summary>
        private bool WardOff(StatusBag bag, StatusEffect effect, UnitRef target)
        {
            // 落在木灵身上而木灵已不在 / 已死(灯花一击打死带浇熄的木灵):不拦截,照常写袋(与改动前一致)——
            // 护盾只给被保护的那只木灵,绝不落到玩家身上(review fix 1)
            bool onSummon = target.Side == UnitSide.Summon;
            if (onSummon && (target.Index < 0 || target.Index >= SummonCap
                    || _summons[target.Index] == null || !_summons[target.Index].Alive))
                return false;

            StatusEffect ward = null;
            foreach (var s in bag.All)
                if (s.Kind == StatusKind.DebuffWard && (s.WardOf == null || s.WardOf == effect.Kind)) { ward = s; break; }
            if (ward == null) return false;

            int units = 1;
            if (effect.Kind == StatusKind.Burn)
            {
                units = Math.Min(effect.Magnitude, CombatCaps.BurnStacks) - (bag.Find(StatusKind.Burn)?.Magnitude ?? 0);
                if (units <= 0) return false;
            }
            if (ward.WardCount > 0 && --ward.WardCount == 0) bag.RemoveEntry(ward);

            int shield = ward.Magnitude * units;
            if (shield <= 0) return true;
            if (onSummon)
                _events.Add(new BattleEvent(BattleEventKind.Shield, target.Index, AddSummonShield(target.Index, shield)));
            else
                _events.Add(new BattleEvent(BattleEventKind.Shield, Targeting.PlayerTarget, AddPlayerShield(shield, persist: false)));
            return true;
        }
    }
}
