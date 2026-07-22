using System;
using System.Linq;
using Brushblade.Core;
using NUnit.Framework;

namespace Brushblade.Core.Tests
{
    /// <summary>拆合引擎:规则来自第 4 章 4.4/4.7 与第 3 章 3.8;容量基准来自第 10 章 10.1。</summary>
    public class ForgeEngineTests
    {
        private static RecipeGraph Graph() => new(new[]
        {
            new CharDef("木", Element.Wood),
            new CharDef("火", Element.Fire),
            new CharDef("丁", null), // 中性部件
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("炎", Element.Fire, new[] { "火", "火" }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }),
            new CharDef("森", Element.Wood, new[] { "林", "木" }),   // 三叠:字(林)+部件(木)
            new CharDef("䨺", Element.Wood, new[] { "森", "林" }),   // 合成图测试字:两字原料,用于库满分支
        });

        private static ForgeState State(string[] library, string[] pool) => new(library, pool);

        // ---- RecipeGraph ----

        [Test]
        public void RecipeElements_DerivedFromIngredients_Deduped()
        {
            // 焚 = 林(木) + 火(火) → {木, 火} → 与 WuxingResolver 相生判定衔接
            var elements = Graph().RecipeElements("焚");
            Assert.That(elements, Is.EquivalentTo(new[] { Element.Wood, Element.Fire }));
        }

        [Test]
        public void RecipeElements_NeutralIngredientIgnored()
        {
            Assert.That(Graph().RecipeElements("灯"), Is.EquivalentTo(new[] { Element.Fire }));
        }

        // ---- 拆(Dismantle) ----

