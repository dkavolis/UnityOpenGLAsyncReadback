using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenglAsyncReadback.Editor
{
    public static class Builder
    {
        private const string BinDir = "../bin";
        private const string PlayerDir = BinDir + "/player";

        [MenuItem("AsyncGPUReadbackPlugin/Build")]
        public static void BuildRelease()
        {
            Build(false);
        }

        [MenuItem("AsyncGPUReadbackPlugin/Build Debug")]
        public static void BuildDebug()
        {
            Build(true);
        }

        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-buildType");
            var debug = false;
            if (index >= 0)
            {
                if (index + 1 >= args.Length) throw new ArgumentException("Missing value for buildType!");
                string buildType = args[index + 1];
                debug = buildType.Equals("debug", StringComparison.OrdinalIgnoreCase);
            }

            Build(debug);
        }

        private static void Build(bool debug)
        {
            Directory.CreateDirectory(PlayerDir);

            var options = new BuildPlayerOptions
            {
                locationPathName = PlayerDir + "/player.exe",
                options = BuildOptions.StrictMode | BuildOptions.BuildScriptsOnly,
                target = BuildTarget.StandaloneWindows64, // doesn't matter which as the scripts are platform agnostic
                targetGroup = BuildTargetGroup.Standalone
            };

            if (debug)
                options.options |= BuildOptions.Development;

            // Called on main thread
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            switch (summary.result)
            {
                case BuildResult.Succeeded:
                    Debug.LogFormat("{2}: Build succeeded in {0}: {1} bytes", summary.totalTime, summary.totalSize,
                        summary.outputPath);
                    break;
                case BuildResult.Failed:
                    Debug.Log("Build failed");
                    break;
                case BuildResult.Unknown:
                case BuildResult.Cancelled:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            foreach (BuildFile file in report.files)
            {
                if (!file.path.Contains("AsyncGPU")) continue;
                string dst = Path.Combine(BinDir, Path.GetFileName(file.path));
                if (File.Exists(dst)) File.Delete(dst);
                File.Copy(file.path, dst);
            }
        }
    }
}
