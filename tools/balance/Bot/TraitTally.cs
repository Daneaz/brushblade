using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;

namespace Brushblade.Balance.Bot
{
    /// <summary>本体每执行一步的观察点(<see cref="Search.PlayTurn"/>):<see cref="Before"/> 在执行前(被拒的步也调),
    /// <see cref="After"/> 只在本体受理之后调,此时 <c>battle.LastEvents</c> 就是这一步的事件(结束回合 = 整轮敌方 / 木灵行动)。</summary>
    public interface ITurnObserver
    {
        void Before(BotAction a, BattleEngine battle);
        void After(BotAction a, BattleEngine battle);
    }

    /// <summary>--triggers(Plan F · A2 §3):按特性名计触发,另按字分攻击面 / 五行面出手。只读本体,不改对局。
    ///
    /// Core 没有统一的「特性触发」事件,名字按三条来源拼:
    /// 1. 出字播报 —— <see cref="TraitCueRules.CastAnnounced"/>(Lv5 / 6 / 8 出字时触发的主动特性,扣掉本场已用尽的),执行前取、受理后记;
    /// 2. 事件 —— <see cref="BattleEventKind.GlyphTrait"/> 取那张字 <c>Traits[Amount]</c> 的名;带 <see cref="EffectSource"/> 的事件
    ///    按来源取名(同名 <see cref="EffectKind"/> / 木灵本命字段在图谱里第一条特性的名,与表现层 TraitNameOf 同口径;机制类给固定标签);
    ///    少数只有事件种类的(绿潮 / 秋实 / 保命 / 格挡 / 斩首 / 折桂…)给固定标签;
    /// 3. 丛生分裂 —— 不走特性事件:木灵那一拍里的 <see cref="BattleEventKind.Summon"/>(TrySprout 落小藻)记 <see cref="SproutLabel"/>。
    /// 计数口径:**同一行动段内同名只计一次**(一次出字 / 一个敌人或木灵的一拍算一段),全体溅射 / 多段打不会按目标数放大。
    /// OnKill / OnCrit 这类条件特性与纯被动加成(不发事件、不播报)数不到。</summary>
    public sealed class TraitTally : ITurnObserver
    {
        public const string SproutLabel = "丛生(分裂)";

        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> AttackFaceByChar = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FeatureFaceByChar = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly RecipeGraph _graph;
        private readonly IReadOnlyDictionary<string, int> _levels;
        private readonly Dictionary<EffectSource, string> _sourceLabels = new Dictionary<EffectSource, string>();
        private List<string> _pending;

        /// <summary>机制类来源(不是某条特性,或多条特性共用):固定标签。</summary>
        private static readonly Dictionary<EffectSource, string> MechanismLabels = new Dictionary<EffectSource, string>
        {
            [EffectSource.Overheal] = "溢流", [EffectSource.Thorns] = "盾反(荆棘)", [EffectSource.Reflect] = "反弹",
            [EffectSource.ArmorStrike] = "镇压", [EffectSource.Embers] = "余烬", [EffectSource.SummonDeathHeal] = "归根",
            [EffectSource.CritMorale] = "锋芒", [EffectSource.BlockCounter] = "格挡反击", [EffectSource.ShieldSpend] = "扣盾",
            [EffectSource.SkillCut] = "杜塞", [EffectSource.BurnGrow] = "上炎", [EffectSource.KillDraw] = "斩获",
        };

        public TraitTally(RecipeGraph graph, IReadOnlyDictionary<string, int> cardLevels)
        {
            _graph = graph;
            _levels = cardLevels;
        }

        public void Before(BotAction a, BattleEngine battle)
        {
            _pending = null;
            if (a.Kind != ActionKind.Cast || !_graph.TryGet(a.CharId, out var def)) return;
            _pending = TraitCueRules.CastAnnounced(def, BattleEngine.FaceOf(def, a.AttackMode), CardAppraiser.LevelOf(_levels, a.CharId),
                t => battle.TraitSpentThisBattle(def, t)).Select(t => t.Name).ToList();
        }

