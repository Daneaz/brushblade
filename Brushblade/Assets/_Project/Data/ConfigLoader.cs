using System;
using System.Collections.Generic;
using Brushblade.Core;
using Newtonsoft.Json;

namespace Brushblade.Data
{
    /// <summary>配置表加载错误(fail fast,见 docs/architecture.md §3)。</summary>
    public sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>字表 JSON → RecipeGraph。schema 见 StreamingAssets/config/chars.json。</summary>
    public static class ConfigLoader
    {
        private sealed class CharsFileDto
        {
            public List<CharDto> Chars { get; set; }
        }

        private sealed class CharDto
        {
            public string Id { get; set; }
            public string Element { get; set; }
            public List<string> Recipe { get; set; }
            public List<EffectDto> Effects { get; set; }
            public List<EffectDto> AttackEffects { get; set; } // 拖到敌人身上出手时的替代效果(水/土)
            public string Rarity { get; set; }
            public string Pinyin { get; set; }
            public string Gloss { get; set; }
        }

        private sealed class EffectDto
        {
            public string Kind { get; set; }
            public int Value { get; set; }
            public bool DoubleVsBurning { get; set; }
            public bool PersistOnce { get; set; }
            public int Count { get; set; } = 1;        // 召唤:召几个
            public int Attack { get; set; }            // 召唤:攻击力
            public string SummonChar { get; set; } = "木"; // 召唤:显示字
            public int Turns { get; set; }             // 持续类:回合数(HoT 用)
            public bool TargetAll { get; set; }        // 治疗类:是否覆盖全部召唤物
            public int Pierce { get; set; }            // 穿透:本次攻击视目标护甲少 N 点(2026-08-12,E-b4)
            public SummonPassive Passive { get; set; }  // 召唤:被动(null = 无)
            public int SummonShield { get; set; }       // 召唤:出字给全场召唤物各 +N 盾(桂)
            public int ExecuteBelowPercent { get; set; } // 斩杀:目标 HP 低于此百分比时触发
            public bool ExecuteKills { get; set; }       // true = 直接击杀(Boss 免疫);false = 伤害 ×2
            public int HitCount { get; set; } = 1;  // 多段:伤害分几段打(剁 = 2)
            public bool Backline { get; set; }   // 偷袭:该发单体直伤无视敌方前排(2026-08-20)
            public string Shape { get; set; }      // 目标形状(2026-08-22):null = Single
            public int ShapePercent { get; set; } = 100; // 非主目标伤害百分比
            public int Shots { get; set; }         // 连发发数
        }

        private sealed class CampaignFileDto
        {
            public List<EnemyDto> Enemies { get; set; }
            public List<string> DropTable { get; set; }
            public List<ChapterDto> Chapters { get; set; }
            public List<EventDto> Events { get; set; }
            public int EventChance { get; set; }
            public EndlessDto Endless { get; set; }
            public Dictionary<string, string> BossSkills { get; set; }
        }

        private sealed class EndlessDto
        {
            public int BossEvery { get; set; } = 5;
            public float ScalePerDepth { get; set; } = 0.10f;
            public float BossScaleBonus { get; set; } = 1.25f;
            public List<BandDto> Bands { get; set; }
        }

        private sealed class BandDto
        {
            public string Name { get; set; }
            public int FromDepth { get; set; }
            public List<string> EnemyPool { get; set; }
            public List<string> BossPool { get; set; }
            public List<IdiomBossDto> IdiomBosses { get; set; }
            public List<string> RewardPool { get; set; }
            public int MilestoneInk { get; set; }
        }

        private sealed class IdiomBossDto
        {
            public string Chars { get; set; }
            public List<string> Elements { get; set; }
        }

        private sealed class EventDto
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public List<EventOptionDto> Options { get; set; }
        }

