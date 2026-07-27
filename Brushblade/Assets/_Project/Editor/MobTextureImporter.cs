using UnityEditor;
using UnityEngine;

namespace Brushblade.Editor
{
    /// <summary>字怪形象 PNG 的导入设置(2026-07-27)。资产是一只一只陆续进来的,
    /// 手改 meta 迟早漏 —— 交给导入钩子自动定。</summary>
    public sealed class MobTextureImporter : AssetPostprocessor
    {
        private const string MobResources = "Presentation/Mobs/Resources/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(MobResources)) return;

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
