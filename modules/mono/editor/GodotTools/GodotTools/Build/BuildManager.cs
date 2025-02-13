using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Godot;
using GodotTools.Internals;
using File = GodotTools.Utils.File;

namespace GodotTools.Build
{
    public static class BuildManager
    {
        private static BuildInfo _buildInProgress;

        public const string MsBuildIssuesFileName = "msbuild_issues.csv";
        private const string MsBuildLogFileName = "msbuild_log.txt";

        public delegate void BuildLaunchFailedEventHandler(BuildInfo buildInfo, string reason);

        public static event BuildLaunchFailedEventHandler BuildLaunchFailed;
        public static event Action<BuildInfo> BuildStarted;
        public static event Action<BuildResult> BuildFinished;
        public static event Action<string> StdOutputReceived;
        public static event Action<string> StdErrorReceived;

        private static void RemoveOldIssuesFile(BuildInfo buildInfo)
        {
            string issuesFile = GetIssuesFilePath(buildInfo);

            if (!File.Exists(issuesFile))
                return;

            File.Delete(issuesFile);
        }

        private static void ShowBuildErrorDialog(string message)
        {
            var plugin = GodotSharpEditor.Instance;
            plugin.ShowErrorDialog(message, "Build error");
            plugin.MakeBottomPanelItemVisible(plugin.MSBuildPanel);
        }

        public static void RestartBuild(BuildOutputView buildOutputView) => throw new NotImplementedException();
        public static void StopBuild(BuildOutputView buildOutputView) => throw new NotImplementedException();

        private static string GetLogFilePath(BuildInfo buildInfo)
        {
            return Path.Combine(buildInfo.LogsDirPath, MsBuildLogFileName);
        }

        private static string GetIssuesFilePath(BuildInfo buildInfo)
        {
            return Path.Combine(buildInfo.LogsDirPath, MsBuildIssuesFileName);
        }

        private static void PrintVerbose(string text)
        {
            if (OS.IsStdOutVerbose())
                GD.Print(text);
        }

