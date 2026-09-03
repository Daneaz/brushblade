using System.Collections.Generic;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>设计板主题(docs/design/字斗设计板.html):语义调色板 + 程序化 sprite + 双字体。
    /// 色值为设计板 oklch 预转 sRGB;View 层只准引用这里的语义色。</summary>
    public static class Theme
    {
        // ---- 调色板 ----
        public static readonly Color Paper = new(0.966f, 0.946f, 0.905f);       // 宣纸底
        public static readonly Color PaperDim = new(0.869f, 0.842f, 0.788f);    // 进度条底/分隔
        public static readonly Color Ink = new(0.066f, 0.087f, 0.122f);         // 墨黑(深底)
        public static readonly Color InkSoft = new(0.239f, 0.304f, 0.41f);      // 深灰蓝按钮
        public static readonly Color TextMain = new(0.088f, 0.105f, 0.132f);
        public static readonly Color TextDim = new(0.363f, 0.391f, 0.435f);
        public static readonly Color CardWhite = Color.white;
        public static readonly Color Cinnabar = new(0.772f, 0.211f, 0.215f);    // 朱砂
        public static readonly Color CinnabarDark = new(0.607f, 0.117f, 0.135f);
        public static readonly Color WarnBg = new(0.984f, 0.890f, 0.886f);   // 稿 #FBE3E2:不可逆告警条底
        public static readonly Color WarnText = new(0.607f, 0.117f, 0.133f); // 稿 #9B1E22:告警条字色
        public static readonly Color Jade = new(0.264f, 0.58f, 0.347f);         // 翠玉
        public static readonly Color Gold = new(0.791f, 0.617f, 0.199f);        // 赭金
        public static readonly Color GoldBorder = new(0.56f, 0.421f, 0.037f);
        public static readonly Color GoldText = new(0.251f, 0.161f, 0.0f);
        public static readonly Color GoldSoft = new(0.965f, 0.929f, 0.835f);    // 金系浅底(墨锭条/满级牌脚)
        public static readonly Color GoldDeep = new(0.561f, 0.42f, 0.035f);     // 压在 GoldSoft 上的字色
        public static readonly Color AdGreen = new(0.232f, 0.586f, 0.332f);
        public static readonly Color AdGreenBg = new(0.892f, 0.955f, 0.901f);
        public static readonly Color AdGreenText = new(0.044f, 0.364f, 0.165f);
        public static readonly Color ExitPink = new(0.477f, 0.246f, 0.362f);
        public static readonly Color ShopNav = new(0.654f, 0.349f, 0.241f);
        public static readonly Color PanelPaper = new(0.984f, 0.973f, 0.945f);   // 面板底(比宣纸底亮一档)
        public static readonly Color PanelBorder = new(0.871f, 0.843f, 0.788f);  // 面板描边(稿上统一 1pt)
        // 稿 #F1EBDE:面板内嵌/凹槽条的底色(如 Reward 选字页牌下方那条 detail 横条)。
        // ⚠ 2026-09-02 review 修:此前这类凹槽误用了 PaperDim(#DED7C9,进度条底)——
        // PaperDim 与 PanelBorder(同样 #DED7C9)撞色,套进 OutlinedPanel 会变成一块
        // 没有描边的灰褐实心板,正是 OutlinedPanel 自己文档里警告的「浅色卡融进浅色底」。
        // PanelInset 单独占一个色阶,别跟 PanelPaper(卡片底)、PaperDim(进度条底)混用。
        public static readonly Color PanelInset = new(0.945f, 0.922f, 0.871f);
        public static readonly Color LockedBg = new(0.856f, 0.843f, 0.816f);
        // 未拥有的字牌(2026-09-03,稿 Main.dc.html 的 .card.locked):牌面褪成宣纸灰、字形压浅。
        // 两条都比 LockedBg 亮 —— 那个是按钮的禁用底,压在牌上会把整格看成一块死板
        public static readonly Color LockedPaper = new(0.937f, 0.918f, 0.878f);  // 稿 #EFEAE0
        public static readonly Color LockedGlyph = new(0.686f, 0.651f, 0.584f);  // 稿 #AFA695
        public static readonly Color LockGray = new(0.534f, 0.563f, 0.611f);
        public static readonly Color DoneGreen = new(0.161f, 0.525f, 0.276f);
        public static readonly Color NeutralPart = new(0.309f, 0.336f, 0.379f); // 中性部件底
        public static readonly Color IngotDark = new(0.1f, 0.122f, 0.17f);      // 墨锭图标
        public static readonly Color IngotGold = new(0.615f, 0.481f, 0.166f);   // 金锭图标(价格)
        public static readonly Color SplitBlue = new(0.098f, 0.311f, 0.506f);   // 「拆」按钮
        public static readonly Color UpgradeText = new(0.107f, 0.333f, 0.173f);
        public static readonly Color Shadow = new(0.088f, 0.105f, 0.132f, 0.08f);
        public static readonly Color Scrim = new(0.088f, 0.105f, 0.132f, 0.55f);  // 模态遮罩
        /// <summary>局内浮层的浅遮罩(稿 rgba(22,27,34,.42))。战利品/换字这几张浮在战斗屏上,
        /// 底下那半张脸要留着 —— 玩家才知道自己还在第几层、字库里有什么。
        /// 全遮死就成了「不知从哪冒出来的窗」(Reward.dc.html / Replace.dc.html 的原话)。</summary>
        public static readonly Color ScrimSoft = new(0.086f, 0.106f, 0.133f, 0.42f);
        /// <summary>段末横幅的**纸色**罩(稿 rgba(246,241,231,.72))。胜负横幅压的是自家宣纸底,
        /// 不是墨色 —— 墨罩会把战场压成深色,与「本段告捷」的明快读感相反(RunEnd.dc.html)。</summary>
        public static readonly Color ScrimPaper = new(0.965f, 0.945f, 0.906f, 0.72f);

        // 层段背景基色(20.2 每段换景):字林竹绿/词渊黛蓝/文山赭石/墨海墨青
        private static readonly Color[] BandInks =
        {
            new(0.42f, 0.58f, 0.38f),
            new(0.36f, 0.48f, 0.64f),
            new(0.66f, 0.52f, 0.34f),
            new(0.28f, 0.32f, 0.42f),
        };

        private static Color BandInk(int bandIndex) =>
            BandInks[Mathf.Min(bandIndex, BandInks.Length - 1)];

        /// <summary>层段宣纸底:随层段换基色,同层段内逐段(每 5 层)加深——进新段有体感。</summary>
        public static Color BandPaper(int bandIndex, int segmentInBand) =>
            Color.Lerp(Paper, BandInk(bandIndex),
                Mathf.Min(0.22f, 0.09f + 0.035f * segmentInBand));

        /// <summary>薄宣纸卡(拆合台等浮层):半透白,透出层段染色自动同调,水印隐约可见。</summary>
        public static readonly Color PaperCard = new(1f, 0.995f, 0.975f, 0.62f);

        /// <summary>层段巨字水印色(背景大字,近乎透明的墨痕)。</summary>
        public static Color BandWatermark(int bandIndex)
        {
            var ink = BandInk(bandIndex) * 0.55f;
            ink.a = 0.10f;
            return ink;
        }

        /// <summary>稀有度色:枚举名 = 皮肤色 = 强度序(见 <see cref="CardRarity"/>),
        /// 视觉层级 白→绿→蓝→紫→金→橙→红。</summary>
        public static Color RarityColor(CardRarity rarity) => rarity switch
        {
            CardRarity.Green => new Color(0.181f, 0.621f, 0.323f),
            CardRarity.Blue => new Color(0.06f, 0.455f, 0.771f),
            CardRarity.Purple => new Color(0.475f, 0.269f, 0.669f),
            CardRarity.Gold => new Color(0.788f, 0.663f, 0.29f),    // 金 #c9a94a
            CardRarity.Orange => new Color(0.883f, 0.473f, 0.106f), // 橙
            CardRarity.Red => new Color(0.802f, 0.151f, 0.181f),    // 红
            _ => new Color(0.632f, 0.62f, 0.594f), // 白
        };

        /// <summary>宝箱配色 = **卡牌稀有度色**(2026-08-30 拍板并成一套)。
        ///
        /// 曾经是 <c>RarityColor((CardRarity)(int)tier)</c>,后来拆成独立一张表 ——
        /// 因为那时 <see cref="ChestTier"/> 只有六档,强转让 Crimson(赤霄匣,当时 = 6)
        /// 拿到橙色、名不副实。2026-08-29 补上朱漆匣之后两个枚举**七档一一对应**
        /// (<see cref="RarityOf"/>),那个坑不存在了,于是并回一套。
        ///
        /// 并之前两张表只差**橙**这一档(朱漆 <c>#D4602A</c> vs 稀有度橙 <c>#E1791B</c>),
        /// 其余六档本来就逐字节相同。这里不再抄一遍数值 —— 委托过去,并了就永远不会再分家。
        ///
        /// ⚠ 宝箱立绘的属性色是**出图时烤进 PNG 的**(<c>tools/design/build_chests.py</c> 的
        /// TIERS 表),改这里要同步改那边并重跑脚本,否则立绘与色块兜底两套色。</summary>
        public static Color ChestColor(ChestTier tier) => RarityColor(RarityOf(tier));

        /// <summary>档位 → 同序稀有度(白绿蓝紫金橙红)。显式写出来而不用强转:
        /// 强转在两个枚举长度再次分家时会静默错位,这张表则会编译不过。</summary>
        public static CardRarity RarityOf(ChestTier tier) => tier switch
        {
            ChestTier.Bamboo => CardRarity.Green,     // 竹简匣
            ChestTier.Celadon => CardRarity.Blue,     // 青瓷匣
            ChestTier.Rosewood => CardRarity.Purple,  // 紫檀匣
            ChestTier.Gilded => CardRarity.Gold,      // 鎏金匣
            ChestTier.Vermilion => CardRarity.Orange, // 朱漆匣
            ChestTier.Crimson => CardRarity.Red,      // 赤霄匣
            _ => CardRarity.White,                    // 素纸匣
        };

        public static Color ElementColor(Element? element) => element switch
        {
            Element.Fire => new Color(0.772f, 0.211f, 0.215f),
            Element.Water => new Color(0.06f, 0.455f, 0.771f),
            Element.Wood => new Color(0.204f, 0.561f, 0.309f),
            Element.Earth => new Color(0.6f, 0.486f, 0.235f),
            Element.Metal => new Color(0.702f, 0.638f, 0.507f),
            Element.Heart => new Color(0.592f, 0.312f, 0.655f),
            _ => NeutralPart,
        };

        /// <summary>能力 chip 底色:朱砂 = 增长的威胁,翠玉 = 恢复,深灰蓝 = 防御/辅助/信息类。</summary>
        public static Color AbilityChipColor(EnemyAbility ability) => ability switch
        {
            EnemyAbility.Scorch => Cinnabar, // 越磨越烫
            EnemyAbility.Sear => Cinnabar,   // 灼身:与焦痕同为火系威胁
            EnemyAbility.Barb => Cinnabar,   // 反噬:打它就疼,与自燃同属「越打越亏」
            EnemyAbility.Regrow => Jade,     // 自我修复
            EnemyAbility.Mend => Jade,       // 治疗同伴:与自补全同属恢复
            _ => InkSoft,                    // 叠字/标点/通假/生僻
        };

        /// <summary>Boss 技能 chip 底色:主动技能走朱砂(威胁),坚壁走深灰蓝(防御)。</summary>
        public static Color BossSkillChipColor(BossSkill skill) =>
            skill == BossSkill.Bulwark ? InkSoft : Cinnabar;

        /// <summary>字形专用属性色(2026-07-28):比 UI 色块用的 <see cref="ElementColor"/> 加深,
        /// 保证在白/宣纸/暖灰底上都过 WCAG 4.5:1。起因是金 #B3A382 对纯白只有 2.48,
        /// 底色再白也够不到大字门槛 —— 那是字色本身的问题。
        /// 金往冷灰偏、土往红褐偏是刻意的:直接加深会双双变成暗黄褐(ΔE 仅 3.2)分不开,
        /// 而组合字「桂」的左右两半正要靠这点色差区分。</summary>
        public static Color GlyphColor(Element? element) => element switch
        {
            Element.Fire => new Color(0.690f, 0.176f, 0.180f),   // #B02D2E
            Element.Water => new Color(0.039f, 0.369f, 0.620f),  // #0A5E9E
            Element.Wood => new Color(0.122f, 0.388f, 0.200f),   // #1F6333
            Element.Metal => new Color(0.420f, 0.392f, 0.286f),  // #6B6449 冷灰调
            Element.Earth => new Color(0.510f, 0.333f, 0.165f),  // #82552A 红褐调
            Element.Heart => new Color(0.494f, 0.255f, 0.565f),  // #7E4190
            _ => TextMain,
        };

        /// <summary>属性淡底(部件池方块)。</summary>
        public static Color ElementSoft(Element? element) => element switch
        {
            Element.Fire => new Color(0.995f, 0.823f, 0.806f),
            Element.Water => new Color(0.785f, 0.883f, 0.986f),
            Element.Wood => new Color(0.801f, 0.902f, 0.817f),
            Element.Earth => new Color(0.925f, 0.864f, 0.741f),
            Element.Metal => new Color(0.933f, 0.892f, 0.81f),
            Element.Heart => new Color(0.925f, 0.835f, 0.945f),
            _ => LockedBg,
        };

        // ---- 底部导航页签配色(2026-08-28 反馈:四格各给一色) ----

        /// <summary>一个页签的三支色:浅底 + 同色系描边 + 深色前景(名字与图标)。
        ///
        /// 三支**同源于一个属性色**,不是手挑的 —— 手挑四组颜色必然有一组不搭,
        /// 而且改配色时要改十二个数。属性色板本身已经过对比度校准
        /// (见 <see cref="GlyphColor"/> 的注释:金系原色对浅底只有 2.48:1,故另有一套加深色),
        /// 蹭它就等于蹭了那次校准。</summary>
        public readonly struct TabPalette
        {
            public readonly Color Bg;
            public readonly Color Border;
            public readonly Color Fg;

            private TabPalette(Color bg, Color border, Color fg)
            {
                Bg = bg; Border = border; Fg = fg;
            }

            /// <summary>由属性色派生:前景取 ElementSoftFg,底色是 PanelPaper 往 ElementSoft 走 35%
            /// (够看出是哪一格,又不压过宣纸底),描边再从 ElementSoft 往前景压 22%。
            ///
            /// ⚠ 描边**不能**直接用 ElementSoft:那样边线对宣纸底只有 1.19 的对比度,比原来的
            /// 素色描边(1.27)还弱 —— 页签一上色反而更塌,与「给页签立体感」的初衷相反。
            /// 压过之后是 1.65,边线清清楚楚而不刺眼。</summary>
            public static TabPalette FromElement(Element element)
            {
                var soft = ElementSoft(element);
                var fg = ElementSoftFg(element);
                return new TabPalette(Color.Lerp(PanelPaper, soft, 0.35f), Color.Lerp(soft, fg, 0.22f), fg);
            }

            /// <summary>商城不属于五行,用它自己那支赭色(<see cref="ShopNav"/>)同法派生:
            /// 底 / 描边 / 前景三档,越往前景越浓。</summary>
            public static TabPalette FromAccent(Color accent)
            {
                var soft = Color.Lerp(PanelPaper, accent, 0.32f);
                return new TabPalette(Color.Lerp(PanelPaper, soft, 0.35f), Color.Lerp(soft, accent, 0.22f), accent);
            }
        }

        // ⚠ 静态字段按书写顺序初始化:这四条读 PanelPaper / ShopNav,必须排在它们后面
        public static readonly TabPalette DeckTab = TabPalette.FromElement(Element.Water);
        public static readonly TabPalette BestiaryTab = TabPalette.FromElement(Element.Wood);
        public static readonly TabPalette PerkTab = TabPalette.FromElement(Element.Heart);
        public static readonly TabPalette ShopTab = TabPalette.FromAccent(ShopNav);

        public static Color ElementSoftFg(Element? element) => element switch
        {
            Element.Fire => new Color(0.525f, 0.151f, 0.149f),
            Element.Water => new Color(0.056f, 0.31f, 0.526f),
            Element.Wood => new Color(0.097f, 0.36f, 0.18f),
            Element.Earth => new Color(0.39f, 0.283f, 0.0f),
            Element.Metal => new Color(0.394f, 0.324f, 0.174f),
            Element.Heart => new Color(0.403f, 0.212f, 0.446f),
            _ => TextDim,
        };

        // ---- 字体 ----
        private static Font _title, _body;

        /// <summary>思源宋体子集:标题/字牌大字/怪物字。缺资源时回退 Ui.Font。</summary>
        public static Font TitleFont => _title ??= Resources.Load<Font>("NotoSerifSC-Subset") ?? Ui.Font;

        /// <summary>思源黑体子集:按钮/正文。</summary>
        public static Font BodyFont => _body ??= Resources.Load<Font>("NotoSansSC-Subset") ?? Ui.Font;

        // ---- 程序化 sprite(生成一次,静态缓存) ----
        private static readonly Dictionary<int, Sprite> _rounded = new();
        private static Sprite _circle, _ingot, _triangle;

        /// <summary>圆角矩形 9-slice。radius 按设计板:卡 20 / 牌 14 / 按钮 10 / 胶囊 24。</summary>
        public static Sprite Rounded(int radius)
        {
            if (_rounded.TryGetValue(radius, out var cached)) return cached;
            int size = radius * 2 + 8;
            var tex = NewTex(size, size);
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 到圆角矩形边界的有符号距离(圆角圆心为四角内缩 r)
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - dist + 0.5f)));
                }
            tex.Apply();
            var border = radius + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            _rounded[radius] = sprite;
            return sprite;
        }

        /// <summary>墨晕外扩宽度(px):牌沿向外洇开多远。<see cref="Halo"/> 与调用方的
        /// RectTransform 外扩量必须用同一个数,不然贴图里的渐变对不上牌的边界。</summary>
        public const int HaloPad = 10;

        private static readonly Dictionary<int, Sprite> _halo = new();

        /// <summary>墨晕 9-slice(2026-08-30):牌沿一线属性色,向外 <see cref="HaloPad"/> px
        /// 洇开到透明,**牌内全透明**。
        ///
        /// 给「这张牌刚到手」用。上一版是 <see cref="Rounded"/> + fillCenter=false 的实边,
        /// 边宽等于九宫格 border(radius+2),56 见方的部件牌被吃掉大半 —— 而发光是唯一
        /// 不占版面的强调方式:牌面、四角、描线一样都不动,只在牌**之外**加一圈会呼吸的光。
        ///
        /// 几何与 Rounded 同一套(到圆角矩形边界的有符号距离),差别是这里把圆角矩形
        /// **内缩 HaloPad**,腾出纹理边缘那一圈给洇开的尾巴;alpha 曲线也换了:
        /// 牌内 1px 收到 0、牌沿冲到 1、向外按平方衰减 —— 平方让尾巴更长更淡,
        /// 线性衰减看着像一圈硬边框。</summary>
        public static Sprite Halo(int radius)
        {
            if (_halo.TryGetValue(radius, out var cached)) return cached;
            int size = (radius + HaloPad) * 2 + 8;
            var tex = NewTex(size, size);
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 圆角圆心 = 四角内缩 (HaloPad + r);signed > 0 在牌内,< 0 在牌外
                    float dx = Mathf.Max(0, Mathf.Max(HaloPad + r - x - 0.5f, x + 0.5f - (size - HaloPad - r)));
                    float dy = Mathf.Max(0, Mathf.Max(HaloPad + r - y - 0.5f, y + 0.5f - (size - HaloPad - r)));
                    float signed = r - Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha;
                    if (signed >= 0)
                        alpha = Mathf.Clamp01(1f - signed);          // 牌内:1px 之内收干净
                    else
                    {
                        float t = Mathf.Clamp01(1f + signed / HaloPad);
                        alpha = t * t;                               // 牌外:平方衰减,尾巴长而淡
                    }
                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            tex.Apply();
            // border 要盖住整条渐变(圆角 + 外扩),中心那块全透明,拉伸不失真
            var border = radius + HaloPad + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            _halo[radius] = sprite;
            return sprite;
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int size = 64;
                var tex = NewTex(size, size);
                const float r = size / 2f - 1f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                            new Vector2(size / 2f, size / 2f));
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - dist + 0.5f)));
                    }
                tex.Apply();
                _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _circle;
            }
        }

        /// <summary>墨锭六边形(设计板 clip-path: 14%,0 86%,0 100%,50% 86%,100% 14%,100% 0,50%)。</summary>
        public static Sprite Ingot => _ingot ??= Convex(56, 34, new[]
        {
            new Vector2(0.14f, 0f), new Vector2(0.86f, 0f), new Vector2(1f, 0.5f),
            new Vector2(0.86f, 1f), new Vector2(0.14f, 1f), new Vector2(0f, 0.5f),
        });

        /// <summary>播放三角(广告位标)。</summary>
        public static Sprite Triangle => _triangle ??= Convex(24, 28, new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 1f),
        });

        private static Sprite Convex(int w, int h, Vector2[] points)
        {
            var tex = NewTex(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                    float minEdge = float.MaxValue;
                    for (int i = 0; i < points.Length; i++)
                    {
                        var a = points[i];
                        var b = points[(i + 1) % points.Length];
                        var edge = b - a;
                        // 逆时针多边形:内侧为左侧;像素级 AA 用法线距离
                        float cross = edge.x * (p.y - a.y) - edge.y * (p.x - a.x);
                        float dist = cross / edge.magnitude * h; // 像素近似
                        minEdge = Mathf.Min(minEdge, dist);
                    }
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(minEdge + 0.5f)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D NewTex(int w, int h) =>
            new(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
    }
}
