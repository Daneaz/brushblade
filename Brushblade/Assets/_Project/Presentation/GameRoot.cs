using System.IO;
using Brushblade.Core;
using Brushblade.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brushblade.Presentation
{
    /// <summary>游戏根:配置与存档加载、地图/战斗视图切换、通关结算(19.1 双层结构)。
    /// 按 Play 即在任意场景运行,原型期免场景资产。</summary>
    public static class GameRoot
    {
        private static GameObject _viewRoot;
        private static RecipeGraph _graph;
        private static CampaignConfig _campaign;
        private static MetaState _meta;
        private static readonly ITimeSource Time = new RealTimeSource();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            EnsureSceneInfrastructure();

            string configDir = Path.Combine(Application.streamingAssetsPath, "config");
            _graph = ConfigLoader.LoadGraph(File.ReadAllText(Path.Combine(configDir, "chars.json")));
            _campaign = ConfigLoader.LoadCampaign(
                File.ReadAllText(Path.Combine(configDir, "enemies.json")), _graph);
            _meta = MetaStore.Load();

            // 初始收集 = 火系初始卡组(19.3.4;人工筛选后替换,当前原型为灯)
            if (_meta.OwnedCards.Count == 0)
                MetaRules.AcquireCard(_meta, "灯");

            ShowMap();
        }

        public static void ShowMap(string message = null)
        {
            var view = NewView("MapView");
            view.AddComponent<MapView>().Init(_campaign, _meta, Time, StartStage, () => MetaStore.Save(_meta), message,
                onOpenCollection: ShowCollection);
        }

        private static void ShowCollection()
        {
            var view = NewView("CollectionView");
            view.AddComponent<CollectionView>().Init(_graph, _meta, () => MetaStore.Save(_meta), () => ShowMap());
        }

        private static void StartStage(int chapter, int stage)
        {
            int level = MetaRules.CharacterLevel(_meta.CharacterXp);
            var battleConfig = new BattleConfig
            {
                DropTable = _campaign.DropTable,
                PlayerMaxHp = MetaRules.MaxHpFor(level), // 19.2.1 生命成长
            };
            var run = new RunEngine(_graph, _campaign.BuildRunConfig(chapter, stage), battleConfig,
                startingLibrary: MetaRules.StartingLibrary(_meta), startingPool: new[] { "木", "木" },
                seed: System.Environment.TickCount, cardLevels: _meta.CardLevels);

            var view = NewView("BattleView");
            view.AddComponent<BattleView>().Init(_graph, run,
                won => OnRunEnded(chapter, stage, won));
        }

        private static void OnRunEnded(int chapter, int stage, bool won)
        {
            string message = null;
            if (won)
            {
                bool firstClear = MetaRules.ApplyStageCleared(_meta, chapter, stage);
                _meta.Ink += firstClear ? 50 : 15;

                // 掉宝箱(19.5.3):档位随角色等级,Boss 关首通 +1 档;箱位满/当日达上限不掉箱、无折算
                bool bossBonus = firstClear && _campaign.Chapters[chapter].Stages[stage].Boss;
                var tier = ChestRules.RollTier(MetaRules.CharacterLevel(_meta.CharacterXp),
                    new GameRandom(System.Environment.TickCount), bossBonus);
                if (ChestRules.TryAwardChest(_meta, tier, _campaign.Chapters[chapter].RewardPool, Time))
                    message = $"获得{ChestRules.TierName(tier)}!在箱位中开启它";
                else
                    message = "箱位已满或今日宝箱达上限,本次未获得宝箱";
            }
            MetaStore.Save(_meta);
            ShowMap(message);
        }

        private static GameObject NewView(string name)
        {
            if (_viewRoot != null) Object.Destroy(_viewRoot);
            _viewRoot = new GameObject("ViewRoot");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(_viewRoot.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);

            var viewGo = new GameObject(name, typeof(RectTransform));
            viewGo.transform.SetParent(canvasGo.transform, false);
            return viewGo;
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
