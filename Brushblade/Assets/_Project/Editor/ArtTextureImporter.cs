using UnityEditor;
using UnityEngine;

namespace Brushblade.Editor
{
    /// <summary>字怪形象与字牌边框 PNG 的导入设置(2026-07-27)。资产是一批一批陆续进来的,
    /// 手改 meta 迟早漏 —— 交给导入钩子自动定。</summary>
    public sealed class ArtTextureImporter : AssetPostprocessor
    {
        private static readonly string[] ArtResources =
        {
            "Presentation/Mobs/Resources/",
            "Presentation/Cards/Resources/",
        };

        private void OnPreprocessTexture()
        {
            bool matched = false;
            foreach (var dir in ArtResources)
                if (assetPath.Contains(dir)) matched = true;
            if (!matched) return;

            var importer = (TextureImporter)assetImporter;
            // 水墨稿全是柔和的半透明边缘:不开这个,Unity 不做 alpha 扩散,边缘会泛暗
            importer.alphaIsTransparency = true;
            // UI 用图,固定尺寸显示:mipmap 只会让它在缩放时发虚
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp; // 防边缘采样到对侧像素
            importer.textureType = TextureImporterType.Default; // 代码走 Texture2D + Sprite.Create
        }
    }
}
