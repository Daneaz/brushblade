using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>「角色等级 + 养成加成 → 一份 BattleConfig」这段映射的逐条守卫
    /// (2026-08-12,E-b4/E-b5 T7)。
    ///
    /// ⚠ **这个文件存在的唯一理由**:这段映射原本手写在 <c>GameRoot.StartSegment</c> 里,
    /// 而 Presentation 层没有任何自动化测试,两个工装(tools/trace、tools/balance)又各自
    /// 造 BattleConfig —— 谁都碰不到 GameRoot。实测把 <c>PlayerDodge</c> 那行整行删掉,
    /// 967 条测试全绿、零 error CS,没有任何东西能抓到。同一个洞覆盖了
    /// PlayerMaxHp / PlayerAttack / PlayerDefense / PlayerDodge 四条角色属性。
    ///
    /// 所以下面每条断言都必须满足一个硬要求:**期望值 ≠ BattleConfig 的字段缺省值**。
    /// 否则「删掉这条注入」和「注入了但等于缺省」在测试里长得一模一样,又是一条零判别力的
    /// 装饰性断言(E-b1 评审的教训)。<see cref="EveryInjectedField_DiffersFromDefault"/>
    /// 把这个要求本身也钉住了。</summary>
    public class MetaRulesBattleConfigTests
    {
        /// <summary>升到 n+1 级需 100 + 50×(n−1) 经验(19.2.1),这里反推「刚好 level 级」的经验。</summary>
        private static int XpForLevel(int level)
        {
            int xp = 0, cost = 100;
            for (int n = 1; n < level; n++) { xp += cost; cost += 50; }
            return xp;
        }

        /// <summary>11 级:四条曲线在这一级的取值**两两不同且都不等于缺省**
        /// (HP 700 / ATK 120 / DEF 5 / 闪避 10),一条注入丢了必然看得出来。</summary>
        private static MetaState LevelElevenWithPerks()
        {
            var meta = new MetaState { CharacterXp = XpForLevel(11) };
            meta.PerkLevels["yangyuan"] = 2; // 养元:+100 HP/级
            meta.PerkLevels["yiqi"] = 2;     // 一气:+1 AP/级
            meta.PerkLevels["bowen"] = 2;    // 博闻:+1 字库格/级
            meta.PerkLevels["jintang"] = 2;  // 金汤:+20 护盾/级(不进 BattleConfig,见下方护盾那条)
            meta.Deck.Add("剑");
            meta.Deck.Add("城");
            return meta;
        }

        private static BattleConfig Build(MetaState meta) =>
            MetaRules.BuildBattleConfig(meta, new[] { "木", "火" });

        [Test]
        public void CharacterLevel_OfTheFixture_IsEleven() // 夹具自检:上面那些期望值全部建立在 11 级上
        {
            Assert.That(MetaRules.CharacterLevel(XpForLevel(11)), Is.EqualTo(11));
            Assert.That(MetaRules.CharacterLevel(XpForLevel(11) - 1), Is.EqualTo(10));
        }

        // ---- 四条角色属性,一条一测 ----

        [Test]
        public void PlayerMaxHp_IsLevelCurvePlusYangyuan()
        {
            // 生命是唯一吃技能加成的角色属性:700(11 级曲线)+ 200(养元 2 级)
            Assert.That(Build(LevelElevenWithPerks()).PlayerMaxHp,
                Is.EqualTo(MetaRules.MaxHpFor(11) + 200));
            Assert.That(Build(LevelElevenWithPerks()).PlayerMaxHp, Is.EqualTo(900));
        }

        [Test]
        public void PlayerMaxHp_LosesTheYangyuanBonus_WhenPerkNotOwned()
        {
            // 只有等级曲线那一半的话,养元加成这一项被删掉不会有任何测试红 —— 这条是它的守卫
            var meta = new MetaState { CharacterXp = XpForLevel(11) };
            Assert.That(Build(meta).PlayerMaxHp, Is.EqualTo(700));
        }

        [Test]
        public void PlayerAttack_IsLevelCurve()
        {
            // 120 ≠ 缺省 100(AttackBaseline):删掉这行注入,伤害会静默退回基准切片
            Assert.That(Build(LevelElevenWithPerks()).PlayerAttack, Is.EqualTo(MetaRules.AttackFor(11)));
            Assert.That(Build(LevelElevenWithPerks()).PlayerAttack, Is.EqualTo(120));
        }

        [Test]
        public void PlayerDefense_IsLevelCurve()
        {
            // 5 ≠ 缺省 0:这正是 T4 变异检查里删掉后无人发现的那一类
            Assert.That(Build(LevelElevenWithPerks()).PlayerDefense, Is.EqualTo(MetaRules.DefenseFor(11)));
            Assert.That(Build(LevelElevenWithPerks()).PlayerDefense, Is.EqualTo(5));
        }

        [Test]
        public void PlayerDodge_IsLevelCurve()
        {
            // 10 ≠ 缺省 0:实测被删掉时 967 条测试无一变红的那条
            Assert.That(Build(LevelElevenWithPerks()).PlayerDodge, Is.EqualTo(MetaRules.DodgeFor(11)));
            Assert.That(Build(LevelElevenWithPerks()).PlayerDodge, Is.EqualTo(10));
        }

        [Test]
        public void PlayerSpeed_IsLevelCurve()
        {
            // 110 ≠ 缺省 100(BattleConfig.PlayerSpeed 的默认值):必须用非 1 级的夹具断言,
            // 否则 SpeedFor(1) == 100 会跟缺省值撞在一起,删掉这行注入也照样绿(2026-08-15 实测踩过)。
            Assert.That(Build(LevelElevenWithPerks()).PlayerSpeed, Is.EqualTo(MetaRules.SpeedFor(11)));
            Assert.That(Build(LevelElevenWithPerks()).PlayerSpeed, Is.EqualTo(110));
        }

        [Test]
        public void PlayerCritChance_IsNotALevelStat_AndStaysZero()
        {
            // 2026-08-12 用户裁定:暴击**不随角色等级成长**,只靠字(锋)与将来的养成技能给。
            // 「刻意不注入」与「忘了注入」在代码里长得一样,这条把裁定钉成可执行的。
            // 缺省 0 还是 RollCrit 短路(一次随机都不摇)的前提,改成非 0 会让黄金轨迹整体发散。
            Assert.That(Build(LevelElevenWithPerks()).PlayerCritChance, Is.EqualTo(0));
            Assert.That(Build(new MetaState { CharacterXp = XpForLevel(26) }).PlayerCritChance, Is.EqualTo(0));
        }

        // ---- 其余四个字段(不是角色属性,但同在那个洞里) ----

        [Test]
        public void ApPerTurn_IsBasePlusYiqi()
        {
            Assert.That(Build(LevelElevenWithPerks()).ApPerTurn,
                Is.EqualTo(MetaRules.BaseApPerTurn + 2));
            Assert.That(Build(new MetaState()).ApPerTurn, Is.EqualTo(MetaRules.BaseApPerTurn));
        }

        [Test]
        public void LibraryCapacity_IsStartingPlusSlackPlusBowen()
        {
            var meta = LevelElevenWithPerks();
            Assert.That(Build(meta).LibraryCapacity, Is.EqualTo(MetaRules.LibraryCapacityFor(meta)));
            Assert.That(Build(meta).LibraryCapacity, Is.EqualTo(
                MetaRules.StartingLibrarySize + MetaRules.LibraryCapacitySlack + 2));
        }

        [Test]
        public void UnlockedChars_IsTheDeck()
        {
            // 合成与回合掉字都锁这份名单(2026-07-20):丢了这条注入 = 玩家能合出全字表
            var meta = LevelElevenWithPerks();
            Assert.That(Build(meta).UnlockedChars, Is.EquivalentTo(meta.Deck));
        }

        [Test]
        public void DropTable_IsPassedThrough()
        {
            Assert.That(Build(LevelElevenWithPerks()).DropTable, Is.EquivalentTo(new[] { "木", "火" }));
        }

        // ---- 反装饰性守卫:期望值必须离缺省值足够远 ----

        [Test]
        public void EveryInjectedField_DiffersFromDefault()
        {
            // 上面每条断言的判别力都建立在「期望值 ≠ 缺省值」上。将来有人改了某条曲线的起点
            // (比如把 DefenseFor 的封顶改到 0),对应那条断言会静默退化成「测了个缺省」——
            // 这条替它们盯着。⚠ PlayerCritChance 不在此列:它**就是**缺省,那是裁定不是遗漏。
            var config = Build(LevelElevenWithPerks());
            var fresh = new BattleConfig();
            Assert.That(config.PlayerMaxHp, Is.Not.EqualTo(fresh.PlayerMaxHp));
            Assert.That(config.PlayerAttack, Is.Not.EqualTo(fresh.PlayerAttack));
            Assert.That(config.PlayerDefense, Is.Not.EqualTo(fresh.PlayerDefense));
            Assert.That(config.PlayerDodge, Is.Not.EqualTo(fresh.PlayerDodge));
            Assert.That(config.PlayerSpeed, Is.Not.EqualTo(fresh.PlayerSpeed));
            Assert.That(config.ApPerTurn, Is.Not.EqualTo(fresh.ApPerTurn));
            Assert.That(config.LibraryCapacity, Is.Not.EqualTo(fresh.LibraryCapacity));
            Assert.That(config.UnlockedChars, Is.Not.Null);   // 缺省 null = 不限合成
            Assert.That(config.DropTable, Is.Not.Empty);      // 缺省空表
        }

        // ---- 另外三处不进 BattleConfig 的养成注入 ----

        [Test]
        public void StartingHp_IsTheSameExpressionAsBattleConfigMaxHp()
        {
            // 新开一次登塔是**满血**登塔(GameRoot.StartTower 的 EndlessSaveState.PlayerHp)。
            // 此前那里与战斗配置各抄了一遍 MaxHpFor(level) + HpBonus(meta) —— 生命将来再加
            // 第二个 Bonus 项,改一处漏一处不会有任何东西报错。现在两处同源,这条钉住同源。
            var meta = LevelElevenWithPerks();
            Assert.That(MetaRules.PlayerMaxHpFor(meta), Is.EqualTo(Build(meta).PlayerMaxHp));
            Assert.That(MetaRules.PlayerMaxHpFor(meta), Is.EqualTo(900));
        }

        [Test]
        public void ShieldBonus_TracksJintangLevel()
        {
            // 金汤的护盾不进 BattleConfig,而是喂给 RunEngine 的三条路径(段首 NormalShield、
            // 续爬 perFloorNormalShield、新开 perFloorNormalShield)。三条路径都在 GameRoot 里,
            // Core 侧测不到「有没有喂到」;能测的是**喂的值**,这条把它钉住 ——
            // 每级 20 点被改动时,三条路径会一起错,至少这里会红。
            Assert.That(PerkRules.ShieldBonus(LevelElevenWithPerks()), Is.EqualTo(40));
            Assert.That(PerkRules.ShieldBonus(new MetaState()), Is.EqualTo(0));
        }
    }
}