        private sealed class EventOptionDto
        {
            public string Label { get; set; }
            public int HpDelta { get; set; }
            public int Ink { get; set; }
            public int InkCost { get; set; }
            public int ComponentCost { get; set; }
            public string GainChar { get; set; }
            public List<string> GainComponents { get; set; }
            public int RandomComponents { get; set; }
            public List<string> GainCharChoices { get; set; }
            public int InkChancePercent { get; set; }
            public int MaxHpPercent { get; set; }
            public int MaxHpChancePercent { get; set; }
        }

        private sealed class ChapterDto
        {
            public string Name { get; set; }
            public float EnemyScale { get; set; } = 1f;
            public List<StageDto> Stages { get; set; }
            public List<string> RewardPool { get; set; }
            public List<string> BossPool { get; set; }
        }

        private sealed class StageDto
        {
            public List<List<string>> Encounters { get; set; }
            public bool Boss { get; set; }
        }

        /// <summary>解析章节战役 JSON(enemies/dropTable/chapters)为 CampaignConfig。</summary>
        public static CampaignConfig LoadCampaign(string json, RecipeGraph graph)
        {
            CampaignFileDto file;
            try
            {
                file = JsonConvert.DeserializeObject<CampaignFileDto>(json);
            }
            catch (JsonException e)
            {
                throw new ConfigException($"战役 JSON 解析失败:{e.Message}", e);
            }
            if (file?.Enemies == null || file.DropTable == null || file.Chapters == null)
                throw new ConfigException("战役 JSON 缺少 enemies / dropTable / chapters");
            if (file.Chapters.Count == 0)
                throw new ConfigException("战役至少需要一个章节");

            var bossSkills = ParseBossSkills(file.BossSkills);
            var enemyDefs = ParseEnemies(file.Enemies, bossSkills);

            foreach (var component in file.DropTable)
                if (!graph.TryGet(component, out _))
                    throw new ConfigException($"掉落表引用了字表中不存在的部件:{component}");

            var chapters = new List<ChapterDef>();
            foreach (var chapterDto in file.Chapters)
            {
                if (chapterDto.Stages == null || chapterDto.Stages.Count == 0)
                    throw new ConfigException($"章节「{chapterDto.Name}」没有关卡");
                foreach (var reward in chapterDto.RewardPool ?? new List<string>())
                    if (!graph.TryGet(reward, out _))
                        throw new ConfigException($"章节「{chapterDto.Name}」奖励池引用了不存在的字:{reward}");

                var bossPool = new List<EnemyDef>();
                foreach (var id in chapterDto.BossPool ?? new List<string>())
                {
                    if (!enemyDefs.TryGetValue(id, out var def))
                        throw new ConfigException($"章节「{chapterDto.Name}」Boss 池引用了未定义的敌人:{id}");
                    bossPool.Add(def);
                }

                var stages = new List<StageDef>();
                foreach (var stageDto in chapterDto.Stages)
                {
                    var encounters = new List<IReadOnlyList<EnemyDef>>();
                    foreach (var encounter in stageDto.Encounters ?? new List<List<string>>())
                    {
                        var group = new List<EnemyDef>();
                        foreach (var id in encounter)
                        {
                            if (id == "$Boss")
                            {
                                if (bossPool.Count == 0)
                                    throw new ConfigException($"章节「{chapterDto.Name}」使用了 $Boss 占位但未配置 bossPool");
                                group.Add(CampaignConfig.BossPlaceholder);
                                continue;
                            }
                            if (!enemyDefs.TryGetValue(id, out var def))
                                throw new ConfigException($"遭遇引用了未定义的敌人:{id}");
                            group.Add(def);
                        }
                        encounters.Add(group);
                    }
                    stages.Add(new StageDef { Encounters = encounters, Boss = stageDto.Boss });
                }

                chapters.Add(new ChapterDef
                {
                    Name = chapterDto.Name,
                    EnemyScale = chapterDto.EnemyScale,
                    Stages = stages,
                    RewardPool = chapterDto.RewardPool ?? new List<string>(),
                    BossPool = bossPool,
                });
            }

            var events = new List<EventDef>();
            foreach (var eventDto in file.Events ?? new List<EventDto>())
            {
                var options = new List<EventOption>();
                foreach (var optionDto in eventDto.Options ?? new List<EventOptionDto>())
                {
                    if (optionDto.GainChar != null && !graph.TryGet(optionDto.GainChar, out _))
                        throw new ConfigException($"奇遇「{eventDto.Id}」选项引用了不存在的字:{optionDto.GainChar}");
                    foreach (var component in optionDto.GainComponents ?? new List<string>())
                        if (!graph.TryGet(component, out _))
                            throw new ConfigException($"奇遇「{eventDto.Id}」选项引用了不存在的部件:{component}");
                    foreach (var choice in optionDto.GainCharChoices ?? new List<string>())
                        if (!graph.TryGet(choice, out _))
                            throw new ConfigException($"奇遇「{eventDto.Id}」任选字引用了不存在的字:{choice}");
                    options.Add(new EventOption
                    {
                        Label = optionDto.Label,
                        HpDelta = optionDto.HpDelta,
                        Ink = optionDto.Ink,
                        InkCost = optionDto.InkCost,
                        ComponentCost = optionDto.ComponentCost,
                        GainChar = optionDto.GainChar,
                        GainComponents = optionDto.GainComponents ?? new List<string>(),
                        RandomComponents = optionDto.RandomComponents,
                        GainCharChoices = optionDto.GainCharChoices ?? new List<string>(),
                        InkChancePercent = optionDto.InkChancePercent,
                        MaxHpPercent = optionDto.MaxHpPercent,
                        MaxHpChancePercent = optionDto.MaxHpChancePercent,
                    });
                }
                events.Add(new EventDef { Id = eventDto.Id, Text = eventDto.Text, Options = options });
            }

            return new CampaignConfig
            {
                Chapters = chapters,
                DropTable = file.DropTable,
                Events = events,
                EventChancePercent = file.EventChance,
                Endless = ParseEndless(file.Endless, enemyDefs, graph, bossSkills),
            };
        }

