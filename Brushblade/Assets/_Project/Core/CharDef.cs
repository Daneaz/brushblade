using System;
using System.Collections.Generic;

namespace Brushblade.Core
{
    /// <summary>字卡稀有度七档(19.3.1):出厂固定,由人工筛选指定。</summary>
    public enum CardRarity
    {
        White = 1,  // 白
        Green = 2,  // 绿
        Blue = 3,   // 蓝
        Purple = 4, // 紫
        Orange = 5, // 橙
        Red = 6,    // 红
        Gold = 7,   // 金:五系四叠字专属(2026-08-03)
    }

    /// <summary>可玩对象定义:部件与汉字共用(第 4 章 4.2)。部件 = 无配方的叶子。</summary>
    public sealed class CharDef
    {
        /// <summary>唯一标识,即字形("火"、"林"、"焚")。</summary>
        public string Id { get; }

        /// <summary>自身属性;中性部件为 null。</summary>
        public Element? Element { get; }

        /// <summary>配方原料(可为部件或更低阶的字);部件为空数组。</summary>
        public IReadOnlyList<string> Recipe { get; }

        /// <summary>出字消耗 AP:由稀有度唯一决定,见 <see cref="ApCostFor"/>(第 3 章 3.3 / 10.1)。</summary>
        public int ApCost { get; }

        /// <summary>稀有度(19.3.1,缺省白)。</summary>
        public CardRarity Rarity { get; }

        /// <summary>出字效果;部件的"单独出战"弱效果也在此(第 4 章 4.2.1)。</summary>
        public IReadOnlyList<EffectDef> Effects { get; }

        /// <summary>攻击模式下的替代效果(2026-07-26):把字拖到敌人身上出手时改用这套。
        /// 空 = 该字没有第二用法,拖放与双击同效。水/土 靠它在「治疗/加盾」之外多一个攻击选项。</summary>
        public IReadOnlyList<EffectDef> AttackEffects { get; }

        /// <summary>拼音(11.2.4 点查安全网);可缺省。</summary>
        public string Pinyin { get; }

        /// <summary>短释义(11.2.4);可缺省。</summary>
        public string Gloss { get; }

        public bool IsLeaf => Recipe.Count == 0;

        /// <summary>出字 AP:一律 1(2026-08-03 拍板,与稀有度解耦)。
        /// 旧规则「白绿蓝1/紫橙2/红3」已作废,第10章 10.1 的 AP 表同步失效。</summary>
        public static int ApCostFor(CardRarity rarity) => 1;

        public CharDef(string id, Element? element, IReadOnlyList<string> recipe = null,
            IReadOnlyList<EffectDef> effects = null, CardRarity rarity = CardRarity.White,
            string pinyin = null, string gloss = null, IReadOnlyList<EffectDef> attackEffects = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Element = element;
            Recipe = recipe ?? Array.Empty<string>();
            ApCost = ApCostFor(rarity);
            Effects = effects ?? Array.Empty<EffectDef>();
            AttackEffects = attackEffects ?? Array.Empty<EffectDef>();
            Rarity = rarity;
            Pinyin = pinyin;
            Gloss = gloss;
        }
    }
}
