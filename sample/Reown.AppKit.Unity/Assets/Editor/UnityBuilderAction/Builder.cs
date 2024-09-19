using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using mixpanel;
using Sentry;
using Sentry.Unity;
using UnityBuilderAction.Input;
using UnityBuilderAction.Reporting;
using UnityBuilderAction.Versioning;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityBuilderAction
{
    internal static class Builder
    {
        public static void BuildProject()
        {
            // Gather values from args
            var options = ArgumentsParser.GetValidatedOptions();

            InjectSecrets(options);

            // Gather values from project
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();

            // Get all buildOptions from options
            var buildOptions = BuildOptions.None;
            foreach (var buildOptionString in Enum.GetNames(typeof(BuildOptions)))
            {
                if (options.ContainsKey(buildOptionString))
                {
                    var buildOptionEnum = (BuildOptions)Enum.Parse(typeof(BuildOptions), buildOptionString);
                    buildOptions |= buildOptionEnum;
                }
            }

#if UNITY_2021_2_OR_NEWER
            // Determine subtarget
            StandaloneBuildSubtarget buildSubtarget;
            if (!options.TryGetValue("standaloneBuildSubtarget", out var subtargetValue) || !Enum.TryParse(subtargetValue, out buildSubtarget))
            {
                buildSubtarget = default;
            }
#endif

            // Define BuildPlayer Options
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = options["customBuildPath"],
                target = (BuildTarget)Enum.Parse(typeof(BuildTarget), options["buildTarget"]),
                options = buildOptions,
#if UNITY_2021_2_OR_NEWER
                subtarget = (int)buildSubtarget
#endif
            };

            // Set version for this build
            VersionApplicator.SetVersion(options["buildVersion"]);

            // Apply Android settings
            if (buildPlayerOptions.target == BuildTarget.Android)
            {
                VersionApplicator.SetAndroidVersionCode(options["androidVersionCode"]);
                AndroidSettings.Apply(options);
            }

            // Execute default AddressableAsset content build, if the package is installed.
            // Version defines would be the best solution here, but Unity 2018 doesn't support that,
            // so we fall back to using reflection instead.
            var addressableAssetSettingsType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings,Unity.Addressables.Editor");
            if (addressableAssetSettingsType != null)
            {
                // ReSharper disable once PossibleNullReferenceException, used from try-catch
                try
                {
                    addressableAssetSettingsType.GetMethod("CleanPlayerContent", BindingFlags.Static | BindingFlags.Public)
                        .Invoke(null, new object[]
                        {
                            null
                        });
                    addressableAssetSettingsType.GetMethod("BuildPlayerContent", new Type[0]).Invoke(null, new object[0]);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to run default addressables build:\n{e}");
                }
            }

            // Perform build
            var buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);

            // Summary
            var summary = buildReport.summary;
            StdOutReporter.ReportSummary(summary);

            // Result
            var result = summary.result;
            StdOutReporter.ExitWithResult(result);
        }

        private static void InjectSecrets(Dictionary<string, string> options)
        {
            if (options.TryGetValue("mixpanelToken", out var mixpanelToken))
            {
                var mixpanelSettings = MixpanelSettings.Instance;
                mixpanelSettings.RuntimeToken = mixpanelToken;
                mixpanelSettings.DebugToken = mixpanelToken;
                EditorUtility.SetDirty(mixpanelSettings);
            }

            if (options.TryGetValue("sentryToken", out var sentryToken))
            {
                var sentryOptions = Resources.Load<ScriptableSentryUnityOptions>("Sentry/SentryOptions");
                sentryOptions.Dsn = sentryToken;
                EditorUtility.SetDirty(sentryOptions);
            }

            AssetDatabase.SaveAssets();
        }
    }
}