        /// <summary>解析无尽层段(20.3);无 endless 段返回 null。</summary>
        private static EndlessConfig ParseEndless(EndlessDto dto,
            Dictionary<string, EnemyDef> enemyDefs, RecipeGraph graph,
            Dictionary<string, BossSkill> bossSkills)
        {
            if (dto == null)
                return null;
            if (dto.Bands == null || dto.Bands.Count == 0)
                throw new ConfigException("endless 段缺少 bands");

            int previousFrom = 0;
            var bands = new List<BandDef>();
            foreach (var bandDto in dto.Bands)
            {
                if (bandDto.FromDepth <= previousFrom)
                    throw new ConfigException($"层段「{bandDto.Name}」fromDepth 必须严格递增");
                previousFrom = bandDto.FromDepth;

                var enemyPool = new List<EnemyDef>();
                foreach (var id in bandDto.EnemyPool ?? new List<string>())
                {
                    if (!enemyDefs.TryGetValue(id, out var def))
                        throw new ConfigException($"层段「{bandDto.Name}」杂兵池引用了未定义的敌人:{id}");
                    enemyPool.Add(def);
                }
                if (enemyPool.Count == 0)
                    throw new ConfigException($"层段「{bandDto.Name}」杂兵池为空");

                var bossPool = new List<EnemyDef>();
                foreach (var id in bandDto.BossPool ?? new List<string>())
                {
                    if (!enemyDefs.TryGetValue(id, out var def))
                        throw new ConfigException($"层段「{bandDto.Name}」Boss 池引用了未定义的敌人:{id}");
                    bossPool.Add(def);
                }
                if (bossPool.Count == 0)
                    throw new ConfigException($"层段「{bandDto.Name}」Boss 池为空");

                foreach (var reward in bandDto.RewardPool ?? new List<string>())
                    if (!graph.TryGet(reward, out _))
                        throw new ConfigException($"层段「{bandDto.Name}」字池引用了不存在的字:{reward}");

                var idiomBosses = new List<IdiomBossDef>();
                foreach (var idiomDto in bandDto.IdiomBosses ?? new List<IdiomBossDto>())
                {
                    if (idiomDto.Chars == null || idiomDto.Chars.Length != 4 ||
                        idiomDto.Elements == null || idiomDto.Elements.Count != 4)
                        throw new ConfigException($"层段「{bandDto.Name}」成语 Boss「{idiomDto.Chars}」需恰好四字四属性");
                    var elements = new List<Element>();
                    foreach (var name in idiomDto.Elements)
                    {
                        if (!Enum.TryParse<Element>(name, out var element))
                            throw new ConfigException($"成语 Boss「{idiomDto.Chars}」属性未知:{name}");
                        elements.Add(element);
                    }
                    var skills = new List<BossSkill>();
                    foreach (var c in idiomDto.Chars)
                        skills.Add(bossSkills.TryGetValue(c.ToString(), out var s) ? s : BossSkill.None);
                    idiomBosses.Add(new IdiomBossDef
                    {
                        Chars = idiomDto.Chars, Elements = elements, Skills = skills,
                    });
                }

                bands.Add(new BandDef
                {
                    Name = bandDto.Name,
                    FromDepth = bandDto.FromDepth,
                    EnemyPool = enemyPool,
                    BossPool = bossPool,
                    IdiomBossPool = idiomBosses,
                    RewardPool = bandDto.RewardPool ?? new List<string>(),
                    MilestoneInk = bandDto.MilestoneInk,
                });
            }
            if (bands[0].FromDepth != 1)
                throw new ConfigException("首个层段的 fromDepth 必须为 1");

            return new EndlessConfig
            {
                Bands = bands,
                BossEvery = dto.BossEvery,
                ScalePerDepth = dto.ScalePerDepth,
                BossScaleBonus = dto.BossScaleBonus,
            };
        }

