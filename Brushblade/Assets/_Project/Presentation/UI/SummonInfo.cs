using System;
using System.Collections.Generic;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>召唤物文案的唯一来源(2026-08-15):属性/血攻盾/被动/生克。战斗页点召唤物看详情。
    /// 与 <see cref="EnemyInfo"/> 同惯例——文案写精确数值,平衡改动时需同步这里;
    /// 但生克倍率读 <see cref="WuxingResolver.KeMultiplier"/> 现取,不写死。</summary>
    public static class SummonInfo
    {
        // ============ 结构化详情(2026-08-31,单位详情轮二 Task 3) ============

        /// <summary>召唤物详情弹窗(稿 UnitAlly.dc.html;右栏本命排版按 SummonInfoNature 稿,2026-10-11 拍板)。**唯一**方法 —— 此前还有一整套整段
        /// 文本 API(Title/Detail/PassiveText/StatusText/WuxingText),Task 5 把
        /// OnSummonClicked 从 Ui.Modal(SummonInfo.Title/Detail) 改成 UnitSheet.Show 之后那是它们
        /// 唯一的调用点,五个方法随之全部归零调用,2026-09-01 review 删除(全仓库 grep 复核过)。
        /// 数值口径见 task-3-report.md 的逐条对照。
        /// 护甲读 <see cref="BattleEngine.SummonDefense"/>(含富甲光环,D2-金 Q15)、射程读 <see cref="BattleEngine.IsSummonRanged"/>
        /// (含箭灵光环)、左栏补上玩家袋里落在这只身上的木灵光环与林荫 / 森严 / 蟾宫(D2-木 Task 5)——
        /// 这些都挂在玩家身上或读全队,SummonState 自己算不出来,所以传引擎与槽位。
        /// <paramref name="graph"/>(可空)只用来把光环 / 本命翻出特性名(游戏数据);取不到时退回不带名的句子。</summary>
        public static UnitDetail Sheet(BattleEngine battle, int slot, RecipeGraph graph = null)
        {
            var summon = battle.Summons[slot];
            bool ranged = battle.IsSummonRanged(slot);
            // 左栏「状态」:自身的袋子 + 玩家袋里落在这只身上的光环(远程光环除外 —— 它写在右栏射程那一条上)+ 本命在场光环
            var statuses = UnitDetailChip.BuildStatuses(summon.Statuses, graph: graph);
            var auras = new List<StatusEffect>();
            foreach (var aura in battle.SummonAurasAt(slot))
                if (aura.Kind != StatusKind.SummonRanged) auras.Add(aura);
            // 玩家袋里护到木灵的本回合减伤 / 受伤上限(战场木灵格画了 chip,详情要有对应条目;Core 木灵承伤同读这两条)
            foreach (var st in battle.PlayerStatuses.All)
                if (st.Kind == StatusKind.DamageCut || st.Kind == StatusKind.HitCap) auras.Add(st);
            statuses.AddRange(UnitDetailChip.BuildStatuses(auras, battle.PlayerStatuses, graph: graph));
            UnitDetailChip.AddWoodNatureEntries(statuses, battle, graph, forSummon: true);
            return new UnitDetail
            {
                PortraitPrefix = null, // 召唤物没有立绘管线,稿子自己也写着「立绘待补·现用字牌位」
                FaceChar = summon.Char,
                Element = summon.Element,
                ElementUnknown = false, // 召唤物属性恒为已知(SummonState.Element 不可空),此字段定恒为 false
                Name = summon.Char,
                // 「召唤物」+ 射程(2026-09-03 用户拍板:与敌人详情同模板)。
                // 稿上第二枚标签原是排位(「后排」)+ 稀有度(「白」):排位这一半连同敌人详情
                // 那一半一起去掉了(战场上一眼就能看见站哪,写进标签只是重复,还挤掉真正
                // 要紧的射程 —— 理由见 EnemyInfo.BuildTags);稀有度在 CharDef 上,SummonState 不携带。
                Tags = new[]
                {
                    Strings.T("summon.detail.tag_type"),
                    StatusText.OfSummonRange(ranged).Name,
                },
                Flavor = null, // CharDef.Gloss(短释义)是候选来源,未接
                Hp = summon.Hp,
                MaxHp = summon.MaxHp,
                Shield = summon.Shield,
                ActionMeter = summon.ActionMeter,
                Figures = BuildFigures(summon, battle.SummonDefense(summon)),
                Statuses = statuses,
                Abilities = BuildAbilities(battle, slot, summon, ranged, graph),
                AbilitiesTitle = Strings.T("summon.detail.abilities_title"),
                Wuxing = UnitDetailChip.WuxingOf(summon.Element),
            };
        }

        /// <summary>攻/盾/甲/速四格。攻的口径与老文本 <see cref="Detail(SummonState)"/> 一致
        /// (读 EffectiveAttack,不是 Attack)——这是稿子标出来的既有规则(见那个属性的文档),
        /// 这里不重新推一遍。</summary>
        private static (string, string, string)[] BuildFigures(SummonState summon, int defenseValue)
        {
            // 老文本(Detail(SummonState))这里是无条件二选一(action_attack / action_passive_only),
            // 第一版这里只映射了 Attack==0 那一支,Attack>0 那句「攒满行动条时攻击最前的敌人」
            // 静默消失了(2026-09-01 review 抓到)。补上——Attack>0 直接复用现成的
            // summon.detail.action_attack(去掉老文本拼接用的尾部换行符),不新开一个短 key,
            // 因为没有稿子例子撑腰,拟一句更短的反而是发明。
            string attackNote = summon.Attack == 0
                ? Strings.T("summon.detail.figure_attack_passive_note")
                : Strings.T("summon.detail.action_attack").TrimEnd('\n');
            // 攻击光环(SummonInfoNature 稿「攻 48 · 含光环 +20%」):攻击一栏是有效值,已含光环,注明一句
            if (summon.TraitAttackPercent > 0)
                attackNote = Strings.T("summon.detail.figure_attack_aura", ("value", summon.TraitAttackPercent));

            string defenseNote = defenseValue > 0
                ? Strings.T("summon.detail.figure_defense_note")
                : null; // 召唤物没有基础护甲字段,EffectiveDefense 全部来自增益,这句恒成立

            // 速度(2026-09-16,加速/急速接线复查补):此前这里只印 summon.Speed 裸值,
            // SpeedModifier(减速/加速)挂上去之后这一格纹丝不动——与 EnemyInfo.BuildFigures
            // 的 speedValue/speedNote 同一口径地补齐,否则玩家在召唤物详情里永远看不出
            // 加速生效了没有(2026-09-16 之前也从没有效果会给召唤物挂 SpeedModifier,
            // 这条缺口一直无害地潜伏着,Haste 是第一个真正踩上去的)。
            int speedMod = summon.Statuses.TotalMagnitude(StatusKind.SpeedModifier);
            int speedValue = TurnScheduler.ClampSpeed(summon.Speed + speedMod);
            string speedNote = UnitDetailChip.BaseNote(summon.Speed,
                speedMod < 0
                    ? UnitDetailChip.DeltaDebuffPts(Strings.T("status.slow.name"), -speedMod)
                    : UnitDetailChip.DeltaBuffPts(Strings.T("status.speed.name"), speedMod));

            return new[]
            {
                (Strings.T("char.stat.attack"), summon.EffectiveAttack.ToString(), attackNote),
                (Strings.T("char.stat.shield"), summon.Shield.ToString(), (string)null),
                (Strings.T("char.stat.defense"),
                    defenseValue > 0 ? "+" + defenseValue : "0", defenseNote),
                (Strings.T("char.stat.speed"), speedValue.ToString(), speedNote),
            };
        }

        /// <summary>右栏「本命」(SummonInfoNature 稿,2026-10-11 拍板),顺序:
        /// ① 射程,恒有、恒第一(同敌人详情;箭灵光环生效时写来源与剩余);
        /// ② 出手形状,偏离单体时才出;
        /// ③ 本命,按战场本命 chip 的优先级(<see cref="NatureChipIcon"/>),其余没有图标的按数据顺序跟在后面。
        /// 第一条本命就是战场格子上那枚 chip:卡片左缘朱砂、尾标「战场上那枚」、名字与说明分两行;其余单行。
        /// 本命的名字取特性名(游戏数据,<see cref="CharInfo.NatureTraitName"/>),取不到就只印说明句;旧被动沿用整句现名(盾反、附灼…)。
        /// 嫁接带来的本命与天生的混排,不分来源。</summary>
        private static List<AbilityEntry> BuildAbilities(BattleEngine battle, int slot, SummonState summon, bool ranged,
            RecipeGraph graph)
        {
            var list = new List<AbilityEntry>();

            // ① 射程(2026-09-03 用户拍板补:与敌人详情的第一张卡同位置、同模板)。
            // 箭灵光环(D2-木 W1):「远程 · 箭灵光环 · 本场;平时近战」
            var range = StatusText.OfSummonRange(ranged);
            string rangeDesc = ranged ? Strings.T("summon.range.ranged.desc") : Strings.T("summon.range.melee.desc");
            if (ranged && !(summon.Passive?.Ranged ?? false))
                foreach (var aura in battle.SummonAurasAt(slot))
                    if (aura.Kind == StatusKind.SummonRanged)
                    {
                        string source = CharInfo.TraitNameOf(graph, aura.SourceId, e => e.Kind == EffectKind.SummonRanged)
                            ?? aura.SourceId ?? "";
                        rangeDesc = Strings.T("summon.range.aura", ("source", source),
                            ("duration", StatusText.Of(aura).Duration));
                        break;
                    }
            list.Add(new AbilityEntry
            {
                IconKey = range.IconKey, ChipColor = UnitDetailChip.Positioning,
                Name = range.Name, Desc = rangeDesc, Inline = true,
            });

            // ② 出手目标形状(2026-09-01 review 补):OfShape(Single) 返回全 null 的 None,单体不出这一条
            var shape = StatusText.OfShape(summon.Passive?.Shape ?? TargetArea.Single);
            if (shape.Name != null)
                list.Add(new AbilityEntry
                {
                    IconKey = shape.IconKey, ChipColor = UnitDetailChip.Positioning,
                    Name = shape.Name, Desc = shape.Desc, Inline = true,
                });

            // ③ 本命
            var p = summon.Passive;
            if (p == null) return list;
            var natures = new List<(string Name, string Line)>();
            void Old(string sentence) => natures.Add((null, sentence));   // 旧被动:整句已带现名(盾反、附灼…)
            void Named(Func<SummonPassive, bool> has, string line) => natures.Add((CharInfo.NatureTraitName(graph, has), line));
            var wood = new Dictionary<CharInfo.NatureKey, string>();
            CharInfo.WoodNatureLines(p, (key, line) => wood[key] = line);
            void Wood(CharInfo.NatureKey key, Func<SummonPassive, bool> has)
            {
                if (wood.TryGetValue(key, out var line)) Named(has, line);
            }

            // 有图标的,按战场 chip 优先级:守护 › 盾反 / 披荆 › 附灼 › 减攻 › 林荫 / 森严 › 蟾宫 › 回血 › 闪避 › 疾
            Wood(CharInfo.NatureKey.Guard, x => x.Guard > 0);
            if (p.Thorns > 0) Old(Strings.T("summon.passive.thorns", ("thorns", p.Thorns)));
            Wood(CharInfo.NatureKey.ThornsBleed, x => x.ThornsBleed > 0);
            if (p.OnHitBurn > 0)
                Old(p.OnHitBurnAll
                    ? Strings.T("summon.passive.burn_all", ("burn", p.OnHitBurn))
                    : Strings.T("summon.passive.burn_single", ("burn", p.OnHitBurn)));
            if (p.OnHitCurse > 0) Old(Strings.T("summon.passive.curse", ("curse", p.OnHitCurse)));
            Wood(CharInfo.NatureKey.GroveCut, x => x.GroveCut > 0);
            Wood(CharInfo.NatureKey.ThicketCut, x => x.ThicketCut > 0);
            Wood(CharInfo.NatureKey.CritAura, x => x.CritAura > 0);
            if (p.HealAlly > 0) Old(Strings.T("summon.passive.heal_ally", ("heal", p.HealAlly)));
            if (p.Regen > 0) Old(Strings.T("summon.passive.regen", ("regen", p.Regen)));
            if (p.Dodge > 0) Old(Strings.T("summon.passive.dodge", ("dodge", p.Dodge)));
            if (p.Speed > 100) Old(Strings.T("summon.passive.haste", ("speed", p.Speed)));
            // 没有图标的:先 D2-木 的(Lv4–Lv8 特性追加,稿的柘灵例「守护 › 良弓 › 坚木」),再 D2-0 那批 Lv1 / Lv3 本命
            Wood(CharInfo.NatureKey.AttackPercent, x => x.AttackPercent > 0);
            Wood(CharInfo.NatureKey.HealAllyPercent, x => x.HealAllyPercent > 0);
            Wood(CharInfo.NatureKey.HealMirror, x => x.HealMirror > 0);
            Wood(CharInfo.NatureKey.ReturnFire, x => x.ReturnFire > 0);
            Wood(CharInfo.NatureKey.DeathBlast, x => x.DeathBlast > 0);
            Wood(CharInfo.NatureKey.SproutDeathSlow, x => x.SproutDeathSlow > 0);
            Wood(CharInfo.NatureKey.DeathPart, x => x.DeathPart);
            if (p.HealAllyTimes > 1)
                Named(x => x.HealAllyTimes > 1, Strings.T("char.passive.healallytimes", ("value", p.HealAllyTimes)));
            if (p.PerAllyAttackPercent > 0)
                Named(x => x.PerAllyAttackPercent > 0, Strings.T("char.passive.perallyattack", ("value", p.PerAllyAttackPercent)));
            if (p.BackRowBonusPercent > 0)
                Named(x => x.BackRowBonusPercent > 0, Strings.T("char.passive.backrowbonus", ("value", p.BackRowBonusPercent)));
            if (p.SproutPercent > 0 && p.SproutMax > 0)
                Named(x => x.SproutPercent > 0, Strings.T("char.passive.sprout", ("value", p.SproutPercent), ("max", p.SproutMax)));
            if (p.EntrySaplings > 0)
                Named(x => x.EntrySaplings > 0, Strings.T("char.passive.entrysaplings", ("value", p.EntrySaplings)));
            if (p.OnHitCharmChance > 0)
                Named(x => x.OnHitCharmChance > 0, Strings.T("char.passive.onhitcharm", ("chance", p.OnHitCharmChance)));
            if (p.Armor > 0) Named(x => x.Armor > 0, Strings.T("char.passive.armor", ("value", p.Armor)));
            // 本命嘲讽(柘,稿例「… › 坚木 › 嘲讽」):复用卡面那句(句子自带「嘲讽:」现名)
            if (p.Taunt) Old(Strings.T("char.passive.taunt"));

            // 第一条 = 战场上那枚 chip(有图标才算):与战场同一个优先级函数,两处对得上
            string chipIcon = NatureChipIcon(p);
            for (int i = 0; i < natures.Count; i++)
            {
                var (name, line) = natures[i];
                bool lead = i == 0 && chipIcon != null;
                list.Add(new AbilityEntry
                {
                    IconKey = lead ? chipIcon : null,
                    ChipColor = Theme.Cinnabar,   // 本命 = 朱砂(与战场本命 chip 同色)
                    Name = name ?? line,
                    Desc = name != null ? line : null,
                    Lead = lead,
                    Inline = !lead,
                });
            }
            return list;
        }

        /// <summary>战场木灵格上那一枚本命 chip 的图标(StatusChipsWood 稿,2026-10-11 拍板):一格只出一枚,
        /// 取排得最前、且有图标的那一条 —— 守护 › 盾反 / 披荆 › 附灼 › 减攻 › 林荫 / 森严 › 蟾宫 › 回血 › 闪避 › 疾。
        /// 判据:先改变敌我怎么挨打的,再是出手附带,再是给全队的光环,最后是自身数值。没有图标的本命返回 null(不出 chip)。
        /// 战场格(BattleView.SummonPassiveChip)与详情右栏的第一条同读这一处。</summary>
        public static string NatureChipIcon(SummonPassive passive)
        {
            if (passive == null) return null;
            if (passive.Guard > 0) return "guard";
            if (passive.Thorns > 0) return "thorns";   // 披荆只认本命荆棘,与盾反同一枚
            if (passive.OnHitBurn > 0) return "burn";
            if (passive.OnHitCurse > 0) return "curse";
            if (passive.GroveCut > 0 || passive.ThicketCut > 0) return "bulwark";
            if (passive.CritAura > 0) return "crit";
            if (passive.HealAlly > 0 || passive.Regen > 0) return "heal";
            if (passive.Dodge > 0) return "dodge";
            if (passive.Speed > 100) return "speed";
            return null;
        }
    }
}
