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

        /// <summary>原子落盘(2026-07-22):先写临时文件并 fsync,再整体替换。
        /// 直接 WriteAllText 有两个移动端风险——数据只到 OS 缓存,被系统回收进程时最近一次
        /// 保存丢失;写一半被杀则存档残缺,Load 验签失败会退回全新状态(等于清档)。</summary>
        public static void Save(MetaState meta)
        {
            string payload = SaveGuard.Seal(SaveSerializer.ToJson(meta));
            string temp = SavePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(payload);
                writer.Flush();
                stream.Flush(true); // true = 连同 OS 缓冲一起刷到存储介质
            }
            if (File.Exists(SavePath))
                File.Replace(temp, SavePath, null); // 原子替换(Unix 下即 rename)
            else
                File.Move(temp, SavePath);
        }
    }
}
