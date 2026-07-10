using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>存档文件读写(persistentDataPath/save.json)。防篡改校验与云同步后续接入(19.9)。</summary>
    public static class MetaStore
    {
        private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static MetaState Load()
        {
            if (!File.Exists(SavePath))
                return new MetaState();
            // 验签失败(篡改/旧明文/损坏)一律回全新状态(19.9)
            return SaveGuard.TryOpen(File.ReadAllText(SavePath), out var payload)
                ? SaveSerializer.FromJson(payload)
                : new MetaState();
        }

        public static void Save(MetaState meta)
        {
            File.WriteAllText(SavePath, SaveGuard.Seal(SaveSerializer.ToJson(meta)));
        }
    }
}
