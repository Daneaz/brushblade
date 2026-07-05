using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>连战原型引导:按 Play 即在任意场景搭出界面(原型期免场景资产)。
    /// 连战内容 = StreamingAssets/config/enemies.json(第一章蒙学基准,4 场);
    /// 初始局面 = 第 3 章 3.9 战例:持「灯」,池有木×2。</summary>
    public static class BattleBootstrap
    {
        private static GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => Restart();

        public static void Restart()
        {
            if (_root != null) Object.Destroy(_root);
            _root = new GameObject("Run");

            EnsureSceneInfrastructure();

            string configDir = Path.Combine(Application.streamingAssetsPath, "config");
            var graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            var campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), graph);

            // 临时:固定第 1 章第 1 关;章节地图 UI 随 Meta 循环接入
            var runConfig = campaign.BuildRunConfig(0, 0);
            var battleConfig = new BattleConfig { DropTable = campaign.DropTable };
            var run = new RunEngine(graph, runConfig, battleConfig,
                startingLibrary: new[] { "灯" }, startingPool: new[] { "木", "木" },
                seed: System.Environment.TickCount);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);

            var viewGo = new GameObject("BattleView", typeof(RectTransform));
            viewGo.transform.SetParent(canvasGo.transform, false);
            viewGo.AddComponent<BattleView>().Init(graph, run);
        }

        private static void EnsureSceneInfrastructure()
        {
            if (Camera.main == null)
            {
                var cameraGo = new GameObject("Main Camera", typeof(Camera));
                cameraGo.tag = "MainCamera";
                var camera = cameraGo.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.09f, 0.09f, 0.11f);
            }
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }
    }
}