        [Test]
        public void Dismantle_CharsToLibrary_LeavesToPool() // 2026-07-22:字回库、部件回池
        {
            // 森 = 林(可合成字)+ 木(部件) → 林 回字库、木 回部件池
            var result = ForgeEngine.TryDismantle("森", Graph(), State(new[] { "森" }, Array.Empty<string>()), 12, 6);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "林" }));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "木" }));
        }

        [Test]
        public void Dismantle_Fen_LinToLibrary_HuoToPool() // 焚=林+火:林回库、火回池
        {
            var result = ForgeEngine.TryDismantle("焚", Graph(), State(new[] { "焚" }, Array.Empty<string>()), 12, 6);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "林" }));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "火" }));
        }

        [Test]
        public void Dismantle_LibraryWouldOverflow_Rejected() // 字原料放不回字库则整体失败
        {
            // 䨺 = 森 + 林,两个字原料;拆掉 䨺 腾 1 位,净 +1;库容 2 且已有 1 张 → 溢出
            var result = ForgeEngine.TryDismantle("䨺", Graph(),
                State(new[] { "䨺", "炎" }, Array.Empty<string>()), 12, libraryCapacity: 2);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.LibraryFull));
        }

        [Test]
        public void Dismantle_ThenCompose_RoundTrips() // 拆完能合回去(274c7e0 珍视的自洽性)
        {
            var dismantled = ForgeEngine.TryDismantle("森", Graph(), State(new[] { "森" }, Array.Empty<string>()), 12, 6);
            var recomposed = ForgeEngine.TryCompose("森", Graph(), dismantled.State, 6);
            Assert.That(recomposed.Success, Is.True);
            Assert.That(recomposed.State.Library, Does.Contain("森"));
        }

        [Test]
        public void Dismantle_LeafComponent_Rejected()
        {
            var result = ForgeEngine.TryDismantle("火", Graph(), State(new[] { "火" }, Array.Empty<string>()), 12, 6);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.NotDismantlable));
        }

        [Test]
        public void Dismantle_CharNotInLibrary_Rejected()
        {
            var result = ForgeEngine.TryDismantle("焚", Graph(), State(Array.Empty<string>(), Array.Empty<string>()), 12, 6);
            Assert.That(result.Error, Is.EqualTo(ForgeError.NotInLibrary));
        }

        [Test]
        public void Dismantle_PoolWouldOverflow_Rejected() // 部件放不回池则失败(焚只回 1 个部件:火)
        {
            var pool = Enumerable.Repeat("木", 12).ToArray(); // 12 + 1(火)> 12
            var result = ForgeEngine.TryDismantle("焚", Graph(), State(new[] { "焚" }, pool), 12, 6);
            Assert.That(result.Error, Is.EqualTo(ForgeError.PoolWouldOverflow));
        }

        // ---- 合(Compose) ----

        [Test]
        public void Compose_ConsumesIngredients_AddsCharToLibrary()
        {
            var result = ForgeEngine.TryCompose("林", Graph(), State(Array.Empty<string>(), new[] { "木", "木", "火" }), 8);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "林" }));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "火" }));
        }

        [Test]
        public void Compose_MultisetCounting_OneWoodCannotMakeLin()
        {
            var result = ForgeEngine.TryCompose("林", Graph(), State(Array.Empty<string>(), new[] { "木", "火" }), 8);
            Assert.That(result.Error, Is.EqualTo(ForgeError.MissingIngredients));
        }

        [Test]
        public void Compose_IngredientCanBeLowerTierChar() // 焚 = 林 + 火,林在池中
        {
            var result = ForgeEngine.TryCompose("焚", Graph(), State(Array.Empty<string>(), new[] { "林", "火" }), 8);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "焚" }));
            Assert.That(result.State.Pool, Is.Empty);
        }

        [Test]
        public void Compose_LibraryFull_Rejected()
        {
            var library = Enumerable.Repeat("灯", 8).ToArray();
            var result = ForgeEngine.TryCompose("林", Graph(), State(library, new[] { "木", "木" }), 8);
            Assert.That(result.Error, Is.EqualTo(ForgeError.LibraryFull));
        }

        [Test]
        public void Compose_UnknownChar_Rejected()
        {
            var result = ForgeEngine.TryCompose("龘", Graph(), State(Array.Empty<string>(), Array.Empty<string>()), 8);
            Assert.That(result.Error, Is.EqualTo(ForgeError.UnknownChar));
        }

        [Test]
        public void Compose_IngredientConsumedFromLibrary() // 3.9 战例:合林(入字库)→ 合焚(消耗字库的林)
        {
            var result = ForgeEngine.TryCompose("焚", Graph(), State(new[] { "林" }, new[] { "火" }), 8);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "焚" }));
            Assert.That(result.State.Pool, Is.Empty);
        }

        [Test]
        public void Compose_PoolPreferredOverLibrary() // 池中有同名原料时不动字库
        {
            var result = ForgeEngine.TryCompose("焚", Graph(), State(new[] { "林" }, new[] { "林", "火" }), 8);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "林", "焚" }));
            Assert.That(result.State.Pool, Is.Empty);
        }

        // ---- 提示(Suggest,第 4 章 4.7) ----

        [Test]
        public void Suggest_ListsFullySatisfiedRecipes()
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "木", "木", "火" }, Array.Empty<string>());
            Assert.That(suggest.Composable, Is.EquivalentTo(new[] { "林" }));
        }

        [Test]
        public void Suggest_NearMiss_MissingExactlyOne() // "还差一个『林』即可合『焚』"
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "木", "木", "火" }, Array.Empty<string>());
            var byChar = suggest.NearMisses.ToDictionary(n => n.CharId, n => n.MissingIngredient);
            Assert.That(byChar["焚"], Is.EqualTo("林"));
            Assert.That(byChar["炎"], Is.EqualTo("火")); // 有火×1,还差一个火
        }

        [Test]
        public void Suggest_MissingTwo_NotListed()
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "丁" }, Array.Empty<string>());
            Assert.That(suggest.Composable, Is.Empty);
            var chars = suggest.NearMisses.Select(n => n.CharId);
            Assert.That(chars, Does.Contain("灯"));      // 差一个火
            Assert.That(chars, Does.Not.Contain("焚")); // 差林+火两个
        }

        [Test]
        public void Suggest_EmptyPool_NothingComposable()
        {
            var suggest = ForgeEngine.Suggest(Graph(), Array.Empty<string>(), Array.Empty<string>());
            Assert.That(suggest.Composable, Is.Empty);
        }

        [Test]
        public void Suggest_SeesLibraryIngredients() // 字库有林、池有火 → 焚应显示为可合成
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "火" }, new[] { "林" });
            Assert.That(suggest.Composable, Does.Contain("焚"));
        }

        [Test]
        public void Suggest_DoesNotCountCharAsItsOwnIngredient() // 字库的林不该让"林"自己显示可合成
        {
            var suggest = ForgeEngine.Suggest(Graph(), Array.Empty<string>(), new[] { "林" });
            Assert.That(suggest.Composable, Is.Empty);
        }

        // ---- 只能合出阵列表里的字(2026-07-20 拍板;注入源见 GameRoot.UnlockedChars) ----

        [Test]
        public void TryCompose_NotUnlocked_Rejected()
        {
            var state = State(Array.Empty<string>(), new[] { "火", "火" });
            var result = ForgeEngine.TryCompose("炎", Graph(), state, 6, unlockedChars: new[] { "林" });
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.NotUnlocked));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "火", "火" })); // 失败不动状态
        }

        [Test]
        public void TryCompose_Unlocked_Succeeds()
        {
            var state = State(Array.Empty<string>(), new[] { "火", "火" });
            var result = ForgeEngine.TryCompose("炎", Graph(), state, 6, unlockedChars: new[] { "炎" });
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "炎" }));
        }

        [Test]
        public void TryCompose_NullUnlocked_NoRestriction() // 缺省不限(工装与旧调用)
        {
            var state = State(Array.Empty<string>(), new[] { "火", "火" });
            Assert.That(ForgeEngine.TryCompose("炎", Graph(), state, 6).Success, Is.True);
        }

        [Test]
        public void Suggest_HidesLockedChars() // 合不出来的字不该出现在拆合台
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "木", "木", "火" },
                Array.Empty<string>(), unlockedChars: new[] { "焚" });
            Assert.That(suggest.Composable, Is.Empty);                          // 林未收集
            Assert.That(suggest.NearMisses.Select(n => n.CharId), Does.Not.Contain("炎"));
        }

        [Test]
        public void Suggest_KeepsUnlockedChars()
        {
            var suggest = ForgeEngine.Suggest(Graph(), new[] { "木", "木", "火" },
                Array.Empty<string>(), unlockedChars: new[] { "林", "炎" });
            Assert.That(suggest.Composable, Is.EquivalentTo(new[] { "林" }));
            Assert.That(suggest.NearMisses.Select(n => n.CharId), Does.Contain("炎"));
        }
    }
}
