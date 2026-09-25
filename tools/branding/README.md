# 品牌资源(App 图标 + 启动图)

产物:`Brushblade/Assets/_Project/Presentation/Branding/`
重出:`python3 tools/branding/build_branding.py`
对账:`python3 -m pytest tools/branding/tests/ -q`

> ⚠ PNG 是**产物,不要手写**——和 `tools/icons/` 同一条规矩。改设计改脚本,然后重跑。

## 设计定稿(2026-09-21)

**图标 = 朱砂印章(白文印)**:满幅 `Theme.Cinnabar` 朱砂,「字」反白,思源宋体 wght 900。

选它的理由,以及另两稿为什么没选:

| 稿 | 形态 | 结论 |
|---|---|---|
| A 朱砂印章 | 满幅朱砂 + 反白「字」 | **采用**。80px 下结构最完整,满幅红在桌面/商店缩略图里辨识度最高 |
| B 宣纸墨字 | 宣纸底 + 墨字 + 角落闲章 | 弃。浅底会融进浅色壁纸;那枚小闲章在 80px 下只剩一个红点 |
| C 拆合 | 墨底 + 「字」沿 宀/子 拆开,缝里透朱砂 | 弃。红缝在小尺寸被读成**删除线**,语义正好相反 |

**启动图** = 宣纸底 + 《字·斗》横向锁定图 + 右下朱砂闲章 + 赭金细线。

## 三个踩过的坑

1. **字重必须 900。** 思源宋体是高对比度宋体(横细竖粗),700 以下的横画在 80px
   图标里会整根消失。放大到 1024 看反而像"断笔",那是笔形被放大后的错觉,不是字体坏了。
2. **Android 自适应前景只保证中心 66/108 可见。** 所以前景层字面按 **42%** 画布走
   (普通图标是 60%),否则圆形遮罩会切掉「字」的左点和右钩。
   `test_自适应前景在安全区内` 守着这条。
3. **闲章会把内容框撑向右。** 只按画布中心排「字·斗」的话,两个字自己是正的,
   整组看上去却偏右。`wordmark()` 里那个 `shift` 就是把含闲章的内容框作为一个单位回正。

## 配色与 Theme.cs 绑定

脚本里写死的四个十六进制必须等于 `Theme.cs` 的语义色,`test_配色与_Theme_一致` 会对账。
Theme.cs 调过色(比如 2026-09-18 那次 WCAG AA 动了七个色)而品牌资源没重出,
图标就会和游戏内 UI 脱色 —— **这种事没有任何编译错会提示你**。

## 已接进 ProjectSettings 的

- `companyName`(原 `DefaultCompany`)
- `applicationIdentifier.Android`(补上;iPhone 那条本来就有,同一个 id)
- `m_SplashScreenBackgroundColor` → `Theme.Paper`,和宣纸启动图配套

## 还要在 Unity 编辑器里做的

工装接不了这几项(序列化结构复杂、改错了 Unity 会静默重置),得打开编辑器手动接:

1. **iOS 图标**:Player Settings → iOS → Icon,19 个槽位。文件名就是槽位尺寸,
   `appicon_ios_180.png` 对 180×180 槽,以此类推;1024 那个进 App Store 槽。
2. **Android 图标**:Player Settings → Android → Icon
   - Adaptive:前景 `appicon_adaptive_fg`,背景 `appicon_adaptive_bg`
   - Legacy/Round:`appicon_android_{192,144,96,72,48}`
3. **启动图**:Splash Image → Logos 加一条 `splash_logo`(透明锁定图);
   Android 的 `androidSplashScreen` 用 `splash_landscape`。
4. **Play 商店图**:`appicon_play_512.png` 直接上传到 Play Console,不进工程。

## 尚未处理

- **Unity 开屏 Logo**:`m_ShowUnitySplashScreen: 1` 还开着。要不要关掉取决于
  Unity 授权档位,涉及许可条款,没替你动。
- **iOS 启动屏**:`iOSLaunchScreenType: 0`(默认)。游戏是**横屏 only**
  (`defaultScreenOrientation: 4`),配启动屏时别拿竖版图。