        private static bool Build(BuildInfo buildInfo)
        {
            if (_buildInProgress != null)
                throw new InvalidOperationException("A build is already in progress.");

            _buildInProgress = buildInfo;

            try
            {
                BuildStarted?.Invoke(buildInfo);

                // Required in order to update the build tasks list
                Internal.GodotMainIteration();

                try
                {
                    RemoveOldIssuesFile(buildInfo);
                }
                catch (IOException e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo, $"Cannot remove issues file: {GetIssuesFilePath(buildInfo)}");
                    Console.Error.WriteLine(e);
                }

                try
                {
                    int exitCode = BuildSystem.Build(buildInfo, StdOutputReceived, StdErrorReceived);

                    if (exitCode != 0)
                        PrintVerbose($"MSBuild exited with code: {exitCode}. Log file: {GetLogFilePath(buildInfo)}");

                    BuildFinished?.Invoke(exitCode == 0 ? BuildResult.Success : BuildResult.Error);

                    return exitCode == 0;
                }
                catch (Exception e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo,
                        $"The build method threw an exception.\n{e.GetType().FullName}: {e.Message}");
                    Console.Error.WriteLine(e);
                    return false;
                }
            }
            finally
            {
                _buildInProgress = null;
            }
        }

        public static async Task<bool> BuildAsync(BuildInfo buildInfo)
        {
            if (_buildInProgress != null)
                throw new InvalidOperationException("A build is already in progress.");

            _buildInProgress = buildInfo;

            try
            {
                BuildStarted?.Invoke(buildInfo);

                try
                {
                    RemoveOldIssuesFile(buildInfo);
                }
                catch (IOException e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo, $"Cannot remove issues file: {GetIssuesFilePath(buildInfo)}");
                    Console.Error.WriteLine(e);
                }

                try
                {
                    int exitCode = await BuildSystem.BuildAsync(buildInfo, StdOutputReceived, StdErrorReceived);

                    if (exitCode != 0)
                        PrintVerbose($"MSBuild exited with code: {exitCode}. Log file: {GetLogFilePath(buildInfo)}");

                    BuildFinished?.Invoke(exitCode == 0 ? BuildResult.Success : BuildResult.Error);

                    return exitCode == 0;
                }
                catch (Exception e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo,
                        $"The build method threw an exception.\n{e.GetType().FullName}: {e.Message}");
                    Console.Error.WriteLine(e);
                    return false;
                }
            }
            finally
            {
                _buildInProgress = null;
            }
        }

        private static bool Publish(BuildInfo buildInfo)
        {
            if (_buildInProgress != null)
                throw new InvalidOperationException("A build is already in progress.");

            _buildInProgress = buildInfo;

            try
            {
                BuildStarted?.Invoke(buildInfo);

                // Required in order to update the build tasks list
                Internal.GodotMainIteration();

                try
                {
                    RemoveOldIssuesFile(buildInfo);
                }
                catch (IOException e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo, $"Cannot remove issues file: {GetIssuesFilePath(buildInfo)}");
                    Console.Error.WriteLine(e);
                }

                try
                {
                    int exitCode = BuildSystem.Publish(buildInfo, StdOutputReceived, StdErrorReceived);

                    if (exitCode != 0)
                        PrintVerbose(
                            $"dotnet publish exited with code: {exitCode}. Log file: {GetLogFilePath(buildInfo)}");

                    BuildFinished?.Invoke(exitCode == 0 ? BuildResult.Success : BuildResult.Error);

                    return exitCode == 0;
                }
                catch (Exception e)
                {
                    BuildLaunchFailed?.Invoke(buildInfo,
                        $"The publish method threw an exception.\n{e.GetType().FullName}: {e.Message}");
                    Console.Error.WriteLine(e);
                    return false;
                }
            }
            finally
            {
                _buildInProgress = null;
            }
        }

        private static bool BuildProjectBlocking(BuildInfo buildInfo)
        {
            if (!File.Exists(buildInfo.Solution))
                return true; // No solution to build

            using var pr = new EditorProgress("dotnet_build_project", "Building .NET project...", 1);

            pr.Step("Building project solution", 0);

            if (!Build(buildInfo))
            {
                ShowBuildErrorDialog("Failed to build project solution");
                return false;
            }

            return true;
        }

        private static bool CleanProjectBlocking(BuildInfo buildInfo)
        {
            if (!File.Exists(buildInfo.Solution))
                return true; // No solution to clean

            using var pr = new EditorProgress("dotnet_clean_project", "Cleaning .NET project...", 1);

            pr.Step("Cleaning project solution", 0);

            if (!Build(buildInfo))
            {
                ShowBuildErrorDialog("Failed to clean project solution");
                return false;
            }

            return true;
        }

        private static bool PublishProjectBlocking(BuildInfo buildInfo)
        {
            using var pr = new EditorProgress("dotnet_publish_project", "Publishing .NET project...", 1);

            pr.Step("Running dotnet publish", 0);

            if (!Publish(buildInfo))
            {
                ShowBuildErrorDialog("Failed to publish .NET project");
                return false;
            }

            return true;
        }

        private static BuildInfo CreateBuildInfo(
            [DisallowNull] string configuration,
            [AllowNull] string platform = null,
            bool rebuild = false,
            bool onlyClean = false
        )
        {
            var buildInfo = new BuildInfo(GodotSharpDirs.ProjectSlnPath, GodotSharpDirs.ProjectCsProjPath, configuration,
                restore: true, rebuild, onlyClean);

            // If a platform was not specified, try determining the current one. If that fails, let MSBuild auto-detect it.
            if (platform != null || Utils.OS.PlatformNameMap.TryGetValue(OS.GetName(), out platform))
                buildInfo.CustomProperties.Add($"GodotTargetPlatform={platform}");

            if (Internal.GodotIsRealTDouble())
                buildInfo.CustomProperties.Add("GodotFloat64=true");

            return buildInfo;
        }

        private static BuildInfo CreatePublishBuildInfo(
            [DisallowNull] string configuration,
            [DisallowNull] string platform,
            [DisallowNull] string runtimeIdentifier,
            [DisallowNull] string publishOutputDir,
            bool includeDebugSymbols = true
        )
        {
            var buildInfo = new BuildInfo(GodotSharpDirs.ProjectSlnPath, GodotSharpDirs.ProjectCsProjPath, configuration,
                runtimeIdentifier, publishOutputDir, restore: true, rebuild: false, onlyClean: false);

            if (!includeDebugSymbols)
            {
                buildInfo.CustomProperties.Add("DebugType=None");
                buildInfo.CustomProperties.Add("DebugSymbols=false");
            }

            buildInfo.CustomProperties.Add($"GodotTargetPlatform={platform}");

            if (Internal.GodotIsRealTDouble())
                buildInfo.CustomProperties.Add("GodotFloat64=true");

            return buildInfo;
        }

        public static bool BuildProjectBlocking(
            [DisallowNull] string configuration,
            [AllowNull] string platform = null,
            bool rebuild = false
        ) => BuildProjectBlocking(CreateBuildInfo(configuration, platform, rebuild));

        public static bool CleanProjectBlocking(
            [DisallowNull] string configuration,
            [AllowNull] string platform = null
        ) => CleanProjectBlocking(CreateBuildInfo(configuration, platform, rebuild: false, onlyClean: true));

        public static bool PublishProjectBlocking(
            [DisallowNull] string configuration,
            [DisallowNull] string platform,
            [DisallowNull] string runtimeIdentifier,
            string publishOutputDir,
            bool includeDebugSymbols = true
        ) => PublishProjectBlocking(CreatePublishBuildInfo(configuration,
            platform, runtimeIdentifier, publishOutputDir, includeDebugSymbols));

        public static bool EditorBuildCallback()
        {
            if (!File.Exists(GodotSharpDirs.ProjectSlnPath))
                return true; // No solution to build

            if (GodotSharpEditor.Instance.SkipBuildBeforePlaying)
                return true; // Requested play from an external editor/IDE which already built the project

            return BuildProjectBlocking("Debug");
        }

        public static void Initialize()
        {
        }
    }
}
