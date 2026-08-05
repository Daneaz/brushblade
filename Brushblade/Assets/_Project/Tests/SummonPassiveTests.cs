using System;
using System.Collections.Generic;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>召唤物被动族(2026-08-05,子项目 C):速度/反伤/回血/出手附带效果/护盾。
    /// 规格见 docs/superpowers/specs/2026-08-05-召唤物被动-design.md。</summary>
    public class SummonPassiveTests
    {
        // 木系召唤字若干,每个带一种被动。敌人一律用「心」属性,避开生克干扰。
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            // 素:无被动的基准召唤(10 血 / 攻 3)
            new CharDef("素", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木") }),
            // 疾:速度 150(桤)
            new CharDef("疾", Element.Wood,
                effects: new[] { new EffectDef(EffectKind.Summon, 10, summonCount: 1, summonAttack: 3, summonChar: "木",
                    passive: new SummonPassive { Speed = 150 }) }),
        });

        private static BattleEngine Engine(string[] library, EnemyDef[] enemies,
            IReadOnlyList<SummonSnapshot> startingSummons = null) =>
            new(Graph(), new BattleConfig { DropTable = new[] { "木" }, PlayerMaxHp = 50 },
                library, Array.Empty<string>(), enemies, seed: 1,
                startingSummons: startingSummons);

        private static EnemyDef Dummy(int hp = 200, int attack = 0) => new("靶", Element.Heart, hp, attack);

        [Test]
        public void Summon_WithoutPassive_HasSpeed100AndNullPassive()
        {
            var engine = Engine(new[] { "素" }, new[] { Dummy() });
            engine.Cast("素");
            Assert.That(engine.Summons[0].Passive, Is.Null);
            Assert.That(engine.Summons[0].Speed, Is.EqualTo(100));
        }

        [Test]
        public void Summon_CarriesPassiveFromEffectDef()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");
            Assert.That(engine.Summons[0].Passive, Is.Not.Null);
            Assert.That(engine.Summons[0].Passive.Speed, Is.EqualTo(150));
            Assert.That(engine.Summons[0].Speed, Is.EqualTo(150), "基础速度应取自被动");
        }

        [Test]
        public void Snapshot_RoundTrip_KeepsSpeedShieldAndPassive()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");

            var meta = new MetaState
            {
                Endless = new EndlessSaveState { Depth = 3, PlayerHp = 40, Seed = 7 },
            };
            foreach (var summon in engine.Summons) meta.Endless.CarriedSummons.Add(summon.Capture());
            meta.Endless.CarriedSummons[0].Shield = 6; // 护盾字段也要过一趟序列化

            var restored = Data.SaveSerializer.FromJson(Data.SaveSerializer.ToJson(meta));
            var revived = Engine(new[] { "疾" }, new[] { Dummy() },
                startingSummons: restored.Endless.CarriedSummons);

            Assert.That(revived.Summons[0].Speed, Is.EqualTo(150));
            Assert.That(revived.Summons[0].Shield, Is.EqualTo(6));
            Assert.That(revived.Summons[0].Passive, Is.Not.Null);
            Assert.That(revived.Summons[0].Passive.Speed, Is.EqualTo(150));
        }

        [Test]
        public void Snapshot_LegacySaveWithoutSpeedField_FallsBackTo100()
        {
            // 老存档没有 Speed 字段 → Newtonsoft 填 0 → 召唤物永远攒不满计量器,一辈子不出手。
            // Restore 必须兜底回 100。
            const string legacy =
                "{\"Endless\":{\"Depth\":3,\"PlayerHp\":40,\"Seed\":7,\"CarriedSummons\":" +
                "[{\"Char\":\"木\",\"Element\":\"Wood\",\"Hp\":10,\"MaxHp\":10,\"Attack\":3,\"ActionMeter\":0}]}}";
            var restored = Data.SaveSerializer.FromJson(legacy);
            var engine = Engine(new[] { "素" }, new[] { Dummy() },
                startingSummons: restored.Endless.CarriedSummons);

            Assert.That(engine.Summons[0].Speed, Is.EqualTo(100));
            Assert.That(engine.Summons[0].Passive, Is.Null);
            Assert.That(engine.Summons[0].Shield, Is.EqualTo(0));
        }

        [Test]
        public void Speed150_ActsOneThenTwoAlternating()
        {
            // 计量器:0+150=150 → 1 次(余 50);50+150=200 → 2 次(余 0);循环。平均 1.5 次/回合。
            // 「当回合即可反击」本就是引擎默认行为(新召唤物 0+100 就够一次),桤 的差异化靠速度。
            var engine = Engine(new[] { "疾" }, new[] { Dummy(hp: 500) });
            engine.Cast("疾");
            int hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 1 回合出手 1 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(6), "第 2 回合出手 2 次");
            hp = engine.Enemies[0].Hp;

            engine.EndTurn();
            Assert.That(hp - engine.Enemies[0].Hp, Is.EqualTo(3), "第 3 回合回到 1 次");
        }

        [Test]
        public void Snapshot_PassiveIsDeepCopied_NotShared()
        {
            var engine = Engine(new[] { "疾" }, new[] { Dummy() });
            engine.Cast("疾");
            var snapshot = engine.Summons[0].Capture();
            Assert.That(snapshot.Passive, Is.Not.SameAs(engine.Summons[0].Passive),
                "快照与实体共享同一条被动会让改一个连带改另一个");
        }
    }
}