        /// <summary>字 → Boss 技能表(spec 2026-07-28)。查不到的字一律 None,
        /// 所以往成语库加字永远不会崩,只是没技能。</summary>
        private static Dictionary<string, BossSkill> ParseBossSkills(Dictionary<string, string> dto)
        {
            var table = new Dictionary<string, BossSkill>();
            foreach (var pair in dto ?? new Dictionary<string, string>())
            {
                if (!Enum.TryParse<BossSkill>(pair.Value, out var skill))
                    throw new ConfigException($"字「{pair.Key}」的 Boss 技能未知:{pair.Value}");
                table[pair.Key] = skill;
            }
            return table;
        }

        private static BossSkill SkillFor(Dictionary<string, BossSkill> table, string phaseChar,
            string explicitSkill, string bossId)
        {
            if (!string.IsNullOrEmpty(explicitSkill))
            {
                if (!Enum.TryParse<BossSkill>(explicitSkill, out var declared))
                    throw new ConfigException($"Boss「{bossId}」阶段「{phaseChar}」技能未知:{explicitSkill}");
                return declared; // 显式字段优先于字表
            }
            return table.TryGetValue(phaseChar, out var looked) ? looked : BossSkill.None;
        }

        private static Dictionary<string, EnemyDef> ParseEnemies(List<EnemyDto> enemies,
            Dictionary<string, BossSkill> bossSkills)
        {
            var enemyDefs = new Dictionary<string, EnemyDef>();
            foreach (var dto in enemies)
            {
                if (string.IsNullOrEmpty(dto.Id))
                    throw new ConfigException("存在缺少 id 的敌人条目");
                if (enemyDefs.ContainsKey(dto.Id))
                    throw new ConfigException($"重复的敌人 id:{dto.Id}");
                if (!Enum.TryParse<Element>(dto.Element, out var element))
                    throw new ConfigException($"敌人「{dto.Id}」的属性未知:{dto.Element}");
                var ability = EnemyAbility.None;
                if (dto.Ability != null && !Enum.TryParse(dto.Ability, out ability))
                    throw new ConfigException($"敌人「{dto.Id}」的能力未知:{dto.Ability}");

                List<BossPhaseDef> phases = null;
                if (dto.Phases != null && dto.Phases.Count > 0)
                {
                    phases = new List<BossPhaseDef>();
                    foreach (var phase in dto.Phases)
                    {
                        if (string.IsNullOrEmpty(phase.Char) || phase.MaxHp <= 0)
                            throw new ConfigException($"Boss「{dto.Id}」存在非法阶段(char/maxHp)");
                        if (!Enum.TryParse<Element>(phase.Element, out var phaseElement))
                            throw new ConfigException($"Boss「{dto.Id}」阶段「{phase.Char}」属性未知:{phase.Element}");
                        phases.Add(new BossPhaseDef(phase.Char, phaseElement, phase.MaxHp, phase.Attack,
                            SkillFor(bossSkills, phase.Char, phase.Skill, dto.Id), phase.Defense));
                    }
                }
                var row = EnemyRow.Front;
                if (dto.Row != null && !Enum.TryParse(dto.Row, out row))
                    throw new ConfigException($"敌人「{dto.Id}」的站位未知:{dto.Row}");
                var range = AttackRange.Melee;
                if (dto.Range != null && !Enum.TryParse(dto.Range, out range))
                    throw new ConfigException($"敌人「{dto.Id}」的攻击距离未知:{dto.Range}");
                var focus = AttackFocus.Default;
                if (dto.Focus != null && !Enum.TryParse(dto.Focus, out focus))
                    throw new ConfigException($"敌人「{dto.Id}」的目标偏好未知:{dto.Focus}");

                enemyDefs[dto.Id] = new EnemyDef(dto.Id, element, dto.MaxHp, dto.Attack, ability, phases,
                    dto.Defense, speed: 0, row: row, range: range, focus: focus);
            }
            return enemyDefs;
        }