        public void After(BotAction a, BattleEngine battle)
        {
            if (a.Kind == ActionKind.Cast && _graph.TryGet(a.CharId, out var def))
            {
                Add(BattleEngine.FaceOf(def, a.AttackMode) == CardFace.Attack ? AttackFaceByChar : FeatureFaceByChar, a.CharId);
                if (_pending != null) foreach (var name in _pending.Distinct()) Add(Counts, name);
            }
            _pending = null;
            Scan(battle.LastEvents, a.Kind == ActionKind.Dismantle || a.Kind == ActionKind.Compose ? a.CharId : null);
        }

        private void Scan(IReadOnlyList<BattleEvent> events, string glyphChar)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var actor = ActorKind.Player;
            foreach (var e in events)
            {
                if (e.Kind == BattleEventKind.ActorActed)
                {
                    seen.Clear();
                    actor = (ActorKind)e.Amount;
                    continue;
                }
                string label = LabelOf(e, glyphChar, actor);
                if (label != null && seen.Add(label)) Add(Counts, label);
            }
        }

        private string LabelOf(BattleEvent e, string glyphChar, ActorKind actor)
        {
            if (e.Source != EffectSource.None) return LabelOf(e.Source);
            switch (e.Kind)
            {
                case BattleEventKind.GlyphTrait:
                    return glyphChar != null && _graph.TryGet(glyphChar, out var def) && e.Amount >= 0 && e.Amount < def.Traits.Count
                        ? def.Traits[e.Amount].Name : "字形特性";
                case BattleEventKind.Summon: return actor == ActorKind.Summon ? SproutLabel : null;
                case BattleEventKind.SummonMerged: return "绿潮";
                case BattleEventKind.PartGranted: return "秋实";
                case BattleEventKind.CharGranted: return "折桂"; // 斩获带 Source KillDraw,走上面那条
                case BattleEventKind.EndureSaved: return "保命";
                case BattleEventKind.BlockUsed: return "格挡";
                case BattleEventKind.DebuffWarded: return "免疫减益";
                case BattleEventKind.BuffBlocked: return "禁增益";
                case BattleEventKind.Behead: return "斩首";
                default: return null;
            }
        }

        /// <summary>来源 → 名:机制类固定标签;否则同名 <see cref="EffectKind"/> 在图谱里第一条特性的名,
        /// 再否则同名木灵本命字段(&gt; 0)的特性名;都取不到为「来源·X」。</summary>
        public string LabelOf(EffectSource source)
        {
            if (_sourceLabels.TryGetValue(source, out var cached)) return cached;
            string label = MechanismLabels.TryGetValue(source, out var fixedLabel) ? fixedLabel
                : TraitNameByKind(source.ToString()) ?? TraitNameByPassive(source.ToString()) ?? $"来源·{source}";
            _sourceLabels[source] = label;
            return label;
        }

        private string TraitNameByKind(string name)
        {
            if (!Enum.TryParse<EffectKind>(name, out var kind)) return null;
            foreach (var def in _graph.All)
                foreach (var t in def.Traits)
                    if (t.Effects.Any(x => x.Kind == kind)) return t.Name;
            return null;
        }

        private string TraitNameByPassive(string name)
        {
            var prop = typeof(SummonPassive).GetProperty(name);
            if (prop == null || prop.PropertyType != typeof(int)) return null;
            foreach (var def in _graph.All)
                foreach (var t in def.Traits)
                    foreach (var x in t.Effects)
                        if (x.Kind == EffectKind.Summon && x.Value == 0 && x.Passive != null && (int)prop.GetValue(x.Passive) > 0)
                            return t.Name;
            return null;
        }

        private static void Add(Dictionary<string, int> map, string key)
        {
            map.TryGetValue(key, out int n);
            map[key] = n + 1;
        }
    }
}
