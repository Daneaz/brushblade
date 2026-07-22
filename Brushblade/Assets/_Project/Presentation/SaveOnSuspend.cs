using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>切后台/退出时保底落盘(2026-07-22)。移动端被系统回收进程不会有额外通知,
    /// 此前只在层边界与看广告等节点写盘,两次写盘之间被杀就丢掉那一段进度
    /// (最易被察觉的是「刚看完广告扩容就退出」——扩容白看)。</summary>
    public sealed class SaveOnSuspend : MonoBehaviour
    {
        private void OnApplicationPause(bool paused)
        {
            if (paused) GameRoot.SaveNow();
        }

        private void OnApplicationQuit() => GameRoot.SaveNow();
    }
}
