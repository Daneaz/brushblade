using System;
using System.Linq;

namespace Brushblade.Core
{
    /// <summary>火系专属机制(Plan D2-火):灼操作族(Task 2,附录 N1 / N2 / N4)、灼附着族与焚城(Task 3,N5 / N6)。ApplyEffects 的 switch 只留一行分派,
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
                else CheckBossPhase(ti);
            }
        }
    }
}
