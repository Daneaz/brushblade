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
            public int ApCost { get; set; } = 1;
            public List<EffectDto> Effects { get; set; }
            public string Rarity { get; set; }
        }

        private sealed class EffectDto
        {
            public string Kind { get; set; }
            public int Value { get; set; }
            public bool DoubleVsBurning { get; set; }
            public bool PersistOnce { get; set; }
        }

        private sealed class CampaignFileDto
        {
            public List<EnemyDto> Enemies { get; set; }
            public List<string> DropTable { get; set; }
            public List<ChapterDto> Chapters { get; set; }
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

            var enemyDefs = ParseEnemies(file.Enemies);

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

            return new CampaignConfig { Chapters = chapters, DropTable = file.DropTable };
        }

        private static Dictionary<string, EnemyDef> ParseEnemies(List<EnemyDto> enemies)
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
                        phases.Add(new BossPhaseDef(phase.Char, phaseElement, phase.MaxHp, phase.Attack, phase.DamageTaken));
                    }
                }
                enemyDefs[dto.Id] = new EnemyDef(dto.Id, element, dto.MaxHp, dto.Attack, ability, phases);
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
        }

        private sealed class PhaseDto
        {
            public string Char { get; set; }
            public string Element { get; set; }
            public int MaxHp { get; set; }
            public int Attack { get; set; }
            public float DamageTaken { get; set; } = 1f;
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
                    dto.Recipe, dto.ApCost, ParseEffects(dto), ParseRarity(dto)));
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

        private static IReadOnlyList<EffectDef> ParseEffects(CharDto dto)
        {
            if (dto.Effects == null)
                return null;
            var effects = new List<EffectDef>();
            foreach (var effect in dto.Effects)
            {
                if (!Enum.TryParse<EffectKind>(effect.Kind, out var kind))
                    throw new ConfigException($"字「{dto.Id}」的效果类型未知:{effect.Kind}");
                effects.Add(new EffectDef(kind, effect.Value,
                    effect.DoubleVsBurning, effect.PersistOnce));
            }
            return effects;
        }
    }
}
