using System.Text;
using Brushblade.Core;

namespace Brushblade.Presentation
{
    /// <summary>字卡简述:从定义机械生成(拼音/释义/属性/稀有度/AP/效果/配方/相生)。</summary>
    public static class CharInfo
    {
        /// <summary>cardLevel:局外卡等级,效果数值按 MetaRules.ScaleByCardLevel 缩放后显示,
        /// 与战斗结算取同一函数(2026-07-20:此前恒显示基础值,升级看不出变化)。</summary>
        public static string Summary(CharDef def, RecipeGraph graph, int cardLevel = 1)
        {
            var text = new StringBuilder();
            text.Append('「').Append(def.Id).Append('」');
            if (def.Pinyin != null)
                text.Append(def.Pinyin).Append(' ');
            if (!string.IsNullOrEmpty(def.Gloss))
                text.Append(def.Gloss).Append('|');
            text.Append(RarityName(def.Rarity)).Append('·')
                .Append(def.Element is { } element ? ElementName(element) + "系" : "中性")
                .Append('·').Append(def.ApCost).Append("AP");

            if (!def.IsLeaf)
                text.Append("|配方:").Append(string.Join("+", def.Recipe));

            if (cardLevel > 1)
                text.Append("|Lv.").Append(cardLevel);

            text.Append('|').Append(EffectsText(def, cardLevel, graph));

            // 相生「他生我」:要拿本字属性去比对配方原料(中性字视作心,永不成对)
            if (WuxingResolver.ShengMultiplier(
                    graph.RecipeElements(def.Id), def.Element ?? Element.Heart) == 3)
                text.Append("|相生:效果已含 ×3");  // 数值本身已乘,这里只解释它为何比同档高

            return text.ToString();
        }

        /// <summary>详情弹窗用:Summary 的分行版。</summary>
        public static string Detail(CharDef def, RecipeGraph graph, int cardLevel = 1) =>
            Summary(def, graph, cardLevel).Replace("|", "\n");

        /// <summary>走生克结算的效果 —— 只有这些吃相生 ×3,与 BattleEngine 里过
        /// <see cref="WuxingResolver.ResolveEffect"/> 的那几支严格对应。灼烧层数、召唤血攻、
        /// 流血、驱散条数等都是平值,不乘。</summary>
        private static bool IsWuxingScaled(EffectKind kind) => kind is
            EffectKind.DamageSingle or EffectKind.DamageAll or EffectKind.Shield or
            EffectKind.HealSelf or EffectKind.HealAll or EffectKind.HealOverTime;

