using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>战斗原型引导:按 Play 即在任意场景搭出战斗界面(原型期免场景资产)。
    /// 初始局面 = 第 3 章 3.9 战例:持「灯」,池有木×2,对面两只木属性杂兵。</summary>
    public static class BattleBootstrap
    {
        private static GameObject _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => Restart();

        public static void Restart()
        {
            if (_root != null) Object.Destroy(_root);
            _root = new GameObject("Battle");

            EnsureSceneInfrastructure();

            var json = File.ReadAllText(
                Path.Combine(Application.streamingAssetsPath, "config/chars.json"));
            var graph = ConfigLoader.LoadGraph(json);

            var config = new BattleConfig
            {
                DropTable = new[] { "木", "火", "火", "丁", "尧", "然" },
            };
            var enemies = new[]
            {
                new EnemyDef("枯妖", Element.Wood, 12, 3),
                new EnemyDef("杇妖", Element.Wood, 12, 3),
            };
            var engine = new BattleEngine(graph, config,
                startingLibrary: new[] { "灯" }, startingPool: new[] { "木", "木" },
                enemies: enemies, seed: System.Environment.TickCount);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);

            var viewGo = new GameObject("BattleView", typeof(RectTransform));
            viewGo.transform.SetParent(canvasGo.transform, false);
            viewGo.AddComponent<BattleView>().Init(graph, engine);
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
