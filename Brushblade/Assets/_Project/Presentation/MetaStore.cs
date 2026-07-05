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
            return File.Exists(SavePath)
                ? SaveSerializer.FromJson(File.ReadAllText(SavePath))
                : new MetaState();
        }

        public static void Save(MetaState meta)
        {
            File.WriteAllText(SavePath, SaveSerializer.ToJson(meta));
        }
    }
}
