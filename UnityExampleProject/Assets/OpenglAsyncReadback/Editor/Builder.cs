using System;
using System.IO;
using System.Linq;
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
                options = BuildOptions.None,
                targetGroup = BuildTargetGroup.Standalone
            };

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (Application.platform) {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    options.target = BuildTarget.StandaloneOSX;
                    break;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    options.target = BuildTarget.StandaloneWindows64;
                    break;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    options.target = BuildTarget.StandaloneLinux64;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

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

            // BuildFile is full of lies, it has an invalid data dir on Linux...
            string dataDir = Directory.GetDirectories(Path.Combine(summary.outputPath, ".."))
                .FirstOrDefault(dir => dir.Contains("Data"));
            if (string.IsNullOrEmpty(dataDir))
            {
                Debug.LogWarning("No data dir generated!");
                return;
            }

            string managedDir = Path.Combine(dataDir, "Managed");
            foreach (string file in Directory.GetFiles(managedDir, "*AsyncGPU*"))
            {
                string dst = Path.Combine(BinDir, Path.GetFileName(file));
                if (File.Exists(dst)) File.Delete(dst);
                Debug.LogFormat("Copying {0} to {1}", file, dst);
                try
                {
                    File.Copy(file, dst);
                }
                catch (FileNotFoundException e)
                {
                    Debug.LogError("For unknown reasons, the file no longer exists!");
                    Debug.LogException(e);
                }
            }
        }
    }
}
