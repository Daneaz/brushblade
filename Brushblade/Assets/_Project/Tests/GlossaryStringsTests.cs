using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 5:词条文案与拼句模板的对账(附录 §C 3.5 第 3、4 条),读真实字符串表。
    ///
    /// · 每个 <see cref="GlossaryTerm"/> 都有 <c>term.&lt;id&gt;.name</c> / <c>.desc</c>(id = 枚举名转 snake_case)。
    ///   这里按约定拼 key 不受字面量扫描约束;「不是孤儿」那个方向由表现层 GlossaryText 的字面量 switch 负责。
    /// · 词表按表现层同一个入口 <see cref="TermMarkup.BuildLexicon"/> 从表里拼(显示名 + 别名 + 排除上下文),
    ///   遍历 <c>char.effect.*</c> / <c>char.passive.*</c> / <c>char.shape.*</c> 模板:「一个术语都扫不出」的模板列成白名单快照 ——
    ///   新增模板时会红,逼人确认它是真没有术语,还是用了词表认不得的新写法(附录 §C 风险 1:措辞与词表漂移)。
    ///
    /// ⚠ 仓库根只能用 TestContext.CurrentContext.TestDirectory(见 StringsTableTests)。</summary>
    public sealed class GlossaryStringsTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Brushblade")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "找不到含 Brushblade/ 的仓库根");
            return dir;
        }

        [SetUp]
        public void LoadRealTable() => Strings.Load(File.ReadAllText(Path.Combine(RepoRoot().FullName,
            "Brushblade", "Assets", "StreamingAssets", "config", "strings.zh-CN.json")));

        private static IEnumerable<GlossaryTerm> AllTerms() => Enum.GetValues(typeof(GlossaryTerm)).Cast<GlossaryTerm>();

        /// <summary>FrostResist → frost_resist。</summary>
        private static string Id(GlossaryTerm term) => Regex.Replace(term.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

        private static string Opt(string key) => Strings.Keys.Contains(key) ? Strings.T(key) : null;

        private static IReadOnlyList<TermSurface> RealLexicon() => TermMarkup.BuildLexicon(
            AllTerms().Select(t => (t, Strings.T($"term.{Id(t)}.name"), Opt($"term.{Id(t)}.alias"))));

        [Test]
        public void EveryTerm_HasNameAndDesc()
        {
            var missing = new List<string>();
            foreach (var t in AllTerms())
                foreach (var part in new[] { "name", "desc" })
                {
                    string key = $"term.{Id(t)}.{part}";
                    if (!Strings.Keys.Contains(key) || string.IsNullOrWhiteSpace(Strings.T(key))) missing.Add(key);
                }
            Assert.That(missing, Is.Empty, "缺词条文案:\n" + string.Join("\n", missing));
        }

        [Test]
        public void TermKeys_OnlyKnownIds()
        {
            var ids = new HashSet<string>(AllTerms().Select(Id));
            var stray = Strings.Keys.Where(k => k.StartsWith("term.", StringComparison.Ordinal))
                .Where(k => !ids.Contains(k.Split('.')[1])).OrderBy(k => k).ToArray();
            Assert.That(stray, Is.Empty, "term.* 里有枚举外的 id:\n" + string.Join("\n", stray));
        }

        [Test]
        public void EveryTerm_NameScansBackToItself()
        {
            var lexicon = RealLexicon();
            foreach (var t in AllTerms())
            {
                var scan = TermMarkup.Scan(Strings.T($"term.{Id(t)}.name"), lexicon);
                Assert.That(scan.Terms, Is.EqualTo(new[] { t }), t.ToString());
            }
        }

        /// <summary>C-Q2 / C-Q3 改过的模板:改完术语必须扫得出来(不改就永远点不到)。</summary>
        [Test]
        public void ChangedTemplates_CarryTheirTerms()
        {
            var lexicon = RealLexicon();
            var expected = new (string Key, GlossaryTerm Term)[]
            {
                ("char.effect.opening", GlossaryTerm.Carry),
                ("char.effect.opening.band", GlossaryTerm.Band),
                ("char.effect.damagecut", GlossaryTerm.DamageCut),
                ("char.effect.counterboost", GlossaryTerm.CounterBoost),
                ("char.shape.volley", GlossaryTerm.AreaScatter),
                ("char.effect.healovertime.single", GlossaryTerm.Hot),
                ("char.effect.defensebuff", GlossaryTerm.Armor),
            };
            foreach (var (key, term) in expected)
            {
                var terms = TermMarkup.Scan(Strings.T(key), lexicon).Terms;
                Assert.That(terms.Contains(term), Is.True, $"{key} 扫不出 {term}");
            }
            Assert.That(TermMarkup.Scan(Strings.T("char.effect.defensebuff"), lexicon).Terms.Contains(GlossaryTerm.DamageCut),
                Is.False, "护甲括注里的减伤不算");
            Assert.That(TermMarkup.Scan(Strings.T("char.effect.pick.all"), lexicon).Terms.Count, Is.EqualTo(0), "(全体敌人) 不算范围");
        }

        /// <summary>模板白名单快照:下列模板里一个术语都没有(扫描结果为空)。新增 / 改写模板让这张表变化时会红 ——
        /// 确认后更新快照;若是用了新写法(又一个「召唤物」),先给词条加别名。</summary>
        private static readonly string[] NoTermTemplates =
        {
            "char.effect.amplify.all", "char.effect.amplify.damage", "char.effect.amplify.item_sep", "char.effect.amplify.items", "char.effect.amplify.heal", "char.effect.apcost",
            "char.effect.armorstrike.each", "char.effect.augment", "char.effect.augment.field.count", "char.effect.augment.field.shots",
            "char.effect.augment.field.turns", "char.effect.augment.target.damage", "char.effect.augment.target.healself",
            "char.effect.blind.single", "char.effect.blockmod.counter",
            "char.effect.blockmod.hits", "char.effect.blockmod.refund", "char.effect.chargedelay", "char.effect.cleanse",
            "char.effect.cleanse.allies", "char.effect.cleanse.allies.count", "char.effect.cleanse.count", "char.effect.cleanse.self",
            "char.effect.cleanse.self.count", "char.effect.damagesingle", "char.effect.damagesingle.body", "char.effect.debuffward",
            "char.effect.debuffward.count", "char.effect.debuffward.of", "char.effect.dispel.single.count", "char.effect.dispel.single.full",
            "char.effect.empower", "char.effect.execute.double", "char.effect.extrastrike", "char.effect.grantchar",
            "char.effect.healself", "char.effect.healself.victim", "char.effect.hitcap", "char.effect.hitcount",
            "char.effect.hitpercent", "char.effect.hurtheal", "char.effect.immunity", "char.effect.lifesteal", "char.effect.mine", "char.effect.mine.body",
            "char.effect.onlyif.countering", "char.effect.onlyif.firstcastthisturn", "char.effect.onlyif.isboss",
            "char.effect.onlyif.notboss", "char.effect.onlyif.playerhpabove70", "char.effect.onlyif.playerhpbelow50",
            "char.effect.onlyif.targethpabove70", "char.effect.onlyif.targethpbelow30", "char.effect.overkillsplash",
            "char.effect.overkillsplash.all", "char.effect.per.cap", "char.effect.per.cleansed", "char.effect.per.extrahittarget",
            "char.effect.perhit", "char.effect.perhit.from", "char.effect.pick.adjacent", "char.effect.pick.adjacentone",
            "char.effect.pick.all", "char.effect.pick.column", "char.effect.pick.highesthp", "char.effect.pick.hittargets",
            "char.effect.pick.random", "char.effect.pick.row", "char.effect.reflect", "char.effect.reshape",
            "char.effect.reshape.retarget", "char.effect.retaliate", "char.effect.retaliate.cap", "char.effect.retaliate.turns",
            "char.effect.retaliate.turns.cap", "char.effect.reveal", "char.effect.selfcost",
            "char.effect.shotpercent", "char.effect.silence", "char.effect.skillcut", "char.effect.summon.other",
            "char.effect.summon.percent", "char.effect.summon.self", "char.effect.summon.stats",
            "char.effect.taunt.self", "char.effect.thornmark", "char.effect.turnpulse", "char.passive.attackpercent",
            "char.passive.backrowbonus", "char.passive.deathpart", "char.passive.dodge", "char.passive.guard", "char.passive.healallypercent",
            "char.passive.healallytimes", "char.passive.healmirror", "char.passive.healmirror.pct", "char.passive.ranged",
            "char.passive.regen", "char.passive.wrap", "char.shape.chain.desc",
            "char.shape.suffix.all", "char.shape.suffix.chain", "char.shape.suffix.splash", "char.shape.suffix.volley",
            "char.shape.sweep.desc", "char.shape.volley.desc",
        };

        [Test]
        public void TemplatesWithoutTerms_MatchSnapshot()
        {
            var lexicon = RealLexicon();
            var prefixes = new[] { "char.effect.", "char.passive.", "char.shape." };
            var actual = Strings.Keys
                .Where(k => prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
                .Where(k => TermMarkup.Scan(Strings.T(k), lexicon).Terms.Count == 0)
                .OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var snapshot = NoTermTemplates.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var added = actual.Except(snapshot).ToArray();
            var removed = snapshot.Except(actual).ToArray();
            Assert.That(added.Length + removed.Length, Is.EqualTo(0),
                "无术语模板快照变了。\n新增(扫不出术语):\n  " + string.Join("\n  ", added)
                + "\n不再无术语:\n  " + string.Join("\n  ", removed));
        }

        /// <summary>Review Focus 3 + spec B §2/§3 的文案:最长匹配、排除上下文、改名与别名。</summary>
        [Test]
        public void SpecB_NewTerms_ScanAsExpected()
        {
            var lexicon = RealLexicon();
            Assert.That(TermMarkup.Scan("出手时额外召出 2 只小木灵", lexicon).Terms,
                Is.EqualTo(new[] { GlossaryTerm.Sapling }), "小木灵不被拆成木灵");
            Assert.That(TermMarkup.Scan(Strings.T("char.effect.spendwellspring"), lexicon).Terms.Contains(GlossaryTerm.Surge),
                Is.False, "涌泉相报不标涌");
            Assert.That(TermMarkup.Scan("涌:立即回复 90", lexicon).Terms, Is.EqualTo(new[] { GlossaryTerm.Surge }));
            Assert.That(TermMarkup.Scan("减攻 15%", lexicon).Terms, Is.EqualTo(new[] { GlossaryTerm.Weaken }), "旧名减攻作别名");
            Assert.That(TermMarkup.Scan("速度 150", lexicon).Terms, Is.EqualTo(new[] { GlossaryTerm.Speed }));
            Assert.That(Strings.T("term.weaken.name"), Is.EqualTo("慑"));
            Assert.That(Strings.T("term.sapling.name"), Is.EqualTo("小木灵"));
            Assert.That(Strings.T("term.spirit.desc"), Is.EqualTo("木系召唤的单位"));
            Assert.That(Strings.T("term.wellspring.desc").Contains("攻击时积累"), Is.True, "泉:治疗或攻击时积累");
            Assert.That(Strings.T("term.heft.desc").Contains("攻击时积累"), Is.True, "厚:获得护盾或攻击时积累");
            Assert.That(Strings.Keys.Contains("term.sapling.alias"), Is.False, "「苗」别名随「幼苗」一起退役");
        }

        [Test]
        public void SpendTerms_AreTheirOwnEntries()
        {
            // 2026-10-15 用户:厚积薄发 / 涌泉相报是特殊词条,单独成条,不再挂在厚 / 泉下面
            var lexicon = RealLexicon();
            Assert.That(TermMarkup.Scan(Strings.T("char.effect.spendheft"), lexicon).Terms.First(),
                Is.EqualTo(GlossaryTerm.SpendHeft), "厚积薄发整词先匹配,不拆成厚");
            Assert.That(TermMarkup.Scan(Strings.T("char.effect.spendwellspring"), lexicon).Terms.First(),
                Is.EqualTo(GlossaryTerm.SpendWellspring));
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.SpendHeft, 9, EffectKind.SpendHeft)),
                Is.EqualTo(GlossaryTerm.SpendHeft), "数值格「每层」点开是厚积薄发");
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.SpendWellspring, 9, EffectKind.SpendWellspring)),
                Is.EqualTo(GlossaryTerm.SpendWellspring));
            Assert.That(Strings.T("char.effect.spendheft").Contains("清空"), Is.False, "清空全部的说明进词条,效果句只留每层伤害");
        }

        /// <summary>词条 chip 的图标必须有 PNG(管线产物,执行期决定 D1:不新增图标)。</summary>
        [Test]
        public void EveryTermChipIcon_HasPng()
        {
            var dir = Path.Combine(RepoRoot().FullName, "Brushblade", "Assets", "_Project", "Presentation", "UI", "Resources");
            var missing = AllTerms().Select(GlossaryRules.ChipOf).Where(c => c != null).Select(c => c.Value.Icon)
                .Distinct().Where(id => !File.Exists(Path.Combine(dir, $"icon_{id}.png"))).ToArray();
            Assert.That(missing, Is.Empty, "词条 chip 用了不存在的图标:" + string.Join(",", missing));
        }

        /// <summary>spec B §6 验收 2:玩家可见文案里不再有「召唤物 / 幼苗 / 藤灵 / 小藻 / 减攻」(别名字段除外)。</summary>
        [Test]
        public void SpecB_NoLegacySummonNames()
        {
            var legacy = new[] { "召唤物", "幼苗", "藤灵", "小藻", "箭灵", "减攻" };
            var aliasKeys = new[] { "term.spirit.alias", "term.weaken.alias" };
            var bad = Strings.Keys.Where(k => !aliasKeys.Contains(k))
                .Where(k => legacy.Any(w => Strings.T(k).Contains(w)))
                .OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.That(bad, Is.Empty, "仍含旧称:\n" + string.Join("\n", bad));
            var sprout = Strings.Keys.Where(k => Strings.T(k).Contains("苗") && !Strings.T(k).Contains("小木灵"))
                .OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.That(sprout, Is.Empty, "「苗」不再作单位名:\n" + string.Join("\n", sprout));
            Assert.That(Strings.T("status.curse.name"), Is.EqualTo("慑"));
            Assert.That(Strings.T("charsheet.stat.weaken"), Is.EqualTo("慑"));
            Assert.That(Strings.T("battle.hint.pick_dual_target").Contains("点自己/木灵=护"), Is.True);
        }
    }
}
