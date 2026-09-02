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
            new CharDef("水", Element.Water),
            new CharDef("冫", Element.Water),
            new CharDef("氵", Element.Water),
            new CharDef("禾", Element.Wood),  // 清单外的形声部件:element 是 Wood 但不与 木 等价
            new CharDef("刂", Element.Metal),
            new CharDef("金", Element.Metal),
            new CharDef("林", Element.Wood, new[] { "木", "木" }),
            new CharDef("炎", Element.Fire, new[] { "火", "火" }),
            new CharDef("灯", Element.Fire, new[] { "火", "丁" }),
            new CharDef("焚", Element.Fire, new[] { "林", "火" }),
            new CharDef("森", Element.Wood, new[] { "林", "木" }),   // 三叠:字(林)+部件(木)
            new CharDef("䨺", Element.Wood, new[] { "森", "林" }),   // 合成图测试字:两字原料,用于库满分支
            new CharDef("冰", Element.Water, new[] { "冫", "水" }),
            new CharDef("利", Element.Metal, new[] { "禾", "刂" }),
        });

        // 只给「拆池中部件」测试用——独立于 Graph()(2026-09-01 二级拆解)。
        // ⚠ 2026-09-01 最终评审:枚举过全部 8 条 Suggest 测试后确认非叶子的 烝/蒸 混进
        // Graph() 其实不会让那些精确列表断言变红——这不是在修一个已发生的冲突。这是
        // 预防性隔离:把新功能(带配方的部件)的夹具与共享图谱分开,避免以后谁往 Graph()
        // 加东西时要连带考虑 Suggest 断言。隔离本身仍有防回归价值,不要因此删掉本方法。
        private static RecipeGraph ComponentGraph() => new(Graph().All.Concat(new[]
        {
            // 带配方的部件(2026-09-01 二级拆解):烝 = 丞 + 灬 —— 既归部件池,又能再拆
            new CharDef("丞", null, isComponent: true),
            new CharDef("灬", Element.Fire, isComponent: true),
            new CharDef("烝", null, new[] { "丞", "灬" }, isComponent: true),
            new CharDef("蒸", Element.Fire, new[] { "艹", "烝" }),
            new CharDef("艹", Element.Wood, isComponent: true),
        }));

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

        // ---- 拆池中部件(2026-09-01 二级拆解)----

        [Test]
        public void Dismantle_ComponentInPool_ProductsStayInPool()
        {
            // 烝 = 丞 + 灬,两个都是部件 → 全进池;源 烝 从池里移除
            var result = ForgeEngine.TryDismantle("烝", ComponentGraph(),
                State(Array.Empty<string>(), new[] { "烝" }), 6, 12);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "丞", "灬" }));
            Assert.That(result.State.Library, Is.Empty);
        }

        [Test]
        public void Dismantle_ComponentInPool_CostsOnePoolSlotNet()
        {
            // 池容量 3,已有 烝 + 2 个占位 → 拆完是 丞 + 灬 + 2 = 4 > 3,应当整体失败
            var before = State(Array.Empty<string>(), new[] { "烝", "木", "火" });
            var result = ForgeEngine.TryDismantle("烝", ComponentGraph(), before, 3, 12);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.PoolWouldOverflow));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "烝", "木", "火" }), "失败不动状态");

            // 容量放到 4 就过得去(净 +1)
            Assert.That(ForgeEngine.TryDismantle("烝", ComponentGraph(), before, 4, 12).Success, Is.True);
        }

        [Test]
        public void Dismantle_ComponentNotInPoolOrLibrary_Fails()
        {
            var result = ForgeEngine.TryDismantle("烝", ComponentGraph(),
                State(Array.Empty<string>(), new[] { "木" }), 6, 12);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.NotInLibrary));
        }

        [Test]
        public void Dismantle_ComponentWithoutRecipe_StillNotDismantlable()
        {
            var result = ForgeEngine.TryDismantle("丞", ComponentGraph(),
                State(Array.Empty<string>(), new[] { "丞" }), 6, 12);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.NotDismantlable));
        }

        [Test]
        public void Dismantle_CharWithComponentIngredient_ComponentGoesToPool()
        {
            // 蒸 = 艹 + 烝:两个都是部件(烝 有配方但仍是部件)→ 都进池,不进字库
            var result = ForgeEngine.TryDismantle("蒸", ComponentGraph(),
                State(new[] { "蒸" }, Array.Empty<string>()), 6, 12);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "艹", "烝" }));
            Assert.That(result.State.Library, Is.Empty);
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

        // ---- 可合成集闭包(2026-09-03:拆出来的中间字要合得回去)----

        [Test]
        public void ComposableSet_IncludesRecipeIngredientsTransitively()
        {
            // 用户 2026-09-03 报的 bug:蕉 拆出 焦,焦 再拆出 隹 + 灬,但 焦 不在出阵列表里,
            // 于是 隹 + 灬 合不回 焦 —— 拆解成了一条不可逆的单行道。
            // 这里用同构的 焚 = 林 + 火、林 = 木 + 木 复现:出阵只有 焚,闭包要连 林/木/火 一起收。
            var set = ForgeEngine.ComposableSet(Graph(), new[] { "焚" });

            Assert.That(set.Contains("焚"), Is.True);
            Assert.That(set.Contains("林"), Is.True, "一级原料");
            Assert.That(set.Contains("木"), Is.True, "二级原料 —— 闭包是递归的");
            Assert.That(set.Contains("火"), Is.True);
            Assert.That(set.Contains("炎"), Is.False, "与出阵字无关的字不该凭空多出来");
        }

        [Test]
        public void ComposableSet_NullStaysUnlimited()
        {
            Assert.That(ForgeEngine.ComposableSet(Graph(), null), Is.Null);
        }

        [Test]
        public void TryCompose_IngredientOfUnlockedChar_IsAllowed()
        {
            // 闭包接上之后:出阵只有 焚,手里两个 木 也能合回 林(此前报 NotUnlocked)
            var state = State(Array.Empty<string>(), new[] { "木", "木" });
            var result = ForgeEngine.TryCompose("林", Graph(), state, 6,
                ForgeEngine.ComposableSet(Graph(), new[] { "焚" }));

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EquivalentTo(new[] { "林" }));
        }

        [Test]
        public void TryCompose_ComponentProduct_GoesToPoolNotLibrary()
        {
            // 带配方的部件(烝 = 丞 + 灬)合出来仍是**部件**,该回部件池 ——
            // 归位规则与 TryDismantle 同一条(部件回池、可出牌字回字库)。
            var state = State(Array.Empty<string>(), new[] { "丞", "灬" });
            var result = ForgeEngine.TryCompose("烝", ComponentGraph(), state, 6, poolCapacity: 6);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "烝" }));
            Assert.That(result.State.Library, Is.Empty, "部件不该占字库位");
        }

        [Test]
        public void TryCompose_ComponentProduct_RespectsPoolCapacity()
        {
            // 池放不下时要报 PoolWouldOverflow(而不是错报 LibraryFull,更不能静默塞进字库)。
            // 容量判在消耗原料之后,与字库那一支同口径。
            var state = State(Array.Empty<string>(), new[] { "丞", "灬" });
            var result = ForgeEngine.TryCompose("烝", ComponentGraph(), state, 6, poolCapacity: 0);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.PoolWouldOverflow));
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "丞", "灬" }), "失败不动状态");
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

        // ---- 部件五系通用(spec 2026-08-15 §1.3)----

        /// <summary>配方要 冫,池里只有 水 也能合 —— 同系部件在匹配上等价。</summary>
        [Test]
        public void Compose_AcceptsKinComponent()
        {
            var result = ForgeEngine.TryCompose("冰", Graph(),
                State(Array.Empty<string>(), new[] { "水", "水" }), 10);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Library, Is.EqualTo(new[] { "冰" }));
            Assert.That(result.State.Pool, Is.Empty);
        }

        /// <summary>氵+冫 同样合得出 冰(两个都不是配方字面量)。</summary>
        [Test]
        public void Compose_AcceptsKinComponent_ForBothIngredients()
        {
            var result = ForgeEngine.TryCompose("冰", Graph(),
                State(Array.Empty<string>(), new[] { "氵", "冫" }), 10);
            Assert.That(result.Success, Is.True);
        }

        /// <summary>跨系不等价:木 不能顶 水。</summary>
        [Test]
        public void Compose_RejectsCrossElementComponent()
        {
            var result = ForgeEngine.TryCompose("冰", Graph(),
                State(Array.Empty<string>(), new[] { "木", "木" }), 10);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.MissingIngredients));
        }

        /// <summary>清单外的部件不参与:禾 的 element 是 Wood,但 木 顶不了它。
        /// 这条守着「不许从 element 推导等价」那道口子。</summary>
        [Test]
        public void Compose_RejectsSubstitutionForPartsOutsideTheKinList()
        {
            var result = ForgeEngine.TryCompose("利", Graph(),
                State(Array.Empty<string>(), new[] { "木", "金" }), 10);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(ForgeError.MissingIngredients));
        }

        /// <summary>金系四member:刂 可由 金 顶(配方 禾+刂,池里 禾+金)。</summary>
        [Test]
        public void Compose_AcceptsKinComponent_WithinMetalGroup()
        {
            var result = ForgeEngine.TryCompose("利", Graph(),
                State(Array.Empty<string>(), new[] { "禾", "金" }), 10);
            Assert.That(result.Success, Is.True);
        }

        /// <summary>**不变量**:拆字产出不归一化(spec §1.2) —— 拆 灯(火+丁) 仍得 火+丁,
        /// 不许被换成组内代表字。变体在池中并存是设计板位形展示的前提。
        /// 这条今天就是绿的,放进来是防止将来有人"顺手"给拆字也加一层归一化。</summary>
        [Test]
        public void Dismantle_DoesNotNormalizeComponents()
        {
            var result = ForgeEngine.TryDismantle("灯", Graph(),
                State(new[] { "灯" }, Array.Empty<string>()), 10, 10);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Pool, Is.EquivalentTo(new[] { "火", "丁" }));
        }

        /// <summary>**精确优先**(spec §1.3):池里同时有 冫 和 水,合 冰(配方 冫+水)时
        /// 先吃掉更"专用"的 冫,剩下的 水 用于配方里的 水 —— 池应正好清空。
        /// 若改成等价优先,会先用 水 顶 冫,再用 冫 顶 水,结果同样清空但取用顺序不可预期;
        /// 本条真正守的是下一条(多余同系部件不被误吃)。</summary>
        [Test]
        public void Compose_PrefersExactMatchOverKin()
        {
            var result = ForgeEngine.TryCompose("冰", Graph(),
                State(Array.Empty<string>(), new[] { "冫", "水", "氵" }), 10);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Pool, Is.EqualTo(new[] { "氵" }),
                "精确的 冫 与 水 被吃掉,多余的 氵 原样留在池里");
        }

        /// <summary>Suggest 必须与 TryCompose 同口径:池里 水+水 时 冰(配方 冫+水)算**可合成**,
        /// 而不是"差一个 冫"。两者不一致会让拆合台提示骗人。</summary>
        [Test]
        public void Suggest_CountsKinComponentsAsComposable()
        {
            var result = ForgeEngine.Suggest(Graph(), new[] { "水", "水" }, Array.Empty<string>());
            Assert.That(result.Composable, Contains.Item("冰"));
            Assert.That(result.NearMisses.Select(m => m.CharId), Does.Not.Contain("冰"));
        }

        /// <summary>撞车共存(spec §1.4):池里 水+水 时,沝(水+水)与 冰(冫+水)都该列为可合成 ——
        /// 合成是玩家指定目标字,不是从原料反推产物,两个都给玩家自己选。</summary>
        [Test]
        public void Suggest_ListsBothCharsWhenRecipesCollideUnderKin()
        {
            var graph = new RecipeGraph(new[]
            {
                new CharDef("水", Element.Water),
                new CharDef("冫", Element.Water),
                new CharDef("沝", Element.Water, new[] { "水", "水" }),
                new CharDef("冰", Element.Water, new[] { "冫", "水" }),
            });
            var result = ForgeEngine.Suggest(graph, new[] { "水", "水" }, Array.Empty<string>());
            Assert.That(result.Composable, Contains.Item("沝"));
            Assert.That(result.Composable, Contains.Item("冰"));
        }

        /// <summary>一致性:Suggest 说可合成的,TryCompose 必须真能合出来。</summary>
        [Test]
        public void Suggest_ComposableAgreesWithTryCompose()
        {
            var graph = Graph();
            string[] pool = { "水", "水", "禾", "金" };
            var suggest = ForgeEngine.Suggest(graph, pool, Array.Empty<string>());
            foreach (var id in suggest.Composable)
            {
                var composed = ForgeEngine.TryCompose(id, graph, State(Array.Empty<string>(), pool), 10);
                Assert.That(composed.Success, Is.True, $"Suggest 说 {id} 可合成,TryCompose 却失败");
            }
        }
    }
}
