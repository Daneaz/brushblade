using System;
using System.Collections.Generic;
using System.Linq;
using Brushblade.Core;
using Brushblade.Data;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>Plan E4 Task 1:词条(Glossary)的纯函数部分 —— Core 的 <see cref="GlossaryTerm"/> / <see cref="GlossaryRules"/>,
    /// Data 的 <see cref="TermMarkup"/>(在纯文本上扫术语、给术语与数字套样式、剥标签)。附录 §C 3.5 第 1、2、5 条。
    /// 词表由表现层从字符串表拼出来;这里用手写小词表,只测扫描规则本身。</summary>
    public class TermMarkupTests
    {
        private static readonly TermSurface[] Lexicon =
        {
            new TermSurface("灼", GlossaryTerm.Burn),
            new TermSurface("灼烧", GlossaryTerm.Burn),
            new TermSurface("护盾", GlossaryTerm.Shield),
            new TermSurface("护甲", GlossaryTerm.Armor),
            new TermSurface("破甲", GlossaryTerm.ArmorBreak),
            new TermSurface("减伤", GlossaryTerm.DamageCut, notBefore: "("),
            new TermSurface("全体", GlossaryTerm.AreaAll, notBefore: "我方", notAfter: "敌人)"),
            new TermSurface("连发", GlossaryTerm.AreaScatter),
        };

        private static List<(string Text, GlossaryTerm Term)> Spans(string plain)
        {
            var scan = TermMarkup.Scan(plain, Lexicon);
            return scan.Spans.Select(s => (plain.Substring(s.Start, s.Length), s.Term)).ToList();
        }

        // ---------------- Scan ----------------

        [Test]
        public void Scan_LongestMatch_NoOverlap()
        {
            // 2026-10-13:删去一条词条后,最长匹配只剩「灼烧 / 灼」这一对来守
            Assert.That(Spans("获得护盾,并灼烧"), Is.EqualTo(new[]
                { ("护盾", GlossaryTerm.Shield), ("灼烧", GlossaryTerm.Burn) }), "灼烧先于灼");
            Assert.That(Spans("本字灼 3"), Is.EqualTo(new[] { ("灼", GlossaryTerm.Burn) }));
            Assert.That(Spans("护甲与破甲"), Is.EqualTo(new[]
                { ("护甲", GlossaryTerm.Armor), ("破甲", GlossaryTerm.ArmorBreak) }), "破甲不含护甲,不会误标");
        }

        [Test]
        public void Scan_Terms_FirstAppearanceOrder_Distinct()
        {
            var scan = TermMarkup.Scan("护盾 +20;灼烧 2;再给护盾;灼 1", Lexicon);
            Assert.That(scan.Spans.Count, Is.EqualTo(4));
            Assert.That(scan.Terms, Is.EqualTo(new[] { GlossaryTerm.Shield, GlossaryTerm.Burn }));
        }

        [Test]
        public void Scan_ExcludedContexts()
        {
            Assert.That(Spans("护甲 +5(减伤 20%)"), Is.EqualTo(new[] { ("护甲", GlossaryTerm.Armor) }), "护甲括注里的减伤不标");
            Assert.That(Spans("减伤:本回合受到的伤害−60%"), Is.EqualTo(new[] { ("减伤", GlossaryTerm.DamageCut) }));
            Assert.That(Spans("减攻 20%(全体敌人)"), Is.Empty, "选择器后缀不标");
            Assert.That(Spans("我方全体清除全部减益"), Is.Empty, "我方全体不标");
            Assert.That(Spans("全体灼烧 2"), Is.EqualTo(new[] { ("全体", GlossaryTerm.AreaAll), ("灼烧", GlossaryTerm.Burn) }));
            Assert.That(Spans("全体"), Is.EqualTo(new[] { ("全体", GlossaryTerm.AreaAll) }), "串首 / 串尾不越界");
        }

        [Test]
        public void Scan_Empty_NoTerms()
        {
            foreach (var plain in new[] { "", null, "本回合格挡反击 ×2" })
            {
                var scan = TermMarkup.Scan(plain, Lexicon);
                Assert.That(scan.Spans.Count, Is.EqualTo(0));
                Assert.That(scan.Terms.Count, Is.EqualTo(0));
            }
            Assert.That(TermMarkup.Scan("灼烧", Array.Empty<TermSurface>()).Spans.Count, Is.EqualTo(0));
            Assert.That(TermMarkup.Scan("灼烧", null).Spans.Count, Is.EqualTo(0));
        }

        [Test]
        public void Scan_NotBefore_Alternatives_PipeSeparated()
        {
            var lexicon = new[] { new TermSurface("减伤", GlossaryTerm.DamageCut, notBefore: "(|,") };
            Assert.That(TermMarkup.Scan("护甲 +5(减伤 20%)", lexicon).Spans.Count, Is.EqualTo(0), "左括号排除");
            Assert.That(TermMarkup.Scan("(自带护甲 9,减伤 8%)", lexicon).Spans.Count, Is.EqualTo(0), "逗号同样排除");
            Assert.That(TermMarkup.Scan("减伤:本回合", lexicon).Spans.Count, Is.EqualTo(1));
            Assert.That(TermMarkup.Scan("我方减伤 5%", lexicon).Spans.Count, Is.EqualTo(1), "其余前文不排除");
        }

        [Test]
        public void BuildLexicon_NamesAliases_AndExclusions()
        {
            var lexicon = TermMarkup.BuildLexicon(new[]
            {
                (GlossaryTerm.Burn, "灼", "灼烧"),
                (GlossaryTerm.Hot, "润泽", "持续治疗|群体润泽"),
                (GlossaryTerm.Shield, "护盾", (string)null),
                (GlossaryTerm.DamageCut, "减伤", ""),
                (GlossaryTerm.AreaAll, "全体", null),
            });
            var texts = lexicon.Select(s => (s.Text, s.Term)).ToList();
            Assert.That(texts, Is.EqualTo(new[]
            {
                ("灼", GlossaryTerm.Burn), ("灼烧", GlossaryTerm.Burn),
                ("润泽", GlossaryTerm.Hot), ("持续治疗", GlossaryTerm.Hot), ("群体润泽", GlossaryTerm.Hot),
                ("护盾", GlossaryTerm.Shield), ("减伤", GlossaryTerm.DamageCut), ("全体", GlossaryTerm.AreaAll),
            }), "显示名在前、别名按 | 拆开;空别名不出词");
            Assert.That(TermMarkup.Scan("护甲 +5(减伤 20%)", lexicon).Spans.Count, Is.EqualTo(0), "减伤带护甲括注排除");
            Assert.That(TermMarkup.Scan("(自带护甲 9,减伤 8%)", lexicon).Spans.Count, Is.EqualTo(0));
            Assert.That(TermMarkup.Scan("减攻 20%(全体敌人)", lexicon).Spans.Count, Is.EqualTo(0), "选择器后缀");
            Assert.That(TermMarkup.Scan("我方全体清除全部减益", lexicon).Spans.Count, Is.EqualTo(0), "我方全体");
            Assert.That(TermMarkup.Scan("治疗改为全体", lexicon).Terms, Is.EqualTo(new[] { GlossaryTerm.AreaAll }));
            Assert.That(TermMarkup.Scan("群体持续治疗 9", lexicon).Terms, Is.EqualTo(new[] { GlossaryTerm.Hot }));
        }

        // ---------------- Numbers / Rich / Strip ----------------

        private const string TO = "<b><color=#3D4E69>", TC = "</color></b>", NO = "<b>", NC = "</b>";

        [Test]
        public void Numbers_Shapes()
        {
            const string plain = "灼烧+2,伤害−60%,×1.5,护盾 20";
            var nums = TermMarkup.Numbers(plain).Select(n => plain.Substring(n.Start, n.Length)).ToList();
            Assert.That(nums, Is.EqualTo(new[] { "+2", "−60%", "×1.5", "20" }));
        }

        [Test]
        public void Rich_TermAndNumber_Tagged_NoOverlap()
        {
            const string plain = "灼烧+2";
            Assert.That(TermMarkup.Rich(plain, TermMarkup.Scan(plain, Lexicon), TO, TC, NO, NC),
                Is.EqualTo(TO + "灼烧" + TC + NO + "+2" + NC), "术语是灼烧、+2 是数字");
            const string plain2 = "护甲 +5(减伤 20%),持续 3 回合";
            Assert.That(TermMarkup.Rich(plain2, TermMarkup.Scan(plain2, Lexicon), TO, TC, NO, NC),
                Is.EqualTo(TO + "护甲" + TC + " " + NO + "+5" + NC + "(减伤 " + NO + "20%" + NC + "),持续 " + NO + "3" + NC + " 回合"));
        }

        [Test]
        public void Rich_DimState_NoColor()
        {
            const string plain = "全体灼烧 2";
            Assert.That(TermMarkup.Rich(plain, TermMarkup.Scan(plain, Lexicon), "<b>", "</b>", "<b>", "</b>"),
                Is.EqualTo("<b>全体</b><b>灼烧</b> <b>2</b>"), "off 态由调用方只给 <b>");
        }

        [Test]
        public void Strip_InvertsRich()
        {
            foreach (var plain in new[]
                     {
                         "", "获得护盾,并灼烧", "护甲 +5(减伤 20%),持续 3 回合", "全体灼烧 2;我方全体清除全部减益",
                         "下 3 场开局:全体灼 2", "本回合格挡反击×2",
                     })
            {
                var rich = TermMarkup.Rich(plain, TermMarkup.Scan(plain, Lexicon), TO, TC, NO, NC);
                Assert.That(TermMarkup.Strip(rich), Is.EqualTo(plain), plain);
            }
            Assert.That(TermMarkup.Rich("", TermMarkup.Scan("", Lexicon), TO, TC, NO, NC), Is.EqualTo(""));
            Assert.That(TermMarkup.Strip(""), Is.EqualTo(""));
            Assert.That(TermMarkup.Strip(null), Is.EqualTo(""));
            Assert.That(TermMarkup.Strip("<size=20><i>甲</i></size>"), Is.EqualTo("甲"));
        }

        // ---------------- GlossaryTerm / GlossaryRules ----------------

        [Test]
        public void GlossaryTerm_45_InSpecOrder_Groups()
        {
            // 2026-10-13:删去一条词条,关键词 32 → 31;2026-10-15 spec B:追加 涓 / 涌 / 苏 / 速度,31 → 35
            var all = Enum.GetValues(typeof(GlossaryTerm)).Cast<GlossaryTerm>().ToList();
            Assert.That(all.Count, Is.EqualTo(45));
            Assert.That(all.Count(t => GlossaryRules.Group(t) == 0), Is.EqualTo(35), "关键词");
            Assert.That(all.Count(t => GlossaryRules.Group(t) == 1), Is.EqualTo(7), "范围");
            Assert.That(all.Count(t => GlossaryRules.Group(t) == 2), Is.EqualTo(3), "存续");
            Assert.That(all.Select(GlossaryRules.Group), Is.Ordered, "组按 spec §3 顺序排");
            Assert.That((all[0], all[30], all[31], all[34]), Is.EqualTo(
                (GlossaryTerm.Burn, GlossaryTerm.Crit, GlossaryTerm.Trickle, GlossaryTerm.Speed)));
            Assert.That((all[35], all[41], all[42], all[44]), Is.EqualTo(
                (GlossaryTerm.AreaSingle, GlossaryTerm.AreaAll, GlossaryTerm.Battle, GlossaryTerm.Band)));
        }

        /// <summary>词条 chip = 战场上那枚状态 chip(同图标、同底色分组;执行期决定 D1 / D2)。每个词条都列到。</summary>
        [Test]
        public void ChipOf_EveryTerm_MatchesBattlefieldChip()
        {
            (string, ChipTone)? C(string icon, ChipTone tone) => (icon, tone);
            var expected = new Dictionary<GlossaryTerm, (string, ChipTone)?>
            {
                [GlossaryTerm.Burn] = C("burn", ChipTone.Threat),
                [GlossaryTerm.Detonate] = null,
                [GlossaryTerm.Freeze] = C("freeze", ChipTone.Control),
                [GlossaryTerm.FrostResist] = C("frostguard", ChipTone.Control),
                [GlossaryTerm.IceStall] = C("chill", ChipTone.Control),
                [GlossaryTerm.Slow] = C("slow", ChipTone.Control),
                [GlossaryTerm.Morale] = C("morale", ChipTone.Boost),
                [GlossaryTerm.Block] = C("block", ChipTone.Guard),
                [GlossaryTerm.Wellspring] = null,
                [GlossaryTerm.Heft] = null,
                [GlossaryTerm.Shield] = C("shield", ChipTone.Guard),
                [GlossaryTerm.Armor] = C("defense", ChipTone.Guard),
                [GlossaryTerm.ArmorBreak] = C("armorbreak", ChipTone.Control),
                [GlossaryTerm.Hot] = C("heal", ChipTone.Guard),
                [GlossaryTerm.Seed] = C("seed", ChipTone.Guard),
                [GlossaryTerm.Spirit] = null,
                [GlossaryTerm.Nature] = null,
                [GlossaryTerm.Sapling] = null,
                [GlossaryTerm.Graft] = null,
                [GlossaryTerm.Taunt] = C("taunt", ChipTone.Control),
                [GlossaryTerm.Execute] = null,
                [GlossaryTerm.Bleed] = C("bleed", ChipTone.Threat),
                [GlossaryTerm.Doom] = C("mortal", ChipTone.Threat),
                [GlossaryTerm.Charm] = null,
                [GlossaryTerm.Mark] = C("mark", ChipTone.Threat),
                [GlossaryTerm.Weaken] = C("curse", ChipTone.Control),
                [GlossaryTerm.DamageCut] = C("bulwark", ChipTone.Guard),
                [GlossaryTerm.CounterBoost] = C("riposte", ChipTone.Boost),
                [GlossaryTerm.Endure] = C("lastlife", ChipTone.Guard),
                [GlossaryTerm.Thorns] = C("thorns", ChipTone.Threat),
                [GlossaryTerm.Crit] = C("crit", ChipTone.Boost),
                [GlossaryTerm.Trickle] = C("heal", ChipTone.Guard),
                [GlossaryTerm.Surge] = C("heal", ChipTone.Guard),
                [GlossaryTerm.Revive] = C("lastlife", ChipTone.Guard),
                [GlossaryTerm.Speed] = C("speed", ChipTone.Guard),
                [GlossaryTerm.AreaSingle] = null,
                [GlossaryTerm.AreaAdjacent] = null,
                [GlossaryTerm.AreaRow] = null,
                [GlossaryTerm.AreaColumn] = null,
                [GlossaryTerm.AreaScatter] = null,
                [GlossaryTerm.AreaChain] = null,
                [GlossaryTerm.AreaAll] = null,
                [GlossaryTerm.Battle] = null,
                [GlossaryTerm.Carry] = null,
                [GlossaryTerm.Band] = null,
            };
            Assert.That(expected.Count, Is.EqualTo(Enum.GetValues(typeof(GlossaryTerm)).Length), "每个词条都列到");
            foreach (var kv in expected)
                Assert.That(GlossaryRules.ChipOf(kv.Key), Is.EqualTo(kv.Value), kv.Key.ToString());
        }

        [Test]
        public void TermOf_FaceStatKind_EachKind()
        {
            var expected = new Dictionary<FaceStatKind, GlossaryTerm?>
            {
                [FaceStatKind.Damage] = null,
                [FaceStatKind.Heal] = null,
                [FaceStatKind.Shield] = GlossaryTerm.Shield,
                [FaceStatKind.SummonHp] = GlossaryTerm.Spirit,
                [FaceStatKind.SummonAttack] = null,
                [FaceStatKind.Burn] = GlossaryTerm.Burn,
                [FaceStatKind.Morale] = GlossaryTerm.Morale,
                [FaceStatKind.Block] = GlossaryTerm.Block,
                [FaceStatKind.Weaken] = GlossaryTerm.Weaken,
                [FaceStatKind.Armor] = GlossaryTerm.Armor,
                [FaceStatKind.Duration] = null,
                [FaceStatKind.Freeze] = GlossaryTerm.Freeze,
                [FaceStatKind.Slow] = GlossaryTerm.Slow,
                [FaceStatKind.Seed] = GlossaryTerm.Seed,
                [FaceStatKind.Charm] = GlossaryTerm.Charm,
                [FaceStatKind.HealOverTime] = GlossaryTerm.Hot,
                [FaceStatKind.ArmorBreak] = GlossaryTerm.ArmorBreak,
                [FaceStatKind.Nature] = GlossaryTerm.Nature,
                [FaceStatKind.SpendWellspring] = GlossaryTerm.Wellspring,
                [FaceStatKind.SpendHeft] = GlossaryTerm.Heft,
                [FaceStatKind.CounterPercent] = null,
                [FaceStatKind.ReviveHp] = GlossaryTerm.Revive,
            };
            Assert.That(expected.Count, Is.EqualTo(Enum.GetValues(typeof(FaceStatKind)).Length), "每个 FaceStatKind 都列到");
            foreach (var kv in expected)
                Assert.That(GlossaryRules.TermOf(kv.Key), Is.EqualTo(kv.Value), kv.Key.ToString());
        }

        [Test]
        public void TermOf_FaceStat_HealBySource()
        {
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.Heal, 30, EffectKind.HealOverTime)), Is.EqualTo(GlossaryTerm.Trickle));
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.Heal, 90, EffectKind.HealSelf)), Is.EqualTo(GlossaryTerm.Surge));
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.Heal, 45, EffectKind.HealAll)), Is.EqualTo(GlossaryTerm.Surge));
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.ReviveHp, 50, EffectKind.Revive)), Is.EqualTo(GlossaryTerm.Revive));
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.CounterPercent, 40, EffectKind.Block)), Is.Null);
            Assert.That(GlossaryRules.TermOf(new FaceStat(FaceStatKind.Burn, 1, EffectKind.BurnSingle)), Is.EqualTo(GlossaryTerm.Burn), "其余同 TermOf(kind)");
        }
    }
}
