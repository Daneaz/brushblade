using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Brushblade.Editor
{
    /// <summary>命令行出包入口(-executeMethod),供无场景资产原型走 iOS 模拟器验收用。</summary>
    public static class MobileBuild
    {
        private const string ScenePath = "Assets/_Project/Presentation/Scenes/Boot.unity";
        private const string BundleId = "com.eugenewu.brushblade";

        public static void BuildIOSSimulator() => BuildIOS(iOSSdkVersion.SimulatorSDK, "iOS");

        public static void BuildIOSDevice() => BuildIOS(iOSSdkVersion.DeviceSDK, "iOS-Device");

        private static void BuildIOS(iOSSdkVersion sdk, string outputFolderName)
        {
            EnsureBootScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.iOS.sdkVersion = sdk;

            string outputDir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Builds", outputFolderName);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputDir,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            });

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception($"iOS build failed: {report.summary.result}");
        }

        private static void EnsureBootScene()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
