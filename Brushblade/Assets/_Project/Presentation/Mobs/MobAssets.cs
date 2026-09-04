using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>字怪形象资产的查找与加载(《敌人形象关键词包》§2 分层规范)。
    /// 战斗代码里 EnemyDef.Id 是中文,资产名是拼音——这里是唯一的对照表,
    /// 与 tools/design/rasterize_mobs.py 的同名表保持一致。</summary>
    public static class MobAssets
    {
        /// <summary>层序 = 叠放次序(先画的在下)。属性气场并在 wisp 层里,没有独立 aura 层。</summary>
        public static readonly string[] Layers = { "body", "face", "wisp" };

        private static readonly Dictionary<string, string> MinionSlugs = new()
        {
            { "错字鬼", "cuozigui" },
            { "缺笔妖", "quebiyao" },
            { "标点小妖", "biaodianxiaoyao" },
            { "叠字怪", "dieziguai" },
            { "夯土妖", "hangtuyao" },
            { "通假字", "tongjiazi" },
            { "生僻字", "shengpizi" },
            { "墨渍", "mozi" },
            { "焦痕", "jiaohen" },
            // 2026-08-29 补的五只(六系各补到 3 只)
            { "涂改", "tugai" },
            { "铁画", "tiehua" },
            { "镇纸", "zhenzhi" },
            { "洇痕", "yinhen" },
            { "衍文", "yanwen" },
            // 2026-09-03 补的十一只:四只旧欠账(灯花/墨溅/悬针/败笔)+ 七只护甲怪。
            // 至此 enemies.json 的 25 只杂兵全部有立绘,不再有回落到字牌格的怪。
            { "灯花", "denghua" },
            { "墨溅", "mojian" },
            { "悬针", "xuanzhen" },
            { "败笔", "baibi" },
            { "枯笔", "kubi" },
            { "火漆", "huoqi" },
            { "砚台", "yantai" },
            { "铜钤", "tongqian" },
            { "版牍", "bandu" },
            { "窑变", "yaobian" },
            { "宿墨", "sumo" },
        };

        /// <summary>Boss 形象按阶段出:四个阶段是四套图。「倒」「海」复用排山倒海的稿。</summary>
        private static readonly Dictionary<string, string[]> BossStages = new()
        {
            { "排山倒海", new[] { "boss_paishandaohai_1pai", "boss_paishandaohai_2shan",
                                  "boss_paishandaohai_3dao", "boss_paishandaohai_4hai" } },
            { "翻江倒海", new[] { "boss_fanjiangdaohai_1fan", "boss_fanjiangdaohai_2jiang",
                                  "boss_paishandaohai_3dao", "boss_paishandaohai_4hai" } },
            { "雷霆万钧", new[] { "boss_leitingwanjun_1lei", "boss_leitingwanjun_2ting",
                                  "boss_leitingwanjun_3wan", "boss_leitingwanjun_4jun" } },
        };

        /// <summary>该怪(该阶段)的资产前缀;没有对应形象返回 null —— 调用方回落到字牌格。</summary>
        public static string PrefixFor(EnemyDef def, int phaseIndex = 0)
        {
            if (def == null) return null;
            if (def.Phases.Count > 0)
            {
                if (!BossStages.TryGetValue(def.Id, out var stages)) return null;
                return stages[Mathf.Clamp(phaseIndex, 0, stages.Length - 1)];
            }
            return MinionSlugs.TryGetValue(def.Id, out var slug) ? "enemy_" + slug : null;
        }

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>取一层的 Sprite;资产不存在返回 null。
        /// 走 Texture2D + Sprite.Create 而不是 Resources.Load&lt;Sprite&gt; ——
        /// 后者依赖 PNG 的导入设置(textureType 必须是 Sprite),没写 .meta 时会取不到。</summary>
        public static Sprite Layer(string prefix, string layer)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            string key = prefix + "_" + layer;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = Resources.Load<Texture2D>(key);
            var sprite = texture == null
                ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>这只怪有没有形象资产(至少有主体层)。</summary>
        public static bool Has(EnemyDef def, int phaseIndex = 0) =>
            Layer(PrefixFor(def, phaseIndex), "body") != null;

        /// <summary>火芯烧到顶所需的自燃层数(表现侧取值:满亮之前要看得见几段变化)。
        /// 焦痕自燃没有层数上限,4 层之后 <see cref="Mathf.Clamp01"/> 按满亮画。</summary>
        private const int ScorchFullStacks = 4;

        /// <summary>L4 状态层的强度 = 该怪的战斗状态(《敌人形象关键词包》§2)。
        /// 这四只的机制本来就有状态字段,配一张层就把「颜色 = 状态」兑现了。</summary>
        public static float StateAmountFor(EnemyState enemy) => enemy.Def.Ability switch
        {
            // 缺笔妖:残笔随补全进度长回来(0→3),补满即实
            EnemyAbility.Regrow => Mathf.Clamp01(enemy.RegrowProgress / 3f),
            // 通假字:面具戴着 = 还没现形;首次行动后真身与伪装一致,面具落下
            EnemyAbility.Disguise => enemy.ApparentElement != enemy.Element ? 1f : 0f,
            // 生僻字:墨雾罩着 = 还没被读懂(受击 2 次后 ApparentElement 才有值)
            EnemyAbility.Obscure => enemy.ApparentElement == null ? 1f : 0f,
            // 焦痕:越磨越烫,火芯随自燃层数越来越亮,ScorchFullStacks 次受击烧到顶。
            //
            // 读的是**加攻百分点**而不是攻击力差值(2026-09-04 修 bug):差值那条写死了分母 8,
            // 是 ×10 之前「基础攻 4、每次 +2、四次到顶」的旧数;全表量级 ×10 后焦痕基础攻 40、
            // 每次自燃 +50% = +20 点,20/8 直接 Clamp 到 1 —— 第一次受击就满亮,后三次纹丝不动。
            // 百分点这条对量级 ×10、层段深度缩放、缺笔妖抬 BaseAttack 一概免疫(它们乘的都是
            // BaseAttack,比值永不乘),分母也跟着 BattleEngine.ScorchGain 走,不再各写一份。
            EnemyAbility.Scorch => Mathf.Clamp01(
                enemy.Statuses.TotalMagnitude(StatusKind.AttackBuff)
                / (float)(ScorchFullStacks * BattleEngine.ScorchGain)),
            _ => 0f,
        };
    }
}
