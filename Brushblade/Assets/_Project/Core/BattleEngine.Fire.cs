using System;
using System.Collections.Generic;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>火系专属机制(Plan D2-火):灼操作族(Task 2,附录 N1 / N2 / N4)、灼附着族与焚城(Task 3,N5 / N6)、
    /// 敌人出手前挂点与受击回敬(Task 4,N7 / N8)。ApplyEffects 的 switch 只留一行分派,
    /// 实现放这里,免得那个 switch 继续膨胀。引爆的 retain / portion(N3)改在 <c>Detonate</c> 原处;每击附带(N4b)
    /// 在 DamageSingle 的多段循环里递归调用 ResolveEffect(BattleEngine.cs)。
    ///
    /// 恒等:这些分支只在新 EffectKind / 新字段非缺省时才走到;一律不摇随机数。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>BurnScale(N1,焦土 / 炎炎 / 灿然):目标灼层数 × percent%(向下取整),钳到上限;只升不降,0 层空转。</summary>
        private void ScaleBurnOn(int enemyIndex, int percent, int potency)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive) return;
            int stacks = enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
            if (stacks <= 0) return;
            RaiseBurnTo(enemyIndex, (int)Math.Min(CombatCaps.BurnStacks, (long)stacks * percent / 100), potency);
        }

        /// <summary>BurnEqualize(N2,火烧连营):存活敌人的最高层数 M,每名存活敌人补到 M(0 层也补);全场 0 层空转。
        /// 按调用时的表长遍历(与全体伤害同纪律)。</summary>
        private void EqualizeBurn(int potency)
        {
            int count = _enemies.Count;
            int max = 0;
            for (int i = 0; i < count; i++)
                if (_enemies[i].Alive) max = Math.Max(max, _enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0);
            if (max <= 0) return;
            for (int i = 0; i < count; i++)
                if (_enemies[i].Alive) RaiseBurnTo(i, max, potency);
        }

        /// <summary>把一名敌人的灼补到 <paramref name="stacks"/> 层(不高于现有层数时什么都不做)。火力取
        /// max(原火力, 100, 本字火力)(spec v7 §4,同 ApplyBurn)。发 Burn 事件报实际增量;记进本次出字的上灼名单
        /// (BurnedByThisCast:翻倍 / 拉平也算本字上了灼)。</summary>
        private void RaiseBurnTo(int enemyIndex, int stacks, int potency)
        {
            var enemy = _enemies[enemyIndex];
            var existing = enemy.Statuses.Find(StatusKind.Burn);
            int before = existing?.Magnitude ?? 0;
            // 记进本次出字的上灼名单放在早退之前(D2-火 Task 3 顺带修):满层翻倍 / 已是最高层的拉平没有增量,
            // 但效果确实落在了这名带灼的敌人身上 —— 与 BurnSingle 满层照记的口径一致,干涸等附着据此能挂上
            if (!_cast.BurnedTargets.Contains(enemyIndex)) _cast.BurnedTargets.Add(enemyIndex);
            if (stacks <= before) return;
            ApplyStatus(enemy.Statuses, new StatusEffect
            {
                Kind = StatusKind.Burn, Polarity = StatusPolarity.Debuff,
                Magnitude = stacks, TurnsLeft = -1, Potency = Math.Max(Math.Max(existing?.Potency ?? 0, 100), potency),
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
            int gain = (enemy.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0) - before;
            if (gain > 0) _events.Add(new BattleEvent(BattleEventKind.Burn, enemyIndex, gain));
        }

        // ---- N4 计数缩放(G2):Amplify 读出字前快照(R3,条件类);HealSelf 读结算那一刻(产出量) ----

        /// <summary>出字前每名敌人的灼层数(死者记 0)。只读状态、不摇号。</summary>
        private int[] CapturePreCastBurnStacks()
        {
            var stacks = new int[_enemies.Count];
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive) stacks[i] = _enemies[i].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
            return stacks;
        }

        /// <summary>出字前这名敌人的灼层数。出字之外(快照为 null)读现值;不选敌(−1)/ 出字途中才出现的敌人 = 0。</summary>
        private int PreCastBurnStacksOf(int enemyIndex)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemies.Count) return 0;
            var snap = _cast.PreCastBurnStacks;
            if (snap == null) return _enemies[enemyIndex].Alive ? _enemies[enemyIndex].Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0 : 0;
            return enemyIndex < snap.Length ? snap[enemyIndex] : 0;
        }

        /// <summary>出字前带灼的存活敌人数。</summary>
        private int PreCastBurningEnemies()
        {
            var snap = _cast.PreCastBurnStacks ?? CapturePreCastBurnStacks();
            int n = 0;
            foreach (int s in snap) if (s > 0) n++;
            return n;
        }

        /// <summary>一条带计数缩放的 Amplify 加成项:百分点 × 计数(出字前),cap &gt; 0 时钳到 cap。</summary>
        private int ScaledAmpPercent(int percent, ScaleBasis per, int cap, int enemyIndex)
        {
            long count = per == ScaleBasis.BurnStack ? PreCastBurnStacksOf(enemyIndex) : PreCastBurningEnemies();
            long sum = percent * count;
            if (cap > 0) sum = Math.Min(sum, cap);
            return (int)sum;
        }

        /// <summary>结算那一刻的计数(温润:本字先上的灼也算,G2)。BurnStack = 存活敌人灼层数之和;BurningEnemy = 带灼的存活敌人数。</summary>
        private int CurrentBurnCount(ScaleBasis per)
        {
            int n = 0;
            foreach (var e in _enemies)
            {
                if (!e.Alive) continue;
                int stacks = e.Statuses.Find(StatusKind.Burn)?.Magnitude ?? 0;
                n += per == ScaleBasis.BurnStack ? stacks : stacks > 0 ? 1 : 0;
            }
            return n;
        }

        // ---- Task 3:灼附着族(N5)与焚城(N6)----
        // 附着 = 只挂在带「本次出字所上之灼」的目标上(_cast.BurnedTargets 且身上有灼),同时挂一条隐藏 TraitRider;
        // 灼从目标身上移除(结算到 0 / 引爆 / 蓄热夺火 / 余烬转走)时 DropRiders 按 SourceId + TraitKey 一并移除。
        // 读取点:干涸 → MostWoundedAlly / RegrowOneEnemy;上炎 → ActEnemyTurn 灼前(GrowBurnOn);四火 → SettleBurnOn;
        // 炽焰 → EnemyState.Attack(MinBurn);焚城 → ResolveDefeat(EnqueueBurnBurst)。全部在状态缺省时一次判断即返回,不摇随机数。

        /// <summary>能附着在灼上的效果 Kind(ConfigLoader 与引擎共用)。</summary>
        public static bool CanRideOnBurn(EffectKind kind) => kind switch
        {
            EffectKind.Blind or EffectKind.Weaken
                or EffectKind.HealBlock or EffectKind.BurnGrow or EffectKind.BurnHold
                or EffectKind.BurnBurst or EffectKind.BurnBacklash => true,
            _ => false,
        };

        /// <summary>只能以附着形式出现的 Kind:不写 riderOf 时引擎什么都不做(焚城的结算形态只由引擎入队,不进字表)。</summary>
        public static bool RidesOnly(EffectKind kind) => kind switch
        {
            EffectKind.HealBlock or EffectKind.BurnGrow or EffectKind.BurnHold
                or EffectKind.BurnBurst or EffectKind.BurnBacklash => true,
            _ => false,
        };

        private static StatusKind RiderStatusOf(EffectKind kind) => kind switch
        {
            EffectKind.Blind => StatusKind.Blind,
            EffectKind.Weaken => StatusKind.Curse,
            EffectKind.HealBlock => StatusKind.HealBlock,
            EffectKind.BurnGrow => StatusKind.BurnGrow,
            EffectKind.BurnHold => StatusKind.BurnHold,
            EffectKind.BurnBurst => StatusKind.BurnBurstMark,
            EffectKind.BurnBacklash => StatusKind.BurnBacklashMark,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不能附着在灼上"),
        };

        /// <summary>通用附着(照烟熏写法抽出):目标带本次出字所上之灼时,挂隐藏载体 + 附带状态。
        /// 附带状态保留自己的回合数(Turns &gt; 0,上炎),否则随载体存续(-1)。同字同特性再挂只刷新(G10 四火据此「重新算第一次」)。</summary>
        private void ApplyRider(int enemyIndex, string sourceId, EffectDef effect, int magnitude)
        {
            var bag = _enemies[enemyIndex].Statuses;
            if (!_cast.BurnedTargets.Contains(enemyIndex) || !bag.Has(StatusKind.Burn)) return;
            string riderKey = effect.TraitKey ?? sourceId;
            AttachRider(bag, sourceId, riderKey, StatusKind.Burn);
            ApplyStatus(bag, new StatusEffect
            {
                Kind = RiderStatusOf(effect.Kind), Polarity = StatusPolarity.Debuff,
                Magnitude = RidesOnly(effect.Kind) && effect.Kind != EffectKind.BurnGrow ? 1 : magnitude,
                TurnsLeft = effect.Turns > 0 ? effect.Turns : -1,
                SourceId = sourceId, TraitKey = riderKey, MinBurn = effect.MinBurn,
            }, UnitRef.Enemy(enemyIndex), UnitRef.Player);
        }

        /// <summary>上炎:敌人行动开始、灼结算前 +N 层(多条取最强,§5.2 第 1 律)。火力不变(ApplyBurn 取 max)。</summary>
        private void GrowBurnOn(int enemyIndex)
        {
            var bag = _enemies[enemyIndex].Statuses;
            if (!bag.Has(StatusKind.BurnGrow)) return;
            int grow = bag.MaxMagnitude(StatusKind.BurnGrow);
            if (grow <= 0 || !bag.Has(StatusKind.Burn)) return;
            int gain = ApplyBurn(enemyIndex, grow, UnitRef.Player);
            if (gain > 0) _events.Add(new BattleEvent(BattleEventKind.Burn, enemyIndex, gain));
        }

        /// <summary>焚城入队(ResolveDefeat 调,余烬之前):死者带焚城标记、剩余灼 S &gt; 0 时,每条标记入队一条
        /// BurnBurst S(火力 = 死者灼的火力,pick All)。R4:反应里(TriggerDepth &gt; 0)打死的不入队 —— 焚城的伤害不连锁焚城。
        /// 引爆致死时灼已被清掉,S = 0(同余烬 ①)。多名敌人同时死亡按 ResolveDefeat 的顺序入队、FIFO 结算。</summary>
        private void EnqueueBurnBurst(int enemyIndex)
        {
            var bag = _enemies[enemyIndex].Statuses;
            if (!bag.Has(StatusKind.BurnBurstMark) || TriggerDepth > 0) return;
            var burn = bag.Find(StatusKind.Burn);
            if (burn == null || burn.Magnitude <= 0) return;
            foreach (var mark in bag.All.Where(s => s.Kind == StatusKind.BurnBurstMark).ToList())
                Enqueue(new Reaction(mark.SourceId, Element.Fire,
                    new[] { EffectDef.BurnBurstOf(burn.Magnitude, burn.Potency, mark.TraitKey) }, -1, TriggerDepth + 1));
        }

        /// <summary>焚城结算:对选中的每名存活敌人按灼烧公式扣一次血(层数 × 每层基数 × 火力 × 攻击% × 生克,同 SettleBurnOn),
        /// 不改任何人的层数。属火、发 BurnTick。打死的走 ResolveDefeat(Burn)并当场判胜。</summary>
        private void BurstBurn(EffectDef effect, int targetIndex)
        {
            int stacks = effect.Value;
            if (stacks <= 0) return;
            foreach (int ti in PickTargets(effect, targetIndex))
            {
                var enemy = _enemies[ti];
                if (!enemy.Alive) continue;
                float wuxing = WuxingResolver.KeMultiplier(Element.Fire, enemy.Element);
                int damage = (int)Math.Floor(BurnBaseWithPotency((long)stacks * EnemyBurnPerStack, effect.BurstPotency)
                    * (EffectiveAttack / (double)BattleConfig.AttackBaseline)
                    * wuxing);
                enemy.Hp = Math.Max(0, enemy.Hp - damage);
                RevealDisguise(ti);
                _events.Add(new BattleEvent(BattleEventKind.BurnTick, ti, damage, ke: wuxing > 1f,
                    attacker: Element.Fire, countered: wuxing < 1f));
                if (!enemy.Alive)
                {
                    ResolveDefeat(ti, UnitRef.Player, EffectSource.Burn);
                    CheckWin();
                }
                else if (!AfterEnemyHpLoss(ti)) CheckBossPhase(ti);   // 致命(D2-金 J5)
            }
        }

        // ---- Task 4:敌人出手前挂点(N7,埋雷 / 焚身)与受击回敬(N8,Q23 通用形态)----

        /// <summary>焚身每回合(玩家回合开始清零)的结算次数上限,按载体的特性键计。</summary>
        internal const int BacklashPerTurn = 2;

        /// <summary>受击回敬里允许的效果(ConfigLoader 与管线共用口径):作用于攻击者的**非伤害**敌方侧效果。
        /// 不收伤害类,所以回敬不占 §5.2 第 3 律的 60% 反伤预算;日后要回敬伤害,须先把它接进那份预算再放进来。
        /// 灼 / 流血这类 DOT 之后每回合结算出来的伤害也**不进**反伤预算:预算只管「受击那一刻折返的伤害」(镜 + 格挡反击)。</summary>
        public static bool RetaliateAllows(EffectKind kind) => kind switch
        {
            EffectKind.BurnSingle or EffectKind.Bleed or EffectKind.Weaken or EffectKind.Blind
                or EffectKind.ArmorBreak or EffectKind.Vulnerable or EffectKind.Slow or EffectKind.Freeze => true,
            _ => false,
        };

        /// <summary>埋雷:给选中的敌人挂 Mine(Magnitude = 出字时按攻击力定死的伤害,同流血的快照语义;同源取大,ApplyStatus)。</summary>
        private void PlantMine(EffectDef effect, int value, int targetIndex, string sourceId)
        {
            foreach (int ti in PickTargets(effect, targetIndex))
            {
                if (!OnlyIfMet(effect, ti) || !_enemies[ti].Alive) continue;
                ApplyStatus(_enemies[ti].Statuses, new StatusEffect
                {
                    Kind = StatusKind.Mine, Polarity = StatusPolarity.Debuff,
                    Magnitude = ScaleByAttack(value), TurnsLeft = -1, SourceId = sourceId, TraitKey = effect.TraitKey,
                }, UnitRef.Enemy(ti), UnitRef.Player);
            }
        }

        /// <summary>受击回敬:给玩家挂 Retaliate(本回合有效,TurnsLeft 1 = 玩家下回合开始到期,同 DamageCut)。
        /// OnHit 记**未缩放**的效果(OpeningEffect 形态,可进存档 JSON),触发时按来源字等级结算;Magnitude = 每回合上限(离散)。</summary>
        private void ArmRetaliate(EffectDef effect, string sourceId, Element element)
        {
            if (effect.PerHit.Count == 0) return;
            ApplyStatus(_playerStatuses, new StatusEffect
            {
                Kind = StatusKind.Retaliate, Polarity = StatusPolarity.Buff,
                Magnitude = Math.Max(0, effect.Value), TurnsLeft = 1, SourceId = sourceId, TraitKey = effect.TraitKey,
                OnHit = effect.PerHit.Select(e => OpeningEffect.Of(e, sourceId, element)).ToList(),
            }, UnitRef.Player, UnitRef.Player);
        }

        /// <summary>我方(玩家 / 召唤物)被敌人 <paramref name="enemyIndex"/> 的挥击命中:每条 Retaliate 各入队一条反应,
        /// 目标 = 攻击者,在下一个安全点(该敌人这次动作之后)兑现。上限按特性键每回合计(0 = 不限)。
        /// 没有 Retaliate 时一次判断即返回(恒等)。回敬效果不含伤害(ConfigLoader 白名单),不占 §5.2 第 3 律的 60% 反伤预算。
        /// 每回合计数在**入队时**就扣:攻击者若在兑现前死了(镜反弹、格挡反击打死),反应落空,计数照样用掉。</summary>
        private void EnqueueRetaliation(int enemyIndex)
        {
            if (!_playerStatuses.Has(StatusKind.Retaliate)) return;
            foreach (var s in _playerStatuses.All.Where(x => x.Kind == StatusKind.Retaliate).ToList())
            {
                if (s.OnHit == null || s.OnHit.Count == 0) continue;
                if (s.Magnitude > 0 && !TryUseTrait(RetaliateUseKey(s), perTurn: s.Magnitude, perBattle: 0))
                    continue;
                // 特性键随效果带过去(G11):回敬挂的减攻 / 致盲与本体分开计时
                var effects = s.OnHit.Select(o => s.TraitKey == null ? o.ToEffect() : o.ToEffect().With(traitKey: s.TraitKey)).ToList();
                Enqueue(new Reaction(s.SourceId, s.OnHit[0].Element, effects, enemyIndex, TriggerDepth + 1));
            }
        }

        /// <summary>回敬每回合次数阀的键(结算与 <see cref="RetaliateChargesLeft"/> 共用,两边各拼一份必然漂)。</summary>
        internal static string RetaliateUseKey(StatusEffect s) => "回敬:" + (s.TraitKey ?? s.SourceId);

        /// <summary>这一条回敬本回合还剩几次;不限次数(Magnitude 0)返回 null。只读。</summary>
        private int? ChargesLeftOf(StatusEffect s)
        {
            if (s.Magnitude <= 0) return null;
            _traitUsesThisTurn.TryGetValue(RetaliateUseKey(s), out int used);
            return Math.Max(0, s.Magnitude - used);
        }

        private IEnumerable<StatusEffect> LiveRetaliates() =>
            _playerStatuses.All.Where(s => s.Kind == StatusKind.Retaliate && s.OnHit != null && s.OnHit.Count > 0);

        /// <summary>回敬 chip 该不该出(StatusChipsFire 稿):玩家身上有回敬、且至少一条还能触发(不限次数,或本回合还有剩余)。
        /// 用完的那条不起作用,chip 隐藏(同格挡用完)。状态只挂在玩家身上,表现层据此给玩家栏与每只召唤物都画。</summary>
        public bool RetaliateArmed => LiveRetaliates().Any(s => ChargesLeftOf(s) is not 0);

        /// <summary>回敬 chip 上的数字:有每回合上限时 = 剩余次数(多条取最大 —— 任一条还能触发,下一次受击就会回敬);
        /// 有任一条不限次数或没有生效的回敬时返回 null(不带数字)。</summary>
        public int? RetaliateChargesLeft()
        {
            int? best = null;
            foreach (var s in LiveRetaliates())
            {
                int? left = ChargesLeftOf(s);
                if (left == null) return null;
                if (left > 0 && (best == null || left > best)) best = left;
            }
            return best;
        }

        /// <summary>标记(Vulnerable)增伤:多个来源只取最强的一份,整数取整;无标记时原样返回。
        /// DamageEnemy 与 <see cref="MineHpLoss"/> 共用。</summary>
        private static int ApplyMark(EnemyState enemy, int damage)
        {
            int markPercent = 0;
            foreach (var mark in enemy.Statuses.All)
                if (mark.Kind == StatusKind.Vulnerable && mark.Magnitude > markPercent) markPercent = mark.Magnitude;
            return markPercent > 0 ? damage * (100 + markPercent) / 100 : damage;
        }

        /// <summary>埋雷血条预扣段(StatusChipsFire 稿):按当前状态,身上的地雷下次攻击前会扣掉多少血。
        /// 与 <see cref="SettlePreStrikeHooks"/> → DamageEnemy 同口径:每颗各炸一次,心属性(生克 1.0×)、无视护甲、吃标记、
        /// 护盾先吸收(按顺序消耗),结果封顶到当前生命(= 整截斜纹 = 出手前就会被炸死)。
        /// 冰滞易伤不计:Boss 那一拍开头先移除冰滞再出手,地雷炸时它已经不在了。灼烧 / 流血在爆炸之前结算,这里不预测。</summary>
        public int MineHpLoss(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (!enemy.Alive || !enemy.Statuses.Has(StatusKind.Mine)) return 0;
            int shield = enemy.Shield, lost = 0;
            bool bossDoom = enemy.IsBoss && enemy.Statuses.Has(StatusKind.Doom);   // 致命 · Boss 版:第一颗 ×2(同 DamageEnemy)
            foreach (var mine in enemy.Statuses.All)
            {
                if (mine.Kind != StatusKind.Mine || mine.Magnitude <= 0) continue;
                int damage = ApplyMark(enemy, WuxingResolver.ResolveEffect(mine.Magnitude, Element.Heart, enemy.Element));
                if (bossDoom) { damage *= 2; bossDoom = false; }
                int absorbed = Math.Min(shield, damage);
                shield -= absorbed;
                lost += damage - absorbed;
            }
            return Math.Min(enemy.Hp, lost);
        }

        /// <summary>这次动作算不算「攻击」(G4):普攻与 Boss 技能释放都算;Boss 开始蓄力的那一拍不出手,不算。
        /// 只读,不改蓄力计数(与 <c>ResolveBossTurn</c> 同判据)。</summary>
        private bool StrikesThisAction(EnemyState enemy)
        {
            if (!enemy.IsBoss || enemy.IsCharging) return true;
            var skill = enemy.Def.Phases[enemy.PhaseIndex].Skill;
            if (skill == BossSkill.None || skill == BossSkill.Bulwark) return true;
            return enemy.ChargeCounter + 1 < _config.BossChargeEvery;
        }

        /// <summary>出手前挂点(N7):ActOneEnemy 每次动作开头调。先炸地雷(每颗各炸一次、移除;心属性、无视护甲),
        /// 再结算焚身(一次灼烧结算,正常减层;多条载体只结算一次,按载体的特性键每回合 <see cref="BacklashPerTurn"/> 次)。
        /// R4:整段抬一层 TriggerDepth —— 这里打死的不触发焚城等死亡类被动(EnqueueBurnBurst / 击杀时特性都按深度跳过)。
        /// 返回 false = 出手者死了:排空反应、判胜,调用方跳过这次出手。两种状态都没有时一次判断即返回(恒等)。</summary>
        private bool SettlePreStrikeHooks(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            var bag = enemy.Statuses;
            if (!bag.Has(StatusKind.Mine) && !bag.Has(StatusKind.BurnBacklashMark)) return true;
            if (!StrikesThisAction(enemy)) return true;
            EnterTrigger();
            try
            {
                foreach (var mine in bag.All.Where(s => s.Kind == StatusKind.Mine).ToList())
                {
                    if (!enemy.Alive) break;
                    bag.RemoveEntry(mine);
                    if (mine.Magnitude > 0)
                        DamageEnemy(enemyIndex, mine.Magnitude, Element.Heart,
                            bypassDefense: true, allowBarb: false,
                            source: EffectSource.Mine, attackerRef: UnitRef.Player);
                }
                if (enemy.Alive && bag.Has(StatusKind.Burn))
                    foreach (var mark in bag.All.Where(s => s.Kind == StatusKind.BurnBacklashMark).ToList())
                    {
                        if (!TryUseTrait("焚身:" + (mark.TraitKey ?? mark.SourceId), perTurn: BacklashPerTurn, perBattle: 0)) continue;
                        SettleBurnOn(enemyIndex);
                        break;
                    }
            }
            finally { ExitTrigger(); }
            if (enemy.Alive) return true;
            DrainReactions();
            CheckWin();
            return false;
        }

        // ---- Task 5:其余单点效果(N9 追加一击、N10 解冻 / 自损、N11 揭示)----

        /// <summary>追加一击(N9,星火 / 烈焚):伤害 = 本体伤害基数(TraitRules.BodyDamageOf,吃等级与 L3)× Value% × 攻击力,
        /// 按来源字元素走 DamageEnemy(过生克 / 护甲)。发数:PerBurningHit = 本次出字命中过、出字前带灼的敌人数(0 则整条跳过,
        /// 一个随机数都不摇),否则 1。每发各自选目标(Random 走 _traitRandom);目标已死 / 不满足条件则这一发作罢,
        /// 暴击(_random)只在真的出手时摇。追加的一击不进 HitTargets、不触发暴击时特性。
        ///
        /// 基数口径(Ruling 8,附录「本体基数」):只含本体伤害(卡等级 + L3 元素加成 + 攻击力),**不含**士气释放的百分比、
        /// 本体的 AmpTerms 与 DoubleVs —— 追加一击是「再打一次本体的 N%」,不是复制本体那一击的全部加成。
        ///
        /// 击杀口径(终审 Important 2):出字内(星火 / 烈焚,TriggerDepth 0)的追加一击算出字本身 —— 打死带焚城载体的敌人
        /// 会让焚城入队、击杀时特性照常触发;R4 只拦反应与出字之外的伤害(埋雷、焚身、连爆打死的不触发)。不会死循环:
        /// 反应里的 ExtraStrike 按 0 结算(Task 5 Ruling 2),焚城的伤害也不连锁焚城。
        /// 测试:FireExclusiveDataTests.Fen_FierceBurn_InCastExtraStrikeKill_TriggersBurningCityOnce。</summary>
        private void ExtraStrike(EffectDef effect, int targetIndex, CharDef def, bool attackMode, int cardLevel, Element attacker)
        {
            int strikes = 1;
            if (effect.PerBurningHit)
            {
                strikes = 0;
                foreach (int i in _cast.HitTargets) if (PreCastBurnStacksOf(i) > 0) strikes++;
            }
            if (strikes == 0 || effect.Value <= 0) return;
            int body = MetaRules.ScaleEffectValue(EffectKind.DamageSingle, TraitRules.BodyDamageOf(EffectsOf(def, attackMode), def), cardLevel);
            body = ApplyElementPercent(body, ElementPercentOf(attacker), EffectKind.DamageSingle);
            int damage = ScaleByAttack(body * effect.Value / 100);
            if (damage <= 0) return;
            for (int n = 0; n < strikes; n++)
            {
                var picked = PickTargets(effect, targetIndex);
                if (picked.Count == 0) continue;
                int ti = picked[0];
                if (!_enemies[ti].Alive || !OnlyIfMet(effect, ti)) continue;
                DamageEnemy(ti, damage, attacker, crit: RollCrit(), attackerRef: UnitRef.Player);
            }
        }

        /// <summary>解冻(N10,水火相激):移除冻结(按「结束」挂等长霜抗,R1)、负的 SpeedModifier(减速;正的是加速,不动)、
        /// 冰滞(照自然结束给霜抗 N+1,R1b)。冰缚的标记不跟着移除。都没有时空转。</summary>
        private void ThawOn(int enemyIndex)
        {
            var bag = _enemies[enemyIndex].Statuses;
            var freeze = bag.Find(StatusKind.Freeze);
            if (freeze != null)
            {
                bag.Remove(StatusKind.Freeze);
                if (freeze.Magnitude > 0)
                    ApplyStatus(bag, new StatusEffect
                    {
                        Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = freeze.Magnitude,
                    }, UnitRef.Enemy(enemyIndex), UnitRef.None);
            }
            foreach (var slow in bag.All.Where(x => x.Kind == StatusKind.SpeedModifier && x.Magnitude < 0).ToList())
                bag.RemoveEntry(slow);
            var stall = bag.Find(StatusKind.IceStall);
            if (stall != null)
            {
                bag.RemoveEntry(stall);
                ApplyStatus(bag, new StatusEffect
                {
                    Kind = StatusKind.FrostResist, Polarity = StatusPolarity.Buff, TurnsLeft = stall.Magnitude + 1,
                }, UnitRef.Enemy(enemyIndex), UnitRef.None);
            }
        }

        /// <summary>自损(N10b,玉石俱焚,G9):失去 ⌊当前生命 × percent%⌋,至少留 1 点;不走护盾 / 护甲,不发 PlayerHit(R4 不算受击),
        /// 但照常检查 50% 阈值(阈值不是受击)。</summary>
        private void PaySelfCost(int percent)
        {
            int loss = Math.Min((int)((long)PlayerHp * percent / 100), PlayerHp - 1);
            if (loss <= 0) return;
            int hpBefore = PlayerHp;
            PlayerHp -= loss;
            CheckThreshold(UnitRef.Player, hpBefore, PlayerHp, _config.PlayerMaxHp);
        }

        /// <summary>揭示(N11,光耀):通假字现形;生僻字直接被读懂。两者都发 EnemyRevealed;普通敌人 / 已揭示过的空转。</summary>
        private void RevealOn(int enemyIndex)
        {
            var enemy = _enemies[enemyIndex];
            if (enemy.Def.Ability == EnemyAbility.Obscure && enemy.ApparentElement == null)
            {
                enemy.ApparentElement = enemy.Element;
                _events.Add(new BattleEvent(BattleEventKind.EnemyRevealed, enemyIndex, 0));
            }
            else RevealDisguise(enemyIndex);
        }
    }
}