        private sealed class EnemyDto
        {
            public string Id { get; set; }
            public string Element { get; set; }
            public int MaxHp { get; set; }
            public int Attack { get; set; }
            public string Ability { get; set; }
            public List<PhaseDto> Phases { get; set; }
            public int Defense { get; set; }   // 护甲点数(2026-08-12,E-b4;缺省 0 = 无甲)
            public string Row { get; set; }    // "Front" / "Back";缺省前排
            public string Range { get; set; }  // "Melee" / "Ranged";缺省近战
            public string Focus { get; set; }  // "Default" / "Player";缺省 Default
        }

        private sealed class PhaseDto
        {
            public string Char { get; set; }
            public string Element { get; set; }
            public int MaxHp { get; set; }
            public int Attack { get; set; }
            public int Defense { get; set; }   // 该阶段的护甲点数(2026-08-12,E-b4)
            public string Skill { get; set; }
        }

        /// <summary>解析字表 JSON;结构非法/属性名未知/原料缺失/重复 id 抛 ConfigException。</summary>
        public static RecipeGraph LoadGraph(string json)
        {
            CharsFileDto file;
            try
            {
                file = JsonConvert.DeserializeObject<CharsFileDto>(json);
            }
            catch (JsonException e)
            {
                throw new ConfigException($"字表 JSON 解析失败:{e.Message}", e);
            }
            if (file?.Chars == null)
                throw new ConfigException("字表 JSON 缺少 chars 数组");

            var defs = new List<CharDef>();
            var ids = new HashSet<string>();
            foreach (var dto in file.Chars)
            {
                if (string.IsNullOrEmpty(dto.Id))
                    throw new ConfigException("存在缺少 id 的字条目");
                if (!ids.Add(dto.Id))
                    throw new ConfigException($"重复的字 id:{dto.Id}");

                defs.Add(new CharDef(dto.Id, ParseElement(dto),
                    dto.Recipe, ParseEffects(dto, dto.Effects), ParseRarity(dto),
                    dto.Pinyin, dto.Gloss, ParseEffects(dto, dto.AttackEffects)));
            }

            // fail fast 二次校验:配方引用必须已定义(完整校验在管线侧,4.9.6)
            foreach (var def in defs)
                foreach (var ingredient in def.Recipe)
                    if (!ids.Contains(ingredient))
                        throw new ConfigException($"字「{def.Id}」的配方引用了未定义的「{ingredient}」");

            return new RecipeGraph(defs);
        }

