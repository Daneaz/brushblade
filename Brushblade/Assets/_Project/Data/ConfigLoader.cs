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
        }

        private sealed class EffectDto
        {
            public string Kind { get; set; }
            public int Value { get; set; }
            public bool DoubleVsBurning { get; set; }
            public bool PersistOnce { get; set; }
        }

        private sealed class RunFileDto
        {
            public List<EnemyDto> Enemies { get; set; }
            public List<List<string>> Encounters { get; set; }
            public List<string> RewardPool { get; set; }
        }

        private sealed class EnemyDto
        {
            public string Id { get; set; }
            public string Element { get; set; }
            public int MaxHp { get; set; }
            public int Attack { get; set; }
        }

        /// <summary>解析敌人/遭遇/奖励池 JSON 为 RunConfig;引用未定义敌人或图谱外奖励字抛 ConfigException。</summary>
        public static RunConfig LoadRunConfig(string json, RecipeGraph graph)
        {
            RunFileDto file;
            try
            {
                file = JsonConvert.DeserializeObject<RunFileDto>(json);
            }
            catch (JsonException e)
            {
                throw new ConfigException($"敌人表 JSON 解析失败:{e.Message}", e);
            }
            if (file?.Enemies == null || file.Encounters == null || file.RewardPool == null)
                throw new ConfigException("敌人表 JSON 缺少 enemies / encounters / rewardPool");

            var enemyDefs = new Dictionary<string, EnemyDef>();
            foreach (var dto in file.Enemies)
            {
                if (string.IsNullOrEmpty(dto.Id))
                    throw new ConfigException("存在缺少 id 的敌人条目");
                if (enemyDefs.ContainsKey(dto.Id))
                    throw new ConfigException($"重复的敌人 id:{dto.Id}");
                if (!Enum.TryParse<Element>(dto.Element, out var element))
                    throw new ConfigException($"敌人「{dto.Id}」的属性未知:{dto.Element}");
                enemyDefs[dto.Id] = new EnemyDef(dto.Id, element, dto.MaxHp, dto.Attack);
            }

            var encounters = new List<IReadOnlyList<EnemyDef>>();
            foreach (var encounter in file.Encounters)
            {
                var group = new List<EnemyDef>();
                foreach (var id in encounter)
                {
                    if (!enemyDefs.TryGetValue(id, out var def))
                        throw new ConfigException($"遭遇引用了未定义的敌人:{id}");
                    group.Add(def);
                }
                encounters.Add(group);
            }

            foreach (var reward in file.RewardPool)
                if (!graph.TryGet(reward, out _))
                    throw new ConfigException($"奖励池引用了字表中不存在的字:{reward}");

            return new RunConfig { Encounters = encounters, RewardPool = file.RewardPool };
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
                    dto.Recipe, dto.ApCost, ParseEffects(dto)));
            }

            // fail fast 二次校验:配方引用必须已定义(完整校验在管线侧,4.9.6)
            foreach (var def in defs)
                foreach (var ingredient in def.Recipe)
                    if (!ids.Contains(ingredient))
                        throw new ConfigException($"字「{def.Id}」的配方引用了未定义的「{ingredient}」");

            return new RecipeGraph(defs);
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
