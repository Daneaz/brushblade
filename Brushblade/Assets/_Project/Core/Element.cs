namespace Brushblade.Core
{
    /// <summary>六属性 = 真五行(木火土金水)+ 心(中立)。规则见 docs/design/wuxing-reference.md。</summary>
    public enum Element
    {
        Wood,   // 木
        Fire,   // 火
        Earth,  // 土
        Metal,  // 金
        Water,  // 水
        Heart,  // 心(不参与生克)
    }
}