        /// <summary>效果串(升级 preview 取前后两级各调一次)。
        ///
        /// <paramref name="graph"/> 给了就把**相生 ×3 也算进显示值**(2026-08-14)。
        /// 此前恒显示配置基础值,而相生是「固有、永久生效」的 —— 燊(焱+木,木生火)卡面写
        /// 「全体 80 伤」,实战打出去是 240,比同为全体伤的金档 焱(200)更高;光看卡面
        /// 却像是橙档反被金档压着。传 null 时行为与旧版逐字相同。</summary>
        public static string EffectsText(CharDef def, int cardLevel = 1, RecipeGraph graph = null)
        {
            if (def.Effects.Count == 0)
                return "无战斗效果(可兜底一击:单体3伤,或作合成材料)";

            int sheng = graph == null ? 1
                : WuxingResolver.ShengMultiplier(graph.RecipeElements(def.Id), def.Element ?? Element.Heart);

            var parts = new StringBuilder();
            for (int i = 0; i < def.Effects.Count; i++)
            {
                // 分隔符是分号,不是逗号(2026-08-10 还债):效果内部本来就带逗号
                // ——Reflect 的「伤害,N回合」、HealOverTime 的「/回合,共N回合」、
                // Summon 的「(血X攻Y,顶前排)」、穿甲的「(穿甲:无视减伤,额外+15%)」——
                // 分隔符与内容同为 U+002C 时,多效果字会被读成比实际更多的段。
                // 分号的层级严格强于逗号(顿号反而更弱,当结构分隔符会把层级弄反),
                // 所以各分支内部照常写逗号即可,不必再逐个改文案。
                if (i > 0) parts.Append(';');
                var e = def.Effects[i];
                int v = MetaRules.ScaleByCardLevel(e.Value, cardLevel);
                if (IsWuxingScaled(e.Kind)) v *= sheng;
                parts.Append(e.Kind switch
                {
                    EffectKind.DamageSingle => $"单体{v}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : "")
                        + PierceText(e),
                    EffectKind.DamageAll => $"全体{v}伤" + (e.DoubleVsBurning ? "(对灼烧目标翻倍)" : "")
                        + PierceText(e),
                    EffectKind.BurnSingle => $"单体灼烧+{v}",
                    EffectKind.BurnAll => $"全体灼烧+{v}",
                    EffectKind.Shield => $"护盾{v}" + (e.PersistOnce ? "(豁免一次回合末清空)" : ""),
                    EffectKind.BurnPotency => $"本场灼烧每层结算+{v}",
                    EffectKind.HealSelf => $"治疗{v}",
                    EffectKind.Summon => $"召{e.SummonCount}×「{e.SummonChar}」" +
                        $"(血{v}攻{MetaRules.ScaleByCardLevel(e.SummonAttack, cardLevel)},顶前排)",
                    EffectKind.Bleed => $"流血{v}/回合(无属性)",
                    EffectKind.HealAll => $"群体治疗{v}(含召唤物)",
                    EffectKind.HealOverTime => e.TargetAll
                        ? $"群体持续治疗{v}/回合,共{e.Turns}回合"
                        : $"持续治疗{v}/回合,共{e.Turns}回合",
                    EffectKind.Freeze => $"冻结{v}回合",
                    EffectKind.Slow => $"减速{v}回合(半速)",
                    EffectKind.DefenseBuff => $"本段护甲 +{v}",
                    EffectKind.ArmorBreak => $"破甲 {v}(本场削目标护甲)",
                    // 驱散条数不吃卡等级(与 BattleEngine 的 EffectKind.Dispel 分支同口径)——
                    // 用 e.Value 而不是 v:真正的约束是正数条数不能被 ScaleByCardLevel 缩放
                    // (Lv.10 系数 1.9,「驱散 2 条」会被算成 ceil(2×1.9)=4 条,与 Core 实际驱散数不符;
                    // −1 哨兵同样不缩放只是顺带受益,不是单独的理由)
                    EffectKind.Dispel => e.Value < 0
                        ? (e.TargetAll ? "全体驱散全部增益" : "驱散全部增益")
                        : (e.TargetAll ? $"全体驱散{e.Value}条增益" : $"驱散{e.Value}条增益"),
                    EffectKind.Cleanse => "净化自身全部减益",
                    EffectKind.Immunity => $"免疫{v}次伤害",
                    EffectKind.Revive => $"复活{v}名召唤物(各回半血)",
                    // 熣(DamageSingle + Blind)曾被读成三段,当时改成空格治标(与 ArmorBreak 的
                    // 「破甲 {v} 回合」同款);根因已由上面的分号分隔符解决,这里保留空格写法不再动
                    EffectKind.Blind => e.TargetAll
                        ? $"全体致盲−{v}% {e.Turns}回合"
                        : $"致盲−{v}% {e.Turns}回合",
                    EffectKind.Silence => $"沉默{e.Turns}回合",
                    EffectKind.Reflect => $"反弹{v}%伤害,{e.Turns}回合",
                    EffectKind.BurnNoDecay => "灼烧不衰减(本场)",
                    EffectKind.BurnSettleNow => "立即结算一次灼烧",
                    EffectKind.Detonate => "引爆灼烧(全额兑现并清空)",
                    // 不写「(基准 100)」:那是内部常量,玩家不该看见,而且为它多占 2 个字体码位。
                    // 跑图界面的角色栏已经在显示「攻击 N」,+50 对玩家是可解释的增量。
                    EffectKind.Empower => $"本场攻击+{v}",
                    EffectKind.Morale => $"战意+{v}层(每层攻击+10,上限 5 层)",
                    // ApBoost 不吃卡等级(与 BattleEngine 的 EffectKind.ApBoost 分支同口径:
                    // AP 是节奏/经济不是资源)——用 e.Value 而不是 v
                    EffectKind.ApBoost => $"本场每回合 AP 上限+{e.Value}",
                    // 倍率读常量而不是写死「×1.5」:E-b5 重平衡会改那个常量,写死了卡面就会骗人
                    EffectKind.CritBuff =>
                        $"本场暴击率+{v}%(暴击伤害×{BattleConfig.CritMultiplierPercent / 100f:0.##})",
                    // 与 PierceText 同一套措辞(「无视 N 点护甲」),差别只在存续:那条是本次,这条是本场。
                    // 锐 身上没有伤害效果,PierceText 不会出现,所以这里必须把口径自己说全。
                    EffectKind.PierceBuff => $"本场穿透+{v}(本场每次攻击无视 {v} 点护甲)",
                    _ => e.Kind.ToString(),
                });
            }
            return parts.ToString();
        }

        /// <summary>穿透后缀(2026-08-12,E-b4 T3)。口径从「穿甲:无视减伤,额外 +15%」换成
        /// 点数 —— 旧的 +15% 已固化进这三个字的基础值,卡面上的伤害数字自己涨了,
        /// 后缀只剩下真正与防御有关的那一半。破甲与穿透的一句话区分见 spec 第七节:
        /// 破甲削的是目标的甲(削掉就一直是削掉的、队友蹭得到),穿透只是这一击的视角。</summary>
        private static string PierceText(EffectDef e) =>
            e.Pierce > 0 ? $"(穿透 {e.Pierce}:本次无视 {e.Pierce} 点护甲)" : "";

        public static string ElementName(Element element) => element switch
        {
            Element.Wood => "木",
            Element.Fire => "火",
            Element.Earth => "土",
            Element.Metal => "金",
            Element.Water => "水",
            Element.Heart => "心",
            _ => "?",
        };

        /// <summary>稀有度显示名(与 <see cref="Theme.RarityColor"/> 同一套):
        /// 枚举名 = 皮肤色 = 强度序,视觉层级 白→绿→蓝→紫→金→橙→红。</summary>
        public static string RarityName(CardRarity rarity) => rarity switch
        {
            CardRarity.White => "白",
            CardRarity.Green => "绿",
            CardRarity.Blue => "蓝",
            CardRarity.Purple => "紫",
            CardRarity.Gold => "金",
            CardRarity.Orange => "橙",
            CardRarity.Red => "红",
            _ => "?",
        };
    }
}