        private static CardRarity ParseRarity(CharDto dto)
        {
            if (dto.Rarity == null)
                return CardRarity.White;
            if (!Enum.TryParse<CardRarity>(dto.Rarity, out var rarity))
                throw new ConfigException($"字「{dto.Id}」的稀有度未知:{dto.Rarity}");
            return rarity;
        }

        private static Element? ParseElement(CharDto dto)
        {
            if (dto.Element == null)
                return null;
            if (!Enum.TryParse<Element>(dto.Element, out var element))
                throw new ConfigException($"字「{dto.Id}」的属性未知:{dto.Element}");
            return element;
        }

        private static IReadOnlyList<EffectDef> ParseEffects(CharDto dto, List<EffectDto> source)
        {
            if (source == null)
                return null;
            var effects = new List<EffectDef>();
            foreach (var effect in source)
            {
                if (!Enum.TryParse<EffectKind>(effect.Kind, out var kind))
                    throw new ConfigException($"字「{dto.Id}」的效果类型未知:{effect.Kind}");
                var shape = TargetShape.Single;
                // Enum.TryParse 单独用会放数字字符串过关(如 "3" 解析成 Skewer、"99" 解析成
                // 越界值),下游 Targeting.ExpandTargets 的 switch 对任何未定义的值都落到
                // `_ => false`——整张字会静默退化成单体、零报错。必须叠加 IsDefined 才拦得住。
                if (!string.IsNullOrEmpty(effect.Shape)
                    && (!Enum.TryParse(effect.Shape, out shape)
                        || !Enum.IsDefined(typeof(TargetShape), shape)))
                    throw new ConfigException($"字「{dto.Id}」的目标形状未知:{effect.Shape}");
                // 召唤被动的 Shape 是 Core 的 SummonPassive.Shape(TargetShape 枚举),不是上面这个
                // string 字段,走 Newtonsoft 整体反序列化——数字型越界值(如 "shape": 99)会被
                // Newtonsoft 直接接住塞进枚举底层 int,不报错,与上面这条 string 校验是同一个坑,
                // 只是入口不同(2026-08-22)。ExpandTargets 对任何未定义值都落到 `_ => false`,
                // 悄悄退化成单体——正是上面那条 player 侧校验存在的理由,这里补齐 summon 侧。
                if (effect.Passive != null && !Enum.IsDefined(typeof(TargetShape), effect.Passive.Shape))
                    throw new ConfigException($"字「{dto.Id}」的召唤被动目标形状未知:{effect.Passive.Shape}");
                effects.Add(new EffectDef(kind, effect.Value,
                    effect.DoubleVsBurning, effect.PersistOnce,
                    effect.Count, effect.Attack, effect.SummonChar,
                    effect.Turns, effect.TargetAll,
                    effect.Passive, effect.SummonShield,
                    effect.ExecuteBelowPercent, effect.ExecuteKills,
                    effect.HitCount, effect.Pierce, effect.Backline,
                    shape, effect.ShapePercent, effect.Shots));
            }
            return effects;
        }
    }
}
