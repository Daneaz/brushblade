using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>五系 Lv1 / Lv3 / 池词条数据落表的自检(D1 Task 13)。读真实 chars.json。
    /// 专属格(spec §9 加粗名)留给 D2,这里不查也不许有:每字特性总数 = 1(Lv1)+ Lv3 + 池格数。</summary>
    public class TraitDataCoverageTests
    {
        private static RecipeGraph _graph;
        private static RecipeGraph Graph => _graph ??= CharTableTests.RealGraph();

        // 增补平面字在 chars.json 里走 PUA 代理码位:𣛧(U+236E7)/ 𨰻(U+28C3B)不在 BMP,
        // 字表里的 id 是 BMP 私用区码位(U+E625 / U+E626,返回值是不可见的 PUA 字符,不是空串)。
        // 只换这两个;其余字原样返回。新增增补平面字要在这里与 EveryPoolCell_… 里的反向映射同步加。
        private static string Id(string ch) => ch == "𣛧" ? "" : ch == "𨰻" ? "" : ch;

        private static List<CharDef> Playable() =>
            Graph.All.Where(d => !d.IsComponent && d.Element != null).ToList();

        private static List<CharDef> OfElement(Element e) => Playable().Where(d => d.Element == e).ToList();

        private static List<TraitDef> At(CharDef d, TraitSlot slot) => d.Traits.Where(t => t.Slot == slot).ToList();

        private static IReadOnlyList<EffectDef> BodyOf(CharDef d, TraitFace face) =>
            face == TraitFace.Attack ? d.AttackEffects : d.Effects;

        private static string KeywordOf(CharDef d)
        {
            switch (d.Element)
            {
                case Element.Fire: return "灼";
                case Element.Metal: return "战意";
                case Element.Earth: return "破甲";
                case Element.Wood: return "种";   // spec A §5:花攻击面魅惑 → 种
                default:
                    return d.AttackEffects.Any(e => e.Kind == EffectKind.Freeze) ? "冻结" : "减速";
            }
        }

        /// <summary>五行面 Lv1 名(spec A §1 / 执行期 D2–D6):火 慑、金 格挡、土 护甲、水按模式、木 = 本命名。</summary>
        private static readonly Dictionary<string, string> WaterMode = new()
        {
            ["冷"] = "涓", ["冻"] = "涓", ["澡"] = "涓", ["淋"] = "涓",
            ["海"] = "涌", ["溃"] = "涌", ["湮"] = "涌", ["冰"] = "涌", ["淼"] = "涌",
            ["沐"] = "苏", ["㵘"] = "苏",
        };

        private static readonly Dictionary<string, string> WoodNature = new()
        {
            ["花"] = "迷香", ["藤"] = "缠绕", ["箭"] = "远射", ["楸"] = "秋燥", ["荆"] = "荆棘", ["林"] = "成林",
            ["柘"] = "坚木", ["森"] = "森然", ["藻"] = "丛生", ["桂"] = "桂香", ["\ue625"] = "参天",
        };

        private static string FeatureNameOf(CharDef d)
        {
            switch (d.Element)
            {
                case Element.Fire: return "慑";
                case Element.Metal: return "格挡";
                case Element.Earth: return "护甲";
                case Element.Wood: return WoodNature[d.Id];
                default: return WaterMode[d.Id];
            }
        }

        private static int BurnStacks(CardRarity r) => r switch
        {
            CardRarity.White or CardRarity.Green or CardRarity.Blue => 1,
            CardRarity.Purple => 2,
            CardRarity.Gold or CardRarity.Orange => 3,
            _ => 4,
        };

        private static int MoraleStacks(CardRarity r) => r switch
        {
            CardRarity.White or CardRarity.Green or CardRarity.Blue => 1,
            CardRarity.Purple or CardRarity.Gold => 2,
            _ => 3,
        };

        private static bool IsBurn(EffectKind k) => k == EffectKind.BurnSingle || k == EffectKind.BurnAll;

        private static EffectDef FirstOf(IReadOnlyList<EffectDef> list, System.Func<EffectKind, bool> pred) =>
            list.First(e => pred(e.Kind));

        // ---- spec §9 池格清单(照表手抄:字 槽 面 名;专属格不在其中) ----
        // 炸 Lv5·攻「引燃」、溃 Lv5·攻「凝冰」:池条目落到全体面,管线自动补 pick All(Ruling 17)。
        private static readonly string[] PoolCells =
        {
            "热 Lv4 Both 先声", "热 Lv5 Attack 引燃", "热 Lv5 Feature 续火", "热 Lv6 Feature 烟熏", "热 Lv8 Feature 火雨",
            "爆 Lv4 Both 精进", "爆 Lv5 Attack 爆燃", "爆 Lv5 Feature 续火", "爆 Lv6 Attack 炽烈", "爆 Lv8 Feature 焦热",
            "炸 Lv4 Attack 克敌", "炸 Lv4 Feature 克敌", "炸 Lv5 Attack 引燃","炸 Lv6 Feature 烟熏", "炸 Lv8 Feature 火雨", "烈 Lv5 Attack 爆燃",
            "烈 Lv6 Attack 炽烈", "燥 Lv5 Feature 续火", "燥 Lv6 Feature 烟熏", "蒸 Lv5 Feature 烟障", "蒸 Lv6 Attack 炽烈",
            "炎 Lv5 Feature 续火", "灿 Lv5 Attack 引燃", "利 Lv4 Both 先声", "利 Lv5 Attack 破甲", "利 Lv5 Feature 砥砺",
            "利 Lv6 Feature 回锋", "利 Lv8 Attack 重斩", "锋 Lv4 Both 精进", "锋 Lv5 Attack 连斩", "锋 Lv5 Feature 砥砺",
            "锋 Lv6 Attack 迎刃", "锋 Lv8 Feature 金钟", "剑 Lv4 Attack 克敌", "剑 Lv4 Feature 克敌", "剑 Lv5 Attack 破甲", "剑 Lv6 Attack 迎刃",
            "剑 Lv8 Feature 反戈", "锥 Lv4 Attack 补刀", "锥 Lv4 Feature 补刀", "锥 Lv5 Attack 连斩", "锥 Lv6 Feature 回锋", "锥 Lv8 Feature 金钟",
            "剿 Lv4 Both 化险", "剿 Lv5 Attack 破甲", "剿 Lv6 Attack 迎刃", "剿 Lv8 Feature 反戈", "剁 Lv5 Attack 连斩",
            "剁 Lv6 Attack 迎刃", "铡 Lv5 Attack 破甲", "铡 Lv6 Feature 回锋", "鍂 Lv5 Feature 蓄势", "冷 Lv4 Attack 克敌", "冷 Lv4 Feature 克敌",
            "冷 Lv5 Attack 凝冰", "冷 Lv5 Feature 涓流", "冷 Lv6 Feature 护持", "冷 Lv8 Feature 甘露", "冻 Lv4 Both 精进",
            "冻 Lv5 Attack 激流", "冻 Lv5 Feature 寒泉", "冻 Lv6 Attack 冰缚", "冻 Lv8 Feature 蓄泉", "海 Lv4 Both 化险",
            "海 Lv5 Feature 涓流", "海 Lv6 Feature 护持", "海 Lv8 Attack 怒涛", "溃 Lv4 Both 先声", "溃 Lv5 Attack 凝冰",
            "溃 Lv6 Feature 护持", "湮 Lv5 Attack 激流", "湮 Lv6 Attack 冰缚", "澡 Lv5 Feature 寒泉",
            "澡 Lv6 Feature 护持", "冰 Lv5 Attack 凝冰", "沐 Lv5 Feature 涓流", "碉 Lv4 Both 先声", "碉 Lv5 Attack 碎石",
            "碉 Lv5 Feature 加固", "碉 Lv6 Feature 反震", "碉 Lv8 Attack 崩岩", "垒 Lv4 Both 化险", "垒 Lv5 Attack 震地",
            "垒 Lv5 Feature 垒土", "垒 Lv6 Feature 反震", "垒 Lv8 Attack 劈山", "壁 Lv4 Both 精进", "壁 Lv5 Attack 碎石",
            "壁 Lv5 Feature 加固", "壁 Lv6 Attack 余震", "壁 Lv8 Attack 崩岩", "堡 Lv4 Attack 克敌", "堡 Lv4 Feature 克敌", "堡 Lv5 Feature 垒土",
            "堡 Lv6 Feature 反震", "堡 Lv8 Attack 劈山", "崩 Lv4 Attack 破敌", "崩 Lv4 Feature 破敌", "崩 Lv5 Attack 震地", "崩 Lv6 Attack 余震",
            "崩 Lv8 Feature 金汤", "碎 Lv4 Attack 补刀", "碎 Lv4 Feature 补刀", "碎 Lv5 Attack 碎石", "碎 Lv6 Attack 余震", "碎 Lv8 Feature 坚壁",
            "塔 Lv5 Feature 加固", "塔 Lv6 Feature 反震", "杜 Lv5 Feature 垒土", "圭 Lv5 Feature 加固", "花 Lv4 Both 先声",
            "花 Lv5 Attack 寄生", "花 Lv5 Feature 新芽", "花 Lv6 Feature 扎根", "花 Lv8 Feature 繁生", "藤 Lv4 Both 精进",
            "藤 Lv5 Attack 寄生", "藤 Lv5 Feature 沃土", "藤 Lv6 Attack 汲取", "藤 Lv8 Attack 缠缚", "箭 Lv4 Attack 破敌", "箭 Lv4 Feature 破敌",
            "箭 Lv5 Attack 蔓刺", "箭 Lv6 Feature 扎根", "箭 Lv8 Feature 灵荫", "楸 Lv5 Feature 新芽", "楸 Lv6 Feature 扎根",
            "荆 Lv5 Feature 沃土", "荆 Lv6 Feature 扎根", "柘 Lv5 Feature 沃土",
            // spec A(2026-10-15):林 Lv5 池·新芽 被林风(专属)替换;烈 Lv5 燃 烈风 → 池·烟障(D14);
            // §6 Lv6 补缺的那一面(白 / 绿 / 蓝 / 紫取本系池该面的 Lv6),共 27 条
            "烈 Lv5 Feature 烟障",
            "热 Lv6 Attack 炽烈", "爆 Lv6 Feature 烟熏", "炸 Lv6 Attack 炽烈", "烈 Lv6 Feature 烟熏", "燥 Lv6 Attack 炽烈",
            "蒸 Lv6 Feature 烟熏",
            "利 Lv6 Attack 迎刃", "锥 Lv6 Attack 迎刃", "铡 Lv6 Attack 迎刃",
            "锋 Lv6 Feature 回锋", "剑 Lv6 Feature 回锋", "剿 Lv6 Feature 回锋", "剁 Lv6 Feature 回锋",
            "冻 Lv6 Feature 护持", "湮 Lv6 Feature 护持",
            "碉 Lv6 Attack 余震", "垒 Lv6 Attack 余震", "堡 Lv6 Attack 余震", "塔 Lv6 Attack 余震",
            "壁 Lv6 Feature 反震", "崩 Lv6 Feature 反震", "碎 Lv6 Feature 反震",
            "花 Lv6 Attack 汲取", "箭 Lv6 Attack 汲取", "楸 Lv6 Attack 汲取", "荆 Lv6 Attack 汲取",
            "藤 Lv6 Feature 扎根",
        };

        // ---- spec §9 专属格清单(加粗名;照表手抄:字 槽 面 名)----
        // D2-火 Task 7:火系 44 格。V4:焚风(两面通用、燃面不起作用的 Lv4 加成)拆成攻击面原条目 + 燃面同档精进,
        // 两条同名,所以清单是 45 条、44 格(Ruling 9:气盛 scope All 在燃面也加灼火力,回到单行两面)。其余各系的专属格随 D2-金 / 水 / 土 / 木 追加。
        private static readonly string[] ExclusiveCells =
        {
            "热 Lv8 Attack 升温",
            "爆 Lv8 Attack 连爆",
            "炸 Lv5 Feature 埋雷", "炸 Lv8 Attack 惊爆",
            "烈 Lv4 Both 气盛", "烈 Lv8 Attack 火烧连营", "烈 Lv8 Feature 烈焰护身",
            "燥 Lv4 Both 干涸", "燥 Lv5 Attack 燥裂", "燥 Lv8 Attack 燥火攻心", "燥 Lv8 Feature 焦土",
            "蒸 Lv4 Both 温润", "蒸 Lv5 Attack 水火相激", "蒸 Lv8 Attack 蒸腾", "蒸 Lv8 Feature 蒸笼",
            "炎 Lv4 Both 双焰", "炎 Lv5 Attack 火上浇油", "炎 Lv6 Feature 上炎", "炎 Lv8 Attack 炎刃", "炎 Lv8 Feature 炎炎",
            "灿 Lv4 Both 火山", "灿 Lv5 Feature 炫目", "灿 Lv6 Attack 光耀", "灿 Lv8 Attack 灿然", "灿 Lv8 Feature 流光",
            "焱 Lv4 Both 三焰", "焱 Lv5 Attack 星火", "焱 Lv5 Feature 飞焰", "焱 Lv6 Feature 炽焰", "焱 Lv8 Attack 火花四溅",
            "焱 Lv8 Feature 星星之火",
            "焚 Lv4 Attack 焚风", "焚 Lv4 Feature 焚风", "焚 Lv5 Attack 烈焚", "焚 Lv5 Feature 焚城", "焚 Lv6 Attack 焚甲",
            "焚 Lv8 Attack 玉石俱焚", "焚 Lv8 Feature 焚林",
            "燚 Lv4 Both 四火", "燚 Lv5 Attack 四炎", "燚 Lv5 Feature 火海", "燚 Lv6 Attack 焚身", "燚 Lv8 Attack 焚天",
            "燚 Lv8 Feature 烈焰天幕",
            // spec A §6.1 金 / 橙 / 红专属 Lv6(补缺的那一面;D1 改名 灿然 → 灿烂、燎原 → 燎天)
            "炎 Lv6 Attack 赤炎", "灿 Lv6 Feature 灿烂", "焚 Lv6 Feature 焚心", "焱 Lv6 Attack 焱风", "燚 Lv6 Feature 燎天",
            // D2-金 Task 6:金系 39 格。Q20:放血(铠面无暴击)、剁骨(铠面无伤害)拆成攻击面原条目 + 铠面同档精进,
            // 两条同名,所以 41 条、39 格。Q7 直捣巢穴 = 横扫;Q8 剁骨从第 2 击起
            "利 Lv8 Feature 得利",
            "锋 Lv8 Attack 锋锐",
            "剑 Lv5 Feature 剑意", "剑 Lv8 Attack 横扫千军",
            "锥 Lv5 Feature 锥立", "锥 Lv8 Attack 锥心",
            "剿 Lv5 Feature 坚营", "剿 Lv8 Attack 直捣巢穴",
            "剁 Lv4 Attack 剁骨", "剁 Lv4 Feature 剁骨", "剁 Lv5 Feature 剁截", "剁 Lv8 Attack 大卸八块",
            "剁 Lv8 Feature 刀山",
            "铡 Lv4 Both 铁则", "铡 Lv5 Feature 立威", "铡 Lv8 Attack 铡刀落", "铡 Lv8 Feature 铡关",
            "鍂 Lv4 Both 双金", "鍂 Lv5 Attack 金石", "鍂 Lv6 Feature 金气", "鍂 Lv8 Attack 双锋",
            "鍂 Lv8 Feature 双金合璧",
            "刲 Lv4 Attack 放血", "刲 Lv4 Feature 放血", "刲 Lv5 Attack 刲刺", "刲 Lv5 Feature 匿锋",
            "刲 Lv6 Attack 割取", "刲 Lv8 Attack 割喉", "刲 Lv8 Feature 刀光",
            "鑫 Lv4 Both 聚金", "鑫 Lv5 Attack 三才", "鑫 Lv5 Feature 金身", "鑫 Lv6 Feature 富甲",
            "鑫 Lv8 Attack 三金破", "鑫 Lv8 Feature 金玉满堂",
            "𨰻 Lv4 Both 刚", "𨰻 Lv5 Attack 四金", "𨰻 Lv5 Feature 千锤", "𨰻 Lv6 Attack 破军",
            "𨰻 Lv8 Attack 千钧", "𨰻 Lv8 Feature 金刚",
            // spec A §6.1(D1 改名 聚金 → 金汇)
            "鍂 Lv6 Attack 金鸣", "鑫 Lv6 Attack 金汇", "刲 Lv6 Feature 刲刃", "𨰻 Lv6 Feature 镇军",
            // D2-水 Task 6:水系 42 格。Q2:冰封 / 怀山(润面无冻结)、汪洋 / 沐恩(攻击面无治疗)、淋漓(润面无减速)
            // 拆成生效面原条目 + 空转面同档精进,两条同名,所以 47 条、42 格。Q1:湮灭保留原文,空效果行
            "冷 Lv8 Attack 冷却",
            "冻 Lv8 Attack 冰冻三尺",
            "海 Lv5 Attack 百川", "海 Lv8 Feature 海纳百川",
            "溃 Lv5 Feature 溃围", "溃 Lv8 Attack 溃不成军",
            "湮 Lv4 Both 湮灭", "湮 Lv5 Feature 沉渊", "湮 Lv8 Attack 湮灭无踪", "湮 Lv8 Feature 潜流",
            "澡 Lv4 Both 洗涤", "澡 Lv5 Attack 洗尽铅华", "澡 Lv8 Attack 涤荡", "澡 Lv8 Feature 濯身",
            "冰 Lv4 Attack 冰封", "冰 Lv4 Feature 冰封", "冰 Lv5 Feature 冰甲", "冰 Lv6 Attack 寒彻",
            "冰 Lv8 Attack 坚冰", "冰 Lv8 Feature 冰晶",
            "沐 Lv4 Attack 沐恩", "沐 Lv4 Feature 沐恩", "沐 Lv5 Attack 新沐", "沐 Lv6 Feature 春雨",
            "沐 Lv8 Attack 沐雨", "沐 Lv8 Feature 栉风沐雨",
            "淋 Lv4 Attack 淋漓", "淋 Lv4 Feature 淋漓", "淋 Lv5 Attack 倾盆", "淋 Lv5 Feature 细雨",
            "淋 Lv6 Feature 浇熄", "淋 Lv8 Attack 暴雨", "淋 Lv8 Feature 大雨滂沱",
            "淼 Lv4 Attack 汪洋", "淼 Lv4 Feature 汪洋", "淼 Lv5 Attack 同寒", "淼 Lv5 Feature 涵养",
            "淼 Lv6 Attack 冰水", "淼 Lv8 Attack 浩瀚", "淼 Lv8 Feature 水大无际",
            "㵘 Lv4 Attack 怀山", "㵘 Lv4 Feature 怀山", "㵘 Lv5 Attack 洪峰", "㵘 Lv5 Feature 泽被",
            "㵘 Lv6 Attack 浩荡", "㵘 Lv8 Attack 滔天", "㵘 Lv8 Feature 泽及四方",
            // spec A §6:冷 / 海 / 溃 / 澡 攻面只减速,冰缚 / 护持都不生效 → 自定义行「精进」(D13,不是池引用);
            // §6.1 专属(D1 改名 淋漓 → 淋透)
            "冷 Lv6 Attack 精进", "海 Lv6 Attack 精进", "溃 Lv6 Attack 精进", "澡 Lv6 Attack 精进",
            "冰 Lv6 Feature 冰肌", "沐 Lv6 Attack 沐霖", "淼 Lv6 Feature 淼茫", "淋 Lv6 Attack 淋透", "㵘 Lv6 Feature 泽润",
            // D8:溃 Lv8 润 池·甘露 → 自定义行(Cleanse pick AllAllies,全体面不选友方)
            "溃 Lv8 Feature 甘露",
            // D2-土 Task 6:土系 35 格。V4:塔基(攻面镇压 / 固面护盾 + 护甲 × 3,Q11)、深厚(攻面无护甲 → 红档精进)
            // 拆成两条单面,两条同名,所以 37 条、35 格。叠土 / 三土是字形特性,粉身碎骨是击杀时
            "碉 Lv8 Feature 碉楼",
            "垒 Lv8 Feature 积土成山",
            "壁 Lv8 Feature 铜墙铁壁",
            "堡 Lv5 Attack 据守反击", "堡 Lv8 Feature 堡垒",
            "崩 Lv5 Feature 落石", "崩 Lv8 Attack 地裂",
            "碎 Lv5 Feature 碎玉", "碎 Lv8 Attack 粉身碎骨",
            "塔 Lv4 Attack 塔基", "塔 Lv4 Feature 塔基", "塔 Lv5 Attack 塔倾", "塔 Lv8 Attack 镇塔", "塔 Lv8 Feature 七级浮屠",
            "杜 Lv4 Both 杜绝", "杜 Lv5 Attack 杜塞", "杜 Lv6 Feature 闭门", "杜 Lv8 Attack 堵截", "杜 Lv8 Feature 杜门不出",
            "圭 Lv4 Both 叠土", "圭 Lv5 Attack 重土", "圭 Lv6 Feature 圭臬", "圭 Lv8 Attack 圭角", "圭 Lv8 Feature 土生金",
            "垚 Lv4 Both 三土", "垚 Lv5 Attack 移山", "垚 Lv5 Feature 叠嶂", "垚 Lv6 Feature 山岳", "垚 Lv8 Attack 三山",
            "垚 Lv8 Feature 对峙",
            "㙓 Lv4 Attack 深厚", "㙓 Lv4 Feature 深厚", "㙓 Lv5 Attack 山压", "㙓 Lv5 Feature 厚土", "㙓 Lv6 Feature 载物",
            "㙓 Lv8 Attack 地动", "㙓 Lv8 Feature 厚德载物",
            // spec A §6.1(D1 改名 圭角 → 圭棱、杜绝 → 杜渐、厚土 → 厚壤)
            "圭 Lv6 Attack 圭棱", "杜 Lv6 Attack 杜渐", "垚 Lv6 Attack 垚峙", "㙓 Lv6 Attack 厚壤",
            // D2-木 Task 6:木系 46 格。V4:秋实 / 披荆 / 馥郁 / 水草 / 参天之力(木灵本命,攻面不起作用)拆成生面本命
            // + 攻面同档精进,两条同名,所以 51 条、46 格。坚心保留原文、空效果行(同湮灭);双木 / 三木是字形特性;折桂是击杀时
            "花 Lv8 Attack 移花接木",
            "藤 Lv8 Feature 攀附",
            "箭 Lv5 Feature 木箭", "箭 Lv8 Attack 万箭齐发",
            "楸 Lv4 Attack 秋实", "楸 Lv4 Feature 秋实", "楸 Lv5 Attack 落叶", "楸 Lv8 Attack 秋风扫叶", "楸 Lv8 Feature 一叶知秋",
            "荆 Lv4 Attack 披荆", "荆 Lv4 Feature 披荆", "荆 Lv5 Attack 荆刺", "荆 Lv8 Attack 荆鞭", "荆 Lv8 Feature 荆天棘地",
            "林 Lv4 Both 双木", "林 Lv5 Attack 众木", "林 Lv5 Feature 林风", "林 Lv6 Feature 林荫", "林 Lv8 Attack 林海", "林 Lv8 Feature 林立",
            "柘 Lv4 Both 坚心", "柘 Lv5 Attack 柘弓", "柘 Lv6 Feature 守护", "柘 Lv8 Attack 硬弓", "柘 Lv8 Feature 良弓",
            "桂 Lv4 Attack 馥郁", "桂 Lv4 Feature 馥郁", "桂 Lv5 Attack 折桂", "桂 Lv5 Feature 桂花雨", "桂 Lv6 Feature 蟾宫",
            "桂 Lv8 Attack 月桂", "桂 Lv8 Feature 桂冠",
            "藻 Lv4 Attack 水草", "藻 Lv4 Feature 水草", "藻 Lv5 Attack 缠足", "藻 Lv5 Feature 浮藻", "藻 Lv6 Feature 藻荇",
            "藻 Lv8 Attack 藻缚", "藻 Lv8 Feature 绿潮",
            "森 Lv4 Both 三木", "森 Lv5 Attack 森罗", "森 Lv5 Feature 森风", "森 Lv6 Feature 森严", "森 Lv8 Attack 林深",
            "森 Lv8 Feature 万木回春",
            "𣛧 Lv4 Attack 参天之力", "𣛧 Lv4 Feature 参天之力", "𣛧 Lv5 Attack 四木成林", "𣛧 Lv5 Feature 木疾",
            "𣛧 Lv6 Feature 落木", "𣛧 Lv8 Attack 撼天", "𣛧 Lv8 Feature 擎天",
            // spec A §4.5:速度从本命挪到五行面 Lv5 被动(林风替换池·新芽;森风 / 浮藻 / 木疾替换丛林 / 丛藻 / 独木生林,已就地改名);
            // §6.1 专属 Lv6(D1 改名 森然 → 森生)
            "林 Lv6 Attack 林间", "柘 Lv6 Attack 柘刺", "森 Lv6 Attack 森生", "藻 Lv6 Attack 藻缠", "桂 Lv6 Attack 桂馥",
            "𣛧 Lv6 Attack 落叶",
        };

        /// <summary>V4 拆行的专属 Lv4:燃面那条按同档「精进」写。</summary>
        private static readonly string[] V4Split = { "焚风" };

        private static bool IsExclusive(CharDef d, TraitDef t)
        {
            var real = d.Id == "\ue625" ? "𣛧" : d.Id == "\ue626" ? "𨰻" : d.Id;
            return ExclusiveCells.Contains($"{real} {t.Slot} {t.Face} {t.Name}");
        }

        [Test]
        public void PoolCellList_Has158Rows()
        {
            // 131(spec v7 §9)− 林 新芽 − 溃 甘露(D8 改自定义行)+ 烈 烟障 + spec A Lv6 补面 27 条
            // + 炸 克敌拆成两条单面(spec A §3 炸五行面改全体、不再选敌,U6 拆行规则自动生效)
            Assert.That(PoolCells.Length, Is.EqualTo(158));
        }

        [Test]
        public void ExclusiveCellList_Fire48Cells_49Rows_Metal43Cells_45Rows_Water52Cells_57Rows_Earth39Cells_41Rows_Wood53Cells_58Rows()
        {
            // spec A:火 −烈风 +5、金 +4、水 +9(含 4 条自定义精进)+ 甘露(D8)、土 +4、木 +1 林风 +6(森风 / 浮藻 / 木疾为就地改名)
            Assert.That(ExclusiveCells.Length, Is.EqualTo(49 + 45 + 57 + 41 + 58));
            Assert.That(ExclusiveCells.Select(c => c.Split(' ')).Select(p => (p[0], p[1], p[3])).Distinct().Count(),
                Is.EqualTo(48 + 43 + 52 + 39 + 53), "去掉 V4 / Q20 / Q2 拆出的五行面行 = 火 48 格 + 金 43 格 + 水 52 格 + 土 39 格 + 木 53 格");
            Assert.That(ExclusiveCells.Distinct().Count(), Is.EqualTo(ExclusiveCells.Length));
            foreach (var c in ExclusiveCells)
                Assert.That(PoolCells.Contains(c), Is.False, $"{c} 不能同时是池格");
        }

        [Test]
        public void All55Chars_HaveLv1_OnePerFace_NamedByFace_NoEffects()
        {
            // spec A §1:Lv1 按面分开命名 —— 攻击面沿用攻击关键词,五行面用五行面名(木 = 本命名,被动)
            var all = Playable();
            Assert.That(all.Count, Is.EqualTo(55));
            foreach (var d in all)
            {
                var lv1 = At(d, TraitSlot.Lv1);
                Assert.That(lv1.Count, Is.EqualTo(2), $"{d.Id} 应恰有两条 Lv1");
                var attack = lv1.Single(t => t.Face == TraitFace.Attack);
                var feature = lv1.Single(t => t.Face == TraitFace.Feature);
                Assert.That(attack.Name, Is.EqualTo(KeywordOf(d)), $"{d.Id} Lv1 攻名");
                Assert.That(feature.Name, Is.EqualTo(FeatureNameOf(d)), $"{d.Id} Lv1 五行面名");
                Assert.That(attack.Form, Is.EqualTo(TraitForm.Active), $"{d.Id} Lv1 攻形态");
                Assert.That(feature.Form, Is.EqualTo(d.Element == Element.Wood ? TraitForm.Passive : TraitForm.Active),
                    $"{d.Id} Lv1 五行面形态(木本命被动)");
                foreach (var t in lv1)
                {
                    Assert.That(t.Replaces.HasValue, Is.False, $"{d.Id} Lv1 不替换");
                    Assert.That(t.Effects.Count, Is.EqualTo(0), $"{d.Id} Lv1 效果在本体里,特性行只放名字");
                }
            }
        }

        [Test]
        public void All55Chars_HaveLv3_ReplacingLv1_NamedStrengthen()
        {
            foreach (var d in Playable())
            {
                var lv3 = At(d, TraitSlot.Lv3);
                // 2026-10-07(D2-0 Task 9,E8):木字生面行是本命强化(被动,Summon 0 + 强化后的被动 token)
                Assert.That(lv3.Count, Is.EqualTo(2), $"{d.Id} Lv3 行数");
                Assert.That(lv3.Any(t => t.Face == TraitFace.Attack), Is.True, $"{d.Id} 缺 Lv3 攻");
                Assert.That(lv3.Any(t => t.Face == TraitFace.Feature), Is.True, $"{d.Id} Lv3 五行面");
                foreach (var t in lv3)
                {
                    Assert.That(t.Replaces, Is.EqualTo(TraitSlot.Lv1), $"{d.Id} Lv3 替换 Lv1");
                    if (d.Element == Element.Wood && t.Face == TraitFace.Feature)
                    {
                        Assert.That(t.Form, Is.EqualTo(TraitForm.Passive), $"{d.Id} Lv3 本命强化是被动");
                        Assert.That(t.Name, Is.EqualTo(FeatureNameOf(d) + "·强化"), $"{d.Id} Lv3 本命强化名");
                        Assert.That(t.Effects.Count, Is.EqualTo(1), $"{d.Id} Lv3 本命强化 = 一条 Summon 0");
                        Assert.That(t.Effects[0].Kind, Is.EqualTo(EffectKind.Summon), $"{d.Id} Lv3 本命强化");
                        Assert.That(t.Effects[0].Value, Is.EqualTo(0), $"{d.Id} Lv3 本命强化不动血量");
                        continue;
                    }
                    Assert.That(t.Form, Is.EqualTo(TraitForm.Active), $"{d.Id} Lv3 主动");
                    string lv1Name = t.Face == TraitFace.Attack ? KeywordOf(d) : FeatureNameOf(d);
                    Assert.That(t.Name, Is.EqualTo(lv1Name + "·强化"), $"{d.Id} Lv3 名 = 同面 Lv1 名 + ·强化");
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} Lv3 要有效果");
                    var body = BodyOf(d, t.Face);
                    foreach (var e in t.Effects)
                        Assert.That(body.Any(b => b.Kind == e.Kind), Is.True,
                            $"{d.Id} Lv3 {t.Face} 的 {e.Kind} 在本体同面里找不到同 Kind(会被追加而不是替换)");
                }
            }
        }

        [Test]
        public void Fire_Lv3_BurnPlusOne_FeatureWeakenPlusFiveAndOneTurn()
        {
            foreach (var d in OfElement(Element.Fire))
            {
                int n = BurnStacks(d.Rarity);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { FirstOf(d.AttackEffects, IsBurn).Kind }), $"{d.Id} 攻");
                Assert.That(a[0].Value, Is.EqualTo(n + 1), $"{d.Id} 攻 灼 N+1");
                // spec A §4.1:慑·强化只强化主手段 —— 慑 +5%、回合 +1;灼恒 1 不动
                Assert.That(f.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Weaken }), $"{d.Id} 慑·强化");
                var bodyWeaken = d.Effects.Single(e => e.Kind == EffectKind.Weaken);
                Assert.That(f[0].Value, Is.EqualTo(bodyWeaken.Value + 5), $"{d.Id} 慑 +5%");
                Assert.That(f[0].Turns, Is.EqualTo(bodyWeaken.Turns + 1), $"{d.Id} 慑回合 +1");
                Assert.That(f[0].Pick, Is.EqualTo(bodyWeaken.Pick), $"{d.Id} 慑选择器随本体");
            }
            // 焱:攻击面改全体(spec A §3),Lv3 攻 BurnAll 4(D16);五行面全体慑
            var yan = Graph.Get("焱");
            var ya = At(yan, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
            Assert.That((ya[0].Kind, ya[0].Value), Is.EqualTo((EffectKind.BurnAll, 4)));
            var yf = At(yan, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
            Assert.That(yf[0].Pick, Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Metal_Lv3_MoralePlusOne_FeatureBlockPlusOneCounterPlusTen()
        {
            foreach (var d in OfElement(Element.Metal))
            {
                int n = MoraleStacks(d.Rarity);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Morale }), $"{d.Id} 攻");
                Assert.That(a[0].Value, Is.EqualTo(n + 1), $"{d.Id} 攻 战意 N+1");
                // spec A §4.2:格挡·强化 = 格挡 +1 次、反击 +10%;战意恒 1 不动
                var block = d.Effects.Single(e => e.Kind == EffectKind.Block);
                Assert.That(f.Select(e => e.Kind).ToList(), Is.EqualTo(new[] { EffectKind.Block }), $"{d.Id} 格挡·强化");
                Assert.That(f[0].Value, Is.EqualTo(block.Value + 1), $"{d.Id} 格挡 +1 次");
                Assert.That(f[0].CounterPercent, Is.EqualTo(block.CounterPercent + 10), $"{d.Id} 反击 +10%");
            }
        }

        [Test]
        public void Water_Lv3_ControlPlusOneTurn_FeatureStrengthensMode()
        {
            foreach (var d in OfElement(Element.Water))
            {
                var ctrl = d.AttackEffects.Single(e => e.Kind == EffectKind.Freeze || e.Kind == EffectKind.Slow);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Count, Is.EqualTo(1), $"{d.Id} 攻");
                Assert.That(a[0].Kind, Is.EqualTo(ctrl.Kind), $"{d.Id} 攻 控制种类随本体");
                Assert.That(a[0].Value, Is.EqualTo(ctrl.Value + 1), $"{d.Id} 攻 控制 +1 回合");
                Assert.That(a[0].Pick, Is.EqualTo(ctrl.Pick), $"{d.Id} 攻 选择器随本体");
                // spec A §4.3:涓 回合 +1;涌 治疗 +20%;苏 复活生命 P +20
                Assert.That(f.Count, Is.EqualTo(1), $"{d.Id} 润");
                var main = d.Effects[0];
                Assert.That(f[0].Kind, Is.EqualTo(main.Kind), $"{d.Id} Lv3 替换本体主手段");
                switch (WaterMode[d.Id])
                {
                    case "涓":
                        Assert.That(f[0].Value, Is.EqualTo(main.Value), $"{d.Id} 涓 每回合量照本体");
                        Assert.That(f[0].Turns, Is.EqualTo(main.Turns + 1), $"{d.Id} 涓 回合 +1");
                        break;
                    case "涌":
                        Assert.That(f[0].Value, Is.EqualTo((main.Value * 12 + 5) / 10), $"{d.Id} 涌 治疗 +20%");
                        break;
                    default:
                        Assert.That(f[0].Value, Is.EqualTo(main.Value), $"{d.Id} 苏 复活只数照本体");
                        Assert.That(f[0].ReviveHpPercent, Is.EqualTo(main.ReviveHpPercent + 20), $"{d.Id} 苏 P +20");
                        break;
                }
            }
            Assert.That(At(Graph.Get("溃"), TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects[0].Pick,
                Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Earth_Lv3_AttackFourTurns_FeatureTurnsPlusOne()
        {
            foreach (var d in OfElement(Element.Earth))
            {
                var ab = d.AttackEffects.Single(e => e.Kind == EffectKind.ArmorBreak);
                var db = d.Effects.Single(e => e.Kind == EffectKind.DefenseBuff);
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                var f = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Feature).Effects;
                Assert.That(a.Count, Is.EqualTo(1), $"{d.Id} 攻");
                Assert.That(a[0].Kind, Is.EqualTo(EffectKind.ArmorBreak));
                Assert.That(a[0].Value, Is.EqualTo(ab.Value), $"{d.Id} 破甲点数照本体");
                Assert.That(a[0].Turns, Is.EqualTo(4), $"{d.Id} 破甲 4 回合");
                Assert.That(a[0].Pick, Is.EqualTo(ab.Pick), $"{d.Id} 选择器随本体");
                Assert.That(f.Count, Is.EqualTo(1), $"{d.Id} 固");
                Assert.That(f[0].Kind, Is.EqualTo(EffectKind.DefenseBuff));
                Assert.That(f[0].Value, Is.EqualTo(db.Value), $"{d.Id} 护甲点数照本体");
                Assert.That(f[0].Turns, Is.EqualTo(db.Turns + 1), $"{d.Id} 护甲·强化 回合 +1(spec A §4.4)");
            }
            Assert.That(At(Graph.Get("崩"), TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects[0].Pick,
                Is.EqualTo(EffectPick.All));
        }

        [Test]
        public void Wood_Lv3_SeedThreeTurns_AttackRow()
        {
            foreach (var d in OfElement(Element.Wood))
            {
                // 2026-10-07(D2-0 Task 9,E8):另有一条生面本命强化行(WoodNatureDataTests 逐字断言)
                Assert.That(At(d, TraitSlot.Lv3).Count(t => t.Face == TraitFace.Feature), Is.EqualTo(1), $"{d.Id} Lv3 生面本命强化");
                var a = At(d, TraitSlot.Lv3).Single(t => t.Face == TraitFace.Attack).Effects;
                Assert.That(a.Count, Is.EqualTo(1));
                // spec A §5:花攻击面也是种(Seed 9),Lv3 与其他木字一致 = 种 3 回合
                var seed = d.AttackEffects.Single(e => e.Kind == EffectKind.Seed);
                Assert.That(a[0].Kind, Is.EqualTo(EffectKind.Seed));
                Assert.That(a[0].Value, Is.EqualTo(seed.Value), $"{d.Id} 种的量照本体");
                Assert.That(a[0].Turns, Is.EqualTo(3), $"{d.Id} 种 3 回合");
            }
        }

        [Test]
        public void EveryPoolCell_FromSpecSection9_IsPresent_AndNothingElseBeyondLv3()
        {
            var expected = new Dictionary<string, List<(TraitSlot slot, TraitFace face, string name)>>();
            foreach (var cell in PoolCells.Concat(ExclusiveCells))
            {
                var p = cell.Split(' ');
                var slot = (TraitSlot)int.Parse(p[1].Substring(2));
                var face = (TraitFace)System.Enum.Parse(typeof(TraitFace), p[2]);
                if (!expected.TryGetValue(p[0], out var list))
                    expected[p[0]] = list = new List<(TraitSlot, TraitFace, string)>();
                list.Add((slot, face, p[3]));
            }
            foreach (var d in Playable())
            {
                var real = d.Id == "" ? "𣛧" : d.Id == "" ? "𨰻" : d.Id;
                var want = expected.TryGetValue(real, out var w) ? w : new List<(TraitSlot, TraitFace, string)>();
                var got = d.Traits.Where(t => (int)t.Slot >= 4).ToList();
                foreach (var (slot, face, name) in want)
                    Assert.That(got.Any(t => t.Slot == slot && t.Face == face && t.Name == name), Is.True,
                        $"{real} 缺 {slot}/{face}/{name}");
                Assert.That(got.Count, Is.EqualTo(want.Count), $"{real} Lv4+ 特性数应等于 spec §9 池格数 + 已落地的专属格数");
            }
            Assert.That(expected.Values.Sum(l => l.Count), Is.EqualTo(PoolCells.Length + ExclusiveCells.Length));
        }

        [Test]
        public void PoolTraits_HaveSlotFormAndPoolShape()
        {
            foreach (var d in Playable())
                foreach (var t in d.Traits.Where(t => (int)t.Slot >= 4 && !IsExclusive(d, t)))
                {
                    if (t.Slot == TraitSlot.Lv4)
                    {
                        // U6:克敌 / 破敌 / 补刀在不选敌的五行面上拆成两条单面(攻击面原条目 + 五行面精进)
                        bool split = t.Name == "克敌" || t.Name == "破敌" || t.Name == "补刀";
                        if (split && t.Face != TraitFace.Both)
                            Assert.That(d.Traits.Count(x => x.Slot == TraitSlot.Lv4 && x.Name == t.Name), Is.EqualTo(2), $"{d.Id} {t.Name} 拆成两条");
                        else
                            Assert.That(t.Face, Is.EqualTo(TraitFace.Both), $"{d.Id} {t.Name}");
                        Assert.That(t.Form, Is.EqualTo(TraitForm.Passive), $"{d.Id} {t.Name}");
                    }
                    else if (t.Slot == TraitSlot.Lv6)
                        Assert.That(t.Form, Is.EqualTo(TraitForm.Passive), $"{d.Id} {t.Name}");
                    else
                        Assert.That(t.Face, Is.Not.EqualTo(TraitFace.Both), $"{d.Id} {t.Name} Lv5/Lv8 要有作用面");
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} {t.Name}");
                }
        }

        [Test]
        public void FireExclusiveTraits_FormAndTrigger_MatchSpec()
        {
            foreach (var d in OfElement(Element.Fire))
                foreach (var t in d.Traits.Where(t => IsExclusive(d, t)))
                {
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} {t.Name} 要有效果");
                    string at = $"{d.Id} {t.Slot}/{t.Face} {t.Name}";
                    switch (t.Name)
                    {
                        case "双焰": case "火山":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnDismantle)), at);
                            Assert.That(t.PartChar, Is.EqualTo(t.Name == "双焰" ? "火" : "山"), at);
                            Assert.That(t.PartCount, Is.EqualTo(t.Name == "双焰" ? 2 : 1), at);
                            break;
                        case "三焰":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnCompose)), at);
                            break;
                        case "连爆":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnKill)), at);
                            Assert.That(t.MaxPerCast, Is.EqualTo(2), at + "(最多 2 次)");
                            break;
                        default:
                            // Lv4 / Lv6 是被动,Lv5 / Lv8 是主动;都是出字时机
                            bool passive = t.Slot == TraitSlot.Lv4 || t.Slot == TraitSlot.Lv6;
                            Assert.That(t.Form, Is.EqualTo(passive ? TraitForm.Passive : TraitForm.Active), at);
                            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.Cast), at);
                            break;
                    }
                }
        }

        [Test]
        public void FireExclusive_V4Split_FeatureRowIsSameTierRefine()
        {
            // V4:焚风(橙 2.5)燃面 = 精进 round(10 × 档位系数),无条件、scope All
            var refine = new Dictionary<string, int> { ["焚风"] = 25 };
            foreach (var (id, name) in new[] { ("焚", "焚风") })
            {
                var d = Graph.Get(id);
                var rows = d.Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == name).ToList();
                Assert.That(rows.Select(t => t.Face).OrderBy(f => f).ToList(),
                    Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), $"{name} 拆成两条单面");
                var feature = rows.Single(t => t.Face == TraitFace.Feature).Effects.Single();
                Assert.That(feature.Kind, Is.EqualTo(EffectKind.Amplify));
                Assert.That(feature.Value, Is.EqualTo(refine[name]), $"{name} 燃面 = 同档精进");
                Assert.That(feature.Scope, Is.EqualTo(AmpScope.All));
                Assert.That(feature.OnlyIf, Is.EqualTo(DamageCondition.None), $"{name} 燃面无条件");
                var attack = rows.Single(t => t.Face == TraitFace.Attack).Effects.Single();
                Assert.That(attack.OnlyIf, Is.Not.EqualTo(DamageCondition.None), $"{name} 攻击面保留原条件");
            }
        }

        [Test]
        public void FireExclusive_QiSheng_SingleBothRow_ConditionalAmplifyAll()
        {
            // Ruling 9:G3 让 scope All 在燃面也加本字灼的火力,V4「燃面不起作用」的前提不成立 → 气盛回到单行两面
            var rows = Graph.Get("烈").Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == "气盛").ToList();
            Assert.That(rows.Count, Is.EqualTo(1), "气盛只有一行");
            Assert.That(rows[0].Face, Is.EqualTo(TraitFace.Both));
            var e = rows[0].Effects.Single();
            Assert.That(e.Kind, Is.EqualTo(EffectKind.Amplify));
            Assert.That(e.Value, Is.EqualTo(20));
            Assert.That(e.Scope, Is.EqualTo(AmpScope.All));
            Assert.That(e.OnlyIf, Is.EqualTo(DamageCondition.PlayerHpAbove70));
        }

        [Test]
        public void MetalExclusiveTraits_FormAndTrigger_MatchSpec()
        {
            int seen = 0;
            foreach (var d in OfElement(Element.Metal))
                foreach (var t in d.Traits.Where(t => IsExclusive(d, t)))
                {
                    seen++;
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), $"{d.Id} {t.Name} 要有效果");
                    string at = $"{d.Id} {t.Slot}/{t.Face} {t.Name}";
                    switch (t.Name)
                    {
                        case "双金":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnDismantle)), at);
                            Assert.That(t.PartChar, Is.Null, at + "(即时类,不挂印记)");
                            break;
                        case "铁则":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnExecute)), at);
                            break;
                        case "割取": case "金汇":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnKill)), at);
                            break;
                        case "放血" when t.Face == TraitFace.Attack:
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnCrit)), at);
                            break;
                        default:
                            bool passive = t.Slot == TraitSlot.Lv4 || t.Slot == TraitSlot.Lv6;
                            Assert.That(t.Form, Is.EqualTo(passive ? TraitForm.Passive : TraitForm.Active), at);
                            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.Cast), at);
                            break;
                    }
                }
            Assert.That(seen, Is.EqualTo(45), "金系专属 43 格 45 行(spec A +4 条 Lv6)");
        }

        [Test]
        public void MetalExclusive_Q20Split_FeatureRowIsSameTierRefine()
        {
            // Q20:剁骨(紫 1.75)、放血(橙 2.5)铠面 = 精进 round(10 × 档位系数),无条件、scope All
            foreach (var (id, name, refine) in new[] { ("剁", "剁骨", 18), ("刲", "放血", 25) })
            {
                var rows = Graph.Get(id).Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == name).ToList();
                Assert.That(rows.Select(t => t.Face).OrderBy(f => f).ToList(),
                    Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), $"{name} 拆成两条单面");
                var feature = rows.Single(t => t.Face == TraitFace.Feature);
                Assert.That((feature.Form, feature.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.Cast)), $"{name} 铠面");
                var e = feature.Effects.Single();
                Assert.That(e.Kind, Is.EqualTo(EffectKind.Amplify));
                Assert.That(e.Value, Is.EqualTo(refine), $"{name} 铠面 = 同档精进");
                Assert.That(e.Scope, Is.EqualTo(AmpScope.All));
                Assert.That(e.OnlyIf, Is.EqualTo(DamageCondition.None), $"{name} 铠面无条件");
            }
            var chop = Graph.Get("剁").Traits.Single(t => t.Slot == TraitSlot.Lv4 && t.Face == TraitFace.Attack).Effects.Single();
            Assert.That(chop.Kind, Is.EqualTo(EffectKind.Reshape));
            Assert.That(chop.PerHitFrom, Is.EqualTo(2), "Q8:从第 2 击起");
            Assert.That(chop.PerHit.Single().Kind, Is.EqualTo(EffectKind.Bleed));
            var bleed = Graph.Get("刲").Traits.Single(t => t.Slot == TraitSlot.Lv4 && t.Face == TraitFace.Attack).Effects.Single();
            Assert.That((bleed.Kind, bleed.Value, bleed.Turns), Is.EqualTo((EffectKind.Bleed, 50, 2)));
        }

        [Test]
        public void WaterExclusiveTraits_FormAndTrigger_MatchSpec()
        {
            int seen = 0;
            foreach (var d in OfElement(Element.Water))
                foreach (var t in d.Traits.Where(t => IsExclusive(d, t)))
                {
                    seen++;
                    string at = $"{d.Id} {t.Slot}/{t.Face} {t.Name}";
                    // Q1:湮灭保留原文(敌人死亡效果上线后才生效),导出空效果行;其余都要有效果
                    if (t.Name == "湮灭")
                        Assert.That(t.Effects.Count, Is.EqualTo(0), at + " 空效果行");
                    else
                        Assert.That(t.Effects.Count, Is.GreaterThan(0), at + " 要有效果");
                    bool passive = t.Slot == TraitSlot.Lv4 || t.Slot == TraitSlot.Lv6;
                    Assert.That(t.Form, Is.EqualTo(passive ? TraitForm.Passive : TraitForm.Active), at);
                    Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.Cast), at);
                }
            Assert.That(seen, Is.EqualTo(57), "水系专属 52 格 57 行(spec A +9 条 Lv6、D8 甘露)");
        }

        [Test]
        public void WaterExclusive_Q2Split_IdleFaceRowIsSameTierRefine()
        {
            // Q2:空转面 = 精进 round(10 × 档位系数),无条件、scope All —— 金 2.1 → 21、橙 2.5 → 25、红 3 → 30
            foreach (var (id, name, idle, refine) in new[]
            {
                ("冰", "冰封", TraitFace.Feature, 21), ("沐", "沐恩", TraitFace.Attack, 21),
                ("淋", "淋漓", TraitFace.Feature, 25), ("淼", "汪洋", TraitFace.Attack, 25),
                ("㵘", "怀山", TraitFace.Feature, 30),
            })
            {
                var rows = Graph.Get(id).Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == name).ToList();
                Assert.That(rows.Select(t => t.Face).OrderBy(f => f).ToList(),
                    Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), $"{name} 拆成两条单面");
                var e = rows.Single(t => t.Face == idle).Effects.Single();
                Assert.That(e.Kind, Is.EqualTo(EffectKind.Amplify), $"{name} 空转面");
                Assert.That(e.Value, Is.EqualTo(refine), $"{name} 空转面 = 同档精进");
                Assert.That(e.Scope, Is.EqualTo(AmpScope.All));
                Assert.That(e.OnlyIf, Is.EqualTo(DamageCondition.None), $"{name} 空转面无条件");
                Assert.That(rows.Single(t => t.Face != idle).Effects.Single().Kind, Is.Not.EqualTo(EffectKind.Amplify),
                    $"{name} 生效面保留原条目");
            }
        }

        [Test]
        public void EarthExclusiveTraits_FormAndTrigger_MatchSpec()
        {
            int seen = 0;
            foreach (var d in OfElement(Element.Earth))
                foreach (var t in d.Traits.Where(t => IsExclusive(d, t)))
                {
                    seen++;
                    string at = $"{d.Id} {t.Slot}/{t.Face} {t.Name}";
                    Assert.That(t.Effects.Count, Is.GreaterThan(0), at + " 要有效果");
                    switch (t.Name)
                    {
                        case "叠土":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnDismantle)), at);
                            Assert.That(t.PartChar, Is.Null, at + "(即时类,不挂印记)");
                            break;
                        case "三土":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnCompose)), at);
                            Assert.That(t.PartChar, Is.Null, at + "(即时类,不挂印记)");
                            break;
                        case "粉身碎骨":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnKill)), at);
                            break;
                        default:
                            bool passive = t.Slot == TraitSlot.Lv4 || t.Slot == TraitSlot.Lv6;
                            Assert.That(t.Form, Is.EqualTo(passive ? TraitForm.Passive : TraitForm.Active), at);
                            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.Cast), at);
                            break;
                    }
                    Assert.That(t.MaxPerBattle, Is.EqualTo(t.Name == "杜门不出" ? 1 : 0), at + " 每场次数(E35)");
                }
            Assert.That(seen, Is.EqualTo(41), "土系专属 39 格 41 行(spec A +4 条 Lv6)");
        }

        [Test]
        public void EarthExclusive_V4Split_TaJiTwoMeanings_ShenHouIdleAttackIsRedRefine()
        {
            var taji = Graph.Get("塔").Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == "塔基").ToList();
            Assert.That(taji.Select(t => t.Face).OrderBy(f => f).ToList(),
                Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), "塔基拆成两条单面");
            var strike = taji.Single(t => t.Face == TraitFace.Attack).Effects.Single();
            Assert.That((strike.Kind, strike.ArmorStrikePercent), Is.EqualTo((EffectKind.Reshape, 300)), "攻面镇压");
            var shield = taji.Single(t => t.Face == TraitFace.Feature).Effects.Single();
            Assert.That((shield.Kind, shield.ArmorShieldPercent), Is.EqualTo((EffectKind.Reshape, 300)), "固面护盾 + 护甲 × 3(Q11)");

            var shenhou = Graph.Get("㙓").Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == "深厚").ToList();
            Assert.That(shenhou.Select(t => t.Face).OrderBy(f => f).ToList(),
                Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), "深厚拆成两条单面");
            var idle = shenhou.Single(t => t.Face == TraitFace.Attack).Effects.Single();
            Assert.That((idle.Kind, idle.Value, idle.Scope, idle.OnlyIf),
                Is.EqualTo((EffectKind.Amplify, 30, AmpScope.All, DamageCondition.None)), "攻面空转 = 红档精进 30");
            var turns = shenhou.Single(t => t.Face == TraitFace.Feature).Effects.Single();
            Assert.That((turns.Kind, turns.Value, turns.AugmentKind, turns.AugmentField),
                Is.EqualTo((EffectKind.Augment, 4, EffectKind.DefenseBuff, AugmentField.Turns)), "固面护甲回合 +4(= ×2)");
        }

        [Test]
        public void WoodExclusiveTraits_FormAndTrigger_MatchSpec()
        {
            int seen = 0;
            foreach (var d in OfElement(Element.Wood))
                foreach (var t in d.Traits.Where(t => IsExclusive(d, t)))
                {
                    seen++;
                    string at = $"{d.Id} {t.Slot}/{t.Face} {t.Name}";
                    // 坚心保留原文(敌人还没有控制木灵的能力),导出空效果行;其余都要有效果
                    if (t.Name == "坚心")
                        Assert.That(t.Effects.Count, Is.EqualTo(0), at + " 空效果行");
                    else
                        Assert.That(t.Effects.Count, Is.GreaterThan(0), at + " 要有效果");
                    switch (t.Name)
                    {
                        case "双木":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnDismantle)), at);
                            Assert.That(t.PartChar, Is.Null, at + "(即时类,不挂印记)");
                            break;
                        case "三木":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnCompose)), at);
                            Assert.That(t.PartChar, Is.Null, at + "(即时类,不挂印记)");
                            break;
                        case "折桂": case "森生":
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.OnKill)), at);
                            break;
                        case "林风": case "森风": case "浮藻": case "木疾":
                            // spec A §4.5 / D22:速度挪到五行面 Lv5,形态被动(Lv5 = 主动的例外)
                            Assert.That((t.Form, t.Trigger), Is.EqualTo((TraitForm.Passive, TraitTrigger.Cast)), at);
                            Assert.That(t.Slot, Is.EqualTo(TraitSlot.Lv5), at);
                            Assert.That(t.Effects.Single().Passive.Speed, Is.EqualTo(150), at + " 速度 150");
                            break;
                        default:
                            bool passive = t.Slot == TraitSlot.Lv4 || t.Slot == TraitSlot.Lv6;
                            Assert.That(t.Form, Is.EqualTo(passive ? TraitForm.Passive : TraitForm.Active), at);
                            Assert.That(t.Trigger, Is.EqualTo(TraitTrigger.Cast), at);
                            break;
                    }
                    Assert.That(t.MaxPerBattle, Is.EqualTo(t.Name == "折桂" ? 1 : 0), at + " 每场次数(E49)");
                }
            Assert.That(seen, Is.EqualTo(58), "木系专属 53 格 58 行(51 − 丛林 / 丛藻 / 独木生林 + 林风 / 森风 / 浮藻 / 木疾 + 6 条 Lv6)");
        }

        [Test]
        public void WoodExclusive_V4Split_FeatureIsNature_AttackIsSameTierRefine()
        {
            // V4:生面 = 本命追加(`Summon 1`(0 血/攻 0) + 本命字段,E37);攻面 = 同档精进 round(10 × 档位系数):紫 18、橙 25、红 30
            foreach (var (id, name, refine) in new[]
                { ("楸", "秋实", 18), ("荆", "披荆", 18), ("桂", "馥郁", 25), ("藻", "水草", 25), ("\ue625", "参天之力", 30) })
            {
                var rows = Graph.Get(id).Traits.Where(t => t.Slot == TraitSlot.Lv4 && t.Name == name).ToList();
                Assert.That(rows.Select(t => t.Face).OrderBy(f => f).ToList(),
                    Is.EqualTo(new[] { TraitFace.Attack, TraitFace.Feature }.OrderBy(f => f).ToList()), $"{name} 拆成两条单面");
                var e = rows.Single(t => t.Face == TraitFace.Attack).Effects.Single();
                Assert.That((e.Kind, e.Value, e.Scope, e.OnlyIf),
                    Is.EqualTo((EffectKind.Amplify, refine, AmpScope.All, DamageCondition.None)), $"{name} 攻面 = 同档精进");
                var nature = rows.Single(t => t.Face == TraitFace.Feature).Effects.Single();
                Assert.That((nature.Kind, nature.Value, nature.Passive != null), Is.EqualTo((EffectKind.Summon, 0, true)),
                    $"{name} 生面 = 本命追加");
            }
        }

        private static EffectDef PoolEffect(string ch, TraitSlot slot, TraitFace face, string name, EffectKind kind)
        {
            var t = Graph.Get(Id(ch)).Traits.Single(x => x.Slot == slot && x.Face == face && x.Name == name);
            return t.Effects.First(e => e.Kind == kind);
        }

        [Test]
        public void PoolValues_ScaleByRarity_BaseTimesMultiplier()
        {
            // 白 1.0:利(白)Lv5 破甲 X=15 → 15;热(白)先声 X=15 → 15
            Assert.That(PoolEffect("利", TraitSlot.Lv5, TraitFace.Attack, "破甲", EffectKind.ArmorBreak).Value, Is.EqualTo(15));
            Assert.That(PoolEffect("热", TraitSlot.Lv4, TraitFace.Both, "先声", EffectKind.Amplify).Value, Is.EqualTo(15));
            // 绿 1.2:冻(绿)精进 10 → 12;垒(绿)劈山 ArmorBreak 15 → 18;藤(绿)沃土 30 → 36
            Assert.That(PoolEffect("冻", TraitSlot.Lv4, TraitFace.Both, "精进", EffectKind.Amplify).Value, Is.EqualTo(12));
            Assert.That(PoolEffect("垒", TraitSlot.Lv8, TraitFace.Attack, "劈山", EffectKind.ArmorBreak).Value, Is.EqualTo(18));
            Assert.That(PoolEffect("藤", TraitSlot.Lv5, TraitFace.Feature, "沃土", EffectKind.HealSummons).Value, Is.EqualTo(36));
            // 蓝 1.45:剿(蓝)化险 20 → 29;崩(蓝)破敌 15 → 22(21.75 入);堡(蓝)克敌 写死 15 不放大
            Assert.That(PoolEffect("剿", TraitSlot.Lv4, TraitFace.Both, "化险", EffectKind.Amplify).Value, Is.EqualTo(29));
            Assert.That(PoolEffect("崩", TraitSlot.Lv4, TraitFace.Attack, "破敌", EffectKind.Amplify).Value, Is.EqualTo(22));
            Assert.That(PoolEffect("堡", TraitSlot.Lv4, TraitFace.Attack, "克敌", EffectKind.Amplify).Value, Is.EqualTo(15));
            // 写死数字不放大:爆(绿)爆燃 +30%
            Assert.That(PoolEffect("爆", TraitSlot.Lv5, TraitFace.Attack, "爆燃", EffectKind.Amplify).Value, Is.EqualTo(30));
        }

        // ---- 终审 Critical:Lv6 被动(出字时机)的非修饰效果要进出字效果表 ----

        [Test]
        public void Lv6PassiveCastEffects_RealData_AreInCastEffects()
        {
            var beng = TraitRules.CastEffects(Graph.Get("崩"), CardFace.Attack, 8);
            Assert.That(beng.Any(e => e.Kind == EffectKind.Weaken && e.Pick == EffectPick.HitTargets), Is.True,
                "崩 Lv8 攻面应含余震(Weaken pick HitTargets)");
            Assert.That(TraitRules.CastEffects(Graph.Get("冷"), CardFace.Feature, 8)
                .Any(e => e.Kind == EffectKind.ShieldFromHeal), Is.True, "冷 Lv8 润面应含护持(ShieldFromHeal)");
            Assert.That(TraitRules.CastEffects(Graph.Get("冻"), CardFace.Attack, 8)
                .Any(e => e.Kind == EffectKind.Vulnerable), Is.True, "冻 Lv8 攻面应含冰缚(Vulnerable)");
            Assert.That(TraitRules.CastEffects(Graph.Get("箭"), CardFace.Feature, 8)
                .Any(e => e.Kind == EffectKind.Endure), Is.True, "箭 Lv8 生面应含扎根(Endure)");
            // 未解锁时不在
            Assert.That(TraitRules.CastEffects(Graph.Get("崩"), CardFace.Attack, 5)
                .Any(e => e.Kind == EffectKind.Weaken), Is.False, "Lv5 不解锁 Lv6");
        }
    }
}
